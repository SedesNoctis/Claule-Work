using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir;

[StaticConstructorOnStartup]
public static class AvirLovinGate
{
	private static readonly GeneDef Fearborn;

	private static NeedDef sexNeed;

	static AvirLovinGate()
	{
		Fearborn = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
		sexNeed = DefDatabase<NeedDef>.GetNamedSilentFail("Sex");
		Harmony harmony = new Harmony("avir.lovingate");
		string[] array = new string[4] { "RimWorld.JobGiver_DoLovin", "rjw.JobGiver_JoinInBed", "rjw.JobGiver_DoQuickie", "rjw.JobGiver_DoFappin" };
		foreach (string text in array)
		{
			Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly(text);
			MethodInfo methodInfo = ((typeInAnyAssembly == null) ? null : AccessTools.DeclaredMethod(typeInAnyAssembly, "TryGiveJob"));
			if (!(methodInfo == null))
			{
				try
				{
					harmony.Patch(methodInfo, new HarmonyMethod(typeof(AvirLovinGate), "Prefix"));
				}
				catch (Exception ex)
				{
					Log.Warning("[Avir] lovin' gate on " + text + " failed: " + ex.Message);
				}
			}
		}
	}

	public static bool Sated(Pawn p)
	{
		if (p == null || sexNeed == null || Fearborn == null || p.genes == null || !p.genes.HasActiveGene(Fearborn))
		{
			return false;
		}
		Need need = p.needs?.TryGetNeed(sexNeed);
		if (need != null)
		{
			return need.CurLevel >= (AvirMod.Settings?.avirLovinThreshold ?? 0.75f);
		}
		return false;
	}

	public static bool Prefix(Pawn __0, ref Job __result)
	{
		if (Sated(__0) || Sated(LovePartnerRelationUtility.GetPartnerInMyBed(__0)))
		{
			__result = null;
			return false;
		}
		return true;
	}
}
