using UnityEngine;
using UnityEngine.UI;

// Screen-space name tag (rounded panel + text) that follows a reward floating above its gift.
[RequireComponent(typeof(CanvasGroup))]
public class RewardLabel : MonoBehaviour
{
    public Transform target;
    public Camera cam;
    public Text text;
    public float heightAbove = 0.12f;
    CanvasGroup group;

    void Awake() => group = GetComponent<CanvasGroup>();

    void LateUpdate()
    {
        if (!target) { Destroy(gameObject); return; }
        Vector3 p = cam.WorldToScreenPoint(target.position + target.parent.up * heightAbove);
        // hidden behind the camera, or while tracking is lost (game paused)
        group.alpha = p.z > 0 && Time.timeScale > 0 ? 1 : 0;
        transform.position = p;
    }
}
