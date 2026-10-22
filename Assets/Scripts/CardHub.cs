using UnityEngine;
using Vuforia;

// App shell: a neutral home screen until a card is seen, then that card's own experience
// (theme, UI, music) takes over. Only a camera-confirmed TRACKED counts as found; EXTENDED_TRACKED is
// what Vuforia reports when the card leaves the view, so after lostGrace seconds of it the card is lost
// ("Find the card again"), and after homeDelay more seconds the app returns home. Progress is kept.
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

    void Start()
    {
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
            homeScreen.SetActive(false);
            greeting.SetShown(true);
            greeting.OnCardFound();
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
            greeting.SetShown(false);
            homeScreen.SetActive(true);
        }
    }
}
