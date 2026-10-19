using UnityEngine;
using UnityEngine.UI;

// Screen-space name tag that follows a reward floating above its gift.
public class RewardLabel : MonoBehaviour
{
    public Transform target;
    public Camera cam;
    public float heightAbove = 0.12f;
    Text text;

    void Awake() => text = GetComponent<Text>();

    void LateUpdate()
    {
        if (!target) { Destroy(gameObject); return; }
        Vector3 p = cam.WorldToScreenPoint(target.position + target.parent.up * heightAbove);
        // hidden behind the camera, or while tracking is lost (game paused)
        text.enabled = p.z > 0 && Time.timeScale > 0;
        transform.position = p;
    }
}
