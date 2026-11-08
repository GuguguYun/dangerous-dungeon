using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// -soak <runs>: unattended play-throughs of every fight on every floor, one class after another,
/// that report what would leave a real player stuck: an enemy inside a wall or pillar, out of its
/// room or under the floor, a room that never opens again, an exception. The hero stands in each
/// room and fires at the nearest enemy (a different weapon each room, the class skill on entry);
/// whatever is still alive after a while is killed outright, so a run always reaches the end.
public class Soak : MonoBehaviour
{
    public static bool Active;
    static int runsLeft, runsDone, rooms, issues, errors, weaponTurn;
    static readonly List<string> errorSamples = new List<string>();
    readonly List<Enemy> live = new List<Enemy>();

    public PlayerController player;
    public Dungeon dungeon;

    /// From the command line, once: how many runs (each class in turn).
    public static void Init(int runs)
    {
        if (Active) return;
        Active = true;
        runsLeft = Mathf.Max(1, runs);
        RunState.ChooseClass(Classes.All[0].id);
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            // Without a window (-nographics) the engine itself logs render-target and SSAO errors, with
            // no script stack (the windowed screenshot run has none): not the game's.
            if (type == LogType.Error && string.IsNullOrEmpty(stack) && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            errors++;
            string first = stack.Split('\n')[0];
            string sample = $"{msg} @ {first}";
            if (errorSamples.Count < 8 && !errorSamples.Contains(sample)) errorSamples.Add(sample);
        };
    }

    void Issue(string what)
    {
        issues++;
        Debug.Log($"SOAK ISSUE run={runsDone + 1} class={RunState.classId} floor={RunState.floor} {what}");
    }

    public IEnumerator Run()
    {
        yield return new WaitForSeconds(0.5f);
        var fights = dungeon.combatRooms.FindAll(r => !r.boss);
        fights.AddRange(dungeon.trialRooms);
        fights.AddRange(dungeon.combatRooms.FindAll(r => r.boss));
        Debug.Log($"SOAK floor start run={runsDone + 1} class={RunState.classId} floor={RunState.floor} seed={RunState.seed} fights={fights.Count}");
        for (int i = 0; i < fights.Count; i++) yield return Fight(fights[i], i);

        if (!dungeon.portal)
        {
            Issue("no portal after the boss");
            yield return EndRun();
            yield break;
        }
        if (RunState.floor >= RunState.Floors)
        {
            yield return EndRun();
            yield break;
        }
        player.Teleport(dungeon.portal.transform.position + Vector3.up * 0.1f);
        yield return new WaitForSeconds(3f);
        Issue("the portal did not lead on"); // reaching this line means the scene never reloaded
        yield return EndRun();
    }

    IEnumerator EndRun()
    {
        runsDone++;
        runsLeft--;
        Debug.Log($"SOAK run done {runsDone} class={RunState.classId} reached floor={RunState.floor} kills={RunState.kills}");
        if (runsLeft > 0)
        {
            RunState.ChooseClass(Classes.All[runsDone % Classes.All.Count].id);
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            yield break;
        }
        foreach (var s in errorSamples) Debug.Log($"SOAK error sample: {s}");
        Debug.Log($"SOAK done runs={runsDone} rooms={rooms} issues={issues} errors={errors}");
        Application.Quit(issues + errors == 0 ? 0 : 1);
    }

