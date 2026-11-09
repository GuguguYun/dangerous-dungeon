using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// -vfxshots <dir>: a quick look at the loot pillars, the energy shields, the potion / crystal pickups,
/// the assassin's stealth and every enemy / boss shot, as close-ups and from the normal play camera,
/// saved as PNGs; then quit. -vfxpart shots: only the enemy shots; -vfxpart circles: only the spells'
/// magic circles (the mage's storm and star staff, the priest's sanctuary).
/// It hooks itself in (no GameManager change) and takes over once the floor is built. Run with -class
/// knight so the dome is the knight's own, -class assassin for the stealth. Needs a window
/// (screenshots), like -shots.
public class VfxShots : MonoBehaviour
{
    string dir, part = "all";
    PlayerController player;
    Camera cam;
    Hud hud;
    Dungeon dungeon;
    Vector3 P, F, R;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = System.Environment.GetCommandLineArgs();
        string part = "all";
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-vfxpart") part = args[i + 1];
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-vfxshots") continue;
            Application.runInBackground = true;
            var go = new GameObject("VfxShots");
            DontDestroyOnLoad(go);
            var v = go.AddComponent<VfxShots>();
            v.dir = args[i + 1];
            v.part = part;
        }
    }

    IEnumerator Start()
    {
        while (!PlayerController.Current || !Hud.I) yield return null;
        yield return null;
        yield return null;
        Tips.Enabled = false; // never use up the player's first-time hints
        Application.targetFrameRate = 60;
        Directory.CreateDirectory(dir);
        player = PlayerController.Current;
        cam = player.cam;
        hud = Hud.I;
        hud.screen = Hud.Screen.None;
        dungeon = FindAnyObjectByType<Dungeon>();
        player.yaw = dungeon.startYaw;
        player.pitch = 12f;
        P = player.transform.position;
        P.y = 0f;
        F = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward;
        R = Quaternion.Euler(0f, player.yaw + 90f, 0f) * Vector3.forward;
        yield return new WaitForSeconds(1f);
        if (part == "circles") yield return Circles();
        else
        {
            if (part != "shots") yield return Effects();
            yield return EnemyShots();
        }
        Debug.Log("VFXSHOTS done");
        Application.Quit();
    }

    IEnumerator Effects()
    {
        // ---- loot pillars: one of each rarity ----
        var drops = new List<Pickup>();
        for (int r = 0; r < Weapons.RarityColors.Length; r++)
        {
            var w = Weapons.All.Find(x => x.rarity == r);
            drops.Add(Pickup.Spawn(PickupKind.Weapon, P + F * 4.5f + R * (r - 2) * 1.5f, 0, w));
        }
        yield return new WaitForSeconds(0.45f);
        yield return Shot("loot_growing.png");
        yield return new WaitForSeconds(1.2f);
        yield return Shot("loot_game.png");
        Look(P + F * 1.0f + Vector3.up * 1.5f, P + F * 4.5f + Vector3.up * 1.4f);
        yield return Shot("loot_close.png");
        Vector3 leg = drops[4].transform.position;
        Look(leg - F * 2.4f - R * 1.2f + Vector3.up * 0.9f, leg + Vector3.up * 0.9f);
        yield return Shot("loot_close_legendary.png");
        foreach (var d in drops) if (d) Destroy(d.gameObject);
        PlayCam();

        // ---- health potions and energy crystals: bounce, settle, hover ----
        player.dead = true; // holds the crystals' magnet off for the pictures
        var potionA = Pickup.Spawn(PickupKind.Health, P + F * 3.2f - R * 1.0f, 2);
        Pickup.Spawn(PickupKind.Health, P + F * 3.8f + R * 1.3f, 2);
        Pickup.SpawnEnergy(P + F * 3.3f + R * 0.2f, 5);
        yield return new WaitForSeconds(0.35f);
        yield return Shot("pickups_bounce.png");
        yield return new WaitForSeconds(2.2f);
        yield return Shot("pickups_game.png");
        // Crystals further off, where they usually lie when a fight ends.
        for (int i = 0; i < 3; i++) Pickup.Spawn(PickupKind.Energy, P + F * (6.5f + i * 0.6f) + R * (i - 1) * 1.4f, 3);
        yield return new WaitForSeconds(2.0f);
        yield return Shot("pickups_far.png");
        Look(P + F * 1.5f + Vector3.up * 1.0f, P + F * 3.4f + Vector3.up * 0.35f);
        yield return Shot("pickups_close.png");
        PlayCam();
        player.dead = false;
        yield return new WaitForSeconds(0.75f);
        yield return Shot("pickups_magnet.png");
        if (potionA) player.Teleport(potionA.transform.position);
        yield return new WaitForSeconds(0.3f);
        yield return Shot("collect_potion.png");
        foreach (var p in Pickup.Live.ToArray()) if (p) Destroy(p.gameObject);
        player.Teleport(P);
        player.yaw = Mathf.Atan2(F.x, F.z) * Mathf.Rad2Deg;
        yield return new WaitForSeconds(0.4f);

        // ---- the knight's dome ----
        if (player.skill.def.skill != SkillKind.Shield) player.skill.Setup(player, Classes.Get("knight"));
        player.skill.cooldownLeft = 0f;
        player.skill.TryUse();
        yield return new WaitForSeconds(0.12f);
        yield return Shot("knight_rising.png");
        yield return new WaitForSeconds(0.5f);
        Look(P + F * 3.6f + R * 1.4f + Vector3.up * 1.7f, P + Vector3.up * 0.9f);
        yield return Shot("knight_close.png");
        Volley(P, F, R, 3);
        yield return new WaitForSeconds(0.2f);
        yield return Shot("knight_close_hit.png");
        PlayCam();
        yield return new WaitForSeconds(0.4f);
        Volley(P, F, R, 3);
        yield return new WaitForSeconds(0.2f);
        yield return Shot("knight_game_hit.png");
        yield return new WaitForSeconds(2.4f); // let it run out and dissolve
        yield return Shot("knight_after.png");

        // ---- the dome over a jumping knight: seen from the side at the top of a jump and of a double jump ----
        yield return new WaitForSeconds(0.6f);
        player.stamina = player.maxStamina;
        player.skill.cooldownLeft = 0f;
        player.skill.TryUse();
        yield return new WaitForSeconds(0.4f);
        Look(P + R * 4.8f + F * 0.6f + Vector3.up * 1.9f, P + Vector3.up * 1.7f);
        player.Jump();
        yield return Apex();
        yield return Shot("knight_jump_side.png");
        PlayCam();
        yield return Land();
        yield return new WaitForSeconds(0.1f);
        player.Jump();
        yield return Apex();
        yield return Shot("knight_jump_game.png");
        yield return Land();
        yield return new WaitForSeconds(0.1f);
        bool hadDouble = RunState.doubleJump;
        RunState.doubleJump = true;
        player.stamina = player.maxStamina;
        Look(P + R * 4.8f + F * 0.6f + Vector3.up * 2.4f, P + Vector3.up * 2.3f);
        player.Jump();
        yield return new WaitForSeconds(0.2f);
        player.Jump();
        yield return Apex();
        yield return Shot("knight_doublejump_side.png");
        RunState.doubleJump = hadDouble;
        PlayCam();
        yield return Land();
        yield return new WaitForSeconds(3.0f);

        // ---- shielded elites ----
        var gunner = Dummy(P + F * 4.2f - R * 1.5f, EnemyKind.Gunner);
        gunner.MakeElite(EliteKind.Shielded);
        var shaman = Dummy(P + F * 4.8f + R * 1.7f, EnemyKind.Spreader);
        shaman.MakeElite(EliteKind.Shielded);
        player.pitch = 10f;
        yield return new WaitForSeconds(1.0f);
        yield return Shot("elite_game.png");
        Vector3 g = gunner.transform.position;
        Look(g - F * 2.6f + R * 0.7f + Vector3.up * 1.3f, g + Vector3.up * 0.8f);
        yield return Shot("elite_close.png");
        // Real shots from its front-right (not from the player behind the camera): the ripples must
        // start on the right of the dome, where the shots came through.
        Vector3 aim = gunner.AimPoint;
        for (int i = 0; i < 2; i++)
        {
            Vector3 from = aim + (R * 0.85f - F * 0.5f).normalized * 2.2f + Vector3.up * (0.5f * i - 0.1f);
            Bullet.Fire(from, (aim + Vector3.up * (0.3f * i - 0.1f) - from).normalized * 22f, true, 0.1f, "bullet_player", 2f, 1);
        }
        yield return new WaitForSeconds(0.2f);
        yield return Shot("elite_close_hit.png");
        gunner.shield = Mathf.CeilToInt(gunner.maxShield * 0.3f);
        yield return new WaitForSeconds(1.0f);
        yield return Shot("elite_close_worn.png");
        gunner.TakeDamage(gunner.shield, F);
        yield return new WaitForSeconds(0.12f);
        yield return Shot("elite_close_break.png");
        PlayCam();
        Destroy(gunner.gameObject);
        Destroy(shaman.gameObject);

        // ---- the assassin's stealth: settling in, close, from the play camera, moving, ending ----
        player.Teleport(P);
        player.yaw = Mathf.Atan2(F.x, F.z) * Mathf.Rad2Deg;
        player.pitch = 12f;
        if (player.skill.def.skill != SkillKind.Stealth) player.skill.Setup(player, Classes.Get("assassin"));
        player.skill.cooldownLeft = 0f;
        yield return new WaitForSeconds(0.5f);
        player.skill.TryUse();
        yield return new WaitForSeconds(0.15f);
        yield return Shot("stealth_start.png");
        yield return new WaitForSeconds(0.9f);
        yield return Shot("stealth_game.png");
        Look(P + F * 2.3f + R * 0.9f + Vector3.up * 1.4f, P + Vector3.up * 0.8f);
        yield return Shot("stealth_close.png");
        foreach (var ps in player.GetComponentsInChildren<ParticleSystem>())
            Debug.Log($"VFXSHOTS particles {ps.name} {ps.particleCount} playing={ps.isPlaying}");
        Look(P - R * 2.4f + F * 0.6f + Vector3.up * 1.3f, P + Vector3.up * 0.8f);
        yield return Shot("stealth_side.png");
        PlayCam();
        for (float t = 0f; t < 0.9f; t += Time.deltaTime)
        {
            player.Teleport(P + R * (t * 3f));
            yield return null;
        }
        yield return Shot("stealth_moving.png");
        while (player.Stealthed) yield return null;
        yield return new WaitForSeconds(0.1f);
        yield return Shot("stealth_end.png");
        player.Teleport(P);

        // ---- the shop's potion and energy crystal ----
        var wares = ShopItem.Live.FindAll(s => s.kind == ShopItem.Kind.Potion || s.kind == ShopItem.Kind.Energy);
        if (wares.Count > 0)
        {
            Vector3 mid = Vector3.zero;
            foreach (var s in wares) mid += s.transform.position;
            mid /= wares.Count;
            Vector3 toDoor = dungeon.shopRoom != null ? mid - dungeon.shopRoom.Center : F;
            toDoor.y = 0f;
            toDoor = toDoor.sqrMagnitude > 0.01f ? toDoor.normalized : F;
            player.Teleport(mid + toDoor * 3.5f);
            yield return new WaitForSeconds(0.8f);
            Look(mid + toDoor * 3.0f + Vector3.up * 1.9f, mid + Vector3.up * 1.2f);
            yield return Shot("shop_close.png");
        }
        player.Teleport(P);
        player.yaw = Mathf.Atan2(F.x, F.z) * Mathf.Rad2Deg;
        PlayCam();
        yield return new WaitForSeconds(0.5f);
    }

    // ---- enemy and boss shots: each fired the way its enemy fires it (same material, size, speed, height) ----

    /// id, material, radius, speed, height, pattern (0 = one shot, n > 0 = fan of n over `arc`, n < 0 = ring of -n).
    static readonly (string id, string mat, float radius, float speed, float height, int pattern, float arc)[] Shots =
    {
        ("gunner", "bullet_enemy", 0.16f, 11f, 1.0f, 0, 0f),
        ("archer", "bullet_arrow", 0.1f, 19f, 1.0f, 0, 0f),
        ("shaman", "bullet_low", 0.16f, 9f, 0.4f, 5, 50f),
        ("king_fan", "bullet_jade", 0.16f, 10f, 0.4f, 7, 70f),
        ("king_ring", "bullet_gold", 0.2f, 9f, 1.0f, -12, 0f),
        ("boar_ring", "bullet_rock", 0.2f, 8f, 1.0f, -20, 0f),
        ("lich_skulls", "bullet_hex", 0.22f, 7f, 1.0f, 3, 60f),
        ("lich_ring", "bullet_frost", 0.2f, 7.5f, 1.0f, -14, 0f),
        ("orc_fan", "bullet_axe", 0.16f, 12f, 1.0f, 7, 70f),
        ("orc_ring", "bullet_axe", 0.2f, 9f, 1.0f, -18, 0f),
    };

    IEnumerator EnemyShots()
    {
        player.hp = player.maxHp = 99; // a stray shot must not end the run
        // Face the longest clear line in the room; the "enemy" stands up to 9 m down it.
        float far = 0f;
        for (int i = 0; i < 24; i++)
        {
            var d = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            float free = Free(P, d);
            if (free > far) { far = free; F = d; }
        }
        far = Mathf.Min(9f, far - 1.2f);
        R = Vector3.Cross(Vector3.up, F);
        player.yaw = Mathf.Atan2(F.x, F.z) * Mathf.Rad2Deg;
        foreach (var s in Shots)
        {
            Vector3 src = P + F * far;
            src.y = s.height;
            // 1) From the play camera, fired at the player as in a fight, caught 3 m before it arrives.
            Clear();
            PlayCam();
            yield return new WaitForSeconds(0.25f);
            Volley(s.mat, s.radius, s.speed, src, new Vector3(P.x, s.height, P.z), s.pattern, s.arc);
            yield return new WaitForSeconds((far - 3f) / s.speed);
            yield return Shot($"shot_{s.id}_game.png");
            // 2) Close-up from the side: two shots crossing 2.2 m in front of a still camera, coming
            //    from the roomier side.
            Clear();
            Vector3 mid = P + F * 3.5f;
            mid.y = s.height;
            Vector3 across = Free(mid, -R) >= Free(mid, R) ? R : -R;
            float run = Mathf.Clamp(Mathf.Min(s.speed * 0.35f, Free(mid, -across) - 1.5f), 0.5f, 4f);
            Look(mid - F * 2.2f + Vector3.up * 0.35f, mid);
            Bullet.Fire(mid - across * run, across * s.speed, false, s.radius, s.mat, 4f);
            Bullet.Fire(mid - across * (run + 1.1f) + F * 0.6f, across * s.speed, false, s.radius, s.mat, 4f);
            yield return new WaitForSeconds(run / s.speed);
            yield return Shot($"shot_{s.id}_side.png");
            // 3) Close-up head-on: coming at a camera at chest height, caught 2.5 m away.
            Clear();
            Vector3 eye = P + F * 0.8f + Vector3.up * 1.3f;
            Look(eye, src + Vector3.up * 0.2f);
            Vector3 by = new Vector3(eye.x, s.height, eye.z) + R * 0.6f;
            Bullet.Fire(src, (by - src).normalized * s.speed, false, s.radius, s.mat, 4f);
            yield return new WaitForSeconds((Vector3.Distance(src, by) - 2.5f) / s.speed);
            yield return Shot($"shot_{s.id}_front.png");
        }
        // The bomber's lob: from the play camera coming down, and close by at the top of the arc.
        Clear();
        PlayCam();
        yield return new WaitForSeconds(0.25f);
        EnemyBomb.Throw(P + F * far + Vector3.up * 1.4f, P + F * 3.5f + R * 2.5f, 1.0f, 2.2f);
        yield return new WaitForSeconds(0.7f);
        yield return Shot("shot_bomber_game.png");
        Clear();
        Vector3 land = P + F * 3f;
        Look(land - F * 2.5f + R * 0.8f + Vector3.up * 1.7f, land + F * 2f + Vector3.up * 2.4f);
        EnemyBomb.Throw(land + F * 4f + Vector3.up * 1.4f, land, 1.0f, 2.2f);
        yield return new WaitForSeconds(0.45f);
        yield return Shot("shot_bomber_side.png");
        Clear();
        PlayCam();
    }

    /// Clear floor from `from` along `dir` (chest height) before a wall, up to 30 m.
    static float Free(Vector3 from, Vector3 dir) =>
        Physics.Raycast(new Vector3(from.x, 1f, from.z), dir, out var hit, 30f, Layers.WorldMask, QueryTriggerInteraction.Ignore) ? hit.distance : 30f;

    /// One shot, a fan aimed at `at`, or a ring around `src`, like Enemy.Shoot / Fan / Ring.
    static void Volley(string mat, float radius, float speed, Vector3 src, Vector3 at, int pattern, float arc)
    {
        Vector3 aim = at - src;
        aim.y = 0f;
        aim.Normalize();
        if (pattern == 0) { Bullet.Fire(src, aim * speed, false, radius, mat, 4f); return; }
        int n = Mathf.Abs(pattern);
        for (int i = 0; i < n; i++)
        {
            var d = pattern > 0
                ? Quaternion.Euler(0f, -arc / 2f + arc * i / (n - 1), 0f) * aim
                : Quaternion.Euler(0f, 360f * i / n, 0f) * aim;
            Bullet.Fire(src, d * speed, false, radius, mat, 4f);
        }
    }

    /// Removes every enemy shot and bomb still in the air.
    static void Clear()
    {
        foreach (var b in Bullet.Live.ToArray()) if (b && !b.HitsEnemies) Destroy(b.gameObject);
        foreach (var b in FindObjectsByType<EnemyBomb>(FindObjectsSortMode.None)) Destroy(b.gameObject);
    }

    /// Waits for the top of a jump.
    IEnumerator Apex()
    {
        yield return null;
        while (player.VerticalSpeed > 0f) yield return null;
    }

    /// Waits until the player is back on the floor (at most 3 s).
    IEnumerator Land()
    {
        for (float t = 0f; t < 3f && !player.Grounded; t += Time.deltaTime) yield return null;
    }

    /// Three enemy shots fired at the dome from just outside it.
    void Volley(Vector3 P, Vector3 F, Vector3 R, int n)
    {
        Vector3 centre = P + Vector3.up * 0.9f;
        for (int i = 0; i < n; i++)
        {
            Vector3 dir = (F + R * (i - 1) * 0.45f + Vector3.up * 0.15f * i).normalized;
            Vector3 from = centre + dir * 2.6f;
            Bullet.Fire(from, (centre - from).normalized * 12f, false, 0.14f, "bullet_enemy", 2f, 1);
        }
    }

    Enemy Dummy(Vector3 at, EnemyKind kind)
    {
        var go = new GameObject("vfx_dummy");
        go.transform.position = new Vector3(at.x, 0.05f, at.z);
        var e = go.AddComponent<Enemy>();
        e.Setup(kind, 1);
        e.hp = e.maxHp = 100;
        go.transform.rotation = Quaternion.LookRotation(player.transform.position - go.transform.position);
        return e;
    }

    /// A fixed close-up camera with the HUD hidden.
    /// The magic circles: each drawing itself in, then whole from the play camera and from above, and the
    /// storm as the mage really casts it (meteors falling) and the star staff's small one.
    IEnumerator Circles()
    {
        foreach (var style in new[] { MagicCircle.Style.Fire, MagicCircle.Style.Holy })
        {
            string name = style == MagicCircle.Style.Fire ? "fire" : "holy";
            float radius = style == MagicCircle.Style.Fire ? 5f : 4.5f;
            Vector3 at = P + F * 7f;
            PlayCam();
            var c = MagicCircle.Spawn(at, radius, style, 0.5f);
            yield return new WaitForSeconds(0.2f);
            yield return Shot($"circle_{name}_drawing.png");
            yield return new WaitForSeconds(0.7f);
            yield return Shot($"circle_{name}_game.png");
            Look(at - F * 0.5f + Vector3.up * 11f, at);
            yield return Shot($"circle_{name}_top.png");
            Look(at - F * (radius + 3.5f) + R * 1.5f + Vector3.up * 1.4f, at + Vector3.up * 0.4f);
            yield return Shot($"circle_{name}_low.png");
            c.Pulse();
            yield return new WaitForSeconds(0.08f);
            yield return Shot($"circle_{name}_pulse.png");
            c.End();
            PlayCam();
            yield return new WaitForSeconds(1.2f);
        }

        // The real thing: the mage casts the storm on the crosshair, then the staff.
        player.skill.Setup(player, Classes.Get("mage"));
        player.skill.cooldownLeft = 0f;
        player.pitch = 18f;
        yield return new WaitForSeconds(0.3f);
        player.skill.TryUse();
        yield return new WaitForSeconds(0.45f);
        yield return Shot("storm_cast.png");
        yield return new WaitForSeconds(0.6f);
        yield return Shot("storm_meteors.png");
        yield return new WaitForSeconds(1.6f);
        player.Attack();
        yield return new WaitForSeconds(0.25f);
        yield return Shot("staff_mark.png");
        yield return new WaitForSeconds(0.4f);
        yield return Shot("staff_strike.png");
        yield return new WaitForSeconds(1.5f);
        // The priest's sanctuary round the player.
        player.skill.Setup(player, Classes.Get("priest"));
        player.skill.cooldownLeft = 0f;
        player.skill.TryUse();
        yield return new WaitForSeconds(0.8f);
        yield return Shot("sanctuary_game.png");
        yield return new WaitForSeconds(5f);
        Debug.Log($"VFXSHOTS circles left={MagicCircle.Live}");
    }

    void Look(Vector3 from, Vector3 at)
    {
        player.externalCamera = true;
        hud.enabled = false;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
    }

    /// Back to the normal third-person camera and HUD.
    void PlayCam()
    {
        player.externalCamera = false;
        hud.enabled = true;
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
        Debug.Log("VFXSHOT " + name);
        yield return null;
        yield return null;
    }
}
