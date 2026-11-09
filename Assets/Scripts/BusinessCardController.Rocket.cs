using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// The logo easter egg: tap the HW logo 5 times quickly.
//   1  the normal spin
//   2  the logo shakes and the lines quiver; the mini-me stops typing and stares at it
//   3  he glitches like a bad hologram: "Uh oh... what are you doing?"
//   4  a flame flickers under the logo and he teleports out of his chair
//   5  lift-off: it rumbles on the pad in a growing cloud of smoke, then climbs slowly straight up, towing the
//      network off the card (each line stretches, then yanks its node up, and the nodes swing below it like a
//      kite tail), and bursts into fireworks at the top. The mini-me pops
//      out of the blast under a parachute and drifts back down to his chair, the nodes tumble down and hop home,
//      the logo falls back, the lines shoot out to the nodes again, and an achievement pops up.
// Stop tapping and it cools down; if he had teleported, he comes back ("Phew!").
// Positions are in the network's space (the logo's and nodes' parent, the same as card space).
public partial class BusinessCardController
{
    const string EggKey = "EasterEggFound";
    static readonly Color Smoke = new Color(0.86f, 0.86f, 0.9f);
    static readonly Vector3 Gravity = new Vector3(0, -0.8f, 0);

    int charge;
    float chargeTime;
    bool rocketing, pilotAway, landed;
    Transform character, flame, chute, canopy;
    Vector3 canopySize;
    Vector3 charRest, charScale;
    Quaternion charRot;
    float flamePower = 0.7f;
    ParticleSystem fire, core, exhaust, cloud, shells, flash; // the rocket's particle effects (BuildRocketFx)
    Light flameLight;
    int ropeMode;                  // 0 off, 1 the rocket is towing, 2 the tethers have snapped and the nodes fall
    bool[] towed, stemCut;
    Vector3[] nodeVel;
    float[] spokeRest;
    Color spokeColour;
    AudioSource chargeSource;
    AudioClip teleportClip, yankClip, snapClip, achievementClip, rumbleClip, bangClip;
    GameObject eggPanel;
    RectTransform eggRect, eggIcon;
    Text eggTitle, eggSub;

    // ---------- Building ----------

    void BuildRocket()
    {
        character = avatarAnim.transform;
        charRest = character.localPosition;
        charScale = character.localScale;
        charRot = character.localRotation;
        if (!pose) pose = avatar.GetComponentInChildren<SeatedPose>(true);
        if (pose) lookRest = pose.lookAt.localPosition;

        towed = new bool[hubs.Length];
        stemCut = new bool[hubs.Length];
        nodeVel = new Vector3[hubs.Length];
        spokeRest = new float[hubs.Length];
        for (int i = 0; i < hubs.Length; i++) spokeRest[i] = Vector3.Distance(logoRest, hubs[i].rest);
        spokeColour = hubs[0].spoke.startColor;

        chargeSource = gameObject.AddComponent<AudioSource>(); // its own source, so the charge clicks can rise in pitch
        chargeSource.playOnAwake = false;
        AudioClip Clip(string name) => Resources.Load<AudioClip>("Audio/" + name);
        teleportClip = Clip("biz_teleport");
        yankClip = Clip("biz_yank");
        snapClip = Clip("biz_snap");
        achievementClip = Clip("biz_achievement");
        rumbleClip = Clip("biz_rumble");
        bangClip = Clip("biz_bang");

        BuildRocketFx(logo.parent);
        BuildChute();
        BuildEggPanel();
    }

