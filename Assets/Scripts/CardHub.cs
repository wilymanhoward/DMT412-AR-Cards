using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vuforia;

// App shell: a neutral home screen until a card is seen, then that card's own experience (theme, UI, music)
// takes over. Works for any number of cards (the greeting card and the business card).
//  - Only a camera-confirmed TRACKED counts as found. EXTENDED_TRACKED is what Vuforia reports when a card
//    leaves the view, so after lostGrace seconds of it the card is lost ("Find the card again"), and after
//    homeDelay more seconds the app returns home. Progress is kept if the same card comes back.
//  - While one card is in view, the others are ignored. If a different card takes over, the old one starts over.
//  - Screens cross-fade; each experience plays its own entrance.
public class CardHub : MonoBehaviour
{
    public GameObject homeScreen;
    public GreetingCardController greeting;
    public BusinessCardController business; // set by Tools > Business Card > Build Business Card
    public float lostGrace = 0.5f;          // ignores brief TRACKED ↔ EXTENDED_TRACKED flickers
    public float homeDelay = 3f;

    class Slot
    {
        public ICardExperience experience;
        public CanvasGroup group;
        public bool tracked;
        public float notTrackedSince = -1;
    }

    readonly List<Slot> slots = new List<Slot>();
    Slot shown;            // the card on screen (in view, or lost and waiting to go home); null at the home screen
    Slot last;             // the card whose experience is in progress, even while hidden at home
    float lostSince = -1;
    CanvasGroup homeGroup;

    void Start()
    {
        homeGroup = Group(homeScreen);
        Add(greeting);
        Add(business);
        homeScreen.SetActive(true);
    }

    void Add(MonoBehaviour behaviour)
    {
        if (!behaviour) return; // that card isn't in the scene
        var experience = (ICardExperience)behaviour;
        var slot = new Slot { experience = experience, group = Group(experience.UiRoot) };
        experience.Content.GetComponentInParent<ObserverBehaviour>().OnTargetStatusChanged += (_, status) => OnStatus(slot, status);
        experience.SetShown(false);
        slots.Add(slot);
    }

    void OnStatus(Slot slot, TargetStatus status)
    {
        if (status.Status == Status.TRACKED)
        {
            slot.notTrackedSince = -1;
            if (slot.tracked) return;
            slot.tracked = true;
            if (shown != null && shown != slot && shown.tracked) return; // another card is in view: stay with it
            Show(slot);
        }
        else if (slot.tracked && slot.notTrackedSince < 0)
            slot.notTrackedSince = Time.unscaledTime;
    }

    void Show(Slot slot)
    {
        StopAllCoroutines();
        lostSince = -1;
        if (last != null && last != slot) last.experience.ResetToStart(); // a different card: the old one starts over
        if (shown != null && shown != slot)
        {
            shown.experience.SetShown(false);
            shown.group.alpha = 1;
        }
        shown = last = slot;
        slot.group.alpha = 1;
        slot.experience.SetShown(true);  // its entrance animations start as it becomes active
        slot.experience.OnCardFound();
        StartCoroutine(Fade(homeGroup, homeGroup.alpha, 0, 0.35f, () => homeScreen.SetActive(false)));
    }

    void Update()
    {
        float now = Time.unscaledTime; // keeps counting while the card's experience is paused
        foreach (var slot in slots)
        {
            if (slot.notTrackedSince < 0 || now - slot.notTrackedSince <= lostGrace) continue;
            slot.notTrackedSince = -1;
            slot.tracked = false;
            if (slot != shown) continue;
            var other = slots.Find(o => o != slot && o.tracked);
            if (other != null) { Show(other); break; } // the other card was already in view
            lostSince = now;
            slot.experience.OnCardLost();
        }
        if (lostSince >= 0 && shown != null && now - lostSince > homeDelay)
        {
            lostSince = -1;
            StartCoroutine(GoHome());
        }
    }

    // The card's back button: leave it for the home screen. The card stays in view but is ignored until it has
    // been lost and found again, and then it starts from the beginning.
    public void ReturnHome()
    {
        if (shown == null) return;
        StopAllCoroutines();
        lostSince = -1;
        Time.timeScale = 1;
        AudioListener.pause = false;
        StartCoroutine(ReturnHomeRoutine(shown));
    }

    IEnumerator ReturnHomeRoutine(Slot slot)
    {
        yield return GoHome();
        slot.experience.ResetToStart();
        if (last == slot) last = null;
    }

    IEnumerator GoHome()
    {
        var slot = shown;
        yield return Fade(slot.group, 1, 0, 0.3f, () => slot.experience.SetShown(false));
        slot.group.alpha = 1;
        shown = null;
        homeScreen.SetActive(true);
        yield return Fade(homeGroup, 0, 1, 0.4f, null);
    }

    static IEnumerator Fade(CanvasGroup group, float from, float to, float duration, System.Action done)
    {
        yield return Ease.RunUnscaled(duration, t => group.alpha = Mathf.Lerp(from, to, Ease.InOutSine(t)));
        done?.Invoke();
    }

    static CanvasGroup Group(GameObject go)
    {
        var g = go.GetComponent<CanvasGroup>(); // not ??: Unity's "missing component" isn't a real C# null
        return g ? g : go.AddComponent<CanvasGroup>();
    }
}
