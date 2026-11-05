using System.Runtime.CompilerServices;
using Verse;

namespace Avir;

public static class ArcaneHungerCache
{
	private class Box
	{
		public int tick = int.MinValue;

		public Gene_ArcaneHunger gene;
	}

	public const int RefreshTicks = 30000;

	private static readonly ConditionalWeakTable<Pawn, Box> cache = new ConditionalWeakTable<Pawn, Box>();

	public static void Invalidate(Pawn pawn)
	{
		if (pawn != null)
		{
			cache.Remove(pawn);
		}
	}

	public static Gene_ArcaneHunger For(Pawn pawn)
	{
		if (pawn == null || pawn.genes == null)
		{
			return null;
		}
		Box value = cache.GetValue(pawn, (Pawn _) => new Box());
		int num = ((Find.TickManager != null) ? Find.TickManager.TicksGame : 0);
		if (value.tick == int.MinValue || num - value.tick >= 30000 || num < value.tick)
		{
			value.gene = pawn.genes.GetFirstGeneOfType<Gene_ArcaneHunger>();
			value.tick = num;
		}
		if (value.gene == null || !value.gene.Active)
		{
			return null;
		}
		return value.gene;
	}
}
