using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// A'Shacsaign runs in the blood. One A'Shacsaign parent gives the child a 40% chance of
    /// being one; two give 80%. With no A'Shacsaign parent nothing happens here and the ordinary
    /// background rate in Patch_AvirAShacsaignSpawn stands.
    ///
    /// WHY THIS CANNOT LIVE IN THE SPAWN PATCH. That patch postfixes PawnGenerator.GeneratePawn
    /// and decides from thingIDNumber alone. A newborn passes through GeneratePawn too, so its
    /// verdict is handed down before anybody knows who the parents are. The parental roll has to
    /// run later, once parentage exists, and it has to be able to OVERRULE the earlier answer in
    /// both directions - a child of two A'Shacsaign who lost the 80% must come out ordinary, not
    /// merely fail to be upgraded.
    ///
    /// WHY THE RESULT IS STORED RATHER THAN DERIVED. The background roll is reproducible: seed on
    /// the pawn's id and the same answer comes back forever, free across saves. A parental roll
    /// cannot be recovered from an id, so it is committed once and the hediff itself is the
    /// record - it lives on the pawn's health tracker and is saved with it. The two paths are
    /// therefore different in kind, and the flag below is what keeps the ID-seeded path from
    /// second-guessing a decision that has already been made.
    ///
    /// TWO ENTRY POINTS, ONE RULE.
    ///   - Hatching: VEF's CompHumanHatcher, which carries hatcheeParent and otherParent. This is
    ///     the ordinary case, since Avir lay.
    ///   - Live birth: PregnancyUtility.ApplyBirthOutcome, for carriers outside the xenotype.
    /// Both funnel into Apply, so the rule exists in exactly one place.
    ///
    /// World-generated parentage is deliberately NOT covered. Pawns handed ready-made relatives
    /// at generation were never born, and chasing them leads straight back to counting
    /// grandparents. Only real births and real hatchings roll.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirAShacsaignInheritance
    {
        public const float ChanceOneParent  = 0.40f;
        public const float ChanceBothParents = 0.80f;

        private const string HatcherCompType = "VEF.Genes.CompHumanHatcher";

        private static FieldInfo hatcheeParentField;
        private static FieldInfo otherParentField;

        static Patch_AvirAShacsaignInheritance()
        {
            Harmony harmony = new Harmony("avir.ashacsaign.inheritance");

            // --- Live birth -------------------------------------------------------------------
            MethodInfo birth = AccessTools.Method(typeof(PregnancyUtility), "ApplyBirthOutcome");
            if (birth != null)
            {
                harmony.Patch(birth, postfix: new HarmonyMethod(
                    typeof(Patch_AvirAShacsaignInheritance), nameof(BirthPostfix)));
            }
            else
            {
                Log.Warning("[Avir] PregnancyUtility.ApplyBirthOutcome not found - A'Shacsaign "
                            + "inheritance will not apply to live births.");
            }

            // --- Hatching ---------------------------------------------------------------------
            Type hatcher = AccessTools.TypeByName(HatcherCompType);
            if (hatcher == null)
            {
                return; // VEF absent; eggs do not exist either, so there is nothing to miss.
            }

            hatcheeParentField = AccessTools.Field(hatcher, "hatcheeParent");
            otherParentField   = AccessTools.Field(hatcher, "otherParent");

            MethodInfo hatch = AccessTools.Method(hatcher, "Hatch");
            if (hatch == null || hatcheeParentField == null || otherParentField == null)
            {
                Log.Warning("[Avir] CompHumanHatcher did not expose Hatch/hatcheeParent/"
                            + "otherParent - A'Shacsaign inheritance will not apply to eggs.");
                return;
            }

            // Prefix, not postfix: the parent fields are read BEFORE hatching, because the comp
            // is destroyed along with the egg on the way out and the references go with it. The
            // parents are stashed in __state and spent once the chick exists.
            harmony.Patch(hatch,
                prefix:  new HarmonyMethod(typeof(Patch_AvirAShacsaignInheritance), nameof(HatchPrefix)),
                postfix: new HarmonyMethod(typeof(Patch_AvirAShacsaignInheritance), nameof(HatchPostfix)));
        }

        // ---------------------------------------------------------------------------------------

        // PARAMETER NAMES ARE NOT A GUESS. Harmony binds injections by name, so a wrong one
        // throws at runtime rather than at compile time. Read off the assembly: ApplyBirthOutcome
        // takes (outcome, quality, ritual, genes, geneticMother, birtherThing, father, doctor,
        // lordJobRitual, assignments, preventLetter). The mother parameter is geneticMother, NOT
        // mother, and birtherThing is a Thing because a baby can be born out of a growth vat.
        //
        // AND THE RETURN TYPE IS Thing, NOT Pawn. This declared Pawn __result, and Harmony
        // refused the whole patch class over it:
        //
        //     Cannot assign method return type Verse.Thing to __result type Verse.Pawn
        //
        // That exception came out of the STATIC CONSTRUCTOR, which means the class never
        // initialised and NONE of the A'Shacsaign inheritance ran - not the live-birth branch,
        // not the egg branch. The system has been silently dead, and nothing in the game said so
        // except one red line at startup.
        //
        // ApplyBirthOutcome returns Thing because a birth can produce something that is not a
        // pawn. So it is taken as Thing here and narrowed below; a non-pawn birth simply falls
        // through, which is correct.
        public static void BirthPostfix(Thing __result, Pawn geneticMother, Pawn father)
        {
            Apply(__result as Pawn, geneticMother, father);
        }

        public static void HatchPrefix(object __instance, ref Pawn[] __state)
        {
            __state = null;
            if (hatcheeParentField == null) return;

            __state = new Pawn[]
            {
                hatcheeParentField.GetValue(__instance) as Pawn,
                otherParentField.GetValue(__instance) as Pawn
            };
        }

        public static void HatchPostfix(object __instance, Pawn[] __state)
        {
            if (__state == null) return;

            // Hatch spawns the chick rather than returning it, so it has to be found. Both parents
            // are known, and a just-hatched pawn is a child of both - that is enough to identify it
            // without reaching into VEF's internals any further than the two fields above.
            Pawn chick = AvirBirthUtility.FindFreshChick(__state[0], __state[1]);
            if (chick != null)
            {
                Apply(chick, __state[0], __state[1]);
            }
        }

        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The whole rule. Silent no-op unless at least one parent is A'Shacsaign.
        /// </summary>
        public static void Apply(Pawn child, Pawn parentA, Pawn parentB)
        {
            if (child == null || child.health == null) return;

            HediffDef subrace = Patch_AvirAShacsaignSpawn.Subrace;
            if (subrace == null) return;

            // Only Fearborn carry the subrace at all. A hybrid that did not inherit the Avir line
            // is not eligible however its parents were built.
            if (!AvirFaction.HasFearborn(child)) return;

            int carriers = 0;
            if (Patch_AvirAShacsaignSpawn.IsSubrace(parentA)) carriers++;
            if (Patch_AvirAShacsaignSpawn.IsSubrace(parentB)) carriers++;
            if (carriers == 0) return; // background rate keeps its verdict

            float chance = carriers >= 2 ? ChanceBothParents : ChanceOneParent;
            bool hit = Rand.Chance(chance);

            bool has = child.health.hediffSet.HasHediff(subrace);

            if (hit && !has)
            {
                child.health.AddHediff(subrace);
                Patch_AvirAShacsaignSpawn.StripEcholocation(child);
            }
            else if (!hit && has)
            {
                // OVERRULES THE BACKGROUND ROLL. The child drew into the 5% on id alone and then
                // lost the parental roll; parentage is the more specific claim and wins. Without
                // this branch the two systems would only ever add, and a chick of two A'Shacsaign
                // could never come out ordinary.
                Hediff present = child.health.hediffSet.GetFirstHediffOfDef(subrace);
                if (present != null) child.health.RemoveHediff(present);
            }
        }

    }
}
