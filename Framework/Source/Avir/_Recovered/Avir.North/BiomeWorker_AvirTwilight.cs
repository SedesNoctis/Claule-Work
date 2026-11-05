using RimWorld;
using RimWorld.Planet;

namespace Avir.North;

public class BiomeWorker_AvirTwilight : BiomeWorker
{
	public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile)
	{
		if (tile.WaterCovered)
		{
			return -100f;
		}
		TwilightPlacement twilightPlacement = biome.GetModExtension<TwilightPlacement>() ?? new TwilightPlacement();
		if (BiomeWorker_AvirNorth.CoverageOff)
		{
			return 0f;
		}
		float num = BiomeWorker_AvirNorth.NoiseAt(planetTile);
		int num2;
		if (tile.temperature >= twilightPlacement.minTemperature && tile.temperature <= twilightPlacement.maxTemperature)
		{
			num2 = ((tile.rainfall >= twilightPlacement.minRainfall) ? 1 : 0);
			if (num2 != 0)
			{
				goto IL_00a8;
			}
		}
		else
		{
			num2 = 0;
		}
		if (!(num >= BiomeWorker_AvirNorth.Shifted(twilightPlacement.outlierNoise)) || !(tile.temperature >= twilightPlacement.minTemperature - 4f) || !(tile.temperature <= twilightPlacement.outlierMaxTemperature))
		{
			goto IL_00a8;
		}
		int num3 = ((tile.rainfall >= twilightPlacement.outlierMinRainfall) ? 1 : 0);
		goto IL_00a9;
		IL_00a9:
		bool flag = (byte)num3 != 0;
		if (num2 == 0 && !flag)
		{
			return 0f;
		}
		if (num < BiomeWorker_AvirNorth.Shifted(twilightPlacement.noiseAbove))
		{
			return 0f;
		}
		if (num >= BiomeWorker_AvirNorth.Shifted(twilightPlacement.northNoise) && tile.temperature <= twilightPlacement.northMaxTemperature)
		{
			return 0f;
		}
		return twilightPlacement.score;
		IL_00a8:
		num3 = 0;
		goto IL_00a9;
	}
}
