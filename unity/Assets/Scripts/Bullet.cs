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
    float radius;           // how wide it hits (the player's shots: never under MinPlayerShot)
    float look;             // how wide it looks up close (Fire's radius as given)
    Vector3 baseScale;      // its size up close; it grows with distance from the camera (SizeForCamera)
    float farSize;          // small shots: how wide they read (radius); 0 = big enough already, never grows
    float closest = 99f;
    bool dodgeChecked;
    float travelled;
    bool returning;
    Transform model;        // enemy shots: the real thing riding on the shot (bullet, arrow, skull, coin, axe ...)
    Vector3 modelSpin;      // its turn about its own axes, degrees per second
    readonly HashSet<Enemy> hit = new HashSet<Enemy>();
    float power;            // the player's arrow: how far the bow was drawn (0..1)
    public static readonly Color ArrowGold = new Color(1f, 0.82f, 0.4f);
    static Mesh disc, slug;
    static Material groundGlow;
    static readonly Dictionary<string, Material> looks = new Dictionary<string, Material>();

    public bool HitsEnemies => hitsEnemies;

    /// The player's shots hit enemies with at least this radius (metres), whatever their look: a thin
    /// tracer hit only on the line through its centre, which made aiming hard.
    public const float MinPlayerShot = 0.14f;

    /// Far shots look this wide per metre from the camera at least (radius): about 16 px across at 15 m,
    /// so small shots stay visible far off, while up close they keep their own size (a tracer leaving the
    /// gun next to the camera must not swell into a ball).
    const float FarLook = 0.012f;

    public static Bullet Fire(Vector3 from, Vector3 velocity, bool hitsEnemies, float radius, string mat,
        float life = 3f, int damage = 1, float explode = 0f, bool crit = false)
    {
        float look = radius;
        if (hitsEnemies) radius = Mathf.Max(radius, MinPlayerShot);
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = "bullet";
        go.transform.position = from;
        go.transform.localScale = Vector3.one * look * 2f;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = Palette.Get(Base(mat));
        r.shadowCastingMode = ShadowCastingMode.Off;

        var tr = go.AddComponent<TrailRenderer>();
        tr.sharedMaterial = Palette.Trail(Base(mat));
        tr.time = 0.12f;
        tr.startWidth = look * 1.6f;
        tr.endWidth = 0f;
        var c = Palette.ColorOf(Base(mat));
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
        b.look = look;
        b.damage = damage;
        b.explode = explode;
        b.crit = crit;
        b.Style(go, r, tr);
        b.baseScale = go.transform.localScale;
        if (!b.model) b.farSize = look;
        return b;
    }

    /// Palette entry of a shot id (the Goblin King's jade coins are green like every low shot).
    static string Base(string mat) => mat == "bullet_jade" ? "bullet_low" : mat == "bullet_dart" ? "bullet_hex" : mat;

    /// Low shots skim the floor (jump them): the shaman's ghost-fire skulls, the Goblin King's jade coins.
    public static bool IsLow(string mat) => mat == "bullet_low" || mat == "bullet_jade";

    /// Per-projectile look: tracers and arrows stretch along their flight, frost is a crystal
    /// with a mist trail, flames are pure particles, rockets and missiles leave smoke.
    /// Enemy shots (and the player's arrows) are real things instead (see EnemyLook).
    void Style(GameObject go, MeshRenderer r, TrailRenderer tr)
    {
        if (mat == "bullet_dart")
        {
            DartLook(go, r, tr);
            Orient();
            return;
        }
        // The hunting bow's arrows are the same wooden arrows the skeleton archers loose (not a glowing needle).
        if ((!hitsEnemies || mat == "bullet_arrow") && EnemyLook(go, r, tr))
        {
            Orient();
            return;
        }
        switch (mat)
        {
            case "bullet_player":
            case "bullet_heavy":
            case "bullet_laser":
                stretch = 3.5f;
                break;
            case "bullet_arrow":
                stretch = 9f;
                go.transform.localScale = new Vector3(look, look, look * 2f);
                break;
            case "bullet_frost":
                stretch = 1f;
                go.GetComponent<MeshFilter>().sharedMesh = Vfx.Crystal();
                go.transform.localScale = Vector3.one * look * 1.8f;
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
        }
        if (stretch > 0f && mat != "bullet_frost")
            go.transform.localScale = new Vector3(go.transform.localScale.x, go.transform.localScale.y, look * 2f * stretch);
        Orient();
    }

    void Orient()
    {
        if ((stretch > 0f || model) && velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity);
    }

    // ---------- enemy shots: real things, never glowing balls ----------

    /// What each enemy throws. The colour still tells the height: green things skim the floor (jump
    /// them), everything else flies at chest height (sidestep or dash). No lights, nothing blinks.
    bool EnemyLook(GameObject go, MeshRenderer r, TrailRenderer tr)
    {
        go.transform.localScale = Vector3.one; // the parts below are sized in metres
        switch (mat)
        {
            case "bullet_enemy":
                // Goblin gunner: a fat copper-jacketed slug trailing gun smoke.
                model = Part(Slug(), Look("copper"), new Vector3(0.25f, 0.25f, 0.55f));
                farSize = 0.125f;
                trailFx = Vfx.Trail(go.transform, "gunsmoke");
                break;
            case "bullet_arrow":
                // Skeleton archer: a wooden arrow, iron head first, red fletching, a faint streak of air.
                model = Arrow();
                farSize = 0.1f; // a thin shaft: head on it is little more than its point
                tr.sharedMaterial = Palette.Trail("bullet_arrow");
                tr.time = 0.08f;
                tr.startWidth = 0.06f;
                tr.startColor = new Color(1f, 0.95f, 0.85f, 0.4f);
                break;
            case "bullet_low":
                // Ghost-fire shaman: a horned beast skull wreathed in green flame, with a glow on the floor under it.
                model = Model("proj_beast_skull", 0.44f, 0.35f, Fit.AsIs) ?? Part(Vfx.Crystal(), Look("bone"), Vector3.one * 0.22f);
                trailFx = Vfx.Trail(go.transform, "ghostflame");
                GroundGlow(go);
                break;
            case "bullet_jade":
                // Goblin King: big jade coins at knee height, face first, turning like wheels.
                model = Model("proj_jade_coin", 0.56f, 0.25f, Fit.ThinZ) ?? Part(Disc(), Look("jade"), new Vector3(0.5f, 0.03f, 0.5f), Quaternion.Euler(90f, 0f, 0f));
                model.parent.localRotation = Quaternion.Euler(-8f, 0f, 0f); // nearly upright: from the raised play camera it shows as a flat coin
                modelSpin = new Vector3(0f, 0f, 220f);
                GroundGlow(go);
                break;
            case "bullet_gold":
                // Goblin King: thick gold crown coins, face first, turning like wheels, a little glitter.
                model = Model("proj_coin", 0.44f, 0.4f, Fit.ThinZ) ?? Part(Disc(), Look("gold"), new Vector3(0.4f, 0.03f, 0.4f), Quaternion.Euler(90f, 0f, 0f));
                model.parent.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                modelSpin = new Vector3(0f, 0f, 200f);
                trailFx = Vfx.Trail(go.transform, "coinglint");
                break;
            case "bullet_rock":
                // Boar Chief: tumbling boulders throwing off dust.
                model = Model("proj_boulder", 0.5f, 0.12f, Fit.AsIs) ?? Part(Vfx.Crystal(), Look("rock"), new Vector3(0.5f, 0.4f, 0.22f));
                model.localRotation = Random.rotation;
                modelSpin = Random.onUnitSphere * 260f;
                trailFx = Vfx.Trail(go.transform, "rockdust");
                break;
            case "bullet_hex":
                // The lich's skulls: an ice-crowned skull trailing violet flame, turning after the player.
                model = Model("proj_skull", 0.48f, 0.35f, Fit.AsIs) ?? Part(Vfx.Crystal(), Look("bone"), Vector3.one * 0.24f);
                trailFx = Vfx.Trail(go.transform, "hexflame");
                break;
            case "bullet_frost":
                // The lich's ice ring: jagged ice shards, point first, trailing frost mist.
                model = Model("proj_ice_shard", 0.62f, 0.45f, Fit.AsIs) ?? Part(Vfx.Crystal(), Look("ice"), Vector3.one * 0.2f);
                trailFx = Vfx.Trail(go.transform, "icemist");
                break;
            case "bullet_axe":
                // Orc Warlord: throwing axes, blade facing you, whirling like pinwheels.
                model = Model("proj_axe", 0.72f, 0.4f, Fit.ThinZ) ?? Part(Vfx.Crystal(), Look("iron_axe"), new Vector3(0.45f, 0.3f, 0.06f));
                model.parent.localRotation = Quaternion.Euler(-30f, 0f, 0f);
                modelSpin = new Vector3(0f, 0f, -540f);
                break;
            default:
                return false;
        }
        r.enabled = false; // the sphere was only ever the glowing ball
        tr.enabled = mat == "bullet_arrow";
        return true;
    }

    /// The assassin's dart: the same kunai held in the hand (Meshy model, point first), a faint violet
    /// streak behind it; a dark steel sliver if the model is missing.
    void DartLook(GameObject go, MeshRenderer r, TrailRenderer tr)
    {
        go.transform.localScale = Vector3.one; // sized in metres below
        var spin = Pivot("dart");
        var dart = Weapons.Get("raven_darts");
        if (dart != null && Resources.Load<GameObject>("Models/Weapons/raven_darts"))
            Weapons.MakeVisual(dart, spin, go.layer);
        else
            AddMesh(spin, Vfx.Crystal(), Look("steel"), new Vector3(0f, 0f, 0.1f), new Vector3(0.05f, 0.025f, 0.3f), Quaternion.identity);
        model = spin;
        farSize = 0.08f;
        r.enabled = false;
        tr.sharedMaterial = Palette.Trail("bullet_hex");
        tr.time = 0.09f;
        tr.startWidth = 0.08f;
        tr.startColor = new Color(0.75f, 0.5f, 1f, 0.45f);
        tr.endColor = new Color(0.5f, 0.25f, 1f, 0f);
    }

    enum Fit { AsIs, ThinZ }

    /// A Meshy model (Resources/Models/Props/<id>) riding on the shot: `size` across its longest side,
    /// centred on the shot, faintly self-lit so it reads in the dark rooms (no bloom). The prepped models
    /// face forward (skulls, the ice shard's point); ThinZ turns the thinnest side forward instead (coins
    /// and axes show their face). Returns the spinning transform (its parent is a still pivot for a fixed
    /// tilt), or null when the model is missing.
    Transform Model(string id, float size, float glow, Fit fit)
    {
        var prefab = Resources.Load<GameObject>("Models/Props/" + id);
        if (!prefab) return null;
        var spin = Pivot(id);
        var m = Instantiate(prefab, spin, false);
        var rs = m.GetComponentsInChildren<Renderer>();
        var b = Measure(rs);
        Vector3 e = b.size;
        if (fit == Fit.ThinZ)
            m.transform.localRotation = e.x <= e.y && e.x <= e.z ? Quaternion.Euler(0f, 90f, 0f) : e.y <= e.z ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        b = Measure(rs);
        float k = size / Mathf.Max(b.size.x, b.size.y, b.size.z);
        m.transform.localScale = Vector3.one * k;
        m.transform.localPosition = -(b.center - spin.position) * k;
        foreach (var r in rs)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.sharedMaterial = SelfLit(id, r.sharedMaterial, glow);
        }
        return spin;
    }

    /// pivot (child of the shot, holds a fixed tilt) -> spin (turns as it flies) -> parts.
    Transform Pivot(string name)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(transform, false);
        var spin = new GameObject("spin").transform;
        spin.SetParent(pivot, false);
        return spin;
    }

    static Bounds Measure(Renderer[] rs)
    {
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material SelfLit(string id, Material src, float amount)
    {
        if (looks.TryGetValue(id, out var m) && m) return m;
        m = new Material(src) { name = "shot_" + id };
        LootFx.MakeSelfLit(m, amount);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
        looks[id] = m;
        return m;
    }

    /// A plain mesh part under a fresh pivot (procedural shots, and stand-ins when a Meshy model is missing).
    Transform Part(Mesh mesh, Material mat, Vector3 scale, Quaternion? rot = null)
    {
        var spin = Pivot("part");
        AddMesh(spin, mesh, mat, Vector3.zero, scale, rot ?? Quaternion.identity);
        return spin;
    }

    static void AddMesh(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Quaternion rot)
    {
        var go = new GameObject("mesh");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    /// An arrow along +Z, 1.2 m at scale 1 (scaled up to read head-on): wooden shaft, flat iron head, three red vanes.
    Transform Arrow()
    {
        var spin = Pivot("arrow");
        spin.localScale = Vector3.one * 1.6f;
        AddMesh(spin, Disc(), Look("wood"), new Vector3(0f, 0f, -0.08f), new Vector3(0.035f, 0.36f, 0.035f), Quaternion.Euler(90f, 0f, 0f));
        AddMesh(spin, Vfx.Crystal(), Look("steel"), new Vector3(0f, 0f, 0.34f), new Vector3(0.075f, 0.025f, 0.085f), Quaternion.identity);
        for (int i = 0; i < 3; i++)
        {
            var around = Quaternion.Euler(0f, 0f, 120f * i);
            AddMesh(spin, Vfx.Crystal(), Look("fletch"), around * new Vector3(0f, 0.04f, -0.36f), new Vector3(0.006f, 0.045f, 0.075f), around);
        }
        return spin;
    }

    /// A fat pistol slug along +Z, 1 long and 0.42 across: flat base, straight jacket, round nose.
    static Mesh Slug()
    {
        if (slug) return slug;
        // (z, radius) from the base edge to the tip
        var prof = new[] { new Vector2(-0.5f, 0.17f), new Vector2(-0.47f, 0.21f), new Vector2(0.02f, 0.21f), new Vector2(0.17f, 0.2f),
                           new Vector2(0.29f, 0.17f), new Vector2(0.38f, 0.13f), new Vector2(0.45f, 0.08f), new Vector2(0.49f, 0.035f), new Vector2(0.5f, 0f) };
        const int sides = 16;
        var v = new List<Vector3>();
        var t = new List<int>();
        foreach (var p in prof)
            for (int j = 0; j <= sides; j++)
            {
                float a = j * Mathf.PI * 2f / sides;
                v.Add(new Vector3(Mathf.Cos(a) * p.y, Mathf.Sin(a) * p.y, p.x));
            }
        for (int i = 0; i < prof.Length - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * (sides + 1) + j, b = a + sides + 1;
                t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
        // The base: its own vertices, so its edge stays sharp.
        int c = v.Count;
        v.Add(new Vector3(0f, 0f, -0.5f));
        for (int j = 0; j <= sides; j++)
        {
            float a = j * Mathf.PI * 2f / sides;
            v.Add(new Vector3(Mathf.Cos(a) * prof[0].y, Mathf.Sin(a) * prof[0].y, -0.5f));
        }
        for (int j = 0; j < sides; j++) t.AddRange(new[] { c, c + 2 + j, c + 1 + j });
        slug = new Mesh { name = "slug" };
        slug.SetVertices(v);
        slug.SetTriangles(t, 0);
        slug.RecalculateNormals();
        slug.RecalculateBounds();
        return slug;
    }

    /// Plain lit materials for the procedural shots and stand-ins, lifted a little so they read in the dark.
    static Material Look(string name)
    {
        if (looks.TryGetValue(name, out var m) && m) return m;
        var (c, metal, smooth, glow) = name switch
        {
            "copper" => (new Color(0.95f, 0.5f, 0.22f), 0.55f, 0.6f, 0.2f),
            "wood" => (new Color(0.62f, 0.42f, 0.24f), 0f, 0.25f, 0.15f),
            "steel" => (new Color(0.62f, 0.64f, 0.7f), 0.6f, 0.55f, 0.15f),
            "fletch" => (new Color(0.85f, 0.18f, 0.12f), 0f, 0.2f, 0.25f),
            "bone" => (new Color(0.9f, 0.86f, 0.74f), 0f, 0.3f, 0.2f),
            "jade" => (new Color(0.2f, 0.75f, 0.4f), 0f, 0.6f, 0.25f),
            "gold" => (new Color(1f, 0.75f, 0.25f), 0.7f, 0.6f, 0.25f),
            "rock" => (new Color(0.5f, 0.42f, 0.35f), 0f, 0.15f, 0.08f),
            "ice" => (new Color(0.7f, 0.9f, 1f), 0f, 0.7f, 0.35f),
            _ => (new Color(0.3f, 0.3f, 0.33f), 0.5f, 0.4f, 0.1f),
        };
        m = new Material(Palette.Get("steel")) { name = "shot_" + name };
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.SetColor("_EmissionColor", c * glow);
        looks[name] = m;
        return m;
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

    /// The player's arrow, drawn `c` of the way (0..1): bigger the further it was drawn, with a gold
    /// streak about as wide as what it hits (Fire was given the matching radius), gold glints behind a
    /// well-drawn one, and gold sparks wherever it goes through someone.
    public void Empower(float c)
    {
        power = c;
        if (model) model.localScale = Vector3.one * (1.7f + 1.3f * c);
        var tr = GetComponent<TrailRenderer>();
        tr.time = 0.1f + 0.15f * c;
        tr.startWidth = Mathf.Lerp(0.07f, radius * 1.4f, c);
        tr.startColor = Color.Lerp(new Color(1f, 0.95f, 0.85f, 0.4f), new Color(ArrowGold.r, ArrowGold.g, ArrowGold.b, 0.55f), c);
        tr.endColor = new Color(ArrowGold.r, ArrowGold.g, ArrowGold.b, 0f);
        if (c < 0.5f) return;
        trailFx = Vfx.Trail(transform, "glitter");
        var em = trailFx.emission;
        em.rateOverDistance = 3f; // the arrow flies fast: a few glints per metre is plenty
    }

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
        SizeForCamera();
        if (model && modelSpin != Vector3.zero) model.Rotate(modelSpin * dt, Space.Self);
        if (burn > 0f && hitsEnemies) Reactions.CheckClouds(transform.position, 0.3f); // flames reaching poison gas
        Vector3 step = velocity * dt;
        bool got = Physics.Raycast(transform.position, step.normalized, out var rh, step.magnitude, mask, QueryTriggerInteraction.Ignore);
        // The player's shots hit enemies with their whole width, not just the line through their centre
        // (walls still stop them on that line, so they don't snag on corners).
        if (hitsEnemies && (mask & Layers.EnemyMask) != 0
            && Physics.SphereCast(transform.position, radius, step.normalized, out var sh, step.magnitude, Layers.EnemyMask, QueryTriggerInteraction.Ignore)
            && (!got || sh.distance < rh.distance))
        {
            rh = sh;
            got = true;
        }
        if (got)
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
            // Already hit this one (pierce / boomerang): slip past it, along the line of flight.
            transform.position += velocity.normalized * (rh.distance + 0.6f);
            return false;
        }
        if (explode > 0f) { Detonate(rh.point); return true; }
        hit.Add(e);
        e.TakeDamage(damage, velocity.normalized, crit, explode > 0f ? 9f : 3.5f, rh.point);
        if (slow > 0f) e.ApplySlow(slow, 2f);
        if (burn > 0f) e.ApplyBurn(burn);
        if (returnTo || pierce > 0)
        {
            if (!returnTo) pierce--;
            if (power > 0f) Vfx.Sparks(rh.point, ArrowGold, 10 + (int)(10 * power), 5f);
            transform.position += velocity.normalized * (rh.distance + 0.6f);
            return false;
        }
        if (power > 0f) Vfx.Sparks(rh.point, ArrowGold, 10 + (int)(10 * power), 5f);
        Impact(rh.point, radius * 3f);
        Destroy(gameObject);
        return true;
    }

    /// Small shots far from the camera grow to stay visible (see FarLook); flames are particles and stay.
    void SizeForCamera()
    {
        var cam = Camera.main;
        if (!cam || farSize <= 0f || mat == "bullet_fire") return;
        float grow = Mathf.Max(1f, Vector3.Distance(cam.transform.position, transform.position) * FarLook / farSize);
        transform.localScale = baseScale * grow;
        var tr = GetComponent<TrailRenderer>();
        if (tr) tr.widthMultiplier = grow;
    }

    void Steer(float dt)
    {
        // Enemy shots that home (the lich's skulls) turn towards the player's chest.
        if (!hitsEnemies)
        {
            var p = PlayerController.Current;
            if (!p || p.dead || p.Stealthed) return; // a hidden player can't be locked onto: it flies on straight
            Vector3 aim = p.transform.position + Vector3.up * 1f - transform.position;
            aim.y = 0f; // stay at their readable height
            velocity = Vector3.RotateTowards(velocity, aim.normalized * velocity.magnitude, homing * Mathf.Deg2Rad * dt, 0f);
            return;
        }
        Enemy best = null;
        float bestD = 30f;
        foreach (var e in Enemy.Live)
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
        var c = Palette.ColorOf(Base(mat));
        if (mat == "bullet_frost") { Vfx.Shatter(pos); return; }
        if (mat == "bullet_fire") { Vfx.Sparks(pos, c, 4, 3f); return; }
        if (model) { Break(pos); return; }
        // Sparks and a little dust; no glowing ball flashing up where it hits (the user doesn't want orbs).
        Vfx.Sparks(pos, c, hitsEnemies ? 8 : 5, hitsEnemies ? 6f : 4f);
        Vfx.Puff(pos, new Color(0.55f, 0.52f, 0.5f, 0.35f), Mathf.Max(0.25f, size * 0.8f), 3);
    }

    /// A real thing hitting something breaks or bounces off: chips, dust, a few sparks, no flash of light.
    void Break(Vector3 pos)
    {
        switch (mat)
        {
            case "bullet_low":
                Vfx.Chips(pos, new Color(0.85f, 0.82f, 0.7f), 7, 3f);
                Vfx.Puff(pos, new Color(0.35f, 0.75f, 0.3f, 0.45f), 0.6f, 6);
                break;
            case "bullet_hex":
                Vfx.Chips(pos, new Color(0.85f, 0.82f, 0.7f), 8, 3.5f);
                Vfx.Puff(pos, new Color(0.45f, 0.25f, 0.7f, 0.5f), 0.7f, 7);
                break;
            case "bullet_jade":
                Vfx.Chips(pos, new Color(0.3f, 0.8f, 0.45f), 8, 3.5f);
                break;
            case "bullet_gold":
                Vfx.Chips(pos, new Color(1f, 0.78f, 0.3f), 6, 3f);
                break;
            case "bullet_rock":
                Vfx.Puff(pos, new Color(0.55f, 0.47f, 0.38f, 0.6f), 0.7f, 8);
                Vfx.Chips(pos, new Color(0.45f, 0.38f, 0.3f), 8, 4f);
                break;
            case "bullet_axe":
                Vfx.Sparks(pos, new Color(1f, 0.75f, 0.45f), 5, 4f);
                Vfx.Chips(pos, new Color(0.4f, 0.28f, 0.18f), 4, 2.5f);
                break;
            case "bullet_arrow":
                Vfx.Chips(pos, new Color(0.62f, 0.42f, 0.24f), 5, 2.5f);
                break;
            default: // the gunner's slug
                Vfx.Puff(pos, new Color(0.6f, 0.58f, 0.55f, 0.45f), 0.4f, 5);
                Vfx.Sparks(pos, new Color(1f, 0.7f, 0.35f), 3, 3f);
                break;
        }
    }
}
