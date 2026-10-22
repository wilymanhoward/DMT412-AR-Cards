using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runs the Christmas card as a state machine:
//   Idle → Scanned → TreeUp → SantaArriving → GiftsReady ⇄ Opening → BellReady → SantaVisit → GoldenGift → Finished
// Bonus: leaning the phone close to the tree (before the bell) makes Santa peek out, wave and flip away.
// CardHub decides when the card is found or lost and shows/hides this experience.
// Losing the card pauses everything (Time.timeScale = 0) and shows "Find the card again"; progress is kept.
// All references are filled in by Tools > Christmas Card > 2. Build Greeting Card.
public class GreetingCardController : MonoBehaviour
{
    public enum State { Idle, Scanned, TreeUp, SantaArriving, GiftsReady, Opening, BellReady, SantaVisit, GoldenGift, Finished }

    [System.Serializable]
    public class Reward
    {
        public string name;
        public GameObject prefab;
        public float weight = 1;
        public bool rare;
    }

    [Header("Scene")]
    public Camera arCamera;
    public Transform content;      // under the image target; card is 1 unit wide, ~0.71 deep
    public Transform tree;         // pivot at the base centre
    public TreeLights treeLights;
    public Transform sleigh;       // pivot at its centre, reindeer facing +Z
    public Transform santa;        // walks in with the golden present, dances in the finale
    public Transform[] giftSpots;
    public GameObject[] giftTemplates;
    public Reward[] rewards;
    public FxLibrary fx;

    [Header("Santa's flight (card units)")]
    public float circleRadius = 0.75f;
    public float circleHeight = 0.8f;
    public int laps = 2;
    public float lapTime = 4f;
    public float approachTime = 3f;
    public Vector3 flyInFrom = new Vector3(3f, 1.8f, 4f);
    public Vector3 flyOutTo = new Vector3(-3.5f, 2.5f, 4.5f);

    [Header("Ring the bell, Santa visits")]
    public Transform bell;                         // hanger: pivot at the top so the bell swings
    public int ringsNeeded = 3;
    public Transform santaEnter, santaStand, goldenSpot; // drag these in the scene to adjust the walk
    public GameObject goldenGiftTemplate;
    public Transform peekHide, peekShow, peekExit; // "lean in" bonus path
    public float leanDistance = 1.4f;              // card units from the camera to the middle of the tree

    [Header("Audio")]
    public AudioSource music;
    public AudioSource bells;      // 3D, on the sleigh: louder as it approaches
    public AudioSource sfx;
    public AudioClip popClip, landClip, sparkleClip, rareClip, fireworksClip, whooshClip, treeTapClip;
    public AudioClip finaleClip;   // plays once in the finale, then the main music returns
    public AudioClip bellClip;
    [Range(0, 1)] public float musicVolume = 0.6f;

    [Header("UI")]
    public GameObject uiRoot;      // the whole Christmas UI; hidden on the home screen
    public GameObject startScreen, hud, lostScreen, finishScreen;
    public Text guideText;         // one line telling the player what to do next
    public Button getGiftButton;
    public Text soundLabel;
    public Image soundIcon;
    public Sprite soundOnSprite, soundOffSprite;
    public RewardLabel labelTemplate;
    public GameObject spiritMeter; // "Christmas spirit": one star per bell ring
    public Image[] spiritStars;

    public State Current { get; private set; } = State.Idle;

    readonly List<GameObject> spawned = new List<GameObject>();
    int giftsLanded, giftsOpened;
    bool treeReady, muted;
    float nextTreePoke;
    Quaternion treeRotation;
    Vector3 santaScale;
    Vector3 sleighPrev, sleighForward = Vector3.forward;
    float sleighRoll;
    AudioClip mainMusic;
    Animator santaAnim;
    Vector3 bellScale;
    int rings;
    float bellRungAt = -10f, closeSince = -1f;
    bool peekDone;
    Coroutine peek;

