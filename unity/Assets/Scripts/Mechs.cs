using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// A pilotable mech. Its own durability soaks all damage before the pilot's; its weapons are built in
/// (a ranged gun on the left button, a melee arm on the right) and it has its own move set.
public class MechDef
{
    public string id, name, model;
    public bool heavy;
    public int maxHp;
    public float speed, height, radius;
    public int boosts;          // Shift/Space charges (the stamina pips while piloting)
    public float camScale;      // the camera pulls back for the bigger body
    public Color color;
    public string ranged, melee, special, dashName, jumpName, desc;
    public string gunBone = "lefthand"; // the Meshy models carry their gun on the left forearm
    public float specialCooldown;
}

public static class Mechs
{
    public static readonly MechDef Light = new MechDef
    {
        id = "light", name = "疾隼", model = "light_mech", heavy = false, maxHp = 30, speed = 7.4f,
        height = 2.1f, radius = 0.55f, boosts = 4, camScale = 1.25f, color = new Color(0.35f, 0.9f, 1f),
        ranged = "蜂鸣双管", melee = "流光刃", special = "超频", dashName = "推进冲刺", jumpName = "喷射跳",
        specialCooldown = 14f, desc = "快、能冲刺闪避；速射炮会过热；光刃能劈掉子弹",
    };

    public static readonly MechDef Heavy = new MechDef
    {
        id = "heavy", name = "磐石", model = "heavy_mech", heavy = true, maxHp = 60, speed = 4.6f,
        height = 2.5f, radius = 0.8f, boosts = 2, camScale = 1.45f, color = new Color(1f, 0.6f, 0.2f),
        ranged = "破城炮", melee = "碎岩拳", special = "蜂巢齐射", dashName = "蛮牛冲撞", jumpName = "陨落踏",
        specialCooldown = 9f, desc = "耐打、不怕地刺和冰面；炮弹爆炸；跳起砸地震飞周围",
    };

    public static MechDef Get(string id) => id == "heavy" ? Heavy : id == "light" ? Light : null;

    public static MechDef Random() => UnityEngine.Random.value < 0.5f ? Light : Heavy;

    /// A ground impact (drop landing, heavy stomp): hurts and throws back enemies, shoves loose props.
    public static int Shock(Vector3 at, float radius, int damage, float knock)
    {
        int hits = 0;
        var seen = new HashSet<Enemy>();
        foreach (var c in Physics.OverlapSphere(at + Vector3.up * 0.5f, radius, Layers.EnemyMask, QueryTriggerInteraction.Ignore))
        {
            var e = c.GetComponentInParent<Enemy>();
            if (!e || e.dead || !seen.Add(e)) continue;
            Vector3 away = e.transform.position - at;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            e.TakeDamage(damage, away, false, 6f);
            e.Knock(away * knock);
            hits++;
        }
        foreach (var c in Physics.OverlapSphere(at, radius * 1.4f, ~0, QueryTriggerInteraction.Ignore))
            if (c.attachedRigidbody && !c.attachedRigidbody.isKinematic)
                c.attachedRigidbody.AddExplosionForce(6f, at, radius * 1.4f, 0.5f, ForceMode.VelocityChange);
        return hits;
    }

