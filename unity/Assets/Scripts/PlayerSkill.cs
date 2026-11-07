using System.Collections;
using UnityEngine;

/// The class skill on right mouse / F: knight shield, assassin stealth, mage meteor storm,
/// priest sanctuary. Cooldowns and durations come from the ClassDef.
public class PlayerSkill : MonoBehaviour
{
    public ClassDef def;
    public float cooldownLeft;
    public float activeLeft;
    public int uses;

    PlayerController p;
    GameObject shieldFx;
    Renderer[] ghosted;
    Material[][] saved;
    Material ghostMat, shieldMat;
    float pulse;
    ParticleSystem motes;

    public bool Active => activeLeft > 0f;

    public void Setup(PlayerController owner, ClassDef d)
    {
        End();
        p = owner;
        def = d;
        cooldownLeft = 0f;
        activeLeft = 0f;
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
            case SkillKind.Meteor: StartCoroutine(Meteors(p.AimPoint())); break;
            case SkillKind.Sanctuary: StartCoroutine(Sanctuary(transform.position)); break;
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
        if (shieldFx)
        {
            shieldFx.transform.position = transform.position + Vector3.up * 0.9f;
            shieldFx.transform.localScale = Vector3.one * (4.4f + Mathf.Sin(Time.time * 10f) * 0.08f);
            foreach (var b in Bullet.Live.ToArray())
                if (!b.HitsEnemies && Vector3.Distance(b.transform.position, shieldFx.transform.position) < 2.25f)
                {
                    // The shield flares where it is struck.
                    Vfx.Sparks(b.transform.position, new Color(0.5f, 0.9f, 1f), 8, 4f);
                    Sfx.Play("clank", 0.4f, 0.1f, 1.4f);
                    pulse = 0.2f;
                    b.Cancel();
                }
            pulse = Mathf.Max(0f, pulse - Time.deltaTime);
            shieldFx.transform.localScale *= 1f + pulse * 0.25f;
        }
    }

    // ---------- knight: shield ----------

    void StartShield()
    {
        activeLeft = 3.5f;
        p.shielded = true;
        shieldFx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(shieldFx.GetComponent<Collider>());
        shieldFx.name = "shield";
        // A force field: a bright cyan rim with moving bands, clear in the middle.
        if (!shieldMat) shieldMat = Vfx.Fresnel(new Color(0.3f, 0.8f, 1.5f), 3.2f, 0f, 0.5f); // rim only: the view stays clear
        var r = shieldFx.GetComponent<Renderer>();
        r.sharedMaterial = shieldMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Bash: knock nearby enemies back.
        foreach (var c in Physics.OverlapSphere(transform.position, 3.5f, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (e && !e.dead) e.Knock((e.transform.position - transform.position).normalized * 9f);
        }
        Vfx.Ring(transform.position, 3.5f, new Color(0.4f, 0.85f, 1f));
        CameraShake.Add(0.2f);
    }

    // ---------- assassin: stealth ----------

    void StartStealth()
    {
        activeLeft = 4f;
        p.stealthed = true;
        Vfx.Puff(transform.position + Vector3.up, new Color(0.5f, 0.3f, 0.7f, 0.6f), 1.2f, 12);
        // Stealth: the body becomes a faint glowing purple outline.
        if (!ghostMat) ghostMat = Vfx.Fresnel(new Color(0.6f, 0.35f, 1.4f), 1.6f, 0.02f, 0.4f);
        ghosted = p.GetComponentsInChildren<Renderer>();
        saved = new Material[ghosted.Length][];
        for (int i = 0; i < ghosted.Length; i++)
        {
            saved[i] = ghosted[i].sharedMaterials;
            var mats = new Material[saved[i].Length];
            for (int k = 0; k < mats.Length; k++) mats[k] = ghostMat;
            ghosted[i].sharedMaterials = mats;
        }
    }

    /// The first attack out of stealth ends it.
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
        if (shieldFx) Destroy(shieldFx);
        if (ghosted != null)
        {
            for (int i = 0; i < ghosted.Length; i++)
                if (ghosted[i]) ghosted[i].sharedMaterials = saved[i];
            ghosted = null;
        }
    }

    // ---------- mage: meteor storm ----------

    IEnumerator Meteors(Vector3 centre)
    {
        centre.y = 0f;
        const float radius = 5f;
        var ring = GroundRing(centre, radius, new Color(1f, 0.45f, 0.1f));
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            ring.widthMultiplier = 0.12f + 0.12f * Mathf.PingPong(t * 8f, 1f);
            yield return null;
        }
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
                8 + RunState.bonusDamage, 2.5f, Random.value < RunState.critChance);
            m.burn = 2f;
            yield return new WaitForSeconds(0.12f);
        }
        yield return new WaitForSeconds(0.6f);
        Destroy(ring.gameObject);
    }

    // ---------- priest: sanctuary ----------

    IEnumerator Sanctuary(Vector3 centre)
    {
        centre.y = 0f;
        const float radius = 4.5f;
        var ring = GroundRing(centre, radius, new Color(1f, 0.85f, 0.35f));
        var moteHost = new GameObject("sanctuary_motes").transform;
        moteHost.position = centre + Vector3.up * 0.1f;
        motes = Vfx.Trail(moteHost, "motes");
        var glow = new GameObject("sanctuary_light").AddComponent<Light>();
        glow.transform.position = centre + Vector3.up * 2f;
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.9f, 0.6f);
        glow.range = radius * 2f;
        glow.intensity = 3f;
        float armorT = 0f, hpT = 0f;
        for (float t = 0f; t < 5f; t += Time.deltaTime)
        {
            armorT -= Time.deltaTime;
            hpT -= Time.deltaTime;
            ring.widthMultiplier = 0.15f + 0.08f * Mathf.Sin(t * 6f);
            // Every friendly unit inside benefits; today that is the player (pets/mercenaries later).
            foreach (var ally in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                var d = ally.transform.position - centre;
                d.y = 0f;
                if (d.magnitude > radius || ally.dead) continue;
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
        Destroy(glow.gameObject);
        Destroy(ring.gameObject);
        Vfx.Detach(motes);
        Destroy(moteHost.gameObject, 2f);
    }

    static LineRenderer GroundRing(Vector3 centre, float radius, Color c)
    {
        var lr = new GameObject("skill_ring").AddComponent<LineRenderer>();
        lr.transform.position = centre;
        lr.loop = true;
        lr.useWorldSpace = false;
        lr.positionCount = 64;
        lr.sharedMaterial = Palette.Trail("shockwave");
        lr.startColor = lr.endColor = c * 2.5f;
        lr.widthMultiplier = 0.15f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI * 2f / 64f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.08f, Mathf.Sin(a) * radius));
        }
        return lr;
    }
}
