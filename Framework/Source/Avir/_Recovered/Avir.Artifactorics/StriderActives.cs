using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics;

public static class StriderActives
{
	public const int CooldownTicks = 10000;

	public static float Cost => 0.1f;

	private static string Label(string path)
	{
		return path switch
		{
			"Hunt" => "Judgement", 
			"Storm" => "Sky Strike", 
			"Flame" => "Fire Rain", 
			"Moon" => "Moon Arrow", 
			_ => "Gravity Knot", 
		};
	}

	private static string Desc(string path)
	{
		return path switch
		{
			"Hunt" => "One arrow that weighs everything the target has ever done to your people: the more harm it has dealt them, the harder it strikes.", 
			"Storm" => "Lightning falls where the archer points, and the storm of it blinds every machine around.", 
			"Flame" => "Burning arrows rain over an area.", 
			"Moon" => "A shot across the whole map: no cover, no distance stops it.", 
			_ => "Every enemy around the point is pulled into it and staggered.", 
		};
	}

	public static Command Command(Pawn p, CompStriderBow b)
	{
		bool cell = b.path == "Storm" || b.path == "Flame" || b.path == "Gravity";
		ArtifactState artifactState = StriderBow.Quiver(b.parent);
		Command_Target command_Target = new Command_Target
		{
			defaultLabel = Label(b.path),
			defaultDesc = Desc(b.path) + "\n\nCosts " + Cost.ToStringPercent() + " of the bow's charge.",
			icon = (SphereIcons.StriderActive(b.path) ?? ContentFinder<Texture2D>.Get(b.parent.def.graphicData.texPath, reportFailure: false) ?? BaseContent.BadTex),
			targetingParams = new TargetingParameters
			{
				canTargetLocations = cell,
				canTargetPawns = !cell,
				canTargetBuildings = false,
				canTargetItems = false,
				validator = (TargetInfo ti) => (!cell) ? (ti.Thing is Pawn { Dead: false } pawn && pawn.HostileTo(p)) : ti.Cell.IsValid
			},
			action = delegate(LocalTargetInfo ti)
			{
				Use(p, b, ti);
			}
		};
		int num = b.activeReadyTick - Find.TickManager.TicksGame;
		if (num > 0)
		{
			command_Target.Disable("Ready in " + num.ToStringTicksToPeriod() + ".");
		}
		else if (artifactState == null || artifactState.charge < Cost)
		{
			command_Target.Disable("The artifact in the bow lacks the charge.");
		}
		else if (p.Downed || !p.Spawned)
		{
			command_Target.Disable("Cannot draw now.");
		}
		return command_Target;
	}

	public static void Use(Pawn p, CompStriderBow b, LocalTargetInfo ti)
	{
		ArtifactState artifactState = StriderBow.Quiver(b.parent);
		if (artifactState == null || artifactState.charge < Cost || !p.Spawned)
		{
			return;
		}
		Map map = p.Map;
		float num = StriderPaths.Power(b, p);
		artifactState.charge -= Cost;
		b.activeReadyTick = Find.TickManager.TicksGame + 10000;
		DefDatabase<SoundDef>.GetNamedSilentFail("Bow_Large")?.PlayOneShot(new TargetInfo(p.Position, map));
		StriderHit.inner = true;
		try
		{
			switch (b.path)
			{
			case "Hunt":
				if (ti.Thing is Pawn { Spawned: not false } pawn)
				{
					float a = GameComponent_StriderGrudges.Instance?.Dealt(pawn, p.Faction) ?? 0f;
					float amount = (25f + Mathf.Min(a, 400f) * 0.5f) * num;
					StriderBowFx.Beam(p.DrawPos, pawn.DrawPos, map, StriderBowFx.Judgement, 0.6f);
					StriderBowFx.Spawn("Judgement", pawn.DrawPos + new Vector3(0f, 0f, 0.6f), map, StriderBowFx.Judgement, 1.6f, 0f);
					pawn.TakeDamage(new DamageInfo(StriderHit.Arrow, amount, ArtifactCE.Pen(1f, 30f), -1f, p, null, b.parent.def));
					if (p.Faction == Faction.OfPlayer)
					{
						Messages.Message("Judgement: " + pawn.LabelShortCap + " had dealt your people " + a.ToString("0") + " harm.", pawn, MessageTypeDefOf.NeutralEvent, historical: false);
					}
				}
				return;
			case "Storm":
			{
				map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, ti.Cell));
				StriderBowFx.Spawn("Storm", ti.Cell.ToVector3Shifted(), map, StriderBowFx.ColorOf("Storm"), 4f);
				IntVec3 cell = ti.Cell;
				float radius = 3.9f * Mathf.Sqrt(num);
				DamageDef eMP = DamageDefOf.EMP;
				Pawn instigator = p;
				int damAmount = Mathf.RoundToInt(20f * num);
				List<Thing> ignoredThings = new List<Thing> { p };
				GenExplosion.DoExplosion(cell, map, radius, eMP, instigator, damAmount, -1f, null, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors: false, null, 0f, 1, 0f, damageFalloff: false, null, ignoredThings);
				return;
			}
			case "Flame":
			{
				for (int i = 0; i < 6; i++)
				{
					IntVec3 intVec = ti.Cell + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(4f))];
					if (intVec.InBounds(map))
					{
						StriderBowFx.Spawn("FireFall", intVec.ToVector3Shifted() + new Vector3(0f, 0f, 0.8f), map, StriderBowFx.ColorOf("Flame"), 1.4f, 0f);
						IntVec3 center = intVec;
						DamageDef flame = DamageDefOf.Flame;
						Pawn instigator2 = p;
						int damAmount2 = Mathf.RoundToInt(10f * num);
						List<Thing> ignoredThings = new List<Thing> { p };
						GenExplosion.DoExplosion(center, map, 1.5f, flame, instigator2, damAmount2, -1f, null, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors: false, null, 0f, 1, 0f, damageFalloff: false, null, ignoredThings);
					}
				}
				return;
			}
			case "Moon":
				if (ti.Thing is Pawn { Spawned: not false } pawn2)
				{
					StriderBowFx.Beam(p.DrawPos, pawn2.DrawPos, map, StriderBowFx.ColorOf("Moon"), 0.7f);
					StriderBowFx.Impact("Moon", pawn2.DrawPos, map, 1.6f);
					pawn2.TakeDamage(new DamageInfo(StriderHit.Arrow, 45f * num, ArtifactCE.Pen(1.2f, 35f), -1f, p, null, b.parent.def));
				}
				return;
			}
			foreach (Pawn item in (from o in GenRadial.RadialDistinctThingsAround(ti.Cell, map, 6f, useCenter: true).OfType<Pawn>()
				where !o.Dead && o.HostileTo(p)
				select o).ToList())
			{
				IntVec3 intVec2 = CellFinder.StandableCellNear(ti.Cell, map, 1.9f);
				if (intVec2.IsValid && intVec2.GetFirstPawn(map) == null)
				{
					FleckMaker.ThrowDustPuff(item.Position, map, 1f);
					item.Position = intVec2;
					item.Notify_Teleported();
				}
				item.stances?.stunner?.StunFor(Mathf.RoundToInt(120f * num), p, addBattleLog: false);
			}
			StriderBowFx.Spawn("Gravity", ti.Cell.ToVector3Shifted(), map, StriderBowFx.ColorOf("Gravity"), 7f);
		}
		finally
		{
			StriderHit.inner = false;
		}
	}
}
