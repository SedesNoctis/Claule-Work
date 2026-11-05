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
    /// THE CULT of the player's Singer, reworked.
    ///   A cultist is anyone whose adoration of a Singer of the player has passed its first stage (devoted, 0.25+). A
    ///   cultist never harms the player's people or her, never leaves the map while the cult holds, is drawn in her
    ///   colour (violet), and stays with its own faction - no enemy to its own until she orders the map guarded.
    ///   Sow adoration: one mind, a stream that grows by the hour (a second sowing doubles it; held 72 hours a sowing at
    ///   its height, then ebbing) - or, ticked, a whole crowd, each by chance, for a day and a half (the night lengthens it).
    ///   Gather the flock: her cultists guard the map's edges for 9-12 hours, then sleep; the gift: when she sleeps, some
    ///   of them ring her at 40 steps and fall on whoever comes; cultists eat less, and hungry they eat anything -
    ///   corpses, and a fallen fellow if it comes to that.
    ///   Ecstasy: those caught stand in a wide ring round the point, rapt - the worship grows, and foes slip into the cult;
    ///   the gift: cultists spread the cult.
    ///   Last order: one fervent cultist - or, ticked, all of them - falls on the nearest unsuspecting foe with a terrible
    ///   first blow and fights to the last; with no foe left, they die by their own hand. The gift: cultists feel no pain.
    ///   Conversion: if no one turns, the hunger is given back. The gift: the cult spreads faster.
    /// </summary>
    public static class LinarianCult
    {
        public const float Devoted = 0.25f, Fervent = 0.5f;
        private static readonly Color Violet = new Color(0.72f, 0.5f, 0.95f);

        public static Pawn SingerOf(Pawn q) => q?.health?.hediffSet == null ? null : LinarianSource.Of(q, LinarianHediffs.Avir_Adoration);

        /// <summary>Her cultist (devoted or more), she being the player's.</summary>
        public static bool IsCultist(Pawn q, out Pawn her)
        {
            her = null;
            if (q == null || q.Dead || q.Faction == Faction.OfPlayer || LinarianHediffs.Avir_Adoration == null) return false;
            Hediff h = q.health?.hediffSet?.GetFirstHediffOfDef(LinarianHediffs.Avir_Adoration);
            if (h == null || h.Severity < Devoted) return false;
            her = SingerOf(q);
            return her != null && !her.Dead && her.Faction == Faction.OfPlayer;
        }

        public static bool IsCultist(Pawn q) => IsCultist(q, out Pawn _);

        public static List<Pawn> Cultists(Pawn her, float min = Devoted) =>
            her.MapHeld == null ? new List<Pawn>() : her.MapHeld.mapPawns.AllPawnsSpawned
                .Where(q => !q.Dead && SingerOf(q) == her && LinarianActs.Worship(q) >= min && q.Faction != Faction.OfPlayer).ToList();

        // ---------------------------------------------------------------- sow adoration

        /// <summary>One mind: the stream. A second sowing on the same mind makes it flow twice as fast (no more).</summary>
        public static void SowOne(Pawn p, Gene_LinarianSong g, Pawn q)
        {
            if (q == null || !LinarianTactics.Takeable(q)) return;
            Hediff h = q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_Adoration);
            if (h != null && SingerOf(q) != p) { q.health.RemoveHediff(h); h = null; }
            if (h == null) h = LinarianSource.Add(q, LinarianHediffs.Avir_Adoration, p, -1, 0.05f);
            var c = h?.TryGetComp<HediffComp_Adoration>();
            if (c == null) return;
            c.stream = true;
            c.stacks = Mathf.Min(2, c.stacks + 1);
            c.peakUntil = 0;
            LinarianTactics.Notify(p, q.LabelShortCap + (c.stacks > 1 ? " sinks deeper into her worship." : " cannot stop thinking of her."));
        }

        /// <summary>A crowd: each by chance, more or less - a day and a half of worship, not a stream.</summary>
        public static void SowMany(Pawn p, Gene_LinarianSong g, IntVec3 at, float r)
        {
            int n = 0;
            foreach (Pawn q in LinarianZone.Around(p.Map, at, r).Where(q => q != p && q.RaceProps.Humanlike && q.Faction != p.Faction && LinarianTactics.Takeable(q)).ToList())
            {
                float chance = LinarianTactics.Chance(p, g, q, false, "C");
                if (!Rand.Chance(chance)) continue;
                Hediff h = q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_Adoration);
                if (h != null && SingerOf(q) != p) { q.health.RemoveHediff(h); h = null; }
                var c = h?.TryGetComp<HediffComp_Adoration>();
                if (c != null && c.stream) continue;   // the stream is its own; the crowd does not add to it
                float add = Mathf.Lerp(0.08f, 0.3f, chance) * Rand.Range(0.6f, 1.2f);
                if (h == null) { h = LinarianSource.Add(q, LinarianHediffs.Avir_Adoration, p, -1, add); c = h?.TryGetComp<HediffComp_Adoration>(); }
                else h.Severity = Mathf.Min(1f, h.Severity + add);
                if (c != null) c.massUntil = Find.TickManager.TicksGame + Mathf.RoundToInt(GenDate.TicksPerDay * 1.5f);
                n++;
            }
            FleckMaker.Static(at, p.Map, FleckDefOf.PsycastAreaEffect, r);
            LinarianTactics.Notify(p, n > 0 ? n + " in the crowd begin to adore her." : "The crowd does not hear her.");
        }

        // ---------------------------------------------------------------- the flock

        /// <summary>The flock guards the map: they walk its edges for 9-12 hours, then sleep.</summary>
        public static void GuardTheMap(Pawn p, Gene_LinarianSong g)
        {
            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_MapGuard");
            int ticks = Mathf.RoundToInt(GenDate.TicksPerHour * (9f + 3f * Mathf.Clamp01((g.Power - 0.5f) / 1.5f)));
            int n = 0;
            foreach (Pawn q in Cultists(p).Where(q => !q.Downed))
                if (LinarianMental.Start(q, p, def, ticks)) n++;
            LinarianTactics.Notify(p, n > 0 ? n + " of her cultists go to guard the edges of the land." : "She has no cultists here.");
        }

        /// <summary>Ecstasy: those caught stand in a wide ring round the point, rapt.</summary>
        public static void Ecstasy(Pawn p, Gene_LinarianSong g, IntVec3 at, float r)
        {
            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_Ecstasy");
            int n = 0;
            foreach (Pawn q in LinarianZone.Around(p.Map, at, r).Where(q => q != p && q.RaceProps.Humanlike && q.Faction != Faction.OfPlayer && !q.Downed).ToList())
            {
                bool mine = SingerOf(q) == p;
                if (!mine && !q.HostileTo(p) && !LinarianPackSiphon.Held(p, q)) continue;
                if (!mine && !Rand.Chance(LinarianTactics.Chance(p, g, q, false, "C"))) continue;
                if (!LinarianMental.Start(q, p, def, LinarianZone.Ticks(GenDate.TicksPerHour * 2f, g))) continue;
                if (q.MentalState is MentalState_SongEcstasy e) e.point = at;
                n++;
            }
            FleckMaker.Static(at, p.Map, FleckDefOf.PsycastAreaEffect, r);
            LinarianTactics.Notify(p, n > 0 ? n + " stand in a ring round the point, rapt." : "No one is caught by it.");
        }

        /// <summary>The last order: one fervent cultist, or all of them.</summary>
        public static void LastOrder(Pawn p, Gene_LinarianSong g, Pawn one, IntVec3 at = default, float r = 0f)
        {
            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_LastOrder");
            // one alone, or every fervent cultist round the point (keep a recruit out of it, send the rest)
            List<Pawn> who = one != null ? new List<Pawn> { one } : Cultists(p, Fervent).Where(q => (q.Position - at).LengthHorizontal <= r).ToList();
            int n = 0;
            foreach (Pawn q in who.Where(q => !q.Downed))
            {
                if (!AnyFoe(q, p) && (one == null || !Comrades(q).Any())) { Die(q); n++; continue; }   // nothing to fight: the order is carried out at once
                if (!LinarianMental.Start(q, p, def, GenDate.TicksPerDay)) { LinarianTactics.Notify(p, q.LabelShortCap + " does not hear the order now."); continue; }
                if (q.MentalState is MentalState_SongLastOrder o) { o.single = one != null; o.firstBlow = true; }
                n++;
            }
            LinarianTactics.Notify(p, n > 0 ? n + " carry out her last order." : "No cultist is fervent enough.");
        }

        public static bool AnyFoe(Pawn q, Pawn her) => Foes(q, her).Any();

        /// <summary>The foes of her people on the map who are not her cultists.</summary>
        public static IEnumerable<Pawn> Foes(Pawn q, Pawn her) =>
            q.Map.mapPawns.AllPawnsSpawned.Where(x => x != q && x != her && !x.Dead && !x.Downed && x.RaceProps.Humanlike
                && x.HostileTo(Faction.OfPlayer) && !IsCultist(x));

        /// <summary>The maniac's own: his comrades of his faction who are not of the cult.</summary>
        public static IEnumerable<Pawn> Comrades(Pawn q) =>
            q.Map.mapPawns.AllPawnsSpawned.Where(x => x != q && !x.Dead && !x.Downed && x.RaceProps.Humanlike && x.Faction != null && x.Faction == q.Faction && !IsCultist(x));

        public static void Die(Pawn q)
        {
            if (q.Spawned) FleckMaker.ThrowSmoke(q.DrawPos, q.Map, 1f);
            q.Kill(null, null);
            if (q.Corpse != null) Messages.Message(q.LabelShortCap + " took their own life at a Singer's word.", q.Corpse, MessageTypeDefOf.NegativeEvent, false);
        }

        // ---------------------------------------------------------------- every hour, on every map

        private static readonly Dictionary<int, int> wardRolled = new Dictionary<int, int>();

        public static void Hourly(Map map, int now)
        {
            foreach (Pawn her in MapComponent_LinarianSong.HeraldsOn(map).Where(h => h.Faction == Faction.OfPlayer && !h.Dead).ToList())
            {
                Gene_LinarianSong g = Linarian.Song(her);
                if (g == null) continue;
                List<Pawn> flock = Cultists(her);
                HediffDef fasting = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CultFasting");
                HediffDef numb = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CultNumb");
                foreach (Pawn q in flock)
                {
                    if (g.Has("C2") && fasting != null) LinarianHediffs.Add(q, fasting, GenDate.TicksPerHour * 2);
                    if (g.Has("C4") && numb != null) LinarianHediffs.Add(q, numb, GenDate.TicksPerHour * 2);
                    if (g.Has("C2")) Feed(q, flock);
                    if (g.Has("C3")) Spread(her, g, q);
                }
                // the gift of the flock: when she sleeps, some of them ring her
                if (g.Has("C2") && g.flockGuards && LinarianTactics.Asleep(her) && her.Spawned)
                {
                    if (!wardRolled.TryGetValue(her.thingIDNumber, out int t) || now - t > GenDate.TicksPerHour * 6)
                    {
                        wardRolled[her.thingIDNumber] = now;
                        MentalStateDef ward = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SleepWard");
                        foreach (Pawn q in flock.Where(q => !q.Downed && !q.InMentalState))
                            if (Rand.Chance(LinarianTactics.Chance(her, g, q, false, "C"))) LinarianMental.Start(q, her, ward, GenDate.TicksPerHour * 8);
                    }
                }
            }
        }

        /// <summary>Her cultists leave their raid for her flock (a group of their own faction round her) - at once, not
        /// on the hour: no wandering off, no guests.</summary>
        public static void Tick(Map map, int now)
        {
            if (now % 250 != 0) return;
            foreach (Pawn her in MapComponent_LinarianSong.HeraldsOn(map).Where(h => h.Faction == Faction.OfPlayer && !h.Dead).ToList())
                foreach (Pawn q in Cultists(her).Where(q => q.Spawned && q.Faction != null))
                    Enlist(her, q);
        }

        public static void Enlist(Pawn her, Pawn q)
        {
            Lord cur = q.GetLord();
            if (cur?.LordJob is LordJob_SingerFlock f && f.her == her) return;
            cur?.RemovePawn(q);
            Lord flock = q.Map.lordManager.lords.FirstOrDefault(l => l.faction == q.Faction && l.LordJob is LordJob_SingerFlock lf && lf.her == her);
            if (flock != null) flock.AddPawn(q);
            else LordMaker.MakeNewLord(q.Faction, new LordJob_SingerFlock(her), q.Map, new List<Pawn> { q });
        }

        /// <summary>One whose worship faded goes back to their own: the raid, if their people are at war with hers; else home.</summary>
        public static void Release(Lord lord, Pawn p)
        {
            lord.RemovePawn(p);
            if (p.Dead || !p.Spawned || p.Faction == null) return;
            if (p.Faction.HostileTo(Faction.OfPlayer)) LinarianRaid.Rejoin(p);
            else LordMaker.MakeNewLord(p.Faction, new LordJob_ExitMapBest(), p.Map, new List<Pawn> { p });
        }

        /// <summary>The cult spreads: a fervent cultist now and then draws someone near into it.</summary>
        private static void Spread(Pawn her, Gene_LinarianSong g, Pawn q)
        {
            if (LinarianActs.Worship(q) < Fervent || !q.Spawned) return;
            if (!Rand.Chance(g.Has("C5") ? 0.15f : 0.08f)) return;
            Pawn near = GenRadial.RadialDistinctThingsAround(q.Position, q.Map, 6f, true).OfType<Pawn>()
                .FirstOrDefault(x => x != q && x != her && x.RaceProps.Humanlike && x.Faction != Faction.OfPlayer && SingerOf(x) == null && LinarianTactics.Takeable(x));
            if (near == null || !Rand.Chance(LinarianTactics.Chance(her, g, near, false, "C"))) return;
            Hediff h = LinarianSource.Add(near, LinarianHediffs.Avir_Adoration, her, -1, 0.1f);
            var c = h?.TryGetComp<HediffComp_Adoration>();
            if (c != null) c.massUntil = Find.TickManager.TicksGame + GenDate.TicksPerDay;
        }

        /// <summary>Hungry, a cultist eats whatever there is within 40 steps - food, a corpse - and if there is nothing,
        /// a fallen fellow cultist.</summary>
        private static void Feed(Pawn q, List<Pawn> flock)
        {
            Need_Food f = q.needs?.food;
            if (f == null || f.CurLevelPercentage > 0.3f || q.Downed || q.InMentalState || !q.Spawned) return;
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Avir_CultFeed");
            if (def == null || q.CurJobDef == def) return;
            Thing food = GenClosest.ClosestThingReachable(q.Position, q.Map, ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree), PathEndMode.Touch,
                TraverseParms.For(q), 40f, t => t.IngestibleNow && (!(t is Corpse c) || c.GetRotStage() != RotStage.Dessicated) && !t.IsForbidden(Faction.OfPlayer) || t is Corpse);
            if (food == null)
                food = flock.Where(x => x != q && x.Downed && !x.Dead && (x.Position - q.Position).LengthHorizontal <= 40f && q.CanReach(x, PathEndMode.Touch, Danger.Deadly))
                    .OrderBy(x => (x.Position - q.Position).LengthHorizontalSquared).FirstOrDefault();
            if (food == null) return;
            q.jobs.StartJob(JobMaker.MakeJob(def, food), JobCondition.InterruptForced);
        }

        public static Color Colour => Violet;
    }

    /// <summary>The stream of adoration and the crowd's: grows by the hour (the stream), holds at its height, ebbs.</summary>
    public class HediffCompProperties_Adoration : HediffCompProperties
    {
        public HediffCompProperties_Adoration() { compClass = typeof(HediffComp_Adoration); }
    }

    public class HediffComp_Adoration : HediffComp
    {
        public bool stream;
        public int stacks;
        public int peakUntil;
        public int massUntil;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref stream, "stream");
            Scribe_Values.Look(ref stacks, "stacks");
            Scribe_Values.Look(ref peakUntil, "peakUntil");
            Scribe_Values.Look(ref massUntil, "massUntil");
        }

        public override string CompLabelInBracketsExtra => stream ? (stacks > 1 ? "stream x2" : "stream") : massUntil > 0 ? "crowd" : null;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (!Pawn.IsHashIntervalTick(GenDate.TicksPerHour, delta)) return;
            Pawn her = LinarianCult.SingerOf(Pawn);
            if (her == null || her.Faction != Faction.OfPlayer) return;   // a wild Singer's cult still grows by the night
            int now = Find.TickManager.TicksGame;
            Gene_LinarianSong g = Linarian.Song(her);
            float faster = g != null && g.Has("C5") ? 1.4f : 1f;
            if (stream)
            {
                if (parent.Severity < 0.999f && peakUntil == 0) { parent.Severity = Mathf.Min(1f, parent.Severity + 0.03f * Mathf.Max(1, stacks) * faster); return; }
                if (peakUntil == 0) peakUntil = now + GenDate.TicksPerHour * 72 * Mathf.Max(1, stacks);
                if (now > peakUntil) Ebb(0.02f);
                return;
            }
            if (massUntil == 0) return;
            if (now < massUntil)
            {
                // by night the crowd's worship does not grow - it lasts longer, each by chance
                if (her.MapHeld != null && LinarianPresence.Night(her.MapHeld) && g != null && Rand.Chance(LinarianTactics.Chance(her, g, Pawn, false, "C")))
                    massUntil += GenDate.TicksPerHour;
                return;
            }
            Ebb(0.05f);
        }

        private void Ebb(float by)
        {
            parent.Severity -= by;
            if (parent.Severity <= 0.01f) Pawn.health.RemoveHediff(parent);
        }
    }

    // ---------------------------------------------------------------- the states

    /// <summary>Guarding the map's edges: walk them, fall on any foe of her people who comes near; then sleep.</summary>
    public class MentalState_SongMapGuard : MentalState_Song
    {
        public override bool Hers => true;
        private IntVec3 post = IntVec3.Invalid;

        protected override void Drive()
        {
            Pawn foe = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 12f, true).OfType<Pawn>()
                .FirstOrDefault(x => !x.Dead && !x.Downed && x != pawn && x.HostileTo(Faction.OfPlayer) && !LinarianCult.IsCultist(x));
            if (foe != null) { Job hit = AttackJob(foe); hit.killIncappedTarget = false; Force(hit); return; }
            // a patrol: from one point of the land's edges to the next, never standing about
            if (!post.IsValid || (pawn.Position - post).LengthHorizontal <= 2f || pawn.CurJobDef != JobDefOf.Goto)
            {
                Map m = pawn.Map;
                if (!post.IsValid || (pawn.Position - post).LengthHorizontal <= 2f)
                    if (!CellFinderLoose.TryGetRandomCellWith(c => c.Standable(m) && c.DistanceToEdge(m) is int d && d >= 5 && d <= 25
                            && (c - pawn.Position).LengthHorizontal >= 20f && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly), m, 60, out post))
                        post = IntVec3.Invalid;
                if (!post.IsValid) CellFinder.TryFindRandomReachableNearbyCell(pawn.Position, m, 25f, TraverseParms.For(pawn), c => c.Standable(m), null, out post);
            }
            if (post.IsValid)
            {
                Job walk = JobMaker.MakeJob(JobDefOf.Goto, post);
                walk.locomotionUrgency = LocomotionUrgency.Walk;
                Force(walk);
            }
        }

        public override bool ForceHostileTo(Thing t) => t is Pawn x && x.HostileTo(Faction.OfPlayer) && !LinarianCult.IsCultist(x) && x.Faction != Faction.OfPlayer;

        public override void PostEnd()
        {
            base.PostEnd();
            if (!pawn.Dead && pawn.Spawned) LinarianHediffs.Sleep(pawn, GenDate.TicksPerHour * 4);
        }
    }

    /// <summary>Her sleep guarded: a ring at forty steps round her; whoever comes in - not her people, not the cult - is met.</summary>
    public class MentalState_SongSleepWard : MentalState_Song
    {
        public override bool Hers => true;
        private IntVec3 post = IntVec3.Invalid;
        private const float Ring = 40f;

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (herald != null && pawn.IsHashIntervalTick(250, delta) && !LinarianTactics.Asleep(herald)) RecoverFromState();
        }

        protected override void Drive()
        {
            Pawn foe = herald.Map.mapPawns.AllPawnsSpawned.FirstOrDefault(x => !x.Dead && !x.Downed && x != pawn && x != herald && x.RaceProps.Humanlike
                && x.Faction != Faction.OfPlayer && !LinarianCult.IsCultist(x) && (x.Position - herald.Position).LengthHorizontal <= Ring);
            if (foe != null) { Force(AttackJob(foe)); return; }
            if (!post.IsValid || (pawn.Position - post).LengthHorizontal <= 2f)
            {
                float a = Rand.Range(0f, 360f);
                IntVec3 want = herald.Position + (Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward * Ring).ToIntVec3();
                if (!want.InBounds(pawn.Map) || !CellFinder.TryFindRandomReachableNearbyCell(want, pawn.Map, 5f, TraverseParms.For(pawn), c => c.Standable(pawn.Map), null, out post))
                    post = IntVec3.Invalid;
            }
            if (post.IsValid) Force(JobMaker.MakeJob(JobDefOf.Goto, post));
        }

        public override bool ForceHostileTo(Thing t) => t is Pawn x && x.RaceProps.Humanlike && x.Faction != Faction.OfPlayer && !LinarianCult.IsCultist(x)
                                                         && herald != null && (x.Position - herald.Position).LengthHorizontal <= Ring;
    }

    /// <summary>Ecstasy: a wide ring round the point, standing rapt; the worship grows, foes slip into the cult.</summary>
    public class MentalState_SongEcstasy : MentalState_Song
    {
        public override bool Hers => true;
        public IntVec3 point = IntVec3.Invalid;
        private IntVec3 post = IntVec3.Invalid;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref point, "point", IntVec3.Invalid);
        }

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (herald == null || !pawn.IsHashIntervalTick(GenDate.TicksPerHour / 2, delta)) return;
            Gene_LinarianSong g = Linarian.Song(herald);
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_Adoration);
            if (h != null && LinarianCult.SingerOf(pawn) == herald) h.Severity = Mathf.Min(1f, h.Severity + 0.04f);
            else if (g != null && Rand.Chance(LinarianTactics.Chance(herald, g, pawn, false, "C")))
            {
                if (h != null) pawn.health.RemoveHediff(h);
                Hediff n = LinarianSource.Add(pawn, LinarianHediffs.Avir_Adoration, herald, -1, 0.1f);
                var c = n?.TryGetComp<HediffComp_Adoration>();
                if (c != null) c.massUntil = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            }
        }

        protected override void Drive()
        {
            if (!point.IsValid) point = pawn.Position;
            if (!post.IsValid)
            {
                float a = Rand.Range(0f, 360f);
                IntVec3 want = point + (Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward * 6f).ToIntVec3();
                if (!want.InBounds(pawn.Map) || !CellFinder.TryFindRandomReachableNearbyCell(want, pawn.Map, 2f, TraverseParms.For(pawn), c => c.Standable(pawn.Map), null, out post)) post = pawn.Position;
            }
            if (pawn.Position != post) { Force(JobMaker.MakeJob(JobDefOf.Goto, post)); return; }
            if (Rand.Chance(0.05f) && pawn.Spawned) FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.Heart);
            Force(JobMaker.MakeJob(JobDefOf.Wait, 300));
        }
    }

    /// <summary>The last order: fall on the nearest foe of her people with a terrible first blow, fight to the last; with
    /// no foe left, die by their own hand.</summary>
    public class MentalState_SongLastOrder : MentalState_Song
    {
        public override bool Hers => true;
        public bool single;
        public bool firstBlow;
        /// <summary>The maniac seen at his work: until then his own people do not know him for an enemy.</summary>
        public bool discovered;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref single, "single");
            Scribe_Values.Look(ref firstBlow, "firstBlow");
            Scribe_Values.Look(ref discovered, "discovered");
        }

        public override void MentalStateTick(int delta)
        {
            if (herald == null) { RecoverFromState(); return; }
            if (pawn.IsHashIntervalTick(30, delta)) Drive();
        }

        protected override void Drive()
        {
            // the one sent alone is a maniac among his own: his comrades' necks first, then any foe
            Pawn foe = (single ? LinarianCult.Comrades(pawn).OrderBy(x => (x.Position - pawn.Position).LengthHorizontalSquared).FirstOrDefault() : null)
                       ?? LinarianCult.Foes(pawn, herald).OrderBy(x => (x.Position - pawn.Position).LengthHorizontalSquared).FirstOrDefault();
            if (foe == null) { LinarianCult.Die(pawn); return; }
            Force(JobMaker.MakeJob(JobDefOf.AttackMelee, foe));
        }

        public override bool ForceHostileTo(Thing t) => t is Pawn x && !LinarianCult.IsCultist(x) && x.Faction != Faction.OfPlayer
                                                         && (x.HostileTo(Faction.OfPlayer) || single && x.Faction == pawn.Faction);
    }

    /// <summary>A cultist feeding: anything at all, a corpse - or a fallen fellow, killed first.</summary>
    public class JobDriver_CultFeed : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil eat = ToilMaker.MakeToil("CultFeed");
            eat.initAction = () =>
            {
                Thing t = job.targetA.Thing;
                if (t is Pawn fallen && !fallen.Dead) { fallen.Kill(null, null); t = fallen.Corpse; }
                if (t == null || t.Destroyed || !t.IngestibleNow || pawn.needs?.food == null) return;
                float got = t.Ingested(pawn, pawn.needs.food.NutritionWanted);
                pawn.needs.food.CurLevel += got;
            };
            eat.defaultCompleteMode = ToilCompleteMode.Delay;
            eat.defaultDuration = 300;
            yield return eat;
        }
    }

    // ---------------------------------------------------------------- patches

    /// <summary>A cultist is no enemy of the player's people (unless a state of hers says otherwise).</summary>
    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
    public static class Patch_CultNotHostile
    {
        public static void Postfix(Thing a, Thing b, ref bool __result)
        {
            if (!__result) return;
            if (a?.Faction == Faction.OfPlayer && b is Pawn pb && LinarianCult.IsCultist(pb) || b?.Faction == Faction.OfPlayer && a is Pawn pa && LinarianCult.IsCultist(pa))
                __result = false;
            // one she has sown adoration in - even while it is still only longing - does not raise a hand against her
            // herself (the colony's other people they may still fight until they are devoted)
            else if (a is Pawn x && b is Pawn y && (LinarianCult.SingerOf(x) == y || LinarianCult.SingerOf(y) == x))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) })]
    public static class Patch_CultNotHostileFaction
    {
        public static void Postfix(Thing t, Faction fac, ref bool __result)
        {
            if (__result && fac == Faction.OfPlayer && t is Pawn p && LinarianCult.IsCultist(p)) __result = false;
        }
    }

    /// <summary>A cultist does not leave the map.</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_CultStays
    {
        public static bool Prefix(Job newJob, Pawn ___pawn) => newJob == null || !newJob.exitMapOnArrival || !LinarianCult.IsCultist(___pawn);
    }

    /// <summary>Her colour on them, as Hospitality draws its guests.</summary>
    [HarmonyPatch(typeof(PawnNameColorUtility), nameof(PawnNameColorUtility.PawnNameColorOf))]
    public static class Patch_CultColour
    {
        // last, after every other mod's colour (Hospitality paints guests green)
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn pawn, ref Color __result)
        {
            if (LinarianCult.IsCultist(pawn)) __result = LinarianCult.Colour;
        }
    }

    /// <summary>The last order's first blow: half again as hard - twice, and at the neck, when one alone was sent.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_LastOrderFirstBlow
    {
        public static void Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn q) || !(q.MentalState is MentalState_SongLastOrder o)) return;
            if (dinfo.Def == null || !dinfo.Def.harmsHealth) return;
            // the maniac: every blow twice as hard, at the neck; the rest: only the first, half again
            if (!o.single && !o.firstBlow) return;
            o.firstBlow = false;
            dinfo.SetAmount(dinfo.Amount * (o.single ? 2f : 1.5f));
            if (!o.single) return;
            BodyPartRecord neck = __instance.health?.hediffSet?.GetNotMissingParts().FirstOrDefault(p => p.def.defName == "Neck");
            if (neck != null) dinfo.SetHitPart(neck);
        }
    }
    /// <summary>The maniac among his own: his own people do not know him for an enemy until he is seen at it - struck
    /// and surviving, or watched by another of them (in sight, within twenty steps). Any other people know him at once.</summary>
    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
    public static class Patch_ManiacUnseen
    {
        public static void Postfix(Thing a, Thing b, ref bool __result)
        {
            if (!__result || !(a is Pawn pa) || !(b is Pawn pb) || pa.Faction == null || pa.Faction != pb.Faction) return;
            if (Hidden(pa) || Hidden(pb)) __result = false;
        }

        public static bool Hidden(Pawn p) => p.MentalState is MentalState_SongLastOrder o && o.single && !o.discovered;
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_ManiacSeen
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn m) || !(m.MentalState is MentalState_SongLastOrder o) || !o.single || o.discovered) return;
            if (__instance.Faction != m.Faction || !m.Spawned) return;
            bool survived = !__instance.Dead && !__instance.Downed;
            bool watched = m.Map.mapPawns.AllPawnsSpawned.Any(x => x != m && x != __instance && x.Faction == m.Faction && !x.Dead && !x.Downed && x.Awake()
                && (x.Position - m.Position).LengthHorizontal <= 20f && GenSight.LineOfSight(x.Position, m.Position, m.Map));
            if (survived || watched)
            {
                o.discovered = true;
                MoteMaker.ThrowText(m.DrawPos, m.Map, "Traitor!", 4f);
            }
        }
    }

}
