using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public static class LinarianCourtship
{
	public static readonly string[] Tastes = new string[5] { "beauty", "strength", "craft", "art", "mind" };

	private static readonly Dictionary<int, int> following = new Dictionary<int, int>();

	private static HediffDef Bond => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_FormingBond");

	public static bool Rolls(Pawn her, bool primal)
	{
		Gene_LinarianSong gene_LinarianSong = Linarian.Song(her);
		if (gene_LinarianSong != null && gene_LinarianSong.shadow)
		{
			return Rand.Chance(0.0001f);
		}
		float num = ((her.ageTracker != null && her.ageTracker.AgeBiologicalYears < 23) ? 1f : 0.2f);
		if (primal)
		{
			num *= 0.5f;
		}
		return Rand.Chance(num);
	}

	public static float Score(Pawn c, string taste)
	{
		float value = Mathf.Clamp(c.GetStatValue(StatDefOf.PawnBeauty) * 4f + 6f, 0f, 20f);
		float value2 = (float)(Lv(SkillDefOf.Melee) + Lv(SkillDefOf.Shooting)) / 2f;
		Dictionary<string, float> obj = new Dictionary<string, float>
		{
			{ "beauty", value },
			{ "strength", value2 },
			{
				"craft",
				Lv(SkillDefOf.Crafting)
			},
			{
				"art",
				Lv(SkillDefOf.Artistic)
			},
			{
				"mind",
				Lv(SkillDefOf.Intellectual)
			}
		};
		float num = 0f;
		float num2 = 0f;
		foreach (KeyValuePair<string, float> item in obj)
		{
			float num3 = ((item.Key == taste) ? 3f : 1f);
			num += item.Value * num3;
			num2 += num3;
		}
		return num / num2;
		int Lv(SkillDef s)
		{
			return (c.skills?.GetSkill(s)?.Level).GetValueOrDefault();
		}
	}

	public static bool Fits(Pawn c, Pawn her)
	{
		if (c == null || c.Dead || c.gender != Gender.Male || !c.RaceProps.Humanlike || Linarian.Is(c))
		{
			return false;
		}
		if (!AvirRhythmUtility.IsAvir(c) || c.Faction != Faction.OfPlayer)
		{
			return false;
		}
		if (AvirPairBond.IsBonded(c) || AvirPairBond.HasLostAMate(c))
		{
			return false;
		}
		Pawn_RelationsTracker relations = c.relations;
		if (relations != null && relations.Children?.Any() == true)
		{
			return false;
		}
		return c.ageTracker.AgeBiologicalYears >= 18;
	}

	public static Pawn Choose(HeraldRecord r, Map map)
	{
		return (from c in map.mapPawns.FreeColonistsSpawned
			where Fits(c, r.her) && c.GetStatValue(StatDefOf.PawnBeauty) >= 0f && Score(c, r.taste) >= 8f
			orderby Score(c, r.taste) descending
			select c).FirstOrDefault();
	}

	public static void Night(HeraldRecord r, Map map)
	{
		if (!r.courts || r.joined || r.her == null)
		{
			return;
		}
		Pawn pawn = r.beloved;
		if (pawn != null && (pawn.Dead || (!pawn.Spawned && !pawn.IsPrisonerOfColony) || (AvirPairBond.IsBonded(pawn) && !Lovers(pawn, r.her))))
		{
			Abandon(r);
			return;
		}
		if (pawn == null)
		{
			pawn = Choose(r, map);
			if (pawn == null)
			{
				return;
			}
			r.beloved = pawn;
			r.favourite = pawn;
		}
		if (pawn.IsPrisonerOfColony)
		{
			Held(r, map);
		}
		else if (r.calledAt < 0)
		{
			Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Bond) ?? ((Bond != null) ? pawn.health.AddHediff(Bond) : null);
			if (hediff == null)
			{
				return;
			}
			hediff.Severity = Mathf.Min(1f, hediff.Severity + Rand.Range(0.14f, 0.22f));
			ThoughtDef namedSilentFail = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_DreamOfWings");
			if (namedSilentFail != null)
			{
				pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(namedSilentFail);
			}
			if (pawn.needs?.rest != null)
			{
				pawn.needs.rest.CurLevel = 1f;
			}
			foreach (Hediff_Injury item in (from i in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>()
				where !i.IsPermanent()
				select i).Take(2).ToList())
			{
				item.Heal(4f);
			}
			if (hediff.Severity >= 0.999f)
			{
				Call(r);
			}
		}
		else if (r.choice == 1 && r.serveSince > 0 && Find.TickManager.TicksGame - r.serveSince > 60000 * Rand.Range(3, 6))
		{
			Join(r);
		}
	}

	public static bool Following(HeraldRecord r, int now)
	{
		if (r.her != null && following.TryGetValue(r.her.thingIDNumber, out var value))
		{
			return now < value;
		}
		return false;
	}

	public static void Tick(HeraldRecord r, Map map, int now)
	{
		if (!r.courts || r.her == null || now % 250 != 0)
		{
			return;
		}
		Pawn him = r.beloved;
		if (him == null)
		{
			return;
		}
		if (!r.joined && !r.leaving && r.her.Spawned && him.Spawned && him.Map == map && !him.IsPrisonerOfColony && r.calledAt < 0)
		{
			if (!Following(r, now) && now % 2500 == 0 && Rand.Chance(0.35f))
			{
				following[r.her.thingIDNumber] = now + 5000;
			}
			if (Following(r, now) && !r.her.Downed && (r.her.Position - him.Position).LengthHorizontal > 11f && CellFinder.TryFindRandomReachableNearbyCell(him.Position, map, 9f, TraverseParms.For(r.her), (IntVec3 c) => c.Standable(map) && (c - him.Position).LengthHorizontal >= 5f, null, out var result))
			{
				Job job = JobMaker.MakeJob(JobDefOf.Goto, result);
				job.locomotionUrgency = LocomotionUrgency.Walk;
				r.her.jobs.StartJob(job, JobCondition.InterruptForced);
			}
		}
		if (him.IsPrisonerOfColony)
		{
			r.wasHeld = true;
		}
		if (r.calledAt > 0 && r.choice == 0 && !him.IsPrisonerOfColony && now - r.calledAt > 30000)
		{
			r.choice = 2;
			GoToHer(r);
		}
		if (r.wasHeld && !him.IsPrisonerOfColony && him.Faction == Faction.OfPlayer && !r.joined)
		{
			r.wasHeld = false;
			GoToHer(r);
		}
		if (r.leaving && him.Spawned && him.Faction == null && (him.CurJob == null || !him.CurJob.exitMapOnArrival) && RCellFinder.TryFindBestExitSpot(him, out var spot))
		{
			Job job2 = JobMaker.MakeJob(JobDefOf.Goto, spot);
			job2.exitMapOnArrival = true;
			job2.locomotionUrgency = LocomotionUrgency.Walk;
			him.jobs.StartJob(job2, JobCondition.InterruptForced);
		}
	}

	private static bool Lovers(Pawn a, Pawn b)
	{
		Pawn_RelationsTracker relations = a.relations;
		if (relations == null || !relations.DirectRelationExists(PawnRelationDefOf.Lover, b))
		{
			return a.relations?.DirectRelationExists(PawnRelationDefOf.Spouse, b) ?? false;
		}
		return true;
	}

	private static void Abandon(HeraldRecord r)
	{
		Pawn beloved = r.beloved;
		Hediff hediff = ((Bond == null) ? null : beloved?.health?.hediffSet?.GetFirstHediffOfDef(Bond));
		if (hediff != null)
		{
			beloved.health.RemoveHediff(hediff);
		}
		r.beloved = null;
		r.calledAt = -1;
		r.choice = 0;
	}

	private static void Call(HeraldRecord r)
	{
		r.calledAt = Find.TickManager.TicksGame;
		ChoiceLetter_HerBeloved choiceLetter_HerBeloved = (ChoiceLetter_HerBeloved)LetterMaker.MakeLetter(DefDatabase<LetterDef>.GetNamed("Avir_HerBelovedLetter"));
		choiceLetter_HerBeloved.her = r.her;
		choiceLetter_HerBeloved.him = r.beloved;
		choiceLetter_HerBeloved.Label = "Called in the night";
		choiceLetter_HerBeloved.Text = r.beloved.LabelShort + " woke in the night smiling, and has been looking towards the trees ever since. Someone out there has been courting him in his dreams - and now she calls.\n\nHe will go to her. You may let him serve her, and she may come to you in time to nest where he is; or you may let him go, and lose them both to the wild. Keeping him here will take a cell.";
		choiceLetter_HerBeloved.lookTargets = r.beloved;
		Find.LetterStack.ReceiveLetter(choiceLetter_HerBeloved);
	}

	public static void Choose(Pawn her, int choice)
	{
		HeraldRecord heraldRecord = LinarianPresence.Record(her);
		if (heraldRecord == null || heraldRecord.choice != 0)
		{
			return;
		}
		heraldRecord.choice = choice;
		if (choice == 1)
		{
			heraldRecord.serveSince = Find.TickManager.TicksGame;
			ThoughtDef namedSilentFail = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_ServesHer");
			if (namedSilentFail != null)
			{
				heraldRecord.beloved?.needs?.mood?.thoughts?.memories?.TryGainMemory(namedSilentFail);
			}
			Messages.Message(heraldRecord.beloved?.LabelShortCap + " serves the one who called him. She will come, in her own time.", heraldRecord.beloved, MessageTypeDefOf.NeutralEvent, historical: false);
		}
		else
		{
			GoToHer(heraldRecord);
		}
	}

	public static void GoToHer(HeraldRecord r)
	{
		Pawn beloved = r.beloved;
		Pawn her = r.her;
		if (beloved != null && her != null && beloved.Spawned && her.Spawned && !beloved.IsPrisonerOfColony)
		{
			MentalStateDef namedSilentFail = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_Lovestruck");
			if (namedSilentFail != null)
			{
				LinarianMental.Start(beloved, her, namedSilentFail, 120000);
			}
		}
	}

	public static void Union(Pawn him, Pawn her)
	{
		HeraldRecord heraldRecord = LinarianPresence.Record(her);
		if (!Lovers(him, her))
		{
			him.relations?.AddDirectRelation(PawnRelationDefOf.Lover, her);
		}
		Hediff hediff = ((Bond == null) ? null : him.health.hediffSet.GetFirstHediffOfDef(Bond));
		if (hediff != null)
		{
			him.health.RemoveHediff(hediff);
		}
		if (heraldRecord == null || !heraldRecord.joined)
		{
			Find.LetterStack.ReceiveLetter("Gone to the wild", him.LabelShortCap + " found the one who called him. They are bonded now - and the wild has them both.", LetterDefOf.NegativeEvent, him);
			him.SetFaction(null);
			if (heraldRecord != null)
			{
				heraldRecord.leaving = true;
			}
			if (him.Spawned && RCellFinder.TryFindBestExitSpot(him, out var spot))
			{
				Job job = JobMaker.MakeJob(JobDefOf.Goto, spot);
				job.exitMapOnArrival = true;
				him.jobs.StartJob(job, JobCondition.InterruptForced);
			}
		}
	}

	public static void Join(HeraldRecord r)
	{
		Pawn her = r.her;
		Pawn beloved = r.beloved;
		if (her != null && !her.Dead)
		{
			r.joined = true;
			LinarianVeil.ClearForm(her);
			LinarianVeil.Unveil(her);
			her.SetFaction(Faction.OfPlayer);
			Ideo ideo = Faction.OfPlayer.ideos?.PrimaryIdeo;
			if (ideo != null && her.ideo != null)
			{
				her.ideo.SetIdeo(ideo);
			}
			if (beloved != null && !Lovers(beloved, her))
			{
				beloved.relations?.AddDirectRelation(PawnRelationDefOf.Lover, her);
			}
			Hediff hediff = ((Bond == null || beloved == null) ? null : beloved.health.hediffSet.GetFirstHediffOfDef(Bond));
			if (hediff != null)
			{
				beloved.health.RemoveHediff(hediff);
			}
			GameComponent_Heralds.Instance?.records.Remove(r);
			Find.LetterStack.ReceiveLetter("She comes to nest", "The one who called " + beloved?.LabelShort + " has come out of the dark to stay. This is where she means to nest - near him.", LetterDefOf.PositiveEvent, her);
		}
	}

	private static void Held(HeraldRecord r, Map map)
	{
		if (r.rescue == 0)
		{
			r.rescue = ((!r.primal) ? ((!Rand.Chance(0.4f)) ? 1 : 2) : ((!Rand.Chance(0.15f)) ? 1 : 2));
		}
		r.heldNights++;
		if (r.rescue == 2)
		{
			if (!r.asked && r.heldNights >= Rand.RangeInclusive(2, 4))
			{
				r.asked = true;
				ChoiceLetter_HerBeloved choiceLetter_HerBeloved = (ChoiceLetter_HerBeloved)LetterMaker.MakeLetter(DefDatabase<LetterDef>.GetNamed("Avir_HerBelovedLetter"));
				choiceLetter_HerBeloved.her = r.her;
				choiceLetter_HerBeloved.him = r.beloved;
				choiceLetter_HerBeloved.asking = true;
				choiceLetter_HerBeloved.Label = "She asks to come in";
				choiceLetter_HerBeloved.Text = "A woman with four wings stands at the edge of the light, hands open. She asks to be let in - only to be near " + r.beloved.LabelShort + ". She will not ask twice.";
				choiceLetter_HerBeloved.lookTargets = r.her;
				Find.LetterStack.ReceiveLetter(choiceLetter_HerBeloved);
			}
			return;
		}
		foreach (Pawn item in map.mapPawns.FreeColonistsSpawned.Where((Pawn c) => c != r.beloved && (c.Position - r.beloved.Position).LengthHorizontal < 25f).InRandomOrder().Take(2)
			.ToList())
		{
			LinarianHediffs.Sleep(item, 7500, r.her);
		}
		if (Rand.Chance(0.5f))
		{
			LinarianPresence.Fire(map, "ManhunterPack");
		}
		Messages.Message("Something in the night wants " + r.beloved.LabelShort + " back.", r.beloved, MessageTypeDefOf.ThreatSmall, historical: false);
	}

	public static void Refused(Pawn her)
	{
		HeraldRecord heraldRecord = LinarianPresence.Record(her);
		if (heraldRecord != null)
		{
			heraldRecord.rescue = 1;
		}
	}
}
