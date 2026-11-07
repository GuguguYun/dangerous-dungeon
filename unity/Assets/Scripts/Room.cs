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

    public Vector3 Center() => (min + max) * 0.5f;

    bool Inside(Vector3 p, float margin) =>
        p.x > min.x + margin && p.x < max.x - margin && p.z > min.z + margin && p.z < max.z - margin;

    void Update()
    {
        if (state == State.Idle && player && !player.dead && Inside(player.transform.position, 2f))
            Begin();
    }

    void Begin()
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
        foreach (var kind in waves[waveIndex])
        {
            if (kind == EnemyKind.Boss) Spawn(kind, Center() + new Vector3(0f, 0.1f, 0f));
            else StartCoroutine(SpawnAfterWarning(kind, SpawnPoint()));
        }
    }

    /// Mark the spot first so nothing pops up unannounced next to the player.
    System.Collections.IEnumerator SpawnAfterWarning(EnemyKind kind, Vector3 at, bool mini = false)
    {
        alive++;
        hud.enemiesLeft = alive;
        var marker = SpawnMarker.Create(at);
        yield return new WaitForSeconds(0.7f);
        Destroy(marker.gameObject);
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
        else if (kind != EnemyKind.Boss && Enemy.ElitesEnabled && Random.value < Enemy.EliteChance(floor))
        {
            e.MakeElite((EliteKind)Random.Range(1, 5));
            Tips.Show("elite", "精英怪：头上写着它的词缀，更耐打，掉更多金币");
        }
        e.Split = (k, p) => StartCoroutine(SpawnAfterWarning(k, ClampInside(p), true));
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
        if (!boss)
        {
            DropReward();
            // Sometimes a mech drops in as well (more likely after rooms that gave none).
            if (Mechs.RollDrop())
                ParkedMech.Drop(Mechs.Random(), MechSpot(), Random.Range(0f, 360f));
        }
        Cleared?.Invoke(this);
    }

    /// Clear floor near the middle for a mech to land: no walls, props or traps underneath.
    Vector3 MechSpot()
    {
        Vector3 c = Center();
        for (int i = 0; i < 16; i++)
        {
            float a = i * 67.5f;
            Vector3 p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (2.5f + 0.4f * i);
            p.y = 0f;
            if (Physics.CheckCapsule(p + Vector3.up * 1.0f, p + Vector3.up * 2.2f, 0.85f, ~(Layers.PlayerMask | Layers.EnemyMask | Layers.DebrisMask), QueryTriggerInteraction.Ignore)) continue;
            bool trap = false;
            foreach (var t in Trap.Live)
                if (t && Vector3.Distance(new Vector3(t.transform.position.x, 0f, t.transform.position.z), p) < t.radius + 1.2f) trap = true;
            if (!trap) return p;
        }
        return c + new Vector3(2.5f, 0f, -2.5f);
    }

    void DropReward()
    {
        float r = Random.value;
        var c = Center();
        if (r < 0.25f && player)
            Pickup.Spawn(PickupKind.Weapon, c, 0, Weapons.RandomExcept(player.weapons[0]?.id, player.weapons[1]?.id));
        else if (r < 0.55f) Pickup.Spawn(PickupKind.Health, c, 2);
        else Pickup.SpawnEnergy(c, 8);
        Pickup.SpawnGold(c, 4);
    }
}
