using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avir.Artifactorics;
using Avir.Linarian;
using Avir.Magic;
using Avir.Spire;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Ritualistics;

public class CompRitualCircle : ThingComp
{
	public bool autoPerform;

	public int autoRestUntil;

	public const int AutoRest = 2500;

	public const int BloodLives = 3;

	public const float BloodCompletion = 300f;

	public const float ShroudCompletion = 500f;

	public const float BloodLifeEnergy = 400f;

	public const float UnbindCost = 200f;

	private const int BloodInterval = 600;

	private const int BloodWaitMax = 2500;

	public const float ShroudDarkness = 0.3f;

	public const int DarknessTicks = 5000;

	private int sacrificed;

	private int bloodTimer;

	private int bloodWait;

	private float awakened;

	public RitualSchema schema;

	public RitualSchema drawn;

	public float drawQuality;

	public float washed;

	public bool armed;

	public List<Pawn> held = new List<Pawn>();

	public Dictionary<Pawn, int> pulsesTaken = new Dictionary<Pawn, int>();

	private List<Pawn> pulseKeys;

	private List<int> pulseVals;

	private int pulseCooldown;

	public Pawn performer;

	public int verbIndex;

	public float bank;

	public float spentTotal;

	public Thing transferFrom;

	public Thing transferTo;

	private int runTicks;

	private float crystalProgress;

	private RitualReading readingCache;

	private RitualSchema readingOf;

	public IntVec3 target = IntVec3.Invalid;

	private int stepTicks;

	public float drawWork;

	private const float RawSpill = 40f;

	private const int RawDuration = 2500;

	public const int AnchorTicks = 60000;

	private const float PurgeCost = 30f;

	private object fromVessel;

	private object toVessel;

	public const float CrystalEnergy = 150f;

	private const float ChargePerCrystal = 100f;

	public const float FeatherEnergy = 6f;

	public float transmuteCarry;

	public const float AttractReach = 6f;

	private RitualSchema aspectFor;

	private Color aspectColor;

	public const float EngraveWork = 5f;

	public const float EngraveQuality = 0.1f;

	public const float ClawEngraving = 2.5f;

	public const float VortexYield = 2f;

	public const float ChiselShare = 0.5f;

	public const float ConduitComposure = 0.6f;

	public bool engraved;

	public bool engraveNext;

	public bool chiselPending;

	public float chiselWork;

	public bool selfSustain;

	public Thing conduit;

	public int reforgeMode;

	private int dryTicks;

	public List<Thing> threads = new List<Thing>();

	public float vortex;

	private BarrierDome dome;

	private float domeHp;

	private StreamNet netCache;

	private int netCacheTick = -9999;

	public static bool DevNoBacklash;

	private int devDomeUntil = -1;

	public Pawn chosenPerformer;

	public Thing chosenThing;

	private readonly Dictionary<object, float> grabbed = new Dictionary<object, float>();

	public const float WdPerUnit = 4.1666665f;

	private const int ReleaseGather = 900;

	private int lastSoilTick = -99999;

	public Thing reforgeThing;

	public const float UnbondCost = 1000f;

	private float infusedXp;

	private int infusedPointsAtStart = -1;

	public const float PulseRange = 18f;

	public const int PulseEvery = 3;

	private static Material netMark;

