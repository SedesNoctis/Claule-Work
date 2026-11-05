using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir;

public class CompFeignalliteCore : ThingComp
{
	private int ticksUntilNextPass;

	private bool isAutoDeconstruct = true;

	private bool isAutoHarvest = true;

	private bool isShowNotifMessage = true;

	public Pawn architect;

	private int architectActiveUntilTick = -1;

	private float hungerDebt;

	private CompRefuelable fuelComp;

	public bool ArchitectWorking
	{
		get
		{
			if (architect != null && !architect.Dead && architect.Spawned)
			{
				return Find.TickManager.TicksGame <= architectActiveUntilTick;
			}
			return false;
		}
	}

	public CompProperties_FeignalliteCore Props => (CompProperties_FeignalliteCore)props;

	private CompRefuelable FuelComp => fuelComp ?? (fuelComp = parent.GetComp<CompRefuelable>());

	private static ThingDef FeignalliteDef => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Feignallite");

	private static Texture2D CoreIcon => ContentFinder<Texture2D>.Get("Things/Building/Production/Avir_FeignalliteCore");

	public void NotifyArchitectWorking()
	{
		architectActiveUntilTick = Find.TickManager.TicksGame + 30;
	}

	public static bool Qualifies(Pawn pawn, CompProperties_FeignalliteCore props)
	{
		if (pawn?.skills == null || pawn.Dead)
		{
			return false;
		}
		GeneDef namedSilentFail = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
		if (namedSilentFail == null || pawn.genes == null || !pawn.genes.HasActiveGene(namedSilentFail))
		{
			return false;
		}
		if (pawn.skills.GetSkill(SkillDefOf.Construction).Level >= props.minConstruction && pawn.skills.GetSkill(SkillDefOf.Crafting).Level >= props.minCrafting)
		{
			return pawn.skills.GetSkill(SkillDefOf.Intellectual).Level >= props.minIntellectual;
		}
		return false;
	}

	public override void CompTick()
	{
		base.CompTick();
		if (!parent.Spawned)
		{
			return;
		}
		ticksUntilNextPass--;
		if (ticksUntilNextPass <= 0)
		{
			ticksUntilNextPass = Props.interval;
			DrainArchitectHunger();
			if (isAutoDeconstruct)
			{
				DoDeconstruct();
			}
			if (isAutoHarvest)
			{
				DoHarvest();
			}
			if (FuelComp != null && FuelComp.HasFuel)
			{
				DoBuilding();
			}
		}
	}

	private static bool HasQuality(BuildableDef bdef)
	{
		if (bdef is ThingDef thingDef)
		{
			return thingDef.HasComp(typeof(CompQuality));
		}
		return false;
	}

	private Pawn WorkerPawn()
	{
		if (ArchitectWorking)
		{
			return architect;
		}
		return parent.Map?.mapPawns.FreeColonistsSpawned.FirstOrDefault();
	}

	public bool HasQualityWorkNearby()
	{
		if (parent?.Map == null)
		{
			return false;
		}
		foreach (Thing item in Nearby())
		{
			if (item != null && !item.Destroyed && item.Spawned && item != parent)
			{
				if (item is Blueprint blueprint && HasQuality(blueprint.EntityToBuild()))
				{
					return true;
				}
				if (item is Frame frame && HasQuality(frame.BuildDef))
				{
					return true;
				}
				if (isAutoDeconstruct && HasQuality(item.def) && parent.Map.designationManager.DesignationOn(item, DesignationDefOf.Deconstruct) != null)
				{
					return true;
				}
			}
		}
		return false;
	}

