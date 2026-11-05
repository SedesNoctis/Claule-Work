using System.Linq;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

public static class StriderBowGear
{
	public static ThingDef BowFor(string palette)
	{
		string text;
		switch (palette)
		{
		case "blood":
			text = "Bloodmoon";
			break;
		case "lilac":
			text = "Shadowsong";
			break;
		case "midnight":
		case "teal":
			text = "Evershadow";
			break;
		case "black":
			text = "Crown";
			break;
		default:
			text = new string[4] { "Shadowsong", "Bloodmoon", "Evershadow", "Crown" }.RandomElement();
			break;
		}
		return DefDatabase<ThingDef>.GetNamedSilentFail("Avir_StriderBow_" + text);
	}

	public static bool Arm(Pawn pawn, string palette)
	{
		ThingDef thingDef = BowFor(palette);
		if (thingDef == null || pawn?.equipment == null)
		{
			return false;
		}
		ThingWithComps thingWithComps = (ThingWithComps)ThingMaker.MakeThing(thingDef);
		thingWithComps.TryGetComp<CompQuality>()?.SetQuality(Rand.Chance(0.3f) ? QualityCategory.Excellent : QualityCategory.Good, ArtGenerationContext.Outsider);
		CompStriderBow c = CompStriderBow.Of(thingWithComps);
		if (c != null)
		{
			c.whiteRobe = palette == "white";
			c.path = (c.whiteRobe ? "Moon" : StriderPaths.All.Where((string x) => StriderPaths.Favoured(c, x)).RandomElement());
			int num = Rand.RangeInclusive(3, 14);
			float num2 = 0f;
			for (int i = 1; i <= num; i++)
			{
				num2 += StriderPaths.StoneXp(i);
			}
			c.xp = num2 + 1f;
		}
		pawn.equipment.AddEquipment(thingWithComps);
		ThingDef avir_CrystalBattery = ArtifactBatteryDefOf.Avir_CrystalBattery;
		if (avir_CrystalBattery != null && pawn.inventory != null)
		{
			Thing thing = ThingMaker.MakeThing(avir_CrystalBattery);
			thing.stackCount = Rand.RangeInclusive(1, 3);
			pawn.inventory.innerContainer.TryAdd(thing);
		}
		return true;
	}
}
