using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Vuforia;
using Image = UnityEngine.UI.Image; // Vuforia also has an Image type

// Tools > Christmas Card:
//   1. Fix Pink Materials   - converts the ithappy + PolyPeople Santa materials to the Built-In renderer.
//   2. Build Greeting Card  - (re)builds the whole Christmas experience on the Greeting Target. Safe to re-run;
//                             it replaces everything it made before, so tweak values here rather than in the scene.
public static class ChristmasCardBuilder
{
    const string Poly = "Assets/polyperfect/Poly Christmas/Prefabs/Christmas/";
    const string Ithappy = "Assets/ithappy/Christmas_Pack_FREE/";
    const string Gen = "Assets/Generated/";

    // Card is 1 unit wide and 0.71 deep (image target size). Everything below is in card units.
    const float CardHalfW = 0.5f, CardHalfD = 0.354f;
    const float TreeHeight = 1.5f, GiftHeight = 0.18f, RewardSize = 0.15f, SantaHeight = 0.7f, SleighLength = 1.6f;

    static readonly Color Red = Hex("C62828"), Green = Hex("1F6B3A"), Gold = Hex("FFC940"), Cream = Hex("FFF8E7");

    static Material sparkleMat, glowMat, softMat, confettiMat, goldMat;
    static Sprite roundSprite, glowSprite, snowSprite, bulbSprite, cornerSprite, hollySprite, snowCapSprite;
    static Sprite noteSprite, noteOffSprite, restartSprite, giftSprite, starSprite;
    static Font font, titleFont;

    // ---------------------------------------------------------------- 1. pink fix

    [MenuItem("Tools/Christmas Card/1. Fix Pink Materials")]
    public static void FixPinkMaterials()
    {
        // ithappy ships URP materials; its own Built-In package swaps them (only its materials + textures).
        AssetDatabase.ImportPackage(Ithappy + "Render_Pipeline_Convert/Unity_6_Built-In_source.unitypackage", false);

        // PolyPeople Santa's material points at URP/Lit, which this project doesn't have.
        var mat = Load<Material>("Assets/PolyPeople_SantaClaus/Materials/SantaClaus.mat");
        mat.shader = Shader.Find("Standard");
        mat.mainTexture = Load<Texture2D>("Assets/PolyPeople_SantaClaus/Textures/SantaClaus_BaseColor_256.png");
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("PINK_FIX_DONE");
    }

    // ---------------------------------------------------------------- 2. build

    [MenuItem("Tools/Christmas Card/2. Build Greeting Card")]
    public static void Build()
    {
        var target = GameObject.Find("Greeting Target");
        if (!target)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/Scenes/ARCards.unity");
            target = GameObject.Find("Greeting Target");
        }
        Cleanup(target.transform);
        MakeAssets();

        var content = NewChild("Greeting Content", target.transform);
        var templates = new GameObject("Greeting Templates"); // inactive and outside the target, so Vuforia never shows them
        templates.SetActive(false);

        var tree = BuildTree(content, out var lights, out float treeRadius);
        var fx = BuildFx(content, treeRadius);
        var sleigh = BuildSleigh(content, fx, out var bells);
        var spots = BuildGiftSpots(content, treeRadius);
        var gifts = BuildGiftTemplates(templates.transform, out var goldenGift);
        var rewards = BuildRewards(templates.transform);
        var santa = BuildSanta(content);
        var santaSpots = BuildSantaSpots(content);
        var bell = BuildBell(content);
        var ui = BuildUI();

        // controller lives outside the target so it keeps running when the card is lost
        var c = new GameObject("Greeting Controller").AddComponent<GreetingCardController>();
        c.arCamera = Object.FindAnyObjectByType<VuforiaBehaviour>().GetComponent<Camera>();
        c.content = content;
        c.tree = tree;
        c.treeLights = lights;
        c.sleigh = sleigh;
        c.santa = santa;
        c.giftSpots = spots;
        c.giftTemplates = gifts;
        c.goldenGiftTemplate = goldenGift;
        c.bell = bell;
        c.santaStand = santaSpots[0];
        c.goldenSpot = santaSpots[1];
        c.peekHide = santaSpots[2];
        c.peekShow = santaSpots[3];
        c.peekExit = santaSpots[4];
        c.rewards = rewards;
        c.fx = fx;
        c.circleRadius = treeRadius + SleighLength * 0.3f + 0.1f; // keeps the bigger sleigh clear of the branches
        c.circleHeight = TreeHeight * 0.65f;

        c.music = NewAudio(c.gameObject, FindClip("music", "song", "carol"), loop: true);
        c.sfx = NewAudio(c.gameObject, null, loop: false);
        c.bells = bells;
        c.popClip = FindClip("pop");
        c.landClip = FindClip("land", "thud", "drop");
        c.sparkleClip = FindClip("sparkle", "magic", "twinkle", "shimmer");
        c.rareClip = FindClip("rare", "chime", "fanfare", "win");
        c.fireworksClip = FindClip("firework");
        c.whooshClip = FindClip("whoosh", "swoosh");
        c.treeTapClip = FindClip("boing", "jingle", "tap");
        c.finaleClip = FindClip("finale");
        c.bellClip = FindClip("ring");

        c.uiRoot = ui.root;
        c.startScreen = ui.start;
        c.hud = ui.hud;
        c.lostScreen = ui.lost;
        c.finishScreen = ui.finish;
        c.guideText = ui.guideText;
        c.getGiftButton = ui.giftButton;
        c.soundLabel = ui.soundLabel;
        c.soundIcon = ui.soundIcon;
        c.soundOnSprite = noteSprite;
        c.soundOffSprite = noteOffSprite;
        c.labelTemplate = ui.labelTemplate;
        c.spiritMeter = ui.spiritMeter;
        c.spiritStars = ui.spiritStars;
        ui.labelTemplate.cam = c.arCamera;

        // wiring: card found/lost and every button
        var handler = target.GetComponent<DefaultObserverEventHandler>();
        // Keep the content drawn through brief EXTENDED_TRACKED flickers; the controller decides
        // "lost" itself from the card's status (with a grace delay). Smoothing calms the jitter.
        handler.StatusFilter = DefaultObserverEventHandler.TrackingStatusFilter.Tracked_ExtendedTracked;
        handler.UsePoseSmoothing = true;
        Clear(handler.OnTargetFound);
        Clear(handler.OnTargetLost);
        UnityEventTools.AddVoidPersistentListener(ui.startButton.onClick, c.StartExperience);
        UnityEventTools.AddVoidPersistentListener(ui.giftButton.onClick, c.GetYourGift);
        UnityEventTools.AddVoidPersistentListener(ui.soundButton.onClick, c.ToggleSound);
        UnityEventTools.AddVoidPersistentListener(ui.restartButton.onClick, c.Restart);
        UnityEventTools.AddVoidPersistentListener(ui.againButton.onClick, c.Restart);

        // no realtime shadows: keeps mid-range phones well above 30 fps
        foreach (var light in Object.FindObjectsByType<Light>()) light.shadows = LightShadows.None;

        // the hub owns tracking: neutral home screen until the card is seen, then the Christmas experience
        var hub = new GameObject("Card Hub").AddComponent<CardHub>();
        hub.homeScreen = BuildHomeUI();
        hub.greeting = c;
        ui.root.SetActive(false);

