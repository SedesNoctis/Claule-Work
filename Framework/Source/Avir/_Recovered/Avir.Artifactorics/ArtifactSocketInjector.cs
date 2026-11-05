using System;
using System.Collections.Generic;
using Verse;

namespace Avir.Artifactorics;

[StaticConstructorOnStartup]
public static class ArtifactSocketInjector
{
	static ArtifactSocketInjector()
	{
		int num = 0;
		int num2 = 0;
		foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (ArtifactCarrierUtility.IsSocketableWeapon(item) && !item.comps.Any((CompProperties c) => c is CompProperties_ArtifactSocket))
			{
				item.comps.Add(new CompProperties_ArtifactSocket());
				num2++;
			}
			else if (item.IsApparel && item.comps != null && !item.comps.Any((CompProperties c) => c is CompProperties_ArtifactSocket) && !item.comps.Any((CompProperties c) => c is CompProperties_Telekinesis))
			{
				item.comps.Add(new CompProperties_ArtifactSocket());
				num++;
			}
		}
		int num3 = 0;
		foreach (ThingDef item2 in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (item2.race == null || !item2.race.Humanlike)
			{
				continue;
			}
			if (item2.inspectorTabs == null)
			{
				item2.inspectorTabs = new List<Type>();
			}
			if (!item2.inspectorTabs.Contains(typeof(ITab_Pawn_Artifacts)))
			{
				item2.inspectorTabs.Add(typeof(ITab_Pawn_Artifacts));
				if (item2.inspectorTabsResolved == null)
				{
					item2.inspectorTabsResolved = new List<InspectTabBase>();
				}
				item2.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Artifacts)));
				if (!item2.inspectorTabs.Contains(typeof(ITab_Pawn_Satchel)))
				{
					item2.inspectorTabs.Add(typeof(ITab_Pawn_Satchel));
					item2.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Satchel)));
				}
				num3++;
			}
		}
		Log.Message("[Avir] Artifactorics: artifact socket added to " + num + " apparel defs and " + num2 + " melee weapons; artifact tab added to " + num3 + " humanlike races.");
	}

	public static CompArtifactSocket Socket(this Thing t)
	{
		return (t as ThingWithComps)?.GetComp<CompArtifactSocket>();
	}
}
