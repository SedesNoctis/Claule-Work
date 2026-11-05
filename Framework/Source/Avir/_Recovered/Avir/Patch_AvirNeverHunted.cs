using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(ThoughtUtility), "CanGetThought")]
public static class Patch_AvirNeverHunted
{
	private static readonly Dictionary<ThoughtDef, bool> hunted = new Dictionary<ThoughtDef, bool>();

	public static void Postfix(Pawn pawn, ThoughtDef def, ref bool __result)
	{
		if (__result && def != null && pawn != null && IsHunted(def) && AvirBody.IsAvir(pawn))
		{
			__result = false;
		}
	}

	private static bool IsHunted(ThoughtDef def)
	{
		if (!hunted.TryGetValue(def, out var value))
		{
			value = (hunted[def] = Compute(def));
		}
		return value;
	}

	private static bool Compute(ThoughtDef def)
	{
		if (def.defName.IndexOf("Hunted", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		if (string.Equals(def.label, "hunted", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (def.stages != null)
		{
			return def.stages.Any((ThoughtStage s) => s != null && string.Equals(s.label, "hunted", StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}

	[DebugAction("Avir", "List thoughts of selected pawn", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
	private static void ListThoughts()
	{
		Pawn pawn = Find.Selector.SingleSelectedThing as Pawn;
		if (pawn?.needs?.mood == null)
		{
			Messages.Message("Select one pawn with a mood.", MessageTypeDefOf.RejectInput, historical: false);
			return;
		}
		StringBuilder stringBuilder = new StringBuilder("[Avir thoughts] " + pawn.LabelShort + ":\n");
		List<Thought> list = new List<Thought>();
		pawn.needs.mood.thoughts.GetAllMoodThoughts(list);
		foreach (Thought item in list)
		{
			stringBuilder.AppendLine("  " + item.LabelCap + " = " + item.MoodOffset() + "  [" + item.def.defName + ", " + item.GetType().Name + ", mod: " + (item.def.modContentPack?.Name ?? "?") + "]" + ((!(item is Thought_Memory { otherPawn: not null } thought_Memory)) ? "" : (" other: " + thought_Memory.otherPawn.LabelShort)));
		}
		Log.Message(stringBuilder.ToString());
		Messages.Message("Avir: thoughts written to the log.", MessageTypeDefOf.NeutralEvent, historical: false);
	}
}
