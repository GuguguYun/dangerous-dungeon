using UnityEngine;

/// One crackling lightning segment (see Fx.Lightning).
public class Bolt : MonoBehaviour
{
    LineRenderer core, glow, halo;
    Vector3 a, b;
    float t, jitterT;
    const float Life = 0.24f;
    const int Points = 12;

    public static void Create(Vector3 a, Vector3 b, float coreWidth, float glowWidth, bool main)
    {
        var go = new GameObject("bolt");
        var bolt = go.AddComponent<Bolt>();
        bolt.a = a;
        bolt.b = b;
        bolt.core = Line(go, "core", Color.white * 4f, coreWidth);
        bolt.glow = Line(go, "glow", Palette.ColorOf("lightning") * 3f * new Color(1f, 1f, 1f, 0.7f), glowWidth);
        // A wide faint halo so the bolt reads from across the room.
        bolt.halo = Line(go, "halo", Palette.ColorOf("lightning") * 1.5f * new Color(1f, 1f, 1f, 0.18f), glowWidth * 2.4f);
        bolt.Jitter();
    }

    static LineRenderer Line(GameObject parent, string name, Color c, float width)
    {
        var lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.transform.SetParent(parent.transform, false);
        lr.sharedMaterial = Palette.Trail("lightning");
        lr.positionCount = Points;
        lr.startColor = lr.endColor = c;
        lr.widthMultiplier = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return lr;
    }

    void Jitter()
    {
        Vector3 side = Vector3.Cross((b - a).normalized, Vector3.up).normalized;
        float span = (b - a).magnitude;
        for (int i = 0; i < Points; i++)
        {
            float k = i / (Points - 1f);
            Vector3 p = Vector3.Lerp(a, b, k);
            if (i > 0 && i < Points - 1)
            {
                float amp = Mathf.Min(0.45f, span * 0.06f) * Mathf.Sin(k * Mathf.PI);
                p += side * Random.Range(-amp, amp) + Vector3.up * Random.Range(-amp, amp);
            }
            core.SetPosition(i, p);
            glow.SetPosition(i, p);
            halo.SetPosition(i, p);
        }
    }

    void Update()
    {
        t += Time.deltaTime;
        jitterT -= Time.deltaTime;
        if (jitterT <= 0f)
        {
            jitterT = 0.03f;
            Jitter();
        }
        float fade = 1f - t / Life;
        core.widthMultiplier *= 0.98f;
        // Flicker as it fades: lightning strobes rather than dimming smoothly.
        float flick = Random.value < 0.25f ? 0.35f : 1f;
        glow.startColor = glow.endColor = Palette.ColorOf("lightning") * 3f * new Color(1f, 1f, 1f, 0.7f * fade * flick);
        halo.startColor = halo.endColor = Palette.ColorOf("lightning") * 1.5f * new Color(1f, 1f, 1f, 0.18f * fade * flick);
        core.startColor = core.endColor = Color.white * 4f * new Color(1f, 1f, 1f, Mathf.Clamp01(fade * 1.5f) * flick);
        if (t >= Life) Destroy(gameObject);
    }
}
