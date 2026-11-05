using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Avir;

[HarmonyPatch(typeof(Pawn_PathFollower), "PatherFailed")]
public static class Patch_AvirIdleWatch_PatherFailed
{
	private static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> pawnRef = AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");

	public static void Prefix(Pawn_PathFollower __instance)
	{
		try
		{
			Pawn pawn = pawnRef(__instance);
			if (AvirIdleWatch.Watched(pawn))
			{
				LocalTargetInfo destination = __instance.Destination;
				string text = "to " + (destination.HasThing ? (destination.Thing.LabelShort + "@" + destination.Thing.PositionHeld.ToString()) : destination.Cell.ToString()) + " from " + pawn.Position.ToString() + ", request found " + (__instance.curPathRequest?.Found?.ToString() ?? "no request") + ", reachable on foot " + (destination.IsValid && pawn.Map.reachability.CanReach(pawn.Position, destination, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors))) + ", standable here " + pawn.Position.Standable(pawn.Map) + "; " + AvirIdleWatch.FlightState(pawn);
				AvirIdleWatch.NotePather(pawn, text);
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir idle] pather watch failed: " + ex.Message, 4870914);
		}
	}
}
