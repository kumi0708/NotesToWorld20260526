using UnityEngine;

public class rot : MonoBehaviour
{
    [SerializeField]
    private float rotSpeed = 45f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.Rotate(0, rotSpeed * TimeManager.Instance.DeltaTime, 0);
    }
}
