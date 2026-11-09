using System.Collections;
using System.Linq;
using UnityEngine;

// "Howard's Node Network": the network printed on the business card comes alive in 3D.
//   Idle → Intro → Hub ⇄ Panel (a 3D scene above the node) or Game (Bug Hunt, BusinessCardController.BugHunt.cs)
// Intro: the HW block lifts off the card, the network draws itself out to four glowing nodes, and a mini-me
// arrives and says hello. Hub: tap a node (About, Skills, Portfolio, Contact) and a pulse of light runs along the
// line to it while its 3D scene grows above it; tap the logo to spin it, or the mini-me to make him wave.
// The words come from Assets/Resources/business_card.json (BusinessCardContent); the UI is built in code
// (BusinessCardController.UI.cs); the 3D scene is built by Tools > Business Card > Build Business Card.
// CardHub decides when the card is found or lost; losing it pauses everything and progress is kept.
public partial class BusinessCardController : MonoBehaviour, ICardExperience
{
    public enum State { Idle, Intro, Hub, Panel, Game }

    [System.Serializable]
    public class Hub
    {
        public string title;
        public Transform node;       // a glowing diamond above the printed one; pivot at its centre
        public LineRenderer spoke;   // logo → node
        public LineRenderer stem;    // node → the printed diamond on the card
        [HideInInspector] public Vector3 rest, baseScale;
        [HideInInspector] public float grow, pop, focus = 1f; // network drawn 0..1, pop-in 0..1 (overshoots), tap focus
    }

    [Header("Scene")]
    public Camera arCamera;
    public Transform content;        // under the Business Target; the card is 1 wide and 0.6 deep
    public Transform logo;           // the 3D HW block, pivot at its centre
    public Hub[] hubs;               // About, Skills, Portfolio, Contact
    public LineRenderer[] ring;      // hub-to-hub lines: About-Skills, Skills-Portfolio, Portfolio-Contact
    public Transform pulse;          // the light that runs along the lines
    public Transform avatar, avatarHead;
    public ParticleSystem burst, motes, swirl;

    [Header("Audio")]
    public AudioSource music, sfx;
    public AudioClip tickClip, openClip, pulseClip, powerupClip, sparkleClip, popClip;
    [Range(0, 1)] public float musicVolume = 0.35f;

    [Header("Art")]
    public Font font;
    public Sprite panelSprite;       // rounded, 9-sliced
    public Sprite iconMail, iconPhone, iconGlobe, iconClose, iconAbout, iconSkills, iconPortfolio, iconPrev, iconNext, iconMusic, iconMuted;

    public State Current { get; private set; } = State.Idle;

    // ICardExperience
    public Transform Content => content;
    public GameObject UiRoot => ui.root;

    BusinessCardContent data;
    Animator avatarAnim;
    Vector3 logoRest, logoScale, avatarScale;
    float ringGrow, logoSpin;
    int selected = -1;
    bool pulsing, muted;
    float avatarYaw;                 // the way he faces as built; swiping turns him, a restart turns him back
    Vector3 avatarSpot;
    // The middle of him, his chair and his desk, in his own frame (see BusinessCardBuilder.BuildDesk: chair back
    // at z -0.04, desk back at z 0.165). Swipes turn everything around this point, not around his feet.
    static readonly Vector3 DeskCentre = new Vector3(0, 0, 0.062f);
    Vector3 turnPivot;
    CapsuleCollider avatarCol;
    bool turning, turned;            // a finger came down on him; it has moved far enough to be a swipe
    Vector2 turnStart;
    float turnFrom;

    string FirstName => data.name.Split(' ')[0];

