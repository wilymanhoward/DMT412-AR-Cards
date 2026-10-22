using UnityEngine;
using UnityEngine.UI;

// Screen-space name tag (rounded panel + text) that follows a reward floating above its gift.
// It pops in (UIPopIn), stays for `lifetime` seconds, then floats up, shrinks and fades away.
[RequireComponent(typeof(CanvasGroup))]
public class RewardLabel : MonoBehaviour
{
    public Transform target;
    public Camera cam;
    public Text text;
    public float heightAbove = 0.12f;
    public float lifetime = 2.5f;
    const float FadeTime = 0.45f;

    CanvasGroup group;
    float age;

    void Awake() => group = GetComponent<CanvasGroup>();

    void LateUpdate()
    {
        age += Time.deltaTime; // game time: the tag waits while the card is lost
        float k = Mathf.Clamp01((age - lifetime) / FadeTime);
        if (!target || k >= 1) { Destroy(gameObject); return; }

        Vector3 p = cam.WorldToScreenPoint(target.position + target.parent.up * heightAbove);
        bool visible = p.z > 0 && Time.timeScale > 0; // behind the camera, or paused while the card is lost
        group.alpha = visible ? 1 - Ease.InQuad(k) : 0;
        transform.position = p + Vector3.up * 40f * Ease.OutCubic(k);
        if (k > 0) transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, Ease.InQuad(k));
    }
}
