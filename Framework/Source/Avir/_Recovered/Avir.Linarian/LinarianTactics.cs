using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

public static class LinarianTactics
{
	public enum Aim
	{
		Self,
		Pawn,
		Cell,
		Voice
	}

	public const string Doe = "Doe";

	public const string Crow = "Crow";

	public const string Wolf = "Wolf";

	public const string Veil = "G1";

	public const string Call = "W1";

	public const string Lullaby = "L1";

	public const string Discord = "W3";

	public const string Pack = "P1";

	public const string Captivate = "W4";

	public const string Mirage = "G3";

	public const string PackVeil = "G5";

	public const string SongHunt = "P2";

	public static Pawn aimer;

	public static float aimRadius;

	private static HediffDef smoke;

	private static HediffDef psychite;

	public static readonly string[] SocPaths = new string[8] { "W", "C", "F", "D", "G", "L", "S", "T" };

	private static TraitDef nerves;

	public static IntVec3 firstZone = IntVec3.Invalid;

	public static IEnumerable<string> All => LinarianNetwork.Nodes.SelectMany((LinarianNode n) => n.unlocks);

	public static TraitDef Nerves => nerves ?? (nerves = DefDatabase<TraitDef>.GetNamedSilentFail("Nerves"));

	public static string Label(string t)
	{
		switch (t)
		{
		case "SL":
			return "lift the pain";
		case "Doe":
			return "form of the doe";
		case "Crow":
			return "form of the crow";
		case "Wolf":
			return "form of the she-wolf";
		default:
		{
			LinarianNode linarianNode = LinarianNetwork.ForTactic(t);
			if (linarianNode == null || linarianNode.unlocks.Length != 1)
			{
				return LinarianNetwork.Get(t)?.label ?? t;
			}
			return linarianNode.label;
		}
		}
	}

	public static string Desc(string t)
	{
		if (t == "SL")
		{
			return "She lifts her phantasmal pain from someone in her zone - her own, or anyone she chooses to spare.";
		}
		LinarianNode linarianNode = LinarianNetwork.ForTactic(t);
		if (linarianNode != null)
		{
			return linarianNode.active.CapitalizeFirst();
		}
		return "";
	}

	public static bool IsForm(string t)
	{
		if (!(t == "Doe") && !(t == "Crow"))
		{
			return t == "Wolf";
		}
		return true;
	}

	public static string Canon(string t)
	{
		if (!(t == "Crow"))
		{
			return t;
		}
		return "Doe";
	}

	public static IEnumerable<string> ChantKeys(LinarianNode n)
	{
		return n.unlocks.Where((string t) => !Innate(t)).Select(Canon).Distinct();
	}

	public static IEnumerable<string> Buttons(string t)
	{
		if (t == "Doe")
		{
			return new string[2] { "Doe", "Crow" };
		}
		return new string[1] { t };
	}

	public static string ChantLabel(string t)
	{
		if (!(t == "Doe"))
		{
			return Label(t);
		}
		return "the doe and the crow";
	}

	public static Aim AimOf(string t, Gene_LinarianSong g)
	{
		if (g != null && t == "C1" && g.adoreMass)
		{
			return Aim.Cell;
		}
		if (g != null && t == "C4" && g.lastOrderMass)
		{
			return Aim.Cell;
		}
		if (g != null && t == "C5" && g.convertOne)
		{
			return Aim.Pawn;
		}
		if (g != null && t == "S1" && g.rendMass)
		{
			return Aim.Cell;
		}
		return AimOf(t);
	}

	public static Aim AimOf(string t)
	{
		switch (t)
		{
		case "D1":
		case "D3":
		case "C1":
		case "C4":
		case "F2":
		case "S1":
		case "SL":
		case "S2":
			return Aim.Pawn;
		case "D2":
		case "D4":
		case "D5":
		case "C3":
		case "F4":
		case "F5":
		case "S3":
		case "L1":
		case "L3":
		case "L4":
		case "L5":
		case "P2":
		case "W1":
		case "W2":
		case "W3":
		case "W4":
		case "W5":
		case "G3":
		case "G4":
		case "G5":
		case "T1":
		case "T3":
		case "T4":
			return Aim.Cell;
		case "F1":
			return Aim.Voice;
		default:
			return Aim.Self;
		}
	}

	public static float Radius(string t, Gene_LinarianSong g)
	{
		if (AimOf(t, g) != Aim.Cell)
		{
			return 0f;
		}
		return LinarianZone.Area(t, g);
	}

