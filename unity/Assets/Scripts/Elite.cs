using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum EliteKind { None, Shielded, Frenzied, Splitting, Volatile }

/// Elite minions: bigger, tougher, with one affix and a name tag. Shielded (a bubble that soaks
/// damage and comes back if left alone), Frenzied (faster, shoots more often), Splitting (dies into
/// two small copies), Volatile (blows up a moment after dying: get away). They drop more gold.
public partial class Enemy
{
    public static readonly List<Enemy> Elites = new List<Enemy>();
    public static bool ElitesEnabled = true; // the self-test turns random elites off to stay deterministic

    public EliteKind elite;
    public int shield, maxShield;
    public float fireMul = 1f;
    public bool mini;
    public System.Action<EnemyKind, Vector3> Split; // the room spawns the halves
    float shieldIdle;
    GameObject shieldFx;
    static Material shieldMat;

    public static float EliteChance(int floor) => 0.06f + 0.06f * floor; // 12%, 18%, 24%

    public string EliteName => elite switch
    {
        EliteKind.Shielded => "精英 · 护盾",
        EliteKind.Frenzied => "精英 · 狂暴",
        EliteKind.Splitting => "精英 · 分裂",
        EliteKind.Volatile => "精英 · 自爆",
        _ => "",
    };

    public Color EliteColor => elite switch
    {
        EliteKind.Shielded => new Color(0.4f, 0.8f, 1f),
        EliteKind.Frenzied => new Color(1f, 0.3f, 0.25f),
        EliteKind.Splitting => new Color(0.75f, 0.45f, 1f),
        EliteKind.Volatile => new Color(1f, 0.6f, 0.15f),
        _ => Color.white,
    };

