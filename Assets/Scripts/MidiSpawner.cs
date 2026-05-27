using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Klak.Timeline.Midi;

public enum SpawnType { House, Tree, Flower, Rock, Lamp }

[System.Serializable]
public class NoteSpawnRule
{
    public byte      minNote  = 0;
    public byte      maxNote  = 127;
    public SpawnType spawnType = SpawnType.House;
}

[System.Serializable]
public class MidiAudioPair
{
    public MidiFileAsset midiAsset;
    public AudioClip     audioClip;
    public bool isPlay = true;
}

public class MidiSpawner : MonoBehaviour
{
    [Header("Tracks (順番に再生)")]
    public List<MidiAudioPair> tracks = new List<MidiAudioPair>();
    public int midiTrackIndex = 0;   // MIDIファイル内のトラック番号

    [Header("Note Spawn Rules (上から順に評価、最初にマッチしたものを使用)")]
    public List<NoteSpawnRule> noteSpawnRules = new List<NoteSpawnRule>
    {
        new NoteSpawnRule { minNote =  0, maxNote = 35,  spawnType = SpawnType.House  },
        new NoteSpawnRule { minNote = 36, maxNote = 59,  spawnType = SpawnType.Tree   },
        new NoteSpawnRule { minNote = 60, maxNote = 71,  spawnType = SpawnType.Flower },
        new NoteSpawnRule { minNote = 72, maxNote = 83,  spawnType = SpawnType.Rock   },
        new NoteSpawnRule { minNote = 84, maxNote = 127, spawnType = SpawnType.Lamp   },
    };

    [Header("Tempo")]
    public float bpm = 120f;

    [Header("Grid")]
    public float gridSize = 2.8f;

    [Header("Camera")]
    public float panSpeed    = 1.2f;
    public float zoomSpeed   = 0.6f;
    public float minZoom     = 10f;
    public float maxZoom     = 32f;
    public float cameraPitch = 48f;
    public float cameraYaw   = 45f;

    // ── Runtime state ──────────────────────────────────────────
    private AudioSource audioSource;
    private List<float> noteTimes = new List<float>();
    private List<byte>  noteNums  = new List<byte>();
    private int noteIndex;
    private int spawnSerial;
    private Camera mainCam;
    private MaterialPropertyBlock mpb;

    private List<Vector2Int> visited     = new List<Vector2Int>();
    private Queue<Vector2Int> houseFront = new Queue<Vector2Int>();
    private Queue<Vector2Int> treeFront  = new Queue<Vector2Int>();

    private Transform        spawnRoot;
    private List<Vector3>    spawnedPos   = new List<Vector3>();

    private Vector3 camLookAt;
    private float   camDist;
    private float   prevAudioTime;
    private bool    isResetting;
    private int     colorCycle;
    private float   originalBpm    = 120f;
    private int     currentTrackIndex = 0;
    private MidiFileAsset currentMidi;
    private AudioClip     currentClip;

    // ── Palette ────────────────────────────────────────────────
    static readonly Color[] WallColors = {
        new Color(0.96f, 0.87f, 0.72f),
        new Color(0.72f, 0.89f, 0.96f),
        new Color(0.96f, 0.78f, 0.78f),
        new Color(0.84f, 0.96f, 0.76f),
        new Color(0.96f, 0.92f, 0.72f),
    };
    static readonly Color RoofBrown  = new Color(0.60f, 0.40f, 0.28f);
    static readonly Color LeafGreen  = new Color(0.52f, 0.76f, 0.40f);
    static readonly Color LeafWhite  = new Color(0.88f, 0.93f, 0.84f);
    static readonly Color TrunkBrown = new Color(0.54f, 0.36f, 0.20f);
    static readonly Color FlowerPink = new Color(0.96f, 0.68f, 0.72f);
    static readonly Color FlowerYell = new Color(0.96f, 0.88f, 0.50f);
    static readonly Color RockGray   = new Color(0.64f, 0.60f, 0.56f);
    static readonly Color GroundGrn  = new Color(0.50f, 0.74f, 0.42f);

    static readonly Vector2Int[] CardinalDirs = {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };
    static readonly Vector2Int[] DiagonalDirs = {
        new Vector2Int(1,1), new Vector2Int(-1,1),
        new Vector2Int(1,-1), new Vector2Int(-1,-1)
    };

