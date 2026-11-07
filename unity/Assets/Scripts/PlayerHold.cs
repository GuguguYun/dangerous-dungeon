using System.Collections.Generic;
using UnityEngine;

/// Weapon grips: after the animation has posed the body, place the held weapon for its grip type
/// and pull the hands onto it with two-bone IK (Meshy rigs use Mixamo bone names). Guns rise to
/// the crosshair while firing and drop to a low-ready carry otherwise; the bow string draws back
/// with the charge.
public partial class PlayerController
{
    Dictionary<string, Transform> bones;
    float aimBlend;
    float twist;                                   // chest turned right for long guns (degrees)
    Quaternion chestSet, chestBase, neckSet, neckBase;
    GameObject offhandVisual;
    bool handsOnWeapon;
    public string ikDebugLeft = "none"; // self-test diagnostics for the left arm
    // Hand frames in each hand bone's own space, read from the bind pose: Meshy rigs have no finger
    // bones, so the hand is one bone whose fingers continue the forearm and whose palm faces the body.
    readonly Dictionary<string, (Vector3 finger, Vector3 palm, float len)> handAxes = new Dictionary<string, (Vector3, Vector3, float)>();

    (Vector3 finger, Vector3 palm, float len) HandAxes(string side)
    {
        if (handAxes.TryGetValue(side, out var cached)) return cached;
        var result = (finger: Vector3.forward, palm: Vector3.up, len: 0.08f);
        var smr = body ? body.GetComponentInChildren<SkinnedMeshRenderer>() : null;
        Transform hand = Bone(side + "Hand"), fore = Bone(side + "ForeArm"), hips = Bone("Hips"), head = Bone("Head");
        if (smr && hand && fore && hips && head)
        {
            int hi = System.Array.IndexOf(smr.bones, hand), fi = System.Array.IndexOf(smr.bones, fore);
            int pi = System.Array.IndexOf(smr.bones, hips), di = System.Array.IndexOf(smr.bones, head);
            var bp = smr.sharedMesh.bindposes;
            if (hi >= 0 && fi >= 0 && pi >= 0 && di >= 0)
            {
                // Inverse bind pose = the bone's frame in the mesh's space at bind time.
                Matrix4x4 h = bp[hi].inverse, f = bp[fi].inverse, p = bp[pi].inverse, d = bp[di].inverse;
                Vector3 handPos = h.GetColumn(3), forePos = f.GetColumn(3);
                Vector3 up = ((Vector3)d.GetColumn(3) - (Vector3)p.GetColumn(3)).normalized;
                Vector3 finger = (handPos - forePos).normalized;
                Vector3 inward = Vector3.ProjectOnPlane((Vector3)p.GetColumn(3) - handPos, up);
                Vector3 palm = Vector3.ProjectOnPlane(inward, finger).normalized;
                Quaternion rot = Quaternion.LookRotation(h.GetColumn(2), h.GetColumn(1));
                // Hand length: about half the forearm on these chibi rigs, measured in world units.
                float worldFore = Vector3.Distance(hand.position, fore.position);
                result = (Quaternion.Inverse(rot) * finger, Quaternion.Inverse(rot) * palm, worldFore * 0.45f);
            }
        }
        handAxes[side] = result;
        return result;
    }

    /// Turn a hand so its fingers and palm point the given world directions.
    void OrientHand(string side, Vector3 finger, Vector3 palm)
    {
        var c = Bone(side + "Hand");
        if (!c) return;
        var ax = HandAxes(side);
        c.rotation = Quaternion.LookRotation(finger, palm) * Quaternion.Inverse(Quaternion.LookRotation(ax.finger, ax.palm));
    }

    /// The thumb side of a hand, in hand space: a held blade or staff points this way.
    Vector3 ThumbLocal(string side)
    {
        var ax = HandAxes(side);
        Vector3 t = Vector3.Cross(ax.palm, ax.finger);
        return side == "Right" ? t : -t;
    }

    /// Wrist position that puts the middle of the palm on a grip point.
    Vector3 WristFor(string side, Vector3 grip, Vector3 finger, Vector3 palm) =>
        grip - finger * HandAxes(side).len * 0.5f - palm * 0.02f;

    /// Put a melee weapon in a hand's palm, blade out of the thumb side (pose independent).
    void SeatInHand(Transform pivot, string side)
    {
        var hand = Bone(side + "Hand");
        var ax = HandAxes(side);
        float s = Mathf.Max(1e-5f, hand.lossyScale.x);
        pivot.SetParent(hand, false);
        pivot.localPosition = (ax.finger * ax.len * 0.5f + ax.palm * 0.02f) / s;
        pivot.localRotation = Quaternion.LookRotation(ThumbLocal(side), ax.finger);
        pivot.localScale = Vector3.one / s;
    }

