using System.Collections.Generic;

/// What carries over between floors of one run (scene reloads reset everything else).
public static class RunState
{
    public const int Floors = 4;

    public static int floor = 1;
    public static string classId = "knight";
    public static int seed = NewSeed();
    public static bool won;
    public static bool buffTaken;

    public static int maxHp, hp, maxArmor, maxEnergy, energy;
    public static string[] weapons;
    public static int slot;

    public static int energyOnKill, maxStamina;
    public static float damageBonus; // blessings: +20% each (Balance.DamageBuff)
    public static bool doubleJump, dodgeMaster;
    public static float fireRateMul, critChance, dashCooldownMul, meleeMul;
    public static List<string> buffs = new List<string>();
    public static int kills;
    public static string mechId; // the mech being piloted, carried through the portal
    public static int mechHp;
    public static float mechPity; // raises the mech drop chance after each room without one
    // Run summary (shown on the death / victory screen).
    public static float runTime;
    public static int damageDealt, damageTaken, perfectDodges, mechsPiloted, bossesKilled;
    public static int gold, shopBuys; // coins carry over between floors
    public static int bestCombo;
    public static int weaponPity;     // cleared rooms in a row without a weapon
    public static Dictionary<string, int> weaponLevels = new Dictionary<string, int>(); // shop upgrades, by weapon id

    public static int LevelOf(string id) => id != null && weaponLevels.TryGetValue(id, out int l) ? l : 0;
    public static List<string> weaponsSeen = new List<string>();

    public static void SawWeapon(string id)
    {
        if (id != null && !weaponsSeen.Contains(id)) weaponsSeen.Add(id);
        if (id == null || Known(id)) return;
        if (!SaveProgress)
        {
            codexScratch += id + ",";
            return;
        }
        UnityEngine.PlayerPrefs.SetString("codex", UnityEngine.PlayerPrefs.GetString("codex", ",") + id + ",");
        UnityEngine.PlayerPrefs.Save();
    }

    /// The weapon handbook (武器图鉴): every weapon held in any run so far, kept between runs.
    public static bool Known(string id) => (SaveProgress ? UnityEngine.PlayerPrefs.GetString("codex", ",") : codexScratch).Contains("," + id + ",");

    /// Off for the self-test and screenshot runs: they share the player's saved data (same app id), so
    /// what they "find" stays in memory instead of filling in the player's handbook.
    public static bool SaveProgress = true;
    static string codexScratch = ",";

    // Self-test bookkeeping that must survive the floor change.
    public static int testPass, testFail;

    static RunState() => NewRun();

    static int NewSeed() => System.Environment.TickCount & 0x7fffffff;

    public static void NewRun()
    {
        floor = 1;
        seed = NewSeed();
        won = false;
        buffTaken = false;
        var c = Classes.Get(classId);
        maxHp = hp = c.maxHp;
        maxArmor = c.maxArmor;
        maxEnergy = energy = c.maxEnergy;
        maxStamina = c.stamina;
        doubleJump = false;
        dodgeMaster = false;
        weapons = (string[])c.weapons.Clone();
        slot = 0;
        damageBonus = 0f;
        energyOnKill = 0;
        fireRateMul = 1f;
        critChance = c.crit;
        dashCooldownMul = c.dashCooldownMul;
        meleeMul = c.meleeMul;
        buffs = new List<string>();
        kills = 0;
        mechId = null;
        mechHp = 0;
        mechPity = 0f;
        runTime = 0f;
        damageDealt = damageTaken = perfectDodges = mechsPiloted = bossesKilled = 0;
        gold = shopBuys = 0;
        bestCombo = 0;
        weaponPity = 0;
        weaponLevels = new Dictionary<string, int>();
        weaponsSeen = new List<string>();
    }

    public static void ChooseClass(string id)
    {
        classId = id;
        NewRun();
    }

    public static void NextFloor(PlayerController p)
    {
        hp = p.hp;
        energy = p.energy;
        weapons = new[] { p.KeptWeapon(0)?.id, p.KeptWeapon(1)?.id }; // not a skill's summoned weapon
        slot = p.slot;
        mechId = p.InMech ? p.mechDef.id : null;
        mechHp = p.mechHp;
        floor++;
        seed = NewSeed();
        buffTaken = false;
    }
}
