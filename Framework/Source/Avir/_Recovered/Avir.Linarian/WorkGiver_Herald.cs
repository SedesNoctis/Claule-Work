using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public abstract class WorkGiver_Herald : WorkGiver_Scanner
{
	protected abstract DesignationDef Des { get; }

	protected abstract bool TameIt { get; }

	public override PathEndMode PathEndMode => PathEndMode.Touch;

	public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
	{
		return from d in pawn.Map.designationManager.SpawnedDesignationsOfDef(Des)
			select d.target.Thing into t
			where t is Pawn p && LinarianPresence.Disguised(p) && !LinarianVeil.Veiled(p)
			select t;
	}

	public override bool ShouldSkip(Pawn pawn, bool forced = false)
	{
		return !PotentialWorkThingsGlobal(pawn).Any();
	}

	public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
	{
		if (!(t is Pawn pawn2) || !LinarianPresence.Disguised(pawn2) || pawn2 == pawn || LinarianVeil.Veiled(pawn2))
		{
			return false;
		}
		if (TameIt && (pawn.skills?.GetSkill(SkillDefOf.Animals)?.Level).GetValueOrDefault() < 12)
		{
			JobFailReason.Is("needs Animals 12");
			return false;
		}
		if (TameIt && !LinarianPresence.TameRested(pawn2))
		{
			JobFailReason.Is("tried recently");
			return false;
		}
		return pawn.CanReach(pawn2, PathEndMode.Touch, Danger.Some);
	}

	public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
	{
		JobDef namedSilentFail = DefDatabase<JobDef>.GetNamedSilentFail(TameIt ? "Avir_TameHerald" : "Avir_ApproachHerald");
		if (namedSilentFail == null)
		{
			return null;
		}
		return JobMaker.MakeJob(namedSilentFail, t);
	}
}