    // The particle effects, in card units (they scale with the card). `flame` is the nozzle under the logo: the
    // fire jet, its white-hot core and an orange light on the card. The smoke and fireworks live in the card's
    // world, so the exhaust trail hangs in the air behind the rocket.
    void BuildRocketFx(Transform net)
    {
        var glowMat = motes.GetComponent<ParticleSystemRenderer>().sharedMaterial; // soft round glow, alpha blended
        var fireMat = new Material(glowMat);
        var smokeMat = new Material(glowMat) { mainTexture = SmokeTexture() };

        flame = new GameObject("Rocket Flame").transform;
        flame.SetParent(net, false);

        // the jet: short-lived streaks, white-yellow at the nozzle, through orange and red to nothing
        fire = Fx("Fire", flame, fireMat, true, 300);
        var main = fire.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        Nozzle(fire, 10, 0.006f);
        Fade(fire, (0, new Color(1f, 0.97f, 0.8f, 1)), (0.25f, new Color(1f, 0.72f, 0.2f, 0.95f)),
                   (0.6f, new Color(1f, 0.35f, 0.06f, 0.7f)), (1, new Color(0.5f, 0.1f, 0.05f, 0)));
        Grow(fire, 0, 1, 1, 0.3f);
        var r = fire.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f;
        r.lengthScale = 1f;
        r.sortingFudge = -20; // drawn over the smoke

        // the white-hot core right at the nozzle
        core = Fx("Core", flame, fireMat, false, 20);
        main = core.main;
        main.startLifetime = 0.1f;
        main.startSpeed = 0;
        main.startColor = new Color(1f, 0.95f, 0.8f, 0.85f);
        var shape = core.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.001f;
        Fade(core, (0, new Color(1, 1, 1, 0)), (0.3f, Color.white), (1, new Color(1, 1, 1, 0)));
        core.GetComponent<ParticleSystemRenderer>().sortingFudge = -40;

        flameLight = flame.gameObject.AddComponent<Light>();
        flameLight.type = LightType.Point;
        flameLight.color = new Color(1f, 0.6f, 0.25f);
        flameLight.shadows = LightShadows.None;

        // the exhaust: smoke pouring out of the nozzle that swells, turns and drifts up as it fades
        exhaust = Fx("Exhaust Smoke", net, smokeMat, true, 160);
        main = exhaust.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
        Nozzle(exhaust, 20, 0.01f);
        Billow(exhaust, 1.5f, 0.5f, 2.6f);

        // the launch cloud: smoke rolling out across the card from under the pad
        cloud = Fx("Launch Cloud", net, smokeMat, true, 220);
        cloud.transform.localPosition = new Vector3(logoRest.x, 0.015f, logoRest.z);
        main = cloud.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        shape = cloud.shape;
        shape.shapeType = ParticleSystemShapeType.Circle; // emits outward, flat along the card
        shape.radius = 0.03f;
        shape.radiusThickness = 0;
        shape.rotation = new Vector3(90, 0, 0);
        Billow(cloud, 2.5f, 0.6f, 2.2f);

        // fireworks: sparks flying out of each shell with glowing trails, slowing down and falling
        shells = Fx("Fireworks", net, fireMat, true, 600);
        main = shells.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
        shape = shells.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.005f;
        Fade(shells, (0, Color.white), (0.6f, Color.white), (1, new Color(1, 1, 1, 0)));
        Grow(shells, 0, 1, 1, 0.4f);
        Drag(shells, 1.2f, -0.25f);
        var trails = shells.trails;
        trails.enabled = true;
        trails.lifetime = 0.25f;
        trails.dieWithParticles = true;
        trails.inheritParticleColor = true;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        var tg = new Gradient();
        tg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                   new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        trails.colorOverTrail = tg;
        shells.GetComponent<ParticleSystemRenderer>().trailMaterial = fireMat;

        // a big soft flash for the lift-off and each firework
        flash = Fx("Flash", net, fireMat, true, 10);
        main = flash.main;
        main.startLifetime = 0.25f;
        main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.4f);
        main.startColor = new Color(1f, 0.92f, 0.75f, 0.9f);
        shape = flash.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.001f;
        Fade(flash, (0, Color.white), (1, new Color(1, 1, 1, 0)));
        Grow(flash, 0, 0.5f, 1, 1.2f);
        flash.GetComponent<ParticleSystemRenderer>().sortingFudge = -50;

