using System.Collections.Generic;
using Avir.Artifactorics;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir;

public static class AvirTelekinesisUtility
{
	public const int TravelTicksPerTile = 2;

	public const int MinTravelTicks = 8;

	public const int MaxTravelTicks = 60;

	private const float GoodEnoughIngredientDistSq = 9f;

	private const float GoodEnoughBillTransferOriginDistSq = 16f;

	public const int RestTicks = 2500;

	private static readonly Dictionary<int, int> movedAt = new Dictionary<int, int>();

	private static readonly IntVec3[] EightNeighborOffsets = new IntVec3[8]
	{
		new IntVec3(0, 0, 1),
		new IntVec3(1, 0, 1),
		new IntVec3(1, 0, 0),
		new IntVec3(1, 0, -1),
		new IntVec3(0, 0, -1),
		new IntVec3(-1, 0, -1),
		new IntVec3(-1, 0, 0),
		new IntVec3(-1, 0, 1)
	};

	public static bool TryGetRadius(Pawn pawn, out float radius)
	{
		radius = 0f;
		if (pawn?.apparel == null)
		{
			return false;
		}
		bool flag = false;
		List<Apparel> wornApparel = pawn.apparel.WornApparel;
		for (int i = 0; i < wornApparel.Count; i++)
		{
			Comp_Telekinesis comp_Telekinesis = wornApparel[i].TryGetComp<Comp_Telekinesis>();
			if (comp_Telekinesis != null)
			{
				flag = true;
				float num = comp_Telekinesis.Props.radius + AmuletDevelopment.RadiusBonus(wornApparel[i].TryGetComp<CompAvirAmulet>());
				if (num > radius)
				{
					radius = num;
				}
			}
		}
		if (flag && AmuletDevelopment.SlotIs(pawn, AvirAmuletMakerDefOf.Avir_Subclass_Architect))
		{
			radius *= AmuletSettingsDef.Main?.wideReachFactor ?? 1.25f;
		}
		float num2 = ArtifactTelekinesis.Radius(pawn);
		if (num2 > 0f)
		{
			radius += num2;
			flag = true;
		}
		return flag;
	}

	public static bool CanHaul(Pawn pawn, Thing thing, int ownerKey)
	{
		if (pawn == null || thing == null || thing.Destroyed || !thing.Spawned || thing.Map != pawn.Map)
		{
			return false;
		}
		if (thing.Position.Fogged(pawn.Map))
		{
			return false;
		}
		if (!thing.def.EverHaulable || thing.def.isUnfinishedThing)
		{
			return false;
		}
		if (thing.IsForbidden(pawn))
		{
			return false;
		}
		if (AvirTelekinesisClaims.IsClaimedByOther(thing, ownerKey))
		{
			return false;
		}
		if (!pawn.CanReserve(thing, 1, thing.stackCount))
		{
			return false;
		}
		return true;
	}

	public static bool WithinRadius(IntVec3 origin, IntVec3 cell, float radius)
	{
		return (float)(cell - origin).LengthHorizontalSquared <= radius * radius;
	}

	public static bool TryFindStorageCellInRadius(Pawn pawn, Thing thing, IntVec3 origin, float radius, HashSet<IntVec3> excluded, out IntVec3 destination)
	{
		destination = IntVec3.Invalid;
		Map map = pawn.Map;
		if (map == null)
		{
			return false;
		}
		if (RitualHold.Held(thing))
		{
			return false;
		}
		StoragePriority storagePriority = StoreUtility.CurrentStoragePriorityOf(thing);
		List<SlotGroup> allGroupsListInPriorityOrder = map.haulDestinationManager.AllGroupsListInPriorityOrder;
		for (int i = 0; i < allGroupsListInPriorityOrder.Count; i++)
		{
			SlotGroup slotGroup = allGroupsListInPriorityOrder[i];
			if (slotGroup?.Settings == null || (int)slotGroup.Settings.Priority <= (int)storagePriority || !slotGroup.Settings.AllowedToAccept(thing))
			{
				continue;
			}
			IntVec3 intVec = IntVec3.Invalid;
			float num = float.MaxValue;
			List<IntVec3> cellsList = slotGroup.CellsList;
			for (int j = 0; j < cellsList.Count; j++)
			{
				IntVec3 intVec2 = cellsList[j];
				if ((excluded == null || !excluded.Contains(intVec2)) && WithinRadius(origin, intVec2, radius) && intVec2.IsValid && !intVec2.Fogged(map) && intVec2.IsValidStorageFor(map, thing))
				{
					float num2 = (intVec2 - thing.Position).LengthHorizontalSquared;
					if (num2 < num)
					{
						num = num2;
						intVec = intVec2;
					}
				}
			}
			if (intVec.IsValid)
			{
				destination = intVec;
				return true;
			}
		}
		return false;
	}

