using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Avir.Linarian;

public static class Patch_ForeignPanes
{
	static Patch_ForeignPanes()
	{
		try
		{
			Harmony harmony = new Harmony("avir.linarian.foreignpanes");
			MethodInfo methodInfo = AccessTools.Method(typeof(Patch_ForeignPanes), "SkipBool");
			MethodInfo methodInfo2 = AccessTools.Method(typeof(Patch_ForeignPanes), "SkipVoid");
			List<string> list = new List<string>();
			foreach (MethodBase item in Harmony.GetAllPatchedMethods().ToList())
			{
				if ((item.DeclaringType?.Name ?? "").IndexOf("Inspect", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				Patches patchInfo = Harmony.GetPatchInfo(item);
				if (patchInfo == null)
				{
					continue;
				}
				foreach (Patch item2 in patchInfo.Prefixes.Concat(patchInfo.Postfixes))
				{
					if ((item2.owner + " " + item2.PatchMethod.DeclaringType?.Assembly.GetName().Name).IndexOf("hud", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						MethodInfo patchMethod = item2.PatchMethod;
						harmony.Patch(patchMethod, new HarmonyMethod((patchMethod.ReturnType == typeof(bool)) ? methodInfo : methodInfo2));
						list.Add(patchMethod.DeclaringType?.FullName + "." + patchMethod.Name);
					}
				}
			}
			if (list.Count > 0)
			{
				Log.Message("[Avir] the beast's own pane is kept over: " + string.Join(", ", list));
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[Avir] could not look over foreign inspect panes: " + ex.Message);
		}
	}

	private static bool SelectedDisguised()
	{
		if (Find.Selector?.SingleSelectedThing is Pawn p)
		{
			return LinarianPresence.Disguised(p);
		}
		return false;
	}

	public static bool SkipBool(ref bool __result)
	{
		if (!SelectedDisguised())
		{
			return true;
		}
		__result = true;
		return false;
	}

	public static bool SkipVoid()
	{
		return !SelectedDisguised();
	}
}
