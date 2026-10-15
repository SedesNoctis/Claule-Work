using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LivingCities
{
    // =====================================================================================
    // THE FIGURES. Every complex is a "circle" (a 16-sided polygon) with a gallery inside its outer wall;
    // what lies within the gallery is the figure:
    //   SmallRing  - an outpost: a hall in the middle and four rooms around it, no gallery;
    //   RingInRing - a ring of rooms between the gallery and a great round hall;
    //   NestChain  - a chain of three octagonal nests (the middle one smaller) in a walled yard;
    //   Spire      - a citadel in the middle (a sanctum within a ring of halls), a crown of octagonal nests
    //                around it, the yard between them cut into wards.
    // A city is two or three figures side by side, their galleries joined by short open passages.
    // =====================================================================================
    public static class CityFigures
    {
        public const int Margin = 3;      // cleared ground round the outer walls
        public const int Gallery = 4;     // outer wall to gallery wall
        public const int Gap = 5;         // between the outer walls of two figures

        private struct Fig { public FigureKind kind; public float R; public Vector2 c; }

        public static CityPlan Compose(CitySize size, List<FigureKind> allowed, int maxExtent)
        {
            var bigKinds = allowed.Where(k => k != FigureKind.SmallRing).ToList();
            if (bigKinds.Count == 0) bigKinds.Add(FigureKind.RingInRing);
            var figs = new List<Fig>();
            switch (size)
            {
                case CitySize.Outpost:
                    figs.Add(new Fig { kind = FigureKind.SmallRing, R = Rand.Range(11, 16) });
                    break;
                case CitySize.Nest:
                    figs.Add(new Fig { kind = bigKinds.RandomElement(), R = Rand.Range(26, 35) });
                    break;
                default:
                    int n = Rand.Chance(0.5f) ? 2 : 3;
                    for (int i = 0; i < n; i++) figs.Add(new Fig { kind = bigKinds.RandomElement(), R = Rand.Range(22, 29) });
                    break;
            }

            // shrink until it fits the map
            for (int tries = 0; tries < 12; tries++)
            {
                Place(figs);
                float minX = figs.Min(f => f.c.x - f.R), maxX = figs.Max(f => f.c.x + f.R);
                float minZ = figs.Min(f => f.c.y - f.R), maxZ = figs.Max(f => f.c.y + f.R);
                float extent = Mathf.Max(maxX - minX, maxZ - minZ) * 1.03f + 2 * (Margin + 2);
                if (extent <= maxExtent) break;
                for (int i = 0; i < figs.Count; i++) { var f = figs[i]; f.R = Mathf.Max(11f, f.R * 0.9f); figs[i] = f; }
            }
            {
                float minX = figs.Min(f => f.c.x - f.R * 1.03f), maxX = figs.Max(f => f.c.x + f.R * 1.03f);
                float minZ = figs.Min(f => f.c.y - f.R * 1.03f), maxZ = figs.Max(f => f.c.y + f.R * 1.03f);
                int pad = Margin + 3;
                int planSize = Mathf.CeilToInt(Mathf.Max(maxX - minX, maxZ - minZ)) + 2 * pad;
                Vector2 shift = new Vector2(pad - minX + (planSize - 2 * pad - (maxX - minX)) / 2f,
                                            pad - minZ + (planSize - 2 * pad - (maxZ - minZ)) / 2f);
                var plan = new CityPlan(planSize);
                for (int i = 0; i < figs.Count; i++) { var f = figs[i]; f.c += shift; f.R = Mathf.Round(f.R); figs[i] = f; }

                foreach (Fig f in figs) plan.Disk(V(f.c), f.R, 16, Margin);
                foreach (Fig f in figs) Draw(plan, f);
                for (int i = 1; i < figs.Count; i++)
                {
                    // each figure joins the one before it (and the third the first as well)
                    Passage(plan, figs[i - 1], figs[i], figs);
                    if (i == 2) Passage(plan, figs[0], figs[2], figs);
                }
                return plan;
            }
        }

        private static IntVec2 V(Vector2 v) => new IntVec2(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y));

        /// <summary>Two side by side; a third sits in the notch between them, touching both at the gap.</summary>
        private static void Place(List<Fig> figs)
        {
            if (figs.Count == 0) return;
            var a = figs[0]; a.c = Vector2.zero; figs[0] = a;
            if (figs.Count < 2) return;
            var b = figs[1]; b.c = new Vector2(a.R + b.R + Gap, 0f); figs[1] = b;
            if (figs.Count < 3) return;
            var c = figs[2];
            float d = b.c.x, ra = a.R + c.R + Gap, rb = b.R + c.R + Gap;
            float x = (d * d + ra * ra - rb * rb) / (2f * d);
            float z = Mathf.Sqrt(Mathf.Max(0f, ra * ra - x * x));
            c.c = new Vector2(x, Rand.Bool ? z : -z);
            figs[2] = c;
        }

        private static void Draw(CityPlan p, Fig f)
        {
            IntVec2 c = V(f.c);
            float R = f.R;
            p.Rim(c, R, 16);
            if (f.kind == FigureKind.SmallRing)
            {
                float hall = Mathf.Round(R * 0.45f);
                p.Rim(c, hall, 8);
                float off = Rand.Bool ? 45f : 0f;
                for (int i = 0; i < 4; i++) p.Spoke(c, (off + 90f * i) * Mathf.Deg2Rad, hall + 1, R);
                p.Seed(c, RoomPlace.Center);
                return;
            }

            float g = R - Gallery;
            p.Rim(c, g, 16);
            p.Seed(new IntVec2(c.x + Mathf.RoundToInt(R - 2), c.z), RoomPlace.Hub);
            p.Seed(new IntVec2(c.x - Mathf.RoundToInt(R - 2), c.z), RoomPlace.Hub);

            switch (f.kind)
            {
                case FigureKind.RingInRing:
                {
                    float hall = Mathf.Round(R * 0.42f);
                    p.Rim(c, hall, 16);
                    int k = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * (g + hall) / 2f / 11f), 6, 12);
                    float off = Rand.Range(0f, 360f / k);
                    for (int i = 0; i < k; i++) p.Spoke(c, (off + 360f * i / k) * Mathf.Deg2Rad, hall + 1, g);
                    p.Seed(c, RoomPlace.Center);
                    break;
                }
                case FigureKind.NestChain:
                {
                    float am = Mathf.Round(R * 0.17f), side = Mathf.Round(R * 0.23f);
                    float sx = am + side;   // the nests share one wall
                    IntVec2 left = new IntVec2(c.x - Mathf.RoundToInt(sx), c.z), right = new IntVec2(c.x + Mathf.RoundToInt(sx), c.z);
                    p.Rim(c, am, 8);
                    p.Rim(left, side, 8);
                    p.Rim(right, side, 8);
                    // the yard round the chain, cut into four wards
                    p.Spoke(c, 90f * Mathf.Deg2Rad, am + 1, g);
                    p.Spoke(c, 270f * Mathf.Deg2Rad, am + 1, g);
                    p.Spoke(c, 0f, sx + side + 1, g);
                    p.Spoke(c, 180f * Mathf.Deg2Rad, sx + side + 1, g);
                    p.Seed(c, RoomPlace.Center);
                    p.Seed(left, RoomPlace.Nest);
                    p.Seed(right, RoomPlace.Nest);
                    break;
                }
                case FigureKind.Spire:
                {
                    float ac = Mathf.Round(R * 0.36f), sanctum = Mathf.Round(ac * 0.5f);
                    p.Rim(c, ac, 16);
                    p.Rim(c, sanctum, 8);
                    for (int i = 0; i < 4; i++) p.Spoke(c, (45f + 90f * i) * Mathf.Deg2Rad, sanctum + 1, ac);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (45f + 90f * i + 45f) * Mathf.Deg2Rad;
                        float rr = (sanctum + ac) / 2f;
                        p.Seed(new IntVec2(c.x + Mathf.RoundToInt(Mathf.Cos(a) * rr), c.z + Mathf.RoundToInt(Mathf.Sin(a) * rr)), RoomPlace.Center);
                    }
                    int k = R >= 30 ? 6 : 5;
                    float an = Mathf.Round(Mathf.Min(R * 0.15f, (g - ac) / 2f - 1.5f));
                    float ring = (ac + g) / 2f, off = Rand.Range(0f, 360f / k);
                    for (int i = 0; i < k; i++)
                    {
                        float a = (off + 360f * i / k) * Mathf.Deg2Rad;
                        var nc = new IntVec2(c.x + Mathf.RoundToInt(Mathf.Cos(a) * ring), c.z + Mathf.RoundToInt(Mathf.Sin(a) * ring));
                        p.Rim(nc, an, 8);
                        p.Seed(nc, RoomPlace.Nest);
                        float mid = a + Mathf.PI / k;
                        p.Spoke(c, mid, ac + 1, g);
                        p.Seed(new IntVec2(c.x + Mathf.RoundToInt(Mathf.Cos(mid + 0.2f) * ring), c.z + Mathf.RoundToInt(Mathf.Sin(mid + 0.2f) * ring)), RoomPlace.Yard);
                    }
                    p.Seed(c, RoomPlace.Center);
                    break;
                }
            }
        }

        /// <summary>An open passage between two galleries: three cells wide, walled on both sides where it
        /// crosses open ground, cutting through both outer walls.</summary>
        private static void Passage(CityPlan p, Fig a, Fig b, List<Fig> all)
        {
            Vector2 d = b.c - a.c;
            float len = d.magnitude;
            if (len < 1f) return;
            Vector2 dir = d / len, perp = new Vector2(-dir.y, dir.x);
            for (int x = 0; x < p.size; x++)
                for (int z = 0; z < p.size; z++)
                {
                    Vector2 v = new Vector2(x, z) - a.c;
                    float t = Vector2.Dot(v, dir), off = Mathf.Abs(Vector2.Dot(v, perp));
                    if (t < 0f || t > len || off > 2.5f) continue;
                    // never into any figure's inner part
                    bool inner = false;
                    foreach (Fig f in all)
                        if (CityPlan.PolyR(x - Mathf.Round(f.c.x), z - Mathf.Round(f.c.y), 16) <= f.R - Gallery) { inner = true; break; }
                    if (inner) continue;
                    PlanCell cur = p.At(x, z);
                    if (off <= 1.5f) p.Set(x, z, PlanCell.Floor);
                    else if (cur == PlanCell.None || cur == PlanCell.Ground) p.Set(x, z, PlanCell.Wall);
                }
        }
    }
}
