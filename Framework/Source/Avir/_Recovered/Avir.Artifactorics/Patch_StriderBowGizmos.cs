using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Pawn), "GetGizmos")]
public static class Patch_StriderBowGizmos
{
	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		Pawn p = __instance;
		if (p?.Faction != Faction.OfPlayer || !p.IsColonistPlayerControlled)
		{
			yield break;
		}
		CompStriderBow b = StriderPaths.Drawn(p);
		if (b == null)
		{
			yield break;
		}
		yield return new Gizmo_BowCharge
		{
			bow = b
		};
		if (b.path == null && b.Stones >= 1)
		{
			yield return new Command_Action
			{
				defaultLabel = "Choose the bow's path",
				defaultDesc = "All eighteen stones of this bow go into one path, chosen now and never changed.",
				icon = (ContentFinder<Texture2D>.Get(b.parent.def.graphicData.texPath, reportFailure: false) ?? BaseContent.BadTex),
				action = delegate
				{
					Find.WindowStack.Add(new Dialog_StriderBowPaths(b, p));
				}
			};
		}
		if (b.path != null && b.Stones >= 18)
		{
			yield return StriderActives.Command(p, b);
		}
	}
}
