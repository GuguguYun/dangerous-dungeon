using UnityEngine;

public enum EnemyKind { Gunner, Charger, Spreader, Boss, Bomber, Archer, Guard, Mimic }

/// Placeholder enemies. Gunner keeps mid range and fires orbs; Charger rushes and hits on contact;
/// Spreader fires fans; Boss cycles ring / burst / charge attacks and summons help.
/// A Meshy model at Resources/Models/Enemies/<kind> replaces the capsule when it exists.
[RequireComponent(typeof(CharacterController))]
public partial class Enemy : MonoBehaviour
{
    public static Enemy ActiveBoss;
    /// Every enemy in the scene, kept up to date (per-frame lookups such as homing must not search the scene).
    public static readonly System.Collections.Generic.List<Enemy> Live = new System.Collections.Generic.List<Enemy>();

    void OnEnable() => Live.Add(this);
    void OnDisable() => Live.Remove(this);

    public EnemyKind kind = EnemyKind.Gunner;
    public int hp = 6;
    public int maxHp = 6;
    public float speed = 3.2f;
    public PlayerController target;
    public bool dead;
    Vector3 lastSeen, decoy;
    float decoyT;

    /// Where the enemy thinks the player is. While the player is stealthed it hunts a decoy point that
    /// wanders around the last place it saw them, so its attacks land at random nearby.
    public Vector3 TargetPos => target && target.Stealthed ? decoy : target ? target.transform.position : transform.position;

    void UpdateDecoy(float dt)
    {
        if (!target) return;
        if (!target.Stealthed)
        {
            lastSeen = target.transform.position;
            decoy = lastSeen;
            decoyT = 0f;
            return;
        }
        decoyT -= dt;
        if (decoyT <= 0f)
        {
            decoyT = Random.Range(0.8f, 1.6f);
            // 2.5-5 m from where it last saw you: never right on top of you, or stealth would not help.
            var off = Random.insideUnitCircle.normalized * Random.Range(2.5f, 5f);
            decoy = lastSeen + new Vector3(off.x, 0f, off.y);
        }
    }

    public void Knock(Vector3 impulse) => knock += new Vector3(impulse.x, 0f, impulse.z);
    public Vector3 AimPoint => cc && cc.enabled ? cc.bounds.center : transform.position + Vector3.up * 0.8f;
    public System.Action<Enemy> Died;
    public System.Action<EnemyKind, Vector3> Summon;

    CharacterController cc;
    Transform model;
    Transform eye;
    Renderer body;
    CharacterAnim anim;
    Transform held;      // the gunner's pistol / the shaman's staff
    ParticleSystem dustFx; // kicked up during a lunge or a boss charge
    Vector3 heldTip;     // muzzle or staff head, in held's space
    float hitAnimCd;
    float slowT, slowMul = 1f, burnT, burnTick;
    ParticleSystem burnFx, chillFx;
    MaterialPropertyBlock mpb;
    string bodyMat = "enemy_body";
    float shootTimer, strafeSign, strafeTimer, flash, vy, spawnT, deathT, contactCd;
    Vector3 knock;

    void Awake()
    {
        gameObject.layer = Layers.Enemy;
        cc = GetComponent<CharacterController>();
        cc.height = 1.6f;
        cc.radius = 0.45f;
        cc.center = new Vector3(0f, 0.8f, 0f);
        cc.stepOffset = 0.3f;
        cc.minMoveDistance = 0f;
        model = new GameObject("model").transform;
        model.SetParent(transform, false);
        mpb = new MaterialPropertyBlock();
        strafeSign = Random.value < 0.5f ? 1f : -1f;
        strafeTimer = Random.Range(1.5f, 3f);
        shootTimer = Random.Range(1.0f, 2.5f);
        transform.localScale = Vector3.one * 0.05f;
    }