    void Awake()
    {
        data = BusinessCardContent.Load();
        // the business card's own sounds in Resources/Audio: pop and sparkle from Tools/make_audio.py (the Christmas
        // card keeps the shared ones), open, pulse and power-up from Kenney's Interface Sounds (CC0)
        void Own(ref AudioClip clip, string name) { var c = Resources.Load<AudioClip>("Audio/" + name); if (c) clip = c; }
        Own(ref popClip, "biz_pop");
        Own(ref sparkleClip, "biz_sparkle");
        Own(ref openClip, "biz_open");
        Own(ref pulseClip, "biz_pulse");
        Own(ref powerupClip, "biz_powerup");
        logoRest = logo.localPosition;
        logoScale = logo.localScale;
        avatarScale = avatar.localScale;
        avatarYaw = avatar.localEulerAngles.y;
        avatarSpot = avatar.localPosition;
        avatarAnim = avatar.GetComponentInChildren<Animator>();
        avatarCol = avatar.GetComponent<CapsuleCollider>();
        if (avatarCol) avatarCol.radius = Mathf.Min(avatarCol.radius, 0.05f); // snug round him, so it doesn't cover the desk
        foreach (var h in hubs) { h.rest = h.node.localPosition; h.baseScale = h.node.localScale; }
        BuildUI();
        BuildStages();
        BuildBugHunt();
        BuildRocket();
        ResetScene();
    }

    // ---------- Called by CardHub ----------

    // Shows or hides this card's UI and its 3D content (hidden while the home screen is up).
    public void SetShown(bool shown)
    {
        ui.root.SetActive(shown);
        foreach (var r in content.GetComponentsInChildren<Renderer>(true)) r.enabled = shown;
    }

    public void OnCardFound()
    {
        Time.timeScale = 1;
        AudioListener.pause = false;
        ui.lost.SetActive(false);
        if (Current == State.Idle) StartCoroutine(Intro());
    }

    public void OnCardLost()
    {
        if (Current == State.Idle) return;
        Time.timeScale = 0;          // freezes every tween, particle and animator
        AudioListener.pause = true;
        ui.lost.SetActive(true);
    }

    // Another card took over: start from the beginning next time this one is shown.
    public void ResetToStart()
    {
        StopAllCoroutines();
        ResetScene();
    }

    void ResetScene()
    {
        Current = State.Idle;
        selected = -1;
        pulsing = false;
        ringGrow = logoSpin = 0;
        logo.gameObject.SetActive(false);
        foreach (var h in hubs)
        {
            h.grow = h.pop = 0;
            h.focus = 1f;
            h.node.gameObject.SetActive(false);
            h.spoke.enabled = h.stem.enabled = false;
        }
        foreach (var line in ring) line.enabled = false;
        pulse.gameObject.SetActive(false);
        avatar.gameObject.SetActive(false);
        avatar.localRotation = Quaternion.Euler(0, avatarYaw, 0);
        avatar.localPosition = avatarSpot;
        turning = false;
        burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        swirl.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        music.Stop();
        project = 0;
        ResetGallery();
        ResetBugHunt();
        ResetRocket();
        ResetUi();
    }

    // ---------- The intro ----------

    IEnumerator Intro()
    {
        Current = State.Intro;
        PlaySfx(powerupClip);
        motes.Play();
        StartCoroutine(FadeMusic(musicVolume, 3f));
        yield return new WaitForSeconds(0.3f);

        // 1. the HW block lifts off the card with a spin
        logo.gameObject.SetActive(true);
        Vector3 floor = new Vector3(logoRest.x, 0.01f, logoRest.z);
        Burst(content.TransformPoint(floor), 30);
        Haptics.Buzz(40, Haptics.Medium);
        yield return Ease.Run(1.1f, t =>
        {
            logo.localPosition = Vector3.Lerp(floor, logoRest, Ease.OutCubic(t));
            logo.localScale = logoScale * Mathf.Max(0.01f, Ease.OutBack(t, 1.3f));
            logo.localRotation = Quaternion.Euler(0, 360f * (1 - Ease.OutCubic(t)), 0);
        });
        logo.localScale = logoScale;

        // 2. the network draws itself out to the nodes, one after another
        for (int i = 0; i < hubs.Length; i++)
        {
            StartCoroutine(DrawHub(i));
            yield return new WaitForSeconds(0.5f);
        }
        yield return new WaitForSeconds(0.6f);
        yield return Ease.Run(0.6f, t => ringGrow = t);
        ringGrow = 1;

        // 3. a mini-me arrives and says hello
        yield return AvatarAppear();
        ShowBubble("Hi, I'm " + FirstName + "!");
        avatarAnim.CrossFade("Wave", 0.1f);
        yield return new WaitForSeconds(2.4f);
        avatarAnim.CrossFade("Look", 0.3f);
        ShowBubble("Tap a node to explore", 3f);
        ShowBanner(true);

        Current = State.Hub;
        StartCoroutine(IdlePulses());
    }

