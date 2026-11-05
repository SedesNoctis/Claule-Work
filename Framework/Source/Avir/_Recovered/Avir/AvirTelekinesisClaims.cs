using System.Collections.Generic;
using Verse;

namespace Avir;

public static class AvirTelekinesisClaims
{
	private static readonly Dictionary<int, int> ClaimedBy = new Dictionary<int, int>();

	public static bool TryClaim(Thing thing, int ownerKey)
	{
		if (thing == null)
		{
			return false;
		}
		if (ClaimedBy.TryGetValue(thing.thingIDNumber, out var value) && value != ownerKey)
		{
			return false;
		}
		ClaimedBy[thing.thingIDNumber] = ownerKey;
		return true;
	}

	public static bool IsClaimedByOther(Thing thing, int ownerKey)
	{
		if (thing != null && ClaimedBy.TryGetValue(thing.thingIDNumber, out var value))
		{
			return value != ownerKey;
		}
		return false;
	}

	public static void Release(Thing thing, int ownerKey)
	{
		if (thing != null && ClaimedBy.TryGetValue(thing.thingIDNumber, out var value) && value == ownerKey)
		{
			ClaimedBy.Remove(thing.thingIDNumber);
		}
	}

	public static void ReleaseAllForOwner(int ownerKey)
	{
		List<int> list = null;
		foreach (KeyValuePair<int, int> item in ClaimedBy)
		{
			if (item.Value == ownerKey)
			{
				(list ?? (list = new List<int>())).Add(item.Key);
			}
		}
		if (list != null)
		{
			for (int i = 0; i < list.Count; i++)
			{
				ClaimedBy.Remove(list[i]);
			}
		}
	}
}
