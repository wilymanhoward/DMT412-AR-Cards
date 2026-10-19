using System.Collections;
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
        running = StartCoroutine(Animate());
    }

    IEnumerator Animate()
    {
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float x = t / duration - 1;
            // easeOutBack: 0 at start, overshoots past 1, settles at 1
            transform.localScale = fullScale * (1 + 2.70158f * x * x * x + 1.70158f * x * x);
            yield return null;
        }
        transform.localScale = fullScale;
    }
}