    // The line grows out to the node, then the node pops in with a spark and its label.
    IEnumerator DrawHub(int i)
    {
        var h = hubs[i];
        h.node.gameObject.SetActive(true);
        h.node.localPosition = h.rest;
        PlaySfx(pulseClip, 0.5f);
        yield return Ease.Run(0.5f, t => h.grow = Ease.InOutSine(t));
        h.grow = 1;
        Burst(h.node.position, 24);
        PlaySfx(tickClip);
        Haptics.Buzz(20, Haptics.Light);
        ShowTag(i);
        yield return Ease.Run(0.45f, t => h.pop = Ease.OutBack(t, 2f));
        h.pop = 1;
    }

    // He never just pops in: an orange spiral gathers, a flash, then he grows in with a full spin and a wave.
    IEnumerator AvatarAppear()
    {
        Vector3 spot = avatar.localPosition;
        float yaw = avatar.localEulerAngles.y;
        avatar.gameObject.SetActive(true);
        avatar.localScale = Vector3.zero;
        swirl.transform.localPosition = spot;
        swirl.Play();
        PlaySfx(sparkleClip);
        yield return new WaitForSeconds(0.6f);

        Burst(content.TransformPoint(spot + Vector3.up * 0.1f), 40);
        Haptics.Buzz(60, Haptics.Medium);
        avatarAnim.CrossFade("Wave", 0.05f);
        yield return Ease.Run(0.7f, t =>
        {
            avatar.localScale = avatarScale * Ease.OutBack(t, 1.4f);
            avatar.localRotation = Quaternion.Euler(0, yaw - 360f * (1 - Ease.OutCubic(t)), 0);
        });
        swirl.Stop();
        avatar.localScale = avatarScale;
    }

    // ---------- Every frame ----------

    void Update()
    {
        if (Time.timeScale == 0 || Current == State.Idle) return;
        float t = Time.time, dt = Time.deltaTime;
        if (gallery) { UpdateGallery(dt); return; }
        if (Current >= State.Hub) Float(t, dt);

        for (int i = 0; i < hubs.Length; i++)
        {
            var h = hubs[i];
            float target = selected < 0 ? 1f : (i == selected ? 1.3f : 0.8f); // the chosen node swells, the others step back
            h.focus = Mathf.Lerp(h.focus, target, 1 - Mathf.Exp(-8f * dt));
            h.node.localScale = h.baseScale * (h.pop * h.focus);
        }

        UpdateStages(t, dt);
        UpdateBugHuntInvite(t, dt);
        UpdateRocket(t, dt);
        if (Current == State.Game) { UpdateBugHunt(t, dt); return; }
        if (turning) TurnAvatar();
        else if (Current >= State.Hub && !busy && !rocketing && TapInput.Started(out Vector2 screen)) HandleTap(screen);
    }

    // The idle motion that keeps it alive: everything bobs a little, the diamonds turn, the logo sways.
    void Float(float t, float dt)
    {
        if (rocketing) return; // the easter egg is flying them (BusinessCardController.Rocket.cs)
        logo.localPosition = logoRest + Vector3.up * (0.008f * Mathf.Sin(t * 1.6f));
        if (charge >= 2) logo.localPosition += Random.insideUnitSphere * (0.0015f * charge); // charging: it shakes
        logo.localRotation = Quaternion.Euler(0, 10f * Mathf.Sin(t * 0.7f) + logoSpin, 0);
        for (int i = 0; i < hubs.Length; i++)
        {
            hubs[i].node.localPosition = hubs[i].rest + Vector3.up * (0.007f * Mathf.Sin(t * 2f + i * 1.7f))
                                       + (logoRest - hubs[i].rest) * (0.012f * Mathf.Max(0, charge - 1)); // leaning toward a charging logo
            hubs[i].node.Rotate(0, 40f * dt, 0, Space.Self);
        }
    }

