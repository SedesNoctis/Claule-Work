using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir
{
    [DefOf]
    public static class AvirGroomingDefOf
    {
        public static JobDef Avir_GroomSelfJob;
        public static JobDef Avir_GroomOtherJob;
        public static JobDef Avir_BeingGroomedJob;
        public static ThoughtDef Avir_Groomed;
        public static ThoughtDef Avir_ChickPreened;
        public static ThoughtDef Avir_SelfPreened;

        static AvirGroomingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirGroomingDefOf));
        }
    }

    /// <summary>
    /// PREENING. Who may groom whom, and whether there is anything in the coat worth the trouble.
    ///
    /// BUILT ON AvirTailing ON PURPOSE, DOWN TO THE PREDICATES. Tailing already solved every hard
    /// part of "one Avir walks up to another and they do something together" - the leisure test,
    /// the 3-13 youngling band, the sibling and parent lookup, the think-tree injection, the joy
    /// payment, and the one-shot error trace. All of that is proven in Maximus's own save. Writing
    /// a second, parallel set of those rules is how this mod previously ended up with two
    /// disagreeing definitions of the same thing, so this file CALLS the tailing versions rather
    /// than restating them.
    ///
    /// WHAT IS NEW HERE, AND IT IS ONLY THIS: a chick cannot preen itself, an adult can, and a
    /// mate or parent does it in half the time.
    /// </summary>
    public static class AvirGrooming
    {
        /// <summary>How close the groomer must stay for the work to continue.</summary>
        public const float GroomRangeSquared = 4f;

        /// <summary>
        /// Assisted grooming is this much more EFFICIENT than doing it yourself - the pair session
        /// runs a full hour but clears two coats, so it is half the time per coat. Kept as a named
        /// constant because the two joyDuration figures in XML have to stay in this relationship or
        /// going to a mate stops being the better choice.
        /// </summary>
        public const float AssistedSpeedFactor = 2f;

        /// <summary>Youngling band, borrowed wholesale from tailing so the two systems cannot
        /// disagree about who is a chick.</summary>
        public static bool IsYoungling(Pawn p)
        {
            return AvirTailing.IsYoungling(p);
        }

        private static readonly HashSet<string> ParentRelationNames =
            new HashSet<string> { "Parent" };

        /// <summary>
        /// Mate relations, by defName rather than by DefOf.
        ///
        /// Same reasoning as ParentRelationNames directly above: these are read off
        /// DirectRelations, which is the one place relations are actually STORED. Spouse, Fiance
        /// and Lover are all recorded directly on both pawns by vanilla, so a name match here is
        /// as reliable as the parent test already is.
        /// </summary>
        private static readonly HashSet<string> MateRelationNames =
            new HashSet<string> { "Spouse", "Fiance", "Lover" };

        /// <summary>
        /// The coat, or null for anyone who has not got one. Every question in this file goes
        /// through here, so a pawn without the gene is rejected in one place.
        /// </summary>
        public static HediffComp_FurShed CoatOf(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return null;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null) return null;

            for (int i = 0; i < hediffs.Count; i++)
            {
                HediffWithComps h = hediffs[i] as HediffWithComps;
                if (h == null) continue;

                HediffComp_FurShed comp = h.TryGetComp<HediffComp_FurShed>();
                if (comp != null) return comp;
            }

            return null;
        }

        public static bool HasLooseFur(Pawn pawn)
        {
            HediffComp_FurShed coat = CoatOf(pawn);
            return coat != null && coat.WorthGrooming;
        }

        /// <summary>Fit to be doing any of this at all. Same shape as AvirTailing.CanTail, minus
        /// the tailing cooldown - the pool IS the cooldown here, and it refills on its own.</summary>
        public static bool CanGroom(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null) return false;
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return false;
            if (pawn.IsPrisoner) return false;
            return true;
        }

        /// <summary>
        /// A chick cannot reach its own back. This is the entire reason the assisted branch exists
        /// rather than being a nicety: without an adult, a youngling's coat only ever empties three
        /// feathers a day onto the floor.
        /// </summary>
        public static bool CanGroomSelf(Pawn pawn)
        {
            if (!CanGroom(pawn)) return false;
            if (!HasLooseFur(pawn)) return false;

            // A CHICK ALONE DOES IT BADLY RATHER THAN NOT AT ALL.
            //
            // This used to be a flat refusal, on the reasoning that a youngling cannot reach its
            // own back - which is true, and which quietly meant an orphan, or a chick in a colony
            // with no adult Avir, sat at the unkempt penalty forever with nothing it could do.
            //
            // So the refusal now only stands while somebody COULD help. With no adult available it
            // makes its own attempt: half the coat at best, and a memory that says how that went.
            // See ChickSelfGroomFraction and JobDriver_AvirGroomSelf.
            if (IsYoungling(pawn) && AnyAdultCouldGroom(pawn)) return false;

            return true;
        }

        /// <summary>How much of its own coat a chick working alone actually manages.</summary>
        public const float ChickSelfGroomFraction = 0.5f;

        /// <summary>
        /// How far through a session she has to get before it counts as having been preened.
        ///
        /// Feathers scale smoothly with time; the mood does not, because relief is not divisible.
        /// Half is chosen so that a session broken off early is worth something in the coat and
        /// nothing in the head, which is what "interrupted" should feel like.
        /// </summary>
        public const float SessionMemoryThreshold = 0.5f;

        /// <summary>
        /// How much of a preening session actually happened, 0 to 1.
        ///
        /// ticksLeftThisToil is a JobDriver field and survives a save/load in the middle of a
        /// session, which a counter closed over in MakeNewToils would not - the driver rebuilds
        /// its toils on load and any captured local starts again at zero. Callers pass their own
        /// duration because the two drivers compute it differently: the solo one scales by trait.
        /// </summary>
        public static float SessionProgress(int ticksLeft, int duration)
        {
            if (duration <= 0) return 1f;
            return Mathf.Clamp01(1f - (ticksLeft / (float)duration));
        }

        /// <summary>
        /// Whether any adult Avir on the map could plausibly do it for this chick.
        ///
        /// Deliberately loose - alive, present, an Avir, not a chick, not hostile. It is not asking
        /// whether anyone WILL, only whether the chick is genuinely on its own. Being unlucky is
        /// not the same as being alone, and a chick surrounded by busy adults should keep waiting
        /// for one of them rather than tearing at itself.
        /// </summary>
        public static bool AnyAdultCouldGroom(Pawn chick)
        {
            IReadOnlyList<Pawn> pawns = chick?.Map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null) return false;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other == chick || other.Dead || other.Downed) continue;
                if (IsYoungling(other)) continue;
                if (!AvirFaction.HasFearborn(other)) continue;
                if (other.HostileTo(chick)) continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Somebody this pawn may preen, or null. Chicks first and deliberately so: they cannot do
        /// it themselves, and an adult who preens a mate while a chick moults onto the floor has
        /// the priorities backwards.
        /// </summary>
        public static Pawn FindGroomingTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return null;

            // Adults look after chicks. A youngling never grooms anyone, including another chick -
            // neither of them can reach.
            if (!IsYoungling(pawn))
            {
                Pawn chick = FindOwnChick(pawn);
                if (chick != null) return chick;
            }

            // A MATE OUTRANKS DOING IT ALONE, EVEN IF ONLY YOUR OWN COAT IS FULL.
            //
            // Maximus asked for this directly: an Avir with a partner should go to the partner
            // rather than sit and pick at itself. Passing eitherCoat means the pair job is offered
            // whenever either of the two has anything loose - and since the session harvests BOTH
            // coats, going together is never the worse choice.
            Pawn lover = LovePartnerRelationUtility.ExistingMostLikedLovePartner(pawn, false);
            if (IsGroomable(pawn, lover, eitherCoat: true)) return lover;

            // TEMPORARY DIAGNOSTIC - see AvirGrooming.ExplainLoverGroomingFailure. Logs exactly
            // which of IsGroomable's conditions is refusing the pair, capped at a few lines.
            // Does not change behaviour: it duplicates IsGroomable's checks read-only, in the
            // same order, purely to name the one that fires. Delete this line and the method
            // below to revert once the cause is found.
            ExplainLoverGroomingFailure(pawn, lover);

            return null;
        }

        private static int loggedLoverChecks;

        private static void ExplainLoverGroomingFailure(Pawn pawn, Pawn lover)
        {
            if (loggedLoverChecks >= 5) return;

            string who = pawn?.LabelShort ?? "?";

            if (lover == null)
            {
                loggedLoverChecks++;
                Log.Message("[Avir grooming] " + who + ": no existing love partner found "
                    + "(ExistingMostLikedLovePartner returned null).");
                return;
            }

            string reason;
            if (lover.Dead) reason = "partner is dead";
            else if (!lover.Spawned) reason = "partner is not spawned (off-map/caravan/world pawn)";
            else if (lover == pawn) reason = "partner resolved to self";
            else if (lover.Map != pawn.Map) reason = "partner is on a different map";
            else if (lover.Downed) reason = "partner is downed";
            else if (lover.InMentalState) reason = "partner is in a mental state";
            else if (lover.IsPrisoner) reason = "partner is a prisoner";
            else if (lover.HostileTo(pawn)) reason = "partner reads as hostile";
            else if (!AvirGroomingTraits.AcceptsGroomingFromOthers(lover)) reason = "partner is Featherbroken";
            else if (lover.CurJob?.def == AvirGroomingDefOf.Avir_BeingGroomedJob
                     || lover.CurJob?.def == AvirGroomingDefOf.Avir_GroomOtherJob)
                reason = "partner is already mid-grooming with someone else";
            else if (!AvirTailing.IsAtLeisure(lover)) reason = "partner is not at leisure (job: "
                + (lover.CurJob?.def?.defName ?? "none") + ")";
            else if (!HasLooseFur(pawn) && !HasLooseFur(lover)) reason = "neither coat has enough "
                + "loose fur yet (actor loose fraction not worth grooming, partner's neither)";
            else reason = "unresolved - every listed condition passed; check CoatOf(pawn) for null "
                + "(no HediffComp_FurShed at all)";

            loggedLoverChecks++;
            Log.Message("[Avir grooming] " + who + " -> " + lover.LabelShort + " refused: " + reason);
        }

        /// <summary>
        /// A youngling of ours with a full coat.
        ///
        /// SEARCHED FROM THE CHICK'S SIDE, NOT THE PARENT'S. The Parent relation is recorded on the
        /// CHILD - that is the direction AvirTailing.FindKin already relies on and the direction
        /// known to work in this game. Looking for a "Child" entry in the parent's own
        /// DirectRelations would mean trusting a reverse relation to be stored rather than derived,
        /// which is exactly the sort of assumption this mod has paid for before.
        ///
        /// So the map's colonists are walked instead. This runs from a joy giver, only for Avir,
        /// only when a pawn is choosing what to do with its free time - not per tick.
        /// </summary>
        // HOTFIX: was private. JobGiver_AvirGroomingUrgent needs this to weigh a chick's coat the
        // same way it now weighs a partner's - see the gate below. Same assembly, same intent as
        // the method already had; visibility is the only thing that changed.
        internal static Pawn FindOwnChick(Pawn pawn)
        {
            List<Pawn> colonists = pawn.Map?.mapPawns?.FreeColonistsSpawned;
            if (colonists == null) return null;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn candidate = colonists[i];
                if (candidate == pawn || !IsYoungling(candidate)) continue;
                if (!IsGroomable(pawn, candidate)) continue;
                if (!IsParentOf(pawn, candidate)) continue;

                return candidate;
            }

            return null;
        }

        // Was private. IsOrderableTarget needs it to answer "is the groomer this pawn's parent",
        // which is the one exception to the same-sex rule below.
        internal static bool IsParentOf(Pawn parent, Pawn child)
        {
            List<DirectPawnRelation> relations = child?.relations?.DirectRelations;
            if (relations == null) return false;

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation rel = relations[i];
                if (rel?.def == null || rel.otherPawn != parent) continue;
                if (ParentRelationNames.Contains(rel.def.defName)) return true;
            }

            return false;
        }

        /// <summary>Mates, read off DirectRelations where the game stores them.</summary>
        private static bool IsMateOf(Pawn a, Pawn b)
        {
            List<DirectPawnRelation> relations = a?.relations?.DirectRelations;
            if (relations == null || b == null) return false;

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation rel = relations[i];
                if (rel?.def == null || rel.otherPawn != b) continue;
                if (MateRelationNames.Contains(rel.def.defName)) return true;
            }

            return false;
        }

        /// <summary>
        /// Everyone this pawn calls a parent.
        ///
        /// Read from the CHILD's own DirectRelations, the same direction FindOwnChick already
        /// trusts. Nothing here asks the parent to remember its children.
        /// </summary>
        private static void CollectParents(Pawn child, List<Pawn> into)
        {
            into.Clear();

            List<DirectPawnRelation> relations = child?.relations?.DirectRelations;
            if (relations == null) return;

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation rel = relations[i];
                if (rel?.def == null || rel.otherPawn == null) continue;
                if (ParentRelationNames.Contains(rel.def.defName)) into.Add(rel.otherPawn);
            }
        }

        private static readonly List<Pawn> parentsA = new List<Pawn>();
        private static readonly List<Pawn> parentsB = new List<Pawn>();

        /// <summary>
        /// Siblings, DERIVED FROM SHARED PARENTS rather than read from a Sibling relation.
        ///
        /// Vanilla treats siblinghood as an implied relation computed from parentage; it is not
        /// sitting in DirectRelations waiting to be found, and looking for it there would return
        /// false for every genuine sibling in the game. Comparing parent lists asks the same
        /// question through the only records that actually exist. One shared parent is enough:
        /// half-siblings are kin.
        /// </summary>
        private static bool AreSiblings(Pawn a, Pawn b)
        {
            if (a == null || b == null || a == b) return false;

            CollectParents(a, parentsA);
            if (parentsA.Count == 0) return false;

            CollectParents(b, parentsB);
            if (parentsB.Count == 0) return false;

            for (int i = 0; i < parentsA.Count; i++)
            {
                if (parentsB.Contains(parentsA[i])) return true;
            }

            return false;
        }

        /// <summary>
        /// KIN, AND NOTHING WIDER.
        ///
        /// Parents, children, siblings and mates. Not friends, not colonists in general, not
        /// guests, not the visiting Avir with the loose coat who happens to be standing there.
        /// Preening is a family act in this mod, and every mood memory it writes assumes that.
        /// </summary>
        public static bool AreKin(Pawn a, Pawn b)
        {
            if (a == null || b == null || a == b) return false;

            if (IsParentOf(a, b) || IsParentOf(b, a)) return true;
            if (IsMateOf(a, b) || IsMateOf(b, a)) return true;

            return AreSiblings(a, b);
        }

        /// <summary>
        /// The ordered version of IsGroomable, minus the leisure test.
        ///
        /// An Avir will not walk off a job to preen a mate of their own accord, and that restraint
        /// is right for a pastime. It is wrong for an instruction: if the player asks, the answer
        /// is yes. Featherbroken still refuses - that is not reluctance, and an order does not
        /// change it.
        /// </summary>
        public static bool IsOrderableTarget(Pawn groomer, Pawn target)
        {
            if (groomer == null || target == null || target == groomer) return false;
            if (target.Dead || !target.Spawned || target.Map != groomer.Map) return false;
            if (target.Downed || target.InMentalState) return false;
            if (target.HostileTo(groomer)) return false;
            if (!AvirGroomingTraits.AcceptsGroomingFromOthers(target)) return false;

            // A CHICK NEVER PREENS ANYONE, ORDERED OR NOT.
            //
            // FindGroomingTarget has said so since it was written - "a youngling never grooms
            // anyone, including another chick" - and the automatic path has honoured it all along.
            // The ordered path never asked, which is how chicks ended up preening their parents.
            if (IsYoungling(groomer)) return false;

            // ONE FACTION. NO EXCEPTIONS FOR THE PLAYER.
            //
            // Covers traders, visitors, guests, prisoners and anyone from a faction that merely
            // is not hostile. HostileTo above was never the right question: an Avir from another
            // faction standing peacefully in the colony is still not somebody you put your hands
            // in the feathers of. Null faction - wild, escaped, released - fails this too, and
            // should.
            if (groomer.Faction == null || groomer.Faction != target.Faction) return false;

            // KIN ONLY. Parents, children, siblings, mates. See AreKin.
            if (!AreKin(groomer, target)) return false;

            // MALES DO NOT PREEN MALES, EXCEPT A FATHER HIS OWN.
            //
            // A father seeing to his chick's coat is care. Two grown males going over each
            // other's plumage is not something the Avir do, and no ordering of it should be
            // available. Deliberately one-directional: the exception is the PARENT doing the
            // preening, so a grown son cannot be ordered to preen his father either.
            if (groomer.gender == Gender.Male && target.gender == Gender.Male
                && !IsParentOf(groomer, target))
            {
                return false;
            }

            return HasLooseFur(target);
        }

        /// <summary>
        /// Worth walking over to, and in a state to be handled.
        ///
        /// `eitherCoat` is what makes a partner session possible when only the GROOMER's coat is
        /// full. Without it a pawn with a full coat and a clean-coated mate had no partner option
        /// at all and quietly preened alone - which is exactly what Maximus was seeing.
        /// </summary>
        public static bool IsGroomable(Pawn groomer, Pawn target, bool eitherCoat = false)
        {
            if (target == null || target.Dead || !target.Spawned) return false;
            if (target == groomer) return false;
            if (target.Map != groomer.Map) return false;
            if (target.Downed || target.InMentalState || target.IsPrisoner) return false;
            if (target.HostileTo(groomer)) return false;

            // Featherbroken: nobody else's hands in the plumage. She preens alone or not at all.
            if (!AvirGroomingTraits.AcceptsGroomingFromOthers(target)) return false;

            // ALREADY IN SOMEONE ELSE'S CLAWS. Being preened is itself a leisure job, so without
            // this a second Avir would walk up and start over the top of the first, cutting the
            // recipient's job out from under them mid-session.
            JobDef targetJob = target.CurJob?.def;
            if (targetJob != null
                && (targetJob == AvirGroomingDefOf.Avir_BeingGroomedJob
                    || targetJob == AvirGroomingDefOf.Avir_GroomOtherJob))
            {
                return false;
            }

            // Somebody hauling steel across the base does not stand still to be preened. Reuses
            // tailing's leisure test rather than inventing a second one.
            if (!AvirTailing.IsAtLeisure(target)) return false;

            // A chick is worth going to only if there is something in its coat. A mate is worth
            // going to if EITHER of you has something in yours: you preen each other, and one full
            // coat is reason enough for both to sit down together.
            if (eitherCoat && HasLooseFur(groomer)) return true;

            return HasLooseFur(target);
        }

        /// <summary>
        /// WHERE THE TWO OF THEM MEET.
        ///
        /// THE CHASE THIS EXISTS TO END. The first version had the groomer walk to wherever the
        /// partner happened to be, and only handed the partner their half of the job ON ARRIVAL.
        /// Until then the partner knew nothing about it and carried on hauling, cooking, wandering
        /// - so the groomer followed them around the map with no work marker on either pawn,
        /// arriving at a place they had already left. Maximus watched exactly that happen.
        ///
        /// Now both are told at once and both walk to the SAME cell. Nobody is chased.
        ///
        /// IN THE HOME AREA BY PREFERENCE, because a pair of Avir standing still for an hour with
        /// their attention entirely on each other should be doing it somewhere the colony has
        /// decided is safe - not in a corridor, a killbox or halfway up a mountainside where the
        /// route happened to cross.
        ///
        /// Three attempts, widening: between them, near the groomer, near the partner. Anything
        /// found must be standable, in the home area, unforbidden, and REACHABLE BY BOTH - a cell
        /// only one of them can get to is a rendezvous that never happens.
        /// </summary>
        public static bool TryFindMeetingCell(Pawn a, Pawn b, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;

            Map map = a?.Map;
            if (map == null || b == null || b.Map != map) return false;

            IntVec3 midpoint = new IntVec3((a.Position.x + b.Position.x) / 2, 0,
                                           (a.Position.z + b.Position.z) / 2);

            if (TryNear(map, a, b, midpoint, 12, out cell)) return true;
            if (TryNear(map, a, b, a.Position, 20, out cell)) return true;
            if (TryNear(map, a, b, b.Position, 20, out cell)) return true;

            // LAST RESORT: where the groomer already stands. Not ideal and not in any home area,
            // but a caravan camp or an away map has no home area at all, and refusing to groom
            // anywhere outside the colony would be a stranger rule than standing in the open.
            if (a.Position.Standable(map)) { cell = a.Position; return true; }

            return false;
        }

        private static bool TryNear(Map map, Pawn a, Pawn b, IntVec3 centre, int radius,
            out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (!centre.IsValid) return false;

            Area home = map.areaManager?.Home;

            IntVec3 found;
            bool ok = CellFinder.TryFindRandomCellNear(centre, map, radius, delegate (IntVec3 c)
            {
                if (!c.InBounds(map) || !c.Standable(map)) return false;
                if (home != null && !home[c]) return false;
                if (c.IsForbidden(a) || c.IsForbidden(b)) return false;
                if (!a.CanReach(c, PathEndMode.OnCell, Danger.None)) return false;
                if (!b.CanReach(c, PathEndMode.OnCell, Danger.None)) return false;
                return true;
            }, out found);

            if (!ok) return false;

            cell = found;
            return true;
        }

        /// <summary>
        /// Whether a session already under way may keep going. Deliberately much looser than
        /// IsGroomable: no leisure test, because the partner is now busy being groomed by us, and
        /// no "already in someone's claws" test for the same reason.
        ///
        /// Also no coat test. A pair session harvests at the END, so demanding loose fur every
        /// tick would be asking a question whose answer has not been decided yet - and the session
        /// is worth finishing for the mood and the recreation even if the harvest comes to little.
        /// </summary>
        public static bool StillGroomable(Pawn target)
        {
            if (target == null || target.Dead || !target.Spawned) return false;
            if (target.Downed || target.InMentalState) return false;
            return true;
        }

        /// <summary>
        /// The mood memory for being handled by someone who cares enough to do it. Chicks get their
        /// own wording, on exactly the same rule tailing uses: if either party is a youngling, both
        /// read the youngling version.
        /// </summary>
        public static void GiveGroomedMemory(Pawn p, Pawn other)
        {
            if (p?.needs?.mood?.thoughts?.memories == null) return;

            // Handled briskly by someone who was not being careful. Not the same event.
            if (AvirGroomingTraits.IsRoughGroomer(other) && p?.needs?.mood?.thoughts?.memories != null)
            {
                ThoughtDef rough = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_RoughlyPreened");
                if (rough != null)
                {
                    p.needs.mood.thoughts.memories.TryGainMemory(rough, other);
                    return;
                }
            }

            bool youngling = IsYoungling(p) || IsYoungling(other);
            ThoughtDef memory = youngling
                ? AvirGroomingDefOf.Avir_ChickPreened
                : AvirGroomingDefOf.Avir_Groomed;

            if (memory == null) memory = AvirGroomingDefOf.Avir_Groomed;
            if (memory == null) return;

            p.needs.mood.thoughts.memories.TryGainMemory(memory, other);
        }

        /// <summary>
        /// PARTNER FIRST, THEN ALONE - AND ALONE IS REACHED WHENEVER THE PARTNER CANNOT BE HAD.
        ///
        /// Both entry points used to inline this, and both had the same hole: if a mate was found
        /// but the pair could not actually be FORMED, they returned the pair job anyway and it died
        /// on the spot. The self branch was never reached, so an Avir with a partner on the map
        /// stopped preening entirely - exactly what Maximus observed, and exactly the diagnosis he
        /// offered.
        ///
        /// Now the pair is only chosen if it can genuinely start: a reachable partner AND a place
        /// for the two of them to meet. Anything short of that falls through to preening alone,
        /// which is worse than company and much better than nothing.
        /// </summary>
        public static Job TryMakeGroomingJob(Pawn pawn)
        {
            if (pawn == null) return null;

            Pawn target = FindGroomingTarget(pawn);

            if (target != null && pawn.CanReach(target, PathEndMode.Touch, Danger.None))
            {
                IntVec3 unused;
                if (TryFindMeetingCell(pawn, target, out unused))
                {
                    return JobMaker.MakeJob(AvirGroomingDefOf.Avir_GroomOtherJob, target);
                }
            }

            if (CanGroomSelf(pawn))
            {
                // Targeted at itself so the progress bar has something to hang over.
                return JobMaker.MakeJob(AvirGroomingDefOf.Avir_GroomSelfJob, pawn);
            }

            return null;
        }

        private static int loggedSessions;

        /// <summary>
        /// SAYS WHAT HAPPENED, FOR THE FIRST FEW SESSIONS OF A SESSION.
        ///
        /// Grooming is now a once-every-three-days event lasting an hour, which is exactly the
        /// frequency at which a player looks away and misses it. Three lines in the log settle
        /// whether it is running, who with, how long it took and what it yielded - without having
        /// to sit and watch for it, and without spamming a message box every time two Avir sit
        /// down together.
        ///
        /// Capped at three rather than one, because the interesting question is not "did it fire
        /// once" but "is it firing as a pair or is everyone doing it alone" - and that needs more
        /// than a single sample.
        /// </summary>
        public static void NoteSession(Pawn actor, Pawn partner, int feathers, int ticks)
        {
            if (loggedSessions >= 3) return;
            loggedSessions++;

            Log.Message("[Avir grooming] " + actor.LabelShort
                + (partner == null ? " preened alone" : " and " + partner.LabelShort + " preened each other")
                + " for " + ticks + " ticks (" + (ticks / 2500f).ToString("0.0")
                + "h), yielding " + feathers + " feathers.");
        }

        /// <summary>
        /// Pays recreation while the job runs, through tailing's resolver - which already handles a
        /// missing JoyKindDef without throwing, and a null JoyKindDef in the tolerance set is one
        /// of the few things here that can raise per tick.
        /// </summary>
        public static void PayJoy(Pawn pawn, JobDef jobDef, float basePerTick)
        {
            Need_Joy joy = pawn?.needs?.joy;
            if (joy == null || joy.CurLevel >= 1f) return;

            JoyKindDef kind = AvirTailing.ResolveJoyKind(jobDef);
            if (kind == null) return;

            float rate = jobDef != null && jobDef.joyGainRate > 0f ? jobDef.joyGainRate : 1f;
            joy.GainJoy(basePerTick * rate, kind);
        }
    }

    /// <summary>
    /// Recreation: sort your own feathers out, or somebody else's.
    ///
    /// A high baseChance in the def, for the same reason tailing has one - this should be a strong
    /// default for an Avir with a full coat, and it costs nothing elsewhere because the giver
    /// returns null immediately for anyone without loose fur.
    /// </summary>
    public class JoyGiver_AvirGrooming : JoyGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            if (!AvirGrooming.CanGroom(pawn)) return null;

            // Somebody who cannot do it for themselves comes first.
            return AvirGrooming.TryMakeGroomingJob(pawn);
        }
    }

    /// <summary>
    /// The second entry point, for idle time rather than scheduled recreation - the same pairing
    /// tailing uses. Registered ahead of tailing's own idle node, so an Avir with a full coat deals
    /// with it before wandering off to shadow a partner.
    /// </summary>
    public class JobGiver_AvirGroomingIdle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!AvirGrooming.CanGroom(pawn)) return null;

            // Never pull a pawn off something that still matters. Same floors as tailing.
            if (pawn.needs != null)
            {
                if (pawn.needs.rest != null && pawn.needs.rest.CurLevel < 0.28f) return null;
                if (pawn.needs.food != null && pawn.needs.food.CurLevel < 0.28f) return null;
            }

            return AvirGrooming.TryMakeGroomingJob(pawn);
        }
    }

    /// <summary>
    /// THE THIRD DOOR, and without it the other two were never opening.
    ///
    /// WHY AUTOMATIC GROOMING NEVER HAPPENED. Read out of Core's Humanlike think tree:
    /// JobGiver_GetJoy sits under ThinkNode_Priority_GetJoy and only runs for a pawn short of
    /// recreation, while the idle node this mod injects sits in the branch tagged Idle, reached
    /// only by a pawn with nothing else to do. A working colonist at 90% recreation walks past
    /// both, every tick, forever. Manual orders worked because an order bypasses the tree entirely
    /// - which is exactly why the button looked fine while the automation looked broken.
    ///
    /// So grooming gets a node ABOVE ordinary work, on the same principle the game already uses
    /// for eating: you do not stop working to eat until you are hungry.
    ///
    /// THE TRIGGER IS THE DEBUFF ITSELF. This fires only once the coat has reached the first
    /// unkempt stage - the exact fraction ThoughtWorker_AvirUnkempt uses for its -3, shared as a
    /// constant so the two cannot drift apart. Below that, grooming stays a pastime and waits for
    /// leisure like everything else. At or above it the itch is costing mood, and an Avir putting
    /// down a hammer to deal with that is the correct behaviour rather than an interruption.
    /// </summary>
    public class JobGiver_AvirGroomingUrgent : ThinkNode_JobGiver
    {
        /// <summary>
        /// The node this one was spliced in ahead of - normally JobGiver_Work.
        ///
        /// SET ONLY WHEN THE PARENT IS A PRIORITY SORTER, which is the case this exists for. See
        /// GetPriority.
        /// </summary>
        public ThinkNode priorityPeer;

        /// <summary>
        /// SOME PARENTS SORT THEIR CHILDREN AND DEMAND A NUMBER FROM EACH.
        ///
        /// THE ERROR THIS FIXES:
        ///
        ///     ThinkNode_PrioritySorter has child node which didn't give a priority:
        ///     Avir.JobGiver_AvirGroomingUrgent
        ///
        /// The urgent node is inserted immediately ahead of JobGiver_Work, and in the humanlike
        /// tree JobGiver_Work lives inside a ThinkNode_PrioritySorter. A sorter does not run its
        /// children in list order - it asks each one how important it is and runs the loudest. The
        /// base implementation here answered with nothing, so the sorter complained on every job
        /// search of every pawn, which is both log spam and wasted work.
        ///
        /// NO INVENTED CONSTANT. The obvious fix is to return some number like 9.5, but that
        /// number would be a guess about vanilla's internal scale and would silently rot the day
        /// vanilla or another mod changes it. Instead this asks the node it was placed in front of
        /// what ITS priority is and returns slightly more. "Just ahead of work" is exactly the
        /// intent, and it stays true whatever work's number happens to be.
        ///
        /// The peer is null when the parent is not a sorter - the toddler tree, for instance,
        /// which has no work node at all. Then this returns 1, which is a fine default for a
        /// parent that was never going to ask.
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            if (priorityPeer == null) return 1f;

            try
            {
                return priorityPeer.GetPriority(pawn) + 0.1f;
            }
            catch
            {
                // The peer refusing to answer must not take the pawn's whole job search with it.
                return 1f;
            }
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!AvirGrooming.CanGroom(pawn)) return null;

            // Never ahead of the basics. A starving or exhausted Avir has worse problems than
            // loose fur, and those needs sit above work in the tree anyway.
            if (pawn.needs != null)
            {
                if (pawn.needs.rest != null && pawn.needs.rest.CurLevel < 0.28f) return null;
                if (pawn.needs.food != null && pawn.needs.food.CurLevel < 0.28f) return null;
            }

            // HOTFIX: A MATE'S FULL COAT IS JUST AS URGENT AS YOUR OWN.
            //
            // TryMakeGroomingJob offers the pair session whenever EITHER coat is full - that is
            // the whole point of eitherCoat in IsGroomable, "one full coat is reason enough for
            // both to sit down together." But this gate was only ever checking the CALLER's own
            // coat before calling it at all. If neither partner's OWN pool happened to cross the
            // urgent threshold at the same busy moment - which is the common case, since the two
            // fill independently - the pair was never summoned automatically, no matter how
            // overdue one of them was. Manual orders skipped this gate entirely (IsOrderableTarget
            // only looks at the target), which is exactly why "works when I order it, never on
            // its own" was the symptom.
            //
            // Checking the lover's fraction too, and gating on whichever is higher, brings this
            // gate in line with what TryMakeGroomingJob was already willing to do underneath it.
            //
            // A CHICK'S COAT COUNTS HERE TOO, AND FOR THE SAME REASON - it was the FIRST thing
            // missed, not a second case. FindGroomingTarget checks the chick BEFORE the lover, so
            // a parent whose own coat was fine and whose chick was overdue had exactly the same
            // silent miss the lover fix above addresses, just one priority rung higher.
            float ownFraction = AvirGrooming.CoatOf(pawn)?.LooseFraction ?? 0f;

            Pawn chick = AvirGrooming.FindOwnChick(pawn);
            float chickFraction = AvirGrooming.CoatOf(chick)?.LooseFraction ?? 0f;

            Pawn lover = LovePartnerRelationUtility.ExistingMostLikedLovePartner(pawn, false);
            float loverFraction = AvirGrooming.CoatOf(lover)?.LooseFraction ?? 0f;

            float highest = Mathf.Max(ownFraction, Mathf.Max(chickFraction, loverFraction));
            if (highest < AvirGroomingTraits.UnkemptStage0) return null;

            return AvirGrooming.TryMakeGroomingJob(pawn);
        }
    }

    /// <summary>
    /// Preening yourself. No target, no walking - the pawn works where it stands, which is why this
    /// can be a strong recreation choice without dragging anyone across the base.
    /// </summary>
    public class JobDriver_AvirGroomSelf : JobDriver
    {
        private const float BaseJoyPerTick = 0.36f / 400f;

        private static bool loggedTickFailure;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !AvirGrooming.CanGroomSelf(pawn));

            Toil preen = new Toil();
            preen.defaultCompleteMode = ToilCompleteMode.Delay;
            int selfDuration = job.def.joyDuration > 0 ? job.def.joyDuration : 5000;

            // Feather-proud takes twice as long over her own coat, and says so at length.
            selfDuration = Mathf.RoundToInt(selfDuration * AvirGroomingTraits.SpeedFactor(pawn));

            preen.defaultDuration = selfDuration;
            preen.handlingFacing = false;

            preen.tickAction = delegate
            {
                try
                {
                    AvirGrooming.PayJoy(pawn, job.def, BaseJoyPerTick);
                }
                catch (Exception e)
                {
                    if (!loggedTickFailure)
                    {
                        loggedTickFailure = true;
                        Log.Error("[Avir] Self-grooming tick threw. Full trace follows; printed "
                            + "once per session.\n" + e);
                    }

                    EndJobWith(JobCondition.Errored);
                }
            };

            // A BAR AFTER ALL. The job now carries the pawn itself as target A, which gives the
            // bar something to hang over - the earlier version had no target and therefore no bar,
            // and a solo session was invisible from outside.
            preen.WithProgressBar(TargetIndex.A,
                () => 1f - (preen.actor.jobs.curDriver.ticksLeftThisToil / (float)selfDuration), true);

            preen.AddFinishAction(delegate
            {
                HediffComp_FurShed coat = AvirGrooming.CoatOf(pawn);
                if (coat == null) return;

                // PAID FOR THE TIME ACTUALLY SPENT, NOT FOR HAVING STARTED.
                //
                // This block runs on EVERY ending of the job, which is what AddFinishAction means -
                // cancelled, drafted, re-prioritised, all of it, not only the delay running out.
                // It used to hand over the whole coat regardless, so ordering a preen and calling
                // it off on the next tick produced two hours of results in one. Scaling by
                // progress closes that without punishing a session a raid interrupted at nine
                // tenths: HarvestLoose(fraction) leaves the remainder in the coat, so a half
                // session reads as a half-done coat and the unkempt mood keeps running on what is
                // left. Which is the honest outcome.
                float progress = AvirGrooming.SessionProgress(ticksLeftThisToil, selfDuration);
                if (progress <= 0f) return;

                // A CHICK GETS WHAT IT CAN REACH, WHICH IS NOT MUCH.
                bool chick = AvirGrooming.IsYoungling(pawn);
                float fraction = (chick ? AvirGrooming.ChickSelfGroomFraction : 1f) * progress;

                int got = coat.HarvestLoose(pawn, fraction);
                if (got <= 0) return;

                // THE MEMORY IS ALL OR NOTHING, AND THE FEATHERS ARE NOT.
                //
                // Feathers are the coat emptying, which happens gradually. The mood is the relief
                // of having dealt with it, and there is no such thing as being three tenths
                // relieved - either she got far enough through it to feel better or she did not.
                if (progress < AvirGrooming.SessionMemoryThreshold)
                {
                    AvirGrooming.NoteSession(pawn, null, got, selfDuration - ticksLeftThisToil);
                    return;
                }

                // A MEMORY FOR THIS TOO, SMALLER THAN THE PAIR'S.
                //
                // Solo preening used to give nothing at all, which is why Maximus saw a stack of
                // feathers appear and no thought anywhere: the mood was only ever written by the
                // pair job. Sorting your own coat out is a relief even when nobody helped - it is
                // simply not the same as being trusted with someone's wings.
                // The chick's memory is a NEGATIVE one, and deliberately so: it managed half a
                // job on itself and knows it. An adult preening alone is a relief; a chick doing it
                // is the absence of someone who should have been there.
                ThoughtDef memory = chick
                    ? DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_ChickSelfPreened")
                    : AvirGroomingDefOf.Avir_SelfPreened;

                if (pawn.needs?.mood?.thoughts?.memories != null && memory != null)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(memory);
                }

                AvirGrooming.NoteSession(pawn, null, got, selfDuration - ticksLeftThisToil);
            });

            yield return preen;
        }
    }

    /// <summary>
    /// Preening somebody else - a mate, or a chick who cannot reach its own back. Twice as quick as
    /// doing it alone, and both of them come away in a better mood.
    /// </summary>
    public class JobDriver_AvirGroomOther : JobDriver
    {
        private const float BaseJoyPerTick = 0.36f / 400f;

        private static bool loggedTickFailure;

        private Pawn Groomed { get { return job.targetA.Thing as Pawn; } }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Reserves nothing, exactly as tailing does not. The other pawn is a person standing
            // still for a moment, not a workpiece, and reserving them would block their own jobs.
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            // CONTINUATION, NOT SELECTION - and conflating the two broke this once already.
            //
            // IsGroomable is the question "should I walk over there and start", and it answers NO
            // for a pawn already being groomed by somebody. Correct for picking a target. Fatal as
            // a running check: the moment this driver hands the partner their half of the job,
            // IsGroomable would see that job and fail the groomer's own task.
            this.FailOn(() => !AvirGrooming.CanGroom(pawn) || !AvirGrooming.StillGroomable(Groomed));

            // ---- 1. AGREE ON A PLACE, AND TELL HER NOW ----
            //
            // This used to happen on arrival, which is what produced the chase: until the groomer
            // reached her she knew nothing about it and kept working, so he followed her around the
            // map with no work marker on either of them. Both are told at the same instant now.
            Toil arrange = new Toil();
            arrange.defaultCompleteMode = ToilCompleteMode.Instant;
            arrange.initAction = delegate
            {
                Pawn other = Groomed;
                if (other?.jobs == null || AvirGroomingDefOf.Avir_BeingGroomedJob == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                IntVec3 spot;
                if (!AvirGrooming.TryFindMeetingCell(pawn, other, out spot))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                job.targetB = spot;

                Job partner = JobMaker.MakeJob(AvirGroomingDefOf.Avir_BeingGroomedJob, pawn, spot);

                // InterruptForced: she was picked for being at leisure, so what is being
                // interrupted is standing about or wandering. This is also what stops the chase -
                // she has somewhere to be now, and it is the same place he does.
                other.jobs.StartJob(partner, JobCondition.InterruptForced);
            };
            yield return arrange;

            // ---- 2. BOTH WALK THERE ----
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            // ---- 3. WAIT FOR HER, BRIEFLY ----
            //
            // Whoever arrives first waits. Bounded, because a partner who gets drafted, downed or
            // pulled into a mental break on the way would otherwise leave the groomer standing in
            // the meeting spot for the rest of the day.
            Toil wait = new Toil();
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.socialMode = RandomSocialMode.Off;
            wait.handlingFacing = true;

            // ITS OWN COUNTER, AND THE LACK OF ONE WAS A REAL BUG.
            //
            // The first version read ticksLeftThisToil for the timeout. That field is only wound up
            // and counted down for ToilCompleteMode.Delay - on a Never toil it sits at zero, so
            // "have I waited too long" was true on the very first tick and the job killed itself
            // before either pawn took a step. That is why grooming never once happened on its own
            // while manual orders appeared to work: the manual order most often ran the SELF job,
            // which has no wait toil at all.
            int waited = 0;

            wait.initAction = delegate { waited = 0; };
            wait.tickAction = delegate
            {
                Pawn other = Groomed;
                if (other == null || other.CurJobDef != AvirGroomingDefOf.Avir_BeingGroomedJob)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (pawn.rotationTracker != null) pawn.rotationTracker.FaceTarget(other);

                if (pawn.Position.DistanceToSquared(other.Position) <= AvirGrooming.GroomRangeSquared)
                {
                    ReadyForNextToil();
                    return;
                }

                waited++;
                if (waited > 2500) EndJobWith(JobCondition.Incompletable);
            };
            yield return wait;

            // ---- 4. THE SESSION ----
            int duration = job.def.joyDuration > 0 ? job.def.joyDuration : 2500;

            // Taken across the PAIR: one feather-proud or preener Avir sets the pace for both,
            // whether they are the one being fussed over or the one doing the fussing.
            duration = Mathf.RoundToInt(duration * AvirGroomingTraits.SpeedFactor(pawn, Groomed));

            Toil preen = new Toil();
            preen.defaultCompleteMode = ToilCompleteMode.Delay;
            preen.defaultDuration = duration;
            preen.handlingFacing = true;

            // Either of them leaving ends it. Both have jobs holding them in place now, so in
            // practice this fires when something with a stronger claim - a fight, a mental break,
            // the player - takes one of them away.
            preen.FailOn(() => pawn.Position.DistanceToSquared(Groomed.Position)
                > AvirGrooming.GroomRangeSquared);

            preen.tickAction = delegate
            {
                try
                {
                    if (pawn.rotationTracker != null) pawn.rotationTracker.FaceTarget(Groomed);
                    AvirGrooming.PayJoy(pawn, job.def, BaseJoyPerTick);
                }
                catch (Exception e)
                {
                    if (!loggedTickFailure)
                    {
                        loggedTickFailure = true;
                        Log.Error("[Avir] Grooming tick threw. Full trace follows; printed once "
                            + "per session.\n" + e);
                    }

                    EndJobWith(JobCondition.Errored);
                }
            };

            preen.WithProgressBar(TargetIndex.A,
                () => 1f - (preen.actor.jobs.curDriver.ticksLeftThisToil / (float)duration), true);

            preen.AddFinishAction(delegate
            {
                Pawn other = Groomed;
                if (other == null) return;

                // Let the recipient go, however this ended. Her own driver watches for this too,
                // but ending it here means she is free on the same tick rather than the next one.
                if (other.jobs != null && other.CurJobDef == AvirGroomingDefOf.Avir_BeingGroomedJob)
                {
                    other.jobs.EndCurrentJob(JobCondition.Succeeded, false);
                }

                // EVERYTHING ABOVE THIS LINE MUST RUN ON EVERY ENDING. Everything below is payment,
                // and payment is for time spent - see the same change in JobDriver_AvirGroomSelf.
                // Releasing the recipient is not a reward and must not be gated on progress.
                float progress = AvirGrooming.SessionProgress(ticksLeftThisToil, duration);
                if (progress <= 0f) return;

                // a mother with milk and no chicks to feed may let her mate drink (RJW only: BirdMilk.cs)
                if (progress >= AvirGrooming.SessionMemoryThreshold) AvirBirdMilk.TryFeedMate(pawn, other);

                // BOTH COATS. They spend the hour working through each other, so both come out of
                // it clean - and this is what makes going to a mate strictly better than preening
                // alone rather than merely nicer.
                int harvested = 0;

                HediffComp_FurShed theirs = AvirGrooming.CoatOf(other);
                if (theirs != null) harvested += theirs.HarvestLoose(pawn, progress);

                HediffComp_FurShed mine = AvirGrooming.CoatOf(pawn);
                if (mine != null) harvested += mine.HarvestLoose(other, progress);

                if (harvested <= 0) return;

                if (progress >= AvirGrooming.SessionMemoryThreshold)
                {
                    AvirGrooming.GiveGroomedMemory(other, pawn);
                    AvirGrooming.GiveGroomedMemory(pawn, other);
                }

                AvirGrooming.NoteSession(pawn, other, harvested, duration - ticksLeftThisToil);
            });

            yield return preen;
        }
    }

    /// <summary>
    /// THE OTHER HALF OF THE SAME ACT: being preened.
    ///
    /// Handed to the recipient by JobDriver_AvirGroomOther the moment the groomer arrives. It does
    /// nothing but hold still, face the groomer, and collect recreation - which is the point. An
    /// Avir leaning into a mate working through their coat is not idle, and should not be readable
    /// as idle by anything else in the game: not by the work scan, not by another Avir looking for
    /// someone to preen, and not by the player watching.
    ///
    /// IT ENDS ITSELF IF THE GROOMER STOPS. The groomer's finish action also releases it, but that
    /// only runs when the groomer's job ends cleanly. If the groomer is downed, drafted or killed
    /// outright, nothing would - and the recipient would sit there being preened by nobody.
    /// </summary>
    public class JobDriver_AvirBeingGroomed : JobDriver
    {
        private const float BaseJoyPerTick = 0.36f / 400f;

        private Pawn Groomer { get { return job.targetA.Thing as Pawn; } }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            // The groomer must still be grooming US, specifically. Checking only that they are
            // alive would leave a pawn posing for someone who walked off to haul steel.
            this.FailOn(() =>
            {
                Pawn groomer = Groomer;
                if (groomer == null || groomer.Dead || groomer.Downed) return true;
                if (groomer.CurJobDef != AvirGroomingDefOf.Avir_GroomOtherJob) return true;
                return groomer.CurJob.targetA.Thing != pawn;
            });

            // ---- WALK TO THE MEETING SPOT ----
            //
            // Touch rather than OnCell: the groomer is standing ON that cell, so this puts her
            // beside him instead of trying to occupy the same square.
            //
            // This toil is why the chase is gone. She is given this job at the START of his,
            // not on his arrival, so she stops hauling and comes to the spot herself.
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.Touch);

            Toil settle = new Toil();
            settle.defaultCompleteMode = ToilCompleteMode.Delay;

            // Longer than the groomer's own toil so this never runs out first and leaves him
            // working on somebody who has already stood up. He decides when it is over. Scaled by
            // the same trait factor for the same reason - a feather-proud pair would otherwise
            // break apart halfway.
            int settleDuration = job.def.joyDuration > 0 ? job.def.joyDuration : 2600;
            settleDuration = Mathf.RoundToInt(
                settleDuration * AvirGroomingTraits.SpeedFactor(pawn, Groomer));

            // Plus the rendezvous wait, so arriving first does not eat into the session.
            settleDuration += 2500;

            settle.defaultDuration = settleDuration;
            settle.handlingFacing = true;

            settle.tickAction = delegate
            {
                Pawn groomer = Groomer;
                if (groomer != null && pawn.rotationTracker != null)
                {
                    pawn.rotationTracker.FaceTarget(groomer);
                }

                AvirGrooming.PayJoy(pawn, job.def, BaseJoyPerTick);
            };

            yield return settle;
        }
    }

    /// <summary>
    /// Puts the idle grooming node into the humanlike think tree.
    ///
    /// ANCHORED AHEAD OF TAILING'S OWN NODE where possible, so a full coat is dealt with before an
    /// Avir goes looking for someone to shadow. Falling back to the vanilla idle anchors after
    /// that, and to nothing at all if neither is found - in which case grooming still happens as
    /// ordinary recreation through the joy giver, which is the larger half of it anyway.
    ///
    /// Anchors are matched by TYPE NAME rather than by type reference, for the reason spelled out
    /// in AvirTailingThinkInjector: a hard reference makes the whole assembly fail to load if a
    /// class is ever renamed, and this feature is not worth that risk.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirGroomingThinkInjector
    {
        private static readonly string[] AnchorTypeNames =
        {
            "JobGiver_AvirTailingIdle",
            "JobGiver_IdleJoy",
            "JobGiver_WanderColony"
        };

        /// <summary>
        /// Where the urgent node goes. Ordinary work, not emergency work - inserting before the
        /// emergency JobGiver_Work would put preening ahead of firefighting, which is not the
        /// intent even for a very itchy Avir.
        ///
        /// The tree contains more than one JobGiver_Work, so the walk below takes the LAST one it
        /// finds; the emergency copy comes first in Core's ordering.
        /// </summary>
        private const string WorkAnchor = "JobGiver_Work";

        static AvirGroomingThinkInjector()
        {
            // THE HUMANLIKE TREE, WHICH IS WHERE ADULTS LIVE.
            InjectInto("Humanlike", isOptional: false);

            // THE TODDLER TREE, WHICH IS WHERE MAXIMUS'S CHICKS LIVE AND WHY THEY WERE NEVER CLEAN.
            //
            // THE BUG, NAMED. cyanobot's Toddlers (packageId cyanobot.toddlers) inserts a
            // HumanlikeToddler life stage into the Human race at minAge 1, and hands that life
            // stage its OWN think tree, also called HumanlikeToddler. It is not a branch of the
            // humanlike tree - it is a separate tree with a separate root.
            //
            // Everything this injector had ever done went into "Humanlike" and nowhere else. So
            // for the two years an Avir chick spends as a toddler, neither grooming node existed
            // on any tree it was ever evaluated against. Not the idle one, not the urgent one.
            //
            // That is the whole of "птенцов никто не чистит". It was never a threshold, never the
            // sweep interval, never the job driver - the chick was being asked a question list
            // that had no grooming on it. The GameComponent sweep was the only thing that ever
            // reached them, and it runs once every day and a half.
            //
            // Optional: absent Toddlers this is a silent no-op, and the log line below simply
            // never appears.
            InjectInto("HumanlikeToddler", isOptional: true);
        }

        /// <summary>
        /// Puts both grooming nodes into one named think tree. Safe to call for a tree that may
        /// not exist - see the Toddlers case above.
        ///
        /// SEPARATE TREES NEED SEPARATE NODE INSTANCES. ThinkNode carries per-tree resolved state,
        /// so the same object cannot be spliced into two trees; every insert below constructs a
        /// fresh one. AlreadyInjected is likewise asked per tree rather than globally, or the
        /// second tree would be skipped because the first had been done.
        /// </summary>
        private static void InjectInto(string treeDefName, bool isOptional)
        {
            try
            {
                ThinkTreeDef tree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(treeDefName);
                if (tree == null || tree.thinkRoot == null)
                {
                    if (!isOptional)
                    {
                        Log.Warning("[Avir] " + treeDefName + " think tree not found; grooming will "
                            + "only happen during recreation time.");
                    }
                    return;
                }

                if (AlreadyInjected(tree.thinkRoot)) return;

                InsertUrgent(tree, treeDefName);

                for (int a = 0; a < AnchorTypeNames.Length; a++)
                {
                    if (InsertBefore(tree.thinkRoot, AnchorTypeNames[a]))
                    {
                        Log.Message("[Avir] Idle grooming inserted into " + treeDefName
                            + " ahead of " + AnchorTypeNames[a] + ".");
                        return;
                    }
                }

                Log.Warning("[Avir] No idle anchor found in " + treeDefName + "; grooming there will "
                    + "only happen during recreation time.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Failed to inject the grooming nodes into " + treeDefName + ": " + e);
            }
        }

        /// <summary>
        /// Puts JobGiver_AvirGroomingUrgent immediately before the LAST JobGiver_Work in the tree.
        ///
        /// Last rather than first, because Core's humanlike tree has an emergency JobGiver_Work
        /// well above the ordinary one, and a pawn should not stop to preen while the kitchen is on
        /// fire.
        /// </summary>
        private static void InsertUrgent(ThinkTreeDef tree, string treeDefName)
        {
            List<ThinkNode> parent = null;
            int index = -1;

            FindLast(tree.thinkRoot, ref parent, ref index);

            if (parent == null)
            {
                // A TODDLER TREE HAS NO WORK NODE, AND THAT IS NOT AN ERROR.
                //
                // Toddlers cannot work, so its tree contains no JobGiver_Work to anchor against.
                // Anchoring on the root instead is correct here rather than a compromise: with no
                // work to interrupt, "urgent" simply means "before the toddler wanders off", and
                // the node's own checks still refuse anyone whose coat is not past the threshold.
                if (tree.thinkRoot?.subNodes != null)
                {
                    parent = tree.thinkRoot.subNodes;
                    index = Mathf.Min(1, parent.Count);
                    parent.Insert(index, new JobGiver_AvirGroomingUrgent());
                    Log.Message("[Avir] " + treeDefName + " has no " + WorkAnchor
                        + " (expected for a tree whose pawns cannot work) - urgent grooming "
                        + "anchored near the root instead.");
                }
                else
                {
                    Log.Warning("[Avir] " + WorkAnchor + " not found in " + treeDefName
                        + " and the root has no subnodes. Avir there will only groom during "
                        + "recreation or idle time.");
                }
                return;
            }

            parent.Insert(index, new JobGiver_AvirGroomingUrgent
            {
                // The node we are stepping in front of. If this parent turns out to be a priority
                // sorter, GetPriority reads its number and returns slightly more - "just ahead of
                // work" stays true without hard-coding what work's number is.
                priorityPeer = index < parent.Count ? parent[index] : null
            });

            Log.Message("[Avir] Urgent grooming inserted into " + treeDefName
                + " ahead of " + WorkAnchor + ".");
        }

        private static void FindLast(ThinkNode node, ref List<ThinkNode> parent, ref int index)
        {
            if (node?.subNodes == null) return;

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                ThinkNode child = node.subNodes[i];
                if (child == null) continue;

                if (child.GetType().Name == WorkAnchor)
                {
                    parent = node.subNodes;
                    index = i;
                }

                FindLast(child, ref parent, ref index);
            }
        }

        private static bool AlreadyInjected(ThinkNode node)
        {
            if (node?.subNodes == null) return false;

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                if (node.subNodes[i] is JobGiver_AvirGroomingIdle) return true;
                if (node.subNodes[i] is JobGiver_AvirGroomingUrgent) return true;
                if (AlreadyInjected(node.subNodes[i])) return true;
            }

            return false;
        }

        private static bool InsertBefore(ThinkNode node, string anchorTypeName)
        {
            if (node?.subNodes == null) return false;

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                if (node.subNodes[i] != null && node.subNodes[i].GetType().Name == anchorTypeName)
                {
                    node.subNodes.Insert(i, new JobGiver_AvirGroomingIdle());
                    return true;
                }
            }

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                if (InsertBefore(node.subNodes[i], anchorTypeName)) return true;
            }

            return false;
        }
    }
}
