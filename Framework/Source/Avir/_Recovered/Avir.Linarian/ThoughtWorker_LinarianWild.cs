using RimWorld;
using Verse;

namespace Avir.Linarian;

public class ThoughtWorker_LinarianWild : ThoughtWorker
{
	protected override ThoughtState CurrentStateInternal(Pawn p)
	{
		if (!Linarian.Is(p) || !p.Spawned)
		{
			return ThoughtState.Inactive;
		}
		Room room = p.GetRoom();
		if (room == null || room.PsychologicallyOutdoors)
		{
			return ThoughtState.ActiveAtStage(0);
		}
		TerrainDef terrain = p.Position.GetTerrain(p.Map);
		if (terrain != null && terrain.natural && p.Position.Roofed(p.Map))
		{
			RoofDef roofDef = p.Map.roofGrid.RoofAt(p.Position);
			if (roofDef != null && roofDef.isNatural)
			{
				return ThoughtState.ActiveAtStage(0);
			}
		}
		return ThoughtState.Inactive;
	}
}
