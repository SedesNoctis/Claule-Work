using RimWorld;
using Verse;

namespace Avir.Linarian;

public class WorkGiver_HeraldTame : WorkGiver_Herald
{
	protected override DesignationDef Des => DesignationDefOf.Tame;

	protected override bool TameIt => true;
}
