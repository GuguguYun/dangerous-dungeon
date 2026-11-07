using UnityEngine;

/// A glowing ring that races outward along the floor. Dashing does not help: the only way past it
/// is to be in the air (feet above the ring) when it reaches you.
public class Shockwave : MonoBehaviour
{
    const int Segments = 72;
    public const float Height = 0.45f; // feet must be higher than this to clear it

    float radius, speed, maxRadius;
    int damage;
    bool resolved;
    LineRenderer line, glow, core;
    float dustT;

    public static Shockwave Spawn(Vector3 center, float speed = 9f, float maxRadius = 18f, int damage = 1)
    {
        Tips.Show("shock", "地面冲击波：冲刺躲不掉，只能跳过去");
        var go = new GameObject("shockwave");
        go.transform.position = new Vector3(center.x, 0.12f, center.z);
        var s = go.AddComponent<Shockwave>();
        s.speed = speed;
        s.maxRadius = maxRadius;
        s.damage = damage;
        s.radius = 0.5f;
        s.line = go.AddComponent<LineRenderer>();
        s.line.loop = true;
        s.line.positionCount = Segments;
        s.line.useWorldSpace = false;
        s.line.widthMultiplier = 0.55f;
        s.line.sharedMaterial = Palette.Trail("shockwave");
        var c = Palette.ColorOf("shockwave");
        s.line.startColor = s.line.endColor = c * 2.5f;
        s.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // A white-hot thin core and a wide faint glow around the orange band.
        s.core = s.Ring("core", 0.16f, Color.white * 3f);
        s.glow = s.Ring("glow", 1.5f, new Color(c.r, c.g, c.b, 0.22f) * 1.5f);
        s.Draw();
        Sfx.Play("door", 1f, 0.05f);
        CameraShake.Add(0.3f);
        return s;
    }

    LineRenderer Ring(string name, float width, Color c)
    {
        var lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.transform.SetParent(transform, false);
        lr.loop = true;
        lr.positionCount = Segments;
        lr.useWorldSpace = false;
        lr.widthMultiplier = width;
        lr.sharedMaterial = Palette.Trail("shockwave");
        lr.startColor = lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return lr;
    }

    void Draw()
    {
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            var p = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            line.SetPosition(i, p);
            if (core) core.SetPosition(i, p + Vector3.up * 0.02f);
            if (glow) glow.SetPosition(i, p - Vector3.up * 0.02f);
        }
        // Fade out over the last few metres.
        float fade = Mathf.Clamp01((maxRadius - radius) / 3f);
        var c = Palette.ColorOf("shockwave");
        line.startColor = line.endColor = c * 2.5f * new Color(1f, 1f, 1f, fade);
        if (core) core.startColor = core.endColor = Color.white * 3f * new Color(1f, 1f, 1f, fade);
        if (glow) glow.startColor = glow.endColor = new Color(c.r, c.g, c.b, 0.22f * fade) * 1.5f;
    }

    void Update()
    {
        radius += speed * Time.deltaTime;
        Draw();
        // Dust kicked up along the front.
        dustT -= Time.deltaTime;
        if (dustT <= 0f && radius < maxRadius - 2f)
        {
            dustT = 0.15f;
            for (int i = 0; i < 4; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vfx.Puff(transform.position + new Vector3(Mathf.Cos(a) * radius, 0.15f, Mathf.Sin(a) * radius), new Color(0.55f, 0.48f, 0.4f, 0.45f), 0.6f, 3);
            }
        }
        var p = PlayerController.Current;
        if (p && !p.dead && !resolved)
        {
            Vector3 d = p.transform.position - transform.position;
            d.y = 0f;
            if (Mathf.Abs(d.magnitude - radius) < 0.45f)
            {
                resolved = true;
                if (p.transform.position.y > Height) p.NearMiss();
                else p.TakeShockwave(damage);
            }
        }
        if (radius > maxRadius) Destroy(gameObject);
    }
}
