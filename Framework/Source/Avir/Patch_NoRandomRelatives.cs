using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// NO RANDOM RELATIVES OF MY COLONISTS. When the game makes a new pawn (a trader, a raider, a visitor, a
    /// wanderer) it may tie it by family to anyone alive or dead - and so every trade caravan brought a host of
    /// cousins and aunts of the colony. While playing, a new pawn is not tied to the colony's pawns, nor to their
    /// close family (up to four steps by parent and child, and spouses), so it cannot become their kin through
    /// them. Relations generated among the starting colonists stay; a quest that brings a relative on purpose
    /// stays. Everyone else's relations are vanilla. A setting (on by default).
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawnRelations")]
    public static class Patch_NoRandomRelatives
    {
        public static HashSet<Pawn> Excluded;

        public static void Prefix(Pawn pawn, ref PawnGenerationRequest request)
        {
            Excluded = null;
            if (!(AvirMod.Settings?.noRandomRelativesOfColonists ?? true)) return;
            if (Current.ProgramState != ProgramState.Playing || Faction.OfPlayerSilentFail == null) return;
            if (request.ExtraPawnForExtraRelationChance != null && request.ExtraPawnForExtraRelationChance.Faction == Faction.OfPlayer) return;   // a relative on purpose
            Excluded = ColonyKin();
        }

        public static void Finalizer() => Excluded = null;

        private static HashSet<Pawn> ColonyKin()
        {
            var seen = new HashSet<Pawn>();
            var frontier = new List<Pawn>();
            foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
                if (p.Faction == Faction.OfPlayer && p.RaceProps.Humanlike && seen.Add(p)) frontier.Add(p);
            for (int depth = 0; depth < 4 && frontier.Count > 0; depth++)
            {
                var next = new List<Pawn>();
                foreach (Pawn p in frontier)
                {
                    if (p.relations == null) continue;
                    foreach (DirectPawnRelation r in p.relations.DirectRelations)
                        if (r.otherPawn != null && (r.def == PawnRelationDefOf.Parent || r.def == PawnRelationDefOf.Spouse) && seen.Add(r.otherPawn)) next.Add(r.otherPawn);
                    foreach (Pawn child in p.relations.Children)
                        if (child != null && seen.Add(child)) next.Add(child);
                }
                frontier = next;
            }
            return seen;
        }
    }

    /// <summary>Every candidate kin of a new pawn passes through here: the colony's kin are taken out.</summary>
    [HarmonyPatch(typeof(PawnGenerator), "GenerateSamples")]
    public static class Patch_NoRandomRelativesSamples
    {
        public static void Prefix(ref Pawn[] pawns)
        {
            HashSet<Pawn> ex = Patch_NoRandomRelatives.Excluded;
            if (ex == null || pawns == null) return;
            pawns = pawns.Where(p => !ex.Contains(p)).ToArray();
        }
    }
}
