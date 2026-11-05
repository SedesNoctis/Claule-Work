using Verse;

namespace Avir.Ritualistics;

public class PlaceWorker_StreamWire : PlaceWorker
{
	public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
	{
		if (!StreamNetwork.Workable(loc, map))
		{
			return new AcceptanceReport("Stream wires are cut only into worked surfaces - built or smoothed floors, under built walls - or laid along the bed of water.");
		}
		return true;
	}
}
