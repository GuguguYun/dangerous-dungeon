using UnityEngine;

/// A goblin bomber's bomb: lobbed in an arc onto a spot marked by a shrinking red circle, it goes off
/// on landing and hurts everyone inside the circle, goblins included. Step out of the circle.
public class EnemyBomb : MonoBehaviour
{
    public static int Exploded; // self-test counter
    Vector3 from, to;
    float flight, t, radius;
    LineRenderer outer, inner;
    ParticleSystem fuse;

    public static EnemyBomb Throw(Vector3 from, Vector3 target, float flight, float radius)
    {
        var go = new GameObject("enemy_bomb");
        go.transform.position = from;
        Model(go.transform, 0.36f);
        var b = go.AddComponent<EnemyBomb>();
        b.from = from;
        b.to = new Vector3(target.x, 0.05f, target.z);
        b.flight = flight;
        b.radius = radius;
        b.fuse = Vfx.Trail(go.transform, "sparks");
        b.outer = Ring(b.to, radius, 0.08f);
        b.inner = Ring(b.to, radius, 0.12f);
        Tips.Show("bomb", "红圈是炸弹的落点：赶紧走出圈子");
        Sfx.Play("slash", 0.4f, 0.2f);
        return b;
    }

    public const string BombModel = "bomb";

    /// The bomb itself, about `size` across and centred on parent: the Meshy bomb with its fuse, or a
    /// black iron ball if the model is missing.
    public static void Model(Transform parent, float size)
    {
        if (Props.Has(BombModel))
        {
            var m = Props.Spawn(BombModel, parent.position, parent.rotation, parent, false);
            m.transform.localScale = Vector3.one * (size / 0.34f); // the prop is made 0.34 m across
            var bounds = new Bounds();
            bool first = true;
            foreach (var r in m.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            m.transform.position -= bounds.center - parent.position;
            return;
        }
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(ball.GetComponent<Collider>());
        ball.transform.SetParent(parent, false);
        ball.transform.localScale = Vector3.one * size;
        ball.GetComponent<Renderer>().sharedMaterial = Palette.Get("iron");
    }

    static LineRenderer Ring(Vector3 at, float r, float width)
    {
        var lr = new GameObject("bomb_ring").AddComponent<LineRenderer>();
        lr.transform.position = at;
        lr.loop = true;
        lr.useWorldSpace = false;
        lr.positionCount = 40;
        lr.widthMultiplier = width;
        lr.sharedMaterial = Palette.Trail("beam");
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetRadius(lr, r);
        return lr;
    }

    static void SetRadius(LineRenderer lr, float r)
    {
        for (int i = 0; i < lr.positionCount; i++)
        {
            float a = i * Mathf.PI * 2f / lr.positionCount;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0.04f, Mathf.Sin(a) * r));
        }
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / flight);
        // A lob: straight line plus a parabola on top.
        transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (2.2f * 4f * k * (1f - k));
        transform.Rotate(Vector3.right, 540f * Time.deltaTime, Space.Self);
        // The inner ring closes in on the outer one: when they meet, it blows.
        SetRadius(inner, radius * k);
        var c = new Color(1f, 0.25f + 0.3f * Mathf.PingPong(t * 6f, 1f), 0.15f, 0.9f) * 2f;
        outer.startColor = outer.endColor = c;
        inner.startColor = inner.endColor = c;
        if (k >= 1f) Explode();
    }

    void Explode()
    {
        Exploded++;
        var p = PlayerController.Current;
        if (p && !p.dead)
        {
            Vector3 d = p.transform.position - to;
            d.y = 0f;
            if (d.magnitude < radius && p.FeetHeight < 1.5f) p.TakeDamage(1);
        }
        foreach (var c in Physics.OverlapSphere(to + Vector3.up * 0.5f, radius, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (e && !e.dead) e.TakeDamage(3, (e.transform.position - to).normalized, false, 5f);
        }
        foreach (var prop in PhysicsProp.Live.ToArray())
            if (prop && Vector3.Distance(prop.transform.position, to) < radius) prop.Hit(4, to, prop.transform.position - to);
        Vfx.Explosion(to + Vector3.up * 0.3f, radius * 0.8f);
        CameraShake.Add(0.25f);
        Sfx.Play("explode", 0.6f, 0.15f);
        Vfx.Detach(fuse);
        Destroy(outer.gameObject);
        Destroy(inner.gameObject);
        Destroy(gameObject);
    }
}