    // The lines follow the bobbing nodes, so they are redrawn after everything has moved.
    void LateUpdate()
    {
        if (Current == State.Idle || gallery) return;
        Vector3 from = logo.localPosition;
        for (int i = 0; i < hubs.Length; i++)
        {
            var h = hubs[i];
            Vector3 to = h.node.localPosition;
            SetLine(h.spoke, from, Vector3.Lerp(from, to, h.grow), h.grow > 0.001f);
            StyleSpoke(i, Time.time);
            SetLine(h.stem, to, Vector3.Lerp(to, new Vector3(h.rest.x, 0.002f, h.rest.z), h.grow), h.grow > 0.001f && h.pop > 0.001f && !StemCut(i));
        }
        for (int i = 0; i < ring.Length; i++)
        {
            float g = Mathf.Min(hubs[i].pop, hubs[i + 1].pop) * ringGrow;
            ring[i].widthMultiplier = g; // fades in as it thickens
            SetLine(ring[i], hubs[i].node.localPosition, hubs[i + 1].node.localPosition, g > 0.01f);
        }
    }

    static void SetLine(LineRenderer line, Vector3 a, Vector3 b, bool on)
    {
        line.enabled = on;
        if (!on) return;
        line.SetPosition(0, a);
        line.SetPosition(1, b);
    }

    // ---------- Taps ----------

