using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// What a node opens: a small 3D scene that grows out above it and stays on the card, built here from primitives.
//   About     a name board with Howard's tagline and interests
//   Skills    skill blocks orbiting the node; tap one to pop it
//   Portfolio takes over the whole card as a gallery (BusinessCardController.Gallery.cs)
//   Contact   an envelope, a phone and a globe; tap one to use it
// Words and pictures come from business_card.json. Sizes are in card units (the card is 1 wide, 0.6 deep).
public partial class BusinessCardController
{
    const float TextScale = 1f; // multiplies every 3D text size; the one number to change if the words look too big or small
    const float StageHeight = 0.19f;

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
            ui.tags[i] = NewSign(content, hubs[i].title, 0.03f, Cream, Navy).gameObject;
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
            ui.tags[i].transform.localPosition = hubs[i].node.localPosition + Vector3.up * 0.05f;
            ui.tags[i].transform.localScale = Vector3.one * hubs[i].pop * (selected == i ? 0 : 1);
        }
        if (openStage < 0) return;

        if (openStage == 1)
            for (int i = 0; i < skillItems.Length; i++)
            {
                float a = t * 0.7f + i * Mathf.PI * 2 / skillItems.Length;
                skillItems[i].localPosition = new Vector3(Mathf.Cos(a) * 0.14f, 0.015f * Mathf.Sin(t * 2 + i), Mathf.Sin(a) * 0.1f);
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
        Prim(PrimitiveType.Cube, "Slab", tb, Vector3.zero, new Vector3(0.38f, 0.27f, 0.014f), Lit(Navy));
        Prim(PrimitiveType.Cube, "Accent", tb, new Vector3(0, 0.125f, -0.0085f), new Vector3(0.38f, 0.016f, 0.004f), Lit(Orange, 0.6f));
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
            var label = NewSign(item, data.skills[i], 0.024f, Cream, Navy);
            label.transform.localPosition = new Vector3(0, 0.05f, 0);
            skillItems[i] = item;
        }
    }

    IEnumerator PopItem(Transform block)
    {
        PlaySfx(popClip);
        Haptics.Buzz(20, Haptics.Light);
        Burst(block.position, 20);
        yield return Ease.Run(0.4f, t => block.localScale = Vector3.one * 0.045f * (1 + 0.5f * Mathf.Sin(Mathf.PI * t)));
        block.localScale = Vector3.one * 0.045f;
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

        contactItems = new Transform[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var (label, model, tap) = entries[i];
            var slot = new GameObject(label).transform;
            slot.SetParent(root, false);
            slot.localPosition = new Vector3((i - (entries.Count - 1) / 2f) * 0.16f, 0, 0);
            var body = model(slot);
            contactItems[i] = body;
            AddTap(body.gameObject, 1f, () => { StartCoroutine(PopItem(body)); tap(); });
            var sign = NewSign(slot, label, 0.026f, Cream, Navy);
            sign.transform.localPosition = new Vector3(0, -0.07f, 0);
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
            flap.localRotation = Quaternion.Euler(0, 0, -side * 30f);
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
        sign.back = Prim(PrimitiveType.Cube, "Back", sign.transform, Vector3.zero, new Vector3(0, 0, 0.008f), Lit(backColour));
        sign.text = Text(sign.transform, s, height, textColour, new Vector3(0, 0, -0.0065f));
        sign.pad = new Vector2(height * 0.7f, height * 0.4f);
        return sign;
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
