using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Avir;

public class MapComponent_AvirExtractUpgrade : MapComponent
{
	public MapComponent_AvirExtractUpgrade(Map map)
		: base(map)
	{
	}

	public override void FinalizeInit()
	{
		base.FinalizeInit();
		try
		{
			ThingDef extract = AvirExtractMedicine.Extract;
			if (extract == null || !typeof(Medicine).IsAssignableFrom(extract.thingClass))
			{
				return;
			}
			int num = 0;
			foreach (Thing item in (from t in map.listerThings.ThingsOfDef(extract)
				where !(t is Medicine) && t.Spawned
				select t).ToList())
			{
				IntVec3 position = item.Position;
				int stackCount = item.stackCount;
				bool flag = item.IsForbidden(Faction.OfPlayer);
				item.Destroy();
				Thing thing = ThingMaker.MakeThing(extract);
				thing.stackCount = stackCount;
				GenSpawn.Spawn(thing, position, map);
				if (flag)
				{
					thing.SetForbidden(value: true, warnOnFail: false);
				}
				num++;
			}
			if (num > 0)
			{
				Log.Message("[Avir] " + num + " stack(s) of essence extract remade as medicine.");
			}
		}
		catch (Exception ex)
		{
			Log.Warning("[Avir] remaking extract as medicine failed: " + ex.Message);
		}
	}
}
