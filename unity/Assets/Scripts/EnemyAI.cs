using UnityEngine;

/// Minion behaviour (gunner, spreader, charger) as small readable state machines:
/// - gunner/spreader: move into range (facing where they walk) -> stop and aim with a red sight
///   line -> fire -> brief recovery;
/// - charger: run in -> wind up with a red ground line -> lunge (only the lunge hurts) -> recover.
/// They push apart from each other and steer around crates and pillars.
public partial class Enemy
{
    enum Ai { Move, Aim, Windup, Lunge, Recover }

    Ai ai;
    float aiT, lungeCd;
    Vector3 lungeDir;
    Vector3 faceDir = Vector3.forward;
    Vector3 smoothMove;
    LineRenderer sight;

    public Vector3 LastMove { get; private set; }
    public int aims, shots, windups, lunges; // counters for the self-test
    public bool Lunging => ai == Ai.Lunge;

    Vector3 MinionThink(Vector3 to, float dist, float dt)
    {
        Vector3 toDir = dist > 0.01f ? to / dist : transform.forward;
        aiT -= dt;
        lungeCd -= dt;
        switch (ai)
        {
            case Ai.Move:
            {
                Vector3 move;
                bool sees = CanSeeTarget();
                if (kind == EnemyKind.Charger)
                {
                    move = dist > 1.2f ? toDir : Vector3.zero;
                    if (dist < 5.5f && lungeCd <= 0f && sees)
                    {
                        ai = Ai.Windup;
                        aiT = 0.45f;
                        windups++;
                    }
                }
                else
                {
                    // Lost sight of the player: close in instead of strafing behind cover.
                    move = sees ? KeepRange(to, dist, dt) : toDir;
                    shootTimer -= dt;
                    if (shootTimer <= 0f && sees)
                    {
                        ai = Ai.Aim;
                        aiT = kind == EnemyKind.Spreader ? 0.5f : kind == EnemyKind.Archer ? 0.6f : kind == EnemyKind.Bomber ? 0.45f : 0.35f;
                        if (kind == EnemyKind.Bomber) anim?.Trigger("throw", 1.3f, 0.5f); // wind up the throw
                        aims++;
                    }
                }
                // Turn the walking direction gradually (strafe flips, separation and detours would
                // otherwise snap it every frame) and face exactly that direction.
                move = Steer(move);
                smoothMove = Vector3.RotateTowards(smoothMove.sqrMagnitude > 0.01f ? smoothMove : move, move, 9f * dt, 6f * dt);
                faceDir = smoothMove.sqrMagnitude > 0.05f ? smoothMove.normalized : toDir;
                return smoothMove;
            }
            case Ai.Aim:
                smoothMove = Vector3.zero;
                faceDir = toDir;
                if (kind != EnemyKind.Bomber) DrawSight(Muzzle(), TargetPoint()); // a lob needs no sight line
                if (aiT <= 0f)
                {
                    HideSight();
                    Fire();
                    ai = Ai.Recover;
                    aiT = 0.3f;
                }
                return Vector3.zero;
            case Ai.Windup:
                if (aiT > 0.15f) lungeDir = toDir; // track, then commit for the last moment
                faceDir = lungeDir;
                DrawGroundLine(transform.position, transform.position + lungeDir * 5.5f);
                if (aiT <= 0f)
                {
                    HideSight();
                    ai = Ai.Lunge;
                    aiT = 0.45f;
                    lunges++;
                    anim?.Trigger("attack", 1.6f, 0.4f);
                    Sfx.Play("slash", 0.5f, 0.2f);
                    dustFx = Vfx.Trail(transform, "dust");
                    Vfx.Puff(transform.position + Vector3.up * 0.2f, new Color(0.55f, 0.47f, 0.38f, 0.6f), 0.8f, 6);
                }
                return Vector3.zero;
            case Ai.Lunge:
                faceDir = lungeDir;
                if (aiT <= 0f)
                {
                    ai = Ai.Recover;
                    aiT = 0.7f;
                    lungeCd = 1.2f;
                    Vfx.Detach(dustFx);
                    dustFx = null;
                }
                return lungeDir * (11f / speed);
            default: // Recover
                faceDir = toDir;
                if (aiT <= 0f) ai = Ai.Move;
                return Vector3.zero;
        }
    }

