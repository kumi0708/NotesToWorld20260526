using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct NoteInfo
{
    public float time;
    public int lane; // 0=D, 1=F, 2=J, 3=K
}

public enum JudgmentType { Perfect, Good, Miss }

public class RhythmGameManager : MonoBehaviour
{
    public static RhythmGameManager Instance { get; private set; }

    private const float JudgeLineY = -3.5f;
    private const float SpawnY = 5.5f;
    private const float ScrollTime = 2f;

    private static readonly float[] LaneX = { -1.5f, -0.5f, 0.5f, 1.5f };
    private static readonly KeyCode[] LaneKeys = { KeyCode.D, KeyCode.F, KeyCode.J, KeyCode.K };
    private static readonly string[] KeyNames = { "D", "F", "J", "K" };

    private static readonly Color[] LaneColors =
    {
        new Color(0.3f, 0.9f, 1f),
        new Color(0.3f, 1f, 0.5f),
        new Color(1f, 0.9f, 0.3f),
        new Color(1f, 0.4f, 0.4f),
    };

    public const float PerfectWindow = 0.05f;
    public const float GoodWindow   = 0.10f;
    public const float MissWindow   = 0.15f;

    public float SongTime { get; private set; }
    public int Score { get; private set; }
    public int Combo { get; private set; }

    public System.Action<JudgmentType, int> OnJudgment;

    private NoteInfo[] chart;
    private int nextNoteIndex;
    private float noteSpeed;
    private List<Note>[] laneNotes;
    private float startTime;
    private Renderer[] laneHighlights;
    private MaterialPropertyBlock mpb;

    private string lastJudgment = "";
    private JudgmentType lastJudgmentType;
    private float judgmentTimer;
    private bool songFinished;

    void Awake()
    {
        Instance = this;
        laneNotes = new List<Note>[4];
        for (int i = 0; i < 4; i++) laneNotes[i] = new List<Note>();
        mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        noteSpeed = (SpawnY - JudgeLineY) / ScrollTime;
        chart = GenerateChart();
        startTime = Time.time;
        OnJudgment += OnJudgmentReceived;
        CreateSceneVisuals();
        SetupCamera();
    }

    void Update()
    {
        SongTime = Time.time - startTime;
        SpawnNotes();
        HandleInput();
        CheckMissedNotes();
        UpdateLaneHighlights();
        if (judgmentTimer > 0) judgmentTimer -= Time.deltaTime;

        // Restart
        if (songFinished && Input.GetKeyDown(KeyCode.R))
            RestartGame();

        if (!songFinished && nextNoteIndex >= chart.Length)
        {
            bool anyPending = false;
            foreach (var list in laneNotes)
                if (list.Count > 0) { anyPending = true; break; }
            if (!anyPending) songFinished = true;
        }
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f);
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    void CreateSceneVisuals()
    {
        float laneTop    = SpawnY + 0.5f;
        float laneBot    = JudgeLineY - 1f;
        float laneHeight = laneTop - laneBot;
        float centerY    = (laneTop + laneBot) / 2f;
        float totalWidth = LaneX[3] - LaneX[0] + 1f;

        // Background panel behind the lanes
        CreateQuad("PlayfieldBG",
            new Vector3(0f, centerY, 2f),
            new Vector3(totalWidth, laneHeight, 1f),
            new Color(0.08f, 0.08f, 0.12f));

        // Lane separator lines (5 lines for 4 lanes)
        for (int i = 0; i <= 4; i++)
        {
            float x = LaneX[0] - 0.5f + i;
            CreateQuad($"Sep_{i}",
                new Vector3(x, centerY, 0.9f),
                new Vector3(0.015f, laneHeight, 1f),
                new Color(0.25f, 0.25f, 0.3f));
        }

        // Judgment line
        CreateQuad("JudgeLine",
            new Vector3(0f, JudgeLineY, 0.8f),
            new Vector3(totalWidth, 0.06f, 1f),
            Color.white);

        // Lane key label backgrounds (small rects below judge line)
        for (int i = 0; i < 4; i++)
        {
            CreateQuad($"KeyBG_{i}",
                new Vector3(LaneX[i], JudgeLineY - 0.55f, 0.8f),
                new Vector3(0.85f, 0.8f, 1f),
                new Color(0.15f, 0.15f, 0.2f));
        }

        // Lane highlight overlays (transparent, flash on keypress)
        float hlHeight  = SpawnY - JudgeLineY;
        float hlCenterY = (SpawnY + JudgeLineY) / 2f;
        laneHighlights = new Renderer[4];
        for (int i = 0; i < 4; i++)
        {
            var go = CreateQuad($"LaneHL_{i}",
                new Vector3(LaneX[i], hlCenterY, 0.85f),
                new Vector3(0.95f, hlHeight, 1f),
                new Color(LaneColors[i].r, LaneColors[i].g, LaneColors[i].b, 0f),
                transparent: true);
            laneHighlights[i] = go.GetComponent<Renderer>();
        }
    }

