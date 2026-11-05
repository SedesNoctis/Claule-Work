using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir
{
    // =====================================================================================
    // BIRD MILK (Defs/Drugs/Avir_BirdMilk.xml - with or without RJW). Hushleaf eaten raw, again and again, brings an Avir
    // female's milk in - harpies too: a thick, slightly golden milk. She fills a long-necked jug with it whenever
    // she is full; it is rare, it spoils, chicks love it and anyone who drinks it is glad of it (Avir +5,
    // others +3). With no chicks of her own to feed, she may let her mate drink straight from the source
    // while they preen each other. With Biotech she can also put a chick to the breast herself: while the milk
    // flows she is lactating as a vanilla mother is, and a chick that suckles from her gets the bird milk's joy.
    // Eaten raw by an Avir male it gives half the draught's fire - with RJW only (Compat/RJW/Defs/BirdMilk/HushleafFire.xml).
    // =====================================================================================
    public static class AvirBirdMilk
    {
        private static readonly System.Reflection.FieldInfo ChargeField = HarmonyLib.AccessTools.Field(typeof(HediffComp_Chargeable), "charge");

        /// <summary>Keeps vanilla's lactation reserve full without taking food for it.</summary>
        public static void FillLactation(HediffComp_Lactating comp)
        {
            if (ChargeField != null && comp.Charge < comp.Props.fullChargeAmount) ChargeField.SetValue(comp, comp.Props.fullChargeAmount);
        }

        private static HediffDef flowDef;
        public static HediffDef FlowDef => flowDef ?? (flowDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_BirdMilkFlow"));

        public static HediffDef Flow => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_BirdMilkFlow");
        public static ThingDef Milk => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_BirdMilk");
        public static JobDef ExpressJob => DefDatabase<JobDef>.GetNamedSilentFail("Avir_ExpressBirdMilk");
        /// <summary>Raw hushleaf in a male: half the draught's fire (Compat/RJW/Defs/BirdMilk/HushleafFire.xml).</summary>
        public static HediffDef HalfFire => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_HushleafFire");

        /// <summary>Who can carry the milk: an Avir female (harpies are Avir too), grown.</summary>
        public static bool CanFlow(Pawn p) =>
            p != null && p.gender == Gender.Female && AvirBody.IsAvir(p) && p.DevelopmentalStage.Adult();

        public static HediffComp_BirdMilk MilkOf(Pawn p)
        {
            HediffDef f = Flow;
            if (f == null || p?.health?.hediffSet == null) return null;
            return p.health.hediffSet.GetFirstHediffOfDef(f)?.TryGetComp<HediffComp_BirdMilk>();
        }

        private static bool HasOwnChicks(Pawn mother)
        {
            if (mother?.relations == null) return false;
            foreach (Pawn c in mother.relations.Children)
                if (!c.Dead && AvirGrooming.IsYoungling(c) && c.MapHeld != null && c.MapHeld == mother.MapHeld) return true;
            return false;
        }

        /// <summary>After a preening session: a mother with milk and no chicks to feed may let her mate drink.</summary>
        public static void TryFeedMate(Pawn a, Pawn b)
        {
            if (Flow == null || a == null || b == null) return;
            TryOneWay(a, b);
        }

        private static void TryOneWay(Pawn a, Pawn b)
        {
            Pawn mother = null, mate = null;
            HediffComp_BirdMilk ma = MilkOf(a), mb = MilkOf(b);
            if (ma != null && ma.CanGive) { mother = a; mate = b; }
            else if (mb != null && mb.CanGive) { mother = b; mate = a; }
            if (mother == null || !mate.DevelopmentalStage.Adult() || !AvirBody.IsAvir(mate)) return;
            if (!LovePartnerRelationUtility.LovePartnerRelationExists(mother, mate)) return;
            if (HasOwnChicks(mother) || !Rand.Chance(0.35f)) return;

            MilkOf(mother).Take(0.5f);
            if (mate.needs?.food != null) mate.needs.food.CurLevel += 0.3f;
            Gain(mate, "Avir_FromTheSource", mother);
            Gain(mother, "Avir_FedMyMate", mate);
        }

        public static void Gain(Pawn p, string thought, Pawn other = null, int stage = -1)
        {
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(thought);
            if (def == null || p?.needs?.mood?.thoughts?.memories == null) return;
            Thought_Memory m = stage >= 0 ? ThoughtMaker.MakeThought(def, stage) : ThoughtMaker.MakeThought(def, null);
            p.needs.mood.thoughts.memories.TryGainMemory(m, other);
        }
    }

    /// <summary>The milk itself: how full she is. Fills over two days while the flow lasts; drains away without it.</summary>
    public class HediffCompProperties_BirdMilk : HediffCompProperties
    {
        public float flowFrom = 0.4f;
        public float daysToFill = 2f;
        public HediffCompProperties_BirdMilk() { compClass = typeof(HediffComp_BirdMilk); }
    }

    public class HediffComp_BirdMilk : HediffComp
    {
        public float fullness;
        private bool lactating;   // the vanilla Lactating hediff was put on by us (and is ours to take off)
        public HediffCompProperties_BirdMilk Props => (HediffCompProperties_BirdMilk)props;

        public bool Flowing => parent.Severity >= Props.flowFrom && AvirBirdMilk.CanFlow(Pawn);
        public bool Full => fullness >= 1f;
        public bool CanGive => Flowing && fullness >= 0.5f;

        public void Take(float f) => fullness = Mathf.Max(0f, fullness - f);

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Pawn.IsHashIntervalTick(250)) return;
            float step = 250f / (Props.daysToFill * GenDate.TicksPerDay);
            fullness = Flowing ? Mathf.Min(1f, fullness + step) : Mathf.Max(0f, fullness - step);
            SyncLactating(Flowing);
            if (Full && Flowing) TryAutoExpress();
        }

        /// <summary>A full mother fills the jug by herself. The "Basic" work type alone is not enough: a colonist who always
        /// has something of higher priority to do (harvesting, hauling...) never reaches it, and the milk sat at 100%.
        /// So the hediff starts the job itself, interrupting only what may be interrupted and resuming it afterwards.</summary>
        private void TryAutoExpress()
        {
            Pawn p = Pawn;
            JobDef def = AvirBirdMilk.ExpressJob;
            if (def == null || AvirBirdMilk.Milk == null || p?.jobs == null) return;
            if (!p.Spawned || p.Faction != Faction.OfPlayer || !p.RaceProps.Humanlike) return;
            if (p.Downed || p.Drafted || p.InMentalState || !p.Awake()) return;
            WorkTypeDef basic = DefDatabase<WorkTypeDef>.GetNamedSilentFail("BasicWorker");
            if (basic != null && p.WorkTypeIsDisabled(basic)) return;
            Job cur = p.CurJob;
            if (cur != null && (cur.def == def || cur.playerForced || !cur.def.casualInterruptible)) return;
            p.jobs.StartJob(JobMaker.MakeJob(def, p), JobCondition.InterruptOptional, resumeCurJobAfterwards: true);
        }

        /// <summary>While the milk flows she is lactating (Biotech): she can breastfeed a chick the vanilla way.</summary>
        private void SyncLactating(bool on)
        {
            if (!ModsConfig.BiotechActive || HediffDefOf.Lactating == null) return;
            HediffSet set = Pawn.health.hediffSet;
            Hediff l = set.GetFirstHediffOfDef(HediffDefOf.Lactating);
            if (on)
            {
                if (l == null) { l = Pawn.health.AddHediff(HediffDefOf.Lactating); lactating = true; }
                if (lactating)
                {
                    l.Severity = 1f;
                    // The bird milk is the milk: vanilla's own reserve stays full, so it never pulls food
                    // out of her to fill itself (see Patch_AvirFoodFall for the other half of its cost).
                    var comp = l.TryGetComp<HediffComp_Lactating>();
                    if (comp != null) AvirBirdMilk.FillLactation(comp);
                }
            }
            else if (lactating)
            {
                if (l != null) Pawn.health.RemoveHediff(l);
                lactating = false;
            }
        }

        public override void CompPostPostRemoved() => SyncLactating(false);

        public override string CompLabelInBracketsExtra => Flowing ? fullness.ToStringPercent() : null;

        public override string CompTipStringExtra => Flowing
            ? "Milk: " + fullness.ToStringPercent() + (Full ? " - full, she will fill a jug" : "")
            : "The milk is not in yet: keep eating hushleaf.";

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref fullness, "birdMilk");
            Scribe_Values.Look(ref lactating, "birdMilkLactating");
        }
    }

    /// <summary>Raw hushleaf: an Avir female's milk comes in after two doses; an Avir male gets half of the quiet nest
    /// draught's fire (desire x2 instead of x3, ten days); for anyone else it is a bitter leaf.</summary>
    public class IngestionOutcomeDoer_Hushleaf : IngestionOutcomeDoer
    {
        public float severityPerDose = 0.25f;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (pawn?.health?.hediffSet == null) return;
            if (pawn.gender == Gender.Male)
            {
                HediffDef half = AvirBirdMilk.HalfFire;
                if (half == null || !AvirBody.IsAvir(pawn) || !pawn.DevelopmentalStage.Adult()) return;
                // the draught's full fire is stronger: the leaf neither adds to it nor shortens it
                if (HeartwingDefOf.Avir_HeartwingFire != null && pawn.health.hediffSet.HasHediff(HeartwingDefOf.Avir_HeartwingFire)) return;
                Hediff f = pawn.health.hediffSet.GetFirstHediffOfDef(half) ?? pawn.health.AddHediff(half);
                f.Severity = 1f;
                return;
            }
            HediffDef flow = AvirBirdMilk.Flow;
            if (flow == null || !AvirBirdMilk.CanFlow(pawn)) return;
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(flow);
            if (h == null) { h = pawn.health.AddHediff(flow); h.Severity = 0f; }
            h.Severity += severityPerDose * Mathf.Max(1, ingestedCount);
        }
    }

    /// <summary>Drinking bird milk: +5 for an Avir, +3 for anyone else, and the chicks love it most.</summary>
    public class IngestionOutcomeDoer_BirdMilk : IngestionOutcomeDoer
    {
        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            int stage = pawn.DevelopmentalStage.Baby() || AvirGrooming.IsYoungling(pawn) ? 2 : AvirBody.IsAvir(pawn) ? 1 : 0;
            AvirBirdMilk.Gain(pawn, "Avir_DrankBirdMilk", null, stage);
        }
    }

    /// <summary>A chick at her breast drinks bird milk: the same joy as from the jug, and it comes out of what would
    /// have filled the next jug (a whole feed is about half a jug).</summary>
    [HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.SuckleFromLactatingPawn))]
    public static class Patch_SuckleBirdMilk
    {
        public static void Postfix(Pawn baby, Pawn feeder, int delta)
        {
            HediffComp_BirdMilk m = AvirBirdMilk.MilkOf(feeder);
            if (m == null || !m.Flowing) return;
            m.Take(0.0002f * delta);
            ThoughtDef joy = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_DrankBirdMilk");
            if (joy != null && baby.needs?.mood?.thoughts?.memories?.GetFirstMemoryOfDef(joy) == null)
                AvirBirdMilk.Gain(baby, "Avir_DrankBirdMilk", feeder, 2);
        }
    }

    /// <summary>A full mother fills a jug (basic work, done where she stands).</summary>
    public class WorkGiver_ExpressBirdMilk : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            HediffComp_BirdMilk m = AvirBirdMilk.MilkOf(pawn);
            if (m == null || !m.Flowing || !m.Full || AvirBirdMilk.ExpressJob == null || AvirBirdMilk.Milk == null) return null;
            return JobMaker.MakeJob(AvirBirdMilk.ExpressJob, pawn);
        }
    }

    public class JobDriver_ExpressBirdMilk : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil fill = Toils_General.Wait(600).WithProgressBarToilDelay(TargetIndex.A);
            fill.FailOn(() => AvirBirdMilk.MilkOf(pawn) == null || !AvirBirdMilk.MilkOf(pawn).Full);
            yield return fill;
            yield return Toils_General.Do(delegate
            {
                HediffComp_BirdMilk m = AvirBirdMilk.MilkOf(pawn);
                if (m == null || !m.Full) return;
                m.Take(1f);
                Thing jug = ThingMaker.MakeThing(AvirBirdMilk.Milk);
                GenPlace.TryPlaceThing(jug, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            });
        }
    }
}
