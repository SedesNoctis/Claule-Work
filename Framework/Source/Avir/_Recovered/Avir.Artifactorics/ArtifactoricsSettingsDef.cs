using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class ArtifactoricsSettingsDef : Def
{
	public const string DefNameMain = "Artifactorics_Settings";

	private static ArtifactoricsSettingsDef cached;

	public List<int> nodeCosts = new List<int>();

	public List<int> milestoneNodes = new List<int>();

	public List<int> nodesLostOnForeignExtraction = new List<int>();

	public float firstPointXp = 10000f;

	public float pointXpStep = 2500f;

	public float pointXpStepLate = 5000f;

	public int lateStepAfterPoint = 10;

	public float pawnShare = 0.8f;

	public float weaponXpFactor = 3f;

	public float artifactShare = 0.2f;

	public float pawnShareWhileRepairing;

	public float repairShareWhileRepairing = 0.8f;

	public bool splitAfterDailyCap = true;

	public float resetRefund = 0.8f;

	public List<float> heartQualityFactors = new List<float> { 0.8f, 0.9f, 1f, 1.05f, 1.1f, 1.15f, 1.2f };

	public float spearShotCharge = 0.0075f;

	public float bowShotCharge = 0.01f;

	public float bowStoneXp = 2500f;

	public float bowStoneXpStep = 500f;

	public int sphereFramesBase = 2;

	public int sphereFramesSkillStep = 7;

	public int sphereFramesMax = 4;

	public float sphereReach = 12f;

	public float sphereSpeed = 0.75f;

	public float sphereChargePerKWork = 0.01f;

	public float sphereStoneXp = 4000f;

	public float sphereStoneXpStep = 1000f;

	public float spearShotPower = 0.5f;

	public float batteryCapacity = 150f;

	public float buildingRechargeBelow = 0.5f;

	public float buildingAttunerMinSensitivity = 0.5f;

	public float buildingRechargePerTick = 0.02f;

	public int buildingRechargeTicks = 2000;

	public List<float> buildingQualityFactors = new List<float> { 0.8f, 0.9f, 1f, 1.05f, 1.1f, 1.2f, 1.3f };

	public float batteryChargeSensitivity = 1.5f;

	public int maxMilestoneArmsPerPawn = 3;

	public int maxActiveAbilitiesPerPawn = 2;

	public float rimStrength = 0.5f;

	public List<float> shieldCapacity = new List<float> { 50f, 100f, 150f };

	public List<float> shieldRegenPerSecond = new List<float> { 5f, 8f, 12f };

	public float shieldCapacityCeiling = 450f;

	public float auraShieldCapacity = 75f;

	public float auraShieldRegenPerSecond = 3f;

	public float auraShieldRadius = 7f;

	public float abilityPowerPerPoint = 0.01f;

	public float stoneFocusStrength = 0.25f;

	public int stoneSetTicks = 600;

	public float autoTeleportMinDistance = 20f;

	public float autoTeleportCost = 1f;

	public float autoTeleportCooldownSeconds = 30f;

	public int architectActiveBuildings = 15;

	public int architectBackgroundBuildings = 5;

	public float architectActiveHpPerSecond = 15f;

	public float architectBackgroundHpPerSecond = 3f;

	public float architectActiveCostPer2s = 0.75f;

	public float architectBackgroundCostPerBuildingPer10s = 0.1875f;

	public float barrierHitPoints = 400f;

	public int shroudGasPerCell = 200;

	public List<float> chargeQualityFactors = new List<float> { 0.6f, 0.8f, 1f, 1.3f, 1.65f, 2.1f, 2.75f };

	public float chargePerSensitivity = 0.75f;

	public float rechargeSensitivityFloor = 0.1f;

	public float rechargeCooldownDays = 3f;

	public int rechargeTicks = 2500;

	public float autoRechargeDefault = 0.3f;

	public float chargeDailyBase = 0.02f;

	public float chargeDailyPerMilestoneArm = 0.01f;

	public float chargePerShieldDamage = 0.1f;

	public float chargeAuraPerSecond = 0.1f;

	public float chargeBlink = 2f;

	public float chargeDissolve = 3f;

	public float chargePhasing = 3f;

	public float chargeVeil = 3f;

	public float psyfocusPerHour = 0.01f;

	public float chargePerPsyfocus = 0.5f;

	public float psyfocusFloor = 0.5f;

	public List<int> telekinesisSlotCosts = new List<int> { 1, 1, 2, 2, 3 };

	public List<float> telekinesisRadiusByLevel = new List<float> { 6f, 12f, 18f, 26f, 36f };

	public float mendCostPerPercent = 50f;

	public float mendArmorOffset = 0.5f;

	public float mendCostFactorAt7 = 0.75f;

	public float mentorRadius = 5f;

	public float insightXp = 1000f;

	public float passingRadius = 6f;

	public float cleanRadius = 3f;

	public float cleanPerSecond = 1f;

	public float tkBaseCapacityKg = 45f;

	public float tkPickupRadius = 2f;

	public float tkUnloadRadius = 6f;

	public float knockbackStunSeconds = 0.5f;

	public float bannerRadius = 3f;

	public float bannerStunSeconds = 2f;

	public float overflowShareOfShield = 0.2f;

	public float reprisalRadius = 3f;

	public float reprisalStunSeconds = 1.5f;

	public float secondWindSeconds = 10f;

	public float blinkChancePerCell = 0.01f;

	public List<int> blinkRangeCells = new List<int> { 4, 9, 12 };

	public float blinkCooldownSeconds = 10f;

	public float blinkFocusSeconds = 2f;

	public float blinkAfterimageSeconds = 2f;

	public float dissolveDamageThreshold = 12f;

	public float dissolveCooldownSeconds = 30f;

	public float dissolveDistanceCells = 5f;

	public float dissolveUnseenSeconds = 2f;

	public List<float> swiftnessChancePerSecond = new List<float> { 0.04f, 0.06f, 0.08f };

	public List<float> swiftnessDurationSeconds = new List<float> { 6f, 10f, 12f };

	public float swiftnessMaxDurationSeconds = 16f;

	public float swiftnessWindowSeconds = 7f;

	public float phasingRescueSeconds = 10f;

	public List<float> unseenChancePerSecond = new List<float> { 0.06f, 0.08f, 0.1f };

	public List<float> unseenDurationSeconds = new List<float> { 6f, 9f, 12f };

	public float unseenMaxDurationSeconds = 20f;

	public List<float> unseenWindowSeconds = new List<float> { 20f, 16f, 12f };

	public float unseenKillExtendSeconds = 6f;

	public float ambushDamageFactor = 2f;

	public float swiftnessCombatChanceFactor = 1.5f;

	public float wallBurstSeconds = 2f;

	public float wallExitStunSeconds = 1f;

	public float dashStrikeFactor = 1f;

	public float dashStunSeconds = 0.5f;

	public float whirlStrikeFactor = 0.75f;

	public float markBonus = 0.25f;

	public float markBonusMax = 0.75f;

	public float pullStunSeconds = 1f;

	public float venomBladeBuildup = 0.05f;

	public float venomBladeSlow = 0.15f;

	public float reflectShare = 0.5f;

	public float reflectRange = 12f;

	public float autoStoneDefault = 0.25f;

	public List<float> edgePierce = new List<float> { 0.15f, 0.25f };

	public float edgeBleedExtra = 0.5f;

	public float edgeSeverChance = 0.1f;

	public float crushStaggerSeconds = 1f;

	public float crushArmorShare = 0.25f;

	public int crushShockEvery = 4;

	public float crushShockRadius = 1.5f;

	public float crushShockStunSeconds = 1.5f;

	public int resonanceEvery = 3;

	public float resonanceEchoShare = 0.5f;

	public float resonanceJumpRadius = 3f;

	public float resonanceStunSeconds = 1f;

	public float resonanceEmpDamage = 10f;

	public float mentalShare = 0.5f;

	public float mentalPainPerDamage = 0.01f;

	public float mentalHealPerSecond = 2f;

	public float mentalArmorFloor = 0.2f;

	public float mentalPsyfocusPerDamage = 0.005f;

	public float mentalReachBase = 3f;

	public float mentalReachMax = 12f;

	public float mentalReachCharge = 1f;

	public float tempoFlurryChance = 0.2f;

	public float tempoFlurryMax = 0.6f;

	public float tempoMomentumPerHit = 0.05f;

	public float tempoMomentumMax = 0.25f;

	public float tempoMomentumResetSeconds = 5f;

	public float tempoStormSeconds = 3f;

	public float tempoStormSpeed = 0.3f;

	public float precisionPierce = 0.1f;

	public float precisionDamage = 0.15f;

	public float precisionPerfectChance = 0.1f;

	public float sneakFirstBlood = 0.5f;

	public float sneakVanishSeconds = 3f;

	public float sneakAssassinBonus = 1.5f;

	public float sneakAssassinStunSeconds = 1f;

	public float rageStackDamage = 0.03f;

	public int rageMaxStacks = 10;

	public float rageDecaySeconds = 10f;

	public float frenzyHealth = 0.5f;

	public float frenzySpeed = 0.25f;

	public float wrathShare = 0.5f;

	public float parryChance = 0.15f;

	public float parryChanceMax = 0.5f;

	public float deflectChance = 0.1f;

	public float deflectChanceMax = 0.3f;

	public float rampartRadius = 3f;

	public float rampartReduction = 0.1f;

	public float rampartReductionMax = 0.3f;

	public float rampartBarrierRadius = 4f;

	public List<float> rampartBarrierCapacity = new List<float> { 40f, 80f };

	public float rampartBarrierRegenPerSecond = 3f;

	public float interceptShare = 0.3f;

	public float thirstLifesteal = 0.1f;

	public int thirstArmorMendPerHit = 1;

	public float vitalityBleedFactor = 0.5f;

	public float feastCharge = 0.03f;

	public float feastArmorMend = 0.02f;

	public float venomBuildup = 0.03f;

	public float venomSeconds = 10f;

	public float numbSlow = 0.15f;

	public float necrosisBonus = 0.25f;

	public float necrosisRadius = 2f;

	public float brokenShieldRegenDelaySeconds = 5f;

	public GeneDef avirGene;

	public List<XenotypeDef> notAvirXenotypes = new List<XenotypeDef>();

	public static ArtifactoricsSettingsDef Main
	{
		get
		{
			if (cached == null)
			{
				cached = DefDatabase<ArtifactoricsSettingsDef>.GetNamed("Artifactorics_Settings");
			}
			return cached;
		}
	}

	public int NodeCount => nodeCosts.Count;

	public int ArmPointCost => nodeCosts.Sum();

	public float HeartQualityFactor(QualityCategory q)
	{
		if ((int)q < 0 || (int)q >= heartQualityFactors.Count)
		{
			return 1f;
		}
		return heartQualityFactors[(int)q];
	}

	public int TelekinesisSlotCost(int slot)
	{
		if (slot < 1 || slot > telekinesisSlotCosts.Count)
		{
			return 0;
		}
		return telekinesisSlotCosts[slot - 1];
	}

	public float TelekinesisRadius(int level)
	{
		if (level <= 0 || telekinesisRadiusByLevel.NullOrEmpty())
		{
			return 0f;
		}
		return telekinesisRadiusByLevel[Mathf.Min(level, telekinesisRadiusByLevel.Count) - 1];
	}

	public int NodeCost(int node)
	{
		if (node < 1 || node > nodeCosts.Count)
		{
			return 0;
		}
		return nodeCosts[node - 1];
	}

	public bool IsMilestone(int node)
	{
		return milestoneNodes.Contains(node);
	}

	public float XpForPoint(int n)
	{
		if (n <= 0)
		{
			return 0f;
		}
		AvirSettings settings = AvirMod.Settings;
		float num = settings?.firstPointXp ?? firstPointXp;
		float num2 = settings?.pointXpStep ?? pointXpStep;
		float num3 = settings?.pointXpStepLate ?? pointXpStepLate;
		int num4 = settings?.lateStepAfterPoint ?? lateStepAfterPoint;
		if (n <= num4)
		{
			return num + num2 * (float)(n - 1);
		}
		return num + num2 * (float)(num4 - 1) + num3 * (float)(n - num4);
	}

	public float XpForPoints(int points)
	{
		float num = 0f;
		for (int i = 1; i <= points; i++)
		{
			num += XpForPoint(i);
		}
		return num;
	}

	public int PointsForXp(float xp)
	{
		int num = 0;
		float num2 = 0f;
		do
		{
			float num3 = XpForPoint(num + 1);
			if (num2 + num3 > xp + 0.01f)
			{
				return num;
			}
			num2 += num3;
			num++;
		}
		while (num <= 10000);
		return num;
	}

	public bool CountsAsAvir(Pawn p)
	{
		if (p?.genes == null || avirGene == null)
		{
			return false;
		}
		if (notAvirXenotypes.Contains(p.genes.Xenotype))
		{
			return false;
		}
		return p.genes.HasActiveGene(avirGene);
	}

	public override IEnumerable<string> ConfigErrors()
	{
		foreach (string item in base.ConfigErrors())
		{
			yield return item;
		}
		if (defName != "Artifactorics_Settings")
		{
			yield return "settings def must be named Artifactorics_Settings";
		}
		if (nodeCosts.NullOrEmpty())
		{
			yield return "nodeCosts is empty";
		}
		foreach (int milestoneNode in milestoneNodes)
		{
			if (milestoneNode < 1 || milestoneNode > nodeCosts.Count)
			{
				yield return "milestone node " + milestoneNode + " is outside 1.." + nodeCosts.Count;
			}
		}
		foreach (int item2 in nodesLostOnForeignExtraction)
		{
			if (item2 < 1 || item2 > nodeCosts.Count)
			{
				yield return "lost node " + item2 + " is outside 1.." + nodeCosts.Count;
			}
		}
		if (Mathf.Abs(pawnShare + artifactShare - 1f) > 0.001f)
		{
			yield return "pawnShare + artifactShare should be 1 (is " + (pawnShare + artifactShare) + ")";
		}
		if (avirGene == null)
		{
			yield return "avirGene is not set";
		}
	}
}
