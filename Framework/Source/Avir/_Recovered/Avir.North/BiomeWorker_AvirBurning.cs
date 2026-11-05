using RimWorld;
using RimWorld.Planet;

namespace Avir.North;

public class BiomeWorker_AvirBurning : BiomeWorker_AvirTwilight
{
	public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile)
	{
		float score = base.GetScore(biome, tile, planetTile);
		if (score <= 0f)
		{
			return score;
		}
		BurningPlacement burningPlacement = biome.GetModExtension<BurningPlacement>() ?? new BurningPlacement();
		if (!(BiomeWorker_AvirNorth.ClusterAt(planetTile) >= burningPlacement.clusterAbove))
		{
			return 0f;
		}
		return burningPlacement.score;
	}
}
