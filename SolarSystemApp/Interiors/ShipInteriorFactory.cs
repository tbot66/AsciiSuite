using System;
using System.Collections.Generic;

namespace SolarSystemApp.Interiors
{
    internal static class ShipInteriorFactory
    {
        // =========================
        // AUTHOR-FACING PREFAB LEGEND
        // =========================
        //   '█' = wall (blocked)
        //   '#' = floor (walkable)
        //   '░' = window wall (blocked)
        //   'D' = door (walkable) [optional]
        //   ' ' = transparent (do not stamp)
        //
        // INTERNAL MAP LEGEND (InteriorMap):
        //   VOID   '#'
        //   WALL   'X'
        //   FLOOR  '.'
        //   DOOR   'D'
        //   WINDOW 'W'

        // ==========================================================
        // ROOM PREFABS (these are "building blocks" for procedural)
        // Openings are simply '#' on the outer border of the prefab.
        // ==========================================================

        // 7x7 room with a NORTH opening (top center).
        private static readonly string[] ROOM_7_N =
        {
            "███#███",
            "█#####█",
            "█#####█",
            "█#####█",
            "█#####█",
            "█#####█",
            "███████"
        };

        // 7x7 room with a SOUTH opening (bottom center).
        private static readonly string[] ROOM_7_S =
        {
            "███████",
            "█#####█",
            "█#####█",
            "█#####█",
            "█#####█",
            "█#####█",
            "███#███"
        };

        // 7x7 room with a WEST opening (left center).
        private static readonly string[] ROOM_7_W =
        {
            "███████",
            "█#####█",
            "█#####█",
            "######█",
            "█#####█",
            "█#####█",
            "███████"
        };

        // 7x7 room with an EAST opening (right center).
        private static readonly string[] ROOM_7_E =
        {
            "███████",
            "█#####█",
            "█#####█",
            "█######",
            "█#####█",
            "█#####█",
            "███████"
        };

        // 7x7 room with NORTH+SOUTH openings (a "corridor-room").
        private static readonly string[] ROOM_7_NS =
        {
            "███#███",
            "█#####█",
            "█#####█",
            "█#####█",
            "█#####█",
            "█#####█",
            "███#███"
        };

        // 7x7 room with WEST+EAST openings.
        private static readonly string[] ROOM_7_WE =
        {
            "███████",
            "█#####█",
            "█#####█",
            "#######",
            "█#####█",
            "█#####█",
            "███████"
        };

        // A slightly bigger “core” room (13w x 9h) with multiple openings.
        private static readonly string[] CORE_13 =
        {
            "██████#██████",
            "█###########█",
            "█###########█",
            "#############",
            "█###########█",
            "█###########█",
            "█###########█",
            "█###########█",
            "██████#██████"
        };

        // Collect procedural room prefabs here (you can add more freely).
        private static readonly RoomPrefab[] PROC_ROOMS =
        {
            new RoomPrefab("ROOM_7_N",  ROOM_7_N),
            new RoomPrefab("ROOM_7_S",  ROOM_7_S),
            new RoomPrefab("ROOM_7_W",  ROOM_7_W),
            new RoomPrefab("ROOM_7_E",  ROOM_7_E),
            new RoomPrefab("ROOM_7_NS", ROOM_7_NS),
            new RoomPrefab("ROOM_7_WE", ROOM_7_WE),
            new RoomPrefab("CORE_13",   CORE_13),
        };

        // ==========================================================
        // SHIP BLUEPRINT PREFABS (pre-designed whole ships)
        // These are stamped as a complete layout (not assembled).
        // Same legend: █ walls, # floors, spaces transparent.
        // ==========================================================

        // This is basically your 2nd example: a “diamond-ish” ship with inner blocks.
        private static readonly string[] BLUEPRINT_DIAMOND =
        {
            "      ███████      ",
            "    ██#######██    ",
            "   █###########█   ",
            "  █#############█  ",
            " █###############█ ",
            " █###############█ ",
            "█████████#█████████",
            "█#####█###########█",
            "█#####█###########█",
            "█#################█",
            "█#####█###########█",
            "█#####█###########█",
            "█████████#█████████",
        };

