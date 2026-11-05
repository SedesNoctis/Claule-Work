using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Avir;

[StaticConstructorOnStartup]
public static class AvirCEPartArmor
{
	private static readonly GeneDef Fur;

	public const float SoftFactor = 0.5f;

	static AvirCEPartArmor()
	{
		Fur = DefDatabase<GeneDef>.GetNamedSilentFail("Avir_FeatheredFur");
		Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly("CombatExtended.ArmorUtilityCE");
		if (typeInAnyAssembly == null || Fur == null)
		{
			return;
		}
		try
		{
			Harmony harmony = new Harmony("avir.ce.partarmor");
			MethodInfo[] methods = typeInAnyAssembly.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo in methods)
			{
				ParameterInfo[] parameters = methodInfo.GetParameters();
				if (!(methodInfo.Name != "PartialStat") && !(methodInfo.ReturnType != typeof(float)) && parameters.Length == 3 && !(parameters[0].ParameterType != typeof(Pawn)) && !(parameters[2].ParameterType != typeof(BodyPartRecord)))
				{
					harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(AvirCEPartArmor), "Postfix"));
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[Avir] CE part armour patch failed: " + ex.Message);
		}
	}

	public static void Postfix(Pawn __0, BodyPartRecord __2, ref float __result)
	{
		if (!(__result <= 0f) && __2 != null && __0?.genes != null && __0.genes.HasActiveGene(Fur) && Soft(__2))
		{
			__result *= 0.5f;
		}
	}

	private static bool Soft(BodyPartRecord part)
	{
		if (part.def == AvirBody.Claw || part.def == AvirBody.Talon)
		{
			return false;
		}
		for (BodyPartRecord bodyPartRecord = part; bodyPartRecord != null; bodyPartRecord = bodyPartRecord.parent)
		{
			switch (bodyPartRecord.def.defName)
			{
			case "Avir_Wing":
			case "Avir_Tail":
			case "Leg":
			case "Foot":
			case "Toe":
				return true;
			}
		}
		return false;
	}
}
