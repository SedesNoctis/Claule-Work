using System.Linq;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class MentalState_BeastFright : MentalState_WanderOwnRoom
{
	public override void PostStart(string reason)
	{
		base.PostStart(reason);
		if (pawn.ownership?.OwnedBed == null && pawn.Map != null)
		{
			Map map = pawn.Map;
			IntVec3 intVec = (from c in map.areaManager.Home.ActiveCells
				where c.Roofed(map) && c.Standable(map) && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly)
				orderby (c - pawn.Position).LengthHorizontalSquared
				select c).FirstOrDefault();
			if (intVec.IsValid && intVec != default(IntVec3))
			{
				target = intVec;
			}
		}
	}
}
