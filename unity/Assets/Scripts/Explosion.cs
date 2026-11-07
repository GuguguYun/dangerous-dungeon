using System.Collections.Generic;
using UnityEngine;

/// One place for blast logic: damage enemies (and optionally the player), break and chain props,
/// throw every rigidbody in range (props, debris, ragdolls, pickups), then the effects.
public static class Explosion
{
    public static void At(Vector3 at, float radius, int damage, bool hurtsPlayer, bool crit = false, float burn = 0f)
    {
        var seen = new HashSet<Enemy>();
        foreach (var c in Physics.OverlapSphere(at, radius, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (!e || !seen.Add(e)) continue;
            e.TakeDamage(damage, (e.transform.position - at).normalized, crit, 6f);
            if (burn > 0f) e.ApplyBurn(burn);
        }
        if (hurtsPlayer)
        {
            var p = PlayerController.Current;
            if (p && Vector3.Distance(p.transform.position + Vector3.up * 0.7f, at) < radius * 0.8f) p.TakeDamage(2);
        }
        foreach (var prop in PhysicsProp.Live.ToArray())
        {
            if (!prop) continue;
            float d = Vector3.Distance(prop.transform.position, at);
            if (d < radius) prop.Hit(damage, at, prop.transform.position - at);
        }
        foreach (var c in Physics.OverlapSphere(at, radius * 1.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            var rb = c.attachedRigidbody;
            // Ragdoll limbs and debris are light: give them less so bodies tumble rather than fly off.
            float push = c.gameObject.layer == Layers.Debris ? 3.5f : 6f;
            if (rb && !rb.isKinematic) rb.AddExplosionForce(push, at, radius * 1.6f, 0.6f, ForceMode.VelocityChange);
        }
        Reactions.CheckClouds(at, radius * 0.6f); // a blast next to poison gas sets it off
        CameraShake.Add(0.3f);
        Sfx.Play("explode", 0.8f);
        Vfx.Explosion(at, radius);
    }
}
