using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir;

[HarmonyPatch(typeof(ThinkNode_JobGiver), "TryIssueJobPackage")]
public static class Patch_AvirNoScavengedWeapons
{
	private static ModContentPack own;

	private static bool ownLooked;

	public static void Postfix(Pawn pawn, ref ThinkResult __result)
	{
		Job job = __result.Job;
		if (job == null || job.playerForced || !IsEquipJob(job.def) || !(job.targetA.Thing is ThingWithComps { Spawned: not false } thingWithComps) || !thingWithComps.def.IsWeapon || !AvirBody.IsAvir(pawn))
		{
			return;
		}
		if (!ownLooked)
		{
			ownLooked = true;
			own = LoadedModManager.RunningModsListForReading.Find((ModContentPack m) => m.assemblies.loadedAssemblies.Contains(typeof(Patch_AvirNoScavengedWeapons).Assembly));
		}
		if (own == null || thingWithComps.def.modContentPack != own)
		{
			__result = ThinkResult.NoJob;
		}
	}

	private static bool IsEquipJob(JobDef def)
	{
		if (def == JobDefOf.Equip)
		{
			return true;
		}
		string defName = def.defName;
		string text = def.driverClass?.Name ?? "";
		if (defName.IndexOf("Equip", StringComparison.OrdinalIgnoreCase) < 0 && defName.IndexOf("Sidearm", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return text.IndexOf("Equip", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return true;
	}
}
