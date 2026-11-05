using Verse;

namespace Avir.North;

public class TwilightPlacement : DefModExtension
{
	public float noiseAbove = 0.56f;

	public float northNoise = 0.53f;

	public float northMaxTemperature;

	public float minTemperature = -10f;

	public float maxTemperature = 12f;

	public float minRainfall = 900f;

	public float outlierNoise = 0.7f;

	public float outlierMaxTemperature = 17f;

	public float outlierMinRainfall = 500f;

	public float score = 100f;
}
