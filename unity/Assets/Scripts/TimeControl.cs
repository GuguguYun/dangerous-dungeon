using UnityEngine;

/// One owner for Time.timeScale: menus pause, a perfect dodge briefly slows time.
public static class TimeControl
{
    public static bool paused;
    static float slowUntil, stopUntil;
    const float SlowScale = 0.3f;

    public static bool Slowed => Time.unscaledTime < slowUntil;

    public static void Reset()
    {
        paused = false;
        slowUntil = 0f;
        Apply();
    }

    public static void SlowMo(float realSeconds) => slowUntil = Time.unscaledTime + realSeconds;

    /// Hit-stop: a near-freeze of a few hundredths of a second on a heavy hit, so it lands with weight.
    /// (Time only; the camera does not move, so it is comfortable for motion-sensitive players.)
    public static void HitStop(float realSeconds = 0.045f)
    {
        if (!Scripted) stopUntil = Mathf.Max(stopUntil, Time.unscaledTime + realSeconds);
    }

    public static bool Scripted; // self-test and screenshot runs keep exact timing

    /// Called every frame by the GameManager.
    public static void Apply()
    {
        Time.timeScale = paused ? 0f : Time.unscaledTime < stopUntil ? 0.05f : Slowed ? SlowScale : 1f;
        Time.fixedDeltaTime = 0.02f * Mathf.Max(Time.timeScale, 0.05f);
    }
}