	private static readonly Material ThreadMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.75f, 0.75f, 0.78f, 0.45f));

	private static TraitDef BloodMage => DefDatabase<TraitDef>.GetNamedSilentFail("Avir_BloodMage");

	private static TraitDef ShroudPriestess => DefDatabase<TraitDef>.GetNamedSilentFail("Avir_ShroudPriestess");

	private static HediffDef ShroudBlind => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ShroudBlind");

	private static HediffDef ShroudVeiled => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ShroudVeiled");

	private Pawn AwakenTarget => (transferTo as Pawn) ?? performer;

	public CompProperties_RitualCircle Props => (CompProperties_RitualCircle)props;

	public RitualCircleSize Size => Props.size;

	public bool Drawn
	{
		get
		{
			if (drawn != null)
			{
				return drawQuality > 0f;
			}
			return false;
		}
	}

	public bool NeedsDrawing
	{
		get
		{
			if (schema != null)
			{
				if (drawn != null && SameSchema(drawn, schema))
				{
					return engraved != engraveNext;
				}
				return true;
			}
			return false;
		}
	}

	public RitualReading Reading
	{
		get
		{
			if (readingCache == null || readingOf != drawn)
			{
				readingCache = RitualEvaluator.Read(drawn);
				readingOf = drawn;
			}
			return readingCache;
		}
	}

	public IntVec3 Center
	{
		get
		{
			if (!target.IsValid || !Drawn || Reading.reach <= 0)
			{
				return parent.Position;
			}
			return target;
		}
	}

	public bool Running
	{
		get
		{
			if (performer == null)
			{
				return conduit != null;
			}
			return true;
		}
	}

	public float Radius => (float)RitualCircles.Cells(Size) / 2f;

	public IEnumerable<IntVec3> Cells => from c in GenRadial.RadialCellsAround(parent.Position, Radius, useCenter: true)
		where c.InBounds(parent.Map)
		select c;

	public float DrawWorkNeeded
	{
		get
		{
			if (schema == null)
			{
				return 1f;
			}
			return (2500f + 1500f * (float)schema.AllRunes.Count() + 800f * (float)schema.components.Count((RitualComponent c) => c.kind != ComponentKind.None)) * (1f + 0.25f * (float)schema.size) * (engraveNext ? 5f : 1f);
		}
	}

	public Color AspectColor
	{
		get
		{
			RitualSchema ritualSchema = drawn ?? schema;
			if (ritualSchema != aspectFor)
			{
				aspectFor = ritualSchema;
				aspectColor = RitualAspects.ColorOfRitual(ritualSchema);
			}
			return aspectColor;
		}
	}

	public bool NeedsChisel
	{
		get
		{
			if (chiselPending)
			{
				return drawn != null;
			}
			return false;
		}
	}

	public float ChiselWorkNeeded
	{
		get
		{
			if (drawn != null)
			{
				if (!engraved)
				{
					return (150f + 60f * (float)drawn.AllRunes.Count()) * (1f + 0.25f * (float)drawn.size);
				}
				return (2500f + 1500f * (float)drawn.AllRunes.Count() + 800f * (float)drawn.components.Count((RitualComponent c) => c.kind != ComponentKind.None)) * (1f + 0.25f * (float)drawn.size) * 5f * 0.5f;
			}
			return 1f;
		}
	}

	public StreamNet Net
	{
		get
		{
			int ticksGame = Find.TickManager.TicksGame;
			if (ticksGame - netCacheTick >= 60)
			{
				netCacheTick = ticksGame;
				netCache = MapComponent_StreamNetwork.Of(parent.Map)?.NetOf(parent);
			}
			return netCache;
		}
	}

	public int MaxThreads
	{
		get
		{
			if (!Drawn)
			{
				return 0;
			}
			return 3 * Mathf.Max(Reading.thread, (vortex > 0f) ? 1 : 0);
		}
	}

	public IEnumerable<Thing> Threaded
	{
		get
		{
			if (!Drawn)
			{
				yield break;
			}
			if (threads.Count > 0 && (Reading.thread > 0 || vortex > 0f))
			{
				foreach (Thing thread in threads)
				{
					if (thread != null && thread.Spawned && thread.Map == parent.Map)
					{
						yield return thread;
					}
				}
			}
			StreamNet net = Net;
			if (net == null)
			{
				yield break;
			}
			foreach (Thing node in net.nodes)
			{
				if (node != parent && node.Spawned && node.TryGetComp<CompRitualCircle>() == null && !threads.Contains(node))
				{
					yield return node;
				}
			}
		}
	}

	public Thing ReforgeTarget
	{
		get
		{
			if (reforgeThing == null || !ReforgeCandidates().Contains(reforgeThing))
			{
				return ReforgeCandidates().FirstOrDefault((Thing t) => t != chosenThing);
			}
			return reforgeThing;
		}
	}

	private static Material NetMarkMat => netMark ?? (netMark = MaterialPool.MatFrom(RitualTex.Ring, ShaderDatabase.Transparent, new Color(0.8f, 0.7f, 1f, 0.55f)));

	public string CannotAuto()
	{
		if (!Drawn)
		{
			return "nothing drawn yet";
		}
		RitualReading reading = Reading;
		if (reading.trap || reading.RawFlow || reading.Dome)
		{
			return "only filling and transmutation are performed on their own";
		}
		if (reading.verbs.Count == 0)
		{
			return "no verb rune";
		}
		if (reading.verbs.Any((RuneDef v) => v.kind != 0 && v.kind != RuneKind.Transmute))
		{
			return "only filling and transmutation are performed on their own";
		}
		if (reading.reach > 0)
		{
			return "a ritual that reaches a chosen point needs someone to choose it";
		}
		return null;
	}

	public bool AutoNeeded()
	{
		RitualReading reading = Reading;
		Map map = parent.Map;
		if (reading.Has(RuneKind.Transmute) && reading.Seal != null)
		{
			List<ThingDef> inputs = reading.Inputs;
			ThingDef target = reading.Seal;
			if (FuelCells(ComponentKind.Material).SelectMany((IntVec3 c) => c.GetThingList(map)).Any((Thing t) => t.def.category == ThingCategory.Item && t.def != target && Transmutation.Allowed(t.def) && ((inputs.Count <= 0) ? Transmutation.Materials.Contains(t.def) : inputs.Contains(t.def))))
			{
				return true;
			}
		}
		if (reading.Has(RuneKind.Fill))
		{
			if (ChargeTargets(reading).Any((ArtifactState st) => st.charge < 0.75f))
			{
				return true;
			}
			foreach (Thing item in GenRadial.RadialDistinctThingsAround(Center, map, Reach(reading), useCenter: true).Concat(Threaded))
			{
				CompEnergyKeeper compEnergyKeeper = item.TryGetComp<CompEnergyKeeper>();
				if (compEnergyKeeper != null && compEnergyKeeper.stored < compEnergyKeeper.Capacity * 0.75f)
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool WantsAutoNow(Pawn p)
	{
		if (autoPerform && parent.Spawned && !Running && Find.TickManager.TicksGame >= autoRestUntil && CannotAuto() == null && CannotPerform(p) == null)
		{
			return AutoNeeded();
		}
		return false;
	}

	public void OrderBy(Pawn p)
	{
		Prepare(p);
	}

	private IEnumerable<Gizmo> AutoGizmos()
	{
		if (Drawn && !Reading.trap)
		{
			string text = CannotAuto();
			Command_Toggle command_Toggle = new Command_Toggle
			{
				defaultLabel = "Perform on its own",
				defaultDesc = "Allow your ritualists to begin this ritual themselves whenever it has work: keepers or artifacts in its reach below three quarters of their charge, or material lying in its material circles. Only filling and transmutation qualify. After each ritual the circle rests an hour.",
				icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Anchor")?.Ink ?? BaseContent.BadTex),
				isActive = () => autoPerform,
				toggleAction = delegate
				{
					autoPerform = !autoPerform;
				}
			};
			if (text != null && !autoPerform)
			{
				command_Toggle.Disable(text);
			}
			yield return command_Toggle;
		}
	}

	private void ExposeAuto()
	{
		Scribe_Values.Look(ref autoPerform, "autoPerform", defaultValue: false);
		Scribe_Values.Look(ref autoRestUntil, "autoRestUntil", 0);
	}

	public static BloodMode BloodModeOf(RitualReading r)
	{
		if (r.Has(RuneKind.Purge))
		{
			return BloodMode.Unbind;
		}
		if (r.Has(RuneKind.Growth))
		{
			return BloodMode.Healing;
		}
		if (r.verbs.Any((RuneDef v) => v.kind != RuneKind.Blood))
		{
			return BloodMode.Sacrifice;
		}
		return BloodMode.Awaken;
	}

	public static ShroudMode ShroudModeOf(RitualReading r)
	{
		if (r.Has(RuneKind.Purge))
		{
			return ShroudMode.Unveil;
		}
		if (r.barrier > 0 || r.reach > 0)
		{
			return ShroudMode.Darkness;
		}
		return ShroudMode.Veil;
	}

	private static bool Chooses(RitualReading r)
	{
		if (!r.Has(RuneKind.Blood) || (BloodModeOf(r) != 0 && BloodModeOf(r) != BloodMode.Unbind))
		{
			if (r.Has(RuneKind.Shroud))
			{
				return ShroudModeOf(r) != ShroudMode.Darkness;
			}
			return false;
		}
		return true;
	}

	private void ResetAwakening()
	{
		sacrificed = 0;
		bloodTimer = 0;
		bloodWait = 0;
		awakened = 0f;
	}

	private void ExposeAwakening()
	{
		Scribe_Values.Look(ref sacrificed, "sacrificed", 0);
		Scribe_Values.Look(ref bloodTimer, "bloodTimer", 0);
		Scribe_Values.Look(ref bloodWait, "bloodWait", 0);
		Scribe_Values.Look(ref awakened, "awakened", 0f);
	}

	private IEnumerable<Pawn> Victims(Pawn spare)
	{
		Faction own = parent.Faction ?? Faction.OfPlayer;
		foreach (Pawn item in parent.Map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (!item.Dead && item != performer && item != spare && item.RaceProps.Humanlike && Inside(item.Position) && (item.IsPrisonerOfColony || held.Contains(item) || (item.Downed && item.Faction != own && !item.IsSlaveOfColony)))
			{
				yield return item;
			}
		}
	}

	private bool TakeLife(Pawn spare, out Pawn taken)
	{
		taken = Victims(spare).FirstOrDefault();
		if (taken == null)
		{
			return false;
		}
		IntVec3 position = taken.Position;
		ExecutionUtility.DoExecutionByCut(performer ?? spare, taken, 12);
		FleckMaker.ThrowLightningGlow(position.ToVector3Shifted(), parent.Map, 1.2f);
		StriderBowFx.Spawn("BloodBurst", position.ToVector3Shifted(), parent.Map, new Color(0.6f, 0.03f, 0.05f), 1.8f);
		return true;
	}

	public static string CannotAwaken(Pawn who, RuneKind kind, bool unbind = false)
	{
		if (who?.story?.traits == null)
		{
			return "cannot";
		}
		TraitDef bloodMage = BloodMage;
		TraitDef shroudPriestess = ShroudPriestess;
		TraitDef traitDef = ((kind == RuneKind.Blood) ? bloodMage : shroudPriestess);
		if (traitDef == null)
		{
			return "cannot";
		}
		if (unbind)
		{
			if (!who.story.traits.HasTrait(traitDef))
			{
				if (kind != RuneKind.Blood)
				{
					return "not a priestess of the shroud";
				}
				return "no blood magic in them";
			}
			return null;
		}
		if (Gene_ArcaneHunger.Of(who) == null)
		{
			return "no arcane hunger in them to feed the gift";
		}
		if (kind == RuneKind.Blood)
		{
			if (who.story.traits.HasTrait(bloodMage))
			{
				return "blood magic already wakes in them";
			}
			if (shroudPriestess != null && who.story.traits.HasTrait(shroudPriestess))
			{
				return "the shroud already holds her";
			}
		}
		else
		{
			if (who.gender != Gender.Female)
			{
				return "the shroud takes only women";
			}
			if (who.story.traits.HasTrait(shroudPriestess))
			{
				return "already a priestess of the shroud";
			}
			if (bloodMage != null && who.story.traits.HasTrait(bloodMage))
			{
				return "blood magic already wakes in her";
			}
		}
		return null;
	}

	private void AwakenMenu(Pawn p, IntVec3 at)
	{
		RitualReading reading = Reading;
		if (!Chooses(reading))
		{
			Order(p, at, null, null, 0);
			return;
		}
		RuneKind runeKind = (reading.Has(RuneKind.Blood) ? RuneKind.Blood : RuneKind.Shroud);
		bool flag = ((runeKind == RuneKind.Blood) ? (BloodModeOf(reading) == BloodMode.Unbind) : (ShroudModeOf(reading) == ShroudMode.Unveil));
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		List<Pawn> list2 = new List<Pawn>();
		list2.Add(p);
		list2.AddRange(parent.Map.mapPawns.FreeColonistsSpawned.Where((Pawn q) => q != p && Inside(q.Position)));
		foreach (Pawn item in list2)
		{
			Pawn qq = item;
			string text = CannotAwaken(item, runeKind, flag);
			string text2 = (flag ? ((runeKind == RuneKind.Blood) ? "Take blood magic back from " : "Lift the shroud from ") : ((runeKind == RuneKind.Blood) ? "Awaken blood magic in " : "Draw the shroud over ")) + item.LabelShort + ((item == p) ? " (the performer)" : "");
			list.Add(new FloatMenuOption((text == null) ? text2 : (text2 + " - " + text), (text != null) ? null : ((Action)delegate
			{
				Order(p, at, null, qq, 0);
			})));
		}
		list.Add(new FloatMenuOption("(Whoever else is chosen must stand in the circle)", null));
		Find.WindowStack.Add(new FloatMenu(list));
	}

	private void BloodFeed(RitualReading r, int delta)
	{
		if (r.Has(RuneKind.Blood) && BloodModeOf(r) == BloodMode.Sacrifice && !(bank >= 200f) && runTicks % 600 < delta && TakeLife(null, out var taken))
		{
			bank += 400f;
			MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(parent.Map);
			if (mapComponent_ExternalFlow != null)
			{
				mapComponent_ExternalFlow.deeds = Mathf.Max(mapComponent_ExternalFlow.deeds, 12000f);
			}
			Messages.Message(taken.LabelShortCap + " is given to the ritual.", parent, MessageTypeDefOf.NeutralEvent, historical: false);
		}
	}

	private bool DoBlood(RitualReading r, int delta)
	{
		switch (BloodModeOf(r))
		{
		case BloodMode.Sacrifice:
			return true;
		case BloodMode.Unbind:
			return DoUnbind(RuneKind.Blood, delta);
		case BloodMode.Healing:
			return DoBloodHealing(delta);
		default:
		{
			Pawn awakenTarget = AwakenTarget;
			if (awakenTarget == null || awakenTarget.Dead || CannotAwaken(awakenTarget, RuneKind.Blood) != null)
			{
				End("the one to be awakened is gone.");
				return true;
			}
			if (sacrificed < 3)
			{
				bloodTimer += delta;
				if (bloodTimer < 600)
				{
					return false;
				}
				if (!Victims(awakenTarget).Any())
				{
					bloodWait += delta;
					if (bloodWait >= 2500)
					{
						End((sacrificed > 0) ? "no more lives in the circle - the blood spilled is lost." : "no lives in the circle: bring prisoners into it.");
						ResetAwakening();
					}
					return false;
				}
				if (bank < 25f)
				{
					return false;
				}
				bank -= 25f;
				spentTotal += 25f;
				bloodTimer = 0;
				bloodWait = 0;
				TakeLife(awakenTarget, out var taken);
				sacrificed++;
				Messages.Message(taken.LabelShortCap + " is given to the blood (" + sacrificed + "/" + 3 + ").", parent, MessageTypeDefOf.NeutralEvent, historical: false);
				return false;
			}
			if (!Pours(awakenTarget, delta, 300f, new Color(0.8f, 0.05f, 0.1f)))
			{
				return false;
			}
			Grant(awakenTarget, BloodMage, "Blood magic wakes in " + awakenTarget.LabelShort + ".");
			return true;
		}
		}
	}

	private IEnumerable<Pawn> Wounded()
	{
		return parent.Map.mapPawns.AllPawnsSpawned.Where((Pawn q) => !q.Dead && Inside(q.Position) && (q.IsColonist || (q.Faction == Faction.OfPlayer && q.RaceProps.Animal)) && q.health.hediffSet.hediffs.Any((Hediff h) => h is Hediff_Injury)).ToList();
	}

	private bool DoBloodHealing(int delta)
	{
		List<Pawn> list = Wounded().ToList();
		if (list.Count == 0)
		{
			return true;
		}
		bloodTimer += delta;
		if (bloodTimer < 600)
		{
			return false;
		}
		if (!TakeLife(null, out var taken))
		{
			return true;
		}
		bloodTimer = 0;
		foreach (Pawn item in list)
		{
			float num = 40f;
			foreach (Hediff_Injury item2 in (from h in item.health.hediffSet.hediffs.OfType<Hediff_Injury>()
				orderby h.IsPermanent() descending
				select h).ToList())
			{
				if (num <= 0f)
				{
					break;
				}
				float num2 = Mathf.Min(num, item2.Severity);
				num -= num2;
				if (num2 >= item2.Severity)
				{
					item.health.RemoveHediff(item2);
				}
				else
				{
					item2.Heal(num2);
				}
			}
			RitualFx.Mote(item.DrawPos, parent.Map, 0.9f, new Color(0.8f, 0.1f, 0.15f));
		}
		Messages.Message(taken.LabelShortCap + "'s blood closes the wounds of the living.", parent, MessageTypeDefOf.NeutralEvent, historical: false);
		return false;
	}

	private bool DoUnbind(RuneKind kind, int delta)
	{
		Pawn awakenTarget = AwakenTarget;
		if (awakenTarget == null || awakenTarget.Dead || CannotAwaken(awakenTarget, kind, unbind: true) != null)
		{
			End("there is nothing to take back.");
			return true;
		}
		if (!Pours(awakenTarget, delta, 200f, (kind == RuneKind.Blood) ? new Color(0.8f, 0.7f, 0.7f) : new Color(0.7f, 0.65f, 0.8f)))
		{
			return false;
		}
		TraitDef tDef = ((kind == RuneKind.Blood) ? BloodMage : ShroudPriestess);
		Trait trait = awakenTarget.story.traits.GetTrait(tDef);
		if (trait != null)
		{
			awakenTarget.story.traits.RemoveTrait(trait);
		}
		ResetAwakening();
		FleckMaker.ThrowLightningGlow(awakenTarget.DrawPos, parent.Map, 1.5f);
		Messages.Message((kind == RuneKind.Blood) ? ("The blood in " + awakenTarget.LabelShort + " falls silent.") : ("The shroud lets " + awakenTarget.LabelShort + " go."), awakenTarget, MessageTypeDefOf.NeutralEvent, historical: false);
		return true;
	}

	private bool DoShroud(RitualReading r, int delta)
	{
		switch (ShroudModeOf(r))
		{
		case ShroudMode.Unveil:
			return DoUnbind(RuneKind.Shroud, delta);
		case ShroudMode.Darkness:
			return DoDarkness(r, delta);
		default:
		{
			Pawn awakenTarget = AwakenTarget;
			if (awakenTarget == null || awakenTarget.Dead || CannotAwaken(awakenTarget, RuneKind.Shroud) != null)
			{
				End("the one to be veiled is gone.");
				return true;
			}
			if (parent.Map.glowGrid.GroundGlowAt(parent.Position) >= 0.3f)
			{
				if (parent.IsHashIntervalTick(600, delta) && performer?.Faction == Faction.OfPlayer)
				{
					Messages.Message("The shroud does not come in the light: darken the circle.", parent, MessageTypeDefOf.RejectInput, historical: false);
				}
				return false;
			}
			if (!Pours(awakenTarget, delta, 500f, new Color(0.25f, 0.15f, 0.4f)))
			{
				return false;
			}
			Grant(awakenTarget, ShroudPriestess, "The shroud has taken " + awakenTarget.LabelShort + ": she is its priestess now.");
			return true;
		}
		}
	}

	private bool DoDarkness(RitualReading r, int delta)
	{
		if (r.anchor == 0 && runTicks >= 5000)
		{
			return true;
		}
		float num = 0.05f * (float)delta;
		if (bank < num)
		{
			return false;
		}
		bank -= num;
		spentTotal += num;
		if (!parent.IsHashIntervalTick(60, delta))
		{
			return false;
		}
		Map map = parent.Map;
		IntVec3 center = Center;
		float num2 = Reach(r) + 2f * (float)r.barrier;
		Faction faction = parent.Faction ?? Faction.OfPlayer;
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (!item.Dead && !((item.Position - center).LengthHorizontal > num2))
			{
				HediffDef hediffDef = (item.HostileTo(faction) ? ShroudBlind : ((item.Faction == faction) ? ShroudVeiled : null));
				if (hediffDef != null)
				{
					(item.health.hediffSet.GetFirstHediffOfDef(hediffDef) ?? item.health.AddHediff(hediffDef)).Severity = 1f;
				}
			}
		}
		for (int i = 0; i < 4; i++)
		{
			StriderBowFx.Spawn("ShroudWisp", center.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * num2, map, new Color(0.05f, 0.03f, 0.09f, 0.8f), Rand.Range(1.6f, 2.8f));
		}
		return false;
	}

	private bool Pours(Pawn who, int delta, float need, Color color)
	{
		if (who != performer && (!who.Spawned || who.Map != parent.Map || !Inside(who.Position)))
		{
			return false;
		}
		float num = Mathf.Min(bank, 0.6f * (float)delta);
		if (num <= 0f)
		{
			return false;
		}
		bank -= num;
		spentTotal += num;
		awakened += num;
		if (parent.IsHashIntervalTick(45, delta))
		{
			RitualFx.Mote(who.DrawPos + Rand.InsideUnitCircleVec3 * 0.6f, parent.Map, Rand.Range(0.4f, 0.8f), color);
		}
		return awakened >= need;
	}

	private void Grant(Pawn who, TraitDef def, string text)
	{
		if (def != null && !who.story.traits.HasTrait(def))
		{
			who.story.traits.GainTrait(new Trait(def, 0, forced: true));
		}
		ResetAwakening();
		FleckMaker.ThrowLightningGlow(who.DrawPos, parent.Map, 2f);
		Messages.Message(text, who, MessageTypeDefOf.PositiveEvent, historical: false);
	}

	private static bool SameSchema(RitualSchema a, RitualSchema b)
	{
		if (a.size == b.size && a.rings.Count == b.rings.Count)
		{
			return a.rings.Select((RitualRing r, int i) => r.runes.SequenceEqual(b.rings[i].runes)).All((bool x) => x);
		}
		return false;
	}

	public bool Inside(IntVec3 c)
	{
		return (float)(c - parent.Position).LengthHorizontalSquared <= Radius * Radius;
	}

	public void Assign(RitualSchema s, bool engrave = false)
	{
		drawWork = 0f;
		schema = s?.Copy();
		engraveNext = engrave;
		armed = false;
		Messages.Message((schema == null) ? "The circle is cleared." : ("\"" + schema.name + "\" will be " + (engrave ? "engraved into the floor" : "drawn into the circle") + " by a ritualist."), parent, MessageTypeDefOf.SilentInput, historical: false);
	}

	public void DevDrawNow()
	{
		if (schema != null)
		{
			drawn = schema.Copy();
			drawQuality = 1f;
			washed = 0f;
			vortex = 0f;
			engraved = engraveNext;
			drawWork = 0f;
			RitualReading reading = Reading;
			if (reading.pattern != null && reading.pattern.hidden)
			{
				RitualDiscoveries.Learn(reading.pattern);
			}
		}
	}

	public void FinishDrawing(Pawn by)
	{
		drawWork = 0f;
		if (schema == null)
		{
			return;
		}
		drawn = schema.Copy();
		washed = 0f;
		vortex = 0f;
		engraved = engraveNext;
		int valueOrDefault = (by.skills?.GetSkill(SkillDefOf.Intellectual)?.Level).GetValueOrDefault();
		int valueOrDefault2 = (by.skills?.GetSkill(SkillDefOf.Artistic)?.Level).GetValueOrDefault();
		drawQuality = Mathf.Clamp(0.25f + 0.035f * (float)valueOrDefault + 0.03f * (float)valueOrDefault2 + Rand.Range(-0.08f, 0.08f), 0.2f, 1.6f);
		if (engraved)
		{
			drawQuality = Mathf.Min(1.7f, drawQuality + 0.1f);
		}
		RitualReading reading = Reading;
		if (reading.pattern != null && reading.pattern.hidden)
		{
			RitualDiscoveries.Learn(reading.pattern);
		}
		Messages.Message(by.LabelShort + " drew \"" + drawn.name + "\" (quality " + drawQuality.ToStringPercent() + ").", parent, MessageTypeDefOf.PositiveEvent, historical: false);
		GameComponent_AvirComposure.Drew(by);
		if (by.Faction == Faction.OfPlayer)
		{
			if (drawQuality < 0.5f)
			{
				Arlia.Say("drawnpoor");
			}
			else if (drawQuality >= 0.9f)
			{
				Arlia.Say("drawnfine");
			}
		}
	}

	public string CannotPerform(Pawn p)
	{
		if (!Drawn)
		{
			return "nothing drawn yet";
		}
		RitualReading reading = Reading;
		if (reading.trap)
		{
			return "a Trap is not performed - arm it";
		}
		if (reading.verbs.Count == 0 && !reading.RawFlow && !reading.Dome)
		{
			return "no verb rune: nothing would happen";
		}
		if (reading.Has(RuneKind.Reforge) && Size != RitualCircleSize.Large)
		{
			return "reforging needs a large circle";
		}
		if (reading.Has(RuneKind.Infuse) && !IsPath(reading) && Size == RitualCircleSize.Small)
		{
			return "infusion needs a medium or large circle";
		}
		if ((reading.Has(RuneKind.Blood) || reading.Has(RuneKind.Shroud)) && Size == RitualCircleSize.Small)
		{
			return "an awakening needs a medium or large circle";
		}
		if (drawn?.preset?.defName == "Avir_Ritual_MagePath" && reading.elements.Count == 0)
		{
			return "write the caster's element into the empty place";
		}
		if (IsPath(reading))
		{
			string text = PathCircle(reading, Size);
			if (text != null)
			{
				return text;
			}
		}
		if (vortex > 0f)
		{
			return "an endless vortex spins in it - erase the chalk first";
		}
		if (reading.verbs.Count > 0 && reading.verbs.All((RuneDef v) => !v.implemented))
		{
			return "its effect is not made yet";
		}
		if (reading.Has(RuneKind.Transmute) && reading.Seal == null && !reading.ToCurrent)
		{
			return "no seal: put a seal of what to make into a component circle";
		}
		if (Running && performer != p)
		{
			return performer.LabelShort + " is performing it";
		}
		if (p != null && (p.Downed || p.Dead))
		{
			return "cannot";
		}
		return MissingMaterial();
	}

	public void Begin(Pawn p, IntVec3 at, Thing from = null, Thing to = null, int mode = 0)
	{
		performer = p;
		target = at;
		reforgeMode = mode;
		stepTicks = 0;
		dryTicks = 0;
		verbIndex = 0;
		bank = 0f;
		spentTotal = 0f;
		transmuteCarry = 0f;
		runTicks = 0;
		transferFrom = from;
		transferTo = to;
		grabbed.Clear();
		ResetAwakening();
	}

	public void End(string why)
	{
		infusedXp = 0f;
		infusedPointsAtStart = -1;
		if (Running)
		{
			autoRestUntil = Find.TickManager.TicksGame + 2500;
		}
		if (performer != null && bank > 0.01f)
		{
			RitualReading ritualReading = ((drawn != null) ? Reading : null);
			if (ritualReading != null && ritualReading.closing > 0 && !ritualReading.RawFlow)
			{
				Hediff_StreamSaturation.Change(performer, bank);
			}
			else
			{
				MapComponent_ExternalFlow mapComponent_ExternalFlow = ((parent.Map != null) ? MapComponent_ExternalFlow.Of(parent.Map) : null);
				if (mapComponent_ExternalFlow != null)
				{
					mapComponent_ExternalFlow.deeds += bank * 40f;
				}
			}
			bank = 0f;
		}
		if (spentTotal > 0f && drawn != null && parent.Map != null && AvirMagic.On)
		{
			foreach (Pawn item in Sources().ToList())
			{
				MagicExposure.Exposed(item, spentTotal * ((item == performer) ? 1f : 0.5f));
			}
		}
		if (performer != null && spentTotal > 0f && drawn != null)
		{
			MapComponent_ExternalFlow mapComponent_ExternalFlow2 = ((parent.Map != null) ? MapComponent_ExternalFlow.Of(parent.Map) : null);
			float risk = Reading.Risk((mapComponent_ExternalFlow2 == null) ? 0f : (mapComponent_ExternalFlow2.Total / 375000f), drawQuality, 0f) * LinarianRitual.RiskFactor(performer);
			GameComponent_AvirComposure.Performed(performer, spentTotal, risk, GameComponent_AvirComposure.For(performer, drawn, this), GameComponent_AvirComposure.KeyOf(drawn));
		}
		if (why != null && (conduit != null || performer?.Faction == Faction.OfPlayer))
		{
			Messages.Message("Ritual \"" + drawn?.name + "\": " + why, parent, MessageTypeDefOf.NeutralEvent, historical: false);
			Arlia.Ended(why);
		}
		performer = null;
		conduit = null;
		transferFrom = (transferTo = null);
		DropDome();
	}

	public bool RitualTick(Pawn p, int delta)
	{
		if (performer != p || !Drawn)
		{
			return false;
		}
		return Step(delta);
	}

	private bool Step(int delta)
	{
		Pawn p = performer;
		RitualReading reading = Reading;
		runTicks += delta;
		Map map = parent.Map;
		MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(map);
		float num = 0f;
		foreach (Pawn item in Sources())
		{
			float num2 = ((!AvirWingParts.HasWingGene(item)) ? 1f : (AvirWingParts.Clipped(item) ? 0.5f : 2f));
			float num3 = item.GetStatValue(StatDefOf.PsychicSensitivity) * 0.05f * (float)delta * num2 * LinarianRitual.GiveFactor(item, p);
			num3 = Hediff_StreamSaturation.Change(item, 0f - num3);
			if (!(num3 <= 0f))
			{
				num += num3;
				if (item.needs?.rest != null)
				{
					item.needs.rest.CurLevel = Mathf.Max(0f, item.needs.rest.CurLevel - Mathf.Min(num3 * 0.0004f, 0.0001f * (float)delta));
				}
			}
		}
		if (reading.focus > 0 && mapComponent_ExternalFlow != null)
		{
			num += (float)reading.focus * (mapComponent_ExternalFlow.Total / 375000f) * 0.03f * (float)delta;
		}
		if (conduit != null)
		{
			num += TakeFrom(conduit, 0.05f * (float)delta);
		}
		if (bank < 150f && runTicks % 120 < delta)
		{
			BreakCrystal();
		}
		BloodFeed(reading, delta);
		num *= reading.FlowCap;
		float num4 = num * reading.Power(drawQuality) * (1f + ArtifactBoost(delta));
		bank += num4;
		if (mapComponent_ExternalFlow != null && reading.actWeight > 0f)
		{
			mapComponent_ExternalFlow.deeds = Mathf.Max(mapComponent_ExternalFlow.deeds, reading.actWeight);
		}
		if (reading.RawFlow && mapComponent_ExternalFlow != null)
		{
			mapComponent_ExternalFlow.deeds += num4 * 40f;
		}
		if (runTicks % 600 < delta && Backlash(p, reading, mapComponent_ExternalFlow))
		{
			return false;
		}
		if (parent.IsHashIntervalTick(90, delta))
		{
			FleckMaker.ThrowLightningGlow(parent.DrawPos, map, 0.6f + 0.3f * (float)Size);
		}
		if (parent.IsHashIntervalTick(30, delta))
		{
			RitualFx.Mote(parent.DrawPos + Rand.InsideUnitCircleVec3 * Radius * 0.9f, map, Rand.Range(0.35f, 0.7f), AspectColor);
		}
		if (reading.RawFlow)
		{
			return RawFlowTick(p, reading, mapComponent_ExternalFlow, delta);
		}
		if (reading.Dome)
		{
			return DomeTick(reading, mapComponent_ExternalFlow, delta);
		}
		if (reading.anchor > 0 && !engraved && runTicks >= 60000)
		{
			End("the anchor's day is over - chalk holds an anchored ritual a day at most.");
			return false;
		}
		stepTicks += delta;
		while (verbIndex < reading.verbs.Count)
		{
			bool flag;
			switch (reading.verbs[verbIndex].kind)
			{
			case RuneKind.Fill:
				flag = DoFill(reading);
				if (vortex > 0f)
				{
					return false;
				}
				break;
			case RuneKind.Purge:
				flag = reading.Has(RuneKind.Blood) || reading.Has(RuneKind.Shroud) || DoPurge(reading);
				break;
			case RuneKind.Transfer:
				flag = DoTransfer(reading);
				break;
			case RuneKind.Hold:
				flag = DoHold(reading, delta);
				break;
			case RuneKind.Drain:
				flag = DoDrain(reading, delta);
				break;
			case RuneKind.Transmute:
				flag = ((reading.Seal == null) ? DoCurrent(reading, delta) : DoTransmute(reading));
				if (!Running)
				{
					return false;
				}
				break;
			case RuneKind.Release:
				flag = DoRelease(reading);
				if (!Running)
				{
					return false;
				}
				break;
			case RuneKind.Lift:
				flag = reading.Throw || DoLift(reading, delta);
				break;
			case RuneKind.Growth:
				flag = DoGrowth(reading, delta);
				break;
			case RuneKind.Reforge:
				flag = DoReforge(reading);
				if (!Running)
				{
					return false;
				}
				break;
			case RuneKind.Infuse:
				flag = (IsPath(reading) ? DoPath(reading, delta) : DoInfuse(reading, delta));
				if (!Running)
				{
					return false;
				}
				break;
			case RuneKind.Blood:
				flag = DoBlood(reading, delta);
				if (!Running)
				{
					return false;
				}
				break;
			case RuneKind.Shroud:
				flag = DoShroud(reading, delta);
				if (!Running)
				{
					return false;
				}
				break;
			default:
				flag = true;
				break;
			}
			if (!flag)
			{
				return true;
			}
			verbIndex++;
			stepTicks = 0;
		}
		if (conduit != null)
		{
			End(null);
			return false;
		}
		if (reading.anchor > 0)
		{
			verbIndex = 0;
			return true;
		}
		End("complete.");
		return false;
	}

	private IEnumerable<Pawn> Sources()
	{
		if (performer != null)
		{
			yield return performer;
		}
		foreach (Pawn item in parent.Map.mapPawns.FreeColonistsSpawned)
		{
			if (item != performer && !item.Downed && Inside(item.Position) && !held.Contains(item))
			{
				yield return item;
			}
		}
	}

	private bool Backlash(Pawn p, RitualReading r, MapComponent_ExternalFlow ext)
	{
		float num = r.Risk((ext == null) ? 0f : (ext.Total / 375000f), drawQuality, bank) * ((p != null) ? LinarianRitual.RiskFactor(p) : 1f);
		float num2 = ((p != null) ? GameComponent_AvirComposure.For(p, drawn, this) : 0.6f);
		if (DevNoBacklash || (p != null && LinarianRitual.NoBacklash(p)) || num <= num2 || !Rand.Chance((num - num2) * 0.4f))
		{
			return false;
		}
		if (p == null || p.Faction == Faction.OfPlayer)
		{
			Arlia.Say("backlash");
		}
		Map map = parent.Map;
		if (p == null)
		{
			Thing thing = conduit;
			FleckMaker.ThrowLightningGlow(thing.DrawPos, map, 2f);
			if (thing.def.useHitPoints)
			{
				thing.TakeDamage(new DamageInfo(DamageDefOf.Burn, 20f + 40f * (num - num2)));
			}
			End("the stream broke through its conduit.");
			return true;
		}
		if (r.safety > 0)
		{
			IntVec3 position = CellFinder.RandomClosewalkCellNear(parent.Position, map, (int)Radius + 3, (IntVec3 c) => !Inside(c));
			p.Position = position;
			p.Notify_Teleported(endCurrentJob: true, resetTweenedPos: false);
			p.stances?.stunner?.StunFor(180, null, addBattleLog: false, showMote: false);
			End("the stream broke - the safety rune threw " + p.LabelShort + " clear.");
			return true;
		}
		Hediff hediff = HediffMaker.MakeHediff(RitualDefOf.Avir_RitualBacklash, p);
		hediff.Severity = Mathf.Clamp(num - num2, 0.1f, 1f);
		p.health.AddHediff(hediff);
		GameComponent_AvirComposure.Lashed(p, hediff.Severity);
		if (num - num2 > 0.8f)
		{
			float num3 = (r.RawFlow ? Mathf.Min(bank / 150f, 6f) : 0f);
			IntVec3 position2 = parent.Position;
			float radius = 1.9f + (float)Size + num3;
			DamageDef bomb = DamageDefOf.Bomb;
			int damAmount = 20 + 15 * (int)Size + Mathf.RoundToInt(num3 * 5f);
			List<Thing> ignoredThings = ((r.barrier > 0) ? Spared(map, p) : null);
			GenExplosion.DoExplosion(position2, map, radius, bomb, null, damAmount, -1f, null, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors: false, null, 0f, 1, 0f, damageFalloff: false, null, ignoredThings);
			if (r.Storm)
			{
				StrikeAround(map, r, 2 + r.focus / 3);
			}
			End("the stream collapsed in a blast.");
		}
		else
		{
			End("the stream collapsed - " + p.LabelShort + " is burnt by it.");
		}
		return true;
	}

	private bool RawFlowTick(Pawn p, RitualReading r, MapComponent_ExternalFlow ext, int delta)
	{
		if (p == null)
		{
			End("raw stream needs a living conduit.");
			return false;
		}
		Map map = parent.Map;
		if (r.Storm && runTicks % 250 < delta)
		{
			if (runTicks >= 600 && !(map.weatherManager.curWeather?.defName ?? "").Contains("Thunderstorm"))
			{
				WeatherDef namedSilentFail = DefDatabase<WeatherDef>.GetNamedSilentFail("RainyThunderstorm");
				if (namedSilentFail != null)
				{
					map.weatherManager.TransitionTo(namedSilentFail);
					Messages.Message("The ritual circle calls a storm.", parent, MessageTypeDefOf.NeutralEvent, historical: false);
				}
			}
			if (Rand.Chance(Mathf.Min(0.9f, 0.12f * (float)r.focus / 6f + bank / 2000f)))
			{
				StrikeAround(map, r, 1);
				bank = Mathf.Max(0f, bank - 40f);
			}
		}
		int num = ((r.anchor > 0) ? 60000 : 2500);
		if (runTicks >= num)
		{
			if (r.Eye && bank > 50f)
			{
				float amount = Hediff_StormMantle.FillWorn(p, bank * 4f);
				Hediff_StormMantle.Grant(p, amount);
				StrikeAround(map, r, 3);
				bank = 0f;
				End("the storm is sealed into " + p.LabelShort + ".");
				return false;
			}
			if (r.Storm && bank > 40f)
			{
				StrikeAround(map, r, Mathf.Min(8, Mathf.RoundToInt(bank / 80f)));
			}
			bank = 0f;
			End("the held stream dispersed.");
			return false;
		}
		return true;
	}

	private void StrikeAround(Map map, RitualReading r, int count)
	{
		float near = Radius + 3f;
		float f = Radius + 6f + (float)r.focus * 1.5f;
		for (int i = 0; i < count; i++)
		{
			if (!CellFinder.TryFindRandomCellNear(parent.Position, map, Mathf.CeilToInt(f), (IntVec3 c) => !c.Roofed(map) && (c - parent.Position).LengthHorizontal >= near, out var result))
			{
				break;
			}
			map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, result));
		}
	}

	private float Reach(RitualReading r)
	{
		return Radius + (float)r.breadth * 4f;
	}

	private IEnumerable<ArtifactState> ChargeTargets(RitualReading r)
	{
		Map map = parent.Map;
		float radius = Reach(r);
		foreach (Thing t in GenRadial.RadialDistinctThingsAround(Center, map, radius, useCenter: true).Concat(Threaded))
		{
			ArtifactState artifactState = t.TryGetComp<CompArtifact>()?.state;
			if (artifactState != null)
			{
				yield return artifactState;
			}
			CompArtifactHost compArtifactHost = t.TryGetComp<CompArtifactHost>();
			if (compArtifactHost != null)
			{
				foreach (ArtifactState slot in compArtifactHost.slots)
				{
					if (slot != null)
					{
						yield return slot;
					}
				}
			}
			if (!(t is Pawn pawn) || pawn.Faction != Faction.OfPlayer)
			{
				continue;
			}
			foreach (CompArtifactSocket item in ArtifactWearCache.Worn(pawn))
			{
				if (item.inlaid != null)
				{
					yield return item.inlaid;
				}
			}
		}
	}

	private bool DoFill(RitualReading r)
	{
		if (AvirMagic.On)
		{
			CompSanctuary compSanctuary = parent.TryGetComp<CompSanctuary>();
			if (compSanctuary != null && compSanctuary.WantsPour)
			{
				float num = compSanctuary.Pour(bank);
				bank -= num;
				spentTotal += num;
				if (bank <= 0.0001f)
				{
					return false;
				}
			}
		}
		foreach (Thing item in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), useCenter: true).Concat(Threaded))
		{
			CompSpireCore compSpireCore = item.TryGetComp<CompSpireCore>();
			if (compSpireCore != null && (!compSpireCore.kindled || !(compSpireCore.Missing <= 0.01f)))
			{
				float num2 = compSpireCore.Pour(bank, r, drawQuality, performer);
				bank -= num2;
				spentTotal += num2;
				if (bank <= 0.0001f)
				{
					return false;
				}
			}
		}
		if (AvirMagic.On)
		{
			foreach (Pawn item2 in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), useCenter: true).OfType<Pawn>().ToList())
			{
				if (item2.Faction != parent.Faction && item2.Faction != Faction.OfPlayer)
				{
					continue;
				}
				Hediff_ArcaneCore hediff_ArcaneCore = Mages.Core(item2);
				if (hediff_ArcaneCore != null)
				{
					float num3 = Mathf.Min(bank, hediff_ArcaneCore.Capacity);
					hediff_ArcaneCore.Add(num3);
					bank -= num3;
					spentTotal += num3;
					if (bank <= 0.0001f)
					{
						return false;
					}
				}
			}
		}
		foreach (ArtifactState item3 in ChargeTargets(r))
		{
			if (!(item3.charge >= 0.999f))
			{
				float num4 = Mathf.Max(1f, ArtifactCharge.Capacity(item3, null));
				float num5 = Mathf.Min((1f - item3.charge) * num4, bank);
				item3.charge = Mathf.Min(1f, item3.charge + num5 / num4);
				bank -= num5;
				spentTotal += num5;
				if (bank <= 0.0001f)
				{
					return false;
				}
			}
		}
		foreach (Thing item4 in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), useCenter: true).Concat(Threaded))
		{
			CompCoreHearth compCoreHearth = item4.TryGetComp<CompCoreHearth>();
			if (compCoreHearth != null)
			{
				if (compCoreHearth.cracked || !(compCoreHearth.AmountCanAccept < 0.5f))
				{
					float num6 = GiveTo(item4, bank);
					bank -= num6;
					spentTotal += num6;
					if (bank <= 0.0001f)
					{
						return false;
					}
				}
				continue;
			}
			CompEnergyKeeper compEnergyKeeper = item4.TryGetComp<CompEnergyKeeper>();
			if (compEnergyKeeper != null && !compEnergyKeeper.IsFull)
			{
				float num7 = compEnergyKeeper.Give(bank);
				bank -= num7;
				spentTotal += num7;
				if (bank <= 0.0001f)
				{
					return false;
				}
				continue;
			}
			CompCrystalBattery compCrystalBattery = item4.TryGetComp<CompCrystalBattery>();
			if (compCrystalBattery != null && !compCrystalBattery.IsFull)
			{
				float num8 = Mathf.Min(CompCrystalBattery.Capacity - compCrystalBattery.Stored, bank);
				compCrystalBattery.stored = compCrystalBattery.Stored + num8;
				bank -= num8;
				spentTotal += num8;
				if (bank <= 0.0001f)
				{
					return false;
				}
			}
		}
		if (!FillPsyfocus(r))
		{
			return false;
		}
		if (r.focus > 0 && !GrabXp(r))
		{
			return false;
		}
		if (r.VortexRisk && spentTotal <= 0.001f && stepTicks > 600)
		{
			StartVortex();
			return true;
		}
		if (r.VortexRisk && spentTotal <= 0.001f)
		{
			return false;
		}
		return true;
	}

	private bool DoPurge(RitualReading r)
	{
		if (!PurgeMinds(r))
		{
			return false;
		}
		foreach (Thing item in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), useCenter: true))
		{
			foreach (object item2 in Vessels(item))
			{
				if (NeedsPurge(item2))
				{
					if (bank < 30f)
					{
						return false;
					}
					bank -= 30f;
					spentTotal += 30f;
					Purge(item2);
					FleckMaker.ThrowLightningGlow(item.DrawPos, parent.Map, 1f);
				}
			}
		}
		return true;
	}

	public static IEnumerable<object> Vessels(Thing t)
	{
		ArtifactState artifactState = t.TryGetComp<CompArtifact>()?.state;
		if (artifactState != null)
		{
			yield return artifactState;
		}
		ArtifactState artifactState2 = t.TryGetComp<CompArtifactSocket>()?.inlaid;
		if (artifactState2 != null)
		{
			yield return artifactState2;
		}
		CompAvirAmulet compAvirAmulet = t.TryGetComp<CompAvirAmulet>();
		if (compAvirAmulet != null && compAvirAmulet.HasCore)
		{
			yield return compAvirAmulet;
		}
		if (!(t is Pawn p))
		{
			yield break;
		}
		if (p.apparel != null)
		{
			foreach (Apparel item in p.apparel.WornApparel)
			{
				foreach (object item2 in Vessels(item))
				{
					yield return item2;
				}
			}
		}
		if (p.equipment?.Primary == null)
		{
			yield break;
		}
		foreach (object item3 in Vessels(p.equipment.Primary))
		{
			yield return item3;
		}
	}

	public static string VesselLabel(object v, Thing holder)
	{
		if (!(v is CompAvirAmulet compAvirAmulet))
		{
			return holder.LabelShortCap + "'s artifact";
		}
		return compAvirAmulet.parent.LabelShortCap;
	}

	private static bool NeedsPurge(object v)
	{
		if (!(v is ArtifactState artifactState))
		{
			if (v is CompAvirAmulet compAvirAmulet)
			{
				Dictionary<AmuletSubclassDef, int> opened = compAvirAmulet.opened;
				if ((opened == null || opened.Count <= 0) && compAvirAmulet.tkLevel <= 0)
				{
					return compAvirAmulet.slotOpen;
				}
				return true;
			}
			return false;
		}
		if (artifactState.opened.Count <= 0)
		{
			return artifactState.charge < 1f;
		}
		return true;
	}

	public static void Purge(object v)
	{
		if (v is ArtifactState artifactState)
		{
			artifactState.opened.Clear();
			artifactState.burntPoints = 0;
			artifactState.dormantArms.Clear();
			artifactState.charge = 1f;
			ArtifactBonusCache.ClearAll();
		}
		else if (v is CompAvirAmulet compAvirAmulet)
		{
			compAvirAmulet.opened?.Clear();
			compAvirAmulet.slotOpen = false;
			compAvirAmulet.tkLevel = 0;
			compAvirAmulet.spentLedger = 0;
		}
	}

	public static float Xp(object v)
	{
		if (!(v is ArtifactState artifactState))
		{
			if (!(v is CompAvirAmulet compAvirAmulet))
			{
				return 0f;
			}
			return compAvirAmulet.xpEarned;
		}
		return artifactState.xpEarned;
	}

	private static void SetXp(object v, float x)
	{
		if (v is ArtifactState artifactState)
		{
			artifactState.xpEarned = x;
		}
		else if (v is CompAvirAmulet compAvirAmulet)
		{
			compAvirAmulet.xpEarned = x;
		}
	}

	public static float FreeXp(object v)
	{
		ArtifactoricsSettingsDef main = ArtifactoricsSettingsDef.Main;
		if (main == null)
		{
			return 0f;
		}
		int points = ((v is ArtifactState artifactState) ? artifactState.PointsSpent : ((v is CompAvirAmulet a) ? AmuletDevelopment.PointsSpent(a) : 0));
		return Mathf.Max(0f, Xp(v) - main.XpForPoints(points));
	}

	private bool DoTransfer(RitualReading r)
	{
		if (fromVessel == null || toVessel == null)
		{
			fromVessel = ((transferFrom == null) ? null : Vessels(transferFrom).FirstOrDefault());
			toVessel = ((transferTo == null) ? null : Vessels(transferTo).FirstOrDefault());
			if (fromVessel == null || toVessel == null || fromVessel == toVessel)
			{
				End("nothing to transfer between.");
				return true;
			}
		}
		bool flag = r.verbs.Skip(verbIndex + 1).Any((RuneDef x) => x.kind == RuneKind.Purge);
		float num = (flag ? Xp(fromVessel) : FreeXp(fromVessel));
		if (num <= 1f)
		{
			fromVessel = (toVessel = null);
			return true;
		}
		float num2 = num / 1000f;
		if (bank < num2)
		{
			return false;
		}
		bank -= num2;
		spentTotal += num2;
		SetXp(fromVessel, Xp(fromVessel) - num);
		SetXp(toVessel, Xp(toVessel) + num);
		if (flag)
		{
			Purge(fromVessel);
		}
		Messages.Message(num.ToString("N0") + " experience streamed from " + transferFrom.LabelShortCap + " into " + transferTo.LabelShortCap + ".", parent, MessageTypeDefOf.PositiveEvent, historical: false);
		fromVessel = (toVessel = null);
		return true;
	}

	private void BreakCrystal()
	{
		Thing thing = FuelCells(ComponentKind.Crystal).SelectMany((IntVec3 x) => x.GetThingList(parent.Map)).FirstOrDefault((Thing t) => t.def == PsychicDefOf.Avir_PsyCrystal);
		if (thing == null)
		{
			BurnFeathers();
			return;
		}
		thing.SplitOff(1).Destroy();
		bank += 150f;
		FleckMaker.ThrowLightningGlow(thing.Spawned ? thing.DrawPos : parent.DrawPos, parent.Map, 1f);
	}

	private void BurnFeathers()
	{
		ThingDef feathers = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_AvirFeatheredFur");
		if (feathers != null)
		{
			Thing thing = FuelCells(ComponentKind.Feathers).SelectMany((IntVec3 x) => x.GetThingList(parent.Map)).FirstOrDefault((Thing t) => t.def == feathers);
			if (thing == null)
			{
				DrawBatteries();
				return;
			}
			int num = Mathf.Min(10, thing.stackCount);
			thing.SplitOff(num).Destroy();
			bank += (float)num * 6f * (Reading.Has(RuneKind.Fill) ? 2f : 1f);
			FleckMaker.ThrowLightningGlow(parent.DrawPos, parent.Map, 0.8f);
		}
	}

	private bool DoDrain(RitualReading r, int delta)
	{
		float reach = Reach(r);
		IntVec3 at = Center;
		bool flag = DrainLiving(r, delta) | DrainThreads(r, delta);
		CompPsychicPylon compPsychicPylon = CompPsychicPylon.All.FirstOrDefault((CompPsychicPylon x) => x.parent.Map == parent.Map && x.charge > 1f && (x.parent.Position - at).LengthHorizontal <= reach);
		if (compPsychicPylon == null)
		{
			if (!flag)
			{
				return r.anchor == 0;
			}
			return false;
		}
		float num = 0.3f * (float)delta;
		if (bank < num)
		{
			return false;
		}
		bank -= num;
		spentTotal += num;
		float num2 = compPsychicPylon.Take(2f * (float)delta);
		crystalProgress += num2 * Mathf.Clamp(drawQuality, 0.2f, 1.2f) * 0.5f;
		if (parent.IsHashIntervalTick(60, delta))
		{
			FleckMaker.ThrowLightningGlow(compPsychicPylon.parent.DrawPos, parent.Map, 0.8f);
		}
		while (crystalProgress >= 100f)
		{
			crystalProgress -= 100f;
			GenPlace.TryPlaceThing(ThingMaker.MakeThing(PsychicDefOf.Avir_PsyCrystal), parent.Position, parent.Map, ThingPlaceMode.Near);
		}
		return false;
	}

	private bool DoTransmute(RitualReading r)
	{
		ThingDef target = r.Seal;
		if (target == null)
		{
			return true;
		}
		Map map = parent.Map;
		List<ThingDef> inputs = r.Inputs;
		float num = Mathf.Max(0.01f, target.BaseMarketValue);
		Thing thing = FuelCells(ComponentKind.Material).SelectMany((IntVec3 c) => c.GetThingList(map)).FirstOrDefault((Thing t) => t.def.category == ThingCategory.Item && t.def != target && Transmutation.Allowed(t.def) && ((inputs.Count <= 0) ? Transmutation.Materials.Contains(t.def) : inputs.Contains(t.def)));
		if (thing == null)
		{
			transmuteCarry = 0f;
			if (r.anchor > 0 && performer != null)
			{
				End("no material left to transmute.");
			}
			return true;
		}
		float num2 = r.TransmuteYield(drawQuality);
		float num3 = Mathf.Max(0.0001f, thing.def.BaseMarketValue * num2);
		int num4 = Mathf.Clamp(Mathf.CeilToInt((5f * num - transmuteCarry) / num3), 1, thing.stackCount);
		float num5 = transmuteCarry + (float)num4 * num3;
		int num6 = Mathf.FloorToInt(num5 / num + 0.0001f);
		float num7 = num * (float)Mathf.Max(1, num6) / 25f;
		if (bank < num7)
		{
			return false;
		}
		bank -= num7;
		spentTotal += num7;
		thing.SplitOff(num4).Destroy();
		transmuteCarry = num5 - (float)num6 * num;
		if (num6 > 0)
		{
			Thing thing2 = ThingMaker.MakeThing(target);
			thing2.stackCount = num6;
			GenPlace.TryPlaceThing(thing2, parent.Position, map, ThingPlaceMode.Near);
		}
		FleckMaker.ThrowLightningGlow(parent.DrawPos, map, 1.2f);
		return false;
	}

	private bool DoHold(RitualReading r, int delta)
	{
		Faction fac = parent.Faction ?? Faction.OfPlayer;
		bool flag = false;
		foreach (Pawn item in parent.Map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (!item.Dead && item.HostileTo(fac) && item.RaceProps.IsFlesh && !((item.Position - Center).LengthHorizontal > Reach(r)))
			{
				flag = true;
				item.stances?.stunner?.StunFor(120, performer, addBattleLog: false, showMote: false);
			}
		}
		float num = 0.02f * (float)delta;
		if (bank < num)
		{
			return false;
		}
		bank -= num;
		spentTotal += num;
		if (!flag)
		{
			return r.anchor == 0;
		}
		return false;
	}

	public override void CompTickRare()
	{
		base.CompTickRare();
		Map map = parent.Map;
		if (map == null)
		{
			return;
		}
		if (Drawn && !engraved && !parent.Position.Roofed(map) && (map.weatherManager.RainRate > 0.05f || map.weatherManager.SnowRate > 0.05f))
		{
			washed += 0.1f;
			if (washed >= 1f)
			{
				drawn = null;
				drawQuality = 0f;
				armed = false;
				ReleaseAll("the rain washed the chalk away");
				if (Running)
				{
					End("the rain washed the chalk away.");
				}
				Messages.Message("Rain and snow washed the ritual circle away.", parent, MessageTypeDefOf.NegativeEvent, historical: false);
				Arlia.Say("washed");
			}
		}
		TrapTick();
		VortexTick();
		SelfSustainTick();
	}

	private void TrapTick()
	{
		if (!armed || !Drawn || !Reading.trap)
		{
			if (held.Count > 0)
			{
				ReleaseAll("the cage is open");
			}
			return;
		}
		Map map = parent.Map;
		Faction own = parent.Faction ?? Faction.OfPlayer;
		foreach (IntVec3 item in GenRadial.RadialCellsAround(parent.Position, Radius, useCenter: true))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> list = map.thingGrid.ThingsListAtFast(item);
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] is Pawn { Dead: false } pawn && pawn.RaceProps.IsFlesh && Catches(pawn, own) && !held.Contains(pawn))
				{
					held.Add(pawn);
					if (!pawn.health.hediffSet.HasHediff(RitualDefOf.Avir_RitualHeld))
					{
						pawn.health.AddHediff(RitualDefOf.Avir_RitualHeld);
					}
					Messages.Message(pawn.LabelShortCap + " is caught in the Trap.", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
					Arlia.Say("caught");
				}
			}
		}
		if (engraved)
		{
			Attract(map, own);
		}
		for (int num = held.Count - 1; num >= 0; num--)
		{
			Pawn pawn2 = held[num];
			if (pawn2 == null || pawn2.Dead || !pawn2.Spawned || pawn2.Map != map)
			{
				Release(pawn2, null);
			}
			else
			{
				pawn2.stances?.stunner?.StunFor(300, null, addBattleLog: false, showMote: false);
				TrapVariantTick(pawn2, Reading);
				if (held.Contains(pawn2))
				{
					if (!Inside(pawn2.Position))
					{
						pawn2.Position = parent.Position;
						pawn2.Notify_Teleported(endCurrentJob: true, resetTweenedPos: false);
					}
					if (Rand.Chance(0.002f / Mathf.Max(0.2f, drawQuality) * ((Reading.trapKind == TrapKind.Pulse) ? 0.25f : 1f)))
					{
						Release(pawn2, pawn2.LabelShortCap + " broke out of the Trap.");
					}
				}
			}
		}
	}

	private static bool Catches(Pawn q, Faction own)
	{
		if (!q.HostileTo(own) && (!q.IsPrisonerOfColony || !PrisonBreakUtility.IsPrisonBreaking(q)))
		{
			if (q.IsSlaveOfColony)
			{
				return SlaveRebellionUtility.IsRebelling(q);
			}
			return false;
		}
		return true;
	}

	private void Attract(Map map, Faction own)
	{
		float num = Radius + 6f;
		foreach (IAttackTarget item in map.attackTargetsCache.TargetsHostileToFaction(own).ToList())
		{
			if (!(item.Thing is Pawn { Spawned: not false, Dead: false, Downed: false } pawn) || !pawn.RaceProps.IsFlesh || held.Contains(pawn))
			{
				continue;
			}
			float lengthHorizontal = (pawn.Position - parent.Position).LengthHorizontal;
			if (!(lengthHorizontal > num) && !(lengthHorizontal <= Radius) && Rand.Chance(0.5f))
			{
				IntVec3 intVec = pawn.Position + new IntVec3(Math.Sign(parent.Position.x - pawn.Position.x), 0, Math.Sign(parent.Position.z - pawn.Position.z));
				if (intVec.InBounds(map) && intVec.Standable(map))
				{
					pawn.pather?.StopDead();
					pawn.Position = intVec;
					pawn.Notify_Teleported(endCurrentJob: false, resetTweenedPos: false);
					RitualFx.Mote(pawn.DrawPos, map, 0.5f);
				}
			}
		}
	}

	private void Release(Pawn q, string msg)
	{
		held.Remove(q);
		pulsesTaken.Remove(q);
		Hediff hediff = q?.health?.hediffSet?.GetFirstHediffOfDef(RitualDefOf.Avir_RitualHeld);
		if (hediff != null)
		{
			q.health.RemoveHediff(hediff);
		}
		if (msg != null)
		{
			Messages.Message(msg, q, MessageTypeDefOf.ThreatSmall, historical: false);
		}
	}

	private void ReleaseAll(string why)
	{
		foreach (Pawn item in held.ToList())
		{
			Release(item, (item != null) ? (item.LabelShortCap + " is free: " + why + ".") : null);
		}
	}

	public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
	{
		base.PostDeSpawn(map, mode);
		ReleaseAll("the circle is gone");
	}

	public override IEnumerable<Gizmo> CompGetGizmosExtra()
	{
		foreach (Gizmo item in base.CompGetGizmosExtra())
		{
			yield return item;
		}
		if (parent.Faction != Faction.OfPlayer)
		{
			yield break;
		}
		yield return new Command_Action
		{
			defaultLabel = ((schema == null) ? "Choose ritual..." : ("Ritual: " + schema.name)),
			defaultDesc = "Open the ritual book: read the rituals you know and choose one to draw into this circle.",
			icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Fill")?.Ink ?? BaseContent.BadTex),
			action = delegate
			{
				Find.WindowStack.Add(new Dialog_RitualBook(this));
			}
		};
		if (Drawn && Reading.trap)
		{
			yield return new Command_Toggle
			{
				defaultLabel = "Arm the Trap",
				defaultDesc = "While armed, any living foe that steps into the circle is held in it. The full Trap shuts the stream in: the prisoner is not drained, only held, until the chalk is washed away, it dies or it breaks out.",
				icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Safety")?.Ink ?? BaseContent.BadTex),
				isActive = () => armed,
				toggleAction = delegate
				{
					armed = !armed;
				}
			};
		}
		else if (Drawn)
		{
			Command_Action command_Action = new Command_Action
			{
				defaultLabel = (Running ? "Stop ritual" : "Begin ritual..."),
				defaultDesc = "Begin with the conduit chosen in the ritual book, or choose one here: a colonist, or an artifact, crystal battery, artifact building or psychic pylon at the centre of the circle (it gives its own charge). The performer stands in the centre; everyone standing in the circle gives energy too.",
				icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Focus")?.Ink ?? BaseContent.BadTex),
				action = (Running ? ((Action)delegate
				{
					performer?.jobs?.EndCurrentJob(JobCondition.InterruptForced);
					End("stopped.");
				}) : ((Action)delegate
				{
					if (chosenPerformer == null && chosenThing == null)
					{
						BeginMenu();
					}
					else
					{
						string text2 = BeginChosen();
						if (text2 != null)
						{
							Messages.Message("Cannot begin: " + text2 + ".", parent, MessageTypeDefOf.RejectInput, historical: false);
						}
					}
				}))
			};
			string text = (Running ? null : CannotPerform(null));
			if (text != null)
			{
				command_Action.Disable(text);
			}
			yield return command_Action;
		}
		foreach (Gizmo item2 in AutoGizmos())
		{
			yield return item2;
		}
		foreach (Gizmo item3 in MoreGizmos())
		{
			yield return item3;
		}
		if (!Prefs.DevMode)
		{
			yield break;
		}
		yield return new Command_Action
		{
			defaultLabel = "Dev: draw now",
			action = DevDrawNow
		};
		foreach (Gizmo item4 in DevGizmos())
		{
			yield return item4;
		}
		yield return new Command_Action
		{
			defaultLabel = "Dev: learn hidden rituals",
			action = delegate
			{
				foreach (RitualPresetDef item5 in RitualPresetDef.All.Where((RitualPresetDef x) => x.hidden))
				{
					RitualDiscoveries.Learn(item5);
				}
			}
		};
	}

	private void BeginMenu()
	{
		_ = Reading;
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		foreach (Pawn item in parent.Map.mapPawns.FreeColonistsSpawned.Where((Pawn x) => !x.Downed && !x.Drafted))
		{
			Pawn pp = item;
			string label = item.LabelShort + " (sensitivity " + item.GetStatValue(StatDefOf.PsychicSensitivity).ToStringPercent() + ", composure " + GameComponent_AvirComposure.For(item, drawn, this).ToStringPercent() + ")";
			list.Add(new FloatMenuOption(label, delegate
			{
				Prepare(pp);
			}));
		}
		string text = CannotSustain(selfRunning: false);
		foreach (Thing item2 in ConduitCandidates())
		{
			Thing tt = item2;
			string text2 = "Through " + item2.LabelShort + " (its own charge, composure " + 0.6f.ToStringPercent() + ")";
			list.Add((text == null) ? new FloatMenuOption(text2, delegate
			{
				chosenThing = tt;
				chosenPerformer = null;
				string text3 = BeginChosen();
				if (text3 != null)
				{
					Messages.Message("Cannot begin: " + text3 + ".", parent, MessageTypeDefOf.RejectInput, historical: false);
				}
			}) : new FloatMenuOption(text2 + ": " + text, null));
		}
		if (list.Count == 0)
		{
			list.Add(new FloatMenuOption("No free colonist, and no artifact or crystal battery at the centre", null));
		}
		Find.WindowStack.Add(new FloatMenu(list));
	}

	private void TransferMenu(Pawn p, IntVec3 at)
	{
		List<Thing> list = (from t in GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Radius, useCenter: true)
			where Vessels(t).Any()
			select t).ToList();
		List<FloatMenuOption> list2 = new List<FloatMenuOption>();
		foreach (Thing item in list)
		{
			foreach (Thing item2 in list)
			{
				if (item != item2)
				{
					Thing aa = item;
					Thing bb = item2;
					object v = Vessels(item).First();
					list2.Add(new FloatMenuOption("From " + VesselLabel(v, item) + " (" + Xp(v).ToString("N0") + " xp, free " + FreeXp(v).ToString("N0") + ") into " + VesselLabel(Vessels(item2).First(), item2), delegate
					{
						Order(p, at, aa, bb, 0);
					}));
				}
			}
		}
		if (list2.Count == 0)
		{
			list2.Add(new FloatMenuOption("Put two amulets or artifacts into the circle (or stand their wearers in it)", null));
		}
		Find.WindowStack.Add(new FloatMenu(list2));
	}

	private void Order(Pawn p, IntVec3 at, Thing from, Thing to, int mode)
	{
		Begin(p, at, from, to, mode);
		Job job = JobMaker.MakeJob(RitualDefOf.Avir_PerformRitual, parent);
		if (!p.jobs.TryTakeOrderedJob(job, JobTag.Misc))
		{
			End(null);
		}
	}

	public override string CompInspectStringExtra()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(RitualCircles.Label(Size).CapitalizeFirst()).Append(" ritual circle");
		StreamNet streamNet = (parent.Spawned ? Net : null);
		if (streamNet != null)
		{
			stringBuilder.Append(" (stream network: ").Append(streamNet.Stored.ToString("0")).Append(" / ")
				.Append(streamNet.Capacity.ToString("0"))
				.Append(")");
		}
		if (schema == null)
		{
			stringBuilder.Append(": no ritual chosen");
		}
		else
		{
			stringBuilder.Append(": ").Append(schema.name);
			if (NeedsDrawing)
			{
				stringBuilder.Append((drawWork > 0f) ? (" - drawn " + (drawWork / DrawWorkNeeded).ToStringPercent()) : " - waits to be drawn");
			}
			else
			{
				stringBuilder.Append(engraved ? " - engraved, quality " : " - drawn in chalk, quality ").Append(drawQuality.ToStringPercent());
			}
			if (NeedsDrawing && engraveNext)
			{
				stringBuilder.Append(" (engraving)");
			}
		}
		if (washed > 0f && Drawn)
		{
			stringBuilder.Append("\nThe chalk is washing away: ").Append(washed.ToStringPercent());
		}
		if (Drawn && !Running)
		{
			string text = MissingMaterial();
			if (text != null)
			{
				stringBuilder.Append("\nWaits for material: ").Append(text);
			}
		}
		if (armed)
		{
			stringBuilder.Append("\nTrap armed").Append((held.Count > 0) ? (", holding " + held.Count) : "");
		}
		if (Running)
		{
			stringBuilder.Append("\nPerformed by ").Append(performer?.LabelShort ?? conduit.LabelShort);
			if (Reading.RawFlow)
			{
				stringBuilder.Append(", energy held ").Append(bank.ToString("0"));
			}
			else
			{
				stringBuilder.Append(", energy spent ").Append(spentTotal.ToString("0"));
			}
		}
		MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(parent.Map);
		if (mapComponent_ExternalFlow != null)
		{
			stringBuilder.Append("\n").Append(("External stream: " + mapComponent_ExternalFlow.Total.ToString("0") + "% (" + MapComponent_ExternalFlow.Tier(mapComponent_ExternalFlow.Total) + ")").Colorize(FlowGauge.ColorOf(mapComponent_ExternalFlow.Total)));
		}
		return stringBuilder.ToString();
	}

	public override void PostDraw()
	{
		base.PostDraw();
		DrawMore();
		Vector3 drawPos = parent.DrawPos;
		drawPos.y = AltitudeLayer.FloorEmplacement.AltitudeFor() + 0.01f;
		RitualSchema ritualSchema = drawn ?? schema;
		bool flag = Running || held.Count > 0;
		float num = ((drawn != null) ? (0.85f * (1f - washed * 0.7f)) : 0.25f);
		Color color = AspectColor;
		Color color2 = (flag ? new Color(color.r, color.g, color.b, num) : ((drawn != null && engraved) ? new Color(0.18f, 0.16f, 0.15f, 0.9f) : new Color(0.92f, 0.9f, 0.85f, num)));
		bool flag2 = drawn != null && engraved && FlowTex.Engraved != null && !flag;
		Material m = (flag2 ? MaterialPool.MatFrom(FlowTex.Engraved, ShaderDatabase.Transparent, Color.white) : MaterialPool.MatFrom(RitualTex.Ring, ShaderDatabase.Transparent, color2));
		float num2 = Radius * 0.96f;
		DrawRing(drawPos, num2 * 2f, m);
		if (!flag2 && RitualTex.Rim != RitualTex.Ring)
		{
			DrawRing(drawPos, num2 * 2f * RitualTex.RimGrow, MaterialPool.MatFrom(RitualTex.Rim, ShaderDatabase.Transparent, color2));
		}
		int num3 = ((ritualSchema != null && ritualSchema.rings.Count > 0) ? ritualSchema.rings.Count : RitualCircles.Rings(Size));
		float num4 = num2 * 0.28f;
		for (int i = 0; i < num3; i++)
		{
			float num5 = num4 + (num2 - num4) * (float)i / (float)num3;
			if (i > 0)
			{
				DrawRing(drawPos, num5 * 2f, m);
			}
		}
		DrawRing(drawPos, num4 * 2f, m);
		if (ritualSchema == null)
		{
			return;
		}
		Color col = (flag ? new Color(Mathf.Min(1f, color.r * 1.2f + 0.1f), Mathf.Min(1f, color.g * 1.2f + 0.1f), Mathf.Min(1f, color.b * 1.2f + 0.1f), num) : (flag2 ? new Color(0.1f, 0.09f, 0.085f, 0.92f) : color2));
		Color color3 = new Color(col.r, col.g, col.b, col.a * (flag2 ? 0.85f : 0.7f));
		DrawGlyph(drawPos, num4 * 1.6f, RitualOrnament.CoreGlow, new Color(col.r, col.g, col.b, col.a * 0.75f));
		for (int j = 0; j < num3; j++)
		{
			List<RuneDef> list = ((j < ritualSchema.rings.Count) ? ritualSchema.rings[j].runes : new List<RuneDef>());
			int num6 = ((j >= ritualSchema.rings.Count) ? 1 : ritualSchema.rings[j].Slots);
			float num7 = num4 + (num2 - num4) * (float)j / (float)num3;
			float num8 = num4 + (num2 - num4) * (float)(j + 1) / (float)num3;
			float radius = (num7 + num8) / 2f;
			float num9 = Mathf.Min(RitualOrnament.GlyphSize(num7, num8, num6, 1.2f), (num8 - num7) * 0.7f);
			for (int k = 0; k < num6; k++)
			{
				float num10 = RitualOrnament.SlotAngle(j, k, num6) + 180f / (float)num6;
				if (num6 > 1)
				{
					DrawGlyph(At(drawPos, radius, num10), RitualOrnament.ConnectorSize(num7, num8, num6) * 0.85f, RitualOrnament.Connector(j, k, num6, glow: true), color3, num10);
				}
				Vector3 pos = At(drawPos, radius, RitualOrnament.SlotAngle(j, k, num6));
				DrawGlyph(pos, num9, RitualOrnament.FrameGlow, (k < list.Count && list[k] != null) ? color3 : new Color(col.r, col.g, col.b, col.a * 0.45f));
				if (k < list.Count && list[k] != null)
				{
					DrawRune(pos, num9 * 0.66f, list[k], col, flag2, flag);
				}
			}
		}
		int count = ritualSchema.components.Count;
		for (int l = 0; l < count; l++)
		{
			RitualComponent ritualComponent = ritualSchema.components[l];
			Vector3 vector = ComponentCell(l, ritualSchema).ToVector3Shifted();
			vector.y = drawPos.y;
			float num11 = 0.92f;
			DrawRing(vector, num11, m);
			bool flag3 = ritualComponent.kind == ComponentKind.Seal;
			DrawGlyph(vector, num11 * (flag3 ? 1.15f : 0.95f), flag3 ? RitualOrnament.SealGlow : RitualOrnament.FrameGlow, color3);
			Texture2D icon = ritualComponent.Icon;
			bool flag4 = ritualComponent.kind != ComponentKind.Seal && ritualComponent.kind != 0 && drawn != null && parent.Spawned && Holding(l) != null;
			if (icon != null && !flag4)
			{
				DrawGlyph(vector, num11 * 0.5f, icon, new Color(1f, 1f, 1f, (ritualComponent.kind == ComponentKind.Seal) ? num : (num * 0.4f)));
			}
		}
	}

	private static void DrawRune(Vector3 pos, float size, RuneDef rune, Color col, bool carved, bool live)
	{
		Vector3 vector = new Vector3(0f, -0.0005f, 0f);
		if (carved)
		{
			DrawGlyph(pos + vector + new Vector3(size * 0.035f, 0f, (0f - size) * 0.035f), size, rune.Glow, new Color(0.78f, 0.76f, 0.72f, 0.55f));
			DrawGlyph(pos, size, rune.Ink, col);
		}
		else
		{
			DrawGlyph(pos + vector, size * 1.06f, rune.Ink, new Color(0f, 0f, 0f, 0.55f * col.a));
			DrawGlyph(pos, size, rune.Glow, live ? col : new Color(1f, 0.98f, 0.94f, col.a));
		}
	}

	private static Vector3 At(Vector3 c, float radius, float screenAngle)
	{
		float f = (0f - screenAngle) * (MathF.PI / 180f);
		return c + new Vector3(Mathf.Cos(f) * radius, 0.001f, Mathf.Sin(f) * radius);
	}

	private static void DrawGlyph(Vector3 pos, float size, Texture2D tex, Color col, float turn = 0f)
	{
		Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.AngleAxis(turn, Vector3.up), new Vector3(size, 1f, size)), MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, col), 0);
	}

	private static void DrawRing(Vector3 c, float d, Material m)
	{
		Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(d, 1f, d)), m, 0);
	}

	public override void PostExposeData()
	{
		base.PostExposeData();
		Scribe_Deep.Look(ref schema, "schema");
		Scribe_Deep.Look(ref drawn, "drawn");
		Scribe_Values.Look(ref drawQuality, "drawQuality", 0f);
		Scribe_Values.Look(ref drawWork, "drawWork", 0f);
		Scribe_Values.Look(ref washed, "washed", 0f);
		Scribe_Values.Look(ref armed, "armed", defaultValue: false);
		Scribe_Collections.Look(ref held, "held", LookMode.Reference);
		if (Scribe.mode == LoadSaveMode.Saving)
		{
			pulsesTaken.RemoveAll((KeyValuePair<Pawn, int> kv) => kv.Key == null || kv.Key.Destroyed);
		}
		Scribe_Collections.Look(ref pulsesTaken, "pulsesTaken", LookMode.Reference, LookMode.Value, ref pulseKeys, ref pulseVals);
		Scribe_Values.Look(ref pulseCooldown, "pulseCooldown", 0);
		Scribe_References.Look(ref performer, "performer");
		Scribe_References.Look(ref transferFrom, "transferFrom");
		Scribe_References.Look(ref transferTo, "transferTo");
		Scribe_Values.Look(ref verbIndex, "verbIndex", 0);
		Scribe_Values.Look(ref bank, "bank", 0f);
		Scribe_Values.Look(ref spentTotal, "spentTotal", 0f);
		Scribe_Values.Look(ref transmuteCarry, "transmuteCarry", 0f);
		Scribe_Values.Look(ref runTicks, "runTicks", 0);
		Scribe_Values.Look(ref crystalProgress, "crystalProgress", 0f);
		Scribe_Values.Look(ref engraved, "engraved", defaultValue: false);
		Scribe_Values.Look(ref engraveNext, "engraveNext", defaultValue: false);
		Scribe_Values.Look(ref chiselPending, "chiselPending", defaultValue: false);
		Scribe_Values.Look(ref chiselWork, "chiselWork", 0f);
		Scribe_Values.Look(ref selfSustain, "selfSustain", defaultValue: false);
		ExposeAuto();
		Scribe_References.Look(ref conduit, "conduit");
		ExposeMore();
		if (Scribe.mode == LoadSaveMode.PostLoadInit && held == null)
		{
			held = new List<Pawn>();
		}
		if (Scribe.mode == LoadSaveMode.PostLoadInit && pulsesTaken == null)
		{
			pulsesTaken = new Dictionary<Pawn, int>();
		}
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			held.RemoveAll((Pawn x) => x == null);
		}
	}

	public void FinishChisel(Pawn by)
	{
		bool flag = engraved;
		chiselPending = false;
		chiselWork = 0f;
		Erase();
		if (by != null)
		{
			Messages.Message(by.LabelShortCap + (flag ? " levelled the engraving away." : " wiped the chalk away."), parent, MessageTypeDefOf.NeutralEvent, historical: false);
		}
	}

	private List<Thing> Spared(Map map, Pawn except)
	{
		Faction faction = parent.Faction ?? Faction.OfPlayer;
		List<Thing> list = new List<Thing> { parent };
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned)
		{
			if (item != except && item.Faction == faction)
			{
				list.Add(item);
			}
		}
		foreach (Building item2 in map.listerBuildings.allBuildingsColonist)
		{
			list.Add(item2);
		}
		return list;
	}

	private void Gain(RitualReading r, float amount)
	{
		if (amount <= 0f)
		{
			return;
		}
		if (verbIndex >= r.verbs.Count - 1)
		{
			crystalProgress += amount * Mathf.Clamp(drawQuality, 0.2f, 1.2f) * 0.5f;
			while (crystalProgress >= 100f)
			{
				crystalProgress -= 100f;
				GenPlace.TryPlaceThing(ThingMaker.MakeThing(PsychicDefOf.Avir_PsyCrystal), parent.Position, parent.Map, ThingPlaceMode.Near);
			}
		}
		else
		{
			bank += amount;
		}
	}

	private bool FillPsyfocus(RitualReading r)
	{
		foreach (Pawn item in PawnsInReach(r))
		{
			if (item.Faction != parent.Faction || item.psychicEntropy == null || !item.HasPsylink)
			{
				continue;
			}
			float num = 1f - item.psychicEntropy.CurrentPsyfocus;
			if (!(num <= 0.005f))
			{
				float num2 = Mathf.Min(num * 100f, bank);
				item.psychicEntropy.OffsetPsyfocusDirectly(num2 / 100f);
				bank -= num2;
				spentTotal += num2;
				if (bank <= 0.0001f)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static IEnumerable<HediffDef> MindStates()
	{
		if (HediffDefOf.PsychicHangover != null)
		{
			yield return HediffDefOf.PsychicHangover;
		}
		if (HediffDefOf.PsychicShock != null)
		{
			yield return HediffDefOf.PsychicShock;
		}
		if (HediffDefOf.DarkPsychicShock != null)
		{
			yield return HediffDefOf.DarkPsychicShock;
		}
		if (RitualDefOf.Avir_RitualBacklash != null)
		{
			yield return RitualDefOf.Avir_RitualBacklash;
		}
		HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_FlowPressure");
		if (namedSilentFail != null)
		{
			yield return namedSilentFail;
		}
	}

	private bool PurgeMinds(RitualReading r)
	{
		foreach (Pawn item in PawnsInReach(r))
		{
			if (item.Faction != parent.Faction)
			{
				continue;
			}
			foreach (HediffDef item2 in MindStates())
			{
				Hediff firstHediffOfDef = item.health.hediffSet.GetFirstHediffOfDef(item2);
				if (firstHediffOfDef != null)
				{
					if (bank < 30f)
					{
						return false;
					}
					bank -= 30f;
					spentTotal += 30f;
					item.health.RemoveHediff(firstHediffOfDef);
					FleckMaker.ThrowLightningGlow(item.DrawPos, parent.Map, 0.8f);
				}
			}
			if (item.psychicEntropy != null && item.psychicEntropy.EntropyValue > 0.5f)
			{
				if (bank < 30f)
				{
					return false;
				}
				bank -= 30f;
				spentTotal += 30f;
				item.psychicEntropy.RemoveAllEntropy();
			}
		}
		return true;
	}

	private IEnumerable<Pawn> PawnsInReach(RitualReading r)
	{
		float reach = Reach(r);
		IntVec3 at = Center;
		foreach (Pawn item in parent.Map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (!item.Dead && (item.Position - at).LengthHorizontal <= reach)
			{
				yield return item;
			}
		}
	}

	public IntVec3 ComponentCell(int k, RitualSchema s = null)
	{
		s = s ?? drawn ?? schema;
		int i = Mathf.Max(1, s?.components.Count ?? 1);
		float num = Radius * 0.96f * 1.22f + 0.4f;
		float f = (0f - (-90f + 360f * (float)k / (float)i)) * (MathF.PI / 180f);
		IntVec3 c = parent.Position + new IntVec3(Mathf.RoundToInt(Mathf.Cos(f) * num), 0, Mathf.RoundToInt(Mathf.Sin(f) * num));
		for (int l = 0; l < 4; l++)
		{
			if (!Inside(c) && !Enumerable.Range(0, k).Any((int j) => ComponentCellRaw(j, s, i) == c))
			{
				break;
			}
			c += new IntVec3(Mathf.RoundToInt(Mathf.Cos(f)), 0, Mathf.RoundToInt(Mathf.Sin(f)));
		}
		return c;
	}

	private IntVec3 ComponentCellRaw(int k, RitualSchema s, int n)
	{
		float num = Radius * 0.96f * 1.22f + 0.4f;
		float f = (0f - (-90f + 360f * (float)k / (float)n)) * (MathF.PI / 180f);
		return parent.Position + new IntVec3(Mathf.RoundToInt(Mathf.Cos(f) * num), 0, Mathf.RoundToInt(Mathf.Sin(f) * num));
	}

	public IEnumerable<IntVec3> FuelCells(ComponentKind kind)
	{
		foreach (IntVec3 cell in Cells)
		{
			yield return cell;
		}
		RitualSchema s = drawn;
		if (s == null)
		{
			yield break;
		}
		for (int i = 0; i < s.components.Count; i++)
		{
			if (s.components[i].kind == kind)
			{
				IntVec3 intVec = ComponentCell(i);
				if (intVec.InBounds(parent.Map))
				{
					yield return intVec;
				}
			}
		}
	}

	public Thing Holding(int k)
	{
		RitualSchema ritualSchema = drawn;
		if (ritualSchema == null || k >= ritualSchema.components.Count || !parent.Spawned)
		{
			return null;
		}
		RitualComponent ritualComponent = ritualSchema.components[k];
		IntVec3 c = ComponentCell(k);
		if (!c.InBounds(parent.Map))
		{
			return null;
		}
		ThingDef expected = ritualComponent.Expected;
		foreach (Thing thing in c.GetThingList(parent.Map))
		{
			bool num;
			if (ritualComponent.kind != ComponentKind.Artifact)
			{
				if (expected == null)
				{
					continue;
				}
				num = thing.def == expected;
			}
			else
			{
				num = thing.TryGetComp<CompArtifact>()?.state != null;
			}
			if (num)
			{
				return thing;
			}
		}
		return null;
	}

	private void DrawBatteries()
	{
		RitualSchema ritualSchema = drawn;
		if (ritualSchema == null)
		{
			return;
		}
		for (int i = 0; i < ritualSchema.components.Count; i++)
		{
			if (ritualSchema.components[i].kind != ComponentKind.Battery)
			{
				continue;
			}
			Thing thing = Holding(i);
			if (thing != null)
			{
				float num = TakeFrom(thing, 150f);
				if (!(num <= 0f))
				{
					bank += num;
					FleckMaker.ThrowLightningGlow(thing.DrawPos, parent.Map, 0.8f);
					return;
				}
			}
		}
		StreamNet net = Net;
		if (net == null)
		{
			return;
		}
		foreach (CompEnergyKeeper keeper in net.Keepers)
		{
			float num2 = keeper.Take(150f);
			if (!(num2 <= 0f))
			{
				bank += num2;
				break;
			}
		}
	}

	public float ArtifactBoost(int delta)
	{
		RitualSchema ritualSchema = drawn;
		if (ritualSchema == null)
		{
			return 0f;
		}
		float num = 0f;
		for (int i = 0; i < ritualSchema.components.Count; i++)
		{
			if (ritualSchema.components[i].kind != ComponentKind.Artifact)
			{
				continue;
			}
			ArtifactState artifactState = Holding(i)?.TryGetComp<CompArtifact>()?.state;
			if (artifactState != null && !(artifactState.charge <= 0.01f))
			{
				num += 0.15f + 0.05f * (float)(int)artifactState.heartQuality;
				if (delta > 0)
				{
					artifactState.charge = Mathf.Max(0f, artifactState.charge - 2E-05f * (float)delta);
				}
			}
		}
		return num;
	}

	private IEnumerable<Gizmo> DevGizmos()
	{
		Map map = parent.Map;
		MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map);
		Command_Action command_Action = new Command_Action();
		MapComponent_ExternalFlow mapComponent_ExternalFlow = ext;
		command_Action.defaultLabel = "Dev: stream " + ((mapComponent_ExternalFlow != null && mapComponent_ExternalFlow.devFlow >= 0f) ? ("(held " + ext.devFlow.ToString("0") + ")") : "(live)");
		command_Action.action = delegate
		{
			List<FloatMenuOption> list = new List<FloatMenuOption>
			{
				new FloatMenuOption("live (release)", delegate
				{
					ext.devFlow = -1f;
				})
			};
			foreach (float item in new float[6] { 10000f, 35000f, 70000f, 150000f, 300000f, 600000f }.Select((float v) => v * 15f))
			{
				float ff = item;
				list.Add(new FloatMenuOption(item.ToString("0") + "% - " + MapComponent_ExternalFlow.Tier(item), delegate
				{
					ext.devFlow = ff;
				}));
			}
			Find.WindowStack.Add(new FloatMenu(list));
		};
		yield return command_Action;
		yield return new Command_Action
		{
			defaultLabel = "Dev: stream weather hour",
			action = delegate
			{
				FlowWeather.Hour(map, ext);
			}
		};
		yield return new Command_Toggle
		{
			defaultLabel = "Dev: no backlash",
			isActive = () => DevNoBacklash,
			toggleAction = delegate
			{
				DevNoBacklash = !DevNoBacklash;
			}
		};
		if (Running)
		{
			yield return new Command_Action
			{
				defaultLabel = "Dev: +1000 energy",
				action = delegate
				{
					bank += 1000f;
				}
			};
			yield return new Command_Action
			{
				defaultLabel = "Dev: skip the gathering",
				action = delegate
				{
					stepTicks = Mathf.Max(stepTicks, 900);
				}
			};
		}
		if (Drawn && vortex <= 0f)
		{
			yield return new Command_Action
			{
				defaultLabel = "Dev: start vortex",
				action = delegate
				{
					vortex = 1f;
				}
			};
		}
		if (vortex > 0f)
		{
			yield return new Command_Action
			{
				defaultLabel = "Dev: vortex +3",
				action = delegate
				{
					vortex = Mathf.Min(10f, vortex + 3f);
				}
			};
		}
		if (selfSustain && conduit == null && Drawn)
		{
			yield return new Command_Action
			{
				defaultLabel = "Dev: wake the engraving",
				action = delegate
				{
					Thing thing = FindConduit();
					if (thing == null)
					{
						Messages.Message("No conduit at the centre.", MessageTypeDefOf.RejectInput, historical: false);
					}
					else
					{
						Begin(null, target);
						conduit = thing;
					}
				}
			};
		}
		yield return new Command_Action
		{
			defaultLabel = "Dev: effects",
			action = delegate
			{
				Vector3 c = parent.DrawPos;
				List<FloatMenuOption> options = new List<FloatMenuOption>
				{
					new FloatMenuOption("barrier ripple", delegate
					{
						RitualFx.Ripple(c, map);
					}),
					new FloatMenuOption("release blast (r 5)", delegate
					{
						RitualFx.Blast(c, map, 5f);
					}),
					new FloatMenuOption("lift wave (r 5)", delegate
					{
						RitualFx.Wave(c, map, 5f);
					}),
					new FloatMenuOption("stream motes", delegate
					{
						for (int i = 0; i < 20; i++)
						{
							RitualFx.Mote(c + Rand.InsideUnitCircleVec3 * 3f, map, Rand.Range(0.4f, 0.9f));
						}
					}),
					new FloatMenuOption("dome for 30 s", delegate
					{
						DropDome();
						dome = new BarrierDome
						{
							map = map,
							center = parent.Position,
							radius = 7f,
							hit = DomeHit
						};
						BarrierDome.All.Add(dome);
						domeHp = 150f;
						devDomeUntil = Find.TickManager.TicksGame + 1800;
					})
				};
				Find.WindowStack.Add(new FloatMenu(options));
			}
		};
	}

	public Thing FindConduit()
	{
		if (chosenThing == null || !ConduitCandidates().Contains(chosenThing))
		{
			return ConduitCandidates().FirstOrDefault();
		}
		return chosenThing;
	}

	public IEnumerable<Thing> ConduitCandidates()
	{
		Map map = parent.Map;
		if (map == null)
		{
			yield break;
		}
		foreach (IntVec3 item in GenRadial.RadialCellsAround(parent.Position, Mathf.Max(0.9f, Radius * 0.35f), useCenter: true))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			foreach (Thing thing in item.GetThingList(map))
			{
				if (thing != parent && !(thing is Pawn) && (thing.TryGetComp<CompArtifact>()?.state != null || thing.TryGetComp<CompCrystalBattery>() != null || thing.TryGetComp<CompArtifactHost>() != null || thing.TryGetComp<CompPsychicPylon>() != null))
				{
					yield return thing;
				}
			}
		}
	}

	public string MissingMaterial()
	{
		RitualSchema ritualSchema = drawn;
		if (ritualSchema == null || !parent.Spawned)
		{
			return null;
		}
		RitualReading reading = Reading;
		if (!reading.Has(RuneKind.Transmute) || reading.Seal == null)
		{
			return null;
		}
		Map map = parent.Map;
		ThingDef target = reading.Seal;
		List<Thing> list = (from t in FuelCells(ComponentKind.Material).Distinct().SelectMany((IntVec3 c) => c.GetThingList(map))
			where t.def.category == ThingCategory.Item
			select t).ToList();
		List<RitualComponent> list2 = ritualSchema.components.Where((RitualComponent c) => c.kind == ComponentKind.Material && c.thing != null && c.thing != target && Transmutation.Allowed(c.thing)).ToList();
		if (list2.Count == 0)
		{
			if (!list.Any((Thing t) => t.def != target && Transmutation.Allowed(t.def) && Transmutation.Materials.Contains(t.def)))
			{
				return "nothing to transmute lies in the circle - lay a metal or jade in it, or add a material circle";
			}
			return null;
		}
		foreach (IGrouping<ThingDef, RitualComponent> g in from m in list2
			group m by m.thing)
		{
			int num = g.Sum((RitualComponent m) => Mathf.Max(1, m.amount));
			int num2 = list.Where((Thing t) => t.def == g.Key).Sum((Thing t) => t.stackCount);
			if (num2 < num)
			{
				return g.Key.label + " " + num2 + "/" + num + " lies in the circle - the haulers bring it to the material circles";
			}
		}
		return null;
	}

	public string BeginChosen()
	{
		if (chosenThing != null)
		{
			string text = CannotPerform(null) ?? CannotSustain(selfRunning: false);
			if (text != null)
			{
				return text;
			}
			if (!ConduitCandidates().Contains(chosenThing))
			{
				return "the chosen conduit is no longer at the centre";
			}
			Begin(null, target);
			conduit = chosenThing;
			return null;
		}
		if (chosenPerformer != null)
		{
			string text2 = CannotPerform(chosenPerformer);
			if (text2 != null)
			{
				return text2;
			}
			if (chosenPerformer.Downed || chosenPerformer.Dead || !chosenPerformer.Spawned || chosenPerformer.Map != parent.Map)
			{
				return chosenPerformer.LabelShort + " cannot come";
			}
			Prepare(chosenPerformer);
			return null;
		}
		return "choose a conduit first";
	}

	public string CannotSustain(bool selfRunning = true)
	{
		if (!Drawn)
		{
			return "nothing drawn";
		}
		if (selfRunning && !engraved)
		{
			return "only an engraving sustains itself - chalk needs someone to begin it";
		}
		RitualReading reading = Reading;
		if (reading.trap)
		{
			return "a Trap holds by itself already";
		}
		if (reading.RawFlow || reading.Has(RuneKind.Release) || reading.Has(RuneKind.Reforge) || reading.Has(RuneKind.Transfer) || reading.Has(RuneKind.Blood) || reading.Has(RuneKind.Shroud))
		{
			return "raw stream, release, reforging, transfer and awakening need a living performer";
		}
		if (reading.verbs.Count == 0 && !reading.Dome)
		{
			return "no verb";
		}
		return null;
	}

	private bool Needed(RitualReading r)
	{
		Map map = parent.Map;
		if (r.Dome)
		{
			return GenHostility.AnyHostileActiveThreatToPlayer(map);
		}
		float reach = Reach(r);
		IntVec3 at = Center;
		IEnumerable<Thing> source = GenRadial.RadialDistinctThingsAround(at, map, reach, useCenter: true).Concat(Threaded);
		foreach (RuneDef verb in r.verbs)
		{
			switch (verb.kind)
			{
			case RuneKind.Fill:
				if (ChargeTargets(r).Any((ArtifactState st) => st.charge < 0.5f))
				{
					return true;
				}
				if (source.Any(delegate(Thing t)
				{
					CompCrystalBattery compCrystalBattery = t.TryGetComp<CompCrystalBattery>();
					return compCrystalBattery != null && compCrystalBattery.Stored < CompCrystalBattery.Capacity * 0.5f;
				}))
				{
					return true;
				}
				break;
			case RuneKind.Hold:
			case RuneKind.Drain:
				if (PawnsInReach(r).Any((Pawn q) => q.HostileTo(Faction.OfPlayer) && !q.Downed))
				{
					return true;
				}
				if (verb.kind == RuneKind.Drain && CompPsychicPylon.All.Any((CompPsychicPylon py) => py.parent.Map == map && py.charge > 50f && (py.parent.Position - at).LengthHorizontal <= reach))
				{
					return true;
				}
				break;
			case RuneKind.Growth:
				if (GenRadial.RadialCellsAround(at, reach, useCenter: true).Any(delegate(IntVec3 c)
				{
					if (c.InBounds(map))
					{
						Plant plant = c.GetPlant(map);
						if (plant != null)
						{
							return plant.LifeStage == PlantLifeStage.Growing;
						}
					}
					return false;
				}))
				{
					return true;
				}
				break;
			case RuneKind.Lift:
				if (source.Any((Thing t) => t.def.category == ThingCategory.Item && t.def.EverHaulable))
				{
					return true;
				}
				break;
			case RuneKind.Transmute:
				return true;
			case RuneKind.Purge:
				if (PawnsInReach(r).Any((Pawn q) => q.Faction == parent.Faction && MindStates().Any((HediffDef d) => q.health.hediffSet.HasHediff(d))))
				{
					return true;
				}
				break;
			}
		}
		return false;
	}

	private void SelfSustainTick()
	{
		if (devDomeUntil > 0 && Find.TickManager.TicksGame > devDomeUntil && !Running)
		{
			DropDome();
			devDomeUntil = -1;
		}
		if (performer != null)
		{
			return;
		}
		if (conduit != null)
		{
			if (CannotSustain(selfRunning: false) != null)
			{
				End(null);
				return;
			}
			if (!conduit.Spawned || conduit.Map != parent.Map)
			{
				End("its conduit is gone.");
				return;
			}
			if (!Step(250))
			{
				conduit = null;
			}
			if (conduit != null && Reading.Dome && !GenHostility.AnyHostileActiveThreatToPlayer(parent.Map) && runTicks > 2500)
			{
				End(null);
			}
		}
		else
		{
			if (!selfSustain || CannotSustain() != null || !parent.IsHashIntervalTick(1250) || MissingMaterial() != null)
			{
				return;
			}
			RitualReading reading = Reading;
			if (Needed(reading))
			{
				Thing thing = FindConduit();
				if (thing != null)
				{
					Begin(null, target);
					conduit = thing;
				}
			}
		}
	}

	public static float GrabCap(RitualReading r, float flowRatio)
	{
		return 400f * (float)r.focus * Mathf.Max(0.25f, flowRatio);
	}

	private bool GrabXp(RitualReading r)
	{
		MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(parent.Map);
		float flowRatio = ((mapComponent_ExternalFlow == null) ? 0f : (mapComponent_ExternalFlow.Total / 375000f));
		float num = GrabCap(r, flowRatio);
		foreach (Thing item in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), useCenter: true).Concat(Threaded).ToList())
		{
			foreach (object item2 in Vessels(item))
			{
				grabbed.TryGetValue(item2, out var value);
				if (!(value >= num - 0.5f))
				{
					float num2 = Mathf.Min(num - value, 50f);
					float num3 = num2 / 10f;
					if (bank < num3)
					{
						return false;
					}
					bank -= num3;
					spentTotal += num3;
					SetXp(item2, Xp(item2) + num2);
					grabbed[item2] = value + num2;
					if (mapComponent_ExternalFlow != null)
					{
						mapComponent_ExternalFlow.deeds += num2 * 5f;
					}
					if (Rand.Chance(0.2f))
					{
						FleckMaker.ThrowLightningGlow(item.DrawPos, parent.Map, 0.6f);
					}
					return false;
				}
			}
		}
		return true;
	}

	private bool DrainLiving(RitualReading r, int delta)
	{
		bool result = false;
		foreach (Pawn item in PawnsInReach(r))
		{
			if (item.Faction == parent.Faction || item.Downed || !item.RaceProps.IsFlesh || held.Contains(item))
			{
				continue;
			}
			Need_Rest need_Rest = item.needs?.rest;
			if (need_Rest != null && !(need_Rest.CurLevel <= 0.01f) && !PsychicShelter.Shielded(item, drone: true))
			{
				result = true;
				float num = 0.0006f * (float)delta * Mathf.Max(0.2f, r.Power(drawQuality));
				need_Rest.CurLevel = Mathf.Max(0f, need_Rest.CurLevel - num);
				Gain(r, num * 120f * Mathf.Max(0.2f, item.GetStatValue(StatDefOf.PsychicSensitivity)));
				if (need_Rest.CurLevel <= 0.01f)
				{
					Collapse(item, item.LabelShortCap + " collapses: the ritual drew all its strength out.");
				}
				if (item.IsHashIntervalTick(90, delta))
				{
					FleckMaker.ThrowLightningGlow(item.DrawPos, parent.Map, 0.5f);
				}
			}
		}
		return result;
	}

	public static void Collapse(Pawn q, string msg)
	{
		HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StrengthDrained");
		if (namedSilentFail != null && !q.health.hediffSet.HasHediff(namedSilentFail))
		{
			q.health.AddHediff(namedSilentFail);
		}
		if (msg != null)
		{
			Messages.Message(msg, q, MessageTypeDefOf.PositiveEvent, historical: false);
		}
	}

	private bool DrainThreads(RitualReading r, int delta)
	{
		bool result = false;
		float want = 0.15f * (float)delta;
		foreach (Thing item in Threaded)
		{
			float num = TakeFrom(item, want);
			if (!(num <= 0f))
			{
				result = true;
				Gain(r, num);
			}
		}
		return result;
	}

	public static float TakeFrom(Thing t, float want)
	{
		CompEnergyKeeper compEnergyKeeper = t.TryGetComp<CompEnergyKeeper>();
		if (compEnergyKeeper != null)
		{
			return compEnergyKeeper.Take(want);
		}
		CompCrystalBattery compCrystalBattery = t.TryGetComp<CompCrystalBattery>();
		if (compCrystalBattery != null && compCrystalBattery.Stored > 0.01f)
		{
			float num = Mathf.Min(want, compCrystalBattery.Stored);
			compCrystalBattery.stored = compCrystalBattery.Stored - num;
			return num;
		}
		CompArtifactHost compArtifactHost = t.TryGetComp<CompArtifactHost>();
		if (compArtifactHost != null)
		{
			foreach (ArtifactState slot in compArtifactHost.slots)
			{
				if (slot != null && !(slot.charge <= 0.01f))
				{
					float num2 = Mathf.Max(1f, ArtifactCharge.Capacity(slot, null));
					float num3 = Mathf.Min(want, slot.charge * num2);
					slot.charge -= num3 / num2;
					return num3;
				}
			}
		}
		CompPowerBattery compPowerBattery = t.TryGetComp<CompPowerBattery>();
		if (compPowerBattery != null && compPowerBattery.StoredEnergy > 1f)
		{
			float num4 = Mathf.Min(want * 4.1666665f, compPowerBattery.StoredEnergy);
			compPowerBattery.DrawPower(num4);
			return num4 / 4.1666665f;
		}
		CompPsychicPylon compPsychicPylon = t.TryGetComp<CompPsychicPylon>();
		if (compPsychicPylon != null && compPsychicPylon.charge > 1f)
		{
			return compPsychicPylon.Take(want);
		}
		ArtifactState artifactState = t.TryGetComp<CompArtifact>()?.state;
		if (artifactState != null && artifactState.charge > 0.01f)
		{
			float num5 = Mathf.Max(1f, ArtifactCharge.Capacity(artifactState, null));
			float num6 = Mathf.Min(want, artifactState.charge * num5);
			artifactState.charge -= num6 / num5;
			return num6;
		}
		return 0f;
	}

	public static float GiveTo(Thing t, float amount)
	{
		CompCoreHearth compCoreHearth = t.TryGetComp<CompCoreHearth>();
		if (compCoreHearth != null && compCoreHearth.cracked)
		{
			return compCoreHearth.Mend(amount);
		}
		CompEnergyKeeper compEnergyKeeper = t.TryGetComp<CompEnergyKeeper>();
		if (compEnergyKeeper != null)
		{
			return compEnergyKeeper.Give(amount);
		}
		CompCrystalBattery compCrystalBattery = t.TryGetComp<CompCrystalBattery>();
		if (compCrystalBattery != null && !compCrystalBattery.IsFull)
		{
			float num = Mathf.Min(amount, CompCrystalBattery.Capacity - compCrystalBattery.Stored);
			compCrystalBattery.stored = compCrystalBattery.Stored + num;
			return num;
		}
		CompArtifactHost compArtifactHost = t.TryGetComp<CompArtifactHost>();
		if (compArtifactHost != null)
		{
			foreach (ArtifactState slot in compArtifactHost.slots)
			{
				if (slot != null && !(slot.charge >= 0.999f))
				{
					float num2 = Mathf.Max(1f, ArtifactCharge.Capacity(slot, null));
					float num3 = Mathf.Min(amount, (1f - slot.charge) * num2);
					slot.charge += num3 / num2;
					return num3;
				}
			}
		}
		CompPowerBattery compPowerBattery = t.TryGetComp<CompPowerBattery>();
		if (compPowerBattery != null && compPowerBattery.AmountCanAccept > 0.5f)
		{
			float num4 = Mathf.Min(amount * 4.1666665f, compPowerBattery.AmountCanAccept);
			compPowerBattery.AddEnergy(num4);
			return num4 / 4.1666665f;
		}
		return 0f;
	}

	private bool DoCurrent(RitualReading r, int delta)
	{
		HashSet<CompPowerBattery> hashSet = new HashSet<CompPowerBattery>();
		foreach (Thing item in Threaded)
		{
			PowerNet powerNet = item.TryGetComp<CompPower>()?.PowerNet;
			if (powerNet == null)
			{
				continue;
			}
			foreach (CompPowerBattery batteryComp in powerNet.batteryComps)
			{
				hashSet.Add(batteryComp);
			}
		}
		if (r.breadth > 0)
		{
			foreach (Thing item2 in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), useCenter: true))
			{
				CompPowerBattery compPowerBattery = item2.TryGetComp<CompPowerBattery>();
				if (compPowerBattery != null)
				{
					hashSet.Add(compPowerBattery);
				}
			}
		}
		List<CompPowerBattery> list = hashSet.Where((CompPowerBattery b) => b.AmountCanAccept > 0.5f).ToList();
		if (list.Count == 0)
		{
			return r.anchor == 0;
		}
		float num = Mathf.Min(bank, 0.1f * (float)delta * r.Power(drawQuality));
		if (num <= 0.0001f)
		{
			return false;
		}
		float num2 = num / (float)list.Count;
		foreach (CompPowerBattery item3 in list)
		{
			float num3 = Mathf.Min(num2 * 4.1666665f, item3.AmountCanAccept);
			item3.AddEnergy(num3);
			bank -= num3 / 4.1666665f;
			spentTotal += num3 / 4.1666665f;
		}
		if (parent.IsHashIntervalTick(120, delta))
		{
			FleckMaker.ThrowLightningGlow(list.RandomElement().parent.DrawPos, parent.Map, 0.6f);
		}
		return false;
	}

	private bool DoRelease(RitualReading r)
	{
		if (stepTicks < 900)
		{
			return false;
		}
		Map map = parent.Map;
		float num = bank;
		bank = 0f;
		spentTotal += num;
		IntVec3 center = Center;
		if (num < 20f)
		{
			FleckMaker.ThrowLightningGlow(center.ToVector3Shifted(), map, 1f);
			End("the release fizzled - too little energy.");
			return true;
		}
		MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(map);
		float num2 = ((mapComponent_ExternalFlow == null) ? 0f : (mapComponent_ExternalFlow.Total / 375000f));
		float num3 = 1f + 0.15f * num2;
		float radius = Mathf.Clamp(1.9f + Mathf.Sqrt(num * num3) / 4f, 1.9f, 4.9f + 2f * (float)Size + 2f * (float)r.breadth + Mathf.Min(14f, num2 * 0.6f));
		int num4 = Mathf.Clamp(Mathf.RoundToInt(12f + num * num3 / 10f), 12, Mathf.RoundToInt(150f + 450f * Mathf.Clamp01(num2 / 12f)));
		bool num5 = center == parent.Position;
		Pawn pawn = performer;
		if (num5 && r.barrier == 0 && r.safety > 0 && pawn != null)
		{
			IntVec3 position = CellFinder.RandomClosewalkCellNear(parent.Position, map, Mathf.CeilToInt(radius) + 3, (IntVec3 c) => (c - parent.Position).LengthHorizontal > radius);
			pawn.Position = position;
			pawn.Notify_Teleported(endCurrentJob: true, resetTweenedPos: false);
		}
		List<Thing> list = ((r.barrier > 0) ? Spared(map, null) : new List<Thing> { parent });
		if (r.SelfDetonation)
		{
			radius *= 1.3f;
			num4 = Mathf.RoundToInt((float)num4 * 1.5f);
			list = null;
			RitualPresetDef namedSilentFail = DefDatabase<RitualPresetDef>.GetNamedSilentFail("Avir_Ritual_SelfDetonation");
			if (namedSilentFail != null)
			{
				RitualDiscoveries.Learn(namedSilentFail);
			}
		}
		if (r.Throw)
		{
			RitualFx.Wave(center.ToVector3Shifted(), map, radius);
			Throw(center, radius, num, list);
		}
		else
		{
			RitualFx.Blast(center.ToVector3Shifted(), map, radius);
			IntVec3 center2 = center;
			float radius2 = radius;
			DamageDef bomb = DamageDefOf.Bomb;
			int damAmount = num4;
			List<Thing> ignoredThings = list;
			GenExplosion.DoExplosion(center2, map, radius2, bomb, null, damAmount, -1f, null, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors: false, null, 0f, 1, 0f, damageFalloff: false, null, ignoredThings);
		}
		if (r.SelfDetonation)
		{
			drawn = null;
			drawQuality = 0f;
			End("the release went off in " + (pawn?.LabelShort ?? "its conduit") + "'s hands.");
			return true;
		}
		if (r.anchor == 0 && verbIndex >= r.verbs.Count - 1)
		{
			End("released.");
		}
		return true;
	}

	private void Throw(IntVec3 at, float radius, float e, List<Thing> ignored)
	{
		Map map = parent.Map;
		float num = Mathf.Clamp(e / 60f, 2f, 8f);
		FleckMaker.ThrowLightningGlow(at.ToVector3Shifted(), map, radius);
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (item.Dead || (ignored != null && ignored.Contains(item)))
			{
				continue;
			}
			float lengthHorizontal = (item.Position - at).LengthHorizontal;
			if (lengthHorizontal > radius)
			{
				continue;
			}
			Vector3 vector = (item.Position - at).ToVector3();
			if (vector.sqrMagnitude < 0.01f)
			{
				vector = Rand.InsideUnitCircleVec3;
			}
			vector.Normalize();
			int num2 = Mathf.RoundToInt(num * (1f - 0.5f * lengthHorizontal / radius));
			IntVec3 intVec = item.Position;
			for (int i = 1; i <= num2; i++)
			{
				IntVec3 intVec2 = (item.Position.ToVector3Shifted() + vector * i).ToIntVec3();
				if (!intVec2.InBounds(map) || !intVec2.Standable(map))
				{
					break;
				}
				intVec = intVec2;
			}
			if (intVec != item.Position)
			{
				item.Position = intVec;
				item.Notify_Teleported(endCurrentJob: false, resetTweenedPos: false);
			}
			item.stances?.stunner?.StunFor(150, null, addBattleLog: false);
			item.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Clamp(e / 40f, 2f, 25f)));
			FleckMaker.ThrowDustPuff(item.Position, map, 1.2f);
		}
	}

	private bool DoLift(RitualReading r, int delta)
	{
		if (stepTicks % 60 >= delta && stepTicks > delta)
		{
			return false;
		}
		Map map = parent.Map;
		float radius = Reach(r);
		IntVec3 center = Center;
		float num = Mathf.Max(0.3f, r.Power(drawQuality));
		bool flag = false;
		int num2 = Mathf.RoundToInt(6f * num);
		foreach (IntVec3 item in GenRadial.RadialCellsAround(center, radius, useCenter: true))
		{
			if (num2 <= 0)
			{
				break;
			}
			if (!item.InBounds(map))
			{
				continue;
			}
			if (map.snowGrid.GetDepth(item) > 0.01f)
			{
				if (bank < 0.5f)
				{
					return false;
				}
				map.snowGrid.SetDepth(item, 0f);
				bank -= 0.5f;
				spentTotal += 0.5f;
				num2--;
				flag = true;
			}
			Thing thing = item.GetThingList(map).FirstOrDefault((Thing t) => t is Filth);
			if (thing != null)
			{
				if (bank < 0.5f)
				{
					return false;
				}
				thing.Destroy();
				bank -= 0.5f;
				spentTotal += 0.5f;
				num2--;
				flag = true;
			}
		}
		float num3 = Mathf.Min(15f * num, bank / 0.1f);
		foreach (Thing item2 in GenRadial.RadialDistinctThingsAround(center, map, radius, useCenter: true).ToList())
		{
			if (num3 <= 0.5f)
			{
				break;
			}
			if (item2.def.category != ThingCategory.Item || !item2.def.EverHaulable || !item2.Spawned || item2.IsForbidden(Faction.OfPlayer) || map.reservationManager.IsReservedByAnyoneOf(item2, Faction.OfPlayer) || map.physicalInteractionReservationManager.IsReserved(item2) || RitualHold.Held(item2))
			{
				continue;
			}
			StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(item2);
			if (!StoreUtility.TryFindBestBetterStoreCellFor(item2, null, map, currentPriority, Faction.OfPlayer, out var foundCell))
			{
				continue;
			}
			float num4 = Mathf.Max(0.001f, item2.GetStatValue(StatDefOf.Mass));
			int num5 = Mathf.Min(item2.stackCount, Mathf.FloorToInt(num3 / num4));
			if (num5 > 0)
			{
				IntVec3 position = item2.Position;
				Thing thing2 = ((num5 >= item2.stackCount) ? item2 : item2.SplitOff(num5));
				if (thing2 == item2)
				{
					item2.DeSpawn();
				}
				if (!GenPlace.TryPlaceThing(thing2, foundCell, map, ThingPlaceMode.Direct) && !GenPlace.TryPlaceThing(thing2, foundCell, map, ThingPlaceMode.Near))
				{
					GenPlace.TryPlaceThing(thing2, position, map, ThingPlaceMode.Near);
					continue;
				}
				num3 -= (float)num5 * num4;
				bank -= (float)num5 * num4 * 0.1f;
				spentTotal += (float)num5 * num4 * 0.1f;
				flag = true;
				FleckMaker.ThrowDustPuff(position, map, 0.8f);
				FleckMaker.ThrowLightningGlow(foundCell.ToVector3Shifted(), map, 0.5f);
			}
		}
		if (!flag)
		{
			return r.anchor == 0;
		}
		return false;
	}

	private bool DoGrowth(RitualReading r, int delta)
	{
		if (stepTicks % 120 >= delta && stepTicks > delta)
		{
			return false;
		}
		Map map = parent.Map;
		float radius = Reach(r);
		IntVec3 center = Center;
		float num = Mathf.Max(0.3f, r.Power(drawQuality));
		bool flag = false;
		foreach (IntVec3 item in GenRadial.RadialCellsAround(center, radius, useCenter: true))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			Plant plant = item.GetPlant(map);
			if (plant != null && plant.LifeStage == PlantLifeStage.Growing && !(plant.Growth >= 0.999f))
			{
				if (bank < 0.2f)
				{
					return false;
				}
				plant.Growth = Mathf.Min(1f, plant.Growth + 0.02f * num);
				bank -= 0.2f;
				spentTotal += 0.2f;
				flag = true;
				if (Rand.Chance(0.05f))
				{
					FleckMaker.ThrowLightningGlow(item.ToVector3Shifted(), map, 0.4f);
				}
			}
		}
		foreach (Pawn item2 in PawnsInReach(r))
		{
			if (item2.Faction != parent.Faction && item2.HostFaction != parent.Faction)
			{
				continue;
			}
			foreach (Hediff_Injury item3 in (from h in item2.health.hediffSet.hediffs.OfType<Hediff_Injury>()
				where !h.IsPermanent()
				select h).ToList())
			{
				if (bank < 0.5f)
				{
					return false;
				}
				item3.Heal(0.6f * num);
				bank -= 0.5f;
				spentTotal += 0.5f;
				flag = true;
			}
		}
		if (r.anchor > 0 && Find.TickManager.TicksGame - lastSoilTick > 2500 && bank >= 30f)
		{
			IntVec3 c2 = (from c in GenRadial.RadialCellsAround(center, radius, useCenter: true)
				where c.InBounds(map) && c.GetTerrain(map) == TerrainDefOf.Soil
				select c).RandomElementWithFallback(IntVec3.Invalid);
			if (c2.IsValid)
			{
				map.terrainGrid.SetTerrain(c2, TerrainDefOf.SoilRich);
				bank -= 30f;
				spentTotal += 30f;
				flag = true;
				lastSoilTick = Find.TickManager.TicksGame;
			}
		}
		if (!flag)
		{
			return r.anchor == 0;
		}
		return false;
	}

	public IEnumerable<Thing> ReforgeCandidates()
	{
		if (parent.Map != null)
		{
			return from t in Cells.SelectMany((IntVec3 c) => c.GetThingList(parent.Map))
				where t.TryGetComp<CompArtifact>()?.state != null || CompStriderBow.Of(t)?.bonded != null
				select t;
		}
		return Enumerable.Empty<Thing>();
	}

	private void PickOnMap(Func<Thing, bool> can, Action<Thing> set, string ask)
	{
		Messages.Message(ask, MessageTypeDefOf.SilentInput, historical: false);
		TargetingParameters targetParams = new TargetingParameters
		{
			canTargetBuildings = true,
			canTargetItems = true,
			canTargetPawns = false,
			canTargetLocations = false,
			mapObjectTargetsMustBeAutoAttackable = false,
			validator = (TargetInfo ti) => ti.Thing != null && can(ti.Thing)
		};
		Find.Targeter.BeginTargeting(targetParams, delegate(LocalTargetInfo ti)
		{
			if (ti.Thing != null && can(ti.Thing))
			{
				set(ti.Thing);
				SoundDefOf.Click.PlayOneShotOnCamera();
			}
		});
	}

	private IEnumerable<Gizmo> ChoiceGizmos()
	{
		if (!Drawn || Running)
		{
			yield break;
		}
		if (ConduitCandidates().Any())
		{
			yield return new Command_Action
			{
				defaultLabel = ((chosenThing != null) ? ("Conduit: " + chosenThing.LabelShortCap) : "Choose conduit..."),
				defaultDesc = "Click the artifact, crystal battery, artifact building or pylon at the centre that will hold the ritual. Clicking this again while one is chosen clears the choice. A colonist chosen in the ritual book clears it too.",
				icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Focus")?.Glow ?? BaseContent.BadTex),
				action = delegate
				{
					if (chosenThing != null)
					{
						chosenThing = null;
					}
					else
					{
						PickOnMap((Thing t) => ConduitCandidates().Contains(t), delegate(Thing t)
						{
							chosenThing = t;
							chosenPerformer = null;
							if (reforgeThing == t)
							{
								reforgeThing = null;
							}
						}, "Click the thing at the centre that will hold the ritual.");
					}
				}
			};
		}
		if ((!Reading.Has(RuneKind.Reforge) && !Reading.Has(RuneKind.Infuse)) || !ReforgeCandidates().Any())
		{
			yield break;
		}
		yield return new Command_Action
		{
			defaultLabel = ((reforgeThing != null) ? ("Artifact: " + reforgeThing.LabelShortCap) : "Choose the artifact..."),
			defaultDesc = "Click the artifact in the circle that the ritual will reforge or infuse. Without a choice it takes the first artifact in the circle that is not the conduit. Clicking this again while one is chosen clears the choice.",
			icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Reforge")?.Ink ?? BaseContent.BadTex),
			action = delegate
			{
				if (reforgeThing != null)
				{
					reforgeThing = null;
				}
				else
				{
					PickOnMap((Thing t) => ReforgeCandidates().Contains(t) && t != chosenThing, delegate(Thing t)
					{
						reforgeThing = t;
					}, "Click the artifact to reforge or infuse.");
				}
			}
		};
	}

	private bool DoInfuse(RitualReading r, int delta)
	{
		Thing reforgeTarget = ReforgeTarget;
		ArtifactState artifactState = reforgeTarget?.TryGetComp<CompArtifact>()?.state;
		if (artifactState == null || !artifactState.HasHeart)
		{
			End("nothing to infuse: lay an artifact with a heart in the circle.");
			return true;
		}
		if (infusedPointsAtStart < 0)
		{
			infusedPointsAtStart = artifactState.PointsEarned;
		}
		int num = ((Size != RitualCircleSize.Large) ? 1 : 2);
		if (artifactState.PointsEarned - infusedPointsAtStart >= num)
		{
			Finish(reforgeTarget);
			return true;
		}
		if (stepTicks % 60 >= delta && stepTicks > delta)
		{
			return false;
		}
		float num2 = Mathf.Min(bank, 20f);
		if (num2 < 1f)
		{
			return false;
		}
		bank -= num2;
		spentTotal += num2;
		float num3 = num2 * Mathf.Max(0.3f, r.Power(drawQuality));
		artifactState.AddXp(num3);
		infusedXp += num3;
		if (Rand.Chance(0.2f))
		{
			FleckMaker.ThrowLightningGlow(reforgeTarget.DrawPos, parent.Map, 0.6f);
		}
		if (artifactState.PointsEarned - infusedPointsAtStart >= num)
		{
			Finish(reforgeTarget);
			return true;
		}
		return false;
	}

	private void Finish(Thing t)
	{
		Messages.Message(t.LabelShortCap + " is infused: +" + infusedXp.ToString("0") + " experience.", t, MessageTypeDefOf.PositiveEvent, historical: false);
		infusedXp = 0f;
		infusedPointsAtStart = -1;
		ArtifactBonusCache.ClearAll();
	}

	public static float ReforgeCost(ArtifactState st, int mode)
	{
		return mode switch
		{
			0 => 500f * (float)(int)(st.heartQuality + 1), 
			1 => 1500f, 
			_ => 1000f, 
		};
	}

	public float ReforgeScoop(RitualReading r)
	{
		MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(parent.Map);
		float b = ((mapComponent_ExternalFlow == null) ? 0f : (mapComponent_ExternalFlow.Total / 375000f));
		return 1500f * (1f + (float)r.focus + 0.5f * (float)r.amplify) * Mathf.Max(0.25f, b) * Mathf.Max(0.3f, drawQuality);
	}

	public static ArtifactMetalDef MetalOf(ThingDef seal)
	{
		if (seal != null)
		{
			return DefDatabase<ArtifactMetalDef>.AllDefsListForReading.FirstOrDefault((ArtifactMetalDef m) => m.material == seal);
		}
		return null;
	}

	public string CannotReforge(ArtifactState st, int mode, RitualReading r)
	{
		if (st == null)
		{
			return "lay an artifact in the circle";
		}
		switch (mode)
		{
		case 0:
			if ((int)st.heartQuality < 6)
			{
				return null;
			}
			return "its heart is already legendary";
		case 1:
		{
			ArtifactMetalDef artifactMetalDef = MetalOf(r.Seal);
			if (artifactMetalDef == null)
			{
				return "a seal of the new metal must lie in a component circle";
			}
			if (artifactMetalDef != st.heartMetal)
			{
				return null;
			}
			return "its heart is already " + artifactMetalDef.label;
		}
		case 2:
			return null;
		default:
			return null;
		}
	}

	public static string ReforgeLabel(int mode, RitualReading r)
	{
		return mode switch
		{
			0 => "Heart one quality step up", 
			1 => "Heart into " + (MetalOf(r.Seal)?.label ?? "the sealed metal"), 
			_ => "Reforge the spiral: every cell closes, every point returns, and it scoops experience out of the stream", 
		};
	}

	private bool DoReforge(RitualReading r)
	{
		Thing reforgeTarget = ReforgeTarget;
		CompStriderBow compStriderBow = CompStriderBow.Of(reforgeTarget);
		List<Thing> ignoredThings;
		if (compStriderBow?.bonded != null)
		{
			if (bank < 1000f)
			{
				return false;
			}
			bank -= 1000f;
			spentTotal += 1000f;
			string labelShort = compStriderBow.bonded.LabelShort;
			compStriderBow.Unbond();
			IntVec3 position = reforgeTarget.Position;
			Map map = parent.Map;
			DamageDef stun = DamageDefOf.Stun;
			ignoredThings = new List<Thing> { reforgeTarget, parent };
			GenExplosion.DoExplosion(position, map, 1.9f, stun, null, 1, -1f, null, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors: false, null, 0f, 1, 0f, damageFalloff: false, null, ignoredThings);
			Messages.Message(reforgeTarget.LabelShortCap + " is reforged: its bond with " + labelShort + " is broken, and it will take a new archer.", reforgeTarget, MessageTypeDefOf.PositiveEvent, historical: false);
			End("the bond is broken.");
			return true;
		}
		ArtifactState artifactState = reforgeTarget?.TryGetComp<CompArtifact>()?.state;
		string text = CannotReforge(artifactState, reforgeMode, r);
		if (text != null)
		{
			End("cannot reforge: " + text + ".");
			return true;
		}
		float num = ReforgeCost(artifactState, reforgeMode);
		if (bank < num)
		{
			return false;
		}
		bank -= num;
		spentTotal += num;
		switch (reforgeMode)
		{
		case 0:
			artifactState.heartQuality++;
			break;
		case 1:
			artifactState.heartMetal = MetalOf(r.Seal);
			break;
		case 2:
		{
			artifactState.opened.Clear();
			artifactState.dormantArms.Clear();
			artifactState.burntPoints = 0;
			float num2 = ReforgeScoop(r);
			artifactState.xpEarned += num2;
			MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(parent.Map);
			if (mapComponent_ExternalFlow != null)
			{
				mapComponent_ExternalFlow.deeds += num2 * 5f;
			}
			break;
		}
		}
		ArtifactBonusCache.ClearAll();
		IntVec3 position2 = reforgeTarget.Position;
		Map map2 = parent.Map;
		DamageDef stun2 = DamageDefOf.Stun;
		ignoredThings = new List<Thing> { reforgeTarget, parent };
		GenExplosion.DoExplosion(position2, map2, 1.9f, stun2, null, 1, -1f, null, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors: false, null, 0f, 1, 0f, damageFalloff: false, null, ignoredThings);
		Messages.Message(reforgeTarget.LabelShortCap + " is reforged: " + ReforgeLabel(reforgeMode, r).ToLower() + ".", reforgeTarget, MessageTypeDefOf.PositiveEvent, historical: false);
		End("the artifact is reforged.");
		return true;
	}

	private bool DomeTick(RitualReading r, MapComponent_ExternalFlow ext, int delta)
	{
		if (runTicks >= 60000)
		{
			End("the anchor's day is over - the dome fades.");
			return false;
		}
		float domeRadius = r.DomeRadius;
		float num = 150f * Mathf.Max(0.3f, r.Power(drawQuality));
		if (dome == null)
		{
			dome = new BarrierDome
			{
				map = parent.Map,
				center = Center,
				radius = domeRadius,
				hit = DomeHit
			};
			BarrierDome.All.Add(dome);
			domeHp = Mathf.Min(num, bank);
			bank -= domeHp;
		}
		float num2 = 0.004f * domeRadius * (float)delta;
		if (bank >= num2)
		{
			bank -= num2;
			spentTotal += num2;
			dryTicks = 0;
		}
		else if ((dryTicks += delta) > 600)
		{
			End("the dome ran dry and faded.");
			return false;
		}
		if (domeHp < num && bank > 1f)
		{
			float num3 = Mathf.Min(num - domeHp, Mathf.Min(bank, 0.5f * (float)delta));
			domeHp += num3;
			bank -= num3;
			spentTotal += num3;
		}
		if (ext != null)
		{
			ext.deeds += 15f * domeRadius * (float)delta / 60f;
		}
		return true;
	}

	private bool DomeHit(Projectile pr)
	{
		float num = Mathf.Max(5f, pr.DamageAmount);
		domeHp -= num;
		if (domeHp > 0f)
		{
			return true;
		}
		Messages.Message("The barrier dome shattered.", parent, MessageTypeDefOf.NegativeEvent, historical: false);
		End("the dome shattered.");
		return true;
	}

	private void DropDome()
	{
		if (dome != null)
		{
			BarrierDome.All.Remove(dome);
			dome = null;
		}
	}

	private void PulseTick(Pawn q)
	{
		if (pulseCooldown > 0)
		{
			pulseCooldown--;
			return;
		}
		Map map = parent.Map;
		Faction f2 = parent.Faction ?? Faction.OfPlayer;
		List<Pawn> list = (from t in map.attackTargetsCache.TargetsHostileToFaction(f2)
			select t.Thing as Pawn into f
			where f != null && f.Spawned && !f.Dead && !f.Downed && !held.Contains(f) && (f.Position - parent.Position).LengthHorizontal <= 18f
			select f).ToList();
		if (list.Count == 0)
		{
			return;
		}
		pulseCooldown = 3;
		pulsesTaken.TryGetValue(q, out var value);
		float num = Mathf.Max(0.2f, q.GetStatValue(StatDefOf.PsychicSensitivity));
		float num2 = (6f + 4f * num) * Mathf.Max(0.3f, drawQuality);
		RitualFx.Wave(parent.DrawPos, map, 9f);
		foreach (Pawn item in list)
		{
			if (item.RaceProps.IsMechanoid)
			{
				item.TakeDamage(new DamageInfo(DamageDefOf.EMP, num2 * 2f, 0f, -1f, q));
			}
			else
			{
				item.TakeDamage(new DamageInfo(DamageDefOf.Burn, num2, 0f, -1f, q));
				item.stances?.stunner?.StunFor(Mathf.RoundToInt(60f + 12f * num2), q, addBattleLog: false);
			}
			RitualFx.Ripple(item.DrawPos, map, 1.2f);
		}
		MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(map);
		if (mapComponent_ExternalFlow != null)
		{
			mapComponent_ExternalFlow.deeds += 300f * num2;
		}
		value++;
		pulsesTaken[q] = value;
		if (q.needs?.rest != null)
		{
			q.needs.rest.CurLevel = Mathf.Max(0f, q.needs.rest.CurLevel - 0.1f);
		}
		if (value >= 9)
		{
			if (q.needs?.rest != null)
			{
				q.needs.rest.CurLevel = 0f;
			}
			Collapse(q, null);
			Release(q, q.LabelShortCap + " is spent: nine tenths of its strength thrown at its own side, it is dropped, drained, and the Trap opens.");
		}
	}

	private void StartVortex()
	{
		vortex = 1f;
		Pawn pawn = performer;
		End("the stream found nothing to fill and nothing to close it: an endless vortex.");
		Arlia.Say("spinning");
		Find.LetterStack.ReceiveLetter("Endless vortex", "The ritual at " + parent.LabelShort + " was meant to fill, but nothing could be filled and nothing closed the circle. The stream of the map pours in and has nowhere to go: it spins, and grows. It lifts dust and things, it calls the storm, it draws the strength of everyone who comes near.\n\nIt is also a pump and a source: the map's stream keeps rising while it spins, and whatever is bound to the circle by threads - crystal batteries, artifact buildings, electric batteries - is filled from it, the more the larger it has grown.\n\nIt holds as long as the drawing does: wash it away, draw another ritual over it, or erase it.", LetterDefOf.ThreatBig, parent);
		RitualPresetDef namedSilentFail = DefDatabase<RitualPresetDef>.GetNamedSilentFail("Avir_Ritual_EndlessVortex");
		if (namedSilentFail != null)
		{
			RitualDiscoveries.Learn(namedSilentFail);
		}
		pawn?.jobs?.EndCurrentJob(JobCondition.InterruptForced);
	}

	private void VortexTick()
	{
		if (vortex <= 0f)
		{
			return;
		}
		Map map = parent.Map;
		if (!Drawn)
		{
			vortex = 0f;
			Messages.Message("The endless vortex is broken.", parent, MessageTypeDefOf.PositiveEvent, historical: false);
			return;
		}
		vortex = Mathf.Min(10f, vortex + 0.02f);
		float num = Radius + 2f + vortex;
		MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(map);
		if (mapComponent_ExternalFlow != null)
		{
			mapComponent_ExternalFlow.deeds += 3000f * vortex;
		}
		foreach (Thing item in Threaded.ToList())
		{
			GiveTo(item, 2f * vortex);
		}
		for (int i = 0; i < 2; i++)
		{
			RitualFx.Mote(parent.DrawPos + Rand.InsideUnitCircleVec3 * num, map, Rand.Range(0.4f, 0.9f));
		}
		for (int j = 0; j < 3; j++)
		{
			FleckMaker.ThrowDustPuff(parent.Position + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(num))], map, 1.5f);
		}
		Thing thing = (from t in GenRadial.RadialDistinctThingsAround(parent.Position, map, num, useCenter: true)
			where t.def.category == ThingCategory.Item && t.def.EverHaulable && t.Spawned
			select t).RandomElementWithFallback();
		if (thing != null && Rand.Chance(0.5f) && CellFinder.TryFindRandomCellNear(parent.Position, map, Mathf.CeilToInt(num + 3f), (IntVec3 c) => c.Standable(map), out var result))
		{
			thing.DeSpawn();
			GenPlace.TryPlaceThing(thing, result, map, ThingPlaceMode.Near);
		}
		foreach (Pawn item2 in map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (!item2.Dead && !((item2.Position - parent.Position).LengthHorizontal > num) && item2.needs?.rest != null)
			{
				item2.needs.rest.CurLevel = Mathf.Max(0f, item2.needs.rest.CurLevel - 0.01f * vortex);
			}
		}
		if (vortex >= 3f && Rand.Chance(0.15f + 0.03f * vortex) && CellFinder.TryFindRandomCellNear(parent.Position, map, Mathf.CeilToInt(num + 6f), (IntVec3 c) => !c.Roofed(map) && (c - parent.Position).LengthHorizontal > Radius + 1f, out var result2))
		{
			map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, result2));
		}
		if (vortex >= 5f && !(map.weatherManager.curWeather?.defName ?? "").Contains("Thunderstorm"))
		{
			WeatherDef namedSilentFail = DefDatabase<WeatherDef>.GetNamedSilentFail("RainyThunderstorm");
			if (namedSilentFail != null)
			{
				map.weatherManager.TransitionTo(namedSilentFail);
			}
		}
	}

	public void Erase()
	{
		if (Running)
		{
			End("the chalk is erased.");
		}
		drawn = null;
		drawQuality = 0f;
		armed = false;
		vortex = 0f;
		engraved = false;
		selfSustain = false;
		chiselPending = false;
		chiselWork = 0f;
		ReleaseAll("the chalk is erased");
		Messages.Message("The ritual circle is erased.", parent, MessageTypeDefOf.NeutralEvent, historical: false);
	}

	private void TrapVariantTick(Pawn q, RitualReading r)
	{
		MapComponent_ExternalFlow mapComponent_ExternalFlow = MapComponent_ExternalFlow.Of(parent.Map);
		switch (r.trapKind)
		{
		case TrapKind.Incomplete:
			if (mapComponent_ExternalFlow != null && mapComponent_ExternalFlow.Total > 1f)
			{
				Sustain(q);
				mapComponent_ExternalFlow.deeds += 500f;
			}
			break;
		case TrapKind.Feeding:
		{
			float num3 = 0f;
			foreach (Thing item in Threaded)
			{
				num3 += TakeFrom(item, 1f - num3);
				if (num3 >= 0.999f)
				{
					break;
				}
			}
			if (num3 >= 0.999f)
			{
				Sustain(q);
			}
			break;
		}
		case TrapKind.Pulse:
			PulseTick(q);
			break;
		case TrapKind.Milking:
		{
			Need_Rest need_Rest = q.needs?.rest;
			if (need_Rest == null)
			{
				break;
			}
			float num = ((r.focus > 0) ? 0.004f : 0.012f);
			need_Rest.CurLevel = Mathf.Max(0f, need_Rest.CurLevel - num);
			if (r.focus > 0 && mapComponent_ExternalFlow != null)
			{
				mapComponent_ExternalFlow.deeds += 800f;
			}
			float num2 = 1.5f * Mathf.Max(0.2f, q.GetStatValue(StatDefOf.PsychicSensitivity)) * Mathf.Max(0.2f, drawQuality);
			foreach (Thing item2 in Threaded)
			{
				num2 -= GiveTo(item2, num2);
				if (num2 <= 0.001f)
				{
					break;
				}
			}
			if (need_Rest.CurLevel <= 0.01f && r.focus == 0)
			{
				Collapse(q, null);
				Release(q, q.LabelShortCap + " is milked dry and collapses: the Trap opens - it can be taken.");
			}
			break;
		}
		}
	}

	private static void Sustain(Pawn q)
	{
		if (q.needs?.food != null)
		{
			q.needs.food.CurLevel = Mathf.Max(q.needs.food.CurLevel, q.needs.food.MaxLevel * 0.5f);
		}
		if (q.needs?.rest != null)
		{
			q.needs.rest.CurLevel = Mathf.Max(q.needs.rest.CurLevel, 0.5f);
		}
	}

	private IEnumerable<Gizmo> MoreGizmos()
	{
		foreach (Gizmo item in ChoiceGizmos())
		{
			yield return item;
		}
		if (Drawn && (Reading.thread > 0 || vortex > 0f))
		{
			Command_Action command_Action = new Command_Action();
			command_Action.defaultLabel = "Threads (" + threads.Count((Thing t) => t?.Spawned ?? false) + "/" + MaxThreads + ")...";
			command_Action.defaultDesc = "Bind things anywhere on the map to the circle by thin grey threads: crystal batteries, artifact buildings, psychic pylons, electric batteries and grids. A filling ritual reaches them; drawing out takes from them; a Trap feeds or milks along them; transmutation into current charges their grids. Right-click ends.";
			command_Action.icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Thread")?.Ink ?? BaseContent.BadTex;
			command_Action.action = BindThread;
			Command_Action command_Action2 = command_Action;
			if (threads.Count((Thing t) => t?.Spawned ?? false) >= MaxThreads)
			{
				command_Action2.Disable("every thread is bound - cut them to bind others");
			}
			yield return command_Action2;
			if (threads.Count > 0)
			{
				yield return new Command_Action
				{
					defaultLabel = "Cut threads",
					defaultDesc = "Cut every thread of this circle.",
					icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Thread")?.Glow ?? BaseContent.BadTex),
					action = delegate
					{
						threads.Clear();
					}
				};
			}
		}
		if (Drawn && engraved)
		{
			string text = CannotSustain();
			Command_Toggle command_Toggle = new Command_Toggle
			{
				defaultLabel = "Self-sustaining",
				defaultDesc = "The engraving wakes by itself whenever there is work for it - artifacts or batteries to fill, foes to hold or drain, plants to grow, a threat for the dome - through a conduit at its centre: an artifact, a crystal battery, an artifact building or a psychic pylon. Energy comes from the conduit, crystals and feathers in the circle, threads, colonists standing in it, and focus runes. A thing holds the stream with " + 0.6f.ToStringPercent() + " composure; a backlash burns it.\n\nConduit now: " + (FindConduit()?.LabelShort ?? "none"),
				icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Anchor")?.Ink ?? BaseContent.BadTex),
				isActive = () => selfSustain,
				toggleAction = delegate
				{
					selfSustain = !selfSustain;
					if (!selfSustain && conduit != null)
					{
						End("no longer self-sustaining.");
					}
				}
			};
			if (text != null)
			{
				command_Toggle.Disable(text);
			}
			yield return command_Toggle;
		}
		if (drawn != null)
		{
			yield return new Command_Toggle
			{
				defaultLabel = ((!engraved) ? ((vortex > 0f) ? "Break the vortex" : "Erase the chalk") : ((vortex > 0f) ? "Level the engraving (vortex)" : "Level the engraving")),
				defaultDesc = (engraved ? "An engraving cannot be broken or attacked - only levelled away, like rough ground: a constructor's work, about half of what it took to cut." : ("Chalk is wiped away like any dirt: whoever cleans does it, quickly." + ((vortex > 0f) ? " Whoever comes near the vortex is drained, mind." : ""))) + " Click again to call it off; the work done is kept." + (chiselPending ? ("\n\nProgress: " + (chiselWork / ChiselWorkNeeded).ToStringPercent()) : ""),
				icon = (DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Purge")?.Ink ?? BaseContent.BadTex),
				isActive = () => chiselPending,
				toggleAction = delegate
				{
					chiselPending = !chiselPending;
				}
			};
		}
	}

	public static bool Threadable(Thing t)
	{
		if (t != null)
		{
			if (t.TryGetComp<CompCrystalBattery>() == null && t.TryGetComp<CompEnergyKeeper>() == null && t.TryGetComp<CompArtifactHost>() == null && t.TryGetComp<CompPsychicPylon>() == null)
			{
				return t.TryGetComp<CompPower>() != null;
			}
			return true;
		}
		return false;
	}

	private void BindThread()
	{
		TargetingParameters targetParams = new TargetingParameters
		{
			canTargetBuildings = true,
			canTargetItems = true,
			canTargetPawns = false,
			canTargetLocations = false,
			mapObjectTargetsMustBeAutoAttackable = false,
			validator = (TargetInfo ti) => Threadable(ti.Thing)
		};
		Find.Targeter.BeginTargeting(targetParams, delegate(LocalTargetInfo ti)
		{
			if (Threadable(ti.Thing) && !threads.Contains(ti.Thing))
			{
				threads.RemoveAll((Thing x) => x == null || !x.Spawned);
				threads.Add(ti.Thing);
				if (threads.Count < MaxThreads)
				{
					BindThread();
				}
			}
		}, null, null, null, null, null, playSoundOnAction: true, null, null);
	}

	private void Prepare(Pawn p)
	{
		string text = MissingMaterial();
		if (text != null)
		{
			Messages.Message("Cannot begin: " + text + ".", parent, MessageTypeDefOf.RejectInput, historical: false);
			return;
		}
		RitualReading r = Reading;
		Action<IntVec3> next = delegate(IntVec3 at)
		{
			if (r.Has(RuneKind.Transfer))
			{
				TransferMenu(p, at);
			}
			else if (r.Has(RuneKind.Reforge))
			{
				ReforgeMenu(p, at);
			}
			else if (IsPath(r))
			{
				PathMenu(p, at);
			}
			else if (r.Has(RuneKind.Blood) || r.Has(RuneKind.Shroud))
			{
				AwakenMenu(p, at);
			}
			else
			{
				Order(p, at, null, null, 0);
			}
		};
		if (r.reach == 0 && (!r.Dome || r.reach <= 0))
		{
			next(IntVec3.Invalid);
			return;
		}
		float range = r.ReachRange;
		Messages.Message("Choose the point the ritual reaches - anywhere on the map.", MessageTypeDefOf.SilentInput, historical: false);
		TargetingParameters targetParams = new TargetingParameters
		{
			canTargetLocations = true,
			canTargetPawns = true,
			canTargetBuildings = true,
			canTargetItems = true
		};
		Find.Targeter.BeginTargeting(targetParams, delegate(LocalTargetInfo ti)
		{
			next(ti.Cell);
		}, null, (LocalTargetInfo ti) => ti.Cell.InBounds(parent.Map) && (ti.Cell - parent.Position).LengthHorizontal <= range, null, null, null, playSoundOnAction: true, null, delegate(LocalTargetInfo ti)
		{
			if (range < GenRadial.MaxRadialPatternRadius)
			{
				GenDraw.DrawRadiusRing(parent.Position, range);
			}
			if (ti.IsValid && Reach(r) < GenRadial.MaxRadialPatternRadius)
			{
				GenDraw.DrawRadiusRing(ti.Cell, r.Dome ? Mathf.Min(r.DomeRadius, GenRadial.MaxRadialPatternRadius - 1f) : Reach(r));
			}
		});
	}

	private void ReforgeMenu(Pawn p, IntVec3 at)
	{
		RitualReading reading = Reading;
		Thing reforgeTarget = ReforgeTarget;
		ArtifactState artifactState = reforgeTarget?.TryGetComp<CompArtifact>()?.state;
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		CompStriderBow compStriderBow = CompStriderBow.Of(reforgeTarget);
		if (compStriderBow?.bonded != null)
		{
			list.Add(new FloatMenuOption("Break the bond of " + reforgeTarget.LabelShort + " with " + compStriderBow.bonded.LabelShort + " (" + 1000f.ToString("0") + " energy)", delegate
			{
				Order(p, at, null, null, 0);
			}));
		}
		else if (artifactState == null)
		{
			list.Add(new FloatMenuOption("Lay an artifact in the circle first", null));
		}
		else
		{
			for (int i = 0; i <= 2; i++)
			{
				int mm = i;
				string text = CannotReforge(artifactState, i, reading);
				string label = ReforgeLabel(i, reading) + " (" + ReforgeCost(artifactState, i).ToString("0") + " energy)" + ((text != null) ? (" - " + text) : "");
				list.Add(new FloatMenuOption(label, (text != null) ? null : ((Action)delegate
				{
					Order(p, at, null, null, mm);
				})));
			}
		}
		Find.WindowStack.Add(new FloatMenu(list));
	}

	private void DrawMore()
	{
		Vector3 drawPos = parent.DrawPos;
		drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
		if (Find.Selector.IsSelected(parent))
		{
			foreach (Thing item in Threaded)
			{
				Vector3 drawPos2 = item.DrawPos;
				drawPos2.y = drawPos.y;
				if (!threads.Contains(item))
				{
					Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawPos2, Quaternion.identity, new Vector3(1.3f, 1f, 1.3f)), NetMarkMat, 0);
				}
				else if (FlowTex.Thread == null)
				{
					GenDraw.DrawLineBetween(drawPos, drawPos2, ThreadMat, 0.06f);
				}
				else
				{
					DrawThread(drawPos, drawPos2);
				}
			}
		}
		if (dome != null)
		{
			Vector3 pos = dome.center.ToVector3Shifted();
			pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
			float value = 0.55f + 0.12f * Mathf.Sin((float)Find.TickManager.TicksGame / 30f) + 0.3f * (1f - Mathf.Clamp01(domeHp / 150f));
			Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(dome.radius * 2f, 1f, dome.radius * 2f)), MaterialPool.MatFrom(FlowTex.Dome ?? RitualTex.Ring, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, Mathf.Clamp01(value))), 0);
		}
		if (vortex > 0f)
		{
			Vector3 drawPos3 = parent.DrawPos;
			drawPos3.y = AltitudeLayer.MoteOverhead.AltitudeFor();
			float num = (Radius + 2f + vortex) * 2f;
			float angle = (float)Find.TickManager.TicksGame * 2f % 360f;
			Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawPos3, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(num, 1f, num)), (FlowTex.Vortex != null) ? MaterialPool.MatFrom(FlowTex.Vortex, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, Mathf.Clamp01(0.45f + 0.05f * vortex))) : MaterialPool.MatFrom(RitualOrnament.CoreGlow, ShaderDatabase.Transparent, new Color(0.7f, 0.5f, 1f, 0.35f)), 0);
		}
	}

	private static void DrawThread(Vector3 a, Vector3 b)
	{
		Vector3 forward = b - a;
		forward.y = 0f;
		float magnitude = forward.magnitude;
		if (!(magnitude < 0.1f))
		{
			Quaternion q = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 90f, 0f);
			Material material = MaterialPool.MatFrom(FlowTex.Thread, ShaderDatabase.MoteGlow, new Color(1f, 1f, 1f, 0.8f));
			int num = Mathf.CeilToInt(magnitude / 4f);
			float num2 = magnitude / (float)num;
			for (int i = 0; i < num; i++)
			{
				Vector3 pos = a + forward.normalized * (num2 * ((float)i + 0.5f));
				Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, q, new Vector3(num2, 1f, 0.25f)), material, 0);
			}
		}
	}

	public override void PostDrawExtraSelectionOverlays()
	{
		base.PostDrawExtraSelectionOverlays();
		if (!Drawn)
		{
			return;
		}
		RitualReading reading = Reading;
		if (chosenThing != null && chosenThing.Spawned)
		{
			GenDraw.DrawLineBetween(parent.DrawPos, chosenThing.DrawPos, SimpleColor.Yellow);
		}
		if (reforgeThing != null && reforgeThing.Spawned)
		{
			GenDraw.DrawLineBetween(parent.DrawPos, reforgeThing.DrawPos, SimpleColor.Magenta);
		}
		if (Running && reading.reach > 0 && target.IsValid)
		{
			GenDraw.DrawLineBetween(parent.DrawPos, target.ToVector3Shifted(), SimpleColor.White);
			float num = (reading.Dome ? reading.DomeRadius : Reach(reading));
			if (num < GenRadial.MaxRadialPatternRadius)
			{
				GenDraw.DrawRadiusRing(target, num);
			}
		}
	}

	private void ExposeMore()
	{
		Scribe_Values.Look(ref target, "target", IntVec3.Invalid);
		Scribe_References.Look(ref chosenPerformer, "chosenPerformer");
		Scribe_References.Look(ref chosenThing, "chosenThing");
		Scribe_References.Look(ref reforgeThing, "reforgeThing");
		Scribe_Values.Look(ref stepTicks, "stepTicks", 0);
		Scribe_Values.Look(ref reforgeMode, "reforgeMode", 0);
		Scribe_Values.Look(ref vortex, "vortex", 0f);
		Scribe_Values.Look(ref domeHp, "domeHp", 0f);
		Scribe_Values.Look(ref lastSoilTick, "lastSoilTick", -99999);
		ExposeAwakening();
		Scribe_Collections.Look(ref threads, "threads", LookMode.Reference);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (threads == null)
			{
				threads = new List<Thing>();
			}
			threads.RemoveAll((Thing x) => x == null);
		}
	}

	public static bool IsPath(RitualReading r)
	{
		if (r.Has(RuneKind.Infuse))
		{
			return r.elements.Count > 0;
		}
		return false;
	}

	public static string PathCircle(RitualReading r, RitualCircleSize size)
	{
		if (r.elements.Distinct().Count() > 1)
		{
			return "one element only - a path is one";
		}
		if (SpellCircle.GreatElement(r.elements[0]) && size == RitualCircleSize.Small)
		{
			return "a great element needs a medium circle";
		}
		return null;
	}

	public static float PathSizeFactor(RuneDef el, RitualCircleSize size)
	{
		int num = (SpellCircle.GreatElement(el) ? 1 : 0);
		return 0.8f + 0.25f * (float)Mathf.Max(0, (int)(size - num));
	}

	private void PathMenu(Pawn p, IntVec3 at)
	{
		RuneDef runeDef = Reading.elements[0];
		List<FloatMenuOption> list = new List<FloatMenuOption>();
		List<Pawn> list2 = new List<Pawn>();
		list2.Add(p);
		list2.AddRange(parent.Map.mapPawns.FreeColonistsSpawned.Where((Pawn q) => q != p && Inside(q.Position)));
		foreach (Pawn item in list2)
		{
			Pawn qq = item;
			string text = MagePath.CannotTake(item);
			string text2 = "Give " + item.LabelShort + " the path of " + runeDef.label + ((item == p) ? " (the performer)" : "");
			list.Add(new FloatMenuOption((text == null) ? text2 : (text2 + " - " + text), (text != null) ? null : ((Action)delegate
			{
				Order(p, at, null, qq, 0);
			})));
		}
		list.Add(new FloatMenuOption("(The caster, if not the performer, must stand in the circle)", null));
		Find.WindowStack.Add(new FloatMenu(list));
	}

	private bool DoPath(RitualReading r, int delta)
	{
		Pawn awakenTarget = AwakenTarget;
		RuneDef runeDef = r.elements[0];
		if (awakenTarget == null || awakenTarget.Dead || MagePath.CannotTake(awakenTarget) != null)
		{
			End("the caster is gone, or already has a path.");
			return true;
		}
		if (!Pours(awakenTarget, delta, 300f, runeDef.color))
		{
			return false;
		}
		float share = Mathf.Clamp01(drawQuality * r.Efficiency * (1f + 0.3f * (float)r.amplify) * PathSizeFactor(runeDef, Size));
		int num = MagePath.Grant(awakenTarget, runeDef, share);
		ResetAwakening();
		FleckMaker.ThrowLightningGlow(awakenTarget.DrawPos, parent.Map, 2f);
		Messages.Message(awakenTarget.LabelShortCap + " walks the path of " + runeDef.label + " now. " + ((num > 0) ? (num + " of its spells are written into " + awakenTarget.LabelShort + "'s book.") : "Its spells are not made yet."), awakenTarget, MessageTypeDefOf.PositiveEvent, historical: false);
		return true;
	}
}
