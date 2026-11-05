using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    // =====================================================================================
    // PHANTASMAL WOUNDS, REWORKED.
    //   Rend: now and then a scar runs up the limb like a blade.
    //   Marked prey: one who bears her scars - marked to suffer, the scars made unbearable; or, set to mercy, lifted.
    //   Terror: pain swells and they scream; the screaming unnerves their own, and kin run to them.
    //   The string: those with deep scars try to cut the aching limbs off - if a hand of theirs can reach.
    //   Flaying: every scarred limb looks flayed; the weak of heart who see it run.
    //   Passives: the scarred glow red to her; terror and pain bend the mind; the pain wanders up the body;
    //   the scarred who break flee for the edge of the land and fall there.
    // =====================================================================================
    public static class LinarianWounds
    {
        private const int H = GenDate.TicksPerHour;

        private static HediffDef terrorPain, terrorHeard, flayed;
        private static MentalStateDef scream, family, sever, flight;
        public static HediffDef TerrorPain => terrorPain ?? (terrorPain = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_TerrorPain"));
        public static HediffDef TerrorHeard => terrorHeard ?? (terrorHeard = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_TerrorHeard"));
        public static HediffDef Flayed => flayed ?? (flayed = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Flayed"));
        public static MentalStateDef Scream => scream ?? (scream = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_TerrorScream"));
        public static MentalStateDef Family => family ?? (family = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_FamilyPanic"));
        public static MentalStateDef Sever => sever ?? (sever = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SelfSever"));
        public static MentalStateDef Flight => flight ?? (flight = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_FlayFlight"));

        private static readonly Dictionary<int, int> nextWander = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> fled = new Dictionary<int, int>();

        public static List<Hediff> Scars(Pawn q) => q.health.hediffSet.hediffs.Where(h => h.def == LinarianHediffs.PhantomScar).ToList();

        /// <summary>Adds (or deepens) her scar on one part.</summary>
        public static Hediff Scar(Pawn q, BodyPartRecord part, float severity, Pawn her)
        {
            if (part != null && !q.health.hediffSet.GetNotMissingParts().Contains(part)) return null;
            Hediff h = q.health.hediffSet.hediffs.FirstOrDefault(x => x.def == LinarianHediffs.PhantomScar && x.Part == part);
            if (h == null)
            {
                h = HediffMaker.MakeHediff(LinarianHediffs.PhantomScar, q, part);
                h.Severity = severity;
                q.health.AddHediff(h, part);
            }
            else h.Severity = Mathf.Min(LinarianHediffs.PhantomScar.maxSeverity, Mathf.Max(h.Severity, severity));
            var c = h.TryGetComp<HediffComp_SongSource>();
            if (c != null && her != null) c.herald = her;
            return h;
        }

        // ---------------------------------------------------------------- rend: the scar runs up

        public static void RunUp(Pawn her, Gene_LinarianSong g, Pawn q, Hediff h)
        {
            BodyPartRecord from = h.Part, to = from?.parent;
            if (to == null || q.Dead || !q.health.hediffSet.GetNotMissingParts().Contains(to)) return;
            Scar(q, to, h.Severity, her);
            if (her.Faction == Faction.OfPlayer || q.Faction == Faction.OfPlayer)
                Messages.Message("Like a blade, the scar runs up " + q.LabelShort + "'s " + from.Label + " into the " + to.Label + ".", q, MessageTypeDefOf.NeutralEvent, false);
        }

        // ---------------------------------------------------------------- marked prey, or mercy

        public static void MarkedPrey(Pawn her, Gene_LinarianSong g, Pawn q)
        {
            if (q == null) return;
            if (g.preyLift) { Lift(her, g, q); return; }
            HediffDef marked = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_MarkedPrey");
            if (marked != null) LinarianHediffs.Add(q, marked, LinarianZone.Ticks(H, g), 1f, her);
            bool burn = Rand.Chance(LinarianTactics.Chance(her, g, q, false, "S"));
            if (burn) foreach (Hediff h in Scars(q)) h.Severity = Mathf.Max(h.Severity, 1.25f);
            if (q.Spawned) FleckMaker.ThrowMetaIcon(q.Position, q.Map, FleckDefOf.IncapIcon);
            LinarianTactics.Notify(her, q.LabelShortCap + " is her prey" + (burn ? "; every scar burns past bearing." : "; the scars hold, for now."));
        }

        /// <summary>She lifts her pain - only what lives in the mind: a Singer is no healer of flesh. With flaying's gift
        /// she takes all her phantasms with it (the terror, the flaying) and clears a failing mind.</summary>
        public static void Lift(Pawn her, Gene_LinarianSong g, Pawn q)
        {
            if (q == null) return;
            foreach (Hediff h in Scars(q)) q.health.RemoveHediff(h);
            if (g != null && g.Has("S5"))
                foreach (Hediff h in q.health.hediffSet.hediffs.Where(h => HeraldHealing.MindIlls.Contains(h.def.defName) || h.def == TerrorPain || h.def == TerrorHeard || h.def == Flayed).ToList())
                    q.health.RemoveHediff(h);
            if (q.Spawned) Avir.Ritualistics.RitualFx.Mote(q.DrawPos, q.Map, 1.4f, new Color(0.85f, 0.9f, 1f));
            LinarianTactics.Notify(her, q.LabelShortCap + "'s phantasmal pain is lifted.");
        }

        // ---------------------------------------------------------------- terror

        public static void Terror(Pawn her, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = 0;
            IEnumerable<Pawn> who = LinarianActs.Scarred(her.Map).Where(q => (q.HostileTo(her) || LinarianCult.SingerOf(q) == her && q.Faction != her.Faction || LinarianPackSiphon.Held(her, q)) && LinarianZone.InZone(her, g, q.Position)).Concat(LinarianZone.FoesAt(her, c, r)).Distinct();
            foreach (Pawn q in who.Where(LinarianTactics.Takeable).ToList())
            {
                if (!Rand.Chance(LinarianTactics.Chance(her, g, q, false, "S"))) continue;
                LinarianHediffs.Add(q, TerrorPain, LinarianZone.Ticks(H * 2, g), 1f, her);
                if (!q.Downed && q.RaceProps.Humanlike) LinarianMental.Start(q, her, Scream, LinarianZone.Ticks(H * 2f, g));
                n++;
            }
            LinarianTactics.Notify(her, n > 0 ? n + " scream." : "No one breaks.");
        }

        /// <summary>Terror and pain bend the mind: those under her terror yield more, and the more it hurts, the more.</summary>
        public static float Bend(Pawn her, Pawn q, float soc)
        {
            if (Linarian.Song(her)?.Has("S3") != true || q?.health?.hediffSet == null) return soc;
            bool afraid = q.health.hediffSet.HasHediff(LinarianHediffs.Avir_SongDread) || TerrorPain != null && q.health.hediffSet.HasHediff(TerrorPain);
            return afraid ? soc * (1.25f + 0.75f * Mathf.Clamp01(q.health.hediffSet.PainTotal)) : soc;
        }

        public static bool Kin(Pawn a, Pawn b) =>
            a?.relations != null && b != null && (a.relations.FamilyByBlood.Contains(b)
                || a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b)
                || a.relations.DirectRelationExists(PawnRelationDefOf.Lover, b)
                || a.relations.DirectRelationExists(PawnRelationDefOf.Fiance, b));

        /// <summary>A scream: its own lose their nerve, and kin run to it.</summary>
        public static void Cry(Pawn crier, Pawn her)
        {
            if (!crier.Spawned) return;
            foreach (Pawn a in crier.Map.mapPawns.SpawnedPawnsInFaction(crier.Faction ?? Faction.OfPlayer).ToList())
            {
                if (a == crier || a == her || a.Dead || a.Downed || !a.RaceProps.Humanlike || a.InMentalState) continue;
                float d = (a.Position - crier.Position).LengthHorizontal;
                if (Kin(a, crier) && d <= 40f && Rand.Chance(Mathf.Clamp01(0.35f * LinarianTactics.Soc(her, a, "S"))))
                {
                    if (LinarianMental.Start(a, her, Family, H / 2) && a.MentalState is MentalState_SongFamily f) f.crier = crier;
                    continue;
                }
                if (d > 12f) continue;
                LinarianHediffs.Add(a, TerrorHeard, H, 1f, her);
                if (Rand.Chance(0.04f * LinarianTactics.Soc(her, a, "S")))
                    a.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, "a scream of terror", true);
            }
        }

        // ---------------------------------------------------------------- the string: the aching limb, cut away

        private static readonly HashSet<string> limbs = new HashSet<string> { "Shoulder", "Arm", "Hand", "Finger", "Leg", "Foot", "Toe" };

        private static bool Limb(BodyPartRecord r) =>
            r != null && r.depth == BodyPartDepth.Outside && r.parent != null
            && (limbs.Contains(r.def.defName) || r.def.tags.Any(t => t == BodyPartTagDefOf.ManipulationLimbCore || t == BodyPartTagDefOf.ManipulationLimbSegment
                || t == BodyPartTagDefOf.ManipulationLimbDigit || t == BodyPartTagDefOf.MovingLimbCore || t == BodyPartTagDefOf.MovingLimbSegment || t == BodyPartTagDefOf.MovingLimbDigit));

        private static bool Hand(BodyPartRecord r) => r.def.defName == "Hand" || r.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbSegment) && r.parts.Any(c => c.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbDigit));

        private static bool Within(BodyPartRecord x, BodyPartRecord anc)
        {
            for (BodyPartRecord r = x.parent; r != null; r = r.parent) if (r == anc) return true;
            return false;
        }

        /// <summary>The scarred limbs this pawn can cut away: a hand of its own must reach - not one on that limb, nor
        /// the limb it hangs from.</summary>
        public static List<BodyPartRecord> Cuttable(Pawn q)
        {
            var res = new List<BodyPartRecord>();
            if (q?.health?.hediffSet == null) return res;
            List<BodyPartRecord> have = q.health.hediffSet.GetNotMissingParts().ToList();
            List<BodyPartRecord> hands = have.Where(Hand).ToList();
            foreach (Hediff h in Scars(q))
            {
                BodyPartRecord t = h.Part;
                if (h.Severity < 0.35f || !Limb(t) || !have.Contains(t) || res.Contains(t)) continue;
                if (hands.Any(hd => hd != t && !Within(hd, t) && !Within(t, hd))) res.Add(t);
            }
            return res;
        }

        public static void String(Pawn her, Gene_LinarianSong g)
        {
            int n = 0, held = 0;
            foreach (Pawn q in LinarianZone.InHerZone(her, g).ToList())
            {
                if (q == her || q.Dead || q.Downed || !q.RaceProps.Humanlike || q.Faction == her.Faction && her.Faction != null || !LinarianTactics.Takeable(q)) continue;
                if (Cuttable(q).Count == 0) continue;
                if (!Rand.Chance(LinarianTactics.Chance(her, g, q, false, "S"))) { held++; continue; }
                if (LinarianMental.Start(q, her, Sever, LinarianZone.Ticks(H * 2, g))) n++;
            }
            LinarianTactics.Notify(her, n > 0 ? n + " reach for the blade" + (held > 0 ? "; " + held + " hold firm." : ".") : held > 0 ? "they hold firm against her." : "No one bears scars deep enough.");
        }

        public static void Cut(Pawn q, BodyPartRecord part)
        {
            var h = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, q, part);
            h.lastInjury = HediffDefOf.Cut;
            q.health.AddHediff(h, part);
            if (q.Spawned)
            {
                FilthMaker.TryMakeFilth(q.Position, q.Map, q.RaceProps.BloodDef ?? ThingDefOf.Filth_Blood, 3);
                MoteMaker.ThrowText(q.DrawPos, q.Map, "It stops hurting...", 3f);
            }
            if (q.Faction == Faction.OfPlayer || q.Map == Find.CurrentMap)
                Messages.Message(q.LabelShortCap + " cuts away " + q.Possessive() + " " + part.Label + ".", q, MessageTypeDefOf.NegativeHealthEvent, false);
        }

        // ---------------------------------------------------------------- flaying

        public static void Flay(Pawn her, Gene_LinarianSong g)
        {
            int n = 0, fled = 0;
            var seen = new HashSet<Pawn>();
            foreach (Pawn q in LinarianActs.Scarred(her.Map).Where(q => q != her && (her.Faction == null || q.Faction != her.Faction) && LinarianZone.InZone(her, g, q.Position)).ToList())
            {
                LinarianHediffs.Add(q, Flayed, LinarianZone.Ticks(H, g), 1f, her);
                n++;
                // the weak of heart who see it run
                foreach (Pawn w in LinarianZone.Around(q.Map, q.Position, 9f).ToList())
                {
                    if (w == q || w == her || w.Dead || w.Downed || w.InMentalState || !w.RaceProps.Humanlike || seen.Contains(w)) continue;
                    if (her.Faction != null && w.Faction == her.Faction || !LinarianTactics.Takeable(w) || !GenSight.LineOfSight(w.Position, q.Position, q.Map)) continue;
                    seen.Add(w);
                    if (Rand.Chance(Mathf.Clamp01(LinarianTactics.Soc(her, w, "S") - 0.6f))
                        && w.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, "the sight of flayed flesh", true)) fled++;
                }
            }
            LinarianTactics.Notify(her, n > 0 ? "the scars of " + n + " open to the bone" + (fled > 0 ? "; " + fled + " who saw it run." : ".") : "no one here carries her scars.");
        }

        // ---------------------------------------------------------------- what the passives do over time

        public static void Tick(Map map, int now)
        {
            if (now % 250 != 0) return;
            foreach (Pawn q in LinarianActs.Scarred(map))
            {
                Pawn her = LinarianSource.Of(q, LinarianHediffs.PhantomScar);
                Gene_LinarianSong g = Linarian.Song(her);
                if (g == null || her.Dead) continue;
                if (g.Has("S4")) Wander(her, q, now);
                if (g.Has("S5")) Glimpse(her, q, now);
            }
        }

        /// <summary>The pain wanders up the body, every few hours: finger to hand, hand to arm, arm to the shoulder, and on.</summary>
        private static void Wander(Pawn her, Pawn q, int now)
        {
            if (!nextWander.TryGetValue(q.thingIDNumber, out int at)) { nextWander[q.thingIDNumber] = now + Rand.Range(H * 2, H * 4); return; }
            if (now < at) return;
            nextWander[q.thingIDNumber] = now + Rand.Range(H * 2, H * 4);
            float soc = Mathf.Clamp01(LinarianTactics.Soc(her, q, "S"));
            Hediff h = Scars(q).Where(x => x.Part != null).RandomElementWithFallback();
            for (int i = 0; h != null && i < 4; i++)
            {
                BodyPartRecord to = h.Part.parent;
                if (to == null)   // from the trunk it leaps anywhere outside
                    to = q.health.hediffSet.GetNotMissingParts().Where(r => r.depth == BodyPartDepth.Outside && r != h.Part).RandomElementWithFallback();
                if (to == null || !q.health.hediffSet.GetNotMissingParts().Contains(to)) break;
                float sev = h.Severity;
                q.health.RemoveHediff(h);
                h = Scar(q, to, sev, her);
                if (!Rand.Chance(0.5f * soc)) break;
            }
        }

        /// <summary>Out of the corner of the eye the scarred limb is raw meat - and then it is not. The scarred who come
        /// near breaking break at once: they flee for the edge of the land, and fall there.</summary>
        private static void Glimpse(Pawn her, Pawn q, int now)
        {
            if (her.Faction != null && q.Faction == her.Faction) return;
            if (q.Downed || q.InMentalState || q.needs?.mood == null || q.mindState?.mentalBreaker == null || !q.RaceProps.Humanlike) return;
            if (fled.TryGetValue(q.thingIDNumber, out int last) && now - last < H * 2) return;
            if (q.mindState.mentalBreaker.CurMood >= q.mindState.mentalBreaker.BreakThresholdMinor) return;
            if (!LinarianMental.Start(q, her, Flight, H * 6)) return;
            if (q.Spawned) MoteMaker.ThrowText(q.DrawPos, q.Map, "My arm- my ARM-", 3f);
        }

        public static void Fell(Pawn q)
        {
            fled[q.thingIDNumber] = Find.TickManager.TicksGame;
            LinarianHediffs.Add(q, Flayed, H * 2, 1f, LinarianSource.Of(q, LinarianHediffs.PhantomScar));
            if (q.Spawned) MoteMaker.ThrowText(q.DrawPos, q.Map, "No further...", 3f);
        }
    }

    /// <summary>Terror: it hurts, and it screams - and stays where it stands.</summary>
    public class MentalState_SongScream : MentalState_Song
    {
        private int nextCry;

        protected override void Drive()
        {
            int now = Find.TickManager.TicksGame;
            if (Rand.Chance(0.12f) && pawn.Spawned) MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, Rand.Element("AAAAH!", "Get it off me!", "It burns!", "Help me!"), 2.5f);
            if (now >= nextCry) { nextCry = now + 250; LinarianWounds.Cry(pawn, herald); }
            Force(JobMaker.MakeJob(JobDefOf.Wait, 90));
        }
    }

    /// <summary>Kin of one who screams run to them, and stay by them.</summary>
    public class MentalState_SongFamily : MentalState_Song
    {
        public Pawn crier;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref crier, "crier");
        }

        protected override void Drive()
        {
            if (crier == null || crier.Dead || !crier.Spawned || crier.Map != pawn.Map || !(crier.MentalState is MentalState_SongScream) && !crier.Downed)
            {
                RecoverFromState();
                return;
            }
            if ((pawn.Position - crier.Position).LengthHorizontal <= 1.9f) { Force(JobMaker.MakeJob(JobDefOf.Wait, 60)); return; }
            Job j = JobMaker.MakeJob(JobDefOf.Goto, crier);
            j.expiryInterval = 120;
            j.checkOverrideOnExpire = true;
            j.locomotionUrgency = LocomotionUrgency.Sprint;
            Force(j);
        }
    }

    /// <summary>The string: the aching limb must go. Can be stopped - and does not fight.</summary>
    public class MentalState_SongSever : MentalState_Song
    {
        private int cutAt;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cutAt, "cutAt");
        }

        protected override void Drive()
        {
            List<BodyPartRecord> l = LinarianWounds.Cuttable(pawn);
            if (l.Count == 0) { RecoverFromState(); return; }
            Force(JobMaker.MakeJob(JobDefOf.Wait, 120));
            int now = Find.TickManager.TicksGame;
            if (cutAt == 0) cutAt = now + 300;
            if (now < cutAt)
            {
                if (Rand.Chance(0.1f) && pawn.Spawned) MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, Rand.Element("Make it stop.", "It has to go.", "Off. Off!"), 2.5f);
                return;
            }
            cutAt = now + 400;
            BodyPartRecord part = l.MaxBy(r => LinarianWounds.Scars(pawn).Where(h => h.Part == r).Sum(h => h.Severity));
            LinarianWounds.Cut(pawn, part);
        }
    }

    /// <summary>Flaying's passive: for the edge of the land - and down, at the very edge.</summary>
    public class MentalState_SongFlayFlight : MentalState_Song
    {
        public override void MentalStateTick(int delta)
        {
            // she need not be near: the pain is enough
            if (Find.TickManager.TicksGame > until) { RecoverFromState(); return; }
            if (pawn.IsHashIntervalTick(30, delta)) Drive();
        }

        protected override void Drive()
        {
            if (pawn.Downed || !pawn.Spawned) return;
            Map map = pawn.Map;
            if (pawn.Position.x < 4 || pawn.Position.z < 4 || pawn.Position.x >= map.Size.x - 4 || pawn.Position.z >= map.Size.z - 4)
            {
                LinarianWounds.Fell(pawn);
                RecoverFromState();
                return;
            }
            if (pawn.pather?.Moving == true) return;
            if (!RCellFinder.TryFindBestExitSpot(pawn, out IntVec3 spot, TraverseMode.ByPawn, false)) return;
            Job j = JobMaker.MakeJob(JobDefOf.Goto, spot);
            j.exitMapOnArrival = false;
            j.locomotionUrgency = LocomotionUrgency.Sprint;
            Force(j);
        }
    }

    /// <summary>The flayed look: red over them, and screams.</summary>
    public class HediffCompProperties_Flayed : HediffCompProperties
    {
        public HediffCompProperties_Flayed() { compClass = typeof(HediffComp_Flayed); }
    }

    public class HediffComp_Flayed : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = Pawn;
            if (!p.Spawned || !p.IsHashIntervalTick(90)) return;
            Avir.Ritualistics.RitualFx.Mote(p.DrawPos, p.Map, 1.3f, new Color(0.85f, 0.08f, 0.08f));
            if (Rand.Chance(0.3f)) MoteMaker.ThrowText(p.DrawPos, p.Map, Rand.Element("AAAAAH!", "My skin-!", "Don't look at me!"), 2.5f);
        }
    }
}
