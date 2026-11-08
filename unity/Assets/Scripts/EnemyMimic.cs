using UnityEngine;

/// The mimic (宝箱怪): a chest-room chest that wakes when you come close. It hops after you with red
/// eyes and a lolling tongue, winds up (red line on the floor) and lunges to bite, then sits still a
/// moment. Beaten, it gives a rare-or-better weapon and gold: a better chest than a real one.
public partial class Enemy
{
    float hopT;

    /// The chest model with eyes and a tongue (no rig: the whole body hops and leans).
    void BuildMimicVisual()
    {
        var chest = Props.Spawn("chest", model.position, model.rotation, model, false);
        foreach (var t in chest.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Enemy;
        body = chest.GetComponentInChildren<Renderer>();
        cc.height = 1.0f;
        cc.radius = 0.6f;
        cc.center = new Vector3(0f, 0.5f, 0f);
        // Eyes peeking out under the lid, a tongue hanging over the front edge.
        for (int side = -1; side <= 1; side += 2)
            Part(PrimitiveType.Sphere, new Vector3(0.2f * side, 0.62f, 0.5f), Vector3.one * 0.13f, "enemy_eye");
        var tongue = Part(PrimitiveType.Capsule, new Vector3(0f, 0.42f, 0.55f), new Vector3(0.22f, 0.16f, 0.08f), "pickup_health");
        tongue.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
        eye = new GameObject("eye").transform;
        eye.SetParent(model, false);
        eye.localPosition = new Vector3(0f, 0.6f, 0.6f);
    }

    /// Turn to face a direction (screenshots of an enemy without a target).
    public void TurnTo(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f) faceDir = dir.normalized;
    }

    Vector3 MimicThink(Vector3 to, float dist, float dt)
    {
        Vector3 toDir = dist > 0.01f ? to / dist : transform.forward;
        aiT -= dt;
        lungeCd -= dt;
        switch (ai)
        {
            case Ai.Move:
            {
                faceDir = toDir;
                hopT += dt;
                // Hops: the body bobs and moves in bursts, like something heavy jumping.
                float hop = Mathf.Abs(Mathf.Sin(hopT * 7f));
                model.localPosition = Vector3.up * hop * 0.28f;
                if (dist < 4.5f && lungeCd <= 0f && CanSeeTarget())
                {
                    ai = Ai.Windup;
                    aiT = 0.45f;
                    windups++;
                    Sfx.Play("chest", 0.6f, 0.2f, 0.7f);
                }
                return Steer(toDir) * (0.4f + hop);
            }
            case Ai.Windup:
                if (aiT > 0.15f) lungeDir = toDir;
                faceDir = lungeDir;
                // Rattles in place before it jumps.
                model.localPosition = new Vector3(Mathf.Sin(Time.time * 60f) * 0.04f, 0f, 0f);
                DrawGroundLine(transform.position, transform.position + lungeDir * 4.5f);
                if (aiT <= 0f)
                {
                    HideSight();
                    ai = Ai.Lunge;
                    aiT = 0.35f;
                    lunges++;
                    Sfx.Play("slash", 0.5f, 0.2f, 0.8f);
                }
                return Vector3.zero;
            case Ai.Lunge:
                faceDir = lungeDir;
                model.localPosition = Vector3.up * Mathf.Sin((0.35f - aiT) / 0.35f * Mathf.PI) * 0.6f;
                if (aiT <= 0f)
                {
                    ai = Ai.Recover;
                    aiT = 0.7f;
                    lungeCd = 1.4f;
                    model.localPosition = Vector3.zero;
                    Vfx.Puff(transform.position + Vector3.up * 0.1f, new Color(0.55f, 0.47f, 0.38f, 0.6f), 0.8f, 6);
                }
                return lungeDir * (12f / speed);
            default: // Recover: it sits, lid ajar, open to a beating
                faceDir = toDir;
                model.localPosition = Vector3.zero;
                if (aiT <= 0f) ai = Ai.Move;
                return Vector3.zero;
        }
    }
}
