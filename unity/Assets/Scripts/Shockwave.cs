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
    LineRenderer line;

    public static Shockwave Spawn(Vector3 center, float speed = 9f, float maxRadius = 18f, int damage = 1)
    {
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
        s.Draw();
        Sfx.Play("door", 1f, 0.05f);
        CameraShake.Add(0.3f);
        return s;
    }

    void Draw()
    {
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
    }

    void Update()
    {
        radius += speed * Time.deltaTime;
        Draw();
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
