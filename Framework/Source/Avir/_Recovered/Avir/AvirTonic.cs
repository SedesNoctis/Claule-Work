using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[StaticConstructorOnStartup]
public static class AvirTonic
{
	public static readonly ThingDef Tonic;

	public static readonly HediffDef Effect;

	static AvirTonic()
	{
		Tonic = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ImmuneTonic");
		Effect = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_TonicImmunity");
		try
		{
			List<RecipeDef> ours = (from n in new string[2] { "Avir_MakeImmuneTonic", "Avir_MakeAvirMedicine" }
				select DefDatabase<RecipeDef>.GetNamedSilentFail(n) into r
				where r != null
				select r).ToList();
			FieldInfo fieldInfo = AccessTools.Field(typeof(ThingDef), "allRecipesCached");
			int num = 0;
			foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
			{
				if (item.category != ThingCategory.Building || !typeof(Building_WorkTable).IsAssignableFrom(item.thingClass) || !item.AllRecipes.Any((RecipeDef r) => r.ProducedThingDef != null && r.ProducedThingDef.IsMedicine && !ours.Contains(r)))
				{
					continue;
				}
				foreach (RecipeDef item2 in ours)
				{
					if (!item.AllRecipes.Contains(item2))
					{
						if (item2.recipeUsers == null)
						{
							item2.recipeUsers = new List<ThingDef>();
						}
						item2.recipeUsers.Add(item);
						num++;
					}
				}
				fieldInfo?.SetValue(item, null);
			}
			if (num > 0)
			{
				Log.Message("[Avir] alchemy recipes added to " + num / Math.Max(1, ours.Count) + " more table(s) that make medicine.");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[Avir] adding alchemy recipes to medicine tables failed: " + ex.Message);
		}
	}

	public static bool Wants(Pawn p)
	{
		if (p == null || p.Dead || !p.Spawned || p.health?.hediffSet == null || !AvirBody.IsAvir(p))
		{
			return false;
		}
		if (Effect != null && p.health.hediffSet.HasHediff(Effect))
		{
			return false;
		}
		if (p.IsPrisonerOfColony)
		{
			if (!GameComponent_AvirTonics.Allowed(p))
			{
				return false;
			}
		}
		else if (p.Faction != Faction.OfPlayer)
		{
			return false;
		}
		return p.health.hediffSet.hediffs.Any(delegate(Hediff h)
		{
			HediffComp_Immunizable hediffComp_Immunizable = h.TryGetComp<HediffComp_Immunizable>();
			return hediffComp_Immunizable != null && !hediffComp_Immunizable.FullyImmune;
		});
	}
}
