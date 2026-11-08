using System.Collections.Generic;
using UnityEngine;

/// A crate or barrel that is a real PhysX body: bullets shove it, explosions throw it, characters
/// push it, and enough damage breaks it into flying pieces. Explosive barrels blow up when broken
/// and set off other barrels nearby.
public class PhysicsProp : MonoBehaviour
{
    public static readonly List<PhysicsProp> Live = new List<PhysicsProp>();

    public int hp = 6;
    public bool explosive;
    public bool toxic;
    public int hits;
    public Rigidbody body;
    public bool broken { get; private set; }

    public static PhysicsProp Make(GameObject go, float mass, int hp, bool explosive = false)
    {
        // Static mesh colliders cannot move: swap in a box from the renderer bounds.
        var b = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        // Now, not at the end of the frame: a concave mesh collider must not meet the rigidbody added below.
        foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
        var box = go.AddComponent<BoxCollider>();
        box.center = go.transform.InverseTransformPoint(b.center);
        var size = go.transform.InverseTransformVector(b.size);
        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.92f;
        var p = go.AddComponent<PhysicsProp>();
        p.body = go.AddComponent<Rigidbody>();
        p.body.mass = mass;
        p.body.interpolation = RigidbodyInterpolation.Interpolate;
        p.body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        p.hp = hp;
        p.explosive = explosive;
        if (explosive)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(1.6f, 0.35f, 0.25f));
            mpb.SetColor("_Color", new Color(1.6f, 0.35f, 0.25f));
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
            var l = new GameObject("fuse").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = Vector3.up * 1.3f;
            l.type = LightType.Point;
            l.color = new Color(1f, 0.35f, 0.2f);
            l.range = 2.5f;
            l.intensity = 2f;
            l.gameObject.AddComponent<Flicker>();
        }
        return p;
    }

    void OnEnable() => Live.Add(this);
    void OnDisable() => Live.Remove(this);

    public void Hit(int damage, Vector3 point, Vector3 dir)
    {
        if (broken) return;
        hits++;
        // An impulse, so heavier props (crates 18 kg) budge less than barrels (12 kg).
        body.AddForceAtPosition(dir.normalized * Mathf.Clamp(damage * 7f, 7f, 45f), point, ForceMode.Impulse);
        hp -= damage;
        if (hp <= 0) Break(dir);
    }

    public void Break(Vector3 dir)
    {
        if (broken) return;
        broken = true;
        Vector3 at = transform.position + Vector3.up * 0.5f;
        if (explosive) Invoke(nameof(Explode), 0.08f); // a beat so chains ripple instead of popping at once
        else Destroy(gameObject);
        if (toxic) PoisonCloud.Create(at);
        Debris(at, dir);
        Sfx.Play("hit", 0.6f, 0.3f);
    }

    void Explode()
    {
        Vector3 at = transform.position + Vector3.up * 0.5f;
        Destroy(gameObject);
        Explosion.At(at, 3.5f, Balance.BarrelBlast, true);
    }

    void Debris(Vector3 at, Vector3 dir)
    {
        var src = GetComponentInChildren<Renderer>();
        for (int i = 0; i < 7; i++)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "debris";
            piece.layer = Layers.Debris;
            piece.transform.position = at + Random.insideUnitSphere * 0.35f;
            piece.transform.rotation = Random.rotation;
            piece.transform.localScale = new Vector3(Random.Range(0.15f, 0.4f), Random.Range(0.06f, 0.14f), Random.Range(0.2f, 0.5f));
            if (src) piece.GetComponent<Renderer>().sharedMaterial = src.sharedMaterial;
            var rb = piece.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.AddForce((Random.onUnitSphere + Vector3.up + dir.normalized) * Random.Range(2f, 5f), ForceMode.VelocityChange);
            rb.AddTorque(Random.onUnitSphere * 10f, ForceMode.VelocityChange);
            piece.AddComponent<Shrink>().duration = 4f;
        }
    }
}
