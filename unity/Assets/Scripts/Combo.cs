using UnityEngine;

/// The hit streak: every hit on an enemy within 2 s of the last one adds to it, each adding 2% damage
/// (up to +30%). Getting hurt or waiting too long breaks it.
public static class Combo
{
    public static int count;
    public static float lastHit = -99f;
    const float Window = 2f;

    public static bool Alive => count > 0 && Time.time - lastHit < Window;
    public static float Bonus => Alive ? Mathf.Min(0.3f, count * 0.02f) : 0f;

    public static void Hit()
    {
        if (!Alive) count = 0;
        count++;
        lastHit = Time.time;
        RunState.bestCombo = Mathf.Max(RunState.bestCombo, count);
    }

    public static void Break() => count = 0;
}
