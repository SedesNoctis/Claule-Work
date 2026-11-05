using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir
{
    /// <summary>
    /// REPAIRS FAMILY TREES THAT ARE ALREADY BROKEN IN THE SAVE.
    ///
    /// Deleting the gender-forcing code stops NEW corruption. It cannot undo the corruption that
    /// is already written into your world, and that is why the crash survived the last two fixes:
    ///
    ///   Look at the stack. The NullReferenceException is inside CreateRelation ITSELF - there is
    ///   no nested PawnGenerator call under it. So the parents were not being generated at that
    ///   moment. THEY ALREADY EXISTED, as world pawns, and they were already wrong.
    ///
    ///   An Avir who was generated Female, married into a family tree as somebody's MOTHER, and
    ///   then had her gender rewritten to Male by the old postfix, is still sitting in your save.
    ///   Her child now has TWO MALE PARENTS. GetMother() returns null. The next time anything asks
    ///   that child for a sibling, the null goes straight into the dereference and the whole pawn
    ///   group dies - "raid declined".
    ///
    ///   No amount of correct generation code fixes a pawn that was already saved wrong. The world
    ///   has to be repaired.
    ///
    /// WHAT THIS DOES, once, on load:
    ///   - walks every pawn in the world and on every map
    ///   - finds any pawn whose PARENTS are not one male and one female
    ///   - repairs it by flipping the surplus parent's gender back to the one the tree needs
    ///
    /// Flipping a world pawn's gender is safe in a way that flipping a NEWLY GENERATED pawn's
    /// gender is not: these pawns are already fully built and already embedded in the tree. We are
    /// restoring the gender the tree was BUILT with, which is by definition the consistent one.
    ///
    /// It logs what it fixed. On a clean world it finds nothing, says so, and costs nothing.
    /// </summary>
    public class GameComponent_AvirRelationRepair : GameComponent
    {
        private bool repaired;

        public GameComponent_AvirRelationRepair(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            // VERSION-KEYED ON PURPOSE. The v1 repair ran, wrote "done" into your save, and only
            // fixed one shape of the corruption - so on the next load it skipped, and the pawns it
            // had missed went right on crashing raids. Bumping the key forces the fixed repair to
            // run once more on saves that already think they are clean.
            Scribe_Values.Look(ref this.repaired, "avirRelationRepairDone_v2", false);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            try
            {
                int fixedCount = Repair();
                int genderFixed = RepairCasteGenders();
                this.repaired = true;

                if (genderFixed > 0)
                {
                    Log.Warning("[Avir] Caste gender repair: corrected " + genderFixed + " world pawn(s) "
                                + "whose gender did not match their caste. These were left behind by an older "
                                + "version of this mod. They mattered because a raid does not always GENERATE a "
                                + "pawn - PawnGenerator.GenerateOrRedressPawnInternal will REDRESS an existing "
                                + "world pawn instead, and a redressed pawn keeps the gender it was saved with. "
                                + "So the broken ones kept coming back in raids even though fresh generation was "
                                + "already correct.");
                }

                if (fixedCount > 0)
                {
                    Log.Warning("[Avir] Family tree repair: fixed " + fixedCount + " pawn(s) whose parents "
                                + "were both the same gender. This corruption was caused by an older version "
                                + "of this mod rewriting pawn gender after generation, and it is what was "
                                + "throwing NullReferenceException in PawnRelationWorker_Sibling and killing "
                                + "your high-value raids. It will not happen again - the gender-forcing code "
                                + "is gone.");
                }
                else
                {
                    Log.Message("[Avir] Family tree repair: nothing to fix. No corrupted parent pairs found.");
                }
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Family tree repair failed: " + e);
            }
        }

        private static int Repair()
        {
            int fixedCount = 0;
            StringBuilder report = new StringBuilder();

            foreach (Pawn pawn in AllPawnsEverywhere())
            {
                if (pawn == null || pawn.relations == null) continue;

                List<Pawn> parents = ParentsOf(pawn);
                if (parents.Count == 0) continue;

                // RimWorld does not STORE "mother" and "father" separately. It stores a Parent
                // relation and works out which is which BY GENDER. So a family tree is valid only
                // if a pawn's parents are at most one Male and at most one Female. Anything else and
                // GetMother() or GetFather() starts handing back nulls.
                //
                // My previous version of this only looked at pawns with TWO OR MORE parents, all of
                // the same gender. That missed the case that was still crashing you: a pawn with a
                // SINGLE parent who is the wrong gender - a lone male recorded as the mother. There
                // is nothing to compare him against, so the old check sailed straight past him.
                //
                // This version checks the shape properly, and where a pair collides it drops the
                // surplus Parent relation outright rather than trying to guess whose gender to flip.
                // A missing parent is harmless. A malformed one aborts raids.
                int males = 0, females = 0;
                foreach (Pawn p in parents)
                {
                    if (p == null) continue;
                    if (p.gender == Gender.Male) males++;
                    else if (p.gender == Gender.Female) females++;
                }

                // Healthy: at most one of each, and no genderless parents.
                bool healthy = males <= 1 && females <= 1 && (males + females) == parents.Count;
                if (healthy) continue;

                // Keep the first Male and the first Female. Sever everything else.
                bool keptMale = false, keptFemale = false;

                foreach (Pawn p in new List<Pawn>(parents))
                {
                    bool keep;

                    if (p == null) keep = false;
                    else if (p.gender == Gender.Male && !keptMale) { keep = true; keptMale = true; }
                    else if (p.gender == Gender.Female && !keptFemale) { keep = true; keptFemale = true; }
                    else keep = false;

                    if (keep) continue;

                    pawn.relations.RemoveDirectRelation(PawnRelationDefOf.Parent, p);

                    report.AppendLine("    " + pawn.LabelShortCap + ": severed malformed parent link to "
                                      + (p != null ? p.LabelShortCap + " (" + p.gender + ")" : "a null pawn"));
                    fixedCount++;
                }
            }

            if (fixedCount > 0) Log.Message("[Avir] repaired:\n" + report.ToString().TrimEnd());

            return fixedCount;
        }

        /// <summary>
        /// Fix Avir world pawns whose gender does not match their caste.
        ///
        /// WHY THIS IS NEEDED EVEN THOUGH GENERATION IS NOW CORRECT:
        ///
        ///   A raid does not always generate a new pawn. PawnGenerator.GenerateOrRedressPawnInternal
        ///   will REDRESS a suitable pawn out of WorldPawns instead - and a redressed pawn keeps the
        ///   gender it was saved with. Every male huntress created back when nothing was locking
        ///   gender is still sitting in that pool, and raids keep pulling her out.
        ///
        ///   That is exactly the symptom: a pawn spawned from the console (freshly generated) has
        ///   the right gender, while pawns arriving in a raid (possibly redressed) sometimes do not.
        ///
        /// THE ONE EXCEPTION - and it is the whole reason this is careful rather than a blanket sweep:
        ///
        ///   A male Avir_Huntress who is somebody's FATHER is CORRECT. The game builds a father by
        ///   reusing the child's kindDef and demanding Male. Flipping him to Female to satisfy the
        ///   caste rule would put a female in the father slot and reintroduce the exact null that
        ///   spent five rounds cancelling your raids. So: if a pawn is anyone's parent, LEAVE THEM.
        /// </summary>
        private static int RepairCasteGenders()
        {
            int count = 0;
            StringBuilder report = new StringBuilder();

            foreach (Pawn pawn in AllPawnsEverywhere())
            {
                if (pawn == null || pawn.kindDef == null || pawn.kindDef.defName == null) continue;

                Gender want = Patch_AvirGenderRequest.WantedGender(pawn.kindDef.defName);
                if (want == Gender.None || pawn.gender == want) continue;

                if (IsSomeonesParent(pawn))
                {
                    // He is a father. He is supposed to be male. Do not touch him.
                    continue;
                }

                report.AppendLine("    " + pawn.LabelShortCap + " (" + pawn.kindDef.defName + "): "
                                  + pawn.gender + " -> " + want);

                pawn.gender = want;

                if (pawn.story != null) pawn.story.bodyType = null;
                PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn);
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();

                count++;
            }

            if (count > 0) Log.Message("[Avir] caste gender repaired:\n" + report.ToString().TrimEnd());

            return count;
        }

        /// <summary>Is this pawn recorded as anybody's parent? If so their gender is load-bearing.</summary>
        private static bool IsSomeonesParent(Pawn pawn)
        {
            if (pawn.relations == null) return false;

            List<DirectPawnRelation> direct = pawn.relations.DirectRelations;
            if (direct == null) return false;

            for (int i = 0; i < direct.Count; i++)
            {
                if (direct[i] != null && direct[i].def == PawnRelationDefOf.Child) return true;
            }

            return false;
        }

        /// <summary>Every pawn the game knows about: world pawns, and everyone on every map.</summary>
        private static IEnumerable<Pawn> AllPawnsEverywhere()
        {
            if (Find.WorldPawns != null)
            {
                foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead) yield return p;
            }

            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map == null || map.mapPawns == null) continue;
                    foreach (Pawn p in map.mapPawns.AllPawns) yield return p;
                }
            }
        }

        private static List<Pawn> ParentsOf(Pawn pawn)
        {
            List<Pawn> parents = new List<Pawn>();

            List<DirectPawnRelation> direct = pawn.relations.DirectRelations;
            if (direct == null) return parents;

            for (int i = 0; i < direct.Count; i++)
            {
                DirectPawnRelation rel = direct[i];
                if (rel == null || rel.def != PawnRelationDefOf.Parent || rel.otherPawn == null) continue;
                parents.Add(rel.otherPawn);
            }

            return parents;
        }
    }
}
