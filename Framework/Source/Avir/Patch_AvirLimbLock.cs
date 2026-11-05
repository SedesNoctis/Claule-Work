using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// NO LIMB REPLACEMENTS. EVER. ON ANY AVIR.
    ///
    /// This is not a balance rule, it is an anatomical one. The essentium claws ARE the Avir's
    /// hands. A bionic arm does not sit alongside them - installing it DESTROYS the natural arm,
    /// claws and all, because that is how RimWorld models an added part. The pawn ends up trading
    /// a weapon that sharpens itself on whatever it kills for a factory-stamped hook.
    ///
    /// So every recipe that ADDS a part to a limb is hidden from the surgery menu on any Avir:
    /// bionic, prosthetic, archotech, drone, whatever a mod calls it. If it goes on an arm, a leg,
    /// a hand or a foot, an Avir cannot have it.
    ///
    /// WHAT IS STILL ALLOWED - deliberately:
    ///   - AMPUTATION and part REMOVAL. Those recipes add no hediff. If a hunter's arm is beyond
    ///     saving, you may still take it. Medicine is not vanity.
    ///   - NON-LIMB implants that survive the rest of the mod's rules - an archotech eye, a
    ///     painstopper, a joywire. Nothing there is a claw.
    ///   - The Avir's OWN parts. Anything Avir_* is theirs and is never blocked.
    ///
    /// This is the ONE bionic rule that applies after generation as well as during it. Everything
    /// else in Patch_AvirBionics fires once, when the pawn is made, and leaves your surgeon alone
    /// afterwards. This one has to hold forever, because the claws can be taken at any time.
    /// </summary>
    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableOnNow))]
    public static class Patch_AvirLimbLock
    {
        public static void Postfix(RecipeDef __instance, Thing thing, BodyPartRecord part, ref bool __result)
        {
            try
            {
                if (!__result) return; // already unavailable - nothing to do

                Pawn pawn = thing as Pawn;
                if (pawn == null) return;
                if (!AvirFaction.ShouldUseAvirContent(pawn)) return;

                if (IsForbiddenInstall(__instance, part)) __result = false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Limb lock check failed: " + e, 0x4A51D1);
            }
        }

        /// <summary>
        /// True if this recipe would ADD a part to a limb.
        ///
        /// "Adds a part" is the test that keeps amputation legal: an amputation recipe has no
        /// addsHediff, so it falls straight through and remains available.
        /// </summary>
        /// <summary>
        /// True if this recipe would install a forbidden part on an Avir.
        ///
        /// NARROWED. This used to forbid ANY added part anywhere on the body - arm, eye, spine,
        /// stomach, jaw, ear, everything. That was too broad: it fought every other bionics/
        /// implant mod wholesale, which was never the actual goal. The actual goal was always
        /// narrower - keep the essentium beak and claws intact, and keep a small set of
        /// identity-defining parts un-replaceable. So now it's exactly four categories:
        ///
        ///   LIMBS (arms, legs, hands, feet, fingers, toes, wings) - a bionic hand destroys the
        ///   essentium claw installing itself, exactly the problem this rule exists to prevent.
        ///
        ///   EYES and JAWS - the beak sits on the jaw; keeping both restricted protects it, and
        ///   eyes are grouped with it as the other "face" part worth the same treatment.
        ///
        ///   HEARTS specifically - not organs generally. Kidneys, livers, lungs, stomachs, spines
        ///   and everything else are fully open to whatever other mods the game has installed.
        ///
        /// AMPUTATION AND REMOVAL remain legal (no addsHediff, falls through). THE AVIR'S OWN
        /// PARTS remain legal (Avir_* hediffs, never blocked).
        /// </summary>
        public static bool IsForbiddenInstall(RecipeDef recipe, BodyPartRecord part)
        {
            if (recipe == null) return false;
            if (recipe.addsHediff == null) return false;          // removal/amputation - allowed
            if (IsAvirOwnHediff(recipe.addsHediff)) return false; // their own beak and claws - allowed
            if (part == null) return false;                      // no specific part - not one of the four categories

            return IsRestrictedCategory(part.def);
        }

        /// <summary>The four still-restricted categories: limbs, eyes, jaws, hearts.</summary>
        public static bool IsRestrictedCategory(BodyPartDef def)
        {
            if (def == null) return false;
            return IsLimb(def) || IsEye(def) || IsJaw(def) || IsHeart(def);
        }

        public static bool IsEye(BodyPartDef def)
        {
            return def != null && def.defName != null && Contains(def.defName, "Eye");
        }

        public static bool IsJaw(BodyPartDef def)
        {
            return def != null && def.defName == "Jaw";
        }

        public static bool IsHeart(BodyPartDef def)
        {
            return def != null && def.defName == "Heart";
        }

        public static bool IsAvirOwnHediff(HediffDef def)
        {
            return def != null && def.defName != null
                && def.defName.StartsWith("Avir_", StringComparison.Ordinal);
        }

        /// <summary>Is this body part a limb - an arm, a leg, or anything hanging off one?</summary>
        public static bool IsLimb(BodyPartDef def)
        {
            if (def == null) return false;

            // TAGS FIRST. This is the vanilla-native answer and it survives modded body parts:
            // an arm carries ManipulationLimbCore, a leg carries MovingLimbCore, and a modded
            // limb that wants to behave like one carries them too.
            if (def.tags != null)
            {
                for (int i = 0; i < def.tags.Count; i++)
                {
                    BodyPartTagDef t = def.tags[i];
                    if (t == null || t.defName == null) continue;

                    if (t.defName == "ManipulationLimbCore"
                     || t.defName == "ManipulationLimbSegment"
                     || t.defName == "ManipulationLimbDigit"
                     || t.defName == "MovingLimbCore"
                     || t.defName == "MovingLimbSegment"
                     || t.defName == "MovingLimbDigit")
                        return true;
                }
            }

            // NAME FALLBACK, for parts that never got tagged. Substring, not equality, because
            // mods produce Shoulder, UpperArm, ForelimbLeft and worse.
            string n = def.defName;
            if (n == null) return false;

            return Contains(n, "Arm") || Contains(n, "Hand") || Contains(n, "Finger")
                || Contains(n, "Leg") || Contains(n, "Foot") || Contains(n, "Toe")
                || Contains(n, "Shoulder") || Contains(n, "Clavicle")
                || Contains(n, "Humerus") || Contains(n, "Radius")
                || Contains(n, "Femur") || Contains(n, "Tibia")
                || Contains(n, "Limb") || Contains(n, "Claw") || Contains(n, "Talon")
                || Contains(n, "Wing");
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Convenience for the generation-time strip: is this HEDIFF sitting on a limb?</summary>
        public static bool IsOnLimb(BodyPartRecord part)
        {
            return part != null && IsLimb(part.def);
        }
    }
}
