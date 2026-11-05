using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class CompHideTannery : CompArtifactAura, IThingHolder, IStoreSettingsParent
{
	public ThingOwner<Thing> hides;

	private int progress;

	private ThingCategoryDef category;

	private static Type genericHideType;

	private static bool genericHideLooked;

	private StorageSettings settings;

	private StorageSettings parentSettings;

	private CompProperties_HideTannery P => (CompProperties_HideTannery)props;

	public ThingCategoryDef Category => category ?? (category = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(P.hideCategory));

	public bool StorageTabVisible => true;

	public int Batch
	{
		get
		{
			if (!Awake(out var h))
			{
				return P.batchMin;
			}
			float t = Mathf.InverseLerp(0.6f, 1.4f, h.Power);
			return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(P.batchMin, P.batchMax, t)), P.batchMin, P.batchMax);
		}
	}

	public int Capacity
	{
		get
		{
			if (!Awake(out var h))
			{
				return 0;
			}
			if (h.Working < 2)
			{
				return Batch;
			}
			return P.queueCapacity;
		}
	}

	public int Stored => hides.TotalStackCount;

	public int Space => Mathf.Max(0, Capacity - Stored);

	public CompHideTannery()
	{
		hides = new ThingOwner<Thing>(this, oneStackOnly: false);
	}

	public bool IsHide(Thing t)
	{
		if (t != null && Category != null && t.def.IsWithinCategory(Category) && Leather(t, out var _) != null)
		{
			if (settings != null)
			{
				return settings.AllowedToAccept(t);
			}
			return true;
		}
		return false;
	}

	public static ThingDef Leather(Thing hide, out int perHide)
	{
		perHide = 1;
		if (hide == null)
		{
			return null;
		}
		if (!genericHideLooked)
		{
			genericHideLooked = true;
			genericHideType = AccessTools.TypeByName("MedievalOverhaul.CompGenericHide");
		}
		if (genericHideType != null && hide is ThingWithComps thingWithComps)
		{
			ThingComp thingComp = thingWithComps.AllComps.FirstOrDefault((ThingComp c) => genericHideType.IsInstanceOfType(c));
			if (thingComp != null)
			{
				Traverse traverse = Traverse.Create(thingComp);
				int value = traverse.Field("leatherAmount").GetValue<int>();
				ThingDef thingDef = traverse.Field("leatherType").GetValue<ThingDef>() ?? ((thingComp.props != null) ? Traverse.Create(thingComp.props).Field("leatherType").GetValue<ThingDef>() : null);
				if (value <= 0 && thingComp.props != null)
				{
					value = Traverse.Create(thingComp.props).Field("leatherAmount").GetValue<int>();
				}
				if (thingDef != null)
				{
					perHide = Mathf.Max(1, value);
					return thingDef;
				}
			}
		}
		ThingDefCountClass thingDefCountClass = hide.def.butcherProducts?.FirstOrDefault();
		if (thingDefCountClass == null)
		{
			return null;
		}
		perHide = Mathf.Max(1, thingDefCountClass.count);
		return thingDefCountClass.thingDef;
	}

	public StorageSettings GetStoreSettings()
	{
		if (settings == null)
		{
			settings = new StorageSettings(this);
			if (Category != null)
			{
				settings.filter.SetAllow(Category, allow: true);
			}
		}
		return settings;
	}

	public StorageSettings GetParentStoreSettings()
	{
		if (parentSettings == null)
		{
			parentSettings = new StorageSettings();
			if (Category != null)
			{
				parentSettings.filter.SetAllow(Category, allow: true);
			}
		}
		return parentSettings;
	}

	public void Notify_SettingsChanged()
	{
		if (!parent.Spawned)
		{
			return;
		}
		foreach (Thing item in hides.ToList())
		{
			if (!GetStoreSettings().AllowedToAccept(item))
			{
				hides.TryDrop(item, parent.Position, parent.Map, ThingPlaceMode.Near, out var _);
			}
		}
	}

	public void GetChildHolders(List<IThingHolder> outChildren)
	{
		ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
	}

	public ThingOwner GetDirectlyHeldThings()
	{
		return hides;
	}

	public override void PostExposeData()
	{
		base.PostExposeData();
		Scribe_Deep.Look(ref hides, "avirTanneryHides", this);
		Scribe_Values.Look(ref progress, "avirTanneryProgress", 0);
		Scribe_Deep.Look(ref settings, "avirTanneryFilter", this);
		if (hides == null)
		{
			hides = new ThingOwner<Thing>(this, oneStackOnly: false);
		}
	}

	public override void PostDestroy(DestroyMode mode, Map previousMap)
	{
		base.PostDestroy(mode, previousMap);
		if (previousMap != null)
		{
			hides.TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near);
		}
	}

	public override void CompTickRare()
	{
		base.CompTickRare();
		if (Stored == 0 || !Awake(out var h))
		{
			return;
		}
		progress += 250;
		if ((float)progress < P.hoursPerBatch * 2500f)
		{
			return;
		}
		progress = 0;
		if (!h.Spend(base.A.chargePerUse))
		{
			return;
		}
		int num = Batch;
		foreach (Thing item in hides.ToList())
		{
			if (num <= 0)
			{
				break;
			}
			int num2 = Mathf.Min(num, item.stackCount);
			int perHide;
			ThingDef thingDef = Leather(item, out perHide);
			item.SplitOff(num2).Destroy();
			num -= num2;
			if (thingDef != null)
			{
				int num3 = num2 * Mathf.Max(1, perHide);
				while (num3 > 0)
				{
					Thing thing = ThingMaker.MakeThing(thingDef);
					thing.stackCount = Mathf.Min(num3, thingDef.stackLimit);
					num3 -= thing.stackCount;
					GenPlace.TryPlaceThing(thing, (parent.InteractionCell.IsValid && parent.def.hasInteractionCell) ? parent.InteractionCell : parent.Position, parent.Map, ThingPlaceMode.Near);
				}
			}
		}
		FleckMaker.ThrowMicroSparks(parent.DrawPos, parent.Map);
	}

	public override string CompInspectStringExtra()
	{
		string text = "Hides: " + Stored + " / " + Capacity;
		if (Stored > 0 && Awake(out var _))
		{
			text = text + " - next batch in " + ((int)(P.hoursPerBatch * 2500f) - progress).ToStringTicksToPeriod();
		}
		return text;
	}

	public override string EffectLine()
	{
		if (Awake(out var h))
		{
			return "Cures " + Batch + " hides into leather every " + P.hoursPerBatch.ToString("0") + " hours; holds " + ((h.Working >= 2) ? (P.queueCapacity + " in its queue.") : ("one batch (a second stone opens a queue of " + P.queueCapacity + ")."));
		}
		return CompArtifactAura.Asleep("Cures raw hides into leather by itself.");
	}
}
