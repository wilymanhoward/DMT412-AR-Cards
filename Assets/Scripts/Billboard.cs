using UnityEngine;

// Turns its object to face the camera (screen-up stays up). With `text` and `back` set it also sizes the
// backdrop to the text once the text's mesh exists (it's built a frame after the text is set).
public class Billboard : MonoBehaviour
{
    public Camera cam;
    public bool face = true;
    public TextMesh text;
    public Transform back;
    public Vector2 pad;
    // Rounded backdrops (the back and its outline): rebuilt to fit the text, each `margins[i]` bigger all round.
    // Their meshes are in the back's scaled space, so the back's scale (and the tap collider on it) work as before.
    public MeshFilter[] panels;
    public float[] margins;
    bool fitted;

    public void SetText(string s)
    {
        text.text = s;
        fitted = false;
    }

    void LateUpdate()
    {
        if (face && cam)
        {
            // turns around the card's up axis only, so it always stands upright whatever the camera angle
            Vector3 up = transform.parent ? transform.parent.up : Vector3.up;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - cam.transform.position, up);
            if (away.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(away, up);
        }
        if (!fitted && text) Fit();
    }

    void Fit()
    {
        var b = text.GetComponent<MeshRenderer>().localBounds;
        if (b.size.x < 0.0001f) return;
        fitted = true;
        if (!back) return;
        var centre = text.transform.localPosition + b.center;
        back.localPosition = new Vector3(centre.x, centre.y, back.localPosition.z);
        float w = b.size.x + pad.x * 2, h = b.size.y + pad.y * 2;
        back.localScale = new Vector3(w, h, back.localScale.z);
        if (panels == null) return;
        for (int i = 0; i < panels.Length; i++)
        {
            float pw = w + margins[i] * 2, ph = h + margins[i] * 2;
            if (panels[i].sharedMesh) Destroy(panels[i].sharedMesh);
            panels[i].sharedMesh = PanelMesh(pw, ph, ph / 2, w, h); // a pill
        }
    }

    // A rounded slab `w` x `h` with corner radius `radius` and a soft bevel on its front edge, 1 deep (scale z
    // for the thickness), facing -z. Vertices are divided by `divX`/`divY` (pass the parent's scale to build it in
    // a scaled space; 1 for real size).
    public static Mesh PanelMesh(float w, float h, float radius, float divX = 1, float divY = 1)
    {
        const int seg = 8;
        float r = Mathf.Min(radius, w / 2, h / 2), bevel = Mathf.Min(r * 0.4f, 0.003f);
        var ring = new Vector2[4 * (seg + 1)];
        var inset = new Vector2[ring.Length];
        for (int c = 0, k = 0; c < 4; c++)
        {
            var centre = new Vector2((c == 0 || c == 3 ? 1 : -1) * (w / 2 - r), (c < 2 ? 1 : -1) * (h / 2 - r));
            for (int i = 0; i <= seg; i++, k++)
            {
                float a = (c * 90 + 90f * i / seg) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); // counter-clockwise, starting at the top right
                ring[k] = centre + dir * r;
                inset[k] = centre + dir * Mathf.Max(0, r - bevel);
            }
        }
        int n = ring.Length;
        var v = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        Vector3 P(Vector2 q, float z) => new Vector3(q.x / divX, q.y / divY, z);
        // front face (flat), then the bevel and the side (smooth), then the back
        int front = v.Count;
        v.Add(new Vector3(0, 0, -0.5f));
        foreach (var q in inset) v.Add(P(q, -0.5f));
        int a0 = v.Count; foreach (var q in inset) v.Add(P(q, -0.5f));
        int b0 = v.Count; foreach (var q in ring) v.Add(P(q, -0.25f));
        int c0 = v.Count; foreach (var q in ring) v.Add(P(q, 0.5f));
        int back = v.Count;
        v.Add(new Vector3(0, 0, 0.5f));
        foreach (var q in ring) v.Add(P(q, 0.5f));
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            tris.AddRange(new[] { front, front + 1 + j, front + 1 + i });
            tris.AddRange(new[] { a0 + i, a0 + j, b0 + j, a0 + i, b0 + j, b0 + i });
            tris.AddRange(new[] { b0 + i, b0 + j, c0 + j, b0 + i, c0 + j, c0 + i });
            tris.AddRange(new[] { back, back + 1 + i, back + 1 + j });
        }
        var mesh = new Mesh { name = "Panel" };
        mesh.SetVertices(v);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