    Transform Bone(string name)
    {
        if (bones == null)
        {
            bones = new Dictionary<string, Transform>();
            if (body) foreach (var t in body.GetComponentsInChildren<Transform>()) bones[t.name] = t;
        }
        return bones.TryGetValue(name, out var b) ? b : null;
    }

    /// Re-attach the weapon for the current grip (called when the weapon changes).
    void SetupGrip()
    {
        if (offhandVisual) Destroy(offhandVisual);
        handsOnWeapon = body && Bone("RightHand") && Bone("LeftHand") && Bone("RightArm") && Bone("LeftArm");
        if (!handsOnWeapon) return;
        var w = Weapon;
        if (w.grip == WeaponGrip.TwoHandMelee || w.grip == WeaponGrip.Dual)
        {
            // Melee sits in the right palm and follows the animated hand (the slash clip swings it).
            SeatInHand(weaponPivot, "Right");
            weaponRest = weaponPivot.localRotation;
            if (w.grip == WeaponGrip.Dual)
            {
                var off = new GameObject("offhand").transform;
                SeatInHand(off, "Left");
                Weapons.MakeVisual(w, off, Layers.Player);
                offhandVisual = off.gameObject;
            }
        }
        else
        {
            // Ranged weapons are placed in front of the chest and the hands follow them.
            weaponPivot.SetParent(model, false);
            weaponPivot.localScale = Vector3.one;
            weaponRest = Quaternion.identity;
        }
    }

    float WeaponLength()
    {
        float len = 0.6f;
        foreach (var r in weaponVisual ? weaponVisual.GetComponentsInChildren<Renderer>() : new Renderer[0])
            len = Mathf.Max(len, r.bounds.size.magnitude * 0.8f);
        return Mathf.Clamp(len, 0.4f, 1.8f);
    }

    void PoseWeapon(float dt)
    {
        if (!handsOnWeapon || dead) return;
        var w = Weapon;
        bool aiming = Time.time - lastShot < 0.7f || charge > 0f || (w.kind == WeaponKind.Beam && testTrigger);
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, dt * 8f);
        // Long guns: blade the stance like a right-handed shooter. The chest turns right (the head
        // turns back to the front), so the gun sits on the right side where the over-the-shoulder
        // camera sees it and the left shoulder comes forward enough for the short arm to reach the barrel.
        bool longGun = w.grip == WeaponGrip.TwoHand || w.grip == WeaponGrip.Shoulder;
        twist = Mathf.MoveTowards(twist, longGun ? 28f : 0f, dt * 140f);
        TwistChest(twist);
        // Anchor everything on the shoulders: spine bones sit at different heights per rig, and
        // the chibi arms are short, so only shoulder-relative targets stay within reach.
        Transform rs = Bone("RightArm"), ls = Bone("LeftArm");
        Vector3 fwd = model.forward, right = model.right, up = Vector3.up;
        Vector3 mid = (rs.position + ls.position) * 0.5f;
        Vector3 aimDir = (AimPoint() - mid).normalized;
        Vector3 lowReady = (fwd - up * 0.45f).normalized;

