using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avir.Artifactorics;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Fire's smoke (the plan, part 50): fire spells raise smoke, and fire wizards and sorcerers (fire their main
    /// element, given by the path ritual), whoever wears a golden amulet (the amulet of fire), and whoever is under a ward
    /// of fire, magma or steam breathe it as if it were not there. Under Combat Extended it is CE's own black smoke,
    /// with its own rules (it thickens, drifts, chokes); without CE - the game's blinding smoke.</summary>
    public static class FireSmoke
    {
        private static ThingDef ceSmoke;
        private static bool looked;
        private static MethodInfo densityBy;

        private static ThingDef CE
        {
            get
            {
                if (!looked) { looked = true; ceSmoke = DefDatabase<ThingDef>.GetNamedSilentFail("Gas_BlackSmoke"); densityBy = ceSmoke != null ? AccessTools.Method(ceSmoke.thingClass, "UpdateDensityBy") : null; }
                return ceSmoke;
            }
        }

        /// <summary>A puff of smoke on a cell: thicker the more is put in (CE: density, up to its own ceiling).</summary>
        public static void Puff(IntVec3 c, Map map, float amount = 1f)
        {
            if (map == null || !c.InBounds(map) || c.Filled(map)) return;
            if (CE != null && densityBy != null)
            {
                Thing smoke = c.GetThingList(map).FirstOrDefault(t => t.def == ceSmoke) ?? GenSpawn.Spawn(ceSmoke, c, map);
                densityBy.Invoke(smoke, new object[] { 3000f * amount });
                return;
            }
            GasUtility.AddGas(c, map, GasType.BlindSmoke, Mathf.RoundToInt(40f * amount));
        }

        private static readonly Dictionary<int, KeyValuePair<int, bool>> known = new Dictionary<int, KeyValuePair<int, bool>>();

        /// <summary>Of fire: fire their main element, a golden amulet, or a fiery ward. The first two are asked often and
        /// change rarely, so they are kept for an in-game hour per pawn; the ward is looked at each time.</summary>
        public static bool Ignores(Pawn p)
        {
            if (p == null) return false;
            if (SpellWards.On(p) is Hediff_SpellWard w && w.Fiery) return true;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (known.TryGetValue(p.thingIDNumber, out var kv) && now - kv.Key < GenDate.TicksPerHour) return kv.Value;
            bool yes = SpherePaths.Element(p) == "Gold";
            Hediff_ArcaneCore core = Mages.Core(p);
            if (!yes && core != null) yes = FireMagic.Is(core.path);
            if (known.Count > 2000) known.Clear();
            known[p.thingIDNumber] = new KeyValuePair<int, bool>(now, yes);
            return yes;
        }
    }

    /// <summary>CE's smoke sensitivity: none at all for those fire smoke does not touch.</summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class Patch_FireSmokeSensitivity
    {
        private static StatDef smoke;
        private static bool looked;
        private static readonly AccessTools.FieldRef<StatWorker, StatDef> statOf = AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");

        public static void Postfix(StatWorker __instance, StatRequest req, ref float __result)
        {
            if (!looked) { looked = true; smoke = DefDatabase<StatDef>.GetNamedSilentFail("SmokeSensitivity"); }
            if (smoke == null || statOf(__instance) != smoke || !(req.Thing is Pawn p) || !AvirMagic.On) return;
            if (FireSmoke.Ignores(p)) __result = 0f;
        }
    }

    /// <summary>What air and water clear away. Air: gas off the ground (smoke - CE's too - toxic gas, any cloud) and out
    /// of the body (toxic buildup, CE's smoke inhalation, any gas exposure). Water: anaesthetic and poisons.</summary>
    public static class Cleansing
    {
        /// <summary>The air blows the cells clear of every gas, the game's and things of gas alike.</summary>
        public static void ClearGas(List<IntVec3> cells, Map map)
        {
            foreach (IntVec3 c in cells)
            {
                if (!c.InBounds(map)) continue;
                map.gasGrid.SetDirect(c, 0, 0, 0, 0);
                foreach (Thing g in c.GetThingList(map).OfType<Gas>().ToList()) g.Destroy();
            }
            if (cells.Count > 0) FleckMaker.ThrowDustPuffThick(cells[cells.Count / 2].ToVector3Shifted(), map, 2f, MagicFx.AirDust);
        }

        private static bool Airy(HediffDef d) => d == HediffDefOf.ToxicBuildup || d.defName == "SmokeInhalation" || d.defName == "ToxGasExposure" || d.defName.Contains("GasExposure");
        private static bool Watery(HediffDef d) => d == HediffDefOf.Anesthetic || d == HediffDefOf.FoodPoisoning || d.defName.Contains("Poison") || d.defName.Contains("Venom");

        /// <summary>Breathed out of the body: every buildup of gas eases by a share.</summary>
        public static void AirOut(Pawn p, float amount) => Ease(p, Airy, amount);

        /// <summary>Washed out of the body: anaesthetic, poisons.</summary>
        public static void WashOut(Pawn p, float amount) => Ease(p, Watery, amount);

        private static void Ease(Pawn p, System.Func<HediffDef, bool> which, float amount)
        {
            foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => which(x.def)).ToList())
            {
                h.Severity -= amount;
                if (h.Severity <= 0.001f) p.health.RemoveHediff(h);
            }
        }
    }

    /// <summary>Blindness from magic takes sight away by a share, but never below 30%: only Light may take it all.</summary>
    [HarmonyPatch(typeof(PawnCapacityUtility), nameof(PawnCapacityUtility.CalculateCapacityLevel))]
    public static class Patch_MagicSightFloor
    {
        public const float Floor = 0.3f;

        public static void Postfix(HediffSet diffSet, PawnCapacityDef capacity, ref float __result)
        {
            if (capacity != PawnCapacityDefOf.Sight || diffSet == null || __result >= Floor) return;
            float taken = 0f;
            foreach (Hediff h in diffSet.hediffs)
            {
                if (h.def != ComboDefOf.Avir_Dazzled && h.def != ComboDefOf.Avir_Dusted) continue;
                var mods = h.CurStage?.capMods;
                if (mods != null) foreach (PawnCapacityModifier m in mods) if (m.capacity == PawnCapacityDefOf.Sight) taken -= m.offset;
            }
            if (taken <= 0f) return;
            // what the eyes would see without the magic, and no lower than the floor of it
            __result = Mathf.Max(__result, Mathf.Min(__result + taken, Floor));
        }
    }
}
