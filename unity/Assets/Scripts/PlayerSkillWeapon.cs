using System.Collections;
using UnityEngine;

/// Class skill weapons (Weapons.SkillWeapons), summoned by PlayerSkill for a few seconds:
/// - 誓约圣剑 (knight): long reach, +20%, with the oath shield on the left arm blocking three hits;
/// - 鸦羽刃 (assassin): each cut blinks to the enemy in front of the crosshair, ×2;
/// - 焚星杖 (mage): every cast drops a meteor on the crosshair;
/// - 晨曦权杖 (priest): a golden beam; each second of it on enemies restores one armour.
public partial class PlayerController
{
    static readonly Color RavenViolet = new Color(0.6f, 0.35f, 1f), OathGold = new Color(1f, 0.84f, 0.42f);

    /// Put a skill weapon in the current slot for `seconds`; the weapon it replaces comes back after.
    public void GrantSkillWeapon(string id, float seconds)
    {
        var w = Weapons.Get(id);
        if (w == null || dead) return;
        if (!HoldingSkillWeapon) skillWeaponSaved = weapons[slot];
        weapons[slot] = w.Clone();
        skillWeaponLeft = skillWeaponTotal = seconds;
        guardCharges = id == "oath_sword" ? GuardMax : 0;
        ambushUsed = false;
        holyT = 0f;
        charge = 0f;
        fireCd = 0f;
        EquipVisual();
        var c = SkillColor(w);
        Vfx.Sparks(muzzle.position, c, 18, 4f);
        Vfx.Puff(transform.position + Vector3.up, new Color(c.r, c.g, c.b, 0.5f), 0.9f, 8);
        Sfx.Play(w.melee ? "swap_melee" : "pickup_weapon", 0.9f, 0f);
        Hud.I?.Show($"{w.name}（{w.type}）", 1.2f);
    }

    /// The skill weapon fades and the replaced weapon returns (also when a run or floor resets).
    public void EndSkillWeapon()
    {
        if (skillWeaponSaved == null && !(Weapon != null && Weapon.IsSkillWeapon)) return;
        var c = Weapon != null && Weapon.IsSkillWeapon ? SkillColor(Weapon) : Color.white;
        skillWeaponLeft = 0f;
        guardCharges = 0;
        if (skillWeaponSaved != null) weapons[slot] = skillWeaponSaved;
        skillWeaponSaved = null;
        if (beam) beam.enabled = false;
        if (beamGlow) beamGlow.enabled = beamLight.enabled = false;
        if (muzzle) Vfx.Sparks(muzzle.position, c, 12, 3f);
        EquipVisual();
    }

    /// What a floor change keeps in a slot: the real weapon, not a summoned one.
    public WeaponDef KeptWeapon(int i) => HoldingSkillWeapon && i == slot ? skillWeaponSaved : weapons[i];

    static Color SkillColor(WeaponDef w) => w.owner switch
    {
        "knight" => OathGold,
        "assassin" => RavenViolet,
        "mage" => new Color(1f, 0.5f, 0.2f),
        _ => new Color(1f, 0.9f, 0.55f),
    };

    // ---------- 鸦羽刃: blink and cut ----------

