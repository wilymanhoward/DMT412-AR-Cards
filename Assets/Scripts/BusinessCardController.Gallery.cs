using System.Collections;
using UnityEngine;

// Portfolio is not a little scene above its node: it takes over the whole card. The network (logo, nodes, lines,
// mini-me) retracts, a navy stage grows over the card, and the projects stand on it as a gallery wall:
//   one big featured frame with its title and caption, the other projects as small frames in a column.
//   Tap a small frame to bring it forward, swipe left or right to step through, drag to tilt the big one,
//   tap the big one to zoom it, and tap Back to bring the network back.
public partial class BusinessCardController
{
    static readonly Vector3 FeaturedSpot = new Vector3(-0.13f, 0.05f, -0.02f), ZoomedSpot = new Vector3(0, 0.08f, -0.02f);
    const float FrameHeight = 0.26f, ThumbScale = 0.45f, ZoomScale = 1.3f;

    bool gallery, busy, zoomed, pressing;
    Transform galleryFloor, galleryWallRoot;
    Transform[] galFrames;
    Billboard galTitle;
    TextMesh galCaption;
    Transform galHint;
    Vector2 pressPos, pointer;
    float drag;

    void BuildGallery()
    {
        galleryFloor = new GameObject("Gallery Floor").transform;
        galleryFloor.SetParent(content, false);
        Prim(PrimitiveType.Cube, "Stage", galleryFloor, new Vector3(0, 0.006f, 0), new Vector3(1.02f, 0.012f, 0.62f), Lit(Navy));
        var glow = Lit(Orange, 0.7f);
        Prim(PrimitiveType.Cube, "Edge Top", galleryFloor, new Vector3(0, 0.008f, 0.305f), new Vector3(1.02f, 0.016f, 0.012f), glow);
        Prim(PrimitiveType.Cube, "Edge Bottom", galleryFloor, new Vector3(0, 0.008f, -0.305f), new Vector3(1.02f, 0.016f, 0.012f), glow);
        Prim(PrimitiveType.Cube, "Edge Left", galleryFloor, new Vector3(-0.505f, 0.008f, 0), new Vector3(0.012f, 0.016f, 0.62f), glow);
        Prim(PrimitiveType.Cube, "Edge Right", galleryFloor, new Vector3(0.505f, 0.008f, 0), new Vector3(0.012f, 0.016f, 0.62f), glow);

        var wall = new GameObject("Gallery Wall").AddComponent<Billboard>();
        wall.transform.SetParent(content, false);
        wall.transform.localPosition = new Vector3(0, 0.28f, 0); // high enough that the lowest thumbnail and the caption stay above the floor
        wall.cam = arCamera;
        galleryWallRoot = wall.transform;

        var items = data.portfolio ?? new BusinessCardContent.Project[0];
        galFrames = new Transform[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            int index = i;
            var texture = string.IsNullOrEmpty(items[i].image) ? null : Resources.Load<Texture2D>(items[i].image);
            float aspect = texture ? (float)texture.width / texture.height : 16f / 9f;
            float h = FrameHeight, w = h * aspect;
            var frame = new GameObject("Frame " + (i + 1)).transform;
            frame.SetParent(galleryWallRoot, false);
            var slab = Prim(PrimitiveType.Cube, "Frame", frame, Vector3.zero, new Vector3(w + 0.02f, h + 0.02f, 0.012f), Lit(Cream));
            Prim(PrimitiveType.Cube, "Trim", frame, new Vector3(0, -(h + 0.02f) / 2, 0), new Vector3(w + 0.02f, 0.008f, 0.014f), Lit(Orange, 0.6f));
            if (texture)
                Prim(PrimitiveType.Quad, "Picture", frame, new Vector3(0, 0, -0.0065f), new Vector3(w, h, 1), new Material(Shader.Find("Unlit/Texture")) { mainTexture = texture });
            AddTap(slab.gameObject, 1f, () => FrameTap(index));
            galFrames[i] = frame;
        }

        galTitle = NewSign(galleryWallRoot, "", 0.03f, Cream, Orange);
        galTitle.face = false;
        galCaption = Text(galleryWallRoot, "", 0.018f, Slate, Vector3.zero);
        galCaption.anchor = TextAnchor.UpperCenter; // hangs from its top, so 2 or 3 lines never ride up into the title

        var header = NewSign(galleryWallRoot, "PORTFOLIO", 0.026f, Orange, Navy);
        header.face = false;
        header.transform.localPosition = new Vector3(0, 0.27f, -0.01f); // above the featured and zoomed frames
        var back = NewSign(galleryWallRoot, "< Back", 0.03f, Cream, Orange);
        back.face = false;
        back.transform.localPosition = new Vector3(-0.4f, 0.27f, -0.01f);
        AddTap(back.back.gameObject, 1f, () => { if (!busy) StartCoroutine(ExitGallery()); });
        var hint = NewSign(galleryWallRoot, items.Length > 1 ? "Swipe or tap a picture" : "Tap the picture to zoom", 0.018f, Slate, Navy);
        hint.face = false;
        galHint = hint.transform;
        galHint.localPosition = new Vector3(0.36f, -0.235f, -0.01f); // under the thumbnail column, clear of the caption

        ResetGallery();
    }

