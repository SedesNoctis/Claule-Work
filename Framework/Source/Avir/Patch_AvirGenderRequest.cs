using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// CASTE GENDER. THE ONLY SAFE MECHANISM. READ ALL OF THIS BEFORE CHANGING ANYTHING HERE.
    ///
    /// THE PROBLEM, precisely:
    ///
    ///   RimWorld makes two pawns siblings by giving them shared PARENTS, and it builds a missing
    ///   parent inside PawnGenerator.GeneratePawnRelations, roughly like this:
    ///
    ///       new PawnGenerationRequest(existingChild.kindDef, ..., fixedGender: genderToGenerate)
    ///
    ///   It reuses THE CHILD'S OWN KINDDEF and demands a specific gender - Male for the father,
    ///   Female for the mother. So to give an Avir_Huntress a sibling, the game must be able to
    ///   generate a MALE Avir_Huntress to be their father. That is not a bug. That is a father.
    ///
    /// WHY <fixedGender> IN XML CANNOT BE USED:
    ///
    ///   PawnKindDef.fixedGender is UNCONDITIONAL. It overrides the request. So when the game asked
    ///   for a male Avir_Huntress father, fixedGender=Female handed back a female, and:
    ///
    ///       "Tried to set Sel-Cielrie with gender Female as Valcrissria's father."
    ///       -> SetFather(<female>) -> null -> NullReferenceException
    ///       -> the whole pawn GROUP aborts -> "raid declined"
    ///
    ///   It is exactly the same failure as a male "mother", from the other direction. There is no
    ///   way to express "female, unless you specifically need a father" in XML. So the fixedGender
    ///   lines are GONE from PawnKind_AvirRoost.xml and must not come back.
    ///
    /// WHY A POSTFIX CANNOT BE USED EITHER:
    ///
    ///   Rewriting gender after generation corrupts family trees that have already been built
    ///   around the old gender, and "only flip pawns with no relations yet" does not save you - a
    ///   parent has no relations at the instant it is generated, because it is about to be
    ///   attached, not yet attached. Both variants were tried. Both killed raids.
    ///
    /// WHAT WORKS - and the reason it works:
    ///
    ///   Set the gender on the REQUEST, and ONLY when the caller has not already set one.
    ///
    ///     - Raid generation asks for an Avir_Huntress with no fixed gender -> we say Female. Every
    ///       huntress who walks out of a raid is female. The caste rule holds.
    ///     - The relation worker asks for an Avir_Huntress with fixedGender: Male -> we say nothing
    ///       and stand aside. It gets its father. The family tree is valid.
    ///
    ///   The caste rule is a statement about WHICH PAWNS A RAID FIELDS. It was never a claim about
    ///   who is allowed to be somebody's father. Every version of this bug came from conflating
    ///   those two things.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
                  new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirGenderRequest
    {
        // VeryHigh, not First. Patch_AvirGenderTrace_In sits at First so it can record the
        // request as the CALLER handed it over, before we touch it. We run immediately after, still
        // ahead of every other mod. Same priority as the tracer would be a coin-flip on ordering.
        [HarmonyPriority(Priority.VeryHigh)]
        public static void Prefix(ref PawnGenerationRequest request)
        {
            try
            {
                // ONLY THE RELATION WORKER MAY OVERRIDE A CASTE. Nobody else.
                //
                // This used to read "if (request.FixedGender.HasValue) return;" - stand aside
                // whenever the caller had already picked a gender. That was too trusting, and it is
                // why huntresses and priestesses kept arriving male:
                //
                //   Sex Matters Redux transpiles PawnGroupKindWorker_Normal.GeneratePawns and fills
                //   in the Nullable<Gender> local that vanilla leaves NULL, weighting it by its
                //   maleProbability setting. So EVERY raider reaches PawnGenerator with a gender
                //   already fixed - and our code read that as "the caller knows best" and obeyed it.
                //   From here, an SMR coin-flip and a relation worker's father request look exactly
                //   the same. The request cannot tell you who set it.
                //
                // So we ask WHERE we are instead of WHAT we were handed. While GenerateParent is on
                // the stack, the gender is load-bearing - a father must be male - and we defer.
                // Anywhere else, including a raid, the caste rule wins and we overwrite it.
                //
                // Sex Matters Redux is untouched otherwise: its ratio still governs every non-Avir
                // raider, and every Avir kind WITHOUT a caste gender (predator, strider, artificer,
                // guild master, hatchling) still rolls through it normally. It simply no longer gets
                // a vote on the four castes whose gender is part of what they ARE.
                if (Patch_AvirParentGenScope.GeneratingParent) return;

                PawnKindDef kind = request.KindDef;
                if (kind == null || kind.defName == null) return;

                Gender want = WantedGender(kind.defName);
                if (want == Gender.None) return;   // no caste rule - SMR's ratio applies as normal

                if (request.FixedGender != want) request.FixedGender = want;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Gender request prefix failed: " + e, 0x4A5201);
            }
        }

        /// <summary>The gender a caste is fielded as, or None if the caste does not care.</summary>
        public static Gender WantedGender(string kindDefName)
        {
            switch (kindDefName)
            {
                case "Avir_Huntress":
                case "Avir_Matriarch":
                case "Avir_Priestess":
                    return Gender.Female;

                case "Avir_Warrior":
                    return Gender.Male;

                // A'SCHIALIS LEGION. The two combat ranks are male-only - the Legion is far more
                // militarised than the A'Shaign, and its centurions and legionnaires are drawn
                // exclusively from the males. Inquisitors and Highlords remain any-gender.
                case "Avir_Centurion":
                case "Avir_Legionnaire":
                    return Gender.Male;

                default:
                    return Gender.None; // predator, strider, artificer, guild, hatchling,
                                        // inquisitor, highlord: either
            }
        }
    }
}
