using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Klak.Timeline.Midi;

public class MidiSpawner : MonoBehaviour
{
    [Header("MIDI")]
    public MidiFileAsset midiAsset;
    public int trackIndex = 0;

    [Header("Audio")]
    public AudioClip audioClip;

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

    private List<GameObject> spawnedRoots = new List<GameObject>();
    private List<Vector3>    spawnedPos   = new List<Vector3>();

    private Vector3 camLookAt;
    private float   camDist;
    private float   prevAudioTime;
    private bool    isResetting;
    private int     colorCycle;
    private float   originalBpm = 120f;

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

        ApplyCameraTransform();
        CreateEnvironment();
        ParseMidi();
        SetupAudio();
        EnqueueHouse(Vector2Int.zero);
    }

    void Update()
    {
        if (audioSource == null || !audioSource.isPlaying) return;
        float t = audioSource.time;

        if (!isResetting && audioClip != null && t < prevAudioTime - audioClip.length * 0.5f)
            StartCoroutine(ResetLoop());
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

        camLookAt = Vector3.Lerp(camLookAt, center, Time.deltaTime * panSpeed);
        camDist   = Mathf.Lerp(camDist,   target,  Time.deltaTime * zoomSpeed);

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
        int s = spawnSerial;
        if (s % 7 == 6)      SpawnFlower();
        else if (s % 3 == 2) SpawnTree();
        else                  SpawnHouse();
    }

    void SpawnHouse()
    {
        var queue = houseFront.Count > 0 ? houseFront : treeFront;
        if (queue.Count == 0) return;

        var cell = queue.Dequeue();
        var pos  = GridPos(cell);
        var wall = WallColors[colorCycle % WallColors.Length];
        colorCycle++;

        var root = MakeRoot($"House_{spawnSerial++}", pos);
        BuildHouse(root.transform, wall, RoofBrown);
        StartCoroutine(PopIn(root.transform, 0.28f));
        RegisterObject(root, pos);

        foreach (var d in CardinalDirs) EnqueueHouse(cell + d);
        foreach (var d in DiagonalDirs) EnqueueTree(cell + d);
    }

    void SpawnTree()
    {
        var queue = treeFront.Count > 0 ? treeFront : houseFront;
        if (queue.Count == 0) return;

        var cell   = queue.Dequeue();
        var jitter = new Vector3(Random.Range(-0.32f, 0.32f), 0f, Random.Range(-0.32f, 0.32f));
        var pos    = GridPos(cell) + jitter;
        var leaf   = Random.value > 0.65f ? LeafWhite : LeafGreen;

        var root = MakeRoot($"Tree_{spawnSerial++}", pos);
        BuildTree(root.transform, leaf);
        StartCoroutine(PopIn(root.transform, 0.22f));
        RegisterObject(root, pos);

        foreach (var d in CardinalDirs) EnqueueTree(cell + d);
    }

    void SpawnFlower()
    {
        if (spawnedPos.Count == 0) return;
        var anchor = spawnedPos[Random.Range(0, spawnedPos.Count)];
        var pos    = anchor + new Vector3(Random.Range(-1.0f, 1.0f), 0f, Random.Range(-1.0f, 1.0f));
        var col    = Random.value > 0.5f ? FlowerPink : FlowerYell;

        var root = MakeRoot($"Flower_{spawnSerial++}", pos);
        BuildFlower(root.transform, col);
        StartCoroutine(PopIn(root.transform, 0.18f));
        RegisterObject(root, pos);
    }

    void RegisterObject(GameObject root, Vector3 pos)
    {
        spawnedRoots.Add(root);
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
            e += Time.deltaTime;
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
            e += Time.deltaTime;
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

        foreach (var go in spawnedRoots)
            if (go) StartCoroutine(PopOut(go.transform, 0.22f));

        yield return new WaitForSeconds(0.26f);

        foreach (var go in spawnedRoots)
            if (go) Destroy(go);

        spawnedRoots.Clear();
        spawnedPos.Clear();
        visited.Clear();
        houseFront.Clear();
        treeFront.Clear();
        noteIndex = 0;
        camLookAt = Vector3.zero;
        camDist   = minZoom;

        EnqueueHouse(Vector2Int.zero);
        isResetting = false;
    }

    // ══ MIDI parsing ═════════════════════════════════════════

    void ParseMidi()
    {
        if (midiAsset == null || midiAsset.tracks == null || midiAsset.tracks.Length == 0)
        { Debug.LogWarning("MidiSpawner: MIDIアセット未設定"); return; }

        int idx  = Mathf.Clamp(trackIndex, 0, midiAsset.tracks.Length - 1);
        var anim = midiAsset.tracks[idx].template;

        if (anim.events == null || anim.events.Length == 0)
        { Debug.LogWarning($"MidiSpawner: トラック{idx}にイベントなし"); return; }

        uint tpqn = anim.ticksPerQuarterNote > 0 ? anim.ticksPerQuarterNote : 480u;

        // MIDIのオリジナルBPMを取得（tempoがマイクロ秒/beatの場合は変換）
        originalBpm = anim.tempo > 1000f
            ? 60_000_000f / anim.tempo
            : (anim.tempo > 0f ? anim.tempo : 120f);

        float spb = 60f / originalBpm;

        var pairs = new List<(float time, byte note)>();
        foreach (var ev in anim.events)
            if (ev.IsNoteOn) pairs.Add((ev.time * spb / tpqn, ev.data1));

        pairs.Sort((a, b) => a.time.CompareTo(b.time));

        foreach (var (time, note) in pairs)
        { noteTimes.Add(time); noteNums.Add(note); }

        Debug.Log($"MidiSpawner: {noteTimes.Count}個のノートオン / TPQN={tpqn} / originalBPM={originalBpm} / targetBPM={bpm} / pitch={bpm/originalBpm:F2}x");
    }

    // ══ Public API ═══════════════════════════════════════════

    public void SetBpm(float newBpm)
    {
        bpm = Mathf.Max(1f, newBpm);
        if (audioSource != null)
            audioSource.pitch = originalBpm > 0f ? bpm / originalBpm : 1f;
    }

    // ══ Audio ════════════════════════════════════════════════

    void SetupAudio()
    {
        audioSource             = gameObject.AddComponent<AudioSource>();
        audioSource.clip        = audioClip;
        audioSource.loop        = true;
        audioSource.playOnAwake = false;
        // BPM比率でpitchを設定 → 音速とaudioSource.timeの進みが両方スケールされMIDIと自動同期
        audioSource.pitch = originalBpm > 0f ? bpm / originalBpm : 1f;
        if (audioClip) audioSource.Play();
        else Debug.LogWarning("MidiSpawner: AudioClip未設定");
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
