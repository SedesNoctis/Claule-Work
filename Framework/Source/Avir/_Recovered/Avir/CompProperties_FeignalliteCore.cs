using Verse;

namespace Avir;

public class CompProperties_FeignalliteCore : CompProperties
{
	public float matterUsedPerWork = 0.5f;

	public int workAmountPerTick = 600;

	public int interval = 60;

	public float radius = 75f;

	public FleckDef fleckDef;

	public int minConstruction = 16;

	public int minCrafting = 12;

	public int minIntellectual = 12;

	public int maxSimultaneousQualityFrames = 15;

	public float hungerPerHour = 0.01f;

	public float matterPerPlantWork = 7f;

	public int minPlantYield;

	public CompProperties_FeignalliteCore()
	{
		compClass = typeof(CompFeignalliteCore);
	}
}