    public void MakeElite(EliteKind k)
    {
        if (k == EliteKind.None || kind == EnemyKind.Boss) return;
        elite = k;
        maxHp = hp = Mathf.CeilToInt(maxHp * 2.2f);
        Grow(1.22f);
        switch (k)
        {
            case EliteKind.Shielded:
                maxShield = shield = Mathf.CeilToInt(maxHp * 0.5f);
                MakeShieldFx();
                break;
            case EliteKind.Frenzied:
                speed *= 1.45f;
                fireMul = 0.6f;
                break;
        }
        // A coloured aura so elites stand out in a crowd.
        var aura = new GameObject("elite_light").AddComponent<Light>();
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.6f;
        aura.type = LightType.Point;
        aura.color = EliteColor;
        aura.range = 3.5f;
        aura.intensity = 3f;
        // And a steady ring on the floor in the affix colour.
        var ring = new GameObject("elite_ring").AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.loop = true;
        ring.useWorldSpace = false;
        ring.positionCount = 40;
        ring.widthMultiplier = 0.08f;
        ring.sharedMaterial = Palette.Trail("shockwave");
        ring.startColor = ring.endColor = EliteColor * 2.5f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        float rr = cc.radius + 0.45f;
        for (int i = 0; i < 40; i++)
        {
            float a = i * Mathf.PI * 2f / 40f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * rr, 0.06f, Mathf.Sin(a) * rr));
        }
        Elites.Add(this);
    }

    /// The small halves of a splitting elite.
    public void MakeMini()
    {
        mini = true;
        maxHp = hp = Mathf.Max(2, Mathf.CeilToInt(maxHp * 0.4f));
        speed *= 1.15f;
        Grow(0.7f);
    }

    void Grow(float f)
    {
        model.localScale *= f;
        cc.height *= f;
        cc.radius *= f;
        cc.center *= f;
    }

    void MakeShieldFx()
    {
        if (!shieldMat)
        {
            shieldMat = new Material(Vfx.Additive);
            var c = new Color(0.3f, 0.7f, 1.4f, 0.25f);
            if (shieldMat.HasProperty("_BaseColor")) shieldMat.SetColor("_BaseColor", c);
            shieldMat.color = c;
        }
        shieldFx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(shieldFx.GetComponent<Collider>());
        shieldFx.name = "elite_shield";
        shieldFx.transform.SetParent(transform, false);
        shieldFx.transform.localPosition = cc.center;
        shieldFx.transform.localScale = Vector3.one * cc.height * 1.15f;
        var r = shieldFx.GetComponent<Renderer>();
        r.sharedMaterial = shieldMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// Shield first; returns what is left for the health bar.
    int AbsorbWithShield(int amount)
    {
        if (elite != EliteKind.Shielded) return amount;
        shieldIdle = 0f;
        if (shield <= 0) return amount;
        int soaked = Mathf.Min(shield, amount);
        shield -= soaked;
        Vfx.Sparks(AimPoint, new Color(0.5f, 0.85f, 1f), 6, 4f);
        if (shield <= 0)
        {
            Vfx.Shatter(AimPoint);
            Sfx.Play("hit", 0.7f, 0.2f);
        }
        return amount - soaked;
    }

    void EliteTick(float dt)
    {
        if (elite != EliteKind.Shielded) return;
        shieldIdle += dt;
        // Left alone for 4 s, the bubble regrows.
        if (shieldIdle > 4f && shield < maxShield) shield = Mathf.Min(maxShield, shield + Mathf.CeilToInt(maxShield * 0.25f * dt + 0.01f));
        if (shieldFx)
        {
            shieldFx.SetActive(shield > 0);
            float k = maxShield > 0 ? shield / (float)maxShield : 0f;
            shieldFx.transform.localScale = Vector3.one * cc.height * (1.0f + 0.15f * k + Mathf.Sin(Time.time * 8f) * 0.02f);
        }
    }

    void EliteDeath()
    {
        Elites.Remove(this);
        if (shieldFx) Destroy(shieldFx);
        if (elite == EliteKind.None) return;
        Pickup.SpawnGold(transform.position, 6);
        if (Random.value < 0.35f) Pickup.Spawn(PickupKind.Health, transform.position + Vector3.left, 2);
        if (elite == EliteKind.Splitting && Split != null)
        {
            Vector3 side = Vector3.Cross(Vector3.up, transform.forward).normalized;
            Split(kind, transform.position + side * 1.2f);
            Split(kind, transform.position - side * 1.2f);
        }
        if (elite == EliteKind.Volatile) DelayedBlast.Arm(transform.position, 2.8f, 0.9f);
    }
}

/// A volatile elite's last word: a red ring pulses for a moment, then it explodes (hurts everyone).
public class DelayedBlast : MonoBehaviour
{
    public static DelayedBlast Arm(Vector3 at, float radius, float delay)
    {
        var go = new GameObject("delayed_blast");
        go.transform.position = new Vector3(at.x, 0f, at.z);
        var b = go.AddComponent<DelayedBlast>();
        b.StartCoroutine(b.Run(radius, delay));
        return b;
    }

    IEnumerator Run(float radius, float delay)
    {
        var ring = new GameObject("blast_ring").AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.loop = true;
        ring.useWorldSpace = false;
        ring.positionCount = 48;
        ring.sharedMaterial = Palette.Trail("shockwave");
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 48; i++)
        {
            float a = i * Mathf.PI * 2f / 48f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.08f, Mathf.Sin(a) * radius));
        }
        var glow = new GameObject("blast_light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = Vector3.up;
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.4f, 0.15f);
        glow.range = radius * 2f;
        Sfx.Play("rocket", 0.5f, 0.1f);
        for (float t = 0f; t < delay; t += Time.deltaTime)
        {
            float pulse = Mathf.PingPong(t * 10f, 1f);
            ring.startColor = ring.endColor = new Color(1f, 0.3f + 0.4f * pulse, 0.1f) * 3f;
            ring.widthMultiplier = 0.1f + 0.12f * pulse;
            glow.intensity = 2f + 6f * t / delay;
            yield return null;
        }
        Explosion.At(transform.position + Vector3.up * 0.5f, radius, 6, true);
        Destroy(gameObject);
    }
}
