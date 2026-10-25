using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runs the Christmas card as a state machine:
//   Idle → Scanned → HouseUp → SantaArriving → Reveal → GiftsReady ⇄ Opening → BellReady → SantaVisit → GoldenGift → Finished
// A snowy house rises from the card; Santa's sleigh drops the gifts down the chimney; the house opens like a dollhouse
// (roof lifts off, front wall folds down); the gifts fall into the room; tapping one sends a kid running to open it;
// three bell rings open the back door, the kids run to greet Santa, and he gives them a golden present;
// after the party he waves goodbye and walks back out.
// CardHub decides when the card is found or lost and shows/hides this experience.
// Losing the card pauses everything (Time.timeScale = 0) and shows "Find the card again"; progress is kept.
// All references are filled in by Tools > Christmas Card > 2. Build Greeting Card.
public class GreetingCardController : MonoBehaviour
{
    public enum State { Idle, Scanned, HouseUp, SantaArriving, Reveal, GiftsReady, Opening, BellReady, SantaVisit, GoldenGift, Finished }

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
    public Transform house;        // pivot at the centre of its base
    public Transform roof;         // roof + attic: lifts off in the reveal
    public Transform frontWall;    // the wall facing the camera: folds down in the reveal (pivot on its bottom edge)
    public Transform door;         // the back door Santa comes through
    public Transform chimneyTop;   // gifts go down here
    public float houseHeight = 1.5f;
    public Transform tree;         // inside the house, pivot at its base
    public TreeLights treeLights;
    public Transform sleigh;       // pivot at its centre, reindeer facing +Z
    public Transform santa;
    public Transform[] kids;
    public Transform[] kidHomes;   // where the kids wait when they aren't busy
    public Transform[] giftSpots;  // inside, around the tree
    public float giftDropHeight = 0.5f; // how far above the floor the gifts fall from
    public GameObject[] giftTemplates;
    public Reward[] rewards;
    public FxLibrary fx;

    [Header("Santa's flight (card units)")]
    public float circleRadius = 0.75f;
    public float circleHeight = 0.8f;
    public float flightClearance = 0.45f; // the sleigh circles at least this far above the top of the house...
    public float maxFlightRadius = 0.9f;  // ...and no wider than this, so it stays over the house and in view
    public int laps = 2;
    public float lapTime = 4f;
    public float approachTime = 3f;
    public Vector3 flyInFrom = new Vector3(3f, 1.8f, 4f);
    public Vector3 flyOutTo = new Vector3(-3.5f, 2.5f, 4.5f);

    [Header("Ring the bell, Santa visits")]
    public Transform bell;                         // hanger: pivot at the top so the bell swings
    public int ringsNeeded = 3;
    public Transform santaOutside, santaStand, goldenSpot; // drag these in the scene to adjust
    public GameObject goldenGiftTemplate;

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
    int giftsLanded, giftsOpened, kidsArrived;
    bool houseReady, revealed, muted;
    float nextTreePoke;
    Quaternion treeRotation, doorRotation;
    Vector3 santaScale;
    Vector3 sleighPrev, sleighForward = Vector3.forward;
    float sleighRoll;
    AudioClip mainMusic;
    Animator santaAnim;
    Animator[] kidAnims;
    Vector3 bellScale;
    int rings;
    float bellRungAt = -10f;
    Vector3 roofPos, roofScale, wallScale;
    Quaternion roofRot, wallRot;
    float flightRadius, flightHeight;
    bool[] kidBusy;            // on the way to a gift or opening it
    Coroutine[] kidRoutines;   // reacting / walking home; cancelled when the kid is needed again
    FireCrackle fire;

