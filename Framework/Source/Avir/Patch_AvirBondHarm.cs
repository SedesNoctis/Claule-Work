using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// HURT THE ONE THEY CHOSE AND THEY WILL NOT FORGET IT.
    ///
    /// TWO MEMORIES, NOT ONE, AND THAT SHAPE IS THE WHOLE DESIGN. A single -25 that expires leaves
    /// nothing behind, which is wrong for this species; a single -25 that never expires makes one
    /// bar brawl permanently unforgivable, which is worse. So: -25 for three days, and underneath
    /// it a -10 that stays for good.
    ///
    /// THE STACK LIMIT IS WHAT KEEPS A FIGHT FROM BEING A DEATH SENTENCE. A social fight lands a
    /// dozen small injuries; without a cap that would be -300 and the two pawns would never share a
    /// room again. Both memories are capped at one per attacker, so a brawl costs exactly the same
    /// as a single punch: the Avir cares that you hurt her mate, not how many times the blow
    /// landed.
    ///
    /// FAMILY IS EXEMPT, as with everything else the bond does socially. A brother who breaks her
    /// mate's arm in a fistfight is still her brother.
    /// </summary>
    public static class Patch_AvirBondHarm
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(Pawn_HealthTracker), "PreApplyDamage");
                if (target == null)
                {
                    Log.Warning("[Avir bond] Pawn_HealthTracker.PreApplyDamage not found; harming a "
                        + "bonded mate will go unanswered.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(Patch_AvirBondHarm), nameof(Damage_Postfix)));

                Log.Message("[Avir bond] harm-to-mate bound to Pawn_HealthTracker.PreApplyDamage.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir bond] could not bind harm-to-mate: " + e);
            }
        }

        /// <summary>
        /// Parameter named dinfo, matching the assembly.
        ///
        /// Hooked at damage rather than at injury creation because this must fire for a blow that
        /// is absorbed by armour as well as one that draws blood - the intent was the same, and an
        /// Avir watching someone swing at her mate does not check whether it connected cleanly.
        /// </summary>
        /// <summary>
        /// dinfo IS BY REFERENCE. PreApplyDamage takes `ref DamageInfo dinfo` - read from the
        /// assembly, not assumed - and Harmony will not bind a by-value parameter to a by-ref one.
        /// Declaring it plainly would have compiled fine and silently disabled the patch, which is
        /// how the map-edge fix spent a whole session doing nothing.
        /// </summary>
        public static void Damage_Postfix(Pawn_HealthTracker __instance, ref DamageInfo dinfo)
        {
            try
            {
                Pawn victim = VictimOf(__instance);
                if (victim == null || victim.Dead) return;

                Pawn attacker = dinfo.Instigator as Pawn;
                if (attacker == null || attacker == victim) return;

                // Only real violence. Anaesthetic, surgery mishaps and the like are not assaults,
                // and a surgeon who loses a patient's finger should not be marked for life.
                if (dinfo.Def == null || !dinfo.Def.harmsHealth) return;
                if (dinfo.Instigator == victim) return;

                // THE ANGRY ONE IS THE AVIR, AND THE HURT ONE NEED NOT BE.
                //
                // The first version asked MateOf(victim), which requires the VICTIM to carry the
                // gene - so an Avir whose HUMAN mate was beaten felt nothing at all. Backwards:
                // this is the Avir's feature, not her partner's, and it fires on what happens to
                // whoever she chose regardless of what that person is.
                //
                // Searched from the victim's relations instead, keeping only bonded partners who
                // actually carry the gene. A human married to an Avir still gets no reaction of
                // their own when the Avir is hurt - that half was right and stays.
                List<Pawn> avenchers = MatesWithGene(victim);
                if (avenchers == null) return;

                for (int i = 0; i < avenchers.Count; i++)
                {
                    Pawn mate = avenchers[i];
                    if (mate == attacker || mate.Dead) continue;
                    if (AvirBondSocial.IsFamily(mate, attacker)) continue;

                    Remember(mate, attacker);
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] harm postfix failed: " + e, 0x4A5290);
            }
        }

        /// <summary>
        /// Everyone bonded to this pawn who carries the gene - which is what decides whether the
        /// bond reacts at all.
        /// </summary>
        private static List<Pawn> MatesWithGene(Pawn victim)
        {
            List<DirectPawnRelation> relations = victim.relations?.DirectRelations;
            if (relations == null) return null;

            List<Pawn> found = null;

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation rel = relations[i];
                if (rel?.def == null || rel.otherPawn == null) continue;
                if (!LovePartnerRelationUtility.IsLovePartnerRelation(rel.def)) continue;
                if (!AvirPairBond.HasBondGene(rel.otherPawn)) continue;

                if (found == null) found = new List<Pawn>();
                found.Add(rel.otherPawn);
            }

            return found;
        }

        private static AccessTools.FieldRef<Pawn_HealthTracker, Pawn> healthPawn;
        private static bool bound;

        /// <summary>
        /// Pawn_HealthTracker.pawn is private - checked, not assumed, after reading a private field
        /// name straight out of the metadata cost a compile earlier today.
        /// </summary>
        private static Pawn VictimOf(Pawn_HealthTracker health)
        {
            if (!bound)
            {
                bound = true;
                try
                {
                    healthPawn = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
                }
                catch
                {
                    healthPawn = null;
                }

                if (healthPawn == null)
                {
                    Log.Warning("[Avir bond] Pawn_HealthTracker.pawn not found; harm-to-mate is "
                        + "standing down.");
                }
            }

            return healthPawn == null || health == null ? null : healthPawn(health);
        }

        private static void Remember(Pawn mate, Pawn attacker)
        {
            MemoryThoughtHandler memories = mate.needs?.mood?.thoughts?.memories;
            if (memories == null) return;

            ThoughtDef acute = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_MateHarmed");
            ThoughtDef lasting = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_MateHarmedLasting");

            // Both are gained at once rather than the second replacing the first when it expires.
            // A timer that swapped one for the other would need somewhere to live and something to
            // tick it; two memories with different durations do the same job with nothing running.
            // The acute one falls away after three days and leaves the permanent one exposed.
            if (acute != null) memories.TryGainMemory(acute, attacker);
            if (lasting != null) memories.TryGainMemory(lasting, attacker);
        }
    }
}
