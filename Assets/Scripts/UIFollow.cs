using UnityEngine;

// Keeps a UI element on screen over a 3D point: a label over a node, a speech bubble over a character.
[RequireComponent(typeof(CanvasGroup))]
public class UIFollow : MonoBehaviour
{
    public Transform target;
    public Camera cam;
    public Vector2 offset; // in canvas reference pixels

    CanvasGroup group;
    Canvas canvas;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        canvas = GetComponentInParent<Canvas>();
    }

    void LateUpdate()
    {
        if (!target || !cam) return;
        Vector3 p = cam.WorldToScreenPoint(target.position);
        // hidden behind the camera, when the target is hidden, and while tracking is lost (the game is paused)
        bool visible = p.z > 0 && target.gameObject.activeInHierarchy && Time.timeScale > 0;
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1 : 0, Time.unscaledDeltaTime * 8f);
        transform.position = new Vector3(p.x, p.y, 0) + (Vector3)(offset * canvas.scaleFactor);
    }
}
