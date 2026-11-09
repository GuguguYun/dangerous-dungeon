using UnityEngine;

/// Elemental reactions: two effects meeting make something bigger, and the reaction's name pops up.
/// 爆燃 fire meets a poison cloud: the whole cloud explodes and sets everything in it alight.
/// 碎冰 a heavy hit (6+) on a frozen (slowed) enemy: double damage, the ice shatters.
/// 超载 lightning strikes a burning enemy: a small blast that hits its neighbours too.
public static class Reactions
{
    public static int Combustions, Shatters, Overloads; // self-test counters

    static void Name(Vector3 at, string text, Color c)
    {
        Hud.I?.AddText(at + Vector3.up * 1.8f, text, c);
        Tips.Show("reaction", "元素反应！火遇毒雾会爆燃、重击冰冻的敌人会碎冰、雷击燃烧的敌人会超载");
    }

    /// Called from Enemy.TakeDamage before the hit lands; returns the (possibly doubled) damage.
    public static int Shatter(Enemy e, int amount)
    {
        if (!e.Slowed || amount < Balance.HeavyHit) return amount;
        Shatters++;
        e.ApplySlow(1f, 0f); // the ice is gone
        e.ClearSlow();
        Vfx.Shatter(e.AimPoint);
        Vfx.Sparks(e.AimPoint, new Color(0.7f, 0.95f, 1f), 14, 6f);
        Sfx.Play("shatter", 0.8f, 0.1f);
        Name(e.transform.position, "碎冰！", new Color(0.6f, 0.9f, 1f));
        return amount * 2;
    }

    /// Lightning about to hit an enemy (thunder staff chains).
    public static void Shock(Enemy e)
    {
        if (!e.Burning) return;
        Overloads++;
        e.ApplyBurn(0f);
        e.ClearBurn();
        Vector3 at = e.AimPoint;
        foreach (var c in Physics.OverlapSphere(at, 2.5f, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var other = c.GetComponentInParent<Enemy>();
            if (other && !other.dead) other.TakeDamage(Balance.Overload, (other.transform.position - at).normalized, false, 5f);
        }
        Vfx.Explosion(at, 1.6f);
        Fx.Lightning(at + Vector3.up * 2f, at);
        Name(e.transform.position, "超载！", new Color(1f, 0.75f, 0.3f));
    }

    /// Something burning reached a poison cloud.
    public static void Combust(PoisonCloud cloud)
    {
        if (!cloud || cloud.ignited) return;
        cloud.ignited = true;
        Combustions++;
        Vector3 at = cloud.transform.position + Vector3.up * 0.6f;
        Explosion.At(at, cloud.radius + 0.5f, Balance.GasBlast, true, false, 3f);
        Vfx.Explosion(at + Vector3.up * 0.4f, cloud.radius);
        Vfx.Flash(at, new Color(1f, 0.6f, 0.2f), 10f, 9f, 0.25f);
        Name(cloud.transform.position, "爆燃！", new Color(1f, 0.55f, 0.2f));
        Object.Destroy(cloud.gameObject);
    }

    /// Fire sources near any poison cloud set it off: flame shots, burning enemies, an erupting pit, a
    /// blast. The same fire catches moss (青苔地宫).
    public static void CheckClouds(Vector3 fireAt, float reach)
    {
        Moss.Fire(fireAt, reach);
        foreach (var t in Trap.Live.ToArray())
            if (t is PoisonCloud cloud && !cloud.ignited)
            {
                Vector3 d = fireAt - cloud.transform.position;
                d.y = 0f;
                if (d.magnitude < cloud.radius + reach) Combust(cloud);
            }
    }
}
