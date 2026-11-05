using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

[StaticConstructorOnStartup]
public static class ShadowsongReservoir
{
	private static bool inner;

	private static readonly Dictionary<int, int> lastSpend;

	static ShadowsongReservoir()
	{
		lastSpend = new Dictionary<int, int>();
		Type type = AccessTools.TypeByName("PipeSystem.CompAdvancedResourceProcessor");
		if (type == null)
		{
			return;
		}
		Harmony harmony = new Harmony("avir.shadowsongreservoir");
		string[] array = new string[4] { "CompTick", "CompTickRare", "CompTickLong", "CompTickInterval" };
		foreach (string name in array)
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(type, name);
			if (!(methodInfo == null))
			{
				harmony.Patch(methodInfo, new HarmonyMethod(typeof(ShadowsongReservoir), "Prefix"), new HarmonyMethod(typeof(ShadowsongReservoir), "Postfix"));
			}
		}
	}

	public static bool Prefix(ThingComp __instance)
	{
		CompArtifactHost compArtifactHost = __instance.parent?.TryGetComp<CompArtifactHost>();
		if (compArtifactHost != null)
		{
			return compArtifactHost.Working > 0;
		}
		return true;
	}

	public static void Postfix(ThingComp __instance, MethodBase __originalMethod, object[] __args)
	{
		if (inner)
		{
			return;
		}
		CompArtifactHost compArtifactHost = __instance.parent?.TryGetComp<CompArtifactHost>();
		if (compArtifactHost == null || compArtifactHost.Working == 0)
		{
			return;
		}
		float num = 0.3f * (float)compArtifactHost.Working * Mathf.Max(0.5f, compArtifactHost.Power);
		int num2 = Mathf.FloorToInt(num);
		int num3 = num2 + (Rand.Chance(num - (float)num2) ? 1 : 0);
		if (num3 == 0)
		{
			return;
		}
		inner = true;
		try
		{
			for (int i = 0; i < num3; i++)
			{
				__originalMethod.Invoke(__instance, __args);
			}
			int ticksGame = Find.TickManager.TicksGame;
			int thingIDNumber = __instance.parent.thingIDNumber;
			if (!lastSpend.TryGetValue(thingIDNumber, out var value) || ticksGame - value >= 250)
			{
				lastSpend[thingIDNumber] = ticksGame;
				compArtifactHost.Spend(0.01f * num);
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] shadowsong reservoir: " + ex.Message, 81723401);
		}
		finally
		{
			inner = false;
		}
	}
}
