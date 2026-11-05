using System.Collections.Generic;
using Avir.Linarian;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

public static class ArtifactWearCache
{
	private class Entry
	{
		public int tick = -99999;

		public readonly List<CompArtifactSocket> list = new List<CompArtifactSocket>();
	}

	private static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();

	private static readonly List<CompArtifactSocket> empty = new List<CompArtifactSocket>();

	private static readonly List<CompArtifactSocket> scratch = new List<CompArtifactSocket>();

	private const int Interval = 250;

	public static void Invalidate(Pawn p)
	{
		if (p != null)
		{
			cache.Remove(p.thingIDNumber);
		}
	}

	public static List<CompArtifactSocket> Worn(Pawn p)
	{
		if (p == null || (p.apparel == null && p.equipment == null))
		{
			return empty;
		}
		if (Avir.Linarian.Linarian.Is(p))
		{
			return empty;
		}
		int num = Find.TickManager?.TicksGame ?? 0;
		if (!cache.TryGetValue(p.thingIDNumber, out var value))
		{
			if (cache.Count > 2000)
			{
				cache.Clear();
			}
			value = new Entry();
			cache[p.thingIDNumber] = value;
		}
		if (num - value.tick >= 250 || num < value.tick)
		{
			value.tick = num;
			value.list.Clear();
			List<Apparel> list = p.apparel?.WornApparel;
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					CompArtifactSocket compArtifactSocket = list[i].Socket();
					if (compArtifactSocket?.inlaid != null)
					{
						value.list.Add(compArtifactSocket);
					}
				}
			}
			CompArtifactSocket compArtifactSocket2 = p.equipment?.Primary?.Socket();
			if (compArtifactSocket2?.inlaid != null)
			{
				value.list.Add(compArtifactSocket2);
			}
		}
		if (value.list.Count == 0)
		{
			return value.list;
		}
		scratch.Clear();
		for (int j = 0; j < value.list.Count; j++)
		{
			CompArtifactSocket compArtifactSocket3 = value.list[j];
			if (compArtifactSocket3.inlaid == null)
			{
				continue;
			}
			bool num2;
			if (!(compArtifactSocket3.parent is Apparel apparel))
			{
				if (p.equipment == null)
				{
					continue;
				}
				num2 = p.equipment.Primary == compArtifactSocket3.parent;
			}
			else
			{
				num2 = apparel.Wearer == p;
			}
			if (num2)
			{
				scratch.Add(compArtifactSocket3);
			}
		}
		return scratch;
	}
}
