using UnityEngine;

namespace Avir.Artifactorics;

public class ArtifactPsychicFocus
{
	public float baseBonus;

	public float perStepBonus;

	public float stepSize = 0.15f;

	public float stepsFrom = 1f;

	public float thresholdWithFearborn;

	public float thresholdWithoutFearborn;

	public float belowThresholdPenalty;

	public bool baseBonusCombatOnly;

	public bool stepBonusAllArms;

	public bool dilutesAcrossArms;

	public bool IsActive(float sensitivity, bool fearborn)
	{
		return sensitivity >= (fearborn ? thresholdWithFearborn : thresholdWithoutFearborn);
	}

	public int Steps(float sensitivity)
	{
		if (stepSize <= 0f)
		{
			return 0;
		}
		return Mathf.Max(0, Mathf.FloorToInt((sensitivity - stepsFrom) / stepSize + 0.0001f));
	}
}
