using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ビートに合わせてグリッド上に家・木・フェンスをスプリングアニメーションで生成する
/// </summary>
public class TickyTackySpawner : MonoBehaviour
{
    [Header("Beat / Music")]
    public float bpm = 120f;
    public AudioClip musicClip; // BGMを入れると一緒に再生

    [Header("Grid")]
    public float gridSize = 2.5f;
    public int maxObjects = 64;

    [Header("Camera")]
    public float orbitDegreesPerSecond = 5f;

    // --- internal ---
    private AudioSource beatSource;
    private AudioSource musicSource;
    private float nextBeatTime;
    private float beatInterval;

    private readonly List<Vector2Int> visited = new List<Vector2Int>();
    private readonly Queue<Vector2Int> frontier = new Queue<Vector2Int>();

    private int beatCount;
    private float hue = 0.55f;

    private Camera mainCam;
    private MaterialPropertyBlock mpb;

    void Start()
    {
        Application.runInBackground = true; // MCP/バックグラウンド実行時にゲームループを止めない
        mainCam = Camera.main;
        mpb = new MaterialPropertyBlock();

        beatInterval = 60f / bpm;
        nextBeatTime = Time.time + 1f; // 1秒後から開始

        PositionCamera();
        CreateGround();
        SetupAudio();

        EnqueueCell(Vector2Int.zero);
    }

    void Update()
    {
        if (beatCount < maxObjects && Time.time >= nextBeatTime)
        {
            OnBeat();
            nextBeatTime += beatInterval;
        }

        if (mainCam != null)
        {
            mainCam.transform.RotateAround(
                new Vector3(0f, 0.8f, 0f),
                Vector3.up,
                orbitDegreesPerSecond * Time.deltaTime
            );
        }
    }

    // ========== Beat ==========

    void OnBeat()
    {
        if (beatSource != null && beatSource.clip != null)
            beatSource.PlayOneShot(beatSource.clip);

        if (frontier.Count == 0) return;

        Vector2Int cell = frontier.Dequeue();
        Vector3 worldPos = new Vector3(cell.x * gridSize, 0f, cell.y * gridSize);
        SpawnObject(worldPos, beatCount);

        foreach (var dir in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            EnqueueCell(cell + dir);

        hue = (hue + 0.07f) % 1f;
        beatCount++;
    }

    void EnqueueCell(Vector2Int cell)
    {
        if (visited.Contains(cell)) return;
        visited.Add(cell);
        frontier.Enqueue(cell);
    }

    // ========== Object Creation ==========

    void SpawnObject(Vector3 pos, int index)
    {
        var root = new GameObject($"Obj_{index}");
        root.transform.position = pos;

        Color mainColor   = Color.HSVToRGB(hue, 0.45f, 0.95f);
        Color accentColor = Color.HSVToRGB((hue + 0.12f) % 1f, 0.55f, 0.85f);

        switch (index % 3)
        {
            case 0: BuildHouse(root.transform, mainColor, accentColor); break;
            case 1: BuildTree(root.transform, mainColor); break;
            case 2: BuildFence(root.transform, mainColor); break;
        }

        StartCoroutine(SpringIn(root.transform, 0.38f));
    }

    void BuildHouse(Transform parent, Color wallColor, Color roofColor)
    {
        AddBox(parent, "Walls",  new Vector3(0, 0.5f, 0),     new Vector3(1.2f, 1f, 1.2f),     wallColor);
        var roof = AddBox(parent, "Roof", new Vector3(0, 1.3f, 0), new Vector3(1.35f, 0.55f, 1.35f), roofColor);
        roof.transform.localRotation = Quaternion.Euler(0, 45f, 0);
        AddBox(parent, "Window", new Vector3(0, 0.55f, 0.61f), new Vector3(0.3f, 0.3f, 0.05f),
               new Color(0.8f, 0.95f, 1f));
    }

    void BuildTree(Transform parent, Color leafColor)
    {
        AddPrim(PrimitiveType.Cylinder, parent, "Trunk",
            new Vector3(0, 0.4f, 0), new Vector3(0.22f, 0.8f, 0.22f),
            new Color(0.55f, 0.35f, 0.15f));
        AddPrim(PrimitiveType.Sphere, parent, "Foliage",
            new Vector3(0, 1.35f, 0), new Vector3(1.1f, 1.1f, 1.1f), leafColor);
    }

    void BuildFence(Transform parent, Color color)
    {
        foreach (float x in new[] { -0.4f, 0f, 0.4f })
            AddBox(parent, "Post", new Vector3(x, 0.4f, 0), new Vector3(0.15f, 0.8f, 0.15f), color);
        AddBox(parent, "Rail", new Vector3(0, 0.62f, 0), new Vector3(1.05f, 0.1f, 0.13f), color);
    }

    // ========== Helpers ==========

    GameObject AddBox(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color) =>
        AddPrim(PrimitiveType.Cube, parent, name, localPos, scale, color);

    GameObject AddPrim(PrimitiveType type, Transform parent, string name,
                       Vector3 localPos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;

        // コライダーを削除してオーバーヘッドを減らす
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;

        // MaterialPropertyBlock で色を設定（新Material生成を避ける）
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            mpb.Clear();
            mpb.SetColor("_BaseColor", color); // URP Lit / Unlit
            mpb.SetColor("_Color", color);     // Built-in RP
            rend.SetPropertyBlock(mpb);
        }

        return go;
    }

    // ========== Spring Animation ==========

    IEnumerator SpringIn(Transform t, float duration)
    {
        t.localScale = Vector3.zero;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            // EaseOutBack
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float s = 1f + c3 * Mathf.Pow(p - 1f, 3f) + c1 * Mathf.Pow(p - 1f, 2f);
            t.localScale = Vector3.one * Mathf.Max(0f, s);
            yield return null;
        }
        t.localScale = Vector3.one;
    }

    // ========== Scene Setup ==========

    void CreateGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);

        var col = ground.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var rend = ground.GetComponent<Renderer>();
        if (rend != null)
        {
            mpb.Clear();
            mpb.SetColor("_BaseColor", new Color(0.45f, 0.72f, 0.45f));
            mpb.SetColor("_Color", new Color(0.45f, 0.72f, 0.45f));
            rend.SetPropertyBlock(mpb);
        }
    }

    void PositionCamera()
    {
        if (mainCam == null) return;
        mainCam.transform.position = new Vector3(12f, 11f, -12f);
        mainCam.transform.LookAt(new Vector3(0f, 1f, 0f));
    }

    void SetupAudio()
    {
        beatSource = gameObject.AddComponent<AudioSource>();
        beatSource.clip = GenerateClick(880f, 0.22f);
        beatSource.volume = 0.6f;
        beatSource.playOnAwake = false;

        if (musicClip != null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.clip = musicClip;
            musicSource.loop = true;
            musicSource.volume = 0.75f;
            musicSource.playOnAwake = false;
            musicSource.Play();
        }
    }

    AudioClip GenerateClick(float freq, float lengthSec)
    {
        int rate = 44100;
        int len = Mathf.RoundToInt(rate * lengthSec);
        float[] data = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-t * 28f);
            data[i] = env * Mathf.Sin(2f * Mathf.PI * freq * t) * 0.7f;
        }
        var clip = AudioClip.Create("BeatClick", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
