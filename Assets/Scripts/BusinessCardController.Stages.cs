using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// What a node opens: a small 3D scene that grows out above it and stays on the card, built here from primitives.
//   About     a name board with Howard's tagline and interests
//   Skills    skill blocks circling the node; tap one to pop it
//   Portfolio takes over the whole card as a gallery (BusinessCardController.Gallery.cs)
//   Contact   an envelope, a phone and a globe; tap one to use it
// Words and pictures come from business_card.json. Sizes are in card units (the card is 1 wide, 0.6 deep).
public partial class BusinessCardController
{
    const float TextScale = 1f; // multiplies every 3D text size; the one number to change if the words look too big or small
    const float StageHeight = 0.28f; // high enough that every scene clears its own diamond (top about 0.14)

    Transform[] stageRoot;
    int openStage = -1;
    Coroutine stageRoutine;
    readonly Dictionary<Color, Material> litMats = new Dictionary<Color, Material>();

    // skills
    Transform[] skillItems;
    int project; // the portfolio project shown
    // contact
    Transform[] contactItems;

    void BuildStages()
    {
        stageRoot = new Transform[hubs.Length];
        ui.tags = new GameObject[hubs.Length];
        for (int i = 0; i < hubs.Length; i++)
        {
            var root = new GameObject(hubs[i].title + " Stage").transform;
            root.SetParent(content, false);
            root.localPosition = new Vector3(hubs[i].rest.x, StageHeight, hubs[i].rest.z);
            stageRoot[i] = root;
            ui.tags[i] = NewSign(content, hubs[i].title, 0.026f, Cream, Navy).gameObject;
            ui.tags[i].SetActive(false);
        }
        BuildAbout(stageRoot[0]);
        BuildSkills(stageRoot[1]);
        BuildGallery();
        BuildContact(stageRoot[3]);
        foreach (var root in stageRoot) root.gameObject.SetActive(false);
    }

    // ---------- Open and close ----------

    void ShowPanel(int i)
    {
        HideBubble();
        ui.banner.SetActive(false);
        if (stageRoutine != null) StopCoroutine(stageRoutine);
        foreach (var root in stageRoot) root.gameObject.SetActive(false);
        openStage = i;
        var stage = stageRoot[i];
        stage.localScale = Vector3.zero;
        stage.gameObject.SetActive(true);
        Burst(stage.position, 30);
        stageRoutine = StartCoroutine(Ease.Run(0.6f, t => stage.localScale = Vector3.one * Mathf.Max(0.001f, Ease.OutBack(t, 1.5f))));
    }

    void HidePanel()
    {
        if (openStage < 0) return;
        var stage = stageRoot[openStage];
        openStage = -1;
        if (stageRoutine != null) StopCoroutine(stageRoutine);
        float from = stage.localScale.x;
        stageRoutine = StartCoroutine(CloseStage(stage, from));
    }

    IEnumerator CloseStage(Transform stage, float from)
    {
        yield return Ease.Run(0.25f, t => stage.localScale = Vector3.one * Mathf.Max(0.001f, from * (1 - Ease.InCubic(t))));
        stage.gameObject.SetActive(false);
    }

    void UpdateStages(float t, float dt)
    {
        for (int i = 0; i < hubs.Length; i++)
        {
            ui.tags[i].transform.localPosition = hubs[i].node.localPosition + Vector3.up * (hubs[i].node.localScale.y + 0.04f); // diamond tip (its scale is its half-height) plus half a sign and a gap
            // while a scene is open every name sign steps aside, so none of them lands in the middle of it
            ui.tags[i].transform.localScale = Vector3.one * hubs[i].pop * (selected < 0 && !gameOn ? 1 : 0);
        }
        if (openStage < 0) return;

        if (openStage == 1)
            for (int i = 0; i < skillItems.Length; i++)
            {
                // a wide ring, every other block a step higher, so names passing each other don't pile up
                float a = t * 0.7f + i * Mathf.PI * 2 / skillItems.Length;
                float y = (i % 2 == 0 ? 0.03f : -0.03f) + 0.008f * Mathf.Sin(t * 2 + i);
                skillItems[i].localPosition = new Vector3(Mathf.Cos(a) * 0.2f, y, Mathf.Sin(a) * 0.13f);
                // each model turns to show you its front, rocking from side to side (not while it is flying away)
                var model = skillItems[i].GetChild(0);
                if (skillFlying != null && skillFlying[i]) continue;
                Vector3 up = skillItems[i].parent.up, away = Vector3.ProjectOnPlane(model.position - arCamera.transform.position, up);
                if (away.sqrMagnitude > 1e-6f)
                    model.rotation = Quaternion.LookRotation(away, up) * Quaternion.Euler(8f * Mathf.Sin(t * 1.7f + i), 40f * Mathf.Sin(t * 1.1f + i * 1.3f), 0);
            }
        else if (openStage == 3)
            for (int i = 0; i < contactItems.Length; i++)
                contactItems[i].localRotation = Quaternion.Euler(0, 35f * Mathf.Sin(t * 1.4f + i * 1.3f), 0);
    }

