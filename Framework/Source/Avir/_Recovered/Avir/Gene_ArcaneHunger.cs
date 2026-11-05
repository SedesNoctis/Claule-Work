using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir;

public class Gene_ArcaneHunger : Gene_Resource, IGeneResourceDrain
{
	public const float SafeThreshold = 0.6f;

	public const float StarvedFloor = 0.2f;

	public const float FillPerSiphon = 0.15f;

	public const float FillPerDrain = 0.1f;

	public const float FillPerKillingBlow = 0.12f;

	public bool wildFeedingAllowed = true;

	public bool extractDrinkingAllowed;

	private static StatDef fallRateFactorStat;

	public const float BondShare = 0.25f;

	public Gene_Resource Resource => this;

	public bool CanOffset => Active;

	private static StatDef FallRateFactorStat => fallRateFactorStat ?? (fallRateFactorStat = DefDatabase<StatDef>.GetNamedSilentFail("Avir_ArcaneHungerFallRateFactor"));

	public float ResourceLossPerDay
	{
		get
		{
			float num = ((FallRateFactorStat != null) ? pawn.GetStatValue(FallRateFactorStat) : 1f);
			return def.resourceLossPerDay * num;
		}
	}

	public Pawn Pawn => pawn;

	public string DisplayLabel => Label;

	public override float InitialResourceMax => 1f;

	public override float MinLevelForAlert => 0.25f;

	public override float MaxLevelOffset => 0.1f;

	protected override Color BarColor => new Color(0.46f, 0.47f, 0.56f);

	protected override Color BarHighlightColor => new Color(0.66f, 0.68f, 0.78f);

	public float FeedBelow => AvirResourceUtil.TargetValue(this, 0.45f);

	public float PsychicFactor => Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(Value / 0.6f));

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref wildFeedingAllowed, "avirWildFeedingAllowed", defaultValue: true);
		Scribe_Values.Look(ref extractDrinkingAllowed, "avirExtractDrinkingAllowed", defaultValue: false);
	}

	public void DrainFor(int ticks)
	{
		if (Active)
		{
			Value = Mathf.Clamp01(Value - ResourceLossPerDay * ((float)ticks / 60000f));
		}
	}

	public static Gene_ArcaneHunger Of(Pawn pawn)
	{
		if (pawn == null || pawn.genes == null)
		{
			return null;
		}
		return pawn.genes.GetFirstGeneOfType<Gene_ArcaneHunger>();
	}

	public override void PostAdd()
	{
		base.PostAdd();
		ArcaneHungerCache.Invalidate(pawn);
	}

	public override void PostRemove()
	{
		base.PostRemove();
		ArcaneHungerCache.Invalidate(pawn);
	}

	public static void Feed(Pawn pawn, float amount)
	{
		Feed(pawn, amount, shared: false);
	}

	private static void Feed(Pawn pawn, float amount, bool shared)
	{
		Gene_ArcaneHunger gene_ArcaneHunger = Of(pawn);
		if (gene_ArcaneHunger != null && gene_ArcaneHunger.Active)
		{
			gene_ArcaneHunger.Value = Mathf.Clamp01(gene_ArcaneHunger.Value + amount);
			if (!shared && !(amount <= 0f))
			{
				FeedBondedMate(pawn, amount);
			}
		}
	}

	private static void FeedBondedMate(Pawn eater, float amount)
	{
		try
		{
			if (AvirPairBond.HasBondGene(eater))
			{
				Pawn pawn = LovePartnerRelationUtility.ExistingMostLikedLovePartner(eater, allowDead: false);
				if (pawn != null && !pawn.Dead && AvirPairBond.HasBondGene(pawn))
				{
					Feed(pawn, amount * 0.25f, shared: true);
				}
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir bond] sharing essence failed: " + ex, 4870793);
		}
	}

	public static bool HasAtLeast(Pawn pawn, float amount)
	{
		Gene_ArcaneHunger gene_ArcaneHunger = Of(pawn);
		if (gene_ArcaneHunger != null && gene_ArcaneHunger.Active)
		{
			return gene_ArcaneHunger.Value >= amount;
		}
		return false;
	}

	public static bool Spend(Pawn pawn, float amount)
	{
		Gene_ArcaneHunger gene_ArcaneHunger = Of(pawn);
		if (gene_ArcaneHunger == null || !gene_ArcaneHunger.Active || gene_ArcaneHunger.Value < amount)
		{
			return false;
		}
		gene_ArcaneHunger.Value = Mathf.Clamp01(gene_ArcaneHunger.Value - amount);
		return true;
	}
}