    void Fire()
    {
        shots++;
        switch (kind)
        {
            case EnemyKind.Spreader:
                Fan(5, 50f, 9f, "bullet_low");
                anim?.Trigger("cast", 1.4f, 0.3f);
                break;
            case EnemyKind.Archer:
                // A fast arrow at chest height after a longer, visible aim.
                Shoot(TargetDir(), 19f, "bullet_arrow", 0.1f);
                anim?.Trigger("shoot", 1.4f, 0.3f);
                break;
            case EnemyKind.Bomber:
            {
                // Lob at where the player is heading, a little ahead.
                Vector3 lead = target && !target.Stealthed ? target.transform.position + Vector3.ProjectOnPlane(target.Velocity, Vector3.up) * 0.5f : TargetPos;
                Vector3 hand = held ? held.position : transform.position + Vector3.up * 1.4f;
                EnemyBomb.Throw(hand, lead, 1.0f, 2.2f);
                if (held) { held.gameObject.SetActive(false); Invoke(nameof(ShowHeld), 0.9f); }
                break;
            }
            default:
                Shoot(TargetDir(), 11f, "bullet_enemy", 0.16f);
                anim?.Trigger("shoot", 1.4f, 0.3f);
                break;
        }
        float gap = kind == EnemyKind.Spreader ? Random.Range(2.4f, 3.2f)
            : kind == EnemyKind.Bomber ? Random.Range(2.8f, 3.6f)
            : kind == EnemyKind.Archer ? Random.Range(2.2f, 3.0f)
            : Random.Range(1.6f, 2.6f);
        shootTimer = gap * fireMul;
    }

    void ShowHeld()
    {
        if (held && !dead) held.gameObject.SetActive(true);
    }

    static readonly Collider[] nearBuf = new Collider[16]; // shared: every enemy steers every frame, no allocations

    /// Separation from other enemies plus a sidestep around obstacles ahead.
    Vector3 Steer(Vector3 move)
    {
        if (move.sqrMagnitude < 0.01f) return move;
        Vector3 sep = Vector3.zero;
        int n = Physics.OverlapSphereNonAlloc(transform.position, 1.6f, nearBuf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var other = nearBuf[i].GetComponentInParent<Enemy>();
            if (!other || other == this || other.dead) continue;
            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float d = away.magnitude;
            if (d > 0.01f) sep += away / d * (1.6f - d) / 1.6f;
        }
        Vector3 dir = move + sep * 0.8f;
        dir.y = 0f;
        Vector3 origin = transform.position + Vector3.up * 0.8f;
        if (Physics.SphereCast(origin, 0.35f, dir.normalized, out _, 1.3f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 left = Quaternion.Euler(0f, -70f, 0f) * dir, right = Quaternion.Euler(0f, 70f, 0f) * dir;
            bool leftFree = !Physics.SphereCast(origin, 0.35f, left.normalized, out _, 1.3f, Layers.WorldMask, QueryTriggerInteraction.Ignore);
            dir = leftFree ? left : right;
        }
        return Vector3.ClampMagnitude(dir, 1f);
    }

    void DrawSight(Vector3 a, Vector3 b)
    {
        if (!sight)
        {
            sight = new GameObject("sight").AddComponent<LineRenderer>();
            sight.sharedMaterial = Palette.Trail("beam");
            sight.positionCount = 2;
            sight.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        sight.enabled = true;
        sight.widthMultiplier = 0.035f;
        var c = new Color(1f, 0.2f, 0.15f, 0.5f); // steady: a pulsing line flickers
        sight.startColor = sight.endColor = c;
        sight.SetPosition(0, a);
        sight.SetPosition(1, b);
    }

    void DrawGroundLine(Vector3 a, Vector3 b)
    {
        DrawSight(new Vector3(a.x, 0.06f, a.z), new Vector3(b.x, 0.06f, b.z));
        sight.widthMultiplier = 0.6f;
    }

    void HideSight()
    {
        if (sight) sight.enabled = false;
    }

    void Face(float dt)
    {
        if (faceDir.sqrMagnitude < 0.01f) return;
        float turn = kind == EnemyKind.Boss ? 360f : 900f;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(faceDir), turn * dt);
    }
}