        // A smaller “barge” style blueprint (simple, wide hull).
        private static readonly string[] BLUEPRINT_BARGE =
        {
            "█████████████████████████",
            "█#######################█",
            "█#######################█",
            "█#####███████████#####███",
            "█#####█#########█#####█  ",
            "█#####█#########█#####█  ",
            "███###█#########█###███  ",
            "  █###█#########█###█    ",
            "  █###█████#█████###█    ",
            "  █#########D########█    ",
            "  █###################    ",
            "  ███████████████████     ",
        };

        // Map shipName -> blueprint.
        private static readonly Dictionary<string, string[]> BLUEPRINTS =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Diamond", BLUEPRINT_DIAMOND },
                { "Barge",   BLUEPRINT_BARGE },
            };

        // =========================
        // PUBLIC ENTRY POINT (KEEP)
        // =========================
        public static InteriorSession Create(int seed, string shipName, int w = 120, int h = 50)
        {
            var rng = new Random(seed ^ (shipName?.GetHashCode() ?? 0));
            var m = new InteriorMap(w, h, InteriorMap.VOID);

            // 1) If shipName matches a predesigned blueprint, use it.
            if (!string.IsNullOrWhiteSpace(shipName) && BLUEPRINTS.TryGetValue(shipName.Trim(), out var exactBlueprint))
            {
                StampBlueprintCentered(m, exactBlueprint, out int sx, out int sy);
                return new InteriorSession(shipName, m, sx, sy);
            }

            // 2) Otherwise: optionally choose a random predesigned blueprint sometimes
            if (BLUEPRINTS.Count > 0 && rng.NextDouble() < 0.25)
            {
                var randomBlueprint = PickRandomBlueprint(rng);
                StampBlueprintCentered(m, randomBlueprint, out int sx, out int sy);
                return new InteriorSession(shipName, m, sx, sy);
            }

            // 3) Procedural: assemble rooms by snapping openings (ports).
            BuildProceduralByRooms(m, rng, out int spawnX, out int spawnY);

            return new InteriorSession(shipName, m, spawnX, spawnY);
        }

        // ==========================================================
        // PROCEDURAL ASSEMBLY (room-prefab-only)
        // ==========================================================

        private static void BuildProceduralByRooms(InteriorMap m, Random rng, out int spawnX, out int spawnY)
        {
            int w = m.W;
            int h = m.H;

            int targetRooms = Clamp(10 + (w * h) / 1200, 12, 40);

            RoomPrefab startPrefab = PROC_ROOMS[rng.Next(PROC_ROOMS.Length)];
            int startPW = PrefabWidth(startPrefab.Rows);
            int startPH = startPrefab.Rows.Length;

            int startOx = (w / 2) - (startPW / 2);
            int startOy = 2;

            if (!CanPlacePrefab(m, startPrefab.Rows, startOx, startOy))
            {
                startPrefab = new RoomPrefab("ROOM_7_NS", ROOM_7_NS);
                startPW = PrefabWidth(startPrefab.Rows);
                startPH = startPrefab.Rows.Length;
                startOx = (w / 2) - (startPW / 2);
                startOy = 2;
            }

            StampPrefab(m, startPrefab.Rows, startOx, startOy);

            PickSpawnInsideStampedArea(m, startOx, startOy, startPW, startPH, out spawnX, out spawnY);

            var openPorts = new List<WorldPort>(64);
            CollectOpenPortsFromPrefab(m, startPrefab.Rows, startOx, startOy, openPorts);

            int placedRooms = 1;

            int safety = 0;
            while (placedRooms < targetRooms && openPorts.Count > 0 && safety++ < 4000)
            {
                int portIndex = rng.Next(openPorts.Count);
                WorldPort anchor = openPorts[portIndex];

                if (!m.InBounds(anchor.OutsideX, anchor.OutsideY) || m.Get(anchor.OutsideX, anchor.OutsideY) != InteriorMap.VOID)
                {
                    openPorts.RemoveAt(portIndex);
                    continue;
                }

                bool placed = TryAttachRoomAtPort(m, rng, anchor, openPorts);

                openPorts.RemoveAt(portIndex);

                if (placed)
                {
                    placedRooms++;
                }
            }

            SealDanglingOpenings(m);
        }

