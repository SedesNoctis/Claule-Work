using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

public static class LinarianNights
{
	public static void Night(Map map)
	{
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (!item.Dead && item.health?.hediffSet != null)
			{
				SlowDevour(item);
				Worship(item, map);
				ScarRot(item);
			}
		}
		foreach (Pawn item2 in MapComponent_LinarianSong.HeraldsOn(map).ToList())
		{
			Gene_LinarianSong gene_LinarianSong = Linarian.Song(item2);
			if (gene_LinarianSong != null && !item2.Dead && !item2.Downed && item2.Faction != Faction.OfPlayer && gene_LinarianSong.Has("D4") && gene_LinarianSong.offender != null && !gene_LinarianSong.offender.Dead && gene_LinarianSong.offender.MapHeld == map)
			{
				LinarianActs.Verdict(item2, gene_LinarianSong, gene_LinarianSong.offender);
				gene_LinarianSong.offender = null;
			}
		}
	}

	private static bool Gone(Pawn her)
	{
		if (her != null && !her.Dead)
		{
			return her.Destroyed;
		}
		return true;
	}

	public static void Sleep(Map map, int now)
	{
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (item.Dead || item.health?.hediffSet == null)
			{
				continue;
			}
			Hediff firstHediffOfDef = item.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_SlowDevour);
			if (firstHediffOfDef == null || !LinarianTactics.Asleep(item))
			{
				continue;
			}
			Pawn pawn = LinarianSource.Of(item, LinarianHediffs.Avir_SlowDevour);
			if (pawn != null && pawn.Faction == Faction.OfPlayer)
			{
				HediffComp_DevourFeeds hediffComp_DevourFeeds = firstHediffOfDef.TryGetComp<HediffComp_DevourFeeds>();
				if (hediffComp_DevourFeeds != null && now - hediffComp_DevourFeeds.lastNight >= 50000)
				{
					hediffComp_DevourFeeds.lastNight = now;
					SlowDevour(item, bySleep: true);
				}
			}
		}
		foreach (Pawn item2 in MapComponent_LinarianSong.HeraldsOn(map).ToList())
		{
			if (item2.Faction == Faction.OfPlayer && !item2.Dead && !item2.Downed)
			{
				Gene_LinarianSong gene_LinarianSong = Linarian.Song(item2);
			}
		}
	}

	private static void SlowDevour(Pawn q, bool bySleep = false)
	{
		Hediff firstHediffOfDef = q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_SlowDevour);
		if (firstHediffOfDef == null)
		{
			return;
		}
		Pawn pawn = LinarianSource.Of(q, LinarianHediffs.Avir_SlowDevour);
		if (!bySleep && !Gone(pawn) && pawn.Faction == Faction.OfPlayer)
		{
			return;
		}
		if (Gone(pawn))
		{
			q.health.RemoveHediff(firstHediffOfDef);
			Hediff firstHediffOfDef2 = q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_DreamMark);
			if (firstHediffOfDef2 != null)
			{
				q.health.RemoveHediff(firstHediffOfDef2);
			}
			return;
		}
		Gene_LinarianSong gene_LinarianSong = Linarian.Song(pawn);
		if (gene_LinarianSong != null && Rand.Chance(0.15f * (1f - LinarianTactics.Chance(pawn, gene_LinarianSong, q, callOrSleep: true, "D"))))
		{
			q.health.RemoveHediff(firstHediffOfDef);
			if (pawn.Faction == Faction.OfPlayer)
			{
				Messages.Message(q.LabelShortCap + " woke screaming in the night - and the slow devouring is broken.", q, MessageTypeDefOf.NeutralEvent, historical: false);
			}
			return;
		}
		firstHediffOfDef.Severity = Mathf.Min(1f, firstHediffOfDef.Severity + 0.1f);
		if (gene_LinarianSong != null)
		{
			gene_LinarianSong.Learn(100f);
			Gene_ArcaneHunger.Feed(pawn, 0.05f);
			if (gene_LinarianSong.Dark("D") && q.ageTracker != null)
			{
				q.ageTracker.AgeBiologicalTicks += 3600000L;
			}
		}
		if (firstHediffOfDef.Severity >= 0.999f)
		{
			bool num = q.Faction == Faction.OfPlayer;
			string labelShortCap = q.LabelShortCap;
			LinarianDevour.Devour(pawn, q);
			if (num)
			{
				Find.LetterStack.ReceiveLetter("Died in sleep", labelShortCap + " did not wake. Night after night something drank from them, and tonight it finished.", LetterDefOf.Death, q.Corpse);
			}
		}
	}

	private static void Worship(Pawn q, Map map)
	{
		Hediff firstHediffOfDef = q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_Adoration);
		if (firstHediffOfDef == null)
		{
			return;
		}
		Pawn pawn = LinarianSource.Of(q, LinarianHediffs.Avir_Adoration);
		if (Gone(pawn))
		{
			q.health.RemoveHediff(firstHediffOfDef);
		}
		else if (pawn.Faction != Faction.OfPlayer)
		{
			Gene_LinarianSong gene_LinarianSong = Linarian.Song(pawn);
			firstHediffOfDef.Severity = Mathf.Min(1f, firstHediffOfDef.Severity + ((gene_LinarianSong != null && gene_LinarianSong.Has("C5")) ? 0.35f : 0.25f));
			if (gene_LinarianSong != null && gene_LinarianSong.Dark("C"))
			{
				Gene_ArcaneHunger.Feed(pawn, 0.03f);
			}
			if (gene_LinarianSong != null && gene_LinarianSong.Has("C2") && firstHediffOfDef.Severity >= 0.25f && pawn.MapHeld == map && pawn.Spawned && !q.Downed && (pawn.Faction == null || pawn.Faction != q.Faction))
			{
				LinarianMental.Start(q, pawn, LinarianMental.CultGuard, 10000);
			}
		}
	}

	private static void ScarRot(Pawn q)
	{
		Hediff firstHediffOfDef = q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.PhantomScar);
		Pawn pawn = ((firstHediffOfDef == null) ? null : LinarianSource.Of(q, LinarianHediffs.PhantomScar));
		if (Gone(pawn))
		{
			return;
		}
		Gene_LinarianSong gene_LinarianSong = Linarian.Song(pawn);
		if (gene_LinarianSong == null || !gene_LinarianSong.Dark("S") || !Rand.Chance(0.03f * Mathf.Max(1f, firstHediffOfDef.Severity * 2f)))
		{
			return;
		}
		HediffDef def = (Rand.Bool ? HediffDefOf.Dementia : (DefDatabase<HediffDef>.GetNamedSilentFail("Alzheimers") ?? HediffDefOf.Dementia));
		BodyPartRecord brain = q.health.hediffSet.GetBrain();
		if (brain != null && !q.health.hediffSet.HasHediff(def))
		{
			q.health.AddHediff(def, brain);
			if (q.Faction == Faction.OfPlayer)
			{
				Messages.Message(q.LabelShortCap + "'s phantom scars have reached the mind.", q, MessageTypeDefOf.NegativeHealthEvent, historical: false);
			}
		}
	}
}
