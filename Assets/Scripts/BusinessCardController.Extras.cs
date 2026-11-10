using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// The business card's extras, built in code (no rebuild):
//   - a guided tour: "Take the tour" and the mini-me walks you through every node (a tap anywhere stops it)
//   - "Save my contact": Android's own add-contact screen, filled in from business_card.json
//   - explorer progress and badges: "Explored 2/4", and Explorer / Bug Squasher / Easter Egg Hunter achievements
//   - music that follows the moment: quicker in Bug Hunt, slower under the rocket, muffled while a node is open
//   - idle life: after 20 quiet seconds he swivels, dances or says something; he looks at the node you open
//   - a QR code to the website on the Portfolio wall (made by Tools/make_qr.py)
public partial class BusinessCardController
{
    const string ExplorerKey = "BadgeExplorer", BugBadgeKey = "BadgeBugSquasher";
    static readonly string[] TourLines =
    {
        "This is me: who I am and what I love.",
        "These are my tools. Tap one and it flies!",
        "Here are games I've made. Swipe through them!",
        "Want to work together? Tap to email or call me.",
    };
    static readonly string[] IdleLines =
    {
        "*yawn*... anyone there?",
        "Psst! Tap a node to explore.",
        "Need... more... coffee.",
        "Tap me, I'm bored!",
        "Fun fact: this whole card is built in code.",
    };

    bool touring, tourStop, badgeShowing, explorerShown, bugBadgeShown;
    readonly bool[] explored = new bool[4];
    GameObject tourButton, tourCaption, progressChip;
    Text tourCaptionText, progressText;
    AudioLowPassFilter musicFilter;
    float idleFor;
    int idleNo;

    // ---------- Building ----------

    void BuildExtras()
    {
        var root = ui.root.transform;
        tourButton = MakePill(root, "Take the tour", Orange, Cream, 40, false, out _);
        PlaceBottom(tourButton);
        MakeButton(tourButton, tourButton.GetComponent<Image>(), StartTour);
        tourButton.AddComponent<UISlideIn>().from = new Vector2(0, -300);
        tourButton.SetActive(false);

        tourCaption = MakePill(root, "", NavyGlass, Cream, 34, true, out tourCaptionText);
        PlaceBottom(tourCaption);
        tourCaption.AddComponent<UIPopIn>();
        tourCaption.SetActive(false);

        progressChip = MakePill(root, "", NavyGlass, Cream, 30, true, out progressText);
        var rect = (RectTransform)progressChip.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, -40);
        progressChip.SetActive(false);

