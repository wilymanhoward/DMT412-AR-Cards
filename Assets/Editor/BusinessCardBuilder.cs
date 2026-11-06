using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using Vuforia;
using Object = UnityEngine.Object;
using static ChristmasCardBuilder; // the shared helpers (Hex, NewChild, NewPS, Wrap, ...)

// Tools > Business Card > Build Business Card: (re)builds the 3D scene on the "Business Target" and wires the
// controller into the hub. Only touches the business card, never the Christmas card.
// The words come from Assets/Resources/business_card.json, so changing them needs no rebuild.
// Re-running it replaces everything it made, so ask for changes rather than editing the scene by hand,
// or carry hand edits over first.
public static class BusinessCardBuilder
{
    const string Gen = "Assets/Generated/";
    const string IconDir = "Assets/BusinessCard/Icons/";
    const string Boy = "Assets/polyperfect/Poly Christmas/Prefabs/People/Seasons People/Boy_Large_Christmas.prefab";

    // The card design is 1024 x 614 px and the target is 1 x 0.6 units, so the spots below, read off the design,
    // land on the printed logo and on four of its orange diamonds. +z is the top of the card.
    const float CardW = 1f, CardD = 0.6f;
    const int PixW = 1024, PixH = 614;
    const float HubHeight = 0.11f, LogoHeight = 0.15f, LogoSize = 0.115f, AvatarHeight = 0.2f;

    static readonly (string title, float px, float py)[] HubSpots =
    {
        ("About", 503, 191), ("Skills", 801, 142), ("Portfolio", 758, 486), ("Contact", 455, 560),
    };
    const float DeskSize = 1.45f; // the mini-me and his desk, as a multiple of the size they are built at
    const float DeskYaw = 55; // which way he faces: 0 = up the card, 90 = to the right
    const float LogoPx = 125, LogoPy = 125, AvatarPx = 190, AvatarPy = 405;

    static readonly Color Navy = Hex("14213D"), Orange = Hex("F26A1B"), Cream = Hex("F7F5F0"), Slate = Hex("5C6B8F");
    static Material navyMat, orangeMat, creamMat, lineMat, sparkMat, glowMat;

    static Vector3 OnCard(float px, float py, float y) => new Vector3((px / PixW - 0.5f) * CardW, y, (0.5f - py / PixH) * CardD);

    [MenuItem("Tools/Business Card/Build Business Card")]
    public static void Build()
    {
        var target = GameObject.Find("Business Target");
        if (!target)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/Scenes/ARCards.unity");
            target = GameObject.Find("Business Target");
        }
        if (!target) throw new System.Exception("There is no \"Business Target\" in the scene.");

        MakeAssets(); // the shared glow and star textures and the rounded UI sprite
        // apply the portfolio import rule (PortfolioImportSettings) to pictures imported before it existed
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Portfolio" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        Cleanup(target.transform);
        MakeMaterials();
        var octahedron = Octahedron();

        var content = NewChild("Business Content", target.transform);
        var net = NewChild("Network", content);
        var logo = BuildLogo(net);
        var hubs = new BusinessCardController.Hub[HubSpots.Length];
        for (int i = 0; i < hubs.Length; i++) hubs[i] = BuildHub(net, HubSpots[i].title, HubSpots[i].px, HubSpots[i].py, octahedron);
        var ring = new LineRenderer[hubs.Length - 1];
        for (int i = 0; i < ring.Length; i++) ring[i] = NewLine("Ring " + (i + 1), net, 0.0035f, Slate);
        var pulse = BuildPulse(net);

        var fx = NewChild("FX", content);
        fx.localPosition = new Vector3(0, 0.04f, 0);
        var burst = BuildBurst(fx);
        var motes = BuildMotes(fx);
        var swirl = BuildSwirl(fx);

        var avatar = BuildAvatar(content, out var head);

        // controller lives outside the target so it keeps running while the card is lost
        var c = new GameObject("Business Controller").AddComponent<BusinessCardController>();
        c.arCamera = Object.FindAnyObjectByType<VuforiaBehaviour>().GetComponent<Camera>();
        c.content = content;
        c.logo = logo;
        c.hubs = hubs;
        c.ring = ring;
        c.pulse = pulse;
        c.avatar = avatar;
        c.avatarHead = head;
        c.burst = burst;
        c.motes = motes;
        c.swirl = swirl;

