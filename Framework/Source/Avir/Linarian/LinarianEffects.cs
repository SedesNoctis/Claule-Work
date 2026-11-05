using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    [DefOf]
    public static class LinarianHediffs
    {
        public static HediffDef Avir_SongSleep;
        public static HediffDef Avir_SongCaptivated;
        public static HediffDef Avir_SongMirage;
        public static HediffDef Avir_PhantomScar;
        public static HediffDef Avir_SongDread;
        public static HediffDef Avir_SongRest;
        public static HediffDef Avir_LinarianVeil;
        public static HediffDef Avir_StrangeAche;
        public static HediffDef Avir_LinarianVeilHidden;
        public static HediffDef Avir_LinarianForm;
        public static HediffDef Avir_SongDrowsy;
        public static HediffDef Avir_SongGroggy;
        public static HediffDef Avir_DreamMark;
        public static HediffDef Avir_SlowDevour;
        public static HediffDef Avir_Sleepwalking;
        public static HediffDef Avir_Adoration;
        public static HediffDef Avir_DeadVoice;
        public static HediffDef Avir_ScarFlare;
        public static HediffDef Avir_TimeSlow;
        public static HediffDef Avir_TimeHaste;
        public static HediffDef Avir_PackBlessing;
        public static HediffDef Avir_Timeless;
        static LinarianHediffs() { DefOfHelper.EnsureInitializedInCtor(typeof(LinarianHediffs)); }

        public static HediffDef PhantomScar => Avir_PhantomScar;
        public static HediffDef Mirage => Avir_SongMirage;

        /// <summary>Adds (or refreshes) a hediff that fades after the given time.</summary>
        public static Hediff Add(Pawn q, HediffDef def, int ticks, float severity = 1f, Pawn her = null)
        {
            if (q?.health == null || def == null) return null;
            Hediff h = q.health.hediffSet.GetFirstHediffOfDef(def) ?? q.health.AddHediff(def);
            if (h == null) return null;
            h.Severity = severity;
            var src = her == null ? null : h.TryGetComp<HediffComp_SongSource>();
            if (src != null) src.herald = her;
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, ticks);
            return h;
        }

        /// <summary>Her sleep: longer under heavy eyelids, dull after with the lullaby of the field.</summary>
        public static void Sleep(Pawn q, int ticks, Pawn her = null)
        {
            if (SongShelter.ShieldedFrom(q, her)) return;
            Gene_LinarianSong g = Linarian.Song(her);
            if (g != null && g.Has("L1")) ticks = Mathf.RoundToInt(ticks * 1.5f);   // heavy eyelids' gift
            Add(q, Avir_SongSleep, ticks, 1f, her);
            if (g != null && g.Has("L5")) Add(q, Avir_SongGroggy, ticks + GenDate.TicksPerHour * 2, 1f, her);
            // a true sleep, not a faint: all weariness at once, and down they lie
            if (q.needs?.rest != null) q.needs.rest.CurLevel = 0f;
            if (q.Spawned && !q.Downed)
            {
                if (q.Faction != null && her != null && q.Faction == her.Faction)
                {
                    Job lay = JobMaker.MakeJob(JobDefOf.LayDown, q.Position);
                    lay.forceSleep = true;
                    q.jobs?.StartJob(lay, JobCondition.InterruptForced, null, false, true);
                }
                else if (her != null) LinarianMental.Start(q, her, LinarianMental.Slumber, ticks);
            }
            if (q.Spawned) FleckMaker.ThrowMetaIcon(q.Position, q.Map, FleckDefOf.SleepZ);
            if (her != null && her.Faction != Faction.OfPlayer && q.Faction == Faction.OfPlayer && q.RaceProps.Humanlike)
                Messages.Message(q.LabelShortCap + " has suddenly fallen asleep.", q, MessageTypeDefOf.CautionInput, false);
        }

        public static bool Captivate(Pawn q, Pawn her, int ticks)
        {
            if (q?.health == null || her?.Faction == null || q.Faction == her.Faction) return false;
            Hediff h = HediffMaker.MakeHediff(Avir_SongCaptivated, q);
            HediffComp_SongCaptivated c = (h as HediffWithComps)?.TryGetComp<HediffComp_SongCaptivated>();
            if (c == null) return false;
            c.origin = q.Faction;
            // an ally for a while, not a convert: they keep their faction and fight her foes
            if (!LinarianMental.Start(q, her, LinarianMental.Captive, ticks)) return false;
            q.health.AddHediff(h);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = ticks;
            var src = h.TryGetComp<HediffComp_SongSource>();
            if (src != null) src.herald = her;
            return true;
        }
    }

    /// <summary>Captivation: while it lasts the captive is hers and feels no pain; when it ends they go back to their
    /// own and fall asleep, so they can be taken.</summary>
    public class HediffCompProperties_SongCaptivated : HediffCompProperties
    {
        public HediffCompProperties_SongCaptivated() { compClass = typeof(HediffComp_SongCaptivated); }
    }

    public class HediffComp_SongCaptivated : HediffComp
    {
        public Faction origin;

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Pawn p = Pawn;
            if (p == null || p.Dead) return;
            if (origin != null && p.Faction != origin && p.Faction == Faction.OfPlayer) p.SetFaction(origin);   // captives of old saves
            if (p.MentalState is MentalState_SongPack s && s.def == LinarianMental.Captive) s.RecoverFromState();
            LinarianHediffs.Sleep(p, GenDate.TicksPerHour * 3);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref origin, "origin");
        }
    }

    // ---------------------------------------------------------------- the mental states of her song

    public static class LinarianMental
    {
        public static MentalStateDef Called => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SongCalled");
        public static MentalStateDef PackState => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SongPack");
        public static MentalStateDef HuntCalled => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SongHuntCalled");
        public static MentalStateDef Sleepwalk => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_Sleepwalk");
        public static MentalStateDef Captive => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_Captivated");
        public static MentalStateDef Bearer => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SongBearer");
        public static MentalStateDef Circling => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SongCircle");
        public static MentalStateDef Slumber => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SongSlumber");
        public static MentalStateDef CultGuard => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_CultGuard");
        public static MentalStateDef VerdictState => DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_DreamVerdict");

        public static bool StartVerdict(Pawn q, Pawn her, Pawn target, int ticks)
        {
            if (!Start(q, her, VerdictState, ticks)) return false;
            if (q.MentalState is MentalState_SongVerdict v) v.target = target;
            return true;
        }

        public static bool StartCalled(Pawn q, Pawn her, int ticks) => Start(q, her, Called, ticks);
        public static bool StartPack(Pawn a, Pawn her, int ticks) => Start(a, her, PackState, ticks);
        public static bool StartHunt(Pawn a, Pawn her, int ticks) => Start(a, her, HuntCalled, ticks);

        public static bool Start(Pawn q, Pawn her, MentalStateDef def, int ticks)
        {
            if (SongShelter.ShieldedFrom(q, her)) return false;
            if (def == null || q?.mindState?.mentalStateHandler == null) return false;
            if (q.InMentalState) q.mindState.mentalStateHandler.CurState.RecoverFromState();
            if (!q.mindState.mentalStateHandler.TryStartMentalState(def, "the Singer's aria", true)) return false;
            if (q.MentalState is MentalState_Song s)
            {
                s.herald = her; s.until = Find.TickManager.TicksGame + ticks; s.tactic = LinarianRivals.singing; s.castTick = Find.TickManager.TicksGame;
                if (s.Hers) LinarianHold.Hold(her, q);   // out of their raid, into her hold: no guests, no wandering off
            }
            q.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            return true;
        }

        /// <summary>Whom this pawn is held by, if it walks under a Herald's Call.</summary>
        public static Pawn CalledBy(Pawn q) => q?.MentalState is MentalState_SongCalled c ? c.herald : null;
    }

    /// <summary>Her lullaby's sleep on one not her own: it lies down where it stands and sleeps, whatever its orders; a
    /// blow wakes it.</summary>
    public class MentalState_SongSlumber : MentalState_Song
    {
        protected override void Drive()
        {
            if (pawn.CurJobDef == JobDefOf.LayDown) return;
            Job lay = JobMaker.MakeJob(JobDefOf.LayDown, pawn.Position);
            lay.forceSleep = true;
            lay.expiryInterval = 2000;
            Force(lay);
        }

        public override void MentalStateTick(int delta)
        {
            // her sleep holds even when she walks away: only its time, or a blow, ends it
            if (Find.TickManager.TicksGame > until) { RecoverFromState(); return; }
            if (pawn.IsHashIntervalTick(30, delta)) Drive();
        }
    }

    /// <summary>A mind held by a Herald's song: it does what the song asks until the time runs out or she is gone.</summary>
    public abstract class MentalState_Song : MentalState
    {
        public Pawn herald;
        public int until;
        /// <summary>The tactic it was sung with (a stronger rival who knows its art breaks it in her zone).</summary>
        public string tactic;
        /// <summary>When it was sung (one cast's walkers are one group), and whether it has struck anyone yet.</summary>
        public int castTick;
        public bool provoked;

        /// <summary>Walking asleep for her (sleepwalking, the procession, the retinue): no one's target until it strikes.</summary>
        public virtual bool Walker => false;

        /// <summary>Walking, fighting or worshipping under her song: no enemy of her or her people while it lasts.</summary>
        public virtual bool Hers => false;

        public override string InspectLine
        {
            get
            {
                int left = until - Find.TickManager.TicksGame;
                return base.InspectLine + (left > 0 && left < GenDate.TicksPerDay * 30 ? " (" + left.ToStringTicksToPeriod() + ")" : "");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref herald, "singer");
            Scribe_Values.Look(ref until, "until");
            Scribe_Values.Look(ref tactic, "tactic");
            Scribe_Values.Look(ref castTick, "castTick");
            Scribe_Values.Look(ref provoked, "provoked");
        }

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (herald == null || herald.Dead || !herald.Spawned || herald.Map != pawn.Map || Find.TickManager.TicksGame > until)
            {
                RecoverFromState();
                return;
            }
            if (pawn.IsHashIntervalTick(30, delta)) Drive();
        }

        protected abstract void Drive();

        /// <summary>Her own song on it (the siphon and the rest): it holds still for her.</summary>
        protected bool HeldStill()
        {
            Job hj = herald?.CurJob;
            return hj != null && hj.targetA.Thing == pawn && (hj.ability != null || hj.verbToUse is Verb_CastAbility);
        }

        /// <summary>An attack with whatever it holds: a gun shoots, only the empty-handed close in.</summary>
        protected Job AttackJob(Thing t)
        {
            Verb v = pawn.TryGetAttackVerb(t, false);
            Job j = JobMaker.MakeJob(v != null && !v.IsMeleeAttack ? JobDefOf.AttackStatic : JobDefOf.AttackMelee, t);
            j.expiryInterval = 300;
            j.checkOverrideOnExpire = true;
            return j;
        }

        protected void Force(Job job)
        {
            if (job == null || pawn.Downed) return;
            if (HeldStill()) job = JobMaker.MakeJob(JobDefOf.Wait, 60);
            if (pawn.CurJob != null && pawn.CurJob.def == job.def && pawn.CurJob.targetA == job.targetA) return;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
        }
    }

    /// <summary>The Call: walk to her. While in it, the called cannot harm her; beside her, she devours them.</summary>
    public class MentalState_SongCalled : MentalState_Song
    {
        public override bool Hers => true;

        protected override void Drive()
        {
            if ((pawn.Position - herald.Position).LengthHorizontal <= 1.5f) { Force(JobMaker.MakeJob(JobDefOf.Wait, 60)); return; }
            // the weapon grows heavy in a hand that walks to her: now and then it slips
            if (pawn.equipment?.Primary != null && Rand.Chance(0.04f)) pawn.equipment.TryDropEquipment(pawn.equipment.Primary, out ThingWithComps _, pawn.Position);
            Job j = JobMaker.MakeJob(JobDefOf.Goto, herald);
            j.expiryInterval = 120;
            j.checkOverrideOnExpire = true;
            j.locomotionUrgency = LocomotionUrgency.Walk;
            Force(j);
        }
    }

    /// <summary>The Pack: a beast of the land falls on her foes, and only on them.</summary>
    public class MentalState_SongPack : MentalState_Song
    {
        public override bool Hers => true;
        public override bool Walker => def == LinarianMental.Sleepwalk;

        protected override void Drive()
        {
            if (pawn.RaceProps.Animal) LinarianPack.Keep(pawn, herald);   // the gift of the raid: a fifth of the pain
            if (pawn.RaceProps.Animal && tactic == "P5") LinarianPack.Guises(pawn, herald);   // with her mirages: shadows of its own shape
            // every foe of hers on the map, the nearest first
            Pawn prey = pawn.Map.mapPawns.AllPawnsSpawned
                .Where(q => !q.Dead && !q.Downed && q != herald && q != pawn && q.HostileTo(herald) && !(q.MentalState is MentalState_SongPack) && !LinarianTactics.Asleep(q))
                .OrderBy(q => (q.Position - pawn.Position).LengthHorizontalSquared).FirstOrDefault();
            if (prey == null) prey = ExtraPrey();
            if (prey == null)
            {
                // no foe left: they amble about where they are - never crowding round her
                if (pawn.pather?.Moving == true) return;
                if (!CellFinder.TryFindRandomReachableNearbyCell(pawn.Position, pawn.Map, 6f, TraverseParms.For(pawn),
                        x => x.Standable(pawn.Map) && (x - herald.Position).LengthHorizontal >= 4f, null, out IntVec3 to)) return;
                Job amble = JobMaker.MakeJob(JobDefOf.Goto, to);
                amble.locomotionUrgency = LocomotionUrgency.Amble;
                Force(amble);
                return;
            }
            Job j = AttackJob(prey);
            j.killIncappedTarget = true;
            j.expiryInterval = 300;
            Force(j);
        }

        protected virtual Pawn ExtraPrey() => null;

        public override bool ForceHostileTo(Thing t) => (!Walker || provoked) && t is Pawn q && q.HostileTo(herald);
        public override bool ForceHostileTo(Faction f) => (!Walker || provoked) && herald != null && f != null && f.HostileTo(herald.Faction ?? Faction.OfPlayer);
    }

    /// <summary>A worshipper at her side: guards her, and (with her blessing) falls on those who do not bow -
    /// even its own.</summary>
    public class MentalState_SongCultGuard : MentalState_SongPack
    {
        private bool Fierce => Linarian.Song(herald)?.Has("C3") == true && LinarianActs.Worship(pawn) >= 0.5f;

        protected override Pawn ExtraPrey()
        {
            if (!Fierce) return null;
            return GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 20f, true).OfType<Pawn>()
                .Where(q => q != pawn && q != herald && !q.Dead && !q.Downed && q.RaceProps.Humanlike && q.Faction == pawn.Faction
                            && LinarianSource.Of(q, LinarianHediffs.Avir_Adoration) != herald)
                .OrderBy(q => (q.Position - pawn.Position).LengthHorizontalSquared).FirstOrDefault();
        }

        public override bool ForceHostileTo(Thing t) => base.ForceHostileTo(t) || (Fierce && t is Pawn q && q.Faction == pawn.Faction && LinarianSource.Of(q, LinarianHediffs.Avir_Adoration) != herald);
    }

    /// <summary>The night verdict: her sleepers rise against one who wronged her.</summary>
    public class MentalState_SongVerdict : MentalState_Song
    {
        public Pawn target;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref target, "target");
        }

        protected override void Drive()
        {
            if (target == null || target.Dead || target.MapHeld != pawn.Map) { RecoverFromState(); return; }
            Job j = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            j.killIncappedTarget = true;
            j.expiryInterval = 300;
            Force(j);
        }

        public override bool ForceHostileTo(Thing t) => t == target;
    }

    /// <summary>The song-hunt: an animal marked for hunting walks to her and dies as if slaughtered.</summary>
    public class MentalState_SongHuntCalled : MentalState_Song
    {
        protected override void Drive()
        {
            if ((pawn.Position - herald.Position).LengthHorizontal <= 1.5f)
            {
                IntVec3 at = pawn.Position;
                Map map = pawn.Map;
                pawn.Map.designationManager.RemoveAllDesignationsOn(pawn);
                ExecutionUtility.DoExecutionByCut(herald, pawn);
                Linarian.Song(herald)?.Learn(150f);
                // she drinks of what she called: the arcane hunger always, the belly too when she is the wild's
                Gene_ArcaneHunger.Feed(herald, 0.4f);   // a whole beast, drunk whole
                Need_Food food = herald.needs?.food;
                if (food != null && herald.Faction != Faction.OfPlayer) food.CurLevelPercentage = Mathf.Min(1f, food.CurLevelPercentage + 0.5f + 0.4f * pawn.BodySize);
                Avir.Ritualistics.RitualFx.Mote(at.ToVector3Shifted(), map, 1.2f, new Color(0.85f, 0.85f, 1f));
                return;
            }
            Job j = JobMaker.MakeJob(JobDefOf.Goto, herald);
            j.expiryInterval = 200;
            j.locomotionUrgency = LocomotionUrgency.Walk;
            Force(j);
        }
    }

    // ---------------------------------------------------------------- harm and scars

    /// <summary>Those walking in her Call cannot harm her.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_LinarianCalledHarmless
    {
        public static void Postfix(Pawn __instance, ref DamageInfo dinfo, ref bool absorbed)
        {
            if (absorbed || !(dinfo.Instigator is Pawn q)) return;
            if (LinarianMental.CalledBy(q) == __instance) absorbed = true;
        }
    }

    /// <summary>Phantom scars: the wound she gives heals, the pain stays.</summary>
    /// (Read off the injury itself as it is added: the blow's own record has no part yet when the part is chosen by the
    /// game, which left the scar on the whole body.)
    [HarmonyPatch(typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury", new[] { typeof(Pawn), typeof(Hediff_Injury), typeof(DamageInfo), typeof(DamageWorker.DamageResult) })]
    public static class Patch_LinarianPhantomScars
    {
        public static void Postfix(Pawn pawn, Hediff_Injury injury, DamageInfo dinfo)
        {
            Pawn __instance = pawn;
            if (__instance == null || injury?.Part == null || __instance.Dead || !(dinfo.Instigator is Pawn her)) return;
            if (dinfo.Def == null || dinfo.Def.isRanged || dinfo.Def.isExplosive) return;
            if (__instance.RaceProps.IsMechanoid || !__instance.RaceProps.IsFlesh) return;   // metal keeps no memory of pain
            Gene_LinarianSong g = Linarian.Song(her);
            if (g == null || !g.Has("S1")) return;
            // the scar stays where her claws went in: on the struck part, not on the whole body
            BodyPartRecord part = injury.Part;
            if (!__instance.health.hediffSet.GetNotMissingParts().Contains(part)) return;   // the part is gone
            Hediff h = __instance.health.hediffSet.hediffs.FirstOrDefault(x => x.def == LinarianHediffs.Avir_PhantomScar && x.Part == part);
            float add = 0.12f;
            if (h == null)
            {
                h = HediffMaker.MakeHediff(LinarianHediffs.Avir_PhantomScar, __instance, part);
                h.Severity = add;
                __instance.health.AddHediff(h, part);
            }
            else h.Severity = Mathf.Min(LinarianHediffs.Avir_PhantomScar.maxSeverity, h.Severity + add);
            var c = h.TryGetComp<HediffComp_SongSource>();
            if (c != null) c.herald = her;
        }
    }

    // ---------------------------------------------------------------- what the network gives as stats

    public class StatPart_LinarianSong : StatPart
    {
        public string node;
        public float offset;

        public bool inForm;

        private bool Applies(StatRequest req) => req.Thing is Pawn p && Linarian.Song(p) is Gene_LinarianSong g && (node == "*" || g.Has(node)) && (!inForm || LinarianVeil.InForm(p) != null);

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (Applies(req)) val += offset;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (Applies(req))
                return "Singer's aria (" + (node == "*" ? "four wings" : LinarianNetwork.Get(node)?.label) + "): " + offset.ToStringByStyle(parentStat.toStringStyle, ToStringNumberSense.Offset);
            return null;
        }
    }
}