    void Awake()
    {
        Application.targetFrameRate = 60;
        treeRotation = tree.localRotation;
        santaScale = santa.localScale;
        santaAnim = santa.GetComponentInChildren<Animator>();
        bellScale = bell.localScale;
        mainMusic = music.clip;
        ResetScene();
        SetState(State.Idle);
    }

    // ---------- Called by CardHub ----------

    // Shows or hides this card's UI and its 3D content (hidden while the home screen is up,
    // even if Vuforia is still extended-tracking the card).
    public void SetShown(bool shown)
    {
        uiRoot.SetActive(shown);
        foreach (var r in content.GetComponentsInChildren<Renderer>(true)) r.enabled = shown;
        if (shown) PlaySfx(sparkleClip); // goes with the UI's reveal burst
    }

    public void OnCardFound()
    {
        if (Current == State.Idle) { SetState(State.Scanned); return; }
        Time.timeScale = 1;
        AudioListener.pause = false;
        lostScreen.SetActive(false);
    }

    public void OnCardLost()
    {
        if (Current == State.Idle) return;
        Time.timeScale = 0;          // freezes every tween, particle and animator
        AudioListener.pause = true;
        lostScreen.SetActive(true);
    }

    // ---------- Buttons ----------

    public void StartExperience()
    {
        if (Current == State.Scanned) StartCoroutine(TreeUpSequence());
    }

    public void GetYourGift()
    {
        if (Current == State.TreeUp && treeReady) StartCoroutine(SantaSequence());
    }

    public void ToggleSound()
    {
        muted = !muted;
        AudioListener.volume = muted ? 0 : 1;
        soundLabel.text = muted ? "Muted" : "Music";
        soundIcon.sprite = muted ? soundOffSprite : soundOnSprite;
    }

    public void Restart()
    {
        StopAllCoroutines();
        ResetScene();
        StartCoroutine(TreeUpSequence());
    }

    // ---------- Sequence 1: the tree rises ----------

    IEnumerator TreeUpSequence()
    {
        treeReady = false;
        SetState(State.TreeUp);
        tree.gameObject.SetActive(true);
        tree.localScale = new Vector3(0.6f, 0.001f, 0.6f);

        // anticipation: sparkles gather on the card before the tree breaks through
        fx.sparkleRing.Play();
        PlaySfx(sparkleClip);
        yield return new WaitForSeconds(0.4f);

        fx.glitter.Play();
        PuffRing(0.25f, 6);
        yield return Ease.Run(2f, t =>
        {
            float xz = Mathf.Lerp(0.6f, 1f, Ease.OutCubic(t));
            tree.localScale = new Vector3(xz, Mathf.Max(0.001f, Ease.OutBack(t, 1.2f)), xz); // small bounce at the end
        });
        fx.glitter.Stop();
        fx.sparkleRing.Stop();
        PuffRing(0.3f, 8);

        // "lights on!": a golden burst at the star, then the tree comes alive
        fx.Rare(treeLights.star.position);
        PlaySfx(rareClip, 0.7f);
        treeLights.enabled = true;
        treeLights.Jiggle(0.6f);
        fx.snowfall.Play();
        fx.orbitStars.Play();
        StartCoroutine(FadeAudio(music, 0, musicVolume, 2.5f, true));
        treeReady = true;
        SetState(State.TreeUp); // enables "Get Your Gift" and updates the guide
    }

    // ---------- Sequence 2: Santa flies in, circles, drops gifts, flies off ----------

