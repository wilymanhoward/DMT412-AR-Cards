using UnityEngine;

// Screens and labels pop in with a small overshoot whenever they are shown.
public class UIPopIn : MonoBehaviour
{
    public float duration = 0.35f;
    public float from = 0.8f;

    void OnEnable()
    {
        transform.localScale = Vector3.one * from;
        StartCoroutine(Ease.RunUnscaled(duration, t =>
            transform.localScale = Vector3.one * Mathf.LerpUnclamped(from, 1f, Ease.OutBack(t))));
    }
}
