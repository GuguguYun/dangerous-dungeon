using UnityEngine;

/// The hit streak: every hit on an enemy within 2 s of the last one keeps it going. The bonus grows
/// with how long it has been kept up, 10% a second to +30% (Balance), not with the number of hits, so a
/// rocket launcher gets there as soon as a pair of daggers. Getting hurt or waiting too long breaks it.
public static class Combo
{
    public static int count;
    public static float lastHit = -99f, since;
    const float Window = 2f;

    public static bool Alive => count > 0 && Time.time - lastHit < Window;
    public static float Bonus => Alive ? Mathf.Min(Balance.StreakMax, (lastHit - since) * Balance.StreakPerSecond) : 0f;

    public static void Hit()
    {
        if (!Alive)
        {
            count = 0;
            since = Time.time;
        }
        count++;
        lastHit = Time.time;
        RunState.bestCombo = Mathf.Max(RunState.bestCombo, count);
    }

    public static void Break() => count = 0;
}
