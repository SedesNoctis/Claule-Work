using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Spire;

public class Building_SpireCradle : Building_GravEngine
{
	public const float EnergyPerFuel = 5f;

	public CompSpireCradle Spire => GetComp<CompSpireCradle>();

	public override Graphic Graphic => base.DefaultGraphic;

	protected override void Tick()
	{
		base.Tick();
		if (!this.IsHashIntervalTick(250))
		{
			return;
		}
		foreach (ThingComp allComp in base.AllComps)
		{
			allComp.CompTickRare();
		}
	}

	public override IEnumerable<Gizmo> GetGizmos()
	{
		string inspect = "CommandInspectGravEngine".Translate(this);
		foreach (Gizmo gizmo in base.GetGizmos())
		{
			if ((!(gizmo is Command command) || !(command.defaultLabel == inspect)) && (!(gizmo is Command_Ritual root) || !(Traverse.Create(root).Field("ritual").GetValue() is Precept_GravshipLaunch)))
			{
				yield return gizmo;
			}
		}
		if (base.Faction != Faction.OfPlayer)
		{
			yield break;
		}
		CompSpireHelm helm = GetComp<CompSpireHelm>();
		Command_Action command_Action = new Command_Action
		{
			defaultLabel = "Fly the spire",
			defaultDesc = "Lift the spire: what is marked in its lift zone rises with it. Everyone is gathered aboard as for any gravship launch; a pilot takes the cradle.",
			icon = (ContentFinder<Texture2D>.Get("UI/Commands/Avir/FlySpire", reportFailure: false) ?? ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip", reportFailure: false) ?? BaseContent.BadTex),
			action = delegate
			{
				MapComponent_SpireLift mapComponent_SpireLift2 = MapComponent_SpireLift.Of(base.Map);
				if (mapComponent_SpireLift2 != null && mapComponent_SpireLift2.Empty)
				{
					mapComponent_SpireLift2.AutoMark();
					Messages.Message("The spire will not fly without a lift zone. A first one is laid out from the colony's rooms in its domain - stretch or trim it, then fly.", this, MessageTypeDefOf.RejectInput, historical: false);
					Find.DesignatorManager.Select(new Designator_SpireLift(add: true));
				}
				else
				{
					Precept_Ritual precept_Ritual = Faction.OfPlayer.ideos?.AllIdeos.SelectMany((Ideo i) => i.PreceptsListForReading).OfType<Precept_GravshipLaunch>().FirstOrDefault();
					if (precept_Ritual != null)
					{
						Find.WindowStack.Add(precept_Ritual.GetRitualBeginWindow(this));
					}
					else
					{
						helm?.StartChoosingDestination();
					}
				}
			}
		};
		if (helm == null)
		{
			command_Action.Disable("the spire flies only with Odyssey");
		}
		else
		{
			MapComponent_SpireLift mapComponent_SpireLift = MapComponent_SpireLift.Of(base.Map);
			if (mapComponent_SpireLift == null || !mapComponent_SpireLift.Empty)
			{
				AcceptanceReport acceptanceReport = helm.CanUseNow();
				if (!acceptanceReport.Accepted)
				{
					command_Action.Disable(acceptanceReport.Reason);
				}
			}
		}
		yield return command_Action;
	}

	public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
	{
		string inspect = "CommandInspectGravEngine".Translate(this);
		foreach (FloatMenuOption floatMenuOption in base.GetFloatMenuOptions(selPawn))
		{
			if (floatMenuOption.Label != inspect)
			{
				yield return floatMenuOption;
			}
		}
	}

	public override string GetInspectString()
	{
		string obj = base.GetInspectString() ?? "";
		string text = "GravEngineNotInspected".Translate();
		return obj.Replace(text + "\n", "").Replace(text, "").Trim();
	}

	public void Domain(HashSet<IntVec3> cells, int maxCells)
	{
		MapComponent_SpireLift mapComponent_SpireLift = MapComponent_SpireLift.Of(base.Map);
		if (mapComponent_SpireLift != null)
		{
			mapComponent_SpireLift.LiftCells(this, cells, maxCells);
		}
		else
		{
			Spire?.DomainCells(cells, maxCells);
		}
	}
}
