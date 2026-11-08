using UnityEngine;

/// The splitting slime (分裂史莱姆), after Enter the Gungeon's blobulons: a blob of goo that hops after
/// you. Each hop is its attack: it squats, springs, and only hurts while in the air, so a step aside is
/// enough. A big one killed bursts into two small quick ones; a small one just splats. Weapons that hit
/// a crowd (explosions, chain lightning) are what it fears.
public partial class Enemy
{
    public System.Action<EnemyKind, Vector3> SplitNow; // the room drops the halves in at once (no warning ring)
    Vector3 slimeScale;
    float squat;

    /// The Meshy blob (no rig: squash and stretch in code).
    void BuildSlimeVisual()
    {
        if (Props.Has("slime"))
        {
            var blob = Props.Spawn("slime", model.position, model.rotation, model, false);
            blob.transform.localScale *= 1.2f; // a big one about 1 m tall: its halves read as clearly smaller
            foreach (var t in blob.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Enemy;
            body = blob.GetComponentInChildren<Renderer>();
        }
        else body = Part(PrimitiveType.Sphere, new Vector3(0f, 0.4f, 0f), new Vector3(1f, 0.8f, 1f), "enemy_body");
        cc.height = 1.0f;
        cc.radius = 0.6f;
        cc.center = new Vector3(0f, 0.5f, 0f);
        eye = new GameObject("eye").transform;
        eye.SetParent(model, false);
        eye.localPosition = new Vector3(0f, 0.6f, 0.6f);
    }

    Vector3 SlimeThink(Vector3 to, float dist, float dt)
    {
        // Taken here rather than at build time: an elite or a split half has been resized since.
        if (slimeScale == Vector3.zero) slimeScale = model.localScale;
        Vector3 toDir = dist > 0.01f ? to / dist : transform.forward;
        float sit = mini ? 0.3f : 0.45f, air = mini ? 0.32f : 0.45f;
        aiT -= dt;
        faceDir = toDir;
        if (ai == Ai.Lunge)
        {
            // In the air: an arc toward where you were at take-off; this is the part that hurts.
            float k = 1f - aiT / air;
            model.localPosition = Vector3.up * Mathf.Sin(k * Mathf.PI) * (mini ? 0.45f : 0.7f);
            squat = Mathf.MoveTowards(squat, -0.25f, dt * 4f); // stretched tall in flight
            ShapeSlime();
            if (aiT > 0f) return lungeDir * 1.7f;
            ai = Ai.Move;
            aiT = sit;
            squat = 0.3f; // lands flat
            model.localPosition = Vector3.zero;
            Vfx.Puff(transform.position + Vector3.up * 0.1f, new Color(0.45f, 0.8f, 0.25f, 0.55f), mini ? 0.45f : 0.75f, 5);
            return Vector3.zero;
        }
        // On the ground: settle from the landing, then squat lower and lower before the next spring.
        float wind = Mathf.Clamp01(1f - aiT / sit);
        squat = Mathf.Lerp(squat, 0.12f + 0.18f * wind * wind, dt * 10f);
        ShapeSlime();
        if (aiT <= 0f && dist < 22f)
        {
            ai = Ai.Lunge;
            aiT = air;
            lungeDir = dist > 0.6f ? toDir : Vector3.zero;
            lunges++;
            Sfx.Play("step", 0.35f, 0.15f, mini ? 1.2f : 0.75f);
        }
        return Vector3.zero;
    }

    /// Squash (squat > 0) or stretch (< 0), keeping the volume about the same.
    void ShapeSlime()
    {
        float y = 1f - squat, xz = 1f / Mathf.Sqrt(Mathf.Max(0.3f, y));
        model.localScale = new Vector3(slimeScale.x * xz, slimeScale.y * y, slimeScale.z * xz);
    }

    /// A big slime bursts into two small ones (a splitting elite's own halves already cover that).
    void SlimeDeath()
    {
        if (kind != EnemyKind.Slime) return;
        Vfx.Puff(transform.position + Vector3.up * 0.3f, new Color(0.45f, 0.85f, 0.25f, 0.7f), mini ? 0.8f : 1.4f, mini ? 8 : 14);
        if (mini || elite == EliteKind.Splitting || SplitNow == null) return;
        Vector3 side = Vector3.Cross(Vector3.up, faceDir).normalized;
        SplitNow(kind, transform.position + side * 0.8f);
        SplitNow(kind, transform.position - side * 0.8f);
    }
}
