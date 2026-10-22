using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Press feedback: the button sinks a little while held and springs back with an overshoot.
public class ButtonBounce : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    Coroutine running;

    public void OnPointerDown(PointerEventData _) => Animate(0.9f, false);
    public void OnPointerUp(PointerEventData _) => Animate(1f, true);

    void Animate(float to, bool springy)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Scale(to, springy));
    }

    IEnumerator Scale(float to, bool springy)
    {
        float from = transform.localScale.x;
        yield return Ease.RunUnscaled(springy ? 0.3f : 0.08f, t =>
            transform.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, springy ? Ease.OutBack(t, 3f) : Ease.OutQuad(t)));
    }
}