    /// The Meshy model, or a blocky stand-in while the model is missing.
    public static GameObject Visual(MechDef d, Transform parent, int layer, out CharacterAnim anim)
    {
        anim = CharacterAnim.Spawn(d.model, parent, layer);
        if (anim) return anim.gameObject;
        var root = new GameObject("mech_" + d.id);
        root.transform.SetParent(parent, false);
        float s = d.height / 2.2f;
        string armor = d.heavy ? "iron" : "steel";
        Block(root, PrimitiveType.Cube, new Vector3(0f, 1.45f, 0f) * s, new Vector3(1.1f, 0.8f, 0.8f) * s, armor);
        Block(root, PrimitiveType.Cube, new Vector3(0f, 2.0f, 0.05f) * s, new Vector3(0.55f, 0.4f, 0.5f) * s, armor);
        Block(root, PrimitiveType.Cube, new Vector3(0f, 2.0f, 0.31f) * s, new Vector3(0.4f, 0.12f, 0.05f) * s, "player_visor");
        for (int side = -1; side <= 1; side += 2)
        {
            Block(root, PrimitiveType.Cube, new Vector3(0.25f * side, 0.55f, 0f) * s, new Vector3(0.3f, 1.1f, 0.35f) * s, armor);
            Block(root, PrimitiveType.Cube, new Vector3(0.75f * side, 1.35f, 0.15f) * s, new Vector3(0.3f, 0.3f, 0.9f) * s, armor);
        }
        foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = layer;
        return root;
    }

    static void Block(GameObject root, PrimitiveType type, Vector3 pos, Vector3 scale, string mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = Palette.Get(mat);
    }
}

/// An empty mech standing in a room: walk up and press E to climb in.
public class ParkedMech : MonoBehaviour
{
    public static readonly List<ParkedMech> Live = new List<ParkedMech>();
    public MechDef def;
    public int hp;
    public bool landed;

    void OnEnable() => Live.Add(this);
    void OnDisable() => Live.Remove(this);

    public static ParkedMech Create(MechDef d, int hp, Vector3 at, float yaw)
    {
        var go = new GameObject("parked_" + d.model);
        go.transform.SetPositionAndRotation(new Vector3(at.x, 0f, at.z), Quaternion.Euler(0f, yaw, 0f));
        go.layer = Layers.World;
        var pm = go.AddComponent<ParkedMech>();
        pm.def = d;
        pm.hp = hp;
        pm.landed = true;
        Mechs.Visual(d, go.transform, Layers.World, out _);
        // Solid: you walk around it and it blocks shots like cover.
        var col = go.AddComponent<CapsuleCollider>();
        col.radius = d.radius;
        col.height = d.height;
        col.center = Vector3.up * d.height / 2f;
        var glow = new GameObject("mech_light").AddComponent<Light>();
        glow.transform.SetParent(go.transform, false);
        glow.transform.localPosition = new Vector3(0f, 0.6f, 1.2f);
        glow.type = LightType.Point;
        glow.color = d.color;
        glow.range = 4f;
        glow.intensity = 2f;
        return pm;
    }

    /// Drop-pod delivery: a marker on the floor, then the mech falls from the sky and lands with a thump.
    public static ParkedMech Drop(MechDef d, Vector3 at, float yaw)
    {
        var pm = Create(d, d.maxHp, at, yaw);
        pm.landed = false;
        pm.StartCoroutine(pm.Fall());
        return pm;
    }

    IEnumerator Fall()
    {
        Vector3 ground = transform.position;
        Vfx.Ring(ground, 2.2f, def.color);
        Hud.I?.Show($"机甲「{def.name}」空投！靠近按 E 搭乘", 2.5f);
        Tips.Show("mech", "机甲：伤害先扣机甲耐久，被打爆你也不掉血；按 E 上下");
        transform.position = ground + Vector3.up * 16f;
        float v = 6f;
        while (transform.position.y > ground.y)
        {
            v += 40f * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, ground, v * Time.deltaTime);
            yield return null;
        }
        landed = true;
        Vfx.Ring(ground, 3f, def.color);
        Vfx.Puff(ground + Vector3.up * 0.3f, new Color(0.6f, 0.58f, 0.55f, 0.6f), 1.4f, 12);
        Vfx.Sparks(ground + Vector3.up * 0.2f, def.color, 14, 6f);
        Mechs.Shock(ground, 2.5f, Balance.MechDrop, 8f);
        CameraShake.Add(0.3f);
        Sfx.Play("explode", 0.6f, 0.1f);
    }
}
