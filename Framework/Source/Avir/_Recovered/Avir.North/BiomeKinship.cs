using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.North;

[StaticConstructorOnStartup]
public static class BiomeKinship
{
	static BiomeKinship()
	{
		foreach (BiomeDef item in DefDatabase<BiomeDef>.AllDefsListForReading)
		{
			BiomeKin modExtension = item.GetModExtension<BiomeKin>();
			if (modExtension?.kin != null)
			{
				try
				{
					Borrow(item, modExtension);
				}
				catch (Exception ex)
				{
					Log.Warning("[Avir] " + item.defName + ": could not borrow from " + modExtension.kin.defName + ": " + ex.Message);
				}
			}
		}
	}

	private static void Borrow(BiomeDef b, BiomeKin k)
	{
		int num = 0;
		int num2 = 0;
		if (k.fish && b.fishTypes == null && k.kin.fishTypes != null)
		{
			b.fishTypes = k.kin.fishTypes;
		}
		AvirPonds modExtension = b.GetModExtension<AvirPonds>();
		if (modExtension != null)
		{
			if (modExtension.shallow != null && modExtension.shallow.waterBodyType == WaterBodyType.None)
			{
				modExtension.shallow.waterBodyType = TerrainDefOf.WaterShallow.waterBodyType;
			}
			if (modExtension.deep != null && modExtension.deep.waterBodyType == WaterBodyType.None)
			{
				modExtension.deep.waterBodyType = TerrainDefOf.WaterDeep.waterBodyType;
			}
		}
		if (k.mutators)
		{
			foreach (TileMutatorDef item in DefDatabase<TileMutatorDef>.AllDefsListForReading)
			{
				if (k.skipMutators.Contains(item.defName))
				{
					continue;
				}
				bool flag = false;
				if (k.extraMutators.Contains(item.defName) && item.biomeWhitelist != null && !item.biomeWhitelist.Contains(b))
				{
					item.biomeWhitelist.Add(b);
					flag = true;
				}
				if (item.biomeWhitelist != null && item.biomeWhitelist.Contains(k.kin) && !item.biomeWhitelist.Contains(b))
				{
					item.biomeWhitelist.Add(b);
					flag = true;
				}
				if (item.biomeBlacklist != null && item.biomeBlacklist.Contains(k.kin) && !item.biomeBlacklist.Contains(b))
				{
					item.biomeBlacklist.Add(b);
					flag = true;
				}
				if (flag)
				{
					Traverse traverse = Traverse.Create(item);
					if (item.biomeWhitelist != null)
					{
						traverse.Field("biomeWhitelistSet").SetValue(new HashSet<BiomeDef>(item.biomeWhitelist));
					}
					if (item.biomeBlacklist != null)
					{
						traverse.Field("biomeBlacklistSet").SetValue(new HashSet<BiomeDef>(item.biomeBlacklist));
					}
					num++;
				}
			}
		}
		if (k.animals)
		{
			foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
			{
				List<AnimalBiomeRecord> list = d.race?.wildBiomes;
				if (list == null || (!k.officialAnimals && d.modContentPack != null && d.modContentPack.IsOfficialMod) || k.skipAnimalPrefixes.Exists((string x) => d.defName.StartsWith(x)))
				{
					continue;
				}
				AnimalBiomeRecord animalBiomeRecord = null;
				bool flag2 = false;
				foreach (AnimalBiomeRecord item2 in list)
				{
					if (item2.biome == k.kin)
					{
						animalBiomeRecord = item2;
					}
					if (item2.biome == b)
					{
						flag2 = true;
					}
				}
				if (!(animalBiomeRecord == null || flag2))
				{
					list.Add(new AnimalBiomeRecord
					{
						biome = b,
						commonality = animalBiomeRecord.commonality * k.animalFactor
					});
					num2++;
				}
			}
		}
		Traverse.Create(b).Field("cachedAnimalCommonalities").SetValue(null);
		if (Prefs.DevMode)
		{
			Log.Message("[Avir] " + b.defName + " borrows from " + k.kin.defName + ": " + num + " landmarks/mutators, " + num2 + " modded animals");
		}
	}
}