    // ---------- About ----------

    void BuildAbout(Transform root)
    {
        var board = new GameObject("Board").AddComponent<Billboard>();
        board.transform.SetParent(root, false);
        board.cam = arCamera;
        var tb = board.transform;
        Board(tb, new Vector2(0.38f, 0.27f));
        Text(tb, data.name, 0.038f, Cream, new Vector3(0, 0.07f, -0.01f));
        string tagline = Wrap(data.tagline, 22);
        Text(tb, tagline, 0.02f, Slate, new Vector3(0, 0.025f, -0.01f));
        for (int i = 0; i < data.interests.Length && i < 3; i++)
        {
            var chip = NewSign(tb, data.interests[i], 0.02f, Navy, Orange);
            chip.face = false;
            chip.transform.localPosition = new Vector3(0, -0.035f - i * 0.04f, -0.011f);
        }
    }

    // ---------- Skills ----------

    void BuildSkills(Transform root)
    {
        skillItems = new Transform[data.skills.Length];
        for (int i = 0; i < skillItems.Length; i++)
        {
            var item = new GameObject("Skill " + data.skills[i]).transform;
            item.SetParent(root, false);
            var model = SkillModel(data.skills[i], item); // child 0: the orbit spins it
            MeshObject("Halo", model, new Vector3(0, -0.034f, 0), Torus(0.032f, 0.0016f), Lit(Cream, 0.35f));
            var label = NewSign(item, data.skills[i], 0.02f, Cream, Navy);
            label.transform.localPosition = new Vector3(0, 0.05f, 0);
            int k = i;
            AddTap(model.gameObject, 1f, () => StartCoroutine(FlyAway(k, model, label.transform)));
            model.GetComponent<BoxCollider>().size = new Vector3(0.07f, 0.08f, 0.07f); // a size-1 box would cover the card
            skillItems[i] = item;
        }
        skillFlying = new bool[skillItems.Length];
    }

    bool[] skillFlying;