    GameObject CreateQuad(string name, Vector3 pos, Vector3 scale, Color color, bool transparent = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material = CreateMaterial(color, transparent);
        return go;
    }

    Material CreateMaterial(Color color, bool transparent = false)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");

        var mat = new Material(shader);

        bool isURP = shader.name.Contains("Universal Render Pipeline");
        if (isURP)
        {
            if (transparent)
            {
                mat.SetFloat("_Surface", 1f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            mat.SetColor("_BaseColor", color);
        }
        mat.color = color;
        return mat;
    }

    void SpawnNotes()
    {
        while (nextNoteIndex < chart.Length)
        {
            var info = chart[nextNoteIndex];
            if (SongTime >= info.time - ScrollTime)
            {
                SpawnNote(info);
                nextNoteIndex++;
            }
            else break;
        }
    }

    void SpawnNote(NoteInfo info)
    {
        var go = CreateQuad($"Note_L{info.lane}",
            new Vector3(LaneX[info.lane], SpawnY, 0f),
            new Vector3(0.85f, 0.12f, 1f),
            LaneColors[info.lane]);
        var note = go.AddComponent<Note>();
        note.Initialize(info.time, info.lane, noteSpeed, JudgeLineY);
        laneNotes[info.lane].Add(note);
    }

    void HandleInput()
    {
        for (int lane = 0; lane < 4; lane++)
            if (Input.GetKeyDown(LaneKeys[lane]))
                TryJudge(lane);
    }

    void TryJudge(int lane)
    {
        CleanLane(lane);
        var notes = laneNotes[lane];

        Note earliest = null;
        float earliestTime = float.MaxValue;
        foreach (var n in notes)
        {
            if (!n.IsJudged && n.HitTime < earliestTime)
            {
                earliestTime = n.HitTime;
                earliest = n;
            }
        }

        if (earliest == null) return;

        float diff = earliest.GetTimeDiff();
        if (diff > MissWindow) return;

        JudgmentType j = diff <= PerfectWindow ? JudgmentType.Perfect
                       : diff <= GoodWindow    ? JudgmentType.Good
                       :                         JudgmentType.Miss;

        earliest.SetJudged();
        notes.Remove(earliest);

        if (j == JudgmentType.Miss)
        {
            Combo = 0;
        }
        else
        {
            Combo++;
            Score += j == JudgmentType.Perfect ? 100 + Combo * 10 : 50 + Combo * 5;
        }

        OnJudgment?.Invoke(j, Combo);
    }

    void CheckMissedNotes()
    {
        for (int lane = 0; lane < 4; lane++)
        {
            var notes = laneNotes[lane];
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                if (notes[i] == null || notes[i].IsJudged) { notes.RemoveAt(i); continue; }
                if (SongTime - notes[i].HitTime > MissWindow)
                {
                    notes[i].SetJudged();
                    notes.RemoveAt(i);
                    Combo = 0;
                    OnJudgment?.Invoke(JudgmentType.Miss, Combo);
                }
            }
        }
    }