    public void Setup(EnemyKind k, int floor)
    {
        kind = k;
        float scale = 1f;
        switch (k)
        {
            case EnemyKind.Gunner:
                maxHp = 6 + floor * 2; speed = 3.2f; bodyMat = "enemy_body";
                break;
            case EnemyKind.Charger:
                maxHp = 9 + floor * 3; speed = 5.4f; bodyMat = "enemy_charger";
                break;
            case EnemyKind.Spreader:
                maxHp = 8 + floor * 2; speed = 2.6f; bodyMat = "enemy_spreader"; shootTimer += 0.8f;
                break;
            case EnemyKind.Bomber:
                maxHp = 8 + floor * 2; speed = 3.0f; bodyMat = "enemy_body"; shootTimer += 1.0f;
                break;
            case EnemyKind.Archer:
                maxHp = 6 + floor * 2; speed = 3.0f; bodyMat = "enemy_charger"; shootTimer += 0.5f;
                break;
            case EnemyKind.Guard:
                maxHp = 12 + floor * 3; speed = 2.4f; bodyMat = "enemy_body";
                break;
            case EnemyKind.Mimic:
                // A chest that was one all along: no spawn pop, it just starts moving.
                maxHp = 30 + floor * 8; speed = 4.2f; bodyMat = "enemy_body";
                spawnT = 1f;
                transform.localScale = Vector3.one;
                break;
            case EnemyKind.Boss:
                bodyMat = "enemy_boss"; scale = 2.3f;
                SetupBoss(floor);
                break;
        }
        hp = maxHp;
        cc.height = 1.6f * scale;
        cc.radius = 0.45f * scale;
        cc.center = new Vector3(0f, 0.8f * scale, 0f);
        BuildVisual(scale);
    }

    void BuildVisual(float scale)
    {
        if (kind == EnemyKind.Mimic) { BuildMimicVisual(); return; }
        string id = kind == EnemyKind.Boss ? BossModel() : kind == EnemyKind.Guard ? GuardModel : kind.ToString().ToLower();
        // The denser second-round minion when it has been imported.
        if (kind != EnemyKind.Boss && Resources.Load<GameObject>($"Models/Chars/{id}_hq/rigged")) id += "_hq";
        anim = CharacterAnim.Spawn(id, model, Layers.Enemy);
        if (anim)
        {
            body = anim.GetComponentInChildren<SkinnedMeshRenderer>();
            // The enemy is still at spawn-pop scale here, so undo it when measuring.
            float h = body ? Mathf.Max(0.8f, body.bounds.size.y / transform.lossyScale.y) : 1.6f;
            cc.height = h;
            cc.radius = Mathf.Clamp(h * 0.28f, 0.35f, 1.1f);
            cc.center = new Vector3(0f, h / 2f, 0f);
            eye = new GameObject("eye").transform;
            eye.SetParent(model, false);
            eye.localPosition = new Vector3(0f, h * 0.7f, cc.radius);
            if (kind == EnemyKind.Gunner) Hold("pistol");
            else if (kind == EnemyKind.Spreader) Hold("thunder");
            else if (kind == EnemyKind.Archer) Hold("bow", "lefthand");
            else if (kind == EnemyKind.Bomber) HoldBomb();
            else if (kind == EnemyKind.Guard) BuildGuardShield(h);
            return;
        }
        if (kind == EnemyKind.Guard) BuildGuardShield(1.6f);
        body = Part(PrimitiveType.Capsule, new Vector3(0f, 0.8f, 0f) * scale, new Vector3(0.9f, 0.8f, 0.9f) * scale, bodyMat);
        eye = Part(PrimitiveType.Sphere, new Vector3(0f, 1.25f, 0.4f) * scale, Vector3.one * 0.26f * scale, "enemy_eye").transform;
        if (kind == EnemyKind.Charger)
            Part(PrimitiveType.Cube, new Vector3(0f, 1.0f, 0.5f), new Vector3(0.7f, 0.2f, 0.3f), "trim");
        if (kind == EnemyKind.Boss)
        {
            Part(PrimitiveType.Cube, new Vector3(-0.35f, 1.7f, 0f) * scale, new Vector3(0.12f, 0.45f, 0.12f) * scale, "enemy_eye");
            Part(PrimitiveType.Cube, new Vector3(0.35f, 1.7f, 0f) * scale, new Vector3(0.12f, 0.45f, 0.12f) * scale, "enemy_eye");
        }
    }

