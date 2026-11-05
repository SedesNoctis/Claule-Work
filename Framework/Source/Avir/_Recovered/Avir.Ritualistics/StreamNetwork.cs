using Avir.Artifactorics;
using Avir.Spire;
using Verse;

namespace Avir.Ritualistics;

public static class StreamNetwork
{
	public static bool Workable(IntVec3 c, Map map)
	{
		if (!c.InBounds(map))
		{
			return false;
		}
		TerrainDef terrain = c.GetTerrain(map);
		if (terrain != null && !terrain.IsWater && (!terrain.natural || terrain.defName.EndsWith("_Smooth")))
		{
			return true;
		}
		if (terrain != null && terrain.IsWater)
		{
			return true;
		}
		Building edifice = c.GetEdifice(map);
		if (edifice != null && edifice.def.building != null && !edifice.def.building.isNaturalRock)
		{
			return edifice.def.IsBuildingArtificial;
		}
		return false;
	}

	public static bool IsNodeDef(ThingDef d)
	{
		if (d != null)
		{
			if (!d.HasComp(typeof(CompEnergyKeeper)) && !d.HasComp(typeof(CompRitualCircle)) && !d.HasComp(typeof(CompArtifactHost)) && !d.HasComp(typeof(CompPsychicPylon)) && !d.HasComp(typeof(CompSpirePylon)) && !d.HasComp(typeof(CompSpireCradle)))
			{
				return d.HasComp(typeof(CompCoreHearth));
			}
			return true;
		}
		return false;
	}

	public static bool IsNode(Thing t)
	{
		if (t != null)
		{
			if (t.TryGetComp<CompEnergyKeeper>() == null && t.TryGetComp<CompRitualCircle>() == null && t.TryGetComp<CompArtifactHost>() == null && t.TryGetComp<CompPsychicPylon>() == null && t.TryGetComp<CompSpirePylon>() == null && t.TryGetComp<CompSpireCradle>() == null)
			{
				return t.TryGetComp<CompCoreHearth>() != null;
			}
			return true;
		}
		return false;
	}
}