    // Tapped: it squashes, launches spiralling up with a sparkly trail and vanishes, then drops back into its
    // place in the ring with a bounce.
    IEnumerator FlyAway(int i, Transform model, Transform label)
    {
        if (skillFlying[i]) yield break;
        skillFlying[i] = true;
        PlaySfx(popClip);
        Haptics.Buzz(20, Haptics.Light);
        yield return Ease.Run(0.12f, t => model.localScale = new Vector3(1 + 0.3f * t, 1 - 0.3f * t, 1 + 0.3f * t));
        PlaySfx(yankClip ? yankClip : powerupClip, 0.8f);
        Haptics.Buzz(40, Haptics.Medium);
        Burst(model.position, 25);
        float trail = 0;
        yield return Ease.Run(0.9f, t =>
        {
            float swirl = t * 9f;
            model.localPosition = new Vector3(Mathf.Cos(swirl) * 0.05f * t, 0.65f * Ease.InQuad(t), Mathf.Sin(swirl) * 0.05f * t);
            model.localScale = Vector3.one * Mathf.Max(0.001f, (1.15f - 0.25f * t) * (t < 0.15f ? 1 : 1 - Ease.InCubic((t - 0.15f) / 0.85f)));
            model.Rotate(0, 900f * Time.deltaTime, 0, Space.Self);
            label.localScale = Vector3.one * Mathf.Max(0.001f, 1 - t * 3);
            if ((trail -= Time.deltaTime) <= 0) { trail = 0.04f; Burst(model.position, 3); }
        });
        Burst(model.position, 15);
        model.localScale = Vector3.one * 0.001f;
        yield return new WaitForSeconds(0.7f);

        // back down from above, landing with a bounce
        yield return Ease.Run(0.6f, t =>
        {
            model.localPosition = new Vector3(0, 0.4f * (1 - Ease.OutBounce(t)), 0);
            model.localScale = Vector3.one * Mathf.Max(0.001f, Ease.OutBack(Mathf.Min(1, t * 2.5f), 1.6f));
            label.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Clamp01(t * 2 - 1));
        });
        model.localPosition = Vector3.zero;
        model.localScale = Vector3.one;
        label.localScale = Vector3.one;
        Burst(model.position, 20);
        PlaySfx(sparkleClip, 0.6f);
        skillFlying[i] = false;
    }

    // ---------- Skill models (about 0.05 across, centred, built in code) ----------

    // A themed model for each skill, picked from its name; anything else gets a glowing crystal.
    Transform SkillModel(string skill, Transform parent)
    {
        var root = new GameObject("Model").transform;
        root.SetParent(parent, false);
        string n = " " + skill.ToLowerInvariant() + " ";
        bool Has(params string[] words) => System.Array.Exists(words, w => n.Contains(w));
        if (Has("unity", "game")) Controller(root);
        else if (Has(" ar ", "vuforia", " xr", " vr ")) Hologram(root);
        else if (Has("c#", "code", "program", "python", "java", "c++")) CodeBrackets(root);
        else if (Has("3d", "anim", "model", "blender", "maya")) WireGem(root);
        else if (Has(" ui", "design", " ux", "figma")) Palette(root);
        else if (Has("web", "html", "site")) Browser(root);
        else Crystal(root);
        return root;
    }

    // a cube between two points, `w` wide and `d` deep
    Transform Bar(Transform parent, Vector3 a, Vector3 b, float w, float d, Material mat)
    {
        var bar = Prim(PrimitiveType.Cube, "Bar", parent, (a + b) / 2, new Vector3(w, Vector3.Distance(a, b), d), mat);
        bar.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        return bar;
    }

    void Controller(Transform r)
    {
        PanelObject("Body", r, Vector3.zero, 0.014f, Lit(Navy)).sharedMesh = Billboard.PanelMesh(0.052f, 0.026f, 0.013f);
        foreach (float side in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Sphere, "Grip", r, new Vector3(side * 0.02f, -0.011f, 0), new Vector3(0.019f, 0.022f, 0.015f), Lit(Navy));
            var stick = Prim(PrimitiveType.Cylinder, "Stick", r, new Vector3(side * 0.007f, -0.006f, -0.008f), new Vector3(0.007f, 0.0015f, 0.007f), Lit(Slate));
            stick.localRotation = Quaternion.Euler(90, 0, 0);
        }
        Prim(PrimitiveType.Cube, "Pad", r, new Vector3(-0.015f, 0.003f, -0.0075f), new Vector3(0.012f, 0.0038f, 0.003f), Lit(Cream));
        Prim(PrimitiveType.Cube, "Pad", r, new Vector3(-0.015f, 0.003f, -0.0075f), new Vector3(0.0038f, 0.012f, 0.003f), Lit(Cream));
        Color[] buttons = { Orange, Cream, Orange, Cream };
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI / 2;
            Prim(PrimitiveType.Sphere, "Button", r, new Vector3(0.015f + Mathf.Cos(a) * 0.0045f, 0.003f + Mathf.Sin(a) * 0.0045f, -0.007f), Vector3.one * 0.0042f, Lit(buttons[k], 0.6f));
        }
        Prim(PrimitiveType.Sphere, "Light", r, new Vector3(0, 0.008f, -0.0072f), new Vector3(0.006f, 0.002f, 0.001f), Lit(Orange, 1f));
    }

    void Hologram(Transform r)
    {
        Prim(PrimitiveType.Cylinder, "Projector", r, new Vector3(0, -0.024f, 0), new Vector3(0.036f, 0.003f, 0.036f), Lit(Navy));
        Prim(PrimitiveType.Cylinder, "Lens", r, new Vector3(0, -0.0205f, 0), new Vector3(0.012f, 0.0012f, 0.012f), Lit(Orange, 1f));
        var cube = new GameObject("Holo Cube").transform;
        cube.SetParent(r, false);
        cube.localPosition = new Vector3(0, 0.006f, 0);
        cube.localRotation = Quaternion.Euler(35, 45, 0); // stood on a corner
        const float h = 0.013f;
        var edge = Lit(Orange, 0.9f);
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                Bar(cube, new Vector3(x * h, y * h, -h), new Vector3(x * h, y * h, h), 0.0016f, 0.0016f, edge);
                Bar(cube, new Vector3(x * h, -h, y * h), new Vector3(x * h, h, y * h), 0.0016f, 0.0016f, edge);
                Bar(cube, new Vector3(-h, x * h, y * h), new Vector3(h, x * h, y * h), 0.0016f, 0.0016f, edge);
                foreach (float z in new[] { -h, h })
                    Prim(PrimitiveType.Sphere, "Corner", cube, new Vector3(x * h, y * h, z), Vector3.one * 0.0035f, Lit(Cream, 0.5f));
            }
        Prim(PrimitiveType.Cube, "Core", cube, Vector3.zero, Vector3.one * 0.009f, Lit(Cream, 0.6f));
        for (int k = 0; k < 4; k++) // the light beams up from the lens to the cube
        {
            float a = k * Mathf.PI / 2 + Mathf.PI / 4;
            Bar(r, new Vector3(0, -0.02f, 0), new Vector3(Mathf.Cos(a) * 0.012f, -0.006f, Mathf.Sin(a) * 0.012f), 0.0008f, 0.0008f, Lit(Orange, 0.5f));
        }
    }

    void CodeBrackets(Transform r)
    {
        PanelObject("Plate", r, new Vector3(0, 0, 0.007f), 0.004f, Lit(Navy)).sharedMesh = Billboard.PanelMesh(0.064f, 0.044f, 0.008f);
        var bracket = Lit(Orange, 0.7f);
        foreach (float side in new[] { -1f, 1f })
        {
            var tip = new Vector3(side * 0.025f, 0, 0);
            Bar(r, tip, new Vector3(side * 0.011f, 0.014f, 0), 0.0055f, 0.008f, bracket);
            Bar(r, tip, new Vector3(side * 0.011f, -0.014f, 0), 0.0055f, 0.008f, bracket);
            Prim(PrimitiveType.Sphere, "Joint", r, tip, new Vector3(0.0055f, 0.0055f, 0.008f), bracket);
        }
        Bar(r, new Vector3(-0.006f, -0.017f, 0), new Vector3(0.006f, 0.017f, 0), 0.005f, 0.008f, Lit(Cream, 0.3f));
    }

    void WireGem(Transform r)
    {
        float phi = (1 + Mathf.Sqrt(5)) / 2;
        var verts = new List<Vector3>();
        foreach (int a in new[] { -1, 1 })
            foreach (int b in new[] { -1, 1 })
            {
                verts.Add(new Vector3(0, a, b * phi));
                verts.Add(new Vector3(a, b * phi, 0));
                verts.Add(new Vector3(b * phi, 0, a));
            }
        float scale = 0.022f / verts[0].magnitude;
        Prim(PrimitiveType.Sphere, "Core", r, Vector3.zero, Vector3.one * 0.03f, Lit(Navy));
        var edge = Lit(Orange, 0.8f);
        for (int i = 0; i < verts.Count; i++)
        {
            Prim(PrimitiveType.Sphere, "Vertex", r, verts[i] * scale, Vector3.one * 0.0042f, Lit(Cream, 0.5f));
            for (int j = i + 1; j < verts.Count; j++)
                if (Mathf.Abs(Vector3.Distance(verts[i], verts[j]) - 2) < 0.01f) // neighbours on the icosahedron
                    Bar(r, verts[i] * scale, verts[j] * scale, 0.0014f, 0.0014f, edge);
        }
    }

    void Palette(Transform r)
    {
        var board = Prim(PrimitiveType.Cylinder, "Palette", r, Vector3.zero, new Vector3(0.052f, 0.0025f, 0.04f), Lit(Cream));
        board.localRotation = Quaternion.Euler(90, 0, 0);
        var hole = Prim(PrimitiveType.Cylinder, "Thumb Hole", r, new Vector3(-0.014f, -0.008f, -0.0014f), new Vector3(0.008f, 0.0012f, 0.008f), Lit(Navy));
        hole.localRotation = Quaternion.Euler(90, 0, 0);
        Color[] paints = { Orange, Navy, Slate, new Color(0.3f, 0.75f, 0.45f), new Color(1f, 0.82f, 0.25f) };
        for (int k = 0; k < paints.Length; k++)
        {
            float a = Mathf.Lerp(-0.2f, 2.6f, k / (float)(paints.Length - 1));
            Prim(PrimitiveType.Sphere, "Paint", r, new Vector3(Mathf.Cos(a) * 0.016f + 0.003f, Mathf.Sin(a) * 0.012f, -0.0022f), new Vector3(0.0085f, 0.0075f, 0.003f), Lit(paints[k], 0.2f));
        }
        var brush = new GameObject("Brush").transform;
        brush.SetParent(r, false);
        brush.localPosition = new Vector3(0.006f, -0.004f, -0.006f);
        brush.localRotation = Quaternion.Euler(0, 0, -50);
        Prim(PrimitiveType.Cylinder, "Handle", brush, new Vector3(0, -0.01f, 0), new Vector3(0.004f, 0.016f, 0.004f), Lit(Navy));
        Prim(PrimitiveType.Cylinder, "Ferrule", brush, new Vector3(0, 0.0075f, 0), new Vector3(0.0046f, 0.0025f, 0.0046f), Lit(Slate));
        Prim(PrimitiveType.Sphere, "Bristles", brush, new Vector3(0, 0.0125f, 0), new Vector3(0.0048f, 0.009f, 0.0048f), Lit(Orange, 0.4f));
    }

    void Browser(Transform r)
    {
        PanelObject("Window", r, Vector3.zero, 0.006f, Lit(Cream)).sharedMesh = Billboard.PanelMesh(0.058f, 0.044f, 0.005f);
        PanelObject("Top Bar", r, new Vector3(0, 0.0165f, -0.0032f), 0.0015f, Lit(Navy)).sharedMesh = Billboard.PanelMesh(0.056f, 0.009f, 0.004f);
        Color[] dots = { Orange, Cream, Slate };
        for (int k = 0; k < 3; k++)
            Prim(PrimitiveType.Sphere, "Dot", r, new Vector3(-0.023f + k * 0.0045f, 0.0165f, -0.0042f), Vector3.one * 0.003f, Lit(dots[k], 0.4f));
        PanelObject("Address", r, new Vector3(0.006f, 0.0165f, -0.0041f), 0.0006f, Lit(Slate)).sharedMesh = Billboard.PanelMesh(0.03f, 0.0045f, 0.00225f);
        PanelObject("Hero", r, new Vector3(0, 0.002f, -0.0034f), 0.001f, Lit(Orange, 0.5f)).sharedMesh = Billboard.PanelMesh(0.048f, 0.014f, 0.003f);
        foreach (var (y, w) in new[] { (-0.0105f, 0.04f), (-0.0145f, 0.03f) })
            PanelObject("Text Line", r, new Vector3(-0.024f + w / 2, y, -0.0033f), 0.0008f, Lit(Slate)).sharedMesh = Billboard.PanelMesh(w, 0.0022f, 0.0011f);
        PanelObject("Button", r, new Vector3(0.018f, -0.0125f, -0.0035f), 0.0012f, Lit(Navy)).sharedMesh = Billboard.PanelMesh(0.012f, 0.005f, 0.0025f);
    }

    void Crystal(Transform r)
    {
        var v = new List<Vector3>();
        var tris = new List<int>();
        Vector3 top = new Vector3(0, 0.028f, 0), bottom = new Vector3(0, -0.022f, 0);
        for (int k = 0; k < 6; k++)
        {
            float a0 = k * Mathf.PI / 3, a1 = (k + 1) * Mathf.PI / 3;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 0.017f, p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 0.017f;
            Tri(v, tris, top, p0, p1, (p0 + p1) / 2 + Vector3.up * 0.01f);
            Tri(v, tris, bottom, p1, p0, (p0 + p1) / 2 + Vector3.down * 0.01f);
        }
        MeshObject("Crystal", r, Vector3.zero, Finish(v, tris, "Crystal"), Lit(Orange, 0.5f));
        Prim(PrimitiveType.Sphere, "Glow", r, new Vector3(0, 0.002f, 0), Vector3.one * 0.012f, Lit(Cream, 0.8f));
    }

    IEnumerator PopItem(Transform block, float size = 0.045f) // size: its resting scale (skill blocks 0.045, contact models 1)
    {
        PlaySfx(popClip);
        Haptics.Buzz(20, Haptics.Light);
        Burst(block.position, 20);
        yield return Ease.Run(0.4f, t => block.localScale = Vector3.one * size * (1 + 0.5f * Mathf.Sin(Mathf.PI * t)));
        block.localScale = Vector3.one * size;
    }

    // ---------- Contact ----------

    void BuildContact(Transform root)
    {
        var entries = new List<(string label, System.Func<Transform, Transform> model, System.Action tap)>
        {
            ("Email", Envelope, () => OpenLink(MailLink)),
            ("Call", Phone, () => OpenLink(PhoneLink)),
            ("Website", Globe, () => OpenLink(WebLink(data.website))),
        };
        if (!string.IsNullOrEmpty(data.linkedin)) entries.Add(("LinkedIn", Globe, () => OpenLink(WebLink(data.linkedin))));
        if (!string.IsNullOrEmpty(data.github)) entries.Add(("GitHub", Globe, () => OpenLink(WebLink(data.github))));

        var row = new GameObject("Row").AddComponent<Billboard>(); // turns to face you, so nothing lines up behind anything
        row.transform.SetParent(root, false);
        row.cam = arCamera;
        contactItems = new Transform[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var (label, model, tap) = entries[i];
            var slot = new GameObject(label).transform;
            slot.SetParent(row.transform, false);
            slot.localPosition = new Vector3((i - (entries.Count - 1) / 2f) * 0.17f, 0, 0);
            var body = model(slot);
            contactItems[i] = body;
            AddTap(body.gameObject, 1f, () => { StartCoroutine(PopItem(body, 1f)); tap(); });
            // the model's root is unscaled, so a size-1 box would cover the whole card: fit it to the model instead
            body.GetComponent<BoxCollider>().size = new Vector3(0.12f, 0.11f, 0.06f);
            var sign = NewSign(slot, label, 0.024f, Cream, Navy);
            sign.face = false;
            sign.transform.localPosition = new Vector3(0, -0.085f, -0.01f);
        }
        // above the row (below it, it ran into the node underneath): put me straight into your phone's contacts
        var save = NewSign(row.transform, "+ Save my contact", 0.026f, Cream, Orange);
        save.face = false;
        save.transform.localPosition = new Vector3(0, 0.1f, -0.01f);
        AddTap(save.back.gameObject, 1.2f, SaveContact);
    }

    // A real envelope front: the orange top flap folded down to a point, the side and bottom folds in slightly
    // different shades so the creases show, and a navy seal with an @ where the flap closes. Faces -z.
    Transform Envelope(Transform parent)
    {
        var e = new GameObject("Envelope").transform;
        e.SetParent(parent, false);
        const float w = 0.05f, h = 0.034f;
        PanelObject("Body", e, Vector3.zero, 0.008f, Lit(Cream)).sharedMesh = Billboard.PanelMesh(w * 2, h * 2, 0.004f);
        var fold = Lit(Color.Lerp(Cream, Slate, 0.1f));
        MeshObject("Fold Left", e, new Vector3(0, 0, -0.0042f), Prism(0.0008f, new Vector2(-w + 0.002f, h - 0.002f), new Vector2(-w + 0.002f, -h + 0.002f), new Vector2(0, 0)), fold);
        MeshObject("Fold Right", e, new Vector3(0, 0, -0.0042f), Prism(0.0008f, new Vector2(w - 0.002f, -h + 0.002f), new Vector2(w - 0.002f, h - 0.002f), new Vector2(0, 0)), fold);
        MeshObject("Fold Bottom", e, new Vector3(0, 0, -0.0046f), Prism(0.0008f, new Vector2(-w + 0.002f, -h + 0.002f), new Vector2(w - 0.002f, -h + 0.002f), new Vector2(0, 0.006f)), Lit(Color.Lerp(Cream, Slate, 0.18f)));
        MeshObject("Flap", e, new Vector3(0, 0, -0.0052f), Prism(0.0012f, new Vector2(w, h), new Vector2(-w, h), new Vector2(0, -0.004f)), Lit(Orange, 0.3f));
        var seal = Prim(PrimitiveType.Cylinder, "Seal", e, new Vector3(0, -0.003f, -0.0064f), new Vector3(0.017f, 0.0012f, 0.017f), Lit(Navy));
        seal.localRotation = Quaternion.Euler(90, 0, 0);
        Text(e, "@", 0.011f, Cream, new Vector3(0, -0.003f, -0.0074f));
        return e;
    }

    // A modern phone ringing: a rounded navy body in a metal frame with side buttons, and a dark screen with the
    // camera hole, my initials as the caller photo, my name, "calling..." and a green answer button.
    Transform Phone(Transform parent)
    {
        var p = new GameObject("Phone").transform;
        p.SetParent(parent, false);
        PanelObject("Frame", p, new Vector3(0, 0, 0.0006f), 0.008f, Lit(Slate)).sharedMesh = Billboard.PanelMesh(0.052f, 0.094f, 0.01f);
        PanelObject("Body", p, Vector3.zero, 0.009f, Lit(Navy)).sharedMesh = Billboard.PanelMesh(0.05f, 0.092f, 0.009f);
        PanelObject("Screen", p, new Vector3(0, 0, -0.0047f), 0.001f, Lit(Ink, 0.2f)).sharedMesh = Billboard.PanelMesh(0.044f, 0.085f, 0.006f);
        Prim(PrimitiveType.Cube, "Power", p, new Vector3(0.0262f, 0.018f, 0), new Vector3(0.0016f, 0.012f, 0.004f), Lit(Slate));
        Prim(PrimitiveType.Cube, "Volume", p, new Vector3(-0.0262f, 0.024f, 0), new Vector3(0.0016f, 0.018f, 0.004f), Lit(Slate));
        Prim(PrimitiveType.Sphere, "Camera", p, new Vector3(0, 0.037f, -0.0052f), new Vector3(0.0035f, 0.0035f, 0.001f), Lit(Ink));

        const float face = -0.0056f;
        var photo = Prim(PrimitiveType.Cylinder, "Caller Photo", p, new Vector3(0, 0.017f, face), new Vector3(0.019f, 0.0004f, 0.019f), Lit(Orange, 0.6f));
        photo.localRotation = Quaternion.Euler(90, 0, 0);
        var initials = string.Concat(System.Array.ConvertAll(data.name.Split(' '), n => n.Length > 0 ? n.Substring(0, 1) : ""));
        Text(p, initials, 0.0075f, Navy, new Vector3(0, 0.017f, face - 0.0006f));
        Text(p, FirstName, 0.0055f, Cream, new Vector3(0, 0.001f, face - 0.0002f));
        Text(p, "calling...", 0.0036f, Slate, new Vector3(0, -0.006f, face - 0.0002f));

        var answer = Prim(PrimitiveType.Cylinder, "Answer", p, new Vector3(0, -0.026f, face), new Vector3(0.015f, 0.0004f, 0.015f), Lit(new Color(0.25f, 0.78f, 0.38f), 0.5f));
        answer.localRotation = Quaternion.Euler(90, 0, 0);
        // the handset: a bar with an earpiece and a mouthpiece, tipped like the phone icon
        var handset = new GameObject("Handset").transform;
        handset.SetParent(p, false);
        handset.localPosition = new Vector3(0, -0.026f, face - 0.0006f);
        handset.localRotation = Quaternion.Euler(0, 0, 45);
        Prim(PrimitiveType.Cube, "Grip", handset, Vector3.zero, new Vector3(0.0016f, 0.0075f, 0.0006f), Lit(Cream));
        foreach (float end in new[] { -1f, 1f })
            Prim(PrimitiveType.Cube, "Cup", handset, new Vector3(0.0012f, end * 0.0038f, 0), new Vector3(0.0036f, 0.0022f, 0.0006f), Lit(Cream));
        return p;
    }

    // The web: a glossy navy globe with glowing latitude and longitude lines, a tilted orbit ring with a little
    // satellite, and a mouse pointer clicking on it.
    Transform Globe(Transform parent)
    {
        var g = new GameObject("Globe").transform;
        g.SetParent(parent, false);
        const float r = 0.0425f, line = 0.0011f;
        Prim(PrimitiveType.Sphere, "Ball", g, Vector3.zero, Vector3.one * r * 2, Lit(Navy));
        var grid = Lit(Orange, 0.6f);
        MeshObject("Equator", g, Vector3.zero, Torus(r + 0.0004f, line), grid);
        foreach (float y in new[] { -0.022f, 0.022f })
            MeshObject("Latitude", g, new Vector3(0, y, 0), Torus(Mathf.Sqrt(r * r - y * y) + 0.0004f, line), grid);
        for (int k = 0; k < 3; k++)
            MeshObject("Meridian", g, Vector3.zero, Torus(r + 0.0004f, line), grid).localRotation = Quaternion.Euler(0, k * 60f, 90);

        var orbit = MeshObject("Orbit", g, Vector3.zero, Torus(0.058f, 0.0012f), Lit(Cream, 0.2f));
        orbit.localRotation = Quaternion.Euler(72, 0, 18);
        Prim(PrimitiveType.Sphere, "Satellite", orbit, new Vector3(0.058f, 0, 0), Vector3.one * 0.007f, Lit(Orange, 0.8f));

        // the pointer: an arrowhead and its tail, cream with a navy edge, in front of the globe's lower right
        var cursor = new GameObject("Pointer").transform;
        cursor.SetParent(g, false);
        cursor.localPosition = new Vector3(0.014f, -0.008f, -0.047f);
        cursor.localRotation = Quaternion.Euler(0, 0, 18);
        var head = Prism(0.003f, new Vector2(0, 0), new Vector2(0, -0.022f), new Vector2(0.016f, -0.016f));
        var tail = Prism(0.003f, new Vector2(-0.0024f, 0), new Vector2(-0.0024f, -0.012f), new Vector2(0.0024f, -0.012f), new Vector2(0.0024f, 0));
        MeshObject("Edge", cursor, new Vector3(-0.0015f, 0.002f, 0.0015f), head, Lit(Navy)).localScale = Vector3.one * 1.25f;
        MeshObject("Edge", cursor, new Vector3(0.0075f, -0.0155f, 0.0015f), tail, Lit(Navy)).localRotation = Quaternion.Euler(0, 0, 33);
        MeshObject("Arrow", cursor, Vector3.zero, head, Lit(Cream));
        var t = MeshObject("Arrow", cursor, new Vector3(0.0075f, -0.0155f, -0.0003f), tail, Lit(Cream));
        t.localRotation = Quaternion.Euler(0, 0, 33);
        t.localScale = new Vector3(0.75f, 0.85f, 1);
        return g;
    }

    Transform MeshObject(string name, Transform parent, Vector3 position, Mesh mesh, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // Adds a triangle turned so it faces `outward` (front faces are what Unity draws).
    static void Tri(List<Vector3> v, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        int i = v.Count;
        bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0;
        v.Add(a); v.Add(flip ? c : b); v.Add(flip ? b : c);
        tris.AddRange(new[] { i, i + 1, i + 2 });
    }

    static Mesh Finish(List<Vector3> v, List<int> tris, string name)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    // A flat convex shape (corners in order) extruded `depth`, facing -z, with crisp edges.
    static Mesh Prism(float depth, params Vector2[] corners)
    {
        var v = new List<Vector3>();
        var tris = new List<int>();
        Vector3 F(int i) => new Vector3(corners[i].x, corners[i].y, -depth / 2);
        Vector3 B(int i) => new Vector3(corners[i].x, corners[i].y, depth / 2);
        var mid = Vector2.zero;
        foreach (var c in corners) mid += c / corners.Length;
        for (int i = 1; i + 1 < corners.Length; i++)
        {
            Tri(v, tris, F(0), F(i), F(i + 1), Vector3.back);
            Tri(v, tris, B(0), B(i), B(i + 1), Vector3.forward);
        }
        for (int i = 0; i < corners.Length; i++)
        {
            int j = (i + 1) % corners.Length;
            var edgeMid = (corners[i] + corners[j]) / 2;
            var outward = (Vector3)(edgeMid - mid);
            Tri(v, tris, F(i), F(j), B(j), outward);
            Tri(v, tris, F(i), B(j), B(i), outward);
        }
        return Finish(v, tris, "Prism");
    }

    // A ring of radius `radius` round the y axis, made of a tube `thickness` across: smooth, for glowing lines.
    static Mesh Torus(float radius, float thickness)
    {
        const int around = 48, tube = 8;
        float t = thickness / 2;
        Vector3 P(int u, int k)
        {
            float a = u * Mathf.PI * 2 / around, b = k * Mathf.PI * 2 / tube;
            return new Vector3((radius + t * Mathf.Cos(b)) * Mathf.Cos(a), t * Mathf.Sin(b), (radius + t * Mathf.Cos(b)) * Mathf.Sin(a));
        }
        var verts = new List<Vector3>();
        var idx = new List<int>();
        for (int u = 0; u <= around; u++)
            for (int k = 0; k <= tube; k++) verts.Add(P(u, k));
        for (int u = 0; u < around; u++)
            for (int k = 0; k < tube; k++)
            {
                int i0 = u * (tube + 1) + k, i1 = i0 + tube + 1;
                float a = (u + 0.5f) * Mathf.PI * 2 / around;
                var centre = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                var outward = (verts[i0] + verts[i1 + 1]) / 2 - centre;
                // shared vertices keep the tube smooth: pick the winding that faces out
                bool flip = Vector3.Dot(Vector3.Cross(verts[i1] - verts[i0], verts[i0 + 1] - verts[i0]), outward) < 0;
                if (!flip) idx.AddRange(new[] { i0, i1, i0 + 1, i0 + 1, i1, i1 + 1 });
                else idx.AddRange(new[] { i0, i0 + 1, i1, i0 + 1, i1 + 1, i1 });
            }
        var mesh = new Mesh { name = "Torus" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(idx, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    // ---------- Building blocks ----------

    Material Lit(Color colour, float glow = 0f)
    {
        var key = colour * (1 + glow); // glowing and plain versions of one colour are different materials
        if (litMats.TryGetValue(key, out var cached)) return cached;
        var m = new Material(Shader.Find("Standard")) { color = colour };
        m.SetFloat("_Glossiness", 0.45f);
        if (glow > 0)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", colour * glow);
        }
        return litMats[key] = m;
    }

    Transform Prim(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        Destroy(go.GetComponent<Collider>()); // only the things AddTap names are tappable
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // A collider `grow` times the object's size (a fat finger target) that calls `action` when tapped.
    void AddTap(GameObject go, float grow, System.Action action)
    {
        var col = go.AddComponent<BoxCollider>();
        col.size = Vector3.one * grow;
        go.AddComponent<Tappable>().onTap = action;
    }

    TextMesh Text(Transform parent, string s, float height, Color colour, Vector3 position)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = s;
        tm.fontSize = 64;
        tm.characterSize = height * TextScale * 10f / 64f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = colour;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return tm;
    }

    // A label on a rounded-off slab, always turned to the camera.
    Billboard NewSign(Transform parent, string s, float height, Color textColour, Color backColour)
    {
        var sign = new GameObject("Sign " + s).AddComponent<Billboard>();
        sign.transform.SetParent(parent, false);
        sign.cam = arCamera;
        // a pill with a soft bevel and a thin glowing outline (orange round navy, a light edge round anything else)
        var back = PanelObject("Back", sign.transform, Vector3.zero, 0.008f, Lit(backColour));
        Color rimColour = backColour == Navy ? Orange : Color.Lerp(backColour, Color.white, 0.5f);
        var rim = PanelObject("Rim", back.transform, new Vector3(0, 0, 0.25f), 1f, Lit(rimColour, 0.5f));
        sign.back = back.transform;
        sign.back.localScale = new Vector3(0, 0, 0.008f); // sized to the text by the Billboard
        sign.panels = new[] { back, rim };
        sign.margins = new[] { 0f, Mathf.Clamp(height * 0.08f, 0.0012f, 0.003f) };
        sign.text = Text(sign.transform, s, height, textColour, new Vector3(0, 0, -0.0065f));
        sign.pad = new Vector2(height * 0.8f, height * 0.45f);
        return sign;
    }

    // An empty rounded-panel object (its mesh comes from Billboard.PanelMesh), `depth` thick.
    MeshFilter PanelObject(string name, Transform parent, Vector3 position, float depth, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = new Vector3(1, 1, depth);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.GetComponent<MeshFilter>();
    }

    // A floating board `size` big (its face at z -0.007, toward the viewer): a soft drop shadow, a glowing
    // orange rim, the navy slab with a slightly lighter inset face, and a pill accent bar along the top.
    void Board(Transform parent, Vector2 size)
    {
        float w = size.x, h = size.y, r = 0.028f;
        PanelObject("Shadow", parent, new Vector3(0.004f, -0.008f, 0.012f), 0.004f, Lit(Ink)).sharedMesh = Billboard.PanelMesh(w + 0.01f, h + 0.01f, r + 0.005f);
        PanelObject("Rim", parent, new Vector3(0, 0, 0.002f), 0.014f, Lit(Orange, 0.7f)).sharedMesh = Billboard.PanelMesh(w + 0.007f, h + 0.007f, r + 0.0035f);
        PanelObject("Slab", parent, Vector3.zero, 0.014f, Lit(Navy)).sharedMesh = Billboard.PanelMesh(w, h, r);
        PanelObject("Face", parent, new Vector3(0, -0.008f, -0.0072f), 0.001f, Lit(Color.Lerp(Navy, Slate, 0.22f))).sharedMesh = Billboard.PanelMesh(w - 0.024f, h - 0.04f, r - 0.012f);
        PanelObject("Accent", parent, new Vector3(0, h / 2 - 0.013f, -0.0078f), 0.002f, Lit(Orange, 0.8f)).sharedMesh = Billboard.PanelMesh(w * 0.35f, 0.007f, 0.0035f);
    }

    static string Wrap(string s, int max)
    {
        var words = s.Split(' ');
        var sb = new System.Text.StringBuilder();
        int line = 0;
        foreach (var w in words)
        {
            if (line > 0 && line + 1 + w.Length > max) { sb.Append('\n'); line = 0; }
            else if (line > 0) { sb.Append(' '); line++; }
            sb.Append(w);
            line += w.Length;
        }
        return sb.ToString();
    }
}
