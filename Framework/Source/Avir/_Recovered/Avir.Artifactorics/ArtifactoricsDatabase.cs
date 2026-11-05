using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Avir.Artifactorics;

[StaticConstructorOnStartup]
public static class ArtifactoricsDatabase
{
	public static readonly List<ArtifactMetalDef> Metals;

	public static readonly List<ArtifactArmDef> ApparelArms;

	public static readonly List<ArtifactArmDef> WeaponArms;

	static ArtifactoricsDatabase()
	{
		Metals = (from m in DefDatabase<ArtifactMetalDef>.AllDefsListForReading
			orderby m.uiOrder, m.label
			select m).ToList();
		ApparelArms = (from a in DefDatabase<ArtifactArmDef>.AllDefsListForReading
			where a.carrier == ArtifactCarrier.Apparel
			orderby a.armNumber
			select a).ToList();
		WeaponArms = (from a in DefDatabase<ArtifactArmDef>.AllDefsListForReading
			where a.carrier == ArtifactCarrier.Weapon
			orderby a.armNumber
			select a).ToList();
		ArtifactoricsSettingsDef namedSilentFail = DefDatabase<ArtifactoricsSettingsDef>.GetNamedSilentFail("Artifactorics_Settings");
		if (namedSilentFail == null)
		{
			Log.Error("[Avir] Artifactorics: settings def Artifactorics_Settings is missing.");
			return;
		}
		Log.Message("[Avir] Artifactorics data: " + Metals.Count + " metals (" + string.Join(", ", Metals.Select((ArtifactMetalDef m) => m.defName)) + "), " + ApparelArms.Count + " armour arms, " + WeaponArms.Count + " weapon arms; " + namedSilentFail.NodeCount + " cells per arm = " + namedSilentFail.ArmPointCost + " points = " + namedSilentFail.XpForPoints(namedSilentFail.ArmPointCost).ToString("N0") + " xp.");
	}

	public static List<ArtifactArmDef> ArmsFor(ArtifactCarrier c)
	{
		if (c != ArtifactCarrier.Weapon)
		{
			return ApparelArms;
		}
		return WeaponArms;
	}
}
