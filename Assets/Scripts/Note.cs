using UnityEngine;

public class Note : MonoBehaviour
{
    public float HitTime { get; private set; }
    public int Lane { get; private set; }
    public bool IsJudged { get; private set; }

    private float speed;
    private float judgeLineY;

    public void Initialize(float hitTime, int lane, float speed, float judgeLineY)
    {
        HitTime = hitTime;
        Lane = lane;
        this.speed = speed;
        this.judgeLineY = judgeLineY;
    }

    void Update()
    {
        if (IsJudged) return;
        float timeUntilHit = HitTime - RhythmGameManager.Instance.SongTime;
        transform.position = new Vector3(transform.position.x, judgeLineY + speed * timeUntilHit, 0f);
    }

    public float GetTimeDiff() => Mathf.Abs(HitTime - RhythmGameManager.Instance.SongTime);

    public void SetJudged()
    {
        IsJudged = true;
        gameObject.SetActive(false);
    }
}
