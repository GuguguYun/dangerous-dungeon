using System.Collections.Generic;
using UnityEngine;

public enum WeaponKind { Gun, Melee, Charge, Beam, Flame, Grenade, Homing, Chain, Frost, Boomerang, Thrust }

/// How the character holds the weapon (hand IK targets, see PlayerHold).
public enum WeaponGrip { OneHand, TwoHand, Shoulder, Staff, Bow, TwoHandMelee, Dual }

/// One weapon's numbers and placeholder look. A Meshy model at Resources/Models/Weapons/<id>
/// replaces the placeholder box when it exists.
public class WeaponDef
{
    public string id, name;
    public WeaponKind kind = WeaponKind.Gun;
    public int rarity; // 0 white, 1 green, 2 blue, 3 purple, 4 orange
    public int damage = 1;
    public float interval = 0.2f;
    public int energy;          // per shot / swing
    public float energyPerSec;  // continuous weapons (beam, flame)
    public float speed = 40f;
    public int pellets = 1;
    public float spread = 1f; // degrees, full cone
    public float radius = 0.08f;
    public string mat = "bullet_player";
    public float explode; // area radius on impact, 0 = none
    public float reach = 3f; // melee
    public float arc = 120f; // melee, degrees
    public Vector3 size = new Vector3(0.12f, 0.16f, 0.7f);
    public string look = "gun";
    public string desc = "";

    public bool melee => kind == WeaponKind.Melee || kind == WeaponKind.Thrust;

    public WeaponDef Clone() => (WeaponDef)MemberwiseClone();

    public WeaponGrip grip => id switch
    {
        "pistol" or "boomerang" => WeaponGrip.OneHand,
        "launcher" or "missile" => WeaponGrip.Shoulder,
        "thunder" or "frost" => WeaponGrip.Staff,
        "bow" => WeaponGrip.Bow,
        "daggers" => WeaponGrip.Dual,
        "sword" or "hammer" or "spear" => WeaponGrip.TwoHandMelee,
        _ => WeaponGrip.TwoHand,
    };

    /// Hold the trigger past a tap to charge a heavier attack. Only where charging is the weapon's
    /// character (the bow charges by itself); quick or already-heavy weapons stay tap/auto.
    public bool chargeable => id is "rifle" or "sword" or "hammer" or "spear";
}

public static class Weapons
{
    public static readonly string[] RarityNames = { "普通", "优秀", "稀有", "史诗", "传说" };
    public static readonly Color[] RarityColors =
    {
        new Color(0.9f, 0.9f, 0.9f), new Color(0.45f, 0.95f, 0.45f), new Color(0.4f, 0.7f, 1f),
        new Color(0.8f, 0.45f, 1f), new Color(1f, 0.65f, 0.2f),
    };

