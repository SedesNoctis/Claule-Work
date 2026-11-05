using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// The two boosts from the passive that actually belong to the MECH's own stat sheet, not
    /// the Architect's - Global Work Speed (+35%) and damage reduction, modeled as
    /// ArmorRating_Blunt/Sharp/Heat (+10%). Postfixes StatWorker.GetValue directly rather than
    /// adding a StatPart via XML patch, matching this mod's existing preference for Harmony
    /// over PatchOperation where a stat needs to look at a DIFFERENT pawn's hediffs (same
    /// reasoning as HediffComp_Phasing being used instead of a stat-based wall-pass trick).
    ///
    /// "All mechanoids" is read literally - any player-aligned mechanoid Pawn on a map where at
    /// least one spawned pawn carries Avir_MasterConstructor gets the boost, regardless of
    /// whether that specific Architect is its mechanitor. Cache is map-keyed and only
    /// refreshed once every 250 ticks per map, since this runs on every relevant stat request
    /// and a live pawn scan every single call would be wasteful.
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), typeof(StatRequest), typeof(bool))]
    public static class Patch_ArchstructrorMechBoosts
    {
        private const float WorkSpeedFactor = 1.35f;
        private const float ArmorFactor = 1.10f;

        private static HediffDef masterConstructorDef;
        private static bool lookedUp;

        private static readonly Dictionary<Map, CacheEntry> cache = new Dictionary<Map, CacheEntry>();

        private class CacheEntry
        {
            public bool active;
            public int checkedAtTick;
        }

        public static void Postfix(StatWorker __instance, StatRequest req, ref float __result)
        {
            try
            {
                StatDef stat = GetStat(__instance);
                if (stat == null) return;

                bool isWorkSpeed = stat == StatDefOf.WorkSpeedGlobal;
                bool isArmor = stat == StatDefOf.ArmorRating_Blunt || stat == StatDefOf.ArmorRating_Sharp || stat == StatDefOf.ArmorRating_Heat;
                if (!isWorkSpeed && !isArmor) return;

                if (!(req.Thing is Pawn mech)) return;
                if (mech.RaceProps == null || !mech.RaceProps.IsMechanoid) return;
                if (mech.Faction == null || mech.Faction != Faction.OfPlayer) return;
                if (!MapHasMasterConstructor(mech.Map)) return;

                __result *= isWorkSpeed ? WorkSpeedFactor : ArmorFactor;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Archstructror mech-boost patch failed: " + e, 0x4A51EA);
            }
        }

        // StatWorker.stat is private in vanilla - AccessTools field access, cached type-side
        // since it's the same field on every call.
        private static readonly AccessTools.FieldRef<StatWorker, StatDef> StatField =
            AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");

        private static StatDef GetStat(StatWorker worker)
        {
            try { return StatField(worker); }
            catch { return null; }
        }

        private static bool MapHasMasterConstructor(Map map)
        {
            if (map == null) return false;

            EnsureLookedUp();
            if (masterConstructorDef == null) return false;

            if (cache.TryGetValue(map, out CacheEntry entry) && Find.TickManager.TicksGame - entry.checkedAtTick < 250)
            {
                return entry.active;
            }

            bool found = false;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p.health?.hediffSet != null && p.health.hediffSet.HasHediff(masterConstructorDef))
                {
                    found = true;
                    break;
                }
            }

            cache[map] = new CacheEntry { active = found, checkedAtTick = Find.TickManager.TicksGame };
            return found;
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            masterConstructorDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_MasterConstructor");
        }
    }
}
