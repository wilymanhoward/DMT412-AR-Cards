using UnityEngine;

// Turns its object to face the camera (screen-up stays up). With `text` and `back` set it also sizes the
// backdrop to the text once the text's mesh exists (it's built a frame after the text is set).
public class Billboard : MonoBehaviour
{
    public Camera cam;
    public bool face = true;
    public TextMesh text;
    public Transform back;
    public Vector2 pad;
    bool fitted;

    public void SetText(string s)
    {
        text.text = s;
        fitted = false;
    }

    void LateUpdate()
    {
        if (face && cam)
        {
            // turns around the card's up axis only, so it always stands upright whatever the camera angle
            Vector3 up = transform.parent ? transform.parent.up : Vector3.up;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - cam.transform.position, up);
            if (away.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(away, up);
        }
        if (!fitted && text) Fit();
    }

    void Fit()
    {
        var b = text.GetComponent<MeshRenderer>().localBounds;
        if (b.size.x < 0.0001f) return;
        fitted = true;
        if (!back) return;
        var centre = text.transform.localPosition + b.center;
        back.localPosition = new Vector3(centre.x, centre.y, back.localPosition.z);
        back.localScale = new Vector3(b.size.x + pad.x * 2, b.size.y + pad.y * 2, back.localScale.z);
    }
}