	public static bool TryFindHaulTransfer(Pawn pawn, IntVec3 origin, float radius, HashSet<Thing> excludedThings, HashSet<IntVec3> excludedDestinations, int ownerKey, out Avir_TeleTransfer transfer)
	{
		transfer = null;
		Map map = pawn.Map;
		if (map == null)
		{
			return false;
		}
		float num = radius * radius;
		Thing thing = null;
		IntVec3 destination = IntVec3.Invalid;
		float num2 = float.MaxValue;
		foreach (Thing item in map.listerHaulables.ThingsPotentiallyNeedingHauling())
		{
			if (!excludedThings.Contains(item) && CanHaul(pawn, item, ownerKey) && !((float)(item.Position - origin).LengthHorizontalSquared > num) && !JustMoved(item) && !StagedAtWantingBench(map, item, null) && TryFindStorageCellInRadius(pawn, item, origin, radius, excludedDestinations, out var destination2))
			{
				float num3 = (item.Position - origin).LengthHorizontalSquared;
				if (num3 < num2)
				{
					num2 = num3;
					thing = item;
					destination = destination2;
				}
			}
		}
		if (thing == null)
		{
			return false;
		}
		transfer = new Avir_TeleTransfer(thing, destination, thing.stackCount);
		return true;
	}

	public static bool TryFindBillIngredientTransfer(Pawn pawn, IntVec3 origin, float radius, HashSet<Thing> excludedThings, int ownerKey, out Avir_TeleTransfer transfer)
	{
		transfer = null;
		Map map = pawn.Map;
		if (map == null)
		{
			return false;
		}
		float num = radius * radius;
		List<Thing> list = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
		Thing thing = null;
		Thing thing2 = null;
		IntVec3 intVec = IntVec3.Invalid;
		float num2 = float.MaxValue;
		for (int i = 0; i < list.Count; i++)
		{
			Thing thing3 = list[i];
			if (!(thing3 is IBillGiver billGiver) || thing3.Map != map || (float)(thing3.Position - origin).LengthHorizontalSquared > num)
			{
				continue;
			}
			List<Bill> list2 = billGiver.BillStack?.Bills;
			if (list2 == null || list2.Count == 0)
			{
				continue;
			}
			for (int j = 0; j < list2.Count; j++)
			{
				if (!(list2[j] is Bill_Production { suspended: false } bill_Production) || !bill_Production.ShouldDoNow())
				{
					continue;
				}
				List<IngredientCount> list3 = bill_Production.recipe?.ingredients;
				if (list3 == null)
				{
					continue;
				}
				for (int k = 0; k < list3.Count; k++)
				{
					IngredientCount ingredientCount = list3[k];
					int num3 = Mathf.CeilToInt(ingredientCount.GetBaseCount());
					if (num3 <= 0)
					{
						continue;
					}
					int num4 = CountMatchingNear(map, thing3.Position, ingredientCount, 2);
					if (num4 >= num3)
					{
						continue;
					}
					int a = num3 - num4;
					Thing thing4 = FindBestIngredientCandidate(map, ingredientCount, thing3, origin, radius, excludedThings, ownerKey);
					if (thing4 == null || !TryFindDropCellNear(map, thing3.Position, out var dropCell))
					{
						continue;
					}
					float num5 = (thing4.Position - origin).LengthHorizontalSquared;
					if (num5 < num2)
					{
						num2 = num5;
						thing = thing4;
						thing2 = thing3;
						intVec = dropCell;
						transfer = new Avir_TeleTransfer(thing4, thing3, dropCell, Mathf.Min(a, thing4.stackCount));
						if (num2 <= 16f)
						{
							return true;
						}
					}
				}
			}
		}
		if (thing != null && thing2 != null)
		{
			return intVec.IsValid;
		}
		return false;
	}

	public static void NoteMoved(Thing t)
	{
		if (t == null)
		{
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		movedAt[t.thingIDNumber] = ticksGame;
		if (movedAt.Count <= 4000)
		{
			return;
		}
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, int> item in movedAt)
		{
			if (ticksGame - item.Value > 2500)
			{
				list.Add(item.Key);
			}
		}
		foreach (int item2 in list)
		{
			movedAt.Remove(item2);
		}
	}

	public static bool JustMoved(Thing t)
	{
		if (t != null && movedAt.TryGetValue(t.thingIDNumber, out var value))
		{
			return Find.TickManager.TicksGame - value < 2500;
		}
		return false;
	}

