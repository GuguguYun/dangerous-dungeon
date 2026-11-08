using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// Loot and pickup looks: rarity light pillars (Meshy/LootBeam), glows on the floor (Meshy/GroundGlow),
/// rising motes, the Meshy potion / energy crystal models with a self-lit material, and the soft burst
/// when something is collected. Materials are cached per look and meshes are built once.
public static class LootFx
{
    public const string PotionModel = "potion_red", CrystalModel = "energy_crystal";
    public static readonly Color HealthColor = new Color(1f, 0.28f, 0.32f);
    public static readonly Color EnergyColor = new Color(0.3f, 0.68f, 1f);

    static Mesh floorQuad, beamQuad, haloQuad;
    static Material beamBase, groundBase, haloBase;
    static readonly Dictionary<int, Material> beams = new Dictionary<int, Material>();
    static readonly Dictionary<Vector4, Material> grounds = new Dictionary<Vector4, Material>();
    static readonly Dictionary<string, Material> glows = new Dictionary<string, Material>();
    static readonly Dictionary<Vector4, Material> halos = new Dictionary<Vector4, Material>();

    // ---------- loot pillars ----------

    public static float BeamHeight(int rarity) => 1.8f + rarity * 1.0f;

    /// The full pillar for a weapon on the floor: beam, floor glow, rising motes. Rarer = taller,
    /// wider and brighter; epic and legendary get a slowly turning ring of dashes. Returns the beam
    /// (its Y scale is the height, so the drop can grow it in).
    public static Transform LootBeam(Transform parent, int rarity)
    {
        rarity = Mathf.Clamp(rarity, 0, Weapons.RarityColors.Length - 1);
        var c = Weapons.RarityColors[rarity];
        float w = 1.0f + rarity * 0.14f;
        var beam = NewRenderer("loot_beam", parent, BeamQuad, BeamMaterial(rarity));
        beam.transform.localScale = new Vector3(w, BeamHeight(rarity), w);
        float glow = rarity == 0 ? 0.55f : 0.8f + 0.2f * rarity;
        var ground = Glow(parent, c, glow, 1.8f + 0.15f * rarity, 1.5f, rarity >= 2 ? 0.9f : 0f);
        ground.name = "loot_ground";
        Motes(parent, c, 0.42f, 2.5f + 2.5f * rarity, 0.55f + 0.1f * rarity);
        return beam.transform;
    }

    static Material BeamMaterial(int rarity)
    {
        if (beams.TryGetValue(rarity, out var m) && m) return m;
        if (!beamBase) beamBase = Resources.Load<Material>("VFX/vfx_beam");
        m = beamBase ? new Material(beamBase) : new Material(Vfx.Additive);
        m.name = "loot_beam_" + rarity;
        float k = rarity == 0 ? 0.4f : 0.55f + 0.2f * rarity;
        m.SetVector("_Color", (Vector4)(Weapons.RarityColors[rarity].linear * k));
        m.SetFloat("_Core", 0.9f);
        m.SetFloat("_Glow", 0.75f);
        m.SetFloat("_Falloff", 1.6f);
        m.SetFloat("_Streaks", 0.3f);
        beams[rarity] = m;
        return m;
    }

    // ---------- floor glow ----------

    /// A glow on the floor: a soft light pool, a ring (ring > 0) and turning dashes (dashes > 0).
    /// diameter in metres (the ring sits at a quarter of it); colour is an sRGB hue, intensity linear.
    /// Lies 2 cm above the parent's origin.
    public static Renderer Glow(Transform parent, Color c, float intensity, float diameter, float ring, float dashes, float pool = 0.7f)
    {
        var r = NewRenderer("floor_glow", parent, FloorQuad, GroundMaterial(c, intensity, ring, dashes, pool));
        r.transform.localPosition = Vector3.up * 0.02f;
        r.transform.localScale = new Vector3(diameter, 1f, diameter);
        return r;
    }

    static Material GroundMaterial(Color c, float intensity, float ring, float dashes, float pool)
    {
        var lin = c.linear * intensity;
        var key = new Vector4(lin.r, lin.g, lin.b, ring * 100f + dashes * 10f + pool);
        if (grounds.TryGetValue(key, out var m) && m) return m;
        if (!groundBase) groundBase = Resources.Load<Material>("VFX/vfx_ground");
        m = groundBase ? new Material(groundBase) : new Material(Vfx.Additive);
        m.name = "floor_glow";
        m.SetVector("_Color", new Vector4(lin.r, lin.g, lin.b, 1f));
        m.SetFloat("_Pool", pool);
        m.SetFloat("_PoolFalloff", 7f);
        m.SetFloat("_Ring", ring);
        m.SetFloat("_RingRadius", 0.5f);
        m.SetFloat("_RingWidth", 0.03f);
        m.SetFloat("_Dashes", dashes);
        m.SetFloat("_DashCount", 14f);
        m.SetFloat("_Spin", 0.3f);
        grounds[key] = m;
        return m;
    }

    // ---------- halo ----------

    /// A soft round glow facing the camera, centred on the parent (diameter in metres).
    public static Renderer Halo(Transform parent, Color c, float intensity, float diameter)
    {
        var lin = c.linear * intensity;
        var key = new Vector4(lin.r, lin.g, lin.b, 1f);
        if (!halos.TryGetValue(key, out var m) || !m)
        {
            if (!haloBase) haloBase = Resources.Load<Material>("VFX/vfx_halo");
            m = haloBase ? new Material(haloBase) : new Material(Vfx.Additive);
            m.name = "halo";
            m.SetVector("_Color", key);
            m.SetFloat("_Falloff", 5f);
            halos[key] = m;
        }
        var r = NewRenderer("halo", parent, HaloQuad, m);
        r.transform.localScale = Vector3.one * diameter;
        return r;
    }

