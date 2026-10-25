using UnityEngine;

// Short phone vibrations (Android). Does nothing in the editor and on other platforms.
// Handheld.Vibrate() is the old fixed ~1 s buzz: it's only the fallback for big moments when the short
// vibration can't be started, and its use is also how Unity knows to add the VIBRATE permission to the app.
public static class Haptics
{
    public const int Light = 70, Medium = 150, Strong = 255; // amplitude 1..255

#if UNITY_ANDROID && !UNITY_EDITOR
    static AndroidJavaObject vibrator;
    static AndroidJavaClass effects;
    static bool triedInit;

    static bool Init()
    {
        if (triedInit) return vibrator != null;
        triedInit = true;
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            effects = new AndroidJavaClass("android.os.VibrationEffect");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Haptics unavailable: " + e.Message);
            vibrator = null;
        }
        return vibrator != null;
    }
#endif

    // One short pulse: `ms` long at `amplitude` (Light / Medium / Strong).
    public static void Buzz(int ms = 30, int amplitude = Medium)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (Init())
            {
                using (var effect = effects.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, amplitude))
                    vibrator.Call("vibrate", effect);
            }
            else if (amplitude >= Strong) Handheld.Vibrate();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Haptics failed: " + e.Message);
        }
#endif
    }

    // Two quick pulses then a long one: for rare finds and the finale.
    public static void Celebrate()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (Init())
            {
                var timings = new long[] { 0, 50, 60, 50, 60, 140 };
                var amplitudes = new int[] { 0, Medium, 0, Medium, 0, Strong };
                using (var effect = effects.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1))
                    vibrator.Call("vibrate", effect);
            }
            else Handheld.Vibrate();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Haptics failed: " + e.Message);
        }
#endif
    }
}
