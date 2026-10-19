using UnityEngine;

// Constant spin plus a gentle up/down float. Good for gifts, ornaments, a logo.
public class Spin : MonoBehaviour
{
    public Vector3 degreesPerSecond = new Vector3(0, 45, 0);
    public float bobHeight = 0.01f; // in metres: image targets are small, so keep this tiny
    public float bobSpeed = 2f;
    Vector3 start;

    void Start() => start = transform.localPosition;

    void Update()
    {
        transform.Rotate(degreesPerSecond * Time.deltaTime);
        transform.localPosition = start + Vector3.up * Mathf.Sin(Time.time * bobSpeed) * bobHeight;
    }
}
