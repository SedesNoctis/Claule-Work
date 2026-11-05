using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // SUBSPACE STORAGE and SUBSPACE CONTROLLER (Apogee, concept 4.1-4.2).
    // The storage is one cell that takes far more than a cell should: things lie on it as usual
    // (every job, bill and building site can use them), 40 stacks, and more with every working stone
    // (95 / 150 / 200); they sink out of sight - only the disc and its count are drawn. Nothing rots
    // or deteriorates there, and the colony's wealth counts it at half.
    // The controller moves things within its reach into storage, and from the subspace storages
    // straight into building sites - limited kilograms per pass, charge for every kilogram.
    // =====================================================================================

    public class Building_SubspaceStorage : Building_Storage
    {
        /// <summary>Stacks by working stones: 0, 1, 2, 3.</summary>
        private static readonly int[] Stacks = { 40, 95, 150, 200 };
        public static readonly HashSet<Building_SubspaceStorage> All = new HashSet<Building_SubspaceStorage>();

        private CompArtifactHost Host => this.TryGetComp<CompArtifactHost>();

        public override int MaxItemsInCell =>
            Mathf.RoundToInt(Stacks[Mathf.Clamp(Host?.Working ?? 0, 0, 3)] * (Spawned && SpherePaths.OnMapCached(Map, SpherePaths.Subspace, 9) ? SphereSubspace.StorageFactor : 1f));

        public IEnumerable<Thing> Stored
        {
            get
            {
                if (!Spawned) yield break;
                foreach (Thing t in Position.GetThingList(Map))
                    if (t.def.category == ThingCategory.Item) yield return t;
            }
        }

        public int StoredStacks => Stored.Count();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            All.Add(this);
            map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);   // what lay here sinks out of sight
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            All.Remove(this);
            base.DeSpawn(mode);
            map?.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);   // and shows again
        }

        public static Building_SubspaceStorage At(Thing t)
        {
            if (All.Count == 0 || t == null || !t.Spawned) return null;
            return t.Position.GetFirstThing<Building_SubspaceStorage>(t.Map);
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("Subspace: ").Append(StoredStacks).Append(" / ").Append(MaxItemsInCell).Append(" stacks. Nothing rots here.");
            return sb.ToString();
        }

        /// <summary>What sank into it is not drawn - only how much of it there is.</summary>
        public override void DrawGUIOverlay()
        {
            base.DrawGUIOverlay();
            if (Find.CameraDriver.CurrentZoom != CameraZoomRange.Closest && Find.CameraDriver.CurrentZoom != CameraZoomRange.Close) return;
            int n = StoredStacks;
            if (n > 0) GenMapUI.DrawThingLabel(this, n + " / " + MaxItemsInCell, new Color(0.85f, 0.75f, 1f));
        }
    }

    /// <summary>Things lying in subspace storage are not printed into the map, nor labelled: dozens of stacks on one
    /// cell drew a heap of icons over and around it.</summary>
    [HarmonyPatch(typeof(SectionLayer_ThingsGeneral), "TakePrintFrom")]
    public static class Patch_SubspaceHideItems
    {
        public static bool Prefix(Thing t) =>
            Building_SubspaceStorage.All.Count == 0 || t.def.category != ThingCategory.Item || Building_SubspaceStorage.At(t) == null;
    }

    [HarmonyPatch]
    public static class Patch_SubspaceHideLabels
    {
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Thing), nameof(Thing.DrawGUIOverlay));
            var c = AccessTools.Method(typeof(ThingWithComps), nameof(ThingWithComps.DrawGUIOverlay));
            if (c != null && c.DeclaringType == typeof(ThingWithComps)) yield return c;
        }

        public static bool Prefix(Thing __instance) =>
            Building_SubspaceStorage.All.Count == 0 || __instance.def.category != ThingCategory.Item || Building_SubspaceStorage.At(__instance) == null;
    }

    /// <summary>Nothing rots in subspace storage.</summary>
    [HarmonyPatch]
    public static class Patch_SubspaceNoRot
    {
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var a = AccessTools.Method(typeof(CompRottable), "CompTickInterval");
            if (a != null) yield return a;
            var b = AccessTools.Method(typeof(CompRottable), "CompTickRare");
            if (b != null && b.DeclaringType == typeof(CompRottable)) yield return b;
        }

        public static bool Prefix(ThingComp __instance) => Building_SubspaceStorage.At(__instance.parent) == null;
    }

    /// <summary>The colony's wealth counts what lies in subspace at half.</summary>
    [HarmonyPatch(typeof(WealthWatcher), "CalculateWealthItems")]
    public static class Patch_SubspaceWealth
    {
        private static readonly AccessTools.FieldRef<WealthWatcher, Map> MapOf = AccessTools.FieldRefAccess<WealthWatcher, Map>("map");

        public static void Postfix(WealthWatcher __instance, ref float __result)
        {
            if (Building_SubspaceStorage.All.Count == 0) return;
            Map map = MapOf(__instance);
            float half = 0f;
            foreach (Building_SubspaceStorage s in Building_SubspaceStorage.All)
                if (s.Map == map)
                    foreach (Thing t in s.Stored) half += t.MarketValue * t.stackCount * 0.5f;
            __result = Mathf.Max(0f, __result - half);
        }
    }

    // ---------------------------------------------------------------- the controller

    public class CompProperties_SubspaceController : CompProperties
    {
        /// <summary>Reach by working stones: 1, 2, 3.</summary>
        public List<float> ranges = new List<float> { 8f, 14f, 22f };
        /// <summary>Kilograms moved per pass (every ~4 s) per working stone, at stone power 1.</summary>
        public float kgPerPassPerStone = 8f;
        /// <summary>Charge spent per kilogram moved.</summary>
        public float chargePerKg = 0.02f;
        public CompProperties_SubspaceController() { compClass = typeof(CompSubspaceController); }
    }

    public class CompSubspaceController : ThingComp, IArtifactHostEffect
    {
        private CompProperties_SubspaceController P => (CompProperties_SubspaceController)props;
        private CompArtifactHost Host => parent.TryGetComp<CompArtifactHost>();
        public float movedToday;

        public float Range
        {
            get
            {
                int n = Host?.Working ?? 0;
                return n <= 0 ? 0f : P.ranges[Mathf.Min(n, P.ranges.Count) - 1];
            }
        }

        public float Budget => P.kgPerPassPerStone * (Host?.Working ?? 0) * Mathf.Max(0.5f, Host?.Power ?? 1f);

        public override void CompTickRare()
        {
            base.CompTickRare();
            CompArtifactHost h = Host;
            if (!parent.Spawned || h == null || h.Working == 0 || parent.Faction != Faction.OfPlayer) return;
            float budget = Budget, start = budget;
            budget = SupplySites(budget);
            if (budget > 0.5f) budget = Gather(budget);
            float moved = start - budget;
            if (moved > 0f)
            {
                h.Spend(moved * P.chargePerKg);
                movedToday += moved;
            }
        }

        private bool InRange(IntVec3 c) => (c - parent.Position).LengthHorizontalSquared <= Range * Range;

        private static bool Free(Thing t, Map map) =>
            !t.IsForbidden(Faction.OfPlayer) && !map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer) && !map.physicalInteractionReservationManager.IsReserved(t)
            && !Ritualistics.RitualHold.Held(t);

        /// <summary>Building sites in reach get their materials straight from the subspace storages in reach.</summary>
        private float SupplySites(float budget)
        {
            Map map = parent.Map;
            foreach (Thing ft in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).ToList())
            {
                if (budget <= 0.5f) break;
                if (!(ft is Frame f) || f.Faction != Faction.OfPlayer || !InRange(f.Position)) continue;
                foreach (ThingDefCountClass need in f.TotalMaterialCost())
                {
                    int want = f.ThingCountNeeded(need.thingDef);
                    if (want <= 0) continue;
                    foreach (Building_SubspaceStorage s in Building_SubspaceStorage.All.Where(x => x.Map == map && InRange(x.Position)).ToList())
                    {
                        Thing src = s.Stored.FirstOrDefault(t => t.def == need.thingDef && Free(t, map));
                        if (src == null) continue;
                        float mass = Mathf.Max(0.001f, src.GetStatValue(StatDefOf.Mass));
                        int n = Mathf.Min(want, src.stackCount, Mathf.FloorToInt(budget / mass));
                        if (n <= 0) return budget;
                        Thing part = src.SplitOff(n);
                        if (!f.resourceContainer.TryAdd(part, true)) { GenPlace.TryPlaceThing(part, s.Position, map, ThingPlaceMode.Near); continue; }
                        budget -= n * mass;
                        want -= n;
                        Flash(s.Position, f.Position);
                        if (want <= 0 || budget <= 0.5f) break;
                    }
                }
            }
            return budget;
        }

        /// <summary>Loose things in reach go to the best storage for them (subspace or any stockpile).</summary>
        private float Gather(float budget)
        {
            Map map = parent.Map;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(parent.Position, map, Range, true).ToList())
            {
                if (budget <= 0.5f) break;
                if (t.def.category != ThingCategory.Item || !t.def.EverHaulable || !t.Spawned || !Free(t, map)) continue;
                StoragePriority cur = StoreUtility.CurrentStoragePriorityOf(t);
                if (!StoreUtility.TryFindBestBetterStoreCellFor(t, null, map, cur, Faction.OfPlayer, out IntVec3 dest, true)) continue;
                float mass = Mathf.Max(0.001f, t.GetStatValue(StatDefOf.Mass));
                int n = Mathf.Min(t.stackCount, Mathf.FloorToInt(budget / mass));
                if (n <= 0) continue;
                IntVec3 from = t.Position;
                Thing moving = n >= t.stackCount ? t : t.SplitOff(n);
                if (moving == t) t.DeSpawn();
                if (!GenPlace.TryPlaceThing(moving, dest, map, ThingPlaceMode.Direct) && !GenPlace.TryPlaceThing(moving, dest, map, ThingPlaceMode.Near))
                {
                    GenPlace.TryPlaceThing(moving, from, map, ThingPlaceMode.Near);
                    continue;
                }
                budget -= n * mass;
                Flash(from, dest);
            }
            return budget;
        }

        private void Flash(IntVec3 a, IntVec3 b)
        {
            Map map = parent.Map;
            FleckMaker.ThrowLightningGlow(a.ToVector3Shifted(), map, 0.6f);
            FleckMaker.ThrowLightningGlow(b.ToVector3Shifted(), map, 0.6f);
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (Range > 0f && Range < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(parent.Position, Range);
        }

        public string EffectLine() => (Host?.Working ?? 0) == 0
            ? "Asleep: no charged artifact. With stones it moves things within 8 / 14 / 22 cells into storage and feeds building sites from the subspace storages."
            : "Within " + Range.ToString("0") + " cells: feeds building sites from the subspace storages, then moves loose things to the best storage for them - "
              + Budget.ToString("0") + " kg every few seconds, " + P.chargePerKg.ToString("0.###") + " charge a kilogram.";

        public override string CompInspectStringExtra() => (Host?.Working ?? 0) == 0 ? "Asleep: no charged artifact." : "Reach " + Range.ToString("0") + " cells, " + Budget.ToString("0") + " kg a pass.";

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref movedToday, "movedToday");
        }
    }

    /// <summary>For the storage's inlay window.</summary>
    public class CompSubspaceStorageEffect : ThingComp, IArtifactHostEffect
    {
        public string EffectLine()
        {
            var s = parent as Building_SubspaceStorage;
            return s == null ? "" : "Holds " + s.MaxItemsInCell + " stacks on one cell (40, then 95 / 150 / 200 with one, two, three working stones), out of sight; the ships above can trade it as if it lay by a trade beacon. Nothing rots or deteriorates in it; wealth counts it at half. "
                                    + "A subspace controller feeds building sites from it.";
        }
    }
}

namespace Avir.Artifactorics
{
    /// <summary>What lies in subspace storage is in reach of the ships above as if it lay by an orbital trade beacon:
    /// offered for sale, and its silver pays for purchases. Only the storage's own cell counts, not its room.</summary>
    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.AllLaunchableThingsForTrade))]
    public static class Patch_SubspaceTradeable
    {
        public static void Postfix(Map map, ITrader trader, ref IEnumerable<Thing> __result)
        {
            if (Building_SubspaceStorage.All.Count == 0 || map == null) return;
            __result = WithSubspace(__result, map, trader);
        }

        private static IEnumerable<Thing> WithSubspace(IEnumerable<Thing> vanilla, Map map, ITrader trader)
        {
            var seen = new HashSet<Thing>();
            foreach (Thing t in vanilla) { seen.Add(t); yield return t; }
            foreach (Building_SubspaceStorage s in Building_SubspaceStorage.All.ToList())
            {
                if (s.Map != map || !s.Spawned) continue;
                foreach (Thing t in s.Stored.ToList())
                    if (!seen.Contains(t) && TradeUtility.PlayerSellableNow(t, trader)) { seen.Add(t); yield return t; }
            }
        }
    }
}