    /// The nearest enemy ahead of the crosshair (within 7.5 m, 35°): dash to it, then cut.
    bool RavenBlink(WeaponDef w)
    {
        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Enemy best = null;
        float bestD = 7.5f;
        foreach (var e in Enemy.Live)
        {
            if (!e || e.dead) continue;
            Vector3 to = e.transform.position - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d < 2.2f || d > bestD || Vector3.Angle(fwd, to) > 35f) continue;
            best = e;
            bestD = d;
        }
        if (!best) return false;
        StartCoroutine(Blink(w, best));
        return true;
    }

    IEnumerator Blink(WeaponDef w, Enemy e)
    {
        Vector3 to = e.transform.position - transform.position;
        to.y = 0f;
        dashDir = to.normalized;
        dashSpeed = 30f;
        dashT = Mathf.Clamp((to.magnitude - 1.3f) / dashSpeed, 0.04f, 0.2f);
        dashInvuln = dashT + 0.08f;
        if (body) Afterimage.Spawn(body.transform, RavenViolet);
        Vfx.Puff(transform.position + Vector3.up, new Color(0.25f, 0.15f, 0.35f, 0.6f), 0.6f, 6);
        Sfx.Play("swap_melee", 0.5f, 0.1f, 1.3f);
        yield return new WaitForSeconds(dashT);
        if (!dead && Weapon != null && Weapon.id == w.id) Melee(Weapon);
    }

    // ---------- 焚星杖: a meteor on the crosshair ----------

    IEnumerator StaffMeteor(WeaponDef w)
    {
        Vector3 at = AimPoint();
        if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var ground, 8f, Layers.WorldMask, QueryTriggerInteraction.Ignore)) at = ground.point;
        var ring = GroundCircle(at, w.explode, new Color(1f, 0.45f, 0.1f));
        Vfx.Sparks(muzzle.position, new Color(1f, 0.55f, 0.2f), 8, 3f);
        Sfx.Play("buff", 0.35f, 0.1f, 0.8f);
        yield return new WaitForSeconds(0.3f);
        var from = at + new Vector3(Random.Range(-1.5f, 1.5f), 12f, Random.Range(-1.5f, 1.5f));
        int dmg = RollDamage(w.damage, out bool crit);
        var m = Bullet.Fire(from, (at - from).normalized * 34f, true, w.radius, w.mat, 3f, dmg, w.explode, crit);
        m.burn = 2f;
        yield return new WaitForSeconds(0.4f);
        if (ring) Destroy(ring.gameObject);
    }

    /// A glowing circle on the floor (where a meteor will land).
    static LineRenderer GroundCircle(Vector3 centre, float radius, Color c)
    {
        var lr = new GameObject("meteor_mark").AddComponent<LineRenderer>();
        lr.transform.position = centre;
        lr.loop = true;
        lr.useWorldSpace = false;
        lr.positionCount = 40;
        lr.sharedMaterial = Palette.Trail("shockwave");
        lr.startColor = lr.endColor = c * 2.5f;
        lr.widthMultiplier = 0.12f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 40; i++)
        {
            float a = i * Mathf.PI * 2f / 40f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.08f, Mathf.Sin(a) * radius));
        }
        return lr;
    }

    // ---------- 晨曦权杖: light that shields you ----------

    void HolyLight(float dt)
    {
        holyT += dt;
        if (holyT < 1f) return;
        holyT -= 1f;
        if (armor >= maxArmor) return;
        armor++;
        Vfx.Puff(transform.position + Vector3.up, new Color(1f, 0.9f, 0.5f, 0.6f), 0.5f, 6);
        Sfx.Play("pickup", 0.35f, 0.05f, 1.3f);
    }

    // ---------- 誓约圣盾: three blocks ----------

    void GuardBlock()
    {
        guardCharges--;
        invuln = 0.35f; // one blow, one block (a shotgun's pellets don't strip it at once)
        Vector3 at = offhandVisual ? offhandVisual.transform.position : transform.position + Vector3.up;
        Vfx.Sparks(at, OathGold, 14, 4.5f);
        Vfx.Ring(transform.position, 1.2f, OathGold);
        Sfx.Play("clank", 0.7f, 0.1f);
        Hud.I?.AddText(at + Vector3.up * 0.5f, guardCharges > 0 ? $"格挡 {guardCharges}/{GuardMax}" : "圣盾碎了", OathGold);
        if (guardCharges > 0) return;
        // Spent: the shield breaks apart; the sword stays until its time is up.
        Vfx.Sparks(at, new Color(0.5f, 0.75f, 1f), 24, 6f);
        Sfx.Play("shatter", 0.6f, 0.05f);
        if (offhandVisual) Destroy(offhandVisual);
    }
}
