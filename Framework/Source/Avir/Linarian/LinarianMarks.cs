using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    /// <summary>Whose mark this is: the Herald behind a dream-mark, a slow devouring, a worship, a drowsiness.</summary>
    public class HediffCompProperties_SongSource : HediffCompProperties
    {
        public HediffCompProperties_SongSource() { compClass = typeof(HediffComp_SongSource); }
    }

    public class HediffComp_SongSource : HediffComp
    {
        public Pawn herald;
        public bool flag;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref herald, "singer");
            Scribe_Values.Look(ref flag, "flag");
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            // the drowsy haze ends in sleep for those it took
            Pawn p = Pawn;
            if (parent.def != LinarianHediffs.Avir_SongDrowsy || !flag || p == null || p.Dead) return;
            LinarianHediffs.Sleep(p, Mathf.RoundToInt(GenDate.TicksPerHour * 1.5f), herald);
            if (Linarian.Song(herald)?.Dark("L") == true) LinarianHediffs.Add(p, LinarianHediffs.Avir_Timeless, GenDate.TicksPerHour * 2);
        }

        public override string CompLabelInBracketsExtra => herald != null && parent.def != LinarianHediffs.Avir_SongDrowsy && Pawn?.Faction == Faction.OfPlayer && herald.Faction == Faction.OfPlayer ? herald.LabelShort : null;
    }

    public static class LinarianSource
    {
        public static Pawn Of(Pawn q, HediffDef def) =>
            q?.health?.hediffSet?.GetFirstHediffOfDef(def)?.TryGetComp<HediffComp_SongSource>()?.herald;

        /// <summary>Adds a mark of hers (ticks below 0: it lasts until taken off).</summary>
        public static Hediff Add(Pawn q, HediffDef def, Pawn her, int ticks, float severity = 1f)
        {
            if (q?.health == null || def == null || SongShelter.ShieldedFrom(q, her)) return null;
            Hediff h = q.health.hediffSet.GetFirstHediffOfDef(def) ?? q.health.AddHediff(def);
            if (h == null) return null;
            h.Severity = severity;
            var c = h.TryGetComp<HediffComp_SongSource>();
            if (c != null) c.herald = her;
            var d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null && ticks > 0) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, ticks);
            return h;
        }
    }

    /// <summary>A dead man's voice: for a day she has the best skill of one she devoured.</summary>
    public class HediffCompProperties_DeadVoice : HediffCompProperties
    {
        public HediffCompProperties_DeadVoice() { compClass = typeof(HediffComp_DeadVoice); }
    }

    public class HediffComp_DeadVoice : HediffComp
    {
        public SkillDef skill;
        public int level;
        public string name;

        public override string CompLabelInBracketsExtra => name == null ? null : name + ", " + skill?.label + " " + level;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Defs.Look(ref skill, "skill");
            Scribe_Values.Look(ref level, "level");
            Scribe_Values.Look(ref name, "name");
        }
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevel))]
    public static class Patch_DeadVoiceSkill
    {
        public static void Postfix(SkillRecord __instance, ref int __result)
        {
            Pawn p = __instance.Pawn;
            if (p?.health?.hediffSet == null || LinarianHediffs.Avir_DeadVoice == null) return;
            var c = p.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_DeadVoice)?.TryGetComp<HediffComp_DeadVoice>();
            if (c != null && c.skill == __instance.def && c.level > __result) __result = c.level;
        }
    }

    /// <summary>The veil's gift: a blow struck from under her veil lands twice as hard.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_LinarianVeilStrike
    {
        public static void Prefix(ref DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn her) || !LinarianVeil.Veiled(her)) return;
            if (Linarian.Song(her)?.Has("G1") != true || dinfo.Def == null || !dinfo.Def.harmsHealth) return;
            dinfo.SetAmount(dinfo.Amount * 2f);
        }
    }

    // ---------------------------------------------------------------- her sleepers, struck; the timeless

    /// <summary>Her sleepers are not to be woken: struck, they wake in a berserk rage (Dreaming 2). The timeless
    /// cannot be harmed at all.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_LinarianSleepers
    {
        public static void Postfix(Pawn __instance, ref DamageInfo dinfo, ref bool absorbed)
        {
            if (absorbed || __instance.health?.hediffSet == null) return;
            if (__instance.health.hediffSet.HasHediff(LinarianHediffs.Avir_Timeless)) { absorbed = true; return; }
            if (!LinarianTactics.Asleep(__instance)) return;
            if (dinfo.Def == null || !dinfo.Def.harmsHealth) return;
            Pawn her = LinarianSource.Of(__instance, LinarianHediffs.Avir_DreamMark) ?? LinarianSource.Of(__instance, LinarianHediffs.Avir_SlowDevour);
            if (her != null && dinfo.Instigator == her) return;   // her own blow does not wake what is hers
            bool rage = her != null && Linarian.Song(her)?.Has("D2") == true;
            // a blow wakes her sleepers
            foreach (Hediff h in __instance.health.hediffSet.hediffs.Where(h => h.def == LinarianHediffs.Avir_SongSleep).ToList()) __instance.health.RemoveHediff(h);
            if (__instance.MentalState is MentalState_SongSlumber s) s.RecoverFromState();
            RestUtility.WakeUp(__instance);
            if (!rage) return;
            __instance.mindState?.mentalStateHandler?.TryStartMentalState(MentalStateDefOf.Berserk, "woken from a Singer's dream", true);
        }
    }

    /// <summary>Near a Herald with the lullaby, her own are not woken by noise.</summary>
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory), new[] { typeof(Thought_Memory), typeof(Pawn) })]
    public static class Patch_LinarianQuietSleep
    {
        public static bool Prefix(MemoryThoughtHandler __instance, Thought_Memory newThought)
        {
            if (newThought?.def != ThoughtDefOf.SleepDisturbed) return true;
            Pawn p = __instance.pawn;
            if (p?.Map == null) return true;
            foreach (Pawn her in MapComponent_LinarianSong.HeraldsOn(p.Map))
                if (her.Faction == p.Faction && Linarian.Song(her) is Gene_LinarianSong g && g.Has("L2") && LinarianZone.InZone(her, g, p.Position)) return false;
            return true;
        }
    }

    // ---------------------------------------------------------------- a Herald's healing

    /// <summary>Any Herald lifts phantom scars - another Herald's too - and heals the failing mind.</summary>
    public static class HeraldHealing
    {
        public static readonly string[] MindIlls = { "Dementia", "Alzheimers" };

        /// <summary>The pain she lifts only once her path of it has grown (its second star); the failing mind, always.</summary>
        public static bool CanLift(Pawn healer) => Linarian.Song(healer)?.Has("S2") == true;

        public static bool Needs(Pawn q, Pawn healer = null) => q?.health?.hediffSet != null &&
            q.health.hediffSet.hediffs.Any(h => h.def == LinarianHediffs.PhantomScar && (healer == null || CanLift(healer)) || MindIlls.Contains(h.def.defName));

        public static void Heal(Pawn healer, Pawn q)
        {
            foreach (Hediff h in q.health.hediffSet.hediffs.Where(h => h.def == LinarianHediffs.PhantomScar && CanLift(healer) || MindIlls.Contains(h.def.defName)).ToList())
                q.health.RemoveHediff(h);
            if (q.Spawned) Avir.Ritualistics.RitualFx.Mote(q.DrawPos, q.Map, 1.4f, new Color(0.85f, 0.9f, 1f));
            Messages.Message(healer.LabelShortCap + " sings " + q.LabelShort + "'s pain and failing mind away.", q, MessageTypeDefOf.PositiveEvent, false);
        }
    }

    public class FloatMenuOptionProvider_HeraldHeal : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn p = context?.FirstSelectedPawn;
            if (p == null || !Linarian.Is(p) || !(clickedThing is Pawn q) || q == p || !HeraldHealing.Needs(q, p)) yield break;
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Avir_HeraldHeal");
            if (def == null) yield break;
            yield return new FloatMenuOption("Sing away " + q.LabelShort + "'s pain and failing mind", () =>
                p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, q), JobTag.Misc));
        }
    }

    public class JobDriver_HeraldHeal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil sing = Toils_General.Wait(600, TargetIndex.A);
            sing.WithProgressBarToilDelay(TargetIndex.A);
            yield return sing;
            yield return Toils_General.Do(() => { if (job.targetA.Pawn is Pawn q && !q.Dead) HeraldHealing.Heal(pawn, q); });
        }
    }
}
