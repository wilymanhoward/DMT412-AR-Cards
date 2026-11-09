using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Saves the hand-tuned layout of the Christmas card (every object's position, rotation, scale and on/off under
// "Greeting Content" and "Greeting Templates") to Assets/Editor/ChristmasLayout.json, and puts it back.
// The greeting card builder restores it at the end of every build, so a rebuild can't wipe the manual layout.
// Objects are matched by their path in the hierarchy; ones that no longer exist are skipped and reported.
// shortcut: only transforms and active flags are kept, not component settings; save again after editing those by hand.
public static class ChristmasLayout
{
    const string FilePath = "Assets/Editor/ChristmasLayout.json";
    static readonly string[] Roots = { "Greeting Content", "Greeting Templates" };

    [System.Serializable] class Entry { public string path; public bool active; public Vector3 position, scale; public Quaternion rotation; }
    [System.Serializable] class Snapshot { public string saved; public List<Entry> entries = new List<Entry>(); }

    [MenuItem("Tools/Christmas Card/3. Save House Layout")]
    public static void Save()
    {
        var snap = new Snapshot { saved = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        foreach (var root in FindRoots())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                snap.entries.Add(new Entry { path = PathOf(t), active = t.gameObject.activeSelf, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
        if (snap.entries.Count == 0) { Debug.LogError("No Christmas card in the open scene: nothing saved."); return; }
        File.WriteAllText(FilePath, JsonUtility.ToJson(snap, true));
        AssetDatabase.ImportAsset(FilePath);
        Debug.Log($"CHRISTMAS_LAYOUT_SAVED {snap.entries.Count} objects to {FilePath}");
    }

    [MenuItem("Tools/Christmas Card/4. Restore House Layout")]
    public static void RestoreMenu() => Restore(true);

    // Returns how many objects were put back. `undo` records it so Ctrl+Z works from the menu.
    public static int Restore(bool undo)
    {
        if (!File.Exists(FilePath)) { Debug.Log("No saved Christmas layout yet (Tools > Christmas Card > 3. Save House Layout)."); return 0; }
        var snap = JsonUtility.FromJson<Snapshot>(File.ReadAllText(FilePath));
        var byPath = new Dictionary<string, Transform>();
        foreach (var root in FindRoots())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                byPath[PathOf(t)] = t;
        int done = 0;
        var missing = new List<string>();
        foreach (var e in snap.entries)
        {
            if (!byPath.TryGetValue(e.path, out var t)) { missing.Add(e.path); continue; }
            if (undo) { Undo.RecordObject(t, "Restore Christmas layout"); Undo.RecordObject(t.gameObject, "Restore Christmas layout"); }
            t.localPosition = e.position;
            t.localRotation = e.rotation;
            t.localScale = e.scale;
            t.gameObject.SetActive(e.active);
            done++;
        }
        if (done > 0) EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"CHRISTMAS_LAYOUT_RESTORED {done} objects from the {snap.saved} save" + (missing.Count > 0 ? $", {missing.Count} not found (e.g. {missing[0]})" : ""));
        return done;
    }

    static IEnumerable<Transform> FindRoots()
    {
        foreach (var name in Roots)
        {
            // Find skips inactive objects (the templates are inactive), so search the scene's objects instead
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == name && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)) { yield return t; break; }
        }
    }

    // "Greeting Content/House/Roof#1": a #n suffix tells apart siblings with the same name.
    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (var x = t; x != null; x = x.parent)
        {
            int n = 0;
            if (x.parent)
                for (int i = 0; i < x.GetSiblingIndex(); i++)
                    if (x.parent.GetChild(i).name == x.name) n++;
            parts.Insert(0, n > 0 ? x.name + "#" + n : x.name);
            if (System.Array.IndexOf(Roots, x.name) >= 0) break;
        }
        return string.Join("/", parts);
    }
}
