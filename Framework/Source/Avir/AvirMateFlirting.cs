using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AVIR MATES FLIRT, CONSTANTLY, AND ONLY WITH EACH OTHER.
    ///
    /// Vanilla Races Expanded - Highmate adds "flirty words" (VRE_FlirtingAttempt): weight 1 for a
    /// pawn with the Flirty trait, 0 otherwise; success is the recipient's opinion of the initiator
    /// as a percentage; on success both get "flirted with", +5 opinion.
    ///
    ///   - BETWEEN MATES, where either is an Avir: either may start it, trait or not, at
    ///     mateFlirtWeight (Avir_PairBond extension, 0.75 against chitchat's 1), and it always lands.
    ///   - AN AVIR NEVER FLIRTS WITH ANYONE ELSE.
    ///   - AN OUTSIDER MAY TRY, under Highmate's own rules (the Flirty trait). If the Avir is bonded
    ///     it fails, and is answered exactly like a wrongful courtship: the Avir and its mate go for
    ///     them, and they get the hidden Avir_RomanceRebuffed - after which they do not try again,
    ///     at flirting or at courting. An unbonded Avir is left to Highmate's ordinary rules.
    ///
    /// The splinter-hearted is under Highmate's ordinary rules, like everyone without the gene.
    /// Late-bound by type name: nothing here references Highmate, and without it this does nothing.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirMateFlirting
    {
        static AvirMateFlirting()
        {
            Type worker = AccessTools.TypeByName("VanillaRacesExpandedHighmate.InteractionWorker_FlirtingAttempt");
            if (worker == null) return;
            try
            {
                Harmony h = new Harmony("Tenebris.Avir.MateFlirting");
                MethodInfo weight = AccessTools.Method(worker, "RandomSelectionWeight", new[] { typeof(Pawn), typeof(Pawn) });
                MethodInfo success = AccessTools.Method(worker, "SuccessChance", new[] { typeof(Pawn), typeof(Pawn) });
                MethodInfo interacted = AccessTools.Method(worker, "Interacted");
                if (weight != null) h.Patch(weight, postfix: new HarmonyMethod(typeof(AvirMateFlirting), nameof(Weight_Postfix)));
                if (success != null) h.Patch(success, postfix: new HarmonyMethod(typeof(AvirMateFlirting), nameof(Success_Postfix)));
                if (interacted != null) h.Patch(interacted, postfix: new HarmonyMethod(typeof(AvirMateFlirting), nameof(Interacted_Postfix)));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Highmate flirting patch failed: " + e);
            }
        }

        private static bool Mates(Pawn a, Pawn b)
        {
            return a?.relations != null && b != null && LovePartnerRelationUtility.LovePartnerRelationExists(a, b);
        }

        /// <summary>A bonded Avir being flirted at by someone who is not its mate.</summary>
        private static bool Wrongful(Pawn initiator, Pawn recipient)
        {
            return initiator != recipient
                && AvirPairBond.HasBondGene(recipient)
                && AvirPairBond.IsBonded(recipient)
                && !Mates(initiator, recipient);
        }

        public static void Weight_Postfix(Pawn initiator, Pawn recipient, ref float __result)
        {
            try
            {
                if (initiator == null || recipient == null) return;
                bool iAvir = AvirPairBond.HasBondGene(initiator);
                bool rAvir = AvirPairBond.HasBondGene(recipient);
                if (!iAvir && !rAvir) return;

                if (initiator.DevelopmentalStage.Juvenile() || recipient.DevelopmentalStage.Juvenile())
                {
                    __result = 0f;
                    return;
                }

                if (Mates(initiator, recipient))
                {
                    __result = AvirPairBond.MateFlirtWeight;
                    return;
                }

                // An Avir does not flirt with anyone but its mate.
                if (iAvir)
                {
                    __result = 0f;
                    return;
                }

                // An outsider who has been answered once does not try again.
                if (AvirPairBond.WasRebuffed(initiator)) __result = 0f;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] mate flirting weight failed: " + e, 0x4A52A2);
            }
        }

        /// <summary>The original returns a 0-100 figure.</summary>
        public static void Success_Postfix(Pawn initiator, Pawn recipient, ref float __result)
        {
            try
            {
                if (initiator == null || recipient == null) return;
                if (Mates(initiator, recipient)
                    && (AvirPairBond.HasBondGene(initiator) || AvirPairBond.HasBondGene(recipient)))
                {
                    __result = 100f;
                }
                else if (Wrongful(initiator, recipient))
                {
                    __result = 0f;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] mate flirting success failed: " + e, 0x4A52A3);
            }
        }

        public static void Interacted_Postfix(Pawn initiator, Pawn recipient)
        {
            try
            {
                if (initiator == null || recipient == null || !Wrongful(initiator, recipient)) return;
                Patch_AvirPairBondRomance.Rebuff(recipient, initiator, "flirted with");
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] mate flirting rebuff failed: " + e, 0x4A52A4);
            }
        }
    }
}