    // ══════════════════════════════════════════════════════════
    void Start()
    {
        Application.runInBackground = true;
        mainCam   = Camera.main;
        mpb       = new MaterialPropertyBlock();
        camLookAt = Vector3.zero;
        camDist   = minZoom;

        // TimeManager がシーンになければ自動生成
        if (TimeManager.Instance == null)
            new GameObject("TimeManager").AddComponent<TimeManager>();

        // スポーンオブジェクトをまとめる親
        var srGo = new GameObject("SpawnRoot");
        srGo.transform.SetParent(transform);
        spawnRoot = srGo.transform;

        ApplyCameraTransform();
        CreateEnvironment();
        LoadTrackData(0);
        SetupAudio();

        TimeManager.Instance.OnBpmChanged += OnBpmChanged;
        EnqueueHouse(Vector2Int.zero);
    }

    void OnDestroy()
    {
        if (TimeManager.Instance != null)
            TimeManager.Instance.OnBpmChanged -= OnBpmChanged;
    }

    void OnBpmChanged(float newBpm)
    {
        if (audioSource != null)
            audioSource.pitch = TimeManager.Instance.SpeedRatio;
    }

    void Update()
    {
        if (audioSource == null) return;

        // AudioSourceが意図せず停止していたら再開
        if (!audioSource.isPlaying && audioSource.clip != null && !isResetting)
        {
            Debug.LogWarning("[MidiSpawner] AudioSource停止を検出 → 再開");
            audioSource.Play();
            noteIndex    = 0;
            prevAudioTime = 0f;
            return;
        }

        if (!audioSource.isPlaying) return;
        float t = audioSource.time;

        if (!isResetting)
        {
            // ループ検出1: 時間が0.1秒以上逆行
            bool loopByJump = t < prevAudioTime - 0.1f;
            // ループ検出2: 全ノート消化済み かつ tが0付近に戻った
            bool loopByWrap = noteIndex >= noteTimes.Count
                           && noteTimes.Count > 0
                           && t < 0.1f
                           && prevAudioTime > 0.1f;

            if (loopByJump || loopByWrap)
            {
                Debug.Log($"[MidiSpawner] ループ検出 jump={loopByJump} wrap={loopByWrap} t={t:F3} prev={prevAudioTime:F3}");
                StartCoroutine(ResetLoop());
            }
        }

        prevAudioTime = t;

        if (!isResetting)
        {
            while (noteIndex < noteTimes.Count && noteTimes[noteIndex] <= t)
            {
                TriggerSpawn(noteNums[noteIndex]);
                noteIndex++;
            }
        }

        SmoothCamera();
    }

    // ══ Camera ════════════════════════════════════════════════

    void SmoothCamera()
    {
        if (mainCam == null || spawnedPos.Count == 0) return;

        Vector3 center = CalcCenter();
        float spread   = CalcSpread(center);
        float target   = Mathf.Clamp(spread * 2.4f, minZoom, maxZoom);

        camLookAt = Vector3.Lerp(camLookAt, center, TimeManager.Instance.BpmDeltaTime * panSpeed);
        camDist   = Mathf.Lerp(camDist,   target,  TimeManager.Instance.BpmDeltaTime * zoomSpeed);

        ApplyCameraTransform();
    }

    void ApplyCameraTransform()
    {
        if (mainCam == null) return;
        var rot = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        mainCam.transform.position = camLookAt + rot * (Vector3.back * camDist);
        mainCam.transform.LookAt(camLookAt + Vector3.up * 0.5f);
    }

    Vector3 CalcCenter()
    {
        var sum = Vector3.zero;
        foreach (var p in spawnedPos) sum += p;
        return sum / spawnedPos.Count;
    }

    float CalcSpread(Vector3 center)
    {
        float max = 0f;
        foreach (var p in spawnedPos)
            max = Mathf.Max(max, Vector3.Distance(p, center));
        return max;
    }

    // ══ Spawn logic ══════════════════════════════════════════

    void TriggerSpawn(byte note)
    {
        Debug.Log($"[MidiSpawner] TriggerSpawn note={note} serial={spawnSerial}");
        int s = spawnSerial++;

        SpawnType type = SpawnType.House;
        foreach (var rule in noteSpawnRules)
        {
            if (note >= rule.minNote && note <= rule.maxNote)
            {
                type = rule.spawnType;
                break;
            }
        }

        switch (type)
        {
            case SpawnType.Flower: SpawnFlower(s); break;
            case SpawnType.Tree:   SpawnTree(s);   break;
            case SpawnType.Rock:   SpawnRock(s);   break;
            case SpawnType.Lamp:   SpawnLamp(s);   break;
            default:               SpawnHouse(s);  break;
        }
    }