    void Awake()
    {
        Application.targetFrameRate = 60;
        treeRotation = tree.localRotation;
        doorRotation = door.localRotation;
        santaScale = santa.localScale;
        santaAnim = santa.GetComponentInChildren<Animator>();
        kidAnims = System.Array.ConvertAll(kids, k => k.GetComponentInChildren<Animator>());
        kidBusy = new bool[kids.Length];
        kidRoutines = new Coroutine[kids.Length];
        fire = FireCrackle.Create(house, fx.sparkle.GetComponent<ParticleSystemRenderer>().sharedMaterial);
        bellScale = bell.localScale;
        roofPos = roof.localPosition; roofRot = roof.localRotation; roofScale = roof.localScale;
        wallRot = frontWall.localRotation; wallScale = frontWall.localScale;
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
        if (Current == State.Scanned) StartCoroutine(HouseUpSequence());
    }

    public void GetYourGift()
    {
        if (Current == State.HouseUp && houseReady) StartCoroutine(SantaSequence());
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
        StartCoroutine(HouseUpSequence());
    }

    // ---------- Sequence 1: the snowy house rises ----------

    IEnumerator HouseUpSequence()
    {
        houseReady = false;
        SetState(State.HouseUp);
        house.gameObject.SetActive(true);
        house.localScale = new Vector3(0.8f, 0.001f, 0.8f);

        // anticipation: sparkles gather on the card before the house breaks through
        fx.sparkleRing.Play();
        PlaySfx(sparkleClip);
        yield return new WaitForSeconds(0.4f);

        fx.glitter.Play();
        PuffRing(Vector3.zero, 0.35f, 8);
        yield return Ease.Run(2f, t =>
        {
            float xz = Mathf.Lerp(0.8f, 1f, Ease.OutCubic(t));
            house.localScale = new Vector3(xz, Mathf.Max(0.001f, Ease.OutBack(t, 1.2f)), xz); // small bounce at the end
        });
        fx.glitter.Stop();
        fx.sparkleRing.Stop();
        PuffRing(Vector3.zero, 0.45f, 10);

        // a cosy glow: a golden burst on the chimney, smoke starts, snow falls, music fades in
        fx.Rare(chimneyTop.position);
        PlaySfx(rareClip, 0.7f);
        fx.chimneySmoke.Play();
        fx.snowfall.Play();
        StartCoroutine(FadeAudio(music, 0, musicVolume, 2.5f, true));
        houseReady = true;
        SetState(State.HouseUp); // enables "Get Your Gift" and updates the guide
    }

    // ---------- Sequence 2: Santa circles the house and drops the gifts down the chimney ----------

