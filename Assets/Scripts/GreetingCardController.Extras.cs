using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Christmas card's extras, all built in code at start (no rebuild, the hand-tuned scene stays as it is):
//   - real sounds: any clip in Resources/ChristmasAudio replaces the generated one of the same role
//   - a personal card: "Merry Christmas, <to>!" when the house is up, and a letter from <from> after the recap
//     (the words are in Resources/greeting_card.json)
//   - the kids talk: speech bubbles over their heads with a high, mumbly cartoon voice
//   - free play after the ending: tap a kid, Santa, the tree or the snow
//   - a Photo button: saves the AR view with a Christmas frame to the phone's gallery
public partial class GreetingCardController
{
    [System.Serializable]
    class Personal { public string to = "", from = "Howard", message = ""; }

    Personal personal = new Personal();
    AudioSource voice;
    AudioClip[] talkClips;
    Transform[] kidHeads;
    GameObject[] kidBubbles;
    Text[] kidBubbleTexts;
    Coroutine[] bubbleRoutines;
    RectTransform recapPanel, letter, photoFrame, flash;
    Font titleFont;
    bool snapping, kidsTappable;
    Sprite roundSprite;

    static readonly string[] WowLines = { "Wow!", "Yay!", "So cool!", "I love it!", "Look at this!" };
    static readonly string[] PartyLines = { "Merry Christmas!", "Best day ever!", "Happy holidays!", "More presents?", "I'm not Santa... or am I?" };

    // ---------- Building ----------

    // Real recordings dropped into Resources/ChristmasAudio replace the generated clips (missing ones keep theirs).
    void LoadRealSounds()
    {
        AudioClip Find(string name) => Resources.Load<AudioClip>("ChristmasAudio/" + name);
        AudioClip Or(AudioClip current, string name) => Find(name) ?? current;
        music.clip = Or(music.clip, "music");
        if (bells) bells.clip = Or(bells.clip, "sleigh_bells");
        finaleClip = Or(finaleClip, "finale");
        popClip = Or(popClip, "pop");
        landClip = Or(landClip, "land");
        sparkleClip = Or(sparkleClip, "sparkle");
        rareClip = Or(rareClip, "rare");
        fireworksClip = Or(fireworksClip, "firework");
        whooshClip = Or(whooshClip, "whoosh");
        treeTapClip = Or(treeTapClip, "tree_tap");
        bellClip = Or(bellClip, "hand_bell");
    }

    void BuildExtras()
    {
        var json = Resources.Load<TextAsset>("greeting_card");
        if (json) personal = JsonUtility.FromJson<Personal>(json.text);

        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        talkClips = new AudioClip[8];
        for (int i = 0; i < talkClips.Length; i++) talkClips[i] = Resources.Load<AudioClip>("Audio/biz_talk_" + i); // the business card's mumble

        roundSprite = labelTemplate.GetComponent<Image>().sprite;
        foreach (var t in finishScreen.GetComponentsInChildren<Text>(true))
            if (t.name == "Text") { titleFont = t.font; break; } // the "Merry Christmas" title font
        if (!titleFont) titleFont = guideText.font;

        kidHeads = new Transform[kids.Length];
        kidBubbles = new GameObject[kids.Length];
        kidBubbleTexts = new Text[kids.Length];
        bubbleRoutines = new Coroutine[kids.Length];
        foreach (Transform child in finishScreen.transform)
            if (child.name == "Recap Panel") recapPanel = (RectTransform)child;
        BuildLetter();
        BuildPhotoButton();

        // the reindeer are one static mesh: make their legs gallop while the sleigh flies (LegRunner.cs)
        foreach (var mf in sleigh.GetComponentsInChildren<MeshFilter>(true))
            if (mf.name == "sleigh-reindeers-wood") LegRunner.Attach(mf, sleigh.forward, sleigh.up);
    }

    void ResetExtras()
    {
        if (letter) letter.gameObject.SetActive(false);
        if (recapPanel) recapPanel.localScale = Vector3.one;
        if (kidBubbles != null) foreach (var b in kidBubbles) if (b) b.SetActive(false);
    }

    // ---------- 2. A personal card ----------

    string Greeting => string.IsNullOrEmpty(personal.to) ? "Merry Christmas!" : "Merry Christmas, " + personal.to + "!";

