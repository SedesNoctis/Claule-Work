using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Pawn), "GetGizmos")]
public static class Patch_AvirTonicPrisonerToggle
{
	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		Pawn p = __instance;
		if (p != null && p.IsPrisonerOfColony && AvirTonic.Tonic != null && AvirBody.IsAvir(p))
		{
			yield return new Command_Toggle
			{
				defaultLabel = "Allow tonics",
				defaultDesc = "Let your doctors give this prisoner immune tonics when it falls ill.",
				icon = AvirTonic.Tonic.uiIcon,
				isActive = () => GameComponent_AvirTonics.Allowed(p),
				toggleAction = delegate
				{
					GameComponent_AvirTonics.Set(p, !GameComponent_AvirTonics.Allowed(p));
				}
			};
		}
	}
}
