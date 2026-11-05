using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class ArtifactMetalDef : Def
{
	public ThingDef material;

	public Color gemColor = Color.white;

	public List<Color> shimmerColors = new List<Color>();

	public float shimmerPeriodSeconds = 6f;

	public List<ArtifactFocusEntry> apparelFocus = new List<ArtifactFocusEntry>();

	public List<ArtifactFocusEntry> weaponFocus = new List<ArtifactFocusEntry>();

	public float allArmsBonus;

	public bool allowedAsNecklace;

	public bool allowedAsAmuletCore = true;

	public float amuletAllBonus;

	public List<AmuletFocusEntry> amuletFocus = new List<AmuletFocusEntry>();

	public ArtifactMetalDef amuletNeedsNecklace;

	public bool amuletPsychicAllSubclasses;

	public List<ArtifactMetalDef> amuletCoresOnly = new List<ArtifactMetalDef>();

	public bool amuletAvirOnly;

	public bool amuletMulticlass;

	public List<ArtifactMetalDef> amuletMulticlassCores = new List<ArtifactMetalDef>();

	public ArtifactPsychicFocus psychic;

	public bool allowedAsHeart = true;

	public float buildingPower = 1f;

	public bool allowedAsRim = true;

	public bool allowedForBuildings = true;

	public bool allowedAsStone = true;

	public float stonePower = 1f;

	public float stoneCombatPower = 1f;

	public float stoneRadius = 1f;

	public float stoneDuration = 1f;

	public float stoneCooldown = 1f;

	public float stoneCost = 1f;

	public float stonePsychicScale = 1f;

	public float stonePerStepBonus;

	public string texKey;

	public Color rimColor = new Color(0f, 0f, 0f, 0f);

	public int uiOrder;

	[MustTranslate]
	public string availability;

	public Color RimColor
	{
		get
		{
			if (!(rimColor.a > 0f))
			{
				return gemColor;
			}
			return rimColor;
		}
	}

	public bool IsPsychic => psychic != null;

	public bool Shimmers
	{
		get
		{
			if (shimmerColors != null)
			{
				return shimmerColors.Count > 1;
			}
			return false;
		}
	}

	public bool MulticlassWith(ArtifactMetalDef core)
	{
		if (amuletMulticlass)
		{
			if (!amuletMulticlassCores.NullOrEmpty())
			{
				if (core != null)
				{
					return amuletMulticlassCores.Contains(core);
				}
				return false;
			}
			return true;
		}
		return false;
	}

	public List<ArtifactFocusEntry> FocusFor(ArtifactCarrier carrier)
	{
		if (carrier != ArtifactCarrier.Weapon)
		{
			return apparelFocus;
		}
		return weaponFocus;
	}

	public float FixedFocus(ArtifactArmDef arm)
	{
		float num = allArmsBonus;
		List<ArtifactFocusEntry> list = FocusFor(arm.carrier);
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].arm == arm)
			{
				num += list[i].factor;
			}
		}
		return num;
	}

	public float RimFocus(ArtifactArmDef arm)
	{
		float rimStrength = ArtifactoricsSettingsDef.Main.rimStrength;
		float num = ((allArmsBonus > 0f) ? allArmsBonus : 0f);
		List<ArtifactFocusEntry> list = FocusFor(arm.carrier);
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].arm == arm && list[i].factor > 0f)
			{
				num += list[i].factor;
			}
		}
		return num * rimStrength;
	}

	public Color GemColorAt(float realtime)
	{
		if (!Shimmers)
		{
			return gemColor;
		}
		float num = realtime / Mathf.Max(0.1f, shimmerPeriodSeconds) % 1f * (float)shimmerColors.Count;
		int num2 = Mathf.FloorToInt(num) % shimmerColors.Count;
		int num3 = (num2 + 1) % shimmerColors.Count;
		float num4 = num - Mathf.Floor(num);
		num4 = num4 * num4 * (3f - 2f * num4);
		return Color.Lerp(shimmerColors[num2], shimmerColors[num3], num4);
	}

	public override IEnumerable<string> ConfigErrors()
	{
		foreach (string item in base.ConfigErrors())
		{
			yield return item;
		}
		if (material == null)
		{
			yield return "no material ThingDef";
		}
		foreach (ArtifactFocusEntry item2 in apparelFocus)
		{
			if (item2.arm == null)
			{
				yield return "apparelFocus entry with a missing arm";
			}
		}
		foreach (ArtifactFocusEntry item3 in weaponFocus)
		{
			if (item3.arm == null)
			{
				yield return "weaponFocus entry with a missing arm";
			}
		}
		foreach (AmuletFocusEntry item4 in amuletFocus)
		{
			if (item4.subclass == null)
			{
				yield return "amuletFocus entry with a missing subclass";
			}
		}
		if (allowedAsNecklace && allowedAsAmuletCore)
		{
			yield return "a necklace metal may not also be an amulet core";
		}
	}
}
