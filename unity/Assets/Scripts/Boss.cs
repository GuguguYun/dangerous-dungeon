using UnityEngine;

public enum BossKind { GoblinKing, BoarChief, OrcWarlord }

/// The three floor bosses, one per floor. Each cycles its own attacks and enrages below half
/// health (faster, denser patterns). Slams send floor shockwaves that only a jump clears;
/// the boar's charge is telegraphed by a red line and stuns it if it hits a wall.
public partial class Enemy
{
    public BossKind bossKind;

    /// One line under the boss's name on its entrance banner.
    public string Epithet => bossKind switch
    {
        BossKind.GoblinKing => "爱撒金币的胖国王 · 小心他跳起来砸地",
        BossKind.BoarChief => "红线预警后直线冲锋 · 引他撞墙会晕",
        _ => "地牢最深处的主人 · 半血后狂暴",
    };

    /// Each boss throws its own thing: the Goblin King gold coins, the Boar Chief rocks, the Orc Warlord axes.
    string BossShot => bossKind switch
    {
        BossKind.GoblinKing => "bullet_gold",
        BossKind.BoarChief => "bullet_rock",
        _ => "bullet_axe",
    };
    public string displayName = "";
    public int leaps, charges, stuns, slams; // counters for the self-test

    int phase;
    float phaseTimer;
    int burstLeft;
    float burstTimer;
    Vector3 chargeDir;
    float chargeT, telegraphT, stunT, leapT;
    int chargesLeft;
    bool leaping, enraged, summoned60, summoned30;
    Vector3 leapVel;
    LineRenderer telegraph;

    public bool Stunned => stunT > 0f;
    public bool Enraged => enraged;
    float Rage => enraged ? 0.65f : 1f; // multiplies cooldowns

    string BossModel() => bossKind switch
    {
        BossKind.GoblinKing => "goblin_king",
        BossKind.BoarChief => "boar_chief",
        _ => Resources.Load<GameObject>("Models/Chars/boss_hq/rigged") ? "boss_hq" : "boss",
    };

    void SetupBoss(int floor)
    {
        bossKind = floor <= 1 ? BossKind.GoblinKing : floor == 2 ? BossKind.BoarChief : BossKind.OrcWarlord;
        switch (bossKind)
        {
            case BossKind.GoblinKing: maxHp = 240; speed = 2.6f; displayName = "贪金王·葛巴"; break;
            case BossKind.BoarChief: maxHp = 340; speed = 2.2f; displayName = "裂地酋长·獠牙"; break;
            default: maxHp = 420; speed = 2.4f; displayName = "血斧督军·克鲁格"; break;
        }
        ActiveBoss = this;
        phaseTimer = 2f;
        phase = 2; // the first attack is phase 0: each boss opens with its signature move
    }

    int ModifyIncoming(int amount) => stunT > 0f ? amount * 2 : amount;

    Vector3 BossThink(Vector3 to, float dist, float dt)
    {
        if (!enraged && hp < maxHp * 0.5f)
        {
            enraged = true;
            speed *= 1.3f;
            Hud.I?.Show($"{displayName}狂暴了！", 1.5f);
            Sfx.Play("boss", 1f, 0f);
            anim?.Trigger("taunt", 1.5f, 0.5f);
        }
        if (!summoned60 && hp < maxHp * 0.6f) { summoned60 = true; SummonMinions(); }
        if (!summoned30 && hp < maxHp * 0.3f) { summoned30 = true; SummonMinions(); }

        if (stunT > 0f)
        {
            stunT -= dt;
            return Vector3.zero;
        }
        if (leaping) return LeapStep(dt);
        if (telegraphT > 0f)
        {
            telegraphT -= dt;
            DrawLine(transform.position, transform.position + chargeDir * 26f, Mathf.PingPong(Time.time * 6f, 1f));
            if (telegraphT <= 0f)
            {
                HideTelegraph();
                chargeT = 1.4f;
                PassThroughPlayer(true);
                charges++;
                anim?.Trigger("swing", 1.3f, 0.6f);
                Sfx.Play("rocket", 0.8f, 0f);
            }
            return Vector3.zero;
        }
        if (chargeT > 0f) return ChargeStep(dt, to, dist);
        if (burstLeft > 0)
        {
            burstTimer -= dt;
            if (burstTimer <= 0f)
            {
                burstLeft--;
                burstTimer = 0.35f * Rage;
                if (bossKind == BossKind.GoblinKing) Fan(7, 70f, 10f, "bullet_low", 0.4f);
                else Fan(7, 70f, 12f, BossShot);
            }
        }

        phaseTimer -= dt;
        if (phaseTimer <= 0f)
        {
            phase = (phase + 1) % 3;
            phaseTimer = 2.6f * Rage;
            switch (bossKind)
            {
                case BossKind.GoblinKing: GoblinAttack(to, dist); break;
                case BossKind.BoarChief: BoarAttack(to, dist); break;
                default: OrcAttack(to, dist); break;
            }
        }
        return dist > 6f ? to / dist : Vector3.zero;
    }

