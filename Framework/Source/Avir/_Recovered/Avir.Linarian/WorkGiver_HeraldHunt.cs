using RimWorld;
using Verse;

namespace Avir.Linarian;

public class WorkGiver_HeraldHunt : WorkGiver_Herald
{
	protected override DesignationDef Des => DesignationDefOf.Hunt;

	protected override bool TameIt => false;
}