	public static bool RaidArmed(Gene_LinarianSong g)
	{
		if (g != null && g.raidArmed >= 0)
		{
			return Find.TickManager.TicksGame - g.raidArmed <= 2500;
		}
		return false;
	}

	public static float Cost(string t, Gene_LinarianSong g)
	{
		if (t == "C5" && g != null && g.convertOne) return 0.15f;   // one chosen mind costs less than the whole flock

		if (t == "D1" && g?.pawn != null && LinarianDream.RetinueActive(g.pawn))
		{
			return 0f;
		}
		if (t == "P5" && RaidArmed(g))
		{
			return 0.15f;
		}
		if ((t == "G1" || IsForm(t)) && g.Has("G5"))
		{
			return 0f;
		}
		if (IsForm(t) || t == "G1")
		{
			return 0.04f;
		}
		switch (t)
		{
		case "F2":
		case "F3":
			return 0f;
		case "SL":
			return 0.03f;
		case "S1":
		case "D1":
		case "L2":
		case "L3":
		case "P2":
		case "C3":
			return 0.06f;
		case "W2":
		case "G3":
		case "G4":
		case "S2":
		case "T1":
			return 0.1f;
		case "L1":
		case "D2":
		case "C1":
		case "S3":
		case "S4":
		case "G5":
			return 0.12f;
		case "W1":
		case "W3":
		case "L4":
		case "D3":
		case "C2":
		case "F4":
		case "P4":
		case "T3":
			return 0.15f;
		case "P1":
		case "S5":
		case "D4":
		case "C4":
			return 0.2f;
		case "W4":
		case "L5":
		case "D5":
		case "F5":
		case "T2":
			return 0.25f;
		case "W5":
		case "P5":
		case "C5":
		case "T4":
			return 0.35f;
		case "T5":
			return 0.6f;
		default:
			return 0.1f;
		}
	}

	public static int Cooldown(string t)
	{
		if (IsForm(t) || t == "G1")
		{
			return 180;
		}
		switch (t)
		{
		case "G4":
		case "T1":
		case "W2":
		case "S1":
		case "SL":
		case "D1":
			return 625;
		case "L1":
		case "W1":
		case "G3":
		case "L3":
		case "S2":
		case "C1":
		case "T3":
			return 2500;
		case "W3":
		case "G5":
		case "L2":
		case "L4":
		case "S3":
		case "S4":
		case "F4":
		case "D2":
		case "F2":
			return 5000;
		case "P1":
		case "W4":
		case "C2":
		case "C3":
		case "P4":
		case "D4":
		case "F1":
		case "F3":
		case "T2":
			return 10000;
		case "L5":
		case "S5":
		case "F5":
		case "D3":
		case "C4":
		case "D5":
			return 15000;
		case "W5":
		case "P5":
		case "C5":
			return 30000;
		case "T4":
			return 60000;
		case "T5":
			return 900000;
		default:
			return 15000;
		}
	}

	public static bool Innate(string t)
	{
		if (!(t == "Doe"))
		{
			return t == "Crow";
		}
		return true;
	}

	public static bool Unlocked(Gene_LinarianSong g, string t)
	{
		if (!Innate(t))
		{
			return LinarianNetwork.Nodes.Any((LinarianNode n) => g.Has(n.key) && n.unlocks.Contains(t));
		}
		return true;
	}

	public static string CannotUse(Pawn p, Gene_LinarianSong g, string t)
	{
		if (t == null)
		{
			return "no tactic chosen";
		}
		if (!Unlocked(g, t))
		{
			return "its star is not lit yet";
		}
		if (!p.Spawned || p.Downed || p.Dead)
		{
			return "cannot now";
		}
		if ((t == "P1" && LinarianPack.HuntActive(p)) || (t == "D5" && LinarianDream.RetinueActive(p)))
		{
			return null;
		}
		int num = g.ReadyIn(t);
		if (num > 0 && (!(t == "P5") || !RaidArmed(g)))
		{
			return "ready in " + num.ToStringTicksToPeriod();
		}
		float num2 = Cost(t, g);
		if (num2 > 0f && !Gene_ArcaneHunger.HasAtLeast(p, Mathf.Max(0f, num2 - LinarianFeast.Overflow(g))))
		{
			return "not enough arcane hunger";
		}
		if (t == "F1" && !g.voices.Any((SongVoice v) => v.Usable))
		{
			return "no voice left to lend her a skill";
		}
		if (t == "F3" && g.store <= 0.01f)
		{
			return "nothing stored";
		}
		if (t == "F4" && g.voices.Count == 0)
		{
			return "no voice in her collection";
		}
		return null;
	}

