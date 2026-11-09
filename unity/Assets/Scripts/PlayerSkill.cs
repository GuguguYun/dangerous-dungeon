using System.Collections;
using UnityEngine;

/// The class skill on right mouse / F. Each one also puts the class's own weapon in hand for a few
/// seconds (Weapons.SkillWeapons, see PlayerSkillWeapon): knight oath sword and shield (with a
/// knock-back), assassin stealth with the raven darts, mage meteor storm and the star staff, priest
/// sanctuary and the dawn scepter. Cooldowns come from the ClassDef.
public class PlayerSkill : MonoBehaviour
{
    public ClassDef def;
    public float cooldownLeft;
    public float activeLeft;
    public int uses;

    PlayerController p;
    CharacterController body;
    Renderer[] ghosted;
    Material[][] saved;
    Material ghostMat;
    float ghostFade;
    ParticleSystem wisps, feathers;

    public static readonly Color ShieldGold = new Color(1f, 0.84f, 0.42f);
    // How long each class holds its skill weapon.
    public const float OathTime = 6f, RavenTime = 5f, StaffTime = 6f, ScepterTime = 6f;

    public bool Active => activeLeft > 0f;

    public void Setup(PlayerController owner, ClassDef d)
    {
        End();
        if (owner) owner.EndSkillWeapon();
        p = owner;
        body = owner ? owner.GetComponent<CharacterController>() : null;
        def = d;
        cooldownLeft = 0f;
        activeLeft = 0f;
        // The magic circle's textures are drawn once; do it now rather than on the first cast.
        if (d != null && d.skill == SkillKind.Meteor) MagicCircle.Warm(MagicCircle.Style.Fire);
        if (d != null && d.skill == SkillKind.Sanctuary) MagicCircle.Warm(MagicCircle.Style.Holy);
    }

    public bool TryUse()
    {
        if (!p || p.dead || cooldownLeft > 0f || Active) return false;
        cooldownLeft = def.cooldown;
        uses++;
        switch (def.skill)
        {
            case SkillKind.Shield: StartShield(); break;
            case SkillKind.Stealth: StartStealth(); break;
            case SkillKind.Meteor:
                StartCoroutine(Meteors(p.AimPoint()));
                p.GrantSkillWeapon("star_staff", StaffTime);
                break;
            case SkillKind.Sanctuary:
                StartCoroutine(Sanctuary(transform.position));
                p.GrantSkillWeapon("dawn_scepter", ScepterTime);
                break;
        }
        Hud.I?.Show(def.skillName, 0.8f);
        Sfx.Play("buff", 0.8f, 0f);
        Debug.Log("SKILL " + def.skillName);
        return true;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        cooldownLeft = Mathf.Max(0f, cooldownLeft - dt);
        if (activeLeft > 0f)
        {
            activeLeft -= dt;
            if (activeLeft <= 0f) End();
        }
        if (ghosted != null && ghostMat)
        {
            // The shadow settles in over a third of a second; its smoke and glint follow the body.
            ghostFade = Mathf.Min(1f, ghostFade + dt / 0.3f);
            ghostMat.SetFloat("_Fade", ghostFade);
            ghostMat.SetFloat("_FeetY", transform.position.y);
            ghostMat.SetFloat("_Height", body ? body.height : 1.6f);
        }
    }

    // ---------- knight: shield ----------

