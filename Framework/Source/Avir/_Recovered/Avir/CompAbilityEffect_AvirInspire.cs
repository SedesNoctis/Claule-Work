using RimWorld;
using Verse;

namespace Avir;

public class CompAbilityEffect_AvirInspire : CompAbilityEffect
{
	private InspirationDef Def => ((CompProperties_AbilityAvirInspire)props).inspiration;

	public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
	{
		Pawn pawn = target.Pawn;
		if (pawn?.mindState?.inspirationHandler == null || Def == null)
		{
			return false;
		}
		if (pawn.mindState.inspirationHandler.Inspired)
		{
			if (throwMessages)
			{
				Messages.Message(pawn.LabelShort + " is already inspired.", pawn, MessageTypeDefOf.RejectInput, historical: false);
			}
			return false;
		}
		return base.Valid(target, throwMessages);
	}

	public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
	{
		base.Apply(target, dest);
		Pawn pawn = target.Pawn;
		if (pawn?.mindState?.inspirationHandler != null && Def != null && !pawn.mindState.inspirationHandler.TryStartInspiration(Def, "Blessed by " + parent.pawn.LabelShort + ", the Lord of Arts."))
		{
			Messages.Message(pawn.LabelShort + " could not be inspired now.", pawn, MessageTypeDefOf.RejectInput, historical: false);
		}
	}
}
