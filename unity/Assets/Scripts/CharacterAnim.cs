using System.Collections.Generic;
using UnityEngine;

/// Drives a Meshy-rigged character with the legacy Animation component.
/// Expects Resources/Models/Chars/<id>/rigged (mesh + skeleton) and any of the clip files
/// actions / walking / running beside it; BuildScript imports them as legacy clips named by
/// clips.txt (idle, run, shoot, hit, die, ...).
public class CharacterAnim : MonoBehaviour
{
    static readonly string[] ClipFiles = { "actions", "walking", "running" };
    static readonly HashSet<string> Looping = new HashSet<string> { "idle", "walk", "run", "shoot" };

    Animation anim;
    Transform hips;
    Vector3 hipsRest;
    // Robed heroes run with half the knee bend, so the heel doesn't kick up through the back of the robe.
    Transform[] knees;
    Quaternion[] kneeRest;
    float kneeBend = 1f;
    string base_ = "idle";
    string oneShot;
    float oneShotUntil;

    /// Instantiates the rigged model under parent, or returns null when the character has no model yet.
    /// Target standing heights in metres (Meshy's rig height is only approximate).
    static readonly Dictionary<string, float> Heights = new Dictionary<string, float>
    {
        { "hero", 1.5f }, { "gunner", 1.3f }, { "charger", 1.7f }, { "spreader", 1.3f }, { "boss", 3.0f },
        { "hero_hq", 1.5f }, { "boss_hq", 3.0f }, { "goblin_king", 2.4f }, { "boar_chief", 3.0f },
        { "assassin", 1.55f }, { "mage", 1.5f }, { "priest", 1.45f },
        { "gunner_hq", 1.3f }, { "charger_hq", 1.7f }, { "spreader_hq", 1.3f },
        { "light_mech", 2.1f }, { "heavy_mech", 2.5f },
        { "merchant", 1.3f }, { "bomber", 1.3f }, { "archer", 1.45f }, { "shield_goblin", 1.5f }, { "lich", 2.8f },
    };

    public static CharacterAnim Spawn(string id, Transform parent, int layer)
    {
        var prefab = Resources.Load<GameObject>($"Models/Chars/{id}/rigged");
        if (!prefab) return null;
        var go = Instantiate(prefab, parent, false);
        go.name = id;
        if (Heights.TryGetValue(id, out float want))
        {
            var b = new Bounds();
            bool first = true;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            if (b.size.y > 0.01f) go.transform.localScale *= want / b.size.y;
        }
        // Long robes re-weighted at build time so running doesn't tear them open (Editor/RobeFix).
        var robe = Resources.Load<Mesh>($"Models/Chars/{id}/robe");
        if (robe)
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (smr.sharedMesh && smr.sharedMesh.vertexCount == robe.vertexCount) smr.sharedMesh = robe;
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = layer;
        foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
        FaceForward(go.transform, parent);
        var ca = go.AddComponent<CharacterAnim>();
        ca.anim = go.GetComponent<Animation>();
        if (!ca.anim) ca.anim = go.AddComponent<Animation>(); // Unity objects must not use ??
        foreach (var file in ClipFiles)
        {
            foreach (var clip in Resources.LoadAll<AnimationClip>($"Models/Chars/{id}/{file}"))
            {
                if (clip.name.StartsWith("__preview__") || !clip.legacy || ca.anim.GetClip(clip.name)) continue;
                ca.anim.AddClip(clip, clip.name);
                var state = ca.anim[clip.name];
                state.wrapMode = Looping.Contains(clip.name) ? WrapMode.Loop : WrapMode.ClampForever;
            }
        }
        ca.anim.playAutomatically = false;
        ca.hips = ca.Find("hips");
        if (robe)
        {
            ca.knees = System.Array.FindAll(go.GetComponentsInChildren<Transform>(), t => t.name == "LeftLeg" || t.name == "RightLeg");
            ca.kneeRest = System.Array.ConvertAll(ca.knees, t => t.localRotation); // straight, as modelled
            ca.kneeBend = 0.5f;
        }
        if (ca.hips) ca.hipsRest = ca.transform.InverseTransformPoint(ca.hips.position);
        ca.Play("idle");
        return ca;
    }

    /// Turns the model so its toes point along the parent's +Z (rigs differ in which way they face).
    static void FaceForward(Transform model, Transform parent)
    {
        Vector3 sum = Vector3.zero;
        foreach (var t in model.GetComponentsInChildren<Transform>())
        {
            if (t.name.ToLower().Contains("toe") && t.parent)
                sum += parent.InverseTransformDirection(t.position - t.parent.position);
        }
        sum.y = 0f;
        if (sum.sqrMagnitude < 1e-6f) return;
        // Rigs face along an axis; the toes splay a few degrees, so snap to the nearest quarter turn.
        float yaw = Mathf.Round(Vector3.SignedAngle(sum, Vector3.forward, Vector3.up) / 90f) * 90f;
        model.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.localRotation;
        Debug.Log($"CHAR {model.name} turned {yaw:F0} deg to face forward");
    }

    /// The bone a held weapon should follow, if the rig has one.
    public Transform Find(string part)
    {
        foreach (var t in GetComponentsInChildren<Transform>())
            if (t.name.ToLower().Replace("_", "").Contains(part)) return t;
        return null;
    }

    public bool Has(string clip) => anim && anim.GetClip(clip) != null;

    public float Length(string clip) => Has(clip) ? anim[clip].length : 0f;

    /// The looping state when nothing else plays: idle, walk or run.
    public void SetBase(string clip, float speed = 1f)
    {
        if (!Has(clip)) clip = Has("run") ? "run" : "idle";
        if (Has(clip)) anim[clip].speed = speed;
        if (clip == base_ && (oneShot != null || anim.IsPlaying(clip))) return;
        base_ = clip;
        if (oneShot == null) Play(clip);
    }

    /// Plays a clip once, then falls back to the base state. lockFor holds it at least that long.
    public void Trigger(string clip, float speed = 1f, float lockFor = 0f)
    {
        if (!Has(clip)) return;
        oneShot = clip;
        anim[clip].speed = speed;
        anim[clip].time = 0f;
        anim.CrossFade(clip, 0.08f);
        oneShotUntil = Time.time + Mathf.Max(lockFor, anim[clip].length / Mathf.Max(0.01f, speed) * 0.9f);
    }

    public void Play(string clip)
    {
        if (Has(clip)) anim.CrossFade(clip, 0.15f);
    }

    /// Library clips may carry the hips forward (rolls, run-and-shoot); the controller moves the
    /// character, so pin the hips' horizontal position and keep only the vertical bob.
    void LateUpdate()
    {
        if (knees != null)
            for (int i = 0; i < knees.Length; i++)
                if (knees[i]) knees[i].localRotation = Quaternion.Slerp(kneeRest[i], knees[i].localRotation, kneeBend);
        if (!hips) return;
        // Work in the model's own space: the rig's parent bones may be rotated (Z-up armatures).
        var p = transform.InverseTransformPoint(hips.position);
        hips.position = transform.TransformPoint(new Vector3(hipsRest.x, p.y, hipsRest.z));
    }

    void Update()
    {
        if (oneShot != null && Time.time >= oneShotUntil && oneShot != "die")
        {
            oneShot = null;
            Play(base_);
        }
    }
}