    // ---------- goblin king: leap slam, low-orb fans ----------

    void GoblinAttack(Vector3 to, float dist)
    {
        if (phase == 0 || phase == 2)
        {
            // Leap at the player and land with a shockwave; the landing spot is marked first.
            Vector3 land = TargetPos;
            leapVel = Vector3.ClampMagnitude((land - transform.position) / 0.9f, 14f);
            leapVel.y = 0f;
            vy = 9f;
            leaping = true;
            leapT = 0f;
            leaps++;
            DrawCircle(land, 2.5f);
            anim?.Trigger("slam", 1f, 0.9f);
            Sfx.Play("slash", 1f, 0f);
        }
        else
        {
            burstLeft = enraged ? 3 : 2;
            burstTimer = 0f;
            anim?.Trigger("cast", 1.2f, 0.5f);
        }
    }

    Vector3 LeapStep(float dt)
    {
        leapT += dt;
        if (leapT > 0.25f && cc.isGrounded)
        {
            leaping = false;
            HideTelegraph();
            Slam();
            if (enraged) Ring(12, 9f);
            return Vector3.zero;
        }
        return leapVel / Mathf.Max(0.1f, speed);
    }

    // ---------- boar chief: telegraphed charge (stuns on walls), double stomp, spin ----------

    void BoarAttack(Vector3 to, float dist)
    {
        switch (phase)
        {
            case 0:
                chargesLeft = enraged ? 2 : 1;
                StartCharge(to, dist);
                break;
            case 1:
                anim?.Trigger("slam", 1.2f, 0.9f);
                Invoke(nameof(Slam), 0.45f);
                Invoke(nameof(SlamEcho), 1.05f);
                break;
            default:
                anim?.Trigger("taunt", 1.5f, 0.4f);
                Ring(20, 8f);
                Invoke(nameof(RingLate), 0.5f);
                break;
        }
    }

    void StartCharge(Vector3 to, float dist)
    {
        chargeDir = dist > 0.01f ? to / dist : transform.forward;
        telegraphT = enraged ? 0.45f : 0.7f;
    }

    Vector3 ChargeStep(float dt, Vector3 to, float dist)
    {
        chargeT -= dt;
        if (!dustFx) dustFx = Vfx.Trail(transform, "dust");
        transform.rotation = Quaternion.LookRotation(chargeDir);
        bool wall = Physics.Raycast(transform.position + Vector3.up * 1f, chargeDir, cc.radius + 0.5f, Layers.WorldMask, QueryTriggerInteraction.Ignore);
        if (bossKind == BossKind.BoarChief && wall && chargeT < 1.3f)
        {
            chargeT = 0f;
            PassThroughPlayer(false);
            stunT = 1.2f;
            stuns++;
            CameraShake.Add(0.5f);
            Sfx.Play("explode", 0.7f);
            Hud.I?.Show($"{displayName}撞晕了！伤害翻倍", 1.2f);
            Vfx.Detach(dustFx);
            dustFx = null;
            Vector3 crash = transform.position + chargeDir * cc.radius + Vector3.up;
            Vfx.Puff(crash, new Color(0.55f, 0.47f, 0.38f, 0.7f), 1.6f, 14);
            Vfx.Sparks(crash, new Color(1f, 0.85f, 0.5f), 16, 7f);
            anim?.Trigger("hit", 0.8f, 1.1f);
            return Vector3.zero;
        }
        if (chargeT <= 0f)
        {
            Vfx.Detach(dustFx);
            dustFx = null;
            PassThroughPlayer(false);
            if (--chargesLeft > 0) StartCharge(to, dist);
        }
        float chargeSpeed = bossKind == BossKind.BoarChief ? 16f : 12f;
        return wall ? Vector3.zero : chargeDir * (chargeSpeed / speed);
    }

