using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class JoyGiver_GentleBeast : JoyGiver
{
	public static Pawn HisBeast(Pawn him)
	{
		HeraldRecord heraldRecord = GameComponent_Heralds.Instance?.records.FirstOrDefault((HeraldRecord x) => x.beloved == him && x.her != null && x.her.Spawned && x.her.Map == him.Map && LinarianVeil.InForm(x.her) != null);
		if (heraldRecord == null)
		{
			return null;
		}
		HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_FormingBond");
		Hediff hediff = ((namedSilentFail != null) ? him.health.hediffSet.GetFirstHediffOfDef(namedSilentFail) : null);
		if (hediff == null || !(hediff.Severity >= 0.3f))
		{
			return null;
		}
		return heraldRecord.her;
	}

	public override bool CanBeGivenTo(Pawn pawn)
	{
		if (pawn.Spawned && !pawn.IsPrisoner && HisBeast(pawn) != null)
		{
			return base.CanBeGivenTo(pawn);
		}
		return false;
	}

	public override float GetChance(Pawn pawn)
	{
		if (HisBeast(pawn) == null)
		{
			return 0f;
		}
		return 60f;
	}

	public override Job TryGiveJob(Pawn pawn)
	{
		Pawn pawn2 = HisBeast(pawn);
		if (pawn2 == null || !pawn.CanReach(pawn2, PathEndMode.Touch, Danger.Some))
		{
			return null;
		}
		return JobMaker.MakeJob(def.jobDef, pawn2);
	}
}
