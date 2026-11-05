using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(SkillRecord), "Learn")]
public static class Patch_ArtifactExperience
{
	private static float OffPathShare => AvirMod.Settings?.ashaignOffPathShare ?? AmuletSettingsDef.Main?.ashaignOffPathXpShare ?? 0f;

	public static void Prefix(SkillRecord __instance, ref float __0, bool __1, bool __2, out float __state)
	{
		__state = 0f;
		if (__0 <= 0f || __1)
		{
			return;
		}
		Pawn pawn = __instance.Pawn;
		if (pawn == null || __instance.TotallyDisabled)
		{
			return;
		}
		List<CompArtifactSocket> list = ArtifactWearCache.Worn(pawn);
		CompAvirAmulet compAvirAmulet = AmuletDevelopment.WornBy(pawn);
		bool flag = compAvirAmulet != null && AmuletDevelopment.WorksFor(compAvirAmulet, pawn) && AmuletDevelopment.Teaches(compAvirAmulet, __instance.def);
		bool flag2 = !flag && compAvirAmulet != null && compAvirAmulet.IsMulticlass && compAvirAmulet.HasCore && AmuletDevelopment.WorksFor(compAvirAmulet, pawn) && OffPathShare > 0f;
		if (list.Count == 0 && !flag && !flag2)
		{
			return;
		}
		ArtifactoricsSettingsDef main = ArtifactoricsSettingsDef.Main;
		if (main == null)
		{
			return;
		}
		float num = (__2 ? __0 : (__0 * __instance.LearnRateFactor()));
		if (num <= 0f)
		{
			return;
		}
		bool flag3 = false;
		bool flag4 = __instance.def == SkillDefOf.Melee;
		for (int i = 0; i < list.Count; i++)
		{
			CompArtifactSocket compArtifactSocket = list[i];
			ArtifactState inlaid = compArtifactSocket.inlaid;
			if (inlaid == null || !inlaid.HasHeart || compArtifactSocket.Self)
			{
				continue;
			}
			float xp = num;
			if (compArtifactSocket.Carrier == ArtifactCarrier.Weapon)
			{
				bool flag5 = __instance.def == SkillDefOf.Shooting && SpearMode.IsOn(compArtifactSocket.parent, pawn);
				if (!flag4 && !flag5)
				{
					continue;
				}
				xp = num * main.weaponXpFactor;
			}
			flag3 = true;
			if (inlaid.AddXp(xp) > 0 && pawn.Faction == Faction.OfPlayer)
			{
				Messages.Message(pawn.LabelShort + "'s artifact in " + compArtifactSocket.parent.LabelNoCount + " gained a development point (" + inlaid.PointsAvailable + " free).", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}
		if (flag)
		{
			flag3 = true;
			if (compAvirAmulet.AddXp(num) > 0 && pawn.Faction == Faction.OfPlayer)
			{
				Messages.Message(pawn.LabelShort + "'s " + compAvirAmulet.parent.LabelNoCount + " gained a development point (" + AmuletDevelopment.PointsAvailable(compAvirAmulet) + " free).", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}
		float num2 = 0f;
		if (flag2)
		{
			num2 = num * (1f - main.pawnShare) * OffPathShare;
			if (compAvirAmulet.AddXp(num2) > 0 && pawn.Faction == Faction.OfPlayer)
			{
				Messages.Message(pawn.LabelShort + "'s " + compAvirAmulet.parent.LabelNoCount + " gained a development point (" + AmuletDevelopment.PointsAvailable(compAvirAmulet) + " free).", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}
		if (!flag3)
		{
			if (!(num2 <= 0f))
			{
				__0 *= (num - num2) / num;
				__state = num2;
			}
			return;
		}
		float num3 = num * main.pawnShare;
		float num4 = num * main.repairShareWhileRepairing;
		float num5 = ArtifactLearning.Mend(pawn, num4);
		if (num5 > 0f)
		{
			num3 = Mathf.Max(0f, num * main.pawnShareWhileRepairing + (num4 - num5));
		}
		__0 *= ((num > 0f) ? (num3 / num) : 0f);
		__state = num - num3;
		ArtifactLearning.TryInsight(pawn, __instance);
	}

	public static void Postfix(SkillRecord __instance, float __state)
	{
		if (__state > 0f)
		{
			__instance.xpSinceMidnight += __state;
		}
		ArtifactLearning.FlushInsight(__instance);
	}
}