    IEnumerator SantaSequence()
    {
        SetState(State.SantaArriving);
        giftsLanded = 0;

        Vector3 center = tree.localPosition + Vector3.up * circleHeight;
        float entry = BestEntryAngle(center);
        Vector3 entryPoint = OnCircle(center, entry);
        Vector3 tangent = Tangent(entry);
        // Bezier handle sized so the sleigh joins the circle at exactly the circling speed
        // (the arrive/depart easings below have slope 0.5 at the joint).
        float speed = 2 * Mathf.PI * circleRadius / lapTime;
        float handle = speed * approachTime / 1.5f;

        sleigh.gameObject.SetActive(true);
        sleighPrev = flyInFrom;
        sleighForward = (entryPoint - flyInFrom).normalized;
        sleighRoll = 0;
        MoveSleigh(flyInFrom);
        fx.trail.Play();
        bells.volume = 0;
        bells.Play();
        StartCoroutine(FadeAudio(bells, 0, 1, 1.5f, false));

        // 1. approach: quick from far away, easing out as it reaches the tree
        Vector3 a1 = flyInFrom + (entryPoint - flyInFrom) * 0.35f + Vector3.up * 0.4f;
        Vector3 a2 = entryPoint - tangent * handle;
        yield return Ease.Run(approachTime, t =>
            MoveSleigh(Bezier(flyInFrom, a1, a2, entryPoint, 0.5f * t + 0.5f * Ease.OutCubic(t))));

        // 2. circle the tree, dropping each gift as Santa passes over its spot
        float circleTime = laps * lapTime;
        int[] order = DropOrder(entry, circleTime, out float[] dropAt);
        int next = 0;
        for (float t = 0; t < circleTime; t += Time.deltaTime)
        {
            float a = entry + 2 * Mathf.PI * t / lapTime;
            MoveSleigh(OnCircle(center, a) + Vector3.up * 0.04f * Mathf.Sin(t * 3f));
            while (next < order.Length && t >= dropAt[next])
                StartCoroutine(DropGift(order[next++], sleigh.localPosition));
            yield return null;
        }
        while (next < order.Length) StartCoroutine(DropGift(order[next++], sleigh.localPosition));

        // 3. depart: a small dip (anticipation), then accelerate away into the distance
        Vector3 exit = OnCircle(center, entry);
        Vector3 d1 = exit + tangent * handle;
        Vector3 d2 = flyOutTo + (exit - flyOutTo) * 0.3f + Vector3.up * 0.5f;
        PlaySfx(whooshClip);
        StartCoroutine(FadeAudio(bells, bells.volume, 0, approachTime, false));
        yield return Ease.Run(approachTime, t =>
        {
            float dip = -0.06f * Mathf.Sin(Mathf.PI * Mathf.Min(t / 0.15f, 1f));
            MoveSleigh(Bezier(exit, d1, d2, flyOutTo, 0.5f * t + 0.5f * Ease.InCubic(t)) + Vector3.up * dip);
        });
        fx.trail.Stop();
        sleigh.gameObject.SetActive(false);

        while (giftsLanded < giftSpots.Length) yield return null;
        SetState(State.GiftsReady);
    }

    void MoveSleigh(Vector3 pos)
    {
        Vector3 delta = pos - sleighPrev;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        if (delta.sqrMagnitude > 1e-10f)
        {
            Vector3 forward = delta.normalized;
            // bank into turns: roll follows how fast the heading swings (left turn → roll left)
            float yawRate = Vector3.SignedAngle(Flat(sleighForward), Flat(forward), Vector3.up) / dt;
            sleighRoll = Mathf.Lerp(sleighRoll, Mathf.Clamp(-yawRate * 0.3f, -35f, 35f), 1 - Mathf.Exp(-5f * dt));
            sleighForward = Vector3.Slerp(sleighForward, forward, 1 - Mathf.Exp(-10f * dt));
        }
        sleigh.localPosition = pos;
        sleigh.localRotation = Quaternion.LookRotation(sleighForward, Vector3.up) * Quaternion.Euler(0, 0, sleighRoll);
        sleighPrev = pos;
    }

