using Verse;

namespace Avir.Ritualistics;

public class CompProperties_RitualCircle : CompProperties
{
	public RitualCircleSize size;

	public CompProperties_RitualCircle()
	{
		compClass = typeof(CompRitualCircle);
	}
}
