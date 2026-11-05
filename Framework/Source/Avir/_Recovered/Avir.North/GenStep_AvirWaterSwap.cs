using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.North;

public class GenStep_AvirWaterSwap : GenStep
{
	public override int SeedPart => 5905680;

	public override void Generate(Map map, GenStepParams parms)
	{
		AvirPonds avirPonds = map.Biome?.GetModExtension<AvirPonds>();
		if (avirPonds == null || !avirPonds.swapStillWater || (avirPonds.shallow == null && avirPonds.deep == null))
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		foreach (IntVec3 allCell in map.AllCells)
		{
			TerrainDef terrain = allCell.GetTerrain(map);
			if (terrain == TerrainDefOf.WaterShallow && avirPonds.shallow != null)
			{
				map.terrainGrid.SetTerrain(allCell, avirPonds.shallow);
				num++;
			}
			else if (terrain == TerrainDefOf.WaterDeep && avirPonds.deep != null)
			{
				map.terrainGrid.SetTerrain(allCell, avirPonds.deep);
				num++;
			}
		}
		if (avirPonds.shorePlant != null)
		{
			foreach (IntVec3 allCell2 in map.AllCells)
			{
				if (allCell2.GetTerrain(map).IsWater || allCell2.GetEdifice(map) != null || allCell2.GetPlant(map) != null || !Rand.Chance(avirPonds.shoreChance))
				{
					continue;
				}
				bool flag = false;
				IntVec3[] cardinalDirections = GenAdj.CardinalDirections;
				foreach (IntVec3 intVec in cardinalDirections)
				{
					IntVec3 c = allCell2 + intVec;
					TerrainDef terrainDef = (c.InBounds(map) ? c.GetTerrain(map) : null);
					if (terrainDef != null && (terrainDef == avirPonds.shallow || terrainDef == avirPonds.deep))
					{
						flag = true;
						break;
					}
				}
				if (flag && avirPonds.shorePlant.CanEverPlantAt(allCell2, map))
				{
					Plant plant = (Plant)ThingMaker.MakeThing(avirPonds.shorePlant);
					plant.Growth = Rand.Range(0.3f, 1f);
					if (plant.def.plant.LimitedLifespan)
					{
						plant.Age = Rand.Range(0, Mathf.Max(plant.def.plant.LifespanTicks - 50, 0));
					}
					GenSpawn.Spawn(plant, allCell2, map);
					num2++;
				}
			}
		}
		if ((num > 0 || num2 > 0) && Prefs.DevMode)
		{
			Log.Message("[Avir] " + map.Biome.label + ": " + num + " cells of still water made the biome's own, " + num2 + " shore plants");
		}
	}
}
