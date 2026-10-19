using UnityEngine;
using UnityEngine.UI;

// Gentle breathing / spinning for UI accents (button glows, snowflakes). Runs while the game is paused.
public class UIPulse : MonoBehaviour
{
    public float scaleAmount = 0.05f;
    public float alphaAmount = 0f;   // 0..1: how much the alpha dips
    public float rotateSpeed = 0f;   // degrees per second
    public float speed = 2f;

    Vector3 baseScale;
    Graphic graphic;
    float baseAlpha;

    void Awake()
    {
        baseScale = transform.localScale;
        graphic = GetComponent<Graphic>();
        if (graphic) baseAlpha = graphic.color.a;
    }

    void Update()
    {
        float s = Mathf.Sin(Time.unscaledTime * speed);
        transform.localScale = baseScale * (1 + scaleAmount * s);
        if (rotateSpeed != 0) transform.Rotate(0, 0, rotateSpeed * Time.unscaledDeltaTime);
        if (graphic && alphaAmount > 0)
        {
            var c = graphic.color;
            c.a = baseAlpha * (1 - alphaAmount * (0.5f + 0.5f * s));
            graphic.color = c;
        }
    }
}