        c.music = NewAudio(c.gameObject, FindClip("biz_ambient"), loop: true);
        c.sfx = NewAudio(c.gameObject, null, loop: false);
        c.tickClip = FindClip("biz_tick");
        c.openClip = FindClip("biz_open");
        c.pulseClip = FindClip("biz_pulse");
        c.powerupClip = FindClip("biz_powerup");
        c.sparkleClip = FindClip("sparkle");
        c.popClip = FindClip("pop");

        c.font = Load<Font>("Assets/Fonts/Fredoka.ttf");
        c.panelSprite = Load<Sprite>(Gen + "UI_Rounded.png");
        c.iconMusic = Load<Sprite>(Gen + "UI_Icon_Music.png");
        c.iconMuted = Load<Sprite>(Gen + "UI_Icon_Muted.png");
        c.iconMail = IconSprite("icon_mail");
        c.iconPhone = IconSprite("icon_phone");
        c.iconGlobe = IconSprite("icon_globe");
        c.iconClose = IconSprite("icon_close");
        c.iconAbout = IconSprite("icon_about");
        c.iconSkills = IconSprite("icon_skills");
        c.iconPortfolio = IconSprite("icon_portfolio");
        c.iconPrev = IconSprite("icon_prev");
        c.iconNext = IconSprite("icon_next");

        // the target keeps its content drawn through brief EXTENDED_TRACKED flickers (the hub decides "lost")
        var handler = target.GetComponent<DefaultObserverEventHandler>();
        handler.StatusFilter = DefaultObserverEventHandler.TrackingStatusFilter.Tracked_ExtendedTracked;
        handler.UsePoseSmoothing = true;
        ClearListeners(handler.OnTargetFound);
        ClearListeners(handler.OnTargetLost);

        var hub = Object.FindAnyObjectByType<CardHub>();
        if (hub)
        {
            hub.business = c;
            EditorUtility.SetDirty(hub);
        }
        else Debug.LogWarning("No Card Hub in the scene: build the Christmas card first (Tools > Christmas Card).");

