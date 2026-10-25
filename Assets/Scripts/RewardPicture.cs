using UnityEngine;

// Takes a little picture of a 3D prop for the recap tiles. A temporary camera and light on an otherwise
// unused layer, 3 km below the scene, render a copy of the prop into a RenderTexture; then everything
// temporary is destroyed on the spot, so nothing is left in the AR scene (the light only touches that layer).
public static class RewardPicture
{
    const int Layer = 31;
    static readonly Vector3 Far = new Vector3(0, -3000, 0);

    // `background` is opaque and should match whatever the picture sits on, so edges blend in.
    // The caller owns the texture and should Release() it when the picture is gone.
    public static RenderTexture Render(GameObject prefab, Color background, int size = 256)
    {
        var rig = new GameObject("Picture Rig");
        rig.transform.position = Far;
        // the prop keeps the way it faces in the house, turned a little so it reads as 3D
        var model = Object.Instantiate(prefab, Far, Quaternion.Euler(0, 25, 0) * prefab.transform.rotation, rig.transform);
        model.SetActive(true);
        foreach (var t in rig.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;

        Bounds bounds = default;
        bool first = true;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }
        if (first) bounds = new Bounds(Far, Vector3.one * 0.1f);

        var camGo = new GameObject("Picture Camera");
        camGo.transform.SetParent(rig.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false; // rendered by hand, once
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        cam.cullingMask = 1 << Layer;
        cam.allowHDR = false;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 2f + bounds.extents.magnitude + 1f;
        cam.transform.position = bounds.center + new Vector3(0, 0.35f, -1f).normalized * 2f;
        cam.transform.LookAt(bounds.center);

        // frame it tightly: project the bounding box into the camera's view, centre that, and fit it with a margin
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 view = cam.transform.InverseTransformPoint(corner);
            minX = Mathf.Min(minX, view.x); maxX = Mathf.Max(maxX, view.x);
            minY = Mathf.Min(minY, view.y); maxY = Mathf.Max(maxY, view.y);
        }
        cam.transform.position += cam.transform.right * ((minX + maxX) / 2) + cam.transform.up * ((minY + maxY) / 2);
        cam.orthographicSize = Mathf.Max(maxX - minX, maxY - minY) / 2 * 1.12f;

        // a key light from the camera's side and a weaker fill from the other, so the shadow side isn't murky
        AddLight(rig.transform, Quaternion.Euler(40, 150, 0), 1.0f);
        AddLight(rig.transform, Quaternion.Euler(25, -40, 0), 0.45f);

        var texture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        texture.Create();
        cam.targetTexture = texture;
        cam.Render();
        cam.targetTexture = null;
        Object.DestroyImmediate(rig);
        return texture;
    }

    static void AddLight(Transform parent, Quaternion rotation, float intensity)
    {
        var go = new GameObject("Picture Light");
        go.transform.SetParent(parent, false);
        go.transform.rotation = rotation;
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        light.cullingMask = 1 << Layer; // only lights the picture, never the AR scene
    }
}
