using System.Collections.Generic;
using UnityEngine;

public enum WeaponKind { Gun, Melee, Charge, Beam, Flame, Grenade, Homing, Chain, Frost, Boomerang, Thrust, Meteor }

/// How the character holds the weapon (hand IK targets, see PlayerHold).
public enum WeaponGrip { OneHand, TwoHand, Shoulder, Staff, Bow, TwoHandMelee, Dual, OneHandMelee }

/// One weapon's numbers and placeholder look. A Meshy model at Resources/Models/Weapons/<id>
/// replaces the placeholder box when it exists.
public class WeaponDef
{
    public string id, name;
    public string type; // what it is (双匕首, 霰弹枪 ...): shown under the name
    public int level;   // shop upgrades (+1 damage each)
    public string Title => level > 0 ? $"{name} +{level}" : name;
    public WeaponKind kind = WeaponKind.Gun;
    public int rarity; // 0 white, 1 green, 2 blue, 3 purple, 4 orange
    public int damage = 1;
    public float interval = 0.2f;
    public float energy;        // per shot / swing (fractions add up: 0.4 = one point every 2.5 shots)
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
    public float damageMul = 1f; // class skill weapons: their own boost on top of everything else
    public string owner;         // class skill weapons: the class whose skill summons it
    public int fan;              // charged throws: how many darts a full charge looses at once (0 = one shot)

    /// Summoned by a class skill for a few seconds; never dropped, found, sold or swapped.
    public bool IsSkillWeapon => owner != null;

    public bool melee => kind == WeaponKind.Melee || kind == WeaponKind.Thrust;

    /// Sustained damage per second on one enemy, before buffs, crits and the hit streak: the number
    /// Balance tunes every weapon by (see Balance.TargetDps), and what the HUD compares.
    public float Dps
    {
        get
        {
            float hit = damage * damageMul * Balance.LevelMul(level);
            return kind switch
            {
                WeaponKind.Charge => hit * (fan > 0 ? fan : Weapons.FullCharge) / (interval + Weapons.ChargeTime), // charged in full each time
                WeaponKind.Flame or WeaponKind.Meteor => hit / interval + Balance.BurnTick / 0.5f, // and it keeps them burning
                WeaponKind.Homing => hit * 2f / interval, // two missiles a shot
                WeaponKind.Boomerang => hit * 2f / interval, // out and back once per throw
                _ => hit * pellets / interval,
            };
        }
    }

    /// Energy per second of steady use.
    public float EnergyRate => energyPerSec > 0f ? energyPerSec
        : kind == WeaponKind.Charge ? energy / (interval + Weapons.ChargeTime) : energy / interval;

    public WeaponDef Clone() => (WeaponDef)MemberwiseClone();

