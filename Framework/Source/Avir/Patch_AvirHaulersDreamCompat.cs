using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    // Integration with "Hauler's Dream" (giwaffed.HaulersDream).
    //
    // HD intercepts hauling at PRODUCTION time via its own Harmony hooks - a mined chunk, a
    // harvested crop, a crafted product - and routes it straight into the working pawn's
    // inventory before it ever exists as a loose Thing on the ground. Our telekinetic hauling
    // only ever looks at loose ground things (map.listerHaulables), so it can never win a
    // straightforward priority fight against HD: there is no WorkGiver-level contest to win,
    // because HD's routing happens before any job is even assigned.
    //
    // HD funnels essentially all of that routing through two static per-pawn gates:
    //     static bool HaulersDream.YieldRouter.IsCandidate(Pawn p, bool overrideOptOut)
    //     static bool HaulersDream.YieldRouter.IsEligible(Pawn p)
    // If either says "no" for a given pawn, HD treats that pawn as outside its system entirely
    // for that check - items they produce or encounter behave exactly like vanilla and land on
    // the ground. We postfix both to force "no", which is the cleanest way to make the amulet's
    // owner invisible to HD's competing system rather than trying to out-prioritize it.
    //
    // FIX 2026-09-13 - WHY THIS IS NO LONGER UNCONDITIONAL.
    //
    // Forcing "no" on the mere presence of an amulet took the pawn out of HD ACROSS THE WHOLE
    // MAP, not just inside the bubble. HD doesn't only intercept production - it also upgrades
    // ordinary hauling jobs into "carry a dozen things at once". Inside the radius that loss is
    // invisible, because telekinesis is better than either. Outside it, the pawn fell through to
    // VANILLA hauling: one item per trip, across the entire map, forever. Maximus watched that
    // for 35 days straight before we found it.
    //
    // So the override now asks whether there is anything in telekinetic reach RIGHT NOW. If yes,
    // the amulet is the better tool and HD stands down. If the bubble is empty, the pawn is just
    // an ordinary hauler at that moment and gets HD's bulk hauling back.
    //
    // This works cleanly only because telekinesis is INSTANT: one item in radius and nine outside
    // does not strand the pawn on the slow path, it resolves the one instantly, the bubble goes
    // empty, and the very next check hands the other nine to HD. A walking implementation would
    // have oscillated here.
    //
    // FIX 2026-10-04 - "HAUL EVERYTHING NEARBY" CARRIED ONE ITEM AT A TIME, WITH EVERY AMULET.
    //
    // All nine amulets (telekinetic I-IV, silversong, goldensight, flameguard, shadowguard,
    // shadowstrider) carry Comp_Telekinesis, so all of them went through the gate above - and the
    // gate asked the wrong question. "Is any haulable thing in the bubble" is almost always yes
    // in a lived-in base (tier IV reaches 50 cells), but telekinesis can only finish a haul when
    // the STORAGE is in the bubble too (TryFindStorageCellInRadius), and never touches forbidden
    // or reserved things. So HD stood down for things telekinesis would never move, and HD's
    // right-click "haul everything nearby" (BulkHaul.BuildBulkJob -> IsEligible) fell back to a
    // single vanilla haul: one item, across the map, every trip.
    //
    // Two changes:
    //  1. The bubble test now asks what telekinesis really does: a thing it may haul AND a
    //     storage cell for it inside the radius. Same rule as WorkGiver_Avir_TelekineticHaul.
    //  2. Bulk hauling is decided per haul, not per pawn. While HD plans a bulk haul the gate
    //     does not interfere at all; the plan itself steps aside only when its first haul has
    //     both ends in the bubble - then the plain vanilla job is kept and teleported by
    //     Patch_AvirHaulToStorageTeleport, exactly as before. Anything farther is HD's.
    //
    // This is a SOFT dependency: if Hauler's Dream is not installed, YieldRouter doesn't exist,
    // both patches are silently skipped, and nothing else in Avir is affected.
    public static class Patch_AvirHaulersDreamCompat
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type yieldRouterType = AccessTools.TypeByName("HaulersDream.YieldRouter");
                if (yieldRouterType == null)
                {
                    // Hauler's Dream not installed - nothing to patch.
                    return;
                }

                bool patchedAny = false;

                MethodInfo isCandidate = AccessTools.Method(yieldRouterType, "IsCandidate", new[] { typeof(Pawn), typeof(bool) });
                if (isCandidate != null)
                {
                    harmony.Patch(isCandidate, postfix: new HarmonyMethod(typeof(Patch_AvirHaulersDreamCompat), nameof(IsCandidatePostfix)));
                    patchedAny = true;
                }

                MethodInfo isEligible = AccessTools.Method(yieldRouterType, "IsEligible", new[] { typeof(Pawn) });
                if (isEligible != null)
                {
                    harmony.Patch(isEligible, postfix: new HarmonyMethod(typeof(Patch_AvirHaulersDreamCompat), nameof(IsEligiblePostfix)));
                    patchedAny = true;
                }

                // BulkHaul.BuildBulkJob(Pawn, Thing, Job, bool, bool) - private; every bulk plan
                // (automatic, prioritised, "haul everything nearby") goes through it.
                Type bulkHaulType = AccessTools.TypeByName("HaulersDream.BulkHaul");
                MethodInfo buildBulk = bulkHaulType == null ? null : AccessTools.Method(bulkHaulType, "BuildBulkJob");
                if (buildBulk != null)
                {
                    harmony.Patch(buildBulk,
                        prefix: new HarmonyMethod(typeof(Patch_AvirHaulersDreamCompat), nameof(BuildBulkJobPrefix)),
                        finalizer: new HarmonyMethod(typeof(Patch_AvirHaulersDreamCompat), nameof(BuildBulkJobFinalizer)));
                }
                else
                {
                    Log.Warning("[Avir] Found Hauler's Dream but not BulkHaul.BuildBulkJob - amulet wearers may "
                                + "fall back to one-item hauls when anything telekinesis can move is nearby.");
                }

                if (!patchedAny)
                {
                    Log.Warning("[Avir] Found Hauler's Dream but neither YieldRouter.IsCandidate(Pawn,bool) nor "
                                + "YieldRouter.IsEligible(Pawn) matched - telekinesis amulets won't be prioritized "
                                + "over its hauling. Hauler's Dream may have changed its internals.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Failed to wire telekinesis amulet priority into Hauler's Dream: " + e);
            }
        }

        public static void IsCandidatePostfix(Pawn p, bool overrideOptOut, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            TryOverride(p, ref __result);
        }

        public static void IsEligiblePostfix(Pawn p, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            TryOverride(p, ref __result);
        }


        // ---- bulk haul: decided per haul ----

        [ThreadStatic] private static int planningBulk;

        public static bool BuildBulkJobPrefix(Pawn pawn, Thing primary, Job vanillaJob, ref Job __result, out bool __state)
        {
            __state = false;
            try
            {
                if (pawn != null && primary != null && primary.Spawned && vanillaJob != null
                    && vanillaJob.def == JobDefOf.HaulToCell && vanillaJob.targetB.Cell.IsValid
                    && !MapComponent_AvirTelekinesisCache.IsAwaitingTeleport(primary)
                    && AvirTelekinesisUtility.TryGetRadius(pawn, out float radius)
                    && AvirTelekinesisUtility.WithinRadius(pawn.Position, primary.Position, radius)
                    && AvirTelekinesisUtility.WithinRadius(pawn.Position, vanillaJob.targetB.Cell, radius))
                {
                    // Both ends in the bubble: no bulk plan, the plain job is teleported instantly.
                    __result = null;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Hauler's Dream bulk-haul check error: " + e, 0x41766973);
            }
            planningBulk++;
            __state = true;
            return true;
        }

        public static Exception BuildBulkJobFinalizer(Exception __exception, bool __state)
        {
            if (__state && planningBulk > 0) planningBulk--;
            return __exception;
        }

        // ---- "is there anything telekinesis would move right now" ----
        //
        // These gates sit on HD's hot path, so this must never be a full map scan per call.
        // Two things keep it cheap: the answer is cached per pawn for CacheTicks, and the scan
        // bails on the FIRST thing found in range rather than counting them.
        //
        // CacheTicks matches MapComponent_AvirTelekinesisCache's own refresh interval on
        // purpose - no point being fresher than the telekinesis scan the answer is about.
        private const int CacheTicks = 30;
        private const int MaxStorageChecks = 24;

        private struct ReachAnswer
        {
            public int tick;
            public bool value;
        }

        private static readonly Dictionary<int, ReachAnswer> reachCache = new Dictionary<int, ReachAnswer>();

        private static bool AnythingInReach(Pawn p, float radius)
        {
            if (p?.Map == null || !p.Spawned) return false;

            int now = Find.TickManager.TicksGame;
            if (reachCache.TryGetValue(p.thingIDNumber, out ReachAnswer cached)
                && now - cached.tick < CacheTicks)
            {
                return cached.value;
            }

            bool found = false;
            float radiusSq = radius * radius;
            IntVec3 origin = p.Position;
            int checkedInRange = 0;

            foreach (Thing t in p.Map.listerHaulables.ThingsPotentiallyNeedingHauling())
            {
                if (t == null || !t.Spawned) continue;

                // A thing already promised a teleport is mid-flight and belongs to nobody for
                // this tick - see MapComponent_AvirTelekinesisCache.IsAwaitingTeleport.
                if (MapComponent_AvirTelekinesisCache.IsAwaitingTeleport(t)) continue;

                if ((t.Position - origin).LengthHorizontalSquared > radiusSq) continue;

                // Only what telekinesis would really finish: allowed, unreserved, and with a
                // storage cell inside the bubble too. The storage search is the costly part, so
                // it is capped; a pawn standing in a junkyard just keeps HD (the safe side).
                if (++checkedInRange > MaxStorageChecks) break;
                if (!AvirTelekinesisUtility.CanHaul(p, t, p.thingIDNumber)) continue;
                if (AvirTelekinesisUtility.TryFindStorageCellInRadius(p, t, origin, radius, null, out _))
                {
                    found = true;
                    break;
                }
            }

            reachCache[p.thingIDNumber] = new ReachAnswer { tick = now, value = found };
            return found;
        }

        private static void TryOverride(Pawn p, ref bool __result)
        {
            // HD is planning a bulk haul: BuildBulkJobPrefix already decided for this haul.
            if (planningBulk > 0) return;
            try
            {
                if (AvirTelekinesisUtility.TryGetRadius(p, out float radius)
                    && AnythingInReach(p, radius))
                {
                    __result = false;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Hauler's Dream compat check error: " + e, 0x41766972);
            }
        }
    }
}
