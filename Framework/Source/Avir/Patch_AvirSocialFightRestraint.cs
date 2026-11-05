using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// A QUARREL IS NOT A DUEL. AVIR DO NOT KILL EACH OTHER OVER AN INSULT.
    ///
    /// MAXIMUS'S CASE, WHICH IS A DESIGN PROBLEM BEFORE IT IS A CODE ONE. A labourer beat an
    /// inquisitor to death on the spot over a social slight. Visiting Avir took his colonists'
    /// limbs off in ordinary arguments. He was reduced to editing aggression out of them, which
    /// treats the symptom and costs him the trait he actually wanted them to have.
    ///
    /// He put the absurdity better than a bug report would: such a great civilisation, and a
    /// worker can club an inquisitor down where he stands. A society that loses a member to every
    /// bad temper does not get to be a thousand-year civilisation.
    ///
    /// WHY THIS HAPPENS TO AVIR AND NOT TO HUMANS. Vanilla social fights are survivable because
    /// vanilla humans throw punches - blunt, weak, and aimed at nothing in particular. An Avir
    /// brings essentium claws to the same argument: a Cut weapon whose top rung sits just under
    /// the best melee weapon in the game, on a body the mod deliberately gave brittle bones. The
    /// game does not know the difference between a shoving match and a killing, so it resolves
    /// both with whatever the pawn happens to be armed with. For an Avir that is always a blade.
    ///
    /// WHAT THIS DOES. While a social fight is running, damage between the two arguing pawns is
    /// cut hard and refused outright once the loser is clearly beaten. They still hurt each other,
    /// still bruise, still end up in bed and still hate each other afterwards - the social
    /// consequences Maximus wants are untouched. What stops is the killing and the amputation.
    ///
    /// WHAT THIS DELIBERATELY DOES NOT TOUCH. Real violence of every kind. Raids, hunts, duels,
    /// executions, a Reaver's killing blow, an Avir defending a bonded mate. The gate is the
    /// SocialFighting mental state and nothing else, so an Avir who genuinely means it is as
    /// lethal as she ever was. This is not a peace patch; it is the difference between a brawl
    /// and a murder.
    ///
    /// EVERY NAME USED HERE IS ALREADY PROVEN IN THIS CODEBASE. Pawn_HealthTracker.PreApplyDamage
    /// and its by-ref dinfo come from Patch_AvirBondHarm; MentalStateDefOf.SocialFighting and
    /// MentalState_SocialFighting come from Patch_AvirPairBondRomance. Nothing is being recalled
    /// from memory, which is the standing rule in this project and the one that cost us three
    /// dead systems when it was broken.
    /// </summary>
    public static class Patch_AvirSocialFightRestraint
    {
        /// <summary>
        /// What is left of a blow thrown in anger rather than in earnest.
        ///
        /// Not zero, on purpose. A social fight that does nothing at all is a different bug: the
        /// pawns would circle each other indefinitely, because the state ends when someone has
        /// clearly lost. They need to be able to lose.
        /// </summary>
        private const float SocialFightDamageFactor = 0.15f;

        /// <summary>
        /// Below this fraction of health the loser stops taking damage at all.
        ///
        /// This is the amputation guard as much as the death guard. Scaling alone still lets a
        /// long fight whittle a part away, and Maximus specifically lost limbs to arguments. At
        /// this point the fight is decided and further blows only decide how crippled the loser
        /// gets.
        /// </summary>
        private const float MercyThreshold = 0.4f;

        private static AccessTools.FieldRef<Pawn_HealthTracker, Pawn> healthPawn;
        private static bool bound;

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(Pawn_HealthTracker), "PreApplyDamage");
                if (target == null)
                {
                    Log.Warning("[Avir brawl] Pawn_HealthTracker.PreApplyDamage not found; social "
                        + "fights will stay lethal.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(Patch_AvirSocialFightRestraint), nameof(Damage_Prefix)));

                Log.Message("[Avir brawl] social fights bound to Pawn_HealthTracker.PreApplyDamage "
                    + "- Avir quarrels will bruise, not kill.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir brawl] could not bind social fight restraint: " + e);
            }
        }

        /// <summary>
        /// A PREFIX, NOT A POSTFIX, because the damage has to be changed BEFORE it lands. The
        /// bond-retaliation patch next door is a postfix precisely because it only wants to know
        /// that a blow happened; this one has to alter it.
        ///
        /// dinfo IS BY REFERENCE, and absorbed with it. Declaring either by value compiles fine
        /// and silently does nothing, which is how the map-edge fix wasted a whole session.
        /// </summary>
        public static void Damage_Prefix(Pawn_HealthTracker __instance, ref DamageInfo dinfo,
                                         ref bool absorbed)
        {
            try
            {
                if (absorbed) return;

                Pawn victim = VictimOf(__instance);
                if (victim == null || victim.Dead) return;

                Pawn attacker = dinfo.Instigator as Pawn;
                if (attacker == null || attacker == victim) return;

                if (dinfo.Def == null || !dinfo.Def.harmsHealth) return;

                // THE ONE GATE. Both sides must be in the social-fighting state, which the game
                // only ever sets on the two pawns actually arguing. A raider who happens to swing
                // at someone mid-quarrel is not covered, and should not be.
                if (!IsSocialFighting(attacker) || !IsSocialFighting(victim)) return;

                // AT LEAST ONE SIDE IS AVIR.
                //
                // Maximus asked for this because of Avir claws, so it is scoped to Avir rather
                // than imposed on his whole colony - a human bar fight is vanilla's business.
                // Widening it to everyone is a matter of deleting these two lines, and the reason
                // to consider that is Maximus's own report of GUESTS taking his colonists' limbs:
                // if those guests turn out not to be Avir, this gate is why it still happens.
                if (!AvirFaction.HasFearborn(attacker) && !AvirFaction.HasFearborn(victim)) return;

                // ALREADY BEATEN: refuse the blow entirely. This is the guard that keeps limbs on.
                if (victim.health?.summaryHealth != null
                    && victim.health.summaryHealth.SummaryHealthPercent < MercyThreshold)
                {
                    absorbed = true;
                    return;
                }

                if (victim.Downed)
                {
                    absorbed = true;
                    return;
                }

                dinfo.SetAmount(dinfo.Amount * SocialFightDamageFactor);
            }
            catch (Exception e)
            {
                // A quarrel must never be the thing that breaks damage handling for the whole map.
                Log.WarningOnce("[Avir brawl] social fight restraint failed: " + e, 0x4A52B3);
            }
        }

        private static bool IsSocialFighting(Pawn p)
        {
            if (p?.MentalState == null) return false;
            return p.MentalState.def == MentalStateDefOf.SocialFighting;
        }

        /// <summary>
        /// Same field access Patch_AvirBondHarm uses - Pawn_HealthTracker does not expose its own
        /// pawn, so it is read by reflection once and cached.
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
                    Log.Warning("[Avir brawl] Pawn_HealthTracker.pawn not found; social fight "
                        + "restraint is standing down.");
                }
            }

            return healthPawn == null || health == null ? null : healthPawn(health);
        }
    }
}
