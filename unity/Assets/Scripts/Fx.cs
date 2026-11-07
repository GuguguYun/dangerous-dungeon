using UnityEngine;

/// Lightning: a jagged white core inside a blue glow, re-jittered every few hundredths of a second
/// so it crackles, with a couple of forks, sparks and a light flash where it lands.
public static class Fx
{
    public static void Lightning(Vector3 a, Vector3 b)
    {
        Bolt.Create(a, b, 0.07f, 0.35f, true);
        for (int i = 0; i < 2; i++)
        {
            Vector3 from = Vector3.Lerp(a, b, Random.Range(0.3f, 0.7f));
            Vector3 dir = (b - a).normalized;
            Vector3 fork = from + (Quaternion.Euler(Random.Range(-40f, 40f), Random.Range(-60f, 60f), 0f) * dir) * Random.Range(1f, 2.5f);
            Bolt.Create(from, fork, 0.035f, 0.18f, false);
        }
        Vfx.Sparks(b, Palette.ColorOf("lightning"), 10, 7f);
        Vfx.Flash(b, Palette.ColorOf("lightning"), 5f, 6f, 0.12f);
    }
}
