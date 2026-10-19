using System;
using System.Collections;
using UnityEngine;

// Easing curves (t in 0..1) plus a tiny tween runner. Every motion in the card goes through these,
// so nothing moves linearly: ease-out for arrivals, ease-in for departures, back/bounce for pops.
public static class Ease
{
    public static float OutQuad(float t) => 1 - (1 - t) * (1 - t);
    public static float InQuad(float t) => t * t;
    public static float OutCubic(float t) { t = 1 - t; return 1 - t * t * t; }
    public static float InCubic(float t) => t * t * t;
    public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1) / 2;
    public static float OutBack(float t, float s = 1.70158f) { t -= 1; return 1 + (s + 1) * t * t * t + s * t * t; }
    public static float InBack(float t, float s = 1.70158f) => (s + 1) * t * t * t - s * t * t;

    public static float OutBounce(float t)
    {
        const float n = 7.5625f, d = 2.75f;
        if (t < 1 / d) return n * t * t;
        if (t < 2 / d) return n * (t -= 1.5f / d) * t + 0.75f;
        if (t < 2.5f / d) return n * (t -= 2.25f / d) * t + 0.9375f;
        return n * (t -= 2.625f / d) * t + 0.984375f;
    }

    // Calls step(0..1) every frame for `duration` seconds of game time (freezes while Time.timeScale is 0).
    public static IEnumerator Run(float duration, Action<float> step)
    {
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            step(t / duration);
            yield return null;
        }
        step(1);
    }

    // Same, but keeps running while the game is paused. For UI only.
    public static IEnumerator RunUnscaled(float duration, Action<float> step)
    {
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            step(t / duration);
            yield return null;
        }
        step(1);
    }
}