	public static bool Dulled(Pawn q)
	{
		smoke = smoke ?? DefDatabase<HediffDef>.GetNamedSilentFail("SmokeleafHigh");
		psychite = psychite ?? DefDatabase<HediffDef>.GetNamedSilentFail("PsychiteHigh");
		HediffSet hediffSet = q?.health?.hediffSet;
		if (hediffSet != null)
		{
			if (smoke == null || !hediffSet.HasHediff(smoke))
			{
				if (psychite != null)
				{
					return hediffSet.HasHediff(psychite);
				}
				return false;
			}
			return true;
		}
		return false;
	}

	public static bool Takeable(Pawn q)
	{
		if (q == null || q.Dead || !q.Spawned)
		{
			return false;
		}
		if (q.RaceProps.IsMechanoid || Linarian.Is(q) || AvirIllithidGuard.IsIllithid(q))
		{
			return false;
		}
		if (SongShelter.Shielded(q))
		{
			return false;
		}
		if (Dulled(q))
		{
			return false;
		}
		return q.GetStatValue(StatDefOf.PsychicSensitivity) > 0.01f;
	}

	public static bool Asleep(Pawn q)
	{
		if (q != null)
		{
			if (q.Awake())
			{
				return q.health.hediffSet.HasHediff(LinarianHediffs.Avir_SongSleep);
			}
			return true;
		}
		return false;
	}

	public static float Soc(Pawn her, Pawn q, string path)
	{
		if (LinarianShadow.EternalNightOn(q.MapHeld))
		{
			return 1f;
		}
		float num = Mathf.Clamp(q.GetStatValue(StatDefOf.PsychicSensitivity), 0f, 2.5f);
		float num2 = SocWeight(path);
		if (num2 <= 0f)
		{
			return LinarianWounds.Bend(her, q, num);
		}
		float num3 = ((StatDefOf.CertaintyLossFactor != null) ? Mathf.Clamp(q.GetStatValue(StatDefOf.CertaintyLossFactor), 0f, 2f) : 1f);
		float num4 = 0.5f + 0.5f * num3;
		float num5 = Mathf.Clamp(her.GetStatValue(StatDefOf.SocialImpact), 0.5f, 2f);
		return LinarianWounds.Bend(her, q, Mathf.Lerp(num, num * num4 * num5, num2));
	}

	public static float SocWeight(string path)
	{
		if (path != null && AvirMod.Settings?.singerSocWeights != null && AvirMod.Settings.singerSocWeights.TryGetValue(path, out var value))
		{
			return Mathf.Clamp01(value);
		}
		return DefaultSocWeight(path);
	}

	public static float DefaultSocWeight(string path)
	{
		if (path != null)
		{
			int length = path.Length;
			if (length == 1)
			{
				switch (path[0])
				{
				case 'C':
				case 'W':
					return 1f;
				case 'F':
					return 0.9f;
				case 'D':
					return 0.8f;
				case 'G':
					return 0.7f;
				case 'L':
					return 0.6f;
				case 'S':
					return 0.5f;
				case 'T':
					return 0f;
				}
			}
		}
		return 0.75f;
	}

	public static float Chance(Pawn her, Gene_LinarianSong g, Pawn q, bool callOrSleep = false, string path = null)
	{
		if (!Takeable(q))
		{
			return 0f;
		}
		if (LinarianPackSiphon.Held(her, q))
		{
			return 1f;
		}
		float num = 0.2f + 0.45f * g.Power * Soc(her, q, path);
		if (callOrSleep && g.Has("W3"))
		{
			num += 0.15f;
		}
		if (g.Has("D1") && Asleep(q))
		{
			num += 0.2f;
		}
		if (LinarianSource.Of(q, LinarianHediffs.Avir_Adoration) == her)
		{
			num += 0.25f;
		}
		if (q.story?.traits != null && (q.story.traits.HasTrait(TraitDefOf.Psychopath) || (Nerves != null && q.story.traits.DegreeOfTrait(Nerves) >= 2)))
		{
			num *= 0.5f;
		}
		return Mathf.Clamp01(num);
	}

	public static int Count(Gene_LinarianSong g, int bonus = 0)
	{
		return Mathf.Max(1, 1 + Mathf.FloorToInt(g.Power * 1.5f) + bonus);
	}

