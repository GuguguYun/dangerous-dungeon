using UnityEngine;

/// Trauma-style screen shake: callers add trauma, the camera reads an offset that decays.
public static class CameraShake
{
    static float trauma;

    public static void Add(float amount) => trauma = Mathf.Min(1f, trauma + amount);

    public static void Reset() => trauma = 0f;

    public static Vector3 Offset(float dt)
    {
        trauma = Mathf.Max(0f, trauma - dt * 2.5f);
        float s = trauma * trauma * 0.35f * GameSettings.shake * 2f; // 0.5 = the original strength
        float t = Time.time * 40f;
        return new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * s;
    }
}
