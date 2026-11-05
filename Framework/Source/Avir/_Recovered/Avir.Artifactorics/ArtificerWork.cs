using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

[StaticConstructorOnStartup]
public static class ArtificerWork
{
	public static WorkGiverDef General;

	public static WorkGiverDef Drugs;

	public static WorkTypeDef DrugWork;

	static ArtificerWork()
	{
		try
		{
			General = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Avir_DoBillsArtificerSpot_Jeweller");
			Drugs = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Avir_DoBillsArtificerSpot_Designer");
			ThingDef spot = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ArtificerSpot");
			ThingDef round = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ArtificerRoundTable");
			int num = 0;
			foreach (RecipeDef item in DefDatabase<RecipeDef>.AllDefsListForReading)
			{
				List<ThingDef> list = item.AllRecipeUsers.ToList();
				if (list.Count != 0 && list.All((ThingDef u) => u == spot || u == round) && item.requiredGiverWorkType != null)
				{
					item.requiredGiverWorkType = null;
					num++;
				}
			}
			DrugWork = DefDatabase<WorkTypeDef>.AllDefsListForReading.FirstOrDefault((WorkTypeDef w) => w.defName.IndexOf("drug", StringComparison.OrdinalIgnoreCase) >= 0 || (w.labelShort ?? "").IndexOf("drug", StringComparison.OrdinalIgnoreCase) >= 0);
			if (Drugs != null && DrugWork != null && Drugs.workType != DrugWork)
			{
				Drugs.workType.workGiversByPriority.Remove(Drugs);
				Drugs.workType = DrugWork;
				DrugWork.workGiversByPriority.Add(Drugs);
				DrugWork.workGiversByPriority.SortByDescending((WorkGiverDef g) => g.priorityInType);
			}
			Log.Message("[Avir] Artificer's tables: own work type, " + num + " recipes opened to it; drugs made there go to " + ((DrugWork != null) ? DrugWork.defName : "artifactory (no drug work type found)") + ".");
		}
		catch (Exception ex)
		{
			Log.Warning("[Avir] Artificer work setup failed: " + ex);
		}
	}

	public static bool IsDrug(RecipeDef r)
	{
		ThingDef thingDef = r?.ProducedThingDef;
		if (thingDef != null)
		{
			if (!thingDef.IsDrug)
			{
				return thingDef.IsMedicine;
			}
			return true;
		}
		return false;
	}
}
