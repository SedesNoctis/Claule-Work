using System.Collections.Generic;
using Verse;

namespace Avir;

public class GameComponent_AvirEssenceDrain : GameComponent
{
	private const int Interval = 2500;

	public GameComponent_AvirEssenceDrain(Game game)
	{
	}

	public override void GameComponentTick()
	{
		if ((Find.TickManager.TicksGame + 1250) % 2500 != 0)
		{
			return;
		}
		List<Pawn> list = AvirPawnScan.AllLivePawns();
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (AvirSimTier.ProcessOnThisPass(pawn, 2500))
			{
				Gene_ArcaneHunger gene_ArcaneHunger = Gene_ArcaneHunger.Of(pawn);
				if (gene_ArcaneHunger != null && gene_ArcaneHunger.Active)
				{
					gene_ArcaneHunger.DrainFor(AvirSimTier.ScaleInterval(pawn, 2500));
				}
			}
		}
	}
}
