using Verse;

namespace Avir.Ritualistics;

public class Building_StreamWire : Building
{
	public override void Print(SectionLayer layer)
	{
	}

	public override void SpawnSetup(Map map, bool respawningAfterLoad)
	{
		base.SpawnSetup(map, respawningAfterLoad);
		MapComponent_StreamNetwork.Of(map)?.AddWire(base.Position);
	}

	public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
	{
		Map map = base.Map;
		IntVec3 position = base.Position;
		base.DeSpawn(mode);
		MapComponent_StreamNetwork.Of(map)?.RemoveWire(position);
	}
}
