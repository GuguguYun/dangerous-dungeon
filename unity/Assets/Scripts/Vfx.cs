using UnityEngine;
using UnityEngine.Rendering;

/// Particle effects built in code: muzzle flashes, hit sparks, explosions, frost, fire, smoke,
/// lightning. Additive particles use an HDR-tinted material so bloom makes them glow.
/// BuildScript saves the materials under Resources/VFX so the URP particle shader is kept in builds.
public static class Vfx
{
    static Material add, alpha;
    static Mesh crystal;

    public static Material Additive => add ? add : add = Load("VFX/vfx_add", true);
    public static Material AlphaBlend => alpha ? alpha : alpha = Load("VFX/vfx_alpha", false);

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
        Flash(at, c, 2.5f, 4f, 0.06f);
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
        bool smoke = kind == "smoke" || kind == "dust";
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
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, burn ? 0.6f : 0.35f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, burn ? 1.2f : 0.6f);
                m.startSize = new ParticleSystem.MinMaxCurve(burn ? 0.2f : 0.25f, burn ? 0.45f : 0.55f);
                m.startColor = Grad(new Color(1f, 0.8f, 0.3f), new Color(1f, 0.4f, 0.1f));
                m.gravityModifier = burn ? -0.4f : 0f;
                if (burn) em.rateOverTime = 22f; else em.rateOverDistance = 9f;
                sh.radius = burn ? 0.35f : 0.1f;
                Fade(ps, new Color(1f, 0.85f, 0.4f), new Color(0.6f, 0.1f, 0.05f));
                if (burn) Shrink(ps, 0.2f);
                else
                {
                    // Projectile flames billow out as they travel, so the stream reads as a cone.
                    var grow = ps.sizeOverLifetime;
                    grow.enabled = true;
                    grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.2f));
                }
                break;
            case "pillar":
                // A column of fire shooting up out of a grate.
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                m.startColor = Grad(new Color(1f, 0.8f, 0.3f), new Color(1f, 0.4f, 0.1f));
                em.rateOverTime = 70f;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 8f;
                sh.radius = 0.6f;
                sh.rotation = new Vector3(-90f, 0f, 0f);
                Fade(ps, new Color(1f, 0.9f, 0.5f), new Color(0.7f, 0.15f, 0.05f));
                Shrink(ps, 0.3f);
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

    /// Stop an attached emitter and let its particles finish where they are.
    public static void Detach(ParticleSystem ps)
    {
        if (!ps) return;
        ps.transform.SetParent(null, true);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Object.Destroy(ps.gameObject, 1.5f);
    }
}
