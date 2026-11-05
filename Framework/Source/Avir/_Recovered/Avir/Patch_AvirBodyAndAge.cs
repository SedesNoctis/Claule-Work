using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
public static class Patch_AvirBodyAndAge
{
	[HarmonyPriority(800)]
	public static void Postfix(Pawn __result)
	{
		try
		{
			if (__result == null || __result.story == null || !AvirFaction.IsAvirPawnKind(__result))
			{
				return;
			}
			string text = ((__result.kindDef != null) ? __result.kindDef.defName : "");
			switch (text)
			{
			case "Avir_Huntress":
			case "Avir_Priestess":
			case "Avir_Matriarch":
				if (__result.gender == Gender.Female)
				{
					BodyTypeDef namedSilentFail = DefDatabase<BodyTypeDef>.GetNamedSilentFail("Female");
					if (namedSilentFail != null && __result.story.bodyType != namedSilentFail)
					{
						__result.story.bodyType = namedSilentFail;
						__result.Drawer?.renderer?.SetAllGraphicsDirty();
					}
					break;
				}
				goto default;
			default:
				if (text == "Avir_Warrior" && __result.gender == Gender.Male)
				{
					BodyTypeDef namedSilentFail2 = DefDatabase<BodyTypeDef>.GetNamedSilentFail("Male");
					if (namedSilentFail2 != null && __result.story.bodyType == null)
					{
						__result.story.bodyType = namedSilentFail2;
						__result.Drawer?.renderer?.SetAllGraphicsDirty();
					}
				}
				else if (text == "Avir_Strider")
				{
					BodyTypeDef namedSilentFail3 = DefDatabase<BodyTypeDef>.GetNamedSilentFail((__result.gender == Gender.Female) ? "Female" : "Male");
					if (namedSilentFail3 != null && __result.story.bodyType != namedSilentFail3)
					{
						__result.story.bodyType = namedSilentFail3;
						__result.Drawer?.renderer?.SetAllGraphicsDirty();
					}
				}
				else if (__result.story.bodyType == null)
				{
					BodyTypeDef namedSilentFail4 = DefDatabase<BodyTypeDef>.GetNamedSilentFail((__result.gender == Gender.Female) ? "Female" : "Male");
					if (namedSilentFail4 != null)
					{
						__result.story.bodyType = namedSilentFail4;
						__result.Drawer?.renderer?.SetAllGraphicsDirty();
					}
				}
				break;
			}
			if (text == "Avir_Strider")
			{
				List<Hediff> list = null;
				foreach (Hediff hediff in __result.health.hediffSet.hediffs)
				{
					if (hediff is Hediff_Injury hd && hd.IsPermanent())
					{
						if (list == null)
						{
							list = new List<Hediff>();
						}
						list.Add(hediff);
					}
				}
				if (list != null)
				{
					foreach (Hediff item in list)
					{
						__result.health.RemoveHediff(item);
					}
				}
			}
			switch (text)
			{
			case "Avir_Hatchling":
				MakeChild(__result, 4, 12);
				break;
			case "Avir_YoungHunter":
			{
				ClampAge(__result, 14, 22);
				BodyTypeDef namedSilentFail5 = DefDatabase<BodyTypeDef>.GetNamedSilentFail("Male");
				BodyTypeDef namedSilentFail6 = DefDatabase<BodyTypeDef>.GetNamedSilentFail("Female");
				BodyTypeDef bodyTypeDef = ((__result.gender == Gender.Female) ? namedSilentFail6 : namedSilentFail5);
				if (bodyTypeDef != null && __result.story.bodyType != bodyTypeDef)
				{
					__result.story.bodyType = bodyTypeDef;
					__result.Drawer?.renderer?.SetAllGraphicsDirty();
				}
				break;
			}
			case "Avir_Architect":
				ClampAge(__result, 23, 72);
				break;
			case "Avir_Artist":
				ClampAge(__result, 21, 42);
				break;
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] Body/age fix failed: " + ex, 4870597);
		}
	}

	private static void MakeChild(Pawn pawn, int minYears, int maxYears)
	{
		if (pawn.ageTracker == null)
		{
			return;
		}
		long num = (long)Rand.RangeInclusive(minYears, maxYears) * 3600000L + Rand.Range(0, 3600000);
		pawn.ageTracker.AgeBiologicalTicks = num;
		pawn.ageTracker.AgeChronologicalTicks = num;
		try
		{
			if (pawn.story != null)
			{
				Patch_AvirBackstories.ApplySkillGains(pawn, pawn.story.Adulthood, -1);
			}
			Patch_AvirBackstories.SetAdulthood(pawn, null);
		}
		catch
		{
		}
		BodyTypeDef namedSilentFail = DefDatabase<BodyTypeDef>.GetNamedSilentFail("Child");
		if (namedSilentFail != null && pawn.story != null)
		{
			pawn.story.bodyType = namedSilentFail;
		}
		pawn.Notify_DisabledWorkTypesChanged();
		pawn.Drawer?.renderer?.SetAllGraphicsDirty();
	}

	private static void ClampAge(Pawn pawn, int minYears, int maxYears)
	{
		if (pawn.ageTracker == null)
		{
			return;
		}
		long num = pawn.ageTracker.AgeBiologicalYears;
		if (num < minYears || num > maxYears)
		{
			long num2 = (long)Rand.RangeInclusive(minYears, maxYears) * 3600000L;
			num2 += Rand.Range(0, 3600000);
			pawn.ageTracker.AgeBiologicalTicks = num2;
			if (pawn.ageTracker.AgeChronologicalTicks < num2)
			{
				pawn.ageTracker.AgeChronologicalTicks = num2;
			}
			pawn.Drawer?.renderer?.SetAllGraphicsDirty();
		}
	}
}
