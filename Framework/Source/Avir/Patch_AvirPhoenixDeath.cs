using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Everything that has to be intercepted for a Phoenix death to behave like a rebirth rather
    /// than a death. Five separate concerns, all keyed off AvirPhoenixUtil.UnderThePhoenix:
    ///
    ///   1. DEATH CAPTURE      record map + cell before the kill despawns the pawn, then queue
    ///                         the rebirth.
    ///   2. PSYCHIC BOND       stop Gene_PsychicBonding tearing the bond.
    ///   3. DEATH THOUGHTS     suppress the ordinary grief, substitute "burned".
    ///   4. FIRE IMMUNITY      a freshly risen pawn cannot be set alight or burned.
    ///   5. CONSCIOUSNESS      hold at 100% so the agony stages cannot down the pawn.
    ///
    /// Every hook that touches a type which might not exist (Gene_PsychicBonding needs Biotech)
    /// is applied manually inside the static constructor and simply skipped if the type is
    /// missing, rather than declared with attributes that would throw at load.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirPhoenixDeath
    {
        static Patch_AvirPhoenixDeath()
        {
            Harmony harmony = new Harmony("Avir.Phoenix");

            TryPatchPsychicBond(harmony);
            TryPatchDeathThoughts(harmony);
        }

        // ------------------------------------------------------------
        //  2. PSYCHIC BOND
        // ------------------------------------------------------------
        /// <summary>
        /// Vanilla tears the psychic bond inside Gene_PsychicBonding.Notify_PawnDied - which is
        /// also the exact method Vanilla Races Expanded - Highmate patches
        /// (VanillaRacesExpandedHighmate_Gene_Notify_PawnDied_Patch), confirming the address.
        /// Skipping the original leaves the bond hediff untouched on both pawns and no
        /// "psychic bond torn" mood.
        ///
        /// KNOWN RISK, FLAGGED RATHER THAN GUESSED: if Highmate's own patch on this method is a
        /// POSTFIX, it still runs even though we skip the original, and it may do its own bond
        /// cleanup. If bonds still break with a Highmate pawn specifically, that postfix is the
        /// culprit and needs a second, narrower patch. It cannot be determined from the shipped
        /// DLL's string table alone.
        /// </summary>
        private static void TryPatchPsychicBond(Harmony harmony)
        {
            // PATCH THE DECLARED METHOD, NOT THE INHERITED ONE.
            //
            // Gene_PsychicBonding does not declare Notify_PawnDied - it inherits it from Verse.Gene.
            // AccessTools.Method on the derived type therefore handed back the BASE method while
            // the patch still named the derived type, and Harmony threw at load:
            //
            //     ArgumentException: You can only patch implemented methods/constructors.
            //     Patch the declared method virtual System.Void Verse.Gene::Notify_PawnDied(...)
            //
            // The exception came out of a static constructor, so the whole class failed to
            // initialise and every OTHER patch in it - death capture, death thoughts - went with
            // it. That is why this crashed rather than just not working.
            //
            // Fixed by patching Verse.Gene.Notify_PawnDied, which is where the method actually
            // lives, and narrowing INSIDE the prefix to the psychic bonding gene alone. Skipping
            // the base method for every gene on a dying pawn would suppress unrelated gene
            // death logic; skipping it for one gene type does not.
            MethodInfo target = AccessTools.Method(typeof(Gene), "Notify_PawnDied");
            if (target == null)
            {
                Log.Message("[Avir] Gene.Notify_PawnDied not found - relying on post-rebirth bond repair instead.");
                return;
            }

            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(Patch_AvirPhoenixDeath), nameof(BondPrefix))
                {
                    priority = Priority.First
                });

            Log.Message("[Avir] Phoenix: bond protection patched onto Verse.Gene.Notify_PawnDied.");
        }

        public static bool BondPrefix(Gene __instance)
        {
            try
            {
                if (__instance == null) return true;

                // Narrowed by type NAME so no compile-time reference to a Biotech class is
                // needed and a rename to something like Gene_ArcaneBonding still matches.
                string n = __instance.GetType().Name;
                if (n.IndexOf("Bond", StringComparison.OrdinalIgnoreCase) < 0) return true;

                if (AvirPhoenixUtil.UnderThePhoenix(__instance.pawn)) return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Phoenix bond prefix failed: " + e, 0x4A5210);
            }
            return true;
        }

        // ------------------------------------------------------------
        //  3. DEATH THOUGHTS
        // ------------------------------------------------------------
        private static void TryPatchDeathThoughts(Harmony harmony)
        {
            Type t = AccessTools.TypeByName("RimWorld.PawnDiedOrDownedThoughtsUtility");
            if (t == null) return;

            MethodInfo target = AccessTools.Method(t, "TryGiveThoughts",
                new Type[] { typeof(Pawn), typeof(DamageInfo?), typeof(PawnDiedOrDownedThoughtsKind) });
            if (target == null) return;

            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(Patch_AvirPhoenixDeath), nameof(DeathThoughtsPrefix))
                {
                    priority = Priority.First
                });
        }

        /// <summary>
        /// No "my friend died" spiral for something that is going to be over in three seconds.
        /// The colony gets a dedicated memory instead - Avir_PhoenixFriendBurned - which
        /// GameComponent_AvirPhoenixRebirth swaps for Avir_PhoenixFriendRose the moment she
        /// stands up.
        /// </summary>
        public static bool DeathThoughtsPrefix(Pawn victim, PawnDiedOrDownedThoughtsKind thoughtsKind)
        {
            try
            {
                if (victim == null || !AvirPhoenixUtil.UnderThePhoenix(victim)) return true;
                if (thoughtsKind != PawnDiedOrDownedThoughtsKind.Died) return true;

                List<Pawn> all = AvirPhoenixUtil.Onlookers();
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p == victim || p.needs == null || p.needs.mood == null) continue;
                    p.needs.mood.thoughts.memories.TryGainMemory(AvirPhoenixDefOf.Avir_PhoenixFriendBurned, victim);
                }
                return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Phoenix death-thought prefix failed: " + e, 0x4A5211);
                return true;
            }
        }
    }

    // ================================================================
    //  1. DEATH CAPTURE
    // ================================================================
    /// <summary>
    /// The position has to be read in a PREFIX. By the time Kill's postfix runs the pawn has
    /// been despawned into its corpse and pawn.Position is no longer meaningful, so the cell is
    /// captured on the way in and handed to the postfix through a static.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_AvirPhoenixKill
    {
        private static Map deathMap;
        private static IntVec3 deathCell;
        private static bool armed;

        public static void Prefix(Pawn __instance)
        {
            armed = false;
            if (__instance == null || __instance.Dead) return;

            // HasEssence, NOT UnderThePhoenix. THIS IS WHAT MAKES IT ONE RESURRECTION.
            //
            // UnderThePhoenix is true for the rebirth hediff as well, and that hediff sits on
            // the pawn for an hour AFTER she stands up - so every death inside that hour queued
            // another rebirth, and the pawn was effectively unkillable for as long as she kept
            // dying. Registering only on the essence means the drug is what buys the
            // resurrection, exactly once, and the fireproof hour afterwards is only fireproofing.
            //
            // The finale still works: the essence hediff never self-removes at severity zero
            // (ShouldRemove is forced false), so it is still present at the moment the pyre
            // kills her. And a corpse revival still works, because
            // CompTargetEffect_AvirPhoenixRevive registers with the component directly instead
            // of going through this patch at all.
            if (!AvirPhoenixUtil.HasEssence(__instance)) return;

            armed = true;
            deathMap = __instance.MapHeld;
            deathCell = __instance.PositionHeld;
        }

        public static void Postfix(Pawn __instance)
        {
            if (!armed) return;
            armed = false;

            try
            {
                GameComponent_AvirPhoenixRebirth comp = GameComponent_AvirPhoenixRebirth.Instance;
                if (comp == null) return;

                // The rebirth hediff is what keeps the pawn flagged from here on: the stage
                // hediff may be gone, and the corpse may not survive the wait.
                if (!AvirPhoenixUtil.HasRebirth(__instance) && __instance.health != null)
                {
                    __instance.health.AddHediff(AvirPhoenixDefOf.Avir_PhoenixRebirth);
                }

                comp.Register(__instance, deathMap, deathCell);
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Phoenix death capture failed: " + e);
            }
        }
    }

    /// <summary>
    /// Keeps the Pawn object alive through the three-second window. The corpse is allowed to be
    /// destroyed - the rebirth does not need it - but the inner Pawn must not be, or the
    /// reference the GameComponent is holding becomes a discarded thing.
    ///
    /// THE ONE WAY THE BOND STILL BREAKS, confirmed in play: annihilate the body outright. If
    /// something destroys the Pawn object itself rather than the corpse around it, there is
    /// nothing left to resurrect and the bond goes with it. That is the correct outcome and it
    /// is deliberately not defended against - a Phoenix can rise from ashes, but there have to
    /// be ashes. The guard below only covers the ordinary case where the corpse is cleaned up
    /// or burned in the seconds before the timer fires.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
    public static class Patch_AvirPhoenixDestroyGuard
    {
        public static bool Prefix(Pawn __instance)
        {
            try
            {
                GameComponent_AvirPhoenixRebirth comp = GameComponent_AvirPhoenixRebirth.Instance;
                if (comp != null && comp.IsPending(__instance)) return false;
            }
            catch { }
            return true;
        }
    }

    // ================================================================
    //  4. FIRE IMMUNITY
    // ================================================================
    /// <summary>
    /// TryAttachFire is an EXTENSION METHOD on FireUtility, not an instance method on Thing.
    /// Targeting it as typeof(Thing) with nameof(Thing.TryAttachFire) is a compile error -
    /// CS8093, "extension method groups are not allowed as an argument to nameof". The patch
    /// has to name the declaring type, and the instance arrives as the first parameter (t)
    /// rather than as __instance.
    /// </summary>
    [HarmonyPatch(typeof(FireUtility), "TryAttachFire")]
    public static class Patch_AvirPhoenixNoFire
    {
        public static bool Prefix(Thing t)
        {
            Pawn p = t as Pawn;
            return p == null || !AvirPhoenixUtil.HasRebirth(p);
        }
    }

    [HarmonyPatch(typeof(Pawn), "PreApplyDamage")]
    public static class Patch_AvirPhoenixNoBurn
    {
        public static bool Prefix(Pawn __instance, ref DamageInfo dinfo, ref bool absorbed)
        {
            if (__instance == null || !AvirPhoenixUtil.HasRebirth(__instance)) return true;

            DamageDef d = dinfo.Def;
            if (d == DamageDefOf.Flame || d == DamageDefOf.Burn)
            {
                absorbed = true;
                return false;
            }
            return true;
        }
    }

    // ================================================================
    //  5. CONSCIOUSNESS FLOOR
    // ================================================================
    /// <summary>
    /// The Judgement and the Immolation would otherwise put the pawn straight into pain shock,
    /// and an unconscious pawn cannot experience the -56 mood the whole stage exists to deliver.
    /// Consciousness is held at 100% for the entire duration - not just the last two stages -
    /// because a floor that switches on partway down would make the pawn collapse and then
    /// stand back up for no visible reason.
    /// </summary>
    [HarmonyPatch(typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.GetLevel))]
    public static class Patch_AvirPhoenixConsciousness
    {
        private static AccessTools.FieldRef<PawnCapacitiesHandler, Pawn> pawnRef;
        private static bool resolved;

        public static void Postfix(PawnCapacitiesHandler __instance, PawnCapacityDef capacity, ref float __result)
        {
            if (capacity != PawnCapacityDefOf.Consciousness) return;
            if (__result >= 1f) return;

            if (!resolved)
            {
                resolved = true;
                try { pawnRef = AccessTools.FieldRefAccess<PawnCapacitiesHandler, Pawn>("pawn"); }
                catch (Exception e) { Log.Warning("[Avir] Phoenix consciousness floor disabled: " + e.Message); }
            }
            if (pawnRef == null) return;

            Pawn p;
            try { p = pawnRef(__instance); }
            catch { return; }

            if (AvirPhoenixUtil.UnderThePhoenix(p)) __result = Mathf.Max(__result, 1f);
        }
    }
}
