using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Avir.Linarian
{
    // =====================================================================================
    // HER OWN PLEASURES. A Singer is wilder than the rest of her people: she takes no joy in what others do for fun,
    // only in her own - the Avir's own games, chess, the beasts set circling, a walk in a beast's shape across the land,
    // and warming up her aria under her breath (those who hear it are the better for it).
    // =====================================================================================
    public static class SingerJoy
    {
        // what a creature of the wild will not stoop to: screens, cards, billiards, horseshoes and consoles
        private static readonly HashSet<string> refused = new HashSet<string>
        {
            "WatchTelevision", "Play_Poker", "Play_Billiards", "Play_Horseshoes"
        };

        public static bool Allowed(JoyGiverDef d)
        {
            if (d == null) return false;
            if (refused.Contains(d.defName)) return false;
            if (d.thingDefs != null && d.thingDefs.Any(t => t.techLevel >= TechLevel.Industrial)) return false;
            return true;
        }
    }

    /// <summary>Whatever kind of fun is offered, a Singer only takes her own.</summary>
    [HarmonyPatch]
    public static class Patch_SingerJoyOnlyHerOwn
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(JoyGiver), nameof(JoyGiver.CanBeGivenTo));
            foreach (System.Type t in typeof(JoyGiver).AllSubclassesNonAbstract())
            {
                MethodInfo m = t.GetMethod(nameof(JoyGiver.CanBeGivenTo), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m != null) yield return m;
            }
        }

        public static void Postfix(JoyGiver __instance, Pawn pawn, ref bool __result)
        {
            if (__result && pawn != null && Linarian.Is(pawn) && !SingerJoy.Allowed(__instance.def)) __result = false;
        }
    }

    public class JoyGiver_Singer : JoyGiver
    {
        public override bool CanBeGivenTo(Pawn pawn) => Linarian.Is(pawn) && pawn.Spawned && !pawn.Downed && base.CanBeGivenTo(pawn);

        public override Job TryGiveJob(Pawn pawn)
        {
            if (!Linarian.Is(pawn) || !pawn.Spawned) return null;
            Map map = pawn.Map;
            switch (def.defName)
            {
                case "Avir_SingerBeastWalk":
                    if (!CellFinder.TryFindRandomReachableNearbyCell(pawn.Position, map, 40f, TraverseParms.For(pawn),
                            c => c.Standable(map) && !c.IsForbidden(pawn) && (c - pawn.Position).LengthHorizontal > 18f && !c.Roofed(map), null, out IntVec3 far)) return null;
                    return JobMaker.MakeJob(def.jobDef, far);
                case "Avir_SingerWhirl":
                    if (!map.mapPawns.AllPawnsSpawned.Any(a => a.RaceProps.Animal && !a.Dead && a.MentalStateDef == null && (a.Position - pawn.Position).LengthHorizontal < 50f)) return null;
                    return JobMaker.MakeJob(def.jobDef, pawn.Position);
                default:
                    return JobMaker.MakeJob(def.jobDef, pawn.Position);
            }
        }
    }

    /// <summary>A walk across the land in a beast's shape - any of hers, for free: it is play, not the aria.</summary>
    public class JobDriver_SingerBeastWalk : JobDriver
    {
        private bool shaped;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref shaped, "shaped");
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(_ => { if (shaped) LinarianVeil.ClearForm(pawn); });
            Toil shape = ToilMaker.MakeToil("SingerTakesShape");
            shape.initAction = () =>
            {
                if (LinarianVeil.InForm(pawn) != null) return;
                var forms = new List<string> { LinarianTactics.Doe, LinarianTactics.Crow };
                Gene_LinarianSong g = Linarian.Song(pawn);
                if (g != null && LinarianTactics.Unlocked(g, LinarianTactics.Wolf)) forms.Add(LinarianTactics.Wolf);
                LinarianVeil.SetForm(pawn, forms.RandomElement());
                shaped = true;
            };
            shape.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return shape;
            Toil go = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            go.tickIntervalAction = delta => JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.None, 1f);
            yield return go;
            Toil roam = ToilMaker.MakeToil("SingerRoams");
            roam.defaultCompleteMode = ToilCompleteMode.Delay;
            roam.defaultDuration = job.def.joyDuration > 0 ? job.def.joyDuration / 2 : 1500;
            roam.tickIntervalAction = delta =>
            {
                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.EndJob, 1f);
                if (!pawn.pather.Moving && pawn.IsHashIntervalTick(300) && CellFinder.TryFindRandomReachableNearbyCell(pawn.Position, pawn.Map, 8f, TraverseParms.For(pawn), c => c.Standable(pawn.Map), null, out IntVec3 c2))
                    pawn.pather.StartPath(c2, PathEndMode.OnCell);
            };
            yield return roam;
        }
    }

    /// <summary>Warming up the aria under her breath: small notes in the air, and those near hear something lovely.</summary>
    public class JobDriver_SingerAriaWarmup : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil sing = ToilMaker.MakeToil("SingerWarmsUp");
            sing.defaultCompleteMode = ToilCompleteMode.Delay;
            sing.defaultDuration = job.def.joyDuration > 0 ? job.def.joyDuration : 2000;
            FleckDef note = DefDatabase<FleckDef>.GetNamedSilentFail("Music");
            ThoughtDef heard = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_HeardHerAria");
            sing.tickIntervalAction = delta =>
            {
                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.EndJob, 1f);
                if (pawn.IsHashIntervalTick(140) && note != null) FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, note);
                if (pawn.IsHashIntervalTick(600) && heard != null)
                    foreach (Pawn q in pawn.Map.mapPawns.AllPawnsSpawned.Where(q => q != pawn && q.RaceProps.Humanlike && q.Awake() && q.needs?.mood != null
                                 && (q.Position - pawn.Position).LengthHorizontal <= 10f && GenSight.LineOfSight(pawn.Position, q.Position, pawn.Map)).ToList())
                        q.needs.mood.thoughts.memories.TryGainMemory(heard);
            };
            yield return sing;
        }
    }

    /// <summary>Setting the beasts circling, for the pleasure of it.</summary>
    public class JobDriver_SingerWhirl : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil start = ToilMaker.MakeToil("SingerStartsWhirl");
            start.initAction = () =>
            {
                Gene_LinarianSong g = Linarian.Song(pawn);
                float r = g != null ? Mathf.Min(g.ZoneRadius, 50f) : 30f;
                if (LinarianManners.Circle(pawn, pawn.Map, pawn.Position, r) == 0) EndJobWith(JobCondition.Incompletable);
            };
            start.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return start;
            Toil watch = ToilMaker.MakeToil("SingerWatchesWhirl");
            watch.defaultCompleteMode = ToilCompleteMode.Delay;
            watch.defaultDuration = job.def.joyDuration > 0 ? job.def.joyDuration : 1500;
            watch.tickIntervalAction = delta => JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.EndJob, 1.5f);
            yield return watch;
        }
    }

    /// <summary>Those who see the beasts circling: the faithful are enraptured, outsiders frightened.</summary>
    public static class WhirlWitnesses
    {
        public static void Look(Pawn beast, Pawn her)
        {
            Map map = beast.Map;
            if (map == null) return;
            ThoughtDef rapt = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_SawTheWhirl");
            ThoughtDef fear = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_UnnervedByWhirl");
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned.Where(q => q.RaceProps.Humanlike && q.Awake() && q.needs?.mood != null && !Linarian.Is(q)
                         && (q.Position - beast.Position).LengthHorizontal <= 16f && GenSight.LineOfSight(q.Position, beast.Position, map)).ToList())
            {
                bool faithful = WildGods.Worships(q) || WildGods.GoddessOf(q) != null;
                if (faithful && rapt != null) q.needs.mood.thoughts.memories.TryGainMemory(rapt);
                else if (!faithful && q.Faction != Faction.OfPlayer && fear != null) q.needs.mood.thoughts.memories.TryGainMemory(fear);
            }
        }
    }

    // ---------------------------------------------------------------- kneeling, all together

    /// <summary>Kneeling to the wild goddess: the whole colony gathers at the fire at once. The free kneel and are hers;
    /// slaves come with them but keep their own faith.</summary>
    public class LordJob_KneelGathering : LordJob_Joinable_Party
    {
        private Pawn goddess;

        public LordJob_KneelGathering() { }

        public LordJob_KneelGathering(IntVec3 spot, Pawn organizer, GatheringDef def, Pawn goddess) : base(spot, organizer, def)
        {
            this.goddess = goddess;
            durationTicks = GenDate.TicksPerHour * 2;
        }

        protected override ThoughtDef AttendeeThought => DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_KneltTogether") ?? base.AttendeeThought;
        protected override ThoughtDef OrganizerThought => AttendeeThought;

        public override StateGraph CreateGraph()
        {
            StateGraph g = base.CreateGraph();
            // only a gathering seen to its end takes them: one called off or broken up takes no one
            foreach (Transition t in g.transitions) if (t.triggers.Contains(timeoutTrigger)) t.AddPreAction(new TransitionAction_Custom(Outcome));
            return g;
        }

        private bool done;

        private void Outcome()
        {
            if (done) return;
            done = true;
            Pawn her = goddess;
            if (her == null || her.Dead) her = WildGods.Present(Map);
            if (her == null) return;
            int knelt = 0;
            foreach (Pawn p in lord.ownedPawns.ToList())
            {
                if (p.IsSlave || p.IsPrisoner || !p.RaceProps.Humanlike || Linarian.Is(p)) continue;
                if ((p.Position - spot).LengthHorizontal > 12f) continue;
                WildGods.Accept(p, her, false);
                knelt++;
            }
            if (knelt > 0)
            {
                Linarian.Song(her)?.Learn(300f * knelt);   // every knee bent makes her stronger at once
                Messages.Message(knelt + " knelt to the wild goddess at the fire.", new TargetInfo(spot, Map), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref goddess, "goddess");
            Scribe_Values.Look(ref done, "done");
        }
    }

    public static class KneelGathering
    {
        public static bool CanStart(Map map) => map.lordManager.lords.All(l => !(l.LordJob is LordJob_KneelGathering));

        public static void Start(Pawn organizer, Thing fire, Pawn her)
        {
            Map map = organizer.Map;
            GatheringDef def = DefDatabase<GatheringDef>.GetNamedSilentFail("Avir_KneelGathering") ?? GatheringDefOf.Party;
            var lj = new LordJob_KneelGathering(fire.Position, organizer, def, her);
            List<Pawn> all = map.mapPawns.FreeColonistsAndPrisonersSpawned.Where(p => !p.IsPrisoner && !p.Downed && !p.Drafted && !p.InMentalState && p.Awake()
                && p.CanReach(fire, PathEndMode.Touch, Danger.Some)).ToList();
            if (!all.Contains(organizer)) all.Add(organizer);
            LordMaker.MakeNewLord(Faction.OfPlayer, lj, map, all);
            Messages.Message("The colony gathers at the fire to kneel to the wild goddess.", fire, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