        EditorSceneManager.MarkSceneDirty(target.scene);
        EditorSceneManager.SaveScene(target.scene);
        Selection.activeGameObject = content.gameObject;
        Debug.Log($"BUSINESS_CARD_BUILT hubs {hubs.Length}, rings {ring.Length}, hub wired {hub != null}, " +
                  $"clips: ambient={(bool)c.music.clip} tick={(bool)c.tickClip} open={(bool)c.openClip} pulse={(bool)c.pulseClip} " +
                  $"powerup={(bool)c.powerupClip} sparkle={(bool)c.sparkleClip} pop={(bool)c.popClip}, " +
                  $"icons={(bool)c.iconMail && (bool)c.iconClose}, font={c.font.name}");
    }

    static void Cleanup(Transform target)
    {
        foreach (var n in new[] { "Test Cube", "Business Content" })
        {
            var old = target.Find(n);
            if (old) Object.DestroyImmediate(old.gameObject);
        }
        var controller = GameObject.Find("Business Controller");
        if (controller) Object.DestroyImmediate(controller);
    }

    // ---------------------------------------------------------------- materials and meshes

    static void MakeMaterials()
    {
        navyMat = Standard("Biz_Navy", Navy, 0.45f, 0.1f, null);
        orangeMat = Standard("Biz_Orange", Orange, 0.4f, 0f, Orange * 0.9f);
        creamMat = Standard("Biz_Cream", Cream, 0.3f, 0f, null);
        lineMat = MakeMaterial("Biz_Line", "Sprites/Default", null);
        // alpha-blended (not additive): the card is light, and additive light would vanish on it
        sparkMat = MakeMaterial("Biz_Spark", "Legacy Shaders/Particles/Alpha Blended", Load<Texture2D>(Gen + "FX_Star.png"));
        glowMat = MakeMaterial("Biz_Glow", "Legacy Shaders/Particles/Alpha Blended", Load<Texture2D>(Gen + "FX_Glow.png"));
        AssetDatabase.SaveAssets();
    }

    static Material Standard(string name, Color colour, float smoothness, float metallic, Color? emission)
    {
        var m = MakeMaterial(name, "Standard", null);
        m.color = colour;
        m.SetFloat("_Glossiness", smoothness);
        m.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // The glowing diamond: two four-sided pyramids base to base, flat-shaded.
    static Mesh Octahedron()
    {
        string path = Gen + "Octahedron.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) return existing;
        Vector3[] p = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        int[,] faces = { { 0, 4, 2 }, { 0, 3, 4 }, { 0, 5, 3 }, { 0, 2, 5 }, { 1, 2, 4 }, { 1, 4, 3 }, { 1, 3, 5 }, { 1, 5, 2 } };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int f = 0; f < 8; f++)
        {
            Vector3 a = p[faces[f, 0]], b = p[faces[f, 1]], c = p[faces[f, 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0) { var swap = b; b = c; c = swap; } // wind outward
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c });
            triangles.AddRange(new[] { i, i + 1, i + 2 });
        }
        var mesh = new Mesh { name = "Octahedron" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // ---------------------------------------------------------------- the 3D logo

    // A navy block with an orange corner accent and a blocky HW (built from bars) on both faces, floating
    // above the printed logo. Its front faces -z, toward the viewer.
    static Transform BuildLogo(Transform parent)
    {
        var logo = NewChild("Logo", parent);
        logo.localPosition = OnCard(LogoPx, LogoPy, LogoHeight);
        logo.localScale = Vector3.one * LogoSize;
        Cube("Plaque", logo, Vector3.zero, new Vector3(1, 1, 0.3f), navyMat);
        var corner = Cube("Corner", logo, new Vector3(0.38f, 0.38f, -0.15f), new Vector3(0.2f, 0.2f, 0.08f), orangeMat);
        corner.localRotation = Quaternion.Euler(0, 0, 45);
        corner = Cube("Corner Back", logo, new Vector3(0.38f, 0.38f, 0.15f), new Vector3(0.2f, 0.2f, 0.08f), orangeMat);
        corner.localRotation = Quaternion.Euler(0, 0, 45);
        Letters(logo, true);
        Letters(logo, false);
        var box = logo.gameObject.AddComponent<BoxCollider>(); // generous: easy to tap
        box.size = new Vector3(1.4f, 1.4f, 0.6f);
        return logo;
    }

    static void Letters(Transform logo, bool front)
    {
        var g = NewChild(front ? "HW Front" : "HW Back", logo);
        g.localPosition = new Vector3(0, 0, front ? -0.19f : 0.19f);
        g.localRotation = front ? Quaternion.identity : Quaternion.Euler(0, 180, 0); // reads right from behind too
        const float stroke = 0.095f;
        // H
        Bar(g, new Vector2(-0.34f, -0.25f), new Vector2(-0.34f, 0.25f), stroke);
        Bar(g, new Vector2(-0.12f, -0.25f), new Vector2(-0.12f, 0.25f), stroke);
        Bar(g, new Vector2(-0.34f, 0), new Vector2(-0.12f, 0), stroke);
        // W
        Vector2[] w = { new Vector2(0.0f, 0.25f), new Vector2(0.08f, -0.25f), new Vector2(0.18f, 0.08f), new Vector2(0.28f, -0.25f), new Vector2(0.36f, 0.25f) };
        for (int i = 0; i < w.Length - 1; i++) Bar(g, w[i], w[i + 1], stroke);
    }

    // A cream bar from a to b (in the face's x/y plane).
    static void Bar(Transform parent, Vector2 a, Vector2 b, float thickness)
    {
        Vector2 d = b - a;
        var bar = Cube("Bar", parent, (a + b) / 2, new Vector3(thickness, d.magnitude + thickness * 0.6f, 0.08f), creamMat);
        bar.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg); // a vertical bar turned to point along d
    }

    static Transform Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material) =>
        Primitive(PrimitiveType.Cube, name, parent, position, scale, material);

    static Transform Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>()); // taps are handled by the parent's collider
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    // ---------------------------------------------------------------- hubs, lines, pulse

    // A glowing orange diamond hovering over a printed orange diamond, tied to it by a thin stem.
    static BusinessCardController.Hub BuildHub(Transform net, string title, float px, float py, Mesh octahedron)
    {
        var node = NewChild("Hub " + title, net);
        node.localPosition = OnCard(px, py, HubHeight);
        node.localScale = Vector3.one * 0.045f;
        node.gameObject.AddComponent<MeshFilter>().sharedMesh = octahedron;
        var r = node.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = orangeMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        node.gameObject.AddComponent<SphereCollider>().radius = 1.7f; // about 0.08 across in the world: easy to tap

        // a slow soft pulse of light behind it (sizes are in the node's own, shrunken units)
        var halo = NewPS("Halo", node, glowMat, local: true);
        var main = halo.main;
        main.playOnAwake = true;
        main.loop = true;
        main.startLifetime = 2f;
        main.startSpeed = 0;
        main.startSize = 4.5f;
        main.startColor = new Color(Orange.r, Orange.g, Orange.b, 0.55f);
        main.maxParticles = 4;
        var emission = halo.emission;
        emission.rateOverTime = 1f;
        var shape = halo.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.001f;
        SizeCurve(halo, 0, 0.6f, 0.5f, 1, 1, 0.6f);
        Fade(halo, 0.4f, 0.6f);

        return new BusinessCardController.Hub
        {
            title = title,
            node = node,
            spoke = NewLine("Spoke " + title, net, 0.0045f, Navy),
            stem = NewLine("Stem " + title, net, 0.003f, Orange),
        };
    }

    static LineRenderer NewLine(string name, Transform parent, float width, Color colour)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 2;
        line.sharedMaterial = lineMat;
        line.widthCurve = AnimationCurve.Constant(0, 1, width);
        line.widthMultiplier = 1; // the controller fades the ring lines in through this
        line.startColor = line.endColor = colour;
        line.numCapVertices = 4;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false; // the controller draws it
        return line;
    }

    // The light that runs along the lines: a small glowing orange ball.
    static Transform BuildPulse(Transform net)
    {
        var pulse = NewChild("Pulse", net);
        Primitive(PrimitiveType.Sphere, "Ball", pulse, Vector3.zero, Vector3.one * 0.022f, orangeMat);

        var halo = NewPS("Halo", pulse, glowMat, local: true);
        var main = halo.main;
        main.playOnAwake = true;
        main.loop = true;
        main.startLifetime = 0.5f;
        main.startSpeed = 0;
        main.startSize = 0.13f;
        main.startColor = new Color(Orange.r, Orange.g, Orange.b, 0.7f);
        main.maxParticles = 6;
        var emission = halo.emission;
        emission.rateOverTime = 10f;
        var shape = halo.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.001f;
        Fade(halo, 0.1f, 0.4f);
        pulse.gameObject.SetActive(false);
        return pulse;
    }

    // ---------------------------------------------------------------- effects

    static ParticleSystem BuildBurst(Transform fx)
    {
        var ps = NewPS("Burst", fx, sparkMat, local: false);
        var main = ps.main;
        main.startLifetime = Range(0.5f, 0.9f);
        main.startSpeed = Range(0.1f, 0.35f);
        main.startSize = Range(0.012f, 0.03f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Orange, Navy, Hex("FF9A55"));
        main.gravityModifier = 0.1f;
        main.maxParticles = 300;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.01f;
        SizeCurve(ps, 0, 1, 1, 0.2f);
        Fade(ps, 0.02f, 0.5f);
        return ps;
    }

    // Tiny dots drifting up over the card, so the air feels alive.
    static ParticleSystem BuildMotes(Transform fx)
    {
        var ps = NewPS("Motes", fx, glowMat, local: true);
        var main = ps.main;
        main.startLifetime = Range(5f, 7f);
        main.startSpeed = Range(0.01f, 0.03f);
        main.startSize = Range(0.006f, 0.014f);
        main.startColor = Colors(Orange, Navy, Slate);
        main.maxParticles = 60;
        var emission = ps.emission;
        emission.rateOverTime = 9;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.95f, 0.55f, 0.01f);
        shape.rotation = new Vector3(-90, 0, 0); // a flat sheet over the card, drifting up
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.02f;
        noise.frequency = 0.5f;
        Fade(ps, 0.2f, 0.6f);
        return ps;
    }

    // The orange spiral that gathers where the mini-me arrives.
    static ParticleSystem BuildSwirl(Transform fx)
    {
        var ps = NewPS("Swirl", fx, sparkMat, local: true);
        var main = ps.main;
        main.startLifetime = Range(1f, 1.4f);
        main.startSpeed = 0;
        main.startSize = Range(0.02f, 0.045f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Orange, Navy, Hex("FF9A55"));
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.rateOverTime = 80;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f;
        shape.radiusThickness = 0;
        shape.rotation = new Vector3(90, 0, 0);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = Range(0, 0);
        velocity.y = Range(0.3f, 0.5f);
        velocity.z = Range(0, 0);
        velocity.orbitalY = 4f;
        SizeCurve(ps, 0, 0, 0.2f, 1, 0.8f, 1, 1, 0);
        return ps;
    }

    // ---------------------------------------------------------------- the mini-me

    // polyperfect's boy in the card's navy and orange, with the ithappy Santa clips (they're Humanoid, so they fit).
    static Transform BuildAvatar(Transform content, out Transform head)
    {
        var avatar = Wrap(Boy, "Mini Me", Measure.Height, AvatarHeight);
        avatar.SetParent(content, false);
        avatar.localPosition = OnCard(AvatarPx, AvatarPy, 0);
        avatar.localScale = Vector3.one * DeskSize;
        avatar.localRotation = Quaternion.Euler(0, DeskYaw, 0); // he faces his monitor; we see him from the side and a little behind

        var animator = avatar.GetComponentInChildren<Animator>();
        animator.runtimeAnimatorController = CharacterController("Me",
            ("Look", "IdleLookAround", true), ("Wave", "WaveHello", false), ("Talk", "TalkGestureActive", false), ("Cheer", "Hip_Hop_Dancing_1", true));
        animator.applyRootMotion = false;
        var controller = (UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController;
        var layers = controller.layers;
        layers[0].iKPass = true; // the seated pose is IK
        controller.layers = layers;
        Recolour(avatar);
        BuildDesk(avatar, animator);

        var capsule = avatar.gameObject.AddComponent<CapsuleCollider>(); // for tapping him
        capsule.center = Vector3.up * AvatarHeight / 2;
        capsule.height = AvatarHeight;
        capsule.radius = 0.07f;
        head = NewChild("Head", avatar);
        head.localPosition = Vector3.up * AvatarHeight * 1.08f;
        avatar.gameObject.SetActive(false);
        return avatar;
    }

    // ---------------------------------------------------------------- the desk

    // He sits at a small desk (all in his own frame: +z is the way he faces, units are card units) doing game dev:
    // code scrolling on the left of the monitor, a tiny platformer with a hopping hero on the right.
    // The prop sizes are for a 0.2 tall boy; his body is lowered onto the seat and IK folds his legs and puts his hands on the keys.
    static void BuildDesk(Transform avatar, Animator animator)
    {
        var rig = NewChild("Desk", avatar);
        const float seat = 0.04f, deskTop = 0.086f;
        animator.transform.localPosition += Vector3.down * 0.048f;

        Primitive(PrimitiveType.Cylinder, "Rug", rig, new Vector3(0, 0.001f, 0.05f), new Vector3(0.3f, 0.001f, 0.3f), creamMat);
        Cube("Seat", rig, new Vector3(0, seat - 0.004f, -0.008f), new Vector3(0.056f, 0.008f, 0.056f), orangeMat);
        Cube("Chair Back", rig, new Vector3(0, seat + 0.036f, -0.038f), new Vector3(0.056f, 0.07f, 0.007f), orangeMat);
        Primitive(PrimitiveType.Cylinder, "Chair Post", rig, new Vector3(0, seat / 2 - 0.004f, -0.008f), new Vector3(0.008f, seat / 2 - 0.004f, 0.008f), navyMat);
        Primitive(PrimitiveType.Cylinder, "Chair Base", rig, new Vector3(0, 0.004f, -0.008f), new Vector3(0.05f, 0.003f, 0.05f), navyMat);

        Cube("Desk Top", rig, new Vector3(0, deskTop - 0.002f, 0.11f), new Vector3(0.16f, 0.004f, 0.11f), creamMat);
        foreach (var (x, z) in new[] { (-0.07f, 0.065f), (0.07f, 0.065f), (-0.07f, 0.155f), (0.07f, 0.155f) })
            Cube("Desk Leg", rig, new Vector3(x, deskTop / 2 - 0.002f, z), new Vector3(0.006f, deskTop - 0.004f, 0.006f), navyMat);

        Cube("Keyboard", rig, new Vector3(0, deskTop + 0.002f, 0.075f), new Vector3(0.06f, 0.004f, 0.02f), navyMat);
        Cube("Mouse", rig, new Vector3(0.05f, deskTop + 0.002f, 0.078f), new Vector3(0.011f, 0.004f, 0.016f), orangeMat);
        Primitive(PrimitiveType.Cylinder, "Mug", rig, new Vector3(-0.063f, deskTop + 0.008f, 0.12f), new Vector3(0.014f, 0.008f, 0.014f), orangeMat);

        float monitorY = deskTop + 0.047f, monitorZ = 0.14f;
        Cube("Monitor Stand", rig, new Vector3(0, deskTop + 0.012f, monitorZ), new Vector3(0.008f, 0.024f, 0.008f), navyMat);
        Cube("Monitor Foot", rig, new Vector3(0, deskTop + 0.001f, monitorZ), new Vector3(0.04f, 0.002f, 0.022f), navyMat);
        Cube("Monitor", rig, new Vector3(0, monitorY, monitorZ), new Vector3(0.104f, 0.062f, 0.006f), navyMat);
        float faceZ = monitorZ - 0.0035f; // the screen side, toward him (a Quad shows its face toward -z)
        var codeMat = MakeMaterial("Biz_Screen_Code", "Unlit/Texture", MakeScreenTexture("Biz_Code.png", true));
        var gameMat = MakeMaterial("Biz_Screen_Game", "Unlit/Texture", MakeScreenTexture("Biz_Game.png", false));
        codeMat.mainTextureScale = new Vector2(1, 0.5f);
        Primitive(PrimitiveType.Quad, "Screen Code", rig, new Vector3(0.0245f, monitorY, faceZ), new Vector3(0.047f, 0.054f, 1), codeMat);
        Primitive(PrimitiveType.Quad, "Screen Game", rig, new Vector3(-0.0265f, monitorY, faceZ), new Vector3(0.049f, 0.054f, 1), gameMat);
        var player = Cube("Hero", rig, new Vector3(0, monitorY, faceZ - 0.0015f), Vector3.one * 0.006f, orangeMat);

        var pose = animator.gameObject.AddComponent<SeatedPose>();
        Transform T(string name, float x, float y, float z)
        {
            var t = NewChild(name, rig);
            t.localPosition = new Vector3(x, y, z);
            t.localRotation = Quaternion.identity;
            return t;
        }
        pose.footL = T("Foot L", -0.018f, 0.006f, 0.052f); // his left is -x when he faces +z
        pose.footR = T("Foot R", 0.018f, 0.006f, 0.052f);
        pose.kneeL = T("Knee L", -0.02f, 0.06f, 0.11f);
        pose.kneeR = T("Knee R", 0.02f, 0.06f, 0.11f);
        pose.handL = T("Hand L", -0.016f, deskTop + 0.007f, 0.073f);
        pose.handR = T("Hand R", 0.016f, deskTop + 0.007f, 0.073f);
        pose.elbowL = T("Elbow L", -0.06f, 0.08f, -0.01f);
        pose.elbowR = T("Elbow R", 0.06f, 0.08f, -0.01f);
        pose.lookAt = T("Look At", 0, monitorY, monitorZ);
        pose.player = player;
        pose.playerRange = new Vector2(-0.0265f - 0.017f, -0.0265f + 0.017f);
        pose.playerFloor = monitorY - 0.0205f;
        pose.codeMat = codeMat;
    }

    // Tiny pixel-art screen pictures: a page of code lines (tiles vertically so it can scroll), or a platformer level.
    static Texture2D MakeScreenTexture(string file, bool code)
    {
        int w = code ? 48 : 49, h = code ? 64 : 54;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        var bg = code ? Hex("0E1730") : Hex("2B3F73");
        for (int i = 0; i < px.Length; i++) px[i] = bg;
        var rng = new System.Random(code ? 7 : 3);
        void Rect(int x, int y, int rw, int rh, Color c)
        {
            for (int j = y; j < y + rh && j < h; j++)
                for (int i = x; i < x + rw && i < w; i++)
                    if (i >= 0 && j >= 0) px[j * w + i] = c;
        }
        if (code)
        {
            var colours = new[] { Orange, Cream, Hex("7FA6FF"), Hex("3DDC6B"), Slate };
            for (int row = 0; row < h / 4; row++)
            {
                int indent = rng.Next(0, 3) * 4 + 2, len = rng.Next(6, 30);
                Rect(indent, row * 4 + 1, len, 2, colours[rng.Next(colours.Length)]);
                if (rng.Next(3) == 0) Rect(indent + len + 2, row * 4 + 1, rng.Next(3, 9), 2, colours[rng.Next(colours.Length)]);
            }
        }
        else
        {
            Rect(0, 0, w, 7, Cream);                       // ground
            Rect(0, 7, w, 2, Hex("3DDC6B"));
            Rect(8, 20, 14, 3, Cream); Rect(30, 31, 15, 3, Cream); // platforms
            foreach (var (x, y) in new[] { (13, 26), (36, 37), (20, 12), (42, 12) }) Rect(x, y, 3, 3, Hex("FFC940")); // coins
            Rect(38, 44, 6, 6, Cream);                     // a moon
        }
        tex.SetPixels(px);
        tex.Apply();
        string path = Gen + file;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = code ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // The boy uses polyperfect's palette atlas: 32 colour bands of equal height, swatches in the left 20/32.
    // His clothes use band 11 (green) and an accent band 28 (greenery). The atlas is shrunk to 512 px (the swatches
    // are flat) and those two bands are filled with flat navy and orange; skin, hair and the rest stay as they were.
    static void Recolour(Transform avatar)
    {
        var skin = avatar.GetComponentInChildren<SkinnedMeshRenderer>();
        var baseMaterial = skin.sharedMaterial;
        var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        full.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(baseMaterial.mainTexture)));
        const int size = 512;
        int stride = full.width / size;
        var all = full.GetPixels32();
        var small = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                small[y * size + x] = all[(y * stride + stride / 2) * full.width + x * stride + stride / 2]; // the middle of each block
        Object.DestroyImmediate(full);

        int band = size / 32, width = size * 20 / 32;
        void Fill(int bandFromTop, Color32 colour)
        {
            int y0 = size - (bandFromTop + 1) * band; // pixel rows count from the bottom
            for (int y = y0; y < y0 + band; y++)
                for (int x = 0; x < width; x++) small[y * size + x] = colour;
        }
        Fill(11, Navy);
        Fill(28, Orange);

        string texturePath = Gen + "Biz_Avatar_Palette.png";
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(small);
        File.WriteAllBytes(texturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(texturePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.maxTextureSize = 512;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        string materialPath = Gen + "Biz_Avatar.mat";
        AssetDatabase.DeleteAsset(materialPath);
        var material = new Material(baseMaterial) { mainTexture = Load<Texture2D>(texturePath) };
        AssetDatabase.CreateAsset(material, materialPath);
        var materials = skin.sharedMaterials;
        for (int i = 0; i < materials.Length; i++) if (materials[i] == baseMaterial) materials[i] = material;
        skin.sharedMaterials = materials;
    }

    // ---------------------------------------------------------------- small helpers

    // The icons are drawn by Tools/make_icons.py; make sure they import as sprites.
    static Sprite IconSprite(string name)
    {
        string path = IconDir + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!importer) throw new System.Exception("Missing icon: " + path + " (run Tools/make_icons.py)");
        if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return Load<Sprite>(path);
    }

    static void ClearListeners(UnityEventBase e)
    {
        for (int i = e.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(e, i);
    }
}