        private static bool TryAttachRoomAtPort(InteriorMap m, Random rng, WorldPort anchor, List<WorldPort> openPorts)
        {
            Dir need = Opposite(anchor.Dir);

            const int attempts = 30;
            for (int a = 0; a < attempts; a++)
            {
                RoomPrefab cand = PROC_ROOMS[rng.Next(PROC_ROOMS.Length)];
                var candPorts = GetPrefabPorts(cand.Rows);

                int foundIndex = -1;
                int tries = 0;
                while (tries++ < 20)
                {
                    int i = rng.Next(candPorts.Count);
                    if (candPorts[i].Dir == need)
                    {
                        foundIndex = i;
                        break;
                    }
                }

                if (foundIndex < 0)
                    continue;

                PrefabPort cp = candPorts[foundIndex];

                int cOutsideLocalX = cp.InsideX + Dx(cp.Dir);
                int cOutsideLocalY = cp.InsideY + Dy(cp.Dir);

                int ox = anchor.OutsideX - cOutsideLocalX;
                int oy = anchor.OutsideY - cOutsideLocalY;

                if (!CanPlacePrefab(m, cand.Rows, ox, oy))
                    continue;

                StampPrefab(m, cand.Rows, ox, oy);

                m.Set(anchor.InsideX, anchor.InsideY, InteriorMap.DOOR);

                int newInsideWorldX = ox + cp.InsideX;
                int newInsideWorldY = oy + cp.InsideY;
                m.Set(newInsideWorldX, newInsideWorldY, InteriorMap.DOOR);

                m.Set(anchor.OutsideX, anchor.OutsideY, InteriorMap.FLOOR);

                CollectOpenPortsFromPrefab(m, cand.Rows, ox, oy, openPorts);

                return true;
            }

            return false;
        }

        private static void SealDanglingOpenings(InteriorMap m)
        {
            for (int y = 1; y < m.H - 1; y++)
            {
                for (int x = 1; x < m.W - 1; x++)
                {
                    char t = m.Get(x, y);
                    if (t != InteriorMap.DOOR) continue;

                    if (m.Get(x + 1, y) == InteriorMap.VOID ||
                        m.Get(x - 1, y) == InteriorMap.VOID ||
                        m.Get(x, y + 1) == InteriorMap.VOID ||
                        m.Get(x, y - 1) == InteriorMap.VOID)
                    {
                        m.Set(x, y, InteriorMap.WALL);
                    }
                }
            }
        }

        // ==========================================================
        // BLUEPRINT STAMPING (pre-designed ships)
        // ==========================================================

        private static string[] PickRandomBlueprint(Random rng)
        {
            int idx = rng.Next(BLUEPRINTS.Count);
            int i = 0;
            foreach (var kv in BLUEPRINTS)
            {
                if (i++ == idx) return kv.Value;
            }
            foreach (var kv in BLUEPRINTS) return kv.Value;
            return BLUEPRINT_DIAMOND;
        }

        private static void StampBlueprintCentered(InteriorMap m, string[] blueprint, out int spawnX, out int spawnY)
        {
            int pw = PrefabWidth(blueprint);
            int ph = blueprint.Length;

            int ox = (m.W / 2) - (pw / 2);
            int oy = (m.H / 2) - (ph / 2);

            StampPrefab(m, blueprint, ox, oy);

            int cx = m.W / 2;
            int cy = m.H / 2;

            if (TryFindNearestWalkable(m, cx, cy, out spawnX, out spawnY))
                return;

            for (int y = 1; y < m.H - 1; y++)
            {
                for (int x = 1; x < m.W - 1; x++)
                {
                    if (m.IsWalkable(x, y))
                    {
                        spawnX = x;
                        spawnY = y;
                        return;
                    }
                }
            }

            spawnX = cx;
            spawnY = cy;
        }

