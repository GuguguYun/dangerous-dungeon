using UnityEngine;

/// Lightning: a jagged white core inside a blue glow, re-jittered every few hundredths of a second
/// so it crackles, with a couple of forks, sparks and a light flash where it lands.
public static class Fx
{
    public static void Lightning(Vector3 a, Vector3 b)
    {
        Bolt.Create(a, b, 0.11f, 0.55f, true);
        for (int i = 0; i < 3; i++)
        {
            Vector3 from = Vector3.Lerp(a, b, Random.Range(0.25f, 0.8f));
            Vector3 dir = (b - a).normalized;
            Vector3 fork = from + (Quaternion.Euler(Random.Range(-40f, 40f), Random.Range(-60f, 60f), 0f) * dir) * Random.Range(1f, 2.5f);
            Bolt.Create(from, fork, 0.05f, 0.26f, false);
        }
        Vfx.Sparks(b, Palette.ColorOf("lightning"), 16, 8f);
        Vfx.Ring(b - Vector3.up * 0.8f, 1.2f, Palette.ColorOf("lightning"));
        // Gentle light flashes (they were twice as bright; with rapid casting the room pulsed).
        Vfx.Flash(b, Palette.ColorOf("lightning"), 4f, 7f, 0.14f);
        Vfx.Flash(Vector3.Lerp(a, b, 0.5f), Palette.ColorOf("lightning"), 2f, 6f, 0.1f);
    }
}