	public static List<Pawn> Foes(Pawn her, float radius)
	{
		return (from q in GenRadial.RadialDistinctThingsAround(her.Position, her.Map, radius, useCenter: true).OfType<Pawn>()
			where q != her && !q.Dead && q.HostileTo(her) && !q.RaceProps.Animal
			orderby (q.Position - her.Position).LengthHorizontalSquared
			select q).ToList();
	}

	public static List<Pawn> Own(Pawn her, float radius)
	{
		return (from q in GenRadial.RadialDistinctThingsAround(her.Position, her.Map, radius, useCenter: true).OfType<Pawn>()
			where q != her && !q.Dead && q.Faction != null && q.Faction == her.Faction && q.RaceProps.Humanlike
			select q).ToList();
	}

	public static void Begin(Pawn p, Gene_LinarianSong g, string t)
	{
		if (IsForm(t) && LinarianVeil.InForm(p) == t)
		{
			LinarianVeil.ClearForm(p);
		}
		else if (t == "G1" && LinarianVeil.Veiled(p))
		{
			LinarianVeil.Unveil(p);
		}
		else
		{
			if (CannotUse(p, g, t) != null)
			{
				return;
			}
			if (t == "P1" && LinarianPack.HuntActive(p))
			{
				aimer = p;
				aimRadius = 8f;
				Find.Targeter.BeginTargeting(new TargetingParameters
				{
					canTargetLocations = true,
					canTargetPawns = false,
					canTargetBuildings = false,
					validator = (TargetInfo ti) => ti.Cell.IsValid
				}, delegate(LocalTargetInfo ti)
				{
					LinarianPack.Point(p, g, ti.Cell);
				}, p);
				return;
			}
			if (t == "D5" && LinarianDream.RetinueActive(p))
			{
				aimer = p;
				aimRadius = LinarianZone.Area("D5", g);
				Find.Targeter.BeginTargeting(new TargetingParameters
				{
					canTargetLocations = true,
					canTargetPawns = false,
					canTargetBuildings = false,
					validator = (TargetInfo ti) => ti.Cell.IsValid
				}, delegate(LocalTargetInfo ti)
				{
					LinarianDream.Point(p, ti.Cell);
				}, p);
				return;
			}
			if (t == "D4" || t == "D5")
			{
				aimer = p;
				aimRadius = Radius(t, g);
				Messages.Message("First the sleepers: where do they rise?", MessageTypeDefOf.NeutralEvent, historical: false);
				Find.Targeter.BeginTargeting(new TargetingParameters
				{
					canTargetLocations = true,
					canTargetPawns = false,
					canTargetBuildings = false,
					validator = (TargetInfo ti) => ti.Cell.IsValid
				}, delegate(LocalTargetInfo first)
				{
					string text2 = Valid(p, g, t, first.Cell, null);
					if (text2 != null)
					{
						Messages.Message(text2, MessageTypeDefOf.RejectInput, historical: false);
					}
					else
					{
						firstZone = first.Cell;
						aimer = p;
						aimRadius = 5f;
						Messages.Message("Now where they walk to.", MessageTypeDefOf.NeutralEvent, historical: false);
						Find.Targeter.BeginTargeting(new TargetingParameters
						{
							canTargetLocations = true,
							canTargetPawns = false,
							canTargetBuildings = false,
							validator = (TargetInfo ti) => ti.Cell.IsValid
						}, delegate(LocalTargetInfo second)
						{
							if (second.Cell.InBounds(p.Map))
							{
								Use(p, g, t, second);
							}
						}, p);
					}
				}, p);
				return;
			}
			switch (AimOf(t, g))
			{
			case Aim.Self:
				Use(p, g, t, LocalTargetInfo.Invalid);
				return;
			case Aim.Voice:
			{
				List<FloatMenuOption> options = (from v in LinarianFeast.Chosen(g)
					select new FloatMenuOption(string.Concat((v.merged ? ("merging (" + v.name + ")") : v.name) + ": " + v.skill.LabelCap + " ", v.level.ToString(), "  - ", v.uses.ToString(), " left"), delegate
					{
						if (CannotUse(p, g, t) == null)
						{
							Pay(p, g, t);
							g.Sing(t, delegate
							{
								LinarianActs.DeadVoice(p, g, v);
							});
						}
					})).ToList();
				Find.WindowStack.Add(new FloatMenu(options));
				return;
			}
			}
			TargetingParameters targetParams = new TargetingParameters
			{
				canTargetPawns = (AimOf(t, g) == Aim.Pawn),
				canTargetLocations = (AimOf(t, g) == Aim.Cell),
				canTargetBuildings = false,
				canTargetItems = false,
				validator = (TargetInfo ti) => (AimOf(t, g) != Aim.Cell) ? (ti.Thing is Pawn) : ti.Cell.IsValid,
				canTargetAnimals = true,
				canTargetHumans = true,
				canTargetMechs = false
			};
			aimer = p;
			aimRadius = Radius(t, g);
			Find.Targeter.BeginTargeting(targetParams, delegate(LocalTargetInfo ti)
			{
				string text = Valid(p, g, t, ti.Cell, ti.Pawn);
				if (text != null)
				{
					Messages.Message(text, MessageTypeDefOf.RejectInput, historical: false);
				}
				else
				{
					Use(p, g, t, ti);
				}
			}, p);
		}
	}

