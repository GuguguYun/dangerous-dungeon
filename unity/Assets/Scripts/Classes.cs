using System.Collections.Generic;
using UnityEngine;

public enum SkillKind { Shield, Stealth, Meteor, Sanctuary }

/// A playable class: its Meshy model, stats, starting weapons and skill.
public class ClassDef
{
    public string id, name, model, desc;
    public int maxHp, maxArmor, maxEnergy;
    public int stamina = 3;          // jumps/dashes in a row
    public float speed = 6f;         // run speed, m/s
    public float crit = 0.05f;
    public float meleeMul = 1f;
    public float dashCooldownMul = 1f;
    public float energyRegen;        // energy per second
    public float hpRegenEvery;       // seconds per hp, 0 = none
    public float armorDelay = 3f;    // seconds out of combat before armor comes back
    public string trait = "";
    public string[] weapons;
    public SkillKind skill;
    public string skillName, skillDesc;
    public float cooldown;
}

public static class Classes
{
    public static readonly List<ClassDef> All = new List<ClassDef>
    {
        new ClassDef { id = "knight", name = "骑士", model = "hero_hq", desc = "皮糙肉厚，正面硬刚",
            maxHp = 7, maxArmor = 5, maxEnergy = 180, weapons = new[] { "pistol", "sword" },
            stamina = 3, speed = 5.6f, armorDelay = 2f, trait = "护甲 2 秒后就开始恢复",
            skill = SkillKind.Shield, skillName = "圣盾", cooldown = 12f,
            skillDesc = "3.5 秒护盾：免疫伤害，碰到护盾的子弹被消掉，开盾时震开身边敌人" },
        new ClassDef { id = "assassin", name = "刺客", model = "assassin", desc = "神出鬼没，一击致命",
            maxHp = 5, maxArmor = 3, maxEnergy = 200, weapons = new[] { "smg", "daggers" },
            stamina = 5, speed = 6.8f, crit = 0.2f, meleeMul = 1.3f, dashCooldownMul = 0.5f, trait = "近战伤害 ×1.3，冲刺冷却减半",
            skill = SkillKind.Stealth, skillName = "隐匿", cooldown = 14f,
            skillDesc = "隐身 4 秒：怪物丢失目标、乱打你最后出现的位置附近；隐身中首击伤害 ×3" },
        new ClassDef { id = "mage", name = "法师", model = "mage", desc = "脆皮，但法术覆盖全场",
            maxHp = 4, maxArmor = 3, maxEnergy = 260, weapons = new[] { "pistol", "frost" },
            stamina = 3, speed = 6f, energyRegen = 2f, trait = "每秒自动回复 2 点能量",
            skill = SkillKind.Meteor, skillName = "陨石雨", cooldown = 15f,
            skillDesc = "准星处 5 米预警圈，0.8 秒后落下 8 颗燃烧陨石：范围爆炸并点燃" },
        new ClassDef { id = "priest", name = "牧师", model = "priest", desc = "续航之王，越打越稳",
            maxHp = 6, maxArmor = 4, maxEnergy = 220, weapons = new[] { "pistol", "spear" },
            stamina = 4, speed = 6f, hpRegenEvery = 15f, trait = "每 15 秒自动回复 1 点生命",
            skill = SkillKind.Sanctuary, skillName = "圣光领域", cooldown = 16f,
            skillDesc = "脚下 4.5 米光圈 5 秒：圈内己方单位每秒回 1 生命、每 0.5 秒回 1 护甲" },
    };

    public static ClassDef Get(string id) => All.Find(c => c.id == id) ?? All[0];
}