    void ResetGallery()
    {
        gallery = busy = zoomed = pressing = false;
        drag = 0;
        if (galleryFloor) galleryFloor.gameObject.SetActive(false);
        if (galleryWallRoot) galleryWallRoot.gameObject.SetActive(false);
    }

    void SetProject(int index)
    {
        if (galFrames.Length == 0) { galTitle.SetText("Coming soon"); galCaption.text = ""; return; }
        project = (index % galFrames.Length + galFrames.Length) % galFrames.Length;
        var item = data.portfolio[project];
        galTitle.SetText(item.title);
        galCaption.text = Wrap(item.caption, 46);
    }

    // ---------- Going in and out ----------

    IEnumerator EnterGallery()
    {
        busy = true;
        selected = 2;
        Current = State.Panel;
        HideBubble();
        ui.banner.SetActive(false);
        PlaySfx(openClip);
        Haptics.Buzz(30, Haptics.Medium);
        yield return PulseTo(2, 0.5f);

        // the network retracts into the logo, then the logo and the mini-me shrink away
        yield return Ease.Run(0.6f, t =>
        {
            float k = 1 - Ease.InOutSine(t);
            ringGrow = k;
            foreach (var h in hubs) { h.grow = k; h.pop = k; }
            logo.localScale = logoScale * k;
            avatar.localScale = avatarScale * k;
        });
        logo.gameObject.SetActive(false);
        avatar.gameObject.SetActive(false);
        pulse.gameObject.SetActive(false);
        foreach (var h in hubs) { h.node.gameObject.SetActive(false); h.spoke.enabled = h.stem.enabled = false; }
        foreach (var line in ring) line.enabled = false;
        foreach (var tag in ui.tags) tag.SetActive(false);
        if (openStage >= 0) HidePanel();

        // the stage grows over the card, then the wall rises
        SetProject(project);
        galleryFloor.localScale = new Vector3(0.001f, 1, 0.001f);
        galleryFloor.gameObject.SetActive(true);
        gallery = true;
        PlaySfx(powerupClip, 0.8f);
        yield return Ease.Run(0.5f, t => galleryFloor.localScale = new Vector3(Mathf.Max(0.001f, Ease.OutCubic(t)), 1, Mathf.Max(0.001f, Ease.OutCubic(t))));
        galleryWallRoot.localScale = Vector3.one * 0.001f;
        galleryWallRoot.gameObject.SetActive(true);
        PlaySfx(sparkleClip);
        Burst(galleryWallRoot.position, 40);
        yield return Ease.Run(0.6f, t => galleryWallRoot.localScale = Vector3.one * Mathf.Max(0.001f, Ease.OutBack(t, 1.3f)));
        galleryWallRoot.localScale = Vector3.one;
        busy = false;
    }

    IEnumerator ExitGallery()
    {
        busy = true;
        pressing = false;
        PlaySfx(tickClip);
        yield return Ease.Run(0.3f, t => galleryWallRoot.localScale = Vector3.one * Mathf.Max(0.001f, 1 - Ease.InCubic(t)));
        galleryWallRoot.gameObject.SetActive(false);
        yield return Ease.Run(0.35f, t => galleryFloor.localScale = new Vector3(Mathf.Max(0.001f, 1 - Ease.InCubic(t)), 1, Mathf.Max(0.001f, 1 - Ease.InCubic(t))));
        galleryFloor.gameObject.SetActive(false);
        gallery = false;
        zoomed = false;

        // the network grows back out of the logo
        logo.localScale = Vector3.zero;
        logo.gameObject.SetActive(true);
        avatar.localScale = Vector3.zero;
        avatar.gameObject.SetActive(true);
        avatarAnim.CrossFade("Look", 0.1f);
        foreach (var h in hubs) { h.grow = h.pop = 0; h.node.gameObject.SetActive(true); }
        foreach (var tag in ui.tags) tag.SetActive(true);
        PlaySfx(powerupClip, 0.8f);
        yield return Ease.Run(0.7f, t =>
        {
            float k = Ease.InOutSine(t);
            ringGrow = k;
            foreach (var h in hubs) { h.grow = k; h.pop = Ease.OutBack(t, 1.6f); }
            logo.localScale = logoScale * Mathf.Max(0.001f, Ease.OutBack(t, 1.3f));
            avatar.localScale = avatarScale * Mathf.Max(0.001f, Ease.OutBack(t, 1.3f));
        });
        ringGrow = 1;
        foreach (var h in hubs) { h.grow = h.pop = 1; }
        logo.localScale = logoScale;
        avatar.localScale = avatarScale;
        selected = -1;
        Current = State.Hub;
        ShowBanner(true);
        busy = false;
    }

