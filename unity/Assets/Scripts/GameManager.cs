using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// Builds the whole game from code when a scene loads: world lighting, dungeon, player, HUD.
/// Each floor is one scene load; RunState carries the run across floors.
/// Command line: -autotest runs the self-check and quits; -shots <dir> saves screenshots and quits;
/// -soak <runs> plays every fight of whole runs unattended and reports anything that gets stuck.
public class GameManager : MonoBehaviour
{
    Hud hud;
    PlayerController player;
    Camera cam;
    Volume post;
    static bool windowApplied;
    Dungeon dungeon;
    string shotDir;
    string gripDir; // -grips <dir>: close-up contact sheets of every weapon grip
    string flickerDir; // -flicker <dir>: consecutive frames of the hero under a slowly turning camera
    string iconDir; // -icons <dir>: save the UI icons and hero portraits (works without a window)
    bool autotest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (FindAnyObjectByType<GameManager>() == null) new GameObject("Game").AddComponent<GameManager>();
        };
    }

    static bool showTitle = true;
    static bool argsApplied;

    bool Scripted => autotest || shotDir != null || gripDir != null || iconDir != null || flickerDir != null || Soak.Active;

    void Start()
    {
        ParseArgs();
        // A player window launched from a script may never get focus; keep running for self-checks.
        if (Scripted) Application.runInBackground = true;
        RunState.SaveProgress = !Scripted;
        argsApplied = true;
        // Self-checks run at a normal game frame rate; real play follows the display.
        if (Scripted) Application.targetFrameRate = 60;
        GameSettings.Load();
        if (!Scripted && !windowApplied)
        {
            windowApplied = true;
            GameSettings.ApplyWindow();
        }
        TimeControl.Reset();
        Physics.IgnoreLayerCollision(Layers.Debris, Layers.Player, true);
        Physics.IgnoreLayerCollision(Layers.Debris, Layers.Enemy, true);
        CameraShake.Reset();
        Sfx.Init(gameObject);
        Music.Init(gameObject);
        var theme = ThemeDef.ForFloor(RunState.floor);
        SetupWorld(theme);
        Props.FireColor = theme.fire;
        if (!Kit.Load(theme)) return;

        hud = new GameObject("Hud").AddComponent<Hud>();
        hud.Resume = () => SetPaused(false);
        hud.Restart = NewRun;
        hud.Quit = Application.Quit;
        hud.Begin = () => hud.screen = Hud.Screen.ClassSelect;
        hud.Continue = () =>
        {
            if (!RunSave.Load()) return;
            showTitle = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        };
        hud.ChooseClass = id =>
        {
            // New run as that class; reload so the player is rebuilt with its model and skill.
            showTitle = false;
            RunState.ChooseClass(id);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        };
        dungeon = new GameObject("Dungeon").AddComponent<Dungeon>();
        dungeon.Build(hud, RunState.floor, RunState.seed);
        hud.dungeon = dungeon;
        hud.themeName = theme.name;

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.clearFlags = RenderSettings.skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
        post = SetupPostFx(cam);
        GameSettings.ApplyDisplay(cam, post, Scripted);
        hud.SettingsChanged = () => GameSettings.ApplyDisplay(cam, post, Scripted);

        var pgo = new GameObject("Player");
        pgo.transform.position = dungeon.playerStart;
        player = pgo.AddComponent<PlayerController>();
        player.cam = cam;
        player.LoadRun();
        player.skill = pgo.AddComponent<PlayerSkill>();
        player.skill.Setup(player, Classes.Get(RunState.classId));
        player.yaw = dungeon.startYaw;
        Pet.Spawn(player, RunState.floor);
        IconStudio.Create();
        hud.player = player;
        dungeon.SetPlayer(player);
        Vfx.Ambient(theme.particles, player.transform);
        player.Died += () =>
        {
            RunSave.Clear();
            hud.screen = Hud.Screen.Dead;
            PlayerController.LockCursor(false);
            RecordRun(false);
        };
        dungeon.PortalEntered += OnPortal;
        if (!showTitle) RunSave.Write(player); // this floor is where 继续冒险 picks up (not the title's backdrop)

        Tips.Enabled = !Scripted; // test and screenshot runs must not use up the player's first-time hints
        TimeControl.Scripted = Scripted;
        Debug.Log("GAME ready");
        if (autotest) StartCoroutine(RunState.floor == 1 ? AutoTest() : AutoTestNextFloor());
        else if (shotDir != null) StartCoroutine(Shots());
        else if (gripDir != null) StartCoroutine(GripSheets());
        else if (flickerDir != null) StartCoroutine(FlickerFrames());
        else if (iconDir != null) StartCoroutine(DumpIcons());
        else if (Soak.Active)
        {
            var soak = gameObject.AddComponent<Soak>();
            soak.player = player;
            soak.dungeon = dungeon;
            StartCoroutine(soak.Run());
        }
        else if (showTitle)
        {
            hud.screen = Hud.Screen.Title;
            player.controlEnabled = false;
            player.pitch = 18f;
            PlayerController.LockCursor(false);
        }
        else OfferBuffs();
    }

    void OfferBuffs()
    {
        if (RunState.buffTaken)
        {
            hud.Show($"第 {RunState.floor} 层 · {hud.themeName}", 2f);
            PlayerController.LockCursor(true);
            return;
        }
        TimeControl.paused = true;
        PlayerController.LockCursor(false);
        hud.OfferBuffs(Buffs.Offer(3), b =>
        {
            Buffs.Take(b, player);
            RunState.buffTaken = true;
            RunSave.Write(player); // with the blessing: quitting now and continuing must not offer another
            Sfx.Play("buff", 0.8f, 0f);
            TimeControl.paused = false;
            PlayerController.LockCursor(true);
            hud.Show($"第 {RunState.floor} 层 · {hud.themeName} · {b.name}", 2f);
        });
    }

    /// Keep the best run on this machine (deeper floor wins; a clear beats everything; ties go to the faster).
    void RecordRun(bool won)
    {
        if (Scripted) return;
        int floor = RunState.floor, time = Mathf.FloorToInt(RunState.runTime);
        bool better = !PlayerPrefs.HasKey("best_floor")
            || (won ? 1 : 0) > PlayerPrefs.GetInt("best_won")
            || ((won ? 1 : 0) == PlayerPrefs.GetInt("best_won") && (floor > PlayerPrefs.GetInt("best_floor") || (floor == PlayerPrefs.GetInt("best_floor") && time < PlayerPrefs.GetInt("best_time"))));
        if (!better) return;
        PlayerPrefs.SetInt("best_floor", floor);
        PlayerPrefs.SetInt("best_won", won ? 1 : 0);
        PlayerPrefs.SetInt("best_time", time);
        PlayerPrefs.SetInt("best_kills", RunState.kills);
        PlayerPrefs.SetString("best_class", Classes.Get(RunState.classId).name);
        PlayerPrefs.Save();
    }

    void SetPaused(bool paused)
    {
        if (player.dead || RunState.won) return;
        hud.screen = paused ? Hud.Screen.Pause : Hud.Screen.None;
        TimeControl.paused = paused;
        PlayerController.LockCursor(!paused);
    }

    void NewRun()
    {
        RunState.NewRun();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnPortal()
    {
        if (RunState.floor >= RunState.Floors)
        {
            RunState.won = true;
            RunSave.Clear();
            player.controlEnabled = false;
            hud.screen = Hud.Screen.Won;
            RecordRun(true);
            PlayerController.LockCursor(false);
            return;
        }
        RunState.NextFloor(player);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void Update()
    {
        TimeControl.Apply();
        // Battle theme while any room is locked, the dungeon ambience otherwise.
        if (Music.I && dungeon)
        {
            bool fighting = dungeon.combatRooms.Exists(r => r.state == Room.State.Fighting) || dungeon.trialRooms.Exists(r => r.state == Room.State.Fighting);
            Music.I.Play(fighting ? "battle" : "explore");
        }
        if (Scripted || !hud) return;
        if (hud.screen == Hud.Screen.Title || hud.screen == Hud.Screen.ClassSelect)
        {
            // A slow orbit behind the menus (slow on purpose: no motion sickness).
            player.yaw += 4f * Time.unscaledDeltaTime;
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (hud.screen == Hud.Screen.None) SetPaused(true);
            else if (hud.screen == Hud.Screen.Pause) SetPaused(false);
        }
    }

    void ParseArgs()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-autotest") autotest = true;
            if (args[i] == "-shots" && i + 1 < args.Length) shotDir = args[i + 1];
            if (args[i] == "-grips" && i + 1 < args.Length) gripDir = args[i + 1];
            if (args[i] == "-flicker" && i + 1 < args.Length) flickerDir = args[i + 1];
            if (args[i] == "-icons" && i + 1 < args.Length) iconDir = args[i + 1];
            if (args[i] == "-soak" && i + 1 < args.Length && int.TryParse(args[i + 1], out int runs)) Soak.Init(runs);
            // Self-check helpers: start as a class and/or on a floor (first load only).
            if (!argsApplied && args[i] == "-class" && i + 1 < args.Length) RunState.ChooseClass(args[i + 1]);
        }
        // Second pass: -class resets the run, so the floor is applied after it.
        for (int i = 0; i < args.Length - 1; i++)
            if (!argsApplied && args[i] == "-floor" && int.TryParse(args[i + 1], out int f)) RunState.floor = f;
    }

    /// Bloom on emissive bits, ACES tone mapping, a little grading and a vignette (URP volume).
    static Volume SetupPostFx(Camera cam)
    {
        if (GraphicsSettings.currentRenderPipeline == null) return null;
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var vol = new GameObject("PostFX").AddComponent<Volume>();
        vol.isGlobal = true;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = p.Add<Bloom>(true);
        bloom.threshold.Override(1.0f);
        bloom.intensity.Override(0.8f);
        bloom.scatter.Override(0.65f);
        p.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
        var grade = p.Add<ColorAdjustments>(true);
        grade.postExposure.Override(0.6f);
        grade.saturation.Override(12f);
        grade.contrast.Override(10f);
        var vig = p.Add<Vignette>(true);
        vig.intensity.Override(0.3f);
        vig.smoothness.Override(0.45f);
        vol.profile = p;
        return vol;
    }

    static void SetupWorld(ThemeDef theme)
    {
        var sky = Resources.Load<Material>("Palette/sky");
        if (sky)
        {
            sky = new Material(sky);
            sky.SetColor("_SkyTint", theme.sky);
        }
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = theme.ambient;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = theme.fogDensity;
        RenderSettings.fogColor = theme.fog;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        sun.intensity = 1.5f;
        sun.color = theme.sun;
        sun.shadows = LightShadows.Soft;

        // Cool back light so characters keep a readable rim against the stone.
        var rim = new GameObject("Rim").AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
        rim.intensity = 0.45f;
        rim.color = new Color(0.55f, 0.65f, 1f);
        rim.shadows = LightShadows.None;
    }

    // ---------- self-test ----------

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) RunState.testPass++;
        else RunState.testFail++;
        Debug.Log($"AUTOTEST {(ok ? "PASS" : "FAIL")} {name} {detail}");
    }

    Enemy Dummy(Vector3 at, EnemyKind kind = EnemyKind.Gunner, int floor = 1)
    {
        var go = new GameObject("dummy");
        go.transform.position = new Vector3(at.x, 0.05f, at.z);
        var e = go.AddComponent<Enemy>();
        e.Setup(kind, floor);
        e.hp = e.maxHp = 100;
        return e;
    }

    Vector3 Ahead(float metres) =>
        player.transform.position + Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward * metres;

    IEnumerator AutoTest()
    {
        Debug.Log("AUTOTEST start");
        Enemy.ElitesEnabled = false; // random elites would make the room checks flaky; EliteTests covers them
        if (Pet.Current) Pet.Current.gameObject.SetActive(false); // the drone would hit other tests' targets; PetTests covers it
        Vector3 floor = Kit.Size("floor_tile"), wall = Kit.Size("wall"), door = Kit.Size("wall_door");
        Check("kit has 11 pieces (with the trap parts)", Kit.Count == 11, $"count={Kit.Count}");
        Check("floor tile is 4x4 m", Mathf.Abs(floor.x - 4f) < 0.05f && Mathf.Abs(floor.z - 4f) < 0.05f, $"size={floor}");
        Check("wall runs along X, 4.3 m tall", wall.x > 3.95f && wall.z < 1f && Mathf.Abs(wall.y - 4.3f) < 0.05f, $"size={wall}");
        Check("door wall matches wall", Mathf.Abs(door.x - 4f) < 0.05f, $"size={door}");
        Check("floor 1 layout: start, 3 combat, chest, shop, boss (+ altar/trial when there is room)", dungeon.rooms.Count >= 7 && dungeon.combatRooms.Count == 4 && dungeon.rooms.Exists(r => r.kind == Dungeon.Kind.Boss), $"rooms={dungeon.rooms.Count} combat={dungeon.combatRooms.Count}");
        Check("every tile reachable from the start", dungeon.AllRoomsConnected());

        // Buff pick (the test takes the first card).
        var offer = Buffs.Offer(3);
        Check("three distinct buffs offered", offer.Count == 3 && offer[0] != offer[1] && offer[1] != offer[2] && offer[0] != offer[2]);
        hud.OfferBuffs(offer, b => { Buffs.Take(b, player); RunState.buffTaken = true; });
        hud.PickBuff(0);
        Check("buff applied", RunState.buffs.Count == 1 && hud.screen == Hud.Screen.None, $"buffs={string.Join(",", RunState.buffs)}");

        yield return new WaitForSeconds(1f);
        float y = player.transform.position.y;
        Check("player stands on the floor", y > -0.1f && y < 0.3f, $"y={y:F2}");
        var cls = Classes.Get(RunState.classId);
        Check("starting weapons follow the class", player.weapons[0]?.id == cls.weapons[0] && player.weapons[1]?.id == cls.weapons[1], $"{RunState.classId}: {player.weapons[0]?.id}+{player.weapons[1]?.id}");
        Check("class base stats applied", player.maxStamina == cls.stamina + (RunState.buffs.Contains("light") ? 1 : 0) && Mathf.Approximately(RunState.dashCooldownMul, cls.dashCooldownMul * (RunState.buffs.Contains("dash") ? 0.5f : 1f)), $"{RunState.classId}: stamina={player.maxStamina}");
        Check("assassin has five dodges, a longer shadow-step dash and faster stamina", Classes.Get("assassin").stamina == 5 && Classes.Get("assassin").dashMul > 1.2f && Classes.Get("assassin").staminaRegen > 1.2f);

        // Aim -> bullet -> damage: put a dummy where the crosshair points, fire, expect a hit.
        player.pitch = 12f;
        yield return null;
        Ray r = player.cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float t = (r.origin.y - 0.9f) / -r.direction.y;
        var dummy = Dummy(r.origin + r.direction * t);
        yield return new WaitForSeconds(0.5f);
        for (int i = 0; i < 3; i++)
        {
            player.Attack();
            yield return new WaitForSeconds(0.25f);
        }
        yield return new WaitForSeconds(0.3f);
        Check("crosshair shots hit the target", dummy.hp < 100, $"dummy hp={dummy.hp} at {dummy.transform.position}");
        Destroy(dummy.gameObject);
        yield return null;

        // Melee: sword swing hurts an enemy in front and cancels an enemy bullet.
        player.Swap();
        Check("Q swaps to the second weapon", player.Weapon.id == cls.weapons[1], $"{player.Weapon.id}");
        player.Equip(Weapons.Get("sword")); // the melee checks below are about the sword
        var near = Dummy(Ahead(2f));
        yield return new WaitForSeconds(0.5f);
        var orb = Bullet.Fire(Ahead(1.5f) + Vector3.up * 1.0f, Vector3.zero, false, 0.16f, "bullet_enemy", 5f);
        yield return null;
        player.Attack();
        yield return new WaitForSeconds(0.45f); // the blow lands when the blade comes through
        Check("sword hits the enemy in front", near.hp < 100, $"hp={near.hp}");
        Check("sword cancels an enemy bullet", orb == null);
        Destroy(near.gameObject);

        // Energy: an energy weapon spends energy; orbs refill it.
        var smg = Pickup.Spawn(PickupKind.Weapon, player.transform.position, 0, Weapons.Get("smg"));
        yield return null;
        Check("weapon pickup is offered nearby", player.nearbyWeapon == smg);
        player.PickUp(smg);
        Check("picking up replaces the held weapon and drops it", player.Weapon.id == "smg" && Pickup.Live.Exists(p => p.weapon?.id == "sword"));
        int before = player.energy;
        for (int i = 0; i < 3; i++) player.Attack(); // 0.4 a shot: three cost at least a point
        Check("smg spends energy", player.energy < before, $"energy {before}->{player.energy}");
        Pickup.SpawnEnergy(player.transform.position + Vector3.forward * 2f, 3);
        yield return new WaitForSeconds(1.2f);
        Check("energy orbs fly in and refill", player.energy > before - 1, $"energy={player.energy}");

        // Armor soaks damage first.
        player.armor = 2;
        int hp0 = player.hp;
        player.TakeDamage(1);
        Check("armor absorbs the first hit", player.armor == 1 && player.hp == hp0, $"armor={player.armor} hp={player.hp}");
        yield return new WaitForSeconds(0.7f);

        // Special weapons, each against fresh dummies in the start room.
        yield return WeaponTests();

        // The combat power system: every weapon's real damage per second against its band.
        yield return BalanceTests();

        // Monster behaviour: face where they walk, aim before shooting, wind up before lunging.
        yield return MonsterTests();

        // Class skills, switching the skill in place.
        yield return SkillTests();

        // Physics: props, explosive chains, ragdolls, bouncing drops.
        yield return PhysicsTests();

        // Grips (hands on the weapon) and charged attacks.
        yield return GripAndChargeTests();

        // Traps: fire pits, spikes, gas canisters, ice.
        yield return TrapTests();

        // Mechs: drop, board, durability first, weapons, heavy moves, eject.
        yield return MechTests();
        // Shop: coins, the merchant's four wares, paying for them.
        yield return ShopTests();

        // Elites: shield, frenzy, splitting, volatile.
        yield return EliteTests();

        // New enemy types: the goblin bomber's lobbed bombs, the skeleton archer.
        yield return EnemyTypeTests();

        // The companion drone: follows, shoots, blocks, goes down, reboots; Sanctuary heals it.
        yield return PetTests();

        // Elemental reactions and loot drops.
        yield return ReactionTests();

        // Levels: blood altar, trial room, loops.
        yield return LevelTests();

        // The floor-3 boss: the lich.
        {
            Vector3 lichHome = dungeon.playerStart;
            Vector3 lichFwd = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
            player.Teleport(lichHome);
            player.hp = player.maxHp = 40;
            var lich = Dummy(lichHome + lichFwd * 5f, EnemyKind.Boss, 3);
            Check("floor 3's boss is the lich", lich.bossKind == BossKind.Lich && lich.displayName == "骸骨巫妖·霜冠", lich.displayName);
            lich.target = player;
            yield return new WaitForSeconds(0.5f);
            lich.ForceAttack(0);
            bool homing = Bullet.Live.Exists(b => !b.HitsEnemies && b.homing > 0f);
            Check("the lich fires homing skulls", lich.skullVolleys == 1 && homing);
            Vector3 lichFrom = lich.transform.position;
            lich.ForceAttack(2);
            Check("the lich blinks away", lich.blinks == 1 && Vector3.Distance(lichFrom, lich.transform.position) > 3f, $"moved {Vector3.Distance(lichFrom, lich.transform.position):F1} m");
            Destroy(lich.gameObject);
            foreach (var b in Bullet.Live.ToArray()) if (b && !b.HitsEnemies) Destroy(b.gameObject);
            player.hp = player.maxHp = RunState.maxHp;
            yield return new WaitForSeconds(0.8f);
        }

        // Hit streak: kept up, it adds 10% a second to +30% whatever the weapon; getting hurt breaks it.
        Combo.Break();
        var streakDummy = Dummy(dungeon.playerStart + Vector3.forward * 30f);
        for (int i = 0; i < 20; i++) streakDummy.TakeDamage(1, Vector3.forward);
        Check("the hit streak counts hits but 20 at once add no bonus", Combo.count == 20 && Combo.Bonus < 0.01f, $"count={Combo.count} bonus={Combo.Bonus}");
        for (int i = 0; i < 9; i++)
        {
            yield return new WaitForSeconds(0.4f);
            streakDummy.TakeDamage(1, Vector3.forward);
        }
        Check("kept up for 3 s the streak caps at +30%", Mathf.Approximately(Combo.Bonus, 0.3f), $"count={Combo.count} bonus={Combo.Bonus}");
        yield return new WaitForSeconds(0.7f); // past any hit invulnerability
        Combo.Hit();
        player.TakeDamage(1);
        Check("getting hurt breaks the streak", Combo.count == 0);
        Destroy(streakDummy.gameObject);
        player.hp = player.maxHp;
        Check("music tracks load", Resources.Load<AudioClip>("Music/explore") && Resources.Load<AudioClip>("Music/battle"));
        // The user found the recorded blade "shing" piercing: swings use the soft synthesised swish only.
        bool recordedSlash = Resources.Load<AudioClip>("Sfx/slash");
        for (int i = 1; i <= 8; i++) recordedSlash |= Resources.Load<AudioClip>($"Sfx/slash_{i}");
        Check("swings use the soft synthesised swish (no recorded blade clips)", !recordedSlash);
        float highest = 0f;
        for (int i = 0; i < 20; i++) { Sfx.Play("hit", 0.01f, 0.2f, 1.3f); highest = Mathf.Max(highest, Sfx.LastPitch); }
        Check("no sound is played above its own pitch", highest <= 1f);
        int fires = 0, crackling = 0;
        foreach (var f in dungeon.GetComponentsInChildren<Flicker>())
        {
            if (f.name != "fire") continue; // the braziers' (candles, fuses and traps flicker too)
            fires++;
            var a = f.GetComponent<AudioSource>();
            if (a && a.isPlaying && a.loop && a.spatialBlend == 1f && a.clip && a.clip.length > 10f) crackling++;
        }
        // The recorded fireplace loop (28 s), not the synthesised crackle the user found piercing.
        Check("braziers play the soft fire recording (3D, heard up close)", fires > 0 && crackling == fires, $"braziers={fires} playing={crackling}");
        Check("run summary tracks the numbers", RunState.damageDealt > 0 && RunState.weaponsSeen.Count >= 2 && RunState.runTime > 1f && RunState.mechsPiloted >= 1,
            $"dealt={RunState.damageDealt} weapons={RunState.weaponsSeen.Count} time={RunState.runTime:F0} mechs={RunState.mechsPiloted}");

        // 继续冒险: a captured run comes back field for field, after a new run has replaced it (in
        // memory only: the self-test never writes the player's save).
        string realRun = RunSave.Capture();
        RunState.floor = 3;
        RunState.gold = 42;
        RunState.weapons = new[] { "bow", null };
        RunState.weaponLevels["bow"] = 2;
        RunState.buffs.Add("light");
        string saved = RunSave.Capture();
        RunState.ChooseClass("mage");
        bool restored = RunSave.Restore(saved);
        Check("a saved run comes back exactly (floor, gold, weapons, upgrades, blessings, class)",
            restored && RunSave.Capture() == saved && RunState.floor == 3 && RunState.gold == 42 && RunState.weapons[0] == "bow" && RunState.weapons[1] == null
            && RunState.LevelOf("bow") == 2 && RunState.buffs.Contains("light") && RunState.classId == cls.id,
            $"restored={restored} floor={RunState.floor} class={RunState.classId}");
        Check("a broken save is refused, not half applied", !RunSave.Restore("floor=2\ngold=x") && RunState.floor == 3);
        RunSave.Restore(realRun);

        // Evasion: stamina limits, jumping over chest-height orbs, shockwaves need a jump.
        yield return new WaitForSeconds(1.5f);
        player.armor = player.maxArmor;
        Check("stamina starts full", Mathf.Approximately(player.stamina, player.maxStamina), $"stamina={player.stamina}");
        Check("jump spends stamina", player.Jump() && player.stamina < player.maxStamina, $"stamina={player.stamina:F1}");
        yield return null;
        Check("a second jump in the air only with the double-jump buff", player.Jump() == RunState.doubleJump, $"doubleJump={RunState.doubleJump}");
        for (int i = 0; i < 8; i++)
        {
            var around = Physics.OverlapCapsule(player.transform.position + Vector3.up * 0.2f, player.transform.position + Vector3.up * 2.2f, 0.6f, ~0, QueryTriggerInteraction.Collide);
            Debug.Log($"AUTOTEST diag jump t={i * 0.15f:F2} y={player.FeetHeight:F2} grounded={player.Grounded} flags={player.LastFlags} vy={player.VerticalSpeed:F1} stamina={player.stamina:F1} near={string.Join(",", System.Array.ConvertAll(around, c => c.name + "/" + c.GetType().Name + "/L" + c.gameObject.layer))}");
            yield return new WaitForSeconds(0.15f);
        }
        float apex = 0f;
        player.Jump();
        for (float u = 0; u < 1f; u += Time.deltaTime) { apex = Mathf.Max(apex, player.FeetHeight); yield return null; }
        Check("jump apex clears chest-height orbs", apex > 1.2f, $"apex={apex:F2}");
        player.stamina = 0f;
        Check("no jump without stamina", !player.Jump());
        yield return new WaitForSeconds(2.5f);
        Check("stamina regenerates on the ground", player.stamina >= 1.5f, $"stamina={player.stamina:F1}");

        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 chestOrbStart() => new Vector3(player.transform.position.x, 1f, player.transform.position.z) + fwdDir * 6f;
        player.stamina = player.maxStamina;
        int taken = player.damageTaken;
        Bullet.Fire(chestOrbStart(), -fwdDir * 11f, false, 0.16f, "bullet_enemy", 3f);
        yield return new WaitForSeconds(1.2f);
        Check("standing still, a chest orb hits", player.damageTaken > taken, $"taken={player.damageTaken - taken}");
        yield return new WaitForSeconds(0.8f);
        taken = player.damageTaken;
        int perfect0 = player.perfectDodges;
        Bullet.Fire(chestOrbStart(), -fwdDir * 11f, false, 0.16f, "bullet_enemy", 3f);
        player.Jump();
        yield return new WaitForSeconds(1.2f);
        Check("a timed jump lets the orb pass under", player.damageTaken == taken, $"taken={player.damageTaken - taken}");
        Check("passing under counts as a perfect dodge", player.perfectDodges > perfect0, $"perfect={player.perfectDodges - perfect0}");
        yield return new WaitForSeconds(2.5f);

        taken = player.damageTaken;
        player.stamina = player.maxStamina;
        Shockwave.Spawn(player.transform.position + fwdDir * 4.5f, 9f, 8f);
        yield return new WaitForSeconds(0.2f);
        player.Dash(Quaternion.Euler(0f, 90f, 0f) * fwdDir);
        yield return new WaitForSeconds(1.0f);
        Check("dashing does not escape a shockwave", player.damageTaken > taken, $"taken={player.damageTaken - taken}");
        yield return new WaitForSeconds(0.8f);
        taken = player.damageTaken;
        player.stamina = player.maxStamina;
        Shockwave.Spawn(player.transform.position + fwdDir * 4.5f, 9f, 8f);
        yield return new WaitForSeconds(0.25f);
        player.Jump();
        yield return new WaitForSeconds(1.0f);
        Check("jumping clears a shockwave", player.damageTaken == taken, $"taken={player.damageTaken - taken}");
        player.hp = player.maxHp;
        player.armor = player.maxArmor;
        yield return new WaitForSeconds(0.8f);

        // Room loop on the first combat room: enter, lock, waves, clear.
        var room = dungeon.combatRooms[0];
        Check("gates open before entering", !room.GatesClosed());
        player.Teleport(room.Center() + new Vector3(0f, 0.1f, 0f));
        yield return new WaitForSeconds(1.5f);
        Check("room locks on entry", room.state == Room.State.Fighting && room.GatesClosed(), $"state={room.state}");
        Check("wave 1 spawned", room.alive == room.waves[0].Length, $"alive={room.alive}");
        int taken0 = player.damageTaken;
        // Up to 12 s: with pillars or low walls in the room, enemies may first have to move to see you,
        // and a wave of slow shield guards or lobbing bombers takes a while to land a hit.
        for (float u = 0; u < 12f && player.damageTaken == taken0; u += 0.25f) yield return new WaitForSeconds(0.25f);
        Check("enemies damage the player (armor or hp)", player.damageTaken > taken0, $"taken={player.damageTaken - taken0} hp={player.hp} armor={player.armor}");
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            Debug.Log($"AUTOTEST diag {e.kind} dist={Vector3.Distance(e.transform.position, player.transform.position):F1} pos={e.transform.position} scale={e.transform.localScale.x:F2}");
        for (int w = 0; w < room.waves.Length; w++)
        {
            // Again for what a slime splits into (its halves come a frame later and the room counts them).
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                    if (!e.dead) e.TakeDamage(999, Vector3.forward);
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(2.6f); // the next wave: 1 s, its marks 0.7 s, appearing 0.1 s apart
        }
        Check("room clears after all waves", room.state == Room.State.Cleared && !room.GatesClosed(), $"state={room.state} wave={room.waveIndex}");
        Check("kills counted", RunState.kills >= room.waves[0].Length, $"kills={RunState.kills}");
        Check("minimap marks the room visited", dungeon.rooms.Exists(ri => ri.combat == room && ri.visited));

        // Floor bosses, each in the cleared room: signature moves.
        player.hp = player.maxHp = 50;
        var king = Dummy(room.Center() + new Vector3(4f, 0f, 4f), EnemyKind.Boss, 1);
        king.target = player;
        int summons = 0;
        king.Summon = (k, p) => summons++;
        Check("floor 1 boss is the goblin king", king.bossKind == BossKind.GoblinKing && king.displayName == "贪金王·葛巴", king.displayName);
        yield return new WaitForSeconds(4.5f);
        Check("goblin king leaps and slams", king.leaps >= 1 && king.slams >= 1, $"leaps={king.leaps} slams={king.slams}");
        king.TakeDamage(45, Vector3.forward);
        yield return null;
        Check("boss summons below 60%", summons == 2, $"summons={summons}");
        bool kingDied = false;
        king.Died += _ => kingDied = true;
        king.TakeDamage(999, Vector3.forward);
        Check("boss dies", kingDied && Enemy.ActiveBoss == null);
        yield return new WaitForSeconds(0.5f);

        // Boar: the player stands with a wall behind; the charge runs on into the wall and stuns.
        // Off the wall's centre line: doors sit in the middle of walls and this room's gates are open.
        player.Teleport(new Vector3(room.Center().x + 6f, 0.1f, room.max.z - 2.2f));
        var boar = Dummy(room.Center() + new Vector3(6f, 0f, -4f), EnemyKind.Boss, 2);
        boar.target = player;
        boar.Summon = (k, p) => { };
        Check("floor 2 boss is the boar chief", boar.bossKind == BossKind.BoarChief, boar.displayName);
        for (float u = 0; u < 7f && boar.stuns == 0; u += Time.deltaTime) yield return null;
        Check("boar charges and stuns itself on the wall", boar.charges >= 1 && boar.stuns >= 1, $"charges={boar.charges} stuns={boar.stuns}");
        int boarHp = boar.hp;
        if (boar.Stunned) boar.TakeDamage(5, Vector3.forward);
        Check("a stunned boss takes double damage", boarHp - boar.hp == 10, $"dmg={boarHp - boar.hp}");
        boar.TakeDamage(999, Vector3.forward);
        yield return new WaitForSeconds(0.5f);

        var orc = Dummy(room.Center(), EnemyKind.Boss, 4);
        orc.target = player;
        orc.Summon = (k, p) => { };
        Check("the final (floor 4) boss is the orc warlord", orc.bossKind == BossKind.OrcWarlord, orc.displayName);
        orc.TakeDamage(55, Vector3.forward);
        yield return null;
        yield return null;
        Check("bosses enrage below half health", orc.Enraged);
        orc.TakeDamage(999, Vector3.forward);
        player.hp = player.maxHp = RunState.maxHp;
        yield return new WaitForSeconds(0.5f);

        // The real floor boss: beat it, get the chest and the portal, step through.
        var arena = dungeon.combatRooms.Find(r => r.boss);
        // The soak run's stuck-enemy probe: inside one of the arena's pillars, but not just beside it.
        Vector3 pillar = arena.Center() + new Vector3(6f, 0.9f, 6f);
        Check("the soak probe finds a point inside a pillar, not one beside it",
            Soak.InsideSolid(pillar) != null && Soak.InsideSolid(pillar + new Vector3(-1.6f, 0f, 0f)) == null,
            $"inside={Soak.InsideSolid(pillar)} beside={Soak.InsideSolid(pillar + new Vector3(-1.6f, 0f, 0f))}");
        player.Teleport(arena.Center() + new Vector3(0f, 0.1f, -8f));
        yield return new WaitForSeconds(2.5f);
        Check("boss room locks and the boss appears", arena.state == Room.State.Fighting && Enemy.ActiveBoss != null, $"state={arena.state}");
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            if (!e.dead) e.TakeDamage(9999, Vector3.forward);
        yield return new WaitForSeconds(1.5f);
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            if (!e.dead) e.TakeDamage(9999, Vector3.forward);
        yield return new WaitForSeconds(1.0f);
        Check("beating the boss opens the way", arena.state == Room.State.Cleared && dungeon.portal != null, $"state={arena.state}");
        Check("boss drops a reward chest", FindAnyObjectByType<Chest>() != null && FindObjectsByType<Chest>(FindObjectsSortMode.None).Length >= 1);
        player.hp = Mathf.Max(player.hp, 2);
        player.Teleport(dungeon.portal.transform.position + Vector3.up * 0.1f);
        Debug.Log("AUTOTEST entering portal");
        yield return new WaitForSeconds(2f);
        Check("portal leads to floor 2", false, "scene did not reload");
        Finish();
    }

    Vector3 CrosshairGround()
    {
        Ray r = player.cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float t = (r.origin.y - 0.9f) / -r.direction.y;
        var p = r.origin + r.direction * t;
        return new Vector3(p.x, 0.05f, p.z);
    }

    IEnumerator Hold(float seconds)
    {
        player.testTrigger = true;
        yield return new WaitForSeconds(seconds);
        player.testTrigger = false;
        yield return null;
        yield return null;
    }

    /// Tilt the view until the crosshair is on the enemy's body (AimAt puts it on the floor at its feet,
    /// where a spread of pellets half goes into the ground).
    IEnumerator AimAtBody(Enemy e)
    {
        float best = float.MaxValue, bestPitch = player.pitch;
        for (float p = -10f; p <= 50f; p += 1f)
        {
            player.pitch = p;
            yield return null;
            var r = player.cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float off = Vector3.Cross(r.direction, e.AimPoint - r.origin).magnitude;
            if (off < best)
            {
                best = off;
                bestPitch = p;
            }
        }
        player.pitch = bestPitch;
        yield return null;
    }

    /// Tilt the view until the crosshair lands about `metres` ahead, so targets stay inside the
    /// 12 m start room whatever direction its door happens to face.
    IEnumerator AimAt(float metres)
    {
        for (player.pitch = 8f; player.pitch < 50f; player.pitch += 1f)
        {
            yield return null;
            if (Vector3.Distance(CrosshairGround(), player.transform.position) <= metres) break;
        }
    }

    IEnumerator WeaponTests()
    {
        yield return AimAt(4f);
        player.energy = player.maxEnergy = 999;
        float critWas = RunState.critChance;
        RunState.critChance = 0f; // damage comparisons below must not be skewed by random crits
        yield return null;
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 sideDir = Quaternion.Euler(0f, 90f, 0f) * fwdDir;

        // Bow: a full charge hits far harder than a tap.
        player.Equip(Weapons.Get("bow"));
        var d = Dummy(CrosshairGround());
        yield return new WaitForSeconds(0.4f);
        yield return Hold(0.05f);
        yield return new WaitForSeconds(0.4f);
        int tap = 100 - d.hp;
        yield return Hold(0.95f);
        yield return new WaitForSeconds(0.4f);
        int full = 100 - d.hp - tap;
        Check("bow: full charge >= 2x a tap", tap > 0 && full >= tap * 2, $"tap={tap} full={full}");
        Destroy(d.gameObject);

        // Laser: holding burns energy and ticks damage.
        player.Equip(Weapons.Get("laser"));
        d = Dummy(CrosshairGround());
        yield return new WaitForSeconds(0.4f);
        int e0 = player.energy;
        yield return Hold(1f);
        Check("laser: held beam ticks damage and drains energy", 100 - d.hp >= 5 && e0 - player.energy >= 3, $"dmg={100 - d.hp} energy={e0 - player.energy}");
        Destroy(d.gameObject);

        // Flamethrower sets enemies alight.
        player.Equip(Weapons.Get("flamer"));
        d = Dummy(CrosshairGround());
        yield return new WaitForSeconds(0.4f);
        yield return Hold(0.6f);
        Check("flamer: burns the target", d.Burning && d.hp < 100, $"burning={d.Burning} hp={d.hp} dist={Vector3.Distance(d.transform.position, player.transform.position):F1}");
        bool roaring = player.FlameRoarOn;
        Destroy(d.gameObject);
        yield return new WaitForSeconds(1f);
        Check("flamer sounds like fire (the recording) while it fires, then fades", roaring && !player.FlameRoarOn, $"while={roaring} after={player.FlameRoarOn}");

        // Frost shards slow.
        player.Equip(Weapons.Get("frost"));
        d = Dummy(player.transform.position + fwdDir * 4f);
        yield return new WaitForSeconds(0.4f);
        // Aiming is covered above; here a shard fired straight at the target checks the slow.
        var shard = Bullet.Fire(player.transform.position + Vector3.up * 1f + fwdDir * 0.8f, (d.AimPoint - (player.transform.position + Vector3.up * 1f + fwdDir * 0.8f)).normalized * 34f, true, 0.11f, "bullet_frost", 2f, 2);
        shard.slow = 0.5f;
        yield return new WaitForSeconds(0.4f);
        Check("frost: slows the target", d.Slowed && d.hp < 100, $"slowed={d.Slowed} hp={d.hp} target={d.transform.position} aim={player.AimPoint()} player={player.transform.position}");
        Destroy(d.gameObject);

        // Thunder locks on to an enemy off the crosshair and the current conducts to the next one.
        player.Equip(Weapons.Get("thunder"));
        var a = Dummy(player.transform.position + Quaternion.Euler(0f, 20f, 0f) * fwdDir * 6f);
        var b = Dummy(a.transform.position + sideDir * 4f);
        yield return new WaitForSeconds(0.4f);
        player.Attack();
        yield return new WaitForSeconds(0.4f);
        Check("thunder: locks on off the crosshair and conducts to the next enemy", a.hp < 100 && b.hp < 100, $"a={a.hp} b={b.hp}");
        Destroy(a.gameObject);
        Destroy(b.gameObject);

        // Missiles home in on a target off to the side.
        player.Equip(Weapons.Get("missile"));
        // 30 deg off and 5 m out keeps the target clear of the corner braziers.
        d = Dummy(player.transform.position + Quaternion.Euler(0f, 30f, 0f) * fwdDir * 5f);
        yield return new WaitForSeconds(0.4f);
        player.Attack();
        yield return new WaitForSeconds(1.8f);
        Check("missile: homes onto an off-axis target", d.hp < 100, $"hp={d.hp}");
        Destroy(d.gameObject);

        // Grenade bounces then explodes near the target.
        player.Equip(Weapons.Get("grenade"));
        d = Dummy(CrosshairGround());
        yield return new WaitForSeconds(0.4f);
        player.Attack();
        yield return new WaitForSeconds(1.6f);
        Check("grenade: explodes on a fuse", d.hp < 100, $"hp={d.hp}");
        Destroy(d.gameObject);

        // Boomerang goes out, comes back.
        player.Equip(Weapons.Get("boomerang"));
        d = Dummy(CrosshairGround());
        yield return new WaitForSeconds(0.4f);
        player.Attack();
        yield return null;
        Check("boomerang: is out after the throw", player.BoomerangOut);
        yield return new WaitForSeconds(2.5f);
        Check("boomerang: hits and returns", !player.BoomerangOut && d.hp < 100, $"out={player.BoomerangOut} hp={d.hp}");
        Destroy(d.gameObject);

        // Spear: long and narrow.
        player.Equip(Weapons.Get("spear"));
        var front = Dummy(player.transform.position + fwdDir * 4f);
        var off = Dummy(player.transform.position + Quaternion.Euler(0f, 45f, 0f) * fwdDir * 4f);
        yield return new WaitForSeconds(0.4f);
        player.Attack();
        yield return new WaitForSeconds(0.5f);
        Check("spear: reaches 4 m ahead but not 45 deg off", front.hp < 100 && off.hp == 100, $"front={front.hp} off={off.hp}");
        Destroy(front.gameObject);
        Destroy(off.gameObject);

        player.Equip(Weapons.Get("pistol"));
        player.energy = player.maxEnergy = RunState.maxEnergy;
        RunState.critChance = critWas;
        yield return new WaitForSeconds(0.5f);
    }

    /// The combat power system (Balance): each weapon, fired for three seconds at a dummy that cannot
    /// die, must deal within 15% of the DPS its rarity and role call for (10% design tolerance plus
    /// misses and frame timing). Each result is logged as a BALANCE line with the hits and seconds it
    /// takes to kill the common enemies on floors 1 and 4.
    IEnumerator BalanceTests()
    {
        float critWas = RunState.critChance, rateWas = RunState.fireRateMul, meleeWas = RunState.meleeMul, bonusWas = RunState.damageBonus;
        RunState.critChance = 0f;
        RunState.fireRateMul = 1f;
        RunState.meleeMul = 1f;
        RunState.damageBonus = 0f;
        var held = player.Weapon;
        player.hp = player.maxHp = 60;
        var all = new List<WeaponDef>(Weapons.All);
        all.AddRange(Weapons.SkillWeapons);
        var modelOff = new List<string>();
        var measuredOff = new List<string>();
        foreach (var w in all)
        {
            float target = Balance.TargetDps(w);
            if (Mathf.Abs(w.Dps / target - 1f) > Balance.BandTolerance) modelOff.Add($"{w.id} {w.Dps:0.0}/{target:0.0}");

            player.Teleport(dungeon.playerStart);
            player.energy = player.maxEnergy = 99999;
            yield return AimAt(w.melee ? 1.8f : w.id is "shotgun" or "flamer" ? 2.5f : 4f);
            player.Equip(w);
            var d = Dummy(CrosshairGround());
            d.hp = d.maxHp = 1000000;
            d.pinned = true; // stays where it stands, as if you kept after it
            yield return AimAtBody(d);
            yield return new WaitForSeconds(0.4f);
            var times = new List<float>();
            var amounts = new List<int>();
            int last = d.hp;
            float t0 = Time.time, bowT = 0f;
            const float fireFor = 3f;
            while (Time.time - t0 < fireFor + 0.3f)
            {
                if (Time.time - t0 >= fireFor) player.testTrigger = false;
                else if (w.chargeable) player.testTrigger = !player.testTrigger; // taps, never a charge
                else if (w.kind == WeaponKind.Charge)
                {
                    // The bow: draw in full, loose, draw again.
                    bowT += Time.deltaTime;
                    player.testTrigger = bowT < Weapons.ChargeTime + w.interval + 0.05f;
                    if (!player.testTrigger) bowT = 0f;
                }
                else player.testTrigger = true;
                Combo.Break(); // no streak bonus in the measurement
                yield return null;
                if (d.hp != last)
                {
                    // One volley is one hit: a shotgun's pellets can land over two frames, and the half that
                    // came a frame late was counted on top of the steady rate (shotgun 36.8 for 32.7, r56).
                    if (times.Count > 0 && Time.time - times[times.Count - 1] < 0.05f) amounts[amounts.Count - 1] += last - d.hp;
                    else
                    {
                        times.Add(Time.time);
                        amounts.Add(last - d.hp);
                    }
                    last = d.hp;
                }
            }
            player.testTrigger = false;
            // The steady rate: everything after the first hit over the time from the first hit to the last.
            float dps = 0f;
            if (times.Count >= 2)
            {
                int sum = 0;
                for (int i = 1; i < amounts.Count; i++) sum += amounts[i];
                dps = sum / Mathf.Max(0.01f, times[times.Count - 1] - times[0]);
            }
            if (Mathf.Abs(dps / target - 1f) > 0.15f) measuredOff.Add($"{w.id} {dps:0.0}/{target:0.0}");
            string Kill(EnemyKind k, int f)
            {
                int hp = Balance.EnemyHp(k, f);
                return dps > 0f ? $"{hp / dps:0.00}s" : "-";
            }
            Debug.Log($"BALANCE {w.id} rarity={w.rarity} target={target:0.0} model={w.Dps:0.0} measured={dps:0.0} hits={times.Count} energy/s={w.EnergyRate:0.0} " +
                      $"ttk gunner f1={Kill(EnemyKind.Gunner, 1)} f4={Kill(EnemyKind.Gunner, 4)} charger f1={Kill(EnemyKind.Charger, 1)} guard f4={Kill(EnemyKind.Guard, 4)}");
            Destroy(d.gameObject);
            yield return new WaitForSeconds(0.3f);
        }
        Check("every weapon's numbers sit in its rarity band (designed DPS within 10%)", modelOff.Count == 0, string.Join(", ", modelOff));
        Check("every weapon really deals its band's DPS (measured within 15%)", measuredOff.Count == 0, string.Join(", ", measuredOff));
        RunState.SawWeapon("missile");
        Check("the weapon handbook remembers what you have held, between runs", RunState.Known("missile") && RunState.Known("pistol") && !RunState.Known("no_such_weapon"));
        Check("enemy health grows 30% a floor", Balance.EnemyHp(EnemyKind.Gunner, 1) == 24 && Balance.EnemyHp(EnemyKind.Gunner, 4) == Mathf.RoundToInt(24 * 1.9f));

        // A beam's ticks on one enemy add up into one damage number; a crit still gets its own.
        var tally = Dummy(Ahead(25f));
        yield return null;
        int numbers0 = hud.DamageNumbers;
        for (int i = 0; i < 5; i++) tally.TakeDamage(3, Vector3.forward);
        int merged = hud.DamageNumbers - numbers0;
        tally.TakeDamage(3, Vector3.forward, true);
        Check("quick hits on one enemy add up into one damage number (crits apart)", merged == 1 && hud.DamageNumbers - numbers0 == 2, $"plain={merged} total={hud.DamageNumbers - numbers0}");
        Destroy(tally.gameObject);


        RunState.critChance = critWas;
        RunState.fireRateMul = rateWas;
        RunState.meleeMul = meleeWas;
        RunState.damageBonus = bonusWas;
        player.Equip(held);
        player.energy = player.maxEnergy = RunState.maxEnergy;
        yield return new WaitForSeconds(0.3f);
    }

    IEnumerator MonsterTests()
    {
        player.hp = player.maxHp = 60;
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        var gunner = Dummy(player.transform.position + fwdDir * 5.5f, EnemyKind.Gunner);
        gunner.target = player;
        int moving = 0, facing = 0;
        for (float u = 0; u < 4.5f; u += Time.deltaTime)
        {
            if (gunner.LastMove.sqrMagnitude > 0.05f)
            {
                moving++;
                var mv = new Vector3(gunner.LastMove.x, 0f, gunner.LastMove.z);
                if (Vector3.Angle(gunner.transform.forward, mv) < 45f) facing++;
            }
            yield return null;
        }
        Check("enemies face the way they walk", moving > 10 && facing >= moving * 0.8f, $"facing {facing}/{moving} frames");
        Check("gunner aims before every shot", gunner.shots >= 1 && gunner.shots <= gunner.aims, $"aims={gunner.aims} shots={gunner.shots}");
        Destroy(gunner.gameObject);

        var charger = Dummy(player.transform.position + fwdDir * 8f, EnemyKind.Charger);
        charger.target = player;
        int taken = player.damageTaken;
        for (float u = 0; u < 4f && charger.lunges == 0; u += Time.deltaTime) yield return null;
        Check("charger winds up, then lunges", charger.windups >= 1 && charger.lunges >= 1, $"windups={charger.windups} lunges={charger.lunges}");
        yield return new WaitForSeconds(0.6f);
        Destroy(charger.gameObject);
        player.hp = player.maxHp = RunState.maxHp;
        player.armor = player.maxArmor;
        yield return new WaitForSeconds(0.7f);
    }

    IEnumerator SkillTests()
    {
        Check("four playable classes", Classes.All.Count == 4 && Classes.All.TrueForAll(c => Resources.Load<GameObject>($"Models/Chars/{c.model}/rigged")), string.Join(",", Classes.All.ConvertAll(c => c.model)));
        var skill = player.skill;
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        player.hp = player.maxHp = 40;

        // Knight: the oath sword and shield; the shield takes three hits, nothing can be swapped meanwhile.
        skill.Setup(player, Classes.Get("knight"));
        string before = player.Weapon.id;
        Check("knight skill draws the oath sword and shield", skill.TryUse() && player.Weapon.id == "oath_sword" && player.guardCharges == PlayerController.GuardMax,
            $"weapon={player.Weapon.id} blocks={player.guardCharges}");
        int taken = player.damageTaken;
        yield return new WaitForSeconds(0.7f); // past any hit invulnerability from earlier checks
        for (int i = 0; i < PlayerController.GuardMax; i++)
        {
            player.TakeDamage(1);
            yield return new WaitForSeconds(0.4f);
        }
        Check("the oath shield blocks three hits", player.damageTaken == taken && player.guardCharges == 0, $"taken={player.damageTaken - taken} left={player.guardCharges}");
        player.TakeDamage(1);
        Check("the fourth hit gets through", player.damageTaken == taken + 1);
        player.Swap();
        Check("no swapping while a skill weapon is out", player.Weapon.id == "oath_sword");
        Check("skill goes on cooldown", !skill.TryUse() && skill.cooldownLeft > 0f);
        Check("skill weapons never drop, sell or turn up in chests", Weapons.All.TrueForAll(w => !w.IsSkillWeapon) && Weapons.SkillWeapons.Count == 4);
        yield return new WaitForSeconds(PlayerSkill.OathTime - 1f);
        Check("the old weapon comes back when the skill ends", player.Weapon.id == before && !player.HoldingSkillWeapon, $"weapon={player.Weapon.id}");

        // Assassin: hidden, enemies lose the lock and nothing lands; the darts fly in a fan, more the
        // longer they were charged, and throwing does not end stealth.
        skill.Setup(player, Classes.Get("assassin"));
        var g = Dummy(player.transform.position + fwdDir * 7f, EnemyKind.Gunner);
        g.target = player;
        yield return new WaitForSeconds(0.4f);
        skill.TryUse();
        Check("stealth puts the raven darts in hand", player.Weapon.id == "raven_darts");
        yield return new WaitForSeconds(1.2f);
        float off = Vector3.Distance(g.TargetPos, player.transform.position);
        Check("stealth: enemies lose the lock (they hunt a decoy, not the player)", player.Stealthed && off > 0.5f, $"decoy {off:F1} m away");
        int hpWas = player.hp, armorWas = player.armor;
        player.TakeDamage(3);
        Check("stealth: nothing lands (invincible)", player.hp == hpWas && player.armor == armorWas, $"hp {hpWas}->{player.hp} armor {armorWas}->{player.armor}");
        Destroy(g.gameObject);
        yield return AimAt(4f);
        var mark = Dummy(CrosshairGround()); // no target: it stands still
        yield return new WaitForSeconds(0.3f);
        int thrown0 = player.DartsThrown;
        yield return Hold(0.05f);
        yield return new WaitForSeconds(0.35f);
        int tapDarts = player.DartsThrown - thrown0;
        yield return Hold(Weapons.ChargeTime + 0.1f);
        yield return new WaitForSeconds(0.4f);
        int fullDarts = player.DartsThrown - thrown0 - tapDarts;
        Check("darts: a tap throws one, a full charge a fan of seven", tapDarts == 1 && fullDarts == 7, $"tap={tapDarts} full={fullDarts}");
        Check("the darts hit", mark.hp < 100, $"hp={mark.hp}");
        Check("throwing does not end stealth", player.Stealthed);
        Destroy(mark.gameObject);
        yield return new WaitForSeconds(PlayerSkill.RavenTime);
        Check("stealth and the darts end together", !player.Stealthed && !player.HoldingSkillWeapon);

        // Mage: meteors fall on the crosshair area.
        skill.Setup(player, Classes.Get("mage"));
        yield return AimAt(4f);
        var target = Dummy(CrosshairGround());
        yield return new WaitForSeconds(0.4f);
        skill.TryUse();
        yield return new WaitForSeconds(2.6f);
        Check("meteor storm hits the target area", target.hp < 100, $"hp={target.hp}");
        Destroy(target.gameObject);
        var staffTarget = Dummy(CrosshairGround());
        yield return new WaitForSeconds(0.3f);
        Check("the storm puts the star staff in hand", player.Weapon.id == "star_staff");
        player.Attack();
        yield return new WaitForSeconds(1.2f);
        Check("the star staff drops a meteor on the crosshair", staffTarget.hp < 100, $"hp={staffTarget.hp}");
        Destroy(staffTarget.gameObject);

        // Priest: sanctuary heals hp and armor.
        skill.Setup(player, Classes.Get("priest"));
        player.hp = 1;
        player.armor = 0;
        skill.TryUse();
        yield return new WaitForSeconds(2.2f);
        Check("sanctuary restores hp and armor", player.hp >= 2 && player.armor >= 2, $"hp={player.hp} armor={player.armor}");
        yield return new WaitForSeconds(3f);
        // The dawn scepter's light gives armour back while it is on an enemy.
        player.GrantSkillWeapon("dawn_scepter", 3f);
        yield return AimAt(4f);
        var lit = Dummy(CrosshairGround());
        player.armor = 0;
        yield return new WaitForSeconds(0.3f);
        player.testTrigger = true;
        yield return new WaitForSeconds(1.6f);
        player.testTrigger = false;
        Check("the dawn scepter's beam burns and restores armour", lit.hp < 100 && player.armor >= 1, $"hp={lit.hp} armor={player.armor}");
        Destroy(lit.gameObject);
        player.EndSkillWeapon();

        skill.Setup(player, Classes.Get(RunState.classId));
        player.hp = player.maxHp = RunState.maxHp;
        player.armor = player.maxArmor;
        player.pitch = 12f;
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator PhysicsTests()
    {
        player.hp = player.maxHp = 40;
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 side = Quaternion.Euler(0f, 90f, 0f) * fwdDir;

        // Bullets shove a crate.
        yield return AimAt(4f);
        var crate = PhysicsProp.Make(Kit.Spawn("crate", CrosshairGround() - Vector3.up * 0.05f, Quaternion.identity, null), 18f, 99);
        yield return new WaitForSeconds(0.6f);
        Vector3 c0 = crate.transform.position;
        player.Equip(Weapons.Get("rifle"));
        player.energy = player.maxEnergy;
        for (int i = 0; i < 4; i++) { player.Attack(); yield return new WaitForSeconds(0.45f); }
        Check("bullets push physical crates", crate && Vector3.Distance(crate.transform.position, c0) > 0.15f, crate ? $"moved {Vector3.Distance(crate.transform.position, c0):F2} m, hits={crate.hits}" : "gone");
        // Melee breaks crates too (it used to pass straight through them).
        var box = PhysicsProp.Make(Kit.Spawn("crate", player.transform.position + fwdDir * 1.8f, Quaternion.identity, null), 18f, 6);
        yield return new WaitForSeconds(0.4f);
        player.Equip(Weapons.Get("sword"));
        for (int i = 0; i < 3 && box && !box.broken; i++) { player.Attack(); yield return new WaitForSeconds(0.5f); }
        Check("a sword breaks a crate", !box || box.broken, box ? $"hits={box.hits}" : "gone");
        if (crate) Destroy(crate.gameObject);

        // Explosive barrels chain and hurt enemies nearby.
        Vector3 b1 = player.transform.position + fwdDir * 4f - side * 2f, b2 = b1 + side * 2.2f;
        var barrelA = PhysicsProp.Make(Props.Spawn("barrel", b1, Quaternion.identity, null), 12f, 2, true);
        var barrelB = PhysicsProp.Make(Props.Spawn("barrel", b2, Quaternion.identity, null), 12f, 2, true);
        var victim = Dummy(b2 + side * 1.4f);
        yield return new WaitForSeconds(0.5f);
        barrelA.Hit(5, barrelA.transform.position, fwdDir);
        yield return new WaitForSeconds(0.8f);
        Check("explosive barrels chain", barrelA == null && barrelB == null);
        Check("barrel blasts hurt enemies", victim == null || victim.hp < 100, victim ? $"hp={victim.hp}" : "killed");
        if (victim) Destroy(victim.gameObject);

        // Ragdoll: a blast-killed enemy flies as a physics body.
        var corpse = Dummy(player.transform.position + fwdDir * 5f + side * 2f);
        corpse.hp = 5;
        yield return new WaitForSeconds(0.5f);
        Explosion.At(corpse.transform.position - fwdDir * 1.2f + Vector3.up * 0.3f, 3f, 20, false);
        yield return null;
        Transform hips = null;
        foreach (var t in corpse.GetComponentsInChildren<Transform>()) if (t.name == "Hips") hips = t;
        bool physical = hips && hips.GetComponent<Rigidbody>() != null;
        Vector3 h0 = hips ? hips.position : Vector3.zero;
        float moved = 0f;
        for (float u = 0; u < 0.7f && hips; u += Time.deltaTime)
        {
            moved = Mathf.Max(moved, Vector3.Distance(hips.position, h0));
            yield return null;
        }
        Check("killed enemies become ragdolls thrown by the blast", physical && moved > 0.2f, physical ? $"hips moved up to {moved:F2} m" : "no ragdoll");

        // Drops bounce before the magnet takes them.
        var orb = Pickup.Spawn(PickupKind.Energy, player.transform.position + fwdDir * 9f, 3);
        float maxUp = 0f;
        for (float u = 0; u < 0.3f && orb; u += Time.deltaTime)
        {
            yield return new WaitForFixedUpdate(); // the launch applies on the next physics step
            if (orb && orb.body) maxUp = Mathf.Max(maxUp, orb.body.linearVelocity.y);
        }
        Check("drops are physical", orb && orb.body != null && maxUp > 0.5f, $"max vy={maxUp:F1}");
        yield return new WaitForSeconds(1.5f);
        Check("drops land on the floor", orb && orb.transform.position.y < 0.6f, orb ? $"y={orb.transform.position.y:F2}" : "collected");
        if (orb) Destroy(orb.gameObject);

        player.Equip(Weapons.Get("pistol"));
        player.hp = player.maxHp = RunState.maxHp;
        player.pitch = 12f;
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator GripAndChargeTests()
    {
        player.hp = player.maxHp = 40;
        player.energy = player.maxEnergy = 999;
        float critWas = RunState.critChance;
        RunState.critChance = 0f;
        Transform rh = player.BoneForTest("RightHand"), lh = player.BoneForTest("LeftHand");
        if (rh && lh)
        {
            player.Equip(Weapons.Get("pistol"));
            yield return null;
            yield return null;
            var rsb = player.BoneForTest("RightArm");
            Check("one-hand grip: right hand on the pistol", Vector3.Distance(rh.position, player.WeaponPivotForTest.position) < 0.25f,
                $"{Vector3.Distance(rh.position, player.WeaponPivotForTest.position):F2} m | hand={rh.position} pivot={player.WeaponPivotForTest.position} shoulder={(rsb ? rsb.position : Vector3.zero)} pivotParent={player.WeaponPivotForTest.parent?.name} dead={player.dead} y={player.transform.position.y:F2}");
            player.Equip(Weapons.Get("rifle"));
            player.Attack(); // raise it to aim
            yield return null;
            yield return null;
            var pv = player.WeaponPivotForTest;
            Vector3 fore = player.Foregrip(pv.position, false);
            Check("two-hand grip: right hand on the grip, left hand on the barrel",
                Vector3.Distance(rh.position, pv.position) < 0.25f && Vector3.Distance(lh.position, fore) < 0.3f,
                $"right {Vector3.Distance(rh.position, pv.position):F2} m, left {Vector3.Distance(lh.position, fore):F2} m | ik {player.ikDebugLeft} now={Time.frameCount}");
            // Blades are held by the grip: the hand sat on the crossguard once and vanished inside it.
            foreach (var (id, from, to) in new[] { ("sword", 0.1f, 0.27f), ("oath_sword", 0.15f, 0.32f), ("daggers", 0.1f, 0.32f) })
            {
                player.Equip(Weapons.Get(id).Clone());
                yield return null;
                var piv = player.WeaponPivotForTest;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var r in player.WeaponVisualForTest.GetComponentsInChildren<Renderer>())
                {
                    var m = piv.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var b = r.localBounds;
                    for (int i = 0; i < 8; i++)
                    {
                        float z = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)).z;
                        lo = Mathf.Min(lo, z);
                        hi = Mathf.Max(hi, z);
                    }
                }
                float k = -lo / Mathf.Max(0.01f, hi - lo); // where the hand is, from the butt
                Check($"{id}: held by the grip, not the crossguard", k > from && k < to, $"hand at {k * 100f:0}% of its length (grip {from * 100f:0}-{to * 100f:0}%)");
            }
            player.Equip(Weapons.Get("bow"));
            yield return null;
            yield return null;
            Check("bow grip: left hand holds the bow", Vector3.Distance(lh.position, player.WeaponPivotForTest.position) < 0.25f, $"{Vector3.Distance(lh.position, player.WeaponPivotForTest.position):F2} m | ik {player.ikDebugLeft} now={Time.frameCount}");
        }
        else Check("character rig has hand bones", false);

        // A staff is raised straight up to cast (it went flat once when a later line re-aimed every
        // ranged weapon at the crosshair after the pose).
        var heldBefore = player.Weapon;
        player.Equip(Weapons.Get("thunder").Clone());
        for (float u = 0f; u < 0.6f; u += Time.deltaTime) { player.HoldAim(); yield return null; }
        yield return new WaitForEndOfFrame();
        float tilt = Vector3.Angle(player.WeaponPivotForTest.forward, Vector3.up);
        Check("casting raises the staff straight up", tilt < 20f, $"{tilt:F0} deg from vertical");
        player.Equip(heldBefore);
        Check("only some weapons charge", Weapons.All.FindAll(x => x.chargeable).Count == 4 && !Weapons.Get("pistol").chargeable && !Weapons.Get("daggers").chargeable);

        // Charged rifle shot outdamages a tap by far.
        player.Equip(Weapons.Get("rifle"));
        yield return AimAt(4f);
        var d = Dummy(CrosshairGround());
        yield return new WaitForSeconds(0.4f);
        yield return Hold(0.05f);
        yield return new WaitForSeconds(0.4f);
        int tap = 100 - d.hp;
        yield return Hold(1.4f);
        yield return new WaitForSeconds(0.4f);
        int held = 100 - d.hp - tap;
        Check("rifle: holding charges a much heavier shot", tap > 0 && held >= tap * 3, $"tap={tap} held={held}");
        Destroy(d.gameObject);

        // Hammer slam reaches all around, including behind.
        player.Equip(Weapons.Get("hammer"));
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        var behind = Dummy(player.transform.position - fwdDir * 2.5f);
        yield return new WaitForSeconds(0.4f);
        yield return Hold(1.4f);
        yield return new WaitForSeconds(0.7f); // the slam lands with the hammer
        Check("hammer: charged slam hits enemies behind you", behind.hp < 100, $"hp={behind.hp}");
        Destroy(behind.gameObject);

        // Spear lunge carries you forward.
        player.Equip(Weapons.Get("spear"));
        player.stamina = player.maxStamina;
        yield return new WaitForSeconds(0.9f);
        Vector3 p0 = player.transform.position;
        yield return Hold(1.2f);
        yield return new WaitForSeconds(0.4f);
        Check("spear: charged thrust lunges forward", Vector3.Distance(player.transform.position, p0) > 1.5f, $"moved {Vector3.Distance(player.transform.position, p0):F1} m");

        RunState.critChance = critWas;
        player.Equip(Weapons.Get("pistol"));
        player.hp = player.maxHp = RunState.maxHp;
        player.energy = player.maxEnergy = RunState.maxEnergy;
        player.Teleport(dungeon.playerStart);
        player.pitch = 12f;
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator TrapTests()
    {
        player.hp = player.maxHp = 40;
        player.armor = player.maxArmor;
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 side = Quaternion.Euler(0f, 90f, 0f) * fwdDir;
        Vector3 home = dungeon.playerStart;

        // Fire pit: idle, warning, then the column burns whoever stands on it.
        var pit = FirePit.Create(home + fwdDir * 3f, null, 0f);
        for (float u = 0; u < 3f && pit.phase != FirePit.Phase.Burning; u += Time.deltaTime) yield return null;
        Check("fire pit warns, then erupts", pit.phase == FirePit.Phase.Burning);
        int taken = player.damageTaken;
        player.Teleport(pit.transform.position + Vector3.up * 0.1f);
        var burnt = Dummy(pit.transform.position + side * 0.6f);
        yield return new WaitForSeconds(0.8f);
        Check("fire hurts the player standing in it", player.damageTaken > taken, $"taken={player.damageTaken - taken}");
        Check("fire hurts and ignites enemies too", burnt.hp < 100, $"hp={burnt.hp} burning={burnt.Burning}");
        Destroy(burnt.gameObject);
        Destroy(pit.gameObject);
        player.Teleport(home);
        yield return new WaitForSeconds(0.8f);

        // Spikes: step on, 0.4 s later they come up.
        var spikes = SpikeTrap.Create(home + side * 3f, null);
        taken = player.damageTaken;
        player.Teleport(spikes.transform.position + Vector3.up * 0.1f);
        yield return new WaitForSeconds(0.7f);
        Check("spikes fire under the player", spikes.fired >= 1 && player.damageTaken > taken, $"fired={spikes.fired} taken={player.damageTaken - taken}");
        player.Teleport(home);
        yield return new WaitForSeconds(1.5f);
        var stepper = Dummy(spikes.transform.position);
        yield return new WaitForSeconds(0.9f);
        Check("spikes hit enemies that walk on them", stepper.hp < 100, $"hp={stepper.hp}");
        Destroy(stepper.gameObject);
        Destroy(spikes.gameObject);

        // Gas canister: break it, the cloud poisons everyone inside.
        if (Props.Has("gas_crate"))
        {
            var can = PhysicsProp.Make(Props.Spawn("gas_crate", home - side * 3f, Quaternion.identity, null), 14f, 3);
            can.toxic = true;
            var victim = Dummy(home - side * 4.5f);
            yield return new WaitForSeconds(0.4f);
            can.Hit(9, can.transform.position, side);
            yield return null;
            Check("a broken gas canister releases a poison cloud", FindAnyObjectByType<PoisonCloud>() != null);
            player.Teleport(home - side * 2.5f);
            taken = player.damageTaken;
            yield return new WaitForSeconds(2.2f);
            Check("the cloud poisons the player", player.poisoned > 0f && player.damageTaken > taken, $"poisoned={player.poisoned:F1} taken={player.damageTaken - taken}");
            Check("the cloud hurts enemies", victim.hp < 100, $"hp={victim.hp}");
            Destroy(victim.gameObject);
            player.Teleport(home);
            player.poisoned = 0f;
            foreach (var c in FindObjectsByType<PoisonCloud>(FindObjectsSortMode.None)) Destroy(c.gameObject);
        }
        else Check("gas canister model imported", false);

        // Ice makes the floor slippery.
        var ice = IcePatch.Create(home, null);
        yield return null;
        yield return null;
        Check("ice patch makes you slide", player.onIce > 0f);
        Destroy(ice.gameObject);

        player.hp = player.maxHp = RunState.maxHp;
        player.armor = player.maxArmor;
        yield return new WaitForSeconds(0.7f);
    }

    IEnumerator LevelTests()
    {
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 side = Quaternion.Euler(0f, 90f, 0f) * fwdDir;
        Vector3 home = dungeon.playerStart;
        player.Teleport(home);
        Debug.Log($"AUTOTEST info layout: rooms={dungeon.rooms.Count} loops={dungeon.loops} altar={dungeon.rooms.Exists(r => r.kind == Dungeon.Kind.Altar)} trial={dungeon.trialRooms.Count}");
        Check("every room is reachable (with loops and optional rooms)", dungeon.AllRoomsConnected());

        // Blood altar: costs 2 max HP, offers three blessings, the pick applies.
        var altar = Altar.Create(home + side * 3f, null);
        int savedMax = player.maxHp, savedRunMax = RunState.maxHp, buffs = RunState.buffs.Count;
        player.maxHp = player.hp = 10;
        altar.Use(player);
        Check("the blood altar takes 2 max health and offers blessings", player.maxHp == 8 && hud.screen == Hud.Screen.Buff, $"max={player.maxHp} screen={hud.screen}");
        hud.PickBuff(0);
        yield return null;
        Check("picking the altar's blessing applies it and resumes", RunState.buffs.Count == buffs + 1 && hud.screen == Hud.Screen.None && !TimeControl.paused);
        Destroy(altar.gameObject);
        player.maxHp = savedMax;
        RunState.maxHp = savedRunMax;
        player.hp = player.maxHp = 40;

        // Trial room: the statue starts it, everything in it is an elite, winning leaves an epic chest.
        if (dungeon.trialRooms.Count > 0)
        {
            var trial = dungeon.trialRooms[0];
            var statue = FindAnyObjectByType<TrialStatue>();
            player.Teleport(trial.Center() + Vector3.forward * 3.5f);
            yield return new WaitForSeconds(0.5f);
            Check("a trial room waits for its statue", trial.state == Room.State.Idle);
            statue.Use(player);
            yield return new WaitForSeconds(2.2f);
            Check("the statue starts the trial with elites", trial.state == Room.State.Fighting && Enemy.Elites.Count > 0, $"state={trial.state} elites={Enemy.Elites.Count}");
            for (float u = 0; u < 25f && trial.state != Room.State.Cleared; u += 0.3f)
            {
                foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                    if (!e.dead && trial.Center() != Vector3.zero && Vector3.Distance(e.transform.position, trial.Center()) < 20f) e.TakeDamage(9999, Vector3.forward);
                player.hp = player.maxHp;
                yield return new WaitForSeconds(0.3f);
            }
            Check("winning the trial leaves an epic chest", trial.state == Room.State.Cleared && FindObjectsByType<Chest>(FindObjectsSortMode.None).Length > 0, $"state={trial.state}");
            player.Teleport(home);
            yield return new WaitForSeconds(1.5f);
        }
        player.hp = player.maxHp = RunState.maxHp;
        player.armor = player.maxArmor;
    }

    IEnumerator ReactionTests()
    {
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 side = Quaternion.Euler(0f, 90f, 0f) * fwdDir;
        Vector3 home = dungeon.playerStart;
        player.Teleport(home);
        player.hp = player.maxHp = 40;

        // 碎冰: a heavy hit on a frozen enemy deals double and breaks the ice.
        var frozen = Dummy(home + fwdDir * 4f);
        frozen.ApplySlow(0.5f, 3f);
        int shatters = Reactions.Shatters;
        frozen.TakeDamage(Balance.HeavyHit, fwdDir);
        Check("碎冰: a heavy hit on a frozen enemy deals double", frozen.hp == 100 - 2 * Balance.HeavyHit && Reactions.Shatters == shatters + 1 && !frozen.Slowed, $"hp={frozen.hp}");
        frozen.TakeDamage(Balance.HeavyHit, fwdDir);
        Check("碎冰 uses up the ice (next hit is normal)", frozen.hp == 100 - 3 * Balance.HeavyHit, $"hp={frozen.hp}");

        // 超载: lightning on a burning enemy blasts it and its neighbours.
        // Well inside the room (its corners hold braziers and its walls are 6 m out).
        var burning = Dummy(home + fwdDir * 2f + side * 1.5f);
        var neighbour = Dummy(home + fwdDir * 2f + side * 2.8f);
        yield return new WaitForSeconds(0.5f);
        burning.ApplyBurn(3f);
        int overloads = Reactions.Overloads;
        Reactions.Shock(burning);
        Check("超载: lightning on a burning enemy hits its neighbours", Reactions.Overloads == overloads + 1 && neighbour.hp < 100 && !burning.Burning,
            $"overloads={Reactions.Overloads - overloads} neighbour={neighbour.hp} apart={Vector3.Distance(burning.AimPoint, neighbour.AimPoint):F2} burningHp={burning.hp}");

        // 爆燃: a flame shot through a poison cloud blows it up.
        var cloud = PoisonCloud.Create(home - side * 4f);
        var inGas = Dummy(home - side * 4.6f);
        yield return new WaitForSeconds(0.4f);
        int combustions = Reactions.Combustions, hpBefore = inGas.hp;
        var flame = Bullet.Fire(home - side * 4f + fwdDir * 3f + Vector3.up, -fwdDir * 12f, true, 0.2f, "bullet_fire", 2f, 1);
        flame.burn = 2f;
        yield return new WaitForSeconds(0.6f);
        Check("爆燃: fire through poison gas explodes the cloud", Reactions.Combustions == combustions + 1 && cloud == null && inGas.hp <= hpBefore - 8,
            $"combustions={Reactions.Combustions - combustions} hp {hpBefore} -> {inGas.hp}");
        foreach (var e in new[] { frozen, burning, neighbour, inGas }) if (e) Destroy(e.gameObject);

        // Loot: a dropped weapon has a light pillar; a forced rare drop respects the floor; pity resets.
        RunState.weaponPity = 3;
        var drop = Pickup.DropWeapon(home + fwdDir * 2f, 3);
        Check("dropped weapons shine (a light pillar) and respect the minimum rarity", drop.weapon.rarity >= 3 && drop.transform.Find("loot_beam") && RunState.weaponPity == 0,
            $"rarity={drop.weapon.rarity} pity={RunState.weaponPity}");
        Destroy(drop.gameObject);
        player.hp = player.maxHp = RunState.maxHp;
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator PetTests()
    {
        var pet = Pet.Current;
        Check("a companion drone comes along", pet != null);
        if (!pet) yield break;
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 home = dungeon.playerStart;
        player.Teleport(home);
        pet.gameObject.SetActive(true);
        pet.transform.position = home + Vector3.up;
        yield return new WaitForSeconds(0.8f);
        Vector3 off = pet.transform.position - player.transform.position;
        Check("the drone hovers beside the player at chest height", off.magnitude < 2f && off.y > 0.8f && off.y < 1.6f, $"offset={off}");

        var foe = Dummy(home + fwdDir * 5f);
        int shots = pet.shots;
        yield return new WaitForSeconds(2.5f);
        Check("the drone shoots enemies it can see", pet.shots > shots && foe.hp < 100, $"shots={pet.shots - shots} hp={foe.hp}");
        Destroy(foe.gameObject);

        int hp0 = pet.hp;
        var shot = Bullet.Fire(pet.transform.position + fwdDir * 2f, -fwdDir * 12f, false, 0.12f, "bullet_enemy", 3f, 2);
        yield return new WaitForSeconds(0.4f);
        Check("the drone blocks enemy shots and takes the damage", pet.hp == hp0 - 2, $"hp {hp0} -> {pet.hp}");

        // Sanctuary (the priest's skill) heals allies inside it, the drone included.
        var priest = Classes.Get("priest");
        player.skill.Setup(player, priest);
        player.skill.TryUse();
        yield return new WaitForSeconds(1.3f);
        Check("Sanctuary heals the drone", pet.hp > hp0 - 2, $"hp={pet.hp}");
        player.skill.Setup(player, Classes.Get(RunState.classId));
        yield return new WaitForSeconds(4f); // let the sanctuary end

        pet.TakeDamage(999);
        Check("at 0 HP the drone goes offline", pet.offline);
        pet.gameObject.SetActive(false);
        player.hp = player.maxHp;
        yield return null;
    }

    IEnumerator EnemyTypeTests()
    {
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 side = Quaternion.Euler(0f, 90f, 0f) * fwdDir;
        Vector3 home = dungeon.playerStart;
        player.Teleport(home);
        player.hp = player.maxHp = 40;
        player.armor = player.maxArmor;
        yield return new WaitForSeconds(0.7f);

        // A bomb thrown at your feet: a circle marks the spot, then it blows (goblins inside get hurt too).
        int blasts = EnemyBomb.Exploded, taken = player.damageTaken;
        var bystander = Dummy(home + side * 1.2f);
        EnemyBomb.Throw(home + fwdDir * 5f + Vector3.up * 1.4f, home, 1.0f, 2.2f);
        yield return new WaitForSeconds(0.5f);
        Check("a bomb marks where it will land first", FindAnyObjectByType<EnemyBomb>() != null && player.damageTaken == taken);
        yield return new WaitForSeconds(0.8f);
        Check("the bomb hurts whoever stays in the circle, goblins too", EnemyBomb.Exploded == blasts + 1 && player.damageTaken > taken && bystander.hp < 100,
            $"blasts={EnemyBomb.Exploded - blasts} taken={player.damageTaken - taken} goblin={bystander.hp}");
        Destroy(bystander.gameObject);
        yield return new WaitForSeconds(0.8f);
        taken = player.damageTaken;
        EnemyBomb.Throw(home + fwdDir * 5f + Vector3.up * 1.4f, home, 1.0f, 2.2f);
        player.Teleport(home + side * 4f);
        yield return new WaitForSeconds(1.3f);
        Check("stepping out of the circle avoids the bomb", player.damageTaken == taken, $"taken={player.damageTaken - taken}");
        player.Teleport(home);

        // The bomber and the archer act on their own.
        var bomber = Dummy(home + fwdDir * 5f, EnemyKind.Bomber);
        bomber.target = player;
        for (float u = 0; u < 7f && bomber.shots == 0; u += Time.deltaTime) yield return null;
        Check("a goblin bomber lobs bombs at the player", bomber.shots > 0, $"aims={bomber.aims} shots={bomber.shots}");
        Destroy(bomber.gameObject);
        var archer = Dummy(home + fwdDir * 5f - side * 2f, EnemyKind.Archer);
        archer.target = player;
        for (float u = 0; u < 7f && archer.shots == 0; u += Time.deltaTime) yield return null;
        Check("a skeleton archer aims, then looses an arrow", archer.shots > 0 && archer.aims > 0, $"aims={archer.aims} shots={archer.shots}");
        Destroy(archer.gameObject);

        // The shield guard turns its tower shield to you: hits from the front glance off, from behind
        // they land, and a heavy blow knocks the shield aside. (Held in place: speed 0, out of bash range.)
        var guard = Dummy(home + fwdDir * 6f, EnemyKind.Guard);
        guard.speed = 0f;
        guard.target = player;
        Vector3 toPlayer = -fwdDir;
        for (float u = 0; u < 4f && Vector3.Dot(guard.transform.forward, toPlayer) < 0.95f; u += Time.deltaTime) yield return null;
        int ghp = guard.hp;
        guard.TakeDamage(3, fwdDir);
        Check("a shield guard blocks hits on its shield", guard.Guarding && guard.hp == ghp && guard.blocks > 0, $"hp {ghp}->{guard.hp} blocks={guard.blocks}");
        guard.TakeDamage(3, guard.transform.forward);
        Check("hits in its back get through", guard.hp == ghp - 3, $"hp {ghp}->{guard.hp}");
        guard.TakeDamage(Balance.HeavyHit, fwdDir);
        Check("a heavy blow breaks its guard", !guard.Guarding && guard.hp < ghp - 3, $"guarding={guard.Guarding} hp={guard.hp}");
        Destroy(guard.gameObject);

        // A mimic: the chest wakes when you come close, lunges to bite, and drops a rare weapon when beaten.
        var fake = Props.Spawn("chest", home + fwdDir * 5f, Quaternion.LookRotation(-fwdDir), dungeon.transform, false);
        var mc = fake.AddComponent<Chest>();
        mc.dungeon = dungeon;
        mc.mimic = true;
        player.Teleport(home + fwdDir * 2.4f);
        yield return new WaitForSeconds(0.3f);
        Check("a mimic chest wakes when you come close", mc.woke != null && mc.woke.kind == EnemyKind.Mimic);
        if (mc.woke)
        {
            var mimic = mc.woke;
            for (float u = 0; u < 5f && mimic.lunges == 0; u += Time.deltaTime) yield return null;
            Check("the mimic lunges to bite", mimic.lunges > 0, $"windups={mimic.windups} lunges={mimic.lunges}");
            Vector3 at = mimic.transform.position;
            mimic.TakeDamage(999, fwdDir);
            yield return new WaitForSeconds(0.4f);
            Check("a beaten mimic drops a rare-or-better weapon", Pickup.Live.Exists(x => x && x.kind == PickupKind.Weapon && x.weapon != null && x.weapon.rarity >= 2 && Vector3.Distance(x.transform.position, at) < 3f));
            foreach (var x in Pickup.Live.ToArray()) if (x && x.kind == PickupKind.Weapon) Destroy(x.gameObject);
        }
        Destroy(fake);
        player.Teleport(home);
        foreach (var b in FindObjectsByType<EnemyBomb>(FindObjectsSortMode.None)) Destroy(b.gameObject);

        // A slime hops after you and lands on you; killed, a big one bursts into two small ones, which
        // just splat. (The room adds the halves a frame later; here they come straight away.)
        var slime = Dummy(home + fwdDir * 6f, EnemyKind.Slime);
        slime.target = player;
        Vector3 s0 = slime.transform.position;
        int st = player.damageTaken;
        for (float u = 0; u < 5f && player.damageTaken == st; u += Time.deltaTime) yield return null;
        Check("a slime hops after you and lands on you", slime.lunges >= 2 && player.damageTaken > st,
            $"hops={slime.lunges} moved={Vector3.Distance(s0, slime.transform.position):0.0} m taken={player.damageTaken - st}");
        var halves = new List<Enemy>();
        System.Action<EnemyKind, Vector3> split = null;
        split = (k, p) => { var h = Dummy(p, k); h.MakeMini(); h.SplitNow = split; halves.Add(h); };
        slime.SplitNow = split;
        slime.TakeDamage(999, fwdDir);
        Check("a killed slime splits into two small ones", halves.Count == 2 && halves.TrueForAll(h => h.mini && h.kind == EnemyKind.Slime), $"halves={halves.Count}");
        foreach (var h in halves.ToArray()) h.TakeDamage(999, fwdDir);
        Check("the small ones don't split again", halves.Count == 2, $"halves={halves.Count}");

        player.hp = player.maxHp = RunState.maxHp;
        player.armor = player.maxArmor;
        yield return new WaitForSeconds(1.0f);
    }

    IEnumerator EliteTests()
    {
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 side = Quaternion.Euler(0f, 90f, 0f) * fwdDir;
        Vector3 home = dungeon.playerStart;
        player.Teleport(home);
        player.hp = player.maxHp;
        player.armor = player.maxArmor;

        var shielded = Dummy(home + fwdDir * 4f + side * 2f);
        shielded.MakeElite(EliteKind.Shielded);
        Check("elites are tougher and tagged", shielded.maxHp == 220 && shielded.hp == 220 && Enemy.Elites.Contains(shielded) && shielded.EliteName.Length > 0,
            $"hp={shielded.hp}/{shielded.maxHp} listed={Enemy.Elites.Contains(shielded)}");
        shielded.TakeDamage(30, fwdDir);
        Check("a shield soaks damage before health", shielded.hp == 220 && shielded.shield == 80, $"hp={shielded.hp} shield={shielded.shield}");
        shielded.TakeDamage(100, fwdDir);
        Check("damage past the shield reaches health", shielded.shield == 0 && shielded.hp == 200, $"hp={shielded.hp} shield={shielded.shield}");

        var frenzied = Dummy(home + fwdDir * 4f - side * 2f);
        float pace = frenzied.speed;
        frenzied.MakeElite(EliteKind.Frenzied);
        Check("frenzied elites are faster and shoot more", frenzied.speed > pace * 1.4f && frenzied.fireMul < 1f, $"speed {pace:F1} -> {frenzied.speed:F1}");

        var splitter = Dummy(home + fwdDir * 6f);
        splitter.MakeElite(EliteKind.Splitting);
        int halves = 0;
        splitter.Split = (k, p) => halves++;
        int gold0 = Pickup.Live.FindAll(p => p.kind == PickupKind.Gold).Count;
        splitter.TakeDamage(9999, fwdDir);
        Check("a splitting elite dies into two", halves == 2, $"halves={halves}");
        Check("elites drop extra gold", Pickup.Live.FindAll(p => p.kind == PickupKind.Gold).Count >= gold0 + 7);

        yield return new WaitForSeconds(0.7f); // past any hit invulnerability
        var bomber = Dummy(home + fwdDir * 1.6f);
        bomber.MakeElite(EliteKind.Volatile);
        yield return new WaitForSeconds(0.4f);
        int taken = player.damageTaken;
        bomber.TakeDamage(9999, fwdDir);
        yield return null;
        Check("a volatile elite warns before it blows", FindAnyObjectByType<DelayedBlast>() != null && player.damageTaken == taken);
        yield return new WaitForSeconds(1.3f);
        Check("then the blast hurts whoever stayed", player.damageTaken > taken, $"taken={player.damageTaken - taken}");

        foreach (var e in new[] { shielded, frenzied }) if (e) Destroy(e.gameObject);
        player.hp = player.maxHp;
        player.armor = player.maxArmor;
        yield return new WaitForSeconds(0.8f);
    }

    IEnumerator ShopTests()
    {
        var shop = dungeon.shopRoom;
        Check("every floor has a shop room", shop != null);
        if (shop == null) yield break;
        var wares = new System.Collections.Generic.List<ShopItem>(ShopItem.Live);
        var keeper = GameObject.Find("merchant");
        Check("the shop has a merchant and six wares, one a mech beacon", keeper && wares.Count == 6 && wares.Exists(x => x.kind == ShopItem.Kind.Mech), $"merchant={(bool)keeper} wares={wares.Count}");
        if (!keeper || wares.Count == 0) yield break;
        Vector3 toDoor = shop.Center - keeper.transform.position;
        toDoor.y = 0f;
        toDoor.Normalize();

        // Coins bounce out and get pulled in.
        player.Teleport(shop.Center + toDoor * 3.5f);
        yield return null;
        int g0 = RunState.gold;
        Pickup.SpawnGold(player.transform.position + toDoor * 1.5f, 3);
        yield return new WaitForSeconds(2.2f);
        Check("coins are picked up", RunState.gold >= g0 + 3, $"gold {g0} -> {RunState.gold}");

        var potion = wares.Find(w => w.kind == ShopItem.Kind.Potion);
        player.Teleport(potion.transform.position + toDoor * 1.2f);
        yield return null;
        yield return null;
        Check("standing at a pedestal offers the ware", player.nearbyWare == potion, $"near={(player.nearbyWare ? player.nearbyWare.Title : "none")}");
        RunState.gold = 0;
        Check("no gold, no sale", !potion.Buy(player) && !potion.sold);
        RunState.gold = 100;
        player.hp = 1;
        Check("buying a potion pays and heals", potion.Buy(player) && potion.sold && RunState.gold == 100 - potion.price && player.hp >= 4,
            $"gold={RunState.gold} hp={player.hp}");
        var gun = wares.Find(w => w.kind == ShopItem.Kind.Weapon);
        int before = Pickup.Live.FindAll(p => p.kind == PickupKind.Weapon).Count;
        int gold1 = RunState.gold;
        bool bought = gun.Buy(player);
        Check("buying a weapon puts it on the floor", bought && Pickup.Live.FindAll(p => p.kind == PickupKind.Weapon).Count == before + 1 && RunState.gold == gold1 - gun.price,
            $"bought={bought} price={gun.price} gold={RunState.gold}");
        foreach (var p in Pickup.Live.ToArray()) if (p.kind == PickupKind.Weapon && p.weapon == gun.weapon) Destroy(p.gameObject);
        // Weapon upgrade: +20% damage on the held weapon (Balance.UpgradeStep), remembered for the run.
        var upgrade = wares.Find(w => w.kind == ShopItem.Kind.Upgrade);
        RunState.gold = 100;
        int dmg0 = player.Weapon.damage, lvl0 = RunState.LevelOf(player.Weapon.id);
        float dps0 = player.Weapon.Dps;
        Check("the weapon upgrade adds +20% damage for the run", upgrade.Buy(player) && RunState.LevelOf(player.Weapon.id) == lvl0 + 1 && player.Weapon.Title.Contains("+")
            && Mathf.Abs(player.Weapon.Dps / dps0 - Balance.LevelMul(lvl0 + 1) / Balance.LevelMul(lvl0)) < 0.01f,
            $"dps {dps0:0.0} -> {player.Weapon.Dps:0.0} title={player.Weapon.Title}");
        Check("upgrades never touch the shared weapon table", Weapons.Get(player.Weapon.id).damage == dmg0 && Weapons.Get(player.Weapon.id).level == 0, $"table={Weapons.Get(player.Weapon.id).damage}");
        // The mech beacon: the only way besides chests to get a mech.
        var beacon = wares.Find(w => w.kind == ShopItem.Kind.Mech);
        RunState.gold = 100;
        int parkedBefore = ParkedMech.Live.Count;
        Check("the mech beacon calls a mech in", beacon.Buy(player) && ParkedMech.Live.Count == parkedBefore + 1, $"parked {parkedBefore} -> {ParkedMech.Live.Count}");
        yield return new WaitForSeconds(1.2f);
        foreach (var m in ParkedMech.Live.ToArray()) if (m) Destroy(m.gameObject);
        RunState.gold = g0;
        player.hp = player.maxHp;
        player.Teleport(dungeon.playerStart);
        yield return new WaitForSeconds(0.3f);
    }

    IEnumerator MechTests()
    {
        player.hp = player.maxHp;
        player.armor = player.maxArmor;
        Vector3 fwdDir = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        Vector3 side = Quaternion.Euler(0f, 90f, 0f) * fwdDir;
        Vector3 home = dungeon.playerStart;
        var cc = player.GetComponent<CharacterController>();
        float footRadius = cc.radius;
        player.Teleport(home);

        // A mech drops in from the sky and lands next to you.
        var parked = ParkedMech.Drop(Mechs.Light, home + fwdDir * 2.6f, player.yaw);
        for (float u = 0; u < 3f && !parked.landed; u += Time.deltaTime) yield return null;
        Check("a mech drops in and lands", parked.landed && parked.transform.position.y < 0.05f, $"y={parked.transform.position.y:F2}");
        yield return null;
        Check("standing next to it offers boarding", player.nearbyMech == parked);
        player.Mount(parked.def, parked.hp, parked);
        yield return null;
        Check("boarding: in the mech with a bigger body", player.InMech && player.mechHp == Mechs.Light.maxHp && cc.radius > footRadius && !parked,
            $"in={player.InMech} hp={player.mechHp} r={cc.radius:F2}");

        // Damage goes to the mech first; the cockpit keeps poison out; medkits repair it.
        int hp0 = player.hp, armor0 = player.armor;
        player.TakeDamage(3);
        Check("mech durability is spent before the pilot's", player.mechHp == Mechs.Light.maxHp - 3 && player.hp == hp0 && player.armor == armor0,
            $"mech={player.mechHp} hp={player.hp} armor={player.armor}");
        player.Poison(3f);
        Check("sealed cockpit: no poison", player.poisoned <= 0f);
        player.Heal(1);
        Check("a medkit repairs the mech", player.mechHp == Mechs.Light.maxHp, $"mech={player.mechHp}");

        // Light mech: the rapid-fire cannon hits what the crosshair is on and heats up.
        // (The mech camera sits higher, so look down a bit more to keep the target inside the room.)
        player.pitch = 22f;
        yield return new WaitForSeconds(0.8f); // let the camera settle at mech height
        // Clear the start room's crates out of the line of fire and aim at the target itself
        // (from the higher mech camera the crosshair ray can clip a crate top first).
        foreach (var prop in PhysicsProp.Live.ToArray())
            if (prop && Vector3.Distance(prop.transform.position, home) < 9f) Destroy(prop.gameObject);
        var target = Dummy(home + fwdDir * 4.5f);
        yield return new WaitForSeconds(0.45f);
        player.aimOverride = target.AimPoint;
        player.testTrigger = true;
        yield return new WaitForSeconds(0.5f);
        player.testTrigger = false;
        player.aimOverride = null;
        yield return new WaitForSeconds(0.2f);
        Check("light mech cannon hits the crosshair target", target.hp < 100 && player.mechShots > 3,
            $"hp={target.hp} shots={player.mechShots} gun={player.MechGunPoint} aim={player.AimPoint()} target={target.transform.position} scale={target.transform.localScale.x:F2} player={player.transform.position}");
        Check("the cannon heats up", player.heat > 0.1f, $"heat={player.heat:F2}");
        Destroy(target.gameObject);
        player.testTrigger = true;
        for (float u = 0; u < 5f && !player.overheated; u += Time.deltaTime) yield return null;
        player.testTrigger = false;
        Check("holding the trigger overheats the cannon", player.overheated, $"heat={player.heat:F2}");
        int shots = player.mechShots;
        player.testTrigger = true;
        yield return new WaitForSeconds(0.3f);
        player.testTrigger = false;
        Check("an overheated cannon cannot fire", player.mechShots == shots);
        Check("overdrive clears the heat", player.MechSpecial() && !player.overheated && player.overdriveT > 0f);

        // Plasma blade: hits in front, cuts enemy shots.
        var near = Dummy(Ahead(2.2f));
        yield return new WaitForSeconds(0.3f);
        var orb = Bullet.Fire(Ahead(1.6f) + Vector3.up * 1.0f, Vector3.zero, false, 0.16f, "bullet_enemy", 5f);
        player.MechMelee();
        yield return new WaitForSeconds(0.45f);
        Check("the plasma blade hits in front", near.hp < 100, $"hp={near.hp}");
        Check("the plasma blade cuts enemy shots", orb == null);
        Destroy(near.gameObject);

        // Melee mode: Q puts the blade on the left button; three swings chain, the third lunges in.
        player.MechSwapMode();
        Check("Q switches the mech to melee mode and shows the blade", player.mechBlade && player.MechArmShown);
        var lungeAt = Dummy(Ahead(5f));
        yield return new WaitForSeconds(0.9f);
        Vector3 beforeLunge = player.transform.position;
        int swings = player.mechSwings;
        player.testTrigger = true;
        for (float u = 0; u < 2.5f && player.mechSwings < swings + 3; u += Time.deltaTime) yield return null;
        player.testTrigger = false;
        yield return new WaitForSeconds(0.3f);
        float lunged = Vector3.Distance(beforeLunge, player.transform.position);
        Check("melee mode: the left button chains swings and the third lunges into the target", player.mechSwings >= swings + 3 && lungeAt.hp < 100 && lunged > 1.5f,
            $"swings={player.mechSwings - swings} hp={lungeAt.hp} moved={lunged:F1}");
        player.MechSwapMode();
        Check("Q again: back to the gun, blade away", !player.mechBlade && !player.MechArmShown);
        Destroy(lungeAt.gameObject);
        player.Teleport(home);
        yield return new WaitForSeconds(0.3f);

        // Light boost: farther than a pilot's dodge.
        Vector3 boostDir = Physics.Raycast(player.transform.position + Vector3.up, side, 7f, Layers.WorldMask) ? -side : side;
        Vector3 boostFrom = player.transform.position;
        player.MechDash(boostDir);
        yield return new WaitForSeconds(0.45f);
        float boosted = Vector3.Distance(boostFrom, player.transform.position);
        Check("light mech boost covers more ground than a pilot's dodge", boosted > 4f, $"moved={boosted:F1}");
        player.Teleport(home);
        yield return new WaitForSeconds(0.2f);

        // Climb out: the mech stays parked with its durability; climbing back in keeps it.
        player.TakeDamage(5);
        int left = player.mechHp;
        player.Dismount(false);
        yield return null;
        var again = ParkedMech.Live.Count == 1 ? ParkedMech.Live[0] : null;
        Check("climbing out parks the mech with its durability", !player.InMech && again && again.hp == left && Mathf.Approximately(cc.radius, footRadius),
            $"live={ParkedMech.Live.Count} hp={(again ? again.hp : -1)}/{left} r={cc.radius:F2}");
        if (again) Destroy(again.gameObject);
        yield return null;

        // Heavy mech: the jump ends in a slam that hits everything around.
        player.Teleport(home);
        player.Mount(Mechs.Heavy, Mechs.Heavy.maxHp);
        yield return new WaitForSeconds(0.6f);
        var around = Dummy(home + side * 2.5f);
        yield return new WaitForSeconds(0.3f);
        player.MechJump();
        for (float u = 0; u < 2f && player.mechSlams == 0; u += Time.deltaTime) yield return null;
        Check("heavy jump slam hits around it", player.mechSlams == 1 && around.hp < 100, $"slams={player.mechSlams} hp={around.hp}");
        Destroy(around.gameObject);

        // Heavy: shoulder ram knocks enemies and hurts them.
        var rammedOne = Dummy(home + fwdDir * 3f);
        yield return new WaitForSeconds(0.6f);
        player.MechDash(fwdDir);
        yield return new WaitForSeconds(0.5f);
        Check("heavy shoulder ram hits what it runs into", rammedOne.hp < 100, $"hp={rammedOne.hp}");
        Destroy(rammedOne.gameObject);
        yield return new WaitForSeconds(0.3f);

        // Heavy: missile salvo, explosive cannon, spikes do nothing.
        int before = Bullet.Live.Count;
        Check("heavy missile salvo fires six missiles", player.MechSpecial() && Bullet.Live.Count - before >= 6, $"new={Bullet.Live.Count - before}");
        player.Teleport(home);
        yield return new WaitForSeconds(0.4f);
        int mechHp = player.mechHp;
        var spikes = SpikeTrap.Create(player.transform.position, null);
        yield return new WaitForSeconds(0.8f);
        Check("the heavy mech ignores spikes", spikes.fired >= 1 && player.mechHp == mechHp, $"fired={spikes.fired} mech={player.mechHp}/{mechHp}");
        Destroy(spikes.gameObject);

        // Destroyed: it explodes and throws the pilot out unhurt.
        yield return new WaitForSeconds(0.5f);
        hp0 = player.hp;
        player.TakeDamage(500);
        yield return null;
        Check("a destroyed mech throws the pilot out unhurt", !player.InMech && player.hp == hp0 && !player.dead, $"in={player.InMech} hp={player.hp}/{hp0}");


        player.Teleport(home);
        player.hp = player.maxHp;
        player.armor = player.maxArmor;
        yield return new WaitForSeconds(1.6f); // past the eject invulnerability
    }

    IEnumerator AutoTestNextFloor()
    {
        if (Pet.Current) Pet.Current.gameObject.SetActive(false);
        yield return new WaitForSeconds(0.5f);
        Check("floor 2 built after the portal", RunState.floor == 2 && dungeon.rooms.Count >= 8, $"floor={RunState.floor} rooms={dungeon.rooms.Count}");
        Check("hp, weapons and buffs carried over", player.hp == RunState.hp && player.weapons[0] != null && RunState.buffs.Count >= 1, $"hp={player.hp} w0={player.weapons[0]?.id} buffs={RunState.buffs.Count}");
        Check("floor 2 connected", dungeon.AllRoomsConnected());
        Check("new floor offers a buff again", !RunState.buffTaken);
        Finish();
    }

    static void Finish()
    {
        Debug.Log($"AUTOTEST done pass={RunState.testPass} fail={RunState.testFail}");
        Application.Quit(RunState.testFail == 0 ? 0 : 1);
    }

    // ---------- screenshots ----------

    /// Grip inspection: the hero holding each weapon, close up from behind (the gameplay view),
    /// front three-quarter and the left side (melee: mid-swing instead of the side), as contact
    /// sheets of six weapons each.
    IEnumerator DumpIcons()
    {
        for (float t = 0f; t < 30f && !(IconStudio.I && IconStudio.I.Ready); t += Time.unscaledDeltaTime) yield return null;
        if (IconStudio.I) IconStudio.I.Dump(iconDir);
        Debug.Log($"ICONS dumped ready={(IconStudio.I && IconStudio.I.Ready)}");
        Application.Quit();
    }

    /// Flicker check (the user gets motion sick from shimmer): the idle hero seen from the play
    /// camera's spot while the view turns slowly, as when aiming with the mouse; 24 consecutive frames
    /// cropped around the hero, then the same close up. Flicker = brightness that jumps up and down from
    /// frame to frame instead of drifting (see scripts/flicker.py).
    IEnumerator FlickerFrames()
    {
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        if (Pet.Current) Pet.Current.gameObject.SetActive(false);
        hud.enabled = false;
        player.controlEnabled = false;
        player.externalCamera = true;
        player.Teleport(dungeon.playerStart);
        yield return new WaitForSeconds(1.5f);
        Directory.CreateDirectory(flickerDir);
        Vector3 pos = player.transform.position, chest = pos + Vector3.up * 1.0f;
        float yaw0 = player.yaw;
        foreach (var (tag, back, side, up, tile) in new[] { ("play", 3.0f, 0.95f, 0.85f, 520), ("close", 1.6f, 0.3f, 0.3f, 640) })
        {
            for (int f = 0; f < 24; f++)
            {
                float yaw = yaw0 + f * 0.35f; // a slow mouse turn: 21°/s at 60 fps
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, right = Vector3.Cross(Vector3.up, fwd);
                Vector3 eye = chest - fwd * back + right * side + Vector3.up * up;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(chest - eye));
                yield return new WaitForEndOfFrame();
                var screen = ScreenCapture.CaptureScreenshotAsTexture();
                int x0 = (screen.width - tile) / 2, y0 = (screen.height - tile) / 2;
                var crop = new Texture2D(tile, tile, TextureFormat.RGB24, false);
                crop.SetPixels(screen.GetPixels(x0, y0, tile, tile));
                crop.Apply();
                File.WriteAllBytes(Path.Combine(flickerDir, $"{tag}_{f:00}.png"), crop.EncodeToPNG());
                Destroy(screen);
                Destroy(crop);
            }
        }
        // The same close turn with one thing off at a time, to see which one the flicker comes from.
        var ssao = SsaoFeature();
        var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        var shadows = System.Array.ConvertAll(lights, l => l.shadows);
        foreach (var variant in new[] { "noshadow", "noao", "nopost" })
        {
            for (int i = 0; i < lights.Length; i++) if (lights[i]) lights[i].shadows = variant == "noshadow" ? LightShadows.None : shadows[i];
            if (ssao) ssao.SetActive(variant != "noao");
            if (post) post.enabled = variant != "nopost";
            yield return new WaitForSeconds(0.3f);
            for (int f = 0; f < 24; f++)
            {
                float yaw = yaw0 + f * 0.35f;
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, right = Vector3.Cross(Vector3.up, fwd);
                Vector3 eye = chest - fwd * 1.6f + right * 0.3f + Vector3.up * 0.3f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(chest - eye));
                yield return new WaitForEndOfFrame();
                var screen = ScreenCapture.CaptureScreenshotAsTexture();
                int x0 = (screen.width - 640) / 2, y0 = (screen.height - 640) / 2;
                var crop = new Texture2D(640, 640, TextureFormat.RGB24, false);
                crop.SetPixels(screen.GetPixels(x0, y0, 640, 640));
                crop.Apply();
                File.WriteAllBytes(Path.Combine(flickerDir, $"{variant}_{f:00}.png"), crop.EncodeToPNG());
                Destroy(screen);
                Destroy(crop);
            }
        }
        for (int i = 0; i < lights.Length; i++) if (lights[i]) lights[i].shadows = shadows[i];
        if (ssao) ssao.SetActive(true);
        if (post) post.enabled = true;

        // Running in place, camera still and behind: a robe or skirt that legs poke through flickers here.
        var anim = player.GetComponentInChildren<CharacterAnim>();
        if (anim)
        {
            player.enabled = false; // its Update would put the idle clip back
            anim.SetBase("run");
            Vector3 fwd = Quaternion.Euler(0f, yaw0, 0f) * Vector3.forward, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 eye = chest - fwd * 3.0f + right * 0.95f + Vector3.up * 0.85f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(chest - eye));
            yield return new WaitForSeconds(0.6f);
            for (int f = 0; f < 24; f++)
            {
                yield return new WaitForEndOfFrame();
                var screen = ScreenCapture.CaptureScreenshotAsTexture();
                int x0 = (screen.width - 520) / 2, y0 = (screen.height - 520) / 2;
                var crop = new Texture2D(520, 520, TextureFormat.RGB24, false);
                crop.SetPixels(screen.GetPixels(x0, y0, 520, 520));
                crop.Apply();
                File.WriteAllBytes(Path.Combine(flickerDir, $"run_{f:00}.png"), crop.EncodeToPNG());
                Destroy(screen);
                Destroy(crop);
            }
        }
        Debug.Log("FLICKER done");
        Application.Quit();
    }

    /// The renderer's SSAO feature (the asset keeps its renderer list private).
    static ScriptableRendererFeature SsaoFeature()
    {
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return null;
        var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (!(field?.GetValue(urp) is ScriptableRendererData[] list)) return null;
        foreach (var data in list)
            if (data) foreach (var f in data.rendererFeatures) if (f && f.name == "SSAO") return f;
        return null;
    }

    IEnumerator GripSheets()
    {
        const int Tile = 420;
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        hud.enabled = false;
        player.controlEnabled = false;
        player.externalCamera = true;
        var all = new System.Collections.Generic.List<WeaponDef>(Weapons.All);
        all.AddRange(Weapons.SkillWeapons); // the class skills' weapons (and the oath shield) too
        int sheets = (all.Count + 5) / 6;
        for (int sheet = 0; sheet < sheets; sheet++)
        {
            int rows = Mathf.Min(6, all.Count - sheet * 6);
            var tex = new Texture2D(Tile * 3, Tile * rows, TextureFormat.RGB24, false);
            for (int row = 0; row < rows; row++)
            {
                var w = all[sheet * 6 + row];
                if (w.IsSkillWeapon) player.GrantSkillWeapon(w.id, 60f);
                else player.Equip(w.Clone());
                player.energy = player.maxEnergy;
                player.Teleport(dungeon.playerStart);
                Vector3 pos = player.transform.position;
                Vector3 fwd = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                player.aimOverride = pos + fwd * 10f + Vector3.up * 1.1f;
                for (int view = 0; view < 3; view++)
                {
                    bool swing = view == 2 && w.melee;
                    float until = Time.time + (swing ? 0f : 0.6f);
                    if (swing) player.Attack();
                    while (Time.time < until) { player.HoldAim(); yield return null; }
                    if (swing) yield return new WaitForSeconds(Mathf.Min(0.12f, w.interval * 0.4f));
                    if (!w.melee) player.HoldAim();
                    yield return null;
                    Vector3 chest = pos + Vector3.up * 1.0f;
                    // View 0 matches the game camera's angle (behind, 0.95 m to the right), just closer.
                    Vector3 eye = view == 0 ? chest - fwd * 3.0f + right * 0.95f + Vector3.up * 0.85f
                        : view == 1 || swing ? chest + fwd * 2.2f + right * 1.4f + Vector3.up * 0.3f
                        : chest - right * 2.6f + Vector3.up * 0.2f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(chest - eye));
                    yield return new WaitForEndOfFrame();
                    var screen = ScreenCapture.CaptureScreenshotAsTexture();
                    int x0 = (screen.width - Tile) / 2, y0 = (screen.height - Tile) / 2;
                    tex.SetPixels(view * Tile, (rows - 1 - row) * Tile, Tile, Tile, screen.GetPixels(x0, y0, Tile, Tile));
                    Destroy(screen);
                }
            }
            tex.Apply();
            Directory.CreateDirectory(gripDir);
            File.WriteAllBytes(Path.Combine(gripDir, $"grips_{sheet + 1}.png"), tex.EncodeToPNG());
            Destroy(tex);
        }
        Debug.Log("GRIPS done");
        Application.Quit();
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        Directory.CreateDirectory(shotDir);
        ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, name));
        yield return null;
        yield return null;
    }

    IEnumerator Shots()
    {
        hud.screen = Hud.Screen.Title;
        player.pitch = 18f;
        yield return new WaitForSeconds(0.5f);
        yield return Shot("00_title.png");
        RunSave.Showcase = (3, "assassin"); // how the title looks with an unfinished run (no real save is touched)
        yield return new WaitForSeconds(0.2f);
        yield return Shot("00e_title_continue.png");
        RunSave.Showcase = null;
        hud.OpenSettings();
        yield return new WaitForSeconds(0.3f);
        yield return Shot("00c_settings.png");
        hud.screen = Hud.Screen.ClassSelect;
        yield return new WaitForSeconds(0.3f);
        yield return Shot("00d_classes.png");
        hud.screen = Hud.Screen.None;
        hud.OfferBuffs(Buffs.Offer(3), b => Buffs.Take(b, player));
        yield return new WaitForSeconds(0.5f);
        yield return Shot("00b_buff.png");
        hud.PickBuff(0);

        player.pitch = 10f;
        yield return new WaitForSeconds(1f);
        yield return Shot("01_start.png");

        var room = dungeon.combatRooms[0];
        Vector3 c = room.Center();
        player.Teleport(c + new Vector3(0f, 0.1f, -(room.max.z - room.min.z) * 0.35f));
        player.yaw = 0f;
        yield return new WaitForSeconds(3f);
        yield return Shot("02_combat.png");

        // Frame-rate check for the comfort target (>= 80 fps): uncapped for three seconds of combat.
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        var times = new System.Collections.Generic.List<float>();
        double cpuMs = 0, gpuMs = 0;
        int timed = 0;
        var timing = new FrameTiming[1];
        for (float u = 0; u < 3f; u += Time.unscaledDeltaTime)
        {
            yield return null;
            times.Add(Time.unscaledDeltaTime);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                cpuMs += timing[0].cpuMainThreadFrameTime;
                gpuMs += timing[0].gpuFrameTime;
                timed++;
            }
        }
        // Where the time goes: a small CPU and GPU time with a slow frame means the window was throttled.
        if (timed > 0) Debug.Log($"PERF frame cost: cpu main {cpuMs / timed:F2} ms, gpu {gpuMs / timed:F2} ms (over {timed} frames)");
        times.Sort();
        float avg = 0f;
        foreach (var t in times) avg += t;
        avg /= times.Count;
        Debug.Log($"PERF {Screen.width}x{Screen.height} avg={1f / avg:0} fps, 1% low={1f / times[(int)(times.Count * 0.99f)]:0} fps, frames={times.Count}");
        Application.targetFrameRate = 60;
        for (int i = 0; i < 6; i++)
        {
            player.Attack();
            yield return new WaitForSeconds(0.12f);
        }
        yield return Shot("03_shooting.png");

        player.Swap();
        player.Attack();
        yield return new WaitForSeconds(0.2f); // mid-swing: the blade and its smear
        yield return Shot("04_melee.png");

        var boss = Dummy(c + new Vector3(0f, 0f, 5f), EnemyKind.Boss);
        boss.target = player;
        yield return new WaitForSeconds(2.8f);
        yield return Shot("05_boss.png");
        Destroy(boss.gameObject);

        // Weapon effects, each fired at a dummy under the crosshair.
        player.Teleport(c + new Vector3(0f, 0.1f, -(room.max.z - room.min.z) * 0.2f));
        player.yaw = 0f;
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        yield return AimAt(5f);
        var fxTarget = Dummy(CrosshairGround());
        fxTarget.hp = fxTarget.maxHp = 9999;
        yield return new WaitForSeconds(0.5f);
        foreach (var (id, file, hold) in new[] { ("frost", "06_frost.png", 0.12f), ("thunder", "07_thunder.png", 0.05f), ("flamer", "08_flame.png", 0.5f), ("laser", "09_laser.png", 0.4f), ("launcher", "10_explosion.png", 0.42f) })
        {
            player.Equip(Weapons.Get(id));
            player.energy = player.maxEnergy;
            player.testTrigger = true;
            yield return new WaitForSeconds(hold);
            yield return Shot(file);
            player.testTrigger = false;
            yield return new WaitForSeconds(0.8f);
        }
        // The bow drawn in full (the bead ring round the crosshair, gold motes gathering), then the arrow loosed.
        player.Equip(Weapons.Get("bow"));
        player.energy = player.maxEnergy;
        player.testTrigger = true;
        yield return new WaitForSeconds(Weapons.ChargeTime + 0.2f);
        yield return Shot("06b_bow_drawn.png");
        player.testTrigger = false;
        yield return null;
        yield return null;
        yield return Shot("06c_bow_arrow.png");
        yield return new WaitForSeconds(0.8f);
        // The assassin's 鸦影遁: hidden, the darts charged in full (seven beads), then the fan in flight.
        var skillWas = player.skill.def;
        player.skill.Setup(player, Classes.Get("assassin"));
        player.skill.TryUse();
        yield return new WaitForSeconds(0.4f);
        player.testTrigger = true;
        yield return new WaitForSeconds(Weapons.ChargeTime + 0.15f);
        yield return Shot("06d_darts_drawn.png");
        player.testTrigger = false;
        yield return null;
        yield return null;
        yield return null;
        yield return Shot("06e_darts_fan.png");
        yield return new WaitForSeconds(0.5f);
        player.skill.BreakStealth();
        player.EndSkillWeapon();
        player.skill.Setup(player, skillWas);
        // Physics: an explosive barrel line goes up and throws the enemies standing by it.
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        Vector3 fw = Vector3.forward, sd = Vector3.right;
        Vector3 row = player.transform.position + fw * 6f - sd * 2.5f;
        var first = PhysicsProp.Make(Props.Spawn("barrel", row, Quaternion.identity, null), 12f, 2, true);
        for (int i = 1; i < 3; i++) PhysicsProp.Make(Props.Spawn("barrel", row + sd * 2.2f * i, Quaternion.identity, null), 12f, 2, true);
        for (int i = 0; i < 3; i++)
        {
            var g = Dummy(row + sd * (2.2f * i + 1f) + fw * 1.3f);
            g.hp = 3;
        }
        PhysicsProp.Make(Kit.Spawn("crate", row + fw * 2.5f + sd * 4f, Quaternion.identity, null), 18f, 9);
        player.pitch = 8f;
        yield return new WaitForSeconds(0.8f);
        if (first) first.Hit(5, first.transform.position, fw);
        else Debug.Log("SHOTS physics: the first barrel went off before it was shot (see the PROP line)");
        yield return new WaitForSeconds(0.32f);
        yield return Shot("14_physics.png");
        yield return new WaitForSeconds(0.6f);
        yield return Shot("15_ragdolls.png");

        // Melee: each weapon's own swing, then its heavy version.
        foreach (var id in new[] { "sword", "hammer", "spear", "daggers" })
        {
            player.Equip(Weapons.Get(id));
            player.Attack();
            yield return new WaitForSeconds(0.16f);
            yield return Shot($"16_melee_{id}.png");
            yield return new WaitForSeconds(0.5f);
        }

        // Traps: an erupting fire pit, raised spikes and a poison cloud around the player, plus
        // this floor's own hazard (lava crack on floor 2, ice on floor 3).
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        Vector3 tp = player.transform.position;
        var shotPit = FirePit.Create(tp + Vector3.forward * 4f + Vector3.left * 2.2f, null, -1.8f); // warning starts at once
        var floorTrap = RunState.floor == 2 ? LavaCrack.Create(tp + Vector3.forward * 2.5f + Vector3.left * 4.5f, 20f, null).gameObject
            : RunState.floor >= 3 ? IcePatch.Create(tp + Vector3.forward * 2.5f + Vector3.left * 4.5f, null).gameObject : null;
        yield return new WaitForSeconds(0.5f);
        var shotSpikes = SpikeTrap.Create(tp + Vector3.forward * 4f + Vector3.right * 2.5f, null);
        var walker = Dummy(shotSpikes.transform.position);
        var shotCloud = PoisonCloud.Create(tp + Vector3.forward * 7f + Vector3.right * 0.5f);
        player.pitch = 12f;
        yield return new WaitForSeconds(0.75f);
        yield return Shot("17_traps.png");
        Destroy(shotPit.gameObject);
        Destroy(shotSpikes.gameObject);
        if (shotCloud) Destroy(shotCloud.gameObject);
        if (floorTrap) Destroy(floorTrap);
        if (walker) Destroy(walker.gameObject);

        // Mechs: one parked (boarding prompt), the light one firing, the heavy one's missile salvo.
        Vector3 mp = player.transform.position;
        var shotMech = ParkedMech.Create(Mechs.Heavy, Mechs.Heavy.maxHp, mp + Vector3.forward * 2.8f + Vector3.right * 1.2f, 200f);
        player.pitch = 10f;
        yield return new WaitForSeconds(0.4f);
        yield return Shot("18_mech_parked.png");
        Destroy(shotMech.gameObject);
        player.Mount(Mechs.Light, Mechs.Light.maxHp);
        player.pitch = 8f;
        var foes = new System.Collections.Generic.List<Enemy>();
        for (int i = -1; i <= 1; i++) foes.Add(Dummy(mp + Vector3.forward * 9f + Vector3.right * (i * 2.5f)));
        yield return new WaitForSeconds(1.0f);
        player.testTrigger = true;
        yield return new WaitForSeconds(0.35f);
        yield return Shot("18_mech_light.png");
        player.testTrigger = false;
        player.Dismount(false);
        foreach (var m in ParkedMech.Live.ToArray()) Destroy(m.gameObject);
        player.Mount(Mechs.Heavy, Mechs.Heavy.maxHp);
        yield return new WaitForSeconds(1.0f);
        player.MechSpecial();
        yield return new WaitForSeconds(0.25f);
        yield return Shot("18_mech_heavy.png");
        yield return new WaitForSeconds(0.8f);
        player.Dismount(false);
        foreach (var m in ParkedMech.Live.ToArray()) Destroy(m.gameObject);
        foreach (var f in foes) if (f) Destroy(f.gameObject);

        // The shop: the merchant behind his four pedestals.
        if (dungeon.shopRoom != null)
        {
            var keeper = GameObject.Find("merchant");
            Vector3 toDoor = dungeon.shopRoom.Center - (keeper ? keeper.transform.position : dungeon.shopRoom.Center - Vector3.forward);
            toDoor.y = 0f;
            toDoor.Normalize();
            player.Teleport(dungeon.shopRoom.Center + toDoor * 4.5f);
            player.yaw = Mathf.Atan2(-toDoor.x, -toDoor.z) * Mathf.Rad2Deg;
            player.pitch = 14f;
            RunState.gold = 57;
            yield return new WaitForSeconds(1.2f);
            yield return Shot("20_shop.png");
            player.Teleport(dungeon.playerStart);
            yield return new WaitForSeconds(0.3f);
        }

        // Elites: one of each affix lined up in front.
        {
            Vector3 ep = player.transform.position;
            Vector3 ef = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward, er = Quaternion.Euler(0f, player.yaw + 90f, 0f) * Vector3.forward;
            var shown = new System.Collections.Generic.List<Enemy>();
            for (int i = 0; i < 4; i++)
            {
                // Inside the 12 m start room (its walls are 6 m from the centre).
                var e = Dummy(ep + ef * 4f + er * (i - 1.5f) * 2.2f, i % 2 == 0 ? EnemyKind.Gunner : EnemyKind.Spreader);
                e.MakeElite((EliteKind)(i + 1)); // dummies have no target, so they hold still for the picture
                shown.Add(e);
            }
            player.pitch = 10f;
            yield return new WaitForSeconds(0.6f);
            yield return Shot("21_elites.png");
            foreach (var e in shown) if (e) Destroy(e.gameObject);
        }

        // New enemies: a goblin bomber with a bomb in the air over its red landing circle, a skeleton archer.
        {
            Vector3 np = player.transform.position;
            Vector3 nf = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward, nr = Quaternion.Euler(0f, player.yaw + 90f, 0f) * Vector3.forward;
            var bomber = Dummy(np + nf * 4.5f - nr * 1.6f, EnemyKind.Bomber);
            var archer = Dummy(np + nf * 4.5f + nr * 1.6f, EnemyKind.Archer);
            var guard = Dummy(np + nf * 4f, EnemyKind.Guard); // between the two, well inside any start room
            guard.speed = 0f;
            guard.target = player; // turns its shield to the camera
            yield return new WaitForSeconds(2.5f);
            EnemyBomb.Throw(bomber.transform.position + Vector3.up * 1.4f, np + nf * 1.8f, 1.2f, 2.2f);
            player.pitch = 10f;
            yield return new WaitForSeconds(0.6f);
            yield return Shot("22_new_enemies.png");
            yield return new WaitForSeconds(0.8f);
            Destroy(bomber.gameObject);
            Destroy(archer.gameObject);
            Destroy(guard.gameObject);
            // A mimic, close up (standing still: no target).
            var mimic = Dummy(np + nf * 3.5f, EnemyKind.Mimic);
            mimic.TurnTo(-nf);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("22b_mimic.png");
            Destroy(mimic.gameObject);
            // Slimes: a big one and two small ones (as after a split) hopping at you.
            var slimes = new System.Collections.Generic.List<Enemy> { Dummy(np + nf * 5f, EnemyKind.Slime) };
            for (int i = -1; i <= 1; i += 2)
            {
                var small = Dummy(np + nf * 4f + nr * i * 1.8f, EnemyKind.Slime);
                small.MakeMini();
                slimes.Add(small);
            }
            foreach (var sl in slimes) sl.target = player;
            yield return new WaitForSeconds(0.75f);
            yield return Shot("22c_slime.png");
            foreach (var sl in slimes) Destroy(sl.gameObject);
        }

        // Loot: three drops of rising rarity with their light pillars.
        {
            Vector3 lp = player.transform.position;
            Vector3 lf = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward, lr = Quaternion.Euler(0f, player.yaw + 90f, 0f) * Vector3.forward;
            var drops = new System.Collections.Generic.List<Pickup>();
            for (int i = 0; i < 3; i++) drops.Add(Pickup.DropWeapon(lp + lf * 4.5f + lr * (i - 1) * 2.2f, i == 0 ? 1 : i == 1 ? 3 : 4));
            player.pitch = 12f;
            yield return new WaitForSeconds(0.9f);
            yield return Shot("23_loot.png");
            foreach (var d in drops) if (d) Destroy(d.gameObject);
        }

        // The blood altar and the trial statue (when the floor has them).
        var altarRoom = dungeon.rooms.Find(r => r.kind == Dungeon.Kind.Altar);
        if (altarRoom != null)
        {
            player.Teleport(altarRoom.Center + Vector3.back * 3.5f);
            player.yaw = 0f;
            player.pitch = 10f;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("24_altar.png");
        }
        if (dungeon.trialRooms.Count > 0)
        {
            player.Teleport(dungeon.trialRooms[0].Center() + Vector3.back * 5f);
            player.yaw = 0f;
            player.pitch = 14f;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("25_trial.png");
        }
        player.Teleport(dungeon.playerStart);
        yield return new WaitForSeconds(0.3f);

        // The lich: a volley of homing skulls.
        {
            Vector3 bp = player.transform.position;
            Vector3 bf = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
            var lich = Dummy(bp + bf * 4.5f, EnemyKind.Boss, 3);
            lich.target = player;
            yield return new WaitForSeconds(0.6f);
            lich.ForceAttack(0);
            player.pitch = 8f;
            yield return new WaitForSeconds(0.7f);
            yield return Shot("26_lich.png");
            Destroy(lich.gameObject);
            foreach (var b in Bullet.Live.ToArray()) if (b && !b.HitsEnemies) Destroy(b.gameObject);
            yield return new WaitForSeconds(0.3f);
        }

        // The pause menu with the build panel.
        hud.screen = Hud.Screen.Pause;
        yield return new WaitForSeconds(0.2f);
        yield return Shot("27_pause.png");
        // The weapon handbook opened from it (a few weapons known, the rest silhouettes).
        foreach (var id in new[] { "pistol", "sword", "smg", "hammer", "thunder", "launcher" }) RunState.SawWeapon(id);
        hud.OpenCodex();
        yield return new WaitForSeconds(0.2f);
        yield return Shot("28_codex.png");
        hud.screen = Hud.Screen.None;

        // The end-of-run summary card.
        hud.screen = Hud.Screen.Dead;
        yield return new WaitForSeconds(0.2f);
        yield return Shot("19_summary.png");
        hud.screen = Hud.Screen.None;

        // Skills in action.
        player.skill.Setup(player, Classes.Get("knight"));
        player.skill.TryUse();
        yield return new WaitForSeconds(0.3f);
        yield return Shot("11_shield.png");
        yield return new WaitForSeconds(3.5f);
        player.skill.Setup(player, Classes.Get("mage"));
        player.skill.TryUse();
        yield return new WaitForSeconds(1.15f);
        yield return Shot("12_meteor.png");
        yield return new WaitForSeconds(1.5f);
        player.skill.Setup(player, Classes.Get("priest"));
        player.skill.TryUse();
        yield return new WaitForSeconds(0.6f);
        yield return Shot("13_sanctuary.png");
        Debug.Log("SHOTS done");
        Application.Quit();
    }
}