    Renderer Part(PrimitiveType type, Vector3 pos, Vector3 scale, string mat)
    {
        var go = GameObject.CreatePrimitive(type);
        DestroyImmediate(go.GetComponent<Collider>());
        go.layer = Layers.Enemy;
        go.transform.SetParent(model, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Palette.Get(mat);
        return r;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dead)
        {
            deathT += dt;
            if (anim)
            {
                // Let the ragdoll (or death clip) settle, then sink into the floor.
                float settle = ragdoll != null ? 2.6f : 1.4f;
                if (deathT > settle && ragdoll != null && ragdoll.Count > 0 && !ragdoll[0].isKinematic) Ragdoll.Freeze(ragdoll);
                if (deathT > settle) transform.position += Vector3.down * dt * 1.5f;
                if (deathT > settle + 0.8f) Destroy(gameObject);
            }
            else
            {
                transform.localScale = Vector3.Lerp(Vector3.one, new Vector3(1.4f, 0.1f, 1.4f), deathT / 0.15f);
                if (deathT >= 0.15f) Destroy(gameObject);
            }
            return;
        }
        if (spawnT < 0.35f)
        {
            spawnT += dt;
            transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, spawnT / 0.35f);
        }
        contactCd -= dt;
        vy = cc.isGrounded ? -2f : vy - 20f * dt;

        Vector3 move = Vector3.zero;
        if (target && !target.dead)
        {
            UpdateDecoy(dt);
            Vector3 to = TargetPos - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (kind == EnemyKind.Boss)
            {
                move = BossThink(to, dist, dt);
                faceDir = chargeT > 0f ? chargeDir : leaping ? new Vector3(leapVel.x, 0f, leapVel.z) : (dist > 0.01f ? to / dist : faceDir);
            }
            else move = kind == EnemyKind.Guard ? GuardThink(to, dist, dt) : kind == EnemyKind.Mimic ? MimicThink(to, dist, dt) : MinionThink(to, dist, dt);

            // Bodies only hurt when they mean to: bosses always, chargers during the lunge.
            float reach = cc.radius + 0.75f;
            bool contact = kind == EnemyKind.Boss || ((kind == EnemyKind.Charger || kind == EnemyKind.Guard || kind == EnemyKind.Mimic) && Lunging);
            float realDist = Vector3.Distance(new Vector3(target.transform.position.x, 0f, target.transform.position.z), new Vector3(transform.position.x, 0f, transform.position.z));
            if (contact && contactCd <= 0f && realDist < reach)
            {
                target.TakeDamage(kind == EnemyKind.Boss ? 2 : 1);
                contactCd = 1f;
                // Claw marks across the target: the hit reads as a slash, not a bump.
                float clawYaw = Quaternion.LookRotation(faceDir.sqrMagnitude > 0.01f ? faceDir : transform.forward).eulerAngles.y;
                for (int i = -1; i <= 1; i++)
                    SlashFx.Spawn(target.transform.position + Vector3.up * (1.0f + 0.15f * i) - faceDir * 0.6f, clawYaw, 1.3f, 50f,
                        new Color(1f, 0.25f, 0.2f), 35f, 0f, 0.16f, 0.85f);
                if (kind == EnemyKind.Boss) anim?.Trigger("attack", 1.5f, 0.3f);
            }
        }
        knock = Vector3.MoveTowards(knock, Vector3.zero, 20f * dt);
        StatusEffects(dt);
        EliteTick(dt);
        if (dead) return;
        cc.Move((move * speed * (slowT > 0f ? slowMul : 1f) + knock + Vector3.up * vy) * dt);
        LastMove = move;
        Face(dt);
        GuardTick(dt);
        hitAnimCd -= dt;
        if (anim)
        {
            bool moving = move.sqrMagnitude > 0.01f;
            if (kind == EnemyKind.Boss) anim.SetBase(moving ? "walk" : "idle", chargeT > 0f ? 2f : 1f);
            else anim.SetBase(moving ? (kind == EnemyKind.Charger ? "run" : "walk") : "idle");
        }

