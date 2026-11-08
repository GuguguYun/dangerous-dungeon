using UnityEngine;

/// The combat power system in one place (design notes: NOTES.md "战力体系").
///
/// Weapons are tuned to a damage-per-second budget, not to a damage-per-hit number: every weapon of a
/// rarity lands within ±10% of that rarity's band of sustained single-target DPS (no buffs, crits or
/// streak), and the band rises about 10% per rarity. What a weapon does besides hitting one enemy is
/// paid for out of that budget: melee gets +20% for the risk of standing close, the free pistol -10%,
/// blasts and chains -25% (they hit several), homing and lock-on -20% (they never miss).
/// Enemy health is set so a common weapon kills a floor-1 goblin gunner in about a second, and grows
/// 30% a floor, about what a floor's blessing and a rarer weapon add. Every buff is a percentage, so it
/// is worth the same to a dagger and to a hammer (a flat +1 used to double the SMG and add 10% to the hammer).
public static class Balance
{
    /// Sustained single-target DPS by rarity: 普通, 优秀, 稀有, 史诗, 传说.
    public static readonly float[] RarityDps = { 25f, 27.5f, 30f, 32.5f, 35f };
    public const float BandTolerance = 0.1f;

    /// What a weapon's other strengths cost out of its band (1 = none).
    public static float RoleMul(WeaponDef w) => w.id switch
    {
        "pistol" => 0.9f,                       // free and endless
        "sword" or "daggers" => 1.2f,           // melee: you have to be close
        "spear" => 1.2f * 0.9f,                 // melee, but reaches 4.6 m along a line
        "hammer" => 1.2f * 0.75f,               // melee, 200° and conducts to two more
        "shotgun" => 1.1f,                      // all six pellets only land up close
        "flamer" => 1.1f * 0.9f,                // short range, but the cone hits everyone in it
        "frost" => 0.9f,                        // slows
        "boomerang" => 0.85f,                   // pierces, hits going out and coming back
        "laser" => 0.95f,                       // hitscan
        "launcher" or "grenade" => 0.75f,       // blast
        "missile" => 0.75f * 0.8f,              // blast, and they home
        "thunder" => 0.75f * 0.8f,              // locks on, then conducts to five more
        _ => 1f,
    };

    /// The DPS a weapon is meant to have (class skill weapons sit above the orange band, see SkillDps).
    public static float TargetDps(WeaponDef w) => w.IsSkillWeapon ? SkillDps(w) : RarityDps[w.rarity] * RoleMul(w);

    /// A class skill's weapon is the skill's payoff for a few seconds: about 1.4x a legendary melee
    /// weapon (blasts and beams pay the same costs as above).
    static float SkillDps(WeaponDef w) => RarityDps[4] * 1.2f * 1.15f * (w.id == "star_staff" ? 0.75f : w.id == "dawn_scepter" ? 0.8f : 1f);

    // ---------- enemies ----------

    /// Health on floor 1; deeper floors multiply it by FloorMul.
    public static int BaseHp(EnemyKind k) => k switch
    {
        EnemyKind.Gunner or EnemyKind.Archer => 24,
        EnemyKind.Spreader or EnemyKind.Bomber => 30,
        EnemyKind.Charger => 36,
        EnemyKind.Guard => 45,
        EnemyKind.Mimic => 110,
        _ => 24,
    };

    public static float FloorMul(int floor) => 1f + 0.3f * (Mathf.Max(1, floor) - 1);
    public static int EnemyHp(EnemyKind k, int floor) => Mathf.RoundToInt(BaseHp(k) * FloorMul(floor));

    /// Bosses: about 27 s of the damage a player of that floor deals (a minute or so of real fighting).
    public static int BossHp(BossKind k) => k switch
    {
        BossKind.GoblinKing => 720,
        BossKind.BoarChief => 960,
        BossKind.Lich => 1200,
        _ => 1500,
    };

    public const float EliteHpMul = 2.2f;

    /// A blow this hard breaks a shield guard's guard, shatters ice and freezes the frame for a moment.
    public const int HeavyHit = 18;

    // ---------- buffs and upgrades (all percentages) ----------

    public const float DamageBuff = 0.2f;   // 磨锋, stacks
    public const float UpgradeStep = 0.2f;  // a shop upgrade, per level
    public const float MeleeBuff = 1.5f;    // 剑圣
    public static float LevelMul(int level) => 1f + UpgradeStep * level;

    /// The hit streak adds 10% per second it is kept up, to +30%: the same for every weapon (it used
    /// to count hits, so a dagger reached +30% in a second and a rocket launcher almost never).
    public const float StreakPerSecond = 0.1f, StreakMax = 0.3f;

    // ---------- status effects and the world ----------

    public const int BurnTick = 3;          // every 0.5 s while burning
    public const int Overload = 12;         // 超载: lightning on a burning enemy, to each neighbour
    public const int GasBlast = 20;         // 爆燃: fire in poison gas
    public const int BarrelBlast = 20;
    public const int EliteDeathBlast = 15;
    public const int GoblinBomb = 8;        // a goblin's bomb, to goblins standing in it
    public const int MeteorStorm = 24;      // the mage's skill, per meteor
    public const int SpikeTrap = 10, FireTrap = 8, SawTrap = 5, CrackTrap = 5;
    public static int DroneShot(int floor) => 3 + floor;

    // ---------- mechs: about twice what the floor's weapons do ----------

    public const int MechGun = 4, MechCannon = 22, MechBlade = 18, MechFist = 30, MechFinisher = 45,
        MechThrust = 30, MechStomp = 25, MechQuake = 20, MechRam = 18, MechWreck = 20, MechMissile = 12, MechDrop = 15;
}
