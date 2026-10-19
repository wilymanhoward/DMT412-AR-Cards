using UnityEngine;

// Scales the object up from zero with a small overshoot.
// Call Play() from the Image Target's On Target Found event, or from a Replay button.
public class PopIn : MonoBehaviour
{
    public float duration = 0.6f;
    Vector3 fullScale;
    Coroutine running;

    void Awake() => fullScale = transform.localScale;

    public void Play()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Ease.Run(duration, t => transform.localScale = fullScale * Ease.OutBack(t)));
    }
}