        if (flash > 0f)
        {
            flash -= dt;
            if (body)
            {
                var fc = flash > 0f ? Color.white : (anim || kind == EnemyKind.Mimic ? Color.white : Palette.ColorOf(bodyMat));
                mpb.SetColor("_Color", fc);
                mpb.SetColor("_BaseColor", fc);
                body.SetPropertyBlock(mpb);
            }
        }
    }

    Vector3 KeepRange(Vector3 to, float dist, float dt)
    {
        strafeTimer -= dt;
        if (strafeTimer <= 0f)
        {
            strafeSign = -strafeSign;
            strafeTimer = Random.Range(1.5f, 3f);
        }
        if (dist < 0.01f) return Vector3.zero;
        // Archers and bombers hang back; gunners and shamans come closer.
        float far = kind == EnemyKind.Archer ? 13f : kind == EnemyKind.Bomber ? 11f : 8f;
        float near = kind == EnemyKind.Archer ? 7f : kind == EnemyKind.Bomber ? 6f : 4.5f;
        if (dist > far) return to / dist;
        if (dist < near) return -to / dist;
        return Vector3.Cross(Vector3.up, to).normalized * strafeSign;
    }

    /// Shots fly level at a fixed height above the floor so the player can read them: green things skim
    /// the floor (jump), everything else (bullets, arrows, coins, rocks, skulls, axes) is at chest height.
    float ShotHeight => kind == EnemyKind.Spreader ? 0.4f : 1.0f;
    Vector3 TargetPoint() => new Vector3(TargetPos.x, ShotHeight, TargetPos.z);
    Vector3 Muzzle() => new Vector3(eye.position.x, ShotHeight, eye.position.z);
    Vector3 TargetDir()
    {
        var d = TargetPoint() - Muzzle();
        d.y = 0f;
        return d.normalized;
    }

    bool CanSeeTarget() =>
        !Physics.Linecast(Muzzle(), TargetPoint(), Layers.WorldMask, QueryTriggerInteraction.Ignore);

    void Shoot(Vector3 dir, float speed, string mat, float radius, float height = -1f)
    {
        var from = Muzzle();
        if (height >= 0f) from.y = height;
        Bullet.Fire(from, dir * speed, false, radius, mat, 4f);
        Sfx.Play("enemy_shot", 0.25f);
        if (mat == "bullet_enemy" || mat == "bullet_arrow") Tips.Show("dodge", "胸口高的子弹：左右躲开，或按 Shift 冲刺穿过去");
        if (held)
        {
            // The shot leaves at its readable fixed height; the flash shows where it came from.
            Vector3 tip = held.TransformPoint(heldTip);
            if (kind == EnemyKind.Gunner) Vfx.MuzzleFlash(tip, dir, Palette.ColorOf(mat));
            else Vfx.Sparks(tip, Palette.ColorOf(mat), 4, 2.5f);
        }
    }

    /// Puts a weapon model in the right hand; LateUpdate keeps it pointing where the body faces.
    void Hold(string weaponId, string bone = "righthand")
    {
        var hand = anim.Find(bone);
        var w = Weapons.Get(weaponId);
        if (!hand || w == null) return;
        held = new GameObject("held_" + weaponId).transform;
        held.SetParent(hand, false);
        // World-size the pivot (the rig is scaled); goblins are a bit smaller than the hero. The enemy is
        // still at its spawn-pop scale here, so size against the root, not the absolute scale.
        held.localScale = Vector3.one * (0.85f * transform.lossyScale.x / Mathf.Max(0.0001f, hand.lossyScale.x));
        var vis = Weapons.MakeVisual(w, held, Layers.Enemy);
        float front = 0.3f;
        foreach (var r in vis.GetComponentsInChildren<Renderer>())
        {
            var m = held.worldToLocalMatrix * r.transform.localToWorldMatrix;
            front = Mathf.Max(front, m.MultiplyPoint3x4(r.localBounds.max).z, m.MultiplyPoint3x4(r.localBounds.min).z);
        }
        heldTip = new Vector3(0f, 0f, front);
    }

    void LateUpdate()
    {
        if (!held || dead) return;
        // Gun level along the facing; the staff upright with its head up.
        held.rotation = kind == EnemyKind.Spreader
            ? Quaternion.LookRotation(Vector3.up, -model.forward)
            : Quaternion.LookRotation(model.forward, Vector3.up);
    }

    /// The bomber carries a lit bomb in its throwing hand (hidden while one is in the air).
    void HoldBomb()
    {
        var hand = anim.Find("righthand");
        if (!hand) return;
        held = new GameObject("held_bomb").transform;
        held.SetParent(hand, false);
        held.localScale = Vector3.one * (transform.lossyScale.x / Mathf.Max(0.0001f, hand.lossyScale.x));
        EnemyBomb.Model(held, 0.3f); // the same Meshy bomb it throws
        foreach (var t in held.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Enemy;
        heldTip = Vector3.zero;
    }

    void Fan(int n, float arc, float speed, string mat, float height = -1f)
    {
        if (Bullet.IsLow(mat)) Tips.Show("low", "绿色贴地弹：按空格跳过去（时机对了还能触发完美闪避）");
        Vector3 dir = TargetDir();
        for (int i = 0; i < n; i++)
        {
            float a = -arc / 2f + arc * i / (n - 1);
            Shoot(Quaternion.Euler(0f, a, 0f) * dir, speed, mat, 0.16f, height);
        }
    }

    void Ring(int n, float speed, float offset = 0f)
    {
        Vector3 from = new Vector3(transform.position.x, 1.0f, transform.position.z);
        for (int i = 0; i < n; i++)
        {
            var d = Quaternion.Euler(0f, offset + 360f * i / n, 0f) * Vector3.forward;
            Bullet.Fire(from, d * speed, false, 0.2f, BossShot, 5f);
        }
    }

    public void ApplySlow(float mul, float seconds)
    {
        slowMul = mul;
        slowT = Mathf.Max(slowT, seconds);
    }

    public void ApplyBurn(float seconds)
    {
        if (burnT <= 0f) burnTick = 0.5f;
        burnT = Mathf.Max(burnT, seconds);
    }

    public bool Slowed => slowT > 0f;
    public bool Burning => burnT > 0f;
    public void ClearSlow() { slowT = 0f; slowMul = 1f; }
    public void ClearBurn() => burnT = 0f;

    void StatusEffects(float dt)
    {
        if (slowT > 0f) slowT -= dt;
        if (burnT > 0f)
        {
            burnT -= dt;
            burnTick -= dt;
            if (burnTick <= 0f)
            {
                burnTick = 0.5f;
                TakeDamage(1, Vector3.zero);
                Reactions.CheckClouds(transform.position, 0f); // a burning goblin walking into gas sets it off
            }
        }
        // Flames while burning, cold mist while slowed.
        if (burnT > 0f && !burnFx) { burnFx = Vfx.Trail(transform, "burn"); burnFx.transform.localPosition = Vector3.up * cc.height * 0.5f; }
        if (burnT <= 0f && burnFx) { Vfx.Detach(burnFx); burnFx = null; }
        if (slowT > 0f && !chillFx) { chillFx = Vfx.Trail(transform, "chill"); chillFx.transform.localPosition = Vector3.up * cc.height * 0.5f; }
        if (slowT <= 0f && chillFx) { Vfx.Detach(chillFx); chillFx = null; }
        if (body && flash <= 0f && (slowT > 0f || burnT > 0f))
        {
            var tint = slowT > 0f ? new Color(0.6f, 0.85f, 1.6f) : new Color(1.6f, 0.8f, 0.4f);
            mpb.SetColor("_Color", tint);
            mpb.SetColor("_BaseColor", tint);
            body.SetPropertyBlock(mpb);
        }
        else if (body && flash <= 0f && (slowT <= 0f && burnT <= 0f) && wasTinted)
        {
            mpb.SetColor("_Color", Color.white);
            mpb.SetColor("_BaseColor", anim ? Color.white : Palette.ColorOf(bodyMat));
            body.SetPropertyBlock(mpb);
        }
        wasTinted = slowT > 0f || burnT > 0f;
    }

    bool wasTinted;

    System.Collections.Generic.List<Rigidbody> ragdoll;

    /// Characters shove loose props they walk into.
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.rigidbody && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForceAtPosition(hit.moveDirection * 1.5f, hit.point, ForceMode.VelocityChange);
    }

    /// at: where the blow landed on the body when that is known (a bullet's hit), so a shield ripples
    /// where it was struck.
    public void TakeDamage(int amount, Vector3 hitDir, bool crit = false, float force = 3.5f, Vector3? at = null)
    {
        if (dead) return;
        if (kind == EnemyKind.Boss) amount = ModifyIncoming(amount);
        amount = GuardBlock(amount, hitDir, at); // the shield guard's tower shield, from the front
        amount = AbsorbWithShield(amount, hitDir, at);
        amount = Reactions.Shatter(this, amount); // a heavy hit on a frozen enemy shatters the ice
        if (amount <= 0)
        {
            Hud.I?.HitMarker();
            return;
        }
        RunState.damageDealt += Mathf.Clamp(amount, 0, Mathf.Max(0, hp));
        if (hitDir != Vector3.zero) Combo.Hit(); // direct hits keep the streak going (burn/poison ticks do not)
        hp -= amount;
        flash = 0.08f;
        if (body)
        {
            mpb.SetColor("_Color", Color.white);
            mpb.SetColor("_BaseColor", new Color(3f, 3f, 3f));
            body.SetPropertyBlock(mpb);
        }
        if (kind != EnemyKind.Boss) knock = new Vector3(hitDir.x, 0f, hitDir.z) * 4f;
        if (anim && hp > 0 && hitAnimCd <= 0f && kind != EnemyKind.Boss)
        {
            anim.Trigger("hit", 2f, 0.12f);
            hitAnimCd = 0.6f;
        }
        Hud.I?.AddDamage(transform.position + Vector3.up * (cc.height + 0.3f), amount, crit);
        if (amount >= 8 || crit) TimeControl.HitStop(crit && amount >= 8 ? 0.07f : 0.045f);
        Hud.I?.HitMarker();
        Sfx.Play("hit", 0.45f, 0.2f);
        if (hp <= 0)
        {
            dead = true;
            cc.enabled = false;
            // Physics death: the Meshy skeleton becomes a ragdoll thrown along the hit.
            if (anim) ragdoll = Ragdoll.Activate(model, new Vector3(hitDir.x, 0f, hitDir.z) * force + Vector3.up * (1.5f + force * 0.3f), kind == EnemyKind.Boss ? 160f : 45f);
            if (ragdoll == null) anim?.Trigger("die", 1.3f);
            if (ActiveBoss == this) ActiveBoss = null;
            CancelInvoke();
            if (kind == EnemyKind.Boss) HideTelegraph();
            HideSight();
            Vfx.Detach(burnFx);
            Vfx.Detach(chillFx);
            Pickup.SpawnEnergy(transform.position, kind == EnemyKind.Boss ? 12 : Random.Range(1, 4));
            Pickup.SpawnGold(transform.position, kind == EnemyKind.Boss ? 20 : Random.Range(1, 4));
            if (Random.value < (kind == EnemyKind.Boss ? 1f : 0.06f))
                Pickup.Spawn(PickupKind.Health, transform.position + Vector3.right, 2);
            RunState.kills++;
            if (kind == EnemyKind.Boss) RunState.bossesKilled++;
            EliteDeath();
            Sfx.Play(kind == EnemyKind.Boss ? "explode" : "die", 0.6f);
            if (target) target.AddEnergy(RunState.energyOnKill);
            if (kind == EnemyKind.Boss) CameraShake.Add(0.8f);
            Died?.Invoke(this);
        }
    }
}
