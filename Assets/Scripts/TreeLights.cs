using System.Collections.Generic;
using UnityEngine;

// Makes the tree feel alive once it has risen: slow spin, a little dance to the beat, flickering garland,
// twinkling lights, a pulsing star glow, and a jelly wobble when something pokes it (Jiggle).
// The controller enables this when the tree is up and disables it on restart.
public class TreeLights : MonoBehaviour
{
    public Transform star;              // glow point at the tree top
    public ParticleSystem twinkles;
    public ParticleSystem starHalo;
    public float spinSpeed = 15f;       // degrees per second
    public float beatsPerMinute = 100f; // idle dance tempo
    public float bounce = 0.035f;       // squash on each beat
    public float sway = 2.5f;           // degrees

    readonly List<Material> glowing = new List<Material>();
    readonly List<Color> glowColours = new List<Color>();
    Vector3 starScale;
    float yaw, jiggleStart = -10f, jiggleStrength;
    Vector2 jiggleDir;

    void Awake()
    {
        // own copies of the glowing materials (garland, lights) so flickering doesn't touch other props
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            foreach (var m in r.materials)
            {
                if (!m.IsKeywordEnabled("_EMISSION")) continue;
                glowing.Add(m);
                glowColours.Add(m.GetColor("_EmissionColor"));
            }
        }
        starScale = star.localScale;
    }

    // strength 1 = a tap; smaller for gentle reactions (a gift landing).
    public void Jiggle(float strength)
    {
        jiggleStart = Time.time;
        jiggleStrength = strength;
        jiggleDir = Random.insideUnitCircle.normalized;
    }

    void OnEnable()
    {
        yaw = 0;
        twinkles.Play();
        starHalo.Play();
    }

    void OnDisable()
    {
        twinkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        starHalo.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        star.localScale = starScale;
        transform.localScale = Vector3.one;
    }

    void Update()
    {
        float t = Time.time;
        yaw += spinSpeed * Time.deltaTime;

        // idle dance: a quick squash on every beat that springs back
        float beat = Mathf.Repeat(t * beatsPerMinute / 60f, 1f);
        float squash = bounce * Mathf.Sin(beat * Mathf.PI * 2) * Mathf.Exp(-3 * beat);
        // jelly wobble after a jiggle: a decaying oscillation
        float j = t - jiggleStart;
        float wobble = j < 1.5f ? jiggleStrength * Mathf.Exp(-4 * j) * Mathf.Sin(j * 22f) : 0;
        squash += 0.12f * wobble;
        transform.localScale = new Vector3(1 + squash * 0.5f, 1 - squash, 1 + squash * 0.5f);

        // slow sway plus the wobble's lean, applied on top of the spin
        Vector2 tilt = new Vector2(Mathf.Sin(t * 1.3f), Mathf.Sin(t * 0.9f)) * sway + jiggleDir * (8f * wobble);
        transform.localRotation = Quaternion.Euler(tilt.x, 0, tilt.y) * Quaternion.Euler(0, yaw, 0);

        for (int i = 0; i < glowing.Count; i++)
            glowing[i].SetColor("_EmissionColor", glowColours[i] * (0.4f + 1.2f * Mathf.PerlinNoise(t * 4f, i * 1.7f)));
        star.localScale = starScale * (1 + 0.15f * Mathf.Sin(t * 3f));
    }
}