        private static bool TryFindNearestWalkable(InteriorMap m, int sx, int sy, out int xOut, out int yOut)
        {
            int maxR = Math.Max(m.W, m.H);
            for (int r = 0; r <= maxR; r++)
            {
                int minX = sx - r;
                int maxX = sx + r;
                int minY = sy - r;
                int maxY = sy + r;

                for (int x = minX; x <= maxX; x++)
                {
                    if (m.IsWalkable(x, minY)) { xOut = x; yOut = minY; return true; }
                    if (m.IsWalkable(x, maxY)) { xOut = x; yOut = maxY; return true; }
                }

                for (int y = minY; y <= maxY; y++)
                {
                    if (m.IsWalkable(minX, y)) { xOut = minX; yOut = y; return true; }
                    if (m.IsWalkable(maxX, y)) { xOut = maxX; yOut = y; return true; }
                }
            }

            xOut = sx;
            yOut = sy;
            return false;
        }

        // ==========================================================
        // PREFAB PORTS (openings)
        // ==========================================================

        private enum Dir { N, E, S, W }

        private static int Dx(Dir d) => d == Dir.E ? 1 : d == Dir.W ? -1 : 0;
        private static int Dy(Dir d) => d == Dir.S ? 1 : d == Dir.N ? -1 : 0;

        private static Dir Opposite(Dir d)
        {
            return d switch
            {
                Dir.N => Dir.S,
                Dir.S => Dir.N,
                Dir.E => Dir.W,
                Dir.W => Dir.E,
                _ => Dir.N
            };
        }

        private readonly struct PrefabPort
        {
            public readonly int InsideX;
            public readonly int InsideY;
            public readonly Dir Dir;

            public PrefabPort(int insideX, int insideY, Dir dir)
            {
                InsideX = insideX;
                InsideY = insideY;
                Dir = dir;
            }
        }

        private readonly struct WorldPort
        {
            public readonly int InsideX;
            public readonly int InsideY;
            public readonly int OutsideX;
            public readonly int OutsideY;
            public readonly Dir Dir;

            public WorldPort(int insideX, int insideY, int outsideX, int outsideY, Dir dir)
            {
                InsideX = insideX;
                InsideY = insideY;
                OutsideX = outsideX;
                OutsideY = outsideY;
                Dir = dir;
            }
        }

        private sealed class RoomPrefab
        {
            public readonly string Name;
            public readonly string[] Rows;

            public RoomPrefab(string name, string[] rows)
            {
                Name = name ?? "Room";
                Rows = rows ?? Array.Empty<string>();
            }
        }

