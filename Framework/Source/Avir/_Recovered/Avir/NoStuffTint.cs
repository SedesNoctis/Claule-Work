using Verse;

namespace Avir;

public class NoStuffTint : DefModExtension
{
	public static bool On(ThingDef d)
	{
		if (d != null)
		{
			if (!d.HasModExtension<NoStuffTint>())
			{
				if (d.entityDefToBuild is ThingDef thingDef)
				{
					return thingDef.HasModExtension<NoStuffTint>();
				}
				return false;
			}
			return true;
		}
		return false;
	}
}
