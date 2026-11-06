using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Our own wind (the author, Fix169: "without that mod - can we break buildings and make such animations,
    /// not by copying, but making something like it?"). A gust is a field of wind for a short while: out from a point
    /// (a hurricane's blow), round a point (a whirl), or one way (a front). Every sixth of a second it acts on what stands
    /// in it, by the wind's strength there (in m/s, the same scale as that mod) and by the weight of the thing:
    ///   things lying about are torn up and fly, tumbling, along the wind, and fall where it drops them - against a wall
    ///   they strike it; people stagger from 20, are knocked down from 28, thrown from 40 (heavier ones hold longer);
    ///   light buildings and walls take the load and can break, doors are battered, plants bend and the weak snap.
    /// With Atmosphere Overhaul loaded and its wind allowed (the setting), that mod's own wind does all this instead.</summary>
    public class MapComponent_MagicWind : MapComponent
    {
        // the wind with fire, gas, ash, water and lightning: see Carry
        public enum Shape { Out, Round, Front }
        public class Gust { public Shape shape; public Vector3 at, dir; public float radius, strength; public int born, life; public Pawn owner; public bool charged, weather; public Color tint = Color.clear; public int sculpt; }

        private readonly List<Gust> gusts = new List<Gust>();
        private readonly Dictionary<int, int> rest = new Dictionary<int, int>();   // a thing just moved is left alone a moment
        public MapComponent_MagicWind(Map map) : base(map) { }
        public static MapComponent_MagicWind Of(Map m) => m?.GetComponent<MapComponent_MagicWind>();

        /// <summary>charged - a storm's wind (a hurricane is a storm's): it carries lightning (Fix170).</summary>
        /// <summary>How strong a wind magic makes (Fix176), for the spell and for the editor alike. Plain air: 22 m/s by the
        /// caster's power, +30% an Amplify; the stream of the place (its tier) adds a fifth each; at most 120. A hurricane:
        /// 72 (its whirls 56), the stream as well - and it may go past the limit, up to 200: winds that spare no one
        /// but those under a ward of the storm.</summary>
        public const float AirMax = 120f, HurricaneMax = 200f;
        public static float AirStrength(float power, int amplify, Map map) =>
            Mathf.Min(AirMax, 22f * power * (1f + 0.3f * amplify) * (1f + 0.2f * Spells.FlowTierAny(map)) * AtmosphereBridge.Scale);
        public static float HurricaneStrength(float power, Map map, bool whirl = false) =>
            Mathf.Min(HurricaneMax, (whirl ? 56f : 72f) * power * (1f + 0.2f * Spells.FlowTierAny(map)) * AtmosphereBridge.Scale);
        public static string What(float s) => s >= 90f ? "tears everything up, throws everyone far" : s >= 40f ? "throws" : s >= 28f ? "fells" : s >= 20f ? "staggers" : s >= 16f ? "carries light things off" : "a breeze";

        /// <summary>Wards hold the wind off (Fix176): a ward of the storm (water and air - a hurricane's own) - entirely;
        /// one of the air (or of dust) - most of it; any other - a quarter. Holding it costs the ward a little.</summary>
        public static float Held(Pawn p, float s)
        {
            float f = 1f;
            foreach (IWard w in SpellWards.Layers(p).ToList())
            {
                if (w == null || w.Energy <= 0f) continue;
                RuneKind[] k = w.Parts ?? new RuneKind[0];
                float here = k.Contains(RuneKind.Air) && k.Contains(RuneKind.Water) ? 0f : k.Contains(RuneKind.Air) ? 0.4f : 0.75f;
                if (here < f) { f = here; w.Energy = Mathf.Max(0f, w.Energy - s * 0.02f); }
            }
            return f;
        }

        public static Gust Blow(Map map, Shape shape, Vector3 at, float radius, float strength, int life, Pawn owner, Vector3 dir = default, bool charged = false, float max = AirMax)
        {
            var c = Of(map);
            if (c == null || strength <= 0f) return null;
            strength = Mathf.Min(strength, max);
            var g = new Gust { shape = shape, at = at, dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.forward, radius = radius, strength = strength, born = Find.TickManager.TicksGame, life = Mathf.Max(10, life), owner = owner, charged = charged };
            // a wind raised by a shaped spell keeps its shaping (Fix189): Selective Protection - it spares the caster's own,
            // Selective Aid - it touches only them. Unshaped, it takes everyone, its own mage too (rule "Own blast")
            g.sculpt = SpellSculpt.Active ? (SpellSculpt.Allies ? 2 : 1) : 0;
            c.gusts.Add(g);
            return g;
        }

        /// <summary>The wind at a cell from one gust: its direction and its strength.</summary>
        private static Vector3 WindAt(Gust g, IntVec3 c, int now, out float s)
        {
            Vector3 o = c.ToVector3Shifted() - g.at; o.y = 0f;
            float d = o.magnitude;
            s = 0f;
            if (d > g.radius) return Vector3.zero;
            float age = 1f - (now - g.born) / (float)g.life;            // it dies down
            float fall = g.shape == Shape.Round ? Mathf.Clamp01(1.2f - Mathf.Abs(d - g.radius * 0.45f) / g.radius) : 1f - 0.6f * d / g.radius;
            s = g.strength * Mathf.Max(0f, fall) * Mathf.Clamp01(age * 1.4f);
            switch (g.shape)
            {
                case Shape.Out: return d < 0.3f ? Rand.InsideUnitCircleVec3.normalized : o / d;
                case Shape.Round: return d < 0.3f ? Vector3.zero : (new Vector3(-o.z, 0f, o.x) / d * 0.85f - o / d * 0.3f).normalized;
                default: return g.dir;
            }
        }

        public override void MapComponentTick()
        {
            if (gusts.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            gusts.RemoveAll(g => now - g.born > g.life);
            if (now % 10 != 0) return;
            foreach (Gust g in gusts.ToList())
            {
                IntVec3 mid = g.at.ToIntVec3();
                // a whirl shows itself: a spinning spiral of wind (drawn by code, Fix178)
                if (g.shape == Shape.Round && now % 20 == 0)
                {
                    if (g.tint.a > 0f) MapComponent_SpellSprites.PutTinted(map, "Wind/Swirl", g.tint, g.at, g.radius * 2.4f, 30, -1f, g.strength > 60f ? -9f : -6f);
                    else MapComponent_SpellSprites.Put(map, "Wind/Swirl", g.at, g.radius * 2.4f, 30, -1f, g.strength > 60f ? -9f : -6f);
                }
                foreach (IntVec3 c in GenRadial.RadialCellsAround(mid, g.radius, true))
                {
                    if (!c.InBounds(map)) continue;
                    Vector3 w = WindAt(g, c, now, out float s);
                    if (s < 12f || w.sqrMagnitude < 0.01f) continue;
                    // the weather's wind is out of doors; and any wind but a whirl's is broken by a wall upwind - what is behind a
                    // solid wall (or a mountain) is in its lee (Fix179)
                    // a hurricane's wind (or a storm's, 45+) tears off a built roof at its edge (Fix180)
                    if (s >= 45f && (g.charged || g.weather) && c.Roofed(map)) Roof(c, s, g);
                    if (g.weather && c.Roofed(map)) continue;
                    // under a shelter tree's canopy (Fix181): the great trees make a zone the wind hardly enters
                    if (Canopy(c, now, out float canopy)) { s *= canopy; if (s < 12f) continue; }
                    if (g.shape != Shape.Round)
                    {
                        // the lee (Fix180): looking up to three cells upwind - a solid wall breaks the wind (a third gets
                        // through), a tree or a half-wall takes a part (seven tenths), and they add up: a belt of trees shelters
                        for (int k = 1; k <= 3 && s >= 12f; k++)
                        {
                            IntVec3 up = (c.ToVector3Shifted() - w * (k + 0.2f)).ToIntVec3();
                            if (!up.InBounds(map)) break;
                            Building wb = up.GetEdifice(map);
                            if (wb != null && wb.def.Fillage == FillCategory.Full) { s *= k == 1 ? 0.35f : 0.55f; break; }
                            Plant tp = up.GetPlant(map);
                            if (tp != null && tp.def.plant.IsTree && tp.Growth > 0.5f || wb != null && wb.def.Fillage == FillCategory.Partial) s *= 0.7f;
                        }
                        if (s < 12f) continue;
                    }
                    if (Rand.Chance(g.weather ? 0.04f : 0.12f)) Streak(c, w, s, g.tint);
                    Carry(g, c, w, s, now);
                    if (s >= 15f) Drift(c, w);
                    Act(g, c, w, s, now);
                }
            }
        }

        /// <summary>Thrown against another body (the author, Fix177): if someone stands on the line of the throw, the two
        /// meet - both are struck and fall, the one hit is pushed on a little; a hurricane (past 60 m/s) does it harder.</summary>
        private bool Smash(Gust g, Pawn p, Vector3 w, float sp)
        {
            int reach = Mathf.Clamp(Mathf.RoundToInt((sp - 34f) / 5f), 1, 6);
            for (int i = 1; i <= reach; i++)
            {
                IntVec3 n = (p.DrawPos + w * i).ToIntVec3();
                if (!n.InBounds(map) || n.Impassable(map)) return false;
                Pawn q = n.GetFirstPawn(map);
                if (q == null || q == p || q.Dead) continue;
                float hit = Mathf.Min(18f, (sp - 30f) * 0.3f) * (sp > 60f ? 1.5f : 1f);
                if (i > 1) SpellHurl.Throw(p, (p.DrawPos - w * 3f).ToIntVec3(), i - 1, map, g.owner, sp / 40f, 60);
                p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, hit, 0f, -1f, g.owner));
                q.TakeDamage(new DamageInfo(DamageDefOf.Blunt, hit, 0f, -1f, g.owner));
                if (q.Spawned) { SpellHurl.Throw(q, (q.DrawPos - w * 3f).ToIntVec3(), Mathf.Max(1, reach / 2), map, g.owner, sp / 50f, 60); if (q.Spawned) Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(q, 120); }
                if (p.Spawned) Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(p, 120);
                FleckMaker.ThrowDustPuffThick(n.ToVector3Shifted(), map, 1.6f, new Color(0.6f, 0.6f, 0.6f, 0.8f));
                MoteMaker.ThrowText(n.ToVector3Shifted(), map, "smashed", 2f);
                if (Prefs.DevMode) Log.Message("[Avir wind] " + p.LabelShort + " smashed into " + q.LabelShort + " at " + sp.ToString("0") + " m/s");
                return true;
            }
            return false;
        }

        /// <summary>A built roof torn off at its edge (Fix180): only a roof over a cell next to an open sky, never a
        /// mountain's; a third of a chance a pulse at 45 m/s, more the harder it blows. Whoever is under it is struck.</summary>
        private void Roof(IntVec3 c, float s, Gust g)
        {
            RoofDef r = map.roofGrid.RoofAt(c);
            if (r == null || r.isNatural || r.isThickRoof) return;
            if (!GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)).Any(n => n.InBounds(map) && !n.Roofed(map))) return;
            if (!Rand.Chance(Mathf.Clamp01((s - 40f) / 90f))) return;
            map.roofGrid.SetRoof(c, null);
            FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.8f, new Color(0.65f, 0.6f, 0.55f, 0.8f));
            foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList()) p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(3f, 8f), 0f, -1f, g.owner));
            if (Prefs.DevMode) Log.Message("[Avir wind] roof torn off at " + c + " (" + s.ToString("0") + " m/s)");
        }

        // the shelter trees' canopies on this map, looked up again every few seconds
        private Dictionary<IntVec3, float> canopies = new Dictionary<IntVec3, float>();
        private int canopiesAt = -9999;

        /// <summary>Is the cell under a grown shelter tree's canopy? It lets through 40% of the wind (a great tree 30%) -
        /// open or closed, by day and by night: the trunk and the wings stand all the same.</summary>
        private bool Canopy(IntVec3 c, int now, out float factor)
        {
            if (now - canopiesAt > 250)
            {
                canopiesAt = now; canopies.Clear();
                foreach (Avir.North.Plant_ShelterTree t in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Avir.North.Plant_ShelterTree>())
                {
                    if (!t.CastsShade) continue;
                    float f = t.def.defName.Contains("Great") ? 0.3f : 0.4f;
                    foreach (IntVec3 x in t.CanopyCells()) if (!canopies.TryGetValue(x, out float old) || f < old) canopies[x] = f;
                }
            }
            return canopies.TryGetValue(c, out factor);
        }

        private static bool AvirBodyIs(Pawn p) { try { return Avir.AvirBody.IsAvir(p); } catch { return false; } }

        private bool Resting(Thing t, int now) => rest.TryGetValue(t.thingIDNumber, out int until) && until > now;
        private void Rest(Thing t, int now, int ticks) => rest[t.thingIDNumber] = now + ticks;

        /// <summary>Who a wind passes by: only those its spell's shaping spares (Fix189: before, it always spared its own mage
        /// - against the rule; a mage keeps off their own wind with a ward, as anyone: a ward of the storm wholly, of the air
        /// mostly, any other a quarter - Held).</summary>
        private static bool Spared(Gust g, Pawn p)
        {
            if (g.sculpt == 0 || g.owner == null) return false;
            bool own = SpellSculpt.Own(p, g.owner);
            return g.sculpt == 1 ? own : !own;
        }

        private void Act(Gust g, IntVec3 c, Vector3 w, float s, int now)
        {
            foreach (Thing t in c.GetThingList(map).ToList())
            {
                if (t.Destroyed || Resting(t, now)) continue;
                if (t is Pawn p)
                {
                    if (p.Dead || !p.Spawned || Spared(g, p) || SpellWards.Stops(p, false)) continue;
                    float hold = Mathf.Sqrt(Mathf.Max(0.5f, p.BodySize));   // the heavier hold longer
                    float sp = s / hold * Held(p, s);
                    // the Avir fly: the wind is their element - they ride an ordinary wind (60% off), a hurricane less (25%)
                    if (AvirBodyIs(p)) sp *= g.charged ? 0.75f : 0.4f;
                    // a hurricane (a storm's wind, 60+): it does not throw, it lifts - high, and lets fall; no body is too heavy
                    // for it - the heavy go up less, but the fall (or the blow, if it cannot lift them) still finds them
                    // a whirl of it mostly spins them round (thrown along its turning again and again), now and then takes them up
                    if (g.charged && s >= 60f && sp >= 25f && (g.shape != Shape.Round || Rand.Chance(0.3f)))
                    {
                        SpellHurl.Lift(p, g.owner, sp / 40f * SpellHurl.HurricaneLift);
                        Rest(p, now, g.shape == Shape.Round ? 45 : 120);   // a whirl takes them up again and again
                        if (Prefs.DevMode) Log.Message("[Avir wind] " + p.LabelShort + ": lifted by the hurricane at " + sp.ToString("0") + " m/s");
                        continue;
                    }
                    IntVec3 was = p.Position;
                    string did = null;
                    if (sp >= 40f && Smash(g, p, w, sp)) { Rest(p, now, 90); did = "smashed into another"; }
                    else if (sp >= 40f)
                    {
                        IntVec3 from = (p.DrawPos - w * 3f).ToIntVec3();
                        if (from == p.Position) from = p.Position - new IntVec3(Mathf.RoundToInt(w.x * 2f), 0, Mathf.RoundToInt(w.z * 2f));
                        SpellHurl.Throw(p, from, Mathf.Clamp(Mathf.RoundToInt((sp - 34f) / 5f), 1, 6), map, g.owner, sp / 40f, 60);
                        // nowhere to fly, or held by something: knocked down where they stand, and the wind's debris strikes
                        if (p.Spawned && p.Position == was) { Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(p, 120); did = "felled (could not fly)"; } else did = "thrown";
                        // lifted and dropped: a bruise from the landing at least, more the harder it blew (debris)
                        p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Min(14f, 3f + (sp - 40f) * 0.3f), 0f, -1f, g.owner));
                        Rest(p, now, g.shape == Shape.Round ? 30 : 90);   // a whirl keeps them going round
                    }
                    else if (sp >= 28f)
                    {
                        Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(p, 90);
                        p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(1f, 2.5f), 0f, -1f, g.owner));   // a fall: a small bruise
                        did = "knocked down"; Rest(p, now, 120);
                    }
                    else if (sp >= 20f) { p.stances?.stagger?.StaggerFor(60, 0.4f); did = "staggered"; Rest(p, now, 40); }
                    if (did != null && Prefs.DevMode) Log.Message("[Avir wind] " + p.LabelShort + ": " + did + " at " + sp.ToString("0") + " m/s");
                    continue;
                }
                if (t.def.EverHaulable && !(t is Corpse k && k.InnerPawn.BodySize > 1.5f))
                {
                    float mass = Mathf.Max(0.05f, t.GetStatValue(StatDefOf.Mass)) * Mathf.Sqrt(Mathf.Max(1, t.stackCount));
                    float need = 16f + 6f * Mathf.Sqrt(mass);
                    if (s < need) continue;
                    // things on shelves (a storage building) are held by it; those lying in a stockpile are packed - harder to lift
                    if (c.GetEdifice(map) is Building_Storage) continue;
                    if (t.IsInAnyStorage() && s < need * 1.5f) continue;
                    WindBorne.Launch(t, w, Mathf.Clamp((s - need) / 4f + 1f, 1f, 9f), s, g.owner, g.charged && t.def.IsMetal);
                    continue;
                }
                if (t is Plant pl && s >= 26f)
                {
                    // the green bends; the weak snap, trees take the load
                    if (!pl.def.plant.IsTree && pl.def.plant.harvestYield <= 0f && Rand.Chance(0.25f)) pl.Destroy(DestroyMode.KillFinalize);
                    // a tree holds by its size and weight: a sapling snaps at ~40 m/s, a grown tree only in a hurricane's wind
                    // (~60+), a big one later still; a shelter tree is rooted like a hill - only past 95 m/s (Fix181)
                    else if (pl.def.plant.IsTree)
                    {
                        float big = Mathf.Clamp((pl.def.graphicData?.drawSize.x ?? 1.5f) - 1.5f, 0f, 4f) * 6f;
                        float holds = pl is Avir.North.Plant_ShelterTree ? 95f : 38f + 25f * pl.Growth + big;
                        if (s >= holds) pl.TakeDamage(new DamageInfo(DamageDefOf.Blunt, (s - holds + 2f) * 0.6f, 0f, -1f, g.owner));
                    }
                    continue;
                }
                if (t is Building b && b.def.useHitPoints && s >= 30f)
                {
                    // the load on what stands: light things break first, doors are battered; the mountain's own rock never
                    // (Fix179), and a sturdy wall (over 300) only in a hurricane's wind (60+), and slowly
                    if (b.def.building != null && (b.def.building.isNaturalRock || b.def.building.isResourceRock)) continue;
                    if (b.MaxHitPoints > 300 && s < 60f) continue;
                    float light = b.MaxHitPoints <= 120 ? 2f : b.MaxHitPoints <= 300 ? 1f : 0.25f;
                    if (b.def.Fillage == FillCategory.Full || b is Building_Door || b.MaxHitPoints <= 300)
                    {
                        b.TakeDamage(new DamageInfo(DamageDefOf.Blunt, (s - 28f) * 0.35f * light, 0f, -1f, g.owner));
                        if (Rand.Chance(0.2f)) FleckMaker.ThrowDustPuffThick(b.TrueCenter(), map, 1.3f, new Color(0.7f, 0.7f, 0.68f, 0.6f));
                    }
                }
            }
        }


        /// <summary>The game's gas grid drifts (Fix185-188): a share of the cell's gas goes on - more in a stronger wind - most
        /// of it downwind, some slipping aside, the rest lingers; a cloud stretches, frays and thins as it goes.</summary>
        public static void DriftGridGas(Map map, IntVec3 c, IntVec3 to, Vector3 w, float s)
        {
            if (!to.InBounds(map) || to == c || !map.gasGrid.AnyGasAt(c) || !map.gasGrid.GasCanMoveTo(to)) return;
            // (Fix185) it drifts, not jumps: a share of the cell's gas goes on - more in a stronger wind - most of it
            // downwind, some slipping aside, and the rest lingers; so a cloud stretches, frays and thins as it goes
            float share = Mathf.Clamp(s / 45f, 0.25f, 0.8f) * Rand.Range(0.75f, 1.1f);
            Vector3 sideDir = Quaternion.AngleAxis(Rand.Bool ? 45f : -45f, Vector3.up) * w;
            IntVec3 side = (c.ToVector3Shifted() + sideDir).ToIntVec3();
            bool canSide = side.InBounds(map) && side != c && map.gasGrid.GasCanMoveTo(side);
            uint a = map.gasGrid.GetDirect(c), b = map.gasGrid.GetDirect(to), sd = canSide ? map.gasGrid.GetDirect(side) : 0u;
            uint stay = 0u, onTo = 0u, onSide = 0u;
            for (int sh = 0; sh < 32; sh += 8)
            {
                int v = (int)((a >> sh) & 0xFF);
                int moved = Mathf.RoundToInt(v * share), aside = canSide ? Mathf.RoundToInt(moved * 0.3f) : 0;
                stay |= (uint)(v - moved) << sh;
                onTo |= (uint)Mathf.Min(255, (int)((b >> sh) & 0xFF) + moved - aside) << sh;
                onSide |= (uint)Mathf.Min(255, (int)((sd >> sh) & 0xFF) + aside) << sh;
            }
            map.gasGrid.SetDirect(c, stay);
            map.gasGrid.SetDirect(to, onTo);
            if (canSide) map.gasGrid.SetDirect(side, onSide);
        }

        /// <summary>A gas that is a thing (steam, marsh gas, a mod's) drifts a cell on, if there is room.</summary>
        public static bool DriftGasThing(Gas gas, Map map, IntVec3 to)
        {
            if (gas == null || gas.Destroyed || !to.InBounds(map) || !to.Walkable(map) || to.Filled(map)) return false;
            if (to.GetThingList(map).Any(x => x.def == gas.def)) return false;
            gas.Position = to;
            return true;
        }

        /// <summary>What the wind carries (Fix170) - neither good nor bad of itself:
        ///   fire - a middling wind fans it and drives it on downwind, a strong one blows the small flames out;
        ///   gas - the game's own (smoke, tox gas, rot stink, deadlife dust - and every mod's gas that lives in the same
        ///   grid) and every gas that is a thing (ours: steam, marsh gas; any mod's of the old kind) drift with the wind,
        ///   a whirl turns them round itself; one's own gas blown back can cover one's own;
        ///   ash - blown on, it settles and feeds the ground where it lands;
        ///   water - a strong wind tears spray off water and puddles and soaks whoever is downwind;
        ///   lightning - a storm's wind (charged): the wet in it are struck now and then.</summary>
        private void Carry(Gust g, IntVec3 c, Vector3 w, float s, int now)
        {
            IntVec3 to = (c.ToVector3Shifted() + w * (s > 30f ? 2f : 1f)).ToIntVec3();
            bool canTo = to.InBounds(map) && to != c;
            // the game's gas grid: the whole cell's gas moves on, added to what is there
            if (canTo) DriftGridGas(map, c, to, w, s);
            var here = c.GetThingList(map);
            for (int i = here.Count - 1; i >= 0; i--)
            {
                Thing t = here[i];
                if (t.Destroyed || Resting(t, now)) continue;
                if (t is Gas gas && canTo && to.Walkable(map) && !to.GetThingList(map).Any(x => x.def == gas.def) && Rand.Chance(Mathf.Clamp01(s / 25f)))
                { gas.Position = to; Rest(gas, now, 20); continue; }
                if (t is Fire f)
                {
                    if (s > 35f && f.fireSize < 0.6f) { f.Destroy(); FleckMaker.ThrowSmoke(c.ToVector3Shifted(), map, 0.8f); continue; }   // blown out
                    f.fireSize = Mathf.Min(1.75f, f.fireSize + 0.04f * s / 20f);                                                         // fanned
                    if (canTo && Rand.Chance(Mathf.Clamp01((s - 10f) / 70f))) FireUtility.TryStartFireIn(to, map, 0.3f, g.owner);
                    continue;
                }
                if (t is HeldFlame hf && canTo && to.Walkable(map) && !to.GetThingList(map).Any(x => x is HeldFlame) && Rand.Chance(Mathf.Clamp01((s - 12f) / 40f)))
                {
                    // a wall of fire is fire: the wind drives it on, cell by cell, and it sets alight what it reaches
                    hf.Position = to; Rest(hf, now, 30);
                    FireUtility.TryStartFireIn(to, map, 0.2f, g.owner);
                    continue;
                }
                if (t.def == ThingDefOf.Filth_Ash && canTo && to.Walkable(map) && Rand.Chance(0.4f))
                {
                    t.Position = to; Rest(t, now, 60);
                    MapComponent_LifeFertility.Of(map)?.Add(to, 0.15f, 3);   // ash settles and feeds the ground
                    continue;
                }
            }
            // spray torn off water
            if (s > 25f && Reactions.Wet(c, map) && Rand.Chance(0.3f))
            {
                FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1.2f);
                for (int k = 1; k <= 2; k++)
                {
                    IntVec3 d = (c.ToVector3Shifted() + w * k).ToIntVec3();
                    if (!d.InBounds(map)) continue;
                    foreach (Pawn p in d.GetThingList(map).OfType<Pawn>().ToList()) WaterMagic.Soak(p, 0.25f);
                }
            }
            // a storm's wind: lightning finds the wet and the metal
            if (g.charged && s > 20f)
                foreach (Pawn p in here.OfType<Pawn>().ToList())
                {
                    if (p.Dead || Spared(g, p)) continue;
                    bool wet = p.health.hediffSet.HasHediff(WaterDefOf.Avir_Soaked) || Reactions.Wet(c, map);
                    bool metal = SpellBody.Of(p).metalArmour;
                    if ((wet || metal) && Rand.Chance(wet && metal ? 0.12f : 0.06f))
                    {
                        p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 4f * s / 40f, 0f, -1f, g.owner));
                        p.stances?.stunner?.StunFor(40, g.owner, false, false);
                        FleckMaker.ThrowLightningGlow(p.DrawPos, map, 1.2f);
                        MapComponent_SpellSprites.Put(map, "Storm/Strike", p.DrawPos, 1.4f, 14);
                        Electricity.Spread(p.Position, map, 0.6f, g.owner, false);   // and runs on (Fix182)
                    }
                }
        }

        /// <summary>The wind at a cell now, the strongest of the gusts there (Fix175: shown in the game's own readout).</summary>
        public float At(IntVec3 c, out Vector3 dir)
        {
            dir = Vector3.zero; float best = 0f;
            if (gusts.Count == 0 || !c.InBounds(map)) return 0f;
            int now = Find.TickManager.TicksGame;
            foreach (Gust g in gusts) { Vector3 w = WindAt(g, c, now, out float s); if (s > best) { best = s; dir = w; } }
            return best;
        }

        public static string Readout(Map map, IntVec3 c)
        {
            var comp = Of(map);
            if (comp == null) return null;
            float s = comp.At(c, out Vector3 dir);
            if (s < 1f) return null;
            string arrow = dir.sqrMagnitude < 0.01f ? "" : " " + "→↗↑↖←↙↓↘"[Mathf.RoundToInt(((Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg) + 360f) % 360f / 45f) % 8];
            string what = What(s);
            return "Magic wind " + s.ToString("0") + " m/s" + arrow + " (" + what + ")";
        }

        /// <summary>A ball lightning in the wind is carried by it (Fix180): its field moves a cell along it, now and then.</summary>
        private void Drift(IntVec3 c, Vector3 w)
        {
            var sf = map.GetComponent<SecondFields>();
            if (sf == null) return;
            foreach (SecondFields.Fld f in sf.fields.Where(f => f.y == SecondKind.BallLightning && f.cells.Contains(c)).ToList())
            {
                if (!Rand.Chance(0.08f)) continue;
                IntVec3 d = new IntVec3(Mathf.RoundToInt(w.x), 0, Mathf.RoundToInt(w.z));
                if (d == IntVec3.Zero) continue;
                f.cells = new HashSet<IntVec3>(f.cells.Select(x => x + d).Where(x => x.InBounds(map)));
                f.center += d.ToVector3(); f.rad = 0f;
            }
        }

        /// <summary>A streak of dust along the wind: what the eye sees of it.</summary>
        private void Streak(IntVec3 c, Vector3 w, float s, Color tint)
        {
            // a thin line of wind along it (drawn by code, Fix178), and now and then a puff of dust
            Vector3 at = c.ToVector3Shifted() + new Vector3(Rand.Range(-0.4f, 0.4f), 0f, Rand.Range(-0.4f, 0.4f));
            float len = Mathf.Clamp(s / 12f, 1.5f, 4.5f);
            if (tint.a > 0f) MapComponent_SpellSprites.PutTinted(map, "Wind/Streak", tint, at, len, 22, w.AngleFlat() - 90f, 0f, 5.3f);
            else MapComponent_SpellSprites.Put(map, "Wind/Streak", at, len, 22, w.AngleFlat() - 90f, 0f, 5.3f);
            if (!Rand.Chance(0.35f)) return;
            FleckCreationData d = FleckMaker.GetDataStatic(c.ToVector3Shifted() + new Vector3(Rand.Range(-0.4f, 0.4f), 0f, Rand.Range(-0.4f, 0.4f)), map, FleckDefOf.DustPuffThick, Rand.Range(0.6f, 1.1f));
            if (tint.a > 0f) d.instanceColor = tint;
            d.velocityAngle = w.AngleFlat();
            d.velocitySpeed = Mathf.Min(6f, s / 8f);
            d.rotationRate = Rand.Range(-90f, 90f);
            map.flecks.CreateFleck(d);
        }
    }

    /// <summary>A thing torn up by the wind: it flies along it in an arc, tumbling, a shadow under it, and comes down
    /// where the wind drops it - against a wall it strikes the wall and falls at its foot. Fragile things suffer from it.</summary>
    public class WindBorne : ThingWithComps, IThingHolder
    {
        private ThingOwner<Thing> inner;
        private Vector3 from, to;
        private int t, dur;
        private float spin, angle, height, speed;
        private Pawn by;
        private bool hitWall;

        public WindBorne() { inner = new ThingOwner<Thing>(this); }
        public ThingOwner GetDirectlyHeldThings() => inner;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        private bool charged;

        public static void Launch(Thing thing, Vector3 dir, float dist, float windSpeed, Pawn by, bool charged = false)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_WindBorne");
            Map map = thing.Map;
            if (def == null || map == null) return;
            IntVec3 start = thing.Position, end = start; bool wall = false;
            for (int i = 1; i <= Mathf.CeilToInt(dist); i++)
            {
                IntVec3 n = (start.ToVector3Shifted() + dir * i).ToIntVec3();
                if (!n.InBounds(map)) break;
                Building e = n.GetEdifice(map);
                if (e != null && e.def.Fillage == FillCategory.Full) { wall = true; if (e.def.useHitPoints && !(e.def.building?.isNaturalRock ?? false)) e.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Min(20f, thing.GetStatValue(StatDefOf.Mass) * windSpeed * 0.05f), 0f, -1f, by)); break; }
                end = n;
            }
            if (end == start) return;
            var w = (WindBorne)ThingMaker.MakeThing(def);
            w.from = start.ToVector3Shifted(); w.to = end.ToVector3Shifted(); w.by = by; w.hitWall = wall; w.speed = windSpeed; w.charged = charged;
            w.dur = Mathf.Clamp(Mathf.RoundToInt((w.to - w.from).magnitude * 7f), 10, 90);
            w.height = Mathf.Clamp((w.to - w.from).magnitude * 0.18f, 0.3f, 1.6f);
            w.spin = Rand.Range(240f, 720f) * (Rand.Bool ? 1f : -1f);
            thing.DeSpawn();
            w.inner.TryAdd(thing);
            GenSpawn.Spawn(w, start, map);
        }

        public override Vector3 DrawPos
        {
            get
            {
                float f = dur > 0 ? Mathf.Clamp01(t / (float)dur) : 1f;
                Vector3 p = Vector3.Lerp(from, to, f);
                p.z += height * 4f * f * (1f - f);   // the arc
                p.y = AltitudeLayer.Skyfaller.AltitudeFor();
                return p;
            }
        }

        protected override void Tick()
        {
            t++; angle += spin / 60f;
            if (Spawned && t % 4 == 0)
            {
                Position = DrawPos.ToIntVec3().InBounds(Map) ? DrawPos.ToIntVec3() : Position;
                // a heavy thing in flight (a stone chunk, a log) strikes whoever it passes, and falls there (Fix177)
                Thing thing = inner.FirstOrDefault();
                if (thing != null && t > 4 && t < dur - 2)
                {
                    float mass = thing.GetStatValue(StatDefOf.Mass) * thing.stackCount;
                    Pawn q = mass >= 3f ? Position.GetFirstPawn(Map) : null;
                    if (q != null && q != by)
                    {
                        q.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Min(30f, mass * speed * 0.02f + 3f), 0f, -1f, by));
                        if (mass >= 10f && q.Spawned) Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(q, 90);
                        FleckMaker.ThrowDustPuffThick(q.DrawPos, Map, 1.3f, new Color(0.6f, 0.6f, 0.6f, 0.8f));
                        to = Position.ToVector3Shifted(); dur = t; // it stops there
                    }
                }
            }
            if (t >= dur) Land();
        }

        private void Land()
        {
            Map map = Map; IntVec3 at = to.ToIntVec3();
            if (map == null) { Destroy(); return; }
            Thing thing = inner.FirstOrDefault();
            if (thing != null)
            {
                inner.Remove(thing);
                // falling hurts the fragile, and whoever it lands on
                if (thing.def.useHitPoints && speed > 30f) thing.HitPoints = Mathf.Max(1, thing.HitPoints - Mathf.RoundToInt(thing.MaxHitPoints * Mathf.Min(0.3f, (speed - 30f) / 100f)));
                Pawn under = at.InBounds(map) ? at.GetFirstPawn(map) : null;
                if (under != null) under.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Min(12f, thing.GetStatValue(StatDefOf.Mass) * 2f), 0f, -1f, by));
                if (!GenPlace.TryPlaceThing(thing, at.InBounds(map) ? at : Position, map, ThingPlaceMode.Near)) thing.Destroy();
                FleckMaker.ThrowDustPuff(at, map, hitWall ? 1.2f : 0.7f);
                if (charged)
                {
                    // metal carried by a storm's wind comes down with the lightning it gathered
                    foreach (Pawn q in GenRadial.RadialDistinctThingsAround(at, map, 1.5f, true).OfType<Pawn>().ToList()) q.TakeDamage(new DamageInfo(DamageDefOf.Burn, 5f, 0f, -1f, by));
                    FleckMaker.ThrowLightningGlow(at.ToVector3Shifted(), map, 1.5f);
                    MapComponent_SpellSprites.Put(map, "Storm/Strike", at.ToVector3Shifted(), 1.6f, 14);
                }
            }
            Destroy();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Thing thing = inner.FirstOrDefault();
            if (thing == null) return;
            Vector3 shadow = Vector3.Lerp(from, to, dur > 0 ? Mathf.Clamp01(t / (float)dur) : 1f); shadow.y = AltitudeLayer.Shadows.AltitudeFor();
            Graphics.DrawMesh(MeshPool.plane08, Matrix4x4.TRS(shadow, Quaternion.identity, new Vector3(0.6f, 1f, 0.35f)), MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowCircle", ShaderDatabase.Transparent, new Color(1f, 1f, 1f, 0.5f)), 0);
            thing.Graphic?.Draw(DrawPos, Rot4.North, thing, angle);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref inner, "inner", this);
            Scribe_Values.Look(ref from, "from"); Scribe_Values.Look(ref to, "to");
            Scribe_Values.Look(ref t, "t"); Scribe_Values.Look(ref dur, "dur");
            Scribe_Values.Look(ref spin, "spin"); Scribe_Values.Look(ref angle, "angle"); Scribe_Values.Look(ref height, "height"); Scribe_Values.Look(ref speed, "speed");
            Scribe_Values.Look(ref hitWall, "hitWall"); Scribe_Values.Look(ref charged, "charged");
            Scribe_References.Look(ref by, "by");
        }
    }
}

