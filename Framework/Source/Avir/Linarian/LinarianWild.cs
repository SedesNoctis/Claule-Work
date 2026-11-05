using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    // =====================================================================================
    // THE HIDDEN HERALDS. A Herald comes onto a map as a beast - a doe, a crow or a she-wolf - listed among the
    // wildlife, out only at night; by day she is nowhere to be seen. Her beasts are predators that hunt nothing and
    // graze nothing, and they eat only the dead. Two kinds, alike to the eye:
    //  WILD: the beasts of the land come to her at night and die beside her; sleepers wake from a song; after a few
    //    nights she calls one colonist (one at a time): it walks into the woods and falls asleep there, and she either
    //    drinks a little of it unseen (it comes back after waking) or shows herself and devours it. A colonist taken
    //    into a prison cell is let go - she picks another. Hunted, she veils and hunts the hunter by night. Seen
    //    through and not caught, she raises the beasts against the colony and leaves once they are dead. She may be
    //    tamed (Animals 12, one in ten); a failed attempt may scare the tamer - the beast spoke back.
    //  PRIMAL: the beasts go mad against the colony - one, then many, then beasts from beyond the map, then the Primal
    //    Hunt itself - and every night she calls or lulls a colonist. Tamed, she devours the tamer and the beasts come.
    //    Hunted or seen through: the beasts come and she veils. Caught and held, she keeps on until she is truly won.
    // =====================================================================================
    public class HeraldRecord : IExposable
    {
        public Pawn her;
        public bool primal;
        public int nights;
        public int lastNight = -1;
        public int stage;
        public Pawn grudge;
        public int revealedUntil = -1;
        public bool leaving;
        public IntVec3 lair = IntVec3.Invalid;
        public int nextMove;
        public int lastFog = -1;
        public bool usedEternal;
        /// <summary>Her main path: what kind of Singer she is, and how she behaves.</summary>
        public string main;
        /// <summary>The one she means to peck tonight (a hunter, or the Primal one's quiet malice), and until when.</summary>
        public Pawn peckTarget;
        public int peckUntil;
        /// <summary>The food the crow is after tonight.</summary>
        public Thing raidTarget;
        public int raidUntil;
        public bool carrying;
        public List<string> habits;
        public Pawn favourite;
        /// <summary>Came to a call of the wild gods: she stays while they are worshipped.</summary>
        public bool devoted;
        /// <summary>Her beloved (LinarianCourtship): whom she courts, by her taste, and how it stands.</summary>
        public bool courts, joined, wasHeld, asked;
        public string taste;
        public Pawn beloved;
        public int calledAt = -1, choice, serveSince = -1, rescue, heldNights;

        public void ExposeData()
        {
            Scribe_References.Look(ref her, "her");
            Scribe_Values.Look(ref primal, "primal");
            Scribe_Values.Look(ref nights, "nights");
            Scribe_Values.Look(ref lastNight, "lastNight", -1);
            Scribe_Values.Look(ref stage, "stage");
            Scribe_References.Look(ref grudge, "grudge");
            Scribe_Values.Look(ref revealedUntil, "revealedUntil", -1);
            Scribe_Values.Look(ref leaving, "leaving");
            Scribe_Values.Look(ref lair, "lair", IntVec3.Invalid);
            Scribe_Values.Look(ref lastFog, "lastFog", -1);
            Scribe_Values.Look(ref usedEternal, "usedEternal");
            Scribe_Values.Look(ref main, "main");
            Scribe_References.Look(ref peckTarget, "peckTarget");
            Scribe_Values.Look(ref peckUntil, "peckUntil");
            Scribe_References.Look(ref raidTarget, "raidTarget");
            Scribe_Values.Look(ref raidUntil, "raidUntil");
            Scribe_Values.Look(ref carrying, "carrying");
            Scribe_Collections.Look(ref habits, "habits", LookMode.Value);
            Scribe_References.Look(ref favourite, "favourite");
            Scribe_Values.Look(ref devoted, "devoted");
            Scribe_Values.Look(ref courts, "courts");
            Scribe_Values.Look(ref joined, "joined");
            Scribe_Values.Look(ref wasHeld, "wasHeld");
            Scribe_Values.Look(ref asked, "asked");
            Scribe_Values.Look(ref taste, "taste");
            Scribe_References.Look(ref beloved, "beloved");
            Scribe_Values.Look(ref calledAt, "calledAt", -1);
            Scribe_Values.Look(ref choice, "choice");
            Scribe_Values.Look(ref serveSince, "serveSince", -1);
            Scribe_Values.Look(ref rescue, "rescue");
            Scribe_Values.Look(ref heldNights, "heldNights");
        }
    }

    public class GameComponent_Heralds : GameComponent
    {
        public static GameComponent_Heralds Instance;
        public List<HeraldRecord> records = new List<HeraldRecord>();

        public GameComponent_Heralds(Game game) { Instance = this; }

        public HeraldRecord For(Pawn p) => records.FirstOrDefault(r => r.her == p);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "singers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null) records = new List<HeraldRecord>();
                records.RemoveAll(r => r?.her == null);
            }
        }
    }

    public static partial class LinarianPresence
    {
        private static GameComponent_Heralds G => GameComponent_Heralds.Instance;

        public static HeraldRecord Record(Pawn p) => G?.For(p);

        /// <summary>A hidden Herald: the map's own beasts and colonists are hers to deal with, not the generic song.</summary>
        public static bool Managed(Pawn p) => Record(p) != null;

        /// <summary>Wearing her beast's disguise: listed as wildlife, named for the beast.</summary>
        public static bool Disguised(Pawn p)
        {
            HeraldRecord r = Record(p);
            return r != null && LinarianVeil.InForm(p) != null && Find.TickManager.TicksGame > r.revealedUntil && p.Faction != Faction.OfPlayer && !p.IsPrisoner;
        }

        public static bool Night(Map map)
        {
            int h = GenLocalDate.HourOfDay(map);
            return h >= 17 || h < 7 || LinarianShadow.EternalNightOn(map);   // from the evening to the first light
        }

        // ---------------------------------------------------------------- arrival

        public static Pawn Arrive(Map map, bool primal)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Herald");
            if (kind == null || G == null) return null;
            Faction primalFaction = PrimalFaction();
            var req = new PawnGenerationRequest(kind, primal ? primalFaction : null, PawnGenerationContext.NonPlayer, map.Tile,
                forceGenerateNewPawn: true, fixedGender: Gender.Female, forcedXenotype: LinarianDefOf.Avir_Linarian);
            Pawn her = PawnGenerator.GeneratePawn(req);
            if (her.Faction != null) her.SetFaction(null);
            MakeHerOwn(her);
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 at, map, CellFinder.EdgeRoadChance_Animal)) return null;
            GenSpawn.Spawn(her, at, map);
            Gene_LinarianSong g = Linarian.Song(her);
            string main = g != null ? Specialize(g, primal) : null;
            var r = new HeraldRecord { her = her, primal = primal, lair = LinarianManners.FindLair(map, at), main = main, habits = LinarianManners.RollHabits() };
            r.lastNight = GenDate.DaysPassed;   // the first night she only looks around
            r.courts = LinarianCourtship.Rolls(her, primal);
            r.taste = LinarianCourtship.Tastes.RandomElement();
            if (r.habits.Contains(LinarianManners.Favourite)) r.favourite = map.mapPawns.FreeColonistsSpawned.RandomElementWithFallback();
            if (!primal && Rand.Chance(0.15f)) r.habits = new List<string> { LinarianManners.Kind };
            else if (Rand.Chance(0.3f)) r.habits.Add(LinarianManners.Cult);
            G.records.Add(r);
            Disguise(r);
            return her;
        }

        /// <summary>Whatever the generator made of her, she is a Singer: of her own kind, in her own feathers (no clothes, no
        /// weapon - a beast wears nothing), and with a Singer's own life behind her.</summary>
        public static void MakeHerOwn(Pawn her)
        {
            XenotypeDef x = LinarianDefOf.Avir_Linarian;
            if (her.genes != null && x != null)
            {
                if (her.genes.Xenotype != x) her.genes.SetXenotype(x);
                // whatever undid it: the song and every gene of her kind, by hand
                if (!Linarian.Is(her))
                {
                    foreach (GeneDef gd in x.genes) if (!her.genes.HasActiveGene(gd)) her.genes.AddGene(gd, !x.inheritable);
                    if (her.genes.GetGene(LinarianDefOf.Avir_LinarianSong) == null) her.genes.AddGene(LinarianDefOf.Avir_LinarianSong, false);
                    her.genes.SetXenotypeDirect(x);
                }
                if (!Linarian.Is(her)) Log.Warning("[Avir] a hidden Singer would not take her own kind: " + her.genes.Xenotype?.defName + ", race " + her.def.defName);
            }
            // a wild Singer is always young
            if (her.ageTracker != null)
            {
                long age = (long)(Rand.Range(19f, 30f) * GenDate.TicksPerYear);
                her.ageTracker.AgeBiologicalTicks = age;
                her.ageTracker.AgeChronologicalTicks = age + (long)(Rand.Range(0f, 2f) * GenDate.TicksPerYear);
                // and none of the old age the generator may have given her first (cataracts, a bad back...)
                foreach (Hediff h in her.health.hediffSet.hediffs.Where(h => h.def.chronic).ToList()) her.health.RemoveHediff(h);
            }
            AvirFaith.Settle(her);
            her.apparel?.DestroyAll();
            her.equipment?.DestroyAllEquipment();
            her.inventory?.DestroyAll();
            if (her.story != null)
            {
                var own = DefDatabase<BackstoryDef>.AllDefsListForReading.Where(b => b.spawnCategories != null && b.spawnCategories.Contains("AvirSinger")).ToList();
                BackstoryDef child = own.Where(b => b.slot == BackstorySlot.Childhood).RandomElementWithFallback();
                BackstoryDef adult = own.Where(b => b.slot == BackstorySlot.Adulthood).RandomElementWithFallback();
                if (child != null) her.story.Childhood = child;
                if (adult != null) her.story.Adulthood = adult;
                her.Notify_DisabledWorkTypesChanged();
            }
        }

        /// <summary>What a hidden Herald is: one path sung through to its crown, her veil and forms, a few first
        /// steps elsewhere - and, rarely, the shadow.</summary>
        private static string Specialize(Gene_LinarianSong g, bool primal)
        {
            string main = primal
                ? new[] { ("C", 0.5f), ("W", 0.3f), ("P", 0.2f) }.RandomElementByWeight(x => x.Item2).Item1
                : new[] { ("W", 0.35f), ("D", 0.3f), ("S", 0.15f), ("P", 0.1f), ("L", 0.1f) }.RandomElementByWeight(x => x.Item2).Item1;
            for (int i = 1; i <= 5; i++) g.opened.Add(main + i);
            g.opened.Add("G1"); g.opened.Add("G2");
            foreach (string a in LinarianNetwork.Areas.Where(a => a != main).InRandomOrder().Take(Rand.RangeInclusive(1, 3)))
            {
                g.opened.Add(a + "1");
                if (Rand.Bool) g.opened.Add(a + "2");
            }
            if (Rand.Chance(0.07f))
            {
                g.shadow = true;
                for (int i = 1; i <= 5; i++) g.opened.Add("T" + i);
            }
            g.songLevel = Mathf.Max(0, g.opened.Count - Gene_LinarianSong.StartPoints);
            return main;
        }

        public static Faction PrimalFaction()
        {
            FactionDef d = DefDatabase<FactionDef>.GetNamedSilentFail("Avir_Primal");
            return d == null ? null : Find.FactionManager.FirstFactionOfDef(d);
        }

        private static IntVec3 FindLair(Map map, IntVec3 near)
        {
            if (CellFinder.TryFindRandomCellNear(near, map, 25, c => c.Standable(map) && !map.areaManager.Home[c] && !c.Roofed(map), out IntVec3 c2)) return c2;
            return near;
        }

        private static void Disguise(HeraldRecord r)
        {
            Pawn her = r.her;
            if (her.Faction != null && her.Faction != Faction.OfPlayer && !her.IsPrisoner) her.SetFaction(null);
            if (LinarianVeil.InForm(her) == null) LinarianVeil.SetForm(her, Forms(her).RandomElement());
            AvirFaith.Settle(her);   // the faith of the wild song, whatever she came with (or an old save gave her)
        }

        /// <summary>The beasts she can be: the doe and the crow always, the she-wolf when her aria has it.</summary>
        private static List<string> Forms(Pawn her)
        {
            var l = new List<string> { LinarianTactics.Doe, LinarianTactics.Crow };
            Gene_LinarianSong g = Linarian.Song(her);
            if (g != null && LinarianTactics.Unlocked(g, LinarianTactics.Wolf)) l.Add(LinarianTactics.Wolf);
            return l;
        }

        /// <summary>A new night, sometimes a new beast: a doe one night, a crow the next. A careful watcher may notice.</summary>
        private static void NewNightForm(HeraldRecord r)
        {
            Pawn her = r.her;
            string now = LinarianVeil.InForm(her);
            if (now == null || !Rand.Chance(0.35f)) return;
            string next = Forms(her).Where(f => f != now).RandomElementWithFallback();
            if (next != null) LinarianVeil.SetForm(her, next);
        }

        private static readonly Dictionary<int, int> lastHunt = new Dictionary<int, int>();

        /// <summary>Hungry by night, she calls a wild beast to her and eats. Only the wild ones: what is the colony's is
        /// never hers to take this way.</summary>
        private static void HuntIfHungry(HeraldRecord r, Map map, int now)
        {
            Pawn her = r.her;
            if (now % 240 != 0 || r.peckTarget != null || r.raidTarget != null) return;
            float food = her.needs?.food?.CurLevelPercentage ?? 1f;
            bool starving = food < 0.3f;
            // the night she comes she only looks around, and one kill a night is enough - unless she is starving
            if (!starving && r.nights == 0) return;
            if (!starving && lastHunt.TryGetValue(her.thingIDNumber, out int last) && now - last < GenDate.TicksPerHour * 4) return;
            bool hungry = food < 0.6f || !Gene_ArcaneHunger.HasAtLeast(her, 0.6f);
            if (!hungry || feeding.ContainsKey(her.thingIDNumber) || her.CurJobDef == JobDefOf.AttackMelee) return;
            if (map.mapPawns.AllPawnsSpawned.Any(q => q.MentalState is MentalState_Song ms && ms.herald == her)) return;   // one is already coming
            Gene_LinarianSong g = Linarian.Song(her);
            float max = g != null ? LinarianSongHunt.MaxSize(g) + 0.6f : 1.2f;
            Pawn prey = map.mapPawns.AllPawnsSpawned
                .Where(a => a.RaceProps.Animal && a.Faction == null && !a.Dead && !a.Downed && a.BodySize <= max && !Linarian.Is(a) && a.MentalStateDef == null
                    && a.CanReach(her, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(a => a.RaceProps.predator ? 1 : 0)   // grazers first: a hunter does not pick a fight for supper
                .ThenBy(a => (a.Position - her.Position).LengthHorizontalSquared).FirstOrDefault();
            if (prey == null) return;
            lastHunt[her.thingIDNumber] = now;
            if (LinarianMental.StartHunt(prey, her, GenDate.TicksPerHour * 3)) return;
            // the aria did not take: then she hunts it as any beast of prey would, and eats what she kills
            Job kill = JobMaker.MakeJob(JobDefOf.AttackMelee, prey);
            kill.killIncappedTarget = true;
            her.jobs.StartJob(kill, JobCondition.InterruptForced);
        }

        private static readonly Dictionary<int, Corpse> feeding = new Dictionary<int, Corpse>();

        /// <summary>A beast eats what it kills - and what it finds fresh. Hungry, she goes to a fresh carcass of a beast
        /// near her and eats from it as a predator does: a few parts, not the whole of it.</summary>
        private static bool Scavenge(HeraldRecord r, int now)
        {
            Pawn her = r.her;
            Need_Food f = her.needs?.food;
            int id = her.thingIDNumber;
            if (f == null || f.CurLevelPercentage >= 0.8f || r.peckTarget != null || r.raidTarget != null) { feeding.Remove(id); return false; }
            if (her.CurJobDef == JobDefOf.AttackMelee) return true;   // the kill first, the meal after
            if (!feeding.TryGetValue(id, out Corpse c) || c == null || c.Destroyed || !c.Spawned || c.Map != her.Map || !c.IngestibleNow)
            {
                feeding.Remove(id);
                if (now % 250 != 0) return false;
                c = her.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                    .Where(x => x.InnerPawn != null && x.InnerPawn.RaceProps.Animal && x.GetRotStage() == RotStage.Fresh && x.IngestibleNow
                                && (x.Position - her.Position).LengthHorizontalSquared <= 30 * 30 && !SongShelter.ShieldedCell(her.Map, x.Position)
                                && her.CanReach(x, PathEndMode.Touch, Danger.Some))
                    .OrderBy(x => (x.Position - her.Position).LengthHorizontalSquared).FirstOrDefault();
                if (c == null) return false;
                feeding[id] = c;
            }
            if ((c.Position - her.Position).LengthHorizontal <= 1.5f)
            {
                float whole = c.GetStatValue(StatDefOf.Nutrition);
                float want = Mathf.Min(f.NutritionWanted, Mathf.Max(0.1f, whole * 0.6f));
                float got = c.Ingested(her, want);
                f.CurLevel += got;
                if (her.Spawned) FleckMaker.ThrowMetaIcon(her.Position, her.Map, FleckDefOf.Meditating);
                feeding.Remove(id);
                return true;
            }
            if (her.CurJobDef != JobDefOf.Goto || her.CurJob.targetA.Thing != c)
                her.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, c), JobCondition.InterruptForced);
            return true;
        }

        // ---------------------------------------------------------------- every second

        private static readonly Dictionary<int, int> lastEvade = new Dictionary<int, int>();

        /// <summary>A Singer who is not the player's is never simply rushed down: when a drafted pawn of the player comes at
        /// her, she slips under her veil and is gone - once in a while, so that she cannot be caught the moment she appears.</summary>
        private static void EvadeTheRush(Map map, int now)
        {
            foreach (Pawn her in map.mapPawns.AllPawnsSpawned.Where(x => x.Faction != Faction.OfPlayer && !x.Dead && !x.Downed && !x.IsPrisoner && Linarian.Is(x)).ToList())
            {
                if (LinarianVeil.Veiled(her)) continue;
                if (lastEvade.TryGetValue(her.thingIDNumber, out int t) && now - t < GenDate.TicksPerHour * 3) continue;
                bool rushed = map.mapPawns.FreeColonistsSpawned.Any(c => c.Drafted && (c.Position - her.Position).LengthHorizontal <= 9f
                    && (c.CurJob?.targetA.Thing == her || c.pather?.Moving == true && (c.pather.Destination.Cell - her.Position).LengthHorizontal <= 3f));
                if (!rushed) continue;
                lastEvade[her.thingIDNumber] = now;
                Gene_LinarianSong hg = Linarian.Song(her);
                HeraldRecord hr = G?.records.FirstOrDefault(x => x.her == her);
                if (hr != null && LinarianManners.IsKind(hr)) hg = null;   // the kind one only slips away
                if (hg != null)
                foreach (Pawn c in map.mapPawns.FreeColonistsSpawned.Where(c => c.Drafted && (c.Position - her.Position).LengthHorizontal <= 12f).ToList())
                {
                    bool aching = LinarianSource.Of(c, LinarianHediffs.Avir_StrangeAche) == her;
                    if (aching || hg != null && hg.Has("L1") && LinarianTactics.Takeable(c) && Rand.Chance(LinarianTactics.Chance(her, hg, c, true, "L")))
                        LinarianHediffs.Sleep(c, GenDate.TicksPerHour, her);
                }
                Flee(her, map.mapPawns.FreeColonistsSpawned.Where(c => c.Drafted).Cast<Thing>().ToList());
                if (Find.CurrentMap == map) FleckMaker.ThrowSmoke(her.DrawPos, map, 1.4f);
            }
        }

        static partial void TickImplP(Map map, int now)
        {
            EvadeTheRush(map, now);
            if (G == null || G.records.Count == 0) return;
            foreach (HeraldRecord r in G.records.ToList())
            {
                Pawn her = r.her;
                if (her == null || her.Destroyed || her.Dead || her.Faction == Faction.OfPlayer) { G.records.Remove(r); continue; }
                if (her.MapHeld != map) continue;
                if (her.Spawned && LinarianVeil.Veiled(her)) HeraldDesignations.Clear(her);   // a mark set before she veiled
                bool night = Night(map) && !EternalNightRest.RealMidday(map);   // under the eternal night she still rests through the real midday
                bool held = her.IsPrisoner;
                // territorial: she leaves her land only in real danger - badly hurt
                if (!r.leaving && !held && her.Spawned && !LinarianManners.IsKind(r) && her.health.summaryHealth.SummaryHealthPercent < 0.5f) r.leaving = true;
                bool revealed = now <= r.revealedUntil;
                if (!held && her.Spawned && !her.Downed && !revealed)
                {
                    if (r.revealedUntil > 0 && r.revealedUntil + 60 >= now) Disguise(r);   // the hour is over: the beast again
                    if (night) { if (!Calling(her)) LinarianVeil.Unveil(her); if (LinarianVeil.InForm(her) == null) Disguise(r); HuntIfHungry(r, map, now); }
                    else if (LinarianCourtship.Following(r, now)) { if (LinarianVeil.Veiled(her)) LinarianVeil.Unveil(her); if (LinarianVeil.InForm(her) == null) Disguise(r); }
                    else
                    {
                        if (!LinarianVeil.Veiled(her)) LinarianVeil.Veil(her, GenDate.TicksPerHour * 2);
                        // starving, she does not wait for the dark
                        if ((her.needs?.food?.CurLevelPercentage ?? 1f) < 0.25f) HuntIfHungry(r, map, now);
                        if (r.peckTarget == null && r.raidTarget == null && !r.leaving) { if (!Scavenge(r, now)) RestByDay(r); continue; }
                    }
                    if (!LinarianCourtship.Following(r, now) && !Scavenge(r, now)) Wander(r, now);
                }
                if (her.Spawned) HandleCalled(r);
                LinarianGods.Aid(r, map, now);
                LinarianCourtship.Tick(r, map, now);
                if (!held && her.Spawned && !her.Downed)
                {
                    if (LinarianManners.IsKind(r)) LinarianManners.Guard(r, map, now);
                    else { Peck(r, now); CrowRaid(r, now); }
                    LinarianManners.Habits(r, map, now);
                }
                if (!held && her.Spawned) ShadowWork(r, map, night);
                int day = GenDate.DaysPassed;
                if (night && r.lastNight != day && !her.Downed)
                {
                    r.lastNight = day;
                    r.nights++;
                    if (!held && her.Spawned && !revealed) NewNightForm(r);
                    // she hunts (HuntIfHungry) or is fed by her faithful; only on a land with no wild beast at all does she
                    // make do with what lies dead in the woods
                    Need_Food food = her.needs?.food;
                    if (food != null && food.CurLevelPercentage < 0.6f && !WildGods.Feed(r, map)
                        && !map.mapPawns.AllPawnsSpawned.Any(a => a.RaceProps.Animal && a.Faction == null && !Linarian.Is(a))) food.CurLevelPercentage = 0.85f;
                    if (LinarianManners.IsKind(r)) { }
                    else if (r.primal) { if (!LinarianGods.Tribute(r, map)) PrimalNight(r, map); }   // fed, the cruel goddess spares them
                    else WildNight(r, map);
                    Gene_LinarianSong mg = Linarian.Song(her);
                    if (mg != null && !LinarianManners.IsKind(r)) LinarianManners.Night(r, map, mg, Colonists(map));
                    LinarianCourtship.Night(r, map);
                }
                if (r.leaving && !held && her.Spawned && !Wave(map).Any())
                {
                    Job j = JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomEdgeCell(map));
                    j.exitMapOnArrival = true;
                    if (her.CurJobDef != JobDefOf.Goto) her.jobs.StartJob(j, JobCondition.InterruptForced);
                }
            }
        }

        /// <summary>By day she lies at her lair, unseen.</summary>
        private static void RestByDay(HeraldRecord r)
        {
            Pawn her = r.her;
            if (her.MentalState != null) return;
            if (r.lair.IsValid && (her.Position - r.lair).LengthHorizontal > 3f)
            {
                if (her.CurJobDef != JobDefOf.Goto) her.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, r.lair), JobCondition.InterruptForced);
                return;
            }
            if (her.CurJobDef == JobDefOf.LayDown) return;
            Job lay = JobMaker.MakeJob(JobDefOf.LayDown, her.Position);
            lay.forceSleep = true;
            her.jobs.StartJob(lay, JobCondition.InterruptForced);
        }

        private static void Wander(HeraldRecord r, int now)
        {
            Pawn her = r.her;
            // never under a pylon: she leaves its reach at once
            if (SongShelter.ShieldedCell(her.Map, her.Position))
            {
                if (CellFinder.TryFindRandomCellNear(her.Position, her.Map, 40, c => c.Standable(her.Map) && !SongShelter.ShieldedCell(her.Map, c), out IntVec3 outside))
                    her.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, outside), JobCondition.InterruptForced);
                return;
            }
            if (r.peckTarget != null || r.raidTarget != null) return;
            if (her.CurJob != null && her.CurJob.exitMapOnArrival && !r.leaving) { her.jobs.EndCurrentJob(JobCondition.InterruptForced); r.nextMove = 0; }
            if (now < r.nextMove || her.CurJobDef == JobDefOf.Goto || her.MentalState != null) return;
            r.nextMove = now + Rand.Range(600, 1800);
            if (!r.lair.IsValid) r.lair = her.Position;
            if (!CellFinder.TryFindRandomCellNear(r.lair, her.Map, 15, c => c.Standable(her.Map) && !SongShelter.ShieldedCell(her.Map, c) && her.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out IntVec3 to)) return;
            Job j = JobMaker.MakeJob(JobDefOf.Goto, to);
            j.locomotionUrgency = LocomotionUrgency.Amble;
            her.jobs.StartJob(j, JobCondition.InterruptForced);
        }

        // ---------------------------------------------------------------- the peck

        /// <summary>By night she comes for her target as a veiled crow: puts them to sleep, pecks once and is gone. The peck
        /// leaves a strange ache on the part it struck - no scar, no name to it - and later, one who bears it and gets
        /// in her way simply drops.</summary>
        private static void Peck(HeraldRecord r, int now)
        {
            if (LinarianGods.ScaredOff(r.her)) return;
            Pawn her = r.her, t = r.peckTarget;
            if (t == null) return;
            if (t.Dead || !t.Spawned || t.Map != her.Map || now > r.peckUntil || SongShelter.Shielded(t)) { r.peckTarget = null; return; }
            if (!Night(her.Map)) return;   // it waits for the dark
            if (LinarianVeil.InForm(her) != LinarianTactics.Crow) LinarianVeil.SetForm(her, LinarianTactics.Crow);
            if (!LinarianVeil.Veiled(her)) LinarianVeil.Veil(her, GenDate.TicksPerHour);
            if ((t.Position - her.Position).LengthHorizontal <= 1.9f)
            {
                if (!LinarianTactics.Asleep(t)) LinarianHediffs.Sleep(t, GenDate.TicksPerHour * 2, her);
                BodyPartRecord part = t.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside).RandomElementWithFallback();
                if (part != null)
                {
                    Hediff h = HediffMaker.MakeHediff(LinarianHediffs.Avir_StrangeAche, t, part);
                    t.health.AddHediff(h, part);
                    var c = h.TryGetComp<HediffComp_SongSource>();
                    if (c != null) c.herald = her;
                }
                r.peckTarget = null;
                Job back = JobMaker.MakeJob(JobDefOf.Goto, r.lair.IsValid ? r.lair : her.Position);
                back.locomotionUrgency = LocomotionUrgency.Sprint;
                her.jobs.StartJob(back, JobCondition.InterruptForced);
                return;
            }
            if (her.CurJobDef != JobDefOf.Goto || her.CurJob.targetA.Thing != t)
            {
                Job j = JobMaker.MakeJob(JobDefOf.Goto, t);
                j.locomotionUrgency = LocomotionUrgency.Sprint;
                her.jobs.StartJob(j, JobCondition.InterruptForced);
            }
        }

        // ---------------------------------------------------------------- the crow at the stores

        private static void StartRaid(HeraldRecord r, Map map)
        {
            Thing food = LinarianManners.Loot(map, Rand.Chance(0.25f)) ?? LinarianManners.Loot(map, false);
            if (food == null) return;
            r.raidTarget = food;
            r.raidUntil = Find.TickManager.TicksGame + GenDate.TicksPerHour * 5;
            r.carrying = false;
        }

        /// <summary>The lullaby's Singer, as a crow, flies into the colony by night and takes food; whoever tries to drive
        /// her off falls asleep (EvadeTheRush).</summary>
        private static void CrowRaid(HeraldRecord r, int now)
        {
            if (LinarianGods.ScaredOff(r.her)) return;
            Pawn her = r.her;
            if (r.raidTarget == null) return;
            Map map = her.Map;
            if (now > r.raidUntil || !Night(map) && !r.carrying) { r.raidTarget = null; return; }
            if (LinarianVeil.InForm(her) != LinarianTactics.Crow) LinarianVeil.SetForm(her, LinarianTactics.Crow);
            if (LinarianVeil.Veiled(her)) LinarianVeil.Unveil(her);   // a crow at the stores is seen - that is the point
            if (r.carrying)
            {
                if (!r.lair.IsValid || (her.Position - r.lair).LengthHorizontal <= 2f)
                {
                    r.raidTarget = null; r.carrying = false;
                    her.inventory?.innerContainer.TryDropAll(her.Position, map, ThingPlaceMode.Near);   // her stash, not her stomach
                    return;
                }
                if (her.CurJobDef != JobDefOf.Goto) her.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, r.lair), JobCondition.InterruptForced);
                return;
            }
            Thing food = r.raidTarget;
            if (food.Destroyed || !food.Spawned || SongShelter.ShieldedCell(map, food.Position)) { r.raidTarget = null; return; }
            if ((food.Position - her.Position).LengthHorizontal <= 1.5f)
            {
                int take = Mathf.Min(food.stackCount, Rand.RangeInclusive(5, 15));
                Thing taken = food.SplitOff(take);
                if (her.inventory == null || !her.inventory.innerContainer.TryAdd(taken)) GenPlace.TryPlaceThing(taken, her.Position, map, ThingPlaceMode.Near);
                r.carrying = true;
                FleckMaker.ThrowDustPuff(her.DrawPos, map, 1f);
                return;
            }
            if (her.CurJobDef != JobDefOf.Goto || her.CurJob.targetA.Thing != food)
                her.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, food), JobCondition.InterruptForced);
        }

        private static IEnumerable<Pawn> Wave(Map map) =>
            map.mapPawns.AllPawnsSpawned.Where(a => a.RaceProps.Animal && !a.Dead && (a.MentalStateDef == MentalStateDefOf.Manhunter || a.MentalStateDef == MentalStateDefOf.ManhunterPermanent));

        private static List<Pawn> Colonists(Map map) =>
            map.mapPawns.FreeColonistsSpawned.Where(c => !c.Dead && LinarianTactics.Takeable(c)).ToList();

        // ---------------------------------------------------------------- the nights

        private static void WildNight(HeraldRecord r, Map map)
        {
            Pawn her = r.her;
            // the beasts of the land come to her and die beside her
            foreach (Pawn a in map.mapPawns.AllPawnsSpawned.Where(a => a.RaceProps.Animal && a.Faction == null && !a.Dead && a.MentalStateDef == null)
                         .InRandomOrder().Take(Rand.RangeInclusive(1, 2)).ToList())
                LinarianMental.StartHunt(a, her, GenDate.TicksPerHour * 4);
            // a sleeper wakes to a song
            if (Rand.Chance(0.35f)) Insomnia(map);
            Gene_LinarianSong g = Linarian.Song(her);
            if (g != null && g.Has("D3"))
            {
                // the dreaming one never calls: she drinks the sleepers where they lie, night by night
                if (r.nights >= 2)
                    foreach (Pawn c in Colonists(map).Where(x => LinarianTactics.Asleep(x) && LinarianSource.Of(x, LinarianHediffs.Avir_SlowDevour) == null).InRandomOrder().ToList())
                        if (!LinarianActs.StartSlowDevour(her, g, c)) break;
                return;
            }
            // the lullaby's one: a crow at the stores
            if (r.main == "L")
            {
                if (r.raidTarget == null && Rand.Chance(0.7f)) StartRaid(r, map);
                return;
            }
            // after a few nights: one colonist at a time
            if (r.nights >= 3 && !Calling(her) && Rand.Chance(0.4f)) Call(r, map);
        }

        private static void PrimalNight(HeraldRecord r, Map map)
        {
            if (!Wave(map).Any())
            {
                string inc;
                switch (r.stage)
                {
                    case 0: inc = "AnimalInsanitySingle"; break;
                    case 1: inc = "AnimalInsanityMass"; break;
                    case 2: inc = "ManhunterPack"; break;
                    default: inc = r.stage % 2 == 1 ? "RaidEnemy" : "ManhunterPack"; break;
                }
                if (r.main == "W" && r.stage >= 1 && Rand.Chance(0.5f)) inc = "RaidEnemy";   // the Will's one calls her people down on them
                if (Fire(map, inc, inc == "RaidEnemy" ? PrimalFaction() : null)) r.stage++;
            }
            if (Rand.Chance(0.5f))
            {
                if (Rand.Bool) Call(r, map);
                else
                {
                    // quiet malice: one of them, asleep and pecked, with an ache no one can explain
                    Pawn c = Colonists(map).Where(x => !x.Downed).RandomElementWithFallback();
                    if (c != null) { r.peckTarget = c; r.peckUntil = Find.TickManager.TicksGame + GenDate.TicksPerHour * 6; }
                }
            }
            if (Rand.Chance(0.3f)) Insomnia(map);
            // the cult: one or two colonists more begin to worship her every night
            Gene_LinarianSong g = Linarian.Song(r.her);
            if (g != null && g.Has("C1"))
                foreach (Pawn c in Colonists(map).Where(x => LinarianSource.Of(x, LinarianHediffs.Avir_Adoration) == null).InRandomOrder().Take(Rand.RangeInclusive(1, 2)).ToList())
                    LinarianActs.Adore(r.her, g, c, 0.1f);
        }

        /// <summary>A shadowed Herald fouls the land: fog by day, every light dead by night, and once, the eternal night.</summary>
        private static void ShadowWork(HeraldRecord r, Map map, bool night)
        {
            Gene_LinarianSong g = Linarian.Song(r.her);
            if (g == null || !g.shadow || !g.Has("T4")) return;
            int day = GenDate.DaysPassed;
            int hour = GenLocalDate.HourOfDay(map);
            if (night) LinarianShadow.Flare(map);
            else if (hour >= 7 && hour < 18 && r.lastFog != day)
            {
                r.lastFog = day;
                MapComponent_BlackFog fog = MapComponent_BlackFog.Of(map);
                for (int i = 0; i < Rand.RangeInclusive(3, 5); i++)
                    fog?.Raise(CellFinder.RandomCell(map), Rand.Range(14f, 24f), (18 - hour) * GenDate.TicksPerHour);
            }
            if (g.Has("T5") && !r.usedEternal && r.nights >= 4 && night && Rand.Chance(0.002f))
            {
                r.usedEternal = true;
                LinarianShadow.EternalNight(map, r.her);
            }
        }

        public static bool Fire(Map map, string incident, Faction faction = null)
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(incident);
            if (def == null) return false;
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.forced = true;
            if (faction != null) parms.faction = faction;
            return def.Worker.TryExecute(parms);
        }

        private static void Insomnia(Map map)
        {
            Pawn c = map.mapPawns.FreeColonistsSpawned.Where(x => x.InBed() && !x.Awake() && LinarianTactics.Takeable(x)).RandomElementWithFallback();
            if (c == null) return;
            RestUtility.WakeUp(c);
            c.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Sad, "an aria in a dream", true);
            ThoughtDef t = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_SongInsomnia");
            if (t != null) c.needs?.mood?.thoughts?.memories?.TryGainMemory(t);
        }

        private static bool Calling(Pawn her) => her.Map.mapPawns.AllPawnsSpawned.Any(q => LinarianMental.CalledBy(q) == her);

        private static void Call(HeraldRecord r, Map map)
        {
            Pawn her = r.her;
            Gene_LinarianSong g = Linarian.Song(her);
            if (g == null) return;
            List<Pawn> cs = Colonists(map).Where(x => !x.Downed && !x.IsPrisoner && !LinarianManners.Spared(r, x)).ToList();
            Pawn target = r.grudge != null && cs.Contains(r.grudge) ? r.grudge : cs.RandomElementWithFallback();
            if (target == null || !Rand.Chance(LinarianTactics.Chance(her, g, target, true, "W"))) return;
            if (!r.primal) LinarianVeil.Veil(her, GenDate.TicksPerHour * 3);   // she hunts unseen
            if (LinarianMental.StartCalled(target, her, GenDate.TicksPerHour * 4))
                Find.LetterStack.ReceiveLetter("Called into the night", target.LabelShortCap + " has risen in the dark and walks out of the colony, as if someone were calling.",
                    LetterDefOf.ThreatSmall, target);
        }

        /// <summary>The called beside her: the wild one puts them to sleep and drinks or devours; the Primal one
        /// devours. One taken into a prison cell is let go.</summary>
        private static void HandleCalled(HeraldRecord r)
        {
            Pawn her = r.her;
            foreach (Pawn q in her.Map.mapPawns.AllPawnsSpawned.Where(q => LinarianMental.CalledBy(q) == her).ToList())
            {
                if (q.RaceProps.Animal && q.IsHashIntervalTick(240) && !q.CanReach(her, PathEndMode.Touch, Danger.Deadly)) { LinarianManners.BreakOut(q); continue; }
                if (q.IsPrisoner || q.InMentalState == false) { q.MentalState?.RecoverFromState(); continue; }
                if ((q.Position - her.Position).LengthHorizontal > 1.9f) continue;
                q.MentalState.RecoverFromState();
                if ((r.primal || Rand.Bool) && q.RaceProps.Humanlike)
                {
                    LinarianVeil.Unveil(her);
                    LinarianVeil.ClearForm(her);
                    LinarianDevour.Devour(her, q);
                    LinarianVeil.Veil(her, GenDate.TicksPerHour);
                    if (!r.primal) Disguise(r);
                }
                else if (q.RaceProps.Animal && q.Faction == null)
                {
                    // a wild beast, called because she was hungry: the hunt ends as hunts do
                    LinarianDevour.Devour(her, q);
                    if (her.needs?.food != null) her.needs.food.CurLevelPercentage = 1f;
                }
                else
                {
                    LinarianHediffs.Sleep(q, GenDate.TicksPerHour * 2);
                    HediffDef drained = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StrengthDrained");
                    if (drained != null) q.health.AddHediff(drained);
                    Gene_ArcaneHunger.Feed(her, 0.1f);
                    Messages.Message(q.LabelShortCap + " lies asleep in the dark. Something has drunk from them.", q, MessageTypeDefOf.NegativeEvent, false);
                }
                if (q == r.grudge) r.grudge = null;
            }
        }

        // ---------------------------------------------------------------- seen through

        static partial void RevealedImplP(Pawn her, Thing by)
        {
            HeraldRecord r = Record(her);
            if (r == null || her.IsPrisoner) return;
            Map map = her.Map;
            int now = Find.TickManager.TicksGame;
            r.revealedUntil = now + GenDate.TicksPerHour;
            bool struck = by is Pawn bp && bp.Faction == Faction.OfPlayer && bp.CurJob != null && (bp.CurJobDef == JobDefOf.AttackStatic || bp.CurJobDef == JobDefOf.AttackMelee || bp.CurJobDef == JobDefOf.Hunt);
            if (r.devoted && map != null && map.mapPawns.FreeColonistsSpawned.Any(WildGods.Worships))
            {
                LinarianVeil.Veil(her, GenDate.TicksPerHour);   // called and worshipped: seen, she only steps back into the dark
                return;
            }
            if (LinarianManners.IsKind(r))
            {
                r.leaving = true;
                r.habits.Remove(LinarianManners.Kind);
                if (struck) Find.LetterStack.ReceiveLetter("A Singer", "The beast was not a beast - and it had never meant you any harm. Struck, the Singer only looks at you, deeply hurt, and turns away. She will not come back.", LetterDefOf.NeutralEvent, her);
                LinarianVeil.Veil(her, GenDate.TicksPerHour);
                return;
            }
            Gene_LinarianSong sg = Linarian.Song(her);
            if (sg != null && sg.Has("D4") && by is Pawn offender) sg.offender = offender;
            if (r.primal)
            {
                Faction pf = PrimalFaction();
                if (pf != null && her.Faction != pf) her.SetFaction(pf);
                if (map != null && sg != null && her.Spawned) LinarianPack.AgainstTheRaid(her, sg, true);
                else if (map != null) Fire(map, "ManhunterPack");
                r.leaving = false;   // while her beasts live, she stays on her land
                Find.LetterStack.ReceiveLetter("A Singer", "The beast was not a beast. A Singer of the Primal Hunt stands revealed - and every beast of the land falls on you at her song.", LetterDefOf.ThreatBig, her);
            }
            else if (struck)
            {
                r.grudge = (Pawn)by;
                Find.LetterStack.ReceiveLetter("A Singer", "The beast was not a beast. A Singer, struck, vanishes into her veil - and she will remember " + r.grudge.LabelShort + ".", LetterDefOf.ThreatSmall, her);
            }
            else
            {
                // territorial: seen through, she does not leave her land - she makes it unwelcoming
                if (map != null) Fire(map, "ManhunterPack");
                Find.LetterStack.ReceiveLetter("A Singer", "The beast was not a beast. Seen through, the Singer raises the beasts of the land against you. This is her land, and she does not mean to leave it.", LetterDefOf.ThreatBig, her);
            }
            LinarianVeil.Veil(her, GenDate.TicksPerHour / 4);
        }

        // ---------------------------------------------------------------- tamed and hunted

        /// <summary>Away from them at a run, as a hunted beast goes.</summary>
        public static void Flee(Pawn her, List<Thing> from)
        {
            if (her?.Map == null || her.Downed || her.CurJobDef == JobDefOf.Flee) return;
            IntVec3 to = CellFinderLoose.GetFleeDest(her, from, 24f);
            if (!to.IsValid || to == her.Position) return;
            Job run = JobMaker.MakeJob(JobDefOf.Flee, to, from.FirstOrDefault());
            run.locomotionUrgency = LocomotionUrgency.Sprint;
            her.jobs?.StartJob(run, JobCondition.InterruptForced);
        }

        public static void Hunt(Pawn hunter, Pawn her)
        {
            // one with the lullaby is not caught so easily: the hunter drops asleep, and in the night a crow comes for them
            HeraldRecord r = G?.records.FirstOrDefault(x => x.her == her);
            Gene_LinarianSong g = Linarian.Song(her);
            if (r != null && LinarianManners.IsKind(r)) { LinarianVeil.Reveal(her, hunter); return; }   // hunted: she is hurt, and goes
            if (r != null && !r.primal && g != null && g.Has("L1") && LinarianTactics.Takeable(hunter))
            {
                LinarianHediffs.Sleep(hunter, GenDate.TicksPerHour, her);
                r.peckTarget = hunter;
                r.peckUntil = Find.TickManager.TicksGame + GenDate.TicksPerDay;
                LinarianVeil.Veil(her, GenDate.TicksPerHour);
                return;
            }
            // the hunter has to see what the beast is: the deaf and the true huntress do; the rest only by their own mind
            float sees = LinarianVeil.SeesThrough(her, hunter);
            if (sees <= 0f) sees = Mathf.Clamp01(0.2f / Mathf.Max(0.3f, LinarianTactics.Soc(her, hunter, "G")));
            if (Rand.Chance(sees)) { LinarianVeil.Reveal(her, hunter); return; }
            // not seen: a beast that bolts, nothing more
            Flee(her, new List<Thing> { hunter });
            LinarianVeil.Veil(her, GenDate.TicksPerHour);
            if (hunter.Faction == Faction.OfPlayer) Messages.Message("The beast " + hunter.LabelShort + " was hunting bolts and is lost from sight.", hunter, MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>When she was last tried: like any beast, she is not tried again for half a day.</summary>
        public static readonly Dictionary<int, int> lastTameAttempt = new Dictionary<int, int>();

        public static bool TameRested(Pawn her) =>
            !lastTameAttempt.TryGetValue(her.thingIDNumber, out int t) || Find.TickManager.TicksGame - t > GenDate.TicksPerHour * 12;

        public static void Tame(Pawn tamer, Pawn her)
        {
            HeraldRecord r = Record(her);
            if (r == null) return;
            if (PawnsFinder.AllMaps_FreeColonists.Any(c => WildGods.Accepted(c, her)))
            {
                Messages.Message("She is not to be tamed: your people knelt to her. She is served, not owned.", her, MessageTypeDefOf.RejectInput, false);
                HeraldDesignations.Clear(her);
                return;
            }
            if (r.primal)
            {
                LinarianVeil.Unveil(her);
                LinarianVeil.ClearForm(her);
                LinarianDevour.Devour(her, tamer);
                Fire(her.Map, "ManhunterPack");
                Find.LetterStack.ReceiveLetter("The beast's answer", "The beast " + tamer.LabelShort + " tried to tame was a Singer of the Primal Hunt. She took " + tamer.LabelShort + " - and the beasts of the land are coming.", LetterDefOf.ThreatBig, her);
                LinarianVeil.Veil(her, GenDate.TicksPerHour);
                return;
            }
            if (Rand.Chance(0.1f))
            {
                LinarianVeil.ClearForm(her);
                LinarianVeil.Unveil(her);
                her.SetFaction(Faction.OfPlayer);
                Ideo own = Faction.OfPlayer.ideos?.PrimaryIdeo;
                Ideo apostate = own != null ? GameComponent_AvirApostasy.Instance?.CopyOf(own) : null;
                if (apostate != null && her.ideo != null) her.ideo.SetIdeo(apostate);
                GameComponent_Heralds.Instance.records.Remove(r);
                Find.LetterStack.ReceiveLetter("A Singer joins", "The beast " + tamer.LabelShort + " was taming was a Singer. She has decided to stay - in her own way, and in no faith but her own.", LetterDefOf.PositiveEvent, her);
                return;
            }
            // a failed attempt: mostly she is only a beast that will not be tamed...
            if (Rand.Chance(0.75f))
            {
                InteractionDef balk = DefDatabase<InteractionDef>.GetNamedSilentFail("Avir_HeraldBalks");
                if (balk != null) Find.PlayLog.Add(new PlayLogEntry_Interaction(balk, her, tamer, null));
                if (her.Spawned) MoteMaker.ThrowText(her.DrawPos, her.Map, "Taming failed", 3.65f);
                return;
            }
            // ...and sometimes the beast speaks back
            InteractionDef spoke = DefDatabase<InteractionDef>.GetNamedSilentFail("Avir_HeraldSpokeBack");
            if (spoke != null) Find.PlayLog.Add(new PlayLogEntry_Interaction(spoke, her, tamer, null));
            bool deaf = tamer.GetStatValue(StatDefOf.PsychicSensitivity) <= 0.01f;
            TraitDef nerves = DefDatabase<TraitDef>.GetNamedSilentFail("Nerves");
            bool iron = nerves != null && tamer.story?.traits?.DegreeOfTrait(nerves) >= 2;
            bool panic = !deaf && !iron && Rand.Chance(0.5f);
            MentalStateDef fright = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_BeastFright") ?? MentalStateDefOf.PanicFlee;
            if (panic) tamer.mindState.mentalStateHandler.TryStartMentalState(fright, "The Beast spoke back.", true);
            if (!deaf && !iron)
            {
                ThoughtDef t = DefDatabase<ThoughtDef>.GetNamedSilentFail(panic ? "Avir_HeraldSpokeBackPanic" : "Avir_HeraldSpokeBack");
                if (t != null) tamer.needs?.mood?.thoughts?.memories?.TryGainMemory(t);
            }
        }
    }

    // ---------------------------------------------------------------- the disguise in the game's lists

    [HarmonyPatch(typeof(MainTabWindow_Wildlife), "Pawns", MethodType.Getter)]
    public static class Patch_HeraldWildlife
    {
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            Map map = Find.CurrentMap;
            if (map == null || GameComponent_Heralds.Instance == null || GameComponent_Heralds.Instance.records.Count == 0) return;
            List<Pawn> extra = GameComponent_Heralds.Instance.records.Select(r => r.her)
                .Where(p => p != null && p.Spawned && p.Map == map && !p.Position.Fogged(map) && LinarianPresence.Disguised(p) && !LinarianVeil.Veiled(p)).ToList();
            if (extra.Count > 0) __result = __result.Concat(extra).Distinct().ToList();
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_Predator), "GetIconFor")]
    public static class Patch_HeraldPredatorIcon
    {
        private static Texture2D icon;
        public static void Postfix(Pawn pawn, ref Texture2D __result)
        {
            if (__result == null && LinarianPresence.Disguised(pawn)) __result = icon ?? (icon = ContentFinder<Texture2D>.Get("UI/Icons/Animal/Predator"));
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.LabelNoCount), MethodType.Getter)]
    public static class Patch_HeraldLabel
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (LinarianPresence.Disguised(__instance)) __result = HeraldLabels.Beast(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.LabelShort), MethodType.Getter)]
    public static class Patch_HeraldLabelShort
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (LinarianPresence.Disguised(__instance)) __result = HeraldLabels.Beast(__instance);
        }
    }

    public static class HeraldLabels
    {
        public static string Beast(Pawn p)
        {
            switch (LinarianVeil.InForm(p))
            {
                case LinarianTactics.Crow: return "masked crow";
                case LinarianTactics.Wolf: return "dusk she-wolf";
                default: return "ashen doe";
            }
        }
    }

    /// <summary>Hunting the beast, or taming it.</summary>
    public class FloatMenuOptionProvider_Herald : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn p = context?.FirstSelectedPawn;
            if (p == null || !(clickedThing is Pawn her) || !LinarianPresence.Disguised(her) || p.Faction != Faction.OfPlayer) yield break;
            string beast = HeraldLabels.Beast(her);
            yield return new FloatMenuOption("Hunt the " + beast, () => HeraldJobs.Order(p, her, false));
            int animals = p.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            if (animals < 12) yield return new FloatMenuOption("Tame the " + beast + " (needs Animals 12)", null);
            else yield return new FloatMenuOption("Tame the " + beast, () => HeraldJobs.Order(p, her, true));
        }
    }

    public static class HeraldJobs
    {
        public static void Order(Pawn p, Pawn her, bool tame)
        {
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(tame ? "Avir_TameHerald" : "Avir_ApproachHerald");
            if (def == null) return;
            Job j = JobMaker.MakeJob(def, her);
            p.jobs.TryTakeOrderedJob(j, JobTag.Misc);
        }
    }

    public class JobDriver_ApproachHerald : JobDriver
    {
        private Pawn Her => job.targetA.Pawn;
        private bool TameJob => job.def.defName == "Avir_TameHerald";   // its own job: a count can be rewritten by others

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => LinarianVeil.Veiled(Her));   // gone under her veil: nothing there to reach
            if (TameJob) yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            else
            {
                Toil near = ToilMaker.MakeToil("ApproachHeraldNear");
                near.initAction = () => pawn.pather.StartPath(Her, PathEndMode.Touch);
                near.tickIntervalAction = delta =>
                {
                    if ((pawn.Position - Her.Position).LengthHorizontal <= 12f && GenSight.LineOfSight(pawn.Position, Her.Position, pawn.Map)) ReadyForNextToil();
                };
                near.defaultCompleteMode = ToilCompleteMode.PatherArrival;
                yield return near;
            }
            Toil act = ToilMaker.MakeToil("ApproachHeraldAct");
            act.defaultCompleteMode = ToilCompleteMode.Delay;
            act.defaultDuration = TameJob ? 300 : 90;   // no progress bar: a tamer at a beast shows none
            act.handlingFacing = true;
            act.tickIntervalAction = delta => pawn.rotationTracker.FaceTarget(Her);
            yield return act;
            // the answer comes after the attempt, not while the job is being torn down: what it does (panic, sleep,
            // a mental break) ends this job, and ending it from inside its own cleanup breaks the game
            Toil answer = ToilMaker.MakeToil("ApproachHeraldAnswer");
            answer.initAction = () =>
            {
                Pawn her = Her, me = pawn;
                if (me.Dead || her == null || her.Dead || !LinarianPresence.Disguised(her)) return;
                bool tame = TameJob;
                EndJobWith(JobCondition.Succeeded);
                if (tame)
                {
                    LinarianPresence.lastTameAttempt[her.thingIDNumber] = Find.TickManager.TicksGame;
                    LinarianPresence.Tame(me, her);
                    // a failed attempt leaves the order standing, as with any beast; only her answer ends it
                    if (!LinarianPresence.Disguised(her) || her.Faction == Faction.OfPlayer) HeraldDesignations.Clear(her);
                }
                else
                {
                    HeraldDesignations.Clear(her);
                    LinarianPresence.Hunt(me, her);
                }
            };
            answer.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return answer;
        }
    }

    /// <summary>A beast that is attacked does not stand there: struck at in melee, she runs; shot at, she is gone under
    /// her veil before the shot.</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_HeraldEvades
    {
        public static bool Prefix(Job newJob, Pawn ___pawn)
        {
            if (newJob?.def == null || ___pawn == null || ___pawn.Faction != Faction.OfPlayer) return true;
            JobDef d = newJob.def;
            bool melee = d == JobDefOf.AttackMelee;
            if (!melee && d != JobDefOf.AttackStatic && d != JobDefOf.Hunt && d != JobDefOf.UseVerbOnThing && d != JobDefOf.UseVerbOnThingStatic) return true;
            if (!(newJob.targetA.Thing is Pawn her) || !LinarianPresence.Disguised(her) || her.Downed || LinarianVeil.Veiled(her)) return true;
            if (melee)
            {
                LinarianPresence.Flee(her, new List<Thing> { ___pawn });
                return true;   // let them chase her
            }
            LinarianVeil.Veil(her, GenDate.TicksPerHour);
            if (her.Spawned) FleckMaker.ThrowSmoke(her.DrawPos, her.Map, 1.4f);
            LinarianPresence.Flee(her, new List<Thing> { ___pawn });
            return false;   // nothing left to shoot at
        }
    }

    // ---------------------------------------------------------------- arrival incidents

    public class IncidentWorker_HeraldArrives : IncidentWorker
    {
        private bool Primal => def.defName.Contains("Primal");

        /// <summary>Offerings to the gods made with no Singer on the land call one.</summary>
        public override float ChanceFactorNow(IIncidentTarget target)
        {
            float f = base.ChanceFactorNow(target) * (1f + (GameComponent_WildGods.Instance?.favour ?? 0f));
            // the twilight and the burning forests are their own land: there they come far more often
            string biome = (target as Map)?.Biome?.defName;
            if (biome == "Avir_TwilightForest" || biome == "Avir_BurningForest") f *= 3f;
            return f;
        }

        /// <summary>No Singer lives where the year goes past what her body bears: no month colder than -30 on
        /// average (the ice of the poles, the tundra), none hotter than +30 (the tropics, the deserts), and she
        /// does not come in such weather either.</summary>
        public static bool Bearable(Map map)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 12; i++)
            {
                float t = GenTemperature.AverageTemperatureAtTileForTwelfth(map.Tile, (Twelfth)i);
                lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t);
            }
            float now = map.mapTemperature.OutdoorTemp;
            return lo >= -30f && hi <= 30f && now >= -30f && now <= 30f;
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms.target is Map map) || !map.IsPlayerHome) return false;
            if (!Bearable(map)) return false;
            if (GameComponent_Heralds.Instance?.records.Any(r => r.her?.MapHeld == map) == true) return false;
            if (Primal && LinarianPresence.PrimalFaction() == null) return false;
            return true;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            Pawn her = LinarianPresence.Arrive(map, Primal);
            if (her == null) return false;
            if (GameComponent_WildGods.Instance != null) GameComponent_WildGods.Instance.favour = 0f;   // the offering is answered
            return true;   // no letter: she is only a beast among the wildlife
        }
    }

    /// <summary>A hidden Singer never wanders off the map on her own: the generic exit of a stray person is refused
    /// until she means to go.</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_HeraldStays
    {
        public static bool Prefix(Job newJob, Pawn ___pawn)
        {
            if (newJob == null || !newJob.exitMapOnArrival || ___pawn == null) return true;
            HeraldRecord r = GameComponent_Heralds.Instance?.records.FirstOrDefault(x => x.her == ___pawn);
            return r == null || r.leaving || ___pawn.IsPrisoner;
        }
    }

    /// <summary>In the wildlife tab a disguised Singer shows her beast, not her face.</summary>
    [HarmonyPatch]
    public static class Patch_HeraldWildlifeIcon
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(PawnColumnWorker_Label), "DoCell");
        static bool Prepare() => TargetMethod() != null;

        public static bool Prefix(Rect rect, Pawn pawn)
        {
            if (!LinarianPresence.Disguised(pawn)) return true;
            List<Graphic> layers = LinarianFormGraphics.Layers(pawn, LinarianVeil.InForm(pawn));
            Rect icon = new Rect(rect.x, rect.y, rect.height, rect.height);
            foreach (Graphic g in layers)
            {
                Material m = g.MatSouth;
                GUI.color = m.color;
                GUI.DrawTexture(icon, m.mainTexture, ScaleMode.ScaleToFit);
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(new Rect(icon.xMax + 4f, rect.y, rect.width - icon.width - 4f, rect.height), pawn.LabelCap);
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(rect)) CameraJumper.TryJumpAndSelect(pawn);
            return false;
        }
    }
}
