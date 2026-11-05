using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

public class Gene_LinarianSong : Gene
{
	public int songLevel;

	public float songXp;

	public HashSet<string> opened = new HashSet<string>();

	public bool shadow;

	public List<SongVoice> voices = new List<SongVoice>();

	public float store;

	public int nextVoice;

	public Dictionary<string, int> pins = new Dictionary<string, int>();

	public bool storeDump;

	private bool storeInEssence;

	public const int StartPoints = 2;

	public const int Slots = 5;

	public List<string> chant = new List<string>();

	public List<int> chantSince = new List<int>();

	public List<SongPreset> presets = new List<SongPreset>();

	public Dictionary<string, int> ready = new Dictionary<string, int>();

	public int lastDouble = -999999;

	public int lastShrug = -999999;

	public Pawn offender;

	private float castScale = 1f;

	private string currentTactic;

	public int offered;

	public bool lullOwn;

	public bool adoreMass;

	public bool lastOrderMass;

	public bool lullSwapped;

	public IntVec3 lastHuntPoint = IntVec3.Invalid;

	public int raidArmed = -1;

	public bool preyLift;

	public int guiseXp = -999999;

	public int lastRing = -999999;

	public float stolenYears;

	public long ageReserve = -1L;

	public bool ageMode;

	public bool flareMode;

	public bool rendMass;

	public static int MaxLevel => ((AvirMod.Settings != null && AvirMod.Settings.singerUncapped) ? Mathf.Clamp(AvirMod.Settings.singerPointCap, 25, 100) : LinarianNetwork.Nodes.Count) - 2;

	public int Points => 2 + songLevel;

	public int PointsFree => Points - opened.Count - offered;

	public float XpToNext
	{
		get
		{
			if (songLevel < MaxLevel)
			{
				return 900f + 300f * (float)songLevel;
			}
			return 0f;
		}
	}

	public float VoiceWorth => LinarianFeast.Worth(this);

	public float Power
	{
		get
		{
			float num = (0.5f + (float)Mathf.Min(songLevel, 20) / 10f) * Mathf.Max(0.5f, pawn.GetStatValue(StatDefOf.PsychicSensitivity));
			if (Has("F1"))
			{
				num *= 1f + (Has("F5") ? 0.5f : 0.25f) * (1f - Mathf.Exp((0f - VoiceWorth) / 12f));
				num *= 1f + 0.12f * (float)(LinarianFeast.Count(this) / 10);
			}
			if (shadow && pawn.MapHeld != null)
			{
				MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(pawn.MapHeld);
				if (mapComponent_ExternalFlow != null)
				{
					num *= 1f + 0.15f * (float)MapComponent_ExternalFlow.TierIndex(mapComponent_ExternalFlow.Total);
				}
			}
			if (Has("T5") && pawn.MapHeld != null && LinarianPresence.Night(pawn.MapHeld))
			{
				num *= 1.3f;
			}
			if (LinarianFeast.Surging(pawn))
			{
				num *= 2f;
			}
			return num * (1f + 0.06f * (float)offered) * castScale * AvirWingParts.SongFactor(pawn);
		}
	}

	public static int WarmTicks => 5000;

	public float CastScale => castScale;

	public float ZoneRadius
	{
		get
		{
			float num = 16f + 1.6f * (float)songLevel + 4f * (float)offered;
			if (!(num >= 120f))
			{
				return num;
			}
			return float.MaxValue;
		}
	}

	public float Reach => Mathf.Clamp(0.75f + 0.25f * Power / CastScale, 0.7f, 2.2f) * CastScale;

	public bool Has(string node)
	{
		return opened.Contains(node);
	}

	public bool Dark(string area)
	{
		if (shadow)
		{
			return Has(area + "4");
		}
		return false;
	}

