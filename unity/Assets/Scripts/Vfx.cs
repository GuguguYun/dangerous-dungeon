using UnityEngine;
using UnityEngine.Rendering;

/// Particle effects built in code: muzzle flashes, hit sparks, explosions, frost, fire, smoke,
/// lightning. Additive particles use an HDR-tinted material so bloom makes them glow.
/// BuildScript saves the materials under Resources/VFX so the URP particle shader is kept in builds.
public static class Vfx
{
    static Material add, alpha, fire, fresnelBase, wispMat, featherMat;
    static Mesh crystal;
    static readonly System.Collections.Generic.Dictionary<Color, Material> fresnels = new System.Collections.Generic.Dictionary<Color, Material>();

    public static Material Additive => add ? add : add = Load("VFX/vfx_add", true);
    public static Material AlphaBlend => alpha ? alpha : alpha = Load("VFX/vfx_alpha", false);
    /// Additive with a streaky noise flame texture (flamer, burning, fire pits, meteors).
    public static Material Fire => fire ? fire : fire = Resources.Load<Material>("VFX/vfx_fire") ?? Additive;

    /// A force-field material in the given colour (bright rim, clear middle, moving bands).
    public static Material Fresnel(Color c, float power = 2.5f, float inner = 0.05f, float bands = 0.6f)
    {
        var key = new Color(c.r, c.g, c.b, power + inner * 10f + bands * 100f);
        if (fresnels.TryGetValue(key, out var m) && m) return m;
        if (!fresnelBase) fresnelBase = Resources.Load<Material>("VFX/vfx_fresnel");
        m = fresnelBase ? new Material(fresnelBase) : new Material(Additive);
        m.SetColor("_Color", c);
        m.SetFloat("_Power", power);
        m.SetFloat("_Inner", inner);
        m.SetFloat("_Bands", bands);
        if (!fresnelBase && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c * 0.4f);
        fresnels[key] = m;
        return m;
    }

