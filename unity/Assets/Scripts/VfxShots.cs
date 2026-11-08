using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// -vfxshots <dir>: a quick look at the loot pillars, the energy shields, the potion / crystal pickups
/// and the assassin's stealth, as close-ups and from the normal play camera, saved as PNGs; then quit.
/// It hooks itself in (no GameManager change) and takes over once the floor is built. Run with -class
/// knight so the dome is the knight's own, -class assassin for the stealth. Needs a window
/// (screenshots), like -shots.
public class VfxShots : MonoBehaviour
{
    string dir;
    PlayerController player;
    Camera cam;
    Hud hud;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-vfxshots") continue;
            Application.runInBackground = true;
            var go = new GameObject("VfxShots");
            DontDestroyOnLoad(go);
            go.AddComponent<VfxShots>().dir = args[i + 1];
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
        var dungeon = FindAnyObjectByType<Dungeon>();
        player.yaw = dungeon.startYaw;
        player.pitch = 12f;
        Vector3 P = player.transform.position;
        P.y = 0f;
        Vector3 F = Quaternion.Euler(0f, player.yaw, 0f) * Vector3.forward, R = Quaternion.Euler(0f, player.yaw + 90f, 0f) * Vector3.forward;
        yield return new WaitForSeconds(1f);

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
        Debug.Log("VFXSHOTS done");
        Application.Quit();
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
