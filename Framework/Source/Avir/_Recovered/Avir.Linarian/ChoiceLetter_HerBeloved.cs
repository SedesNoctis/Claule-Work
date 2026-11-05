using System.Collections.Generic;
using Verse;

namespace Avir.Linarian;

public class ChoiceLetter_HerBeloved : ChoiceLetter
{
	public Pawn her;

	public Pawn him;

	public bool asking;

	public override IEnumerable<DiaOption> Choices
	{
		get
		{
			if (base.ArchivedOnly)
			{
				yield return base.Option_Close;
				yield break;
			}
			if (asking)
			{
				yield return new DiaOption("Let her in")
				{
					action = delegate
					{
						HeraldRecord heraldRecord = LinarianPresence.Record(her);
						if (heraldRecord != null)
						{
							LinarianCourtship.Join(heraldRecord);
						}
						Find.LetterStack.RemoveLetter(this);
					},
					resolveTree = true
				};
				yield return new DiaOption("Turn her away")
				{
					action = delegate
					{
						LinarianCourtship.Refused(her);
						Find.LetterStack.RemoveLetter(this);
					},
					resolveTree = true
				};
			}
			else
			{
				yield return new DiaOption("Let him serve her")
				{
					action = delegate
					{
						LinarianCourtship.Choose(her, 1);
						Find.LetterStack.RemoveLetter(this);
					},
					resolveTree = true
				};
				yield return new DiaOption("Let him go to her")
				{
					action = delegate
					{
						LinarianCourtship.Choose(her, 2);
						Find.LetterStack.RemoveLetter(this);
					},
					resolveTree = true
				};
			}
			yield return base.Option_Postpone;
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref her, "her");
		Scribe_References.Look(ref him, "him");
		Scribe_Values.Look(ref asking, "asking", defaultValue: false);
	}
}
