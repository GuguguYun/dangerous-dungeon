using System.Collections.Generic;
using UnityEngine;

/// Generates one floor and builds it from the Blender kit on a 4 m tile grid.
/// Rooms sit on a coarse cell grid (Soul Knight style): a main path from the start room through
/// combat rooms to the end room, plus a chest room on a branch; straight corridors join
/// neighbouring cells. Walls go on every floor edge that faces empty space, a door frame where a
/// room meets a corridor, and a pillar on every corner a wall touches.
public class Dungeon : MonoBehaviour
{
    public const float T = 4f;
    const int CellSpan = 11; // tiles between neighbouring room centres
    const int Corridor = -1;

    public enum Kind { Start, Combat, Chest, End, Boss, Shop, Altar, Trial }

    public class RoomInfo
    {
        public Kind kind;
        public Vector2Int cell;
        public int half;
        public Vector3 min, max;
        public bool visited;
        public Room combat;
        public Vector3 Center => (min + max) * 0.5f;
        public bool Contains(Vector3 p, float margin = 0f) =>
            p.x > min.x + margin && p.x < max.x - margin && p.z > min.z + margin && p.z < max.z - margin;
    }

    struct Door
    {
        public Vector3 pos;
        public Quaternion rot;
        public int a, b;
    }

    static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    public Vector3 playerStart;
    public float startYaw;
    public readonly List<RoomInfo> rooms = new List<RoomInfo>();
    public readonly List<(Vector2Int a, Vector2Int b)> links = new List<(Vector2Int, Vector2Int)>();
    public readonly List<Room> combatRooms = new List<Room>();
    public GameObject portal;
    public System.Action PortalEntered;
    public RoomInfo current;

    Hud hud;
    PlayerController player;
    System.Random rng;
    readonly Dictionary<Vector2Int, int> tiles = new Dictionary<Vector2Int, int>();
    readonly List<Door> doors = new List<Door>();
    struct WallSlot
    {
        public Vector3 pos, inward;
        public int room;
    }
    readonly List<WallSlot> wallSlots = new List<WallSlot>();
    RoomInfo endRoom;
    public int loops;                                       // extra corridors that make the floor a loop
    public readonly List<Room> trialRooms = new List<Room>();
    public RoomInfo shopRoom;

    static Vector3 Center(Vector2Int t) => new Vector3(t.x * T, 0f, t.y * T);

    public void Build(Hud hud, int floor, int seed)
    {
        this.hud = hud;
        rng = new System.Random(seed);
        GenerateLayout(floor);
        FillTiles();
        BuildGeometry();
        BuildRooms(floor);
        PlaceTraps(floor);
        Decorate();
        Debug.Log($"DUNGEON floor={floor} seed={seed} rooms={rooms.Count} tiles={tiles.Count}");
    }

    public void SetPlayer(PlayerController p)
    {
        player = p;
        foreach (var r in combatRooms) r.player = p;
        if (portal) portal.GetComponent<Portal>().player = p;
    }

    void Update()
    {
        if (!player) return;
        current = null;
        foreach (var r in rooms)
        {
            if (r.Contains(player.transform.position))
            {
                r.visited = true;
                current = r;
            }
        }
    }

    // ---------- layout ----------