    // The letter that takes the recap's place: a greeting in the title font, the message, and a signature.
    void BuildLetter()
    {
        letter = Panel("Letter", finishScreen.transform, new Vector2(0, -540), new Vector2(940, 420));
        var title = MakeText("Greeting", letter, Greeting, 64, FontStyle.Bold, RecapGold);
        title.font = titleFont;
        Place(title.rectTransform, new Vector2(0, -16), 90);
        var body = MakeText("Message", letter, personal.message, 34, FontStyle.Normal, Color.white);
        Place(body.rectTransform, new Vector2(0, -112), 170);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        var sign = MakeText("Signature", letter, "Love, " + personal.from, 50, FontStyle.Bold, RecapGold);
        sign.font = titleFont;
        sign.alignment = TextAnchor.MiddleRight;
        Place(sign.rectTransform, new Vector2(-30, -286), 70);
        var hint = MakeText("Hint", letter, "Tap the kids, Santa, the tree or the snow!", 24, FontStyle.Italic, new Color(1, 1, 1, 0.7f));
        Place(hint.rectTransform, new Vector2(0, -360), 40);
        letter.gameObject.SetActive(false);
    }

    RectTransform Panel(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        panel.SetParent(parent, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1);
        panel.anchoredPosition = position;
        panel.sizeDelta = size;
        var image = panel.GetComponent<Image>();
        image.sprite = roundSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.04f, 0.18f, 0.1f, 0.92f);
        image.raycastTarget = false;
        var rim = panel.gameObject.AddComponent<Outline>();
        rim.effectColor = RecapGold;
        rim.effectDistance = new Vector2(5, -5);
        return panel;
    }

    // Stretches a text across its parent's width, `height` tall, its top at `top` below the parent's top.
    static void Place(RectTransform t, Vector2 top, float height)
    {
        t.anchorMin = new Vector2(0, 1);
        t.anchorMax = new Vector2(1, 1);
        t.pivot = new Vector2(0.5f, 1);
        t.anchoredPosition = top;
        t.sizeDelta = new Vector2(-60, height);
    }

    // After the recap has had its moment, it folds away and the letter pops in.
    IEnumerator LetterAfterRecap()
    {
        yield return new WaitForSeconds(1.5f + 0.5f * found.Count + 3.5f);
        if (recapPanel) yield return Ease.Run(0.3f, t => recapPanel.localScale = Vector3.one * Mathf.Max(0.001f, 1 - Ease.InBack(t)));
        if (recapPanel) recapPanel.localScale = Vector3.zero;
        letter.localScale = Vector3.zero;
        letter.gameObject.SetActive(true);
        PlaySfx(sparkleClip, 0.8f);
        Haptics.Buzz(30, Haptics.Light);
        yield return Ease.Run(0.45f, t => letter.localScale = Vector3.one * Ease.OutBack(t, 1.8f));
    }

    // ---------- 3. The kids talk ----------

    void KidSay(int k, string line, float seconds = 1.8f)
    {
        if (!kids[k].gameObject.activeInHierarchy) return;
        if (!kidHeads[k]) MakeBubble(k);
        kidBubbleTexts[k].text = line;
        kidBubbles[k].SetActive(false);
        kidBubbles[k].SetActive(true); // pops in again
        if (bubbleRoutines[k] != null) StopCoroutine(bubbleRoutines[k]);
        bubbleRoutines[k] = StartCoroutine(HideBubbleLater(k, seconds));
        StartCoroutine(Mumble(line, k));
    }

    // Made the first time a kid talks (they are visible by then, so their size can be measured).
    void MakeBubble(int k)
    {
        var bounds = new Bounds(kids[k].position, Vector3.zero);
        foreach (var r in kids[k].GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        kidHeads[k] = new GameObject("Head").transform;
        kidHeads[k].SetParent(kids[k], false);
        kidHeads[k].position = new Vector3(kids[k].position.x, 0, kids[k].position.z) + Vector3.up * bounds.max.y;
        kidHeads[k].localPosition = new Vector3(0, kidHeads[k].localPosition.y, 0);

        var bubble = new GameObject("Kid Bubble", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(UIFollow), typeof(UIPopIn));
        bubble.transform.SetParent(uiRoot.transform, false);
        var rect = (RectTransform)bubble.transform;
        rect.sizeDelta = new Vector2(300, 84);
        var image = bubble.GetComponent<Image>();
        image.sprite = roundSprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(1f, 0.98f, 0.92f);
        image.raycastTarget = false;
        var rim = bubble.AddComponent<Outline>();
        rim.effectColor = new Color(0.78f, 0.16f, 0.16f);
        rim.effectDistance = new Vector2(3, -3);
        var follow = bubble.GetComponent<UIFollow>();
        follow.target = kidHeads[k];
        follow.cam = arCamera;
        follow.offset = new Vector2(0, 70);
        var text = MakeText("Line", rect, "", 34, FontStyle.Bold, new Color(0.65f, 0.1f, 0.1f));
        text.GetComponent<Outline>().enabled = false;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = new Vector2(-20, -8);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 20;
        text.resizeTextMaxSize = 34;
        kidBubbles[k] = bubble;
        kidBubbleTexts[k] = text;
        bubble.SetActive(false);
    }

    IEnumerator HideBubbleLater(int k, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        kidBubbles[k].SetActive(false);
        bubbleRoutines[k] = null;
    }

    // A syllable or two per word in a child's voice: the mini-me's mumble, pitched up (each kid a bit different).
    IEnumerator Mumble(string line, int k)
    {
        int syllables = Mathf.Clamp(Mathf.RoundToInt(line.Split(' ').Length * 1.5f), 2, 8);
        float basePitch = k % 2 == 0 ? 1.55f : 1.75f;
        for (int i = 0; i < syllables; i++)
        {
            var clip = talkClips[Random.Range(0, talkClips.Length)];
            voice.pitch = basePitch * Random.Range(0.94f, 1.1f) * (i == syllables - 1 && line.EndsWith("!") ? 1.12f : 1f); // excited endings go up
            if (clip) voice.PlayOneShot(clip, 0.55f);
            yield return new WaitForSeconds(Random.Range(0.07f, 0.1f));
        }
    }

    // ---------- 4. Free play after the ending ----------

    // Tap boxes on the kids and Santa, sized to their models (they have none for the story itself).
    void MakeTapTargets()
    {
        if (kidsTappable) return;
        kidsTappable = true;
        foreach (var who in kids) TapBox(who);
        TapBox(santa);
    }

    static void TapBox(Transform who)
    {
        if (who.GetComponent<Collider>()) return;
        var renderers = who.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        var box = who.gameObject.AddComponent<BoxCollider>();
        box.center = who.InverseTransformPoint(b.center);
        var size = who.InverseTransformVector(b.size);
        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 1.2f; // a little bigger for fingers
    }

    void FreePlay(RaycastHit[] hits, Vector2 screen)
    {
        foreach (var hit in hits)
        {
            var t = hit.collider.transform;
            for (int k = 0; k < kids.Length; k++)
                if (t == kids[k]) { StartCoroutine(KidHop(k)); return; }
            if (t == santa && santa.gameObject.activeSelf)
            {
                santaAnim.CrossFade("Wave", 0.2f);
                PlaySfx(santaClip, 0.9f);
                fx.Sparkle(santa.position + content.up * 0.2f, 25);
                Haptics.Buzz(30, Haptics.Medium);
                return;
            }
            if (t == tree) { if (Time.time >= nextTreePoke) PokeTree(hit.point); return; }
        }
        // anywhere else on the card: a puff of snow where you touched
        var ray = arCamera.ScreenPointToRay(screen);
        if (new Plane(content.up, content.position).Raycast(ray, out float d))
        {
            var p = ray.GetPoint(d);
            fx.Puff(p, 12);
            fx.Sparkle(p, 8);
            PlaySfx(popClip, 0.35f);
            Haptics.Buzz(12, Haptics.Light);
        }
    }

    // The kid jumps for joy and says something cheerful.
    IEnumerator KidHop(int k)
    {
        var kid = kids[k];
        kidAnims[k].CrossFade("Cheer", 0.15f);
        KidSay(k, PartyLines[Random.Range(0, PartyLines.Length)]);
        fx.Sparkle(kid.position, 15);
        Haptics.Buzz(20, Haptics.Light);
        Vector3 from = kid.localPosition;
        yield return Ease.Run(0.45f, t => kid.localPosition = from + Vector3.up * (Mathf.Sin(Mathf.PI * t) * 0.12f));
        kid.localPosition = from;
    }

    // ---------- 5. Photo ----------

    // A copy of the Restart button with a camera icon, above it: saves what you see with a Christmas frame.
    void BuildPhotoButton()
    {
        Transform restart = null;
        foreach (Transform child in hud.transform) if (child.GetComponent<Button>() && child.name.Contains("Restart")) { restart = child; break; }
        if (!restart) { Debug.LogWarning("No Restart button to copy: no Photo button."); return; }
        // copied while the original is off, so the copy's slide-in starts fresh from its own resting place
        bool wasOn = restart.gameObject.activeSelf;
        restart.gameObject.SetActive(false);
        var photo = Instantiate(restart.gameObject, restart.parent);
        restart.gameObject.SetActive(wasOn);
        photo.name = "Photo Button";
        var rect = (RectTransform)photo.transform;
        rect.anchoredPosition = new Vector2(125, 350); // above Restart (built at 125, 120, bottom left)
        var button = photo.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent(); // drop the copied Restart action
        button.onClick.AddListener(() => { if (!snapping) StartCoroutine(TakePhoto()); });
        foreach (var label in photo.GetComponentsInChildren<Text>(true)) label.text = "Photo";
        foreach (var icon in photo.GetComponentsInChildren<Image>(true))
            if (icon.name == "Icon") { icon.sprite = CameraIcon(); break; }
        photo.SetActive(true);

        // the frame drawn into the photo: a gold border and a ribbon with the greeting and who it's from
        photoFrame = new GameObject("Photo Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        photoFrame.SetParent(uiRoot.transform, false);
        photoFrame.anchorMin = Vector2.zero;
        photoFrame.anchorMax = Vector2.one;
        photoFrame.sizeDelta = Vector2.zero;
        foreach (var (min, max) in new[] { (new Vector2(0, 0), new Vector2(1, 0.012f)), (new Vector2(0, 0.988f), new Vector2(1, 1)), (new Vector2(0, 0), new Vector2(0.02f, 1)), (new Vector2(0.98f, 0), new Vector2(1, 1)) })
            Fill("Border", photoFrame, min, max, RecapGold);
        var ribbon = Fill("Ribbon", photoFrame, new Vector2(0, 0.012f), new Vector2(1, 0.12f), new Color(0.6f, 0.08f, 0.1f, 0.92f));
        var words = MakeText("Words", ribbon, Greeting + "   from " + personal.from, 64, FontStyle.Bold, Color.white);
        words.font = titleFont;
        words.rectTransform.anchorMin = Vector2.zero;
        words.rectTransform.anchorMax = Vector2.one;
        words.rectTransform.sizeDelta = Vector2.zero;
        words.resizeTextForBestFit = true;
        words.resizeTextMaxSize = 64;
        photoFrame.gameObject.SetActive(false);

        flash = Fill("Flash", (RectTransform)uiRoot.transform, Vector2.zero, Vector2.one, Color.white);
        flash.gameObject.SetActive(false);
    }

    static RectTransform Fill(string name, RectTransform parent, Vector2 min, Vector2 max, Color colour)
    {
        var r = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        r.SetParent(parent, false);
        r.anchorMin = min;
        r.anchorMax = max;
        r.sizeDelta = Vector2.zero;
        var image = r.GetComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return r;
    }

    IEnumerator TakePhoto()
    {
        snapping = true;
        // only the frame (and the kids' bubbles) go in the picture, not the buttons
        var hide = new List<GameObject>();
        foreach (Transform child in uiRoot.transform)
            if (child.gameObject.activeSelf && child != photoFrame && !child.name.StartsWith("Kid Bubble")) { hide.Add(child.gameObject); child.gameObject.SetActive(false); }
        photoFrame.gameObject.SetActive(true);
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        photoFrame.gameObject.SetActive(false);
        foreach (var go in hide) go.SetActive(true);

        PlaySfx(popClip, 0.8f);
        Haptics.Buzz(40, Haptics.Medium);
        flash.gameObject.SetActive(true);
        flash.SetAsLastSibling();
        var flashImage = flash.GetComponent<Image>();
        StartCoroutine(Ease.RunUnscaled(0.4f, t => flashImage.color = new Color(1, 1, 1, 1 - t)));

        bool saved = SaveToGallery(shot.EncodeToPNG(), "AR Christmas " + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        yield return ShowThumbnail(shot, saved);
        flash.gameObject.SetActive(false);
        Destroy(shot);
        snapping = false;
    }

    // The photo shrinks into a little card in the corner with "Saved to Photos", then slides away.
    IEnumerator ShowThumbnail(Texture2D shot, bool saved)
    {
        var card = new GameObject("Photo Saved", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        card.SetParent(uiRoot.transform, false);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0, 0);
        card.anchoredPosition = new Vector2(40, 420);
        float aspect = (float)shot.height / shot.width;
        card.sizeDelta = new Vector2(260, 260 * aspect + 70);
        card.GetComponent<Image>().color = Color.white;
        var picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
        picture.SetParent(card, false);
        picture.anchorMin = new Vector2(0, 0);
        picture.anchorMax = new Vector2(1, 1);
        picture.offsetMin = new Vector2(10, 64);
        picture.offsetMax = new Vector2(-10, -10);
        picture.GetComponent<RawImage>().texture = shot;
        var label = MakeText("Saved", card, saved ? "Saved to Photos!" : "Couldn't save", 26, FontStyle.Bold, saved ? new Color(0.1f, 0.4f, 0.2f) : new Color(0.7f, 0.1f, 0.1f));
        label.GetComponent<Outline>().enabled = false;
        label.rectTransform.anchorMin = new Vector2(0, 0);
        label.rectTransform.anchorMax = new Vector2(1, 0);
        label.rectTransform.pivot = new Vector2(0.5f, 0);
        label.rectTransform.anchoredPosition = new Vector2(0, 10);
        label.rectTransform.sizeDelta = new Vector2(0, 46);
        yield return Ease.RunUnscaled(0.35f, t =>
        {
            card.localScale = Vector3.one * Ease.OutBack(t, 1.6f);
            card.localRotation = Quaternion.Euler(0, 0, -6f * t);
        });
        yield return new WaitForSecondsRealtime(2.2f);
        yield return Ease.RunUnscaled(0.3f, t => card.anchoredPosition = new Vector2(Mathf.Lerp(40, -400, Ease.InCubic(t)), 420));
        Destroy(card.gameObject);
    }

    // Android 10+: straight into Pictures/AR Cards through MediaStore (no storage permission needed for our own
    // pictures). In the editor it goes to the persistent data folder.
    static bool SaveToGallery(byte[] png, string fileName)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
            using var values = new AndroidJavaObject("android.content.ContentValues");
            values.Call("put", "_display_name", fileName);
            values.Call("put", "mime_type", "image/png");
            values.Call("put", "relative_path", "Pictures/AR Cards");
            using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
            using var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
            using var uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
            if (uri == null) return false;
            using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);
            stream.Call("write", png);
            stream.Call("close");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Photo not saved: " + e.Message);
            return false;
        }
#else
        try
        {
            var path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            System.IO.File.WriteAllBytes(path, png);
            Debug.Log("Photo saved to " + path);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Photo not saved: " + e.Message);
            return false;
        }
#endif
    }

    // A white camera glyph (body, top bump, lens ring) for the Photo button.
    static Sprite CameraIcon()
    {
        const int n = 96;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x + 0.5f, v = y + 0.5f;
                bool body = u > 8 && u < 88 && v > 16 && v < 72 && !(Corner(u, v, 8, 16) || Corner(u, v, 88, 16) || Corner(u, v, 8, 72) || Corner(u, v, 88, 72));
                bool bump = u > 30 && u < 58 && v >= 70 && v < 82;
                float lens = Mathf.Sqrt((u - 48) * (u - 48) + (v - 44) * (v - 44));
                bool ring = lens >= 13 && lens < 20; // a clear ring round the filled lens
                bool on = (body || bump) && !ring;
                px[y * n + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
    }

    // true in the cut-off corner of a rounded rectangle whose corner point is (cx, cy), radius 10
    static bool Corner(float u, float v, float cx, float cy)
    {
        const float r = 10;
        float ox = cx < 48 ? cx + r : cx - r, oy = cy < 48 ? cy + r : cy - r;
        bool inCorner = (cx < 48 ? u < ox : u > ox) && (cy < 48 ? v < oy : v > oy);
        return inCorner && (u - ox) * (u - ox) + (v - oy) * (v - oy) > r * r;
    }
}