    void StartShield()
    {
        // 誓约壁垒: the oath sword in the right hand and the oath shield on the left arm, which takes
        // the next three hits (PlayerSkillWeapon); calling them knocks nearby enemies back.
        activeLeft = OathTime;
        p.GrantSkillWeapon("oath_sword", OathTime);
        // Bash: knock nearby enemies back.
        foreach (var c in Physics.OverlapSphere(transform.position, 3.5f, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (e && !e.dead) e.Knock((e.transform.position - transform.position).normalized * 9f);
        }
        Vfx.Ring(transform.position, 3.5f, ShieldGold);
        CameraShake.Add(0.2f);
    }

    // ---------- assassin: stealth ----------

    // Shadow colours: the see-through body (linear, alpha = opacity) and its faint edge.
    static readonly Color ShadowBody = new Color(0.025f, 0.008f, 0.055f, 0.68f), ShadowEdge = new Color(0.78f, 0.45f, 1f);

    void StartStealth()
    {
        // 鸦影遁: hidden for the whole time, throwing included: enemies lose the lock (they hunt a decoy,
        // see Enemy.TargetPos) and nothing lands (PlayerController.TakeDamage); the raven darts in hand.
        activeLeft = RavenTime;
        p.stealthed = true;
        float h = body ? body.height : 1.6f;
        Vfx.Puff(transform.position + Vector3.up * h * 0.55f, new Color(0.2f, 0.11f, 0.32f, 0.65f), 1.1f, 14);
        // 鸦影遁: the body turns into a dark violet shadow you can half see through, with a faint edge
        // and slow smoke inside (Meshy/Stealth); wisps of shadow curl up off it and now and then a dark
        // feather drifts away. Readable, clearly hidden, nothing flickers.
        if (!ghostMat)
        {
            ghostMat = Vfx.Stealth();
            ghostMat.SetVector("_Shadow", (Vector4)ShadowBody);
            ghostMat.SetVector("_Edge", (Vector4)(ShadowEdge.linear * 0.6f));
            ghostMat.SetFloat("_Smoke", 0.35f);
        }
        ghostFade = 0f;
        ghostMat.SetFloat("_Fade", 0f);
        // Only the body and what it holds: particles and lines keep their own look.
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in p.GetComponentsInChildren<Renderer>())
            if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);
        ghosted = list.ToArray();
        saved = new Material[ghosted.Length][];
        for (int i = 0; i < ghosted.Length; i++)
        {
            saved[i] = ghosted[i].sharedMaterials;
            var mats = new Material[saved[i].Length];
            for (int k = 0; k < mats.Length; k++) mats[k] = ghostMat;
            ghosted[i].sharedMaterials = mats;
        }
        wisps = Vfx.ShadowWisps(transform, h);
        feathers = Vfx.Feathers(transform, h);
        p.GrantSkillWeapon("raven_darts", RavenTime); // after the body swap: the darts keep their own look
    }

    /// Stealth ends early (climbing into a mech).
    public void BreakStealth()
    {
        if (def != null && def.skill == SkillKind.Stealth && Active)
        {
            activeLeft = 0f;
            End();
        }
    }

    void End()
    {
        if (p)
        {
            p.shielded = false;
            p.stealthed = false;
        }
        if (ghosted != null)
        {
            for (int i = 0; i < ghosted.Length; i++)
                if (ghosted[i]) ghosted[i].sharedMaterials = saved[i];
            ghosted = null;
            // Out of the shadow: a last soft puff, and the wisps and feathers drift off and fade.
            Vfx.Puff(transform.position + Vector3.up * 0.9f, new Color(0.2f, 0.11f, 0.32f, 0.5f), 0.9f, 8);
        }
        Vfx.Detach(wisps, 2f);
        Vfx.Detach(feathers, 2.5f);
        wisps = feathers = null;
    }

    // ---------- mage: meteor storm ----------

    IEnumerator Meteors(Vector3 centre)
    {
        centre.y = 0f;
        const float radius = 5f;
        var circle = MagicCircle.Spawn(centre, radius, MagicCircle.Style.Fire, 0.5f);
        yield return new WaitForSeconds(0.8f);
        circle.Pulse();
        // Half the meteors seek enemies inside the circle, the rest carpet it at random.
        var inside = new System.Collections.Generic.List<Enemy>();
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            if (!e.dead && Vector3.Distance(new Vector3(e.transform.position.x, 0f, e.transform.position.z), centre) < radius) inside.Add(e);
        for (int i = 0; i < 8; i++)
        {
            var off = Random.insideUnitCircle * (radius - 0.5f);
            var spot = centre + new Vector3(off.x, 0f, off.y);
            if (i % 2 == 0 && inside.Count > 0)
            {
                var e = inside[Random.Range(0, inside.Count)];
                if (e) spot = new Vector3(e.transform.position.x, 0f, e.transform.position.z) + (Vector3)(Random.insideUnitCircle * 0.6f);
                spot.y = 0f;
            }
            var from = spot + new Vector3(Random.Range(-2f, 2f), 14f, Random.Range(-2f, 2f));
            var m = Bullet.Fire(from, (spot - from).normalized * 30f, true, 0.4f, "bullet_fire", 3f,
                Mathf.RoundToInt(Balance.MeteorStorm * (1f + RunState.damageBonus)), 2.5f, Random.value < RunState.critChance);
            m.burn = 2f;
            yield return new WaitForSeconds(0.12f);
        }
        yield return new WaitForSeconds(0.6f);
        if (circle) circle.End();
    }

    // ---------- priest: sanctuary ----------

    IEnumerator Sanctuary(Vector3 centre)
    {
        centre.y = 0f;
        const float radius = 4.5f;
        var circle = MagicCircle.Spawn(centre, radius, MagicCircle.Style.Holy, 0.45f);
        float armorT = 0f, hpT = 0f;
        for (float t = 0f; t < 5f; t += Time.deltaTime)
        {
            armorT -= Time.deltaTime;
            hpT -= Time.deltaTime;
            // Friendly units inside benefit: the player here, the companion drone below.
            var ally = PlayerController.Current;
            if (ally && !ally.dead && Vector3.Distance(new Vector3(ally.transform.position.x, 0f, ally.transform.position.z), centre) <= radius)
            {
                if (armorT <= 0f) ally.armor = Mathf.Min(ally.maxArmor, ally.armor + 1);
                if (hpT <= 0f)
                {
                    ally.Heal(1);
                    Vfx.Puff(ally.transform.position + Vector3.up, new Color(1f, 0.9f, 0.5f, 0.6f), 0.5f, 6);
                }
            }
            // The companion drone counts as an ally too.
            var pet = Pet.Current;
            if (hpT <= 0f && pet && pet.isActiveAndEnabled && !pet.offline && Vector3.Distance(new Vector3(pet.transform.position.x, 0f, pet.transform.position.z), centre) <= radius)
            {
                pet.Heal(1);
                Vfx.Puff(pet.transform.position, new Color(1f, 0.9f, 0.5f, 0.6f), 0.4f, 5);
            }
            if (armorT <= 0f) armorT = 0.5f;
            if (hpT <= 0f) hpT = 1f;
            yield return null;
        }
        if (circle) circle.End();
    }
}
