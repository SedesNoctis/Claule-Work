using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Pawn), "PostApplyDamage")]
public static class Patch_StriderArrowEffects
{
	public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
	{
		if (!StriderHit.IsArrow(dinfo, out var archer, out var bow))
		{
			return;
		}
		Pawn t = __instance;
		if (t.Dead || !t.Spawned)
		{
			return;
		}
		float num = StriderPaths.Power(bow, archer);
		int stones = bow.Stones;
		Map map = t.Map;
		if (stones >= 3)
		{
			StriderBowFx.Impact(bow.path, t.DrawPos, map, 0.9f);
		}
		switch (bow.path)
		{
		case "Hunt":
			if (stones >= 3 && t.RaceProps.IsFlesh && Rand.Chance(Mathf.Clamp01(0.35f * num)))
			{
				StriderHit.Hit(t, archer, 4f * num, DamageDefOf.Cut, ArtifactCE.Pen(0.5f, 8f));
			}
			break;
		case "Storm":
			if (stones >= 3)
			{
				StriderHit.Hit(t, archer, 8f * num, DamageDefOf.EMP, 0f);
			}
			if (stones >= 9 && Rand.Chance(Mathf.Clamp01(0.3f * num)))
			{
				Pawn pawn2 = (from o in GenRadial.RadialDistinctThingsAround(t.Position, map, 5f, useCenter: true).OfType<Pawn>()
					where o != t && !o.Dead && !o.Downed && o.HostileTo(archer)
					orderby o.Position.DistanceToSquared(t.Position)
					select o).FirstOrDefault();
				if (pawn2 != null)
				{
					StriderBowFx.Beam(t.DrawPos, pawn2.DrawPos, map, StriderBowFx.ColorOf("Storm"), 0.35f);
					StriderBowFx.Impact("Storm", pawn2.DrawPos, map, 0.8f);
					StriderHit.Hit(pawn2, archer, 10f * num, DamageDefOf.Burn, ArtifactCE.Pen(0.3f, 4f));
					StriderHit.Hit(pawn2, archer, 6f * num, DamageDefOf.EMP, 0f);
				}
			}
			break;
		case "Flame":
			if (stones >= 3 && Rand.Chance(Mathf.Clamp01(0.3f * num)))
			{
				t.TryAttachFire(0.4f * num, archer);
			}
			break;
		case "Moon":
			if (stones >= 9)
			{
				Vector3 dir = (t.DrawPos - archer.DrawPos).normalized;
				Pawn pawn = (from o in GenRadial.RadialDistinctThingsAround(t.Position, map, 4f, useCenter: true).OfType<Pawn>()
					where o != t && !o.Dead && o.HostileTo(archer) && Vector3.Dot((o.DrawPos - t.DrawPos).normalized, dir) > 0.8f
					orderby o.Position.DistanceToSquared(t.Position)
					select o).FirstOrDefault();
				if (pawn != null)
				{
					StriderBowFx.Beam(t.DrawPos, pawn.DrawPos, map, StriderBowFx.ColorOf("Moon"), 0.3f);
					StriderHit.Hit(pawn, archer, dinfo.Amount * 0.6f, StriderHit.Arrow, dinfo.ArmorPenetrationInt);
				}
			}
			break;
		case "Gravity":
			if (stones >= 3 && Rand.Chance(Mathf.Clamp01(0.3f * num)))
			{
				IntVec3 intVec = t.Position + (t.Position - archer.Position).ToVector3().normalized.ToIntVec3();
				if (intVec != t.Position && intVec.InBounds(map) && intVec.Standable(map) && intVec.GetFirstPawn(map) == null)
				{
					t.Position = intVec;
					t.Notify_Teleported(endCurrentJob: false);
					FleckMaker.ThrowDustPuff(intVec, map, 0.8f);
				}
			}
			if (stones >= 9)
			{
				t.stances?.stagger?.StaggerFor(Mathf.RoundToInt(90f * num));
				if (Rand.Chance(Mathf.Clamp01(0.15f * num)))
				{
					t.stances?.stunner?.StunFor(Mathf.RoundToInt(60f * num), archer, addBattleLog: false);
				}
			}
			break;
		}
	}
}
