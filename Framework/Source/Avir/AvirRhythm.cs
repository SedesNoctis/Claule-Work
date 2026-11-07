using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// CIRCADIAN RHYTHM.
    ///
    /// Every Avir carries exactly one of two hidden marker hediffs - Avir_RhythmNocturnal or
    /// Avir_RhythmDiurnal - and that marker is the ONLY place the current mode is stored. It has
    /// exactly two consequences:
    ///
    ///   VISION   KeenSensesStance picks one of four stages from (sunlight, rhythm). A nocturnal
    ///            Avir sees further in the dark; a diurnal one no longer goes half-blind under an
    ///            open sky. Nobody loses anything they had before - see Hediffs/KeenSensesStance.xml.
    ///   MOOD     Avir_RhythmMood, three stages, driven off the cached state on this hediff.
    ///
    /// Hearing is untouched. No new genes. No new gene-panel entries. No new UI - the schedule
    /// tab IS the control surface.
    ///
    /// WHY THE COUNTER LIVES ON THE HEDIFF AND NOT ON THE GAME COMPONENT.
    /// The obvious alternative is a Dictionary&lt;Pawn,int&gt; on the GameComponent, scribed with
    /// LookMode.Reference. That needs its own eviction pass for dead, lost and world-discarded
    /// pawns, and reference scribing on a dictionary key is exactly the kind of thing that comes
    /// back as a corrupted save two months later. A field on the hediff is saved by the pawn,
    /// loaded by the pawn, and garbage-collected by the pawn. Nothing to clean up.
    ///
    /// VERIFIED AGAINST THE REAL 1.6 ASSEMBLY (not assumed - see the manual's closing note):
    ///   - Pawn_TimetableTracker exposes the public field `times` (List&lt;TimeAssignmentDef&gt;,
    ///     always 24 entries) plus GetAssignment / SetAssignment / CurrentAssignment.
    ///   - TimeAssignmentDefOf carries Anything, Work, Joy, Sleep, Meditate.
    ///   - Pawn.timetable is created in PawnComponentsUtility.AddAndRemoveDynamicComponents and
    ///     ONLY inside the "faction is player" branch. Non-player pawns have timetable == null,
    ///     always. So restricting the sweep to the player faction is not an optimisation, it is
    ///     the only thing that can possibly work.
    ///   - The vanilla default timetable is Sleep for hours 0-5 and 22-23, Anything for 6-21.
    ///     That reads as DIURNAL, which is why NocturnalDefault below exists.
    /// </summary>
    public class Hediff_AvirRhythm : HediffWithComps
    {
        /// <summary>Consecutive hourly samples that disagreed with the stored mode.</summary>
        public int mismatchStreak;

        /// <summary>Last sampled agreement, cached for the ThoughtWorker. See RhythmState.</summary>
        public byte state = RhythmState.Agree;

        /// <summary>
        /// Set once, the first time the sweep ever looks at this pawn as a player colonist.
        /// Guards the one-off nocturnal schedule stamp so it can never fire twice.
        /// </summary>
        public bool scheduleStamped;

        // Visibility is handled in XML by becomeVisible false on the single stage - the same
        // route Avir_TailingCooldown uses, and the one confirmed to work. Deliberately NOT a
        // C# override of Visible: Patch_AvirHiddenHediffs already patches that getter for its
        // own list, and a second, invisible mechanism on the same property is how a future
        // session loses an afternoon. It also keeps dev mode able to reveal this like anything
        // else hidden.

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref mismatchStreak, "avirRhythmStreak", 0);
            Scribe_Values.Look(ref state, "avirRhythmState", RhythmState.Agree);
            Scribe_Values.Look(ref scheduleStamped, "avirRhythmStamped", false);
        }
    }

    public static class RhythmState
    {
        public const byte Agree = 0;
        public const byte Mismatch = 1;
        public const byte Ambiguous = 2;
    }

    [DefOf]
    public static class AvirRhythmDefOf
    {
        public static HediffDef Avir_RhythmNocturnal;
        public static HediffDef Avir_RhythmDiurnal;
        public static ThoughtDef Avir_RhythmMood;

        static AvirRhythmDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirRhythmDefOf));
        }
    }

    public static class AvirRhythmUtility
    {
        /// <summary>
        /// Hours counted as night. Deliberately the same eight hours the vanilla default
        /// timetable marks as Sleep, so "untouched schedule" and "fully diurnal" agree exactly.
        /// </summary>
        private static readonly int[] NightHours = { 22, 23, 0, 1, 2, 3, 4, 5 };

        /// <summary>
        /// Hours counted as day. Eight hours, symmetric with the night band, and safely inside
        /// full daylight at every latitude. 6-9 and 18-21 are twilight and count for neither
        /// side - a pawn sleeping through dawn is not making a statement about anything.
        /// </summary>
        private static readonly int[] DayHours = { 10, 11, 12, 13, 14, 15, 16, 17 };

        /// <summary>Samples of sustained agreement needed to flip. 72 x 2500 ticks = 3 days.</summary>
        public const int SamplesToFlip = 72;

        /// <summary>How lopsided the sleep block has to be before the schedule counts as a vote.</summary>
        private const int StrongSide = 5;
        private const int WeakSide = 1;

        public static bool IsAvir(Pawn pawn)
        {
            return pawn != null
                && pawn.genes != null
                && pawn.genes.HasActiveGene(AvirDefOf.Fearborn);
        }

        public static Hediff_AvirRhythm Get(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return null;

            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(AvirRhythmDefOf.Avir_RhythmNocturnal)
                       ?? pawn.health.hediffSet.GetFirstHediffOfDef(AvirRhythmDefOf.Avir_RhythmDiurnal);

            return h as Hediff_AvirRhythm;
        }

        /// <summary>
        /// True when the pawn is currently in nocturnal mode. False for a diurnal Avir AND for
        /// anything that is not an Avir at all, so callers that only care about the bonus side
        /// can use it directly without a second gene check.
        /// </summary>
        public static bool IsNocturnal(Pawn pawn)
        {
            Hediff_AvirRhythm h = Get(pawn);
            return h != null && h.def == AvirRhythmDefOf.Avir_RhythmNocturnal;
        }

        /// <summary>
        /// A'Schialis are the single exception to "Avir are born nocturnal" - the Legion lives in
        /// the light and keeps daylight hours. Checked by faction AND by pawnkind, because a
        /// legionnaire captured or recruited into someone else's colony is still a legionnaire.
        ///
        /// Both defs live in the VMemes-gated Compat folder, so AvirDefOf holds them behind
        /// MayRequire and they are simply null when VME is not loaded. Null checks below are not
        /// paranoia; without VME this whole branch legitimately does not exist.
        /// </summary>
        public static bool IsBornDiurnal(Pawn pawn)
        {
            if (pawn == null) return false;

            // A chick keeps its mother's hours, or its father's when there is no mother to copy -
            // ahead of the faction rule, so a nest of mixed A'Shaign and A'Schialis raises each
            // hatchling the way its own parent lives. Adults are left to the faction rule: a
            // grown legionnaire generated with an A'Shaign mother is still a legionnaire.
            if (!pawn.DevelopmentalStage.Adult())
            {
                Hediff_AvirRhythm parent = Get(pawn.GetMother()) ?? Get(pawn.GetFather());
                if (parent != null) return parent.def == AvirRhythmDefOf.Avir_RhythmDiurnal;
            }

            if (AvirDefOf.Avir_AShialis != null
                && pawn.Faction != null
                && pawn.Faction.def == AvirDefOf.Avir_AShialis) return true;

            // Kind check by name, not by DefOf field: the Legion pawnkind list grows, and a
            // missed string costs nothing while a missing DefOf field costs the assembly.
            if (pawn.kindDef != null && pawn.kindDef.defName.StartsWith("Avir_Legion")) return true;

            if (pawn.kindDef != null
                && (pawn.kindDef == AvirDefOf.Avir_Centurion
                    || pawn.kindDef == AvirDefOf.Avir_Legionnaire
                    || pawn.kindDef == AvirDefOf.Avir_Inquisitor)) return true;

            return false;
        }

        /// <summary>
        /// Vanilla Night Owl is a hard lock: the pawn is nocturnal, the schedule is ignored, and
        /// the counter never runs. Matched by defName string rather than a DefOf field so a
        /// renamed or removed vanilla trait costs one silent miss instead of a dead assembly.
        /// </summary>
        public static bool NightOwlLocked(Pawn pawn)
        {
            // The A'Shacsaign are night-born and nothing changes that - not a parent, a faction or a
            // schedule. Same lock as Night Owl, so every path that honours one honours the other.
            if (pawn != null && Patch_AvirAShacsaignSpawn.IsSubrace(pawn)) return true;
            // Linarians are night-born too, wherever they live (the author, Fix215)
            if (pawn?.genes?.Xenotype != null && pawn.genes.Xenotype.defName == "Avir_Linarian") return true;
            if (pawn?.story?.traits == null) return false;

            List<Trait> traits = pawn.story.traits.allTraits;
            if (traits == null) return false;

            for (int i = 0; i < traits.Count; i++)
            {
                Trait t = traits[i];
                if (t != null && !t.Suppressed && t.def != null && t.def.defName == "NightOwl") return true;
            }

            return false;
        }

        /// <summary>
        /// Give the pawn a rhythm marker if it has none. Carries the counter fields across when
        /// replacing an existing marker of the other polarity.
        /// </summary>
        public static Hediff_AvirRhythm Assign(Pawn pawn, bool nocturnal)
        {
            if (pawn?.health == null) return null;

            HediffDef want = nocturnal
                ? AvirRhythmDefOf.Avir_RhythmNocturnal
                : AvirRhythmDefOf.Avir_RhythmDiurnal;

            Hediff_AvirRhythm existing = Get(pawn);
            if (existing != null && existing.def == want) return existing;

            bool stamped = existing != null && existing.scheduleStamped;
            if (existing != null) pawn.health.RemoveHediff(existing);

            // Add, then re-fetch rather than using the return value. AddHediff's signature has
            // shifted between versions and this is one lookup on an event that happens a
            // handful of times in a pawn's life - not worth a hard bet on a return type.
            pawn.health.AddHediff(want);

            Hediff_AvirRhythm added = Get(pawn);
            if (added != null)
            {
                added.scheduleStamped = stamped;
                added.mismatchStreak = 0;
                added.state = RhythmState.Agree;
            }

            return added;
        }

        /// <summary>
        /// Read the schedule and decide what it votes for.
        /// Returns RhythmState-style polarity: true = the schedule says nocturnal,
        /// false = it says diurnal, null = it says nothing usable.
        ///
        /// A schedule with no sleep at all lands in the null case on purpose. That is the
        /// "muddled" state - the pawn keeps no hours worth speaking of, gets the small mood
        /// penalty for it, and the adaptation counter freezes rather than drifting at random.
        /// </summary>
        public static bool? ReadSchedule(Pawn pawn)
        {
            Pawn_TimetableTracker tt = pawn?.timetable;
            if (tt == null) return null;

            List<TimeAssignmentDef> times = tt.times;
            if (times == null || times.Count < 24) return null;

            int nightSleep = 0;
            for (int i = 0; i < NightHours.Length; i++)
                if (times[NightHours[i]] == TimeAssignmentDefOf.Sleep) nightSleep++;

            int daySleep = 0;
            for (int i = 0; i < DayHours.Length; i++)
                if (times[DayHours[i]] == TimeAssignmentDefOf.Sleep) daySleep++;

            // Sleeps through the day -> awake at night -> nocturnal.
            if (daySleep >= StrongSide && nightSleep <= WeakSide) return true;

            // Sleeps through the night -> awake by day -> diurnal.
            if (nightSleep >= StrongSide && daySleep <= WeakSide) return false;

            return null;
        }

        /// <summary>
        /// True only if the schedule is bit-for-bit the vanilla default: Sleep 22:00-05:59,
        /// Anything the rest. Used as the "the player has never touched this" test before the
        /// one-off nocturnal stamp, so an intentional diurnal schedule set in the first in-game
        /// hour is never quietly overwritten.
        /// </summary>
        public static bool IsUntouchedDefault(Pawn pawn)
        {
            Pawn_TimetableTracker tt = pawn?.timetable;
            if (tt?.times == null || tt.times.Count < 24) return false;

            for (int h = 0; h < 24; h++)
            {
                TimeAssignmentDef want = (h <= 5 || h >= 22)
                    ? TimeAssignmentDefOf.Sleep
                    : TimeAssignmentDefOf.Anything;

                if (tt.times[h] != want) return false;
            }

            return true;
        }

        /// <summary>Sleep 09:00-16:59, Anything otherwise. The mirror of the vanilla default.</summary>
        public static void StampNocturnalDefault(Pawn pawn)
        {
            Pawn_TimetableTracker tt = pawn?.timetable;
            if (tt?.times == null || tt.times.Count < 24) return;

            for (int h = 0; h < 24; h++)
            {
                tt.times[h] = (h >= 9 && h <= 16)
                    ? TimeAssignmentDefOf.Sleep
                    : TimeAssignmentDefOf.Anything;
            }
        }
    }

    /// <summary>
    /// The whole moving part of the system: one sweep an in-game hour, player Avir only.
    ///
    /// PHASE. Three Avir sweeps already run on a 2500-tick cycle - Patch_AvirHatchling at +0,
    /// AvirExtractEssenceFarm at +625, GameComponent_AvirEssenceDrain at +1250. This one takes
    /// +1875, the last free quarter.
    ///
    /// That spacing is LOAD-BEARING, not cosmetic. AvirPawnScan.AllLivePawns() hands out a single
    /// shared static buffer that it clears on entry; two sweeps landing on the same tick would
    /// have one of them iterating a list the other just emptied. Anything added as a fifth
    /// consumer must either take a phase none of these four use, or stop using the shared buffer.
    /// </summary>
    public class GameComponent_AvirRhythm : GameComponent
    {
        private const int Interval = 2500; // one in-game hour
        private const int Phase = 1875;

        public GameComponent_AvirRhythm(Game game) { }

        public override void GameComponentTick()
        {
            if ((Find.TickManager.TicksGame + Phase) % Interval != 0) return;

            List<Pawn> pawns = AvirPawnScan.AllLivePawns();
            for (int i = 0; i < pawns.Count; i++)
            {
                Sample(pawns[i]);
            }
        }

        private static void Sample(Pawn pawn)
        {
            // Non-player pawns have no timetable at all (verified - see the class comment on
            // Hediff_AvirRhythm). Their marker is assigned once at spawn and then left alone;
            // there is nothing here to read and nothing to decide.
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return;
            if (!AvirRhythmUtility.IsAvir(pawn)) return;

            // Free ride on a sweep that is already walking exactly the right set of pawns:
            // scars that appear mid-game close within the hour. On a healthy Avir this is one
            // pass over the hediff list with nothing to do. See AvirScars for why this is
            // belt-and-braces rather than the main mechanism.
            AvirScars.Clear(pawn);

            if (pawn.timetable == null) return;

            Hediff_AvirRhythm h = AvirRhythmUtility.Get(pawn);
            if (h == null)
            {
                // No marker: a colonist who gained Fearborn after generation, or a save from
                // before this system existed. Born nocturnal unless it is a legionnaire.
                h = AvirRhythmUtility.Assign(pawn, !AvirRhythmUtility.IsBornDiurnal(pawn));
                if (h == null) return;
            }

            // One-off: hand a newly arrived nocturnal Avir a schedule that matches what it is,
            // but only while the schedule is still untouched vanilla default. A'Schialis skip
            // this - the vanilla default already reads diurnal, which is what they want.
            if (!h.scheduleStamped)
            {
                h.scheduleStamped = true;

                if (h.def == AvirRhythmDefOf.Avir_RhythmNocturnal
                    && AvirRhythmUtility.IsUntouchedDefault(pawn))
                {
                    AvirRhythmUtility.StampNocturnalDefault(pawn);
                }
            }

            // Night Owl overrides everything: permanently nocturnal, schedule ignored, counter
            // parked. The vanilla daytime penalty for the trait is suppressed on Avir in
            // Patch_AvirRhythm so the player is not billed twice for one thing.
            if (AvirRhythmUtility.NightOwlLocked(pawn))
            {
                if (h.def != AvirRhythmDefOf.Avir_RhythmNocturnal)
                {
                    h = AvirRhythmUtility.Assign(pawn, true);
                    if (h == null) return;
                }

                h.mismatchStreak = 0;
                h.state = RhythmState.Agree;
                return;
            }

            bool? vote = AvirRhythmUtility.ReadSchedule(pawn);

            if (!vote.HasValue)
            {
                // Ambiguous. Freeze the counter - without this third state a muddled schedule
                // slowly walks the mode over to whichever side happened to sample first.
                h.state = RhythmState.Ambiguous;
                return;
            }

            bool wantsNocturnal = vote.Value;
            bool isNocturnal = h.def == AvirRhythmDefOf.Avir_RhythmNocturnal;

            if (wantsNocturnal == isNocturnal)
            {
                h.state = RhythmState.Agree;
                h.mismatchStreak = 0;
                return;
            }

            h.state = RhythmState.Mismatch;
            h.mismatchStreak++;

            if (h.mismatchStreak >= AvirRhythmUtility.SamplesToFlip)
            {
                // The gap between "schedule changed" and "mode changed" IS the adaptation
                // period. No separate mechanism, and a player who flip-flops the schedule
                // simply never gets out of the red, which is fair.
                AvirRhythmUtility.Assign(pawn, wantsNocturnal);
            }
        }
    }

    /// <summary>
    /// Reads the cached byte written by the sweep. Deliberately does NOT re-walk the 24-hour
    /// schedule: situational thoughts are recalculated far more often than once an hour, and
    /// this mod has spent enough time removing exactly that kind of repeated work.
    /// </summary>
    public class ThoughtWorker_AvirRhythm : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.timetable == null) return ThoughtState.Inactive;
            if (!AvirRhythmUtility.IsAvir(p)) return ThoughtState.Inactive;

            Hediff_AvirRhythm h = AvirRhythmUtility.Get(p);
            if (h == null) return ThoughtState.Inactive;

            switch (h.state)
            {
                case RhythmState.Agree: return ThoughtState.ActiveAtStage(0);
                case RhythmState.Mismatch: return ThoughtState.ActiveAtStage(1);
                case RhythmState.Ambiguous: return ThoughtState.ActiveAtStage(2);
                default: return ThoughtState.Inactive;
            }
        }
    }
}

namespace Avir
{
    /// <summary>The Avir's side of Light (the author, Fix215): night-born eyes - the A'Shaign, the A'Shacsaign, the Linarians,
    /// the Primals, and any Avir living by night - are blinded harder by it; the day-living A'Schialis bear it as anyone does
    /// (their A'Shacsaign stay night-born). Hooked into the magic from here, so the magic itself knows nothing of the Avir.</summary>
    [StaticConstructorOnStartup]
    public static class AvirLightEyes
    {
        public const float NightBorn = 1.5f;
        static AvirLightEyes()
        {
            Avir.Magic.LightMagic.Sensitivity.Add(p => AvirRhythmUtility.IsAvir(p) && (AvirRhythmUtility.IsNocturnal(p) || AvirRhythmUtility.NightOwlLocked(p)) ? NightBorn : 1f);
        }
    }
}
