using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// MATERIALS ARRIVE AT THE BUILDING SITE ON THEIR OWN.
    ///
    /// THE HOLE THIS FILLS. Telekinesis intercepted three paths - hauling to a stockpile, feeding
    /// a workbench, and the mod's own hauling job - and construction was not one of them. Not by
    /// decision; it simply was never written. So Maximus's builder walked for every plank, and the
    /// times it looked like the amulet was working were ordinary stockpile hauls that happened to
    /// pass the site. "Sometimes it does it, sometimes it walks" was that coincidence, nothing more.
    ///
    /// THE INTENT, IN HIS WORDS: the pawn pulls what is inside the bubble to itself and applies it
    /// if the target is inside too, instead of making dozens of trips. That is exactly the rule the
    /// other intercepts use - both ends inside the radius - and he confirmed that rule is right,
    /// so it is kept here rather than loosened.
    ///
    /// BOUND BY REFLECTION, NOT BY A REMEMBERED NAME. The construction delivery method is vanilla
    /// internals and this project has a standing rule against naming those from memory - a rule
    /// that cost three dead systems the last time it was broken. So the method is looked up, and
    /// if the lookup fails the log says exactly what WAS found on the type instead of the patch
    /// quietly doing nothing forever.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirConstructDeliverTeleport
    {
        private const string WorkGiverTypeName = "RimWorld.WorkGiver_ConstructDeliverResources";
        private const string DeliverMethodName = "ResourceDeliverJobFor";

        static Patch_AvirConstructDeliverTeleport()
        {
            try
            {
                Type type = AccessTools.TypeByName(WorkGiverTypeName);
                if (type == null)
                {
                    Log.Warning("[Avir telekinesis] " + WorkGiverTypeName + " not found - building "
                        + "materials will always be carried by hand.");
                    return;
                }

                MethodInfo target = null;
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                         | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (m.Name == DeliverMethodName && m.ReturnType == typeof(Job))
                    {
                        target = m;
                        break;
                    }
                }

                if (target == null)
                {
                    Log.Warning("[Avir telekinesis] no Job-returning " + DeliverMethodName + " on "
                        + WorkGiverTypeName + ". " + DescribeCandidates(type)
                        + " Building materials will be carried by hand; send me that list and this "
                        + "binds properly next pass.");
                    return;
                }

                new Harmony("Avir.ConstructDeliverTeleport").Patch(target,
                    postfix: new HarmonyMethod(typeof(Patch_AvirConstructDeliverTeleport),
                                               nameof(Postfix)));

                Log.Message("[Avir telekinesis] construction delivery bound to "
                    + WorkGiverTypeName + "." + target.Name + ".");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir telekinesis] could not bind construction delivery: " + e);
            }
        }

        private static string DescribeCandidates(Type type)
        {
            StringBuilder sb = new StringBuilder("Job-returning methods on the type: ");
            bool any = false;

            foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                     | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.ReturnType != typeof(Job)) continue;
                if (any) sb.Append(", ");
                sb.Append(m.Name);
                any = true;
            }

            if (!any) sb.Append("(none)");
            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>
        /// The delivery job hands a resource to a frame or blueprint. Vanilla builds it as a
        /// carry-to-container job: targetA is the resource, targetB is the thing under
        /// construction, and count is how much of the stack it means to take.
        ///
        /// UNLIKE THE STOCKPILE INTERCEPT, THE JOB IS NOT CANCELLED. Delivering to a frame is not
        /// just moving an item - the frame has to actually absorb the resource, and only the
        /// vanilla job knows how to do that. So the material is teleported to the pawn's feet and
        /// the pawn then completes the delivery normally from where it already stands. That is the
        /// behaviour Maximus described wanting: pull it to yourself, then apply it.
        /// </summary>
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (__result == null || pawn == null || pawn.Map == null) return;

                Thing resource = __result.targetA.Thing;
                if (resource == null || !resource.Spawned || resource.Map != pawn.Map) return;

                Thing construct = __result.targetB.Thing;
                if (construct == null || !construct.Spawned) return;

                if (!AvirTelekinesisUtility.TryGetRadius(pawn, out float radius)) return;

                // In flight already - see MapComponent_AvirTelekinesisCache.IsAwaitingTeleport.
                if (MapComponent_AvirTelekinesisCache.IsAwaitingTeleport(resource)) return;

                // BOTH ENDS INSIDE THE BUBBLE, the same rule the other two intercepts use.
                // The bubble is centred on the pawn, so a plank across the map still gets walked
                // to - which is the intended limit, not an oversight.
                IntVec3 origin = pawn.Position;
                float radiusSq = radius * radius;

                if ((resource.Position - origin).LengthHorizontalSquared > radiusSq) return;
                if ((construct.Position - origin).LengthHorizontalSquared > radiusSq) return;

                // Already at hand - moving it would be a teleport to where it already is.
                if ((resource.Position - origin).LengthHorizontalSquared <= 4) return;

                MapComponent_AvirTelekinesisCache cache =
                    pawn.Map.GetComponent<MapComponent_AvirTelekinesisCache>();
                if (cache == null) return;

                int count = __result.count > 0 ? __result.count : -1;

                // Deferred, never synchronous - this postfix can be running inside vanilla's own
                // enumeration of candidate resources, and despawning one there is what crashed
                // hauling before. The cache drains the queue at a safe point.
                cache.EnqueueTeleportNearBench(resource, construct, count);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir telekinesis] construction delivery intercept failed: " + e,
                                0x4A52C1);
            }
        }
    }
}
