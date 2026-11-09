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
                skillItems[i].GetChild(0).Rotate(0, 60 * dt, 0, Space.Self);
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
            var cube = Prim(PrimitiveType.Cube, "Block", item, Vector3.zero, Vector3.one * 0.045f, Lit(i % 2 == 0 ? Orange : Navy, 0.3f));
            Prim(PrimitiveType.Cube, "Edge", cube, Vector3.zero, Vector3.one * 1.001f, Lit(i % 2 == 0 ? Navy : Orange, 0f)).localScale = new Vector3(0.45f, 1.08f, 1.08f);
            AddTap(cube.gameObject, 1.9f, () => StartCoroutine(PopItem(cube)));
            var label = NewSign(item, data.skills[i], 0.02f, Cream, Navy);
            label.transform.localPosition = new Vector3(0, 0.045f, 0);
            skillItems[i] = item;
        }
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
    }

    Transform Envelope(Transform parent)
    {
        var e = new GameObject("Envelope").transform;
        e.SetParent(parent, false);
        Prim(PrimitiveType.Cube, "Body", e, Vector3.zero, new Vector3(0.1f, 0.068f, 0.012f), Lit(Cream));
        foreach (float side in new[] { -1f, 1f })
        {
            var flap = Prim(PrimitiveType.Cube, "Flap", e, new Vector3(side * 0.024f, 0.014f, -0.0075f), new Vector3(0.062f, 0.007f, 0.004f), Lit(Orange, 0.6f));
            flap.localRotation = Quaternion.Euler(0, 0, side * 30f); // a V hanging from the top edge, like a real envelope flap
        }
        return e;
    }

    Transform Phone(Transform parent)
    {
        var p = new GameObject("Phone").transform;
        p.SetParent(parent, false);
        Prim(PrimitiveType.Cube, "Body", p, Vector3.zero, new Vector3(0.05f, 0.092f, 0.01f), Lit(Navy));
        Prim(PrimitiveType.Cube, "Screen", p, new Vector3(0, 0.003f, -0.0055f), new Vector3(0.042f, 0.076f, 0.002f), Lit(Orange, 0.8f));
        Prim(PrimitiveType.Sphere, "Button", p, new Vector3(0, -0.038f, -0.0065f), Vector3.one * 0.006f, Lit(Cream));
        return p;
    }

    Transform Globe(Transform parent)
    {
        var g = new GameObject("Globe").transform;
        g.SetParent(parent, false);
        Prim(PrimitiveType.Sphere, "Ball", g, Vector3.zero, Vector3.one * 0.085f, Lit(Navy));
        Prim(PrimitiveType.Sphere, "Equator", g, Vector3.zero, new Vector3(0.0875f, 0.006f, 0.0875f), Lit(Orange, 0.6f));
        Prim(PrimitiveType.Sphere, "Meridian", g, Vector3.zero, new Vector3(0.006f, 0.0875f, 0.0875f), Lit(Orange, 0.6f));
        return g;
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