namespace Avir.Magic
{
    public static class MagicWindDev
    {
        [LudeonTK.DebugAction("Avir magic", "Wind: blast out here (46 m/s)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Out() => MapComponent_MagicWind.Blow(Find.CurrentMap, MapComponent_MagicWind.Shape.Out, Verse.UI.MouseCell().ToVector3Shifted(), 6f, 46f, 60, null);

        [LudeonTK.DebugAction("Avir magic", "Wind: whirl here (44 m/s, 5 s)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Round() => MapComponent_MagicWind.Blow(Find.CurrentMap, MapComponent_MagicWind.Shape.Round, Verse.UI.MouseCell().ToVector3Shifted(), 3f, 44f, 300, null);

        [LudeonTK.DebugAction("Avir magic", "Wind: front east (32 m/s, 3 s)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Front() => MapComponent_MagicWind.Blow(Find.CurrentMap, MapComponent_MagicWind.Shape.Front, Verse.UI.MouseCell().ToVector3Shifted(), 7f, 32f, 180, null, UnityEngine.Vector3.right);
    }
}

namespace Avir.Magic
{
    /// <summary>The magic wind in the game's own readout at the bottom left (the author's choice, Fix175): our line goes
    /// at the bottom of it, the game's lines move up by one.</summary>
    [HarmonyLib.HarmonyPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
    public static class Patch_WindReadout
    {
        private static readonly System.Reflection.FieldInfo botLeft = HarmonyLib.AccessTools.Field(typeof(MouseoverReadout), "BotLeft");
        private static Vector2 saved; private static bool moved;

        public static void Prefix()
        {
            moved = false;
            if (botLeft == null || Event.current.type != EventType.Repaint || Find.CurrentMap == null || Find.MainTabsRoot.OpenTab != null) return;
            string line = MapComponent_MagicWind.Readout(Find.CurrentMap, Verse.UI.MouseCell());
            if (line == null) return;
            saved = (Vector2)botLeft.GetValue(null);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.88f, 1f);
            Widgets.Label(new Rect(saved.x, Verse.UI.screenHeight - saved.y, 999f, 999f), line);
            GUI.color = Color.white;
            botLeft.SetValue(null, new Vector2(saved.x, saved.y + 19f));
            moved = true;
        }

        public static void Postfix() { if (moved && botLeft != null) botLeft.SetValue(null, saved); moved = false; }
    }
}
