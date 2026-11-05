using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE ESSENCE GATHERER: a chest strap with an essence crystal at its heart. Whatever its wearer
    /// kills - by any blow, or by the siphon - gives up one essence extract on the spot, and by the
    /// strap's quality a chance of a second. Only living flesh has essence to give.
    ///
    /// This replaces the siphon's old free bottle (which was a bug): the extract now comes from the
    /// artifact, not from the act.
    /// </summary>
    public class CompProperties_EssenceGatherer : CompProperties
    {
        public CompProperties_EssenceGatherer() { compClass = typeof(CompEssenceGatherer); }
    }

    public class CompEssenceGatherer : ThingComp
    {
        /// <summary>Chance of a second extract, by quality.</summary>
        public float ExtraChance
        {
            get
            {
                if (!parent.TryGetQuality(out QualityCategory q)) return 0.1f;
                switch (q)
                {
                    case QualityCategory.Awful: return 0f;
                    case QualityCategory.Poor: return 0.05f;
                    case QualityCategory.Normal: return 0.10f;
                    case QualityCategory.Good: return 0.20f;
                    case QualityCategory.Excellent: return 0.30f;
                    case QualityCategory.Masterwork: return 0.45f;
                    default: return 0.60f;   // legendary
                }
            }
        }

        public override string CompInspectStringExtra() =>
            "Extra extract chance: " + ExtraChance.ToStringPercent();
    }

    public static class AvirEssenceGatherer
    {
        private static ThingDef def;
        public static ThingDef Def => def ?? (def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_EssenceGatherer"));

        public static CompEssenceGatherer WornBy(Pawn p)
        {
            if (p?.apparel == null) return null;
            foreach (Apparel a in p.apparel.WornApparel)
            {
                var c = a.TryGetComp<CompEssenceGatherer>();
                if (c != null) return c;
            }
            return null;
        }

        /// <summary>Draws the extract out of a fresh kill, if the killer wears a gatherer.</summary>
        public static void OnKill(Pawn killer, Pawn victim, Map map, IntVec3 at)
        {
            if (killer == null || victim == null || killer == victim || map == null || !at.IsValid) return;
            if (!victim.RaceProps.IsFlesh) return;
            CompEssenceGatherer g = WornBy(killer);
            ThingDef extract = AvirEssenceExtractDefOf.Avir_EssenceExtract;
            if (g == null || extract == null) return;
            int n = 1 + (Rand.Chance(g.ExtraChance) ? 1 : 0);
            Thing t = ThingMaker.MakeThing(extract);
            t.stackCount = n;
            GenPlace.TryPlaceThing(t, at, map, ThingPlaceMode.Near);
        }

        /// <summary>The hunters of the two orders carry one.</summary>
        public static void Give(Pawn pawn)
        {
            if (Def == null || pawn?.apparel == null || pawn.kindDef?.defName != "Avir_Huntress") return;
            string f = pawn.Faction?.def?.defName;
            if (f != "Avir_HighNesting" && f != "Avir_AShialis") return;
            foreach (Apparel worn in pawn.apparel.WornApparel)
            {
                if (worn.def == Def) return;
                if (!ApparelUtility.CanWearTogether(Def, worn.def, pawn.RaceProps.body)) return;
            }
            ThingDef stuff = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_AvirFeatheredFur") ?? GenStuff.DefaultStuffFor(Def);
            var belt = (Apparel)ThingMaker.MakeThing(Def, stuff);
            belt.TryGetComp<CompQuality>()?.SetQuality(Rand.Chance(0.3f) ? QualityCategory.Good : QualityCategory.Normal, ArtGenerationContext.Outsider);
            pawn.apparel.Wear(belt, false);
        }
    }

    /// <summary>Any killing blow by a wearer (the siphon calls OnKill itself: it kills with no instigator).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_AvirEssenceGathererKill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                if (__instance == null || __instance.Dead || !dinfo.HasValue) return;
                if (!(dinfo.Value.Instigator is Pawn killer)) return;
                AvirEssenceGatherer.OnKill(killer, __instance, __instance.MapHeld, __instance.PositionHeld);
            }
            catch (Exception e) { Log.WarningOnce("[Avir] essence gatherer: " + e.Message, 0x4A51C5); }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirEssenceGathererGear
    {
        public static void Postfix(Pawn __result)
        {
            try
            {
                if (__result == null || !AvirFaction.IsAvirPawnKind(__result) || AvirFaction.IsPlayerAvir(__result)) return;
                AvirEssenceGatherer.Give(__result);
            }
            catch (Exception e) { Log.WarningOnce("[Avir] essence gatherer gear: " + e.Message, 0x4A51C6); }
        }
    }
}