    IEnumerator SantaSequence()
    {
        SetState(State.SantaArriving);

        flightRadius = Mathf.Min(circleRadius, maxFlightRadius);
        flightHeight = Mathf.Max(circleHeight, houseHeight + flightClearance);
        Vector3 center = Vector3.up * flightHeight;
        float entry = BestEntryAngle(center);
        Vector3 entryPoint = OnCircle(center, entry);
        Vector3 tangent = Tangent(entry);
        // Bezier handle sized so the sleigh joins the circle at exactly the circling speed
        // (the arrive/depart easings below have slope 0.5 at the joint).
        float speed = 2 * Mathf.PI * flightRadius / lapTime;
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

        // 1. approach: quick from far away, easing out as it reaches the house
        Vector3 a1 = flyInFrom + (entryPoint - flyInFrom) * 0.35f + Vector3.up * 0.4f;
        Vector3 a2 = entryPoint - tangent * handle;
        yield return Ease.Run(approachTime, t =>
            MoveSleigh(Bezier(flyInFrom, a1, a2, entryPoint, 0.5f * t + 0.5f * Ease.OutCubic(t))));

        // 2. circle the house, tossing a gift down the chimney now and then
        float circleTime = laps * lapTime;
        int dropped = 0, toDrop = giftSpots.Length;
        for (float t = 0; t < circleTime; t += Time.deltaTime)
        {
            float a = entry + 2 * Mathf.PI * t / lapTime;
            MoveSleigh(OnCircle(center, a) + Vector3.up * 0.04f * Mathf.Sin(t * 3f));
            if (dropped < toDrop && t >= circleTime * (dropped + 1) / (toDrop + 1))
                StartCoroutine(DropIntoChimney(dropped++, sleigh.localPosition));
            yield return null;
        }
        while (dropped < toDrop) StartCoroutine(DropIntoChimney(dropped++, sleigh.localPosition));

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

        StartCoroutine(RevealSequence());
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

    // A gift arcs from the sleigh into the chimney and disappears with a puff of smoke.
    IEnumerator DropIntoChimney(int i, Vector3 from)
    {
        var gift = Instantiate(giftTemplates[i % giftTemplates.Length], content);
        gift.SetActive(true);
        Transform g = gift.transform;
        Vector3 full = g.localScale, spinAxis = Random.onUnitSphere;
        Vector3 to = content.InverseTransformPoint(chimneyTop.position);
        yield return Ease.Run(1f, t =>
        {
            Vector3 p = Vector3.Lerp(from, to, Ease.OutQuad(t));
            p.y = Mathf.Lerp(from.y, to.y, Ease.InQuad(t));
            g.localPosition = p;
            g.localRotation = Quaternion.AngleAxis(540f * t, spinAxis);
            g.localScale = full * Mathf.Lerp(0.6f, 0.3f, Ease.InQuad(t)); // shrinks to slip down the chimney
        });
        fx.Puff(chimneyTop.position, 14);
        PlaySfx(popClip, 0.5f);
        Destroy(gift);
    }

    // ---------- Sequence 3: the house opens like a dollhouse; the gifts fall into the room ----------

    IEnumerator RevealSequence()
    {
        SetState(State.Reveal);
        fx.chimneySmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        PlaySfx(whooshClip, 0.8f);

        // the roof dips (anticipation), lifts off with a tilt, then shrinks away into the sky
        StartCoroutine(Ease.Run(1.4f, t =>
        {
            float dip = -0.03f * Mathf.Sin(Mathf.PI * Mathf.Min(t / 0.15f, 1f));
            roof.localPosition = roofPos + Vector3.up * (dip + 0.9f * Ease.InCubic(t));
            roof.localRotation = roofRot * Quaternion.Euler(0, 0, 14f * Ease.InQuad(t));
            roof.localScale = roofScale * (1 - Ease.InBack(Mathf.Clamp01((t - 0.55f) / 0.45f)));
        }));
        yield return new WaitForSeconds(0.5f);

        // the front wall folds down toward you with a bounce, then sinks away
        foreach (var kid in kids) kid.gameObject.SetActive(true);
        yield return Ease.Run(0.9f, t => frontWall.localRotation = wallRot * Quaternion.Euler(-90f * Ease.OutBounce(t), 0, 0));
        fx.Puff(frontWall.position, 20);
        Haptics.Buzz(60, Haptics.Medium); // the wall thumps down
        yield return Ease.Run(0.35f, t => frontWall.localScale = wallScale * (1 - Ease.InBack(t)));
        roof.gameObject.SetActive(false);
        frontWall.gameObject.SetActive(false);

        // inside: the tree lights up and the kids wave hello
        revealed = true;
        fx.Rare(treeLights.star.position);
        PlaySfx(rareClip, 0.7f);
        treeLights.enabled = true;
        treeLights.Jiggle(0.6f);
        fx.orbitStars.Play();
        if (fire) // the fireplace crackles to life
        {
            fire.Ignite();
            PlaySfx(whooshClip, 0.4f);
            Haptics.Buzz(30, Haptics.Light);
        }
        foreach (var anim in kidAnims) anim.CrossFade("Wave", 0.2f);
        guideText.text = "Look out above!";
        yield return new WaitForSeconds(0.8f);

        // the gifts drop in from above, one after another
        giftsLanded = 0;
        for (int i = 0; i < giftSpots.Length; i++)
        {
            StartCoroutine(DropGift(i, giftSpots[i].localPosition + Vector3.up * giftDropHeight));
            yield return new WaitForSeconds(0.4f);
        }
        while (giftsLanded < giftSpots.Length) yield return null;
        foreach (var anim in kidAnims) anim.CrossFade("Look", 0.3f);
        SetState(State.GiftsReady);
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
        yield return Ease.Run(0.8f, t =>
        {
            Vector3 p = Vector3.Lerp(from, to, Ease.OutQuad(t));
            p.y = Mathf.Lerp(from.y, to.y, Ease.InQuad(t));
            g.localPosition = p;
            g.localRotation = rest * Quaternion.AngleAxis(360f * (1 - Ease.OutCubic(t)), spinAxis);
            g.localScale = full * Mathf.Lerp(0.4f, 1f, Ease.OutCubic(t));
        });

        // landing: snow puff, squash, a hop with stretch, then settle
        fx.Puff(g.position, 10);
        PlaySfx(landClip, 0.7f);
        Haptics.Buzz(25, Haptics.Light);
        yield return Ease.Run(0.08f, t => g.localScale = Squash(full, 0.3f * Ease.OutQuad(t)));
        yield return Ease.Run(0.35f, t =>
        {
            g.localPosition = to + Vector3.up * 0.04f * Mathf.Sin(Mathf.PI * t);
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

    // ---------- Sequence 4: tap a gift, a kid runs over and opens it ----------

    void Update()
    {
        if (Time.timeScale == 0) return;
        if (bell.gameObject.activeSelf) SwingBell();
        if (!TapStarted(out Vector2 screen)) return;
        var hits = Physics.RaycastAll(arCamera.ScreenPointToRay(screen), 100f);

        // gifts win over the tree, since they sit right next to it
        if (Current == State.GiftsReady || Current == State.GoldenGift)
            foreach (var hit in hits)
            {
                var box = hit.collider.GetComponentInParent<GiftBox>();
                if (box && !box.Opened && !box.Claimed) { StartCoroutine(KidOpens(box)); return; }
            }

        if (Current == State.BellReady)
            foreach (var hit in hits)
                if (hit.collider.transform.IsChildOf(bell)) { RingBell(); return; }

        if (!revealed || Time.time < nextTreePoke) return;
        foreach (var hit in hits)
            if (hit.collider.transform == tree) { PokeTree(hit.point); return; }
    }

    // The nearest free kid runs to the gift you tapped and opens it. When the reward pops out, both kids react
    // (they cheer longer for a rare find), then walk back to their spots.
    IEnumerator KidOpens(GiftBox box)
    {
        box.Claimed = true;
        SetState(State.Opening);
        Vector3 giftPos = box.transform.localPosition;
        int k;
        while ((k = PickKid(giftPos)) < 0) yield return null; // both kids are busy: wait for one to be free
        kidBusy[k] = true;
        StopKidRoutine(k);
        Transform kid = kids[k];
        Animator anim = kidAnims[k];

        Vector3 away = Flat(kid.localPosition - giftPos);
        Vector3 stop = giftPos + (away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.back) * 0.09f;
        yield return RunTo(kid, anim, stop);
        yield return Face(kid, giftPos);

        anim.CrossFade("Look", 0.15f);
        box.Open();
        for (float wait = 0; !box.RewardShown && wait < 3f; wait += Time.deltaTime) yield return null;
        kidBusy[k] = false;

        bool strong = box.RewardRare;
        kidRoutines[k] = StartCoroutine(KidReacts(k, giftPos, strong));
        for (int j = 0; j < kids.Length; j++)
            if (j != k && !kidBusy[j])
            {
                StopKidRoutine(j);
                kidRoutines[j] = StartCoroutine(KidReacts(j, giftPos, strong));
            }
    }

    // The nearest kid who isn't on the way to a gift; -1 if all of them are.
    int PickKid(Vector3 giftPos)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < kids.Length; i++)
        {
            if (kidBusy[i]) continue;
            float d = Vector3.Distance(Flat(kids[i].localPosition), Flat(giftPos));
            if (d < bestDistance) { best = i; bestDistance = d; }
        }
        return best;
    }

    // Turns to the gift and cheers, then walks back to its spot and settles down.
    IEnumerator KidReacts(int k, Vector3 giftPos, bool strong)
    {
        Transform kid = kids[k];
        Animator anim = kidAnims[k];
        yield return Face(kid, giftPos);
        anim.CrossFade("Cheer", 0.2f);
        yield return new WaitForSeconds(strong ? 3.5f : 1.8f);
        yield return RunTo(kid, anim, kidHomes[k].localPosition);
        Quaternion from = kid.localRotation, home = kidHomes[k].localRotation;
        yield return Ease.Run(0.25f, t => kid.localRotation = Quaternion.Slerp(from, home, Ease.OutCubic(t)));
        anim.CrossFade("Look", 0.3f);
        kidRoutines[k] = null;
    }

    void StopKidRoutine(int k)
    {
        if (kidRoutines[k] != null) StopCoroutine(kidRoutines[k]);
        kidRoutines[k] = null;
    }

    void StopKidRoutines()
    {
        for (int i = 0; i < kids.Length; i++) StopKidRoutine(i);
    }

    // Both kids run to Santa's sides, turn to him and wave.
    IEnumerator KidsGreetSanta()
    {
        StopKidRoutines();
        Vector3 s = santaStand.localPosition;
        kidsArrived = 0;
        for (int i = 0; i < kids.Length; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            StartCoroutine(KidGreets(i, s + new Vector3(side * 0.2f, 0, -0.1f), s));
        }
        while (kidsArrived < kids.Length) yield return null;
    }

    IEnumerator KidGreets(int k, Vector3 spot, Vector3 santaPos)
    {
        yield return RunTo(kids[k], kidAnims[k], spot);
        yield return Face(kids[k], santaPos);
        kidAnims[k].CrossFade("Wave", 0.2f);
        kidsArrived++;
    }

    IEnumerator RunTo(Transform who, Animator anim, Vector3 to)
    {
        Vector3 from = who.localPosition;
        float distance = Vector3.Distance(Flat(from), Flat(to));
        if (distance < 0.01f) yield break;
        who.localRotation = Quaternion.LookRotation(Flat(to - from), Vector3.up);
        anim.CrossFade("Run", 0.1f);
        yield return Ease.Run(Mathf.Max(0.4f, distance / 0.5f), t => who.localPosition = Vector3.Lerp(from, to, Ease.InOutSine(t)));
    }

    IEnumerator Face(Transform who, Vector3 target)
    {
        Vector3 dir = Flat(target - who.localPosition);
        if (dir.sqrMagnitude < 1e-6f) yield break;
        Quaternion from = who.localRotation, to = Quaternion.LookRotation(dir, Vector3.up);
        yield return Ease.Run(0.2f, t => who.localRotation = Quaternion.Slerp(from, to, Ease.OutCubic(t)));
    }

    // Tapping the tree once it's inside and lit: jelly wobble, sparkles where you touched and on the star.
    void PokeTree(Vector3 point)
    {
        nextTreePoke = Time.time + 0.35f;
        treeLights.Jiggle(1f);
        fx.Sparkle(point, 25);
        fx.Sparkle(treeLights.star.position, 20);
        PlaySfx(treeTapClip ? treeTapClip : sparkleClip, 0.8f);
        Haptics.Buzz(20, Haptics.Light);
    }

    public void OnGiftOpening() => SetState(State.Opening);
    public void PlayPop()
    {
        PlaySfx(popClip);
        Haptics.Buzz(45, Haptics.Medium);
    }
    public void ReleaseReward(GiftBox box, Vector3 top) => StartCoroutine(RewardRoutine(box, top));

    IEnumerator RewardRoutine(GiftBox box, Vector3 top)
    {
        bool golden = box.golden;
        var reward = golden ? PickRare() : PickReward(); // Santa's golden present is always rare
        box.RewardRare = reward.rare || golden; // the kids react to this
        box.RewardShown = true;
        var go = Instantiate(reward.prefab, content);
        go.SetActive(true);
        spawned.Add(go);
        Transform r = go.transform;
        Vector3 start = content.InverseTransformPoint(top);
        Vector3 end = start + Vector3.up * 0.18f;
        Vector3 full = r.localScale * (golden ? 1.6f : reward.rare ? 1.35f : 1f);
        Quaternion facing = r.localRotation;
        r.localPosition = start;
        r.localScale = Vector3.zero;

        if (reward.rare) { fx.Rare(top); PlaySfx(rareClip); Haptics.Celebrate(); }
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
        spin.bobHeight = 0.012f;
        ShowLabel(r, reward);
        if (!golden) StartCoroutine(Vanish(r, box.transform, reward.rare ? 4f : 3f)); // Santa's present stays for the finale

        yield return new WaitForSeconds(0.6f);
        if (golden) { StartCoroutine(FinishSequence()); yield break; }
        giftsOpened++;
        if (giftsOpened >= giftSpots.Length) StartCoroutine(BellSequence());
        else SetState(State.GiftsReady);
    }

    // Opened gifts don't hang around: after a moment (once the name tag has faded) the reward spins up and
    // away in a sparkle, then the empty box shrinks into a puff of snow.
    IEnumerator Vanish(Transform reward, Transform box, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (reward)
        {
            var spin = reward.GetComponent<Spin>();
            if (spin) Destroy(spin);
            Vector3 from = reward.localPosition, size = reward.localScale;
            PlaySfx(sparkleClip, 0.5f);
            yield return Ease.Run(0.6f, t =>
            {
                reward.localPosition = from + Vector3.up * 0.12f * Ease.InCubic(t);
                reward.localRotation *= Quaternion.Euler(0, 720f * Time.deltaTime, 0);
                reward.localScale = size * (1 - Ease.InBack(t));
            });
            fx.Sparkle(reward.position, 20);
            Destroy(reward.gameObject);
        }
        if (box)
        {
            Vector3 size = box.localScale;
            fx.Puff(box.position, 10);
            yield return Ease.Run(0.35f, t => box.localScale = size * (1 - Ease.InBack(t)));
            Destroy(box.gameObject);
        }
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

    Reward PickRare()
    {
        var rare = System.Array.FindAll(rewards, r => r.rare);
        return rare.Length > 0 ? rare[Random.Range(0, rare.Length)] : PickReward();
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

    // ---------- Sequence 5: ring the bell, the door opens, Santa walks in with a golden present ----------

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
        Haptics.Buzz(40, Haptics.Medium);
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

        // the bell has done its job
        yield return Ease.Run(0.35f, t => bell.localScale = bellScale * (1 - Ease.InBack(t)));
        bell.gameObject.SetActive(false);

        // the back door swings open
        PlaySfx(whooshClip, 0.5f);
        Haptics.Buzz(50, Haptics.Medium);
        yield return Ease.Run(0.8f, t => door.localRotation = doorRotation * Quaternion.Euler(0, -100f * Ease.OutBack(t), 0));

        // Santa appears by magic just outside, then walks in through the door
        guideText.text = "Ho ho ho!";
        var inward = Quaternion.LookRotation(Flat(santaStand.localPosition - santaOutside.localPosition), Vector3.up);
        yield return SantaAppear(santaOutside.localPosition, inward);
        santaAnim.CrossFade("Walk", 0.2f);
        yield return Ease.Run(1.6f, t =>
            santa.localPosition = Vector3.Lerp(santaOutside.localPosition, santaStand.localPosition, Ease.InOutSine(t)));

        // the kids run over to greet him, and he talks to them
        yield return KidsGreetSanta();
        santaAnim.CrossFade("Talk", 0.3f);
        guideText.text = "Ho ho ho!\nSanta has a surprise for you!";
        yield return new WaitForSeconds(1.6f);

        // the golden present pops down in front of him
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
        foreach (var anim in kidAnims) anim.CrossFade("Look", 0.3f);
        SetState(State.GoldenGift);
    }

    // Santa never just pops in: a golden spiral gathers and snow puffs ring the spot (anticipation),
    // a flash, then he grows in with a full spin and an overshoot while waving, and squashes as he lands.
    IEnumerator SantaAppear(Vector3 at, Quaternion facing)
    {
        santa.gameObject.SetActive(true);
        santa.localPosition = at;
        santa.localScale = Vector3.zero;
        Vector3 world = content.TransformPoint(at);
        float yaw = facing.eulerAngles.y;

        fx.magicSwirl.transform.localPosition = at;
        fx.magicSwirl.Play();
        PlaySfx(sparkleClip);
        PuffRing(at, 0.1f, 8);
        yield return new WaitForSeconds(0.7f);
        fx.Rare(world + content.up * 0.2f);
        PlaySfx(rareClip, 0.8f);
        Haptics.Buzz(60, Haptics.Medium);
        santaAnim.CrossFade("Wave", 0.05f);

        yield return Ease.Run(0.7f, t =>
        {
            santa.localScale = santaScale * Ease.OutBack(t, 1.4f);
            santa.localRotation = Quaternion.Euler(0, yaw - 360f * (1 - Ease.OutCubic(t)), 0);
        });
        yield return Ease.Run(0.25f, t => santa.localScale = Squash(santaScale, 0.12f * Mathf.Sin(Mathf.PI * t)));
        fx.magicSwirl.Stop();
        santa.localScale = santaScale;
        yield return new WaitForSeconds(0.8f); // let the wave finish before he moves on
    }

    // ---------- Finale ----------

    IEnumerator FinishSequence()
    {
        yield return new WaitForSeconds(0.8f);
        SetState(State.Finished);
        StopKidRoutines();
        treeLights.Jiggle(1.5f); // the tree celebrates too
        fx.confettiRain.Play();
        Haptics.Celebrate();
        StartCoroutine(FinaleMusic());
        santaAnim.CrossFade("Dance", 0.3f);
        foreach (var anim in kidAnims) anim.CrossFade("Cheer", 0.3f);
        StartCoroutine(SantaGoodbye());

        Vector3 sky = Vector3.up * (houseHeight * 0.8f);
        while (true)
        {
            var p = sky + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0f, 0.5f), Random.Range(-0.3f, 0.4f));
            fx.Firework(content.TransformPoint(p));
            PlaySfx(fireworksClip, 0.6f);
            yield return new WaitForSeconds(Random.Range(0.45f, 0.9f));
        }
    }

    // After the party Santa waves goodbye (the kids wave back), walks out through the back door, and the door
    // closes behind him.
    IEnumerator SantaGoodbye()
    {
        yield return new WaitForSeconds(6f); // he dances along with everyone first

        Quaternion from = santa.localRotation, toCamera = Quaternion.Euler(0, 180, 0);
        yield return Ease.Run(0.3f, t => santa.localRotation = Quaternion.Slerp(from, toCamera, Ease.OutCubic(t)));
        santaAnim.CrossFade("Wave", 0.25f);
        foreach (var anim in kidAnims) anim.CrossFade("Wave", 0.25f);
        guideText.text = "Bye bye, Santa!";
        PlaySfx(bellClip ? bellClip : sparkleClip, 0.7f);
        yield return new WaitForSeconds(2.8f);

        // turns and walks out the way he came
        Vector3 start = santa.localPosition, end = santaOutside.localPosition;
        Quaternion here = santa.localRotation, toDoor = Quaternion.LookRotation(Flat(end - start), Vector3.up);
        yield return Ease.Run(0.4f, t => santa.localRotation = Quaternion.Slerp(here, toDoor, Ease.OutCubic(t)));
        santaAnim.CrossFade("Walk", 0.2f);
        yield return Ease.Run(1.8f, t => santa.localPosition = Vector3.Lerp(start, end, Ease.InOutSine(t)));
        fx.Puff(santa.position, 14);
        PlaySfx(whooshClip, 0.5f);
        yield return Ease.Run(0.25f, t => santa.localScale = santaScale * (1 - Ease.InBack(t)));
        santa.gameObject.SetActive(false);

        // the door closes behind him and the kids go back to cheering
        yield return Ease.Run(0.7f, t => door.localRotation = doorRotation * Quaternion.Euler(0, -100f * (1 - Ease.InOutSine(t)), 0));
        foreach (var anim in kidAnims) anim.CrossFade("Cheer", 0.3f);
        guideText.text = "See you next Christmas!";
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
        hud.SetActive(s >= State.HouseUp);
        getGiftButton.gameObject.SetActive(s == State.HouseUp || s == State.SantaArriving);
        getGiftButton.interactable = s == State.HouseUp && houseReady;
        finishScreen.SetActive(s == State.Finished);
        spiritMeter.SetActive(s == State.BellReady);

        int left = giftSpots.Length - giftsOpened;
        switch (s)
        {
            case State.Scanned: guideText.text = "Card found! Tap Start to begin"; break;
            case State.HouseUp:
                guideText.text = houseReady ? "Tap \"Get Your Gift\" to call Santa!" : "A snowy house is appearing...";
                break;
            case State.SantaArriving: guideText.text = "Here comes Santa!\nWatch the chimney..."; break;
            case State.Reveal: guideText.text = "Let's peek inside!"; break;
            case State.GiftsReady: guideText.text = $"Tap a gift - the kids will open it!  ({left} left)"; break;
            case State.Opening: guideText.text = "What's inside?"; break;
            case State.BellReady: guideText.text = $"Ring the bell to call Santa!  ({rings}/{ringsNeeded})"; break;
            case State.SantaVisit: guideText.text = "Someone's at the door..."; break;
            case State.GoldenGift: guideText.text = "Tap Santa's golden present!"; break;
            case State.Finished: guideText.text = "Merry Christmas from Santa!"; break;
        }
    }

    void ResetScene()
    {
        foreach (var go in spawned) if (go) Destroy(go);
        spawned.Clear();
        giftsLanded = giftsOpened = 0;
        houseReady = revealed = false;
        treeLights.enabled = false;
        tree.localRotation = treeRotation;
        house.gameObject.SetActive(false);
        roof.gameObject.SetActive(true);
        roof.localPosition = roofPos; roof.localRotation = roofRot; roof.localScale = roofScale;
        frontWall.gameObject.SetActive(true);
        frontWall.localRotation = wallRot; frontWall.localScale = wallScale;
        door.localRotation = doorRotation;
        if (fire) fire.Extinguish();
        for (int i = 0; i < kids.Length; i++)
        {
            kidBusy[i] = false;
            kidRoutines[i] = null; // Restart already stopped every coroutine
            kids[i].gameObject.SetActive(false); // they're inside; they show up when the house opens
            kids[i].localPosition = kidHomes[i].localPosition;
            kids[i].localRotation = kidHomes[i].localRotation;
        }
        sleigh.gameObject.SetActive(false);
        santa.gameObject.SetActive(false);
        santa.localScale = santaScale;
        bell.gameObject.SetActive(false);
        bell.localScale = bellScale;
        rings = 0;
        fx.StopAll();
        music.Stop();
        music.clip = mainMusic;
        music.loop = true;
        bells.Stop();
    }

    void PuffRing(Vector3 centre, float radius, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i * 2 * Mathf.PI / count;
            fx.Puff(content.TransformPoint(centre + radius * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))), 6);
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

    Vector3 OnCircle(Vector3 center, float a) => center + flightRadius * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
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
