using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LivingCities
{
    public enum PlanCell : byte { None, Ground, Floor, Wall, HeavyDoor, Door }

    public class PlanRoom
    {
        public int id;
        public List<IntVec2> cells = new List<IntVec2>();
        public RoomPlace place = RoomPlace.Band;
        public ZoneDef zone;
        public bool connected;
        public int doors;
        public int Area => cells.Count;

        public IntVec2 Centroid()
        {
            long x = 0, z = 0;
            foreach (IntVec2 c in cells) { x += c.x; z += c.z; }
            var mid = new IntVec2((int)(x / Math.Max(1, cells.Count)), (int)(z / Math.Max(1, cells.Count)));
            return cells.OrderBy(c => (c.x - mid.x) * (c.x - mid.x) + (c.z - mid.z) * (c.z - mid.z)).First();
        }
    }

    // =====================================================================================
    // THE PLAN: a grid of what each cell will be, drawn before anything touches the map. Figures are
    // drawn as polygons (a 16-sided "circle" or an octagon, flat sides facing the four directions -
    // straight runs and stair-stepped diagonals, never a diagonal wall), walls on their rims, spokes
    // between them. Then the floor falls apart into rooms on its own (4-connected), each room is told
    // where it lies by the seed points the figure left, and doors are cut: heavy ones from the outside
    // into the galleries, plain ones along a tree from the galleries to every room.
    // =====================================================================================
    public class CityPlan
    {
        public readonly int size;
        public readonly PlanCell[] cells;
        public readonly bool[] well;
        public readonly int[] room;
        public readonly List<PlanRoom> rooms = new List<PlanRoom>();
        /// <summary>Points the figures left to say what the room around them is.</summary>
        public readonly List<KeyValuePair<IntVec2, RoomPlace>> seeds = new List<KeyValuePair<IntVec2, RoomPlace>>();

        public CityPlan(int size)
        {
            this.size = size;
            cells = new PlanCell[size * size];
            well = new bool[size * size];
            room = new int[size * size];
        }

        public bool In(int x, int z) => x >= 0 && z >= 0 && x < size && z < size;
        public int Idx(int x, int z) => z * size + x;
        public PlanCell At(int x, int z) => In(x, z) ? cells[Idx(x, z)] : PlanCell.None;
        public void Set(int x, int z, PlanCell c) { if (In(x, z)) cells[Idx(x, z)] = c; }
        public static bool Open(PlanCell c) => c == PlanCell.Floor;
        public static bool Outside(PlanCell c) => c == PlanCell.None || c == PlanCell.Ground;

        // ------------------------------------------------------------------ shapes

        /// <summary>How far a point lies from a regular polygon's centre, measured as the polygon measures
        /// it: inside while this is at most the apothem. Sides face 0°, 360/n°, ... so an octagon and a
        /// 16-gon both have flat sides to the four directions.</summary>
        public static float PolyR(float dx, float dz, int n)
        {
            float best = float.MinValue;
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                float d = dx * Mathf.Cos(a) + dz * Mathf.Sin(a);
                if (d > best) best = d;
            }
            return best;
        }

        public static bool InPoly(int x, int z, IntVec2 c, float a, int n) => PolyR(x - c.x, z - c.z, n) <= a;

        /// <summary>Lays floor over the whole polygon and ground around it (a margin of cleared land).</summary>
        public void Disk(IntVec2 c, float a, int n, int margin)
        {
            int r = Mathf.CeilToInt(a / Mathf.Cos(Mathf.PI / n)) + margin + 1;
            for (int x = c.x - r; x <= c.x + r; x++)
                for (int z = c.z - r; z <= c.z + r; z++)
                {
                    if (!In(x, z)) continue;
                    float p = PolyR(x - c.x, z - c.z, n);
                    if (p <= a) Set(x, z, PlanCell.Floor);
                    else if (p <= a + margin && At(x, z) == PlanCell.None) Set(x, z, PlanCell.Ground);
                }
        }

        /// <summary>A wall on the rim of the polygon: every inside cell with a 4-neighbour outside it.</summary>
        public void Rim(IntVec2 c, float a, int n)
        {
            int r = Mathf.CeilToInt(a / Mathf.Cos(Mathf.PI / n)) + 1;
            for (int x = c.x - r; x <= c.x + r; x++)
                for (int z = c.z - r; z <= c.z + r; z++)
                {
                    if (!In(x, z) || !InPoly(x, z, c, a, n)) continue;
                    if (!InPoly(x + 1, z, c, a, n) || !InPoly(x - 1, z, c, a, n) || !InPoly(x, z + 1, c, a, n) || !InPoly(x, z - 1, c, a, n))
                        Set(x, z, PlanCell.Wall);
                }
        }

        /// <summary>A straight wall from radius r1 to r2 at an angle - stepped, never a bare diagonal, so it
        /// closes rooms for pawns and air alike. Only laid over floor.</summary>
        public void Spoke(IntVec2 c, float angle, float r1, float r2)
        {
            Vector2 d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Line(new IntVec2(c.x + Mathf.RoundToInt(d.x * r1), c.z + Mathf.RoundToInt(d.y * r1)),
                 new IntVec2(c.x + Mathf.RoundToInt(d.x * r2), c.z + Mathf.RoundToInt(d.y * r2)), PlanCell.Wall, true);
        }

        public void Line(IntVec2 a, IntVec2 b, PlanCell kind, bool onlyOverFloor)
        {
            int x = a.x, z = a.z, dx = Math.Abs(b.x - a.x), dz = Math.Abs(b.z - a.z);
            int sx = a.x < b.x ? 1 : -1, sz = a.z < b.z ? 1 : -1, err = dx - dz;
            while (true)
            {
                if (!onlyOverFloor || At(x, z) == PlanCell.Floor) Set(x, z, kind);
                if (x == b.x && z == b.z) break;
                int e2 = 2 * err;
                bool mx = e2 > -dz, mz = e2 < dx;
                if (mx && mz)
                {
                    // a diagonal step: fill the corner, so the line is 4-connected
                    if (!onlyOverFloor || At(x + sx, z) == PlanCell.Floor) Set(x + sx, z, kind);
                }
                if (mx) { err -= dz; x += sx; }
                if (mz) { err += dx; z += sz; }
            }
        }

        public static int Dist2(IntVec2 a, IntVec2 b) => (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);

        public void Seed(IntVec2 c, RoomPlace place) => seeds.Add(new KeyValuePair<IntVec2, RoomPlace>(c, place));

        // ------------------------------------------------------------------ rooms and doors

        private static readonly IntVec2[] Dirs = { new IntVec2(1, 0), new IntVec2(-1, 0), new IntVec2(0, 1), new IntVec2(0, -1) };

        public void FindRooms()
        {
            rooms.Clear();
            for (int i = 0; i < room.Length; i++) room[i] = -1;
            var queue = new Queue<IntVec2>();
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    if (!Open(At(x, z)) || room[Idx(x, z)] >= 0) continue;
                    var r = new PlanRoom { id = rooms.Count };
                    rooms.Add(r);
                    room[Idx(x, z)] = r.id;
                    queue.Enqueue(new IntVec2(x, z));
                    while (queue.Count > 0)
                    {
                        IntVec2 c = queue.Dequeue();
                        r.cells.Add(c);
                        foreach (IntVec2 d in Dirs)
                        {
                            int nx = c.x + d.x, nz = c.z + d.z;
                            if (!In(nx, nz) || !Open(At(nx, nz)) || room[Idx(nx, nz)] >= 0) continue;
                            room[Idx(nx, nz)] = r.id;
                            queue.Enqueue(new IntVec2(nx, nz));
                        }
                    }
                }
            // slivers left between walls are walled in rather than kept as rooms
            foreach (PlanRoom r in rooms.Where(r => r.Area < 4).ToList())
                foreach (IntVec2 c in r.cells) Set(c.x, c.z, PlanCell.Wall);
            if (rooms.Any(r => r.Area < 4)) { FindRooms(); return; }

            foreach (var s in seeds)
            {
                if (!In(s.Key.x, s.Key.z)) continue;
                int id = room[Idx(s.Key.x, s.Key.z)];
                if (id < 0) continue;
                // a hub seed wins over everything; otherwise the first seed names the room
                if (s.Value == RoomPlace.Hub || rooms[id].place == RoomPlace.Band) rooms[id].place = s.Value;
            }
        }

        public int RoomAt(int x, int z) => In(x, z) ? room[Idx(x, z)] : -1;

        /// <summary>Wall cells a door may take between two sides: a straight run (walls on the other two
        /// sides), open floor or the outside on either face.</summary>
        private IEnumerable<(IntVec2 cell, int a, int b)> DoorSpots()
        {
            for (int z = 1; z < size - 1; z++)
                for (int x = 1; x < size - 1; x++)
                {
                    if (At(x, z) != PlanCell.Wall) continue;
                    for (int axis = 0; axis < 2; axis++)
                    {
                        int ax = axis == 0 ? 1 : 0, az = 1 - ax;
                        PlanCell s1 = At(x + ax, z + az), s2 = At(x - ax, z - az);
                        PlanCell w1 = At(x + az, z + ax), w2 = At(x - az, z - ax);
                        if (w1 != PlanCell.Wall || w2 != PlanCell.Wall) continue;
                        int a = Open(s1) ? RoomAt(x + ax, z + az) : Outside(s1) ? -2 : -3;
                        int b = Open(s2) ? RoomAt(x - ax, z - az) : Outside(s2) ? -2 : -3;
                        if (a == -3 || b == -3 || a == b) continue;
                        yield return (new IntVec2(x, z), a, b);
                    }
                }
        }

        public void CutDoors(int heavyDoors)
        {
            var spots = DoorSpots().ToList();
            IntVec2 mid = new IntVec2(size / 2, size / 2);

            // heavy doors: from the outside into the galleries (or, with none, into any room on the rim),
            // spread round the compass
            bool hasHub = rooms.Any(r => r.place == RoomPlace.Hub);
            var outer = spots.Where(s => (s.a == -2 || s.b == -2) && (s.a >= 0 || s.b >= 0))
                             .Where(s => !hasHub || rooms[Math.Max(s.a, s.b)].place == RoomPlace.Hub).ToList();
            var placedHeavy = new List<IntVec2>();
            for (int i = 0; i < heavyDoors && outer.Count > 0; i++)
            {
                float want = (i * 360f / heavyDoors + Rand.Range(-20f, 20f)) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(want), Mathf.Sin(want));
                var cands = outer.Where(s => placedHeavy.All(p => Dist2(p, s.cell) > 400))
                                 .OrderByDescending(s => (s.cell.x - mid.x) * dir.x + (s.cell.z - mid.z) * dir.y).Take(1).ToList();
                if (cands.Count == 0) break;
                var pick = cands[0];
                Set(pick.cell.x, pick.cell.z, PlanCell.HeavyDoor);
                placedHeavy.Add(pick.cell);
                int rid = Math.Max(pick.a, pick.b);
                rooms[rid].connected = true;
                rooms[rid].doors++;
            }
            if (!rooms.Any(r => r.connected) && rooms.Count > 0) rooms.MaxBy(r => r.Area).connected = true;

            // plain doors: grow a tree from what is reached to what is not; big halls get a second way in
            var inner = spots.Where(s => s.a >= 0 && s.b >= 0).ToList();
            bool grew = true;
            while (grew)
            {
                grew = false;
                var options = inner.Where(s => rooms[s.a].connected != rooms[s.b].connected).ToList();
                if (options.Count == 0) break;
                // per unreached room, prefer the door nearest the middle of the shared stretch: approximated
                // by taking the spot of each pair at random but favouring hub-side doors
                var byRoom = options.GroupBy(s => rooms[s.a].connected ? s.b : s.a);
                foreach (var g in byRoom)
                {
                    PlanRoom target = rooms[g.Key];
                    if (target.connected) continue;
                    var list = g.ToList();
                    var hubSide = list.Where(s => rooms[s.a].place == RoomPlace.Hub || rooms[s.b].place == RoomPlace.Hub).ToList();
                    var from = hubSide.Count > 0 ? hubSide : list;
                    var pick = from[from.Count / 2];
                    if (!Adjacent(pick.cell, PlanCell.Door) && !Adjacent(pick.cell, PlanCell.HeavyDoor))
                    {
                        Set(pick.cell.x, pick.cell.z, PlanCell.Door);
                        target.connected = true;
                        rooms[pick.a].doors++; rooms[pick.b].doors++;
                        grew = true;
                    }
                    else
                    {
                        foreach (var s in from)
                        {
                            if (Adjacent(s.cell, PlanCell.Door) || Adjacent(s.cell, PlanCell.HeavyDoor)) continue;
                            Set(s.cell.x, s.cell.z, PlanCell.Door);
                            target.connected = true;
                            rooms[s.a].doors++; rooms[s.b].doors++;
                            grew = true;
                            break;
                        }
                    }
                }
            }
            // large rooms with only one door get a second, to another neighbour
            foreach (PlanRoom r in rooms.Where(r => r.Area >= 120 && r.doors < 2 && r.place != RoomPlace.Hub))
            {
                var opts = inner.Where(o => (o.a == r.id || o.b == r.id) && At(o.cell.x, o.cell.z) == PlanCell.Wall
                                          && !Adjacent(o.cell, PlanCell.Door) && !Adjacent(o.cell, PlanCell.HeavyDoor)).ToList();
                if (opts.Count == 0) continue;
                var s = opts.RandomElement();
                Set(s.cell.x, s.cell.z, PlanCell.Door);
                r.doors++;
            }
        }

        private bool Adjacent(IntVec2 c, PlanCell kind)
        {
            for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                    if ((dx != 0 || dz != 0) && At(c.x + dx, c.z + dz) == kind) return true;
            return false;
        }

        // ------------------------------------------------------------------ wells

        /// <summary>A cell is "deep" when nothing but floor lies within this distance.</summary>
        public bool Deep(IntVec2 c, int d)
        {
            for (int dx = -d; dx <= d; dx++)
                for (int dz = -d; dz <= d; dz++)
                    if (!Open(At(c.x + dx, c.z + dz))) return false;
            return true;
        }

        /// <summary>The sky wells: an opening in the roof at the heart of every room big enough (wider in the
        /// big halls), and every so often along the galleries.</summary>
        public void Wells(Func<PlanRoom, bool> wants)
        {
            foreach (PlanRoom r in rooms)
            {
                if (r.place == RoomPlace.Hub)
                {
                    var placed = new List<IntVec2>();
                    foreach (IntVec2 c in r.cells.InRandomOrder())
                    {
                        if (!Deep(c, 1) || placed.Any(p => Dist2(p, c) < 196)) continue;
                        placed.Add(c);
                        well[Idx(c.x, c.z)] = true;
                    }
                    continue;
                }
                if (!wants(r) || r.Area < 16) continue;
                int radius = r.Area >= 150 ? 2 : 1;
                IntVec2 centre = r.Centroid();
                if (!Deep(centre, radius))
                {
                    var alts = r.cells.Where(c => Deep(c, radius)).OrderBy(c => Dist2(c, centre)).Take(1).ToList();
                    if (alts.Count == 0) radius = 0; else centre = alts[0];
                }
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dz = -radius; dz <= radius; dz++)
                        if (Math.Abs(dx) + Math.Abs(dz) <= radius + (radius >= 2 ? 1 : 0) && Open(At(centre.x + dx, centre.z + dz)))
                            well[Idx(centre.x + dx, centre.z + dz)] = true;
            }
        }
    }
}