    void GenerateLayout(int floor)
    {
        int combatOnPath = Mathf.Min(2 + floor, 5); // 3, 4, 5, 5
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var path = new List<Vector2Int> { Vector2Int.zero };
            var used = new HashSet<Vector2Int> { Vector2Int.zero };
            bool stuck = false;
            while (path.Count < combatOnPath + 2)
            {
                var options = FreeNeighbours(path[path.Count - 1], used);
                if (options.Count == 0)
                {
                    stuck = true;
                    break;
                }
                var next = options[rng.Next(options.Count)];
                path.Add(next);
                used.Add(next);
            }
            if (stuck) continue;

            // Chest room on a branch off a middle path room.
            var branchFrom = new List<Vector2Int>();
            for (int i = 1; i < path.Count - 1; i++)
                if (FreeNeighbours(path[i], used).Count > 0) branchFrom.Add(path[i]);
            if (branchFrom.Count == 0) continue;
            var host = branchFrom[rng.Next(branchFrom.Count)];
            var chestOptions = FreeNeighbours(host, used);
            var chest = chestOptions[rng.Next(chestOptions.Count)];
            used.Add(chest);

            // A shop on another branch (retry the layout until there is room for one).
            var shopFrom = new List<Vector2Int>();
            for (int i = 1; i < path.Count - 1; i++)
                if (FreeNeighbours(path[i], used).Count > 0) shopFrom.Add(path[i]);
            if (shopFrom.Count == 0) continue;
            var shopHost = shopFrom[rng.Next(shopFrom.Count)];
            var shopOptions = FreeNeighbours(shopHost, used);
            var shop = shopOptions[rng.Next(shopOptions.Count)];
            used.Add(shop);

            // Optional branches where there is room: a blood altar and a trial room.
            (Vector2Int host, Vector2Int cell)? Branch()
            {
                var hosts = new List<Vector2Int>();
                for (int i = 1; i < path.Count - 1; i++)
                    if (FreeNeighbours(path[i], used).Count > 0) hosts.Add(path[i]);
                if (hosts.Count == 0) return null;
                var h = hosts[rng.Next(hosts.Count)];
                var opts = FreeNeighbours(h, used);
                var c = opts[rng.Next(opts.Count)];
                used.Add(c);
                return (h, c);
            }
            var altar = Branch();
            var trial = Branch();

            rooms.Clear();
            links.Clear();
            for (int i = 0; i < path.Count; i++)
            {
                Kind kind = i == 0 ? Kind.Start : i == path.Count - 1 ? Kind.Boss : Kind.Combat;
                int half = kind == Kind.Combat ? (rng.Next(2) == 0 ? 2 : 3) : kind == Kind.Boss ? 4 : 1;
                AddRoom(kind, path[i], half);
                if (i > 0) links.Add((path[i - 1], path[i]));
            }
            AddRoom(Kind.Chest, chest, 1);
            links.Add((host, chest));
            AddRoom(Kind.Shop, shop, 1);
            links.Add((shopHost, shop));
            if (altar.HasValue) { AddRoom(Kind.Altar, altar.Value.cell, 1); links.Add((altar.Value.host, altar.Value.cell)); }
            if (trial.HasValue) { AddRoom(Kind.Trial, trial.Value.cell, 2); links.Add((trial.Value.host, trial.Value.cell)); }

            // Loops: neighbouring fight rooms not yet joined get a second way through (up to two).
            int loops = 0;
            for (int i = 1; i < path.Count - 1 && loops < 2; i++)
                for (int j = i + 2; j < path.Count - 1 && loops < 2; j++)
                {
                    var d = path[j] - path[i];
                    if (Mathf.Abs(d.x) + Mathf.Abs(d.y) != 1 || rng.NextDouble() < 0.4) continue;
                    links.Add((path[i], path[j]));
                    loops++;
                }
            this.loops = loops;
            return;
        }
        Debug.LogError("DUNGEON layout failed");
    }

    List<Vector2Int> FreeNeighbours(Vector2Int c, HashSet<Vector2Int> used)
    {
        var list = new List<Vector2Int>();
        foreach (var d in Dirs)
        {
            var n = c + d;
            if (!used.Contains(n) && Mathf.Abs(n.x) <= 3 && Mathf.Abs(n.y) <= 3) list.Add(n);
        }
        return list;
    }

    void AddRoom(Kind kind, Vector2Int cell, int half)
    {
        var c = cell * CellSpan;
        rooms.Add(new RoomInfo
        {
            kind = kind,
            cell = cell,
            half = half,
            min = new Vector3((c.x - half) * T - T / 2f, 0f, (c.y - half) * T - T / 2f),
            max = new Vector3((c.x + half) * T + T / 2f, 0f, (c.y + half) * T + T / 2f),
        });
    }

    RoomInfo RoomAt(Vector2Int cell) => rooms.Find(r => r.cell == cell);

    void FillTiles()
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            var c = r.cell * CellSpan;
            for (int x = c.x - r.half; x <= c.x + r.half; x++)
            for (int z = c.y - r.half; z <= c.y + r.half; z++)
                tiles[new Vector2Int(x, z)] = i;
        }
        foreach (var (a, b) in links)
        {
            var ra = RoomAt(a);
            var rb = RoomAt(b);
            var d = b - a; // unit step between cells
            var ca = a * CellSpan;
            var cb = b * CellSpan;
            var from = ca + d * (ra.half + 1);
            var to = cb - d * (rb.half + 1);
            for (var t = from; ; t += d)
            {
                tiles[t] = Corridor;
                if (t == to) break;
            }
        }
    }

    // ---------- geometry ----------

    void BuildGeometry()
    {
        var done = new HashSet<(Vector2Int, Vector2Int)>();
        var corners = new HashSet<Vector2Int>(); // in half-tile units
        foreach (var kv in tiles)
        {
            var t = kv.Key;
            Kit.Spawn("floor_tile", Center(t), Quaternion.identity, transform);
            foreach (var d in Dirs)
            {
                var n = t + d;
                var key = t.x < n.x || (t.x == n.x && t.y < n.y) ? (t, n) : (n, t);
                if (done.Contains(key)) continue;
                string piece;
                if (!tiles.TryGetValue(n, out int other)) piece = "wall";
                else if (other != kv.Value) piece = "wall_door";
                else continue;
                done.Add(key);

                var pos = Center(t) + new Vector3(d.x, 0f, d.y) * (T / 2f);
                var rot = d.x != 0 ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
                Kit.Spawn(piece, pos, rot, transform);
                if (piece == "wall_door") doors.Add(new Door { pos = pos, rot = rot, a = kv.Value, b = other });
                else if (kv.Value >= 0) wallSlots.Add(new WallSlot { pos = pos, inward = -new Vector3(d.x, 0f, d.y), room = kv.Value });
                if (d.x != 0)
                {
                    corners.Add(new Vector2Int(2 * t.x + d.x, 2 * t.y + 1));
                    corners.Add(new Vector2Int(2 * t.x + d.x, 2 * t.y - 1));
                }
                else
                {
                    corners.Add(new Vector2Int(2 * t.x + 1, 2 * t.y + d.y));
                    corners.Add(new Vector2Int(2 * t.x - 1, 2 * t.y + d.y));
                }
            }
        }
        foreach (var c in corners)
            Kit.Spawn("pillar", new Vector3(c.x * T / 2f, 0f, c.y * T / 2f), Quaternion.identity, transform);
    }

    void BuildRooms(int floor)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            var info = rooms[i];
            float size = (info.half * 2 + 1) * T;
            AddLight(info.Center + Vector3.up * 3.5f, new Color(1f, 0.85f, 0.65f), size * 0.8f, 4f);
            float inset = info.half * T + T / 2f - 1.6f; // just inside the corner pillars
            foreach (var corner in new[] { new Vector3(-inset, 0f, -inset), new Vector3(inset, 0f, -inset), new Vector3(-inset, 0f, inset), new Vector3(inset, 0f, inset) })
                Props.Brazier(info.Center + corner, transform);

            switch (info.kind)
            {
                case Kind.Start:
                    playerStart = info.Center + Vector3.up * 0.1f;
                    var first = links[0].b - links[0].a;
                    startYaw = Mathf.Atan2(first.x, first.y) * Mathf.Rad2Deg;
                    break;
                case Kind.Combat:
                case Kind.Boss:
                    MakeCombatRoom(i, info, floor);
                    break;
                case Kind.Chest:
                    var host = links.Find(l => l.b == info.cell).a - info.cell;
                    var face = Quaternion.LookRotation(new Vector3(host.x, 0f, host.y));
                    var chest = Props.Spawn("chest", info.Center, face, transform);
                    var real = chest.AddComponent<Chest>();
                    real.dungeon = this;
                    real.mimic = rng.NextDouble() < Chest.MimicChance;
                    real.mechChance = 0.3f;
                    break;
                case Kind.Altar:
                    Altar.Create(info.Center, transform);
                    break;
                case Kind.Trial:
                {
                    // A fight room that only the statue starts: three waves, all elites, an epic chest.
                    var trialRoom = MakeCombatRoom(i, info, floor);
                    combatRooms.Remove(trialRoom);
                    trialRooms.Add(trialRoom);
                    trialRoom.manual = true;
                    trialRoom.forceElite = true;
                    trialRoom.waves = new[]
                    {
                        new[] { EnemyKind.Gunner, EnemyKind.Charger },
                        new[] { EnemyKind.Spreader, EnemyKind.Bomber, EnemyKind.Gunner },
                        new[] { EnemyKind.Archer, EnemyKind.Charger, EnemyKind.Bomber },
                    };
                    var statue = TrialStatue.Create(info.Center, trialRoom, transform);
                    trialRoom.blocked.Add(info.Center);
                    trialRoom.Cleared += r =>
                    {
                        var chest = Props.Spawn("chest", r.Center() + Vector3.forward * 3f, Quaternion.identity, transform);
                        var c = chest.AddComponent<Chest>();
                        c.dungeon = this;
                        c.minRarity = 3;
                        Pickup.SpawnGold(r.Center(), 15);
                        hud.Show("试炼完成！史诗宝箱出现了", 2.5f);
                    };
                    break;
                }
                case Kind.Shop:
                {
                    var from = RoomAt(links.Find(l => l.b == info.cell).a);
                    Shop.Build(info.Center, from.Center, transform);
                    shopRoom = info;
                    break;
                }
                case Kind.End:
                    endRoom = info;
                    SpawnPortal(info.Center);
                    break;
            }
        }
    }

    Room MakeCombatRoom(int index, RoomInfo info, int floor)
    {
        var room = new GameObject($"room_{index}_{info.kind}").AddComponent<Room>();
        room.transform.SetParent(transform, false);
        room.min = info.min;
        room.max = info.max;
        room.hud = hud;
        room.floor = floor;
        room.boss = info.kind == Kind.Boss;
        int baseCount = 2 + floor + (info.half >= 3 ? 1 : 0);
        room.waves = room.boss
            ? new[] { new[] { EnemyKind.Boss } }
            : new[] { Wave(baseCount, floor), Wave(baseCount + 1, floor) };
        foreach (var door in doors)
            if (door.a == index || door.b == index)
                room.gates.Add(Kit.Spawn("gate", door.pos, door.rot, room.transform));

        if (room.boss)
        {
            // Arena: four pillars to duck behind, no crates.
            foreach (var p in new[] { new Vector3(-6f, 0f, -6f), new Vector3(6f, 0f, -6f), new Vector3(-6f, 0f, 6f), new Vector3(6f, 0f, 6f) })
            {
                Kit.Spawn("pillar", info.Center + p, Quaternion.identity, transform);
                room.blocked.Add(info.Center + p);
            }
        }
        // Bigger fight rooms get a layout: four pillars, or low walls (chest-high cover that stops level
        // shots but not lobbed bombs), or just the scattered crates below.
        int layout = room.boss || info.half < 2 ? 0 : rng.Next(3);
        float span = info.half * T;
        bool ClearOfDoors(Vector3 p)
        {
            foreach (var door in doors) if (Vector3.Distance(p, door.pos) < 5f) return false;
            return true;
        }
        if (layout == 1)
        {
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                {
                    var p = info.Center + new Vector3(sx, 0f, sz) * span * 0.45f;
                    if (!ClearOfDoors(p)) continue;
                    Kit.Spawn("pillar", p, Quaternion.identity, transform);
                    room.blocked.Add(p);
                }
        }
        else if (layout == 2)
        {
            for (int k = 0; k < 4; k++)
            {
                var dir = Quaternion.Euler(0f, 90f * k + 45f, 0f) * Vector3.forward;
                var p = info.Center + dir * span * 0.5f;
                if (!ClearOfDoors(p)) continue;
                var cover = Kit.Spawn("wall", p, Quaternion.LookRotation(dir), transform); // runs across the room's radius
                cover.transform.localScale = new Vector3(0.75f, 0.42f, 1f);
                room.blocked.Add(p);
            }
        }
        int crates = room.boss ? 0 : info.half * 2 - 1 - (layout > 0 ? 2 : 0);
        float inner = info.half * T;
        for (int k = 0, tries = 0; k < crates && tries < 60; tries++)
        {
            var p = info.Center + new Vector3((float)(rng.NextDouble() * 2 - 1) * (inner - 2f), 0f, (float)(rng.NextDouble() * 2 - 1) * (inner - 2f));
            if (Vector3.Distance(p, info.Center) < 3f) continue;
            bool nearDoor = false;
            foreach (var door in doors) if (Vector3.Distance(p, door.pos) < 5f) nearDoor = true;
            bool nearCrate = room.blocked.Exists(b => Vector3.Distance(b, p) < 3f);
            if (nearDoor || nearCrate) continue;
            var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            // Cover is physical: bullets shove it, enough damage breaks it; every third barrel is explosive.
            if (k % 2 == 1 && Props.Has("barrel"))
                PhysicsProp.Make(Props.Spawn("barrel", p, rot, transform), 12f, 6, k % 3 == 0 || k == 1);
            else
                PhysicsProp.Make(Kit.Spawn("crate", p, rot, transform), 18f, 9);
            room.blocked.Add(p);
            k++;
        }
        room.SetGates(false);
        room.Cleared += r =>
        {
            if (info.kind != Kind.Boss) return;
            SpawnPortal(r.Center());
            // Reward: a rare-or-better weapon chest towards the way in, and a little health.
            var back = links.Find(l => l.b == info.cell).a - info.cell;
            var chest = Props.Spawn("chest", r.Center() + new Vector3(back.x, 0f, back.y) * 5f, Quaternion.LookRotation(new Vector3(back.x, 0f, back.y)), transform);
            var c = chest.AddComponent<Chest>();
            c.dungeon = this;
            c.minRarity = 2;
            player?.Heal(2);
        };
        info.combat = room;
        combatRooms.Add(room);
        return room;
    }

    /// Floor 1 meets gunners and chargers; spreaders join from floor 2.
    EnemyKind[] Wave(int count, int floor)
    {
        // Floor 1 adds the odd bomber and shield guard; skeleton archers join from floor 2, more guards on 3+.
        var pool = floor == 1
            ? new[] { EnemyKind.Gunner, EnemyKind.Gunner, EnemyKind.Charger, EnemyKind.Gunner, EnemyKind.Bomber, EnemyKind.Guard }
            : floor == 2
                ? new[] { EnemyKind.Gunner, EnemyKind.Charger, EnemyKind.Spreader, EnemyKind.Bomber, EnemyKind.Archer, EnemyKind.Guard }
                : new[] { EnemyKind.Gunner, EnemyKind.Charger, EnemyKind.Spreader, EnemyKind.Bomber, EnemyKind.Archer, EnemyKind.Archer, EnemyKind.Guard, EnemyKind.Guard };
        var wave = new EnemyKind[count];
        for (int i = 0; i < count; i++) wave[i] = pool[rng.Next(pool.Length)];
        return wave;
    }

    /// Hazards on tile centres inside fight rooms (never the middle tile, door mouths or cover):
    /// floor 1 spikes and gas canisters; floor 2 fire pits, lava cracks, gas; floor 3 adds ice.
    void PlaceTraps(int floor)
    {
        string[] pool = floor switch
        {
            1 => new[] { "spikes", "spikes", "gas" },
            2 => new[] { "fire", "lava", "gas", "fire" },
            3 => new[] { "spikes", "ice", "fire", "gas" },
            _ => new[] { "fire", "lava", "spikes", "gas", "fire" }, // the throne: fire and lava again
        };
        foreach (var info in rooms)
        {
            if (info.kind != Kind.Combat && info.kind != Kind.Boss) continue;
            var room = info.combat;
            int count = info.kind == Kind.Boss ? 2 : 1 + (info.half >= 3 ? 1 : 0) + (floor >= 2 ? 1 : 0);
            for (int k = 0, tries = 0; k < count && tries < 80; tries++)
            {
                int tx = rng.Next(-info.half, info.half + 1), tz = rng.Next(-info.half, info.half + 1);
                if (tx == 0 && tz == 0) continue;
                var p = Center(info.cell * CellSpan + new Vector2Int(tx, tz));
                if (doors.Exists(d => Vector3.Distance(d.pos, p) < 5f)) continue;
                if (room.blocked.Exists(b => Vector3.Distance(b, p) < 2.6f)) continue;
                string type = pool[rng.Next(pool.Length)];
                switch (type)
                {
                    case "fire": FirePit.Create(p, transform, (float)rng.NextDouble() * 3f); break;
                    case "lava": LavaCrack.Create(p, (float)rng.NextDouble() * 180f, transform); break;
                    case "ice": IcePatch.Create(p, transform); break;
                    case "gas":
                        if (!Props.Has("gas_crate")) continue;
                        PhysicsProp.Make(Props.Spawn("gas_crate", p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), transform), 14f, 3).toxic = true;
                        break;
                    default: SpikeTrap.Create(p, transform); break;
                }
                room.blocked.Add(p);
                k++;
            }
        }
    }

    /// Meshy decorations (skipped if a model is missing): banners on walls, skulls and rubble along
    /// the walls, broken pillars as extra cover, weapon racks in safe rooms, candles by bosses.
    void Decorate()
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            var info = rooms[i];
            var slots = wallSlots.FindAll(s => s.room == i);
            Shuffle(slots);
            int used = 0;
            if (Props.Has("banner"))
                for (int k = 0; k < Mathf.Min(slots.Count, info.kind == Kind.Boss ? 4 : 2); k++, used++)
                    Props.Spawn("banner", slots[used].pos + slots[used].inward * 0.33f + Vector3.up * 0.9f, Quaternion.LookRotation(slots[used].inward), transform, false);
            string[] floorDecor = { "skulls", "rubble" };
            for (int k = 0; k < (info.half >= 2 ? 3 : 1) && used < slots.Count; k++, used++)
            {
                string id = floorDecor[rng.Next(floorDecor.Length)];
                if (!Props.Has(id)) continue;
                var s = slots[used];
                Props.Spawn(id, s.pos + s.inward * 1.1f + Vector3.Cross(Vector3.up, s.inward) * (float)(rng.NextDouble() * 2 - 1), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), transform, false);
            }
            if ((info.kind == Kind.Start || info.kind == Kind.Chest) && Props.Has("weapon_rack") && used < slots.Count)
            {
                var s = slots[used++];
                Props.Spawn("weapon_rack", s.pos + s.inward * 0.7f, Quaternion.LookRotation(s.inward), transform);
            }
            if (info.kind == Kind.Combat && info.half >= 3 && Props.Has("broken_pillar") && info.combat)
            {
                for (int tries = 0; tries < 20; tries++)
                {
                    var p = info.Center + new Vector3((float)(rng.NextDouble() * 2 - 1) * (info.half * T - 3f), 0f, (float)(rng.NextDouble() * 2 - 1) * (info.half * T - 3f));
                    if (Vector3.Distance(p, info.Center) < 3f || info.combat.blocked.Exists(b => Vector3.Distance(b, p) < 3f)) continue;
                    if (doors.Exists(dr => Vector3.Distance(dr.pos, p) < 5f)) continue;
                    Props.Spawn("broken_pillar", p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), transform);
                    info.combat.blocked.Add(p);
                    break;
                }
            }
            if ((info.kind == Kind.Boss || info.kind == Kind.Start) && Props.Has("candles"))
            {
                float inset = info.half * T + T / 2f - 3f;
                foreach (var c in new[] { new Vector3(-inset, 0f, 0f), new Vector3(inset, 0f, 0f) })
                {
                    var go = Props.Spawn("candles", info.Center + c, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), transform, false);
                    var l = new GameObject("candle_light").AddComponent<Light>();
                    l.transform.SetParent(go.transform, false);
                    l.transform.localPosition = Vector3.up * 0.6f;
                    l.type = LightType.Point;
                    l.color = Props.FireColor;
                    l.range = 4f;
                    l.intensity = 2f;
                    l.gameObject.AddComponent<Flicker>();
                }
            }
        }
    }

    void Shuffle<TItem>(List<TItem> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void AddLight(Vector3 pos, Color color, float range, float intensity)
    {
        var go = new GameObject("light");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
    }

    void SpawnPortal(Vector3 at)
    {
        if (portal) return;
        portal = new GameObject("portal");
        portal.transform.SetParent(transform, false);
        portal.transform.position = at;
        Kit.Spawn("portal_ring", at, Quaternion.identity, portal.transform, false);
        Kit.Spawn("portal_core", at, Quaternion.identity, portal.transform, false);
        var l = new GameObject("portal_light").AddComponent<Light>();
        l.transform.SetParent(portal.transform, false);
        l.transform.localPosition = Vector3.up * 1.6f;
        l.type = LightType.Point;
        l.color = Palette.ColorOf("portal_ring");
        l.range = 8f;
        l.intensity = 2f;
        var p = portal.AddComponent<Portal>();
        p.player = player;
        p.Entered = () => PortalEntered?.Invoke();
    }

    public void ShowMessage(string text, float seconds) => hud.Show(text, seconds);

    /// Every room tile reachable from the start room by walking over floor tiles.
    public bool AllRoomsConnected()
    {
        var start = rooms[0].cell * CellSpan;
        var seen = new HashSet<Vector2Int> { start };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            foreach (var d in Dirs)
            {
                var n = t + d;
                if (tiles.ContainsKey(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        return seen.Count == tiles.Count;
    }
}
