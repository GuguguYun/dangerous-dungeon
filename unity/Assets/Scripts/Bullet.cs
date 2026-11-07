using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// A projectile that ray-tests the segment it travels each frame, so fast shots never tunnel.
/// Optional behaviours for the special weapons: pierce, gravity + bounces + fuse (grenade),
/// homing (missile), boomerang return, and slow / burn on hit.
public class Bullet : MonoBehaviour
{
    public static readonly List<Bullet> Live = new List<Bullet>();

    public Vector3 velocity;
    public int damage = 1;
    public bool crit;
    public float life;
    public float explode;
    public int pierce;          // enemies it may pass through after the first
    public float gravity;       // grenades fall
    public int bounces;         // grenades bounce off world geometry
    public float fuse = -1f;    // explode after this long (grenade), <0 = never
    public float homing;        // degrees per second turned towards the nearest enemy
    public Transform returnTo;  // boomerang owner
    public float outDistance;   // boomerang turns back after this far
    public float slow;          // speed multiplier applied on hit (frost), 0 = none
    public float burn;          // seconds of burning applied on hit (flame), 0 = none
    public System.Action Returned;

    bool hitsEnemies;
    float stretch;          // tracer / arrow length factor, 0 = round
    ParticleSystem trailFx; // smoke, frost mist or flames riding along
    int mask;
    string mat;
    float radius;
    float closest = 99f;
    bool dodgeChecked;
    float travelled;
    bool returning;
    Vector3 spinAxis;       // coins, axes and rocks spin as they fly
    float spinSpeed;
    readonly HashSet<Enemy> hit = new HashSet<Enemy>();
    static Mesh disc;
    static Material groundGlow;

    public bool HitsEnemies => hitsEnemies;

    public static Bullet Fire(Vector3 from, Vector3 velocity, bool hitsEnemies, float radius, string mat,
        float life = 3f, int damage = 1, float explode = 0f, bool crit = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = "bullet";
        go.transform.position = from;
        go.transform.localScale = Vector3.one * radius * 2f;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = Palette.Get(mat);
        r.shadowCastingMode = ShadowCastingMode.Off;

        var tr = go.AddComponent<TrailRenderer>();
        tr.sharedMaterial = Palette.Trail(mat);
        tr.time = 0.12f;
        tr.startWidth = radius * 1.6f;
        tr.endWidth = 0f;
        var c = Palette.ColorOf(mat);
        tr.startColor = c;
        tr.endColor = new Color(c.r, c.g, c.b, 0f);
        tr.shadowCastingMode = ShadowCastingMode.Off;

        var b = go.AddComponent<Bullet>();
        b.velocity = velocity;
        b.hitsEnemies = hitsEnemies;
        b.mask = Layers.WorldMask | (hitsEnemies ? Layers.EnemyMask : Layers.PlayerMask);
        b.life = life;
        b.mat = mat;
        b.radius = radius;
        b.damage = damage;
        b.explode = explode;
        b.crit = crit;
        b.Style(go, r, tr);
        return b;
    }

    /// Per-projectile look: tracers and arrows stretch along their flight, frost is a crystal
    /// with a mist trail, flames are pure particles, rockets and missiles leave smoke.
    void Style(GameObject go, MeshRenderer r, TrailRenderer tr)
    {
        switch (mat)
        {
            case "bullet_player":
            case "bullet_heavy":
            case "bullet_laser":
                stretch = 3.5f;
                break;
            case "bullet_arrow":
                stretch = 9f;
                go.transform.localScale = new Vector3(radius, radius, radius * 2f);
                break;
            case "bullet_frost":
                stretch = 1f;
                go.GetComponent<MeshFilter>().sharedMesh = Vfx.Crystal();
                go.transform.localScale = Vector3.one * radius * 1.8f;
                trailFx = Vfx.Trail(go.transform, "frost");
                break;
            case "bullet_fire":
                r.enabled = false;
                tr.enabled = false;
                trailFx = Vfx.Trail(go.transform, "fire");
                break;
            case "bullet_missile":
            case "bullet_rocket":
                stretch = 2.5f;
                trailFx = Vfx.Trail(go.transform, "smoke");
                break;
            // ---- enemy shots: each source has its own look, colour still says the height ----
            case "bullet_enemy":
                // Goblin gunpowder bolt.
                stretch = 2.2f;
                trailFx = Vfx.Trail(go.transform, "sparks");
                break;
            case "bullet_low":
                // Green ghost fire skimming the floor, with a glow on the ground under it: jump it.
                go.transform.localScale = Vector3.one * radius * 1.3f;
                trailFx = Vfx.Trail(go.transform, "ghostfire");
                GroundGlow(go);
                break;
            case "bullet_gold":
                // Goblin King: spinning gold coins.
                go.GetComponent<MeshFilter>().sharedMesh = Disc();
                go.transform.localScale = new Vector3(radius * 2.4f, radius * 0.35f, radius * 2.4f);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                spinAxis = Vector3.up;
                spinSpeed = 900f;
                trailFx = Vfx.Trail(go.transform, "glitter");
                break;
            case "bullet_rock":
                // Boar Chief: tumbling boulders throwing off dust.
                go.GetComponent<MeshFilter>().sharedMesh = Vfx.Crystal();
                go.transform.localScale = new Vector3(radius * 2.6f, radius * 2.0f, radius * 2.3f);
                go.transform.rotation = Random.rotation;
                spinAxis = Random.onUnitSphere;
                spinSpeed = 400f;
                tr.enabled = false;
                trailFx = Vfx.Trail(go.transform, "dust");
                break;
            case "bullet_axe":
                // Orc Warlord: whirling red axe blades.
                go.GetComponent<MeshFilter>().sharedMesh = Disc();
                go.transform.localScale = new Vector3(radius * 3.2f, radius * 0.2f, radius * 3.2f);
                spinAxis = Vector3.up;
                spinSpeed = 1080f;
                tr.startWidth = radius * 2.6f;
                tr.time = 0.18f;
                break;
        }
        if (stretch > 0f && mat != "bullet_frost")
            go.transform.localScale = new Vector3(go.transform.localScale.x, go.transform.localScale.y, radius * 2f * stretch);
        Orient();
    }

