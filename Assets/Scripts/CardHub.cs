using System.Collections;
using UnityEngine;
using Vuforia;

// App shell: a neutral home screen until a card is seen, then that card's own experience
// (theme, UI, music) takes over. Only a camera-confirmed TRACKED counts as found; EXTENDED_TRACKED is
// what Vuforia reports when the card leaves the view, so after lostGrace seconds of it the card is lost
// ("Find the card again"), and after homeDelay more seconds the app returns home. Progress is kept.
// Switching screens cross-fades; the Christmas UI plays its own entrance (RevealBurst, UISlideIn).
// shortcut: handles one card (the greeting card); when the business card gets its own experience,
// turn `greeting` into a list and give each card its own pause instead of Time.timeScale.
public class CardHub : MonoBehaviour
{
    public GameObject homeScreen;
    public GreetingCardController greeting;
    public float lostGrace = 0.5f; // ignores brief TRACKED ↔ EXTENDED_TRACKED flickers
    public float homeDelay = 3f;

    bool found;
    float notTrackedSince = -1, lostSince = -1;
    CanvasGroup homeGroup, cardGroup;

    void Start()
    {
        homeGroup = Group(homeScreen);
        cardGroup = Group(greeting.uiRoot);
        greeting.content.GetComponentInParent<ObserverBehaviour>().OnTargetStatusChanged += OnStatus;
        greeting.SetShown(false);
        homeScreen.SetActive(true);
    }

    void OnStatus(ObserverBehaviour _, TargetStatus status)
    {
        if (status.Status == Status.TRACKED)
        {
            notTrackedSince = lostSince = -1;
            if (found) return;
            found = true;
            StopAllCoroutines();
            cardGroup.alpha = 1;
            greeting.SetShown(true);   // its entrance animations start as it becomes active
            greeting.OnCardFound();
            StartCoroutine(Fade(homeGroup, homeGroup.alpha, 0, 0.35f, () => homeScreen.SetActive(false)));
        }
        else if (found && notTrackedSince < 0)
            notTrackedSince = Time.unscaledTime;
    }

    void Update()
    {
        float now = Time.unscaledTime; // keeps counting while the card's experience is paused
        if (notTrackedSince >= 0 && now - notTrackedSince > lostGrace)
        {
            notTrackedSince = -1;
            lostSince = now;
            found = false;
            greeting.OnCardLost();
        }
        if (lostSince >= 0 && now - lostSince > homeDelay)
        {
            lostSince = -1;
            StartCoroutine(GoHome());
        }
    }

    IEnumerator GoHome()
    {
        yield return Fade(cardGroup, 1, 0, 0.3f, () => greeting.SetShown(false));
        cardGroup.alpha = 1;
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