    public WeaponGrip grip => id switch
    {
        "pistol" or "boomerang" => WeaponGrip.OneHand,
        "launcher" => WeaponGrip.Shoulder, // a slim tube rides the shoulder; the bulky missile box is carried at the hip
        "thunder" or "frost" or "star_staff" or "dawn_scepter" => WeaponGrip.Staff,
        "raven_darts" or "oath_sword" => WeaponGrip.OneHandMelee, // a dart rides in the palm like a blade
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
        // Numbers follow Balance: each weapon's Dps sits within 10% of its rarity's band times its role.
        new WeaponDef { id = "pistol", name = "老伙计", type = "左轮", rarity = 0, damage = 5, interval = 0.22f, speed = 42f, spread = 1f, desc = "不耗能量的老伙计" },
        new WeaponDef { id = "smg", name = "蜂鸣", type = "冲锋枪", rarity = 1, damage = 3, interval = 0.11f, energy = 0.4f, speed = 48f, spread = 4f, size = new Vector3(0.14f, 0.2f, 0.8f), desc = "射速极快" },
        new WeaponDef { id = "shotgun", name = "开花炮", type = "霰弹枪", rarity = 1, damage = 3, interval = 0.55f, energy = 2f, speed = 36f, pellets = 6, spread = 14f, radius = 0.07f, mat = "bullet_heavy", size = new Vector3(0.16f, 0.18f, 0.95f), desc = "近距离一次 6 发" },
        new WeaponDef { id = "rifle", name = "穿星", type = "能量步枪", rarity = 2, damage = 12, interval = 0.4f, energy = 1.5f, speed = 80f, spread = 0.2f, radius = 0.1f, mat = "bullet_laser", size = new Vector3(0.12f, 0.16f, 1.1f), look = "gun_green", desc = "高速精准；按住蓄力：电磁贯穿弹" },
        new WeaponDef { id = "launcher", name = "轰天雷", type = "火箭筒", rarity = 3, damage = 22, interval = 0.9f, energy = 3.5f, speed = 22f, radius = 0.18f, explode = 3f, mat = "bullet_rocket", size = new Vector3(0.22f, 0.22f, 1.0f), look = "gun_red", desc = "命中爆炸" },
        new WeaponDef { id = "sword", name = "誓约之刃", type = "骑士大剑", kind = WeaponKind.Melee, rarity = 2, damage = 15, interval = 0.42f, reach = 3.2f, arc = 140f, size = new Vector3(0.08f, 0.12f, 1.4f), look = "blade", desc = "挥砍能打掉敌人子弹；按住蓄力：旋风斩" },
        new WeaponDef { id = "hammer", name = "震岳", type = "雷霆重锤", kind = WeaponKind.Melee, rarity = 3, damage = 22, interval = 0.75f, energy = 1f, reach = 3.6f, arc = 200f, size = new Vector3(0.3f, 0.3f, 1.2f), look = "gun_red", desc = "大范围重击，电流传导给身边 2 个敌人；按住蓄力：360° 砸地" },
        // round 2: special mechanics
        new WeaponDef { id = "bow", name = "追月", type = "猎弓", kind = WeaponKind.Charge, rarity = 2, damage = 7, interval = 0.3f, energy = 1f, speed = 45f, radius = 0.09f, mat = "bullet_arrow", look = "blade", desc = "按住蓄力，满蓄伤害 ×5 并穿透" },
        new WeaponDef { id = "laser", name = "灼线", type = "激光枪", kind = WeaponKind.Beam, rarity = 3, damage = 3, interval = 0.1f, energyPerSec = 3.6f, mat = "beam", look = "gun_red", desc = "按住持续照射" },
        new WeaponDef { id = "flamer", name = "龙息", type = "喷火器", kind = WeaponKind.Flame, rarity = 2, damage = 2, interval = 0.08f, energyPerSec = 4f, speed = 14f, spread = 18f, radius = 0.22f, mat = "bullet_fire", look = "gun_red", desc = "近距离烧到火焰里的所有敌人并点燃" },
        new WeaponDef { id = "grenade", name = "跳跳雷", type = "弹跳榴弹", kind = WeaponKind.Grenade, rarity = 2, damage = 16, interval = 0.7f, energy = 2.5f, speed = 16f, radius = 0.16f, explode = 3.5f, mat = "bullet_grenade", desc = "撞墙反弹，1.2 秒后爆炸" },
        new WeaponDef { id = "missile", name = "群鸦", type = "追踪导弹", kind = WeaponKind.Homing, rarity = 4, damage = 6, interval = 0.55f, energy = 2f, speed = 18f, radius = 0.13f, explode = 2.2f, mat = "bullet_missile", look = "gun_red", desc = "一次两发，自动追敌" },
        new WeaponDef { id = "thunder", name = "惊蛰", type = "雷电法杖", kind = WeaponKind.Chain, rarity = 3, damage = 9, interval = 0.45f, energy = 1.6f, look = "blade", desc = "自动锁定准星附近的敌人，电流再传导 5 个" },
        new WeaponDef { id = "frost", name = "霜语", type = "寒冰法杖", kind = WeaponKind.Frost, rarity = 2, damage = 3, interval = 0.33f, energy = 1.2f, speed = 34f, pellets = 3, spread = 6f, radius = 0.11f, mat = "bullet_frost", look = "blade", desc = "冰片命中减速 50%" },
        new WeaponDef { id = "boomerang", name = "归燕", type = "回旋镖", kind = WeaponKind.Boomerang, rarity = 1, damage = 12, interval = 0.95f, speed = 20f, radius = 0.25f, mat = "bullet_heavy", desc = "飞出再飞回，来回都伤人" },
        new WeaponDef { id = "spear", name = "破阵", type = "长枪", kind = WeaponKind.Thrust, rarity = 2, damage = 16, interval = 0.5f, reach = 4.6f, arc = 30f, size = new Vector3(0.06f, 0.06f, 1.8f), look = "blade", desc = "远距离突刺，一线穿透；按住蓄力：冲刺突刺" },
        new WeaponDef { id = "daggers", name = "影牙", type = "双匕首", kind = WeaponKind.Melee, rarity = 1, damage = 5, interval = 0.15f, reach = 2.3f, arc = 100f, size = new Vector3(0.05f, 0.05f, 0.5f), look = "blade", desc = "极快的连斩" },
    };

