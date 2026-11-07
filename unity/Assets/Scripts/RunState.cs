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

    public static int bonusDamage, energyOnKill, maxStamina;
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
    public static int weaponPity;     // cleared rooms in a row without a weapon
    public static List<string> weaponsSeen = new List<string>();

    public static void SawWeapon(string id)
    {
        if (id != null && !weaponsSeen.Contains(id)) weaponsSeen.Add(id);
    }

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
        bonusDamage = 0;
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
        weaponPity = 0;
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
        weapons = new[] { p.weapons[0]?.id, p.weapons[1]?.id };
        slot = p.slot;
        mechId = p.InMech ? p.mechDef.id : null;
        mechHp = p.mechHp;
        floor++;
        seed = NewSeed();
        buffTaken = false;
    }
}