    // ---------- Every frame in the gallery ----------

    void UpdateGallery(float dt)
    {
        if (!busy) GalleryInput(dt);
        float k = 1 - Exp(-9f * dt);
        int rank = 0;
        for (int i = 0; i < galFrames.Length; i++)
        {
            Vector3 spot;
            float scale;
            Quaternion tilt = Quaternion.identity;
            if (i == project)
            {
                spot = (zoomed ? ZoomedSpot : FeaturedSpot) + Vector3.right * drag * 0.0006f;
                scale = zoomed ? ZoomScale : 1f;
                tilt = Quaternion.Euler(0, Mathf.Clamp(-drag * 0.08f, -25f, 25f), 0);
            }
            else
            {
                spot = new Vector3(0.36f, 0.15f - rank * 0.14f, 0);
                scale = zoomed ? 0.001f : ThumbScale;
                rank++;
            }
            var f = galFrames[i];
            f.localPosition = Vector3.Lerp(f.localPosition, spot, k);
            f.localScale = Vector3.Lerp(f.localScale, Vector3.one * Mathf.Max(0.001f, scale), k);
            f.localRotation = Quaternion.Slerp(f.localRotation, tilt, k);
        }
        var textSpot = zoomed ? ZoomedSpot.x : FeaturedSpot.x;
        float bottom = zoomed ? ZoomedSpot.y - FrameHeight * ZoomScale / 2 : FeaturedSpot.y - FrameHeight / 2;
        galTitle.transform.localPosition = Vector3.Lerp(galTitle.transform.localPosition, new Vector3(textSpot, bottom - 0.045f, -0.01f), k);
        galCaption.transform.localPosition = Vector3.Lerp(galCaption.transform.localPosition, new Vector3(textSpot, bottom - 0.09f, -0.01f), k);
    }

    static float Exp(float x) => Mathf.Exp(x);

    // A tap picks (or zooms) a frame, a swipe steps through, and while a finger is down the big frame follows it.
    void GalleryInput(float dt)
    {
        if (!pressing)
        {
            if (TapInput.Started(out Vector2 start)) { pressing = true; pressPos = pointer = start; }
        }
        else
        {
            bool up;
            if (Input.touchCount > 0) { pointer = Input.GetTouch(0).position; up = false; }
            else { pointer = Input.mousePosition; up = !Input.GetMouseButton(0); }
            if (up)
            {
                pressing = false;
                float dx = pointer.x - pressPos.x;
                if (Mathf.Abs(dx) > 70f) { if (galFrames.Length > 1) GalleryStep(dx < 0 ? 1 : -1); }
                else if (Mathf.Abs(dx) < 30f) GalleryTap(pressPos);
            }
        }
        drag = Mathf.Lerp(drag, pressing ? pointer.x - pressPos.x : 0f, 1 - Exp(-14f * dt));
    }

    void GalleryTap(Vector2 screen)
    {
        var hits = Physics.RaycastAll(arCamera.ScreenPointToRay(screen), 100f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var tap = hit.collider.GetComponentInParent<Tappable>();
            if (tap) { tap.onTap(); return; }
        }
    }

    void GalleryStep(int direction)
    {
        SetProject(project + direction);
        zoomed = false;
        PlaySfx(tickClip);
        Haptics.Buzz(15, Haptics.Light);
    }

    void FrameTap(int index)
    {
        PlaySfx(tickClip);
        Haptics.Buzz(15, Haptics.Light);
        if (index == project) { zoomed = !zoomed; return; }
        SetProject(index);
        zoomed = false;
        Burst(galFrames[index].position, 15);
    }
}
