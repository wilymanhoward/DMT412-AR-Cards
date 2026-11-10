using System.Collections.Generic;
using UnityEngine;

// Makes the legs of a static (unrigged) animal mesh gallop, by moving its vertices: the sleigh's reindeer are one
// mesh with no skeleton. At start it finds the legs (thin, separate pieces reaching the bottom of the mesh, below a
// cut line it searches for), then swings each one from its hip around the side-to-side axis. Front and back
// alternate and each animal is a little out of step. Finds nothing leg-like → leaves the mesh untouched.
[RequireComponent(typeof(MeshFilter))]
public class LegRunner : MonoBehaviour
{
    public float speed = 11f;   // strides per second × 2π
    public float swing = 30f;   // degrees each way

    class Leg { public int[] verts; public Vector3 pivot; public float length, phase; }

    Mesh mesh;
    Vector3[] rest, work;
    Leg[] legs;
    Vector3 side;

    // Attaches to the mesh under `model` and gallops along `forward` (a world direction). Returns null when no legs.
    public static LegRunner Attach(MeshFilter model, Vector3 forward, Vector3 up)
    {
        var runner = model.gameObject.AddComponent<LegRunner>();
        if (runner.Setup(model, forward, up)) return runner;
        Destroy(runner);
        return null;
    }

    bool Setup(MeshFilter mf, Vector3 forwardWorld, Vector3 upWorld)
    {
        var source = mf.sharedMesh;
        if (!source || !source.isReadable) { Debug.LogWarning("LegRunner: mesh not readable"); return false; }
        Vector3 up = transform.InverseTransformDirection(upWorld).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(transform.InverseTransformDirection(forwardWorld), up).normalized;
        side = Vector3.Cross(up, fwd).normalized;
        rest = source.vertices;
        var tris = source.triangles;

        // height along `up`, and the welded vertex ids (split vertices share a position)
        var h = new float[rest.Length];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < rest.Length; i++) { h[i] = Vector3.Dot(rest[i], up); lo = Mathf.Min(lo, h[i]); hi = Mathf.Max(hi, h[i]); }
        var weld = new int[rest.Length];
        var seen = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < rest.Length; i++)
        {
            var key = Vector3Int.RoundToInt(rest[i] * 10000f);
            if (!seen.TryGetValue(key, out int first)) seen[key] = first = i;
            weld[i] = first;
        }

        // try cut lines from low to high; keep the one that finds the most leg-shaped pieces
        List<Leg> best = null;
        for (float frac = 0.12f; frac <= 0.46f; frac += 0.02f)
        {
            var found = FindLegs(lo + frac * (hi - lo), lo, hi, h, weld, tris, fwd);
            if (best == null || found.Count > best.Count) best = found;
        }
        if (best == null || best.Count < 2) { Debug.LogWarning("LegRunner: no legs found"); return false; }

        // phases from where each leg stands: legs one row apart (front and back of an animal) are half a stride
        // apart, left and right a little apart, and each animal (by its side position) a bit out of step
        best.Sort((a, b) => Vector3.Dot(a.pivot, fwd).CompareTo(Vector3.Dot(b.pivot, fwd)));
        var rows = new List<float>();
        foreach (var leg in best)
        {
            float f = Vector3.Dot(leg.pivot, fwd);
            if (rows.Count == 0 || f - rows[rows.Count - 1] > 0.1f * (hi - lo)) rows.Add(f);
        }
        float gap = rows.Count > 1 ? (rows[rows.Count - 1] - rows[0]) / (rows.Count - 1) : 1f;
        foreach (var leg in best)
        {
            float f = Vector3.Dot(leg.pivot, fwd), s = Vector3.Dot(leg.pivot, side);
            leg.phase = f / gap * Mathf.PI + Mathf.Sign(s) * 0.5f + Mathf.Abs(s) / gap * 1.3f;
        }
        legs = best.ToArray();

        mesh = Instantiate(source);
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;
        work = (Vector3[])rest.Clone();
        Debug.Log($"LegRunner: {legs.Length} legs on {name}");
        return true;
    }

    List<Leg> FindLegs(float cut, float lo, float hi, float[] h, int[] weld, int[] tris, Vector3 fwd)
    {
        // union-find over welded vertices below the cut, joined by triangle edges that stay below it
        var parent = new Dictionary<int, int>();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        void Add(int x) { if (!parent.ContainsKey(x)) parent[x] = x; }
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
            if (h[a] >= cut || h[b] >= cut || h[c] >= cut) continue;
            int wa = weld[a], wb = weld[b], wc = weld[c];
            Add(wa); Add(wb); Add(wc);
            parent[Find(wa)] = Find(wb);
            parent[Find(wb)] = Find(wc);
        }
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < weld.Length; i++)
        {
            if (h[i] >= cut || !parent.ContainsKey(weld[i])) continue;
            int r = Find(weld[i]);
            if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<int>();
            list.Add(i);
        }

        var legs = new List<Leg>();
        float height = hi - lo;
        foreach (var g in groups.Values)
        {
            if (g.Count < 6) continue;
            float gLo = float.MaxValue, gHi = float.MinValue;
            var min = Vector3.one * float.MaxValue;
            var max = Vector3.one * float.MinValue;
            var topSum = Vector3.zero;
            int topN = 0;
            foreach (int i in g) { gLo = Mathf.Min(gLo, h[i]); gHi = Mathf.Max(gHi, h[i]); min = Vector3.Min(min, rest[i]); max = Vector3.Max(max, rest[i]); }
            foreach (int i in g) if (h[i] > gHi - 0.15f * (gHi - gLo)) { topSum += rest[i]; topN++; }
            float tall = gHi - gLo;
            var size = max - min;
            float wide = Mathf.Max(Mathf.Abs(Vector3.Dot(size, side)), Mathf.Abs(Vector3.Dot(size, fwd)));
            // a leg: reaches the bottom, is clearly taller than it is wide, and isn't a big slab
            if (gLo > lo + 0.06f * height || tall < 1.4f * wide || tall < 0.08f * height) continue;
            legs.Add(new Leg { verts = g.ToArray(), pivot = topSum / Mathf.Max(1, topN), length = tall });
        }
        return legs;
    }

    void LateUpdate()
    {
        if (legs == null) return;
        float t = Time.time * speed;
        System.Array.Copy(rest, work, rest.Length);
        foreach (var leg in legs)
        {
            float angle = Mathf.Sin(t + leg.phase) * swing;
            foreach (int i in leg.verts)
            {
                // bends most at the hoof, eases to nothing at the hip, so the leg doesn't tear from the body
                float k = Mathf.Clamp01(Vector3.Distance(rest[i], leg.pivot) / (0.35f * leg.length));
                work[i] = leg.pivot + Quaternion.AngleAxis(angle * k, side) * (rest[i] - leg.pivot);
            }
        }
        mesh.vertices = work;
        mesh.RecalculateBounds();
    }
}
