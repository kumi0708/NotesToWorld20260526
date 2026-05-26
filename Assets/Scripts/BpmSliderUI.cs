using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

/// <summary>
/// BPMスライダーUIを自己構築してMidiSpawnerと接続する
/// </summary>
public class BpmSliderUI : MonoBehaviour
{
    [Header("Range")]
    public float minBpm = 60f;
    public float maxBpm = 240f;

    private MidiSpawner spawner;
    private Slider      slider;
    private TMP_Text    valueLabel;

    void Start()
    {
        spawner = FindAnyObjectByType<MidiSpawner>();
        BuildUI();
    }

    // ══ UI Construction ══════════════════════════════════════

    void BuildUI()
    {
        // ── EventSystem（スライダー入力に必須） ──
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
        }

        // ── Canvas ──
        var canvasGo = new GameObject("BpmCanvas");
        DontDestroyOnLoad(canvasGo);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasGo.AddComponent<GraphicRaycaster>();

        // ── Panel（左下） ──
        var panel = MakeRect("BpmPanel", canvasGo.transform);
        panel.anchorMin        = Vector2.zero;
        panel.anchorMax        = Vector2.zero;
        panel.pivot            = Vector2.zero;
        panel.anchoredPosition = new Vector2(28, 28);
        panel.sizeDelta        = new Vector2(300, 80);

        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.05f, 0.65f);

        // ── BPMラベル ──
        var labelRect = MakeRect("Label", panel);
        labelRect.anchorMin        = new Vector2(0, 1);
        labelRect.anchorMax        = new Vector2(1, 1);
        labelRect.pivot            = new Vector2(0.5f, 1);
        labelRect.anchoredPosition = new Vector2(0, -10);
        labelRect.sizeDelta        = new Vector2(0, 26);

        valueLabel           = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        valueLabel.fontSize  = 18;
        valueLabel.alignment = TextAlignmentOptions.Center;
        valueLabel.color     = Color.white;
        UpdateLabel(spawner != null ? spawner.bpm : 120f);

        // ── Slider ──
        var sliderGo   = DefaultControls.CreateSlider(WhiteResources());
        sliderGo.name  = "BpmSlider";
        sliderGo.transform.SetParent(panel, false);

        var sliderRect             = sliderGo.GetComponent<RectTransform>();
        sliderRect.anchorMin       = new Vector2(0, 0);
        sliderRect.anchorMax       = new Vector2(1, 0);
        sliderRect.pivot           = new Vector2(0.5f, 0);
        sliderRect.anchoredPosition = new Vector2(0, 16);
        sliderRect.sizeDelta       = new Vector2(-24, 22);

        slider              = sliderGo.GetComponent<Slider>();
        slider.minValue     = minBpm;
        slider.maxValue     = maxBpm;
        slider.wholeNumbers = true;
        slider.value        = spawner != null ? spawner.bpm : 120f;
        slider.onValueChanged.AddListener(OnSliderChanged);

        StyleSlider(slider);
    }

    // ── スライダーの見た目を調整 ────────────────────────────

    void StyleSlider(Slider s)
    {
        // Background
        var bg = s.transform.Find("Background")?.GetComponent<Image>();
        if (bg) bg.color = new Color(0.25f, 0.25f, 0.25f, 1f);

        // Fill
        var fill = s.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
        if (fill) fill.color = new Color(0.35f, 0.78f, 0.98f, 1f);

        // Handle
        var handle = s.transform.Find("Handle Slide Area/Handle")?.GetComponent<Image>();
        if (handle) handle.color = Color.white;
    }

    // ── Callback ─────────────────────────────────────────────

    void OnSliderChanged(float val)
    {
        if (spawner != null) spawner.SetBpm(val);
        UpdateLabel(val);
    }

    void UpdateLabel(float val)
    {
        if (valueLabel != null)
            valueLabel.text = $"BPM  {Mathf.RoundToInt(val)}";
    }

    // ── Helpers ──────────────────────────────────────────────

    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static DefaultControls.Resources WhiteResources()
    {
        var tex = Texture2D.whiteTexture;
        var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
        return new DefaultControls.Resources { standard = spr };
    }
}