    /// A charging boss runs through the player (who still takes the hit) instead of stopping on them,
    /// so side-stepping is what makes it slam into the wall behind.
    void PassThroughPlayer(bool on)
    {
        var pc = target ? target.GetComponent<CharacterController>() : null;
        if (pc) Physics.IgnoreCollision(cc, pc, on);
    }

    // ---------- orc warlord: slam + ring, burst fans, short charge ----------

    void OrcAttack(Vector3 to, float dist)
    {
        switch (phase)
        {
            case 0:
                anim?.Trigger("slam", 1.2f, 0.6f);
                Invoke(nameof(Slam), 0.45f);
                Invoke(nameof(RingLate), 0.9f);
                if (enraged) Invoke(nameof(SlamEcho), 1.05f);
                break;
            case 1:
                burstLeft = enraged ? 5 : 3;
                burstTimer = 0f;
                if (enraged) Ring(16, 8f);
                break;
            default:
                anim?.Trigger("swing", 1.3f, 0.6f);
                chargeDir = dist > 0.01f ? to / dist : transform.forward;
                chargeT = 0.8f;
                chargesLeft = 1;
                charges++;
                PassThroughPlayer(true);
                CameraShake.Add(0.2f);
                break;
        }
    }

    // ---------- shared ----------

    void RingLate() { if (!dead) Ring(18, 9f, 10f); }

    void Slam()
    {
        if (dead) return;
        slams++;
        Shockwave.Spawn(transform.position, 9f, 20f, 1);
        if (RunState.floor >= 3 && bossKind == BossKind.OrcWarlord && !enraged) Invoke(nameof(SlamEcho), 0.6f);
    }

    void SlamEcho()
    {
        if (dead) return;
        slams++;
        Shockwave.Spawn(transform.position, 11f, 20f, 1);
    }

    void SummonMinions()
    {
        Hud.I?.Show($"{displayName}召唤了援军！", 1.5f);
        var pair = bossKind switch
        {
            BossKind.GoblinKing => new[] { EnemyKind.Gunner, EnemyKind.Spreader },
            BossKind.BoarChief => new[] { EnemyKind.Charger, EnemyKind.Charger },
            _ => new[] { EnemyKind.Gunner, EnemyKind.Charger },
        };
        for (int i = 0; i < 2; i++)
        {
            var off = Quaternion.Euler(0f, 120f * i + 60f, 0f) * Vector3.forward * 3f;
            Summon?.Invoke(pair[i], transform.position + off);
        }
    }

    LineRenderer Telegraph()
    {
        if (telegraph) return telegraph;
        telegraph = new GameObject("telegraph").AddComponent<LineRenderer>();
        telegraph.sharedMaterial = Palette.Trail("beam");
        telegraph.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return telegraph;
    }

    void DrawLine(Vector3 a, Vector3 b, float pulse)
    {
        var t = Telegraph();
        t.enabled = true;
        t.loop = false;
        t.positionCount = 2;
        t.widthMultiplier = 1.2f;
        var c = new Color(1f, 0.15f, 0.1f, 0.35f + 0.35f * pulse);
        t.startColor = t.endColor = c;
        t.SetPosition(0, new Vector3(a.x, 0.06f, a.z));
        t.SetPosition(1, new Vector3(b.x, 0.06f, b.z));
    }

    void DrawCircle(Vector3 centre, float r)
    {
        var t = Telegraph();
        t.enabled = true;
        t.loop = true;
        t.positionCount = 40;
        t.widthMultiplier = 0.25f;
        t.startColor = t.endColor = new Color(1f, 0.15f, 0.1f, 0.8f);
        for (int i = 0; i < 40; i++)
        {
            float a = i * Mathf.PI * 2f / 40f;
            t.SetPosition(i, new Vector3(centre.x + Mathf.Cos(a) * r, 0.06f, centre.z + Mathf.Sin(a) * r));
        }
    }

    void HideTelegraph()
    {
        if (telegraph) telegraph.enabled = false;
    }

    void OnDestroy()
    {
        if (telegraph) Destroy(telegraph.gameObject);
        if (sight) Destroy(sight.gameObject);
        if (ActiveBoss == this) ActiveBoss = null;
        Elites.Remove(this);
    }
}