	public void Learn(float xp)
	{
		if (!(xp <= 0f) && songLevel < MaxLevel)
		{
			songXp += xp;
			bool flag = false;
			while (songLevel < MaxLevel && songXp >= XpToNext)
			{
				songXp -= XpToNext;
				songLevel++;
				flag = true;
			}
			if (flag && pawn.Faction != Faction.OfPlayer)
			{
				AutoSpend(this);
			}
			if (flag && pawn.Faction == Faction.OfPlayer)
			{
				Messages.Message(pawn.LabelShortCap + "'s Aria deepens: a new point for the aria's network.", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}
	}

	public void WakeShadow(string why)
	{
		if (!shadow)
		{
			shadow = true;
			if (pawn.Faction == Faction.OfPlayer)
			{
				Find.LetterStack.ReceiveLetter("The shadow of the Stream", pawn.LabelShortCap + " " + why + " Something in her aria has turned to the dark: a hidden ring of the Aria lies open to her now.", LetterDefOf.NeutralEvent, pawn);
			}
		}
	}

	public void TakeVoice(Pawn victim)
	{
		if (victim != null && victim.RaceProps?.Humanlike == true && victim.Name != null)
		{
			SkillRecord skillRecord = (from s in victim.skills?.skills?.Where((SkillRecord s) => !s.TotallyDisabled)
				orderby s.Level descending
				select s).FirstOrDefault();
			float worth = 1f;
			if (Has("F4") && (victim.Faction?.leader == victim || victim.HasPsylink || Linarian.Is(victim) || AvirBody.IsAvir(victim)))
			{
				worth = 3f;
			}
			SongVoice songVoice = new SongVoice
			{
				name = victim.Name.ToStringShort,
				skill = skillRecord?.def,
				level = (skillRecord?.Level ?? 0),
				worth = worth
			};
			voices.Add(songVoice);
			LinarianFeast.Added(this, songVoice);
		}
	}

	public override void PostAdd()
	{
		base.PostAdd();
		storeInEssence = true;
		lullSwapped = true;
		PostAddTactics();
		Pawn_StoryTracker story = pawn.story;
		if (story != null && story.traits?.HasTrait(LinarianDefOf.Avir_ArcaneDependence) == true)
		{
			shadow = true;
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref songLevel, "songLevel", 0);
		Scribe_Values.Look(ref songXp, "songXp", 0f);
		Scribe_Collections.Look(ref opened, "songOpened", LookMode.Value);
		Scribe_Values.Look(ref shadow, "songShadow", defaultValue: false);
		Scribe_Collections.Look(ref voices, "songVoices", LookMode.Deep);
		Scribe_Values.Look(ref store, "songStore", 0f);
		Scribe_Values.Look(ref nextVoice, "songNextVoice", 0);
		Scribe_Collections.Look(ref pins, "songVoicePins", LookMode.Value, LookMode.Value);
		Scribe_Values.Look(ref storeDump, "songStoreDump", defaultValue: false);
		Scribe_Values.Look(ref storeInEssence, "songStoreInEssence", defaultValue: false);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (pins == null)
			{
				pins = new Dictionary<string, int>();
			}
			if (!storeInEssence)
			{
				store = Mathf.Min(1200f, store * 1000f);
				storeInEssence = true;
			}
			if (opened == null)
			{
				opened = new HashSet<string>();
			}
			opened.RemoveWhere((string k) => LinarianNetwork.Get(k) == null);
			if (voices == null)
			{
				voices = new List<SongVoice>();
			}
			foreach (SongVoice voice in voices)
			{
				if (voice.id == 0)
				{
					voice.id = ++nextVoice;
					voice.since = Find.TickManager.TicksGame;
				}
			}
		}
		ExposeTactics();
	}

	private void FitChant()
	{
		if (chant == null)
		{
			chant = new List<string>();
		}
		if (chantSince == null)
		{
			chantSince = new List<int>();
		}
		while (chant.Count < 5)
		{
			chant.Add("");
		}
		while (chantSince.Count < 5)
		{
			chantSince.Add(-999999);
		}
		if (chant.Count > 5)
		{
			chant.RemoveRange(5, chant.Count - 5);
		}
		if (chantSince.Count > 5)
		{
			chantSince.RemoveRange(5, chantSince.Count - 5);
		}
	}

	public string Slot(int i)
	{
		FitChant();
		string text = chant[i];
		if (!text.NullOrEmpty())
		{
			return text;
		}
		return null;
	}

	public int SlotOf(string t)
	{
		FitChant();
		if (t != null)
		{
			return chant.IndexOf(LinarianTactics.Canon(t));
		}
		return -1;
	}

	public void SetSlot(int i, string t, bool warm = true)
	{
		FitChant();
		if (i < 0 || i >= 5)
		{
			return;
		}
		t = ((t == null) ? null : LinarianTactics.Canon(t));
		if (!((chant[i].NullOrEmpty() ? null : chant[i]) == t))
		{
			int num = (warm ? Find.TickManager.TicksGame : (-999999));
			int num2 = ((t == null) ? (-1) : chant.IndexOf(t));
			if (num2 >= 0)
			{
				num = chantSince[num2];
				chant[num2] = "";
				chantSince[num2] = -999999;
			}
			chant[i] = t ?? "";
			chantSince[i] = ((t == null) ? (-999999) : num);
		}
	}

	public void Apply(SongPreset pr)
	{
		FitChant();
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		for (int i = 0; i < 5; i++)
		{
			if (!chant[i].NullOrEmpty())
			{
				dictionary[chant[i]] = chantSince[i];
			}
		}
		for (int j = 0; j < 5; j++)
		{
			string text = ((j < pr.tactics.Count && !pr.tactics[j].NullOrEmpty() && LinarianTactics.Unlocked(this, pr.tactics[j])) ? LinarianTactics.Canon(pr.tactics[j]) : "");
			chant[j] = text;
			chantSince[j] = ((text == "") ? (-999999) : (dictionary.TryGetValue(text, out var value) ? value : Find.TickManager.TicksGame));
		}
	}

	public SongPreset Snapshot(string name)
	{
		FitChant();
		return new SongPreset
		{
			name = name,
			tactics = new List<string>(chant)
		};
	}

	public int WarmingFor(int i)
	{
		FitChant();
		if (chant[i].NullOrEmpty())
		{
			return 0;
		}
		return Mathf.Max(0, chantSince[i] + WarmTicks - Find.TickManager.TicksGame);
	}

	public float WarmFactor(string t)
	{
		int num = SlotOf(t);
		if (num < 0 || WarmingFor(num) <= 0)
		{
			return 1f;
		}
		return 0.5f;
	}

	public void Sing(string t, Action act)
	{
		float num = LinarianRivals.Damp(pawn, this, t);
		castScale = WarmFactor(t) * num;
		LinarianRivals.singing = t;
		try
		{
			act();
		}
		finally
		{
			castScale = 1f;
			LinarianRivals.singing = null;
		}
	}

	public void Opened(LinarianNode n)
	{
		FitChant();
		foreach (string item in LinarianTactics.ChantKeys(n))
		{
			if (SlotOf(item) < 0)
			{
				int num = chant.FindIndex((string x) => x.NullOrEmpty());
				if (num < 0)
				{
					break;
				}
				SetSlot(num, item);
			}
		}
	}

	public int ReadyIn(string key)
	{
		if (!ready.TryGetValue(key, out var value))
		{
			return 0;
		}
		return Mathf.Max(0, value - Find.TickManager.TicksGame);
	}

	public void Cooldown(string key, int ticks)
	{
		ready[key] = Find.TickManager.TicksGame + ticks;
	}

	public override IEnumerable<Gizmo> GetGizmos()
	{
		IEnumerable<Gizmo> gizmos = base.GetGizmos();
		if (gizmos != null)
		{
			foreach (Gizmo item in gizmos)
			{
				yield return item;
			}
		}
		if (pawn.Faction != Faction.OfPlayer || !pawn.IsColonistPlayerControlled)
		{
			yield break;
		}
		List<Gizmo> list = new List<Gizmo>();
		try
		{
			list.AddRange(LinarianTactics.Gizmos(pawn, this));
		}
		catch (Exception ex)
		{
			Log.ErrorOnce("[Avir] the Singer's aria buttons failed: " + ex, 5351143);
		}
		foreach (Gizmo item2 in list)
		{
			yield return item2;
		}
	}

	private void PostAddTactics()
	{
		FitChant();
		if (presets.Count == 0)
		{
			presets.Add(new SongPreset
			{
				name = "Ambush",
				tactics = new List<string> { "G5", "W1", "L1", "", "" }
			});
			presets.Add(new SongPreset
			{
				name = "Harvest",
				tactics = new List<string> { "P1", "W3", "", "", "" }
			});
			presets.Add(new SongPreset
			{
				name = "Captivation",
				tactics = new List<string> { "L1", "W4", "", "", "" }
			});
		}
	}

	private void ExposeTactics()
	{
		Scribe_Collections.Look(ref chant, "songChant", LookMode.Value);
		Scribe_Collections.Look(ref chantSince, "songChantSince", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			Scribe_Values.Look(ref currentTactic, "songTactic");
		}
		Scribe_Collections.Look(ref presets, "songPresets", LookMode.Deep);
		Scribe_Collections.Look(ref ready, "songReady", LookMode.Value, LookMode.Value);
		Scribe_Values.Look(ref lastDouble, "songLastDouble", -999999);
		ExposeZone();
		Scribe_References.Look(ref offender, "songOffender");
		if (Scribe.mode != LoadSaveMode.PostLoadInit)
		{
			return;
		}
		if (presets == null)
		{
			presets = new List<SongPreset>();
		}
		if (ready == null)
		{
			ready = new Dictionary<string, int>();
		}
		HashSet<string> hashSet = new HashSet<string>(LinarianTactics.All);
		foreach (SongPreset preset in presets)
		{
			for (int i = 0; i < preset.tactics.Count; i++)
			{
				if (preset.tactics[i] == null || !hashSet.Contains(preset.tactics[i]))
				{
					preset.tactics[i] = "";
				}
			}
			if (preset.tactics.Count > 5)
			{
				preset.tactics.RemoveRange(5, preset.tactics.Count - 5);
			}
		}
		FitChant();
		for (int j = 0; j < 5; j++)
		{
			if (!chant[j].NullOrEmpty() && !hashSet.Contains(chant[j]))
			{
				chant[j] = "";
			}
		}
		for (int k = 0; k < 5; k++)
		{
			if (!chant[k].NullOrEmpty())
			{
				chant[k] = LinarianTactics.Canon(chant[k]);
			}
		}
		for (int l = 0; l < 5; l++)
		{
			if (LinarianTactics.Innate(chant[l]))
			{
				chant[l] = "";
			}
		}
		for (int m = 0; m < 5; m++)
		{
			if (!chant[m].NullOrEmpty() && chant.IndexOf(chant[m]) < m)
			{
				chant[m] = "";
			}
		}
		if (currentTactic != null && hashSet.Contains(currentTactic) && chant.All((string x) => x.NullOrEmpty()))
		{
			SetSlot(0, currentTactic, warm: false);
		}
		currentTactic = null;
	}

	public static void AutoSpend(Gene_LinarianSong g)
	{
		while (g.PointsFree > 0)
		{
			string main = LinarianNetwork.Areas.OrderByDescending((string a) => g.opened.Count((string k) => k.StartsWith(a))).FirstOrDefault();
			string text = ((main == null) ? null : (from i in Enumerable.Range(1, 5)
				select main + i).FirstOrDefault((string k) => !g.opened.Contains(k)));
			if (text != null)
			{
				g.opened.Add(text);
			}
			else
			{
				g.Offer();
			}
		}
	}

	public void Offer()
	{
		if (PointsFree > 0)
		{
			offered++;
		}
	}

	private void ExposeZone()
	{
		Scribe_Values.Look(ref offered, "songOffered", 0);
		Scribe_Values.Look(ref lullOwn, "songLullOwn", defaultValue: false);
		Scribe_Values.Look(ref adoreMass, "songAdoreMass", defaultValue: false);
		Scribe_Values.Look(ref lastOrderMass, "songLastOrderMass", defaultValue: false);
		Scribe_Values.Look(ref lullSwapped, "songLullSwapped", defaultValue: false);
		Scribe_Values.Look(ref lastHuntPoint, "songHuntPoint", IntVec3.Invalid);
		Scribe_Values.Look(ref raidArmed, "songRaidArmed", -1);
		Scribe_Values.Look(ref preyLift, "songPreyLift", defaultValue: false);
		Scribe_Values.Look(ref guiseXp, "songGuiseXp", -999999);
		Scribe_Values.Look(ref lastRing, "songLastRing", -999999);
		Scribe_Values.Look(ref stolenYears, "songStolenYears", 0f);
		Scribe_Values.Look(ref ageReserve, "songAgeReserve", -1L);
		Scribe_Values.Look(ref ageMode, "songAgeMode", defaultValue: false);
		Scribe_Values.Look(ref flareMode, "songFlareMode", defaultValue: false);
		Scribe_Values.Look(ref rendMass, "songRendMass", defaultValue: false);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && !lullSwapped)
		{
			bool flag = opened != null && opened.Remove("L1");
			bool num = opened != null && opened.Remove("L4");
			if (flag)
			{
				opened.Add("L4");
			}
			if (num)
			{
				opened.Add("L1");
			}
			lullSwapped = true;
		}
	}
}
