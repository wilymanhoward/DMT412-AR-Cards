using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Bug Hunt, the business card's mini-game. Tap the mini-me's monitor (it has a blinking "!") and bugs pour out
// of his screen and scurry along the network lines; tap one and a fly swatter swings down on it. Squash as many
// as you can in 20 seconds.
//   bug 1 point, golden bug 5, a small blue zig-zagging "speedy" bug 2, a green "feature" costs 3
//   ("That's not a bug, that's a feature!"). Bugs burst out of his screen in waves that come faster as time
//   runs out; ones that reach the card's edge just leave (no penalty).
//   Levels: each has a target score for its 20 seconds. Hit it and Next level is harder (faster bugs, bigger
//   waves, more decoys, speedy bugs from level 3, and it keeps scaling after level 4); miss it and it is game
//   over. Results: total score, level reached, bests (kept on the phone), a rating, and Hire me (opens Contact).
// Everything is built here in code, like the node scenes (BusinessCardController.Stages.cs), so the business card
// needs no rebuild. Sizes are in card units (the card is 1 wide, 0.6 deep).
public partial class BusinessCardController
{
    const float GameLength = 20f;
    const int MaxBugs = 22;
    const float BugY = 0.004f, BugSize = 1.3f;
    const string BestKey = "BugHuntBest", BestLevelKey = "BugHuntBestLevel";
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f), Bad = new Color(0.32f, 0.78f, 0.36f), Alarm = new Color(0.95f, 0.15f, 0.12f);
    static readonly Color Speedy = new Color(0.3f, 0.62f, 1f), Hot = new Color(1f, 0.45f, 0.1f);
    static readonly Color Ink = new Color(0.06f, 0.06f, 0.09f), Swat = new Color(0.36f, 0.13f, 0.13f), Steel = new Color(0.78f, 0.8f, 0.84f);

    enum Kind { Bug, Golden, Feature, Speedy }
    enum Move { Hop, Pause, Walk, Escape, Splat, Dead } // Splat: being squashed, not free to respawn yet

    class Bug
    {
        public Kind kind;
        public Transform t;
        public Transform[] legs;
        public Quaternion[] legRest;
        public Move move;
        public Vector3 start, dir;
        public int at, prev, next, hopsLeft;
        public float timer, travel, speed, scale, land;
    }

    // One level's settings. Spawn gaps and wave sizes go from the first value to the second over the 20 seconds.
    struct Level
    {
        public int number, target, waveMax, hops;
        public string name;
        public float speed, gapFrom, gapTo, feature, golden, speedy;
    }

    static Level LevelAt(int n)
    {
        switch (n)
        {
            case 1: return new Level { number = 1, name = "Hotfix", target = 10, speed = 1f, gapFrom = 0.8f, gapTo = 0.4f, waveMax = 2, hops = 5, feature = 0.08f, golden = 0.12f, speedy = 0 };
            case 2: return new Level { number = 2, name = "Sprint", target = 18, speed = 1.3f, gapFrom = 0.7f, gapTo = 0.32f, waveMax = 3, hops = 4, feature = 0.16f, golden = 0.1f, speedy = 0 };
            case 3: return new Level { number = 3, name = "Crunch Time", target = 26, speed = 1.5f, gapFrom = 0.6f, gapTo = 0.28f, waveMax = 4, hops = 3, feature = 0.18f, golden = 0.08f, speedy = 0.25f };
            default: // Production: it never ends, it just keeps getting harder
                int k = n - 3;
                return new Level { number = n, name = "Production", target = 26 + 8 * k, speed = 1.5f + 0.12f * k, gapFrom = 0.55f, gapTo = Mathf.Max(0.18f, 0.28f - 0.02f * k), waveMax = 5, hops = 3, feature = Mathf.Min(0.3f, 0.18f + 0.03f * k), golden = 0.07f, speedy = 0.3f };
        }
    }

    readonly List<Bug> bugs = new List<Bug>();
    Vector3[] bugPoints;           // the logo and the four node spots, down on the card
    List<int>[] bugLinks;          // the network lines between them
    Transform monitor, alarm, bang, hud, timerFill, bigSign, results;
    Collider monitorCol;
    SeatedPose pose;
    Vector3 lookRest;
    Bug watched;                   // the bug he is looking at (the last one out of his screen)
    Billboard scoreSign, bigSignText, ratingSign;
    TextMesh resScore, resBest;
    MeshRenderer timerFillR;
    readonly Billboard[] popups = new Billboard[6];
    readonly float[] popAge = new float[6];
    AudioClip splatClip;
    Mesh domeMesh;
    readonly Transform[] swatters = new Transform[3];
    bool gameOn, playing, resultsUp, hinted;
    int score, lastTick, levelNo = 1, total;
    Level level;
    bool targetHit, cleared;
    AudioClip drumClip;
    MeshRenderer bigSignBack;
    Billboard againSign;
    float timeLeft, spawnTimer, hubIdle;

    // ---------- Building ----------

    void BuildBugHunt()
    {
        splatClip = Resources.Load<AudioClip>("Audio/biz_splat");
        drumClip = Resources.Load<AudioClip>("Audio/biz_drum");
        pose = avatar.GetComponentInChildren<SeatedPose>(true);
        foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            if (t.name == "Monitor") monitor = t;
        if (!monitor || !pose) return; // an older scene: no game until the business card is rebuilt
        lookRest = pose.lookAt.localPosition;
        var rig = monitor.parent;

        var box = monitor.gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(1.3f, 1.6f, 6f); // the monitor is thin: a deep box is easier to hit
        monitorCol = box;

        // a red flash over the game half of his screen when the bugs break in
        var screen = rig.Find("Screen Game");
        alarm = Prim(PrimitiveType.Cube, "Alarm", rig, screen.localPosition + new Vector3(0, 0, -0.001f), new Vector3(0.05f, 0.055f, 0.0005f), Lit(Alarm, 1.2f));
        alarm.gameObject.SetActive(false);

        var mark = NewSign(rig, "!", 0.022f, Cream, Orange);
        bang = mark.transform;
        bang.localPosition = monitor.localPosition + Vector3.up * 0.065f;

        // the paths: logo to each node, and node to node, like the lines drawn above them
        bugPoints = new Vector3[hubs.Length + 1];
        bugLinks = new List<int>[bugPoints.Length];
        bugPoints[0] = new Vector3(logoRest.x, BugY, logoRest.z);
        for (int i = 0; i < hubs.Length; i++) bugPoints[i + 1] = new Vector3(hubs[i].rest.x, BugY, hubs[i].rest.z);
        for (int i = 0; i < bugPoints.Length; i++) bugLinks[i] = new List<int>();
        void Link(int a, int b) { bugLinks[a].Add(b); bugLinks[b].Add(a); }
        for (int i = 1; i <= hubs.Length; i++) Link(0, i);
        for (int i = 1; i < hubs.Length; i++) Link(i, i + 1);

        for (int i = 0; i < swatters.Length; i++) swatters[i] = NewSwatter();
        for (int i = 0; i < 16; i++) bugs.Add(NewBug(Kind.Bug));
        for (int i = 0; i < 4; i++) bugs.Add(NewBug(Kind.Golden));
        for (int i = 0; i < 5; i++) bugs.Add(NewBug(Kind.Feature));
        for (int i = 0; i < 6; i++) bugs.Add(NewBug(Kind.Speedy));

        // score and timer, floating over the top of the card
        var hudBoard = new GameObject("Bug Hunt HUD").AddComponent<Billboard>();
        hudBoard.cam = arCamera;
        hud = hudBoard.transform;
        hud.SetParent(content, false);
        hud.localPosition = new Vector3(0.05f, 0.3f, 0.12f);
        scoreSign = NewSign(hud, "Score 0", 0.032f, Cream, Navy);
        scoreSign.face = false;
        PanelObject("Timer Back", hud, new Vector3(0, -0.048f, 0), 0.006f, Lit(Navy)).sharedMesh = Billboard.PanelMesh(0.3f, 0.016f, 0.008f);
        PanelObject("Timer Rim", hud, new Vector3(0, -0.048f, 0.0015f), 0.006f, Lit(Orange, 0.5f)).sharedMesh = Billboard.PanelMesh(0.304f, 0.02f, 0.01f);
        timerFill = Prim(PrimitiveType.Cube, "Timer Fill", hud, new Vector3(0, -0.048f, -0.004f), new Vector3(0.29f, 0.01f, 0.004f), Lit(Cream));
        timerFillR = timerFill.GetComponent<MeshRenderer>();
        hud.gameObject.SetActive(false);

        // the big 3-2-1 / Go! / Time! sign
        bigSignText = NewSign(content, "3", 0.07f, Cream, Orange);
        bigSign = bigSignText.transform;
        bigSign.localPosition = new Vector3(0.05f, 0.17f, 0);
        bigSignBack = bigSignText.back.GetComponent<MeshRenderer>();
        bigSign.gameObject.SetActive(false);

        // floating +1 / +5 / -3
        for (int i = 0; i < popups.Length; i++)
        {
            var p = new GameObject("Score Pop").AddComponent<Billboard>();
            p.cam = arCamera;
            p.transform.SetParent(content, false);
            p.text = Text(p.transform, "", 0.03f, Orange, Vector3.zero);
            p.gameObject.SetActive(false);
            popups[i] = p;
        }

        BuildResults();
    }

    // A cartoon beetle: a glossy domed shell with spots and a seam down the middle, a navy head with big googly
    // eyes, feelers with orange tips, and six jointed legs that walk in a tripod gait. Orange bugs are worth 1,
    // golden ones glow and are worth 5, and a green one is a "feature" (it costs you 3).
    Bug NewBug(Kind kind)
    {
        var root = new GameObject(kind + " Bug").transform;
        root.SetParent(content, false);
        float scale = BugSize * (kind == Kind.Speedy ? 0.75f : 1f);
        root.localScale = Vector3.one * scale;
        Color shellColour = kind == Kind.Golden ? Gold : kind == Kind.Feature ? Bad : kind == Kind.Speedy ? Speedy : Orange;
        var shell = Lit(shellColour, kind == Kind.Golden ? 0.9f : 0.15f);
        var spotMat = kind == Kind.Feature || kind == Kind.Speedy ? Lit(Cream) : Lit(Navy);

        Prim(PrimitiveType.Sphere, "Belly", root, new Vector3(0, 0.007f, -0.002f), new Vector3(0.025f, 0.009f, 0.031f), Lit(Navy));
        var radii = new Vector3(0.015f, 0.016f, 0.019f);
        var shellBase = new Vector3(0, 0.006f, -0.003f);
        Dome("Shell", root, shellBase, radii * 2, shell);
        // a spot or seam piece sitting on the shell at a direction from its middle, turned to lie flat on it
        void OnShell(string name, Vector3 dir, Vector3 size, Material mat, PrimitiveType type)
        {
            dir = dir.normalized;
            var normal = new Vector3(dir.x / radii.x, dir.y / radii.y, dir.z / radii.z).normalized;
            var piece = Prim(type, name, root, shellBase + Vector3.Scale(dir, radii) + normal * 0.0004f, size, mat);
            piece.localRotation = Quaternion.FromToRotation(Vector3.up, normal);
        }
        foreach (var d in new[] { new Vector3(0.55f, 0.75f, 0.35f), new Vector3(-0.55f, 0.75f, 0.35f), new Vector3(0.6f, 0.65f, -0.45f), new Vector3(-0.6f, 0.65f, -0.45f), new Vector3(0, 0.8f, -0.6f) })
            OnShell("Spot", d, new Vector3(0.0075f, 0.0012f, 0.0075f), spotMat, PrimitiveType.Sphere);
        for (int k = -3; k <= 3; k++) // the line where the two wing cases meet
            OnShell("Seam", new Vector3(0, Mathf.Cos(k * 0.24f), Mathf.Sin(k * 0.24f)), new Vector3(0.0012f, 0.0008f, 0.0052f), Lit(Navy), PrimitiveType.Cube);

        Prim(PrimitiveType.Sphere, "Head", root, new Vector3(0, 0.009f, 0.017f), new Vector3(0.017f, 0.013f, 0.014f), Lit(Navy));
        foreach (float side in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Sphere, "Eye", root, new Vector3(side * 0.0045f, 0.0135f, 0.0215f), Vector3.one * 0.0075f, Lit(Cream));
            Prim(PrimitiveType.Sphere, "Pupil", root, new Vector3(side * 0.0042f, 0.0142f, 0.0249f), Vector3.one * 0.0036f, Lit(Ink));
            var feeler = Prim(PrimitiveType.Capsule, "Feeler", root, new Vector3(side * 0.0055f, 0.0195f, 0.025f), new Vector3(0.0014f, 0.0065f, 0.0014f), Lit(Navy));
            feeler.localRotation = Quaternion.Euler(35, 0, -side * 28);
            Prim(PrimitiveType.Sphere, "Tip", root, feeler.localPosition + feeler.localRotation * Vector3.up * 0.0068f, Vector3.one * 0.0038f, Lit(Orange, 0.5f));
        }

        // legs: a pivot at the body for each, a thigh up and out to the knee, a shin down and out to the floor
        var legs = new Transform[6];
        var legMat = Lit(Navy);
        for (int i = 0; i < 6; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            var pivot = new GameObject("Leg").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(side * 0.009f, 0.008f, (i / 2 - 1) * 0.0105f);
            pivot.localRotation = Quaternion.Euler(0, (i / 2 - 1) * -side * 25f, 0); // front legs reach forward, back legs back
            var thighDir = new Vector3(side * Mathf.Cos(25 * Mathf.Deg2Rad), Mathf.Sin(25 * Mathf.Deg2Rad), 0);
            var thigh = Prim(PrimitiveType.Capsule, "Thigh", pivot, thighDir * 0.008f, new Vector3(0.0028f, 0.008f, 0.0028f), legMat);
            thigh.localRotation = Quaternion.FromToRotation(Vector3.up, thighDir);
            var knee = thighDir * 0.016f;
            var shinDir = new Vector3(side * Mathf.Sin(20 * Mathf.Deg2Rad), -Mathf.Cos(20 * Mathf.Deg2Rad), 0);
            var shin = Prim(PrimitiveType.Capsule, "Shin", pivot, knee + shinDir * 0.0075f, new Vector3(0.0024f, 0.0075f, 0.0024f), legMat);
            shin.localRotation = Quaternion.FromToRotation(Vector3.up, shinDir);
            legs[i] = pivot;
        }
        if (kind == Kind.Feature)
        {
            var tag = NewSign(root, "feature", 0.011f, Cream, Bad);
            tag.transform.localPosition = new Vector3(0, 0.045f, 0);
        }

        var col = root.gameObject.AddComponent<SphereCollider>(); // a fat finger target, about twice the bug
        col.center = new Vector3(0, 0.012f, 0);
        col.radius = 0.03f;
        root.gameObject.SetActive(false);
        var bug = new Bug { kind = kind, t = root, legs = legs, move = Move.Dead, scale = scale };
        bug.legRest = System.Array.ConvertAll(legs, l => l.localRotation);
        return bug;
    }

    // The top half of a sphere (a smooth dome, base at y 0), sized by `size` like a primitive.
    Transform Dome(string name, Transform parent, Vector3 position, Vector3 size, Material mat)
    {
        if (!domeMesh)
        {
            const int rings = 8, segs = 20;
            var v = new List<Vector3>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float a = r * Mathf.PI / 2 / rings; // 0 at the top, 90 at the rim
                for (int g = 0; g <= segs; g++)
                {
                    float b = g * Mathf.PI * 2 / segs;
                    v.Add(new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)) * 0.5f);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int g = 0; g < segs; g++)
                {
                    int i0 = r * (segs + 1) + g, i1 = i0 + segs + 1;
                    tris.AddRange(new[] { i0, i0 + 1, i1, i0 + 1, i1 + 1, i1 });
                }
            domeMesh = new Mesh { name = "Bug Dome" };
            domeMesh.SetVertices(v);
            domeMesh.SetTriangles(tris, 0);
            domeMesh.RecalculateNormals();
        }
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<MeshFilter>().sharedMesh = domeMesh;
        var r2 = go.GetComponent<MeshRenderer>();
        r2.sharedMaterial = mat;
        r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // A fly swatter like a real one: a maroon mesh head (a lattice of bars), a steel shaft and a maroon grip with
    // finger bumps. Its pivot is the end of the grip, so it swings down onto the card like it is held by a hand.
    // Built pointing along +z, lying flat; the head's middle is SwatReach from the pivot.
    const float SwatReach = 0.24f;
    Transform NewSwatter()
    {
        var root = new GameObject("Swatter").transform;
        root.SetParent(content, false);
        var maroon = Lit(Swat);
        var head = new GameObject("Head").transform;
        head.SetParent(root, false);
        head.localPosition = new Vector3(0, 0, SwatReach);
        const float w = 0.1f, d = 0.11f, bar = 0.0035f, thick = 0.003f;
        Prim(PrimitiveType.Cube, "Rim", head, new Vector3(0, 0, d / 2), new Vector3(w, thick, bar * 1.6f), maroon);
        Prim(PrimitiveType.Cube, "Rim", head, new Vector3(0, 0, -d / 2), new Vector3(w, thick, bar * 1.6f), maroon);
        Prim(PrimitiveType.Cube, "Rim", head, new Vector3(w / 2, 0, 0), new Vector3(bar * 1.6f, thick, d), maroon);
        Prim(PrimitiveType.Cube, "Rim", head, new Vector3(-w / 2, 0, 0), new Vector3(bar * 1.6f, thick, d), maroon);
        for (int i = 1; i < 9; i++) Prim(PrimitiveType.Cube, "Bar", head, new Vector3(-w / 2 + i * w / 9, 0, 0), new Vector3(bar, thick * 0.9f, d), maroon);
        for (int i = 1; i < 10; i++) Prim(PrimitiveType.Cube, "Bar", head, new Vector3(0, 0, -d / 2 + i * d / 10), new Vector3(w, thick * 0.9f, bar), maroon);
        Prim(PrimitiveType.Cube, "Neck", root, new Vector3(0, 0, SwatReach - d / 2 - 0.012f), new Vector3(0.012f, thick * 1.4f, 0.026f), maroon);

        var along = Quaternion.Euler(90, 0, 0); // a cylinder or capsule runs along y; this lays it along +z
        var shaft = Prim(PrimitiveType.Cylinder, "Shaft", root, new Vector3(0, 0, 0.125f), new Vector3(0.0045f, 0.055f, 0.0045f), Lit(Steel));
        shaft.localRotation = along;
        var grip = Prim(PrimitiveType.Capsule, "Grip", root, new Vector3(0, 0, 0.04f), new Vector3(0.016f, 0.04f, 0.013f), maroon);
        grip.localRotation = along;
        for (int i = 0; i < 4; i++) // finger bumps along one side, like the picture
            Prim(PrimitiveType.Sphere, "Bump", root, new Vector3(0, -0.0055f, 0.012f + i * 0.017f), new Vector3(0.015f, 0.009f, 0.012f), maroon);
        root.gameObject.SetActive(false);
        return root;
    }

    // Brings a free swatter down onto `at` (card space) from the side the camera is on, as if you swung it.
    // Calls `hit` the moment it lands, then lifts it away.
    IEnumerator SwatAt(Vector3 at, System.Action hit)
    {
        var s = System.Array.Find(swatters, x => !x.gameObject.activeSelf) ?? swatters[0];
        var toCam = content.InverseTransformPoint(arCamera.transform.position) - at;
        toCam.y = 0;
        if (toCam.sqrMagnitude < 1e-6f) toCam = Vector3.back;
        var yaw = Quaternion.LookRotation(-toCam.normalized); // the head reaches away from you, the grip comes toward you
        s.localPosition = new Vector3(at.x, 0.012f, at.z) - yaw * Vector3.forward * SwatReach;
        s.localScale = Vector3.one;
        s.gameObject.SetActive(true);
        // up, then a fast swing down (pitch 0 = flat on the card)
        yield return Ease.Run(0.06f, t => s.localRotation = yaw * Quaternion.Euler(-Mathf.Lerp(40f, 70f, t), 0, 0));
        yield return Ease.Run(0.08f, t => s.localRotation = yaw * Quaternion.Euler(-70f * (1 - Ease.InCubic(t)), 0, 0));
        hit();
        yield return new WaitForSeconds(0.14f);
        yield return Ease.Run(0.22f, t =>
        {
            s.localRotation = yaw * Quaternion.Euler(-45f * Ease.OutCubic(t), 0, 0);
            s.localScale = Vector3.one * Mathf.Max(0.001f, 1 - Ease.InCubic(t));
        });
        s.gameObject.SetActive(false);
    }

    Billboard resetBest;
    float resetArmedUntil = -1;

    // First tap asks, a second tap within 3 seconds clears the best score saved on the phone.
    void ResetBestTap()
    {
        if (Time.time > resetArmedUntil)
        {
            resetArmedUntil = Time.time + 3f;
            resetBest.SetText("Tap again to reset");
            PlaySfx(tickClip, 0.6f);
            Haptics.Buzz(15, Haptics.Light);
            return;
        }
        resetArmedUntil = -1;
        PlayerPrefs.DeleteKey(BestKey);
        PlayerPrefs.DeleteKey(BestLevelKey);
        PlayerPrefs.Save();
        resBest.text = "Best 0  (level 0)";
        resetBest.SetText("Best reset");
        PlaySfx(popClip);
        Haptics.Buzz(40, Haptics.Medium);
    }

    void BuildResults()
    {
        var board = new GameObject("Bug Hunt Results").AddComponent<Billboard>();
        board.cam = arCamera;
        results = board.transform;
        results.SetParent(content, false);
        results.localPosition = new Vector3(0.05f, 0.33f, 0); // the Reset best button under it stays well off the card
        Board(results, new Vector2(0.42f, 0.32f));
        Text(results, "BUG HUNT", 0.03f, Orange, new Vector3(0, 0.11f, -0.01f));
        resScore = Text(results, "", 0.04f, Cream, new Vector3(0, 0.058f, -0.01f));
        resBest = Text(results, "", 0.02f, Slate, new Vector3(0, 0.012f, -0.01f));
        ratingSign = NewSign(results, "", 0.022f, Navy, Orange);
        ratingSign.face = false;
        ratingSign.transform.localPosition = new Vector3(0, -0.035f, -0.011f);

        againSign = NewSign(results, "Next level", 0.022f, Cream, Orange);
        againSign.face = false;
        againSign.transform.localPosition = new Vector3(-0.095f, -0.105f, -0.011f);
        AddTap(againSign.back.gameObject, 1f, () => { if (resultsUp) StartCoroutine(PlayAgain()); });
        // under the board: wipe the saved best. Two taps, so a stray finger in a demo can't do it by accident.
        resetBest = NewSign(results, "Reset best", 0.015f, Slate, Navy);
        resetBest.face = false;
        resetBest.transform.localPosition = new Vector3(0, -0.19f, -0.011f);
        AddTap(resetBest.back.gameObject, 1.3f, () => { if (resultsUp) ResetBestTap(); });
        var hire = NewSign(results, "Hire me >", 0.022f, Navy, Cream);
        hire.face = false;
        hire.transform.localPosition = new Vector3(0.1f, -0.105f, -0.011f);
        AddTap(hire.back.gameObject, 1f, () => { if (resultsUp) StartCoroutine(HireMe()); });
        results.gameObject.SetActive(false);
    }

    void ResetBugHunt()
    {
        gameOn = playing = resultsUp = hinted = false;
        levelNo = 1;
        total = 0;
        hubIdle = 0;
        if (!monitor) return;
        foreach (var b in bugs) { b.move = Move.Dead; b.t.gameObject.SetActive(false); }
        foreach (var p in popups) p.gameObject.SetActive(false);
        foreach (var w in swatters) w.gameObject.SetActive(false);
        hud.gameObject.SetActive(false);
        bigSign.gameObject.SetActive(false);
        results.gameObject.SetActive(false);
        alarm.gameObject.SetActive(false);
        pose.typing = 1;
        pose.lookAt.localPosition = lookRest;
    }

    // ---------- On the main screen ----------

    // The blinking "!" over his monitor, and a nudge from him if nobody has found the game after 10 seconds.
    void UpdateBugHuntInvite(float t, float dt)
    {
        if (!monitor) return;
        bool show = Current == State.Hub && !rocketing;
        bang.gameObject.SetActive(show);
        if (!show) return;
        bang.localScale = Vector3.one * (1 + 0.15f * Mathf.Sin(t * 6f));
        if (hinted || turning) return;
        hubIdle += dt;
        if (hubIdle < 10f || ui.bubble.activeSelf) return;
        hinted = true;
        ShowBubble("Psst... tap my monitor!", 3f);
    }

    bool TappedMonitor(RaycastHit[] hits) =>
        monitorCol && Current == State.Hub && System.Array.Exists(hits, h => h.collider == monitorCol);

    // ---------- The game ----------

    IEnumerator BugHuntRoutine()
    {
        Current = State.Game;
        gameOn = true;
        HideBubble();
        ShowBanner(false);
        PlaySfx(openClip);
        Haptics.Buzz(40, Haptics.Medium);
        if (hubs[0].pop > 0) yield return NetworkOut(); // not again on Play again: it is already gone
        level = LevelAt(levelNo);

        // the bugs break into his build: the screen flashes red and he calls for help
        avatarAnim.CrossFade("Talk", 0.2f);
        ShowBubble(levelNo == 1 ? "Bugs in my build! Squash them!" : "Level " + levelNo + "! They're getting faster!", 2.4f);
        for (int i = 0; i < 3; i++)
        {
            alarm.gameObject.SetActive(true);
            PlaySfx(tickClip, 0.5f);
            yield return new WaitForSeconds(0.18f);
            alarm.gameObject.SetActive(false);
            yield return new WaitForSeconds(0.14f);
        }
        yield return new WaitForSeconds(0.4f);
        avatarAnim.CrossFade("Look", 0.3f); // back to the keyboard

        // the level card: its number and name, then the target
        yield return Pop("Level " + levelNo + "\n" + level.name);
        PlaySfx(Voice("level"));
        yield return new WaitForSeconds(0.5f);
        PlaySfx(Voice(levelNo.ToString())); // the pack counts to 10; after that it just says "level"
        yield return new WaitForSeconds(0.6f);
        yield return Pop("Squash " + level.target);
        yield return new WaitForSeconds(0.9f);
        yield return Countdown();

        score = 0;
        targetHit = false;
        timeLeft = GameLength;
        lastTick = 6;
        UpdateScoreSign();
        timerFillR.sharedMaterial = Lit(Cream);
        hud.gameObject.SetActive(true);
        playing = true;
        Wave(0, level.waveMax + 2); // the first wave erupts on "Go!"
        spawnTimer = level.gapFrom;
        while (timeLeft > 0) yield return null; // UpdateBugHunt runs the clock
        playing = false;
        cleared = score >= level.target;
        total += score;

        // time's up: the bugs that are left run for it
        yield return Pop(cleared ? "Level clear!" : "Time!");
        PlaySfx(cleared ? sparkleClip : openClip);
        PlaySfx(Voice(cleared ? "level_up" : "time_over"));
        if (cleared) Burst(bigSign.position, 50);
        Haptics.Buzz(60, Haptics.Medium);
        foreach (var b in bugs)
            if (b.move == Move.Hop || b.move == Move.Pause || b.move == Move.Walk) { b.move = Move.Escape; b.dir = Outward(b.t.localPosition); b.speed = 0.6f; }
        yield return new WaitForSeconds(1.1f);
        bigSign.gameObject.SetActive(false);
        hud.gameObject.SetActive(false);
        foreach (var b in bugs) if (b.move != Move.Dead) { b.move = Move.Dead; b.t.gameObject.SetActive(false); }
        pose.typing = 1;
        pose.lookAt.localPosition = lookRest;

        yield return ShowResults();
    }

    // 3, 2, 1, Go! The music drops away, he stops typing, and each number slams in with a drum hit, a spoken
    // count (if the voice clips are in Resources/Audio), sparks and a stronger buzz, going from orange to red.
    IEnumerator Countdown()
    {
        if (music.isPlaying) StartCoroutine(FadeMusic(musicVolume * 0.2f, 0.4f));
        pose.typing = 0;
        string[] numbers = { "3", "2", "1" };
        Color[] colours = { Orange, Hot, Alarm };
        for (int i = 0; i < 3; i++)
        {
            bigSignText.SetText(numbers[i]);
            bigSignBack.sharedMaterial = Lit(colours[i], 0.3f + 0.3f * i);
            bigSign.gameObject.SetActive(true);
            PlaySfx(Voice(numbers[i]));
            PlaySfx(drumClip, 0.6f + 0.2f * i);
            Haptics.Buzz(30 + 25 * i, i == 2 ? Haptics.Strong : Haptics.Medium);
            Burst(bigSign.position, 15 + 10 * i);
            yield return Slam(2.2f, 0.22f);
            yield return Ease.Run(0.6f, t => bigSign.localScale = Vector3.one * (1 - 0.12f * t)); // it shrinks while you wait
        }
        bigSignText.SetText("GO!");
        bigSignBack.sharedMaterial = Lit(Orange, 0.8f);
        PlaySfx(Voice("go"));
        PlaySfx(drumClip);
        PlaySfx(bangClip ? bangClip : powerupClip, 0.7f);
        Haptics.Buzz(120, Haptics.Strong);
        Burst(bigSign.position, 60);
        if (music.isPlaying) StartCoroutine(FadeMusic(musicVolume, 0.5f));
        StartCoroutine(FlashAlarm());
        yield return Slam(3f, 0.25f);
        yield return new WaitForSeconds(0.25f);
        bigSign.gameObject.SetActive(false);
    }

    // It drops in big and punches down to size, overshooting a little.
    IEnumerator Slam(float from, float seconds) =>
        Ease.Run(seconds, t => bigSign.localScale = Vector3.one * Mathf.Max(0.001f, 1 + (from - 1) * (1 - Ease.OutBack(t, 1.8f))));

    IEnumerator FlashAlarm()
    {
        alarm.gameObject.SetActive(true);
        yield return new WaitForSeconds(0.12f);
        alarm.gameObject.SetActive(false);
    }

    // The announcer (Kenney's Voiceover Pack, male, in Resources/Audio/voice_*). Missing clips just stay quiet.
    static AudioClip Voice(string word) => Resources.Load<AudioClip>("Audio/voice_" + word);

    void UpdateScoreSign() =>
        scoreSign.SetText("Lv " + levelNo + "   " + score + " / " + level.target + (targetHit ? "   Clear!" : ""));

    IEnumerator Pop(string text)
    {
        bigSignText.SetText(text);
        bigSignBack.sharedMaterial = Lit(Orange);
        bigSign.gameObject.SetActive(true);
        yield return Ease.Run(0.25f, t => bigSign.localScale = Vector3.one * Mathf.Max(0.001f, Ease.OutBack(t, 2f)));
    }

    IEnumerator ShowResults()
    {
        int best = PlayerPrefs.GetInt(BestKey, 0), bestLevel = PlayerPrefs.GetInt(BestLevelKey, 0);
        int reached = cleared ? levelNo : levelNo - 1; // levels cleared
        bool newBest = total > best || reached > bestLevel;
        if (newBest)
        {
            best = Mathf.Max(best, total);
            bestLevel = Mathf.Max(bestLevel, reached);
            PlayerPrefs.SetInt(BestKey, best);
            PlayerPrefs.SetInt(BestLevelKey, bestLevel);
            PlayerPrefs.Save();
        }
        resScore.text = cleared ? "Level " + levelNo + " clear!" : "Game over";
        resBest.text = "Total " + total + "    " + (newBest ? "New best!" : "Best " + best + "  (level " + bestLevel + ")");
        resetBest.SetText("Reset best");
        resetArmedUntil = -1;
        againSign.SetText(cleared ? "Next level" : "Try again");
        ratingSign.SetText(reached >= 4 ? "Lead Engineer" : reached == 3 ? "Senior Debugger" : reached == 2 ? "Bug Squasher" : "Junior Dev");

        results.localScale = Vector3.one * 0.001f;
        results.gameObject.SetActive(true);
        PlaySfx(sparkleClip);
        Burst(results.position, 30);
        yield return Ease.Run(0.5f, t => results.localScale = Vector3.one * Mathf.Max(0.001f, Ease.OutBack(t, 1.4f)));
        resultsUp = true;
        if (!cleared) PlaySfx(Voice(newBest ? "new_highscore" : "game_over"));
        if (cleared)
        {
            BugHuntCleared(levelNo);
            avatarAnim.CrossFade("Cheer", 0.2f);
            ShowBubble(levelNo >= 3 ? "You're a machine! Ready for more?" : "Nice! Ready for the next one?", 3f);
            for (int i = 0; i < 3; i++) { Burst(results.position + Random.insideUnitSphere * 0.08f, 25); yield return new WaitForSeconds(0.15f); }
        }
        else if (newBest)
        {
            avatarAnim.CrossFade("Cheer", 0.2f);
            ShowBubble("New best! " + total + " bugs fixed!", 3f);
            PlaySfx(powerupClip, 0.8f);
            for (int i = 0; i < 4; i++) { Burst(results.position + Random.insideUnitSphere * 0.08f, 25); yield return new WaitForSeconds(0.15f); }
        }
        else
        {
            avatarAnim.CrossFade("Talk", 0.2f);
            ShowBubble("The bugs won this time... try again?", 2.5f);
        }
    }

    IEnumerator HideResults()
    {
        resultsUp = false;
        PlaySfx(tickClip);
        yield return Ease.Run(0.25f, t => results.localScale = Vector3.one * Mathf.Max(0.001f, 1 - Ease.InCubic(t)));
        results.gameObject.SetActive(false);
    }

    // Next level after a clear, or back to level 1 after a game over.
    IEnumerator PlayAgain()
    {
        if (cleared) levelNo++;
        else { levelNo = 1; total = 0; }
        yield return HideResults();
        StartCoroutine(BugHuntRoutine());
    }

    // Back to the main screen: the nodes grow back (they follow gameOn).
    IEnumerator ExitBugHunt()
    {
        yield return HideResults();
        gameOn = false;
        levelNo = 1;
        total = 0;
        avatarAnim.CrossFade("Look", 0.3f);
        yield return NetworkIn();
        Current = State.Hub;
        hubIdle = 0;
        ShowBanner(true);
    }

    // The nodes, their name signs and the lines sink away so only the bugs are on the card (the HW logo stays),
    // and grow back out of the logo afterwards, like the Portfolio does.
    IEnumerator NetworkOut()
    {
        PlaySfx(pulseClip, 0.5f);
        yield return Ease.Run(0.5f, t =>
        {
            float k = 1 - Ease.InOutSine(t);
            ringGrow = k;
            foreach (var h in hubs) { h.grow = k; h.pop = k; }
        });
        ringGrow = 0;
        foreach (var h in hubs) h.grow = h.pop = 0;
    }

    IEnumerator NetworkIn()
    {
        PlaySfx(powerupClip, 0.6f);
        yield return Ease.Run(0.7f, t =>
        {
            float k = Ease.InOutSine(t);
            ringGrow = k;
            foreach (var h in hubs) { h.grow = k; h.pop = Ease.OutBack(t, 1.6f); }
        });
        ringGrow = 1;
        foreach (var h in hubs) h.grow = h.pop = 1;
        foreach (var h in hubs) Burst(h.node.position, 12);
    }

    IEnumerator HireMe()
    {
        yield return ExitBugHunt();
        yield return new WaitForSeconds(0.3f);
        OpenPanel(3);
        yield return new WaitForSeconds(0.7f);
        if (contactItems.Length > 0) StartCoroutine(PopItem(contactItems[0], 1f)); // the envelope bounces: email me
        ShowBubble("Email me!", 2.5f);
    }

    // Called from Update while the game (or its intro and results) is on.
    void UpdateBugHunt(float t, float dt)
    {
        if (playing)
        {
            timeLeft = Mathf.Max(0, timeLeft - dt);
            float progress = 1 - timeLeft / GameLength;
            pose.typing = Mathf.Lerp(1.6f, 3.2f, progress) * (1 + 0.15f * (levelNo - 1)); // he types faster as it gets hectic
            float frac = timeLeft / GameLength;
            timerFill.localScale = new Vector3(0.29f * frac, 0.01f, 0.004f);
            timerFill.localPosition = new Vector3(-0.145f * (1 - frac), -0.048f, -0.004f);
            int whole = Mathf.CeilToInt(timeLeft);
            if (whole <= 5 && whole < lastTick && timeLeft > 0) // the last five seconds tick and the bar turns orange
            {
                lastTick = whole;
                PlaySfx(tickClip, 0.8f);
                if (whole <= 3) PlaySfx(Voice(whole.ToString()), 0.7f); // "three, two, one" again at the end
                else if (whole == 5) PlaySfx(Voice("hurry_up"), 0.8f);
                timerFillR.sharedMaterial = Lit(Orange, 0.6f);
            }
            spawnTimer -= dt;
            if (spawnTimer <= 0)
            {
                spawnTimer = Mathf.Lerp(level.gapFrom, level.gapTo, progress);
                Wave(progress, Random.Range(1, Mathf.RoundToInt(Mathf.Lerp(1, level.waveMax, progress)) + 2));
            }
        }

        foreach (var b in bugs)
            if (b.move != Move.Dead && b.move != Move.Splat) MoveBug(b, t, dt);
        bool watching = watched != null && watched.move != Move.Dead && watched.move != Move.Splat;
        Vector3 look = watching ? watched.t.position : pose.lookAt.parent.TransformPoint(lookRest); // he glances at the newest bug
        pose.lookAt.position = Vector3.Lerp(pose.lookAt.position, look, 1 - Mathf.Exp(-4f * dt));

        for (int i = 0; i < popups.Length; i++)
        {
            if (!popups[i].gameObject.activeSelf) continue;
            popAge[i] += dt;
            popups[i].transform.localPosition += Vector3.up * (0.08f * dt);
            var c = popups[i].text.color;
            popups[i].text.color = new Color(c.r, c.g, c.b, 1 - popAge[i] / 0.8f);
            if (popAge[i] >= 0.8f) popups[i].gameObject.SetActive(false);
        }

        if (TapInput.Started(out Vector2 screen)) GameTap(screen);
    }

    // A wave of `count` bugs flung out of his monitor one right after another.
    void Wave(float progress, int count)
    {
        for (int i = 0; i < count; i++) Spawn(progress, i * 0.07f);
        if (count > 0) { PlaySfx(popClip, 0.4f); Haptics.Buzz(10 + 4 * count, Haptics.Light); }
    }

    void Spawn(float progress, float delay)
    {
        int alive = 0;
        foreach (var b in bugs) if (b.move != Move.Dead) alive++;
        if (alive >= MaxBugs) return;
        float r = Random.value;
        var kind = r < level.feature ? Kind.Feature : r < level.feature + level.golden ? Kind.Golden
                 : r < level.feature + level.golden + level.speedy ? Kind.Speedy : Kind.Bug;
        var bug = bugs.Find(b => b.kind == kind && b.move == Move.Dead) ?? bugs.Find(b => b.kind == Kind.Bug && b.move == Move.Dead);
        if (bug == null) return;

        // it is flung out of his monitor and lands on any spot on the network (so a wave spreads out)
        bug.start = content.InverseTransformPoint(monitor.position);
        bug.at = bug.prev = Random.Range(0, bugPoints.Length);
        bug.move = Move.Hop;
        bug.timer = -delay / HopTime(bug); // waits its turn in the screen
        bug.hopsLeft = Random.Range(2, level.hops + 1);
        float kindSpeed = bug.kind == Kind.Golden ? 1.6f : bug.kind == Kind.Feature ? 0.8f : bug.kind == Kind.Speedy ? 1.8f : 1f;
        bug.speed = Mathf.Lerp(0.12f, 0.26f, progress) * level.speed * kindSpeed;
        bug.land = 0;
        bug.t.localPosition = bug.start;
        bug.t.localScale = Vector3.one * bug.scale;
        bug.t.gameObject.SetActive(true);
        watched = bug;
    }

    // Quick: about a quarter of a second for a near spot, a bit longer for the far side of the card.
    float HopTime(Bug b) => 0.16f + 0.28f * Vector3.Distance(b.start, bugPoints[b.at]);

    void MoveBug(Bug b, float t, float dt)
    {
        Vector3 p = b.t.localPosition, heading = Vector3.zero;
        switch (b.move)
        {
            case Move.Hop:
                b.timer += dt / HopTime(b);
                float k = Mathf.Clamp01(b.timer), arc = 0.05f + 0.12f * Vector3.Distance(b.start, bugPoints[b.at]);
                p = Vector3.Lerp(b.start, bugPoints[b.at], k) + Vector3.up * (4 * k * (1 - k) * arc); // a thrown arc
                heading = bugPoints[b.at] - b.start;
                if (k >= 1) // splat down: squash and a puff of dust, then straight off
                {
                    b.move = Move.Pause;
                    b.timer = 0.05f;
                    b.land = 1;
                    Burst(b.t.position, 3);
                }
                break;
            case Move.Pause:
                b.timer -= dt;
                if (b.timer > 0) break;
                if (b.hopsLeft <= 0) { b.move = Move.Escape; b.dir = Outward(p); break; }
                var links = bugLinks[b.at];
                b.next = links[Random.Range(0, links.Count)];
                if (b.next == b.prev && links.Count > 1) b.next = links[(links.IndexOf(b.next) + 1) % links.Count]; // don't turn straight back
                b.travel = 0;
                b.move = Move.Walk;
                break;
            case Move.Walk:
                Vector3 a = bugPoints[b.at], z = bugPoints[b.next];
                float len = Vector3.Distance(a, z);
                b.travel += b.speed * dt;
                p = Vector3.Lerp(a, z, Mathf.Min(1, b.travel / len));
                heading = z - a;
                if (b.kind == Kind.Speedy) // it zig-zags along the line
                {
                    float wiggle = Mathf.Sin(b.travel * 45f) * Mathf.Sin(Mathf.PI * Mathf.Min(1, b.travel / len));
                    p += Vector3.Cross(Vector3.up, heading.normalized) * (0.016f * wiggle);
                    heading += Vector3.Cross(Vector3.up, heading) * (0.6f * Mathf.Cos(b.travel * 45f));
                }
                if (b.travel >= len) { b.prev = b.at; b.at = b.next; b.hopsLeft--; b.move = Move.Pause; b.timer = Random.Range(0.1f, 0.35f); }
                break;
            case Move.Escape:
                p += b.dir * (b.speed * 1.3f * dt);
                p.y = Mathf.MoveTowards(p.y, BugY, dt * 0.3f);
                heading = b.dir;
                if (Mathf.Abs(p.x) > 0.53f || Mathf.Abs(p.z) > 0.33f) { b.move = Move.Dead; b.t.gameObject.SetActive(false); return; }
                break;
        }
        b.t.localPosition = p;
        if (b.land > 0)
        {
            b.land = Mathf.MoveTowards(b.land, 0, dt * 6f);
            b.t.localScale = b.scale * new Vector3(1 + 0.3f * b.land, 1 - 0.4f * b.land, 1 + 0.3f * b.land);
        }
        heading.y = 0;
        if (heading.sqrMagnitude > 1e-6f) b.t.localRotation = Quaternion.Slerp(b.t.localRotation, Quaternion.LookRotation(heading), 1 - Mathf.Exp(-12f * dt));
        // tripod gait: front and back legs on one side step with the middle leg on the other
        float stride = b.move == Move.Pause ? 0 : 22f, step = t * (18f + b.speed * 60f);
        for (int i = 0; i < b.legs.Length; i++)
        {
            float phase = step + ((i / 2 + i % 2) % 2) * Mathf.PI;
            float lift = Mathf.Max(0, Mathf.Sin(phase)) * (b.move == Move.Pause ? 0 : 14f);
            b.legs[i].localRotation = b.legRest[i] * Quaternion.Euler(0, Mathf.Cos(phase) * stride, (i % 2 == 0 ? -lift : lift));
        }
    }

    // Away from the middle of the card, toward the nearest edge.
    static Vector3 Outward(Vector3 p)
    {
        var d = new Vector3(p.x, 0, p.z * 1.6f); // the card is wider than deep, so lean toward the closer long edge
        return d.sqrMagnitude < 1e-4f ? Vector3.back : d.normalized;
    }

    void GameTap(Vector2 screen)
    {
        var hits = Physics.RaycastAll(arCamera.ScreenPointToRay(screen), 100f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (playing)
            {
                var bug = bugs.Find(b => b.t == hit.collider.transform && (b.move == Move.Hop || b.move == Move.Pause || b.move == Move.Walk));
                if (bug != null) { StartCoroutine(Squash(bug)); return; }
            }
            var tap = hit.collider.GetComponentInParent<Tappable>();
            if (tap && resultsUp) { tap.onTap(); return; }
        }
        if (resultsUp) StartCoroutine(ExitBugHunt()); // a tap on nothing after the results: back to the network
    }

    IEnumerator Squash(Bug b)
    {
        b.move = Move.Splat; // it freezes under the swatter; the score counts when it lands
        bool landed = false;
        StartCoroutine(SwatAt(b.t.localPosition, () => landed = true));
        yield return new WaitUntil(() => landed);
        int points = b.kind == Kind.Golden ? 5 : b.kind == Kind.Feature ? -3 : b.kind == Kind.Speedy ? 2 : 1;
        score = Mathf.Max(0, score + points);
        if (!targetHit && score >= level.target) // target reached: keep going for a better total
        {
            targetHit = true;
            PlaySfx(sparkleClip, 0.8f);
            Burst(scoreSign.transform.position, 30);
        }
        UpdateScoreSign();
        ScorePop(b.t.localPosition, points);
        PlaySfx(splatClip ? splatClip : popClip);
        if (b.kind == Kind.Golden) PlaySfx(sparkleClip, 0.7f);
        if (b.kind == Kind.Feature) ShowBubble("That's not a bug, that's a feature!", 1.8f);
        Haptics.Buzz(b.kind == Kind.Feature ? 60 : 25, b.kind == Kind.Feature ? Haptics.Medium : Haptics.Light);
        Burst(b.t.position, b.kind == Kind.Golden ? 40 : 18);

        var flat = new Vector3(1.35f, 0.15f, 1.35f) * b.scale;
        yield return Ease.Run(0.1f, t => b.t.localScale = Vector3.Lerp(Vector3.one * b.scale, flat, t));
        yield return new WaitForSeconds(0.3f);
        b.t.gameObject.SetActive(false);
        b.t.localScale = Vector3.one * b.scale;
        b.move = Move.Dead;
    }

    void ScorePop(Vector3 at, int points)
    {
        int i = System.Array.FindIndex(popups, p => !p.gameObject.activeSelf);
        if (i < 0) i = 0;
        popAge[i] = 0;
        var p = popups[i];
        p.text.text = (points > 0 ? "+" : "") + points;
        p.text.color = points < 0 ? Alarm : points > 1 ? Gold : Orange;
        p.transform.localPosition = at + Vector3.up * 0.05f;
        p.gameObject.SetActive(true);
    }
}
