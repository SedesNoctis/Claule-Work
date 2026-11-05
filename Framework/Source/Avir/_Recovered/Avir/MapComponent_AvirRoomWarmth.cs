using System;
using System.Collections.Generic;
using Verse;

namespace Avir;

public class MapComponent_AvirRoomWarmth : MapComponent
{
	private List<int> cells = new List<int>();

	private List<float> temps = new List<float>();

	private int watchTicks;

	private int fixedTotal;

	public MapComponent_AvirRoomWarmth(Map map)
		: base(map)
	{
	}

	public override void ExposeData()
	{
		base.ExposeData();
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			cells = new List<int>();
			temps = new List<float>();
			HashSet<Room> hashSet = new HashSet<Room>();
			foreach (Room allRoom in map.regionGrid.AllRooms)
			{
				if (allRoom == null || allRoom.UsesOutdoorTemperature || allRoom.CellCount == 0 || !hashSet.Add(allRoom))
				{
					continue;
				}
				IntVec3 c = IntVec3.Invalid;
				using (IEnumerator<IntVec3> enumerator2 = allRoom.Cells.GetEnumerator())
				{
					if (enumerator2.MoveNext())
					{
						c = enumerator2.Current;
					}
				}
				if (c.IsValid)
				{
					cells.Add(map.cellIndices.CellToIndex(c));
					temps.Add(allRoom.Temperature);
				}
			}
		}
		Scribe_Collections.Look(ref cells, "avirRoomCells", LookMode.Value);
		Scribe_Collections.Look(ref temps, "avirRoomTemps", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (cells == null)
			{
				cells = new List<int>();
			}
			if (temps == null)
			{
				temps = new List<float>();
			}
			watchTicks = ((cells.Count > 0 && cells.Count == temps.Count) ? 600 : 0);
		}
	}

	public override void MapComponentTick()
	{
		if (watchTicks <= 0)
		{
			return;
		}
		watchTicks--;
		if (watchTicks % 15 != 0)
		{
			return;
		}
		int num = 0;
		for (int i = 0; i < cells.Count; i++)
		{
			if (cells[i] >= 0 && cells[i] < map.cellIndices.NumGridCells)
			{
				Room room = map.cellIndices.IndexToCell(cells[i]).GetRoom(map);
				if (room != null && !room.UsesOutdoorTemperature && !(Math.Abs(room.Temperature - temps[i]) < 5f))
				{
					room.Temperature = temps[i];
					num++;
				}
			}
		}
		fixedTotal += num;
		if (watchTicks == 0 && fixedTotal > 0)
		{
			Log.Message("[Avir] Put back the saved temperature of rooms that came up wrong after loading (" + fixedTotal + " corrections).");
		}
	}
}