    void Orient()
    {
        if (stretch > 0f && velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity);
    }

    static Mesh Disc()
    {
        if (disc) return disc;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        return disc;
    }

    /// A soft glow on the floor right under a low shot, so its height reads without guessing depth.
    void GroundGlow(GameObject go)
    {
        if (!groundGlow)
        {
            groundGlow = new Material(Vfx.Additive);
            var c = new Color(0.4f, 1f, 0.3f, 0.55f);
            if (groundGlow.HasProperty("_BaseColor")) groundGlow.SetColor("_BaseColor", c);
            groundGlow.color = c;
        }
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "ground_glow";
        q.transform.SetParent(go.transform, false);
        float s = go.transform.localScale.x;
        q.transform.localPosition = Vector3.down * (go.transform.position.y - 0.03f) / s;
        q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        q.transform.localScale = Vector3.one * 1.1f / s;
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = groundGlow;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    void OnDestroy() => Vfx.Detach(trailFx);

    void OnEnable() => Live.Add(this);
    void OnDisable() => Live.Remove(this);

    void Update()
    {
        float dt = Time.deltaTime;
        life -= dt;
        if (fuse >= 0f)
        {
            fuse -= dt;
            if (fuse < 0f) { Detonate(transform.position); return; }
        }
        if (life <= 0f)
        {
            if (explode > 0f) Detonate(transform.position);
            else Destroy(gameObject);
            return;
        }
        if (gravity > 0f) velocity += Vector3.down * gravity * dt;
        if (homing > 0f) Steer(dt);
        if (returnTo && Boomerang(dt)) return;

        Orient();
        if (spinSpeed != 0f) transform.Rotate(spinAxis, spinSpeed * dt, Space.World);
        if (burn > 0f && hitsEnemies) Reactions.CheckClouds(transform.position, 0.3f); // flames reaching poison gas
        Vector3 step = velocity * dt;
        if (Physics.Raycast(transform.position, step.normalized, out var rh, step.magnitude, mask, QueryTriggerInteraction.Ignore))
        {
            if (OnHit(rh)) return;
        }
        else transform.position += step;
        travelled += step.magnitude;
        if (!hitsEnemies) CheckNearMiss();
    }

    /// Returns true when the bullet is gone.
    bool OnHit(RaycastHit rh)
    {
        var prop = rh.collider.GetComponentInParent<PhysicsProp>();
        if (prop && explode <= 0f && bounces <= 0) prop.Hit(hitsEnemies ? damage : 1, rh.point, velocity);
        if (!hitsEnemies)
        {
            var p = rh.collider.GetComponentInParent<PlayerController>();
            if (p) p.TakeDamage(damage);
            var pet = rh.collider.GetComponentInParent<Pet>();
            if (pet) pet.TakeDamage(damage); // the drone took it for you
            Impact(rh.point, radius * 3f);
            Destroy(gameObject);
            return true;
        }
        bool world = ((1 << rh.collider.gameObject.layer) & Layers.WorldMask) != 0;
        if (world)
        {
            if (bounces > 0)
            {
                bounces--;
                velocity = Vector3.Reflect(velocity, rh.normal) * 0.65f;
                transform.position = rh.point + rh.normal * (radius + 0.02f);
                Sfx.Play("hit", 0.2f, 0.3f);
                return false;
            }
            if (returnTo)
            {
                StartReturn();
                transform.position = rh.point + rh.normal * (radius + 0.05f);
                return false;
            }
            if (explode > 0f) Detonate(rh.point);
            else { Impact(rh.point, radius * 3f); Destroy(gameObject); }
            return true;
        }
        var e = rh.collider.GetComponentInParent<Enemy>();
        if (!e || e.dead || hit.Contains(e))
        {
            // Already hit this one (pierce / boomerang): slip past it.
            transform.position = rh.point + velocity.normalized * 0.6f;
            return false;
        }
        if (explode > 0f) { Detonate(rh.point); return true; }
        hit.Add(e);
        e.TakeDamage(damage, velocity.normalized, crit, explode > 0f ? 9f : 3.5f);
        if (slow > 0f) e.ApplySlow(slow, 2f);
        if (burn > 0f) e.ApplyBurn(burn);
        if (returnTo || pierce > 0)
        {
            if (!returnTo) pierce--;
            transform.position = rh.point + velocity.normalized * 0.6f;
            return false;
        }
        Impact(rh.point, radius * 3f);
        Destroy(gameObject);
        return true;
    }

    void Steer(float dt)
    {
        Enemy best = null;
        float bestD = 30f;
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            if (e.dead) continue;
            float d = Vector3.Distance(e.transform.position, transform.position);
            if (d < bestD) { bestD = d; best = e; }
        }
        if (!best) return;
        Vector3 want = (best.AimPoint - transform.position).normalized * velocity.magnitude;
        velocity = Vector3.RotateTowards(velocity, want, homing * Mathf.Deg2Rad * dt, 0f);
    }

