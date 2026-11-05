using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics
{
    /// <summary>Right-click a loose artifact: "Inlay into ..." for each socketed garment or melee weapon the selected
    /// colonist has on, at the nearest artificer's spot. The colonist brings the piece, does the work itself and puts
    /// it back on - no waiting for the order to find an artisan. Right-click an artificer's spot: work on a waiting
    /// inlay or extraction order now.</summary>
    public class FloatMenuOptionProvider_ArtifactInlay : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn p = context?.FirstSelectedPawn;
            if (p == null || clickedThing == null || p.Map == null) yield break;
            MapComponent_ArtifactOrders orders = MapComponent_ArtifactOrders.For(p.Map);
            if (orders == null) yield break;

            if (clickedThing is Building_ArtificerSpot spotClicked)
            {
                foreach (ArtifactOrder o in orders.orders.Where(o => o.spot == spotClicked && o.stage == ArtifactOrderStage.Ready).ToList())
                {
                    ArtifactOrder oo = o;
                    string why = ArtifactOrderWork.CannotDo(p, oo);
                    yield return new FloatMenuOption("Work now: " + oo.Describe() + (why != null ? " (" + why + ")" : ""),
                        why != null ? null : (System.Action)(() => ArtifactOrderWork.Start(p, oo)));
                }
                // drawing out what the selected colonist wears or wields
                var carried = new List<Thing>();
                if (p.equipment?.Primary?.Socket() is CompArtifactSocket ws && ws.HasArtifact && !ws.Self) carried.Add(p.equipment.Primary);
                if (p.apparel != null) carried.AddRange(p.apparel.WornApparel.Where(a => a.Socket() is CompArtifactSocket s && s.HasArtifact && !s.Self));
                foreach (Thing g in carried)
                    foreach (FloatMenuOption o in ExtractOptions(p, g, spotClicked, orders)) yield return o;
                yield break;
            }

            // a loose item with an artifact in it: draw it out at the nearest artificer's spot
            if (clickedThing.Spawned && clickedThing.Socket() is CompArtifactSocket cs && cs.HasArtifact && !cs.Self)
            {
                Building_ArtificerSpot near = p.Map.listerBuildings.AllBuildingsColonistOfClass<Building_ArtificerSpot>()
                    .Where(sp => p.CanReach(sp, PathEndMode.InteractionCell, Danger.Some))
                    .OrderBy(sp => sp.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                if (near == null) { yield return new FloatMenuOption("Extract the artifact (no artificer's spot in reach)", null); yield break; }
                foreach (FloatMenuOption o in ExtractOptions(p, clickedThing, near, orders)) yield return o;
                yield break;
            }

            CompArtifact art = clickedThing.TryGetComp<CompArtifact>();
            if (art?.state == null || !clickedThing.Spawned) yield break;
            string label0 = "Inlay " + clickedThing.LabelShort;
            if (!art.HasHeart) { yield return new FloatMenuOption(label0 + " (it has no heart yet)", null); yield break; }
            if (orders.HasOrderFor(clickedThing)) { yield return new FloatMenuOption(label0 + " (already ordered)", null); yield break; }
            Building_ArtificerSpot spot = p.Map.listerBuildings.AllBuildingsColonistOfClass<Building_ArtificerSpot>()
                .Where(s => p.CanReach(s, PathEndMode.InteractionCell, Danger.Some))
                .OrderBy(s => s.Position.DistanceToSquared(p.Position)).FirstOrDefault();
            if (spot == null) { yield return new FloatMenuOption(label0 + " (no artificer's spot in reach)", null); yield break; }

            var pieces = new List<Thing>();
            if (p.equipment?.Primary?.Socket() != null) pieces.Add(p.equipment.Primary);
            if (p.apparel != null) pieces.AddRange(p.apparel.WornApparel.Where(a => a.Socket() != null));
            if (pieces.Count == 0) { yield return new FloatMenuOption(label0 + " (" + p.LabelShort + " has nothing on that takes an artifact)", null); yield break; }
            foreach (Thing g in pieces)
            {
                Thing gg = g;
                string label = label0 + " into " + p.LabelShort + "'s " + g.LabelNoCount;
                if (g.Socket().HasArtifact) { yield return new FloatMenuOption(label + " (it already carries one)", null); continue; }
                if (orders.HasOrderFor(g)) { yield return new FloatMenuOption(label + " (already ordered)", null); continue; }
                yield return new FloatMenuOption(label, () =>
                {
                    var o = new ArtifactOrder { kind = ArtifactOrderKind.Inlay, artifact = clickedThing, target = gg, spot = spot, artisan = p };
                    orders.Add(o);
                    Messages.Message("Ordered: " + o.Describe(), spot, MessageTypeDefOf.SilentInput, false);
                    // the wearer goes at once (the order would send it within a couple of seconds anyway)
                    if (o.stage == ArtifactOrderStage.WaitingForWearer && o.wearer == p)
                        p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(ArtifactoricsJobDefOf.Avir_ArtifactBringGarment, gg, spot), JobTag.Misc);
                    else if (o.stage == ArtifactOrderStage.Ready) ArtifactOrderWork.Start(p, o);
                });
            }
        }

        /// <summary>"Extract the artifact from X": an Avir draws it out cleanly; anyone else ruins the piece.</summary>
        private static IEnumerable<FloatMenuOption> ExtractOptions(Pawn p, Thing g, Building_ArtificerSpot spot, MapComponent_ArtifactOrders orders)
        {
            string label = "Extract the artifact from " + (ArtifactCarrierUtility.HolderOf(g) == p ? p.LabelShort + "'s " : "") + g.LabelNoCount;
            if (orders.HasOrderFor(g)) { yield return new FloatMenuOption(label + " (already ordered)", null); yield break; }
            bool avir = ArtifactoricsSettingsDef.Main.CountsAsAvir(p);
            yield return new FloatMenuOption(label + (avir ? "" : " (not an Avir: the piece is ruined)"), () =>
            {
                var o = new ArtifactOrder { kind = ArtifactOrderKind.Extract, target = g, spot = spot, allowNonAvir = !avir, artisan = p };
                orders.Add(o);
                Messages.Message("Ordered: " + o.Describe(), spot, MessageTypeDefOf.SilentInput, false);
                if (o.stage == ArtifactOrderStage.WaitingForWearer && o.wearer == p)
                    p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(ArtifactoricsJobDefOf.Avir_ArtifactBringGarment, g, spot), JobTag.Misc);
                else if (o.stage == ArtifactOrderStage.Ready) ArtifactOrderWork.Start(p, o);
            });
        }
    }

    /// <summary>The artisan's job for a ready order, given straight to one pawn.</summary>
    public static class ArtifactOrderWork
    {
        public static string CannotDo(Pawn p, ArtifactOrder o)
        {
            if (o.target == null || !o.target.Spawned) return "the item is not lying ready";
            if (!p.CanReserveAndReach(o.target, PathEndMode.ClosestTouch, Danger.Some)) return "cannot reach the item";
            if (!p.CanReserveAndReach(o.spot, PathEndMode.InteractionCell, Danger.Some)) return "cannot reach the spot";
            if (o.kind == ArtifactOrderKind.Inlay)
            {
                if (o.artifact == null || !o.artifact.Spawned) return "the artifact is not lying ready";
                if (!p.CanReserveAndReach(o.artifact, PathEndMode.ClosestTouch, Danger.Some)) return "cannot reach the artifact";
            }
            else if (!o.allowNonAvir && !ArtifactoricsSettingsDef.Main.CountsAsAvir(p)) return "only an Avir draws it out cleanly";
            return null;
        }

        public static Job JobFor(ArtifactOrder o)
        {
            Job j = o.kind == ArtifactOrderKind.Inlay
                ? JobMaker.MakeJob(ArtifactoricsJobDefOf.Avir_ArtifactInlay, o.artifact, o.target, o.spot)
                : JobMaker.MakeJob(ArtifactoricsJobDefOf.Avir_ArtifactExtract, o.target, null, o.spot);
            j.count = 1;
            return j;
        }

        public static bool Start(Pawn p, ArtifactOrder o)
        {
            if (CannotDo(p, o) != null) return false;
            Job j = JobFor(o);
            j.playerForced = true;
            return p.jobs.TryTakeOrderedJob(j, JobTag.Misc);
        }
    }
}
