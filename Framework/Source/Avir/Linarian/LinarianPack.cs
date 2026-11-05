using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    /// <summary>
    /// THE PACK, reworked. Beasts have no will to bend: on this path her chance rests on her own skill with animals,
    /// and every song of it teaches her that skill.
    ///   The hunt (the pack renamed): once - the beasts round her answer and wait; again, anywhere - they go there,
    ///   falling on her foes on the way, and circle the place wide. Pointed twice at the same place, they hunt the wild
    ///   beasts there. While they hunt she learns, as a handler does.
    ///   Aria-hunt: the gift - one beast marked for hunting at a time comes to her to die; the song - a whole herd walks
    ///   to her home and drops senseless for hours, marked for the hunters' knives.
    ///   Form of the she-wolf: beasts fighting near her wear their prey down - every bite harries it - and she learns from
    ///   every bite.
    ///   Call a predator: beasts of the wild, mixed, more and stronger the stronger she is; the gift: she draws beasts to a
    ///   land that runs short of them (twice a day at most) - any Singer who has it, wild or not.
    ///   Beasts against the raid: most of the land's beasts (her skill decides); pressed again within the hour, the rest -
    ///   the great beasts too. The gift: her beasts feel a fifth of their pain.
    /// </summary>
    public static class LinarianPack
    {
        public const float LearnPerSong = 400f;

        public static float Chance(Pawn her, Gene_LinarianSong g, Pawn a)
        {
            int skill = her.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            return Mathf.Clamp01(0.25f + 0.3f * g.Power + 0.025f * skill);
        }

        public static void Teach(Pawn her, float xp) => her.skills?.GetSkill(SkillDefOf.Animals)?.Learn(xp);

        public static bool HuntActive(Pawn her) =>
            her?.Map != null && her.Map.mapPawns.AllPawnsSpawned.Any(a => a.MentalState is MentalState_SongHunt h && h.herald == her);

        // ---------------------------------------------------------------- the hunt

        public static void Gather(Pawn p, Gene_LinarianSong g, float r)
        {
            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_PackHunt");
            int n = 0;
            int ticks = LinarianZone.Ticks(GenDate.TicksPerHour * 3f, g);
            foreach (Pawn a in GenRadial.RadialDistinctThingsAround(p.Position, p.Map, r, true).OfType<Pawn>()
                         .Where(a => a.RaceProps.Animal && a.Faction == null && !a.Dead && !a.Downed && !Linarian.Is(a)).ToList())
            {
                if (!Rand.Chance(Chance(p, g, a))) continue;
                if (LinarianMental.Start(a, p, def, ticks)) n++;
            }
            Teach(p, LearnPerSong);
            g.lastHuntPoint = IntVec3.Invalid;
            LinarianTactics.Notify(p, n > 0 ? n + " beasts answer her and wait. Point where they go." : "No beast round her answers.");
        }

        public static void Point(Pawn p, Gene_LinarianSong g, IntVec3 at)
        {
            bool again = g.lastHuntPoint.IsValid && (g.lastHuntPoint - at).LengthHorizontal <= 8f;
            g.lastHuntPoint = at;
            foreach (Pawn a in p.Map.mapPawns.AllPawnsSpawned.Where(a => a.MentalState is MentalState_SongHunt h && h.herald == p).ToList())
            {
                var h = (MentalState_SongHunt)a.MentalState;
                h.point = at;
                h.hunting = again;
            }
            LinarianTactics.Notify(p, again ? "her pack hunts the wild beasts there." : "her pack goes where she points.");
        }

        // ---------------------------------------------------------------- the aria-hunt: a whole herd

        public static void DriveHerd(Pawn p, Gene_LinarianSong g, IntVec3 at, float r)
        {
            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_HerdDrive");
            float max = LinarianSongHunt.MaxSize(g);
            IntVec3 home = Home(p);
            int n = 0;
            foreach (Pawn a in LinarianZone.Around(p.Map, at, r).Where(a => a.RaceProps.Animal && a.Faction == null && !a.Dead && !a.Downed
                         && a.BodySize <= max && !Linarian.Is(a) && !(a.MentalState is MentalState_Song)).ToList())
            {
                if (!Rand.Chance(Chance(p, g, a))) continue;
                if (!LinarianMental.Start(a, p, def, GenDate.TicksPerHour * 6)) continue;
                if (a.MentalState is MentalState_SongHerd h) h.dest = home;
                n++;
            }
            Teach(p, LearnPerSong);
            LinarianTactics.Notify(p, n > 0 ? n + " beasts of the herd walk to her home, to be taken." : "The herd does not hear her.");
        }

        /// <summary>Her home: the heart of the home area, or where she stands.</summary>
        public static IntVec3 Home(Pawn p)
        {
            var cells = p.Map.areaManager.Home?.ActiveCells?.ToList();
            if (cells == null || cells.Count == 0) return p.Position;
            IntVec3 c = new IntVec3((int)cells.Average(x => x.x), 0, (int)cells.Average(x => x.z));
            return CellFinder.TryFindRandomReachableNearbyCell(c, p.Map, 12f, TraverseParms.For(TraverseMode.PassDoors), x => x.Standable(p.Map), null, out IntVec3 s) ? s : p.Position;
        }

        // ---------------------------------------------------------------- call a predator

        public static void CallPredators(Pawn p, Gene_LinarianSong g)
        {
            List<PawnKindDef> kinds = p.Map.Biome.AllWildAnimals.Where(k => k.RaceProps.Animal && (k.RaceProps.predator || k.RaceProps.baseBodySize >= 0.8f)).ToList();
            if (kinds.Count == 0) kinds = new List<PawnKindDef> { DefDatabase<PawnKindDef>.GetNamedSilentFail("Wolf_Timber") }.Where(k => k != null).ToList();
            float budget = 120f + 160f * g.Power + (g.Dark("P") ? 150f : 0f);
            if (ModsConfig.IsActive("CETeam.CombatExtended")) budget *= 1.5f;   // beasts are frailer under Combat Extended
            int n = 0, most = 20;
            // they come together, out of one edge of the land
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, p.Map, 0f)) { LinarianTactics.Notify(p, "Nothing answers from the wild."); return; }
            while (budget > 0f && n < most)
            {
                var fit = kinds.Where(k => k.combatPower <= budget || n == 0).ToList();
                if (fit.Count == 0) break;
                PawnKindDef kind = fit.RandomElementByWeight(k => k.combatPower);
                IntVec3 at = CellFinder.RandomClosewalkCellNear(edge, p.Map, 4);
                Pawn a = PawnGenerator.GeneratePawn(kind);
                GenSpawn.Spawn(a, at, p.Map);
                if (g.Dark("P")) FleckMaker.ThrowSmoke(a.DrawPos, a.Map, 1.5f);
                LinarianMental.StartPack(a, p, GenDate.TicksPerHour * 6);
                budget -= kind.combatPower;
                n++;
            }
            Teach(p, LearnPerSong);
            LinarianTactics.Notify(p, n > 0 ? n + " beasts answer her from the wild." : "Nothing answers from the wild.");
        }

        private static readonly Dictionary<int, int> lastDraw = new Dictionary<int, int>();

        /// <summary>The gift of the call: a land that runs short of beasts draws new ones, twice a day at most.</summary>
        public static void DrawBeasts(Pawn her, Gene_LinarianSong g, int now)
        {
            Map map = her.Map;
            if (map == null || !g.Has("P4")) return;
            if (lastDraw.TryGetValue(map.uniqueID, out int t) && now - t < GenDate.TicksPerHour * 12) return;
            int wild = map.mapPawns.AllPawnsSpawned.Count(a => a.RaceProps.Animal && a.Faction == null && !a.Dead);
            if (wild >= 10) return;
            lastDraw[map.uniqueID] = now;
            var kinds = map.Biome.AllWildAnimals.Where(k => map.mapTemperature.SeasonAndOutdoorTemperatureAcceptableFor(k.race)).ToList();
            if (kinds.Count == 0) return;
            PawnKindDef kind = kinds.RandomElementByWeight(k => map.Biome.CommonalityOfAnimal(k));
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 at, map, 0f)) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(3f / Mathf.Max(0.5f, kind.RaceProps.baseBodySize)), 1, 6);
            for (int i = 0; i < count; i++)
            {
                IntVec3 c = CellFinder.RandomClosewalkCellNear(at, map, 4);
                GenSpawn.Spawn(PawnGenerator.GeneratePawn(kind), c, map);
            }
        }

        // ---------------------------------------------------------------- beasts against the raid

        /// <summary>First: most of the land's beasts, by her skill; again within the hour: the rest, the great beasts too.</summary>
        public static void AgainstTheRaid(Pawn p, Gene_LinarianSong g, bool second)
        {
            float max = second ? 999f : LinarianSongHunt.MaxSize(g) + 1.5f;
            int n = 0;
            int ticks = Mathf.RoundToInt(GenDate.TicksPerHour * 4f);
            foreach (Pawn a in p.Map.mapPawns.AllPawnsSpawned.Where(a => a.RaceProps.Animal && a.Faction == null && !a.Dead && !a.Downed
                         && !Linarian.Is(a) && a.BodySize <= max && !(a.MentalState is MentalState_Song)).ToList())
            {
                if (!second && !Rand.Chance(Chance(p, g, a))) continue;
                if (second && !Rand.Chance(Mathf.Min(1f, Chance(p, g, a) + 0.2f))) continue;
                if (!LinarianMental.StartPack(a, p, ticks)) continue;
                n++;
                // with her veil of the pack, some of them go unseen - until their first bite
                if (g.Has("G5") && Rand.Chance(Mathf.Clamp(0.1f + 0.02f * (p.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0), 0.1f, 0.6f)))
                {
                    LinarianVeil.Veil(a, ticks);
                    unseenUntilBite.Add(a.thingIDNumber);
                }
            }
            Teach(p, LearnPerSong);
            LinarianTactics.Notify(p, n > 0 ? n + " beasts of the land fall on her foes." + (second ? "" : " Sing it again within the hour for the rest.") : "No beast answers.");
        }

        /// <summary>Beasts veiled by her pack's veil, unseen until they bite.</summary>
        public static readonly HashSet<int> unseenUntilBite = new HashSet<int>();
        private static readonly Dictionary<int, int> lastGuise = new Dictionary<int, int>();

        /// <summary>With her mirages, a beast of the raid now and then casts shadows of its own shape that draw the
        /// shooters fooled by them (by the mind) - shorter-lived than her own.</summary>
        public static void Guises(Pawn a, Pawn her)
        {
            Gene_LinarianSong g = Linarian.Song(her);
            if (g == null || !g.Has("G3") || !a.Spawned || !Rand.Chance(0.006f)) return;
            int now = Find.TickManager.TicksGame;
            if (lastGuise.TryGetValue(a.thingIDNumber, out int t) && now - t < GenDate.TicksPerHour / 2) return;
            lastGuise[a.thingIDNumber] = now;
            if (!CellFinder.TryFindRandomCellNear(a.Position, a.Map, 2, c => c.Standable(a.Map) && c.GetEdifice(a.Map) == null, out IntVec3 at)) return;
            Building_ShadowDecoy d = Building_ShadowDecoy.SpawnOne(her, at, a.Map, now + 600, false);
            if (d != null) d.beastOf = a;
        }

        /// <summary>Her beasts feel a fifth of their pain (the gift of the raid), and fight better near her.</summary>
        public static void Keep(Pawn a, Pawn her)
        {
            Gene_LinarianSong g = Linarian.Song(her);
            if (g == null) return;
            HediffDef numb = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_PackNumb");
            if (g.Has("P5") && numb != null) LinarianHediffs.Add(a, numb, 300, 1f, her);
        }
    }

    /// <summary>The hunt: wait by her; once pointed, go there - falling on her foes on the way - and circle the place wide;
    /// pointed twice at it, hunt the wild beasts there.</summary>
    public class MentalState_SongHunt : MentalState_Song
    {
        public override bool Hers => true;

        public IntVec3 point = IntVec3.Invalid;
        public bool hunting;
        private float angle = -1f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref point, "point", IntVec3.Invalid);
            Scribe_Values.Look(ref hunting, "hunting");
            Scribe_Values.Look(ref angle, "angle", -1f);
        }

        protected override void Drive()
        {
            LinarianPack.Keep(pawn, herald);
            Pawn foe = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 10f, true).OfType<Pawn>()
                .Where(q => !q.Dead && !q.Downed && q != herald && q != pawn && q.HostileTo(herald) && !(q.MentalState is MentalState_Song))
                .OrderBy(q => (q.Position - pawn.Position).LengthHorizontalSquared).FirstOrDefault();
            if (foe == null && hunting && point.IsValid)
                foe = GenRadial.RadialDistinctThingsAround(point, pawn.Map, 9f, true).OfType<Pawn>()
                    .Where(q => !q.Dead && !q.Downed && q.RaceProps.Animal && q.Faction == null && !(q.MentalState is MentalState_Song) && !Linarian.Is(q))
                    .OrderBy(q => (q.Position - pawn.Position).LengthHorizontalSquared).FirstOrDefault();
            if (foe != null) { Job hit = JobMaker.MakeJob(JobDefOf.AttackMelee, foe); hit.killIncappedTarget = true; hit.expiryInterval = 300; Force(hit); return; }
            if (!point.IsValid)
            {
                if ((pawn.Position - herald.Position).LengthHorizontal > 6f) { Job w = JobMaker.MakeJob(JobDefOf.Goto, herald); w.expiryInterval = 200; Force(w); }
                return;
            }
            // circle the place wide: from one point of the ring to the next
            if (angle < 0f) angle = Rand.Range(0f, 360f);
            IntVec3 on = point + (Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * 8f).ToIntVec3();
            if (!on.InBounds(pawn.Map) || !on.Standable(pawn.Map) || (pawn.Position - on).LengthHorizontal <= 1.9f) { angle = (angle + 35f) % 360f; return; }
            Job go = JobMaker.MakeJob(JobDefOf.Goto, on);
            go.expiryInterval = 300;
            Force(go);
        }

        public override bool ForceHostileTo(Thing t) => t is Pawn q && (q.HostileTo(herald) || hunting && q.RaceProps.Animal && q.Faction == null && point.IsValid && (q.Position - point).LengthHorizontal <= 9f);
    }

    /// <summary>A herd driven home: walk there, then drop senseless, marked for the hunters.</summary>
    public class MentalState_SongHerd : MentalState_Song
    {
        public IntVec3 dest = IntVec3.Invalid;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref dest, "dest", IntVec3.Invalid);
        }

        protected override void Drive()
        {
            if (!dest.IsValid) { RecoverFromState(); return; }
            if ((pawn.Position - dest).LengthHorizontal <= 6f)
            {
                HediffDef daze = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_HerdDaze");
                RecoverFromState();
                if (daze != null) LinarianHediffs.Add(pawn, daze, GenDate.TicksPerHour * 6, 1f, herald);
                if (pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt) == null) pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.Hunt));
                return;
            }
            Force(JobMaker.MakeJob(JobDefOf.Goto, dest));
        }
    }

    /// <summary>Beasts fighting near her wear their prey down: every bite harries it; she learns from every bite.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_PackHarries
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt <= 0f || !(dinfo.Instigator is Pawn a) || !a.RaceProps.Animal || !(a.MentalState is MentalState_Song s) || s.herald == null) return;
            Pawn her = s.herald;
            if (Linarian.Song(her)?.Has("P3") != true || her.Map != a.Map || (a.Position - her.Position).LengthHorizontal > 8f) return;
            HediffDef harried = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Harried");
            if (harried != null)
            {
                Hediff h = __instance.health.hediffSet.GetFirstHediffOfDef(harried) ?? __instance.health.AddHediff(harried);
                h.Severity = Mathf.Min(1f, h.Severity + 0.15f);
                var d = h.TryGetComp<HediffComp_Disappears>();
                if (d != null) d.ticksToDisappear = 1200;
            }
            LinarianPack.Teach(her, 20f);
        }
    }

    /// <summary>She may drink her own beasts: the siphon takes a beast of her pack standing.</summary>
    /// <summary>A beast unseen by her pack's veil shows itself with its first bite.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_PackUnseenBites
    {
        public static void Postfix(DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn a) || !LinarianPack.unseenUntilBite.Remove(a.thingIDNumber)) return;
            LinarianVeil.Unveil(a);
            if (a.Spawned) FleckMaker.ThrowSmoke(a.DrawPos, a.Map, 1f);
        }
    }

    public static class LinarianPackSiphon
    {
        /// <summary>Anything held by her song (beasts of her pack, sleepwalkers, the captivated, the rapt) or walking her dream.</summary>
        /// <summary>Held by one of her songs right now (not her dream): her newest song takes it over.</summary>
        public static bool Held(Pawn her, Pawn a) => a?.MentalState is MentalState_Song s && s.herald == her && s.Hers;

        public static bool Hers(Pawn caster, Pawn a) =>
            a != null && (a.MentalState is MentalState_Song s && s.herald == caster && s.Hers
                || LinarianDream.IsWalker(a) && LinarianSource.Of(a, LinarianDream.Walker) == caster
                || LinarianSource.Of(a, LinarianHediffs.Avir_SongCaptivated) == caster);
    }
}