        private static List<PrefabPort> GetPrefabPorts(string[] prefab)
        {
            int w = PrefabWidth(prefab);
            int h = prefab.Length;

            var ports = new List<PrefabPort>(8);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    char c = GetPrefabChar(prefab, x, y);
                    if (!IsFloorishPrefabChar(c)) continue;

                    bool onLeft = (x == 0);
                    bool onRight = (x == w - 1);
                    bool onTop = (y == 0);
                    bool onBottom = (y == h - 1);

                    if (!(onLeft || onRight || onTop || onBottom))
                        continue;

                    if ((onLeft || onRight) && (onTop || onBottom))
                        continue;

                    if (onTop) ports.Add(new PrefabPort(x, y, Dir.N));
                    else if (onBottom) ports.Add(new PrefabPort(x, y, Dir.S));
                    else if (onLeft) ports.Add(new PrefabPort(x, y, Dir.W));
                    else if (onRight) ports.Add(new PrefabPort(x, y, Dir.E));
                }
            }

            return ports;
        }

        private static void CollectOpenPortsFromPrefab(InteriorMap m, string[] prefab, int ox, int oy, List<WorldPort> openPorts)
        {
            var ports = GetPrefabPorts(prefab);
            for (int i = 0; i < ports.Count; i++)
            {
                PrefabPort p = ports[i];
                int insideX = ox + p.InsideX;
                int insideY = oy + p.InsideY;

                int outsideX = insideX + Dx(p.Dir);
                int outsideY = insideY + Dy(p.Dir);

                if (m.InBounds(outsideX, outsideY) && m.Get(outsideX, outsideY) == InteriorMap.VOID)
                {
                    openPorts.Add(new WorldPort(insideX, insideY, outsideX, outsideY, p.Dir));
                }
            }
        }

        // ==========================================================
        // PREFAB STAMPING + PLACEMENT RULES
        // ==========================================================

        private static bool CanPlacePrefab(InteriorMap m, string[] prefab, int ox, int oy)
        {
            int w = PrefabWidth(prefab);
            int h = prefab.Length;

            for (int py = 0; py < h; py++)
            {
                for (int px = 0; px < w; px++)
                {
                    char src = GetPrefabChar(prefab, px, py);
                    if (src == ' ') continue;

                    int x = ox + px;
                    int y = oy + py;

                    if (!m.InBounds(x, y)) return false;

                    if (m.Get(x, y) != InteriorMap.VOID)
                        return false;
                }
            }

            return true;
        }

        private static void StampPrefab(InteriorMap m, string[] prefab, int ox, int oy)
        {
            int w = PrefabWidth(prefab);
            int h = prefab.Length;

            for (int py = 0; py < h; py++)
            {
                for (int px = 0; px < w; px++)
                {
                    char src = GetPrefabChar(prefab, px, py);
                    if (src == ' ') continue;

                    int x = ox + px;
                    int y = oy + py;
                    if (!m.InBounds(x, y)) continue;

                    char tile = src switch
                    {
                        '█' => InteriorMap.WALL,
                        '░' => InteriorMap.WINDOW,
                        'D' => InteriorMap.DOOR,
                        '#' => InteriorMap.FLOOR,
                        '.' => InteriorMap.FLOOR,
                        _ => src
                    };

                    char cur = m.Get(x, y);

                    if (tile == InteriorMap.FLOOR)
                    {
                        if (cur == InteriorMap.VOID) m.Set(x, y, InteriorMap.FLOOR);
                    }
                    else if (tile == InteriorMap.WALL || tile == InteriorMap.WINDOW)
                    {
                        if (cur == InteriorMap.VOID) m.Set(x, y, tile);
                    }
                    else if (tile == InteriorMap.DOOR)
                    {
                        if (cur == InteriorMap.VOID || cur == InteriorMap.WALL || cur == InteriorMap.WINDOW)
                            m.Set(x, y, InteriorMap.DOOR);
                    }
                    else
                    {
                        if (cur == InteriorMap.FLOOR) m.Set(x, y, tile);
                    }
                }
            }
        }

        private static char GetPrefabChar(string[] prefab, int x, int y)
        {
            if (y < 0 || y >= prefab.Length) return ' ';
            string row = prefab[y] ?? string.Empty;
            if (x < 0 || x >= row.Length) return ' ';
            return row[x];
        }

        private static int PrefabWidth(string[] prefab)
        {
            int w = 0;
            for (int i = 0; i < prefab.Length; i++)
                w = Math.Max(w, prefab[i]?.Length ?? 0);
            return w;
        }

        private static bool IsFloorishPrefabChar(char c)
        {
            return c == '#' || c == '.' || c == 'D';
        }

        private static void PickSpawnInsideStampedArea(InteriorMap m, int ox, int oy, int pw, int ph, out int sx, out int sy)
        {
            int cx = ox + pw / 2;
            int cy = oy + ph / 2;
            if (m.IsWalkable(cx, cy))
            {
                sx = cx;
                sy = cy;
                return;
            }

            int minX = ox;
            int maxX = ox + pw - 1;
            int minY = oy;
            int maxY = oy + ph - 1;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (m.IsWalkable(x, y))
                    {
                        sx = x;
                        sy = y;
                        return;
                    }
                }
            }

            for (int y = 1; y < m.H - 1; y++)
            {
                for (int x = 1; x < m.W - 1; x++)
                {
                    if (m.IsWalkable(x, y))
                    {
                        sx = x;
                        sy = y;
                        return;
                    }
                }
            }

            sx = m.W / 2;
            sy = m.H / 2;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
