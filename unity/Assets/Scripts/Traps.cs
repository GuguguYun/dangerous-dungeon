using System.Collections.Generic;
using UnityEngine;

/// Room hazards. They hurt enemies as much as the player, so luring monsters into them pays off.
/// Fire pits cycle idle -> warning glow -> fire column; spike plates fire 0.4 s after something
/// steps on them (jump to dodge); gas canisters burst into a poison cloud; lava cracks burn feet on
/// floor 2; ice patches on floor 3 make you slide.
public abstract class Trap : MonoBehaviour
{
    public static readonly List<Trap> Live = new List<Trap>();
    public float radius = 1f;

    protected virtual void OnEnable() => Live.Add(this);
    protected virtual void OnDisable() => Live.Remove(this);

    protected bool Inside(Vector3 p, float r) =>
        new Vector2(p.x - transform.position.x, p.z - transform.position.z).magnitude < r;

    protected static IEnumerable<Enemy> EnemiesNear(Vector3 at, float r)
    {
        foreach (var c in Physics.OverlapSphere(at, r, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (e && !e.dead) yield return e;
        }
    }
}

public class FirePit : Trap
{
    public enum Phase { Idle, Warning, Burning }
    public Phase phase;
    float t, tick;
    ParticleSystem fire;
    Light glow;
    Renderer embers;
    MaterialPropertyBlock mpb;

    public static FirePit Create(Vector3 at, Transform parent, float offset)
    {
        var go = Kit.Spawn("trap_grate", at, Quaternion.identity, parent, false);
        var f = go.AddComponent<FirePit>();
        f.radius = 1.1f;
        f.t = -offset; // staggered so a room's pits don't all fire together
        f.glow = new GameObject("fire_light").AddComponent<Light>();
        f.glow.transform.SetParent(go.transform, false);
        f.glow.transform.localPosition = Vector3.up * 1f;
        f.glow.type = LightType.Point;
        f.glow.color = Props.FireColor;
        f.glow.range = 5f;
        f.embers = go.GetComponentInChildren<Renderer>();
        f.mpb = new MaterialPropertyBlock();
        return f;
    }

    void Update()
    {
        t += Time.deltaTime;
        switch (phase)
        {
            case Phase.Idle:
                glow.intensity = 0.6f;
                if (t > 1.8f) { phase = Phase.Warning; t = 0f; Sfx.Play("rocket", 0.2f, 0.2f); }
                break;
            case Phase.Warning:
                glow.intensity = 0.6f + 3f * t / 0.7f;
                if (Random.value < 0.3f) Vfx.Sparks(transform.position + Vector3.up * 0.1f, Props.FireColor, 2, 2f);
                if (t > 0.7f)
                {
                    phase = Phase.Burning;
                    t = 0f;
                    tick = 0f;
                    fire = Vfx.Trail(transform, "pillar");
                    Sfx.Play("explode", 0.35f, 0.2f);
                }
                break;
            case Phase.Burning:
                glow.intensity = 6f + Mathf.PingPong(Time.time * 20f, 2f);
                tick -= Time.deltaTime;
                if (tick <= 0f)
                {
                    tick = 0.5f;
                    var p = PlayerController.Current;
                    if (p && !p.dead && Inside(p.transform.position, radius) && p.FeetHeight < 1.7f) p.TakeShockwave(1);
                    foreach (var e in EnemiesNear(transform.position + Vector3.up, radius))
                    {
                        e.TakeDamage(3, Vector3.up, false, 1f);
                        e.ApplyBurn(2f);
                    }
                }
                if (t > 1.3f) { phase = Phase.Idle; t = 0f; Vfx.Detach(fire); fire = null; }
                break;
        }
        float heat = phase == Phase.Idle ? 0.6f : phase == Phase.Warning ? 1f + 2f * t : 3f;
        mpb.SetColor("_EmissionColor", Props.FireColor * heat);
        embers.SetPropertyBlock(mpb);
    }
}

public class SpikeTrap : Trap
{
    public bool raised;
    public int fired;
    Transform spikes;
    float armT = -1f, upT, cooldown;
    readonly HashSet<Object> hitThisTime = new HashSet<Object>();

    public static SpikeTrap Create(Vector3 at, Transform parent)
    {
        var go = Kit.Spawn("spike_plate", at, Quaternion.identity, parent, false);
        var s = go.AddComponent<SpikeTrap>();
        s.radius = 0.95f;
        s.spikes = Kit.Spawn("spikes", at + Vector3.down * 0.62f, Quaternion.identity, go.transform, false).transform;
        return s;
    }

