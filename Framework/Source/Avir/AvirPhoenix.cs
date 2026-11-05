using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir
{
    [DefOf]
    public static class AvirPhoenixDefOf
    {
        public static ThingDef Avir_PhoenixEssence;
        public static HediffDef Avir_PhoenixEssenceHediff;
        public static HediffDef Avir_PhoenixRebirth;

        public static ThoughtDef Avir_PhoenixGrace;
        public static ThoughtDef Avir_PhoenixAgony;
        public static ThoughtDef Avir_PhoenixRose;
        public static ThoughtDef Avir_PhoenixFriendBurned;
        public static ThoughtDef Avir_PhoenixFriendRose;

        static AvirPhoenixDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirPhoenixDefOf));
        }
    }

    /// <summary>
    /// Marker for anything the fire must not consume. Attach it to a HediffDef and the Phoenix
    /// will leave that implant alone:
    ///
    ///   &lt;HediffDef&gt;
    ///     &lt;defName&gt;SomeImplant&lt;/defName&gt;
    ///     &lt;modExtensions&gt;
    ///       &lt;li Class="Avir.PhoenixProof" /&gt;
    ///     &lt;/modExtensions&gt;
    ///   &lt;/HediffDef&gt;
    ///
    /// Written as an extension rather than a hardcoded list so third-party implants can be
    /// spared by a one-line XML patch instead of a rebuild. Avir implants and anything
    /// archotech-tier are spared automatically - see AvirPhoenixUtil.SurvivesTheFire.
    /// </summary>
    public class PhoenixProof : DefModExtension
    {
    }

    /// <summary>The opposite: burns in the Phoenix fire although it is the Avir's own (a metal beak).</summary>
    public class PhoenixBurns : DefModExtension
    {
    }

    public static class AvirPhoenixUtil
    {
        /// <summary>
        /// Decides whether an implanted thing survives the burn.
        ///
        /// CONSIDERED AT ALL: Hediff_AddedPart (bionics, prosthetics) and Hediff_Implant
        /// (joywires, painstoppers, tattoos). Anything that is neither is never looked at -
        /// which is the case that covers RJW's biological parts. They are flesh, the fire has
        /// no quarrel with flesh, and nothing needs whitelisting for them.
        ///
        /// SPARED:
        ///   - psylink, always. It derives from Hediff_Implant, so a blanket implant burn eats
        ///     it, and Patch_AvirPsycasts hands Matriarchs and Priestesses up to eighteen levels
        ///     of it. Burning that out would silently gut the senior castes. Matched by class
        ///     AND by name so a mod that reimplements psylink still gets through.
        ///   - anything carrying the PhoenixProof extension (the manual escape hatch)
        ///   - anything whose replaced-part item is archotech tier
        ///   - anything defined by this mod (defName starts with "Avir_")
        ///
        /// EXPLICITLY NOT SPARED: magical tattoos. Patch_AvirBionics.IsExempt lets them through
        /// the generation lock because they are ink rather than machinery - but the Phoenix is
        /// not enforcing a cultural rule about machinery, it is a fire that starts at the heart
        /// and works outward, and ink on skin is exactly the sort of thing that does not survive
        /// that. The two exemption lists are deliberately different and must not be merged.
        /// </summary>
        public static bool SurvivesTheFire(Hediff h)
        {
            if (h == null || h.def == null) return true;
            if (!(h is Hediff_AddedPart) && !(h is Hediff_Implant)) return true;

            // Data-driven, from Defs/MiscDefs/ImplantExemptions_Avir.xml. Note that this reads
            // the PhoenixFire list, which is deliberately SHORTER than the GenerationLock one:
            // psychic links survive the fire, mechlinks and tattoos do not. The two lists are
            // different rules and merging them would be a bug in both directions.
            if (AvirImplantExemption.ExemptFromFire(h.def)) return true;

            if (h.def.HasModExtension<PhoenixBurns>()) return false;
            if (h.def.HasModExtension<PhoenixProof>()) return true;
            if (h.def.defName != null && h.def.defName.StartsWith("Avir_")) return true;

            ThingDef part = h.def.spawnThingOnRemoved;
            if (part != null && part.techLevel >= TechLevel.Archotech) return true;
            if (h.def.defName != null && h.def.defName.ToLowerInvariant().Contains("archotech")) return true;

            return false;
        }

        /// <summary>Every added part the fire destroys, newest first so removal is stable.</summary>
        public static List<Hediff> DoomedImplants(Pawn pawn)
        {
            List<Hediff> doomed = new List<Hediff>();
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null) return doomed;

            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (!SurvivesTheFire(all[i])) doomed.Add(all[i]);
            }
            return doomed;
        }

        public static bool HasEssence(Pawn pawn)
        {
            return pawn != null && pawn.health != null && pawn.health.hediffSet != null
                && pawn.health.hediffSet.HasHediff(AvirPhoenixDefOf.Avir_PhoenixEssenceHediff);
        }

        public static bool HasRebirth(Pawn pawn)
        {
            return pawn != null && pawn.health != null && pawn.health.hediffSet != null
                && pawn.health.hediffSet.HasHediff(AvirPhoenixDefOf.Avir_PhoenixRebirth);
        }

        /// <summary>True while the pawn is under the Phoenix in any form - burning down through
        /// the stages, or freshly risen. This is the flag every patch in
        /// Patch_AvirPhoenixDeath keys off.</summary>
        public static bool UnderThePhoenix(Pawn pawn)
        {
            return HasEssence(pawn) || HasRebirth(pawn);
        }

        /// <summary>
        /// Every free colonist who could notice, across all maps and all player caravans.
        ///
        /// REPLACES PawnsFinder.AllMapsCaravansAndTravelingTransporters_Alive_FreeColonists,
        /// which does not exist (CS0117). PawnsFinder's members have been renamed and reshuffled
        /// across RimWorld versions, so this walks Find.Maps and Find.WorldObjects.Caravans
        /// directly instead - MapPawns.FreeColonists, Caravan.IsPlayerControlled and
        /// Pawn.IsFreeColonist are all long-stable API and will not move under us again.
        /// </summary>
        internal static List<Pawn> Onlookers()
        {
            List<Pawn> result = new List<Pawn>();

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].mapPawns == null) continue;
                List<Pawn> colonists = maps[i].mapPawns.FreeColonists;
                for (int j = 0; j < colonists.Count; j++) result.Add(colonists[j]);
            }

            if (Find.WorldObjects != null)
            {
                List<Caravan> caravans = Find.WorldObjects.Caravans;
                for (int i = 0; i < caravans.Count; i++)
                {
                    if (!caravans[i].IsPlayerControlled) continue;
                    List<Pawn> ps = caravans[i].PawnsListForReading;
                    for (int j = 0; j < ps.Count; j++)
                    {
                        if (ps[j] != null && !ps[j].Dead && ps[j].IsFreeColonist) result.Add(ps[j]);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Every pawn anywhere that could be holding a psychic bond - maps, caravans, and the
        /// world pawn pool. Deliberately wider than Onlookers(): a bond partner is very often
        /// not a colonist and may not be on any map at all.
        /// </summary>
        internal static List<Pawn> AllBondCandidates()
        {
            List<Pawn> result = new List<Pawn>();

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].mapPawns == null) continue;
                List<Pawn> ps = maps[i].mapPawns.AllPawns;
                for (int j = 0; j < ps.Count; j++) if (ps[j] != null) result.Add(ps[j]);
            }

            if (Find.WorldObjects != null)
            {
                List<Caravan> caravans = Find.WorldObjects.Caravans;
                for (int i = 0; i < caravans.Count; i++)
                {
                    List<Pawn> ps = caravans[i].PawnsListForReading;
                    for (int j = 0; j < ps.Count; j++) if (ps[j] != null) result.Add(ps[j]);
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (p != null) result.Add(p);
                }
            }

            return result;
        }
    }
}