	public static bool StagedAtWantingBench(Map map, Thing thing, Thing except)
	{
		foreach (Thing item in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver))
		{
			if (item == except || !(item is IBillGiver billGiver) || (item.Position - thing.Position).LengthHorizontalSquared > 8)
			{
				continue;
			}
			List<Bill> list = billGiver.BillStack?.Bills;
			if (list == null)
			{
				continue;
			}
			foreach (Bill item2 in list)
			{
				if (!(item2 is Bill_Production { suspended: false } bill_Production) || bill_Production.recipe?.ingredients == null || !bill_Production.ShouldDoNow())
				{
					continue;
				}
				foreach (IngredientCount ingredient in bill_Production.recipe.ingredients)
				{
					if (ingredient.filter.Allows(thing) && bill_Production.ingredientFilter.Allows(thing))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static int CountMatchingNear(Map map, IntVec3 center, IngredientCount ingredientCount, int cellRadius)
	{
		int num = 0;
		foreach (IntVec3 item in GenRadial.RadialCellsAround(center, cellRadius, useCenter: true))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing thing = thingList[i];
				if (thing.def.EverHaulable && ingredientCount.filter.Allows(thing))
				{
					num += thing.stackCount;
				}
			}
		}
		return num;
	}

	private static Thing FindBestIngredientCandidate(Map map, IngredientCount ingredientCount, Thing bench, IntVec3 origin, float radius, HashSet<Thing> excludedThings, int ownerKey)
	{
		MapComponent_AvirTelekinesisCache component = map.GetComponent<MapComponent_AvirTelekinesisCache>();
		if (component == null)
		{
			return null;
		}
		float num = radius * radius;
		Thing result = null;
		float num2 = float.MaxValue;
		foreach (ThingDef allowedThingDef in ingredientCount.filter.AllowedThingDefs)
		{
			if (!component.HaulablesByDef.TryGetValue(allowedThingDef, out var value))
			{
				continue;
			}
			for (int i = 0; i < value.Count; i++)
			{
				Thing thing = value[i];
				if (excludedThings.Contains(thing) || thing.Map != map || thing.Destroyed || !thing.Spawned || !ingredientCount.filter.Allows(thing) || (float)(thing.Position - origin).LengthHorizontalSquared > num || thing.Position.Fogged(map) || thing.IsForbidden(Faction.OfPlayer) || AvirTelekinesisClaims.IsClaimedByOther(thing, ownerKey) || JustMoved(thing) || StagedAtWantingBench(map, thing, bench))
				{
					continue;
				}
				float num3 = (thing.Position - bench.Position).LengthHorizontalSquared;
				if (num3 < num2)
				{
					num2 = num3;
					result = thing;
					if (num2 <= 9f)
					{
						return result;
					}
				}
			}
		}
		return result;
	}

	public static bool TryFindDropCellNear(Map map, IntVec3 benchPosition, out IntVec3 dropCell)
	{
		for (int i = 0; i < EightNeighborOffsets.Length; i++)
		{
			IntVec3 intVec = benchPosition + EightNeighborOffsets[i];
			if (intVec.InBounds(map) && intVec.Standable(map) && !intVec.Fogged(map) && intVec.GetFirstItem(map) == null)
			{
				dropCell = intVec;
				return true;
			}
		}
		dropCell = IntVec3.Invalid;
		return false;
	}

	public static bool TryTeleportThingDirectly(Thing thing, IntVec3 destination, Map map, int count = -1)
	{
		if (thing == null || map == null || !thing.Spawned || thing.Map != map || !destination.IsValid)
		{
			return false;
		}
		IntVec3 position = thing.Position;
		if (position == destination)
		{
			return false;
		}
		Thing thing2 = thing;
		if (count > 0 && count < thing.stackCount)
		{
			thing2 = thing.SplitOff(count);
		}
		thing2.DeSpawn();
		if (!GenPlace.TryPlaceThing(thing2, destination, map, ThingPlaceMode.Direct, out var lastResultingThing) && !GenPlace.TryPlaceThing(thing2, destination, map, ThingPlaceMode.Near, out lastResultingThing))
		{
			GenPlace.TryPlaceThing(thing2, position, map, ThingPlaceMode.Near, out var _);
			return false;
		}
		FleckMaker.ThrowMicroSparks(WorldPosForCell(position), map);
		FleckMaker.ThrowMicroSparks(WorldPosForCell(lastResultingThing?.Position ?? destination), map);
		return true;
	}

	public static bool TryTeleportIngredientNear(Thing ingredient, Thing bench, Map map, int count = -1)
	{
		if (ingredient == null || bench == null || map == null || !ingredient.Spawned || ingredient.Map != map)
		{
			return false;
		}
		if (!TryFindDropCellNear(map, bench.Position, out var dropCell))
		{
			return false;
		}
		IntVec3 position = ingredient.Position;
		if (position == dropCell)
		{
			return false;
		}
		Thing thing = ingredient;
		if (count > 0 && count < ingredient.stackCount)
		{
			thing = ingredient.SplitOff(count);
		}
		thing.DeSpawn();
		if (!GenPlace.TryPlaceThing(thing, dropCell, map, ThingPlaceMode.Near, out var lastResultingThing))
		{
			GenPlace.TryPlaceThing(thing, position, map, ThingPlaceMode.Near, out lastResultingThing);
			return false;
		}
		FleckMaker.ThrowMicroSparks(WorldPosForCell(position), map);
		FleckMaker.ThrowMicroSparks(WorldPosForCell(thing.Position), map);
		return true;
	}

	public static Vector3 WorldPosForCell(IntVec3 cell)
	{
		Vector3 result = cell.ToVector3Shifted();
		result.y = AltitudeLayer.MetaOverlays.AltitudeFor();
		return result;
	}

	public static int CalculateTravelTicks(Vector3 start, Vector3 end)
	{
		return Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(start, end) * 2f), 8, 60);
	}
}
