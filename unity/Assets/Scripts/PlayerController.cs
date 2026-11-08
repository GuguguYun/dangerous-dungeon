using UnityEngine;

/// Third-person controller: camera behind the shoulder (Naraka-style), mouse look, crosshair aim,
/// two weapon slots (guns and melee), energy, regenerating armor, dash with brief invulnerability.
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(100)] // after CharacterAnim, so the hand IK sees the final animated pose
public partial class PlayerController : MonoBehaviour
{
    public static PlayerController Current;

    float Speed = 6f; // per class
    const float Accel = 60f;
    const float DashSpeed = 16f;
    const float DashTime = 0.22f;
    const float DashCooldown = 0.3f;
    const float JumpVelocity = 7.6f; // apex ~1.45 m: chest-height orbs pass under the feet
    const float Gravity = 20f;
    const float AirControl = 0.25f;
    const float StaminaRegen = 1f;   // per second, after StaminaDelay on the ground
    const float StaminaDelay = 0.5f;
    const float PerfectCooldown = 2f;
    const float MouseSens = 2.2f;
    const float CamDistance = 5.0f;
    float ArmorDelay = 3f; // per class
    const float ArmorTick = 0.8f;
    static readonly Vector3 Shoulder = new Vector3(0.95f, 0.35f, 0f);

    public int maxHp = 6, hp = 6;
    public int maxArmor = 4, armor = 4;
    public int maxEnergy = 200, energy = 200;
    public int maxStamina = 3;
    public float stamina = 3f;
    public int perfectDodges;
    public WeaponDef[] weapons = new WeaponDef[2];
    public int slot;
    public bool dead;
    public bool controlEnabled = true;
    public bool externalCamera;        // a scripted camera (grip sheets) owns the view
    public Vector3? aimOverride;       // ... and then this is where the gun points
    public float yaw;
    public float pitch = 10f;
    public Camera cam;
    public Pickup nearbyWeapon;
    public ShopItem nearbyWare;
    public Interactable nearbyThing;
    public int damageTaken; // hp + armor lost, for the self-test
    public bool shielded, stealthed;
    public float poisoned;   // seconds of poison left
    public float onIce;      // set each frame by an ice patch underfoot
    float poisonTick;
    public bool Stealthed => stealthed;
    public PlayerSkill skill;
    // A class skill's own weapon (Weapons.SkillWeapons) sits in the current slot for a few seconds;
    // the weapon it replaced comes back after. No swapping or picking up meanwhile.
    WeaponDef skillWeaponSaved;
    float skillWeaponLeft, skillWeaponTotal;
    public float SkillWeaponLeft => skillWeaponLeft;
    public float SkillWeaponTotal => skillWeaponTotal;
    public bool HoldingSkillWeapon => skillWeaponLeft > 0f;
    /// The knight's oath shield: each hit that would hurt uses one up instead.
    public int guardCharges;
    public const int GuardMax = 3;
    float holyT;      // the priest's scepter: seconds of light on enemies towards the next armour point
    public System.Action Died;

    CharacterController cc;
    Transform model;
    Transform weaponPivot;
    GameObject weaponVisual;
    Transform muzzle;
    Renderer bodyRenderer;
    CharacterAnim body;
    string bodyId; // the pilot model's folder under Resources/Models/Chars (hand measurements live there)
    float lastShot = -9f;
    Vector3 faceDir = Vector3.forward;
    Quaternion weaponRest = Quaternion.identity;
    bool faceInit;
    MaterialPropertyBlock mpb;
    Vector3 velocity;
    Vector3 dashDir;
    float fireCd, dashT, dashCd, invuln, dashInvuln, sinceHit = 99f, armorTimer, swingT, noEnergyMsgCd;
    float dashSpeed = DashSpeed;
    float groundT, perfectCd, energyRegenAcc, hpRegenT;
    ClassDef cls;
    bool airJumped, nextCrit;
    float camDist = CamDistance;
    // special weapons
    public bool testTrigger;      // the self-test holds the trigger with this
    bool prevTrigger, boomerangOut;
    float holdT;
    Light chargeLight;
    AudioSource roar;  // the flamethrower's fire, looping while it fires
    float flamingAt = -1f;
    ParticleSystem gather; // motes drawn in to a drawn bow's arrow
    float charge, energyAcc, beamTick;
    LineRenderer beam, beamGlow;
    Light beamLight;
    float beamSparkT;
    public float Charge => charge;
    public bool BoomerangOut => boomerangOut;

    public WeaponDef Weapon => weapons[slot];
    public bool Grounded => cc && cc.isGrounded;
    public bool Dashing => dashT > 0f;
    public float FeetHeight => transform.position.y;
    public float VerticalSpeed => velocity.y;
    public Vector3 Velocity => velocity;
    public CollisionFlags LastFlags => cc.collisionFlags;

    void Awake()
    {
        Current = this;
        gameObject.layer = Layers.Player;
        cc = GetComponent<CharacterController>();
        cc.height = 1.7f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, 0.85f, 0f);
        cc.stepOffset = 0.35f;
        cc.skinWidth = 0.04f;
        cc.minMoveDistance = 0f; // at high frame rates per-frame moves are tiny; never drop them

        model = new GameObject("model").transform;
        model.SetParent(transform, false);
        weaponPivot = new GameObject("weapon").transform;
        weaponPivot.SetParent(model, false);
        weaponPivot.localPosition = new Vector3(0.42f, 1.15f, 0.2f);
        // The chosen class's Meshy model; the first-round knight if a model is missing.
        string modelId = Classes.Get(RunState.classId).model;
        if (!Resources.Load<GameObject>($"Models/Chars/{modelId}/rigged")) modelId = "hero";
        bodyId = modelId;
        body = CharacterAnim.Spawn(modelId, model, Layers.Player);
        if (body)
        {
            cc.height = 1.5f;
            cc.center = new Vector3(0f, 0.75f, 0f);
            bodyRenderer = body.GetComponentInChildren<SkinnedMeshRenderer>();
            var hand = body.Find("righthand");
            if (hand)
            {
                weaponPivot.SetParent(hand, true);
                weaponPivot.position = hand.position;
                weaponPivot.rotation = model.rotation;
                weaponRest = weaponPivot.localRotation;
            }
        }
        else
        {
            bodyRenderer = Part(PrimitiveType.Capsule, new Vector3(0f, 0.85f, 0f), new Vector3(0.8f, 0.85f, 0.8f), "player_body");
            Part(PrimitiveType.Cube, new Vector3(0f, 1.45f, 0.36f), new Vector3(0.5f, 0.16f, 0.1f), "player_visor");
        }
        muzzle = new GameObject("muzzle").transform;
        muzzle.SetParent(weaponPivot, false);
        mpb = new MaterialPropertyBlock();
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    Renderer Part(PrimitiveType type, Vector3 pos, Vector3 scale, string mat)
    {
        var go = GameObject.CreatePrimitive(type);
        DestroyImmediate(go.GetComponent<Collider>());
        go.layer = Layers.Player;
        go.transform.SetParent(model, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Palette.Get(mat);
        return r;
    }

    /// Walking into a crate or barrel pushes it.
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.rigidbody && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForceAtPosition(hit.moveDirection * 2f, hit.point, ForceMode.VelocityChange);
    }