    IEnumerator Fight(Room room, int index)
    {
        string where = $"room={index}{(room.boss ? " (boss)" : room.manual ? " (trial)" : "")}";
        if (!player.HoldingSkillWeapon)
        {
            player.Equip(Weapons.All[weaponTurn++ % Weapons.All.Count]);
        }
        Vector3 stand = room.Center() + new Vector3(0f, 0.1f, room.boss ? -8f : -3f);
        player.Teleport(stand);
        if (room.manual)
        {
            var statue = System.Array.Find(FindObjectsByType<TrialStatue>(FindObjectsSortMode.None), s => s.room == room);
            if (statue) statue.Use(player);
            else room.Begin();
        }
        yield return new WaitForSeconds(0.3f);
        if (room.state == Room.State.Idle)
        {
            Issue($"{where} did not lock when entered");
            yield break;
        }
        player.skill.TryUse();
        rooms++;

        var seen = new Dictionary<Enemy, float>();
        var flagged = new HashSet<(Enemy, string)>();
        var insideFor = new Dictionary<Enemy, int>();
        float t = 0f, empty = 0f, nextLook = 0f;
        int enemies = 0;
        while (room.state == Room.State.Fighting && t < 150f)
        {
            Enemy target = null;
            float best = float.MaxValue;
            bool any = false;
            live.Clear();
            live.AddRange(Enemy.Live); // killing below changes the list
            foreach (var e in live)
            {
                if (!e || e.dead || !e.transform.IsChildOf(room.transform)) continue;
                any = true;
                if (!seen.ContainsKey(e))
                {
                    seen[e] = t;
                    enemies++;
                }
                float d = Vector3.Distance(e.transform.position, player.transform.position);
                if (d < best)
                {
                    best = d;
                    target = e;
                }
                if (t >= nextLook) Look(e, room, where, t - seen[e], flagged, insideFor);
                // Whatever the hero hasn't shot by now goes down outright (bosses get longer, to use their moves).
                if (t - seen[e] > (e.kind == EnemyKind.Boss ? 16f : 7f)) e.TakeDamage(9999, (e.transform.position - player.transform.position).normalized);
            }
            if (t >= nextLook) nextLook = t + 0.25f;

            // The hero: topped up, aiming at the nearest enemy and firing (released now and then, so
            // charged weapons let go).
            player.hp = player.maxHp = 999;
            player.armor = player.maxArmor;
            player.energy = player.maxEnergy;
            if (target) Aim(target);
            player.testTrigger = target && (t % 1.2f) < 1f;

            // Spawns are announced 0.7 s ahead and waves come 1 s apart: a fight with nothing in it
            // for 5 s has lost count of its enemies, and its gates would never open.
            empty = any ? 0f : empty + Time.deltaTime;
            if (empty > 5f)
            {
                Issue($"{where} stays locked with nothing left to fight (alive={room.alive}, wave {room.waveIndex + 1}/{room.waves.Length})");
                break;
            }
            t += Time.deltaTime;
            yield return null;
        }
        player.testTrigger = false;
        if (room.state == Room.State.Fighting && empty <= 5f)
            Issue($"{where} not cleared after {t:F0} s (alive={room.alive}, wave {room.waveIndex + 1}/{room.waves.Length})");
        Debug.Log($"SOAK fight floor={RunState.floor} {where} enemies={enemies} {room.state} in {t:F1} s");
        yield return new WaitForSeconds(0.4f);
    }

    /// Where a live enemy is: inside its room, above the floor, not inside solid stone.
    void Look(Enemy e, Room room, string where, float age, HashSet<(Enemy, string)> flagged, Dictionary<Enemy, int> insideFor)
    {
        Vector3 p = e.transform.position;
        void Flag(string kind, string detail)
        {
            if (flagged.Add((e, kind)))
                Issue($"{where} {e.kind}{(e.elite != EliteKind.None ? " elite" : "")} {kind} {detail} at ({p.x:F1}, {p.y:F1}, {p.z:F1}) {age:F1} s after it appeared");
        }
        if (age < 1f) return; // still popping out of its spawn mark
        if (p.x < room.min.x - 0.2f || p.x > room.max.x + 0.2f || p.z < room.min.z - 0.2f || p.z > room.max.z + 0.2f)
            Flag("out of its room", $"(room x {room.min.x:F0}..{room.max.x:F0}, z {room.min.z:F0}..{room.max.z:F0})");
        if (p.y < -0.8f) Flag("under the floor", "");
        // Stuck for a second inside a wall, pillar or cover (a one-frame graze while walking is fine).
        string solid = InsideSolid(p + Vector3.up * 0.9f);
        insideFor[e] = solid != null ? (insideFor.TryGetValue(e, out int n) ? n + 1 : 1) : 0;
        if (insideFor[e] >= 4) Flag("inside solid", solid);
    }

    /// Inside a closed mesh, every way out first meets the inside of its faces (normals pointing on).
    public static string InsideSolid(Vector3 p)
    {
        bool was = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        Collider hitBy = null;
        int inside = 0;
        for (int k = 0; k < 8; k++)
        {
            var dir = Quaternion.Euler(0f, 45f * k, 0f) * Vector3.forward;
            if (Physics.Raycast(p, dir, out var hit, 8f, Layers.WorldMask, QueryTriggerInteraction.Ignore)
                && Vector3.Dot(hit.normal, dir) > 0f && (hitBy == null || hit.collider == hitBy))
            {
                hitBy = hit.collider;
                inside++;
            }
        }
        Physics.queriesHitBackfaces = was;
        return inside >= 6 ? hitBy.name : null;
    }

    /// Turn the view until the crosshair is on the enemy (the camera sits off the shoulder, so a few
    /// frames of correction converge on it).
    void Aim(Enemy e)
    {
        var r = player.cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 to = e.AimPoint - r.origin;
        float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float haveYaw = Mathf.Atan2(r.direction.x, r.direction.z) * Mathf.Rad2Deg;
        player.yaw += Mathf.DeltaAngle(haveYaw, wantYaw);
        float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        float havePitch = -Mathf.Asin(Mathf.Clamp(r.direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        player.pitch = Mathf.Clamp(player.pitch + (wantPitch - havePitch), -40f, 60f);
    }
}
