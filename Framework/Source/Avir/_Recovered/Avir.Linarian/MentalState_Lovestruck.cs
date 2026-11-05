using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class MentalState_Lovestruck : MentalState_Song
{
	protected override void Drive()
	{
		if ((pawn.Position - herald.Position).LengthHorizontal <= 1.9f)
		{
			Pawn her = herald;
			RecoverFromState();
			LinarianCourtship.Union(pawn, her);
		}
		else
		{
			Job job = JobMaker.MakeJob(JobDefOf.Goto, herald);
			job.expiryInterval = 150;
			job.locomotionUrgency = LocomotionUrgency.Jog;
			Force(job);
		}
	}
}
