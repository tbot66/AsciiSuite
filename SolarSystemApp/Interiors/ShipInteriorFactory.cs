using System;
using System.Collections.Generic;

namespace SolarSystemApp.Interiors
{
    internal static class ShipInteriorFactory
    {
        // Ship models are full ASCII layouts. Blank characters are walkable; █ are walls.
        private static readonly string[] SHIP_MODEL_ATLAS =
        {
            @"                             ██",
            @"                             ██",
            @"                             ██",
            @"                           ██████",
            @"                         ██      ██",
            @"                        ███      ███",
            @"                        ██        ██",
            @"                      ██            ██ ",
            @"                     ███            ███",
            @"                     ██              ██",
            @"                   ██                  ██ ",
            @"                  ███__________________███",
            @"                  ██      ¿             ██",
            @"                ██\    ¡ __________  ┌┌┌ /██",
            @"                ██ \   /            \   / ██",
            @"               ███  \ |    ______    | /  ███",
            @"               ███   |    ┌| nav|┐    |   ███",
            @"               ███   |   {└-╔--╗-┘}   |   ███",
            @"               ███   |    ]_|  |_[    |   ███",
            @"             ██\  \  |                |  /  /██",
            @"             ██ \  \ |                | /  / ██",
            @"             ██  \__\|      _▓▓_      |/__/  ██",
            @"            ███  |           ╦╦           |║ ███",
            @"            ███  |           ··           |║ ███",
            @"            ███ °|                        |║ ███",
            @"        ███████ °|                        |║ ███████",
            @"      █████████ °|                        |║ █████████",
            @"      ████||███ °|                        |║ ███||████",
            @" ██(((══════███ °|                        |║ ███══════)))██",
            @"███(((=═=═=-███ °|                        |  ███-=═=═=)))███",
            @"███(((══════=██ °|                        |  ██=══════)))███",
            @"{█{]]]]]   \███ ║|                        |  ███/   [[[[[}█}",
            @"{█{]   ]]   ███ ║|                        |  ███   [[   [}█}",
            @"{█{]]]]]    ███ ║|                        |  ███    [[[[[}█}",
            @"║║█║ ║      ███ ║|                        |  ███      ║ ║█║║",
            @"║║█║ ║      ███ ║|                        |  ███      ║ ║█║║",
            @" ║█║ ║      ███ ║|                        |  ███      ║ ║█║",
            @" ║█║ ║      ███ ║|                        |° ███      ║ ║█║",
            @" ║█║ ║      ███  |                        |° ███      ║ ║█║",
            @" ║█║ ║      ███  |                        |° ███      ║ ║█║",
            @" /  \║      ███  |                        |° ███      ║/  \",
            @" \__/       ███  |                        |° ███       \__/",
            @"            ███  |                        |° ███ ",
            @"            ███  |                        |° ███",
            @"            ███  |                        |  ███",
            @"            ███  |________________________|  ███",
            @"            ███  /  /__________________\  \  ███",
            @"            ███ /  / __________________ \  \ ███",
            @"             ██/  / ____________________ \  \██ ",
            @"              ████  ____________________  ████",
            @"               ███ /     / ______ \     \ ███",
            @"               ███/     / ________ \     \███",
            @"                  ██████  ________  ██████",
            @"                   █████ /  ____  \ █████",
            @"                    ████/  /____\  \████",
            @"                        ███═|__|═███",
            @"                        ███═|__|═███",
            @"                        ███═|__|═███",
            @"                      ██\___|__|┌__/██ ",
            @"                     ███|▄▄       |/  █",
            @"                     ███|▒▒     └ ■╗/|█",
            @"                     ███|       └ ■╝\|█",
            @"                     ███|_________|\  █",
            @"                      ██/ ,, =-=   \██ ",
            @"                        ████████████",
            @"                           ██████",
            @"                            ████"
        };

        private static readonly Dictionary<string, string[]> SHIP_MODELS =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Atlas", SHIP_MODEL_ATLAS },
            };

