using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Melee timing and look. A blow lands when the blade (or fist) actually comes through in the
/// animation — the moment the hand moves fastest in the clip (CharacterAnim.PeakTime) — not when the
/// button is pressed; charged attacks spread their effect over the whole move (the whirlwind hits as
/// it turns, the lunge skewers along its path, the hammer lands with the hammer). The swing shows as a
/// pale smear on the real blade (BladeSmear) and sparks where it connects, not a separate arc of light.
public partial class PlayerController
{
    BladeSmear smear;
    float spinT; // > 0 while the charged whirlwind turns the body

    /// How long one swing takes: the slash clip is played to fit the weapon's rhythm.
    float SwingTime(WeaponDef w) => Mathf.Max(0.25f, w.interval / RunState.fireRateMul);

    /// Seconds from the start of a `clip` played over `swing` seconds to the moment it lands.
    float HitDelay(string clip, float swing)
    {
        if (!body) return 0f;
        float len = body.Length(clip);
        return len > 0f ? body.PeakTime(clip, "RightHand") / len * swing : 0f;
    }

    IEnumerator After(float seconds, System.Action act)
    {
        if (seconds > 0f) yield return new WaitForSeconds(seconds);
        if (!dead) act();
    }

    void Later(float seconds, System.Action act)
    {
        if (seconds < 0.02f) act();
        else StartCoroutine(After(seconds, act));
    }

    static Color SmearColor(WeaponDef w) => w.id switch
    {
        "oath_sword" => new Color(1f, 0.88f, 0.55f, 0.6f),
        "raven_blade" => new Color(0.55f, 0.35f, 0.9f, 0.6f),
        "daggers" => new Color(0.78f, 0.62f, 1f, 0.5f),
        "hammer" => new Color(1f, 0.9f, 0.72f, 0.45f),
        "spear" => new Color(0.95f, 0.95f, 1f, 0.45f),
        _ => new Color(0.85f, 0.92f, 1f, 0.55f),
    };

    /// The smear rides on the held melee weapon, from a quarter of the way up to the tip.
    void AttachSmear(float tipZ)
    {
        if (smear) Destroy(smear.gameObject);
        smear = Weapon.melee ? BladeSmear.Attach(weaponPivot, Mathf.Max(0.12f, tipZ * 0.25f), tipZ) : null;
    }

    void Melee(WeaponDef w, bool heavy = false)
    {
        float swing = SwingTime(w);
        float atYaw = yaw;
        swingT = body ? 0f : 0.18f;
        if (body)
        {
            model.rotation = Quaternion.Euler(0f, atYaw, 0f);
            body.Trigger("slash", body.Length("slash") / swing, 0.2f);
        }
        if (smear) smear.Swing(swing, SmearColor(w));
        Sfx.Play("slash", 0.55f, 0.15f, 1.1f); // the swish now; the hit sounds when it lands
        Later(HitDelay("slash", swing), () => MeleeHit(w, heavy, atYaw));
    }