    IEnumerator DropGift(int i, Vector3 from)
    {
        var gift = Instantiate(giftTemplates[i % giftTemplates.Length], content);
        gift.SetActive(true);
        spawned.Add(gift);
        var box = gift.GetComponent<GiftBox>();
        box.controller = this;
        Transform g = gift.transform;
        Vector3 to = giftSpots[i].localPosition;
        Vector3 full = g.localScale;
        Quaternion rest = Quaternion.Euler(0, Random.Range(-25f, 25f), 0); // roughly facing the camera
        Vector3 spinAxis = Random.onUnitSphere;

        // falls in an arc: horizontal eases out while the drop accelerates in
        yield return Ease.Run(1.1f, t =>
        {
            Vector3 p = Vector3.Lerp(from, to, Ease.OutQuad(t));
            p.y = Mathf.Lerp(from.y, to.y, Ease.InQuad(t));
            g.localPosition = p;
            g.localRotation = rest * Quaternion.AngleAxis(540f * (1 - Ease.OutCubic(t)), spinAxis);
            g.localScale = full * Mathf.Lerp(0.4f, 1f, Ease.OutCubic(t));
        });

        // landing: snow puff, squash, a hop with stretch, then settle
        fx.Puff(g.position, 10);
        PlaySfx(landClip, 0.7f);
        yield return Ease.Run(0.08f, t => g.localScale = Squash(full, 0.3f * Ease.OutQuad(t)));
        yield return Ease.Run(0.35f, t =>
        {
            g.localPosition = to + Vector3.up * 0.05f * Mathf.Sin(Mathf.PI * t);
            float a = t < 0.3f ? Mathf.Lerp(0.3f, -0.12f, Ease.OutQuad(t / 0.3f))
                               : Mathf.Lerp(-0.12f, 0f, Ease.OutCubic((t - 0.3f) / 0.7f));
            g.localScale = Squash(full, a);
        });
        yield return Ease.Run(0.06f, t => g.localScale = Squash(full, 0.1f * Ease.OutQuad(t)));
        yield return Ease.Run(0.2f, t => g.localScale = Squash(full, 0.1f * (1 - Ease.OutBack(t))));
        g.localPosition = to;
        g.localScale = full;

        box.SetReady();
        treeLights.Jiggle(0.35f); // the tree gives a happy little wobble for each gift
        giftsLanded++;
    }

    // ---------- Sequence 3: opening gifts ----------

    void Update()
    {
        if (Time.timeScale == 0) return;
        if (bell.gameObject.activeSelf) SwingBell();
        CheckLeanIn();
        if (!TapStarted(out Vector2 screen)) return;
        var hits = Physics.RaycastAll(arCamera.ScreenPointToRay(screen), 100f);

        // gifts win over the tree, since they sit under its branches
        if (Current == State.GiftsReady || Current == State.GoldenGift)
            foreach (var hit in hits)
            {
                var box = hit.collider.GetComponentInParent<GiftBox>();
                if (box && !box.Opened) { box.Open(); return; }
            }

        if (Current == State.BellReady)
            foreach (var hit in hits)
                if (hit.collider.transform.IsChildOf(bell)) { RingBell(); return; }

        if (!treeReady || Time.time < nextTreePoke) return;
        foreach (var hit in hits)
            if (hit.collider.transform == tree) { PokeTree(hit.point); return; }
    }

    // Tapping the tree any time after it's up: jelly wobble, sparkles where you touched and on the star.
    void PokeTree(Vector3 point)
    {
        nextTreePoke = Time.time + 0.35f;
        treeLights.Jiggle(1f);
        fx.Sparkle(point, 25);
        fx.Sparkle(treeLights.star.position, 20);
        PlaySfx(treeTapClip ? treeTapClip : sparkleClip, 0.8f);
    }

    public void OnGiftOpening() => SetState(State.Opening);
    public void PlayPop() => PlaySfx(popClip);
    public void ReleaseReward(Vector3 top, bool golden) => StartCoroutine(RewardRoutine(top, golden));

