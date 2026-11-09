using System.Collections.Generic;
using UnityEngine;

/// A combat room: locks its gates when the player walks in, runs its enemy waves (plus anything a
/// boss summons), then reopens the gates, drops a reward and reports Cleared.
public class Room : MonoBehaviour
{
    public enum State { Idle, Fighting, Cleared }

    public State state = State.Idle;
    public Vector3 min, max;
    public int floor = 1;
    public bool boss;
    public List<GameObject> gates = new List<GameObject>();
    public List<Vector3> blocked = new List<Vector3>();
    public EnemyKind[][] waves = { new[] { EnemyKind.Gunner } };
    public int waveIndex;
    public int alive;
    public PlayerController player;
    public Hud hud;
    public System.Action<Room> Cleared;
    public bool manual;      // a trial room: only the statue starts it
    public bool forceElite;  // ... and every enemy in it is an elite

    public Vector3 Center() => (min + max) * 0.5f;

    bool Inside(Vector3 p, float margin) =>
        p.x > min.x + margin && p.x < max.x - margin && p.z > min.z + margin && p.z < max.z - margin;

    void Update()
    {
        if (!manual && state == State.Idle && player && !player.dead && Inside(player.transform.position, 2f))
            Begin();
    }

    public void Begin()
    {
        state = State.Fighting;
        SetGates(true);
        Tips.Show("lock", "战斗房会锁门：清光所有敌人，门才会打开");
        if (!boss) hud.Show("房间已锁定！清空所有敌人", 2f);
        if (boss) Sfx.Play("boss", 1f, 0f);
        Invoke(nameof(SpawnWave), boss ? 1.2f : 0.8f);
    }

    public bool GatesClosed() => gates.Count > 0 && gates[0].activeSelf;

    public void SetGates(bool closed)
    {
        if (gates.Count > 0 && gates[0].activeSelf != closed && state != State.Idle)
        {
            Sfx.Play(closed ? "gate_close" : "gate_open", 0.9f, 0.02f);
            Sfx.Play("door", 0.5f, 0.02f);
        }
        foreach (var g in gates) g.SetActive(closed);
    }

    void SpawnWave()
    {
        // One after another, 0.1 s apart: a wave appearing in a single frame (seven Meshy characters set up
        // at once, the first time on a floor) stalled that frame for 45 ms.
        int n = 0;
        foreach (var kind in waves[waveIndex])
        {
            if (kind == EnemyKind.Boss) Spawn(kind, Center() + new Vector3(0f, 0.1f, 0f));
            else StartCoroutine(SpawnAfterWarning(kind, SpawnPoint(), delay: Mathf.Min(0.1f * n++, 0.8f)));
        }
    }

    /// Mark the spot first so nothing pops up unannounced next to the player. (warn false: a slime's
    /// halves come out of its body, on the next frame: never in the middle of a loop over the live enemies.)
    System.Collections.IEnumerator SpawnAfterWarning(EnemyKind kind, Vector3 at, bool mini = false, bool warn = true, float delay = 0f)
    {
        alive++; // counted at once, so the room can't think it is clear in between
        hud.enemiesLeft = alive;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (warn)
        {
            var marker = SpawnMarker.Create(at);
            yield return new WaitForSeconds(0.7f);
            Destroy(marker.gameObject);
        }
        else yield return null;
        Spawn(kind, at, counted: true, mini);
    }

    void Spawn(EnemyKind kind, Vector3 at, bool counted = false, bool mini = false)
    {
        var go = new GameObject("enemy_" + kind);
        go.transform.SetParent(transform, false);
        go.transform.position = at;
        var e = go.AddComponent<Enemy>();
        e.Setup(kind, floor);
        // Some minions come as elites with an affix; a splitting elite's halves come out small.
        if (mini) e.MakeMini();
        else if (kind != EnemyKind.Boss && (forceElite || (Enemy.ElitesEnabled && Random.value < Enemy.EliteChance(floor))))
        {
            e.MakeElite((EliteKind)Random.Range(1, 5));
            Tips.Show("elite", "精英怪：头上写着它的词缀，更耐打，掉更多金币");
        }
        e.Split = (k, p) => StartCoroutine(SpawnAfterWarning(k, ClampInside(p), true));
        e.SplitNow = (k, p) => StartCoroutine(SpawnAfterWarning(k, ClampInside(p), true, warn: false));
        if (kind == EnemyKind.Slime && !mini) Tips.Show("slime", "分裂史莱姆：打死会分成两只小的。爆炸、雷电一下打一群");
        if (kind == EnemyKind.Boss) hud.BossBanner(e.displayName, $"第 {floor} 层首领 · {e.Epithet}");
        e.target = player;
        e.Died += OnEnemyDied;
        e.Summon = (k, p) => StartCoroutine(SpawnAfterWarning(k, ClampInside(p)));
        if (!counted)
        {
            alive++;
            hud.enemiesLeft = alive;
        }
    }

    Vector3 ClampInside(Vector3 p) => new Vector3(
        Mathf.Clamp(p.x, min.x + 2f, max.x - 2f), 0.1f, Mathf.Clamp(p.z, min.z + 2f, max.z - 2f));

    Vector3 SpawnPoint()
    {
        Vector3 lo = min + new Vector3(2.5f, 0f, 2.5f);
        Vector3 hi = max - new Vector3(2.5f, 0f, 2.5f);
        Vector3 best = Center();
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var p = new Vector3(Random.Range(lo.x, hi.x), 0.1f, Random.Range(lo.z, hi.z));
            best = p;
            if (player && Vector3.Distance(p, player.transform.position) < 7f) continue;
            bool ok = true;
            foreach (var b in blocked)
            {
                if (new Vector2(p.x - b.x, p.z - b.z).magnitude < 1.8f)
                {
                    ok = false;
                    break;
                }
            }
            if (ok) return p;
        }
        return best;
    }

    void OnEnemyDied(Enemy e)
    {
        alive--;
        hud.enemiesLeft = alive;
        if (alive > 0) return;
        waveIndex++;
        if (waveIndex < waves.Length)
        {
            hud.Show($"第 {waveIndex + 1} 波", 1.5f);
            Invoke(nameof(SpawnWave), 1f);
            return;
        }
        state = State.Cleared;
        hud.enemiesLeft = -1;
        SetGates(false);
        hud.Show(boss ? "首领被击败！宝箱和传送门出现了" : "房间已清空！", 2.5f);
        // Fights pay in health, energy and gold; weapons and mechs come only from chests and the shop.
        if (!boss) DropReward();
        Cleared?.Invoke(this);
    }

    void DropReward()
    {
        var c = Center();
        if (Random.value < 0.45f) Pickup.Spawn(PickupKind.Health, c, 2);
        else Pickup.SpawnEnergy(c, 8);
        Pickup.SpawnGold(c, 6);
    }
}
