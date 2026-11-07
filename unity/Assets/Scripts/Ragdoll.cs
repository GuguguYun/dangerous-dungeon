using System.Collections.Generic;
using UnityEngine;

/// Turns a Meshy-rigged character (Mixamo-style bone names) into a PhysX ragdoll on death:
/// 11 bodies joined with CharacterJoints, on the Debris layer so corpses only hit the world.
public static class Ragdoll
{
    // bone, parent body, bone the capsule reaches towards
    static readonly (string bone, string parent, string tip)[] Parts =
    {
        ("Hips", null, "Spine01"),
        ("Spine01", "Hips", "neck"),
        ("Head", "Spine01", "head_end"),
        ("LeftArm", "Spine01", "LeftForeArm"),
        ("LeftForeArm", "LeftArm", "LeftHand"),
        ("RightArm", "Spine01", "RightForeArm"),
        ("RightForeArm", "RightArm", "RightHand"),
        ("LeftUpLeg", "Hips", "LeftLeg"),
        ("LeftLeg", "LeftUpLeg", "LeftFoot"),
        ("RightUpLeg", "Hips", "RightLeg"),
        ("RightLeg", "RightUpLeg", "RightFoot"),
    };

    public static List<Rigidbody> Activate(Transform model, Vector3 impulse, float mass)
    {
        var bones = new Dictionary<string, Transform>();
        foreach (var t in model.GetComponentsInChildren<Transform>()) bones[t.name] = t;
        if (!bones.ContainsKey("Hips") || !bones.ContainsKey("Spine01")) return null;

        foreach (var a in model.GetComponentsInChildren<Animation>()) a.enabled = false;
        foreach (var c in model.GetComponentsInChildren<CharacterAnim>()) c.enabled = false;

        var bodies = new Dictionary<string, Rigidbody>();
        foreach (var (bone, parent, tip) in Parts)
        {
            if (!bones.TryGetValue(bone, out var b) || !bones.TryGetValue(tip, out var t)) continue;
            b.gameObject.layer = Layers.Debris;
            var rb = b.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass / Parts.Length;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            // Capsule from this bone towards the next one, sized in world units then mapped back.
            Vector3 local = b.InverseTransformPoint(t.position);
            float lossy = Mathf.Max(1e-4f, b.lossyScale.x);
            float worldLen = Vector3.Distance(b.position, t.position);
            if (bone == "Head")
            {
                var s = b.gameObject.AddComponent<SphereCollider>();
                s.center = local * 0.5f;
                s.radius = Mathf.Max(0.08f, worldLen * 0.45f) / lossy;
            }
            else
            {
                var cap = b.gameObject.AddComponent<CapsuleCollider>();
                int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                cap.direction = axis;
                cap.center = local * 0.5f;
                float r = Mathf.Clamp(worldLen * (bone == "Hips" || bone == "Spine01" ? 0.45f : 0.22f), 0.05f, 0.6f);
                cap.radius = r / lossy;
                cap.height = Mathf.Max(local.magnitude, cap.radius * 2.2f);
            }
            bodies[bone] = rb;
            if (parent != null && bodies.TryGetValue(parent, out var pr))
            {
                var j = b.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = pr;
                j.enableProjection = true;
                var low = j.lowTwistLimit; low.limit = -30f; j.lowTwistLimit = low;
                var high = j.highTwistLimit; high.limit = 40f; j.highTwistLimit = high;
                var s1 = j.swing1Limit; s1.limit = 50f; j.swing1Limit = s1;
                var s2 = j.swing2Limit; s2.limit = 30f; j.swing2Limit = s2;
            }
        }
        foreach (var rb in bodies.Values) rb.AddForce(impulse, ForceMode.VelocityChange);
        if (bodies.TryGetValue("Spine01", out var chest)) chest.AddForce(impulse * 0.5f, ForceMode.VelocityChange);
        return new List<Rigidbody>(bodies.Values);
    }

    public static void Freeze(List<Rigidbody> bodies)
    {
        if (bodies == null) return;
        foreach (var rb in bodies)
        {
            if (!rb) continue;
            rb.isKinematic = true;
            foreach (var c in rb.GetComponents<Collider>()) c.enabled = false;
        }
    }
}