	private IEnumerable<Thing> Nearby()
	{
		return GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Props.radius, useCenter: true);
	}

	private void Notify(string msg)
	{
		if (isShowNotifMessage)
		{
			Messages.Message(msg, parent, MessageTypeDefOf.SilentInput, historical: false);
		}
	}

	private void DrainArchitectHunger()
	{
		if (!ArchitectWorking)
		{
			hungerDebt = 0f;
			return;
		}
		hungerDebt += Props.hungerPerHour * ((float)Props.interval / 2500f);
		if (!(hungerDebt < 0.01f))
		{
			float num = Mathf.Floor(hungerDebt * 100f) / 100f;
			hungerDebt -= num;
			if (!Gene_ArcaneHunger.Spend(architect, num))
			{
				architectActiveUntilTick = -1;
				Notify("Feignallite core: the architect has nothing left to spend.");
			}
		}
	}

	private float FuelCostFor(float workAmount)
	{
		return workAmount * Props.matterUsedPerWork / (float)Mathf.Max(1, Props.workAmountPerTick);
	}

	private void ThrowFleck(IntVec3 pos)
	{
		if (Props.fleckDef != null)
		{
			Map map = parent.Map;
			if (map != null)
			{
				FleckCreationData dataStatic = FleckMaker.GetDataStatic(pos.ToVector3Shifted(), map, Props.fleckDef);
				map.flecks.CreateFleck(dataStatic);
			}
		}
	}

	private void StoreFeignallite(int amount, IntVec3 fallbackPos)
	{
		if (amount <= 0)
		{
			return;
		}
		ThingDef feignalliteDef = FeignalliteDef;
		if (feignalliteDef == null)
		{
			return;
		}
		int num = amount;
		if (FuelComp != null)
		{
			float f = FuelComp.Props.fuelCapacity - FuelComp.Fuel;
			int num2 = Mathf.Min(amount, Mathf.FloorToInt(f));
			if (num2 > 0)
			{
				FuelComp.Refuel(num2);
				num -= num2;
			}
		}
		while (num > 0)
		{
			int num3 = Mathf.Min(num, feignalliteDef.stackLimit);
			Thing thing = ThingMaker.MakeThing(feignalliteDef);
			thing.stackCount = num3;
			GenPlace.TryPlaceThing(thing, fallbackPos, parent.Map, ThingPlaceMode.Near);
			num -= num3;
		}
	}

	private void DoBuilding()
	{
		Pawn pawn = WorkerPawn();
		if (pawn == null)
		{
			return;
		}
		bool architectWorking = ArchitectWorking;
		int num = 0;
		foreach (Thing item in Nearby().ToList())
		{
			if (!FuelComp.HasFuel)
			{
				break;
			}
			if (item == null || item.Destroyed || !item.Spawned)
			{
				continue;
			}
			if (item is Blueprint blueprint)
			{
				BuildableDef buildableDef = blueprint.EntityToBuild();
				if (buildableDef != null && (!HasQuality(buildableDef) || architectWorking))
				{
					blueprint.TryReplaceWithSolidThing(pawn, out var createdThing, out var _);
					if (createdThing != null)
					{
						ThrowFleck(createdThing.Position);
					}
				}
			}
			else
			{
				if (!(item is Frame frame))
				{
					continue;
				}
				bool flag = HasQuality(frame.BuildDef);
				if (flag && !architectWorking)
				{
					continue;
				}
				if (flag)
				{
					if (num >= Props.maxSimultaneousQualityFrames)
					{
						continue;
					}
					num++;
				}
				float workToBuild = frame.WorkToBuild;
				if (frame.workDone >= workToBuild)
				{
					continue;
				}
				float num2 = Mathf.Min(Props.workAmountPerTick, workToBuild - frame.workDone);
				float num3 = Mathf.Min(FuelCostFor(num2), FuelComp.Fuel);
				if (!(num3 <= 0f))
				{
					frame.workDone += num2;
					FuelComp.ConsumeFuel(num3);
					ThrowFleck(frame.Position);
					if (frame.workDone >= workToBuild)
					{
						string text = ((frame.BuildDef != null) ? frame.BuildDef.label : "structure");
						frame.CompleteConstruction(pawn);
						Notify("Feignallite core: finished " + text + ".");
					}
				}
			}
		}
	}

	private void DoDeconstruct()
	{
		Map map = parent.Map;
		if (FeignalliteDef == null)
		{
			return;
		}
		bool architectWorking = ArchitectWorking;
		foreach (Thing item in Nearby().ToList())
		{
			if (item == null || item.Destroyed || !item.Spawned || item == parent || (HasQuality(item.def) && !architectWorking))
			{
				continue;
			}
			Designation designation = map.designationManager.DesignationOn(item, DesignationDefOf.Deconstruct);
			if (designation != null)
			{
				float statValueAbstract = item.def.GetStatValueAbstract(StatDefOf.WorkToBuild);
				int amount = Mathf.Max(1, Mathf.RoundToInt(FuelCostFor(statValueAbstract)));
				string label = item.def.label;
				IntVec3 position = item.Position;
				map.designationManager.RemoveDesignation(designation);
				Patch_AvirTablesPackUp.Bypass = true;
				try
				{
					item.Destroy();
				}
				finally
				{
					Patch_AvirTablesPackUp.Bypass = false;
				}
				ThrowFleck(position);
				StoreFeignallite(amount, position);
				Notify("Feignallite core: deconstructed " + label + ".");
			}
		}
	}

	private void DoHarvest()
	{
		Map map = parent.Map;
		if (FeignalliteDef == null || map == null)
		{
			return;
		}
		foreach (Thing item in Nearby().ToList())
		{
			if (item == null || item.Destroyed || !item.Spawned || !(item is Plant plant))
			{
				continue;
			}
			Designation designation = map.designationManager.DesignationOn(item, DesignationDefOf.CutPlant) ?? map.designationManager.DesignationOn(item, DesignationDefOf.HarvestPlant);
			if (designation != null)
			{
				float workAmount = plant.def.plant?.harvestWork ?? 0f;
				int num = Mathf.Max(Props.minPlantYield, Mathf.RoundToInt(FuelCostFor(workAmount) * Props.matterPerPlantWork));
				string label = plant.def.label;
				IntVec3 position = plant.Position;
				map.designationManager.RemoveDesignation(designation);
				plant.Destroy();
				ThrowFleck(position);
				if (num > 0)
				{
					StoreFeignallite(num, position);
				}
				Notify("Feignallite core: unmade " + label + ".");
			}
		}
	}

	private void EjectFeignallite()
	{
		if (FuelComp == null || FuelComp.Fuel <= 0f)
		{
			return;
		}
		ThingDef feignalliteDef = FeignalliteDef;
		if (feignalliteDef != null)
		{
			int num = Mathf.Min(feignalliteDef.stackLimit, Mathf.FloorToInt(FuelComp.Fuel));
			if (num > 0)
			{
				FuelComp.ConsumeFuel(num);
				Thing thing = ThingMaker.MakeThing(feignalliteDef);
				thing.stackCount = num;
				GenPlace.TryPlaceThing(thing, parent.Position, parent.Map, ThingPlaceMode.Near);
			}
		}
	}

	public override IEnumerable<Gizmo> CompGetGizmosExtra()
	{
		foreach (Gizmo item in base.CompGetGizmosExtra())
		{
			yield return item;
		}
		yield return new Command_Toggle
		{
			defaultLabel = (isShowNotifMessage ? "Notifications: On" : "Notifications: Off"),
			defaultDesc = "Show messages when the core builds or deconstructs something.",
			icon = CoreIcon,
			isActive = () => isShowNotifMessage,
			toggleAction = delegate
			{
				isShowNotifMessage = !isShowNotifMessage;
			}
		};
		yield return new Command_Toggle
		{
			defaultLabel = (isAutoDeconstruct ? "Auto-deconstruct: On" : "Auto-deconstruct: Off"),
			defaultDesc = "Convert player-designated, quality-less buildings into feignallite. Designated only - the core never touches anything you haven't flagged yourself.",
			icon = CoreIcon,
			isActive = () => isAutoDeconstruct,
			toggleAction = delegate
			{
				isAutoDeconstruct = !isAutoDeconstruct;
			}
		};
		yield return new Command_Action
		{
			defaultLabel = ((architect != null) ? ("Architect: " + architect.LabelShortCap) : "Architect: none"),
			defaultDesc = "Bind this core to one Avir. Only the bound pawn can open its work on things that carry quality, and their skills decide how those things turn out. Requires Fearborn, construction " + Props.minConstruction + ", crafting " + Props.minCrafting + ", intellectual " + Props.minIntellectual + ".",
			icon = CoreIcon,
			action = OpenArchitectMenu
		};
		yield return new Command_Toggle
		{
			defaultLabel = "Unmake marked plants",
			defaultDesc = "Cut and absorb plants the colony has marked for cutting or harvesting, anywhere in range. They drop nothing; they become feignallite.",
			icon = CoreIcon,
			isActive = () => isAutoHarvest,
			toggleAction = delegate
			{
				isAutoHarvest = !isAutoHarvest;
			}
		};
		yield return new Command_Action
		{
			defaultLabel = "Eject x75 feignallite",
			defaultDesc = "Drop a full stack of feignallite from the core's storage.",
			icon = CoreIcon,
			action = EjectFeignallite
		};
	}

	private void OpenArchitectMenu()
	{
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		if (architect != null)
		{
			list.Add(new FloatMenuOption("Unbind " + architect.LabelShortCap, delegate
			{
				architect = null;
				architectActiveUntilTick = -1;
			}));
		}
		Map map = parent.Map;
		if (map != null)
		{
			foreach (Pawn item in map.mapPawns.FreeColonistsSpawned)
			{
				if (item == architect)
				{
					continue;
				}
				if (Qualifies(item, Props))
				{
					Pawn captured = item;
					list.Add(new FloatMenuOption(item.LabelShortCap, delegate
					{
						architect = captured;
						architectActiveUntilTick = -1;
					}));
				}
				else
				{
					list.Add(new FloatMenuOption(item.LabelShortCap + " - not qualified", null));
				}
			}
		}
		if (list.Count == 0)
		{
			list.Add(new FloatMenuOption("No colonists available", null));
		}
		Find.WindowStack.Add(new FloatMenu(list));
	}

	public override string CompInspectStringExtra()
	{
		if (architect == null)
		{
			return "No architect bound.";
		}
		if (ArchitectWorking)
		{
			return "Architect: " + architect.LabelShortCap + " - working.";
		}
		return "Architect: " + architect.LabelShortCap + " - not at the core.";
	}

	public override void PostExposeData()
	{
		base.PostExposeData();
		Scribe_References.Look(ref architect, "avirArchitect");
		Scribe_Values.Look(ref hungerDebt, "avirHungerDebt", 0f);
		Scribe_Values.Look(ref isAutoHarvest, "avirAutoHarvest", defaultValue: true);
		Scribe_Values.Look(ref ticksUntilNextPass, "ticksUntilNextPass", 0);
		Scribe_Values.Look(ref isAutoDeconstruct, "isAutoDeconstruct", defaultValue: true);
		Scribe_Values.Look(ref isShowNotifMessage, "isShowNotifMessage", defaultValue: true);
	}
}