    // ---------- motes ----------

    /// Small sparks of light drifting slowly up out of a circle on the floor.
    public static ParticleSystem Motes(Transform parent, Color c, float radius, float rate, float rise)
    {
        var ps = NewParticles("motes", parent, Vfx.Additive);
        var m = ps.main;
        m.loop = true;
        m.duration = 5f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(rise * 0.6f, rise);
        m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        m.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(c, Color.white, 0.35f), c);
        m.maxParticles = 64;
        var em = ps.emission;
        em.rateOverTime = rate;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = radius;
        sh.radiusThickness = 1f;
        sh.rotation = new Vector3(-90f, 0f, 0f); // emit upward from a flat circle
        sh.position = Vector3.up * 0.05f;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.15f;
        FadeInOut(ps, 0.2f);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.5f));
        ps.Play();
        return ps;
    }

    // ---------- pickup models ----------

    /// The Meshy model for a pickup or shop ware, centred on the parent and scaled; null when the model
    /// was not imported (callers keep their old look). Self-lit so it reads in dark rooms.
    public static GameObject PickupModel(string id, Transform parent, float scale, bool shadows)
    {
        if (!Props.Has(id)) return null;
        var go = Props.Spawn(id, parent.position, parent.rotation, parent, false);
        go.transform.localScale = Vector3.one * scale;
        var b = new Bounds();
        bool first = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = GlowMaterial(id, r.sharedMaterial);
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        // Props have their origin at the bottom centre: move the middle onto the parent.
        go.transform.position -= b.center - parent.position;
        return go;
    }

    /// The model's own material with its texture as emission (BuildScript saves it as VFX/glow_<id> so
    /// the emissive variant ships; made at runtime if missing).
    static Material GlowMaterial(string id, Material src)
    {
        if (glows.TryGetValue(id, out var m) && m) return m;
        m = Resources.Load<Material>("VFX/glow_" + id);
        if (!m && src)
        {
            m = new Material(src) { name = "glow_" + id };
            MakeSelfLit(m, id == CrystalModel ? 1.1f : 0.3f);
        }
        glows[id] = m ? m : src;
        return glows[id];
    }

    /// URP Lit: the base map doubles as the emission map, at a fraction (linear) of full brightness.
    public static void MakeSelfLit(Material m, float amount)
    {
        var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
        if (m.HasProperty("_EmissionColor")) m.SetVector("_EmissionColor", new Vector4(amount, amount, amount, 1f));
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.7f);
    }

    // ---------- collect ----------

    /// Soft feedback when the player takes a pickup: motes rising around the body, a ring on the floor
    /// and a short local light (big), or just a small sparkle (energy crystals arrive in bunches).
    public static void Collect(Vector3 feet, Color c, bool big)
    {
        var host = new GameObject("collect").transform;
        host.position = feet;
        var ps = NewParticles("collect_motes", host, Vfx.Additive);
        var m = ps.main;
        m.loop = false;
        m.duration = 0.1f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(big ? 0.7f : 0.35f, big ? 1.1f : 0.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(big ? 0.8f : 0.5f, big ? 1.8f : 1.2f);
        m.startSize = new ParticleSystem.MinMaxCurve(big ? 0.07f : 0.05f, big ? 0.14f : 0.09f);
        m.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(c, Color.white, 0.4f), c);
        m.gravityModifier = -0.15f;
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(big ? 22 : 6)) });
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = big ? 0.55f : 0.3f;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        sh.position = Vector3.up * (big ? 0.3f : 0.9f);
        FadeInOut(ps, 0.1f);
        ps.Play();
        Object.Destroy(host.gameObject, 1.4f);
        if (!big) return;
        Vfx.Ring(feet, 1.1f, c);
        Vfx.Flash(feet + Vector3.up, c, 2f, 4f, 0.4f);
    }

    // ---------- helpers ----------

    static MeshRenderer NewRenderer(string name, Transform parent, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        return r;
    }

    static ParticleSystem NewParticles(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// Particles fade in, hold, and fade out: nothing pops on or off.
    static void FadeInOut(ParticleSystem ps, float fadeIn)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    /// A 1 x 1 quad lying flat (XZ), centred, UV 0..1.
    static Mesh FloorQuad
    {
        get
        {
            if (floorQuad) return floorQuad;
            floorQuad = new Mesh { name = "floor_quad" };
            floorQuad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) });
            floorQuad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            floorQuad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            floorQuad.RecalculateNormals();
            floorQuad.RecalculateBounds();
            return floorQuad;
        }
    }

    /// A 1 x 1 quad centred on the origin (the halo shader turns it to the camera).
    static Mesh HaloQuad
    {
        get
        {
            if (haloQuad) return haloQuad;
            haloQuad = new Mesh { name = "halo_quad" };
            haloQuad.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) });
            haloQuad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            haloQuad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            haloQuad.bounds = new Bounds(Vector3.zero, Vector3.one);
            return haloQuad;
        }
    }

    /// A 1 x 1 upright quad from the origin up (x -0.5..0.5, y 0..1); the beam shader turns it to the
    /// camera, so its bounds cover the whole turn.
    static Mesh BeamQuad
    {
        get
        {
            if (beamQuad) return beamQuad;
            beamQuad = new Mesh { name = "beam_quad" };
            beamQuad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) });
            beamQuad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            beamQuad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            beamQuad.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 1f));
            return beamQuad;
        }
    }
}
