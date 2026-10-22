using UnityEngine;
using UnityEngine.UI;

// Keeps the UI the same size in portrait (phone) and landscape (editor Game view):
// the reference resolution follows the screen's orientation.
[RequireComponent(typeof(CanvasScaler))]
public class CanvasOrientation : MonoBehaviour
{
    CanvasScaler scaler;

    void Awake() => scaler = GetComponent<CanvasScaler>();

    void Update()
    {
        var reference = Screen.width > Screen.height ? new Vector2(1920, 1080) : new Vector2(1080, 1920);
        if (scaler.referenceResolution != reference) scaler.referenceResolution = reference;
    }
}
