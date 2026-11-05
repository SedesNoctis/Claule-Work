using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Avir.Linarian
{
    /// <summary>
    /// THE DREAMING, reworked for the player's Singer.
    ///   Into the dream: she marks a mind as hers (ten days). Awake it is what it always was; asleep, it rises -
    ///   speechless, still resting - and walks under the player's hand until woken (a blow, or rested out). Each sleep
    ///   by chance against the mind: or it does not rise, but lies awake in a sleepless wretchedness instead. While it
    ///   walks it is still of its own people and no one turns on it - unless it is made to attack.
    ///   Every sleepwalker of hers walks at 80% speed and goes on resting.
    ///   Night procession (the night verdict reborn): two casts - the sleepers of one zone rise and walk to another,
    ///   breaking through whatever stands in the way, fall on any foe they meet there and lie down to sleep again. The
    ///   gift: she sees every sleeper on the map, through walls, in violet.
    ///   Sleeping retinue: the sleepers of a zone rise and follow her pointing, as long as any of them walks: shambling
    ///   about the point, falling on foes that come; nothing wakes them but full rest, and they rest at a fifth the speed.
    ///   While it lasts, pointing and marking minds into the dream cost her nothing.
    /// </summary>
    public static class LinarianDream
    {
        public static HediffDef Walker => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Dreamwalker");
        public const float SleepGainPer250 = 0.0125f;

        public static void IntoTheDream(Pawn p, Pawn q)
        {
            if (q == null) return;
            Hediff h = LinarianSource.Add(q, LinarianHediffs.Avir_DreamMark, p, GenDate.TicksPerDay * 10);
            var c = h?.TryGetComp<HediffComp_DreamControl>();
            if (c != null) { c.control = p.Faction == Faction.OfPlayer; c.rolled = false; }
            LinarianTactics.Notify(p, q.LabelShortCap + " dreams of her now. Asleep, they will be hers.");
            // asleep already (her own lullaby too): the dream takes them at once
            if (c != null && c.control && (LinarianTactics.Asleep(q) || Walking(q))) { c.rolled = true; Roll(q, p); }
        }

        /// <summary>The dream's chance against the mind: it rises hers (out of her aria-sleep too), or lies sleepless.</summary>
        public static void Roll(Pawn q, Pawn her)
        {
            Gene_LinarianSong g = Linarian.Song(her);
            if (her == null || g == null || her.Faction != Faction.OfPlayer || q.Dead) return;
            if (!Rand.Chance(LinarianTactics.Chance(her, g, q, true, "D"))) { if (!Walking(q)) Insomnia(q); return; }
            if (q.MentalState is MentalState_Song walking) walking.RecoverFromState();   // the dream outranks any other walking
            Rise(q);
            if (q.Downed) return;
            TakeControl(q, her);
        }

        /// <summary>Walking asleep under one of her other songs (sleepwalking, the procession, the retinue).</summary>
        public static bool Walking(Pawn q) => q?.MentalState is MentalState_Song s && s.Walker;

        public static bool IsWalker(Pawn q) => Walker != null && q?.health?.hediffSet?.HasHediff(Walker) == true;

        public static HediffComp_Dreamwalker WalkerComp(Pawn q) => Walker == null ? null : q?.health?.hediffSet?.GetFirstHediffOfDef(Walker)?.TryGetComp<HediffComp_Dreamwalker>();

        public static void TakeControl(Pawn q, Pawn her)
        {
            if (Walker == null || q.Dead) return;
            Faction orig = q.Faction;
            q.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            Hediff h = LinarianHediffs.Add(q, Walker, -1, 1f, her);
            var c = h?.TryGetComp<HediffComp_Dreamwalker>();
            if (c != null) { c.origFaction = orig; c.provoked = false; }
            if (orig != Faction.OfPlayer) q.SetFaction(Faction.OfPlayer);
            Messages.Message(q.LabelShortCap + " rises in their sleep - and walks where you bid.", q, MessageTypeDefOf.PositiveEvent, false);
        }

        public static void Release(Pawn q)
        {
            if (Walker == null || q == null) return;
            Hediff h = q.health?.hediffSet?.GetFirstHediffOfDef(Walker);
            var c = h?.TryGetComp<HediffComp_Dreamwalker>();
            Faction orig = c?.origFaction;
            if (h != null) q.health.RemoveHediff(h);
            if (q.Dead) return;
            if (orig != null && orig != Faction.OfPlayer && q.Faction == Faction.OfPlayer) q.SetFaction(orig);
            if (q.drafter != null && q.Faction != Faction.OfPlayer) q.drafter.Drafted = false;
            LinarianRaid.Rejoin(q);
            if (q.Spawned) MoteMaker.ThrowText(q.DrawPos, q.Map, "awake", 3f);
        }

        public static void Insomnia(Pawn q)
        {
            RestUtility.WakeUp(q);
            q.mindState?.mentalStateHandler?.TryStartMentalState(MentalStateDefOf.Wander_Sad, "sleepless, hounded by a dream", true);
            ThoughtDef t = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_DreamInsomnia");
            if (t != null) q.needs?.mood?.thoughts?.memories?.TryGainMemory(t);
        }

        /// <summary>Any of her retinue still walking.</summary>
        public static bool RetinueActive(Pawn her) =>
            her?.Map != null && her.Map.mapPawns.AllPawnsSpawned.Any(q => q.MentalState is MentalState_SongRetinue r && r.herald == her);

        // ---------------------------------------------------------------- the two zones

        public static IEnumerable<Pawn> SleepersAt(Pawn her, IntVec3 at, float r) =>
            LinarianZone.Around(her.Map, at, r).Where(q => q != her && !q.Dead && q.Faction != Faction.OfPlayer && !q.RaceProps.IsMechanoid
                && (LinarianTactics.Asleep(q) || q.MentalState is MentalState_SongPack sw && sw.Walker && sw.herald == her) && !at.Fogged(her.Map)).ToList();

        public static void Procession(Pawn her, Gene_LinarianSong g, IntVec3 from, float r, IntVec3 to)
        {
            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_Procession");
            var group = SleepersAt(her, from, r).ToList();
            var ids = new HashSet<int>(group.Select(q => q.thingIDNumber));
            int n = 0;
            foreach (Pawn q in group)
            {
                Rise(q);
                if (q.Downed || !LinarianMental.Start(q, her, def, GenDate.TicksPerHour * 8)) continue;
                if (q.MentalState is MentalState_SongProcession s) { s.dest = to; s.spread = 5f; s.group = new List<int>(ids); }
                n++;
            }
            LinarianTactics.Notify(her, n > 0 ? n + " sleepers rise and walk where she points." : "No one sleeps there.");
        }

        public static void Retinue(Pawn her, Gene_LinarianSong g, IntVec3 from, float r, IntVec3 to)
        {
            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_Retinue");
            int n = 0;
            foreach (Pawn q in SleepersAt(her, from, r))
            {
                Rise(q);
                if (q.Downed || !LinarianMental.Start(q, her, def, GenDate.TicksPerDay * 5)) continue;
                if (q.MentalState is MentalState_SongRetinue s) s.dest = to;
                n++;
            }
            LinarianTactics.Notify(her, n > 0 ? n + " sleepers follow her as a retinue." : "No one sleeps there.");
        }

        public static void Point(Pawn her, IntVec3 to)
        {
            foreach (Pawn q in her.Map.mapPawns.AllPawnsSpawned.Where(q => q.MentalState is MentalState_SongRetinue r && r.herald == her).ToList())
                ((MentalState_SongRetinue)q.MentalState).dest = to;
            LinarianTactics.Notify(her, "her retinue turns where she points.");
        }

        /// <summary>Up out of her aria-sleep, still asleep: a sleepwalker.</summary>
        public static void Rise(Pawn q)
        {
            Hediff aria = q.health?.hediffSet?.GetFirstHediffOfDef(LinarianHediffs.Avir_SongSleep);
            if (aria != null) q.health.RemoveHediff(aria);
            HediffDef eternal = LinarianSleep.Eternal;
            Hediff e = eternal == null ? null : q.health?.hediffSet?.GetFirstHediffOfDef(eternal);
            if (e != null) q.health.RemoveHediff(e);
            RestUtility.WakeUp(q);
            q.GetLord()?.RemovePawn(q);
        }

        /// <summary>To the destination, through whatever stands in the way.</summary>
        public static Job Walk(Pawn q, IntVec3 to)
        {
            if (q.CanReach(to, PathEndMode.OnCell, Danger.Deadly))
            {
                Job go = JobMaker.MakeJob(JobDefOf.Goto, to);
                go.canBashDoors = true;
                go.canBashFences = true;
                return go;
            }
            using (PawnPath path = q.Map.pathFinder.FindPathNow(q.Position, to, TraverseParms.For(TraverseMode.PassAllDestroyableThings)))
            {
                if (path == null || !path.Found) return JobMaker.MakeJob(JobDefOf.Wait, 120);
                Thing wall = path.FirstBlockingBuilding(out IntVec3 before, q);
                if (wall == null) return JobMaker.MakeJob(JobDefOf.Goto, to);
                if ((q.Position - wall.Position).LengthHorizontal > 1.5f) return JobMaker.MakeJob(JobDefOf.Goto, before);
                return JobMaker.MakeJob(JobDefOf.AttackMelee, wall);
            }
        }
    }

    /// <summary>On her mark: each time the marked one falls asleep, it rises under the player's hand - or lies awake.</summary>
    public class HediffCompProperties_DreamControl : HediffCompProperties
    {
        public HediffCompProperties_DreamControl() { compClass = typeof(HediffComp_DreamControl); }
    }

    public class HediffComp_DreamControl : HediffComp
    {
        public bool control;
        public bool rolled;

        /// <summary>Marked by Into the dream (hers to take when asleep), or only her sleeper (walked for her once).</summary>
        public override string CompLabelInBracketsExtra => control ? "hers to take" : "her sleeper";

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref control, "control");
            Scribe_Values.Look(ref rolled, "rolled");
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (!control || !Pawn.IsHashIntervalTick(250, delta) || !Pawn.Spawned || Pawn.Dead) return;
            if (LinarianDream.IsWalker(Pawn) || Pawn.InMentalState && !LinarianDream.Walking(Pawn)) return;
            bool aria = LinarianSleep.SongAsleep(Pawn);
            bool sleeping = LinarianDream.Walking(Pawn) || LinarianTactics.Asleep(Pawn) && (aria || !Pawn.Downed);
            if (!sleeping) { rolled = false; return; }
            if (rolled) return;
            rolled = true;
            LinarianDream.Roll(Pawn, LinarianSource.Of(Pawn, parent.def));
        }
    }

    /// <summary>A sleepwalker of hers: slower (the hediff), still resting; one under the player's hand also remembers
    /// its own people and whether it was made to attack.</summary>
    public class HediffCompProperties_Dreamwalker : HediffCompProperties
    {
        public bool releaseWhenRested;
        public HediffCompProperties_Dreamwalker() { compClass = typeof(HediffComp_Dreamwalker); }
    }

    public class HediffComp_Dreamwalker : HediffComp
    {
        public Faction origFaction;
        public bool provoked;

        public HediffCompProperties_Dreamwalker Props => (HediffCompProperties_Dreamwalker)props;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref origFaction, "origFaction");
            Scribe_Values.Look(ref provoked, "provoked");
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (!Pawn.IsHashIntervalTick(250, delta) || Pawn.Dead) return;
            LinarianWalkerLook.Seen(Pawn);
            Need_Rest rest = Pawn.needs?.rest;
            if (rest == null) return;
            Pawn her = LinarianSource.Of(Pawn, parent.def);
            float f = Pawn.MentalState is MentalState_SongRetinue ? 0.2f : 1f;   // the retinue rests at a fifth the speed
            rest.CurLevel = Mathf.Min(1f, rest.CurLevel + LinarianDream.SleepGainPer250 * f);
            if (Props.releaseWhenRested && rest.CurLevelPercentage >= 0.99f) LinarianDream.Release(Pawn);
        }
    }

    /// <summary>The night procession: to the point, through walls and rock; fall on any foe there who was not one of
    /// them; then lie down and sleep.</summary>
    public class MentalState_SongProcession : MentalState_Song
    {
        public override bool Hers => true;
        public override bool Walker => true;

        public IntVec3 dest = IntVec3.Invalid;
        public float spread = 5f;
        public List<int> group = new List<int>();
        private IntVec3 spot = IntVec3.Invalid;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref dest, "dest", IntVec3.Invalid);
            Scribe_Values.Look(ref spread, "spread", 5f);
            Scribe_Collections.Look(ref group, "group", LookMode.Value);
            if (group == null) group = new List<int>();
        }

        public override void MentalStateTick(int delta)
        {
            if (Find.TickManager.TicksGame > until) { RecoverFromState(); return; }
            if (pawn.IsHashIntervalTick(30, delta)) Drive();
        }

        protected override void Drive()
        {
            if (!dest.IsValid) { RecoverFromState(); return; }
            LinarianWalkers.Keep(pawn, herald);
            Pawn foe = Foe();
            if (foe != null && (foe.Position - pawn.Position).LengthHorizontal <= 30f) { Force(AttackJob(foe)); return; }
            if (!spot.IsValid && !CellFinder.TryFindRandomCellNear(dest, pawn.Map, Mathf.RoundToInt(spread), c => c.Standable(pawn.Map), out spot)) spot = dest;
            if ((pawn.Position - spot).LengthHorizontal <= 1.5f)
            {
                RecoverFromState();
                LinarianHediffs.Sleep(pawn, GenDate.TicksPerHour * 4);
                return;
            }
            Force(LinarianDream.Walk(pawn, spot));
        }

        /// <summary>A foe of her people at the point who was not of the procession.</summary>
        private Pawn Foe() => pawn.Map.mapPawns.AllPawnsSpawned.Where(x => !x.Dead && !x.Downed && x != pawn && x != herald && !group.Contains(x.thingIDNumber)
                                  && x.HostileTo(Faction.OfPlayer) && Awake(x) && (x.Position - dest).LengthHorizontal <= spread + 3f)
                              .OrderBy(x => (x.Position - pawn.Position).LengthHorizontalSquared).FirstOrDefault();

        public override bool ForceHostileTo(Thing t) => provoked && t is Pawn x && !group.Contains(x.thingIDNumber) && x.HostileTo(Faction.OfPlayer) && Awake(x) && dest.IsValid && (x.Position - dest).LengthHorizontal <= spread + 3f;

        /// <summary>Only the active: not asleep, not of her cult.</summary>
        public static bool Awake(Pawn x) => !LinarianTactics.Asleep(x) && !LinarianCult.IsCultist(x);
    }

    /// <summary>The sleeping retinue: follow her pointing, shamble about the point, fall on foes that come; nothing wakes
    /// them but full rest.</summary>
    public class MentalState_SongRetinue : MentalState_Song
    {
        public override bool Hers => true;
        public override bool Walker => true;

        public IntVec3 dest = IntVec3.Invalid;
        private IntVec3 spot = IntVec3.Invalid;
        private IntVec3 aimed = IntVec3.Invalid;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref dest, "dest", IntVec3.Invalid);
        }

        public override void MentalStateTick(int delta)
        {
            if (herald == null || herald.Dead) { RecoverFromState(); return; }
            if (pawn.IsHashIntervalTick(250, delta) && (pawn.needs?.rest?.CurLevelPercentage ?? 0f) >= 0.99f) { RecoverFromState(); return; }
            if (pawn.IsHashIntervalTick(30, delta)) Drive();
        }

        protected override void Drive()
        {
            if (!dest.IsValid) dest = pawn.Position;
            LinarianWalkers.Keep(pawn, herald);
            Pawn foe = pawn.Map.mapPawns.AllPawnsSpawned.Where(x => !x.Dead && !x.Downed && x != pawn && x.HostileTo(Faction.OfPlayer) && MentalState_SongProcession.Awake(x)
                    && !(x.MentalState is MentalState_SongRetinue) && (x.Position - dest).LengthHorizontal <= 10f)
                .OrderBy(x => (x.Position - pawn.Position).LengthHorizontalSquared).FirstOrDefault();
            if (foe != null) { Force(AttackJob(foe)); return; }
            if (aimed != dest) { aimed = dest; spot = IntVec3.Invalid; }
            if (!spot.IsValid || (pawn.Position - spot).LengthHorizontal <= 1.5f)
                if (!CellFinder.TryFindRandomCellNear(dest, pawn.Map, 5, c => c.Standable(pawn.Map), out spot)) spot = dest;
            Job j = LinarianDream.Walk(pawn, spot);
            j.locomotionUrgency = LocomotionUrgency.Amble;   // shambling
            Force(j);
        }

        public override bool ForceHostileTo(Thing t) => provoked && t is Pawn x && x.HostileTo(Faction.OfPlayer) && !(x.MentalState is MentalState_SongRetinue);
    }

    public static class LinarianWalkers
    {
        /// <summary>Every sleepwalker of hers walks at 80% and feels no pain (the sleepwalking hediff).</summary>
        public static void Keep(Pawn q, Pawn her)
        {
            if (LinarianHediffs.Avir_Sleepwalking != null) LinarianHediffs.Add(q, LinarianHediffs.Avir_Sleepwalking, 300, 1f, her);
        }
    }

    /// <summary>Back into a raid: a raider who wakes from her sleep goes on with the raid - the one it was in if that is
    /// still an assault, else a new one of its own people on the map.</summary>
    public static class LinarianRaid
    {
        public static void Rejoin(Pawn q)
        {
            if (q == null || q.Dead || !q.Spawned || q.Downed || q.GetLord() != null || q.Faction == null || q.Faction == Faction.OfPlayer) return;
            if (!q.Faction.HostileTo(Faction.OfPlayer) || !q.RaceProps.Humanlike) return;
            Lord lord = q.Map.lordManager.lords.FirstOrDefault(l => l.faction == q.Faction && l.LordJob is LordJob_AssaultColony && l.ownedPawns.Count > 0);
            if (lord != null) { lord.AddPawn(q); return; }
            LordMaker.MakeNewLord(q.Faction, new LordJob_AssaultColony(q.Faction), q.Map, new List<Pawn> { q });
        }
    }

    /// <summary>Waking from her sleep (a blow, its end, or her lullaby sung again), a raider goes on with the raid.</summary>
    public class HediffCompProperties_SleepRejoin : HediffCompProperties
    {
        public HediffCompProperties_SleepRejoin() { compClass = typeof(HediffComp_SleepRejoin); }
    }

    public class HediffComp_SleepRejoin : HediffComp
    {
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Pawn q = Pawn;
            if (q != null && !q.Dead) LinarianRaid.Rejoin(q);
        }
    }

    // ---------------------------------------------------------------- the gift of the procession: sleepers in violet

    public class MapComponent_DreamSight : MapComponent
    {
        public MapComponent_DreamSight(Map map) : base(map) { }

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || RimWorld.Planet.WorldRendererUtility.WorldSelected) return;
            // marked prey's gift: everyone who carries her scars glows red to her
            if (MapComponent_LinarianSong.HeraldsOn(map).Any(h => h.Faction == Faction.OfPlayer && !h.Dead && Linarian.Song(h)?.Has("S2") == true))
                foreach (Pawn q in LinarianActs.Scarred(map))
                    if (q.Faction != Faction.OfPlayer) LinarianRivals.Silhouette(q, LinarianRivals.Red);
            if (!MapComponent_LinarianSong.HeraldsOn(map).Any(h => h.Faction == Faction.OfPlayer && !h.Dead && Linarian.Song(h)?.Has("D4") == true)) return;
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned)
                if (!q.Dead && !q.RaceProps.IsMechanoid && q.Faction != Faction.OfPlayer && LinarianTactics.Asleep(q))
                    LinarianRivals.Silhouette(q, LinarianRivals.Violet);
        }
    }

    // ---------------------------------------------------------------- patches

    /// <summary>One walking under the player's hand is no one's enemy - until it is made to attack.</summary>
    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
    public static class Patch_DreamwalkerPeace
    {
        public static void Postfix(Thing a, Thing b, ref bool __result)
        {
            if (!__result) return;
            if (a is Pawn pa && Calm(pa) || b is Pawn pb && Calm(pb)) __result = false;
        }

        public static bool Calm(Pawn p) => LinarianDream.WalkerComp(p) is HediffComp_Dreamwalker c && c.origFaction != null && !c.provoked;
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) })]
    public static class Patch_DreamwalkerPeaceFaction
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (__result && t is Pawn p && Patch_DreamwalkerPeace.Calm(p)) __result = false;
        }
    }

    /// <summary>Made to attack, it is fought back.</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_DreamwalkerProvoked
    {
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            if (newJob?.def == null || !(newJob.targetA.Thing is Pawn)) return;
            if (newJob.def != JobDefOf.AttackMelee && newJob.def != JobDefOf.AttackStatic && newJob.def != JobDefOf.UseVerbOnThing) return;
            var c = LinarianDream.WalkerComp(___pawn);
            if (c != null) c.provoked = true;
        }
    }

    /// <summary>A blow wakes one walking under the player's hand.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_DreamwalkerWakes
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt <= 0f || dinfo.Def == null || !dinfo.Def.harmsHealth) return;
            if (dinfo.Instigator != null && dinfo.Instigator == LinarianSource.Of(__instance, LinarianDream.Walker)) return;   // she may strike what is hers
            if (LinarianDream.WalkerComp(__instance)?.origFaction != null) LinarianDream.Release(__instance);
        }
    }
    /// <summary>Her sleepwalkers look like shamblers: the head hangs to one side (each its own angle), and the body sways
    /// as it walks.</summary>
    public static class LinarianWalkerLook
    {
        private static readonly Dictionary<int, int> seen = new Dictionary<int, int>();

        public static void Seen(Pawn p) => seen[p.thingIDNumber] = Find.TickManager.TicksGame;

        public static bool Walking(Pawn p) => p != null && seen.TryGetValue(p.thingIDNumber, out int t) && Find.TickManager.TicksGame - t < 400;
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker_Head), nameof(PawnRenderNodeWorker_Head.RotationFor))]
    public static class Patch_SleepwalkerHead
    {
        public static void Postfix(PawnDrawParms parms, ref Quaternion __result)
        {
            Pawn p = parms.pawn;
            if (p == null || p.Dead || !LinarianWalkerLook.Walking(p)) return;
            __result *= Quaternion.Euler(Vector3.up * Rand.RangeSeeded(-20f, 20f, p.thingIDNumber));
        }
    }

    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawPos), MethodType.Getter)]
    public static class Patch_SleepwalkerSway
    {
        public static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            if (___pawn == null || ___pawn.pather?.Moving != true || !LinarianWalkerLook.Walking(___pawn)) return;
            __result.x += Mathf.Sin(Time.realtimeSinceStartup * 4f + ___pawn.thingIDNumber) * 0.05f;
        }
    }

}
