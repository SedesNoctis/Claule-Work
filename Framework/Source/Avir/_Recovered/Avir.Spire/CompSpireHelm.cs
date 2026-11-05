using RimWorld;
using Verse;

namespace Avir.Spire;

public class CompSpireHelm : CompPilotConsole
{
	public override void PostSpawnSetup(bool respawningAfterReload)
	{
		base.PostSpawnSetup(respawningAfterReload);
		if (parent is Building_GravEngine building_GravEngine && engine == null)
		{
			engine = building_GravEngine;
			building_GravEngine.AddComponent(this);
		}
	}

	public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
	{
		if (engine != null)
		{
			engine.RemoveComponent(this);
			engine = null;
		}
		base.PostDeSpawn(map, mode);
	}

	public override string CompInspectStringExtra()
	{
		return null;
	}
}
