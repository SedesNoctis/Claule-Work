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
    // =====================================================================================
    // HER PEOPLE, WHOEVER THEY BELONG TO.
    //   Those who walk, fight or worship under her song (sleepwalkers, the procession, the retinue, the captivated,
    //   the beasts, her guards) are no enemy of her or of her people while it lasts - whatever their faction thinks.
    //   Her cultists keep their faction but leave its raid for her flock: a group of its own that keeps round her and
    //   fights her foes (no guests, no wandering off the land). Those whose worship fades are let go.
    //   A fervent cultist may offer himself to her: she devours him.
    // =====================================================================================

    /// <summary>One under her song is no enemy of her or her people (nor of their friends).</summary>
    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
    public static class Patch_HersNotHostile
    {
        public static void Postfix(Thing a, Thing b, ref bool __result)
        {
            if (__result && (Ruled(a, b) || Ruled(b, a))) __result = false;
        }

        /// <summary>Whether x walks under her song and y is her, or her people, or their friends.</summary>
        public static bool Ruled(Thing x, Thing y)
        {
            if (!(x is Pawn px) || !(px.MentalState is MentalState_Song s) || !s.Hers || s.herald == null || y == null) return false;
            if (y == s.herald) return true;
            Faction hf = s.herald.Faction, yf = y.Faction;
            return hf != null && yf != null && (yf == hf || !yf.HostileTo(hf));
        }
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) })]
    public static class Patch_HersNotHostileFaction
    {
        public static void Postfix(Thing t, Faction fac, ref bool __result)
        {
            if (!__result || fac == null || !(t is Pawn p) || !(p.MentalState is MentalState_Song s) || !s.Hers || s.herald?.Faction == null) return;
            if (fac == s.herald.Faction || !fac.HostileTo(s.herald.Faction)) __result = false;
        }
    }

    // ---------------------------------------------------------------- the flock: her cultists' own group

    public class LordJob_SingerFlock : LordJob
    {
        public Pawn her;

        public LordJob_SingerFlock() { }
        public LordJob_SingerFlock(Pawn her) { this.her = her; }

        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            var g = new StateGraph();
            g.AddToil(new LordToil_SingerFlock());
            return g;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref her, "her");
        }
    }

    /// <summary>Round her, ready to fall on her foes; those whose worship has faded are let go.</summary>
    public class LordToil_SingerFlock : LordToil
    {
        private Pawn Her => (lord.LordJob as LordJob_SingerFlock)?.her;

        public override void UpdateAllDuties()
        {
            Pawn her = Her;
            foreach (Pawn p in lord.ownedPawns)
            {
                IntVec3 at = her != null && her.Spawned && her.Map == p.Map ? her.Position : p.Position;
                p.mindState.duty = new PawnDuty(DutyDefOf.Defend, at, 14f);
            }
        }

        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 500 != 0) return;
            foreach (Pawn p in lord.ownedPawns.ToList())
                if (p.Dead || !LinarianCult.IsCultist(p, out Pawn h) || h != Her) LinarianCult.Release(lord, p);
            UpdateAllDuties();
        }
    }

    // ---------------------------------------------------------------- devouring one who offers himself

    public class FloatMenuOptionProvider_DevourCultist : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn p = context?.FirstSelectedPawn;
            if (p == null || !Linarian.Is(p) || !(clickedThing is Pawn q) || q == p || q.Dead) yield break;
            if (LinarianCult.SingerOf(q) != p || LinarianActs.Worship(q) < LinarianCult.Fervent) yield break;
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Avir_DevourCultist");
            if (def == null) yield break;
            yield return new FloatMenuOption("Devour " + q.LabelShort + " (offers " + q.Possessive() + "self)", () =>
                p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, q), JobTag.Misc));
        }
    }

    public class JobDriver_DevourCultist : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            Toil wait = ToilMaker.MakeToil("CultistKneels");
            wait.initAction = () =>
            {
                // he comes to her and kneels
                if (job.targetA.Pawn is Pawn q && !q.Downed && q.jobs != null)
                    q.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, pawn), JobCondition.InterruptForced);
            };
            wait.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return wait;
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil drink = Toils_General.Wait(180, TargetIndex.A);
            drink.WithProgressBarToilDelay(TargetIndex.A);
            yield return drink;
            yield return Toils_General.Do(() => { if (job.targetA.Pawn is Pawn q && !q.Dead) LinarianDevour.Devour(pawn, q); });
        }
    }

    // ---------------------------------------------------------------- her hold: those her song holds, out of their raid

    /// <summary>Those her song holds (sleepwalkers, the procession, the retinue, the captivated, the rapt) leave their
    /// raid for her hold while it lasts: a group of their own faction with nothing to do but what the song says.
    /// Without it the game - and Hospitality - took a raider no longer hostile for a guest.</summary>
    public static class LinarianHold
    {
        public static void Hold(Pawn her, Pawn q)
        {
            if (her == null || q?.Map == null || !q.RaceProps.Humanlike || q.Faction == null || q.Faction == Faction.OfPlayer) return;
            Lord cur = q.GetLord();
            if (cur?.LordJob is LordJob_SingerFlock || cur?.LordJob is LordJob_SingerHold h0 && h0.her == her) return;
            cur?.RemovePawn(q);
            Lord hold = q.Map.lordManager.lords.FirstOrDefault(l => l.faction == q.Faction && l.LordJob is LordJob_SingerHold h && h.her == her);
            if (hold != null) hold.AddPawn(q);
            else LordMaker.MakeNewLord(q.Faction, new LordJob_SingerHold(her), q.Map, new List<Pawn> { q });
        }

        /// <summary>The song let go: back to the raid if awake (a sleeper rejoins when it wakes), home if not at war.</summary>
        public static void Let(Lord lord, Pawn p)
        {
            lord.RemovePawn(p);
            if (p.Dead || !p.Spawned || p.Faction == null || p.Downed || LinarianTactics.Asleep(p)) return;
            if (p.Faction.HostileTo(Faction.OfPlayer)) LinarianRaid.Rejoin(p);
            else LordMaker.MakeNewLord(p.Faction, new LordJob_ExitMapBest(), p.Map, new List<Pawn> { p });
        }
    }

    public class LordJob_SingerHold : LordJob
    {
        public Pawn her;

        public LordJob_SingerHold() { }
        public LordJob_SingerHold(Pawn her) { this.her = her; }

        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            var g = new StateGraph();
            g.AddToil(new LordToil_SingerHold());
            return g;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref her, "her");
        }
    }

    public class LordToil_SingerHold : LordToil
    {
        public override void UpdateAllDuties()
        {
            foreach (Pawn p in lord.ownedPawns) p.mindState.duty = new PawnDuty(DutyDefOf.Defend, p.Position, 3f);
        }

        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0) return;
            foreach (Pawn p in lord.ownedPawns.ToList())
                if (p.Dead || !(p.MentalState is MentalState_Song s) || !s.Hers) LinarianHold.Let(lord, p);
        }
    }

    // ---------------------------------------------------------------- the walkers: no one's target until they strike

    /// <summary>A walker of hers (sleepwalking, the procession, the retinue) who strikes anyone not of her side is
    /// fought back from then on - and with it every walker of the same singing.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_WalkerProvoked
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn m) || !(m.MentalState is MentalState_Song s) || !s.Walker || s.provoked || s.herald == null) return;
            if (__instance == s.herald || __instance.Faction != null && __instance.Faction == s.herald.Faction) return;
            s.provoked = true;
            if (s.tactic == null || m.Map == null) return;
            foreach (Pawn o in m.Map.mapPawns.AllPawnsSpawned)
                if (o.MentalState is MentalState_Song os && os.herald == s.herald && os.tactic == s.tactic && os.castTick == s.castTick) os.provoked = true;
        }
    }

    // ---------------------------------------------------------------- held by her song: no will of their own

    /// <summary>One held by her song does only what the song says: no raiding the larder, no wandering to work or play
    /// between its orders (idling and wandering are left to it).</summary>
    [HarmonyPatch(typeof(ThinkNode_JobGiver), nameof(ThinkNode_JobGiver.TryIssueJobPackage))]
    public static class Patch_HeldHaveNoWill
    {
        public static bool Prefix(ThinkNode_JobGiver __instance, Pawn pawn, ref ThinkResult __result)
        {
            if (!(pawn?.MentalState is MentalState_Song s) || !s.Hers || pawn.Faction == Faction.OfPlayer) return true;
            string n = __instance.GetType().Name;
            if (n.Contains("Idle") || n.Contains("Wander")) return true;
            __result = ThinkResult.NoJob;
            return false;
        }
    }

    /// <summary>Struck by her, what is hers does not strike back - her cultists, and anything her song or dream holds.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_HersBearHerBlows
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn her) || her == __instance || !Linarian.Is(her) || __instance.Dead) return;
            if (!LinarianPackSiphon.Hers(her, __instance) && LinarianCult.SingerOf(__instance) != her) return;
            var ms = __instance.mindState;
            if (ms == null) return;
            if (ms.meleeThreat == her) ms.meleeThreat = null;
            if (ms.enemyTarget == her) ms.enemyTarget = null;
            Job j = __instance.CurJob;
            if (j != null && j.targetA.Thing == her && (j.def == JobDefOf.AttackMelee || j.def == JobDefOf.AttackStatic))
                __instance.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
    }

    /// <summary>One her song turned on its own people is fought back by them - the one struck, and its comrades near.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_TurnedFoughtBack
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn m) || !(m.MentalState is MentalState_Song s) || !s.Hers || m.Faction == null) return;
            if (__instance.Faction != m.Faction || __instance.Dead || !__instance.Spawned) return;
            foreach (Pawn c in GenRadial.RadialDistinctThingsAround(__instance.Position, __instance.Map, 14f, true).OfType<Pawn>().ToList())
            {
                if (c == m || c.Faction != m.Faction || c.Dead || c.Downed || c.InMentalState || c.jobs == null || c.mindState == null) continue;
                if (c.CurJob != null && (c.CurJobDef == JobDefOf.AttackMelee || c.CurJobDef == JobDefOf.AttackStatic)) continue;
                c.mindState.enemyTarget = m;
                Verb v = c.TryGetAttackVerb(m, false);
                Job j = JobMaker.MakeJob(v != null && !v.IsMeleeAttack ? JobDefOf.AttackStatic : JobDefOf.AttackMelee, m);
                j.expiryInterval = 600;
                j.checkOverrideOnExpire = true;
                c.jobs.StartJob(j, JobCondition.InterruptForced, null, false, true);
            }
        }
    }
}