    /// A soft blob broken up by stretched value noise: reads as a tongue of flame or a wisp.
    public static Texture2D FlameTexture()
    {
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "vfx_flame", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n - 0.5f;
            float d = Mathf.Sqrt(u * u * 4f + v * v * 2.2f) * 2f;
            float noise = Mathf.PerlinNoise(x * 0.11f, y * 0.045f) * 0.65f + Mathf.PerlinNoise(x * 0.3f + 7f, y * 0.12f) * 0.35f;
            float a = Mathf.Clamp01(1f - d) * Mathf.Lerp(0.35f, 1.25f, noise);
            t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a * a * 1.4f)));
        }
        t.Apply();
        return t;
    }

    static Material Load(string path, bool additive)
    {
        var m = Resources.Load<Material>(path);
        return m ? m : CreateMaterial(additive, SoftTexture());
    }

    /// Radial soft dot used by every particle.
    public static Texture2D SoftTexture()
    {
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "vfx_soft", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
            float a = Mathf.Clamp01(1f - d);
            t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }
        t.Apply();
        return t;
    }

    public static Material CreateMaterial(bool additive, Texture tex)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        var m = new Material(shader) { name = additive ? "vfx_add" : "vfx_alpha" };
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        // HDR tint on the additive material so bloom picks the sparks up.
        var c = additive ? new Color(3f, 3f, 3f, 1f) : Color.white;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    /// An elongated octahedron for ice shards.
    public static Mesh Crystal()
    {
        if (crystal) return crystal;
        var v = new[]
        {
            new Vector3(0, 0, 1.6f), new Vector3(0, 0, -0.8f),
            new Vector3(0.45f, 0, 0), new Vector3(-0.45f, 0, 0), new Vector3(0, 0.45f, 0), new Vector3(0, -0.45f, 0),
        };
        var t = new[] { 0, 4, 2, 0, 2, 5, 0, 5, 3, 0, 3, 4, 1, 2, 4, 1, 5, 2, 1, 3, 5, 1, 4, 3 };
        crystal = new Mesh { name = "crystal", vertices = v, triangles = t };
        crystal.RecalculateNormals();
        crystal.RecalculateBounds();
        return crystal;
    }

    static ParticleSystem Make(string name, Vector3 at, Material mat, float duration, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static ParticleSystem.MinMaxGradient Grad(Color a, Color b) => new ParticleSystem.MinMaxGradient(a, b);

    static void Fade(ParticleSystem ps, Color from, Color to)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    /// Fire colour over life: white-hot core, yellow, orange, deep red, fading out as dark smoke.
    static void Flames(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.97f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.78f, 0.25f), 0.2f),
                    new GradientColorKey(new Color(1f, 0.42f, 0.08f), 0.5f), new GradientColorKey(new Color(0.55f, 0.08f, 0.04f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    /// Spirit fire: fades in pale, burns green (or violet), darkens and fades out; no flash anywhere.
    static void SpiritFlames(ParticleSystem ps, bool green)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(green
                ? new[] { new GradientColorKey(new Color(0.8f, 1f, 0.55f), 0f), new GradientColorKey(new Color(0.3f, 0.95f, 0.3f), 0.35f), new GradientColorKey(new Color(0.05f, 0.4f, 0.15f), 1f) }
                : new[] { new GradientColorKey(new Color(0.95f, 0.8f, 1f), 0f), new GradientColorKey(new Color(0.6f, 0.3f, 1f), 0.35f), new GradientColorKey(new Color(0.25f, 0.08f, 0.5f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.15f), new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    static void Grow(ParticleSystem ps, float to)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, to));
    }

    static void Burst(ParticleSystem ps, int count)
    {
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
    }

    static void Shrink(ParticleSystem ps, float end = 0f)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, end));
    }

    static void Finish(ParticleSystem ps, float life)
    {
        ps.Play();
        Object.Destroy(ps.gameObject, ps.main.duration + life + 0.1f);
    }

    public static void Flash(Vector3 at, Color c, float intensity, float range, float seconds)
    {
        var l = new GameObject("flash").AddComponent<Light>();
        l.transform.position = at;
        l.type = LightType.Point;
        l.color = c;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        l.gameObject.AddComponent<FadeLight>().duration = seconds;
    }

    // ---------- one-shots ----------

    public static void Sparks(Vector3 at, Color c, int count = 10, float speed = 6f)
    {
        var ps = Make("sparks", at, Additive, 0.05f);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor = Grad(c, Color.white);
        main.gravityModifier = 1.2f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.05f;
        Burst(ps, count);
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f;
        r.lengthScale = 1f;
        Fade(ps, Color.white, c);
        Finish(ps, 0.3f);
    }

    public static void MuzzleFlash(Vector3 at, Vector3 dir, Color c)
    {
        var ps = Make("muzzle", at, Additive, 0.03f);
        ps.transform.rotation = Quaternion.LookRotation(dir);
        var main = ps.main;
        main.startLifetime = 0.06f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startColor = Grad(c, new Color(1f, 0.95f, 0.8f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 20f;
        sh.radius = 0.02f;
        Burst(ps, 6);
        Shrink(ps);
        Finish(ps, 0.06f);
        MuzzleGlow.Pulse(at, c); // a steady swell under rapid fire, not a per-shot strobe
    }

    public static void Explosion(Vector3 at, float radius)
    {
        // Fireball
        var fire = Make("fireball", at, Additive, 0.05f);
        var m = fire.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.5f, radius * 3.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(radius * 0.35f, radius * 0.7f);
        m.startColor = Grad(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
        var sh = fire.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.2f;
        Burst(fire, 26);
        var limit = fire.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1f;
        limit.dampen = 0.25f;
        Fade(fire, new Color(1f, 0.9f, 0.5f), new Color(0.9f, 0.2f, 0.05f));
        Shrink(fire, 0.3f);
        Finish(fire, 0.5f);

        // Smoke
        var smoke = Make("smoke", at, AlphaBlend, 0.1f);
        var sm = smoke.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, radius);
        sm.startSize = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 0.9f);
        sm.startColor = Grad(new Color(0.2f, 0.18f, 0.18f, 0.7f), new Color(0.35f, 0.32f, 0.3f, 0.6f));
        sm.gravityModifier = -0.15f;
        var ssh = smoke.shape;
        ssh.shapeType = ParticleSystemShapeType.Sphere;
        ssh.radius = radius * 0.3f;
        Burst(smoke, 12);
        Fade(smoke, new Color(0.3f, 0.28f, 0.27f), new Color(0.15f, 0.15f, 0.15f));
        var grow = smoke.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        Finish(smoke, 1.4f);

        Ring(at, radius * 1.3f, new Color(1f, 0.6f, 0.25f));
        Sparks(at, new Color(1f, 0.6f, 0.2f), 18, 10f);
        Flash(at + Vector3.up, new Color(1f, 0.6f, 0.3f), 8f, radius * 4f, 0.25f);
    }

    /// A flat ring racing out along the floor.
    public static void Ring(Vector3 at, float radius, Color c)
    {
        var ps = Make("ring", new Vector3(at.x, 0.1f, at.z), Additive, 0.02f);
        var m = ps.main;
        m.startLifetime = 0.3f;
        m.startSpeed = 0f;
        m.startSize = radius * 2f;
        m.startColor = c;
        Burst(ps, 1);
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));
        Fade(ps, c, c);
        Finish(ps, 0.3f);
    }

    /// Small solid bits (bone, stone, jade, wood) knocked off a thrown thing: unlit, they just fall.
    public static void Chips(Vector3 at, Color c, int count, float speed)
    {
        var ps = Make("chips", at, AlphaBlend, 0.05f);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        m.startColor = Grad(c, c * 0.7f);
        m.gravityModifier = 1.6f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.08f;
        Burst(ps, count);
        Fade(ps, Color.white, Color.white);
        Finish(ps, 0.6f);
    }

    public static void Shatter(Vector3 at)
    {
        var c = new Color(0.65f, 0.9f, 1f);
        Sparks(at, c, 14, 7f);
        Puff(at, new Color(0.8f, 0.95f, 1f, 0.5f), 0.6f, 6);
    }

    /// A soft alpha-blended puff (frost mist, dust).
    public static void Puff(Vector3 at, Color c, float size, int count)
    {
        var ps = Make("puff", at, AlphaBlend, 0.05f);
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        m.startColor = c;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.2f;
        Burst(ps, count);
        Fade(ps, c, c);
        Finish(ps, 0.6f);
    }

    // ---------- attached emitters ----------

    /// Particles left behind a moving projectile or carried by a burning enemy.
    public static ParticleSystem Trail(Transform parent, string kind)
    {
        bool smoke = kind == "smoke" || kind == "dust" || kind == "gunsmoke" || kind == "rockdust" || kind == "icemist";
        var ps = Make("trail_" + kind, parent.position, smoke || kind == "frost" ? AlphaBlend : Additive, 10f, parent);
        ps.transform.localPosition = Vector3.zero;
        var m = ps.main;
        m.loop = true;
        var em = ps.emission;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        switch (kind)
        {
            case "smoke":
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
                m.startSpeed = 0.2f;
                m.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                m.startColor = new Color(0.55f, 0.52f, 0.5f, 0.55f);
                em.rateOverDistance = 14f;
                sh.radius = 0.05f;
                Fade(ps, new Color(0.7f, 0.65f, 0.6f), new Color(0.3f, 0.3f, 0.3f));
                break;
            case "frost":
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                m.startSpeed = 0.1f;
                m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
                m.startColor = new Color(0.8f, 0.95f, 1f, 0.6f);
                em.rateOverDistance = 10f;
                sh.radius = 0.04f;
                Fade(ps, new Color(0.85f, 0.97f, 1f), new Color(0.6f, 0.85f, 1f));
                break;
            case "fire":
            case "burn":
                bool burn = kind == "burn";
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fire;
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var spinF = ps.rotationOverLifetime;
                spinF.enabled = true;
                spinF.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, burn ? 0.6f : 0.35f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, burn ? 1.2f : 0.6f);
                m.startSize = new ParticleSystem.MinMaxCurve(burn ? 0.25f : 0.35f, burn ? 0.5f : 0.75f);
                m.startColor = Grad(new Color(1f, 0.8f, 0.3f), new Color(1f, 0.4f, 0.1f));
                m.gravityModifier = burn ? -0.4f : 0f;
                if (burn) em.rateOverTime = 26f; else em.rateOverDistance = 14f;
                sh.radius = burn ? 0.35f : 0.1f;
                Flames(ps);
                if (burn) Shrink(ps, 0.2f);
                else
                {
                    // Projectile flames billow out as they travel, so the stream reads as a cone.
                    var grow = ps.sizeOverLifetime;
                    grow.enabled = true;
                    grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.8f));
                }
                break;
            case "pillar":
                // A column of fire shooting up out of a grate.
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fire;
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                m.startColor = Grad(new Color(1f, 0.8f, 0.3f), new Color(1f, 0.4f, 0.1f));
                em.rateOverTime = 70f;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 8f;
                sh.radius = 0.6f;
                sh.rotation = new Vector3(-90f, 0f, 0f);
                Flames(ps);
                Shrink(ps, 0.3f);
                break;
            case "motes":
                // Golden motes rising out of the priest's sanctuary circle.
                m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                m.startColor = Grad(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.8f, 0.35f));
                m.gravityModifier = -0.05f;
                em.rateOverTime = 40f;
                sh.shapeType = ParticleSystemShapeType.Circle;
                sh.radius = 4.3f;
                sh.radiusThickness = 1f;
                sh.rotation = new Vector3(-90f, 0f, 0f);
                Fade(ps, new Color(1f, 0.95f, 0.7f), new Color(1f, 0.75f, 0.3f));
                break;
            case "bubbles":
                // Bubbles that rise and pop out of a poison cloud.
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = AlphaBlend;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                m.startColor = new Color(0.55f, 1f, 0.35f, 0.8f);
                m.gravityModifier = -0.15f;
                em.rateOverTime = 18f;
                sh.radius = 2.4f;
                sh.scale = new Vector3(1f, 0.15f, 1f);
                Fade(ps, new Color(0.7f, 1f, 0.5f), new Color(0.4f, 0.8f, 0.25f));
                break;
            case "gas":
                // A low cloud hugging the floor over the whole 3 m area, there from the first frame.
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = AlphaBlend;
                m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
                m.startSize = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.startColor = new Color(0.5f, 0.95f, 0.25f, 0.38f);
                m.gravityModifier = -0.02f;
                em.rateOverTime = 26f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)36) });
                sh.radius = 2.6f;
                sh.scale = new Vector3(1f, 0.22f, 1f);
                var spin = ps.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
                Fade(ps, new Color(0.6f, 1f, 0.3f), new Color(0.3f, 0.6f, 0.15f));
                break;
            case "sparks":
                // Gunpowder bolt: a spray of hot sparks and a wisp of smoke behind it.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                m.startColor = Grad(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.5f, 0.15f));
                m.gravityModifier = 0.6f;
                em.rateOverDistance = 14f;
                sh.radius = 0.05f;
                Fade(ps, new Color(1f, 0.9f, 0.5f), new Color(0.8f, 0.3f, 0.1f));
                break;
            case "ghostfire":
                // Green spirit flame skimming the floor.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
                m.startColor = Grad(new Color(0.6f, 1f, 0.4f), new Color(0.25f, 0.9f, 0.35f));
                m.gravityModifier = -0.35f;
                em.rateOverTime = 26f;
                em.rateOverDistance = 8f;
                sh.radius = 0.12f;
                Fade(ps, new Color(0.75f, 1f, 0.5f), new Color(0.1f, 0.5f, 0.2f));
                Shrink(ps, 0.2f);
                break;
            case "dust":
                // Rock dust kicked off a tumbling boulder.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
                m.startColor = new Color(0.55f, 0.47f, 0.38f, 0.5f);
                em.rateOverDistance = 7f;
                sh.radius = 0.1f;
                Fade(ps, new Color(0.6f, 0.52f, 0.42f), new Color(0.4f, 0.36f, 0.3f));
                break;
            case "arcane":
                // Violet motes behind the lich's skulls.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                m.startColor = Grad(new Color(0.85f, 0.6f, 1f), new Color(0.55f, 0.3f, 1f));
                em.rateOverDistance = 12f;
                sh.radius = 0.12f;
                Fade(ps, new Color(0.9f, 0.7f, 1f), new Color(0.4f, 0.2f, 0.8f));
                break;
            case "glitter":
                // Gold coin sparkle.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                m.startColor = Grad(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.75f, 0.25f));
                em.rateOverDistance = 9f;
                sh.radius = 0.1f;
                Fade(ps, new Color(1f, 0.95f, 0.7f), new Color(1f, 0.6f, 0.2f));
                break;
            // ---- behind enemy shots (Bullet.EnemyLook): sized in metres, on an unscaled shot ----
            case "gunsmoke":
                // Gun smoke streaming off a slug: warm grey at its base, cooling and spreading behind.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
                m.startColor = new Color(0.85f, 0.75f, 0.62f, 0.5f);
                m.gravityModifier = -0.05f;
                em.rateOverDistance = 18f;
                sh.radius = 0.02f;
                sh.position = new Vector3(0f, 0f, -0.2f);
                Grow(ps, 2.6f);
                Fade(ps, new Color(1f, 0.8f, 0.6f), new Color(0.5f, 0.5f, 0.52f));
                break;
            case "ghostflame":
            case "hexflame":
                // Spirit flame streaming off a flying skull (green: the shaman's, violet: the lich's): one
                // tongue of fire that rides with it (local space), born at the back of the crown and blown
                // back and up, so the face stays clear.
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fire;
                m.simulationSpace = ParticleSystemSimulationSpace.Local;
                m.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                var sway = ps.rotationOverLifetime;
                sway.enabled = true;
                sway.z = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.45f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.46f);
                em.rateOverTime = 75f;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 18f;
                sh.radius = 0.13f;
                sh.rotation = new Vector3(0f, 180f, 0f); // blown backwards
                sh.position = new Vector3(0f, 0.08f, -0.06f);
                var rise = ps.velocityOverLifetime;
                rise.enabled = true;
                rise.space = ParticleSystemSimulationSpace.Local;
                rise.x = new ParticleSystem.MinMaxCurve(0f);
                rise.y = new ParticleSystem.MinMaxCurve(0.7f);
                rise.z = new ParticleSystem.MinMaxCurve(0f);
                SpiritFlames(ps, kind == "ghostflame");
                Shrink(ps, 0.3f);
                break;
            case "coinglint":
                // A few soft gold glints left hanging behind a coin (they fade, never blink).
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                m.startColor = Grad(new Color(1f, 0.9f, 0.55f, 0.6f), new Color(1f, 0.7f, 0.25f, 0.5f));
                em.rateOverDistance = 4f;
                sh.radius = 0.15f;
                Fade(ps, new Color(1f, 0.9f, 0.6f), new Color(1f, 0.6f, 0.2f));
                break;
            case "rockdust":
                // Dust and grit shed by a tumbling boulder.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.75f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
                m.startColor = new Color(0.55f, 0.47f, 0.38f, 0.45f);
                m.gravityModifier = 0.1f;
                em.rateOverDistance = 6f;
                sh.radius = 0.18f;
                Grow(ps, 1.8f);
                Fade(ps, new Color(0.62f, 0.54f, 0.44f), new Color(0.42f, 0.38f, 0.32f));
                break;
            case "icemist":
                // Cold mist curling off an ice shard.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
                m.startColor = new Color(0.85f, 0.95f, 1f, 0.45f);
                m.gravityModifier = 0.05f;
                em.rateOverDistance = 9f;
                sh.radius = 0.06f;
                sh.position = new Vector3(0f, 0f, -0.15f);
                Grow(ps, 2f);
                Fade(ps, new Color(0.9f, 0.97f, 1f), new Color(0.65f, 0.85f, 1f));
                break;
            case "chill":
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
                m.startColor = new Color(0.75f, 0.92f, 1f, 0.5f);
                m.gravityModifier = 0.05f;
                em.rateOverTime = 14f;
                sh.radius = 0.4f;
                Fade(ps, new Color(0.8f, 0.95f, 1f), new Color(0.6f, 0.85f, 1f));
                break;
        }
        ps.Play();
        return ps;
    }

    // ---------- shadow (the assassin's stealth) ----------

    /// A fresh Meshy/Stealth material (dark see-through body, faint violet edge, slow smoke): one per
    /// wearer, since it carries their feet height and fade-in.
    public static Material Stealth()
    {
        var src = Resources.Load<Material>("VFX/vfx_stealth");
        var m = src ? new Material(src) : new Material(Fresnel(new Color(0.6f, 0.35f, 1.4f), 1.6f, 0.02f, 0.4f));
        m.name = "stealth";
        return m;
    }

    /// Dark violet smoke curling slowly up off a hidden body, from just outside its outline (world
    /// space, so it trails behind a move).
    public static ParticleSystem ShadowWisps(Transform body, float height)
    {
        if (!wispMat)
        {
            wispMat = new Material(AlphaBlend) { name = "shadow_wisp" };
            var tex = Fire.mainTexture;
            if (tex) { wispMat.mainTexture = tex; if (wispMat.HasProperty("_BaseMap")) wispMat.SetTexture("_BaseMap", tex); }
        }
        var ps = Make("shadow_wisps", body.position, wispMat, 5f, body);
        ps.transform.localPosition = Vector3.up * height * 0.5f;
        var m = ps.main;
        m.loop = true;
        m.maxParticles = 48;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.75f); // the texture's blob fills about half of it
        m.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        m.startColor = Grad(new Color(0.3f, 0.12f, 0.5f, 0.5f), new Color(0.17f, 0.07f, 0.3f, 0.42f));
        m.gravityModifier = -0.04f; // drifts up, slowly
        var em = ps.emission;
        em.rateOverTime = 9f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.4f;
        sh.radiusThickness = 0.3f; // a shell around the body, so the smoke shows at its edges
        sh.scale = new Vector3(1f, height / 0.8f * 0.75f, 1f); // a tall ellipsoid wrapping the body
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.2f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.4f));
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        SoftFade(ps, 0.25f);
        ps.Play();
        return ps;
    }

    /// A few dark crow feathers shed from the shoulders, rocking gently as they drift down and fade.
    public static ParticleSystem Feathers(Transform body, float height)
    {
        if (!featherMat)
        {
            featherMat = new Material(AlphaBlend) { name = "shadow_feather" };
            var tex = FeatherTexture();
            featherMat.mainTexture = tex;
            if (featherMat.HasProperty("_BaseMap")) featherMat.SetTexture("_BaseMap", tex);
        }
        var ps = Make("shadow_feathers", body.position, featherMat, 5f, body);
        ps.transform.localPosition = Vector3.up * height * 0.7f;
        var m = ps.main;
        m.loop = true;
        m.maxParticles = 12;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.32f);
        m.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        m.startColor = Grad(new Color(0.4f, 0.28f, 0.58f, 1f), new Color(0.22f, 0.14f, 0.36f, 1f));
        m.gravityModifier = 0.03f; // a feather falls slowly
        var em = ps.emission;
        em.rateOverTime = 4f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.42f;
        sh.radiusThickness = 0.15f; // from just outside the shoulders, outward
        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 0.35f;
        drag.dampen = 0.06f;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.45f;
        noise.scrollSpeed = 0.15f;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.1f, 1.1f); // a slow rocking spin, under 65°/s
        SoftFade(ps, 0.15f);
        ps.Play();
        return ps;
    }

    /// A soft-edged feather: a curved quill down the middle, a vane wider on one side, faint barbs.
    /// Grey levels (the particle colour tints it): the quill is the lightest part.
    public static Texture2D FeatherTexture()
    {
        const int w = 32, h = 64;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "vfx_feather", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = (y + 0.5f) / h, u = (x + 0.5f) / w;
            float quill = 0.5f + 0.07f * Mathf.Sin(v * Mathf.PI) - 0.03f; // slightly bent
            float side = u - quill;
            // Vane width along the feather: a stub of bare quill at the bottom, a rounded tip.
            float along = Mathf.Clamp01((v - 0.12f) / 0.86f);
            float width = Mathf.Pow(Mathf.Sin(along * Mathf.PI), 0.55f) * (side < 0f ? 0.3f : 0.4f) * (1f - 0.25f * along);
            float vane = Mathf.Clamp01((width - Mathf.Abs(side)) * 30f);
            // Barbs: faint diagonal grooves sweeping toward the tip.
            float barbs = 0.8f + 0.2f * Mathf.Sin((v * 2.6f - Mathf.Abs(side) * 1.4f) * 60f);
            float shaft = Mathf.Clamp01(1f - Mathf.Abs(side) * w / 1.2f) * (v < 0.97f ? 1f : 0f);
            float a = Mathf.Max(vane * barbs * 0.9f, shaft);
            float grey = Mathf.Lerp(0.6f, 1f, shaft);
            t.SetPixel(x, y, new Color(grey, grey, grey, a));
        }
        t.Apply();
        return t;
    }

    /// Fade in, hold, fade out over each particle's life (nothing pops on or off).
    static void SoftFade(ParticleSystem ps, float fadeIn)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0.75f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    /// Floor-theme ambience around the player: drifting dust, rising embers or falling snow.
    public static ParticleSystem Ambient(string kind, Transform follow)
    {
        bool add = kind == "embers";
        var ps = Make("ambient_" + kind, follow.position, add ? Additive : AlphaBlend, 5f);
        ps.gameObject.AddComponent<Follow>().target = follow;
        var m = ps.main;
        m.loop = true;
        m.maxParticles = 600;
        var em = ps.emission;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(30f, kind == "snow" ? 1f : 6f, 30f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = kind == "snow" ? 0.4f : 0.25f;
        noise.frequency = 0.3f;
        switch (kind)
        {
            case "embers":
                sh.position = new Vector3(0f, 0.5f, 0f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                m.startColor = Grad(new Color(1f, 0.6f, 0.2f), new Color(1f, 0.35f, 0.1f));
                m.gravityModifier = -0.06f;
                em.rateOverTime = 40f;
                Fade(ps, new Color(1f, 0.7f, 0.3f), new Color(1f, 0.25f, 0.05f));
                break;
            case "snow":
                sh.position = new Vector3(0f, 7f, 0f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
                m.startSpeed = 0f;
                m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                m.startColor = new Color(0.9f, 0.95f, 1f, 0.85f);
                m.gravityModifier = 0.03f;
                em.rateOverTime = 70f;
                Fade(ps, Color.white, new Color(0.8f, 0.9f, 1f));
                break;
            default: // dust motes
                sh.position = new Vector3(0f, 2.5f, 0f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
                m.startColor = new Color(1f, 0.95f, 0.85f, 0.35f);
                em.rateOverTime = 25f;
                Fade(ps, new Color(1f, 0.95f, 0.85f), new Color(1f, 0.9f, 0.8f));
                break;
        }
        ps.Play();
        return ps;
    }

    /// Stop an attached emitter and let its particles finish where they are (linger: their longest life).
    public static void Detach(ParticleSystem ps, float linger = 1.5f)
    {
        if (!ps) return;
        ps.transform.SetParent(null, true);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Object.Destroy(ps.gameObject, linger);
    }
}