        EditorSceneManager.MarkSceneDirty(target.scene);
        EditorSceneManager.SaveScene(target.scene);
        Selection.activeGameObject = content.gameObject;
        Debug.Log($"GREETING_CARD_BUILT tree footprint radius {treeRadius:F2}, flight radius {c.circleRadius:F2}, " +
                  $"clips: music={(bool)c.music.clip} bells={(bool)bells.clip} pop={(bool)c.popClip} land={(bool)c.landClip} " +
                  $"sparkle={(bool)c.sparkleClip} rare={(bool)c.rareClip} fireworks={(bool)c.fireworksClip} whoosh={(bool)c.whooshClip} " +
                  $"treeTap={(bool)c.treeTapClip} finale={(bool)c.finaleClip}, font={font.name}, title={titleFont.name}");
    }

    static void Cleanup(Transform target)
    {
        foreach (var n in new[] { "Greeting Content", "Test Cube" })
        {
            var old = target.Find(n);
            if (old) Object.DestroyImmediate(old.gameObject);
        }
        foreach (var n in new[] { "Greeting UI", "Greeting Templates", "Greeting Controller", "Home UI", "Card Hub" })
        {
            var old = GameObject.Find(n) ?? FindInactiveRoot(n);
            if (old) Object.DestroyImmediate(old);
        }
    }

    // ---------------------------------------------------------------- tree

    static Transform BuildTree(Transform content, out TreeLights lights, out float radius)
    {
        // ithappy indoor tree + its toys (ornaments and topper star). Both share one origin, so they
        // are wrapped and scaled together; the height includes the topper.
        string treePath = Ithappy + "Prefabs/Christmass_tree_indoor_001.prefab";
        string toysPath = Ithappy + "Prefabs/Christmass_tree_indoor_001_toys.prefab";
        MakeReadable(treePath); // the twinkle particles spawn on the tree's mesh
        var tree = Wrap(new[] { treePath, toysPath }, "Tree", Measure.Height, TreeHeight);
        var b = WorldBounds(tree.gameObject);
        radius = Mathf.Max(b.size.x, b.size.z) / 2;
        tree.SetParent(content, false);
        var treeRenderer = tree.GetChild(0).GetComponentInChildren<MeshRenderer>();

        // tap target for "poke the tree"
        var col = tree.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0, TreeHeight / 2, 0);
        col.height = TreeHeight;
        col.radius = radius * 0.75f;

        // the topper star is part of the toys mesh, so the glow sits on it and pulses
        var star = NewChild("Star Glow", tree);
        star.localPosition = new Vector3(0, TreeHeight - 0.06f, 0);

        var twinkles = NewPS("Twinkle Lights", tree, glowMat, local: true);
        var main = twinkles.main;
        main.startLifetime = Range(0.8f, 1.6f);
        main.startSpeed = 0;
        main.startSize = Range(0.03f, 0.05f);
        main.startColor = Colors(Gold, Hex("FF4D4D"), Color.white, Hex("7CFF8A"));
        main.maxParticles = 70;
        var em = twinkles.emission;
        em.rateOverTime = 35;
        var sh = twinkles.shape;
        sh.shapeType = ParticleSystemShapeType.MeshRenderer;
        sh.meshRenderer = treeRenderer;
        SizeCurve(twinkles, 0, 0, 0.3f, 1, 1, 0);

        var halo = NewPS("Star Halo", star, glowMat, local: true);
        main = halo.main;
        main.startLifetime = 1.6f;
        main.startSpeed = 0;
        main.startSize = 0.4f;
        main.startColor = new Color(Gold.r, Gold.g, Gold.b, 0.9f);
        main.maxParticles = 3;
        em = halo.emission;
        em.rateOverTime = 1.25f;
        sh = halo.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.001f;
        SizeCurve(halo, 0, 0.6f, 0.5f, 1, 1, 0.6f);
        Fade(halo, 0.4f, 0.6f);

        lights = tree.gameObject.AddComponent<TreeLights>();
        lights.star = star;
        lights.twinkles = twinkles;
        lights.starHalo = halo;
        lights.enabled = false;
        tree.gameObject.SetActive(false);
        return tree;
    }

    // ---------------------------------------------------------------- effects

    static FxLibrary BuildFx(Transform content, float treeRadius)
    {
        var root = NewChild("FX", content);
        var fx = root.gameObject.AddComponent<FxLibrary>();

        // sparkle ring on the card around the tree base, drifting up while the tree rises
        var ring = NewPS("Sparkle Ring", root, sparkleMat, local: true);
        var main = ring.main;
        main.startLifetime = Range(0.6f, 1.2f);
        main.startSpeed = Range(0.02f, 0.08f);
        main.startSize = Range(0.025f, 0.06f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Gold, Color.white, Hex("FFE08A"));
        main.maxParticles = 200;
        var em = ring.emission;
        em.rateOverTime = 70;
        var sh = ring.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = treeRadius + 0.04f;
        sh.radiusThickness = 0;
        sh.rotation = new Vector3(90, 0, 0);
        var vel = ring.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = 0; vel.y = 0.15f; vel.z = 0;
        SizeCurve(ring, 0, 0, 0.3f, 1, 1, 0);
        fx.sparkleRing = ring;

        // gold glitter fountain rising with the tree
        var glitter = NewPS("Gold Glitter", root, sparkleMat, local: true);
        main = glitter.main;
        main.startLifetime = Range(0.8f, 1.4f);
        main.startSpeed = Range(0.4f, 0.9f);
        main.startSize = Range(0.012f, 0.028f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Gold, Hex("FFE9A8"), Hex("FFB300"));
        main.gravityModifier = 0.05f;
        main.maxParticles = 300;
        em = glitter.emission;
        em.rateOverTime = 110;
        sh = glitter.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 18;
        sh.radius = treeRadius * 0.6f;
        sh.rotation = new Vector3(-90, 0, 0);
        Fade(glitter, 0.1f, 0.6f);
        fx.glitter = glitter;

        // light snowfall over the card
        var snow = NewPS("Snowfall", root, softMat, local: true);
        snow.transform.localPosition = new Vector3(0, TreeHeight + 0.45f, 0);
        main = snow.main;
        main.startLifetime = 6f;
        main.startSpeed = 0.2f;
        main.startSize = Range(0.012f, 0.026f);
        main.startColor = new Color(1, 1, 1, 0.9f);
        main.maxParticles = 260;
        em = snow.emission;
        em.rateOverTime = 35;
        sh = snow.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.rotation = new Vector3(90, 0, 0); // box emits along its Z; this points it down
        sh.scale = new Vector3(1.3f, 0.9f, 0.01f);
        var noise = snow.noise;
        noise.enabled = true;
        noise.strength = 0.05f;
        noise.frequency = 0.6f;
        Fade(snow, 0.1f, 0.85f);
        fx.snowfall = snow;

        // world-space bursts, emitted at a position by FxLibrary
        var puff = NewPS("Snow Puff", root, softMat, local: false);
        main = puff.main;
        main.startLifetime = Range(0.5f, 0.9f);
        main.startSpeed = Range(0.08f, 0.25f);
        main.startSize = Range(0.04f, 0.09f);
        main.startColor = Color.white;
        main.maxParticles = 300;
        sh = puff.shape;
        sh.shapeType = ParticleSystemShapeType.Hemisphere;
        sh.radius = 0.03f;
        sh.rotation = new Vector3(-90, 0, 0);
        SizeCurve(puff, 0, 0.5f, 1, 1.3f);
        Fade(puff, 0.05f, 0.4f);
        fx.snowPuff = puff;

        var confetti = NewPS("Confetti", root, confettiMat, local: false);
        main = confetti.main;
        main.startLifetime = Range(1.2f, 1.8f);
        main.startSpeed = Range(0.5f, 0.9f);
        main.startSize = Range(0.012f, 0.022f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Red, Hex("2E9E4F"), Gold, Color.white);
        main.gravityModifier = 0.25f;
        main.maxParticles = 400;
        sh = confetti.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 35;
        sh.radius = 0.02f;
        sh.rotation = new Vector3(-90, 0, 0);
        var spin = confetti.rotationOverLifetime;
        spin.enabled = true;
        spin.z = Range(-12f, 12f);
        Fade(confetti, 0.01f, 0.7f);
        fx.confetti = confetti;

        var sparkle = NewPS("Sparkle", root, sparkleMat, local: false);
        main = sparkle.main;
        main.startLifetime = Range(0.5f, 0.9f);
        main.startSpeed = Range(0.15f, 0.45f);
        main.startSize = Range(0.02f, 0.045f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Gold, Color.white);
        main.maxParticles = 300;
        sh = sparkle.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.03f;
        SizeCurve(sparkle, 0, 1, 1, 0);
        fx.sparkle = sparkle;

        var rare = NewPS("Rare Burst", root, sparkleMat, local: false);
        main = rare.main;
        main.startLifetime = Range(0.9f, 1.4f);
        main.startSpeed = Range(0.4f, 0.9f);
        main.startSize = Range(0.035f, 0.07f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Gold, Color.white, Hex("FF5A5A"));
        main.gravityModifier = -0.02f;
        main.maxParticles = 300;
        sh = rare.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.05f;
        var slow = rare.limitVelocityOverLifetime;
        slow.enabled = true;
        slow.limit = 0.2f;
        slow.dampen = 0.08f;
        SizeCurve(rare, 0, 1, 1, 0);
        fx.rareBurst = rare;

        var firework = NewPS("Firework", root, sparkleMat, local: false);
        main = firework.main;
        main.startLifetime = Range(1f, 1.5f);
        main.startSpeed = Range(0.45f, 0.6f);
        main.startSize = Range(0.02f, 0.035f);
        main.gravityModifier = 0.06f;
        main.maxParticles = 800;
        sh = firework.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.01f;
        sh.radiusThickness = 0;
        slow = firework.limitVelocityOverLifetime;
        slow.enabled = true;
        slow.limit = 0.15f;
        slow.dampen = 0.08f;
        Fade(firework, 0.01f, 0.6f);
        fx.firework = firework;

        // golden stars swirling up around the tree once it's lit
        var orbit = NewPS("Orbit Stars", root, sparkleMat, local: true);
        main = orbit.main;
        main.startLifetime = Range(3f, 4f);
        main.startSpeed = 0;
        main.startSize = Range(0.02f, 0.04f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Gold, Color.white, Hex("FFE08A"));
        main.maxParticles = 80;
        em = orbit.emission;
        em.rateOverTime = 18;
        sh = orbit.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = treeRadius + 0.15f;
        sh.radiusThickness = 0;
        sh.rotation = new Vector3(90, 0, 0);
        vel = orbit.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = Range(0, 0);
        vel.y = Range(0.05f, 0.3f);
        vel.z = Range(0, 0);
        vel.orbitalY = 1.2f; // radians per second around the tree
        SizeCurve(orbit, 0, 0, 0.2f, 1, 0.8f, 1, 1, 0);
        fx.orbitStars = orbit;

        // finale: confetti raining down over the whole card
        var rain = NewPS("Confetti Rain", root, confettiMat, local: true);
        rain.transform.localPosition = new Vector3(0, TreeHeight + 0.4f, 0);
        main = rain.main;
        main.startLifetime = 3.5f;
        main.startSpeed = Range(0.1f, 0.25f);
        main.startSize = Range(0.015f, 0.025f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Red, Hex("2E9E4F"), Gold, Color.white);
        main.gravityModifier = 0.05f;
        main.maxParticles = 300;
        em = rain.emission;
        em.rateOverTime = 70;
        sh = rain.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.rotation = new Vector3(90, 0, 0);
        sh.scale = new Vector3(1.3f, 0.9f, 0.01f);
        spin = rain.rotationOverLifetime;
        spin.enabled = true;
        spin.z = Range(-8f, 8f);
        noise = rain.noise;
        noise.enabled = true;
        noise.strength = 0.1f;
        noise.frequency = 0.8f;
        Fade(rain, 0.05f, 0.85f);
        fx.confettiRain = rain;

        // golden spiral that gathers where Santa appears (the controller moves it to the spot)
        var swirl = NewPS("Magic Swirl", root, sparkleMat, local: true);
        main = swirl.main;
        main.startLifetime = Range(1f, 1.4f);
        main.startSpeed = 0;
        main.startSize = Range(0.02f, 0.045f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Gold, Color.white, Hex("FFE08A"));
        main.maxParticles = 200;
        em = swirl.emission;
        em.rateOverTime = 90;
        sh = swirl.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.18f;
        sh.radiusThickness = 0;
        sh.rotation = new Vector3(90, 0, 0);
        vel = swirl.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = Range(0, 0);
        vel.y = Range(0.35f, 0.6f);
        vel.z = Range(0, 0);
        vel.orbitalY = 4f; // tight, fast spiral
        SizeCurve(swirl, 0, 0, 0.2f, 1, 0.8f, 1, 1, 0);
        fx.magicSwirl = swirl;

        return fx;
    }

    // ---------------------------------------------------------------- sleigh

    static Transform BuildSleigh(Transform content, FxLibrary fx, out AudioSource bells)
    {
        // Story scale: at the tree's scale the sleigh would be ~2 cards long, so it is shrunk to fly around the tree.
        var sleigh = Wrap(Poly + "Sleigh_Christmas.prefab", "Sleigh", Measure.Length, SleighLength, atCenter: true, orient: ReindeerForward);
        sleigh.SetParent(content, false);

        var trail = NewPS("Star Trail", sleigh, sparkleMat, local: false);
        trail.transform.localPosition = new Vector3(0, 0, -SleighLength * 0.35f);
        var main = trail.main;
        main.startLifetime = 1.5f; // trail fades over ~1.5 s
        main.startSpeed = 0.02f;
        main.startSize = Range(0.02f, 0.04f);
        main.startRotation = Range(0, Mathf.PI * 2);
        main.startColor = Colors(Gold, Color.white, Hex("FFE08A"));
        main.maxParticles = 300;
        var em = trail.emission;
        em.rateOverTime = 0;
        em.rateOverDistance = 90;
        var sh = trail.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.02f;
        SizeCurve(trail, 0, 1, 1, 0.2f);
        Fade(trail, 0.01f, 0.3f);
        fx.trail = trail;

        bells = NewAudio(sleigh.gameObject, FindClip("sleigh"), loop: true);
        bells.spatialBlend = 1;              // 3D: louder as Santa gets closer to the camera
        bells.rolloffMode = AudioRolloffMode.Linear;
        bells.minDistance = 0.5f;
        bells.maxDistance = 8f;
        bells.dopplerLevel = 0;

        sleigh.gameObject.SetActive(false);
        return sleigh;
    }

    // Turn the sleigh model so its reindeer point along +Z (the flight direction).
    static Quaternion ReindeerForward(GameObject model)
    {
        var deer = FindDeep(model.transform, "sleigh-reindeers-wood");
        var santa = FindDeep(model.transform, "sleigh-santa");
        if (!deer || !santa) return Quaternion.identity;
        Vector3 d = WorldBounds(deer.gameObject).center - WorldBounds(santa.gameObject).center;
        return Quaternion.Euler(0, -Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0);
    }

    // ---------------------------------------------------------------- gifts & rewards

    static Transform[] BuildGiftSpots(Transform content, float treeRadius)
    {
        // The big tree fills the middle of the card, so the gifts take the corners, which are furthest from
        // the trunk: front-left, back-left, back-right. (Front-right is the finale Santa's.) Drag to fine-tune.
        float gx = CardHalfW - GiftHeight * 0.6f, gz = CardHalfD - GiftHeight * 0.6f;
        var positions = new[] { new Vector3(-gx, 0, -gz), new Vector3(-gx, 0, gz), new Vector3(gx, 0, gz) };
        float clearance = new Vector2(gx, gz).magnitude - GiftHeight * 0.5f;
        if (treeRadius > clearance)
            Debug.LogWarning($"Tree footprint (radius {treeRadius:F2}) reaches the corner gifts ({clearance:F2}); they will tuck under the lowest branches.");

        var root = NewChild("Gift Spots", content);
        return positions.Select((p, i) =>
        {
            var spot = NewChild("Spot " + (i + 1), root);
            spot.localPosition = p;
            return spot;
        }).ToArray();
    }

    static GameObject[] BuildGiftTemplates(Transform parent, out GameObject golden)
    {
        var kinds = new[]
        {
            ("Cube", "Gift_Christmas_Cube_Open_Base", "Gift_Christmas_Cube_Open_Lid"),
            ("Cylinder", "Gift_Christmas_Cylinder_Open_Base", "Gift_Christmas_Cylinder_Open_Lid"),
            ("Long", "Gift_Christmas_Long_Open", "Gift_Christmas_Long_Open_Lid"),
        };
        float scale = 0; // shared, so all gift kinds keep their real size relative to each other
        var gifts = kinds.Select(k => BuildGift(k.Item1, k.Item2, k.Item3, parent, ref scale)).ToArray();

        golden = BuildGift("Golden", kinds[0].Item2, kinds[0].Item3, parent, ref scale);
        foreach (var r in golden.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)))
            r.sharedMaterials = Enumerable.Repeat(goldMat, r.sharedMaterials.Length).ToArray();
        var glow = golden.GetComponent<GiftBox>().glow;
        var em = glow.emission;
        em.rateOverTime = 22;
        var main = glow.main;
        main.maxParticles = 60;
        main.startSize = Range(0.07f, 0.12f);
        golden.GetComponent<GiftBox>().golden = true;
        return gifts;
    }

    static GameObject BuildGift(string name, string basePrefab, string lidPrefab, Transform parent, ref float scale)
    {
        var root = new GameObject("Gift " + name).transform;
        var box = Spawn(Poly + basePrefab + ".prefab").transform;
        var lid = Spawn(Poly + lidPrefab + ".prefab").transform;
        box.SetParent(root, false);
        lid.SetParent(root, false);

        Bounds bb = WorldBounds(box.gameObject), lb = WorldBounds(lid.gameObject);
        // shortcut: lid fit is a heuristic (rim overlaps 35% of the lid); fix the Lid in the template if it floats or sinks
        if (lb.min.y < bb.center.y)
            lid.position += new Vector3(bb.center.x - lb.center.x, bb.max.y - lb.size.y * 0.35f - lb.min.y, bb.center.z - lb.center.z);

        if (scale == 0) scale = GiftHeight / WorldBounds(root.gameObject).size.y;
        foreach (var t in new[] { box, lid })
        {
            t.localScale *= scale;
            t.localPosition *= scale;
        }
        var all = WorldBounds(root.gameObject);
        var baseCentre = new Vector3(all.center.x, all.min.y, all.center.z); // pivot at the base
        box.localPosition -= baseCentre;
        lid.localPosition -= baseCentre;

        lb = WorldBounds(lid.gameObject);
        var lidPivot = NewChild("Lid", root); // pivot at the lid's centre so it tumbles around itself
        lidPivot.position = lb.center;
        lid.SetParent(lidPivot, true);

        all = WorldBounds(root.gameObject);
        var col = root.gameObject.AddComponent<BoxCollider>();
        col.center = all.center;
        col.size = all.size * 1.6f; // tap target bigger than the box, so it's easy to hit on a phone

        var glow = NewPS("Glow", root, glowMat, local: true);
        glow.transform.localPosition = new Vector3(0, all.size.y * 0.2f, 0);
        var main = glow.main;
        main.startLifetime = Range(1.2f, 1.8f);
        main.startSpeed = Range(0.03f, 0.08f);
        main.startSize = Range(0.05f, 0.09f);
        main.startColor = new Color(Gold.r, Gold.g, Gold.b, 0.7f);
        main.maxParticles = 30;
        var em = glow.emission;
        em.rateOverTime = 9;
        var sh = glow.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.rotation = new Vector3(-90, 0, 0);
        sh.scale = new Vector3(all.size.x, all.size.z, 0.01f);
        SizeCurve(glow, 0, 0, 0.4f, 1, 1, 0);

        var gift = root.gameObject.AddComponent<GiftBox>();
        gift.lid = lidPivot;
        gift.glow = glow;
        gift.height = all.size.y;
        root.SetParent(parent, false);
        return root.gameObject;
    }

    static GreetingCardController.Reward[] BuildRewards(Transform parent)
    {
        // Weighted table: common ~75%, uncommon ~18%, rare ~8% per gift. Edit weights on the controller.
        var table = new[]
        {
            ("Candy Cane", "Candy_Cane", 25f, false),
            ("Gingerbread Man", "Gingerbread_Man", 25f, false),
            ("Jingle Bell", "Jingle_Bell_Christmas", 20f, false),
            ("Hot Chocolate", "Hot_Chocolate", 20f, false),
            ("Teddy Bear", "Toy_Teddy_Bear_A", 12f, false),
            ("Nutcracker", "Nutcracker", 10f, false),
            ("Snow Globe", "Snow_Globe", 5f, true),
            ("Toy Train", "Toy_Train", 5f, true),
        };
        return table.Select(r =>
        {
            var pivot = Wrap(Poly + r.Item2 + ".prefab", "Reward " + r.Item1, Measure.Largest, RewardSize);
            pivot.localRotation = Quaternion.Euler(0, 180, 0); // face the camera
            pivot.SetParent(parent, false);
            return new GreetingCardController.Reward { name = r.Item1, prefab = pivot.gameObject, weight = r.Item3, rare = r.Item4 };
        }).ToArray();
    }

    // ---------------------------------------------------------------- Santa, his spots, the bell

    static Transform BuildSanta(Transform content)
    {
        var santa = Wrap(Ithappy + "Prefabs/Santa.prefab", "Santa", Measure.Height, SantaHeight);
        santa.SetParent(content, false);
        var animator = santa.GetComponentInChildren<Animator>();
        if (!animator)
        {
            animator = santa.GetChild(0).gameObject.AddComponent<Animator>();
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(Ithappy + "Meshes/Santa.fbx").OfType<Avatar>().First();
        }
        animator.runtimeAnimatorController = SantaController();
        animator.applyRootMotion = false; // the controller moves him; the clips animate in place
        santa.gameObject.SetActive(false);
        return santa;
    }

    // One state per move; the controller cross-fades between them by name.
    // Clips aren't set to loop, so Look, Walk and Dance restart themselves at the end.
    static AnimatorController SantaController()
    {
        string path = Gen + "Santa.controller";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.DeleteAsset(Gen + "FinaleSanta.controller"); // old name
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = controller.layers[0].stateMachine;
        const string clips = Ithappy + "Animations/Other_Animations/Santa_";
        AnimatorState State(string name, string clip, bool loop)
        {
            var state = sm.AddState(name);
            state.motion = Load<AnimationClip>(clips + clip + ".anim");
            if (loop)
            {
                var again = state.AddTransition(state);
                again.hasExitTime = true;
                again.exitTime = 0.95f;
                again.duration = 0.15f;
            }
            return state;
        }
        sm.defaultState = State("Look", "IdleLookAround", true);
        State("Walk", "WalkWithItem", true);
        State("Wave", "WaveHello", false);
        State("Talk", "TalkGestureActive", false);
        State("Flip", "Running_Forward_Flip", false);
        State("Dance", "DanceIdle", true);
        return controller;
    }

    // Where Santa walks in from and stops, where the golden present lands, and the peek path.
    // He appears in front of the tree, just past the card's front edge so the branches don't cut through him,
    // and the golden present lands beside him. Drag the spots in the scene to adjust.
    static Transform[] BuildSantaSpots(Transform content)
    {
        var root = NewChild("Santa Spots", content);
        var spots = new[]
        {
            ("Stand", new Vector3(0, 0, -(CardHalfD + 0.16f))),            // centred, in front of the tree
            ("Golden Present", new Vector3(0.24f, 0, -(CardHalfD + 0.1f))), // beside him, toward the camera
            ("Peek Hide", new Vector3(0.15f, 0, 0.32f)),                   // behind the tree
            ("Peek Show", new Vector3(0.52f, 0, -0.02f)),                  // just right of the tree
            ("Peek Exit", new Vector3(1.1f, 0, 0.6f)),
        };
        return spots.Select(spot =>
        {
            var t = NewChild(spot.Item1, root);
            t.localPosition = spot.Item2;
            return t;
        }).ToArray();
    }

    // A golden hand bell hanging in front of the tree; its pivot is at the top so it swings.
    static Transform BuildBell(Transform content)
    {
        const float size = 0.18f;
        var hanger = NewChild("Call Bell", content);
        hanger.localPosition = new Vector3(0.2f, 0.65f, -0.4f);
        var bell = Wrap(Poly + "Bell_A_Christmas.prefab", "Bell", Measure.Height, size);
        bell.SetParent(hanger, false);
        bell.localPosition = new Vector3(0, -size, 0);
        foreach (var r in bell.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = Enumerable.Repeat(goldMat, r.sharedMaterials.Length).ToArray();
        var col = bell.gameObject.AddComponent<SphereCollider>(); // generous tap target
        col.center = new Vector3(0, size / 2, 0);
        col.radius = size * 0.9f;
        hanger.gameObject.SetActive(false);
        return hanger;
    }

    // ---------------------------------------------------------------- UI

    class Ui
    {
        public GameObject root, start, hud, lost, finish;
        public Button startButton, giftButton, soundButton, restartButton, againButton;
        public Text guideText, soundLabel;
        public Image soundIcon;
        public RewardLabel labelTemplate;
        public GameObject spiritMeter;
        public Image[] spiritStars;
    }

    static readonly Color[] BulbColours = { Hex("FF3B3B"), Hex("FFC940"), Hex("3DDC6B"), Hex("4FC3F7"), Color.white };

    static Ui BuildUI()
    {
        var fonts = Directory.Exists("Assets/Fonts")
            ? Directory.GetFiles("Assets/Fonts").Where(f => f.EndsWith(".ttf") || f.EndsWith(".otf")).Select(f => f.Replace('\\', '/')).ToArray()
            : new string[0];
        var bodyPath = fonts.FirstOrDefault(f => f.Contains("Fredoka")) ?? fonts.FirstOrDefault(f => !f.Contains("Christmas"));
        var titlePath = fonts.FirstOrDefault(f => f.Contains("Christmas"));
        font = bodyPath != null ? Load<Font>(bodyPath) : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleFont = titlePath != null ? Load<Font>(titlePath) : font;

        var root = NewCanvas("Greeting UI", 10);
        var ui = new Ui { root = root.gameObject };
        Vector2 top = new Vector2(0.5f, 1), bottom = new Vector2(0.5f, 0), centre = new Vector2(0.5f, 0.5f);

        // reward name tags: a rounded tag that follows each reward (behind every screen)
        var labels = Stretch("Reward Labels", root);
        var tag = Panel("Reward Tag", labels, centre, Vector2.zero, new Vector2(340, 84), new Color(0.07f, 0.3f, 0.16f, 0.92f));
        tag.raycastTarget = false;
        tag.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
        ui.labelTemplate = tag.gameObject.AddComponent<RewardLabel>();
        ui.labelTemplate.text = Label("Reward", tag.transform, 32, Color.white, centre, Vector2.zero, new Vector2(320, 84), Hex("0A2A14"));
        tag.gameObject.AddComponent<UIPopIn>();
        tag.gameObject.SetActive(false);

        // always on: a string of twinkling Christmas lights and the guide banner
        ChristmasLights(root);
        var guide = Panel("Guide Banner", root, top, new Vector2(0, -160), new Vector2(900, 124), Green);
        SlideIn(guide, new Vector2(0, 400), 0.25f);
        SnowCap(guide.transform, 900);
        Holly(guide.transform, new Vector2(-425, 48), 96, -15);
        Holly(guide.transform, new Vector2(425, 48), 96, 15);
        ui.guideText = Label("Card found! Tap Start to begin", guide.transform, 36, Color.white, centre, new Vector2(0, -4), new Vector2(780, 116), Hex("0A2A14"));

        // Scanned: Start
        ui.start = Stretch("Start Screen", root).gameObject;
        ui.startButton = BigButton("Start", starSprite, ui.start.transform, bottom, new Vector2(0, 120), new Vector2(520, 160), Red, 58, out _);
        SlideIn(ui.startButton, new Vector2(0, -500), 0.45f);

        // HUD: Restart (left), Get Your Gift (centre), Music (right)
        ui.hud = Stretch("HUD", root).gameObject;
        ui.restartButton = SmallButton("Restart", restartSprite, ui.hud.transform, new Vector2(0, 0), new Vector2(125, 120), out _, out _);
        ui.soundButton = SmallButton("Music", noteSprite, ui.hud.transform, new Vector2(1, 0), new Vector2(-125, 120), out ui.soundLabel, out ui.soundIcon);
        ui.giftButton = BigButton("Get Your Gift", giftSprite, ui.hud.transform, bottom, new Vector2(0, 120), new Vector2(520, 160), Red, 48, out _);
        SlideIn(ui.giftButton, new Vector2(0, -500), 0.2f);
        SlideIn(ui.restartButton, new Vector2(-400, 0), 0.3f);
        SlideIn(ui.soundButton, new Vector2(400, 0), 0.3f);

        // "Christmas spirit" meter: one star lights per bell ring
        var meter = Panel("Spirit Meter", ui.hud.transform, bottom, new Vector2(0, 120), new Vector2(520, 160), Green);
        SnowCap(meter.transform, 520);
        ui.spiritStars = Enumerable.Range(0, 3).Select(i =>
            Icon(meter.transform, starSprite, new Vector2((i - 1) * 130, -6), 100)).ToArray();
        foreach (var star in ui.spiritStars) star.color = new Color(1, 1, 1, 0.3f);
        SlideIn(meter, new Vector2(0, -500), 0.1f);
        ui.spiritMeter = meter.gameObject;
        ui.spiritMeter.SetActive(false);

        // Finished: Merry Christmas + Play Again
        ui.finish = Stretch("Finish Screen", root).gameObject;
        var title = Label("Merry Christmas!", ui.finish.transform, 120, Gold, top, new Vector2(0, -330), new Vector2(1080, 180), Red, titleFont);
        title.GetComponent<Outline>().effectDistance = new Vector2(6, -6);
        title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -10);
        title.gameObject.AddComponent<UIPopIn>().from = 0.4f;
        Deco(ui.finish.transform, top, new Vector2(-470, -330), 80, 25, 0.95f);
        Deco(ui.finish.transform, top, new Vector2(470, -330), 80, -25, 0.95f);
        ui.againButton = BigButton("Play Again", restartSprite, ui.finish.transform, bottom, new Vector2(0, 120), new Vector2(520, 160), Red, 54, out _);
        SlideIn(ui.againButton, new Vector2(0, -500), 0.8f);

        // entrance burst over everything except the lost screen
        var burst = Stretch("Reveal Burst", root);
        var flash = burst.gameObject.AddComponent<Image>();
        flash.color = new Color(1f, 0.92f, 0.7f, 0);
        flash.raycastTarget = false;
        var reveal = burst.gameObject.AddComponent<RevealBurst>();
        reveal.flash = flash;
        reveal.pieces = Enumerable.Range(0, 18).Select(i =>
        {
            var piece = Rect("Piece", burst, centre, Vector2.zero, new Vector2(90, 90)).gameObject.AddComponent<Image>();
            piece.sprite = i % 2 == 0 ? snowSprite : starSprite;
            piece.color = i % 3 == 0 ? Gold : Color.white;
            piece.raycastTarget = false;
            piece.gameObject.SetActive(false);
            return piece;
        }).ToArray();

        // Lost tracking: blocks taps and explains what to do (drawn on top of everything)
        ui.lost = Stretch("Lost Screen", root).gameObject;
        ui.lost.AddComponent<Image>().color = new Color(0.04f, 0.08f, 0.15f, 0.55f);
        var lostPanel = Panel("Panel", ui.lost.transform, centre, Vector2.zero, new Vector2(880, 380), Red);
        lostPanel.gameObject.AddComponent<UIPopIn>();
        SnowCap(lostPanel.transform, 880);
        Holly(lostPanel.transform, new Vector2(-405, 160), 130, -15);
        Holly(lostPanel.transform, new Vector2(405, 160), 130, 15);
        Label("Find the card again", lostPanel.transform, 60, Color.white, centre, new Vector2(0, 40), new Vector2(820, 100), Hex("5A0A0A"));
        Label("Point your camera back at your card", lostPanel.transform, 36, Cream, centre, new Vector2(0, -60), new Vector2(820, 80), Hex("5A0A0A"));
        Deco(lostPanel.transform, centre, new Vector2(0, 280), 150, 40, 0.95f);

        foreach (var screen in new[] { ui.start, ui.hud, ui.finish, ui.lost }) screen.SetActive(false);

        if (!Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>())
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
        return ui;
    }

    // Neutral app home: not Christmas, not business. Shown until a card is seen, and again after one is lost.
    static GameObject BuildHomeUI()
    {
        var root = NewCanvas("Home UI", 5);
        Vector2 top = new Vector2(0.5f, 1), bottom = new Vector2(0.5f, 0), centre = new Vector2(0.5f, 0.5f);
        Color glass = new Color(0.1f, 0.12f, 0.16f, 0.8f), soft = new Color(1, 1, 1, 0.3f), ink = Hex("0B0E14");

        var header = Panel("Header", root, top, new Vector2(0, -150), new Vector2(760, 170), glass, soft);
        Label("AR Cards", header.transform, 66, Color.white, centre, new Vector2(0, 22), new Vector2(740, 90), ink);
        Label("Greeting & business cards that come alive", header.transform, 30, Hex("C9D3DF"), centre, new Vector2(0, -42), new Vector2(740, 50), ink);

        // a frame showing where to aim
        var frame = Rect("Scan Frame", root, centre, new Vector2(0, 40), new Vector2(760, 540));
        var framePulse = frame.gameObject.AddComponent<UIPulse>();
        framePulse.scaleAmount = 0.03f;
        framePulse.speed = 2.5f;
        for (int i = 0; i < 4; i++)
        {
            var corner = Rect("Corner", frame, new Vector2(i % 2, i / 2), Vector2.zero, new Vector2(120, 120)).gameObject.AddComponent<Image>();
            corner.sprite = cornerSprite;
            corner.color = new Color(1, 1, 1, 0.95f);
            corner.raycastTarget = false;
            // sprite is the top-left corner; rotate it into each corner of the frame
            corner.rectTransform.localEulerAngles = new Vector3(0, 0, new[] { 90f, 180f, 0f, 270f }[i]);
            corner.rectTransform.anchoredPosition = new Vector2(i % 2 == 0 ? 50 : -50, i / 2 == 0 ? 50 : -50);
        }

        var hint = Panel("Hint", root, bottom, new Vector2(0, 150), new Vector2(900, 170), glass, soft);
        Label("Point your camera at your\ngreeting card or business card", hint.transform, 38, Color.white, centre, Vector2.zero, new Vector2(860, 160), ink);
        return root.gameObject;
    }

    static RectTransform NewCanvas(string name, int order)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<CanvasOrientation>(); // portrait on the phone, landscape in the editor
        return go.GetComponent<RectTransform>();
    }

    // A wire draped in swags across the top of the screen (wider than any screen), with coloured bulbs that twinkle out of sync.
    static void ChristmasLights(Transform root)
    {
        var strip = Rect("Christmas Lights", root, new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        SlideIn(strip, new Vector2(0, 220), 0.1f);
        System.Func<float, Vector2> wire = u => new Vector2(Mathf.Lerp(-960, 960, u), -18 - 28 * Mathf.Abs(Mathf.Sin(Mathf.PI * 5 * u)));
        const int segments = 100, bulbs = 21;
        for (int i = 0; i < segments; i++)
        {
            Vector2 a = wire(i / (float)segments), b = wire((i + 1) / (float)segments);
            var seg = Rect("Wire", strip, new Vector2(0.5f, 0.5f), (a + b) / 2, new Vector2((b - a).magnitude + 1, 5)).gameObject.AddComponent<Image>();
            seg.color = Hex("1B3A24");
            seg.raycastTarget = false;
            seg.rectTransform.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }
        var rng = new System.Random(3);
        for (int k = 0; k < bulbs; k++)
        {
            Vector2 p = wire((k + 0.5f) / bulbs) + new Vector2(0, -20);
            var colour = BulbColours[k % BulbColours.Length];
            var halo = Rect("Bulb Glow", strip, new Vector2(0.5f, 0.5f), p, new Vector2(80, 80)).gameObject.AddComponent<Image>();
            halo.sprite = glowSprite;
            halo.color = new Color(colour.r, colour.g, colour.b, 0.75f);
            halo.raycastTarget = false;
            var twinkle = halo.gameObject.AddComponent<UIPulse>();
            twinkle.alphaAmount = 0.8f;
            twinkle.scaleAmount = 0.1f;
            twinkle.speed = 2.5f + (float)rng.NextDouble() * 2f;
            twinkle.phase = (float)rng.NextDouble() * 6.28f;
            var bulb = Rect("Bulb", strip, new Vector2(0.5f, 0.5f), p, new Vector2(30, 40)).gameObject.AddComponent<Image>();
            bulb.sprite = bulbSprite;
            bulb.color = colour;
            bulb.raycastTarget = false;
            var cap = Rect("Cap", strip, new Vector2(0.5f, 0.5f), p + new Vector2(0, 21), new Vector2(14, 11)).gameObject.AddComponent<Image>();
            cap.sprite = roundSprite;
            cap.type = Image.Type.Sliced;
            cap.color = Hex("B8892E");
            cap.raycastTarget = false;
        }
    }

    // Wavy snow resting on the top edge of a panel or button.
    static void SnowCap(Transform parent, float width)
    {
        var cap = Rect("Snow Cap", parent, new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(width + 16, 46)).gameObject.AddComponent<Image>();
        cap.sprite = snowCapSprite;
        cap.type = Image.Type.Tiled;
        cap.raycastTarget = false;
    }

    static void Holly(Transform parent, Vector2 pos, float size, float tilt)
    {
        var holly = Rect("Holly", parent, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size)).gameObject.AddComponent<Image>();
        holly.sprite = hollySprite;
        holly.raycastTarget = false;
        holly.rectTransform.localEulerAngles = new Vector3(0, 0, tilt);
    }

    // Big festive button: soft gold glow behind a red rounded face with snow on top, an icon and a label.
    static Button BigButton(string text, Sprite icon, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color colour, int fontSize, out Text label)
    {
        var holder = Rect(text + " Button", parent, anchor, pos, size);
        holder.gameObject.AddComponent<UIPopIn>();
        holder.gameObject.AddComponent<ButtonBounce>();
        var glow = Rect("Glow", holder, new Vector2(0.5f, 0.5f), Vector2.zero, size * 1.7f).gameObject.AddComponent<Image>();
        glow.sprite = glowSprite;
        glow.color = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
        glow.raycastTarget = false;
        var pulse = glow.gameObject.AddComponent<UIPulse>();
        pulse.scaleAmount = 0.08f;
        pulse.alphaAmount = 0.5f;
        pulse.speed = 3f;
        var face = Panel("Face", holder, new Vector2(0.5f, 0.5f), Vector2.zero, size, colour);
        SnowCap(face.transform, size.x);
        Icon(face.transform, icon, new Vector2(-size.x / 2 + 95, -6), 96);
        label = Label(text, face.transform, fontSize, Color.white, new Vector2(0.5f, 0.5f), new Vector2(45, -6), new Vector2(size.x - 170, size.y), Hex("5A0A0A"));
        return MakeButton(holder.gameObject, face);
    }

    // Small green button: icon on top, word underneath.
    static Button SmallButton(string text, Sprite icon, Transform parent, Vector2 anchor, Vector2 pos, out Text label, out Image iconImage)
    {
        var size = new Vector2(170, 150);
        var face = Panel(text + " Button", parent, anchor, pos, size, Green);
        face.gameObject.AddComponent<ButtonBounce>();
        SnowCap(face.transform, size.x);
        iconImage = Icon(face.transform, icon, new Vector2(0, 14), 64);
        label = Label(text, face.transform, 28, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, -46), new Vector2(size.x, 44), Hex("0A2A14"));
        return MakeButton(face.gameObject, face);
    }

    static void SlideIn(Component element, Vector2 from, float delay)
    {
        var slide = element.gameObject.AddComponent<UISlideIn>();
        slide.from = from;
        slide.delay = delay;
    }

    static Image Icon(Transform parent, Sprite sprite, Vector2 pos, float size)
    {
        var img = Rect("Icon", parent, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size)).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        var shadow = img.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.35f);
        shadow.effectDistance = new Vector2(0, -4);
        return img;
    }

    static Button MakeButton(GameObject go, Image face)
    {
        var b = go.AddComponent<Button>();
        b.targetGraphic = face;
        var colours = b.colors;
        colours.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colours.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
        b.colors = colours;
        return b;
    }

    static Image Panel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color colour, Color? rimColour = null)
    {
        var img = Rect(name, parent, anchor, pos, size).gameObject.AddComponent<Image>();
        img.sprite = roundSprite;
        img.type = Image.Type.Sliced;
        img.color = colour;
        var rim = img.gameObject.AddComponent<Outline>();
        rim.effectColor = rimColour ?? Gold;
        rim.effectDistance = new Vector2(5, -5);
        var shadow = img.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.35f);
        shadow.effectDistance = new Vector2(0, -10);
        return img;
    }

    static Text Label(string text, Transform parent, int size, Color colour, Vector2 anchor, Vector2 pos, Vector2 rectSize, Color outline, Font withFont = null)
    {
        var t = Rect("Text", parent, anchor, pos, rectSize).gameObject.AddComponent<Text>();
        t.text = text;
        t.font = withFont ? withFont : font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.color = colour;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.lineSpacing = 0.9f;
        t.raycastTarget = false;
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = outline;
        o.effectDistance = new Vector2(3, -3);
        return t;
    }

    // Spinning snowflake accent.
    static Image Deco(Transform parent, Vector2 anchor, Vector2 pos, float size, float spin, float alpha)
    {
        var img = Rect("Snowflake", parent, anchor, pos, new Vector2(size, size)).gameObject.AddComponent<Image>();
        img.sprite = snowSprite;
        img.color = new Color(1, 1, 1, alpha);
        img.raycastTarget = false;
        var p = img.gameObject.AddComponent<UIPulse>();
        p.rotateSpeed = spin;
        p.scaleAmount = 0.06f;
        return img;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static RectTransform Stretch(string name, Transform parent)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero);
        rt.anchorMax = Vector2.one;
        return rt;
    }

    // ---------------------------------------------------------------- generated textures & materials

    static void MakeAssets()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
        var star = Load<Texture2D>(MakeTexture("FX_Star", 128, StarAlpha, false));
        var glow = Load<Texture2D>(MakeTexture("FX_Glow", 128, GlowAlpha, false));
        var round = MakeTexture("UI_Rounded", 64, RoundedAlpha, true, 30);
        roundSprite = Load<Sprite>(round);
        glowSprite = Load<Sprite>(MakeTexture("UI_Glow", 128, GlowAlpha, true));
        snowSprite = Load<Sprite>(MakeTexture("UI_Snowflake", 128, SnowflakeAlpha, true));
        bulbSprite = Load<Sprite>(MakeTexture("UI_Bulb", 64, BulbAlpha, true));
        cornerSprite = Load<Sprite>(MakeTexture("UI_Corner", 64, CornerAlpha, true));
        noteSprite = Load<Sprite>(MakeTexture("UI_Icon_Music", 128, NoteAlpha, true));
        noteOffSprite = Load<Sprite>(MakeTexture("UI_Icon_Muted", 128, NoteOffAlpha, true));
        restartSprite = Load<Sprite>(MakeTexture("UI_Icon_Restart", 128, RestartAlpha, true));
        giftSprite = Load<Sprite>(MakeTexture("UI_Icon_Gift", 128, GiftAlpha, true));
        starSprite = Load<Sprite>(MakeTexture("UI_Icon_Star", 128, FivePointStarAlpha, true));
        hollySprite = Load<Sprite>(MakeTexture("UI_Holly", 128, 128, HollyPixel, true, Vector4.zero, TextureWrapMode.Clamp));
        snowCapSprite = Load<Sprite>(MakeTexture("UI_SnowCap", 128, 48,
            (x, y) => new Color(1, 1, 1, SnowCapAlpha(x, y)), true, Vector4.zero, TextureWrapMode.Repeat));

        const string additive = "Legacy Shaders/Particles/Additive", blended = "Legacy Shaders/Particles/Alpha Blended";
        sparkleMat = MakeMaterial("FX_Sparkle", additive, star);
        glowMat = MakeMaterial("FX_Glow", additive, glow);
        softMat = MakeMaterial("FX_Soft", blended, glow);
        confettiMat = MakeMaterial("FX_Confetti", blended, Load<Texture2D>(round));
        goldMat = MakeMaterial("Gold", "Standard", null);
        goldMat.color = new Color(1f, 0.78f, 0.25f);
        goldMat.SetFloat("_Metallic", 0.6f);
        goldMat.SetFloat("_Glossiness", 0.75f);
        goldMat.EnableKeyword("_EMISSION");
        goldMat.SetColor("_EmissionColor", new Color(1f, 0.65f, 0.15f) * 0.5f);
        AssetDatabase.SaveAssets();
    }

    static Material MakeMaterial(string name, string shaderName, Texture tex)
    {
        string path = Gen + name + ".mat";
        var shader = Shader.Find(shaderName) ?? throw new System.Exception("Missing shader: " + shaderName);
        var m = Load<Material>(path, required: false);
        if (!m)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        if (tex) m.mainTexture = tex;
        EditorUtility.SetDirty(m);
        return m;
    }

    // Square white texture whose alpha comes from alpha(x, y) with x, y in -0.5..0.5.
    static string MakeTexture(string name, int size, System.Func<float, float, float> alpha, bool sprite, int border = 0) =>
        MakeTexture(name, size, size, (x, y) => new Color(1, 1, 1, alpha(x, y)), sprite,
                    new Vector4(border, border, border, border), TextureWrapMode.Clamp);

    // Texture whose pixels come from pixel(x, y) with x, y in -0.5..0.5 across each axis.
    static string MakeTexture(string name, int width, int height, System.Func<float, float, Color> pixel, bool sprite,
                              Vector4 border, TextureWrapMode wrap)
    {
        string path = Gen + name + ".png";
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tex.SetPixel(x, y, pixel((x + 0.5f) / width - 0.5f, (y + 0.5f) / height - 0.5f));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.wrapMode = wrap;
        importer.mipmapEnabled = !sprite;
        if (sprite)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; // needed for 9-slicing and tiling
            importer.SetTextureSettings(settings);
        }
        importer.SaveAndReimport();
        return path;
    }

    // ---------------------------------------------------------------- drawn shapes (x, y in -0.5..0.5)

    // anti-aliased fill: 1 inside (d < 0), 0 outside, with a ~1.5 px soft edge at 128 px
    static float Fill(float d, float edge = 0.012f) => Mathf.Clamp01(0.5f - d / edge);

    static float BulbAlpha(float x, float y)
    {
        // teardrop: wide round bottom, narrower neck at the top
        float w = Mathf.Lerp(0.42f, 0.24f, Mathf.Clamp01((y + 0.1f) / 0.55f));
        float d = new Vector2(x / w, (y + 0.08f) / 0.42f).magnitude - 1;
        return Fill(d * 0.4f, 0.02f);
    }

    static float CornerAlpha(float x, float y)
    {
        // top-left bracket: two thick rounded arms
        float d = Mathf.Min(Segment(new Vector2(x, y), new Vector2(-0.38f, 0.38f), new Vector2(0.38f, 0.38f)),
                            Segment(new Vector2(x, y), new Vector2(-0.38f, 0.38f), new Vector2(-0.38f, -0.38f)));
        return Fill(d - 0.08f, 0.02f);
    }

    static float NoteAlpha(float x, float y)
    {
        var p = new Vector2(x, y);
        float heads = Mathf.Min(Ellipse(p, new Vector2(-0.2f, -0.26f), new Vector2(0.14f, 0.1f), -20),
                                Ellipse(p, new Vector2(0.22f, -0.18f), new Vector2(0.14f, 0.1f), -20));
        float stems = Mathf.Min(Segment(p, new Vector2(-0.08f, -0.24f), new Vector2(-0.08f, 0.3f)),
                                Segment(p, new Vector2(0.34f, -0.16f), new Vector2(0.34f, 0.38f))) - 0.035f;
        float beam = Segment(p, new Vector2(-0.08f, 0.3f), new Vector2(0.34f, 0.38f)) - 0.065f;
        return Fill(Mathf.Min(heads, Mathf.Min(stems, beam)));
    }

    static float NoteOffAlpha(float x, float y)
    {
        // the note with a clear gap cut around a diagonal slash, plus the slash itself
        float slash = Segment(new Vector2(x, y), new Vector2(-0.4f, 0.4f), new Vector2(0.4f, -0.4f));
        return Mathf.Max(NoteAlpha(x, y) * (1 - Fill(slash - 0.09f)), Fill(slash - 0.045f));
    }

    static float RestartAlpha(float x, float y)
    {
        var p = new Vector2(x, y);
        float a = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        float ring = Mathf.Abs(p.magnitude - 0.3f) - 0.06f;
        bool inGap = a > 20 && a < 75; // opening at the top right
        float arc = inGap ? 1 : ring;
        // arrowhead at the 75° end, pointing clockwise into the gap
        float r = 75 * Mathf.Deg2Rad;
        var basePt = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * 0.3f;
        var radial = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        var clockwise = new Vector2(Mathf.Sin(r), -Mathf.Cos(r));
        float head = Triangle(p, basePt + radial * 0.15f, basePt - radial * 0.15f, basePt + clockwise * 0.2f);
        return Mathf.Max(Fill(arc), head);
    }

    static float GiftAlpha(float x, float y)
    {
        var p = new Vector2(x, y);
        float body = Box(p, new Vector2(0, -0.14f), new Vector2(0.32f, 0.26f));
        float lid = Box(p, new Vector2(0, 0.17f), new Vector2(0.38f, 0.08f));
        float shape = Mathf.Min(body, lid);
        // ribbon shows as gaps: a vertical cut, and a cut between lid and body
        if (Mathf.Abs(x) < 0.045f || Mathf.Abs(y - 0.08f) < 0.018f) shape = 1;
        float bow = Mathf.Min(Mathf.Abs(Ellipse(p, new Vector2(-0.13f, 0.34f), new Vector2(0.13f, 0.08f), 25)) - 0.035f,
                              Mathf.Abs(Ellipse(p, new Vector2(0.13f, 0.34f), new Vector2(0.13f, 0.08f), -25)) - 0.035f);
        return Fill(Mathf.Min(shape, bow));
    }

    static float FivePointStarAlpha(float x, float y)
    {
        float r = new Vector2(x, y).magnitude, a = Mathf.Atan2(y, x) - Mathf.PI / 2;
        float edge = 0.19f + 0.24f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(5 * a), 2.2f);
        return Fill(r - edge);
    }

    // two spiky leaves and three berries with a highlight (baked colours)
    static Color HollyPixel(float x, float y)
    {
        var p = new Vector2(x, y);
        Color leaf = Hex("1E7A34"), vein = Hex("6CC07A"), berry = Hex("D62828");
        float best = 0;
        Color c = Color.clear;
        foreach (var (centre, angle) in new[] { (new Vector2(-0.18f, -0.08f), 35f), (new Vector2(0.18f, -0.08f), -35f) })
        {
            var q = Rotate(p - centre, -angle);
            float spikes = 1 - 0.22f * Mathf.Abs(Mathf.Sin(q.x / 0.3f * Mathf.PI * 2.5f));
            float d = new Vector2(q.x / 0.3f, q.y / (0.12f * spikes)).magnitude - 1;
            float a = Fill(d * 0.12f, 0.012f);
            if (a > best)
            {
                best = a;
                c = Mathf.Abs(q.y) < 0.01f && Mathf.Abs(q.x) < 0.24f ? vein : leaf;
            }
        }
        foreach (var centre in new[] { new Vector2(-0.07f, 0.1f), new Vector2(0.07f, 0.1f), new Vector2(0, 0.2f) })
        {
            float a = Fill((p - centre).magnitude - 0.075f);
            if (a > 0.01f)
            {
                var shine = (p - centre - new Vector2(-0.025f, 0.025f)).magnitude < 0.022f;
                c = Color.Lerp(c, shine ? Color.white : berry, a);
                best = Mathf.Max(best, a);
            }
        }
        c.a = best;
        return c;
    }

    // solid snow on top, a wavy dripping bottom edge; one drip per tile so it repeats seamlessly
    static float SnowCapAlpha(float x, float y)
    {
        float u = x + 0.5f, v = y + 0.5f;
        float bottom = 0.42f - 0.32f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2 * Mathf.PI * u), 4);
        float top = 0.92f - 0.06f * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 2); // slightly lumpy top
        return Mathf.Clamp01((v - bottom) / 0.04f) * Mathf.Clamp01((top - v) / 0.04f);
    }

    static float Ellipse(Vector2 p, Vector2 centre, Vector2 radii, float degrees)
    {
        var q = Rotate(p - centre, -degrees);
        return (new Vector2(q.x / radii.x, q.y / radii.y).magnitude - 1) * Mathf.Min(radii.x, radii.y);
    }

    static float Box(Vector2 p, Vector2 centre, Vector2 half)
    {
        var d = new Vector2(Mathf.Abs(p.x - centre.x) - half.x, Mathf.Abs(p.y - centre.y) - half.y);
        return new Vector2(Mathf.Max(d.x, 0), Mathf.Max(d.y, 0)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0);
    }

    static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float Side(Vector2 p1, Vector2 p2) => (p.x - p2.x) * (p1.y - p2.y) - (p1.x - p2.x) * (p.y - p2.y);
        float d1 = Side(a, b), d2 = Side(b, c), d3 = Side(c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return neg && pos ? 0 : 1;
    }

    // four-point sparkle with a soft halo
    static float StarAlpha(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y) * 2, a = Mathf.Atan2(y, x);
        float edge = 0.12f + 0.83f * Mathf.Pow(Mathf.Abs(Mathf.Cos(2 * a)), 12);
        return Mathf.Clamp01(Mathf.Clamp01((edge - r) / 0.05f) + 0.5f * Mathf.Pow(Mathf.Clamp01(1 - r), 3));
    }

    static float GlowAlpha(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y) * 2;
        return Mathf.Pow(Mathf.Clamp01(1 - r), 2);
    }

    static float RoundedAlpha(float x, float y)
    {
        const float radius = 0.42f, px = 1 / 64f;
        float qx = Mathf.Max(Mathf.Abs(x) - (0.5f - radius), 0), qy = Mathf.Max(Mathf.Abs(y) - (0.5f - radius), 0);
        float dist = Mathf.Sqrt(qx * qx + qy * qy) - radius;
        return Mathf.Clamp01(0.5f - dist / px);
    }

    // six arms, each with a pair of side branches
    static float SnowflakeAlpha(float x, float y)
    {
        var p = new Vector2(x, y) * 2;
        float d = 9;
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var branch = dir * 0.55f;
            d = Mathf.Min(d, Segment(p, Vector2.zero, dir * 0.9f));
            d = Mathf.Min(d, Segment(p, branch, branch + Rotate(dir, 45) * 0.28f));
            d = Mathf.Min(d, Segment(p, branch, branch + Rotate(dir, -45) * 0.28f));
        }
        return Mathf.Clamp01((0.075f - d) / 0.03f);
    }

    static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // ---------------------------------------------------------------- model helpers

    enum Measure { Height, Length, Largest }

    static Transform Wrap(string path, string name, Measure measure, float size, bool atCenter = false,
                          System.Func<GameObject, Quaternion> orient = null) =>
        Wrap(new[] { path }, name, measure, size, atCenter, orient);

    // Puts prefabs (sharing one origin) inside an empty pivot, scales them together so the measured size
    // equals `size`, and offsets them so the pivot sits at the base centre (or the centre).
    // Pivot starts at the world origin.
    static Transform Wrap(string[] paths, string name, Measure measure, float size, bool atCenter = false,
                          System.Func<GameObject, Quaternion> orient = null)
    {
        var pivot = new GameObject(name).transform;
        var models = paths.Select(path =>
        {
            var model = Spawn(path);
            model.transform.SetParent(pivot, false);
            if (orient != null) model.transform.localRotation = orient(model) * model.transform.localRotation;
            return model.transform;
        }).ToArray();

        var b = WorldBounds(pivot.gameObject);
        float m = measure == Measure.Height ? b.size.y
                : measure == Measure.Length ? Mathf.Max(b.size.x, b.size.z)
                : Mathf.Max(b.size.x, b.size.y, b.size.z);
        foreach (var t in models)
        {
            t.localScale *= size / m;
            t.localPosition *= size / m;
        }
        b = WorldBounds(pivot.gameObject);
        var anchor = atCenter ? b.center : new Vector3(b.center.x, b.min.y, b.center.z);
        foreach (var t in models) t.localPosition -= anchor;
        return pivot;
    }

    static GameObject Spawn(string path)
    {
        var prefab = Load<GameObject>(path);
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void MakeReadable(string prefabPath)
    {
        var mesh = Load<GameObject>(prefabPath).GetComponentInChildren<MeshFilter>().sharedMesh;
        if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) is ModelImporter importer && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform child in t)
        {
            var found = FindDeep(child, name);
            if (found) return found;
        }
        return null;
    }

    static GameObject FindInactiveRoot(string name) =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);

    static Transform NewChild(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    // ---------------------------------------------------------------- particle helpers

    static ParticleSystem NewPS(string name, Transform parent, Material mat, bool local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var em = ps.emission;
        em.rateOverTime = 0;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static ParticleSystem.MinMaxCurve Range(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);

    // Each particle picks one of these colours.
    static ParticleSystem.MinMaxGradient Colors(params Color[] colours)
    {
        var g = new Gradient { mode = GradientMode.Fixed };
        g.SetKeys(colours.Select((c, i) => new GradientColorKey(c, (i + 1f) / colours.Length)).ToArray(),
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
    }

    static void Fade(ParticleSystem ps, float inUntil, float holdUntil)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, inUntil), new GradientAlphaKey(1, holdUntil), new GradientAlphaKey(0, 1) });
        col.color = g;
    }

    // pairs of (time, size) over the particle's life
    static void SizeCurve(ParticleSystem ps, params float[] timeSize)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        var curve = new AnimationCurve();
        for (int i = 0; i < timeSize.Length; i += 2) curve.AddKey(timeSize[i], timeSize[i + 1]);
        s.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    // ---------------------------------------------------------------- misc

    static AudioSource NewAudio(GameObject go, AudioClip clip, bool loop)
    {
        var a = go.AddComponent<AudioSource>();
        a.clip = clip;
        a.loop = loop;
        a.playOnAwake = false;
        return a;
    }

    // First clip in Assets/Audio whose file name contains any of the keywords.
    static AudioClip FindClip(params string[] keywords)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Audio")) return null;
        return AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => keywords.Any(k => Path.GetFileName(p).ToLower().Contains(k)))
            .Select(p => Load<AudioClip>(p))
            .FirstOrDefault();
    }

    static T Load<T>(string path, bool required = true) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset && required) throw new System.Exception("Missing asset: " + path);
        return asset;
    }

    static void Clear(UnityEventBase e)
    {
        for (int i = e.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(e, i);
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }
}