        switch (w.grip)
        {
            case WeaponGrip.TwoHandMelee:
            case WeaponGrip.Dual:
                // The right hand carries the blade; the left hand closes on the hilt for two-handers.
                if (w.grip == WeaponGrip.TwoHandMelee)
                {
                    // The left hand closes on the hilt just below the right, wrapped the same way round.
                    Vector3 hilt = weaponPivot.position - weaponPivot.forward * HandAxes("Right").len * 0.9f;
                    Vector3 lf = Bone("RightHand").rotation * HandAxes("Right").finger;
                    Vector3 lp = -(Bone("RightHand").rotation * HandAxes("Right").palm);
                    Reach("Left", WristFor("Left", hilt, lf, lp), ls.position - right * 0.3f - up * 0.3f - fwd * 0.4f);
                    OrientHand("Left", lf, lp);
                }
                return;
            case WeaponGrip.Bow:
            {
                Vector3 dir = Vector3.Slerp(lowReady, aimDir, Mathf.Max(aimBlend, 0.35f));
                Vector3 grip = ls.position + dir * ArmLength("Left") * 0.85f;
                weaponPivot.position = grip;
                weaponPivot.rotation = Quaternion.LookRotation(dir, up);
                // Left fist round the riser: fingers across it towards the body, palm facing forward.
                Vector3 bowRight = Vector3.Cross(Vector3.up, dir).normalized;
                Reach("Left", WristFor("Left", grip, bowRight, dir), ls.position - right * 0.4f - up * 0.3f - fwd * 0.4f);
                OrientHand("Left", bowRight, dir);
                // Draw the string back towards the cheek as the charge builds; fingers hook the string.
                Vector3 nock = Vector3.Lerp(grip, rs.position + up * 0.05f, 0.25f + 0.6f * charge);
                Reach("Right", nock - dir * HandAxes("Right").len * 0.4f, rs.position + right * 0.4f - up * 0.3f - fwd * 0.4f);
                OrientHand("Right", dir, -bowRight);
                return;
            }
            case WeaponGrip.Staff:
            {
                // Upright at the right side, leaning towards the target when casting; the head stays clear of the face.
                Vector3 dir = Vector3.Slerp((fwd * 0.25f + up + right * 0.12f).normalized, (aimDir * 0.5f + up * 0.8f + right * 0.15f).normalized, aimBlend);
                Vector3 grip = rs.position + right * 0.1f + fwd * ArmLength("Right") * (0.45f + 0.2f * aimBlend) - up * 0.15f;
                weaponPivot.position = grip;
                weaponPivot.rotation = Quaternion.LookRotation(dir, -fwd);
                // Fist round the shaft: fingers across it towards the body, palm against it.
                Vector3 across = Vector3.ProjectOnPlane(-right, dir).normalized;
                Vector3 palmTo = Vector3.Cross(dir, across);
                Reach("Right", WristFor("Right", grip, across, palmTo), rs.position + right * 0.4f - up * 0.4f - fwd * 0.4f);
                OrientHand("Right", across, palmTo);
                return;
            }
            default:
            {
                bool shoulder = w.grip == WeaponGrip.Shoulder, oneHand = w.grip == WeaponGrip.OneHand;
                Vector3 dir = Vector3.Slerp(lowReady, aimDir, aimBlend);
                float arm = ArmLength("Right");
                // Two-handed guns sit near the body's midline: chibi shoulders are narrow and arms short
                // (~0.36 m), so a gun held off the right shoulder leaves the left hand out of reach.
                // Two-handers: grip just in front of the (turned-back) right shoulder; the bladed chest brings
                // the left shoulder forward to the barrel. Launchers ride on top of the right shoulder,
                // outboard of the head so they don't block the camera.
                Vector3 flatAim = Vector3.ProjectOnPlane(dir, up).normalized;
                Vector3 anchor = oneHand ? rs.position + dir * arm * 0.9f
                    : shoulder ? rs.position + up * 0.06f + right * 0.1f
                    : rs.position + flatAim * arm * 0.5f - up * 0.1f - right * 0.04f;
                weaponPivot.position = anchor;
                weaponPivot.rotation = Quaternion.LookRotation(dir, up);
                Vector3 wf = weaponPivot.forward, wu = weaponPivot.up, wr = weaponPivot.right;
                // Right fist round the grip under the gun: fingers forward and curling in, palm against
                // its right side. Left hand under the barrel: palm up, fingers wrapping round to the right.
                Vector3 rFinger = (wf - wu * 0.6f + -wr * 0.25f).normalized, rPalm = -wr;
                Vector3 hand = shoulder ? anchor - up * 0.12f + fwd * 0.12f : anchor - wu * 0.06f;
                Reach("Right", WristFor("Right", hand, rFinger, rPalm), rs.position + right * 0.4f - up * 0.4f - fwd * 0.4f);
                OrientHand("Right", rFinger, rPalm);
                if (!oneHand)
                {
                    Vector3 lFinger = (wr + wf * 0.5f).normalized, lPalm = wu;
                    Vector3 fore = Foregrip(anchor, shoulder) - wu * 0.03f;
                    Reach("Left", WristFor("Left", fore, lFinger, lPalm), ls.position - right * 0.4f - up * 0.4f - fwd * 0.4f);
                    OrientHand("Left", lFinger, lPalm);
                }
                return;
            }
        }
    }

    /// Turn the chest about the vertical and the neck back by the same amount (after the animation pose).
    void TwistChest(float degrees)
    {
        Transform chest = Bone("Spine"), neck = Bone("neck");
        if (!chest) return;
        // If a clip doesn't key these bones, undo last frame's twist instead of stacking it.
        if (chest.localRotation == chestSet) chest.localRotation = chestBase;
        if (neck && neck.localRotation == neckSet) neck.localRotation = neckBase;
        chestBase = chest.localRotation;
        if (neck) neckBase = neck.localRotation;
        if (degrees < 0.1f) { chestSet = neckSet = new Quaternion(0f, 0f, 0f, 0f); return; }
        chest.rotation = Quaternion.AngleAxis(degrees, Vector3.up) * chest.rotation;
        if (neck) neck.rotation = Quaternion.AngleAxis(-degrees, Vector3.up) * neck.rotation;
        chestSet = chest.localRotation;
        if (neck) neckSet = neck.localRotation;
    }

    float ArmLength(string side)
    {
        Transform a = Bone(side + "Arm"), b = Bone(side + "ForeArm"), c = Bone(side + "Hand");
        return a && b && c ? Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, c.position) : 0.4f;
    }

    /// Where the left hand supports the gun: as far along the barrel as the (short, chibi) arm reaches.
    public Vector3 Foregrip(Vector3 anchor, bool shoulder)
    {
        Transform la = Bone("LeftArm"), lf = Bone("LeftForeArm"), lh = Bone("LeftHand");
        float reach = la && lf && lh ? (Vector3.Distance(la.position, lf.position) + Vector3.Distance(lf.position, lh.position)) * 0.95f : 0.5f;
        float want = WeaponLength() * (shoulder ? 0.25f : 0.38f);
        Vector3 down = -weaponPivot.up * 0.05f;
        for (float t = want; t > 0.08f; t -= 0.03f)
        {
            Vector3 p = anchor + weaponPivot.forward * t + down;
            if (!la || Vector3.Distance(la.position, p) <= reach) return p;
        }
        return anchor + weaponPivot.forward * 0.08f + down;
    }

    public Transform BoneForTest(string name) => Bone(name);
    public Transform WeaponPivotForTest => weaponPivot;

    /// Two-bone IK for one arm: Holden's analytic solver in the arm's current bend plane (exact from
    /// any starting pose), then a twist about the shoulder-to-target axis so the elbow points at the pole.
    void Reach(string side, Vector3 target, Vector3 pole)
    {
        Transform a = Bone(side + "Arm"), b = Bone(side + "ForeArm"), c = Bone(side + "Hand");
        if (!a || !b || !c)
        {
            if (side == "Left") ikDebugLeft = $"missing bones a={(bool)a} b={(bool)b} c={(bool)c} frame={Time.frameCount}";
            return;
        }
        const float eps = 0.001f;
        Vector3 ap = a.position, bp = b.position, cp = c.position;
        float lab = (bp - ap).magnitude, lcb = (bp - cp).magnitude;
        float lat = Mathf.Clamp((target - ap).magnitude, eps, lab + lcb - eps);
        float acab0 = Angle(cp - ap, bp - ap), babc0 = Angle(ap - bp, cp - bp), acat0 = Angle(cp - ap, target - ap);
        float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
        // Bend in the plane the arm is already bent in; only a straight arm needs the pole for it.
        Vector3 axis0 = Vector3.Cross(cp - ap, bp - ap);
        if (axis0.sqrMagnitude < 1e-6f) axis0 = Vector3.Cross(cp - ap, pole - ap);
        if (axis0.sqrMagnitude < 1e-6f) axis0 = Vector3.Cross(cp - ap, model.forward);
        axis0 = axis0.normalized;
        Vector3 axis1 = Vector3.Cross(cp - ap, target - ap);
        Quaternion agr = a.rotation, bgr = b.rotation;
        Quaternion r0 = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, Quaternion.Inverse(agr) * axis0);
        Quaternion r1 = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, Quaternion.Inverse(bgr) * axis0);
        Quaternion r2 = axis1.sqrMagnitude > 1e-8f ? Quaternion.AngleAxis(acat0 * Mathf.Rad2Deg, Quaternion.Inverse(agr) * axis1.normalized) : Quaternion.identity;
        a.localRotation = a.localRotation * (r0 * r2);
        b.localRotation = b.localRotation * r1;

        // Swing the whole arm about the shoulder-target line so the elbow faces the pole (the hand stays put).
        Vector3 along = (c.position - a.position).normalized;
        Vector3 elbowDir = Vector3.ProjectOnPlane(b.position - a.position, along);
        Vector3 poleDir = Vector3.ProjectOnPlane(pole - a.position, along);
        if (elbowDir.sqrMagnitude > 1e-6f && poleDir.sqrMagnitude > 1e-6f)
            a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(elbowDir, poleDir, along), along) * a.rotation;

        if (side == "Left")
            ikDebugLeft = $"frame={Time.frameCount} reach={lab + lcb:F2} toTarget={(target - ap).magnitude:F2} after={(c.position - target).magnitude:F2} acab0={acab0:F2} babc0={babc0:F2}";
    }

    static float Angle(Vector3 x, Vector3 y) =>
        Mathf.Acos(Mathf.Clamp(Vector3.Dot(x.normalized, y.normalized), -1f, 1f));
}