    IEnumerator RewardRoutine(Vector3 top, bool golden)
    {
        var reward = golden ? PickRare() : PickReward(); // Santa's golden present is always rare
        var go = Instantiate(reward.prefab, content);
        go.SetActive(true);
        spawned.Add(go);
        Transform r = go.transform;
        Vector3 start = content.InverseTransformPoint(top);
        Vector3 end = start + Vector3.up * 0.22f;
        Vector3 full = r.localScale * (golden ? 1.6f : reward.rare ? 1.35f : 1f);
        Quaternion facing = r.localRotation;
        r.localPosition = start;
        r.localScale = Vector3.zero;

        if (reward.rare) { fx.Rare(top); PlaySfx(rareClip); }
        else PlaySfx(sparkleClip);
        if (golden) { fx.Firework(top + content.up * 0.3f); fx.Confetti(top); }

        // floats up out of the box with an overshoot pop and one full turn
        yield return Ease.Run(0.8f, k =>
        {
            r.localPosition = Vector3.Lerp(start, end, Ease.OutCubic(k));
            r.localScale = full * Ease.OutBack(Mathf.Min(k / 0.7f, 1f));
            r.localRotation = Quaternion.Euler(0, 360f * Ease.OutCubic(k), 0) * facing;
        });
        var spin = go.AddComponent<Spin>(); // slow idle spin + bob
        spin.degreesPerSecond = new Vector3(0, 40, 0);
        spin.bobHeight = 0.015f;
        ShowLabel(r, reward);

        yield return new WaitForSeconds(0.6f);
        if (golden) { StartCoroutine(FinishSequence()); yield break; }
        giftsOpened++;
        if (giftsOpened >= giftSpots.Length) StartCoroutine(BellSequence());
        else SetState(State.GiftsReady);
    }

    Reward PickRare()
    {
        var rare = System.Array.FindAll(rewards, r => r.rare);
        return rare.Length > 0 ? rare[Random.Range(0, rare.Length)] : PickReward();
    }

    Reward PickReward()
    {
        float total = 0;
        foreach (var r in rewards) total += r.weight;
        float roll = Random.value * total;
        foreach (var r in rewards)
            if ((roll -= r.weight) <= 0) return r;
        return rewards[rewards.Length - 1];
    }

    void ShowLabel(Transform target, Reward reward)
    {
        var label = Instantiate(labelTemplate, labelTemplate.transform.parent);
        label.text.text = reward.rare ? "RARE!\n" + reward.name : reward.name;
        label.text.color = reward.rare ? new Color(1f, 0.84f, 0.3f) : Color.white;
        label.target = target;
        label.lifetime = reward.rare ? 3.5f : 2.5f; // rare finds stay up a bit longer
        label.gameObject.SetActive(true);
        spawned.Add(label.gameObject);
    }

    // ---------- Sequence 4: ring the bell, Santa visits with a golden present ----------

    IEnumerator BellSequence()
    {
        yield return new WaitForSeconds(0.6f);
        rings = 0;
        UpdateSpirit();
        SetState(State.BellReady);
        bell.gameObject.SetActive(true);
        fx.Sparkle(bell.position, 30);
        PlaySfx(sparkleClip);
        yield return Ease.Run(0.5f, t => bell.localScale = bellScale * Ease.OutBack(t));
    }

    // A gentle idle swing, plus a big decaying swing right after each ring.
    void SwingBell()
    {
        float since = Time.time - bellRungAt;
        float swing = 6f * Mathf.Sin(Time.time * 2f) + (since < 2f ? 35f * Mathf.Exp(-3f * since) * Mathf.Sin(since * 12f) : 0);
        bell.localRotation = Quaternion.Euler(0, 0, swing);
    }

