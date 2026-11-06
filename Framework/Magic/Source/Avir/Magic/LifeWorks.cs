using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The works of Life (the author, Fix194): healing - each kind of healing with its own after-gift; Pestilence
    /// takes the sickness onto the healer; tending the crops; gathering from the animals at a touch; emerald statues; and
    /// soothing - a caster easing the others. All out of the store of life (Symbiosis: Life makes nothing, it moves), and a
    /// little Arcana for the casting.</summary>
    public static class LifeWorks
    {
        public const float Arcana = 2f, Xp = 8f;

        /// <summary>How a caster heals: by the element they know best with Life. From Expert in it - the deep meeting (Wild
        /// Flame, Blood, Emerald, Pestilence), else the wide one (Warmth, Sap, Frame, Breath); with no element - plain Life.</summary>
        public static RuneKind Kind(Hediff_ArcaneCore c, out bool deep)
        {
            deep = false;
            if (c == null) return LifeMagic.None;
            RuneKind best = LifeMagic.None; int bs = -1;
            foreach (RuneKind e in new[] { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air })
            {
                if (!MagicWork.Knows(c, e)) continue;
                int st = Arcane.Stage(c, RuneKind.Life, e);
                if (st > bs) { bs = st; best = e; }
            }
            deep = best != LifeMagic.None && bs >= 3;
            return best;
        }

        /// <summary>The store a work of this caster spends: the deep meeting's own, or plain Life's.</summary>
        public static RuneKind Store(Hediff_ArcaneCore c) { RuneKind k = Kind(c, out bool deep); return deep ? k : LifeMagic.None; }

        public static bool Has(Hediff_ArcaneCore c, float need) => c != null && c.Store(Store(c)) >= need;

        /// <summary>Takes up to `need` from the store; the share it got.</summary>
        public static float Spend(Hediff_ArcaneCore c, float need)
        {
            RuneKind k = Store(c); float have = c.Store(k); float got = Mathf.Min(have, need);
            c.SetStore(k, have - got);
            return need <= 0f ? 1f : got / need;
        }

        public static Hediff_ArcaneCore Able(Pawn p, float store) { Hediff_ArcaneCore c = MagicWork.Able(p, RuneKind.Life, Arcana); return c != null && Has(c, store) ? c : null; }

        public static string KindName(Hediff_ArcaneCore c)
        {
            RuneKind k = Kind(c, out bool deep);
            return k == LifeMagic.None ? "Life" : deep ? LifeMagic.PairName(k) : LifeMagic.AspectName(k);
        }

        private static HediffDef H(string n) => DefDatabase<HediffDef>.GetNamedSilentFail(n);

        /// <summary>What a healing leaves after it - each kind its own (the author).</summary>
        public static void AfterGift(Hediff_ArcaneCore c, Pawn p, float q)
        {
            RuneKind k = Kind(c, out bool deep);
            int ticks = Mathf.RoundToInt(GenDate.TicksPerDay * 0.5f * Mathf.Max(0.5f, q));
            if (k == LifeMagic.None) { MagicFx.Give(p, H("Avir_LifeMended"), ticks); return; }
            if (!deep)
                switch (k)
                {
                    case RuneKind.Fire: MagicFx.Give(p, H("Avir_LifeWarmth"), ticks); break;                       // warmth: quicker mending, no cold
                    case RuneKind.Water:                                                                             // sap: the poison washed out
                        { Hediff tox = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup); if (tox != null) tox.Severity -= 0.15f * q; }
                        MagicFx.Give(p, H("Avir_LifeMended"), ticks); break;
                    case RuneKind.Earth: MagicFx.Give(p, H("Avir_LifeHardened"), ticks); break;                     // frame: a little armour
                    case RuneKind.Air: MagicFx.Give(p, H("Avir_LifeBreath"), ticks); break;                         // breath: easier breathing
                }
            else
                switch (k)
                {
                    case RuneKind.Fire: MagicFx.Give(p, H("Avir_LifeEmberHeart"), ticks); break;                    // a smouldering heart: warm, the pain dulled
                    case RuneKind.Water:                                                                             // blood: given back, and rich
                        { Hediff bl = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss); if (bl != null) bl.Severity -= 0.25f * q; }
                        MagicFx.Give(p, H("Avir_LifeRichBlood"), ticks); break;
                    case RuneKind.Earth: MagicFx.Give(p, H("Avir_LifeStoneCalm"), ticks); break;                    // emerald: the body slows, hardens, hungers little
                    case RuneKind.Air: MagicFx.Give(p, H("Avir_LifeTaught"), ticks * 2); break;                     // pestilence: the body taught its sicknesses
                }
        }

        /// <summary>A caster of Decay (Life with earth and air, the mixed triple): the works of rot and of time are theirs -
        /// the blight taken off the plants, the brew aged (the author: the works shared out among the druids, Fix197).</summary>
        public static bool Decay(Hediff_ArcaneCore c) => c != null && MagicWork.Knows(c, RuneKind.Earth) && MagicWork.Knows(c, RuneKind.Air) && MagicWork.Knows(c, RuneKind.Life);

        public static float Quality(Hediff_ArcaneCore c) => Mathf.Clamp(0.5f + 0.15f * Arcane.Stage(c, RuneKind.Life), 0.4f, 1.3f);
    }

    // ------------------------------------------------------------------ healing

    public class WorkGiver_LifeHeal : WorkGiver_Scanner
    {
        public const float Store = 4f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => LifeWorks.Able(pawn, Store) == null;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t is Pawn p && !p.Dead && p.RaceProps.IsFlesh && !p.HostileTo(pawn) && (p.Faction == pawn.Faction || p.HostFaction == pawn.Faction)
            && LifeMagic.Hurt(p) && (forced || p.health.hediffSet.hediffs.Any(h => h is Hediff_Injury i && !i.IsPermanent() && i.Severity >= 3f) || p.health.hediffSet.BleedRateTotal > 0f)
            && !p.IsForbidden(pawn) && pawn.CanReserve(p, 1, -1, null, forced) && LifeWorks.Able(pawn, Store) != null;
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeHeal"), t);
    }

    public class JobDriver_LifeHeal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            WorkCast wc = WorkSpells.Make(Mages.Core(pawn), "Avir_LifeHeal", new[] { RuneKind.Life }, LifeWorks.Arcana);
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, wc.FromBook ? 240 : 320, wc.spell ?? WorkSpells.Charm("Avir_LifeHeal", new[] { RuneKind.Life }), false, new Color(0.6f, 0.9f, 0.5f, 0.5f));
            Toil done = ToilMaker.MakeToil("LifeHeal");
            done.initAction = () =>
            {
                Pawn p = TargetA.Thing as Pawn;
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                if (p == null || c == null) return;
                float f = LifeWorks.Spend(c, WorkGiver_LifeHeal.Store * (1f + 0.3f * wc.amplify));
                if (f <= 0f) return;
                float q = LifeWorks.Quality(c) * f * (1f + 0.2f * wc.amplify);
                LifeMagic.HealInjuries(p, 14f * q);
                LifeMagic.AddRegen(p, 0.2f * q);
                LifeWorks.AfterGift(c, p, q);
                FleckMaker.ThrowMetaIcon(p.Position, p.Map, FleckDefOf.HealingCross);
                MapComponent_SpellSprites.PutTinted(p.Map, "Life/Mend", new Color(0.6f, 0.95f, 0.55f), p.DrawPos, 1.3f, 60);
                MagicWork.Done(c, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp * (wc.FromBook ? 1.5f : 1f));
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    // ------------------------------------------------------------------ Pestilence: the sickness taken onto the healer

    public class WorkGiver_LifeCure : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);
        private static bool Pest(Pawn pawn, out Hediff_ArcaneCore c) { c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana); return c != null && LifeWorks.Kind(c, out bool deep) == RuneKind.Air && deep; }
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !Pest(pawn, out _);
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t is Pawn p && p != pawn && !p.Dead && p.RaceProps.IsFlesh && !p.HostileTo(pawn) && (p.Faction == pawn.Faction || p.HostFaction == pawn.Faction)
            && p.health.hediffSet.hediffs.Any(h => h.TryGetComp<HediffComp_Immunizable>() != null && h.Visible)
            && !p.IsForbidden(pawn) && pawn.CanReserve(p, 1, -1, null, forced) && Pest(pawn, out _);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeCure"), t);
    }

    public class JobDriver_LifeCure : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, 360, WorkSpells.Charm("Avir_LifeCure", new[] { RuneKind.Life, RuneKind.Air }), false, new Color(0.75f, 0.78f, 0.3f, 0.5f));
            Toil done = ToilMaker.MakeToil("LifeCure");
            done.initAction = () =>
            {
                Pawn p = TargetA.Thing as Pawn;
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                if (p == null || c == null) return;
                // the healer of Pestilence takes the sickness onto himself and carries it as fuel (the author)
                float got = Pestilence.TakeFrom(pawn, p, 8f);
                if (got > 0f) c.AddTo(RuneKind.Air, got * 0.5f);
                MapComponent_SpellSprites.PutTinted(p.Map, "Life/PoisonFog", LifeMagic.PairColor(RuneKind.Air), p.DrawPos, 1.4f, 70);
                MagicWork.Done(c, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    // ------------------------------------------------------------------ the crops: blight taken off, the growing tended

    public class WorkGiver_LifeCrops : WorkGiver_Scanner
    {
        public const float Store = 2f;
        private static readonly Dictionary<int, int> tended = new Dictionary<int, int>();   // a zone's cell tended - not again for a day
        public static void Mark(Thing t) => tended[t.thingIDNumber] = Find.TickManager.TicksGame + GenDate.TicksPerDay;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Plant);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => LifeWorks.Able(pawn, Store) == null;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Plant pl) || pl.IsForbidden(pawn) || !(pawn.Map.zoneManager.ZoneAt(pl.Position) is Zone_Growing)) return false;
            bool sick = pl.Blighted && LifeWorks.Decay(Mages.Core(pawn));   // the blight is Decay's to take off (Fix197)
            bool due = pl.LifeStage == PlantLifeStage.Growing && pl.Growth < 0.85f && (!tended.TryGetValue(pl.thingIDNumber, out int u) || u <= Find.TickManager.TicksGame);
            return (sick || due) && pawn.CanReserve(pl, 1, -1, null, forced) && LifeWorks.Able(pawn, Store) != null;
        }
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeCrops"), t);
    }

    public class JobDriver_LifeCrops : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            WorkCast wc = WorkSpells.Make(Mages.Core(pawn), "Avir_LifeCrops", new[] { RuneKind.Life }, LifeWorks.Arcana);
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, wc.FromBook ? 150 : 200, wc.spell ?? WorkSpells.Charm("Avir_LifeCrops", new[] { RuneKind.Life }), false, new Color(0.6f, 0.9f, 0.45f, 0.5f));
            Toil done = ToilMaker.MakeToil("LifeCrops");
            done.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                if (c == null || !(TargetA.Thing is Plant centre)) return;
                Map map = pawn.Map;
                // the whole patch round it, a little wider with Breadth
                float r = 1.9f + wc.breadth;
                var plants = GenRadial.RadialDistinctThingsAround(centre.Position, map, r, true).OfType<Plant>().Where(p => map.zoneManager.ZoneAt(p.Position) is Zone_Growing).ToList();
                float f = LifeWorks.Spend(c, WorkGiver_LifeCrops.Store + 0.15f * plants.Count);
                if (f <= 0f) return;
                foreach (Plant p in plants)
                {
                    if (p.Blighted && LifeWorks.Decay(c)) p.Blight?.Destroy();
                    if (p.LifeStage == PlantLifeStage.Growing) p.Growth = Mathf.Min(1f, p.Growth + 0.08f * f * LifeWorks.Quality(c));
                    WorkGiver_LifeCrops.Mark(p);
                    if (Rand.Chance(0.3f)) FleckMaker.ThrowDustPuffThick(p.DrawPos, map, 0.8f, new Color(0.6f, 0.9f, 0.5f, 0.45f));
                }
                MagicWork.Done(c, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp * 0.6f);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    // ------------------------------------------------------------------ the animals: gathered at a touch (milk, wool...)

    public class WorkGiver_LifeGather : WorkGiver_Scanner
    {
        public const float Store = 1f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => LifeWorks.Able(pawn, Store) == null;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t is Pawn a && a.RaceProps.Animal && a.Faction == pawn.Faction && !a.Dead && !a.Downed && a.AllComps.OfType<CompHasGatherableBodyResource>().Any(g => g.ActiveAndFull)
            && !a.IsForbidden(pawn) && pawn.CanReserve(a, 1, -1, null, forced) && LifeWorks.Able(pawn, Store) != null;
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeGather"), t);
    }

    public class JobDriver_LifeGather : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return WorkSpells.GoTo(this, false, 0f);
            // a touch of Life: the animal stands still for it, whatever the handler's skill
            Toil hold = ArcaneChannel.Channel(this, 180, WorkSpells.Charm("Avir_LifeGather", new[] { RuneKind.Life }), false, new Color(0.6f, 0.9f, 0.5f, 0.5f));
            hold.AddPreInitAction(() => { if (TargetA.Thing is Pawn a) { a.jobs?.StopAll(); a.pather?.StopDead(); } });
            yield return hold;
            Toil done = ToilMaker.MakeToil("LifeGather");
            done.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                if (c == null || !(TargetA.Thing is Pawn a)) return;
                if (LifeWorks.Spend(c, WorkGiver_LifeGather.Store) <= 0f) return;
                foreach (CompHasGatherableBodyResource g in a.AllComps.OfType<CompHasGatherableBodyResource>().Where(g => g.ActiveAndFull).ToList()) g.Gathered(pawn);
                MagicFx.Give(a, DefDatabase<HediffDef>.GetNamedSilentFail("Avir_LifeMended"), GenDate.TicksPerDay / 4);   // it goes away eased
                MagicWork.Done(c, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp * 0.5f);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    // ------------------------------------------------------------------ Emerald: living things turned to emerald statues

    public class Designator_LifeEmerald : Designator_Arcane
    {
        public Designator_LifeEmerald()
        {
            defaultLabel = "Emerald statue (emerald)";
            defaultDesc = "Mark a grown plant or tree for a caster of Emerald (Life with earth, deep) to turn into an emerald statue - stone that remembers the plant, beautiful and lasting. Its life goes into the caster's store of Emerald. Another order of Emerald brings a statue back to life.";
            icon = ArcaneIcons.For("Avir_LifeEmerald", "UI/Designators/SmoothSurface");
        }
        protected override DesignationDef Designation => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_LifeEmerald");
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map) || Designation == null || Map.designationManager.DesignationAt(c, Designation) != null) return false;
            Plant pl = c.GetPlant(Map);
            return pl != null && LifeMagic.Sculptable(pl);
        }
    }

    public class WorkGiver_LifeEmerald : WorkGiver_Scanner
    {
        private static DesignationDef D => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_LifeEmerald");
        private static bool Emeraldist(Pawn pawn) { Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana); return c != null && LifeWorks.Kind(c, out bool deep) == RuneKind.Earth && deep; }
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => D == null || !Emeraldist(pawn);
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) => pawn.Map.designationManager.SpawnedDesignationsOfDef(D).Select(x => x.target.Cell).ToList();
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false) => pawn.Map.designationManager.DesignationAt(c, D) != null && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced) && Emeraldist(pawn);
        public override Job JobOnCell(Pawn pawn, IntVec3 c, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeEmerald"), c);
    }

    public class JobDriver_LifeEmerald : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, 400, WorkSpells.Charm("Avir_LifeEmerald", new[] { RuneKind.Life, RuneKind.Earth }), false, new Color(0.2f, 0.75f, 0.45f, 0.5f));
            Toil done = ToilMaker.MakeToil("LifeEmerald");
            done.initAction = () =>
            {
                Map map = pawn.Map; IntVec3 cell = TargetA.Cell;
                DesignationDef d = DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_LifeEmerald");
                if (d != null) map.designationManager.TryRemoveDesignation(cell, d);
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                Plant pl = cell.GetPlant(map);
                if (c == null || pl == null || !LifeMagic.Sculptable(pl)) return;
                float got = EmeraldPlant.Make(pl, map);
                c.AddTo(RuneKind.Earth, got * 0.5f);
                MagicWork.Done(c, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    // ------------------------------------------------------------------ soothing: a caster eases the others

    public class WorkGiver_LifeSoothe : WorkGiver_Scanner
    {
        public const float Store = 1.5f;
        private static readonly Dictionary<int, int> eased = new Dictionary<int, int>();
        public static void Mark(Pawn p) => eased[p.thingIDNumber] = Find.TickManager.TicksGame + GenDate.TicksPerDay / 2;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => LifeWorks.Able(pawn, Store) == null;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn p) || p == pawn || p.Dead || p.Downed || !p.RaceProps.Humanlike || p.Faction != pawn.Faction || p.Drafted || p.InMentalState) return false;
            if (eased.TryGetValue(p.thingIDNumber, out int u) && u > Find.TickManager.TicksGame) return false;
            bool low = (p.needs?.joy != null && p.needs.joy.CurLevel < 0.45f) || (p.needs?.mood != null && p.needs.mood.CurLevel < 0.35f);
            bool free = p.CurJobDef == null || p.CurJobDef.joyKind != null || p.mindState.IsIdle || p.CurJobDef == JobDefOf.Wait_Wander || p.CurJobDef == JobDefOf.GotoWander;
            return (low || forced) && free && !p.IsForbidden(pawn) && pawn.CanReserve(p, 1, -1, null, forced) && LifeWorks.Able(pawn, Store) != null;
        }
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeSoothe"), t);
    }

    public class JobDriver_LifeSoothe : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return WorkSpells.GoTo(this, false, 0f);
            Toil hold = ArcaneChannel.Channel(this, 600, WorkSpells.Charm("Avir_LifeSoothe", new[] { RuneKind.Life }), false, new Color(0.7f, 0.95f, 0.6f, 0.45f));
            hold.AddPreInitAction(() => { if (TargetA.Thing is Pawn p) p.pather?.StopDead(); });
            yield return hold;
            Toil done = ToilMaker.MakeToil("LifeSoothe");
            done.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                if (c == null || !(TargetA.Thing is Pawn p)) return;
                float f = LifeWorks.Spend(c, WorkGiver_LifeSoothe.Store);
                if (f <= 0f) return;
                float q = LifeWorks.Quality(c) * f;
                p.needs?.joy?.GainJoy(0.3f * q, JoyKindDefOf.Social);
                if (p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Min(1f, p.needs.rest.CurLevel + 0.08f * q);
                ThoughtDef th = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_LifeSoothed");
                if (th != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(th, pawn);
                WorkGiver_LifeSoothe.Mark(p);
                MapComponent_SpellSprites.PutTinted(p.Map, "Life/Petals", new Color(0.75f, 0.95f, 0.65f), p.DrawPos, 1.2f, 70);
                MagicWork.Done(c, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp * 0.6f);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    // ------------------------------------------------------------------ Decay: ageing the brew

    /// <summary>Time on what ripens (Decay, Fix197): a fermenting barrel is aged on by a quarter - the brew ripens without
    /// waiting. Out of the store of life.</summary>
    public class WorkGiver_LifeAge : WorkGiver_Scanner
    {
        public const float Store = 3f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        private static bool Able(Pawn pawn) { Hediff_ArcaneCore c = LifeWorks.Able(pawn, Store); return c != null && LifeWorks.Decay(c); }
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !Able(pawn);
        /// <summary>Wort in it (its own 'Empty' is not ours to call).</summary>
        public static bool Has(Building_FermentingBarrel b) => b.SpaceLeftForWort < 25;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t is Building_FermentingBarrel b && Has(b) && !b.Fermented && b.Progress < 0.9f && !b.IsForbidden(pawn) && pawn.CanReserve(b, 1, -1, null, forced) && Able(pawn);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeAge"), t);
    }

    public class JobDriver_LifeAge : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, 300, WorkSpells.Charm("Avir_LifeAge", new[] { RuneKind.Life, RuneKind.Earth, RuneKind.Air }), false, new Color(0.72f, 0.68f, 0.55f, 0.5f));
            Toil done = ToilMaker.MakeToil("LifeAge");
            done.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                if (c == null || !(TargetA.Thing is Building_FermentingBarrel b) || !WorkGiver_LifeAge.Has(b) || b.Fermented) return;
                float f = LifeWorks.Spend(c, WorkGiver_LifeAge.Store);
                if (f <= 0f) return;
                b.Progress = Mathf.Min(1f, b.Progress + 0.25f * f * LifeWorks.Quality(c));
                FleckMaker.ThrowDustPuffThick(b.DrawPos, b.Map, 1f, new Color(0.72f, 0.68f, 0.55f, 0.45f));
                MagicWork.Done(c, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp * 0.7f);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }
}
