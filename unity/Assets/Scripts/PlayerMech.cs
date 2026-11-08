using UnityEngine;

/// Piloting a mech: the pilot climbs in (hidden), the mech's durability soaks damage first, and the
/// controls switch to the mech's own kit. Light: rapid-fire cannon that overheats, plasma blade,
/// boost dash, jet jump, overdrive. Heavy: explosive cannon, hydraulic fist, shoulder ram, jump slam,
/// missile salvo. Destroyed mechs explode and throw the pilot out unhurt.
public partial class PlayerController
{
    public MechDef mechDef;
    public int mechHp;
    public float heat;            // light mech cannon, 0..1
    public bool overheated;
    public float mechSpecialCd, overdriveT;
    public ParkedMech nearbyMech;
    public int mechShots, mechSlams, mechSwings;
    public bool mechBlade;        // Q: the left button swings the melee arm instead of firing
    public int mechCombo;         // 0, 1, 2: the third swing of a chain is the finisher

    public bool InMech => mechDef != null;
    public Vector3 MechGunPoint => InMech ? MechMuzzle(mechDef.gunBone, 0.6f) : transform.position; // self-test
    public bool HeavyMech => mechDef != null && mechDef.heavy;
    public bool MechArmShown => mechArm != null && mechArm.Length > 0 && mechArm[0] && mechArm[0].activeSelf; // self-test

    CharacterAnim pilotBody;
    Renderer pilotRenderer;
    GameObject mechVisual;
    float savedSpeed, savedStamina, savedRadius, savedHeight;
    Vector3 savedCenter;
    int savedMaxStamina;
    float camScale = 1f, camScaleTarget = 1f;
    float mechFireCd, mechMeleeCd, heatIdle;
    bool ramming, slamPending;
    int barrel;
    float mechComboT, afterimageT;
    GameObject[] mechArm;
    Quaternion mechVisualBase;
    readonly System.Collections.Generic.HashSet<Enemy> rammed = new System.Collections.Generic.HashSet<Enemy>();

    /// Climb into a mech (a parked one, or straight from the run state on a new floor).
    public void Mount(MechDef d, int hp, ParkedMech parked = null)
    {
        if (dead || InMech || d == null) return;
        if (parked)
        {
            RunState.mechsPiloted++;
            yaw = parked.transform.eulerAngles.y;
            Teleport(new Vector3(parked.transform.position.x, transform.position.y, parked.transform.position.z));
            Destroy(parked.gameObject);
        }
        skill?.BreakStealth();
        charge = 0f;
        mechDef = d;
        mechHp = Mathf.Clamp(hp, 1, d.maxHp);
        poisoned = 0f;
        heat = 0f;
        overheated = false;
        mechSpecialCd = 0f;
        overdriveT = 0f;

        pilotBody = body;
        pilotRenderer = bodyRenderer;
        weaponPivot.gameObject.SetActive(false); // guns hang in front of the chest, not on the hidden body
        if (pilotBody) pilotBody.gameObject.SetActive(false);
        else foreach (var r in model.GetComponentsInChildren<Renderer>()) r.enabled = false;
        mechVisual = Mechs.Visual(d, model, Layers.Player, out var anim);
        body = anim;
        bodyRenderer = mechVisual.GetComponentInChildren<Renderer>();
        handsOnWeapon = false;
        mechVisualBase = mechVisual.transform.localRotation;
        mechArm = MechArm.Build(anim, d);
        SetMechBlade(false, true);
        mechCombo = 0;

        savedRadius = cc.radius;
        savedHeight = cc.height;
        savedCenter = cc.center;
        cc.radius = d.radius;
        cc.height = d.height;
        cc.center = Vector3.up * d.height / 2f;
        savedSpeed = Speed;
        Speed = d.speed;
        savedMaxStamina = maxStamina;
        savedStamina = stamina;
        maxStamina = d.boosts;
        stamina = d.boosts;
        camScaleTarget = d.camScale;

        Vfx.Ring(transform.position, 2f, d.color);
        Sfx.Play("clank", 0.8f, 0f, 0.8f);
        Sfx.Play("buff", 0.5f, 0f);
        Hud.I?.Show($"搭乘「{d.name}」", 1.2f);
        Tips.Show("mech_controls", $"机甲操作：左键{d.ranged}，右键{d.melee}；按 Q 切到近战模式，左键连斩三段、第三段{(d.heavy ? "砸地" : "突刺")}；Shift {d.dashName}，F {d.special}");
        Debug.Log("MECH mount " + d.id);
    }

