using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// The Christmas entrance: a warm flash plus snowflakes and stars bursting out from the centre,
// played every time the Christmas UI is shown. Runs while the game is paused.
public class RevealBurst : MonoBehaviour
{
    public Image flash;
    public Image[] pieces;
    public float duration = 1f;

    void OnEnable() => StartCoroutine(Play());

    IEnumerator Play()
    {
        int n = pieces.Length;
        var dirs = new Vector2[n];
        var dist = new float[n];
        var spin = new float[n];
        for (int i = 0; i < n; i++)
        {
            float a = (i + Random.value * 0.5f) * Mathf.PI * 2 / n;
            dirs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            dist[i] = Random.Range(450f, 900f);
            spin[i] = Random.Range(-360f, 360f);
            pieces[i].gameObject.SetActive(true);
        }

        yield return Ease.RunUnscaled(duration, t =>
        {
            var c = flash.color;
            c.a = 0.55f * Mathf.Sin(Mathf.PI * Mathf.Min(t * 2.5f, 1f)); // quick flash in the first 40%
            flash.color = c;
            for (int i = 0; i < n; i++)
            {
                var rt = pieces[i].rectTransform;
                rt.anchoredPosition = dirs[i] * dist[i] * Ease.OutCubic(t);
                rt.localEulerAngles = new Vector3(0, 0, spin[i] * t);
                rt.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.3f, Ease.OutCubic(t));
                var pc = pieces[i].color;
                pc.a = 1 - Ease.InQuad(t);
                pieces[i].color = pc;
            }
        });
        foreach (var p in pieces) p.gameObject.SetActive(false);
    }
}
