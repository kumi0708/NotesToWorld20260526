using UnityEngine;

/// <summary>
/// BPMベースの時間管理シングルトン。
/// DeltaTime / SpeedRatio を参照するだけで BPM 連動の速度が得られる。
/// </summary>
public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance { get; private set; }

    // ── 公開プロパティ ──────────────────────────────────────
    /// <summary>現在の BPM</summary>
    public float Bpm         { get; private set; } = 120f;

    /// <summary>元となる BPM（MIDIファイルから設定）</summary>
    public float OriginalBpm { get; private set; } = 120f;

    /// <summary>BPM / OriginalBpm の比率。1.0 が等速。</summary>
    public float SpeedRatio  => OriginalBpm > 0f ? Bpm / OriginalBpm : 1f;

    /// <summary>通常の deltaTime。BPM に影響されない（エフェクト・物理など）。</summary>
    public float DeltaTime    => Time.deltaTime;

    /// <summary>BPM スケール済み deltaTime。カメラ・スポーンアニメに使用。</summary>
    public float BpmDeltaTime => Time.deltaTime * SpeedRatio;

    /// <summary>BPM 変更イベント（新BPMを引数として通知）</summary>
    public event System.Action<float> OnBpmChanged;

    // ── ライフサイクル ──────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 公開 API ─────────────────────────────────────────────
    /// <summary>BPM を変更する。OnBpmChanged を発火する。</summary>
    public void SetBpm(float newBpm)
    {
        Bpm = Mathf.Max(1f, newBpm);
        OnBpmChanged?.Invoke(Bpm);
    }

    /// <summary>MIDI から読み取ったオリジナル BPM を設定する。</summary>
    public void SetOriginalBpm(float original)
    {
        OriginalBpm = Mathf.Max(1f, original);
    }

    /// <summary>
    /// WaitForSeconds に渡す待機秒数を BPM 比率で短縮して返す。
    /// 例: ScaledWait(0.26f) → BPM 2倍なら 0.13f
    /// </summary>
    public float ScaledWait(float seconds) => seconds / Mathf.Max(0.01f, SpeedRatio);
}
