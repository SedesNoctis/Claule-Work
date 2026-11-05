using System;
using Verse;

namespace Avir.Linarian;

public static class HeraldParts
{
	[ThreadStatic]
	public static Pawn current;

	public static string Rename(BodyPartRecord part, string label)
	{
		if (current == null || part?.def == null || label == null || !Linarian.Is(current))
		{
			return label;
		}
		string defName = part.def.defName;
		if (!(defName == "Avir_Wing"))
		{
			if (defName == "Avir_Tail")
			{
				return label.Replace("tail", "double tail").Replace("Tail", "Double tail");
			}
			return label;
		}
		return label.Replace("wing", "pair of wings").Replace("Wing", "Pair of wings");
	}
}
