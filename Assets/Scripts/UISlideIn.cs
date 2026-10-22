using System.Collections;
using UnityEngine;

// Slides a UI element in from an offset every time it's shown, with a small overshoot.
// Give neighbours different delays for a staggered entrance. Runs while the game is paused.
public class UISlideIn : MonoBehaviour
{
    public Vector2 from = new Vector2(0, 300); // start offset from the element's resting place
    public float delay;
    public float duration = 0.6f;

    RectTransform rect;
    Vector2 rest;
    bool hasRest;

    void OnEnable()
    {
        rect = (RectTransform)transform;
        if (!hasRest) { rest = rect.anchoredPosition; hasRest = true; }
        rect.anchoredPosition = rest + from;
        StartCoroutine(Slide());
    }

    IEnumerator Slide()
    {
        for (float t = 0; t < delay; t += Time.unscaledDeltaTime) yield return null;
        yield return Ease.RunUnscaled(duration, k => rect.anchoredPosition = rest + from * (1 - Ease.OutBack(k, 1.2f)));
    }
}
