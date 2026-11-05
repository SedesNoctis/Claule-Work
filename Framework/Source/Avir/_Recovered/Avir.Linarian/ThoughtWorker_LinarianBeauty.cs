using RimWorld;
using Verse;

namespace Avir.Linarian;

public class ThoughtWorker_LinarianBeauty : ThoughtWorker
{
	protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
	{
		if (!other.RaceProps.Humanlike || !Linarian.Is(other) || Linarian.Is(p) || !AvirBody.IsAvir(p))
		{
			return false;
		}
		if (!RelationsUtility.PawnsKnowEachOther(p, other))
		{
			return false;
		}
		return true;
	}
}
