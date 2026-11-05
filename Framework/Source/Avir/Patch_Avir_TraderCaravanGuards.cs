using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Vanilla's PawnGroupMaker.guards is a weighted pool spent against the caravan's points
    /// budget - it can bias toward certain pawnkinds, but it can never guarantee "2-3 of X and
    /// 4-6 of Y every time." To get a fixed, curated escort composition on every trade caravan
    /// regardless of points, this patches the same chokepoint GenerateCarriers already hooks
    /// into and adds a deterministic (within-range) roster directly to the output list, on top
    /// of whatever the normal trader/carrier generation already produced.
    ///
    /// Covers both Avir factions: A'Shaign (Avir_HighNesting) and A'Schialis
    /// (Avir_AShialis). A'Schialis previously had no top-up here and relied solely on its
    /// faction def's weighted &lt;guards&gt; pool, which - same as any vanilla weighted pool -
    /// can roll to little or nothing depending on the caravan's points budget. That's why
    /// their caravans could show up unescorted while A'Shaign's didn't.
    ///
    /// Ranges roll independently and additively - e.g. Huntress and YoungHunter don't compete
    /// for a shared budget, each is its own Rand.RangeInclusive(min, max) roll. Then the whole
    /// caravan is cut to the settings' cap (10 Avir by default, the trader included): the
    /// pool's random guards go first, then the curated roster from its end. 10-20 Avir at the
    /// gate were a death sentence for any colony.
    /// </summary>
    [HarmonyPatch(typeof(PawnGroupKindWorker_Trader), "GeneratePawns",
        new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(List<Pawn>), typeof(bool) })]
    public static class Patch_Avir_TraderCaravanGuards
    {
        public static void Postfix(PawnGroupMakerParms parms, List<Pawn> outPawns)
        {
            if (outPawns == null)
            {
                return;
            }

            int before = outPawns.Count;
            Top(parms, outPawns);
            if (parms.faction?.def == AvirDefOf.Avir_HighNesting || parms.faction?.def == AvirDefOf.Avir_AShialis)
                Cap(outPawns, before, AvirMod.Settings?.caravanMax ?? 10);
        }

        /// <summary>No more than cap humanlike pawns: the trader stays; the pool's guards (before index `curated`)
        /// leave first, then the curated escort from its end.</summary>
        private static void Cap(List<Pawn> outPawns, int curated, int cap)
        {
            int people = 0;
            foreach (Pawn p in outPawns) if (p.RaceProps.Humanlike) people++;
            for (int pass = 0; pass < 2 && people > cap; pass++)
                for (int i = pass == 0 ? curated - 1 : outPawns.Count - 1; i >= (pass == 0 ? 0 : curated) && people > cap; i--)
                {
                    Pawn p = outPawns[i];
                    if (!p.RaceProps.Humanlike || p.kindDef.trader || (p.TraderKind != null)) continue;
                    outPawns.RemoveAt(i);
                    if (i < curated) curated--;
                    people--;
                    if (!p.Destroyed) Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                }
        }

        private static void Top(PawnGroupMakerParms parms, List<Pawn> outPawns)
        {
            if (parms.faction?.def == AvirDefOf.Avir_HighNesting)
            {
                AddGuards(outPawns, AvirDefOf.Avir_Strider, 2, 3, parms, mustBeCapableOfViolence: true);
                AddGuards(outPawns, AvirDefOf.Avir_Huntress, 4, 6, parms, mustBeCapableOfViolence: true);
                AddGuards(outPawns, AvirDefOf.Avir_Priestess, 0, 1, parms, mustBeCapableOfViolence: true); // "perhaps, sometimes" - min 0 already covers absence
                AddGuards(outPawns, AvirDefOf.Avir_YoungHunter, 5, 8, parms, mustBeCapableOfViolence: true);
                AddGuards(outPawns, AvirDefOf.Avir_Predator, 1, 2, parms, mustBeCapableOfViolence: true);
                AddGuards(outPawns, AvirDefOf.Avir_Artificer, 1, 4, parms, mustBeCapableOfViolence: false); // isFighter=false - support role, not combat
                return;
            }

            // A'Schialis Legion caravans (Avir_Centurion/Legionnaire/Inquisitor only exist
            // when Vanilla Memes Expanded is active - see AvirDefOf). Same deterministic
            // top-up as the A'Shaign, but Huntress-led per request: the Legion's own
            // pawnGroupMakers already lean on huntresses for their war-cohorts, so the
            // escort mirrors that instead of the A'Shaign's Strider/YoungHunter-heavy mix.
            // Strider has no A'Schialis analog (see PawnKind_AvirLegion.xml notes), so it's
            // simply not added here rather than substituted.
            if (parms.faction?.def == AvirDefOf.Avir_AShialis)
            {
                AddGuards(outPawns, AvirDefOf.Avir_Huntress, 6, 9, parms, mustBeCapableOfViolence: true); // mostly huntresses, per request
                AddGuards(outPawns, AvirDefOf.Avir_Centurion, 2, 4, parms, mustBeCapableOfViolence: true);
                AddGuards(outPawns, AvirDefOf.Avir_Legionnaire, 0, 2, parms, mustBeCapableOfViolence: true);
                AddGuards(outPawns, AvirDefOf.Avir_Inquisitor, 0, 1, parms, mustBeCapableOfViolence: true); // "perhaps, sometimes" - min 0 already covers absence
                AddGuards(outPawns, AvirDefOf.Avir_Artificer, 1, 4, parms, mustBeCapableOfViolence: false); // isFighter=false - support role, not combat
            }
        }

        private static void AddGuards(List<Pawn> outPawns, PawnKindDef kind, int min, int max,
            PawnGroupMakerParms parms, bool mustBeCapableOfViolence)
        {
            if (kind == null)
            {
                return;
            }

            int count = Rand.RangeInclusive(min, max);
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind,
                    parms.faction,
                    PawnGenerationContext.NonPlayer,
                    parms.tile,
                    mustBeCapableOfViolence: mustBeCapableOfViolence));
                outPawns.Add(pawn);
            }
        }
    }
}