    void SpawnHouse(int serial)
    {
        var queue = houseFront.Count > 0 ? houseFront : treeFront;
        if (queue.Count == 0) return;

        var cell = queue.Dequeue();
        var pos  = GridPos(cell);
        var wall = WallColors[colorCycle % WallColors.Length];
        colorCycle++;

        var root = MakeRoot($"House_{serial}", pos);
        BuildHouse(root.transform, wall, RoofBrown);
        StartCoroutine(PopIn(root.transform, 0.28f));
        RegisterObject(root, pos);

        foreach (var d in CardinalDirs) EnqueueHouse(cell + d);
        foreach (var d in DiagonalDirs) EnqueueTree(cell + d);
    }

    void SpawnTree(int serial)
    {
        var queue = treeFront.Count > 0 ? treeFront : houseFront;
        if (queue.Count == 0) return;

        var cell   = queue.Dequeue();
        var jitter = new Vector3(Random.Range(-0.32f, 0.32f), 0f, Random.Range(-0.32f, 0.32f));
        var pos    = GridPos(cell) + jitter;
        var leaf   = Random.value > 0.65f ? LeafWhite : LeafGreen;

        var root = MakeRoot($"Tree_{serial}", pos);
        BuildTree(root.transform, leaf);
        StartCoroutine(PopIn(root.transform, 0.22f));
        RegisterObject(root, pos);

        foreach (var d in CardinalDirs) EnqueueTree(cell + d);
    }

    void SpawnFlower(int serial)
    {
        if (spawnedPos.Count == 0) return;
        var anchor = spawnedPos[Random.Range(0, spawnedPos.Count)];
        var pos    = anchor + new Vector3(Random.Range(-1.0f, 1.0f), 0f, Random.Range(-1.0f, 1.0f));
        var col    = Random.value > 0.5f ? FlowerPink : FlowerYell;

        var root = MakeRoot($"Flower_{serial}", pos);
        BuildFlower(root.transform, col);
        StartCoroutine(PopIn(root.transform, 0.18f));
        RegisterObject(root, pos);
    }

