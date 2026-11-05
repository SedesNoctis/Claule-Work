using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Sense (Магия-правила.md, "Чутьё"): each element feels by what it is. Everything is counted once, at the
    /// cast, and only marked afterwards - nothing is searched tick by tick.
    ///   Fire - warmth: every warm body, the machines too (they run hot).
    ///   Water - blood: every living body (not machines), and what hides inside it: hidden sicknesses come to light, and a
    ///           metalhorror inside a body is found out.
    ///   Earth (and magma) - the deep: the ore deep below shows for some hours; a strong cast draws new ore up from the
    ///           depths - the stronger, the more and the dearer; ground once drawn on rests a while.
    ///   Air - every sound (as before).</summary>
    public static class SpellSense
    {
        public enum By { Warmth, Blood, Ground, Sound }

        public static void Feel(Pawn caster, Map map, float radius, float power, By by)
        {
            if (caster == null || map == null) return;
            IntVec3 at = caster.Position;
            var found = map.mapPawns.AllPawnsSpawned.Where(p => p != caster && !p.Dead && p.Position.DistanceTo(at) <= radius && Feels(p, by)).ToList();
            map.GetComponent<MapComponent_FireMagic>()?.AddSense(found, Mathf.RoundToInt(1800f * power));
            int foes = found.Count(p => p.HostileTo(caster));
            string what = by == By.Warmth ? "warm bodies" : by == By.Blood ? "living bodies" : by == By.Ground ? "steps on the ground" : "breathing shapes";
            string more = "";
            if (by == By.Blood) more = Blood(found, caster);
            Messages.Message(caster.LabelShortCap + " feels " + found.Count + " " + what + " around" + (foes > 0 ? " - " + foes + " of them hostile" : "") + "." + more, caster, MessageTypeDefOf.NeutralEvent, false);
        }

        private static bool Feels(Pawn p, By by)
        {
            switch (by)
            {
                case By.Warmth: return p.RaceProps.IsFlesh || p.RaceProps.IsMechanoid;   // a machine runs hot
                case By.Blood: return p.RaceProps.IsFlesh;                                 // no blood in a machine
                default: return true;
            }
        }

        /// <summary>What the blood tells: hidden sicknesses come to light; a metalhorror inside a body is found out.</summary>
        private static string Blood(List<Pawn> found, Pawn caster)
        {
            int sick = 0, horrors = 0;
            foreach (Pawn p in found)
            {
                foreach (Hediff h in p.health.hediffSet.hediffs.ToList())
                {
                    HediffComp_Discoverable d = h.TryGetComp<HediffComp_Discoverable>();
                    if (d == null || Discovered(d)) continue;
                    Discover(d); sick++;
                }
                try
                {
                    if (ModsConfig.AnomalyActive && MetalhorrorUtility.IsInfected(p))
                    {
                        MetalhorrorUtility.Detect(p, "A caster's sense of the blood felt something of metal moving inside " + p.LabelShort + ".", null, 1f);   // plain words: shown as they are whether or not the game translates them
                        horrors++;
                    }
                }
                catch (System.Exception e) { Log.WarningOnce("[Avir] water sense - metalhorror: " + e.Message, 0x5E45E1); }
            }
            return (sick > 0 ? " Hidden sickness found in " + sick + "." : "") + (horrors > 0 ? " Something of metal moves in the blood of " + horrors + "!" : "");
        }

        private static readonly AccessTools.FieldRef<HediffComp_Discoverable, bool> discovered = AccessTools.FieldRefAccess<HediffComp_Discoverable, bool>("discovered");
        private static bool Discovered(HediffComp_Discoverable d) => discovered(d);
        private static void Discover(HediffComp_Discoverable d) { discovered(d) = true; d.Pawn?.health?.Notify_HediffChanged(d.parent); }

        // ------------------------------------------------------------------ the deep

        /// <summary>Earth and magma: the ore deep below shows on the map for a while (as under a ground-penetrating
        /// scanner); a strong cast also draws new ore up from the depths near the caster - more of it and dearer the
        /// stronger it is. The ground it was drawn from rests for a day.</summary>
        public static void Deep(Pawn caster, Map map, float radius, float power)
        {
            if (caster == null || map?.deepResourceGrid == null) return;
            var c = map.GetComponent<MapComponent_DeepSense>();
            if (c == null) return;
            c.showUntil = Mathf.Max(c.showUntil, Find.TickManager.TicksGame + Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power));
            IntVec3 at = caster.Position;
            int known = GenRadial.RadialCellsAround(at, Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f), true)
                .Count(x => x.InBounds(map) && map.deepResourceGrid.ThingDefAt(x) != null && map.deepResourceGrid.CountAt(x) > 0);
            string raised = power >= DrawFrom ? Raise(caster, map, at, radius, power, c) : "";
            Messages.Message("The ground answers " + caster.LabelShortCap + ": " + (known > 0 ? known + " cells with ore deep below round about" : "nothing deep below round about") + "." + raised
                + " The deep shows for " + (power * 4f).ToString("0.#") + " h.", new TargetInfo(at, map), MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>The strength from which a cast draws new ore up.</summary>
        public const float DrawFrom = 1.5f;

        private static string Raise(Pawn caster, Map map, IntVec3 at, float radius, float power, MapComponent_DeepSense c)
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

    /// <summary>The deep shown on the map for a while, and the ground that rests after giving up its ore.</summary>
    public class MapComponent_DeepSense : MapComponent
    {
        public class Rest : IExposable
        {
            public IntVec3 at; public int until;
            public void ExposeData() { Scribe_Values.Look(ref at, "at"); Scribe_Values.Look(ref until, "until"); }
        }

        public int showUntil;
        public List<Rest> rested = new List<Rest>();

        public MapComponent_DeepSense(Map map) : base(map) { }

        public bool Showing => Find.TickManager.TicksGame < showUntil;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref showUntil, "showUntil");
            Scribe_Collections.Look(ref rested, "rested", LookMode.Deep);
            if (rested == null) rested = new List<Rest>();
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 == 0) rested.RemoveAll(r => r.until <= Find.TickManager.TicksGame);
        }
    }

    /// <summary>While the deep is sensed, the map shows what lies below, as under a ground-penetrating scanner.</summary>
    [HarmonyPatch(typeof(DeepResourceGrid), nameof(DeepResourceGrid.AnyActiveDeepScannersOnMap))]
    public static class Patch_DeepSenseShows
    {
        private static readonly AccessTools.FieldRef<DeepResourceGrid, Map> mapOf = AccessTools.FieldRefAccess<DeepResourceGrid, Map>("map");

        public static void Postfix(DeepResourceGrid __instance, ref bool __result)
        {
            if (__result) return;
            Map m = mapOf(__instance);
            if (m?.GetComponent<MapComponent_DeepSense>()?.Showing == true) __result = true;
        }
    }
}
