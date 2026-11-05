using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics;

public class WorkGiver_ArtifactInlay : WorkGiver_Scanner
{
	public override bool ShouldSkip(Pawn pawn, bool forced = false)
	{
		MapComponent_ArtifactOrders mapComponent_ArtifactOrders = MapComponent_ArtifactOrders.For(pawn.Map);
		if (mapComponent_ArtifactOrders != null)
		{
			return !mapComponent_ArtifactOrders.orders.Any((ArtifactOrder o) => o.stage == ArtifactOrderStage.Ready);
		}
		return true;
	}

	public override Job NonScanJob(Pawn pawn)
	{
		MapComponent_ArtifactOrders mapComponent_ArtifactOrders = MapComponent_ArtifactOrders.For(pawn.Map);
		if (mapComponent_ArtifactOrders == null)
		{
			return null;
		}
		foreach (ArtifactOrder order in mapComponent_ArtifactOrders.orders)
		{
			if (order.stage != ArtifactOrderStage.Ready || order.target == null || !order.target.Spawned || !pawn.CanReserveAndReach(order.target, PathEndMode.ClosestTouch, Danger.Some) || !pawn.CanReserveAndReach(order.spot, PathEndMode.InteractionCell, Danger.Some))
			{
				continue;
			}
			if (order.kind == ArtifactOrderKind.Inlay)
			{
				if (order.artifact != null && order.artifact.Spawned && !order.artifact.IsForbidden(pawn) && pawn.CanReserveAndReach(order.artifact, PathEndMode.ClosestTouch, Danger.Some))
				{
					Job job = JobMaker.MakeJob(ArtifactoricsJobDefOf.Avir_ArtifactInlay, order.artifact, order.target, order.spot);
					job.count = 1;
					return job;
				}
			}
			else if (order.allowNonAvir || ArtifactoricsSettingsDef.Main.CountsAsAvir(pawn))
			{
				Job job2 = JobMaker.MakeJob(ArtifactoricsJobDefOf.Avir_ArtifactExtract, order.target, null, order.spot);
				job2.count = 1;
				return job2;
			}
		}
		return null;
	}
}
