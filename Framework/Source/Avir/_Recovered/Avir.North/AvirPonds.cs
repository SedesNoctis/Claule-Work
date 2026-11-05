using RimWorld;
using Verse;

namespace Avir.North;

public class AvirPonds : DefModExtension
{
	public IntRange count = new IntRange(2, 3);

	public FloatRange radius = new FloatRange(4f, 7f);

	public float lakeChance = 0.2f;

	public FloatRange lakeRadius = new FloatRange(9f, 13f);

	public string preferTerrain;

	public float elsewhereChance = 0.12f;

	public TerrainDef shallow;

	public TerrainDef deep;

	public bool swapStillWater;

	public ThingDef shorePlant;

	public float shoreChance = 0.35f;

	public static readonly AvirPonds North = new AvirPonds
	{
		preferTerrain = "Avir_LooseSnow"
	};

	public TerrainDef Shallow => shallow ?? TerrainDefOf.WaterShallow;

	public TerrainDef Deep => deep ?? TerrainDefOf.WaterDeep;
}
