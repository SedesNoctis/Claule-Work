using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Avir.Artifactorics;

public class MapComponent_ArtifactOrders : MapComponent
{
	public List<ArtifactOrder> orders = new List<ArtifactOrder>();

	private static readonly HashSet<JobDef> keepJobs = new HashSet<JobDef>();

	public MapComponent_ArtifactOrders(Map map)
		: base(map)
	{
	}

	public static MapComponent_ArtifactOrders For(Map m)
	{
		return m?.GetComponent<MapComponent_ArtifactOrders>();
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref orders, "artifactOrders", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (orders == null)
			{
				orders = new List<ArtifactOrder>();
			}
			orders.RemoveAll((ArtifactOrder o) => o == null || o.target == null);
		}
	}

	public bool HasOrderFor(Thing t)
	{
		if (t != null)
		{
			return orders.Any((ArtifactOrder o) => o.stage != ArtifactOrderStage.Done && (o.target == t || o.artifact == t));
		}
		return false;
	}

	public ArtifactOrder OrderFor(Thing t)
	{
		return orders.FirstOrDefault((ArtifactOrder o) => o.stage != ArtifactOrderStage.Done && (o.target == t || o.artifact == t));
	}

	public void Add(ArtifactOrder o)
	{
		o.createdTick = Find.TickManager.TicksGame;
		Pawn pawn = ArtifactCarrierUtility.HolderOf(o.target);
		if (pawn != null)
		{
			o.wearer = pawn;
			o.stage = ArtifactOrderStage.WaitingForWearer;
		}
		else
		{
			o.stage = ArtifactOrderStage.Ready;
		}
		orders.Add(o);
	}

	public void Cancel(ArtifactOrder o)
	{
		if (o.target != null && !o.target.Destroyed && o.target.Spawned && o.wearer != null)
		{
			o.target.SetForbidden(value: false, warnOnFail: false);
		}
		orders.Remove(o);
	}

	public void WorkFinished(ArtifactOrder o, bool garmentSurvived)
	{
		if (garmentSurvived && o.wearer != null && !o.wearer.Dead)
		{
			o.stage = ArtifactOrderStage.ReturnToWearer;
			o.returnSinceTick = Find.TickManager.TicksGame;
			return;
		}
		if (garmentSurvived && o.target != null && o.target.Spawned)
		{
			o.target.SetForbidden(value: false, warnOnFail: false);
		}
		orders.Remove(o);
	}

	public override void MapComponentTick()
	{
		if (orders.Count == 0 || Find.TickManager.TicksGame % 120 != 0)
		{
			return;
		}
		for (int num = orders.Count - 1; num >= 0; num--)
		{
			ArtifactOrder artifactOrder = orders[num];
			if (!Validate(artifactOrder))
			{
				Cancel(artifactOrder);
			}
			else
			{
				switch (artifactOrder.stage)
				{
				case ArtifactOrderStage.WaitingForWearer:
					TickWaiting(artifactOrder);
					break;
				case ArtifactOrderStage.Ready:
					TickReady(artifactOrder);
					break;
				case ArtifactOrderStage.ReturnToWearer:
					TickReturn(artifactOrder);
					break;
				}
			}
		}
	}

	private bool Validate(ArtifactOrder o)
	{
		if (o.target == null || o.target.Destroyed)
		{
			return false;
		}
		if (o.spot == null || o.spot.Destroyed || o.spot.Map != map)
		{
			return false;
		}
		if (o.kind == ArtifactOrderKind.Inlay && o.stage != ArtifactOrderStage.ReturnToWearer)
		{
			if (o.artifact == null || o.artifact.Destroyed)
			{
				return false;
			}
			CompArtifactSocket compArtifactSocket = o.target.Socket();
			if (compArtifactSocket != null && compArtifactSocket.HasArtifact)
			{
				return false;
			}
		}
		if (o.kind == ArtifactOrderKind.Extract && o.stage != ArtifactOrderStage.ReturnToWearer)
		{
			CompArtifactSocket compArtifactSocket2 = o.target.Socket();
			if (compArtifactSocket2 == null || !compArtifactSocket2.HasArtifact)
			{
				return false;
			}
		}
		return true;
	}

	internal static bool Available(Pawn p, Map m)
	{
		if (p == null || p.Dead || !p.Spawned || p.Map != m || p.Downed || p.Drafted || p.InMentalState)
		{
			return false;
		}
		if (!p.Awake() || p.mindState == null || p.mindState.anyCloseHostilesRecently)
		{
			return false;
		}
		if (p.GetLord() != null)
		{
			return false;
		}
		if (keepJobs.Count == 0)
		{
			keepJobs.Add(JobDefOf.LayDown);
			keepJobs.Add(JobDefOf.Ingest);
			keepJobs.Add(JobDefOf.AttackMelee);
			keepJobs.Add(JobDefOf.AttackStatic);
			keepJobs.Add(JobDefOf.Wait_Combat);
			keepJobs.Add(JobDefOf.Rescue);
			keepJobs.Add(JobDefOf.TendPatient);
			keepJobs.Add(JobDefOf.BeatFire);
			keepJobs.Add(JobDefOf.Flee);
			keepJobs.Add(JobDefOf.FleeAndCower);
			keepJobs.Add(JobDefOf.Wear);
			keepJobs.Add(JobDefOf.Equip);
			keepJobs.Add(ArtifactoricsJobDefOf.Avir_ArtifactBringGarment);
		}
		Job curJob = p.CurJob;
		if (curJob != null && (curJob.playerForced || keepJobs.Contains(curJob.def)))
		{
			return false;
		}
		return true;
	}

	private void TickReady(ArtifactOrder o)
	{
		Pawn pawn = ArtifactCarrierUtility.HolderOf(o.target);
		if (pawn != null)
		{
			o.wearer = pawn;
			o.stage = ArtifactOrderStage.WaitingForWearer;
		}
		else if (o.artisan != null && Available(o.artisan, map) && o.artisan.CurJobDef != ArtifactoricsJobDefOf.Avir_ArtifactInlay && o.artisan.CurJobDef != ArtifactoricsJobDefOf.Avir_ArtifactExtract)
		{
			ArtifactOrderWork.Start(o.artisan, o);
		}
	}

	private void TickWaiting(ArtifactOrder o)
	{
		Pawn pawn = ArtifactCarrierUtility.HolderOf(o.target);
		if (pawn == null)
		{
			o.stage = ArtifactOrderStage.Ready;
			return;
		}
		if (pawn != o.wearer)
		{
			o.wearer = pawn;
		}
		if (Available(o.wearer, map) && o.wearer.CanReach(o.spot, PathEndMode.InteractionCell, Danger.Some))
		{
			Job job = JobMaker.MakeJob(ArtifactoricsJobDefOf.Avir_ArtifactBringGarment, o.target, o.spot);
			o.wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
		}
	}

	private void TickReturn(ArtifactOrder o)
	{
		Thing target = o.target;
		if (target == null || target.Destroyed)
		{
			orders.Remove(o);
		}
		else if (ArtifactCarrierUtility.HolderOf(target) != null)
		{
			orders.Remove(o);
		}
		else if (o.wearer == null || o.wearer.Dead || Find.TickManager.TicksGame - o.returnSinceTick > 180000)
		{
			if (target.Spawned)
			{
				target.SetForbidden(value: false, warnOnFail: false);
			}
			orders.Remove(o);
		}
		else if (target.Spawned && target.Map == map && Available(o.wearer, map) && o.wearer.CanReserveAndReach(target, PathEndMode.ClosestTouch, Danger.Some))
		{
			target.SetForbidden(value: false, warnOnFail: false);
			Job job = JobMaker.MakeJob(ArtifactCarrierUtility.ReturnJob(target), target);
			job.playerForced = true;
			o.wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
		}
	}
}