    void RingBell()
    {
        if (Time.time - bellRungAt < 0.4f) return;
        bellRungAt = Time.time;
        rings++;
        PlaySfx(bellClip ? bellClip : sparkleClip);
        fx.Sparkle(bell.position, 35);
        treeLights.Jiggle(0.6f);
        UpdateSpirit();
        if (rings >= ringsNeeded) StartCoroutine(SantaVisit());
        else SetState(State.BellReady); // refreshes "(n/3)"
    }

    void UpdateSpirit()
    {
        for (int i = 0; i < spiritStars.Length; i++)
            spiritStars[i].color = i < rings ? new Color(1f, 0.82f, 0.25f) : new Color(1, 1, 1, 0.3f);
        if (rings > 0 && rings <= spiritStars.Length)
        {
            var star = spiritStars[rings - 1].transform; // the star that just lit pops
            StartCoroutine(Ease.Run(0.35f, t => star.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, Ease.OutBack(t, 3f))));
        }
    }

    IEnumerator SantaVisit()
    {
        SetState(State.SantaVisit);
        if (peek != null) { StopCoroutine(peek); peek = null; }

        // the bell has done its job
        yield return Ease.Run(0.35f, t => bell.localScale = bellScale * (1 - Ease.InBack(t)));
        bell.gameObject.SetActive(false);

        // Santa walks in from the edge of the card
        santa.gameObject.SetActive(true);
        santa.localScale = santaScale;
        santa.localPosition = santaEnter.localPosition;
        santa.localRotation = Quaternion.LookRotation(santaStand.localPosition - santaEnter.localPosition, Vector3.up);
        fx.Puff(santa.position, 16);
        PlaySfx(whooshClip, 0.5f);
        santaAnim.CrossFade("Walk", 0.1f);
        yield return Ease.Run(2.2f, t =>
            santa.localPosition = Vector3.Lerp(santaEnter.localPosition, santaStand.localPosition, Ease.InOutSine(t)));

        // turns to you, waves, and talks
        Quaternion from = santa.localRotation, toCamera = Quaternion.Euler(0, 180, 0);
        santaAnim.CrossFade("Wave", 0.25f);
        yield return Ease.Run(0.4f, t => santa.localRotation = Quaternion.Slerp(from, toCamera, Ease.OutCubic(t)));
        guideText.text = "Ho ho ho!\nSanta has a surprise for you!";
        yield return new WaitForSeconds(2.2f);
        santaAnim.CrossFade("Talk", 0.3f);
        yield return new WaitForSeconds(1.2f);

        // the golden present pops down beside him
        var gift = Instantiate(goldenGiftTemplate, content);
        gift.SetActive(true);
        spawned.Add(gift);
        gift.GetComponent<GiftBox>().controller = this;
        Transform g = gift.transform;
        Vector3 full = g.localScale;
        g.localPosition = goldenSpot.localPosition;
        g.localRotation = Quaternion.Euler(0, Random.Range(-15f, 15f), 0);
        fx.Rare(g.position);
        PlaySfx(rareClip, 0.8f);
        yield return Ease.Run(0.5f, t => g.localScale = full * Ease.OutBack(t, 2f));
        gift.GetComponent<GiftBox>().SetReady();
        santaAnim.CrossFade("Look", 0.4f);
        SetState(State.GoldenGift);
    }

    // ---------- Bonus: lean in close and Santa peeks out ----------

    void CheckLeanIn()
    {
        bool allowed = !peekDone && treeReady && !santa.gameObject.activeSelf &&
                       (Current == State.TreeUp || Current == State.GiftsReady);
        if (!allowed) { closeSince = -1; return; }
        Vector3 cam = content.InverseTransformPoint(arCamera.transform.position);
        bool close = (cam - (tree.localPosition + Vector3.up * 0.6f)).magnitude < leanDistance;
        if (!close) closeSince = -1;
        else if (closeSince < 0) closeSince = Time.time;
        else if (Time.time - closeSince > 0.3f) { peekDone = true; peek = StartCoroutine(SantaPeek()); }
    }

    IEnumerator SantaPeek()
    {
        santa.gameObject.SetActive(true);
        santa.localScale = santaScale;
        santa.localPosition = peekHide.localPosition;
        santa.localRotation = Quaternion.LookRotation(peekShow.localPosition - peekHide.localPosition, Vector3.up);
        santaAnim.CrossFade("Walk", 0.1f);
        yield return Ease.Run(0.8f, t => santa.localPosition = Vector3.Lerp(peekHide.localPosition, peekShow.localPosition, Ease.OutCubic(t)));

        Quaternion from = santa.localRotation, toCamera = Quaternion.Euler(0, 180, 0);
        santaAnim.CrossFade("Wave", 0.2f);
        PlaySfx(treeTapClip, 0.8f);
        guideText.text = "Santa spotted you!";
        yield return Ease.Run(0.3f, t => santa.localRotation = Quaternion.Slerp(from, toCamera, Ease.OutCubic(t)));
        yield return new WaitForSeconds(1.6f);

        // runs off with a flip and vanishes in a puff of snow
        Vector3 start = santa.localPosition;
        santa.localRotation = Quaternion.LookRotation(peekExit.localPosition - start, Vector3.up);
        santaAnim.CrossFade("Flip", 0.1f);
        PlaySfx(whooshClip, 0.6f);
        yield return Ease.Run(1.3f, t => santa.localPosition = Vector3.Lerp(start, peekExit.localPosition, Ease.InQuad(t)));
        fx.Puff(santa.position, 18);
        yield return Ease.Run(0.25f, t => santa.localScale = santaScale * (1 - Ease.InBack(t)));
        santa.gameObject.SetActive(false);
        SetState(Current); // put the guide text back
        peek = null;
    }

    // ---------- Finale ----------

    IEnumerator FinishSequence()
    {
        yield return new WaitForSeconds(0.8f);
        SetState(State.Finished);
        treeLights.Jiggle(1.5f); // the tree celebrates too
        fx.confettiRain.Play();
        StartCoroutine(FinaleMusic());
        santaAnim.CrossFade("Dance", 0.3f);

        Vector3 sky = tree.localPosition + Vector3.up * 1.1f;
        while (true)
        {
            var p = sky + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0f, 0.5f), Random.Range(-0.3f, 0.4f));
            fx.Firework(content.TransformPoint(p));
            PlaySfx(fireworksClip, 0.6f);
            yield return new WaitForSeconds(Random.Range(0.45f, 0.9f));
        }
    }

    // Fade the carol out, play the finale tune once, then bring the carol back.
    IEnumerator FinaleMusic()
    {
        yield return FadeAudio(music, music.volume, 0, 0.6f, false);
        if (finaleClip)
        {
            music.clip = finaleClip;
            music.loop = false;
            music.volume = musicVolume;
            music.Play();
            yield return new WaitForSeconds(finaleClip.length);
        }
        music.clip = mainMusic;
        music.loop = true;
        yield return FadeAudio(music, 0, musicVolume, 2f, true);
    }

    // ---------- Helpers ----------

    void SetState(State s)
    {
        Current = s;
        startScreen.SetActive(s == State.Scanned);
        hud.SetActive(s >= State.TreeUp);
        getGiftButton.gameObject.SetActive(s == State.TreeUp || s == State.SantaArriving);
        getGiftButton.interactable = s == State.TreeUp && treeReady;
        finishScreen.SetActive(s == State.Finished);
        spiritMeter.SetActive(s == State.BellReady);

        int left = giftSpots.Length - giftsOpened;
        switch (s)
        {
            case State.Scanned: guideText.text = "Card found! Tap Start to begin"; break;
            case State.TreeUp:
                guideText.text = treeReady ? "Tap \"Get Your Gift\" to call Santa!\nTip: poke the tree!" : "Watch your Christmas tree grow...";
                break;
            case State.SantaArriving: guideText.text = "Here comes Santa!\nListen for the sleigh bells..."; break;
            case State.GiftsReady: guideText.text = $"Tap a gift to open it!  ({left} left)"; break;
            case State.Opening: guideText.text = "What's inside?"; break;
            case State.BellReady: guideText.text = $"Ring the bell to call Santa!  ({rings}/{ringsNeeded})"; break;
            case State.SantaVisit: guideText.text = "Santa is coming!"; break;
            case State.GoldenGift: guideText.text = "Tap Santa's golden present!"; break;
            case State.Finished: guideText.text = "Merry Christmas from Santa!"; break;
        }
    }

    void ResetScene()
    {
        foreach (var go in spawned) if (go) Destroy(go);
        spawned.Clear();
        giftsLanded = giftsOpened = 0;
        treeReady = false;
        treeLights.enabled = false;
        tree.localRotation = treeRotation;
        tree.gameObject.SetActive(false);
        sleigh.gameObject.SetActive(false);
        santa.gameObject.SetActive(false);
        santa.localScale = santaScale;
        bell.gameObject.SetActive(false);
        bell.localScale = bellScale;
        rings = 0;
        peekDone = false;
        closeSince = -1;
        peek = null;
        fx.StopAll();
        music.Stop();
        music.clip = mainMusic;
        music.loop = true;
        bells.Stop();
    }

    void PuffRing(float radius, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i * 2 * Mathf.PI / count;
            fx.Puff(content.TransformPoint(tree.localPosition + radius * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))), 6);
        }
    }

    void PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (clip) sfx.PlayOneShot(clip, volume);
    }

    IEnumerator FadeAudio(AudioSource source, float from, float to, float duration, bool play)
    {
        if (play) source.Play();
        yield return Ease.Run(duration, t => source.volume = Mathf.Lerp(from, to, Ease.InOutSine(t)));
        if (to <= 0) source.Stop();
    }

    // Entry angle whose anticlockwise tangent best matches the direction Santa arrives from.
    float BestEntryAngle(Vector3 center)
    {
        float best = 0, bestDot = -2;
        for (int i = 0; i < 72; i++)
        {
            float a = i * Mathf.PI / 36;
            float dot = Vector3.Dot(Tangent(a), (OnCircle(center, a) - flyInFrom).normalized);
            if (dot > bestDot) { bestDot = dot; best = a; }
        }
        return best;
    }

    // When to drop each gift: as the sleigh passes over its spot, after it has settled into the circle.
    int[] DropOrder(float entry, float circleTime, out float[] times)
    {
        int n = giftSpots.Length;
        times = new float[n];
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 d = giftSpots[i].localPosition - tree.localPosition;
            float t = Mathf.Repeat(Mathf.Atan2(d.z, d.x) - entry, 2 * Mathf.PI) / (2 * Mathf.PI) * lapTime;
            if (t < 0.3f * lapTime) t += lapTime;
            times[i] = Mathf.Min(t, circleTime - 0.5f);
            order[i] = i;
        }
        System.Array.Sort(times, order);
        return order;
    }

    Vector3 OnCircle(Vector3 center, float a) => center + circleRadius * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
    static Vector3 Tangent(float a) => new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        float v = 1 - u;
        return v * v * v * p0 + 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u * p3;
    }

    // amount > 0 squashes (wider, shorter); < 0 stretches.
    static Vector3 Squash(Vector3 full, float amount) =>
        Vector3.Scale(full, new Vector3(1 + amount * 0.6f, 1 - amount, 1 + amount * 0.6f));

    static bool TapStarted(out Vector2 pos)
    {
        pos = default;
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Began || EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return false;
            pos = touch.position;
            return true;
        }
        if (!Input.GetMouseButtonDown(0) || EventSystem.current.IsPointerOverGameObject()) return false;
        pos = Input.mousePosition;
        return true;
    }
}
