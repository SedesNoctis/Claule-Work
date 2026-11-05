using System.Collections.Generic;
using Verse;

namespace Avir.North;

public static class WaterNearness
{
	private static readonly Dictionary<(Map, int), (int tick, bool[] grid)> cache = new Dictionary<(Map, int), (int, bool[])>();

	public static bool Near(Map map, IntVec3 c, int dist)
	{
		if (!c.InBounds(map))
		{
			return false;
		}
		int num = Find.TickManager?.TicksGame ?? 0;
		if (!cache.TryGetValue((map, dist), out (int, bool[]) value) || value.Item2.Length != map.cellIndices.NumGridCells || num - value.Item1 > 60000 || num < value.Item1)
		{
			if (cache.Count > 16)
			{
				cache.Clear();
			}
			value = (num, Build(map, dist));
			cache[(map, dist)] = value;
		}
		return value.Item2[map.cellIndices.CellToIndex(c)];
	}

	private static bool[] Build(Map map, int dist)
	{
		bool[] array = new bool[map.cellIndices.NumGridCells];
		List<IntVec3> list = new List<IntVec3>();
		foreach (IntVec3 allCell in map.AllCells)
		{
			if (allCell.GetTerrain(map).IsWater)
			{
				array[map.cellIndices.CellToIndex(allCell)] = true;
				list.Add(allCell);
			}
		}
		for (int i = 0; i < dist; i++)
		{
			if (list.Count <= 0)
			{
				break;
			}
			List<IntVec3> list2 = new List<IntVec3>();
			foreach (IntVec3 item in list)
			{
				IntVec3[] cardinalDirections = GenAdj.CardinalDirections;
				foreach (IntVec3 intVec in cardinalDirections)
				{
					IntVec3 intVec2 = item + intVec;
					if (intVec2.InBounds(map))
					{
						int num = map.cellIndices.CellToIndex(intVec2);
						if (!array[num])
						{
							array[num] = true;
							list2.Add(intVec2);
						}
					}
				}
			}
			list = list2;
		}
		return array;
	}
}