    public static readonly List<WeaponDef> All = new List<WeaponDef>
    {
        new WeaponDef { id = "pistol", name = "旧手枪", rarity = 0, damage = 2, interval = 0.22f, speed = 42f, spread = 1f, desc = "不耗能量的老伙计" },
        new WeaponDef { id = "smg", name = "冲锋枪", rarity = 1, damage = 1, interval = 0.075f, energy = 1, speed = 48f, spread = 4f, size = new Vector3(0.14f, 0.2f, 0.8f), desc = "射速极快" },
        new WeaponDef { id = "shotgun", name = "霰弹枪", rarity = 1, damage = 2, interval = 0.6f, energy = 3, speed = 36f, pellets = 6, spread = 14f, radius = 0.07f, mat = "bullet_heavy", size = new Vector3(0.16f, 0.18f, 0.95f), desc = "近距离一次 6 发" },
        new WeaponDef { id = "rifle", name = "能量步枪", rarity = 2, damage = 5, interval = 0.4f, energy = 2, speed = 80f, spread = 0.2f, radius = 0.1f, mat = "bullet_laser", size = new Vector3(0.12f, 0.16f, 1.1f), look = "gun_green", desc = "高速精准；按住蓄力：电磁贯穿弹" },
        new WeaponDef { id = "launcher", name = "火箭筒", rarity = 3, damage = 6, interval = 0.9f, energy = 5, speed = 22f, radius = 0.18f, explode = 3f, mat = "bullet_rocket", size = new Vector3(0.22f, 0.22f, 1.0f), look = "gun_red", desc = "命中爆炸" },
        new WeaponDef { id = "sword", name = "骑士大剑", kind = WeaponKind.Melee, rarity = 2, damage = 5, interval = 0.42f, reach = 3.2f, arc = 140f, size = new Vector3(0.08f, 0.12f, 1.4f), look = "blade", desc = "挥砍能打掉敌人子弹；按住蓄力：旋风斩" },
        new WeaponDef { id = "hammer", name = "雷霆重锤", kind = WeaponKind.Melee, rarity = 3, damage = 9, interval = 0.8f, energy = 1, reach = 3.6f, arc = 200f, size = new Vector3(0.3f, 0.3f, 1.2f), look = "gun_red", desc = "大范围重击；按住蓄力：360° 砸地" },
        // round 2: special mechanics
        new WeaponDef { id = "bow", name = "猎弓", kind = WeaponKind.Charge, rarity = 2, damage = 3, interval = 0.25f, energy = 1, speed = 45f, radius = 0.09f, mat = "bullet_arrow", look = "blade", desc = "按住蓄力，满蓄伤害 ×4 并穿透" },
        new WeaponDef { id = "laser", name = "激光枪", kind = WeaponKind.Beam, rarity = 3, damage = 1, interval = 0.1f, energyPerSec = 6f, mat = "beam", look = "gun_red", desc = "按住持续照射" },
        new WeaponDef { id = "flamer", name = "喷火器", kind = WeaponKind.Flame, rarity = 2, damage = 1, interval = 0.05f, energyPerSec = 5f, speed = 14f, spread = 18f, radius = 0.22f, mat = "bullet_fire", look = "gun_red", desc = "近距离点燃敌人" },
        new WeaponDef { id = "grenade", name = "弹跳榴弹", kind = WeaponKind.Grenade, rarity = 2, damage = 7, interval = 0.7f, energy = 3, speed = 16f, radius = 0.16f, explode = 3.5f, mat = "bullet_grenade", desc = "撞墙反弹，1.2 秒后爆炸" },
        new WeaponDef { id = "missile", name = "追踪导弹", kind = WeaponKind.Homing, rarity = 4, damage = 4, interval = 0.55f, energy = 3, speed = 18f, radius = 0.13f, explode = 2.2f, mat = "bullet_missile", look = "gun_red", desc = "一次两发，自动追敌" },
        new WeaponDef { id = "thunder", name = "雷电法杖", kind = WeaponKind.Chain, rarity = 3, damage = 4, interval = 0.45f, energy = 2, look = "blade", desc = "瞬间命中并连锁 3 个敌人" },
        new WeaponDef { id = "frost", name = "寒冰法杖", kind = WeaponKind.Frost, rarity = 2, damage = 2, interval = 0.3f, energy = 1, speed = 34f, pellets = 3, spread = 10f, radius = 0.11f, mat = "bullet_frost", look = "blade", desc = "冰片命中减速 50%" },
        new WeaponDef { id = "boomerang", name = "回旋镖", kind = WeaponKind.Boomerang, rarity = 1, damage = 4, interval = 0.3f, speed = 20f, radius = 0.25f, mat = "bullet_heavy", desc = "飞出再飞回，来回都伤人" },
        new WeaponDef { id = "spear", name = "长枪", kind = WeaponKind.Thrust, rarity = 2, damage = 7, interval = 0.5f, reach = 4.6f, arc = 30f, size = new Vector3(0.06f, 0.06f, 1.8f), look = "blade", desc = "远距离突刺，一线穿透；按住蓄力：冲刺突刺" },
        new WeaponDef { id = "daggers", name = "双匕首", kind = WeaponKind.Melee, rarity = 1, damage = 3, interval = 0.15f, reach = 2.3f, arc = 100f, size = new Vector3(0.05f, 0.05f, 0.5f), look = "blade", desc = "极快的连斩" },
    };

    public static WeaponDef Get(string id) => All.Find(w => w.id == id);

    /// A weapon the player doesn't hold; deeper floors lean towards rarer ones.
    public static WeaponDef RandomExcept(params string[] owned)
    {
        var pool = All.FindAll(w => w.id != "pistol" && System.Array.IndexOf(owned, w.id) < 0);
        if (pool.Count == 0) return All[1];
        float total = 0f;
        foreach (var w in pool) total += Weight(w);
        float r = Random.value * total;
        foreach (var w in pool)
        {
            r -= Weight(w);
            if (r <= 0f) return w;
        }
        return pool[pool.Count - 1];
    }

    static float Weight(WeaponDef w) => Mathf.Pow(0.6f, Mathf.Max(0, w.rarity - RunState.floor));

    /// The weapon's look: a Meshy model if one was imported, else a coloured box.
    public static GameObject MakeVisual(WeaponDef w, Transform parent, int layer)
    {
        var prefab = Resources.Load<GameObject>("Models/Weapons/" + w.id);
        GameObject go;
        if (prefab)
        {
            go = Object.Instantiate(prefab, parent, false);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = w.size;
            go.transform.localPosition = new Vector3(0f, 0f, w.size.z * 0.5f - 0.15f);
            go.GetComponent<Renderer>().sharedMaterial = Palette.Get(w.look);
        }
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = layer;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        return go;
    }
}
