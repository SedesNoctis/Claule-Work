using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avir;
using HarmonyLib;
using Verse;
using Verse.AI.Group;

namespace FlightGene;

public static class Patch_AvirTrappedTrigger
{
	public static void Apply(Harmony harmony)
	{
		try
		{
			int num = 0;
			Type[] types = typeof(Trigger).Assembly.GetTypes();
			foreach (Type type in types)
			{
				if (!type.IsAbstract && typeof(Trigger).IsAssignableFrom(type) && type.Name.IndexOf("MapEdge", StringComparison.OrdinalIgnoreCase) >= 0 && type.Name.IndexOf("Cannot", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					MethodInfo method = type.GetMethod("ActivateOn", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (!(method == null) && !(method.ReturnType != typeof(bool)))
					{
						harmony.Patch(method, null, new HarmonyMethod(typeof(Patch_AvirTrappedTrigger), "ActivateOn_Postfix"));
						num++;
						Log.Message("[Avir trapped] bound to " + type.Name + ".ActivateOn.");
					}
				}
			}
			if (num == 0)
			{
				Log.Warning("[Avir trapped] no map-edge trigger found: Avir traders may still be reported as trapped.");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[Avir trapped] failed to apply: " + ex);
		}
	}

	public static void ActivateOn_Postfix(Lord lord, ref bool __result)
	{
		if (!__result || lord == null)
		{
			return;
		}
		try
		{
			bool flag = false;
			bool flag2 = false;
			List<Pawn> ownedPawns = lord.ownedPawns;
			for (int i = 0; i < ownedPawns.Count; i++)
			{
				Pawn pawn = ownedPawns[i];
				if (pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed && pawn.Map != null)
				{
					if (IsFreeAvir(pawn))
					{
						flag2 = true;
					}
					else if (!pawn.Map.reachability.CanReachMapEdge(pawn.Position, TraverseParms.For(pawn)))
					{
						flag = true;
					}
				}
			}
			if (flag2 && !flag)
			{
				__result = false;
			}
			else if (AvirFactionRelations.IsOrder(lord.faction) && FreeTheTrapped(lord))
			{
				__result = false;
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir trapped] postfix failed: " + ex, 4870769);
		}
	}

	private static bool FreeTheTrapped(Lord lord)
	{
		bool result = true;
		foreach (Pawn item in lord.ownedPawns.ToList())
		{
			if (item == null || !item.Spawned || item.Dead || item.Downed || item.Map == null || IsFreeAvir(item) || item.Map.reachability.CanReachMapEdge(item.Position, TraverseParms.For(item)))
			{
				continue;
			}
			Map map = item.Map;
			IntVec3 position = item.Position;
			IntVec3 intVec = IntVec3.Invalid;
			foreach (IntVec3 item2 in GenRadial.RadialCellsAround(position, 40f, useCenter: false))
			{
				if (item2.InBounds(map) && item2.Standable(map) && !item2.Fogged(map) && map.reachability.CanReachMapEdge(item2, TraverseParms.For(TraverseMode.PassDoors)))
				{
					intVec = item2;
					break;
				}
			}
			if (!intVec.IsValid)
			{
				result = false;
				string[] obj = new string[7]
				{
					"[Avir trapped] ",
					item.LabelShort,
					" of ",
					lord.faction?.Name,
					" is shut in at ",
					null,
					null
				};
				IntVec3 intVec2 = position;
				obj[5] = intVec2.ToString();
				obj[6] = " and no open ground was found within 40 cells.";
				Log.Message(string.Concat(obj));
			}
			else
			{
				item.pather?.StopDead();
				item.Position = intVec;
				item.Notify_Teleported(endCurrentJob: true, resetTweenedPos: false);
				string[] obj2 = new string[11]
				{
					"[Avir trapped] ",
					item.LabelShort,
					" of ",
					lord.faction?.Name,
					" was shut in at ",
					null,
					null,
					null,
					null,
					null,
					null
				};
				IntVec3 intVec2 = position;
				obj2[5] = intVec2.ToString();
				obj2[6] = " (";
				obj2[7] = position.GetRoof(map)?.defName ?? "no roof";
				obj2[8] = "); brought out to ";
				intVec2 = intVec;
				obj2[9] = intVec2.ToString();
				obj2[10] = " instead of the group leaving.";
				Log.Message(string.Concat(obj2));
			}
		}
		return result;
	}

	private static bool IsFreeAvir(Pawn p)
	{
		if (!AvirFlightUtility.CarriesFlightGene(p))
		{
			return false;
		}
		Map map = p.Map;
		IntVec3 position = p.Position;
		if (!position.InBounds(map))
		{
			return false;
		}
		if (AvirFlightRules.CannotStandAt(map, position))
		{
			return true;
		}
		Room room = position.GetRoom(map);
		if (room != null && room.ProperRoom)
		{
			return room.PsychologicallyOutdoors;
		}
		return true;
	}
}