	public static string Valid(Pawn p, Gene_LinarianSong g, string t, IntVec3 c, Pawn q)
	{
		Map map = p.Map;
		switch (AimOf(t, g))
		{
		case Aim.Cell:
			if (!c.IsValid || !c.InBounds(map))
			{
				return "not there";
			}
			if (!LinarianZone.InZone(p, g, c))
			{
				return "beyond her influence";
			}
			if ((t == "G4" || t == "T1") && !c.Standable(map))
			{
				return "she cannot stand there";
			}
			if (t == "T1" && map.glowGrid.GroundGlowAt(c) > 0.3f)
			{
				return "not dark enough";
			}
			return null;
		case Aim.Pawn:
			if (q == null || q.Dead || q == p)
			{
				return "not someone";
			}
			if (!LinarianZone.InZone(p, g, q.Position))
			{
				return "beyond her influence";
			}
			switch (t)
			{
			case "D1":
				if (q.RaceProps.Humanlike && q.Faction != Faction.OfPlayer && !q.IsPrisoner)
				{
					if (Takeable(q))
					{
						return null;
					}
					return "this mind is closed to her";
				}
				return "not someone to dream of her";
			case "D3":
				if (Asleep(q))
				{
					if (Takeable(q))
					{
						return null;
					}
					return "this mind is closed to her";
				}
				return "not asleep";
			case "D2":
				if (Asleep(q))
				{
					if (Takeable(q))
					{
						return null;
					}
					return "this mind is closed to her";
				}
				return "not asleep";
			case "C1":
				if (Takeable(q))
				{
					return null;
				}
				return "this mind is closed to her";
			case "C4":
				if (LinarianSource.Of(q, LinarianHediffs.Avir_Adoration) == p && !(q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_Adoration).Severity < 0.5f))
				{
					return null;
				}
				return "not fervent in her worship";
			case "F2":
				if (Asleep(q) || q.Downed || q.IsPrisoner || q.health.hediffSet.HasHediff(LinarianHediffs.Avir_SongCaptivated))
				{
					return null;
				}
				return "not asleep or held";
			case "S1":
			case "S2":
			case "SL":
				if (!q.health.hediffSet.HasHediff(LinarianHediffs.PhantomScar))
				{
					return "bears no phantasmal pain";
				}
				return null;
			default:
				return null;
			}
		default:
			return null;
		}
	}

	private static void Pay(Pawn p, Gene_LinarianSong g, string t)
	{
		float num = LinarianFeast.PayFromOverflow(g, Cost(t, g));
		if (num > 0f)
		{
			Gene_ArcaneHunger.Spend(p, num);
		}
		if (t == "P5" && RaidArmed(g))
		{
			g.raidArmed = -1;
		}
		else if (!(t == "D1") || !LinarianDream.RetinueActive(p))
		{
			g.Cooldown(t, Cooldown(t));
		}
		bool flag = t == "G1" || IsForm(t);
		int ticksGame = Find.TickManager.TicksGame;
		if (!flag || ticksGame - g.guiseXp >= 2500)
		{
			g.Learn(120f);
			if (flag)
			{
				g.guiseXp = ticksGame;
			}
		}
		if (p.Spawned)
		{
			RitualFx.Mote(p.DrawPos, p.Map, 2.2f, new Color(0.8f, 0.85f, 1f));
		}
	}

	public static void Use(Pawn p, Gene_LinarianSong g, string t, LocalTargetInfo target)
	{
		if (CannotUse(p, g, t) == null)
		{
			Pay(p, g, t);
			g.Sing(t, delegate
			{
				LinarianActs.Do(p, g, t, target);
			});
		}
	}

	public static void Notify(Pawn p, string text)
	{
		if (p.Faction == Faction.OfPlayer)
		{
			Messages.Message(p.LabelShortCap + ": " + text, p, MessageTypeDefOf.NeutralEvent, historical: false);
		}
	}

	public static string Brief(string t, Gene_LinarianSong g)
	{
		return Desc(t) + "\n\nCosts " + Cost(t, g).ToStringPercent() + " of arcane hunger; again in " + Cooldown(t).ToStringTicksToPeriod() + ".";
	}

	public static IEnumerable<Gizmo> Gizmos(Pawn p, Gene_LinarianSong g)
	{
		for (int j = 0; j < 5; j++)
		{
			string text = g.Slot(j);
			if (text == null)
			{
				continue;
			}
			foreach (string t2 in Buttons(text))
			{
				int num = g.WarmingFor(j);
				Command_SongTactic command_SongTactic = new Command_SongTactic
				{
					her = p,
					radius = Radius(t2, g),
					defaultLabel = Label(t2).CapitalizeFirst(),
					defaultDesc = Brief(t2, g) + ((num > 0) ? ("\n\nShe is still singing herself into it: half strength for " + num.ToStringTicksToPeriod() + ".") : ""),
					icon = LinarianTex.Tactic(t2),
					Order = 20f + (float)j + ((t2 == "Crow") ? 0.5f : 0f),
					action = delegate
					{
						Begin(p, g, t2);
					}
				};
				if (num > 0)
				{
					command_SongTactic.defaultIconColor = new Color(0.78f, 0.74f, 0.92f);
				}
				if (t2 == "G1" && LinarianVeil.Veiled(p))
				{
					command_SongTactic.defaultDesc = "She is veiled now. Press again to step out of the veil.";
					yield return command_SongTactic;
					continue;
				}
				if (IsForm(t2) && LinarianVeil.InForm(p) == t2)
				{
					command_SongTactic.defaultDesc = "She is in this form now. Press again to return to her own shape.";
					yield return command_SongTactic;
					continue;
				}
				string text2 = CannotUse(p, g, t2);
				if (text2 != null)
				{
					command_SongTactic.Disable(text2);
				}
				yield return command_SongTactic;
			}
		}
		string[] array = new string[2] { "Doe", "Crow" };
		foreach (string t in array)
		{
			Command_SongTactic command_SongTactic2 = new Command_SongTactic
			{
				her = p,
				defaultLabel = Label(t).CapitalizeFirst(),
				defaultDesc = ((LinarianVeil.InForm(p) == t) ? "She is in this form now. Press again to return to her own shape." : Brief(t, g)),
				icon = LinarianTex.Tactic(t),
				Order = 19f + ((t == "Crow") ? 0.5f : 0f),
				action = delegate
				{
					Begin(p, g, t);
				}
			};
			string text3 = ((LinarianVeil.InForm(p) == t) ? null : CannotUse(p, g, t));
			if (text3 != null)
			{
				command_SongTactic2.Disable(text3);
			}
			yield return command_SongTactic2;
		}
		if (LinarianVeil.InForm(p) != null)
		{
			yield return new Command_Action
			{
				defaultLabel = "Own form",
				defaultDesc = "Return to her own shape.",
				icon = LinarianTex.OwnForm,
				Order = 26f,
				action = delegate
				{
					LinarianVeil.ClearForm(p);
				}
			};
		}
	}

	public static List<FloatMenuOption> ChantMenu(Gene_LinarianSong g, int slot)
	{
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		if (g.Slot(slot) != null)
		{
			list.Add(new FloatMenuOption("(leave the place empty)", delegate
			{
				g.SetSlot(slot, null);
			}));
		}
		foreach (string area in LinarianNetwork.Areas.Concat(new string[1] { "T" }))
		{
			foreach (LinarianNode item in LinarianNetwork.Nodes.Where((LinarianNode n) => n.area == area && (g.Has(n.key) || n.unlocks.Any(Innate))))
			{
				foreach (string item2 in ChantKeys(item))
				{
					string tt = item2;
					int num = g.SlotOf(item2);
					list.Add(new FloatMenuOption(LinarianNetwork.AreaLabel(area) + ": " + ChantLabel(item2) + ((num >= 0) ? ("  (place " + (num + 1) + ")") : ""), delegate
					{
						g.SetSlot(slot, tt);
					}, LinarianTex.Tactic(item2), Color.white));
				}
			}
		}
		if (list.Count == 0)
		{
			list.Add(new FloatMenuOption("No stone of the network is open yet", null));
		}
		return list;
	}
}