    /// Load hp, weapons and the rest from the run.
    public void LoadRun()
    {
        maxHp = RunState.maxHp;
        hp = RunState.hp;
        maxArmor = armor = RunState.maxArmor;
        maxEnergy = RunState.maxEnergy;
        energy = RunState.energy;
        maxStamina = RunState.maxStamina;
        stamina = maxStamina;
        cls = Classes.Get(RunState.classId);
        Speed = cls.speed;
        ArmorDelay = cls.armorDelay;
        weapons[0] = Weapons.Held(RunState.weapons[0]);
        weapons[1] = RunState.weapons[1] != null ? Weapons.Held(RunState.weapons[1]) : null;
        slot = weapons[RunState.slot] != null ? RunState.slot : 0;
        RunState.SawWeapon(weapons[0]?.id);
        RunState.SawWeapon(weapons[1]?.id);
        EquipVisual();
        // A mech you were piloting through the portal comes along.
        if (RunState.mechId != null) Mount(Mechs.Get(RunState.mechId), RunState.mechHp);
    }

    void EquipVisual()
    {
        if (weaponVisual) Destroy(weaponVisual);
        weaponVisual = Weapons.MakeVisual(Weapon, weaponPivot, Layers.Player);
        // Muzzle at the front of whatever model is held (Meshy model or placeholder box).
        var b = new Bounds();
        bool any = false;
        foreach (var r in weaponVisual.GetComponentsInChildren<Renderer>())
        {
            var lb = r.localBounds;
            var m = weaponPivot.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var c = m.MultiplyPoint3x4(lb.center);
            if (!any) { b = new Bounds(c, Vector3.zero); any = true; }
            b.Encapsulate(m.MultiplyPoint3x4(lb.min));
            b.Encapsulate(m.MultiplyPoint3x4(lb.max));
        }
        muzzle.localPosition = Weapon.melee ? new Vector3(0f, 0f, 0.4f) : new Vector3(0f, b.center.y, b.max.z);
        SetupGrip();
        AttachSmear(any ? b.max.z : 0.8f);
    }

    public void Swap()
    {
        if (HoldingSkillWeapon) { Hud.I?.Show("技能武器还在手上，结束后才能换", 0.8f); return; }
        if (weapons[1 - slot] == null) return;
        slot = 1 - slot;
        fireCd = Mathf.Max(fireCd, 0.15f);
        EquipVisual();
        Sfx.Play(Weapon.melee ? "swap_melee" : "swap_gun", 0.7f, 0.05f);
    }

    public void PickUp(Pickup p)
    {
        if (HoldingSkillWeapon) { Hud.I?.Show("技能武器还在手上，结束后才能捡", 0.8f); return; }
        var old = Weapon;
        int target = weapons[1 - slot] == null ? 1 - slot : slot;
        if (target == slot)
            Pickup.Spawn(PickupKind.Weapon, transform.position + model.forward * 1.2f, 0, old);
        weapons[target] = Weapons.Held(p.weapon.id) ?? p.weapon;
        slot = target;
        RunState.SawWeapon(p.weapon.id);
        Destroy(p.gameObject);
        nearbyWeapon = null;
        EquipVisual();
        Sfx.Play("pickup_weapon", 0.8f, 0.05f);
        Hud.I?.Show($"获得 {Weapon.name}（{Weapon.type}）", 1.2f);
    }

    /// Jump: costs one stamina; a second jump in the air only with the double-jump buff.
    public bool Jump()
    {
        if (dead) return false;
        if (stamina < 1f) { Tips.Show("stamina", "体力用完了：落地 0.5 秒后开始恢复（准星下方的小格子）"); return false; }
        if (!Grounded)
        {
            if (!RunState.doubleJump || airJumped) return false;
            airJumped = true;
        }
        stamina -= 1f;
        groundT = 0f;
        velocity.y = JumpVelocity;
        Sfx.Play("step", 0.5f, 0.1f);
        return true;
    }

    /// Dash: costs one stamina, brief invulnerability to bullets (not to shockwaves).
    public bool Dash(Vector3 direction)
    {
        if (dead || dashCd > 0f) return false;
        if (stamina < 1f) { Tips.Show("stamina", "体力用完了：落地 0.5 秒后开始恢复（准星下方的小格子）"); return false; }
        stamina -= 1f;
        groundT = 0f;
        dashDir = direction;
        dashT = DashTime;
        dashSpeed = DashSpeed * (cls != null && !InMech ? cls.dashMul : 1f);
        afterimageT = 0f;
        dashCd = DashCooldown * RunState.dashCooldownMul;
        dashInvuln = DashTime + 0.05f;
        if (body) body.Trigger("dodge", body.Length("dodge") / 0.45f, 0.4f);
        return true;
    }

    /// Something dangerous just missed while we were airborne or dashing: reward it.
    public void NearMiss()
    {
        if (dead || (Grounded && !Dashing) || perfectCd > 0f) return;
        perfectCd = PerfectCooldown;
        perfectDodges++;
        RunState.perfectDodges++;
        stamina = Mathf.Min(maxStamina, stamina + 1f);
        AddEnergy(5);
        nextCrit = true;
        if (RunState.dodgeMaster) armor = Mathf.Min(maxArmor, armor + 1);
        TimeControl.SlowMo(RunState.dodgeMaster ? 0.6f : 0.35f);
        Hud.I?.PerfectDodge();
        Tips.Show("perfect", "完美闪避！擦身躲开攻击：子弹时间、退回体力、下一击必暴击");
        Sfx.Play("buff", 0.7f, 0f);
        Debug.Log("PERFECT dodge");
    }

    public void Teleport(Vector3 pos)
    {
        cc.enabled = false;
        transform.position = pos;
        cc.enabled = true;
        velocity = Vector3.zero;
    }

