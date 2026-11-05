using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Pawn), "GetGizmos")]
public static class Patch_SphereGizmos
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
		CompArchitectSpheres c = SpherePaths.Of(p);
		if (c == null)
		{
			yield break;
		}
		yield return new Command_Action
		{
			defaultLabel = ((c.path == null) ? "Spheres: choose a path" : ("Spheres: " + SpherePaths.Label(c.path))),
			defaultDesc = "The constellation of the control spheres: their path and the stones it has gained.",
			icon = (((c.path != null) ? SphereIcons.Path(c.path) : null) ?? ContentFinder<Texture2D>.Get(c.parent.def.graphicData.texPath, reportFailure: false) ?? BaseContent.BadTex),
			action = delegate
			{
				Find.WindowStack.Add(new Dialog_SphereConstellation(c, p));
			}
		};
		if (c.path == null)
		{
			yield break;
		}
		foreach (Command item2 in SphereActives.Commands(p, c))
		{
			yield return item2;
		}
	}
}