        NaturalFx();
        musicFilter = music.gameObject.AddComponent<AudioLowPassFilter>();
        musicFilter.cutoffFrequency = 22000;
        BuildQr();
    }

    void PlaceBottom(GameObject pill)
    {
        var rect = (RectTransform)pill.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = new Vector2(0, 150);
    }

    void ResetExtras()
    {
        touring = tourStop = explorerShown = bugBadgeShown = false;
        for (int i = 0; i < explored.Length; i++) explored[i] = false;
        idleFor = 0;
        if (!tourButton) return;
        tourButton.SetActive(false);
        tourCaption.SetActive(false);
        progressChip.SetActive(false);
        music.pitch = 1;
        musicFilter.cutoffFrequency = 22000;
    }

    // ---------- Every frame ----------

    void UpdateExtras(float t, float dt)
    {
        bool hub = Current == State.Hub && !rocketing && !gallery && selected < 0;
        SetOn(tourButton, hub && !touring);
        SetOn(progressChip, (Current == State.Hub || Current == State.Panel) && !gameOn && !rocketing && !touring);
        int badges = PlayerPrefs.GetInt(ExplorerKey, 0) + PlayerPrefs.GetInt(BugBadgeKey, 0) + PlayerPrefs.GetInt(EggKey, 0);
        int count = 0;
        foreach (bool e in explored) if (e) count++;
        progressText.text = $"Explored {count}/4    Badges {badges}/3";

        // the music follows the moment
        float pitch = gameOn && Current == State.Game ? 1.08f : rocketing ? 0.92f : 1f;
        music.pitch = Mathf.MoveTowards(music.pitch, pitch, dt * 0.25f);
        float cutoff = Current == State.Panel && !gallery ? 2400f : 22000f; // a node open: softer, as if behind glass
        musicFilter.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(musicFilter.cutoffFrequency), Mathf.Log(cutoff), 1 - Mathf.Exp(-3f * dt)));

        // he looks at the node you opened, and back at his screen afterwards
        if (pose && charge < 2 && !rocketing && Current != State.Game)
        {
            if (Current == State.Panel && selected >= 0 && selected != 2)
                pose.lookAt.position = Vector3.Lerp(pose.lookAt.position, hubs[selected].node.position, 1 - Mathf.Exp(-4f * dt));
            else if (Current == State.Hub)
                pose.lookAt.localPosition = Vector3.Lerp(pose.lookAt.localPosition, lookRest, 1 - Mathf.Exp(-4f * dt));
        }

        // idle life
        if (hub && !touring && charge == 0)
        {
            idleFor += dt;
            if (idleFor > 20f) { idleFor = 0; StartCoroutine(IdleBit()); }
        }
        else idleFor = 0;

        if (touring && gallery && TapInput.Started(out _)) tourStop = true; // the gallery takes its own taps
    }

    static void SetOn(GameObject go, bool on)
    {
        if (go.activeSelf != on) go.SetActive(on);
    }

    // Any tap on the hub: he isn't bored, and a running tour stops. Returns true when the tap was used up.
    bool NoteTap()
    {
        idleFor = 0;
        if (!touring) return false;
        tourStop = true;
        return true;
    }

    // ---------- 1. The guided tour ----------

    void StartTour()
    {
        if (Current != State.Hub || touring || busy || rocketing) return;
        Haptics.Buzz(20, Haptics.Light);
        StartCoroutine(Tour());
    }

    IEnumerator Tour()
    {
        touring = true;
        tourStop = false;
        ShowBanner(false);
        avatarAnim.CrossFade("Wave", 0.15f);
        Say("Let me show you around! (tap to stop)");
        yield return TourWait(2.6f);
        for (int i = 0; i < hubs.Length && !tourStop; i++)
        {
            if (i == 2)
            {
                yield return EnterGallery();
                Say(TourLines[i]);
                yield return TourWait(5f);
                yield return ExitGallery();
            }
            else
            {
                OpenPanel(i);
                Say(TourLines[i]);
                yield return TourWait(4.4f);
                if (Current == State.Panel) ClosePanel();
            }
            yield return TourWait(0.6f);
        }
        if (gallery && !busy) yield return ExitGallery();
        else if (Current == State.Panel && !gallery) ClosePanel();
        Say(tourStop ? "Okay, explore on your own!" : "Your turn! Psst... try tapping my monitor.");
        avatarAnim.CrossFade("Wave", 0.15f);
        yield return new WaitForSeconds(2.8f);
        avatarAnim.CrossFade("Look", 0.3f);
        tourCaption.SetActive(false);
        touring = false;
        ShowBanner(true);
    }

    // Waits, but a tap ends the wait at once (the tour then winds down at a safe point, never mid-transition).
    IEnumerator TourWait(float seconds)
    {
        for (float t = 0; t < seconds && !tourStop; t += Time.deltaTime) yield return null;
    }

    // The line goes in the caption (always) and in his bubble with his voice (when he's on the card).
    void Say(string line)
    {
        tourCaptionText.text = line;
        tourCaption.SetActive(false);
        tourCaption.SetActive(true);
        if (avatar.gameObject.activeInHierarchy) ShowBubble(line, 3.5f);
    }

    // ---------- 2. Save my contact ----------

    void SaveContact()
    {
        PlaySfx(popClip);
        Haptics.Buzz(25, Haptics.Light);
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.INSERT");
            void Call(string method, params object[] args) { using var r = intent.Call<AndroidJavaObject>(method, args); }
            Call("setType", "vnd.android.cursor.dir/raw_contact"); // ContactsContract.RawContacts.CONTENT_TYPE
            Call("putExtra", "name", data.name);                    // ContactsContract.Intents.Insert.*
            Call("putExtra", "email", data.email);
            Call("putExtra", "phone", data.phone);
            Call("putExtra", "job_title", data.tagline);
            Call("putExtra", "notes", WebLink(data.website));
            activity.Call("startActivity", intent);
            ShowBubble("Thanks! Let's keep in touch.", 2.5f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Save contact failed: " + e.Message);
            ShowBubble("Couldn't open Contacts, sorry!", 2.5f);
        }
#else
        Debug.Log($"Save contact: {data.name}, {data.email}, {data.phone}, {WebLink(data.website)}");
        ShowBubble("Thanks! Let's keep in touch.", 2.5f);
#endif
    }

    // ---------- 3. Explorer progress and badges ----------

    void MarkExplored(int i)
    {
        if (i < 0 || i >= explored.Length || explored[i]) return;
        explored[i] = true;
        StartCoroutine(Ease.Run(0.35f, k => progressChip.transform.localScale = Vector3.one * (1 + 0.25f * Mathf.Sin(Mathf.PI * k))));
        foreach (bool e in explored) if (!e) return;
        if (explorerShown) return;
        explorerShown = true;
        StartCoroutine(ShowBadge(ExplorerKey, "Explorer", "Opened every node"));
    }

    void BugHuntCleared(int level)
    {
        if (level < 2 || bugBadgeShown) return;
        bugBadgeShown = true;
        StartCoroutine(ShowBadge(BugBadgeKey, "Bug Squasher", "Cleared Bug Hunt level 2"));
    }

    // The achievement card slides down from the top (one at a time). Shown every time it's earned in a session,
    // so a demo always shows it; the phone remembers which badges were ever earned.
    IEnumerator ShowBadge(string key, string name, string how)
    {
        while (badgeShowing) yield return null;
        badgeShowing = true;
        bool first = PlayerPrefs.GetInt(key, 0) == 0;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
        eggHead.text = name;
        eggTitle.text = first ? "ACHIEVEMENT UNLOCKED" : "ACHIEVEMENT";
        eggSub.text = first ? how : how + " (again!)";
        eggPanel.SetActive(true);
        PlaySfx(achievementClip ? achievementClip : sparkleClip);
        Haptics.Buzz(40, Haptics.Medium);
        yield return Ease.Run(0.45f, t =>
        {
            eggRect.anchoredPosition = new Vector2(0, Mathf.LerpUnclamped(260, -160, Ease.OutBack(t, 1.2f)));
            eggIcon.localScale = Vector3.one * Mathf.Max(0.001f, Ease.OutBack(Mathf.Clamp01(t * 1.6f - 0.4f), 2f));
        });
        yield return new WaitForSeconds(3f);
        yield return Ease.Run(0.35f, t => eggRect.anchoredPosition = new Vector2(0, Mathf.Lerp(-160, 260, Ease.InCubic(t))));
        eggPanel.SetActive(false);
        badgeShowing = false;
    }

    // ---------- 5. Idle life ----------

    IEnumerator IdleBit()
    {
        string line = IdleLines[idleNo % IdleLines.Length];
        int kind = idleNo % 3;
        idleNo++;
        ShowBubble(line, 2.8f);
        if (kind == 0 && character) // a swivel in his chair
        {
            Quaternion rest = charRot;
            yield return Ease.Run(2f, k => character.localRotation = rest * Quaternion.Euler(0, 28f * Mathf.Sin(k * Mathf.PI * 2), 0));
            character.localRotation = rest;
        }
        else if (kind == 1) // a little dance
        {
            avatarAnim.CrossFade("Cheer", 0.2f);
            yield return new WaitForSeconds(2.6f);
            if (Current == State.Hub) avatarAnim.CrossFade("Look", 0.3f);
        }
        else
        {
            avatarAnim.CrossFade("Talk", 0.2f);
            yield return new WaitForSeconds(2.4f);
            if (Current == State.Hub) avatarAnim.CrossFade("Look", 0.3f);
        }
    }

    // ---------- Natural effects ----------

    ParticleSystem dust;

    // The built effects used star sprites in orange and navy: glittery, and the same everywhere. Here they become
    // what real things do: warm sparks that streak, slow down, fall and fade; soft dust where something lands;
    // a few faint motes drifting in the light; and a quick, soft shimmer for the teleport.
    void NaturalFx()
    {
        var dot = SoftDot();

        // sparks
        var r = burst.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(r.sharedMaterial) { mainTexture = dot };
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f;
        r.lengthScale = 1.5f;
        var main = burst.main;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.93f, 0.78f), new Color(1f, 0.62f, 0.28f));
        main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.008f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.38f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
        main.startRotation = 0;
        main.gravityModifier = 0;
        Drag(burst, 3f, -0.9f); // air drag, then they fall
        Fade(burst, (0, Color.white), (0.5f, new Color(1f, 0.75f, 0.45f, 0.9f)), (1, new Color(0.9f, 0.4f, 0.15f, 0)));
        Grow(burst, 0, 1, 1, 0.35f);
        var rotation = burst.rotationOverLifetime;
        rotation.enabled = false;

        // dust: a soft, short puff for impacts
        var smokeMat = new Material(r.sharedMaterial) { mainTexture = SmokeTexture() };
        dust = Fx("Dust", burst.transform.parent, smokeMat, true, 80);
        var dm = dust.main;
        dm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        dm.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        dm.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
        var shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.01f;
        shape.rotation = new Vector3(-90, 0, 0); // a dome opening upward
        Billow(dust, 2.5f, 0.6f, 1.9f);
        dm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.96f, 0.93f, 0.88f, 0.45f), new Color(0.85f, 0.83f, 0.8f, 0.35f));

        // motes: dust in the light
        var mr = motes.GetComponent<ParticleSystemRenderer>();
        mr.sharedMaterial = new Material(mr.sharedMaterial) { mainTexture = dot };
        var mm = motes.main;
        mm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.85f, 0.35f), new Color(1f, 0.85f, 0.65f, 0.2f));
        mm.startSize = new ParticleSystem.MinMaxCurve(0.0025f, 0.005f);
        mm.startSpeed = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
        var me = motes.emission;
        me.rateOverTime = 4;
        var noise = motes.noise;
        noise.strength = 0.012f;
        noise.frequency = 0.3f;
        Fade(motes, (0, new Color(1, 1, 1, 0)), (0.3f, Color.white), (0.5f, new Color(1, 1, 1, 0.5f)), (0.7f, Color.white), (1, new Color(1, 1, 1, 0))); // a slow twinkle

        // teleport: a quick soft shimmer rising round him, not a spiral of stars
        var sr = swirl.GetComponent<ParticleSystemRenderer>();
        sr.sharedMaterial = new Material(sr.sharedMaterial) { mainTexture = dot };
        var sm = swirl.main;
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.85f, 0.8f), new Color(1f, 0.7f, 0.4f, 0.6f));
        sm.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.012f);
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
        var se = swirl.emission;
        se.rateOverTime = 45;
        var orbit = swirl.velocityOverLifetime;
        orbit.orbitalY = 2f;
    }

    // A soft round dot (a gaussian falloff), white so the particle colour tints it.
    static Texture2D SoftDot()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                float a = Mathf.Exp(-(u * u + v * v) * 4.5f) * Mathf.Clamp01(1 - (u * u + v * v)); // soft, and zero at the edge
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // ---------- 6. QR code on the Portfolio wall ----------

    // Top right of the wall, clear of the header and the thumbnail column; tapping it opens the site too.
    void BuildQr()
    {
        var tex = Resources.Load<Texture2D>("Icons/qr_website");
        if (!tex || !galleryWallRoot) return;
        tex.filterMode = FilterMode.Point; // crisp squares, so phones can read it
        var qr = new GameObject("QR Code").transform;
        qr.SetParent(galleryWallRoot, false);
        qr.localPosition = new Vector3(0.4f, 0.3f, -0.01f);
        var plate = PanelObject("Plate", qr, Vector3.zero, 0.006f, Lit(Cream));
        plate.sharedMesh = Billboard.PanelMesh(0.105f, 0.13f, 0.01f);
        plate.transform.localPosition = new Vector3(0, 0.012f, 0);
        Prim(PrimitiveType.Quad, "Code", qr, new Vector3(0, 0, -0.0035f), new Vector3(0.09f, 0.09f, 1), new Material(Shader.Find("Unlit/Texture")) { mainTexture = tex });
        Text(qr, "Scan me", 0.013f, Navy, new Vector3(0, 0.058f, -0.004f));
        AddTap(plate.gameObject, 1f, () => OpenLink(WebLink(data.website)));
        plate.GetComponent<BoxCollider>().size = new Vector3(0.105f, 0.13f, 1); // the plate's own size (its z is scaled to 0.006)
    }
}