    public static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void Update()
    {
        FlameRoar();
        if (Time.timeScale == 0f) return;
        float dt = Time.deltaTime;
        fireCd -= dt;
        dashCd -= dt;
        invuln -= dt;
        dashInvuln -= dt;
        perfectCd -= dt;
        noEnergyMsgCd -= dt;
        sinceHit += dt;
        if (skillWeaponLeft > 0f && (skillWeaponLeft -= dt) <= 0f) EndSkillWeapon();
        bool active = controlEnabled && !dead;
        if (active) RunState.runTime += dt;

        if (Grounded && dashT <= 0f)
        {
            groundT += dt;
            airJumped = false;
            if (groundT > StaminaDelay) stamina = Mathf.Min(maxStamina, stamina + StaminaRegen * (cls != null && !InMech ? cls.staminaRegen : 1f) * dt);
        }
        else groundT = 0f;

        onIce -= dt;
        if (poisoned > 0f && !dead)
        {
            poisoned -= dt;
            poisonTick -= dt;
            if (poisonTick <= 0f)
            {
                poisonTick = 1.2f;
                float saved = invuln;
                invuln = 0f; // poison ticks through the hit invulnerability
                TakeDamage(1);
                invuln = Mathf.Max(invuln, saved);
            }
        }

        // Class passives: the mage's energy trickle, the priest's slow heal.
        if (!dead && cls != null)
        {
            if (cls.energyRegen > 0f && energy < maxEnergy)
            {
                energyRegenAcc += cls.energyRegen * dt;
                while (energyRegenAcc >= 1f) { energyRegenAcc -= 1f; AddEnergy(1); }
            }
            if (cls.hpRegenEvery > 0f && hp < maxHp)
            {
                hpRegenT += dt;
                if (hpRegenT >= cls.hpRegenEvery) { hpRegenT = 0f; Heal(1); }
            }
        }

        if (!dead && armor < maxArmor && sinceHit > ArmorDelay)
        {
            armorTimer -= dt;
            if (armorTimer <= 0f)
            {
                armor++;
                armorTimer = ArmorTick;
            }
        }

        if (active && Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor(true);
            fireCd = Mathf.Max(fireCd, 0.2f);
        }
        if (active && Cursor.lockState == CursorLockMode.Locked)
        {
            // Raw mouse, no smoothing (smoothing adds tens of ms of latency).
            float s = MouseSens * GameSettings.sensitivity;
            yaw += Input.GetAxisRaw("Mouse X") * s * (GameSettings.invertX ? -1f : 1f);
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * s * (GameSettings.invertY ? -1f : 1f), -40f, 60f);
        }

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 fwd = yawRot * Vector3.forward;
        Vector3 right = yawRot * Vector3.right;
        Vector2 input = Vector2.zero;
        if (active)
        {
            input.x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            input.y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        }
        Vector3 dir = Vector3.ClampMagnitude(right * input.x + fwd * input.y, 1f);

        if (active && Input.GetKeyDown(KeyCode.LeftShift))
        {
            Vector3 dashTo = dir.sqrMagnitude > 0.01f ? dir.normalized : fwd;
            if (InMech) MechDash(dashTo); else Dash(dashTo);
        }

        Vector3 h = new Vector3(velocity.x, 0f, velocity.z);
        Footsteps(dt);
        if (dashT > 0f)
        {
            dashT -= dt;
            // The assassin's shadow step leaves fading copies behind.
            if (!InMech && cls != null && cls.dashShadows && body && (afterimageT -= dt) <= 0f)
            {
                afterimageT = 0.045f;
                Afterimage.Spawn(body.transform, new Color(0.55f, 0.3f, 1f));
            }
            h = dashDir * (ramming ? 12f : dashSpeed);
        }
        else
        {
            // Committed jumps: little steering in the air.
            float pace = charge > 0f ? 0.6f : 1f; // charging slows you down
            float grip = Grounded ? (onIce > 0f && !HeavyMech ? 0.12f : 1f) : AirControl; // ice: you slide (the heavy mech grips)
            h = Vector3.MoveTowards(h, dir * Speed * pace, Accel * grip * dt);
        }
        velocity.x = h.x;
        velocity.z = h.z;

        if (cc.isGrounded && velocity.y <= 0f) velocity.y = -2f;
        else velocity.y -= Gravity * dt;
        if (active && Input.GetKeyDown(KeyCode.Space)) { if (InMech) MechJump(); else Jump(); }
        cc.Move(velocity * dt);

        nearbyWeapon = null;
        float best = 2.2f;
        foreach (var p in Pickup.Live)
        {
            if (p.kind != PickupKind.Weapon) continue;
            float d = Vector3.Distance(p.transform.position, transform.position);
            if (d < best) { best = d; nearbyWeapon = p; }
        }
        nearbyWare = null;
        float bestWare = 1.9f;
        if (!InMech)
            foreach (var it in ShopItem.Live)
            {
                float d = Vector3.Distance(new Vector3(it.transform.position.x, 0f, it.transform.position.z), new Vector3(transform.position.x, 0f, transform.position.z));
                if (!it.sold && d < bestWare) { bestWare = d; nearbyWare = it; }
            }
        nearbyThing = null;
        float bestThing = 2.6f;
        if (!InMech)
            foreach (var it in Interactable.Live)
            {
                float d = Vector3.Distance(new Vector3(it.transform.position.x, 0f, it.transform.position.z), new Vector3(transform.position.x, 0f, transform.position.z));
                if (it.Available && d < bestThing) { bestThing = d; nearbyThing = it; }
            }
        nearbyMech = null;
        float bestMech = 3.2f;
        if (!InMech)
            foreach (var m in ParkedMech.Live)
            {
                float d = Vector3.Distance(m.transform.position, transform.position);
                if (m.landed && d < bestMech) { bestMech = d; nearbyMech = m; }
            }

