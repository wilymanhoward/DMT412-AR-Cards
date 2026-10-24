using UnityEngine;

// All particle effects for the card. The systems are built and tuned by the editor builder;
// this just plays them. World-space bursts are emitted at a world position.
public class FxLibrary : MonoBehaviour
{
    [Header("Looping (card space)")]
    public ParticleSystem sparkleRing; // around the tree base while it rises
    public ParticleSystem glitter;     // gold glitter rising with the tree
    public ParticleSystem snowfall;
    public ParticleSystem trail;       // behind the sleigh
    public ParticleSystem orbitStars;  // golden stars swirling around the tree once it's up
    public ParticleSystem confettiRain; // finale
    public ParticleSystem magicSwirl;   // golden spiral where Santa appears (move it, then Play)

    [Header("Bursts (world space)")]
    public ParticleSystem snowPuff;
    public ParticleSystem confetti;
    public ParticleSystem sparkle;
    public ParticleSystem rareBurst;
    public ParticleSystem firework;

    public Color[] fireworkColors =
    {
        new Color(1f, 0.25f, 0.25f), new Color(1f, 0.82f, 0.25f), new Color(0.35f, 1f, 0.45f), Color.white
    };

    public void Puff(Vector3 at, int count = 14) => Burst(snowPuff, at, count);
    public void Confetti(Vector3 at) => Burst(confetti, at, 70);
    public void Sparkle(Vector3 at, int count = 30) => Burst(sparkle, at, count);

    public void Rare(Vector3 at)
    {
        Burst(rareBurst, at, 90);
        Burst(sparkle, at, 50);
    }

    public void Firework(Vector3 at)
    {
        var p = new ParticleSystem.EmitParams
        {
            position = at,
            applyShapeToPosition = true,
            startColor = fireworkColors[Random.Range(0, fireworkColors.Length)]
        };
        firework.Emit(p, 120);
    }

    public void StopAll()
    {
        foreach (var ps in new[] { sparkleRing, glitter, snowfall, trail, orbitStars, confettiRain, magicSwirl, snowPuff, confetti, sparkle, rareBurst, firework })
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    static void Burst(ParticleSystem ps, Vector3 at, int count) =>
        ps.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true }, count);
}