    void SpawnRock(int serial)
    {
        if (spawnedPos.Count == 0) { SpawnHouse(serial); return; }
        var anchor = spawnedPos[Random.Range(0, spawnedPos.Count)];
        var pos    = anchor + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));

        var root = MakeRoot($"Rock_{serial}", pos);
        BuildRock(root.transform);
        StartCoroutine(PopIn(root.transform, 0.15f));
        RegisterObject(root, pos);
    }

    void SpawnLamp(int serial)
    {
        var queue = treeFront.Count > 0 ? treeFront : houseFront;
        if (queue.Count == 0) return;

        var cell   = queue.Dequeue();
        var jitter = new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f));
        var pos    = GridPos(cell) + jitter;

        var root = MakeRoot($"Lamp_{serial}", pos);
        BuildLamp(root.transform);
        StartCoroutine(PopIn(root.transform, 0.20f));
        RegisterObject(root, pos);

        foreach (var d in CardinalDirs) EnqueueTree(cell + d);
    }

    void RegisterObject(GameObject root, Vector3 pos)
    {
        spawnedPos.Add(pos);
    }

    void EnqueueHouse(Vector2Int cell)
    {
        if (visited.Contains(cell)) return;
        visited.Add(cell);
        houseFront.Enqueue(cell);
    }

    void EnqueueTree(Vector2Int cell)
    {
        if (visited.Contains(cell)) return;
        visited.Add(cell);
        treeFront.Enqueue(cell);
    }

    Vector3 GridPos(Vector2Int cell) => new Vector3(cell.x * gridSize, 0f, cell.y * gridSize);

    GameObject MakeRoot(string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(spawnRoot);
        go.transform.position = pos;
        return go;
    }

    // ══ Object builders ══════════════════════════════════════

    void BuildHouse(Transform p, Color wall, Color roof)
    {
        AddBox(p, "Wall", new Vector3(0, 0.5f, 0),     new Vector3(1.2f, 1.0f, 1.2f),    wall);
        var r = AddBox(p, "Roof", new Vector3(0, 1.3f, 0), new Vector3(1.35f, 0.55f, 1.35f), roof);
        r.transform.localRotation = Quaternion.Euler(0, 45f, 0);
        AddBox(p, "Win", new Vector3(0, 0.55f, 0.61f), new Vector3(0.30f, 0.30f, 0.05f), new Color(0.82f, 0.95f, 1f));
    }

    void BuildTree(Transform p, Color leaf)
    {
        AddPrim(PrimitiveType.Cylinder, p, "Trunk",
            new Vector3(0, 0.32f, 0), new Vector3(0.16f, 0.64f, 0.16f), TrunkBrown);
        AddPrim(PrimitiveType.Sphere, p, "Leaf",
            new Vector3(0, 1.00f, 0), new Vector3(0.85f, 0.85f, 0.85f), leaf);
    }

    void BuildFlower(Transform p, Color col)
    {
        AddPrim(PrimitiveType.Cylinder, p, "Stem",
            new Vector3(0, 0.12f, 0), new Vector3(0.05f, 0.24f, 0.05f), LeafGreen);
        AddPrim(PrimitiveType.Sphere, p, "Bloom",
            new Vector3(0, 0.30f, 0), new Vector3(0.26f, 0.26f, 0.26f), col);
    }

    void BuildRock(Transform p)
    {
        float s = Random.Range(0.28f, 0.55f);
        AddBox(p, "Rock", new Vector3(0, s * 0.35f, 0),
            new Vector3(s, s * 0.65f, s * 0.82f), RockGray);
    }

    void BuildLamp(Transform p)
    {
        var poleGray = new Color(0.25f, 0.25f, 0.28f);
        AddPrim(PrimitiveType.Cylinder, p, "Pole",
            new Vector3(0, 0.7f, 0), new Vector3(0.08f, 1.4f, 0.08f), poleGray);
        AddBox(p, "Arm",
            new Vector3(0.18f, 1.35f, 0), new Vector3(0.36f, 0.06f, 0.06f), poleGray);
        AddPrim(PrimitiveType.Sphere, p, "Globe",
            new Vector3(0.38f, 1.35f, 0), new Vector3(0.22f, 0.22f, 0.22f), new Color(1f, 0.95f, 0.70f));
    }

    // ══ Primitive helpers ════════════════════════════════════

    GameObject AddBox(Transform p, string n, Vector3 lp, Vector3 sc, Color c) =>
        AddPrim(PrimitiveType.Cube, p, n, lp, sc, c);

    GameObject AddPrim(PrimitiveType t, Transform p, string n, Vector3 lp, Vector3 sc, Color c)
    {
        var go  = GameObject.CreatePrimitive(t);
        go.name = n;
        var col = go.GetComponent<Collider>();
        if (col) Destroy(col);
        go.transform.SetParent(p, false);
        go.transform.localPosition = lp;
        go.transform.localScale    = sc;
        var rend = go.GetComponent<Renderer>();
        if (rend)
        {
            mpb.Clear();
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color",     c);
            rend.SetPropertyBlock(mpb);
        }
        return go;
    }

    // ══ Animation ════════════════════════════════════════════

    IEnumerator PopIn(Transform t, float dur)
    {
        t.localScale = Vector3.zero;
        float e = 0f;
        while (e < dur)
        {
            e += TimeManager.Instance.BpmDeltaTime;
            float p = Mathf.Clamp01(e / dur);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float s = 1f + c3 * Mathf.Pow(p - 1f, 3f) + c1 * Mathf.Pow(p - 1f, 2f);
            if (t) t.localScale = Vector3.one * Mathf.Max(0f, s);
            yield return null;
        }
        if (t) t.localScale = Vector3.one;
    }

    IEnumerator PopOut(Transform t, float dur)
    {
        float e = 0f;
        while (e < dur)
        {
            e += TimeManager.Instance.BpmDeltaTime;
            float p = Mathf.Clamp01(e / dur);
            if (t) t.localScale = Vector3.one * (1f - p * p * p);
            yield return null;
        }
        if (t) t.localScale = Vector3.zero;
    }

    // ══ Loop reset ═══════════════════════════════════════════