        if (!dead)
        {
            bool aiming = Time.time - lastShot < 0.6f;
            if (!faceInit) { faceDir = fwd; faceInit = true; model.rotation = Quaternion.LookRotation(fwd); }
            // A light mech boosting keeps facing the crosshair (no spinning round to dash backwards).
            if (aiming || !body || (InMech && !HeavyMech && dashT > 0f)) faceDir = fwd;
            else if (h.sqrMagnitude > 0.5f) faceDir = h.normalized;
            if (spinT <= 0f) model.rotation = Quaternion.Slerp(model.rotation, Quaternion.LookRotation(faceDir), 1f - Mathf.Exp(-15f * dt)); // the whirlwind turns it itself
            if (body)
            {
                float speed01 = h.magnitude / Speed;
                string moving = HeavyMech && body.Has("walk") ? "walk" : "run"; // the heavy mech stomps
                if (speed01 < 0.15f) body.SetBase("idle");
                // Guns use the run-and-shoot clip while firing; a staff is raised by the arms alone (PlayerHold).
                else body.SetBase(aiming && (InMech || (!Weapon.melee && Weapon.grip != WeaponGrip.Staff)) && body.Has("shoot") ? "shoot" : moving, Mathf.Lerp(0.8f, 1.15f, speed01));
            }
            if (active && InMech)
            {
                // In a mech: right button is its melee arm, Q swaps the left button between gun and
                // melee arm, F its special, E climbs out.
                if (Input.GetMouseButton(1)) MechMelee();
                if (Input.GetKeyDown(KeyCode.Q)) MechSwapMode();
                if (Input.GetKeyDown(KeyCode.F)) MechSpecial();
                if (Input.GetKeyDown(KeyCode.E)) Dismount(false);
            }
            else if (active)
            {
                if (Input.GetKeyDown(KeyCode.Q)) Swap();
                if ((Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.F)) && skill) skill.TryUse();
                if (Input.GetKeyDown(KeyCode.E))
                {
                    bool mechCloser = nearbyMech && (!nearbyWeapon || bestMech < best);
                    if (nearbyThing && !nearbyWeapon) nearbyThing.Use(this);
                    else if (nearbyWare && !nearbyWeapon) nearbyWare.Buy(this);
                    else if (mechCloser) Mount(nearbyMech.def, nearbyMech.hp, nearbyMech);
                    else if (nearbyWeapon) PickUp(nearbyWeapon);
                }
            }
            bool trigger = active && ((Input.GetMouseButton(0) && Cursor.lockState == CursorLockMode.Locked) || testTrigger);
            if (InMech)
            {
                MechTick(dt);
                if (mechBlade) { if (trigger) MechMelee(); }
                else MechFire(trigger);
            }
            else HandleWeapon(trigger, dt);
            prevTrigger = trigger;
        }
        else
        {
            if (beam) beam.enabled = false;
            if (beamGlow) beamGlow.enabled = beamLight.enabled = false;
        }

        // Melee swing: rotate the weapon across the body.
        if (swingT > 0f)
        {
            swingT -= dt;
            float k = 1f - swingT / 0.18f;
            weaponPivot.localRotation = weaponRest * Quaternion.Euler(0f, Mathf.Lerp(-70f, 80f, k), 0f);
        }
        else weaponPivot.localRotation = weaponRest;

        Color c = body ? Color.white : Palette.ColorOf("player_body");
        // Hurt: one soft red tint that fades over a third of a second. (It used to blink red/white at
        // 10 Hz through the whole invulnerability, a strobe that brings on 3D sickness.)
        if (!dead && sinceHit < 0.35f) c = Color.Lerp(new Color(1f, 0.55f, 0.55f), c, sinceHit / 0.35f);
        mpb.SetColor("_Color", c);
        mpb.SetColor("_BaseColor", c);
        if (bodyRenderer && (body || !InMech)) bodyRenderer.SetPropertyBlock(mpb); // the blocky stand-in mech keeps its colours
    }

    void LateUpdate()
    {
        if (!cam) return;
        // After animation: grips and hand IK (PlayerHold). Without a rig, point the gun at the crosshair.
        if (handsOnWeapon) PoseWeapon(Time.deltaTime);
        else if (body && !dead && !Weapon.melee)
        {
            Vector3 to = AimPoint() - weaponPivot.position;
            if (to.sqrMagnitude > 0.25f) weaponPivot.rotation = Quaternion.LookRotation(to, Vector3.up);
        }
        if (InMech && !dead) MechAim(Time.deltaTime); // after the pose, never between its if and else
        if (externalCamera) return;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        // A mech is bigger: the camera rises and pulls back, smoothly (no jump cut when climbing in).
        camScale = Mathf.MoveTowards(camScale, camScaleTarget, 0.8f * Time.unscaledDeltaTime);
        Vector3 pivot = transform.position + Vector3.up * 1.5f * camScale;
        Vector3 shoulder = pivot + rot * (Shoulder * camScale);
        Vector3 back = rot * Vector3.back;
        float dist = CamDistance * camScale;
        if (Physics.SphereCast(pivot, 0.2f, (shoulder - pivot).normalized, out var sh, Shoulder.magnitude * camScale, Layers.WorldMask, QueryTriggerInteraction.Ignore))
            shoulder = pivot + (shoulder - pivot).normalized * Mathf.Max(0f, sh.distance - 0.05f);
        if (Physics.SphereCast(shoulder, 0.2f, back, out var hit, dist, Layers.WorldMask, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(0.3f, hit.distance - 0.05f);
        // Snap in so walls never cut the view, but ease back out: no one-frame jumps when the wall
        // behind is passed (a sudden zoom is exactly the kind of visual jolt that causes nausea).
        camDist = dist < camDist ? dist : Mathf.MoveTowards(camDist, dist, 6f * Time.unscaledDeltaTime);
        cam.transform.SetPositionAndRotation(shoulder + back * camDist + rot * CameraShake.Offset(Time.unscaledDeltaTime), rot);
    }

    /// Where the crosshair points, ignoring anything between the camera and the player.
    public Vector3 AimPoint()
    {
        if (aimOverride.HasValue) return aimOverride.Value;
        Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float skip = Vector3.Distance(cam.transform.position, transform.position + Vector3.up * 1.5f * camScale);
        Vector3 o = r.origin + r.direction * skip;
        return Physics.Raycast(o, r.direction, out var hit, 200f, Layers.WorldMask | Layers.EnemyMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : o + r.direction * 200f;
    }

    float stepT;

    /// Footfalls at the running cadence; a mech stomps (the heavy one with a deep metal clank).
    void Footsteps(float dt)
    {
        float v = new Vector2(velocity.x, velocity.z).magnitude;
        if (!Grounded || v < 1.5f || dead) { stepT = 0f; return; }
        stepT += dt * v;
        float stride = HeavyMech ? 2.6f : InMech ? 2.2f : 1.6f;
        if (stepT < stride) return;
        stepT = 0f;
        if (HeavyMech) Sfx.Play("clank", 0.35f, 0.08f, 0.55f);
        else if (InMech) Sfx.Play("step", 0.35f, 0.08f, 0.8f);
        else Sfx.Play("step", 0.22f, 0.1f);
    }

    bool WeaponRaised(WeaponDef w) => !handsOnWeapon || (w.grip == WeaponGrip.Staff ? staffLift > 0.7f : aimBlend > 0.75f);

    /// Keep the firing pose up without shooting (grip inspection).
    public void HoldAim() => lastShot = Time.time;

    int RollDamage(int baseDamage, out bool crit)
    {
        crit = nextCrit || Random.value < RunState.critChance;
        // Every bonus is a percentage (Balance): blessings, the hit streak (up to +30%), and for hand
        // weapons the shop upgrades and a class skill weapon's own boost.
        float mul = (1f + RunState.damageBonus) * (1f + Combo.Bonus);
        if (!InMech) mul *= Weapon.damageMul * Balance.LevelMul(Weapon.level);
        else mul *= Balance.FloorMul(RunState.floor); // a mech keeps pace with the floor's enemies
        int d = Mathf.Max(1, Mathf.RoundToInt(baseDamage * mul));
        return crit ? d * 2 : d;
    }

    /// Trigger handling per weapon kind: tap/auto fire, charge-and-release, or a held beam.
    void HandleWeapon(bool trigger, float dt)
    {
        var w = Weapon;
        bool beaming = false;
        switch (w.kind)
        {
            case WeaponKind.Charge:
                if (trigger && fireCd <= 0f)
                {
                    float before = charge;
                    charge = Mathf.Min(1f, charge + dt / Weapons.ChargeTime);
                    if (before < 1f && charge >= 1f) FullyDrawn();
                }
                else if (!trigger && prevTrigger && charge > 0f) { FireCharged(w, charge); charge = 0f; }
                break;
            case WeaponKind.Beam:
                if (trigger) beaming = BeamTick(w, dt);
                break;
            default:
                if (w.chargeable)
                {
                    // Tap = normal attack on press; keep holding to charge; release for the heavy one.
                    if (trigger && !prevTrigger) { if (fireCd <= 0f) Attack(); holdT = 0f; }
                    else if (trigger)
                    {
                        holdT += dt;
                        if (holdT > 0.35f)
                        {
                            float before = charge;
                            charge = Mathf.Min(1f, charge + dt / 0.9f);
                            if (before < 1f && charge >= 1f) Sfx.Play("pickup", 0.5f, 0f);
                        }
                    }
                    else if (prevTrigger)
                    {
                        if (charge >= 0.25f) ChargedAttack(w, charge);
                        charge = 0f;
                        holdT = 0f;
                    }
                    break;
                }
                if (w.kind == WeaponKind.Flame && trigger) DrainEnergy(w, dt);
                bool canFlame = w.kind != WeaponKind.Flame || energy > 0;
                if (trigger && fireCd <= 0f && canFlame && !(w.kind == WeaponKind.Boomerang && boomerangOut))
                {
                    // The first shot waits until the gun (or staff) is up: it used to leave before the arm moved.
                    if (!w.melee && !WeaponRaised(w)) HoldAim();
                    else Attack();
                }
                break;
        }
        if (w.kind != WeaponKind.Charge && !w.chargeable) charge = 0f;
        ChargeGlow(w);
        if (beam) beam.enabled = beaming;
        if (beamGlow) beamGlow.enabled = beamLight.enabled = beaming;
    }

    /// Pay for a shot. Costs may be fractions of a point (the SMG's 0.4): they add up in energyAcc,
    /// the part of the next point already spent.
    bool SpendEnergy(float cost)
    {
        if (cost <= 0f) return true;
        if (energy - energyAcc < cost - 0.001f) return false;
        energyAcc += cost;
        while (energyAcc >= 1f - 0.001f && energy > 0)
        {
            energyAcc -= 1f;
            energy--;
        }
        energyAcc = Mathf.Max(0f, energyAcc);
        return true;
    }

    bool DrainEnergy(WeaponDef w, float dt)
    {
        if (w.energyPerSec <= 0f) return true;
        energyAcc += w.energyPerSec * dt;
        while (energyAcc >= 1f && energy > 0)
        {
            energyAcc -= 1f;
            energy--;
        }
        if (energy > 0) return true;
        if (noEnergyMsgCd <= 0f)
        {
            Hud.I?.Show("能量不足 — 换武器（Q）或捡能量球", 1f);
            noEnergyMsgCd = 1.5f;
        }
        return false;
    }

    /// The flamethrower's sound: the fire recording (as the braziers'), up while it fires and fading when
    /// it stops, silent while the game is paused. (It was bursts of synthesised hiss, which the user
    /// found piercing.)
    void FlameRoar()
    {
        bool on = Time.timeScale > 0f && Time.unscaledTime - flamingAt < 0.15f && !dead;
        if (!roar)
        {
            var clip = on ? Sfx.Clip("fire") : null;
            if (!clip) return;
            roar = gameObject.AddComponent<AudioSource>();
            roar.clip = clip;
            roar.loop = true;
            roar.spatialBlend = 0f;
            roar.volume = 0f;
            roar.Play();
        }
        roar.volume = Mathf.MoveTowards(roar.volume, on ? 0.9f : 0f, Time.unscaledDeltaTime * (on ? 6f : 2.5f));
    }

    /// Is the flamethrower's fire sounding now (the recording, not a synthesised hiss)?
    public bool FlameRoarOn => roar && roar.isPlaying && roar.volume > 0.05f && roar.clip && roar.clip.length > 10f;

    /// The bow at full draw: a puff of gold at the arrow and a soft chime.
    void FullyDrawn()
    {
        Vfx.Sparks(muzzle.position, Weapon.fan > 0 ? RavenViolet : Bullet.ArrowGold, 10, 2.5f);
        Sfx.Play("pickup", 0.35f, 0f);
    }

    void ChargeGlow(WeaponDef w)
    {
        // Gold motes drawn in to the arrow as the bow is drawn (more the further it is drawn).
        if (w.kind == WeaponKind.Charge && charge > 0f)
        {
            if (!gather)
            {
                gather = Vfx.Trail(muzzle, "gather");
                if (w.fan > 0)
                {
                    var main = gather.main; // the assassin's darts gather shadow, not gold
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.6f, 1f, 0.8f), new Color(0.55f, 0.3f, 1f, 0.7f));
                }
            }
            var em = gather.emission;
            em.rateOverTime = 10f + 40f * charge;
        }
        else if (gather)
        {
            Vfx.Detach(gather, 0.5f);
            gather = null;
        }
        if (charge <= 0f)
        {
            if (chargeLight) chargeLight.enabled = false;
            return;
        }
        if (!chargeLight)
        {
            chargeLight = new GameObject("charge_glow").AddComponent<Light>();
            chargeLight.type = LightType.Point;
            chargeLight.range = 3f;
        }
        chargeLight.enabled = true;
        chargeLight.transform.position = muzzle.position;
        // The darts glow the shadow's violet, and only faintly: a gold pool of light at your feet gave the
        // hidden assassin away.
        bool darts = w.fan > 0;
        chargeLight.color = darts ? RavenViolet : charge >= 1f ? new Color(1f, 0.8f, 0.3f) : Weapons.RarityColors[w.rarity];
        // Fully charged: a slow swell (about once a second), never a flicker.
        chargeLight.intensity = (1f + charge * 4f + (charge >= 1f ? 0.8f * (1f + Mathf.Sin(Time.time * 6f)) : 0f)) * (darts ? 0.4f : 1f);
    }

    /// The heavy version of each chargeable weapon, scaled by how long it was charged (0.25..1).
    void ChargedAttack(WeaponDef w, float c)
    {
        if (!SpendEnergy(Mathf.Max(1f, w.energy * 2f))) return;
        lastShot = Time.time;
        fireCd = w.interval / RunState.fireRateMul;
        Vector3 from = muzzle.position;
        Vector3 aim = (AimPoint() - from).normalized;
        Vector3 fwd = Quaternion.Euler(0f, w.melee ? MeleeYaw() : yaw, 0f) * Vector3.forward;
        switch (w.id)
        {
            case "rifle":
            {
                var b = Bullet.Fire(from, aim * 120f, true, 0.12f, "bullet_laser", 2f, RollDamage(Mathf.RoundToInt(w.damage * (1.5f + 2f * c)), out bool cr), 0f, cr);
                b.pierce = 5;
                Fx.Lightning(from, from + aim * 3f);
                break;
            }
            case "hammer":
            {
                // Overhead: the floor cracks when the hammer comes down, not when the button is let go.
                int dmg = Mathf.RoundToInt(RollDamage(w.damage, out _) * (2f + c) * RunState.meleeMul);
                float radius = 3.5f + 1.5f * c, swing = 0.55f;
                if (body) body.Trigger("slash", body.Length("slash") / swing, swing);
                if (smear) smear.Swing(swing, SmearColor(w));
                Later(HitDelay("slash", swing), () => Slam(dmg, radius));
                break;
            }
            case "spear":
            {
                // Lunge: dash along the view; everything on the line is skewered as you pass it.
                Vector3 start = transform.position;
                Dash(fwd);
                if (body) body.Trigger("slash", body.Length("slash") / 0.32f, 0.32f);
                if (smear) smear.Swing(0.32f, SmearColor(w));
                StartCoroutine(Lunge(w, start, fwd, Mathf.RoundToInt(w.damage * (2f + c))));
                break;
            }
            default: // sword: a whirlwind, hitting three times as the blade comes round
                StartCoroutine(Whirlwind(w, Mathf.RoundToInt(w.damage * (2f + c)), w.reach + 1.2f));
                break;
        }
        if (!w.melee)
        {
            Vfx.MuzzleFlash(from, aim, Palette.ColorOf(w.mat));
            CameraShake.Add(0.25f + 0.2f * c);
        }
        Sfx.Play(w.melee ? "slash" : "rocket", 0.9f, 0.05f);
        nextCrit = false;
        Debug.Log($"CHARGED {w.id} {c:F2}");
    }

    /// Hammer: everything around you takes the hit and is thrown back.
    void Slam(int damage, float radius)
    {
        foreach (var col in Physics.OverlapSphere(transform.position, radius, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = col.GetComponentInParent<Enemy>();
            if (!e || e.dead) continue;
            Vector3 away = (e.transform.position - transform.position).normalized;
            e.TakeDamage(damage, away, false, 6f);
            e.Knock(away * 10f);
        }
        foreach (var col in Physics.OverlapSphere(transform.position, radius * 1.4f, ~0, QueryTriggerInteraction.Ignore))
            if (col.attachedRigidbody && !col.attachedRigidbody.isKinematic && col.attachedRigidbody != null)
                col.attachedRigidbody.AddExplosionForce(6f, transform.position, radius * 1.4f, 0.5f, ForceMode.VelocityChange);
        HitProps(transform.position + Vector3.up, transform.forward, radius, 360f, damage);
        Vfx.Ring(transform.position, radius, new Color(1f, 0.85f, 0.4f));
        MeleeFx(Weapon, transform.position + Vector3.up, true);
    }

    /// Bow: damage x1..x5 with the charge; a full charge also pierces three enemies.
    void FireCharged(WeaponDef w, float c)
    {
        if (w.fan > 0)
        {
            ThrowDarts(w, c);
            return;
        }
        if (!SpendEnergy(w.energy)) return;
        fireCd = w.interval / RunState.fireRateMul;
        lastShot = Time.time;
        Vector3 from = muzzle.position;
        Vector3 aim = (AimPoint() - from).normalized;
        int dmg = RollDamage(Mathf.RoundToInt(w.damage * (1f + (Weapons.FullCharge - 1f) * c)), out bool crit);
        // The further it was drawn, the wider it hits: a full draw takes anyone within half a metre of
        // its line (aiming a thin arrow across the room was too hard).
        var b = Bullet.Fire(from, aim * w.speed * (1f + c), true, Mathf.Lerp(Bullet.MinPlayerShot, Weapons.FullDrawRadius, c), w.mat, 3f, dmg, 0f, crit);
        b.Empower(c);
        if (c >= 0.95f) b.pierce = 3;
        nextCrit = false;
        CameraShake.Add(0.05f + 0.15f * c);
        if (c >= 0.5f) Vfx.Sparks(from, Bullet.ArrowGold, (int)(12 * c), 4f);
        Sfx.Play("slash", 0.5f + 0.3f * c, 0.05f, 0.85f); // a soft whoosh (no square-wave laser zap)
    }

    /// Laser: a continuous ray to the crosshair, damage every interval while energy lasts.
    bool BeamTick(WeaponDef w, float dt)
    {
        if (!DrainEnergy(w, dt)) return false;
        lastShot = Time.time;
        if (!beam)
        {
            beam = new GameObject("beam").AddComponent<LineRenderer>();
            beam.sharedMaterial = Palette.Trail(w.mat);
            beam.startColor = beam.endColor = Palette.ColorOf(w.mat) * 3f;
            beam.widthMultiplier = 0.12f;
            beam.positionCount = 2;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Vector3 from = muzzle.position;
        Vector3 dir = (AimPoint() - from).normalized;
        Vector3 end = from + dir * 40f;
        if (Physics.Raycast(from, dir, out var rh, 40f, Layers.WorldMask | Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            end = rh.point;
            beamTick -= dt;
            var e = rh.collider.GetComponentInParent<Enemy>();
            if (e && !e.dead && w.id == "dawn_scepter") HolyLight(dt);
            if (e && beamTick <= 0f)
            {
                beamTick = w.interval / RunState.fireRateMul;
                e.TakeDamage(RollDamage(w.damage, out bool crit), dir, crit);
                nextCrit = false;
            }
        }
        beam.SetPosition(0, from);
        beam.SetPosition(1, end);
        beam.startColor = beam.endColor = w.id == "dawn_scepter" ? new Color(3f, 2.7f, 1.8f) : new Color(3f, 2.4f, 2.4f);
        beam.widthMultiplier = 0.05f + Mathf.PingPong(Time.time * 2f, 0.03f);
        // Outer red glow, sparks and a light where the beam lands.
        if (!beamGlow)
        {
            beamGlow = new GameObject("beam_glow").AddComponent<LineRenderer>();
            beamGlow.sharedMaterial = Palette.Trail(w.mat);
            beamGlow.positionCount = 2;
            beamGlow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamLight = new GameObject("beam_light").AddComponent<Light>();
            beamLight.type = LightType.Point;
            beamLight.range = 4f;
            beamLight.color = Palette.ColorOf(w.mat);
        }
        beamGlow.enabled = beamLight.enabled = true;
        beamGlow.startColor = beamGlow.endColor = Palette.ColorOf(w.mat) * 2.5f * new Color(1f, 1f, 1f, 0.45f);
        beamGlow.widthMultiplier = 0.22f + Mathf.PingPong(Time.time * 3f, 0.08f);
        beamGlow.SetPosition(0, from);
        beamGlow.SetPosition(1, end);
        beamLight.color = Palette.ColorOf(w.mat);
        beamLight.transform.position = end - dir * 0.3f;
        beamLight.intensity = 3.5f; // steady (a random value each frame strobed the room)
        beamSparkT -= dt;
        if (beamSparkT <= 0f)
        {
            beamSparkT = 0.06f;
            Vfx.Sparks(end, Palette.ColorOf(w.mat), 4, 4f);
        }
        return true;
    }

    /// Melee swings toward what the crosshair is on: the camera looks over the shoulder, so straight
    /// ahead of the body passes up to 20° beside a close enemy under the crosshair (the spear's 30° thrust
    /// missed it outright).
    float MeleeYaw()
    {
        Vector3 to = AimPoint() - transform.position;
        to.y = 0f;
        return to.sqrMagnitude > 0.25f ? Quaternion.LookRotation(to).eulerAngles.y : yaw;
    }

    /// Thunder: locks on to the enemy nearest the crosshair (35°, 20 m, in sight) so it needn't be dead
    /// centre, strikes it, and the current conducts from enemy to enemy (see Conduct).
    void ChainLightning(WeaponDef w)
    {
        Vector3 from = muzzle.position;
        Vector3 dir = (AimPoint() - from).normalized;
        Enemy first = LockOn(from, dir, 20f, 35f);
        Vector3 end = from + dir * 40f;
        if (first) end = first.AimPoint;
        else if (Physics.Raycast(from, dir, out var rh, 40f, Layers.WorldMask | Layers.EnemyMask, QueryTriggerInteraction.Ignore)) end = rh.point;
        Fx.Lightning(from, end);
        Sfx.Play("laser", 0.7f, 0.2f);
        if (!first) return;
        Reactions.Shock(first);
        first.TakeDamage(RollDamage(w.damage, out bool c0), (end - from).normalized, c0);
        StartCoroutine(Conduct(w, first, 5, 9f, 0.85f));
    }

    /// The enemy a lock-on weapon takes: the one under the crosshair, else the one closest to it within
    /// `cone` degrees and `range` metres that a wall doesn't hide.
    Enemy LockOn(Vector3 from, Vector3 dir, float range, float cone)
    {
        if (Physics.Raycast(from, dir, out var rh, range, Layers.WorldMask | Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var under = rh.collider.GetComponentInParent<Enemy>();
            if (under && !under.dead) return under;
        }
        Enemy best = null;
        float score = float.MaxValue;
        foreach (var e in Enemy.Live)
        {
            if (!e || e.dead) continue;
            Vector3 to = e.AimPoint - from;
            float d = to.magnitude, ang = Vector3.Angle(dir, to);
            if (d > range || ang > cone) continue;
            if (Physics.Linecast(from, e.AimPoint, Layers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
            float s = ang + d * 0.5f; // nearest the crosshair first, then nearest to you
            if (s < score) { score = s; best = e; }
        }
        return best;
    }

    /// Conduction: from `start` the current jumps to the nearest enemy it hasn't touched within `reach`
    /// metres, `hops` times, a beat apart so you see it travel, each jump a little weaker (`falloff`).
    System.Collections.IEnumerator Conduct(WeaponDef w, Enemy start, int hops, float reach, float falloff)
    {
        var struck = new System.Collections.Generic.List<Enemy> { start };
        Enemy last = start;
        float scale = 1f;
        for (int hop = 0; hop < hops; hop++)
        {
            yield return new WaitForSeconds(0.07f);
            if (!last) break;
            Enemy next = null;
            float best = reach;
            foreach (var e in Enemy.Live)
            {
                if (!e || e.dead || struck.Contains(e)) continue;
                float d = Vector3.Distance(e.transform.position, last.transform.position);
                if (d < best) { best = d; next = e; }
            }
            if (!next) break;
            scale *= falloff;
            Fx.Lightning(last.AimPoint, next.AimPoint);
            Sfx.Play("laser", 0.4f, 0.1f);
            Reactions.Shock(next);
            int dmg = Mathf.Max(1, Mathf.RoundToInt(RollDamage(w.damage, out bool crit) * scale));
            next.TakeDamage(dmg, (next.transform.position - last.transform.position).normalized, crit);
            struck.Add(next);
            last = next;
        }
    }

    /// Attack with the current weapon: a melee swing or a shot. Also used by the self-test.
    public void Attack()
    {
        var w = Weapon;
        if (!SpendEnergy(w.energy))
        {
            fireCd = 0.2f;
            if (noEnergyMsgCd <= 0f)
            {
                Hud.I?.Show("能量不足 — 换武器（Q）或捡能量球", 1f);
                noEnergyMsgCd = 1.5f;
            }
            return;
        }
        // Holding the trigger, carry over the part of a frame the last shot overshot by, so a weapon fires
        // at its stated rate whatever the frame rate (resetting it lost up to a frame a shot: -10% on the SMG).
        fireCd = (fireCd > -0.05f ? fireCd : 0f) + w.interval / RunState.fireRateMul;
        lastShot = Time.time;
        if (w.melee) Melee(w);
        else Shoot(w);
        nextCrit = false;
    }

    void Shoot(WeaponDef w)
    {
        Vector3 from = muzzle.position;
        Vector3 aim = (AimPoint() - from).normalized;
        switch (w.kind)
        {
            case WeaponKind.Chain:
                ChainLightning(w);
                return;
            case WeaponKind.Meteor:
                StartCoroutine(StaffMeteor(w));
                return;
            case WeaponKind.Flame:
                // The flames are for show (and light gas and enemies they touch); the damage is the cone:
                // everyone inside it, in sight, takes it each tick. Per flame it came out at 40 a second.
                for (int i = 0; i < 3; i++)
                {
                    var sp = Quaternion.Euler(Random.Range(-w.spread, w.spread) * 0.5f, Random.Range(-w.spread, w.spread) * 0.5f, 0f);
                    var f = Bullet.Fire(from, Quaternion.LookRotation(aim) * sp * Vector3.forward * w.speed * Random.Range(0.85f, 1.15f), true, w.radius, w.mat, 0.38f, 0);
                    f.burn = 2f;
                }
                FlameCone(w, from, aim);
                flamingAt = Time.unscaledTime;
                return;
            case WeaponKind.Grenade:
            {
                // Lob so the arc lands on the crosshair point, then bounce until the fuse runs out.
                const float g0 = 18f;
                Vector3 target = AimPoint();
                Vector3 flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
                float flight = Mathf.Clamp(flat.magnitude / w.speed, 0.25f, 0.9f);
                Vector3 v = flat / flight + Vector3.up * ((target.y - from.y + 0.5f * g0 * flight * flight) / flight);
                int dmg = RollDamage(w.damage, out bool crit);
                var g = Bullet.Fire(from, v, true, w.radius, w.mat, 4f, dmg, w.explode, crit);
                g.gravity = g0;
                g.bounces = 3;
                g.fuse = 1.2f;
                Sfx.Play("shotgun", 0.4f, 0.1f);
                return;
            }
            case WeaponKind.Homing:
                for (int i = 0; i < 2; i++)
                {
                    // Launch level and a little upward, then let homing bring them down onto targets.
                    Vector3 flat = new Vector3(aim.x, 0f, aim.z).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, flat).normalized * (i == 0 ? -0.35f : 0.35f);
                    int dmg = RollDamage(w.damage, out bool crit);
                    var m = Bullet.Fire(from + side, (flat + side + Vector3.up * 0.35f).normalized * w.speed, true, w.radius, w.mat, 4f, dmg, w.explode, crit);
                    m.homing = 360f;
                }
                Sfx.Play("rocket", 0.6f);
                CameraShake.Add(0.1f);
                return;
            case WeaponKind.Boomerang:
            {
                int dmg = RollDamage(w.damage, out bool crit);
                var b = Bullet.Fire(from, aim * w.speed, true, w.radius, w.mat, 4f, dmg, 0f, crit);
                b.returnTo = transform;
                b.outDistance = 10f;
                boomerangOut = true;
                b.Returned = () => boomerangOut = false;
                Invoke(nameof(ClearBoomerang), 4f);
                Sfx.Play("slash", 0.6f);
                return;
            }
        }
        for (int i = 0; i < w.pellets; i++)
        {
            var spread = Quaternion.Euler(Random.Range(-w.spread, w.spread) * 0.5f, Random.Range(-w.spread, w.spread) * 0.5f, 0f);
            int dmg = RollDamage(w.damage, out bool crit);
            var b = Bullet.Fire(from, Quaternion.LookRotation(aim) * spread * Vector3.forward * w.speed, true, w.radius, w.mat, 3f, dmg, w.explode, crit);
            if (w.kind == WeaponKind.Frost) b.slow = 0.5f;
        }
        CameraShake.Add(w.pellets > 1 || w.explode > 0f ? 0.18f : 0.04f);
        Vfx.MuzzleFlash(from, aim, Palette.ColorOf(w.mat));
        Sfx.Play(w.pellets > 1 ? "shotgun" : w.explode > 0f ? "rocket" : w.id == "rifle" ? "laser" : "shoot", 0.6f);
    }

    int swingIndex;

    /// The flamethrower's tick: every enemy within the flames' reach and about their width.
    void FlameCone(WeaponDef w, Vector3 from, Vector3 aim)
    {
        float reach = w.speed * 0.38f; // how far the flames fly
        foreach (var e in EnemiesAround(from, reach + 0.5f))
        {
            Vector3 to = e.AimPoint - from;
            float along = Vector3.Dot(to, aim);
            if (along <= 0f || along > reach) continue;
            float off = (to - aim * along).magnitude; // distance from the flame's centre line
            if (off > 0.6f + along * Mathf.Tan(w.spread * 0.5f * Mathf.Deg2Rad)) continue;
            if (Physics.Linecast(from, e.AimPoint, Layers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
            e.TakeDamage(RollDamage(w.damage, out bool crit), aim, crit, 1f);
            e.ApplyBurn(2f);
        }
    }

    /// What a melee blow does where it lands (the swing itself is the blade's smear, see PlayerMelee):
    /// the hammer cracks the floor with a ring of dust, sparks and a few small arcs; the spear's tip
    /// sparks; blades need nothing more than the sparks on whatever they hit.
    void MeleeFx(WeaponDef w, Vector3 origin, bool heavy)
    {
        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        switch (w.id)
        {
            case "hammer":
            {
                Vector3 impact = transform.position + fwd * Mathf.Min(w.reach * 0.6f, 2.2f);
                Vfx.Ring(impact, heavy ? 2.5f : 1.6f, new Color(1f, 0.8f, 0.35f));
                Vfx.Puff(impact, new Color(0.55f, 0.5f, 0.45f, 0.6f), heavy ? 1.2f : 0.8f, heavy ? 10 : 6);
                Vfx.Sparks(impact + Vector3.up * 0.2f, new Color(1f, 0.9f, 0.4f), heavy ? 18 : 10, 6f);
                for (int i = 0; i < (heavy ? 4 : 2); i++)
                    Fx.Lightning(impact + Vector3.up * 0.1f, impact + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(1f, 2f));
                CameraShake.Add(heavy ? 0.4f : 0.18f);
                Sfx.Play("smash", 0.9f, 0.1f);
                Sfx.Play("door", 0.6f, 0.1f);
                break;
            }
            case "spear":
                Vfx.Sparks(origin + fwd * w.reach, new Color(1f, 0.95f, 0.7f), 6, 3f);
                break;
        }
    }

    void ClearBoomerang() => boomerangOut = false;

    /// Shop upgrade: the held weapon gets +1 damage for the rest of the run.
    public void UpgradeWeapon()
    {
        var id = Weapon.id;
        RunState.weaponLevels[id] = RunState.LevelOf(id) + 1;
        weapons[slot] = Weapons.Held(id);
    }

    /// Put a weapon in the current slot (self-test).
    public void Equip(WeaponDef w)
    {
        weapons[slot] = w;
        charge = 0f;
        EquipVisual();
    }

    public void AddEnergy(int amount) => energy = Mathf.Min(maxEnergy, energy + amount);

    public void Poison(float seconds)
    {
        if (dead || shielded || InMech) return; // a mech cockpit is sealed
        if (poisoned <= 0f) poisonTick = 0.6f;
        poisoned = Mathf.Max(poisoned, seconds);
    }

    public void Heal(int amount)
    {
        if (dead) return;
        if (InMech)
        {
            RepairMech(amount);
            return;
        }
        hp = Mathf.Min(hp + amount, maxHp);
    }

    /// Ground shockwaves ignore dash i-frames; only jumping clears them.
    public void TakeShockwave(int amount)
    {
        float saved = dashInvuln;
        dashInvuln = 0f;
        TakeDamage(amount);
        dashInvuln = saved;
    }

    public void TakeDamage(int amount)
    {
        if (dead || shielded || invuln > 0f || dashInvuln > 0f) return;
        if (stealthed)
        {
            // 鸦影遁: nothing lands while hidden; a wisp of shadow shows it passed through.
            if (amount > 0) Vfx.Puff(transform.position + Vector3.up, new Color(0.2f, 0.11f, 0.32f, 0.5f), 0.5f, 4);
            return;
        }
        if (guardCharges > 0 && amount > 0 && !InMech)
        {
            GuardBlock();
            return;
        }
        if (InMech)
        {
            MechDamage(amount); // the mech's durability goes first
            return;
        }
        sinceHit = 0f;
        armorTimer = ArmorTick;
        invuln = 0.6f;
        damageTaken += amount;
        RunState.damageTaken += amount;
        Combo.Break();
        int fromArmor = Mathf.Min(armor, amount);
        armor -= fromArmor;
        hp = Mathf.Max(hp - (amount - fromArmor), 0);
        CameraShake.Add(0.35f);
        Hud.I?.Hurt();
        Sfx.Play("hurt");
        if (body && hp > 0 && dashT <= 0f) body.Trigger("hit", 1.6f, 0.15f);
        if (hp == 0)
        {
            dead = true;
            if (body) body.Trigger("die");
            else
            {
                model.localRotation = Quaternion.Euler(-90f, yaw, 0f);
                model.localPosition = new Vector3(0f, 0.4f, 0f);
            }
            Died?.Invoke();
        }
    }
}
