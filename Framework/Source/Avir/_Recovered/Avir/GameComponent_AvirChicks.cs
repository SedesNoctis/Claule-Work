using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Avir;

public class GameComponent_AvirChicks : GameComponent
{
	private List<Pawn> chicks = new List<Pawn>();

	private List<Pawn> carriers = new List<Pawn>();

	private List<Pawn> autoOff = new List<Pawn>();

	public override void GameComponentTick()
	{
		if (Find.TickManager.TicksGame % 250 != 0)
		{
			return;
		}
		foreach (Map map in Find.Maps)
		{
			AvirChicks.AutoTick(map);
		}
	}

	public GameComponent_AvirChicks(Game game)
	{
		AvirChicks.Clear();
	}

	public override void ExposeData()
	{
		base.ExposeData();
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			chicks = AvirChicks.CarrierOf.Keys.ToList();
			carriers = chicks.Select((Pawn c) => AvirChicks.CarrierOf[c]).ToList();
		}
		Scribe_Collections.Look(ref chicks, "chicks", LookMode.Reference);
		Scribe_Collections.Look(ref carriers, "carriers", LookMode.Reference);
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			autoOff = AvirChicks.AutoOff.Where((Pawn x) => x != null && !x.Destroyed).ToList();
		}
		Scribe_Collections.Look(ref autoOff, "chickAutoOff", LookMode.Reference);
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		AvirChicks.Clear();
		if (chicks != null && carriers != null)
		{
			for (int i = 0; i < chicks.Count && i < carriers.Count; i++)
			{
				if (chicks[i] != null && carriers[i] != null)
				{
					AvirChicks.CarrierOf[chicks[i]] = carriers[i];
					if (!AvirChicks.ChicksOf.TryGetValue(carriers[i], out var value))
					{
						value = (AvirChicks.ChicksOf[carriers[i]] = new List<Pawn>());
					}
					value.Add(chicks[i]);
				}
			}
		}
		if (autoOff == null)
		{
			return;
		}
		foreach (Pawn item in autoOff)
		{
			if (item != null)
			{
				AvirChicks.AutoOff.Add(item);
			}
		}
	}
}