    /// The blow itself: everything in the arc in front takes it; a parrying blade cuts enemy shots.
    void MeleeHit(WeaponDef w, bool heavy, float atYaw)
    {
        Vector3 fwd = Quaternion.Euler(0f, atYaw, 0f) * Vector3.forward;
        Vector3 origin = transform.position + Vector3.up * 1.0f;
        var hit = new HashSet<Enemy>();
        int landed = 0;
        foreach (var c in Physics.OverlapSphere(origin, w.reach, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (!e || e.dead || !hit.Add(e)) continue;
            Vector3 to = e.transform.position - transform.position;
            to.y = 0f;
            if (Vector3.Angle(fwd, to) > w.arc / 2f && to.magnitude > 1f) continue;
            int dmg = Mathf.RoundToInt(RollDamage(w.damage, out bool crit) * RunState.meleeMul);
            e.TakeDamage(dmg, to.normalized, crit);
            Struck(e, w);
            landed++;
        }
        HitProps(origin, fwd, w.reach, w.arc, Mathf.RoundToInt(w.damage * RunState.meleeMul));
        if (RunState.meleeMul > 1f || w.id == "sword" || w.id == "oath_sword")
        {
            foreach (var b in Bullet.Live.ToArray())
            {
                if (b.HitsEnemies) continue;
                Vector3 to = b.transform.position - origin;
                if (to.magnitude <= w.reach + 0.5f && Vector3.Angle(fwd, new Vector3(to.x, 0f, to.z)) <= w.arc / 2f) b.Cancel();
            }
        }
        MeleeFx(w, origin, heavy);
        CameraShake.Add(landed > 0 ? 0.15f : 0.04f);
        // The thunder hammer's blow conducts into two more enemies nearby.
        if (w.id == "hammer" && landed > 0)
            foreach (var e in hit) if (e && !e.dead) { StartCoroutine(Conduct(w, e, 2, 6f, 0.5f)); break; }
    }

    /// Where a blade connects: a burst of sparks on the enemy.
    void Struck(Enemy e, WeaponDef w)
    {
        var c = SmearColor(w);
        Vfx.Sparks(e.AimPoint, new Color(c.r, c.g, c.b), 7, 4.5f);
    }

    // ---------- charged melee ----------

    /// Sword: the body turns a full circle; the blade hits everything around three times as it comes round.
    IEnumerator Whirlwind(WeaponDef w, int damage, float reach)
    {
        const float spin = 0.5f;
        spinT = spin;
        float yaw0 = yaw;
        if (body) body.Trigger("slash", body.Length("slash") / spin, spin);
        if (smear) smear.Swing(spin, SmearColor(w));
        int tick = 0;
        for (float t = 0f; t < spin && !dead; t += Time.deltaTime)
        {
            model.rotation = Quaternion.Euler(0f, yaw0 + 360f * t / spin, 0f);
            if (t >= spin * (tick + 1) / 3f)
            {
                tick++;
                Vector3 origin = transform.position + Vector3.up;
                foreach (var e in EnemiesAround(origin, reach))
                {
                    Vector3 away = e.transform.position - transform.position;
                    away.y = 0f;
                    e.TakeDamage(Mathf.RoundToInt(RollDamage(damage, out bool crit) * 0.45f * RunState.meleeMul), away.normalized, crit);
                    Struck(e, w);
                }
                foreach (var b in Bullet.Live.ToArray())
                    if (!b.HitsEnemies && Vector3.Distance(b.transform.position, origin) <= reach + 0.5f) b.Cancel();
                HitProps(origin, transform.forward, reach, 360f, Mathf.RoundToInt(damage * 0.45f * RunState.meleeMul));
                Sfx.Play("slash", 0.6f, 0.15f);
                CameraShake.Add(0.08f);
            }
            yield return null;
        }
        spinT = 0f;
    }

    /// Spear: the dash carries you forward and everything on the line is skewered as you pass.
    IEnumerator Lunge(WeaponDef w, Vector3 start, Vector3 fwd, int damage)
    {
        var struck = new HashSet<Enemy>();
        for (float t = 0f; t < 0.32f && !dead; t += Time.deltaTime)
        {
            Vector3 tipAt = transform.position + fwd * 2.2f;
            foreach (var c in Physics.OverlapCapsule(start + Vector3.up, tipAt + Vector3.up, 1.0f, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
            {
                var e = c.GetComponentInParent<Enemy>();
                if (!e || e.dead || !struck.Add(e)) continue;
                e.TakeDamage(Mathf.RoundToInt(RollDamage(damage, out bool crit) * RunState.meleeMul), fwd, crit, 6f);
                Struck(e, w);
                CameraShake.Add(0.1f);
            }
            yield return null;
        }
        HitProps(start + Vector3.up, fwd, Vector3.Distance(start, transform.position) + 2.2f, 30f, Mathf.RoundToInt(damage * RunState.meleeMul));
        MeleeFx(w, transform.position + Vector3.up, true);
    }

    /// Crates, barrels and the explosive kegs in the swing's arc take the blow too (they used to shrug
    /// melee off: only shots, blasts and the heavy mech's fist broke them).
    static void HitProps(Vector3 origin, Vector3 fwd, float reach, float arc, int damage)
    {
        foreach (var prop in PhysicsProp.Live.ToArray())
        {
            if (!prop) continue;
            Vector3 to = prop.transform.position - origin;
            to.y = 0f;
            if (to.magnitude > reach + 0.4f) continue;
            if (arc < 360f && Vector3.Angle(fwd, to) > arc / 2f && to.magnitude > 1f) continue;
            prop.Hit(Mathf.Max(1, damage), prop.transform.position + Vector3.up * 0.5f, to.sqrMagnitude > 0.01f ? to : fwd);
        }
    }

    static readonly Collider[] aroundBuf = new Collider[32];

    static List<Enemy> EnemiesAround(Vector3 at, float radius)
    {
        var list = new List<Enemy>();
        int n = Physics.OverlapSphereNonAlloc(at, radius, aroundBuf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var e = aroundBuf[i].GetComponentInParent<Enemy>();
            if (e && !e.dead && !list.Contains(e)) list.Add(e);
        }
        return list;
    }
}
