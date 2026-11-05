using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Sense (Магия-правила.md, "Чутьё"; the author's design, Nov 2026): a sense is laid on the caster and lasts a
    /// while (4 h x strength, longer for each Anchor). What it feels and where, by the element:
    ///   Fire - warmth, round the caster as they walk: warm bodies, the machines too; the unseen among them show.
    ///   Water - blood, round the caster as they walk: the living (no machines); hidden sickness comes to light, a
    ///           metalhorror in a body is found out; the unseen show.
    ///   Earth - the ground, once, where it was cast: steps round about, and the ore deep below - shown to the caster for
    ///           good (select them to see it, as a deep drill sees it); a strong cast draws new ore up.
    ///   Air - the whole map in the open (not under a roof): every unseen one out there is shown.
    ///   Bog - a watcher on a place: for long, the unseen there are shown; no ore.
    ///   Magma - a place: the unseen living there, and the ore below it.
    ///   Steam - round the caster: everyone, the machines too; an unseen one found is warned of.
    ///   Dust - a place far off: the unseen there, and the ore below.
    ///   Haze, Storm - round the caster, under roofs too.
    /// Everything is looked at every four seconds, not every tick.</summary>
    public static class SpellSense
    {
        public enum By { Warmth, Blood, Ground, Sound }

        /// <summary>Lays a sense: element (or what two elements made), where, how wide, how strong.</summary>
        public static void Begin(Pawn caster, Map map, IntVec3 target, float radius, float power, RuneKind el, ComboKind k, int anchor)
        {
            if (caster == null || map == null) return;
            var s = new MapComponent_Sensing.Sense { caster = caster, el = el, combo = k, radius = radius, at = caster.Position,
                until = Find.TickManager.TicksGame + Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power * (1 + anchor)) };
            switch (k)
            {
                case ComboKind.None:
                    if (el == RuneKind.Earth)
                    {
                        // the ground answers once: steps, and the deep
                        Feel(caster, map, caster.Position, radius, power, By.Ground);
                        Deep(caster, map, caster.Position, radius, power);
                        return;
                    }
                    s.follow = el != RuneKind.Air; s.unseen = true; s.whole = el == RuneKind.Air;
                    break;
                case ComboKind.Bog: s.at = target; s.unseen = true; s.until += (s.until - Find.TickManager.TicksGame) * 2; break;
                case ComboKind.Magma: s.at = target; s.unseen = true; Deep(caster, map, target, radius, power); break;
                case ComboKind.Dust: s.at = target; s.unseen = true; Deep(caster, map, target, radius, power); break;
                case ComboKind.Steam: s.follow = true; s.unseen = true; s.warn = true; break;
                default: s.follow = true; s.unseen = true; s.indoors = true; break;   // haze, storm
            }
            map.GetComponent<MapComponent_Sensing>()?.Add(s);
            Hediff_Sensing.Show(caster);
            Messages.Message(caster.LabelShortCap + " " + Phrase(s) + " for " + ((s.until - Find.TickManager.TicksGame) / (float)GenDate.TicksPerHour).ToString("0.#") + " h.", caster, MessageTypeDefOf.NeutralEvent, false);
        }

        private static string Phrase(MapComponent_Sensing.Sense s)
        {
            if (s.whole) return "listens to the wind over the whole land - the unseen in the open will be shown";
            if (!s.follow) return "keeps watch on a place - the unseen there will be shown";
            if (s.combo == ComboKind.Steam) return "feels everyone round about, the machines too";
            return s.el == RuneKind.Water ? "feels the blood of the living round about" : "feels the warmth round about, the machines too";
        }

        /// <summary>One look: whom it feels round a point, marked for a while.</summary>
        public static void Feel(Pawn caster, Map map, IntVec3 at, float radius, float power, By by)
        {
            var found = map.mapPawns.AllPawnsSpawned.Where(p => p != caster && !p.Dead && p.Position.DistanceTo(at) <= radius && Feels(p, by)).ToList();
            map.GetComponent<MapComponent_FireMagic>()?.AddSense(found, Mathf.RoundToInt(1800f * power));
            int foes = found.Count(p => p.HostileTo(caster));
            Messages.Message(caster.LabelShortCap + " feels " + found.Count + " " + (by == By.Ground ? "steps on the ground" : "bodies") + " around" + (foes > 0 ? " - " + foes + " of them hostile" : "") + ".", caster, MessageTypeDefOf.NeutralEvent, false);
        }

        public static bool Feels(Pawn p, By by)
        {
            switch (by)
            {
                case By.Warmth: return p.RaceProps.IsFlesh || p.RaceProps.IsMechanoid;   // a machine runs hot
                case By.Blood: return p.RaceProps.IsFlesh;                                 // no blood in a machine
                default: return true;
            }
        }

        /// <summary>Fire's old call (FireMagic.Sense): now the sense of warmth.</summary>
        public static void Feel(Pawn caster, Map map, float radius, float power, By by) =>
            Begin(caster, map, caster.Position, radius, power, by == By.Blood ? RuneKind.Water : RuneKind.Fire, ComboKind.None, 0);

        /// <summary>What the blood tells: hidden sickness comes to light; a metalhorror in a body is found out.</summary>
        public static int Blood(Pawn p)
        {
            int n = 0;
            foreach (Hediff h in p.health.hediffSet.hediffs.ToList())
            {
                HediffComp_Discoverable d = h.TryGetComp<HediffComp_Discoverable>();
                if (d == null || discovered(d)) continue;
                discovered(d) = true; p.health.Notify_HediffChanged(h); n++;
            }
            try
            {
                if (ModsConfig.AnomalyActive && MetalhorrorUtility.IsInfected(p))
                {
                    MetalhorrorUtility.Detect(p, "A caster's sense of the blood felt something of metal moving inside " + p.LabelShort + ".", null, 1f);
                    n++;
                }
            }
            catch (System.Exception e) { Log.WarningOnce("[Avir] water sense - metalhorror: " + e.Message, 0x5E45E1); }
            return n;
        }

        private static readonly AccessTools.FieldRef<HediffComp_Discoverable, bool> discovered = AccessTools.FieldRefAccess<HediffComp_Discoverable, bool>("discovered");

        /// <summary>The unseen made seen.</summary>
        public static bool Unveil(Pawn p)
        {
            bool any = false;
            foreach (HediffComp_Invisibility inv in p.health.hediffSet.hediffs.OfType<HediffWithComps>().SelectMany(h => h.comps.OfType<HediffComp_Invisibility>()).ToList())
                if (!inv.PsychologicallyVisible) { inv.BecomeVisible(); any = true; }
            return any;
        }

        // ------------------------------------------------------------------ the deep

        /// <summary>The strength from which a cast draws new ore up.</summary>
        public const float DrawFrom = 1.5f;

        /// <summary>The ore deep below round a point becomes known to the caster for good - select them to see it; a strong
        /// cast also draws new ore up from the depths (more of it, and dearer, the stronger). The ground it was drawn from
        /// rests a day.</summary>
        public static void Deep(Pawn caster, Map map, IntVec3 at, float radius, float power)
        {
            if (caster == null || map?.deepResourceGrid == null) return;
            var c = map.GetComponent<MapComponent_DeepSense>();
            if (c == null) return;
            string raised = power >= DrawFrom ? Raise(map, at, radius, power, c) : "";
            int known = c.Learn(caster, GenRadial.RadialCellsAround(at, Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f), true)
                .Where(x => x.InBounds(map) && map.deepResourceGrid.ThingDefAt(x) != null && map.deepResourceGrid.CountAt(x) > 0));
            Messages.Message("The ground answers " + caster.LabelShortCap + ": " + (known > 0 ? known + " cells with ore deep below" : "nothing deep below") + "." + raised
                + (known > 0 ? " Select " + caster.LabelShort + " to see where it lies." : ""), new TargetInfo(at, map), MessageTypeDefOf.PositiveEvent, false);
        }

        private static string Raise(Map map, IntVec3 at, float radius, float power, MapComponent_DeepSense c)
        {
            int now = Find.TickManager.TicksGame;
            if (c.rested.Any(r => r.until > now && r.at.DistanceTo(at) < 20f)) return " This ground has given up its ore lately - it rests.";
            // the dearer ores open with strength: the cheapest first, the dearest only to a strong cast
            var ores = DefDatabase<ThingDef>.AllDefs.Where(d => d.deepCommonality > 0f && d.deepCountPerCell > 0).OrderBy(d => d.BaseMarketValue).ToList();
            if (ores.Count == 0) return "";
            int open = Mathf.Clamp(Mathf.CeilToInt(ores.Count * Mathf.Clamp01((power - 1f) / 3f)), 1, ores.Count);
            int lumps = Mathf.Clamp(Mathf.FloorToInt(power - 0.5f), 1, 4);
            var made = new List<string>();
            for (int i = 0; i < lumps; i++)
            {
                ThingDef ore = ores.Take(open).RandomElementByWeight(d => d.deepCommonality * (1f + 0.5f * ores.IndexOf(d)));
                IntVec3 heart = GenRadial.RadialCellsAround(at, Mathf.Min(radius, 25f), false).Where(x => x.InBounds(map) && x.Walkable(map) && map.deepResourceGrid.ThingDefAt(x) == null).InRandomOrder().FirstOrDefault();
                if (!heart.IsValid) break;
                int size = Mathf.RoundToInt(Rand.Range(8f, 14f) * Mathf.Sqrt(power));
                int n = 0;
                foreach (IntVec3 x in GenRadial.RadialCellsAround(heart, 4f, true).Where(x => x.InBounds(map) && map.deepResourceGrid.ThingDefAt(x) == null).Take(size))
                {
                    map.deepResourceGrid.SetAt(x, ore, ore.deepCountPerCell);
                    n++;
                }
                if (n > 0) { made.Add(ore.label); FleckMaker.ThrowDustPuffThick(heart.ToVector3Shifted(), map, 3f, MagicFx.EarthDust); }
            }
            c.rested.Add(new MapComponent_DeepSense.Rest { at = at, until = now + GenDate.TicksPerDay });
            return made.Count > 0 ? " Drawn up from the depths: " + string.Join(", ", made) + "." : "";
        }
    }

    /// <summary>The senses laid on casters on a map, looked at every four seconds.</summary>
    public class MapComponent_Sensing : MapComponent
    {
        public class Sense : IExposable
        {
            public Pawn caster; public RuneKind el; public ComboKind combo; public IntVec3 at; public float radius; public int until;
            public bool follow, whole, unseen, warn, indoors;
            public void ExposeData()
            {
                Scribe_References.Look(ref caster, "caster"); Scribe_Values.Look(ref el, "el"); Scribe_Values.Look(ref combo, "combo");
                Scribe_Values.Look(ref at, "at"); Scribe_Values.Look(ref radius, "radius"); Scribe_Values.Look(ref until, "until");
                Scribe_Values.Look(ref follow, "follow"); Scribe_Values.Look(ref whole, "whole"); Scribe_Values.Look(ref unseen, "unseen");
                Scribe_Values.Look(ref warn, "warn"); Scribe_Values.Look(ref indoors, "indoors");
            }
        }

        public List<Sense> all = new List<Sense>();
        private readonly HashSet<int> warned = new HashSet<int>();

        public MapComponent_Sensing(Map map) : base(map) { }

        public void Add(Sense s)
        {
            // one sense of a kind on a caster: the new one takes the old one's place
            all.RemoveAll(o => o.caster == s.caster && o.el == s.el && o.combo == s.combo);
            all.Add(s);
            Look(s, Find.TickManager.TicksGame);
        }

        public static Sense Of(Pawn p) => p?.MapHeld?.GetComponent<MapComponent_Sensing>()?.all.FirstOrDefault(s => s.caster == p);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref all, "senses", LookMode.Deep);
            if (all == null) all = new List<Sense>();
        }

        public override void MapComponentTick()
        {
            if (all.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            if ((now + map.uniqueID) % 250 != 0) return;
            all.RemoveAll(s => s.until <= now || s.caster == null || s.caster.Dead || (s.follow && (!s.caster.Spawned || s.caster.Map != map)));
            foreach (Sense s in all) Look(s, now);
        }

        private void Look(Sense s, int now)
        {
            IntVec3 c = s.follow && s.caster.Spawned ? s.caster.Position : s.at;
            SpellSense.By by = s.combo == ComboKind.None ? (s.el == RuneKind.Water ? SpellSense.By.Blood : s.el == RuneKind.Fire ? SpellSense.By.Warmth : SpellSense.By.Sound)
                : s.combo == ComboKind.Bog || s.combo == ComboKind.Magma ? SpellSense.By.Blood : SpellSense.By.Sound;
            var found = new List<Pawn>();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == s.caster || p.Dead) continue;
                if (s.whole) { if (p.Position.Roofed(map)) continue; }        // the wind: the whole land, in the open
                else if (p.Position.DistanceTo(c) > s.radius) continue;
                if (!SpellSense.Feels(p, by)) continue;
                if (!s.whole && !s.indoors && s.combo != ComboKind.None && p.Position.Roofed(map) && s.combo != ComboKind.Bog && s.combo != ComboKind.Magma && s.combo != ComboKind.Dust && s.combo != ComboKind.Steam) continue;
                found.Add(p);
                if (s.unseen && SpellSense.Unveil(p) && s.warn && warned.Add(p.thingIDNumber))
                    Messages.Message("Something unseen moves near " + s.caster.LabelShort + "!", p, MessageTypeDefOf.ThreatSmall, false);
                if (by == SpellSense.By.Blood) SpellSense.Blood(p);
            }
            if (!s.whole) map.GetComponent<MapComponent_FireMagic>()?.AddSense(found, 300);
        }
    }

    /// <summary>The sense on the caster's health tab: what it feels and for how long.</summary>
    public class Hediff_Sensing : HediffWithComps
    {
        private static HediffDef def_;
        public static HediffDef Def => def_ ?? (def_ = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Sensing"));

        public static void Show(Pawn p)
        {
            if (Def == null || p?.health == null) return;
            if (p.health.hediffSet.GetFirstHediffOfDef(Def) == null) p.health.AddHediff(Def);
        }

        private MapComponent_Sensing.Sense S => MapComponent_Sensing.Of(pawn);

        public override string LabelInBrackets
        {
            get
            {
                var s = S;
                if (s == null) return null;
                string what = s.whole ? "the whole land" : s.follow ? "round them" : "a place";
                return what + " · " + Mathf.Max(0f, (s.until - Find.TickManager.TicksGame) / (float)GenDate.TicksPerHour).ToString("0.#") + " h";
            }
        }

        public override bool ShouldRemove => S == null;
    }

    /// <summary>The ore deep below that casters have felt: each knows what they found, for good; select them to see it.</summary>
    public class MapComponent_DeepSense : MapComponent
    {
        public class Rest : IExposable
        {
            public IntVec3 at; public int until;
            public void ExposeData() { Scribe_Values.Look(ref at, "at"); Scribe_Values.Look(ref until, "until"); }
        }

        public List<Rest> rested = new List<Rest>();
        private Dictionary<int, HashSet<IntVec3>> known = new Dictionary<int, HashSet<IntVec3>>();

        public MapComponent_DeepSense(Map map) : base(map) { }

        public int Learn(Pawn p, IEnumerable<IntVec3> cells)
        {
            if (!known.TryGetValue(p.thingIDNumber, out var set)) known[p.thingIDNumber] = set = new HashSet<IntVec3>();
            int n = 0;
            foreach (IntVec3 c in cells) { set.Add(c); n++; }
            return n;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref rested, "rested", LookMode.Deep);
            if (rested == null) rested = new List<Rest>();
            List<int> keys = null; List<string> vals = null;
            if (Scribe.mode == LoadSaveMode.Saving) { keys = known.Keys.ToList(); vals = keys.Select(k => string.Join(";", known[k].Select(c => c.x + "," + c.z))).ToList(); }
            Scribe_Collections.Look(ref keys, "knownWho", LookMode.Value);
            Scribe_Collections.Look(ref vals, "knownCells", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                known = new Dictionary<int, HashSet<IntVec3>>();
                if (keys != null && vals != null)
                    for (int i = 0; i < keys.Count && i < vals.Count; i++)
                        known[keys[i]] = new HashSet<IntVec3>((vals[i] ?? "").Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Split(',')).Where(a => a.Length == 2).Select(a => new IntVec3(int.Parse(a[0]), 0, int.Parse(a[1]))));
            }
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 == 0) rested.RemoveAll(r => r.until <= Find.TickManager.TicksGame);
        }

        private static readonly Dictionary<ThingDef, Material> mats = new Dictionary<ThingDef, Material>();

        /// <summary>The caster selected: what they found deep below is drawn on the ground - the ore's sign on each cell
        /// still holding it (an emptied cell is no longer drawn).</summary>
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || known.Count == 0) return;
            foreach (object o in Find.Selector.SelectedObjects)
            {
                if (!(o is Pawn p) || !known.TryGetValue(p.thingIDNumber, out var set)) continue;
                foreach (IntVec3 c in set)
                {
                    ThingDef d = map.deepResourceGrid.ThingDefAt(c);
                    if (d == null || map.deepResourceGrid.CountAt(c) <= 0 || d.uiIcon == null) continue;
                    if (!mats.TryGetValue(d, out Material m)) mats[d] = m = MaterialPool.MatFrom(new MaterialRequest(d.uiIcon, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, 0.7f)));
                    Vector3 pos = c.ToVector3Shifted(); pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                    Graphics.DrawMesh(MeshPool.plane08, pos, Quaternion.identity, m, 0);
                }
            }
        }
    }
}
