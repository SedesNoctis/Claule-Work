using System.Collections.Generic;
using Verse;

namespace Avir;

[StaticConstructorOnStartup]
public static class OwnBuildingsKeepTheirColours
{
	static OwnBuildingsKeepTheirColours()
	{
		ModContentPack modContentPack = LoadedModManager.GetMod<AvirMod>()?.Content;
		if (modContentPack == null)
		{
			return;
		}
		foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (item.modContentPack == modContentPack && item.plant != null && item.plant.harvestedThingDef != null)
			{
				item.neverMultiSelect = false;
			}
			if (item.modContentPack != modContentPack || item.building == null || item.category != ThingCategory.Building || item.HasModExtension<AvirStuffTinted>())
			{
				continue;
			}
			item.building.paintable = false;
			if (!item.HasModExtension<NoStuffTint>())
			{
				if (item.modExtensions == null)
				{
					item.modExtensions = new List<DefModExtension>();
				}
				item.modExtensions.Add(new NoStuffTint());
			}
		}
	}
}
