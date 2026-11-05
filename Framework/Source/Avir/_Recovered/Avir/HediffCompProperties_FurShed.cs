using Verse;

namespace Avir;

public class HediffCompProperties_FurShed : HediffCompProperties
{
	public float feathersPerDay = 10f;

	public float poolCapacity = 30f;

	public int minTicksBetween = 18000;

	public int maxTicksBetween = 22000;

	public int amountPerShed = 1;

	public float minLooseToGroom = 6f;

	public HediffCompProperties_FurShed()
	{
		compClass = typeof(HediffComp_FurShed);
	}
}
