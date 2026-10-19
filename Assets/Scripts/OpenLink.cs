using UnityEngine;

// UI Buttons can't call Application.OpenURL directly, so wire On Click to Open() and type the link.
// Examples: https://linkedin.com/in/you   mailto:you@example.com   tel:+60123456789
public class OpenLink : MonoBehaviour
{
    public void Open(string url) => Application.OpenURL(url);
}