    /// Climb out (E) and leave the mech parked, or get thrown out when it is destroyed.
    public void Dismount(bool destroyed)
    {
        if (!InMech) return;
        var d = mechDef;
        Vector3 at = transform.position;
        float facing = model.eulerAngles.y;
        if (mechVisual) Destroy(mechVisual);
        body = pilotBody;
        bodyRenderer = pilotRenderer;
        if (pilotBody) pilotBody.gameObject.SetActive(true);
        else foreach (var r in model.GetComponentsInChildren<Renderer>()) r.enabled = true;
        weaponPivot.gameObject.SetActive(true);
        mechDef = null;
        mechArm = null; // it hung on the mech's bones
        mechBlade = false;
        ramming = false;
        slamPending = false;

        cc.radius = savedRadius;
        cc.height = savedHeight;
        cc.center = savedCenter;
        Speed = savedSpeed;
        maxStamina = savedMaxStamina;
        stamina = Mathf.Min(savedStamina, maxStamina);
        camScaleTarget = 1f;
        EquipVisual();

        // Step out beside the mech, or behind it if a wall is there.
        Vector3 side = model.right * (d.radius + 0.9f);
        Vector3 back = -model.forward * (d.radius + 0.9f);
        Vector3 spot = at + side;
        if (Physics.CheckCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 1.3f, 0.4f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
            spot = at + back;
        if (destroyed)
        {
            Vfx.Explosion(at + Vector3.up, 2.5f);
            Mechs.Shock(at, 3f, 8, 8f);
            CameraShake.Add(0.4f);
            Sfx.Play("explode", 0.9f);
            invuln = 1.5f;
            Hud.I?.Show($"机甲「{d.name}」被摧毁！", 1.6f);
            Teleport(spot + Vector3.up * 0.1f);
            velocity = Vector3.up * 5f;
        }
        else
        {
            ParkedMech.Create(d, mechHp, at, facing);
            Teleport(spot + Vector3.up * 0.1f);
            Sfx.Play("clank", 0.6f, 0.1f);
        }
        Debug.Log($"MECH dismount destroyed={destroyed}");
    }

    /// The mech takes the hit; when it breaks the pilot is thrown out, unhurt.
    void MechDamage(int amount)
    {
        sinceHit = 0f;
        invuln = 0.25f;
        damageTaken += amount;
        RunState.damageTaken += amount;
        mechHp = Mathf.Max(0, mechHp - amount);
        CameraShake.Add(0.2f);
        Hud.I?.Hurt();
        Sfx.Play("clank", 0.6f, 0.15f);
        Vfx.Sparks(transform.position + Vector3.up * mechDef.height * 0.6f, new Color(1f, 0.8f, 0.4f), 8, 5f);
        if (mechHp <= 0) Dismount(true);
    }

    /// Damage a mech shrugs off entirely: poison (sealed cockpit); the heavy also ignores spikes.
    public bool SpikeProof => HeavyMech;

    void MechTick(float dt)
    {
        mechFireCd -= dt;
        mechMeleeCd -= dt;
        mechSpecialCd = Mathf.Max(0f, mechSpecialCd - dt);
        overdriveT -= dt;
        heatIdle += dt;
        if (heatIdle > 0.25f) heat = Mathf.Max(0f, heat - 0.6f * dt);
        if (overheated && heat < 0.35f) overheated = false;
        if (Time.time - mechComboT > 0.9f) mechCombo = 0;

        // Light mech boost: lean into the boost and leave fading ghosts behind.
        if (mechVisual && !HeavyMech)
        {
            Quaternion lean = Quaternion.identity;
            if (dashT > 0f)
            {
                Vector3 local = model.InverseTransformDirection(dashDir);
                lean = Quaternion.Euler(local.z * 16f, 0f, -local.x * 18f);
                afterimageT -= dt;
                if (afterimageT <= 0f && body)
                {
                    afterimageT = 0.05f;
                    Afterimage.Spawn(body.transform, mechDef.color * 0.8f);
                }
            }
            mechVisual.transform.localRotation = Quaternion.Slerp(mechVisual.transform.localRotation, mechVisualBase * lean, 1f - Mathf.Exp(-16f * dt));
        }

        // Heavy: the ram hits whatever it runs into; the jump ends in a slam.
        if (ramming)
        {
            if (dashT <= 0f) ramming = false;
            else
                foreach (var c in Physics.OverlapSphere(transform.position + Vector3.up, mechDef.radius + 0.8f, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
                {
                    var e = c.GetComponentInParent<Enemy>();
                    if (!e || e.dead || !rammed.Add(e)) continue;
                    e.TakeDamage(RollDamage(7, out _), dashDir, false, 8f);
                    e.Knock(dashDir * 14f);
                    CameraShake.Add(0.2f);
                    Sfx.Play("hit", 0.8f, 0.1f);
                }
        }
        if (slamPending && Grounded && velocity.y <= 0f)
        {
            slamPending = false;
            mechSlams++;
            Mechs.Shock(transform.position, 4.5f, RollDamage(10, out _), 12f);
            Vfx.Ring(transform.position, 4.5f, mechDef.color);
            Vfx.Puff(transform.position + Vector3.up * 0.3f, new Color(0.6f, 0.55f, 0.5f, 0.6f), 1.4f, 14);
            Vfx.Sparks(transform.position + Vector3.up * 0.2f, mechDef.color, 16, 6f);
            CameraShake.Add(0.35f);
            Sfx.Play("explode", 0.6f, 0.15f);
        }
    }

    /// Shift in a mech: the light one boosts (brief bullet immunity), the heavy one rams.
    public void MechDash(Vector3 dir)
    {
        if (!Dash(dir)) return;
        if (HeavyMech)
        {
            dashInvuln = 0f; // a ram is not a dodge
            ramming = true;
            rammed.Clear();
            dashT = 0.35f;
            if (body) body.Trigger("swing", 1.4f, 0.2f);
            Vfx.Puff(transform.position + Vector3.up * 0.8f - dir * 0.8f, new Color(0.7f, 0.8f, 1f, 0.5f), 0.8f, 8);
        }
        else
        {
            // A quick boost: farther and faster than a pilot's dodge, facing stays on the crosshair.
            dashT = 0.24f;
            dashSpeed = 21f;
            dashInvuln = dashT + 0.05f;
            afterimageT = 0f;
            Vector3 back = transform.position + Vector3.up * mechDef.height * 0.62f - dir * (mechDef.radius + 0.1f);
            Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.3f;
            Vfx.MuzzleFlash(back + side, -dir, mechDef.color);
            Vfx.MuzzleFlash(back - side, -dir, mechDef.color);
            Vfx.Puff(transform.position + Vector3.up * 0.2f - dir * 0.6f, new Color(0.7f, 0.8f, 1f, 0.45f), 0.9f, 10);
        }
        Sfx.Play("rocket", 0.4f, 0.2f);
    }

    /// Space in a mech: the light one jet-jumps, the heavy one hops and slams down.
    public void MechJump()
    {
        if (!Grounded || !Jump()) return;
        if (HeavyMech)
        {
            velocity.y = 6.5f;
            slamPending = true;
            if (body) body.Trigger("stomp", 1.2f, 0.3f);
        }
        else
        {
            velocity.y = 8.6f;
            Vfx.Puff(transform.position + Vector3.up * 0.3f, new Color(0.7f, 0.85f, 1f, 0.5f), 0.9f, 10);
        }
    }

    Vector3 MechMuzzle(string bone, float forward)
    {
        var t = body ? body.Find(bone) : null;
        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 basePos = t ? t.position : transform.position + Vector3.up * mechDef.height * 0.6f + model.right * mechDef.radius;
        return basePos + fwd * forward;
    }

    /// Left button: the mech's gun.
    void MechFire(bool trigger)
    {
        if (!trigger || mechFireCd > 0f) return;
        lastShot = Time.time;
        Vector3 aimAt = AimPoint();
        if (!HeavyMech)
        {
            if (overheated) return;
            bool od = overdriveT > 0f;
            mechFireCd = od ? 0.04f : 0.07f;
            barrel = 1 - barrel;
            Vector3 from = MechMuzzle(mechDef.gunBone, 0.6f) + model.right * (barrel == 0 ? -0.08f : 0.08f);
            Vector3 aim = (aimAt - from).normalized;
            var spread = Quaternion.Euler(Random.Range(-1.2f, 1.2f), Random.Range(-1.2f, 1.2f), 0f);
            int dmg = RollDamage(2, out bool crit);
            Bullet.Fire(from, Quaternion.LookRotation(aim) * spread * Vector3.forward * 60f, true, 0.08f, "bullet_player", 2f, dmg, 0f, crit);
            Vfx.MuzzleFlash(from, aim, Palette.ColorOf("bullet_player"));
            CameraShake.Add(0.03f);
            Sfx.Play("shoot", 0.4f, 0.15f);
            if (!od)
            {
                heat += 0.035f;
                heatIdle = 0f;
                if (heat >= 1f)
                {
                    heat = 1f;
                    overheated = true;
                    Vfx.Puff(from, new Color(0.9f, 0.9f, 0.9f, 0.5f), 0.7f, 10);
                    Hud.I?.Show("速射炮过热！", 0.8f);
                    Sfx.Play("rocket", 0.3f, 0.2f);
                }
            }
        }
        else
        {
            mechFireCd = 0.7f;
            Vector3 from = MechMuzzle(mechDef.gunBone, 0.7f);
            Vector3 aim = (aimAt - from).normalized;
            int dmg = RollDamage(9, out bool crit);
            Bullet.Fire(from, aim * 34f, true, 0.2f, "bullet_rocket", 3f, dmg, 2.5f, crit);
            Vfx.MuzzleFlash(from, aim, Palette.ColorOf("bullet_rocket"));
            Vfx.Puff(from, new Color(0.6f, 0.6f, 0.6f, 0.5f), 0.6f, 6);
            CameraShake.Add(0.15f);
            Sfx.Play("rocket", 0.8f, 0.1f);
        }
        mechShots++;
        nextCrit = false;
    }

    /// Q: switch the left button between the gun and the melee arm.
    public void MechSwapMode() => SetMechBlade(!mechBlade, false);

    void SetMechBlade(bool on, bool quiet)
    {
        mechBlade = on;
        mechCombo = 0;
        if (mechArm != null) foreach (var g in mechArm) if (g) g.SetActive(on);
        if (quiet) return;
        Sfx.Play(on ? "swap_melee" : "swap_gun", 0.8f, 0.05f, 0.8f);
        Hud.I?.Show(on ? $"近战模式：{mechDef.melee}" : $"射击模式：{mechDef.ranged}", 0.9f);
        if (on) Vfx.Sparks(transform.position + Vector3.up * mechDef.height * 0.55f + model.right * mechDef.radius, mechDef.color, 10, 4f);
    }

    /// Right button (or the left one in melee mode): the mech's melee arm. Swings chain into a
    /// three-hit combo; the third is a finisher (light: a lunging thrust, heavy: a ground smash).
    public void MechMelee()
    {
        if (mechMeleeCd > 0f) return;
        lastShot = Time.time;
        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        int step = mechCombo;
        mechCombo = (mechCombo + 1) % 3;
        mechComboT = Time.time;
        mechSwings++;
        bool finisher = step == 2;
        if (finisher && !HeavyMech)
        {
            MechThrust(fwd);
            return;
        }
        model.rotation = Quaternion.LookRotation(fwd);
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        float reach = HeavyMech ? 3.4f : 3.6f, arc = HeavyMech ? 110f : 150f;
        int baseDmg = HeavyMech ? (finisher ? 18 : 12) : 7;
        mechMeleeCd = HeavyMech ? (finisher ? 0.9f : 0.55f) : 0.3f;
        var hit = new System.Collections.Generic.HashSet<Enemy>();
        foreach (var c in Physics.OverlapSphere(origin, reach, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (!e || e.dead || !hit.Add(e)) continue;
            Vector3 to = e.transform.position - transform.position;
            to.y = 0f;
            if (Vector3.Angle(fwd, to) > arc / 2f && to.magnitude > 1.2f) continue;
            e.TakeDamage(RollDamage(baseDmg, out bool crit), to.normalized, crit, HeavyMech ? 9f : 4f);
            if (HeavyMech) e.Knock(to.normalized * 12f);
        }
        if (HeavyMech)
        {
            // The fist also smashes crates and barrels in front.
            foreach (var prop in PhysicsProp.Live.ToArray())
                if (prop && Vector3.Distance(prop.transform.position, transform.position + fwd * 1.5f) < 2f)
                    prop.Hit(baseDmg, prop.transform.position, fwd);
            Vector3 impact = transform.position + fwd * 2f;
            Vfx.Ring(impact, 1.8f, mechDef.color);
            Vfx.Sparks(impact + Vector3.up, new Color(1f, 0.8f, 0.4f), 14, 6f);
            Vfx.Puff(impact + Vector3.up * 0.5f, new Color(0.6f, 0.55f, 0.5f, 0.6f), 1f, 8);
            if (body) body.Trigger("swing", body.Length("swing") / 0.7f, 0.3f);
            CameraShake.Add(0.3f);
            Sfx.Play("smash", 1f, 0.1f, 0.8f);
            if (finisher)
            {
                // Third punch: both fists into the floor.
                Mechs.Shock(impact, 3.2f, RollDamage(8, out _), 10f);
                Vfx.Ring(impact, 3.2f, mechDef.color);
                Vfx.Puff(impact + Vector3.up * 0.3f, new Color(0.6f, 0.55f, 0.5f, 0.6f), 1.4f, 12);
                CameraShake.Add(0.2f);
            }
        }
        else
        {
            // The plasma blade also cuts enemy shots out of the air.
            foreach (var b in Bullet.Live.ToArray())
            {
                if (b.HitsEnemies) continue;
                Vector3 to = b.transform.position - origin;
                if (to.magnitude <= reach + 0.5f && Vector3.Angle(fwd, new Vector3(to.x, 0f, to.z)) <= arc / 2f) b.Cancel();
            }
            SlashFx.Spawn(origin, yaw, reach, arc, mechDef.color, swingIndex++ % 2 == 0 ? 25f : -25f, 0f, 0.18f, 0.7f);
            if (body) body.Trigger("slash", body.Length("slash") / 0.38f, 0.2f);
            Sfx.Play("slash", 0.9f, 0.05f);
            CameraShake.Add(hit.Count > 0 ? 0.15f : 0.05f);
        }
        nextCrit = false;
    }

    /// Light mech finisher: lunge at the nearest enemy ahead and run it through.
    void MechThrust(Vector3 fwd)
    {
        mechMeleeCd = 0.55f;
        Vector3 dir = fwd;
        float best = 7f;
        foreach (var e in Enemy.Live)
        {
            if (!e || e.dead) continue;
            Vector3 to = e.transform.position - transform.position;
            to.y = 0f;
            if (to.magnitude < best && Vector3.Angle(fwd, to) < 35f) { best = to.magnitude; dir = to.normalized; }
        }
        model.rotation = Quaternion.LookRotation(dir);
        // Close most of the gap, stopping just short of the target.
        dashDir = dir;
        dashSpeed = 18f;
        dashT = Mathf.Clamp((best - 1.6f) / dashSpeed, 0.06f, 0.22f);
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        const float reach = 5.2f;
        var pierced = new System.Collections.Generic.HashSet<Enemy>();
        foreach (var c in Physics.OverlapCapsule(origin, origin + dir * reach, 0.9f, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (!e || e.dead || !pierced.Add(e)) continue;
            e.TakeDamage(RollDamage(12, out bool crit), dir, crit, 7f);
            e.Knock(dir * 9f);
        }
        foreach (var b in Bullet.Live.ToArray())
            if (!b.HitsEnemies && Vector3.Distance(b.transform.position, origin + dir * reach * 0.5f) < reach * 0.6f) b.Cancel();
        float thrustYaw = Quaternion.LookRotation(dir).eulerAngles.y;
        SlashFx.Spawn(origin, thrustYaw, reach, 22f, mechDef.color, 0f, 0f, 0.22f, 0.9f);
        Vfx.Sparks(origin + dir * reach, mechDef.color, 12, 6f);
        if (body) body.Trigger("slash", body.Length("slash") / 0.3f, 0.15f);
        CameraShake.Add(0.18f);
        Sfx.Play("slash", 1f, 0.05f, 0.85f);
        nextCrit = false;
    }

    /// F: the light mech overdrives (double fire rate, no heat), the heavy fires a missile salvo.
    public bool MechSpecial()
    {
        if (mechSpecialCd > 0f) return false;
        mechSpecialCd = mechDef.specialCooldown;
        if (!HeavyMech)
        {
            overdriveT = 4f;
            heat = 0f;
            overheated = false;
            Vfx.Ring(transform.position, 2f, mechDef.color);
            Hud.I?.Show("超频：4 秒双倍射速、不过热", 1.2f);
            Sfx.Play("buff", 0.8f, 0f);
        }
        else
        {
            lastShot = Time.time;
            Vector3 flat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            for (int i = 0; i < 6; i++)
            {
                Vector3 from = MechMuzzle(i % 2 == 0 ? "leftshoulder" : "rightshoulder", 0f) + Vector3.up * 0.5f;
                Vector3 side = model.right * ((i % 2 == 0 ? -1f : 1f) * (0.3f + 0.15f * (i / 2)));
                var m = Bullet.Fire(from, (flat + side + Vector3.up * 0.6f).normalized * 16f, true, 0.13f, "bullet_missile", 4f,
                    RollDamage(5, out bool crit), 2.2f, crit);
                m.homing = 300f;
            }
            if (body) body.Trigger("shoot", 1.5f, 0.3f);
            CameraShake.Add(0.2f);
            Sfx.Play("rocket", 0.9f, 0f);
        }
        return true;
    }

    /// Medkits and heals repair the mech while you are inside.
    void RepairMech(int amount)
    {
        mechHp = Mathf.Min(mechDef.maxHp, mechHp + amount * 4);
        Vfx.Sparks(transform.position + Vector3.up * mechDef.height * 0.6f, new Color(0.5f, 1f, 0.5f), 8, 3f);
    }
}
