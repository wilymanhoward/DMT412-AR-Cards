using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runs the Christmas card as a state machine:
//   Idle → Scanned → TreeUp → SantaArriving → GiftsReady ⇄ Opening → Finished
// Losing the card pauses everything (Time.timeScale = 0) and shows "Find the card again"; progress is kept.
// All references are filled in by Tools > Christmas Card > 2. Build Greeting Card.
public class GreetingCardController : MonoBehaviour
{
    public enum State { Idle, Scanned, TreeUp, SantaArriving, GiftsReady, Opening, Finished }

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
    public Transform santa;        // dancing Santa for the finale
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

    [Header("Audio")]
    public AudioSource music;
    public AudioSource bells;      // 3D, on the sleigh: louder as it approaches
    public AudioSource sfx;
    public AudioClip popClip, landClip, sparkleClip, rareClip, fireworksClip, whooshClip, treeTapClip;
    [Range(0, 1)] public float musicVolume = 0.6f;

    [Header("UI")]
    public GameObject scanScreen, startScreen, hud, lostScreen, finishScreen, tapHint;
    public Button getGiftButton;
    public Text soundLabel;
    public Text labelTemplate;

    public State Current { get; private set; } = State.Idle;

    readonly List<GameObject> spawned = new List<GameObject>();
    int giftsLanded, giftsOpened;
    bool treeReady, muted;
    float nextTreePoke;
    Quaternion treeRotation;
    Vector3 santaScale;
    Vector3 sleighPrev, sleighForward = Vector3.forward;
    float sleighRoll;

    void Awake()
    {
        Application.targetFrameRate = 60;
        treeRotation = tree.localRotation;
        santaScale = santa.localScale;
        ResetScene();
        SetState(State.Idle);
    }

    // ---------- Tracking (wired to the Image Target's On Target Found / Lost) ----------

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
        soundLabel.text = muted ? "Sound: Off" : "Sound: On";
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

        treeLights.enabled = true;
        fx.snowfall.Play();
        StartCoroutine(FadeAudio(music, 0, musicVolume, 2.5f, true));
        treeReady = true;
        getGiftButton.interactable = true;
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
        if (Time.timeScale == 0 || !TapStarted(out Vector2 screen)) return;
        var hits = Physics.RaycastAll(arCamera.ScreenPointToRay(screen), 100f);

        // gifts win over the tree, since they sit under its branches
        if (Current == State.GiftsReady)
            foreach (var hit in hits)
            {
                var box = hit.collider.GetComponentInParent<GiftBox>();
                if (box && !box.Opened) { box.Open(); return; }
            }

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
    public void ReleaseReward(GiftBox box, Vector3 top) => StartCoroutine(RewardRoutine(top));

    IEnumerator RewardRoutine(Vector3 top)
    {
        var reward = PickReward();
        var go = Instantiate(reward.prefab, content);
        go.SetActive(true);
        spawned.Add(go);
        Transform r = go.transform;
        Vector3 start = content.InverseTransformPoint(top);
        Vector3 end = start + Vector3.up * 0.22f;
        Vector3 full = r.localScale * (reward.rare ? 1.35f : 1f);
        Quaternion facing = r.localRotation;
        r.localPosition = start;
        r.localScale = Vector3.zero;

        if (reward.rare) { fx.Rare(top); PlaySfx(rareClip); }
        else PlaySfx(sparkleClip);

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
        giftsOpened++;
        if (giftsOpened >= giftSpots.Length) StartCoroutine(FinishSequence());
        else SetState(State.GiftsReady);
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
        label.text = reward.rare ? "RARE!\n" + reward.name : reward.name;
        label.color = reward.rare ? new Color(1f, 0.84f, 0.3f) : Color.white;
        label.GetComponent<RewardLabel>().target = target;
        label.gameObject.SetActive(true);
        spawned.Add(label.gameObject);
    }

    // ---------- Finale ----------

    IEnumerator FinishSequence()
    {
        yield return new WaitForSeconds(0.8f);
        SetState(State.Finished);
        treeLights.Jiggle(1.5f); // the tree celebrates too

        santa.gameObject.SetActive(true);
        santa.localScale = Vector3.zero;
        fx.Puff(santa.position, 16);
        StartCoroutine(Ease.Run(0.5f, t => santa.localScale = santaScale * Ease.OutBack(t)));

        Vector3 sky = tree.localPosition + Vector3.up * 1.1f;
        while (true)
        {
            var p = sky + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0f, 0.5f), Random.Range(-0.3f, 0.4f));
            fx.Firework(content.TransformPoint(p));
            PlaySfx(fireworksClip, 0.6f);
            yield return new WaitForSeconds(Random.Range(0.45f, 0.9f));
        }
    }

    // ---------- Helpers ----------

    void SetState(State s)
    {
        Current = s;
        scanScreen.SetActive(s == State.Idle);
        startScreen.SetActive(s == State.Scanned);
        hud.SetActive(s >= State.TreeUp);
        getGiftButton.gameObject.SetActive(s == State.TreeUp || s == State.SantaArriving);
        getGiftButton.interactable = s == State.TreeUp && treeReady;
        tapHint.SetActive(s == State.GiftsReady);
        finishScreen.SetActive(s == State.Finished);
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
        fx.StopAll();
        music.Stop();
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