    void Update()
    {
        cooldown -= Time.deltaTime;
        var p = PlayerController.Current;
        bool someone = (p && !p.dead && Inside(p.transform.position, radius) && p.Grounded);
        foreach (var e in EnemiesNear(transform.position + Vector3.up * 0.5f, radius)) { someone = true; break; }
        if (armT < 0f && upT <= 0f && cooldown <= 0f && someone)
        {
            armT = 0.4f;
            Sfx.Play("hit", 0.4f, 0.1f);
        }
        if (armT >= 0f)
        {
            armT -= Time.deltaTime;
            if (armT < 0f)
            {
                upT = 0.8f;
                raised = true;
                fired++;
                hitThisTime.Clear();
                Sfx.Play("clank", 0.7f, 0.1f, 1.3f);
            }
        }
        if (upT > 0f)
        {
            upT -= Time.deltaTime;
            // Only things on the plate when the spikes come up get hit; a jump clears them.
            if (p && !p.dead && !p.SpikeProof && Inside(p.transform.position, radius) && p.FeetHeight < 0.5f && hitThisTime.Add(p)) p.TakeDamage(1);
            foreach (var e in EnemiesNear(transform.position + Vector3.up * 0.5f, radius))
                if (hitThisTime.Add(e)) e.TakeDamage(4, Vector3.up, false, 2f);
            if (upT <= 0f) { raised = false; cooldown = 1.2f; }
        }
        float target = raised ? 0f : -0.62f;
        var lp = spikes.localPosition;
        spikes.localPosition = new Vector3(lp.x, Mathf.MoveTowards(lp.y, target, Time.deltaTime * (raised ? 12f : 2f)), lp.z);
    }
}

public class PoisonCloud : Trap
{
    float life = 5f, tick;
    Light light;

    public static PoisonCloud Create(Vector3 at)
    {
        var go = new GameObject("poison_cloud");
        go.transform.position = new Vector3(at.x, 0f, at.z);
        var c = go.AddComponent<PoisonCloud>();
        c.radius = 3f;
        var fx = Vfx.Trail(go.transform, "gas");
        fx.transform.localPosition = Vector3.up * 0.6f;
        var bubbles = Vfx.Trail(go.transform, "bubbles");
        bubbles.transform.localPosition = Vector3.up * 0.15f;
        // A sickly glow on the floor marks how far the cloud reaches.
        var glow = new GameObject("gas_light").AddComponent<Light>();
        glow.transform.SetParent(go.transform, false);
        glow.transform.localPosition = Vector3.up * 1.2f;
        glow.type = LightType.Point;
        glow.color = new Color(0.45f, 1f, 0.25f);
        glow.range = 4.5f;
        glow.intensity = 2.5f;
        c.light = glow;
        Sfx.Play("explode", 0.4f, 0.3f);
        return c;
    }

    void Update()
    {
        life -= Time.deltaTime;
        tick -= Time.deltaTime;
        light.intensity = 2.5f * Mathf.Clamp01(life);
        if (tick <= 0f)
        {
            tick = 0.5f;
            var p = PlayerController.Current;
            if (p && !p.dead && Inside(p.transform.position, radius)) p.Poison(3f);
            foreach (var e in EnemiesNear(transform.position + Vector3.up, radius)) e.TakeDamage(2, Vector3.zero, false, 0f);
        }
        if (life <= 0f)
        {
            foreach (var ps in GetComponentsInChildren<ParticleSystem>()) Vfx.Detach(ps);
            Destroy(gameObject);
        }
    }
}

public class LavaCrack : Trap
{
    float tick;

    public static LavaCrack Create(Vector3 at, float yaw, Transform parent)
    {
        var go = new GameObject("lava_crack");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(at.x, 0.015f, at.z), Quaternion.Euler(0f, yaw, 0f));
        var l = go.AddComponent<LavaCrack>();
        l.radius = 1.3f;
        // A glowing zig-zag fissure plus a heat glow.
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 7;
        for (int i = 0; i < 7; i++)
            lr.SetPosition(i, new Vector3((i % 2 == 0 ? -0.18f : 0.18f) * Random.Range(0.5f, 1.4f), 0f, -1.5f + i * 0.5f));
        lr.widthMultiplier = 0.32f;
        lr.sharedMaterial = Palette.Trail("bullet_fire");
        lr.startColor = lr.endColor = new Color(1f, 0.45f, 0.1f) * 3f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var glow = new GameObject("lava_light").AddComponent<Light>();
        glow.transform.SetParent(go.transform, false);
        glow.transform.localPosition = Vector3.up * 0.5f;
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.4f, 0.1f);
        glow.range = 4f;
        glow.intensity = 3f;
        glow.gameObject.AddComponent<Flicker>();
        var embers = Vfx.Trail(go.transform, "burn");
        embers.transform.localPosition = Vector3.zero;
        return l;
    }

    bool OnCrack(Vector3 p)
    {
        var local = transform.InverseTransformPoint(p);
        return Mathf.Abs(local.x) < 0.45f && Mathf.Abs(local.z) < 1.6f;
    }

    void Update()
    {
        tick -= Time.deltaTime;
        if (tick > 0f) return;
        tick = 0.6f;
        var p = PlayerController.Current;
        if (p && !p.dead && OnCrack(p.transform.position) && p.FeetHeight < 0.3f) p.TakeShockwave(1);
        foreach (var e in EnemiesNear(transform.position, 1.6f))
            if (OnCrack(e.transform.position)) e.TakeDamage(2, Vector3.up, false, 0f);
    }
}

public class IcePatch : Trap
{
    public static IcePatch Create(Vector3 at, Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(go.GetComponent<Collider>());
        go.name = "ice_patch";
        go.transform.SetParent(parent, false);
        // 1.5 cm above the floor: never in the floor's plane, so no z-fighting.
        go.transform.SetPositionAndRotation(new Vector3(at.x, 0.015f, at.z), Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        go.transform.localScale = new Vector3(4.5f, 3.5f, 1f);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Palette.Get("ice");
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var ice = go.AddComponent<IcePatch>();
        ice.radius = 2f;
        return ice;
    }

    void Update()
    {
        var p = PlayerController.Current;
        if (p && Inside(p.transform.position, radius) && p.Grounded) p.onIce = 0.1f;
    }
}
