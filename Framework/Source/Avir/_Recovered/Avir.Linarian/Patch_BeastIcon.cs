using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Widgets), "ThingIcon", new Type[]
{
	typeof(Rect),
	typeof(Thing),
	typeof(float),
	typeof(Rot4?),
	typeof(bool),
	typeof(float),
	typeof(bool)
})]
public static class Patch_BeastIcon
{
	public static bool Prefix(Rect rect, Thing thing, float alpha)
	{
		if (!(thing is Pawn pawn) || pawn.Faction == Faction.OfPlayer)
		{
			return true;
		}
		string text = LinarianVeil.InForm(pawn);
		if (text == null)
		{
			return true;
		}
		List<Graphic> list = LinarianFormGraphics.Layers(pawn, text);
		if (list.Count == 0)
		{
			return true;
		}
		Color color = GUI.color;
		foreach (Graphic item in list)
		{
			Material matEast = item.MatEast;
			if (!(matEast?.mainTexture == null))
			{
				Color color2 = matEast.color;
				color2.a *= alpha;
				GUI.color = color2;
				Widgets.DrawTextureFitted(rect, matEast.mainTexture, 1f);
			}
		}
		GUI.color = color;
		return false;
	}
}
