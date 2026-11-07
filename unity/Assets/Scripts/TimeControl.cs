using UnityEngine;

/// One owner for Time.timeScale: menus pause, a perfect dodge briefly slows time.
public static class TimeControl
{
    public static bool paused;
    static float slowUntil;
    const float SlowScale = 0.3f;

    public static bool Slowed => Time.unscaledTime < slowUntil;

    public static void Reset()
    {
        paused = false;
        slowUntil = 0f;
        Apply();
    }

    public static void SlowMo(float realSeconds) => slowUntil = Time.unscaledTime + realSeconds;

    /// Called every frame by the GameManager.
    public static void Apply()
    {
        Time.timeScale = paused ? 0f : Slowed ? SlowScale : 1f;
        Time.fixedDeltaTime = 0.02f * Mathf.Max(Time.timeScale, 0.05f);
    }
}
