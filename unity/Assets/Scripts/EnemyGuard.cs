using UnityEngine;

/// The shield guard (盾卫): walks at you behind a tower shield and turns slowly to keep it on you.
/// Anything that hits the shield side glances off; circle round it, hit it in the back, or break the
/// guard with a heavy blow (8+ damage) or a blast. Up close it winds up and bashes, then stands open
/// for a moment with the shield swung aside.
public partial class Enemy
{
    public const string GuardModel = "shield_goblin", GuardShield = "tower_shield";
    const float GuardTurn = 1.3f; // rad/s: a dash or a sprint round its side gets behind the shield

    Transform guardShield;
    float guardPose = 1f, guardBreakT;
    public int blocks; // self-test counter

    /// True while the shield is up and facing forward.
    public bool Guarding => kind == EnemyKind.Guard && !dead && ai != Ai.Recover && guardBreakT <= 0f;

    /// The tower shield carried in front of the body (it follows the facing, not the arms).
    void BuildGuardShield(float h)
    {
        var holder = new GameObject("guard_shield").transform;
        holder.SetParent(model, false);
        guardShield = holder;
        if (Props.Has(GuardShield))
        {
            var s = Props.Spawn(GuardShield, holder.position, holder.rotation, holder, false);
            // The prop stands 1.15 m tall with its origin at the bottom middle; size it to the goblin
            // (in the model's own metres: the enemy is still at its spawn-pop scale here).
            s.transform.localScale = Vector3.one * (h * 0.72f / 1.15f);
            s.transform.localPosition = Vector3.down * h * 0.36f;
            foreach (var t in s.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Enemy;
        }
        else
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(slab.GetComponent<Collider>());
            slab.layer = Layers.Enemy;
            slab.transform.SetParent(holder, false);
            slab.transform.localScale = new Vector3(h * 0.45f, h * 0.72f, 0.08f);
            slab.GetComponent<Renderer>().sharedMaterial = Palette.Get("trim");
        }
        PoseGuardShield(h, 1f);
    }

    void PoseGuardShield(float h, float up)
    {
        if (!guardShield) return;
        // Up: square in front of the chest. Down: swung out to the right and tipped back.
        var raised = new Vector3(0.05f, h * 0.5f, cc.radius + 0.28f);
        var lowered = new Vector3(cc.radius + 0.35f, h * 0.38f, cc.radius * 0.4f);
        guardShield.localPosition = Vector3.Lerp(lowered, raised, up);
        guardShield.localRotation = Quaternion.Slerp(Quaternion.Euler(15f, 75f, 0f), Quaternion.identity, up);
    }

    /// Damage that reaches a guard: from the shield side it is blocked (0), unless it is heavy enough
    /// to knock the shield aside, which also halves it.
    int GuardBlock(int amount, Vector3 hitDir, Vector3? at)
    {
        if (!Guarding || amount <= 0) return amount;
        var d = new Vector3(hitDir.x, 0f, hitDir.z);
        if (d.sqrMagnitude < 0.0001f) return amount; // burning, poison: no direction, no block
        d.Normalize();
        if (Vector3.Dot(d, faceDir) > -0.4f) return amount; // from the side or behind
        Vector3 p = at ?? (guardShield ? guardShield.position : AimPoint);
        Tips.Show("guard", "盾卫正面挡子弹：绕到侧面或背后打，或者用重击、爆炸把盾打开");
        if (amount >= 8)
        {
            guardBreakT = 1.8f;
            ai = Ai.Recover;
            aiT = 1.8f;
            HideSight();
            anim?.Trigger("hit", 1.6f, 0.1f);
            Vfx.Sparks(p, new Color(1f, 0.7f, 0.3f), 18, 6f);
            Sfx.Play("smash", 0.5f, 0.1f);
            Hud.I?.AddText(p + Vector3.up * 0.4f, "破防！", new Color(1f, 0.7f, 0.3f));
            return amount / 2;
        }
        blocks++;
        Vfx.Sparks(p, new Color(1f, 0.85f, 0.5f), 8, 4f);
        Sfx.Play("clank", 0.45f, 0.15f);
        Hud.I?.AddText(p + Vector3.up * 0.3f, "格挡", new Color(0.8f, 0.82f, 0.9f));
        return 0;
    }

    Vector3 GuardThink(Vector3 to, float dist, float dt)
    {
        Vector3 toDir = dist > 0.01f ? to / dist : transform.forward;
        aiT -= dt;
        lungeCd -= dt;
        guardBreakT -= dt;
        switch (ai)
        {
            case Ai.Move:
            {
                // Keeps the shield on you, but only turns so fast.
                faceDir = Vector3.RotateTowards(faceDir, toDir, GuardTurn * dt, 0f);
                if (dist < 3.4f && lungeCd <= 0f && CanSeeTarget())
                {
                    ai = Ai.Windup;
                    aiT = 0.55f;
                    windups++;
                }
                var move = dist > 1.6f ? Steer(toDir) : Vector3.zero;
                smoothMove = move;
                return move;
            }
            case Ai.Windup:
                faceDir = Vector3.RotateTowards(faceDir, toDir, GuardTurn * 1.5f * dt, 0f);
                lungeDir = faceDir;
                DrawGroundLine(transform.position, transform.position + lungeDir * 3.2f);
                if (aiT <= 0f)
                {
                    HideSight();
                    ai = Ai.Lunge;
                    aiT = 0.3f;
                    lunges++;
                    anim?.Trigger("bash", 1.6f, 0.3f);
                    Sfx.Play("slash", 0.5f, 0.2f);
                    Vfx.Puff(transform.position + Vector3.up * 0.2f, new Color(0.55f, 0.47f, 0.38f, 0.6f), 0.8f, 6);
                }
                return Vector3.zero;
            case Ai.Lunge:
                faceDir = lungeDir;
                if (aiT <= 0f)
                {
                    // Open after the bash: the shield is out to the side.
                    ai = Ai.Recover;
                    aiT = 1.3f;
                    lungeCd = 2.2f;
                }
                return lungeDir * (9f / speed);
            default: // Recover (after a bash, or knocked open)
                if (aiT <= 0f) ai = Ai.Move;
                return Vector3.zero;
        }
    }

    void GuardTick(float dt)
    {
        if (!guardShield) return;
        guardPose = Mathf.MoveTowards(guardPose, Guarding ? 1f : 0f, dt * (Guarding ? 3f : 6f));
        PoseGuardShield(cc.height, guardPose);
    }
}