    void StartReturn()
    {
        if (returning) return;
        returning = true;
        hit.Clear(); // the way back hits everyone again
        mask = Layers.EnemyMask; // flies back through walls
    }

    /// Returns true when the boomerang is back in hand.
    bool Boomerang(float dt)
    {
        transform.Rotate(0f, 900f * dt, 0f);
        if (!returning && travelled >= outDistance) StartReturn();
        if (!returning) return false;
        Vector3 to = returnTo.position + Vector3.up * 1.0f - transform.position;
        velocity = to.normalized * Mathf.Max(velocity.magnitude, 22f);
        if (to.magnitude < 0.9f)
        {
            Returned?.Invoke();
            Destroy(gameObject);
            return true;
        }
        return false;
    }

    void Detonate(Vector3 at)
    {
        if (explode > 0f)
        {
            Explosion.At(at, explode, damage, false, crit, burn);
            Destroy(gameObject);
            return;
        }
        Impact(at, radius * 3f);
        Destroy(gameObject);
    }

    /// An enemy shot that brushes past a jumping or dashing player counts as a perfect dodge.
    void CheckNearMiss()
    {
        var p = PlayerController.Current;
        if (dodgeChecked || !p) return;
        Vector3 chest = p.transform.position + Vector3.up * 0.75f;
        Vector3 to = chest - transform.position;
        float d = new Vector2(to.x, to.z).magnitude;
        if (d < closest)
        {
            closest = d;
            return;
        }
        // Moving away now: it has passed. Close enough and the player was evading -> reward.
        if (closest < 1.1f && Vector3.Dot(velocity, to) < 0f)
        {
            dodgeChecked = true;
            if (!p.Grounded || p.Dashing) p.NearMiss();
        }
    }

    /// Destroyed by a melee swing.
    public void Cancel()
    {
        Impact(transform.position, radius * 2f);
        Destroy(gameObject);
    }

    void Impact(Vector3 pos, float size)
    {
        var c = Palette.ColorOf(mat);
        if (mat == "bullet_frost") { Vfx.Shatter(pos); return; }
        if (mat == "bullet_fire") { Vfx.Sparks(pos, c, 4, 3f); return; }
        if (mat == "bullet_rock") { Vfx.Puff(pos, new Color(0.55f, 0.47f, 0.38f, 0.6f), 0.7f, 8); Vfx.Sparks(pos, c, 6, 4f); return; }
        if (mat == "bullet_low") { Vfx.Puff(pos, new Color(0.4f, 1f, 0.3f, 0.5f), 0.6f, 6); return; }
        Vfx.Sparks(pos, c, hitsEnemies ? 8 : 5, hitsEnemies ? 6f : 4f);
        size *= 0.6f;
        var fx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(fx.GetComponent<Collider>());
        fx.transform.position = pos;
        fx.transform.localScale = Vector3.one * size * 2f;
        var r = fx.GetComponent<MeshRenderer>();
        r.sharedMaterial = Palette.Get(mat);
        r.shadowCastingMode = ShadowCastingMode.Off;
        fx.AddComponent<Shrink>().duration = explode > 0f ? 0.3f : 0.15f;
    }
}