IEnumerator ResetLoop()
{
        isResetting = true;

        foreach (Transform child in spawnRoot)
            StartCoroutine(PopOut(child, 0.22f));

        yield return new WaitForSeconds(TimeManager.Instance.ScaledWait(0.26f));

        foreach (Transform child in spawnRoot)
            Destroy(child.gameObject);

        spawnedPos.Clear();
        visited.Clear();
        houseFront.Clear();
        treeFront.Clear();
        noteIndex   = 0;
        spawnSerial = 0;
        colorCycle  = 0;
        camLookAt   = Vector3.zero;
        camDist     = minZoom;

        // 次のトラックへ進んで再解析・オーディオ切り替え
        currentTrackIndex = (currentTrackIndex + 1) % Mathf.Max(1, tracks.Count);

		// tracksのisPlayがfalseなら次のトラックへ（全てfalseなら最初のトラックに戻る）
		int attempts = 0;
		while (!tracks[currentTrackIndex].isPlay && attempts < tracks.Count)
		{
			currentTrackIndex = (currentTrackIndex + 1) % Mathf.Max(1, tracks.Count);
			attempts++;
		}

		LoadTrackData(currentTrackIndex);
        SwitchAudio();

        EnqueueHouse(Vector2Int.zero);

        // リセット完了時点のaudioSource.timeにprevAudioTimeを合わせる
        // (高BPM時: 待機中に複数回クリップがループし prevが大きいまま残るのを防ぐ)
        prevAudioTime = audioSource != null ? audioSource.time : 0f;
        isResetting = false;

        Debug.Log($"[MidiSpawner] リセット完了 prevReset={prevAudioTime:F3}");
    }

    // ══ Track loading ════════════════════════════════════════

    void LoadTrackData(int index)
    {
        noteTimes.Clear();
        noteNums.Clear();

        if (tracks == null || tracks.Count == 0)
        { Debug.LogWarning("MidiSpawner: Tracksリストが空"); return; }

        var pair = tracks[index % tracks.Count];
        currentMidi = pair.midiAsset;
        currentClip = pair.audioClip;

        if (currentMidi == null || currentMidi.tracks == null || currentMidi.tracks.Length == 0)
        { Debug.LogWarning($"MidiSpawner: Track[{index}] MIDIアセット未設定"); return; }

        int midiIdx = Mathf.Clamp(midiTrackIndex, 0, currentMidi.tracks.Length - 1);
        var anim    = currentMidi.tracks[midiIdx].template;

        if (anim.events == null || anim.events.Length == 0)
        { Debug.LogWarning($"MidiSpawner: Track[{index}] イベントなし"); return; }

        uint tpqn = anim.ticksPerQuarterNote > 0 ? anim.ticksPerQuarterNote : 480u;

        originalBpm = anim.tempo > 1000f
            ? 60_000_000f / anim.tempo
            : (anim.tempo > 0f ? anim.tempo : 120f);

        float spb = 60f / originalBpm;

        var evPairs = new List<(float time, byte note)>();
        foreach (var ev in anim.events)
            if (ev.IsNoteOn) evPairs.Add((ev.time * spb / tpqn, ev.data1));

        evPairs.Sort((a, b) => a.time.CompareTo(b.time));
        foreach (var (time, note) in evPairs)
        { noteTimes.Add(time); noteNums.Add(note); }

        Debug.Log($"[MidiSpawner] Track[{index}] {noteTimes.Count}ノート / BPM={originalBpm} / clip={currentClip?.name}");
    }

    // ══ Public API ═══════════════════════════════════════════

    // ══ Audio ════════════════════════════════════════════════

    void SetupAudio()
    {
        audioSource             = gameObject.AddComponent<AudioSource>();
        audioSource.clip        = currentClip;
        audioSource.loop        = true;
        audioSource.playOnAwake = false;
        TimeManager.Instance.SetOriginalBpm(originalBpm);
        audioSource.pitch = TimeManager.Instance.SpeedRatio;
        if (currentClip) audioSource.Play();
        else Debug.LogWarning("MidiSpawner: AudioClip未設定 (Track[0])");
    }

    void SwitchAudio()
    {
        if (audioSource == null) return;
        audioSource.Stop();
        audioSource.clip  = currentClip;
        TimeManager.Instance.SetOriginalBpm(originalBpm);
        audioSource.pitch = TimeManager.Instance.SpeedRatio;
        if (currentClip) audioSource.Play();
        else Debug.LogWarning($"[MidiSpawner] Track[{currentTrackIndex}] AudioClip未設定");
    }

    // ══ Environment ══════════════════════════════════════════

    void CreateEnvironment()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(7f, 1f, 7f);
        var gc = ground.GetComponent<Collider>(); if (gc) Destroy(gc);
        PrimColor(ground, GroundGrn);

        for (int i = 0; i < 7; i++)
        {
            float angle = i * 51.4f + Random.Range(-18f, 18f);
            float dist  = Random.Range(5f, 11f);
            var pos = new Vector3(
                Mathf.Cos(angle * Mathf.Deg2Rad) * dist,
                0f,
                Mathf.Sin(angle * Mathf.Deg2Rad) * dist
            );
            var rock = new GameObject($"Rock_{i}");
            rock.transform.position = pos;
            rock.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            BuildRock(rock.transform);
        }
    }

    void PrimColor(GameObject go, Color c)
    {
        var rend = go.GetComponent<Renderer>(); if (!rend) return;
        mpb.Clear(); mpb.SetColor("_BaseColor", c); mpb.SetColor("_Color", c);
        rend.SetPropertyBlock(mpb);
    }
}
