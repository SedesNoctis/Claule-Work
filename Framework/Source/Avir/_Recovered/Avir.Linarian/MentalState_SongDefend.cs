using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class MentalState_SongDefend : MentalState_Song
{
	protected override void Drive()
	{
		Pawn pawn = (from q in base.pawn.Map.mapPawns.AllPawnsSpawned
			where !q.Dead && !q.Downed && q != base.pawn && q.HostileTo(Faction.OfPlayer) && !(q.MentalState is MentalState_SongDefend)
			orderby (q.Position - base.pawn.Position).LengthHorizontalSquared
			select q).FirstOrDefault();
		if (pawn == null)
		{
			RecoverFromState();
			return;
		}
		Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, pawn);
		job.expiryInterval = 300;
		Force(job);
	}

	public override bool ForceHostileTo(Thing t)
	{
		if (t is Pawn t2)
		{
			return t2.HostileTo(Faction.OfPlayer);
		}
		return false;
	}

	public override bool ForceHostileTo(Faction f)
	{
		return f?.HostileTo(Faction.OfPlayer) ?? false;
	}
}
