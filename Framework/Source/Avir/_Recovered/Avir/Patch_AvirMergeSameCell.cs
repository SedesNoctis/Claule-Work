using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir;

[HarmonyPatch(typeof(WorkGiver_Merge), "JobOnThing")]
public static class Patch_AvirMergeSameCell
{
	public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
	{
		if (__result == null || t == null || __result.targetB.Cell != t.Position)
		{
			return;
		}
		__result = null;
		try
		{
			ISlotGroup slotGroup = t.GetSlotGroup();
			if (slotGroup == null)
			{
				return;
			}
			ISlotGroup storageGroup = slotGroup.StorageGroup;
			slotGroup = storageGroup ?? slotGroup;
			foreach (Thing heldThing in slotGroup.HeldThings)
			{
				if (heldThing != t && !(heldThing.Position == t.Position) && heldThing.CanStackWith(t) && (forced || heldThing.stackCount >= t.stackCount) && heldThing.stackCount < heldThing.def.stackLimit && !heldThing.IsForbidden(pawn.Faction) && heldThing.Position.InAllowedArea(pawn) && pawn.CanReserve(heldThing.Position, 1, -1, null, forced) && pawn.CanReserve(heldThing) && heldThing.Position.IsValidStorageFor(heldThing.Map, t) && !heldThing.Position.ContainsStaticFire(heldThing.Map))
				{
					Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, t, heldThing.Position);
					job.count = Mathf.Min(heldThing.def.stackLimit - heldThing.stackCount, t.stackCount);
					job.haulMode = HaulMode.ToCellStorage;
					__result = job;
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] merge same-cell guard failed: " + ex.Message, 4870928);
		}
	}
}