    void HandleTap(Vector2 screen)
    {
        var hits = Physics.RaycastAll(arCamera.ScreenPointToRay(screen), 100f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        if (!System.Array.Exists(hits, h => h.collider.transform.IsChildOf(logo))) CancelCharge(); // tapped away from a charging logo
        // his monitor starts Bug Hunt even through the desk, but a tap on him (nearer than the monitor) is for him
        int me = System.Array.FindIndex(hits, h => h.collider == avatarCol), screen0 = System.Array.FindIndex(hits, h => h.collider == monitorCol);
        if (TappedMonitor(hits) && (me < 0 || screen0 < me)) { StartCoroutine(BugHuntRoutine()); return; }
        foreach (var hit in hits)
        {
            Transform t = hit.collider.transform;
            var tap = hit.collider.GetComponentInParent<Tappable>();
            if (tap) { tap.onTap(); return; }
            for (int i = 0; i < hubs.Length; i++)
                if (t.IsChildOf(hubs[i].node)) { OpenPanel(i); return; }
            if (t.IsChildOf(logo)) { LogoTap(); return; } // 5 quick taps: the easter egg
            if (t.IsChildOf(avatar)) { turning = true; turned = false; turnStart = screen; turnFrom = avatar.localEulerAngles.y; turnPivot = avatar.localPosition + CentreOffset(turnFrom); return; }
        }
        if (Current == State.Panel) ClosePanel(); // a tap on nothing closes what is open
    }

    static readonly string[] PanelLines = { "Here's a bit about me!", "These are the tools I use.", "Check out my games!", "Let's talk! Pick one." };

    void OpenPanel(int i)
    {
        if (i == 2) { if (!busy) { ShowBubble(PanelLines[2], 2.2f); StartCoroutine(EnterGallery()); } return; } // Portfolio takes over the whole card
        if (selected == i) { ClosePanel(); return; }
        selected = i;
        Current = State.Panel;
        StartCoroutine(PulseTo(i, 0.6f));
        PlaySfx(openClip);
        Haptics.Buzz(30, Haptics.Medium);
        avatarAnim.CrossFade("Talk", 0.25f);
        ShowBubble(PanelLines[i], 2.4f);
        ShowPanel(i);
    }

    void ClosePanel()
    {
        if (selected < 0) return;
        selected = -1;
        Current = State.Hub;
        avatarAnim.CrossFade("Look", 0.3f);
        HidePanel();
        ShowBanner(true);
        PlaySfx(tickClip);
    }

    // A pulse of light runs from the logo along the line to a node and sparks when it arrives.
    IEnumerator PulseTo(int i, float duration)
    {
        if (pulsing) yield break;
        pulsing = true;
        var h = hubs[i];
        pulse.gameObject.SetActive(true);
        yield return Ease.Run(duration, t => pulse.localPosition = Vector3.Lerp(logo.localPosition, h.node.localPosition, Ease.InOutSine(t)));
        pulse.gameObject.SetActive(false);
        pulsing = false;
        Burst(h.node.position, 24);
    }

    IEnumerator IdlePulses()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(2.5f, 4.5f));
            if (Current == State.Hub && !pulsing && !rocketing) yield return PulseTo(Random.Range(0, hubs.Length), 0.9f);
        }
    }

    // Tap the logo: it spins twice with a little stretch.
    IEnumerator SpinLogo()
    {
        PlaySfx(popClip);
        Haptics.Buzz(30, Haptics.Medium);
        Burst(logo.position, 30);
        yield return Ease.Run(1.1f, t =>
        {
            logoSpin = 720f * Ease.OutCubic(t);
            logo.localScale = logoScale * (1 + 0.15f * Mathf.Sin(Mathf.PI * t));
        });
        logoSpin = 0;
        logo.localScale = logoScale;
    }

    // A finger on the mini-me: a swipe turns him (and his desk) around, a tap makes him wave.
    void TurnAvatar()
    {
        bool held;
        Vector2 at;
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            at = touch.position;
            held = touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
        }
        else { at = Input.mousePosition; held = Input.GetMouseButton(0); }

        float dx = at.x - turnStart.x;
        if (!turned && Mathf.Abs(dx) > 20f) turned = true;
        if (turned) // he and his desk follow the finger, turning around their shared middle
        {
            float yaw = turnFrom - dx * 0.35f;
            avatar.localRotation = Quaternion.Euler(0, yaw, 0);
            avatar.localPosition = turnPivot - CentreOffset(yaw);
        }
        if (held) return;
        turning = false;
        if (!turned) StartCoroutine(AvatarWave());
    }

    // From his feet (the pivot) to the middle of the desk set, in card space, when he faces `yaw`.
    Vector3 CentreOffset(float yaw) => Quaternion.Euler(0, yaw, 0) * Vector3.Scale(avatar.localScale, DeskCentre);

    // Tap the mini-me: he waves and says the next of his lines (they also hint at the hidden things to try).
    int lineNo;
    string[] Lines() => new[]
    {
        "Hi, I'm " + FirstName + "! Tap a node to explore.",
        "I make games and AR apps.",
        "Psst... tap my monitor.",
        "Swipe me to turn my desk around.",
        "Try tapping my logo 5 times. Trust me.",
        "Want to work together? Tap Contact!",
    };

    IEnumerator AvatarWave()
    {
        PlaySfx(popClip, 0.7f);
        Haptics.Buzz(25, Haptics.Light);
        avatarAnim.CrossFade(lineNo % 2 == 0 ? "Wave" : "Talk", 0.15f);
        var lines = Lines();
        ShowBubble(lines[lineNo++ % lines.Length], 2.6f);
        yield return new WaitForSeconds(2.8f);
        avatarAnim.CrossFade(Current == State.Panel ? "Talk" : "Look", 0.3f);
    }

    // ---------- Contact links ----------

    void OpenLink(string url)
    {
        Haptics.Buzz(25, Haptics.Light);
        PlaySfx(tickClip);
        Application.OpenURL(url);
    }

    string MailLink => "mailto:" + data.email;
    string PhoneLink => "tel:" + new string(data.phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
    static string WebLink(string address) => address.StartsWith("http") ? address : "https://" + address;

    // ---------- Helpers ----------

    void Burst(Vector3 world, int count) =>
        burst.Emit(new ParticleSystem.EmitParams { position = world, applyShapeToPosition = true }, count);

    void PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (clip) sfx.PlayOneShot(clip, volume);
    }

    IEnumerator FadeMusic(float to, float seconds)
    {
        if (!music.isPlaying) { music.volume = 0; music.Play(); }
        float from = music.volume;
        yield return Ease.Run(seconds, t => music.volume = Mathf.Lerp(from, to, Ease.InOutSine(t)));
    }

    void ToggleSound()
    {
        muted = !muted;
        AudioListener.volume = muted ? 0 : 1;
        ui.soundIcon.sprite = muted ? iconMuted : iconMusic;
        Haptics.Buzz(15, Haptics.Light);
    }
}