    /// The class skills' own weapons (see PlayerSkill): held only while the skill lasts, so they are
    /// kept out of All (drops, chests, the shop and the random pools never see them).
    public static readonly List<WeaponDef> SkillWeapons = new List<WeaponDef>
    {
        new WeaponDef { id = "oath_sword", owner = "knight", name = "誓约圣剑", type = "骑士专属", kind = WeaponKind.Melee, rarity = 4, damage = 16, damageMul = 1.2f, interval = 0.38f, reach = 4.3f, arc = 150f, look = "blade", desc = "剑气更长，伤害 +20%" },
        new WeaponDef { id = "raven_darts", owner = "assassin", name = "鸦羽镖", type = "刺客专属", kind = WeaponKind.Charge, rarity = 4, damage = 7, interval = 0.25f, speed = 38f, radius = 0.06f, fan = 7, mat = "bullet_dart", look = "blade", desc = "蓄力越久，掷出越多（最多 7 枚）" },
        new WeaponDef { id = "star_staff", owner = "mage", name = "焚星杖", type = "法师专属", kind = WeaponKind.Meteor, rarity = 4, damage = 15, interval = 0.5f, radius = 0.35f, explode = 2.4f, mat = "bullet_fire", look = "blade", desc = "准星处落陨石，爆炸并点燃" },
        new WeaponDef { id = "dawn_scepter", owner = "priest", name = "晨曦权杖", type = "牧师专属", kind = WeaponKind.Beam, rarity = 4, damage = 4, interval = 0.1f, mat = "bullet_gold", look = "blade", desc = "圣光光束，照到敌人回护甲" },
    };

    /// The bow: seconds to a full charge, and what a full charge multiplies the damage by.
    public const float ChargeTime = 0.8f, FullCharge = 5f;
    public const float FullDrawRadius = 0.5f; // how wide a fully drawn arrow hits (metres either side of its line)

    /// 鸦羽镖: darts thrown for a charge of c (0..1): one on a tap, one more for each sixth of the charge,
    /// `fan` in full; DartSpread degrees apart, a tight fan so a full volley still lands on one enemy at
    /// a few metres and spreads over a group further off.
    public static int DartsFor(WeaponDef w, float c) => 1 + Mathf.FloorToInt(Mathf.Clamp01(c) * (w.fan - 1) + 0.001f);
    public const float DartSpread = 2f;

    public static WeaponDef Get(string id) => All.Find(w => w.id == id) ?? SkillWeapons.Find(w => w.id == id);

    /// The weapon as the player holds it: a copy with this run's shop upgrades (+20% damage per level,
    /// applied when the damage is rolled, see Balance.LevelMul).
    public static WeaponDef Held(string id)
    {
        var w = Get(id);
        if (w == null) return null;
        int level = RunState.LevelOf(id);
        if (level == 0) return w;
        var c = w.Clone();
        c.level = level;
        return c;
    }

    /// A weapon the player doesn't hold; deeper floors lean towards rarer ones.
    public static WeaponDef RandomExcept(params string[] owned) => RandomAtLeast(0, owned);

    /// A weapon of at least minRarity that the player doesn't hold (weighted towards the floor's level).
    public static WeaponDef RandomAtLeast(int minRarity, params string[] owned)
    {
        var pool = All.FindAll(w => w.id != "pistol" && w.rarity >= minRarity && System.Array.IndexOf(owned, w.id) < 0);
        if (pool.Count == 0) return minRarity > 0 ? RandomAtLeast(0, owned) : All[1];
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
    /// Where the hand closes on a blade, as a share of its length from the butt (measured on the prep
    /// previews). The models' origin sits at 0.3 (blender/prep_static.py), which on the swords and daggers
    /// is the crossguard: the right hand disappeared inside it. The hammer and spear are held on the shaft
    /// there already.
    static float GripAt(string id) => id switch
    {
        "sword" => 0.21f,       // grip 10-26 %, guard 28-35 %: the right hand under the guard, the left below it
        "oath_sword" => 0.23f,  // grip 15-32 %
        "daggers" => 0.21f,     // grip 10-32 %
        _ => -1f,
    };

    /// Slide a blade along its length so its grip, not its crossguard, sits on the holder's origin.
    static void SeatGrip(WeaponDef w, GameObject go, Transform parent)
    {
        float at = GripAt(w.id);
        if (at < 0f || !parent) return;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var m = parent.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var b = r.localBounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                float z = m.MultiplyPoint3x4(c).z;
                lo = Mathf.Min(lo, z);
                hi = Mathf.Max(hi, z);
            }
        }
        if (hi > lo) go.transform.localPosition -= new Vector3(0f, 0f, lo + at * (hi - lo));
    }

    public static GameObject MakeVisual(WeaponDef w, Transform parent, int layer)
    {
        var prefab = Resources.Load<GameObject>("Models/Weapons/" + w.id);
        GameObject go;
        if (prefab)
        {
            go = Object.Instantiate(prefab, parent, false);
            SeatGrip(w, go, parent);
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
