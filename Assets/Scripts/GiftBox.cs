using System.Collections;
using UnityEngine;

// One dropped gift. Once landed it glows and shakes every few seconds to invite a tap;
// tapped, it squashes, pops its lid, bursts confetti and releases a reward.
public class GiftBox : MonoBehaviour
{
    public Transform lid;          // pivot at the lid's centre so it tumbles around itself
    public ParticleSystem glow;
    public float height = 0.13f;   // set by the builder
    public bool golden;            // Santa's present: always a rare reward

    [HideInInspector] public GreetingCardController controller;
    public bool Opened { get; private set; }
    public bool Claimed { get; set; } // a kid is already on the way to open it

    bool ready;
    float nextShake;
    Vector3 baseScale, restPosition;
    Quaternion baseRotation;

    void Awake() => baseScale = transform.localScale;

    // Called by the controller once the landing bounce has finished.
    public void SetReady()
    {
        ready = true;
        baseRotation = transform.localRotation;
        restPosition = transform.localPosition;
        glow.Play();
        nextShake = Time.time + Random.Range(1f, 2.5f);
    }

    void Update()
    {
        if (!ready || Opened || Time.time < nextShake) return;
        nextShake = Time.time + Random.Range(3f, 4.5f);
        StartCoroutine(Shake());
    }

    IEnumerator Shake()
    {
        // anticipation: a quick crouch, then a decaying wobble with a tiny hop
        yield return Ease.Run(0.12f, t => transform.localScale = Squash(Ease.OutQuad(t) * 0.08f));
        Vector3 start = transform.localPosition;
        yield return Ease.Run(0.6f, t =>
        {
            float decay = 1 - Ease.OutCubic(t);
            transform.localRotation = baseRotation * Quaternion.Euler(0, 0, 10f * Mathf.Sin(t * 28f) * decay);
            transform.localPosition = start + Vector3.up * 0.02f * Mathf.Sin(Mathf.PI * Mathf.Min(t * 3f, 1f));
            transform.localScale = Squash(0.08f * (1 - Ease.OutBack(Mathf.Min(t * 3f, 1f))));
        });
        transform.localRotation = baseRotation;
        transform.localPosition = start;
        transform.localScale = baseScale;
    }

    public void Open()
    {
        if (Opened || !ready) return;
        Opened = true;
        StopAllCoroutines(); // may cut a shake mid-hop, so put the box back down
        transform.localRotation = baseRotation;
        transform.localPosition = restPosition;
        StartCoroutine(OpenRoutine());
    }

    IEnumerator OpenRoutine()
    {
        controller.OnGiftOpening();
        glow.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // anticipation: squash down, then spring back as the lid pops
        yield return Ease.Run(0.15f, t => transform.localScale = Squash(Ease.InQuad(t) * 0.18f));
        StartCoroutine(Ease.Run(0.3f, t => transform.localScale = Squash(0.18f * (1 - Ease.OutBack(t, 3f)))));
        StartCoroutine(PopLid());
        controller.PlayPop();

        Vector3 top = transform.position + transform.up * height;
        controller.fx.Confetti(top);
        controller.fx.Sparkle(top);
        yield return new WaitForSeconds(0.1f);
        controller.ReleaseReward(this, top);
    }

    IEnumerator PopLid()
    {
        Vector3 start = lid.localPosition;
        Quaternion startRot = lid.localRotation;
        Vector3 drift = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)).normalized * 0.08f;
        Vector3 tumbleAxis = Vector3.Cross(Vector3.up, drift).normalized;
        Vector3 lidScale = lid.localScale;

        yield return Ease.Run(0.45f, t =>
        {
            float k = Ease.OutCubic(t);
            lid.localPosition = start + (Vector3.up * 0.2f + drift) * k;
            lid.localRotation = Quaternion.AngleAxis(220f * k, tumbleAxis) * startRot;
        });
        yield return Ease.Run(0.25f, t => lid.localScale = lidScale * (1 - Ease.InBack(t)));
        lid.gameObject.SetActive(false);
    }

    // amount > 0 squashes (wider, shorter); < 0 stretches.
    Vector3 Squash(float amount) =>
        Vector3.Scale(baseScale, new Vector3(1 + amount * 0.6f, 1 - amount, 1 + amount * 0.6f));
}
