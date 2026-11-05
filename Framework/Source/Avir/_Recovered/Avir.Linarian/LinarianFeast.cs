using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

public static class LinarianFeast
{
	public const float StoreFull = 1000f;

	public const float StoreOver = 1200f;

	public const float PerHunger = 1000f;

	public const float XpPerEssence = 1.5f;

	public static int Uses => Mathf.Max(1, AvirMod.Settings?.singerVoiceUses ?? 5);

	public static float ForgottenShare => AvirMod.Settings?.singerForgottenShare ?? 0.25f;

	public static float FadeDays => AvirMod.Settings?.singerFadeDays ?? 30f;

	public static int Count(Gene_LinarianSong g)
	{
		return g.voices.Sum((SongVoice v) => Mathf.Max(1, v.count));
	}

	public static float Worth(Gene_LinarianSong g)
	{
		return g.voices.Sum((SongVoice v) => (!v.forgotten) ? v.worth : (v.worth * ForgottenShare));
	}

	public static void Added(Gene_LinarianSong g, SongVoice v)
	{
		v.id = ++g.nextVoice;
		v.uses = Uses;
		v.since = Find.TickManager?.TicksGame ?? 0;
		Merge(g);
	}

	public static List<SongVoice> Chosen(Gene_LinarianSong g)
	{
		List<SongVoice> list = new List<SongVoice>();
		foreach (IGrouping<SkillDef, SongVoice> item in from v in g.voices
			where v.Usable
			group v by v.skill)
		{
			int id;
			SongVoice songVoice = (g.pins.TryGetValue(item.Key.defName, out id) ? item.FirstOrDefault((SongVoice v) => v.id == id) : null);
			list.Add(songVoice ?? (from v in item
				orderby v.level descending, v.uses descending
				select v).First());
		}
		return list.OrderByDescending((SongVoice v) => v.level).ToList();
	}

	public static bool IsChosen(Gene_LinarianSong g, SongVoice v)
	{
		if (v.Usable)
		{
			return Chosen(g).Contains(v);
		}
		return false;
	}

	public static void Spend(Gene_LinarianSong g, SongVoice v)
	{
		v.uses--;
		v.since = Find.TickManager.TicksGame;
		if (v.uses <= 0)
		{
			Forget(g, v);
		}
	}

	private static void Forget(Gene_LinarianSong g, SongVoice v)
	{
		v.forgotten = true;
		v.uses = 0;
		if (v.skill != null && g.pins.TryGetValue(v.skill.defName, out var value) && value == v.id)
		{
			g.pins.Remove(v.skill.defName);
		}
		v.skill = null;
	}

	public static void Merge(Gene_LinarianSong g)
	{
		foreach (IGrouping<SkillDef, SongVoice> item in (from v in g.voices
			where v.Usable && v.level < 10 && !Pinned(g, v)
			group v by v.skill).ToList())
		{
			List<SongVoice> list = item.OrderBy((SongVoice v) => v.level).ToList();
			while (list.Count >= 2)
			{
				List<SongVoice> list2 = new List<SongVoice>();
				int num = 0;
				foreach (SongVoice item2 in list)
				{
					list2.Add(item2);
					num += item2.level;
					if (num >= 10)
					{
						break;
					}
				}
				if (num < 10 || list2.Count < 2)
				{
					break;
				}
				foreach (SongVoice item3 in list2)
				{
					list.Remove(item3);
					g.voices.Remove(item3);
				}
				g.voices.Add(new SongVoice
				{
					id = ++g.nextVoice,
					name = string.Join(" + ", list2.Select((SongVoice v) => v.name)),
					skill = item.Key,
					level = Mathf.Min(20, num),
					worth = list2.Sum((SongVoice v) => v.worth),
					count = list2.Sum((SongVoice v) => Mathf.Max(1, v.count)),
					uses = Uses,
					merged = true,
					since = Find.TickManager.TicksGame
				});
			}
		}
	}

	private static bool Pinned(Gene_LinarianSong g, SongVoice v)
	{
		if (v.skill != null && g.pins.TryGetValue(v.skill.defName, out var value))
		{
			return value == v.id;
		}
		return false;
	}

	public static void Fade(Gene_LinarianSong g)
	{
		int now = Find.TickManager.TicksGame;
		int limit = Mathf.RoundToInt(FadeDays * 60000f);
		List<SongVoice> chosen = Chosen(g);
		foreach (SongVoice item in g.voices.Where((SongVoice v) => v.Usable && !chosen.Contains(v) && now - v.since > limit).ToList())
		{
			Forget(g, item);
		}
	}

