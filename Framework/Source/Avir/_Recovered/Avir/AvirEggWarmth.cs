using RimWorld;
using Verse;

namespace Avir;

public static class AvirEggWarmth
{
	public const float NestFloor = -40f;

	public static bool IsAvirEgg(Thing t)
	{
		return t?.def?.defName == "Avir_Egg";
	}

	public static bool Nested(Thing egg)
	{
		if (!egg.Spawned)
		{
			return false;
		}
		Map map = egg.Map;
		if (egg.Position.GetThingList(map).Any((Thing t) => t is Building_AvirEggShelf))
		{
			return true;
		}
		Pawn pawn = CompAvirEggParents.Mother(egg);
		if (pawn != null && pawn.Spawned && pawn.Map == map)
		{
			return pawn.Position.InHorDistOf(egg.Position, 1.5f);
		}
		return false;
	}

	public static bool Chilling(Thing egg)
	{
		CompTemperatureRuinable compTemperatureRuinable = egg.TryGetComp<CompTemperatureRuinable>();
		if (compTemperatureRuinable == null || compTemperatureRuinable.Ruined || !egg.Spawned)
		{
			return false;
		}
		float ambientTemperature = egg.AmbientTemperature;
		if (ambientTemperature >= compTemperatureRuinable.Props.minSafeTemperature)
		{
			return false;
		}
		if (Nested(egg))
		{
			return !(ambientTemperature >= -40f);
		}
		return true;
	}
}
