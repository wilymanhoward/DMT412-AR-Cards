using UnityEngine;

// The fireplace comes alive once the house has opened: the flame grows in and flickers, a warm light pulses
// on the room, and sparks pop in time with a crackling loop. It finds the flame in the house by name, so it
// follows the fireplace wherever it's placed in the scene. Extinguish() puts it all back for a restart.
public class FireCrackle : MonoBehaviour
{
    const string FlameName = "Fireplace_fire_001";
    const string ClipName = "fire_crackle_loop"; // Assets/Resources, so no scene wiring is needed

    Transform flame;
    Vector3 flameScale;
    Light glow;
    ParticleSystem sparks;
    AudioSource crackle;
    float burn, spike, nextPop;
    bool lit;

    // Builds the fire under `house` (or returns null if the house has no fireplace flame). Starts unlit and inactive.
    public static FireCrackle Create(Transform house, Material sparkMaterial)
    {
        Transform flame = null;
        foreach (var t in house.GetComponentsInChildren<Transform>(true))
            if (t.name == FlameName) { flame = t; break; }
        if (!flame) { Debug.LogWarning("No " + FlameName + " in the house: no fire."); return null; }

        // built on an inactive object, so nothing starts until Ignite()
        var go = new GameObject("Fire Crackle");
        go.SetActive(false);
        go.transform.SetParent(house, false);
        go.transform.position = flame.position + flame.up * 0.04f;
        var fire = go.AddComponent<FireCrackle>();
        fire.flame = flame;
        fire.flameScale = flame.localScale;
        fire.Build(sparkMaterial);
        flame.localScale = Vector3.zero; // unlit until the reveal
        return fire;
    }

    void Build(Material sparkMaterial)
    {
        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.55f, 0.2f);
        glow.range = 0.8f;
        glow.intensity = 0;
        glow.shadows = LightShadows.None;

        sparks = gameObject.AddComponent<ParticleSystem>();
        var main = sparks.main;
        main.playOnAwake = true;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.28f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.15f), new Color(1f, 0.9f, 0.45f));
        main.gravityModifier = -0.05f; // embers drift up
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;
        var emission = sparks.emission;
        emission.rateOverTime = 4;
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25;
        shape.radius = 0.02f;
        shape.rotation = new Vector3(-90, 0, 0);
        var fade = sparks.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                         new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
        var r = GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = sparkMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        crackle = gameObject.AddComponent<AudioSource>();
        crackle.clip = Resources.Load<AudioClip>(ClipName);
        crackle.loop = true;
        crackle.playOnAwake = true;
        crackle.spatialBlend = 0; // plain stereo: it's a small room and the phone is close
        crackle.volume = 0;
    }

    public void Ignite()
    {
        lit = true;
        nextPop = Time.time + 0.3f;
        gameObject.SetActive(true);
    }

    public void Extinguish()
    {
        lit = false;
        burn = spike = 0;
        flame.localScale = Vector3.zero;
        gameObject.SetActive(false); // stops the light, sparks and sound; sparks clear when it re-enables
    }

    void Update()
    {
        // grows in over ~1.2 s; Time.deltaTime and Time.time freeze while the card is lost
        burn = Mathf.MoveTowards(burn, lit ? 1f : 0f, Time.deltaTime / 1.2f);
        float flicker = Mathf.PerlinNoise(Time.time * 7f, 0.3f);
        float sway = Mathf.PerlinNoise(Time.time * 5f, 7.7f) - 0.5f;

        if (lit && Time.time >= nextPop)
        {
            sparks.Emit(Random.Range(4, 10)); // a loud crackle: a burst of sparks and a flash of light
            spike = 1f;
            nextPop = Time.time + Random.Range(0.4f, 1.6f);
        }
        spike = Mathf.MoveTowards(spike, 0f, Time.deltaTime * 6f);

        flame.localScale = Vector3.Scale(flameScale, new Vector3(1f + 0.25f * sway, 0.85f + 0.4f * flicker, 1f - 0.25f * sway)) * burn;
        glow.intensity = burn * (0.8f + 0.9f * flicker + 0.8f * spike);
        crackle.volume = burn * 0.4f;
    }
}