    void CleanLane(int lane)
    {
        for (int i = laneNotes[lane].Count - 1; i >= 0; i--)
            if (laneNotes[lane][i] == null || laneNotes[lane][i].IsJudged)
                laneNotes[lane].RemoveAt(i);
    }

    void UpdateLaneHighlights()
    {
        if (laneHighlights == null) return;
        for (int i = 0; i < 4; i++)
        {
            if (laneHighlights[i] == null) continue;
            float alpha = Input.GetKey(LaneKeys[i]) ? 0.28f : 0f;
            var c = LaneColors[i];
            c.a = alpha;
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            laneHighlights[i].SetPropertyBlock(mpb);
        }
    }

    void OnJudgmentReceived(JudgmentType type, int combo)
    {
        lastJudgment = type.ToString().ToUpper();
        lastJudgmentType = type;
        judgmentTimer = 0.5f;
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

        // Score
        style.fontSize = 26;
        style.alignment = TextAnchor.UpperLeft;
        GUI.Label(new Rect(12, 12, 280, 50), $"Score  {Score:N0}", style);

        // Combo
        if (Combo > 1)
        {
            style.fontSize = 30;
            style.alignment = TextAnchor.UpperCenter;
            GUI.color = Color.yellow;
            GUI.Label(new Rect(Screen.width / 2 - 120, 12, 240, 50), $"{Combo}x COMBO", style);
            GUI.color = Color.white;
        }

        // Judgment flash
        if (judgmentTimer > 0 && lastJudgment.Length > 0)
        {
            style.fontSize = 44;
            style.alignment = TextAnchor.MiddleCenter;
            GUI.color = lastJudgmentType == JudgmentType.Perfect ? Color.yellow
                      : lastJudgmentType == JudgmentType.Good    ? Color.green
                      :                                             Color.red;
            GUI.Label(new Rect(Screen.width / 2 - 130, Screen.height / 2 - 120, 260, 80), lastJudgment, style);
            GUI.color = Color.white;
        }

        // Key labels
        style.fontSize = 20;
        style.alignment = TextAnchor.MiddleCenter;
        for (int i = 0; i < 4; i++)
        {
            var sp = Camera.main.WorldToScreenPoint(new Vector3(LaneX[i], JudgeLineY - 0.55f, 0f));
            GUI.Label(new Rect(sp.x - 15, Screen.height - sp.y - 15, 30, 30), KeyNames[i], style);
        }

        // Song end
        if (songFinished)
        {
            style.fontSize = 32;
            style.alignment = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 20, 400, 60),
                      $"CLEAR!  Final Score: {Score:N0}", style);
            style.fontSize = 20;
            GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height / 2 + 40, 400, 40),
                      "Press R to retry", style);
        }
    }

    void RestartGame()
    {
        // Destroy all note objects
        foreach (var list in laneNotes)
        {
            foreach (var n in list)
                if (n != null) Destroy(n.gameObject);
            list.Clear();
        }

        Score = 0;
        Combo = 0;
        nextNoteIndex = 0;
        SongTime = 0;
        songFinished = false;
        lastJudgment = "";
        judgmentTimer = 0f;
        startTime = Time.time;
    }

    NoteInfo[] GenerateChart()
    {
        var notes = new List<NoteInfo>();
        float beat = 60f / 120f; // 120 BPM

        // Main pattern (16 variations, repeat twice = 32 notes)
        int[] pat = { 0, 2, 1, 3, 0, 1, 2, 3, 0, 2, 1, 3, 1, 2, 0, 3 };
        for (int i = 0; i < 32; i++)
            notes.Add(new NoteInfo { time = 3f + i * beat, lane = pat[i % pat.Length] });

        return notes.ToArray();
    }
}