        flame.gameObject.SetActive(false);
    }

    static ParticleSystem Fx(string name, Transform parent, Material mat, bool world, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = max;
        main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy; // card units, like the card's other effects
        var emission = ps.emission;
        emission.rateOverTime = 0;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    // A cone pointing down out of the nozzle.
    static void Nozzle(ParticleSystem ps, float angle, float radius)
    {
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        shape.rotation = new Vector3(90, 0, 0);
    }

    // Smoke: fades in and out, swells, slowly turns, slows down and drifts up.
    static void Billow(ParticleSystem ps, float drag, float from, float to)
    {
        var main = ps.main;
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(Smoke.r, Smoke.g, Smoke.b, 0.75f), new Color(0.7f, 0.7f, 0.74f, 0.65f));
        Fade(ps, (0, new Color(1, 1, 1, 0)), (0.1f, Color.white), (0.5f, new Color(1, 1, 1, 0.6f)), (1, new Color(1, 1, 1, 0)));
        Grow(ps, 0, from, 0.35f, to * 0.75f, 1, to);
        var spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
        Drag(ps, drag, 0.04f);
    }

    // Slows particles down (air drag) and pushes them up (rise > 0) or down, in card units.
    static void Drag(ParticleSystem ps, float drag, float rise)
    {
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1000;
        limit.drag = drag;
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
        var force = ps.forceOverLifetime;
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.Local;
        force.x = force.z = 0;
        force.y = rise;
    }

    static void Fade(ParticleSystem ps, params (float t, Color c)[] keys)
    {
        var g = new Gradient();
        g.SetKeys(System.Array.ConvertAll(keys, k => new GradientColorKey(k.c, k.t)),
                  System.Array.ConvertAll(keys, k => new GradientAlphaKey(k.c.a, k.t)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = g;
    }

    // Size over the particle's life, as time/size pairs.
    static void Grow(ParticleSystem ps, params float[] timeSize)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i < timeSize.Length; i += 2) curve.AddKey(timeSize[i], timeSize[i + 1]);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, curve);
    }

    // A lumpy, soft-edged smoke puff, white so the particle colour tints it.
    static Texture2D SmokeTexture()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                float noise = 0.5f * Mathf.PerlinNoise(7.3f + u * 2.5f, 3.1f + v * 2.5f)
                            + 0.3f * Mathf.PerlinNoise(1.7f + u * 5, 9.2f - v * 5)
                            + 0.2f * Mathf.PerlinNoise(u * 10 - 4.4f, v * 10 + 6.6f);
                float edge = Mathf.Clamp01(1 - Mathf.Sqrt(u * u + v * v) * (0.75f + 0.5f * noise)); // lumpy outline
                float a = edge * edge * (3 - 2 * edge) * (0.55f + 0.45f * noise);
                byte shade = (byte)(195 + 60 * noise);
                px[y * n + x] = new Color32(shade, shade, shade, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    const int Gores = 12;
    const float CanopyFlat = 0.6f, VentAngle = 9f, HemSeam = 84f, HemMiddle = 74f;

    // A round canopy like a real one: 12 cloth panels (gores) in alternating orange and cream, each one puffed
    // out between its seams, with a scalloped hem and a vent hole at the top. A suspension line runs from every
    // seam down to two risers on his shoulders. Built in card units with its pivot at his feet; it follows him
    // while he floats down and the canopy breathes.
    void BuildChute()
    {
        chute = new GameObject("Parachute").transform;
        chute.SetParent(content, false);
        float h = avatar.localScale.y * 0.2f, top = h + 0.2f; // long lines, like a real rig

        var go = new GameObject("Canopy", typeof(MeshFilter), typeof(MeshRenderer));
        canopy = go.transform;
        canopy.SetParent(chute, false);
        canopy.localPosition = new Vector3(0, top, 0);
        canopySize = Vector3.one * 0.32f;
        canopy.localScale = canopySize;
        go.GetComponent<MeshFilter>().sharedMesh = CanopyMesh();
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { Lit(Orange, 0.15f), Lit(Cream) };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // the lines: one from each seam on the hem to the riser on that side, then the risers to his shoulders
        var line = Lit(Navy);
        for (int side = -1; side <= 1; side += 2)
        {
            var shoulder = new Vector3(side * 0.012f, h * 0.8f, 0);
            var riser = shoulder + new Vector3(side * 0.006f, 0.035f, 0);
            Cord(chute, shoulder, riser, 0.0022f, line);
            for (int g = 0; g < Gores; g++)
            {
                float b = g * Mathf.PI * 2 / Gores + 0.01f; // nudged so no seam sits exactly on the dividing line
                if (Mathf.Sign(Mathf.Cos(b)) != side) continue;
                Cord(chute, riser, canopy.localPosition + Vector3.Scale(CanopyPoint(b, HemSeam, 0), canopySize), 0.0011f, line);
            }
        }
        chute.gameObject.SetActive(false);
    }

    void Cord(Transform parent, Vector3 a, Vector3 b, float width, Material mat)
    {
        var d = b - a;
        var cord = Prim(PrimitiveType.Cylinder, "Line", parent, (a + b) / 2, new Vector3(width, d.magnitude / 2, width), mat);
        cord.localRotation = Quaternion.FromToRotation(Vector3.up, d);
    }

    // A point on the canopy (unit size, pivot at its centre): `around` is the angle round it, `down` the angle
    // from the top, and `puff` how far out the cloth bulges between two seams (0 on a seam, 1 mid-panel).
    static Vector3 CanopyPoint(float around, float down, float puff)
    {
        float a = down * Mathf.Deg2Rad, r = 0.5f * (1 + 0.1f * puff * Mathf.Sin(a));
        return new Vector3(Mathf.Sin(a) * Mathf.Cos(around) * r, Mathf.Cos(a) * r * CanopyFlat, Mathf.Sin(a) * Mathf.Sin(around) * r);
    }

    // Two-sided (you see inside it from below), one submesh per colour, each gore with its own vertices so the
    // seams show as creases.
    static Mesh CanopyMesh()
    {
        const int across = 6, rings = 10;
        var v = new System.Collections.Generic.List<Vector3>();
        var tris = new[] { new System.Collections.Generic.List<int>(), new System.Collections.Generic.List<int>() };
        for (int g = 0; g < Gores; g++)
            for (int face = 0; face < 2; face++) // 0 outside, 1 inside
            {
                int start = v.Count;
                for (int r = 0; r <= rings; r++)
                    for (int k = 0; k <= across; k++)
                    {
                        float f = (float)k / across, puff = Mathf.Sin(Mathf.PI * f);
                        float hem = Mathf.Lerp(HemSeam, HemMiddle, puff); // the hem rises between seams: scallops
                        float down = Mathf.Lerp(VentAngle, hem, (float)r / rings);
                        v.Add(CanopyPoint((g + f) * Mathf.PI * 2 / Gores, down, puff));
                    }
                var list = tris[g % 2];
                for (int r = 0; r < rings; r++)
                    for (int k = 0; k < across; k++)
                    {
                        int i0 = start + r * (across + 1) + k, i1 = i0 + across + 1;
                        if (face == 0) list.AddRange(new[] { i0, i0 + 1, i1, i0 + 1, i1 + 1, i1 });
                        else list.AddRange(new[] { i0, i1, i0 + 1, i0 + 1, i1, i1 + 1 });
                    }
            }
        var mesh = new Mesh { name = "Canopy" };
        mesh.SetVertices(v);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(tris[0], 0);
        mesh.SetTriangles(tris[1], 1);
        mesh.RecalculateNormals();
        return mesh;
    }

    // The game-console style "Achievement unlocked" card that slides down from the top.
    void BuildEggPanel()
    {
        var panel = Box("Achievement", ui.root.transform, NavyGlass);
        eggRect = panel.rectTransform;
        Place(eggRect, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 260), new Vector2(760, 200), new Vector2(0.5f, 1));
        var edge = Box("Edge", panel.transform, Orange);
        Place(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(14, 0), new Vector2(0, 0.5f));
        var back = Box("Trophy Back", panel.transform, NavyLight);
        Place(back.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(136, 136), new Vector2(0, 0.5f));
        var tex = Resources.Load<Texture2D>("Icons/icon_trophy");
        var sprite = tex ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100) : null;
        eggIcon = Icon(back.transform, sprite, new Color(1f, 0.8f, 0.25f), 96).rectTransform;
        eggTitle = Label(panel.transform, "", 28, Orange, FontStyle.Bold, TextAnchor.MiddleLeft);
        Place(eggTitle.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(97, 52), new Vector2(-234, 40));
        var head = Label(panel.transform, "Easter Egg Hunter", 50, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
        Place(head.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(97, 4), new Vector2(-234, 64));
        eggSub = Label(panel.transform, "", 28, Slate, FontStyle.Normal, TextAnchor.MiddleLeft);
        Place(eggSub.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(97, -50), new Vector2(-234, 40));
        eggPanel = panel.gameObject;
        eggPanel.SetActive(false);
    }

    void ResetRocket()
    {
        rocketing = pilotAway = landed = false;
        charge = ropeMode = 0;
        if (!character) return;
        character.localScale = charScale;
        character.localPosition = charRest;
        character.localRotation = charRot;
        if (pose) { pose.seated = 1; pose.typing = 1; pose.lookAt.localPosition = lookRest; }
        flame.gameObject.SetActive(false);
        chute.gameObject.SetActive(false);
        SetRate(cloud, 0);
        foreach (var ps in new[] { exhaust, cloud, shells, flash }) ps.Clear();
        for (int i = 0; i < hubs.Length; i++) { towed[i] = stemCut[i] = false; hubs[i].spoke.widthMultiplier = 1; }
        eggPanel.SetActive(false);
    }

    // ---------- Charging ----------

    // Called for every tap on the logo (on the main screen).
    void LogoTap()
    {
        if (rocketing) return;
        if (Current != State.Hub) { StartCoroutine(SpinLogo()); return; } // with a node scene open it only spins
        if (Time.time - chargeTime > 1f) charge = 0; // too slow: start counting again
        charge++;
        chargeTime = Time.time;
        chargeSource.pitch = 1 + 0.14f * (charge - 1);
        if (tickClip) chargeSource.PlayOneShot(tickClip, 0.9f);
        Haptics.Buzz(15 + 12 * charge, charge >= 4 ? Haptics.Medium : Haptics.Light);
        Burst(logo.position, 4 + 6 * charge);
        switch (charge)
        {
            case 1: StartCoroutine(SpinLogo()); break;
            case 2: if (pose) pose.typing = 0; break;
            case 3: StartCoroutine(Glitch()); ShowBubble("Uh oh... what are you doing?", 2f); break;
            case 4: flame.gameObject.SetActive(true); StartCoroutine(TeleportOut()); break;
            default: StartCoroutine(RocketShow()); break;
        }
    }

    // A tap anywhere else, or too long a pause, cools the logo down.
    void CancelCharge()
    {
        if (charge == 0 || rocketing) return;
        charge = 0;
        flame.gameObject.SetActive(false);
        if (pose) { pose.typing = 1; pose.lookAt.localPosition = lookRest; }
        if (pilotAway) StartCoroutine(TeleportBack());
    }

    // He flickers like a hologram losing signal.
    IEnumerator Glitch()
    {
        for (int i = 0; i < 6 && !pilotAway; i++)
        {
            character.localScale = i % 2 == 0 ? Vector3.Scale(charScale, new Vector3(1.12f, 0.85f, 1.12f)) : charScale * 0.001f;
            yield return new WaitForSeconds(0.05f);
            if (pilotAway) yield break; // he is already teleporting: leave his size to that
            character.localScale = charScale;
            yield return new WaitForSeconds(0.07f);
        }
    }

    IEnumerator TeleportOut()
    {
        pilotAway = true;
        PlaySfx(teleportClip);
        swirl.transform.localPosition = avatar.localPosition;
        swirl.Play();
        yield return Ease.Run(0.45f, t =>
        {
            character.localScale = charScale * Mathf.Max(0.001f, 1 - Ease.InBack(t, 1.2f));
            character.localRotation = charRot * Quaternion.Euler(0, 720 * t, 0);
        });
        character.localScale = charScale * 0.001f;
        character.localRotation = charRot;
        Burst(character.position + content.up * 0.12f, 30);
        swirl.Stop();
    }

    IEnumerator TeleportBack()
    {
        PlaySfx(teleportClip);
        swirl.transform.localPosition = avatar.localPosition;
        swirl.Play();
        yield return Ease.Run(0.45f, t => character.localScale = charScale * Mathf.Max(0.001f, Ease.OutBack(t, 1.4f)));
        character.localScale = charScale;
        swirl.Stop();
        pilotAway = false;
        ShowBubble("Phew!", 1.5f);
    }

    // ---------- The show ----------

    IEnumerator RocketShow()
    {
        rocketing = true;
        charge = 0;
        landed = false;
        HideBubble();
        ShowBanner(false);
        ropeMode = 1;
        for (int i = 0; i < hubs.Length; i++) { towed[i] = stemCut[i] = false; nodeVel[i] = Vector3.zero; }

        // ignition: it rumbles on the pad, the flame roars up and smoke billows out across the card
        flame.gameObject.SetActive(true);
        logo.localRotation = Quaternion.identity;
        PlaySfx(rumbleClip);
        Haptics.Buzz(300, Haptics.Medium);
        Vector3 pad = logoRest, top = logoRest + Vector3.up * 0.9f;
        yield return Ease.Run(1.4f, t =>
        {
            flamePower = Mathf.Lerp(0.7f, 1.6f, t);
            logo.localPosition = pad + Random.insideUnitSphere * (0.002f + 0.006f * t);
            SetRate(cloud, Mathf.Lerp(15, 60, t));
        });

        // lift-off: a slow, heavy start that keeps building speed, straight up, still shaking
        PlaySfx(rumbleClip);
        Haptics.Buzz(500, Haptics.Medium);
        Burst(content.TransformPoint(new Vector3(pad.x, 0.02f, pad.z)), 50);
        SetRate(cloud, 0);
        cloud.Emit(50); // one last big push of smoke out from under it
        flash.Emit(new ParticleSystem.EmitParams { position = flame.position, applyShapeToPosition = true }, 1);
        flamePower = 2f;
        yield return Ease.Run(2.8f, t =>
        {
            float shake = 0.005f * (1 - t);
            logo.localPosition = Vector3.LerpUnclamped(pad, top, Ease.InCubic(t)) + Random.insideUnitSphere * shake;
            logo.localRotation = Quaternion.Euler(0, 90f * t * t, 0);
            if (t > 0.5f && t - Time.deltaTime / 2.8f <= 0.5f) PlaySfx(rumbleClip, 0.8f); // keep it roaring
        });

        // boom: the tethers snap, fireworks, and out pops the pilot
        Vector3 blast = logo.position;
        logo.gameObject.SetActive(false);
        flame.gameObject.SetActive(false);
        ropeMode = 2;
        StartCoroutine(SnapTethers());
        StartCoroutine(Fireworks(blast));
        StartCoroutine(Parachute(blast));

        yield return new WaitForSeconds(1.3f); // the nodes tumble and bounce on the card meanwhile
        yield return NodesHome();
        yield return LogoLand();
        for (int i = 0; i < hubs.Length; i++) StartCoroutine(Reconnect(i, i * 0.1f));
        yield return new WaitUntil(() => landed);
        yield return new WaitForSeconds(0.4f);
        StartCoroutine(ShowAchievement());
        yield return new WaitForSeconds(2.6f);
        avatarAnim.CrossFade("Look", 0.4f);
        if (pose) { pose.typing = 1; pose.lookAt.localPosition = lookRest; }
        rocketing = false;
        ShowBanner(true);
    }

    static void SetRate(ParticleSystem ps, float perSecond)
    {
        var emission = ps.emission;
        emission.rateOverTime = perSecond;
    }

    IEnumerator SnapTethers()
    {
        for (int i = 0; i < hubs.Length; i++)
        {
            hubs[i].grow = 0; // its line is gone
            if (towed[i]) { PlaySfx(snapClip, 0.8f); Haptics.Buzz(20, Haptics.Light); }
            yield return new WaitForSeconds(0.07f);
        }
    }

    IEnumerator Fireworks(Vector3 at)
    {
        Color32[] colours = { Orange, new Color(1f, 0.82f, 0.3f), new Color(0.45f, 0.8f, 1f) };
        var trails = shells.trails;
        trails.minVertexDistance = 0.004f * content.lossyScale.x; // world units: about 4 mm on the card
        for (int i = 0; i < 3; i++)
        {
            var pos = at + content.TransformVector(Random.insideUnitSphere * 0.1f);
            flash.Emit(new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true }, 1);
            shells.Emit(new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true, startColor = colours[i] }, 90);
            Burst(pos, 25);
            PlaySfx(bangClip, 1f - i * 0.2f);
            Haptics.Buzz(60, Haptics.Medium);
            yield return new WaitForSeconds(0.18f);
        }
    }

    // The freed nodes hop back onto their printed spots, one after another, and their stems reconnect.
    IEnumerator NodesHome()
    {
        ropeMode = 0;
        for (int i = 0; i < hubs.Length; i++)
        {
            if (!towed[i]) continue;
            var node = hubs[i].node;
            Vector3 from = node.localPosition, to = hubs[i].rest;
            int k = i;
            StartCoroutine(Ease.Run(0.5f, t => node.localPosition = Vector3.Lerp(from, to, Ease.InOutSine(t)) + Vector3.up * (Mathf.Sin(Mathf.PI * t) * 0.12f)));
            yield return new WaitForSeconds(0.12f);
            StartCoroutine(Arrive(k, 0.4f));
        }
        yield return new WaitForSeconds(0.45f);
    }

    IEnumerator Arrive(int i, float delay)
    {
        yield return new WaitForSeconds(delay);
        hubs[i].node.localPosition = hubs[i].rest;
        towed[i] = stemCut[i] = false;
        Burst(hubs[i].node.position, 10);
        PlaySfx(tickClip, 0.5f);
    }

    // It drops back from above with a spin, squashes on landing, and kicks up a little dust.
    IEnumerator LogoLand()
    {
        logoSpin = 0;
        logo.localScale = logoScale;
        logo.gameObject.SetActive(true);
        Vector3 high = logoRest + Vector3.up * 0.7f;
        yield return Ease.Run(0.6f, t =>
        {
            logo.localPosition = Vector3.Lerp(high, logoRest, Ease.InQuad(t));
            logo.localRotation = Quaternion.Euler(0, 720 * t, 0);
        });
        logo.localRotation = Quaternion.identity;
        Burst(content.TransformPoint(new Vector3(logoRest.x, 0.01f, logoRest.z)), 30);
        PlaySfx(popClip);
        Haptics.Buzz(50, Haptics.Medium);
        yield return Ease.Run(0.35f, t =>
        {
            float s = 1 + 0.35f * Mathf.Sin(Mathf.PI * t) * (1 - t);
            logo.localScale = Vector3.Scale(logoScale, new Vector3(s, 1 / s, s));
        });
        logo.localScale = logoScale;
    }

    // A line shoots back out from the logo to its node and sparks when it connects.
    IEnumerator Reconnect(int i, float delay)
    {
        yield return new WaitForSeconds(delay);
        yield return Ease.Run(0.3f, t => hubs[i].grow = Ease.OutCubic(t));
        hubs[i].grow = 1;
        Burst(hubs[i].node.position, 12);
    }

    // He pops out of the blast under a parachute and drifts back down into his chair, waving.
    IEnumerator Parachute(Vector3 blastWorld)
    {
        Vector3 from = character.parent.InverseTransformPoint(blastWorld);
        character.localPosition = from;
        character.localRotation = charRot;
        if (pose) pose.seated = 0;
        chute.gameObject.SetActive(true);
        FollowChute(0);
        avatarAnim.CrossFade("Wave", 0.1f);
        yield return Ease.Run(0.35f, t =>
        {
            float s = Mathf.Max(0.001f, Ease.OutBack(t, 1.6f));
            character.localScale = charScale * s;
            chute.localScale = Vector3.one * s;
        });
        ShowBubble("Surprise! It was me!", 2.2f);
        float sway = 0.05f / Mathf.Max(0.01f, character.parent.lossyScale.x / content.lossyScale.x); // 0.05 card units, in his parent's units
        yield return Ease.Run(3.4f, t =>
        {
            var p = Vector3.Lerp(from, charRest, Ease.OutQuad(t));
            p.x += Mathf.Sin(t * Mathf.PI * 3) * sway * (1 - t);
            character.localPosition = p;
            FollowChute(t);
        });
        character.localPosition = charRest;
        pilotAway = false;

        // touchdown: he settles into the seat and the canopy collapses
        PlaySfx(popClip, 0.8f);
        Haptics.Buzz(40, Haptics.Light);
        Burst(character.position, 20);
        avatarAnim.CrossFade("Cheer", 0.2f);
        ShowBubble("Nailed the landing!", 2.2f);
        StartCoroutine(Ease.Run(0.5f, t => { if (pose) pose.seated = t; }));
        yield return Ease.Run(0.45f, t => chute.localScale = new Vector3(1 + 0.4f * t, Mathf.Max(0.001f, 1 - Ease.InCubic(t)), 1 + 0.4f * t));
        chute.gameObject.SetActive(false);
        landed = true;
    }

    void FollowChute(float t)
    {
        chute.localPosition = content.InverseTransformPoint(character.position);
        chute.localRotation = Quaternion.Euler(0, 0, 9f * Mathf.Sin(Time.time * 2.2f) * (1 - t));
        float breathe = 0.035f * Mathf.Sin(Time.time * 3.1f) * (1 - t); // the canopy breathes as air spills out
        canopy.localScale = Vector3.Scale(canopySize, new Vector3(1 + breathe, 1 - breathe * 1.5f, 1 + breathe));
    }

    IEnumerator ShowAchievement()
    {
        bool first = PlayerPrefs.GetInt(EggKey, 0) == 0;
        PlayerPrefs.SetInt(EggKey, 1);
        PlayerPrefs.Save();
        eggTitle.text = first ? "ACHIEVEMENT UNLOCKED" : "ACHIEVEMENT";
        eggSub.text = first ? "Found the secret in the logo" : "Found again!";
        eggPanel.SetActive(true);
        PlaySfx(achievementClip ? achievementClip : sparkleClip);
        Haptics.Buzz(40, Haptics.Medium);
        yield return Ease.Run(0.45f, t =>
        {
            eggRect.anchoredPosition = new Vector2(0, Mathf.LerpUnclamped(260, -160, Ease.OutBack(t, 1.2f)));
            eggIcon.localScale = Vector3.one * Mathf.Max(0.001f, Ease.OutBack(Mathf.Clamp01(t * 1.6f - 0.4f), 2f));
        });
        yield return new WaitForSeconds(3f);
        yield return Ease.Run(0.35f, t => eggRect.anchoredPosition = new Vector2(0, Mathf.Lerp(-160, 260, Ease.InCubic(t))));
        eggPanel.SetActive(false);
    }

    // ---------- Every frame ----------

    // Charge cool-down, the flame and its smoke, and the towed network.
    void UpdateRocket(float t, float dt)
    {
        if (!character) return;
        if (charge >= 2 && !rocketing)
        {
            if (Time.time - chargeTime > 1.5f) CancelCharge();
            else if (pose) pose.lookAt.position = Vector3.Lerp(pose.lookAt.position, logo.position, 1 - Mathf.Exp(-6f * dt));
        }

        if (flame.gameObject.activeSelf)
        {
            flame.localRotation = logo.localRotation;
            flame.localPosition = logo.localPosition - logo.localRotation * Vector3.up * (logoScale.y * 0.62f);
            float power = rocketing ? flamePower : 0.7f;
            var main = fire.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f * power, 0.6f * power);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f * power, 0.032f * power);
            SetRate(fire, 70 * power);
            main = core.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f * power, 0.065f * power);
            SetRate(core, 60);
            flameLight.intensity = power * (1.2f + 0.8f * Mathf.PerlinNoise(t * 14f, 0)); // flickering firelight on the card
            flameLight.range = 0.45f * power * content.lossyScale.x;
            exhaust.transform.localPosition = flame.localPosition;
            SetRate(exhaust, rocketing ? 30 : 0);
        }
        else SetRate(exhaust, 0);

        if (ropeMode == 1) TowNetwork(dt);
        else if (ropeMode == 2) DropNetwork(dt);
        if (ropeMode != 0)
            foreach (var h in hubs) h.node.Rotate(0, 220f * dt, 0, Space.Self);
    }

    // Each node waits on the card until its line is stretched too far, then it is yanked up and hangs from the
    // rocket on a stretchy line, tied to its neighbours, so the four of them swing behind it like a kite tail.
    void TowNetwork(float dt)
    {
        Vector3 rocket = logo.localPosition;
        for (int i = 0; i < hubs.Length; i++)
        {
            var node = hubs[i].node;
            if (!towed[i])
            {
                node.localPosition = hubs[i].rest;
                if (Vector3.Distance(rocket, hubs[i].rest) > spokeRest[i] * 1.5f + 0.05f)
                {
                    towed[i] = stemCut[i] = true;
                    nodeVel[i] = Vector3.up * 0.4f;
                    PlaySfx(yankClip);
                    Haptics.Buzz(30, Haptics.Light);
                    Burst(node.position, 14);
                }
                continue;
            }
            Vector3 p = node.localPosition, v = nodeVel[i] + Gravity * dt;
            Vector3 d = rocket - p;
            float len = d.magnitude, rope = 0.22f;
            if (len > rope) v += d / len * ((len - rope) * 60f * dt);
            foreach (int j in new[] { i - 1, i + 1 }) // the ring lines tie neighbours together
            {
                if (j < 0 || j >= hubs.Length || !towed[j]) continue;
                Vector3 e = hubs[j].node.localPosition - p;
                float el = e.magnitude;
                if (el > 1e-4f) v += e / el * ((el - 0.15f) * 25f * dt);
            }
            v *= Mathf.Exp(-1.2f * dt);
            p += v * dt;
            if (p.y < 0.03f) { p.y = 0.03f; v.y = Mathf.Max(0, v.y); }
            nodeVel[i] = v;
            node.localPosition = p;
        }
    }

    // After the snap: the nodes fall, bounce on the card and roll to a stop.
    void DropNetwork(float dt)
    {
        for (int i = 0; i < hubs.Length; i++)
        {
            if (!towed[i]) continue;
            var node = hubs[i].node;
            Vector3 p = node.localPosition, v = nodeVel[i] + Gravity * dt * 1.5f;
            p += v * dt;
            float floor = hubs[i].rest.y;
            if (p.y < floor)
            {
                p.y = floor;
                v = new Vector3(v.x * 0.6f, -v.y * 0.45f, v.z * 0.6f);
                if (Mathf.Abs(v.y) > 0.08f) Burst(node.position, 4);
            }
            p.x = Mathf.Clamp(p.x, -0.48f, 0.48f); // stay over the card
            p.z = Mathf.Clamp(p.z, -0.28f, 0.28f);
            nodeVel[i] = v;
            node.localPosition = p;
        }
    }

    // The logo-to-node lines quiver while it charges and thin out and glow orange as the rocket stretches them.
    void StyleSpoke(int i, float t)
    {
        var line = hubs[i].spoke;
        float stretch = Vector3.Distance(logo.localPosition, hubs[i].node.localPosition) / Mathf.Max(0.01f, spokeRest[i]);
        float width = Mathf.Clamp(1 / Mathf.Max(1, stretch), 0.35f, 1f);
        if (charge >= 2 && !rocketing) width *= 1 + 0.3f * (charge / 4f) * Mathf.Sin(t * 70f + i * 1.3f);
        line.widthMultiplier = width;
        line.startColor = line.endColor = Color.Lerp(spokeColour, Orange, Mathf.Clamp01((stretch - 1) * 1.5f));
    }

    bool StemCut(int i) => stemCut != null && stemCut[i];
}
