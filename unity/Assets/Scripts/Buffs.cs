using System.Collections.Generic;
using UnityEngine;

/// The pick-one-of-three upgrades offered at the start of every floor.
public class BuffDef
{
    public string id, name, desc;
    public bool stackable;
    public System.Action<PlayerController> apply;
}

public static class Buffs
{
    public static readonly List<BuffDef> All = new List<BuffDef>
    {
        new BuffDef { id = "hp", name = "铁骨", desc = "生命上限 +2，并恢复 2 点生命", stackable = true,
            apply = p => { RunState.maxHp += 2; p.maxHp = RunState.maxHp; p.Heal(2); } },
        new BuffDef { id = "armor", name = "重铠", desc = "护甲上限 +2", stackable = true,
            apply = p => { RunState.maxArmor += 2; p.maxArmor = p.armor = RunState.maxArmor; } },
        new BuffDef { id = "damage", name = "磨锋", desc = "所有伤害 +20%", stackable = true,
            apply = p => RunState.damageBonus += Balance.DamageBuff },
        new BuffDef { id = "rate", name = "疾手", desc = "攻击速度 +25%", stackable = true,
            apply = p => RunState.fireRateMul *= 1.25f },
        new BuffDef { id = "energy", name = "满蓄", desc = "能量上限 +100，并回满", stackable = true,
            apply = p => { RunState.maxEnergy += 100; p.maxEnergy = p.energy = RunState.maxEnergy; } },
        new BuffDef { id = "dash", name = "风行", desc = "冲刺冷却减半",
            apply = p => RunState.dashCooldownMul = 0.5f },
        new BuffDef { id = "crit", name = "鹰眼", desc = "暴击率 +20%（暴击 ×2）", stackable = true,
            apply = p => RunState.critChance += 0.2f },
        new BuffDef { id = "leech", name = "噬能", desc = "每击杀一个敌人回复 4 点能量",
            apply = p => RunState.energyOnKill += 4 },
        new BuffDef { id = "melee", name = "剑圣", desc = "近战伤害 +50%，挥砍能消掉敌人子弹",
            apply = p => RunState.meleeMul *= Balance.MeleeBuff },
        new BuffDef { id = "light", name = "燕身", desc = "体力上限 +1（跳跃和冲刺各耗 1 点）", stackable = true,
            apply = p => { RunState.maxStamina += 1; p.maxStamina = RunState.maxStamina; p.stamina = p.maxStamina; } },
        new BuffDef { id = "double", name = "踏云", desc = "空中还能再跳一次（同样耗体力）",
            apply = p => RunState.doubleJump = true },
        new BuffDef { id = "dodge", name = "惊鸿", desc = "完美闪避时回 1 点护甲，子弹时间更长",
            apply = p => RunState.dodgeMaster = true },
    };

    public static List<BuffDef> Offer(int n)
    {
        var pool = All.FindAll(b => b.stackable || !RunState.buffs.Contains(b.id));
        var picked = new List<BuffDef>();
        while (picked.Count < n && pool.Count > 0)
        {
            int i = Random.Range(0, pool.Count);
            picked.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return picked;
    }

    public static void Take(BuffDef b, PlayerController p)
    {
        RunState.buffs.Add(b.id);
        b.apply(p);
        Debug.Log("BUFF " + b.id);
    }
}