        // =========================
        // PUBLIC ENTRY POINT (KEEP)
        // =========================
        public static InteriorSession Create(int seed, string shipName, int w = 120, int h = 50)
        {
            var rng = new Random(seed ^ (shipName?.GetHashCode() ?? 0));
            var m = new InteriorMap(w, h, InteriorMap.VOID);

            if (!string.IsNullOrWhiteSpace(shipName) && SHIP_MODELS.TryGetValue(shipName.Trim(), out var exactModel))
            {
                StampShipCentered(m, exactModel, out int sx, out int sy);
                return new InteriorSession(shipName, m, sx, sy);
            }

            var model = PickRandomModel(rng) ?? SHIP_MODEL_ATLAS;
            StampShipCentered(m, model, out int spawnX, out int spawnY);

            return new InteriorSession(shipName, m, spawnX, spawnY);
        }

        private static string[] PickRandomModel(Random rng)
        {
            if (SHIP_MODELS.Count == 0) return null;

            int idx = rng.Next(SHIP_MODELS.Count);
            int i = 0;
            foreach (var kv in SHIP_MODELS)
            {
                if (i++ == idx) return kv.Value;
            }

            foreach (var kv in SHIP_MODELS) return kv.Value;
            return SHIP_MODEL_ATLAS;
        }

        private static void StampShipCentered(InteriorMap m, string[] model, out int spawnX, out int spawnY)
        {
            int mw = ModelWidth(model);
            int mh = model.Length;

            int ox = (m.W / 2) - (mw / 2);
            int oy = (m.H / 2) - (mh / 2);

            StampShip(m, model, ox, oy);
            ClearExteriorSpaces(m);

            int cx = ox + mw / 2;
            int cy = oy + mh / 2;

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

            spawnX = m.W / 2;
            spawnY = m.H / 2;
        }

        private static void StampShip(InteriorMap m, string[] model, int ox, int oy)
        {
            int mw = ModelWidth(model);
            int mh = model.Length;

            for (int y = 0; y < mh; y++)
            {
                for (int x = 0; x < mw; x++)
                {
                    char c = GetModelChar(model, x, y);
                    int wx = ox + x;
                    int wy = oy + y;

                    if (!m.InBounds(wx, wy)) continue;

                    m.Set(wx, wy, c == '\0' ? InteriorMap.FLOOR : c);
                }
            }
        }

        private static void ClearExteriorSpaces(InteriorMap m)
        {
            var queue = new Queue<(int x, int y)>();

            for (int x = 0; x < m.W; x++)
            {
                TryQueueExterior(m, queue, x, 0);
                TryQueueExterior(m, queue, x, m.H - 1);
            }

            for (int y = 0; y < m.H; y++)
            {
                TryQueueExterior(m, queue, 0, y);
                TryQueueExterior(m, queue, m.W - 1, y);
            }

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                if (!m.InBounds(x, y)) continue;
                if (m.Get(x, y) != InteriorMap.FLOOR) continue;

                m.Set(x, y, InteriorMap.VOID);

                TryQueueExterior(m, queue, x + 1, y);
                TryQueueExterior(m, queue, x - 1, y);
                TryQueueExterior(m, queue, x, y + 1);
                TryQueueExterior(m, queue, x, y - 1);
            }
        }

        private static void TryQueueExterior(InteriorMap m, Queue<(int x, int y)> queue, int x, int y)
        {
            if (!m.InBounds(x, y)) return;
            if (m.Get(x, y) != InteriorMap.FLOOR) return;
            queue.Enqueue((x, y));
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

        private static int ModelWidth(string[] model)
        {
            int w = 0;
            for (int i = 0; i < model.Length; i++)
                w = Math.Max(w, model[i]?.Length ?? 0);
            return w;
        }

        private static char GetModelChar(string[] model, int x, int y)
        {
            if (y < 0 || y >= model.Length) return InteriorMap.FLOOR;
            string row = model[y] ?? string.Empty;
            if (x < 0 || x >= row.Length) return InteriorMap.FLOOR;
            return row[x];
        }
    }
}