	public static float Cap(Gene_LinarianSong g)
	{
		if (!g.Has("F3"))
		{
			return 1000f;
		}
		return 1200f;
	}

	public static void Store(Gene_LinarianSong g, float essence)
	{
		if (!(essence <= 0f))
		{
			g.store = Mathf.Min(Cap(g), g.store + essence);
		}
	}

	public static void FeedOrStore(Pawn p, Gene_LinarianSong g, float hunger, float storeShare)
	{
		Gene_ArcaneHunger gene_ArcaneHunger = Gene_ArcaneHunger.Of(p);
		float num = Mathf.Min((gene_ArcaneHunger != null && gene_ArcaneHunger.Active) ? (1f - gene_ArcaneHunger.Value) : 0f, hunger);
		if (num > 0f)
		{
			Gene_ArcaneHunger.Feed(p, num);
		}
		if (g != null && storeShare > 0f)
		{
			Store(g, (hunger - num) * 1000f * storeShare);
		}
	}

	public static float PayFromOverflow(Gene_LinarianSong g, float cost)
	{
		if (!g.Has("F3") || cost <= 0f || g.store <= 1000f)
		{
			return cost;
		}
		float num = Mathf.Min(cost * 1000f, g.store - 1000f);
		g.store -= num;
		return cost - num / 1000f;
	}

	public static float Overflow(Gene_LinarianSong g)
	{
		if (!g.Has("F3"))
		{
			return 0f;
		}
		return Mathf.Max(0f, g.store - 1000f) / 1000f;
	}

	public static void Release(Pawn p, Gene_LinarianSong g)
	{
		Gene_ArcaneHunger gene_ArcaneHunger = Gene_ArcaneHunger.Of(p);
		float b = ((gene_ArcaneHunger != null && gene_ArcaneHunger.Active) ? ((1f - gene_ArcaneHunger.Value) * 1000f) : 0f);
		if (!g.storeDump)
		{
			float num = Mathf.Min(g.store, b);
			Gene_ArcaneHunger.Feed(p, num / 1000f);
			g.store -= num;
			LinarianTactics.Notify(p, "she feeds from her store (" + Mathf.RoundToInt(num) + " essence).");
			return;
		}
		float store = g.store;
		g.store = 0f;
		float num2 = Mathf.Min(store, b);
		Gene_ArcaneHunger.Feed(p, num2 / 1000f);
		float num3 = store - num2;
		if (num3 <= 0f)
		{
			LinarianTactics.Notify(p, "her store only filled her hunger.");
			return;
		}
		if (g.songLevel < Gene_LinarianSong.MaxLevel)
		{
			g.Learn(num3 * 1.5f);
			LinarianTactics.Notify(p, "she pours out her store: " + Mathf.RoundToInt(num3) + " essence into her aria.");
			return;
		}
		HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StoreSurge");
		if (namedSilentFail != null)
		{
			int num4 = Mathf.RoundToInt(num3 / 100f * 3.6f * 2500f);
			LinarianHediffs.Add(p, namedSilentFail, num4);
			LinarianTactics.Notify(p, "her aria has nothing left to learn: the store surges through her for " + num4.ToStringTicksToPeriod() + ".");
		}
	}

	public static bool Surging(Pawn p)
	{
		HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StoreSurge");
		if (namedSilentFail != null)
		{
			if (p == null)
			{
				return false;
			}
			return p.health?.hediffSet?.HasHediff(namedSilentFail) == true;
		}
		return false;
	}

	public static bool FalseRetreat(Pawn her, Pawn q)
	{
		MentalStateDef namedSilentFail = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_FalseRetreat");
		if (!LinarianMental.Start(q, her, namedSilentFail, 2500))
		{
			return false;
		}
		if (q.MentalState is MentalState_SongRetreat mentalState_SongRetreat)
		{
			mentalState_SongRetreat.herald = her;
			mentalState_SongRetreat.until = Find.TickManager.TicksGame + 3750;
		}
		return true;
	}

	public static bool Confuse(Pawn her, Pawn q, int ticks)
	{
		MentalStateDef namedSilentFail = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_ScreamConfused");
		return LinarianMental.Start(q, her, namedSilentFail, ticks);
	}

	public static void DropWeapon(Pawn q, ref ThingWithComps dropped)
	{
		ThingWithComps thingWithComps = q?.equipment?.Primary;
		if (thingWithComps != null && q.Spawned && q.equipment.TryDropEquipment(thingWithComps, out var resultingEq, q.Position))
		{
			dropped = resultingEq;
		}
	}
}
