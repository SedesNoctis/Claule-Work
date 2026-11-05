using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.North;

[StaticConstructorOnStartup]
public static class AvirBiomeCheck
{
	static AvirBiomeCheck()
	{
		foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (item.plant != null && item.HasModExtension<ShelterTree>())
			{
				item.plant.blockAdjacentSow = false;
			}
		}
		foreach (BiomeDef item2 in DefDatabase<BiomeDef>.AllDefsListForReading)
		{
			if (item2.defName.StartsWith("Avir_"))
			{
				int num = item2.wildPlants.RemoveAll((BiomePlantRecord r) => r?.plant == null);
				int num2 = Traverse.Create(item2).Field("wildAnimals").GetValue<List<BiomeAnimalRecord>>()?.RemoveAll((BiomeAnimalRecord r) => r?.animal == null) ?? 0;
				int num3 = item2.terrainsByFertility.RemoveAll((TerrainThreshold r) => r?.terrain == null);
				if (num + num2 + num3 > 0)
				{
					Log.Warning("[Avir] " + item2.defName + ": dropped " + num + " missing plants, " + num2 + " missing animals, " + num3 + " missing grounds from its lists");
					Traverse.Create(item2).Field("cachedPlantCommonalities").SetValue(null);
					Traverse.Create(item2).Field("cachedAnimalCommonalities").SetValue(null);
				}
			}
		}
		foreach (TerrainDef item3 in DefDatabase<TerrainDef>.AllDefsListForReading)
		{
			if (item3.defName.StartsWith("Avir_") && !item3.texturePath.NullOrEmpty())
			{
				Texture texture = item3.graphic?.MatSingle?.mainTexture;
				if (texture == null || texture == BaseContent.BadTex)
				{
					Log.Error("[Avir] the ground " + item3.defName + " has no picture: Textures/" + item3.texturePath + ".png is missing or unreadable");
				}
			}
		}
	}
}
