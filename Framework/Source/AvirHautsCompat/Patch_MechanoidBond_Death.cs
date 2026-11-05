using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Same split-prefix/postfix shape as Patch_CrowDeath_Effects, and for the same reason:
    /// the mechanoid-restore case needs the death position/corpse captured BEFORE Kill()
    /// finishes and the map potentially gets torn down around it, while the actual
    /// resurrection has to happen AFTER Kill() so there's something to resurrect.
    ///
    /// Two independent triggers on this one method:
    ///   - The bonded MECHANOID dies (has Avir_MechanoidLink) -> burn the victim, restore the
    ///     mechanoid at the death location, buff it with Avir_ArchstructrorRestored. This is
    ///     the "again and again" loop from the spec - nothing here prevents it firing on the
    ///     very next death of the same mechanoid, by design.
    ///   - The marked VICTIM dies (has Avir_Soulmarked) -> the bond ends outright. No burn, no
    ///     restore - "if not saved marked target dies and hediff disappears from both."
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class Patch_MechanoidBond_Death
    {
        private class MechDeathState
        {
            public Pawn victim;
            public Pawn architect;
            public IntVec3 position;
            public Map map;
            public Corpse heldCorpse;
        }

        private class PatchState
        {
            public MechDeathState mechState;
            public bool victimDied;
        }

        public static void Prefix(Pawn __instance, out object __state)
        {
            __state = null;

            try
            {
                if (__instance == null || __instance.Dead) return;

                PatchState state = new PatchState();

                state.mechState = CaptureMechDeathState(__instance);
                state.victimDied = HandleVictimDeath(__instance);

                if (state.mechState != null || state.victimDied) __state = state;
            }
            catch (Exception e)
            {
                Log.Warning("[AvirHautsCompat] Patch_MechanoidBond_Death prefix failed: " + e);
            }
        }

        public static void Postfix(Pawn __instance, object __state)
        {
            try
            {
                if (!(__state is PatchState state)) return;
                if (state.mechState == null) return;

                RestoreMechanoid(__instance, state.mechState);
            }
            catch (Exception e)
            {
                Log.Warning("[AvirHautsCompat] Patch_MechanoidBond_Death postfix failed: " + e);
            }
        }

        // Captures everything needed to restore this mechanoid AFTER Kill() runs. Also
        // despawns+holds the corpse if the map is about to be at risk of cleanup, same
        // cross-map fix as Patch_CrowDeath_Effects.
        private static MechDeathState CaptureMechDeathState(Pawn mech)
        {
            HediffDef linkDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_MechanoidLink");
            if (linkDef == null) return null;

            Hediff h = mech.health?.hediffSet?.GetFirstHediffOfDef(linkDef);
            HediffComp_MechanoidLink comp = h?.TryGetComp<HediffComp_MechanoidLink>();
            if (comp == null) return null; // not a soulmark-bound mechanoid - normal, not an error

            Pawn victim = comp.victim;
            Pawn architect = comp.architect;

            if (victim == null || victim.Dead || victim.Destroyed)
            {
                // The victim is already gone - nothing left to pay the cost, so this
                // mechanoid just stays dead. Sever the (already one-sided) link so it doesn't
                // linger on a corpse.
                return null;
            }

            return new MechDeathState
            {
                victim = victim,
                architect = architect,
                position = mech.PositionHeld,
                map = mech.MapHeld
            };
        }

        private static void RestoreMechanoid(Pawn mech, MechDeathState state)
        {
            HediffDef linkDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_MechanoidLink");

            // Cross-map fix, same reasoning as Patch_CrowDeath_Effects: if this mechanoid died
            // on a map with no colonists left, its corpse is a cleanup risk before we get a
            // chance to restore it. Hold it directly.
            Corpse corpse = mech.Corpse;
            if (corpse != null && corpse.Map != null)
            {
                bool anyColonistsHere = corpse.Map.mapPawns.FreeColonistsSpawnedCount > 0 || corpse.Map.mapPawns.ColonistsSpawnedCount > 0;
                if (!anyColonistsHere && corpse.Spawned)
                {
                    corpse.DeSpawn(DestroyMode.Vanish);
                    state.heldCorpse = corpse;
                }
            }

            ResurrectionEffectUtility.PerformResurrection(
                mech, state.position, state.map, state.heldCorpse,
                watchedOverHediff: null, comaHediff: null, restoredThought: null);

            if (mech.Dead)
            {
                Log.Message("[AvirHautsCompat] Soulmark: restoration failed for " + mech.LabelShortCap + " (no usable corpse) - bond ends without cost to the victim.");
                SeverVictimSide(state.victim, mech);
                return;
            }

            ApplyRestoredBuff(mech);

            // Make sure the mechanoid's link hediff survived the resurrection's body-cleanup
            // pass (it strips Hediff_Injury/MissingPart/AddedPart, not HediffWithComps like
            // this one, but re-check defensively rather than assume). Deliberately done BEFORE
            // PayVictimCost: that call can itself kill the victim (real burn damage, not a
            // slow illness), which would reentrantly call Pawn.Kill on the victim and sever
            // this exact link via HediffComp_Soulmarked.Sever - if this re-add ran AFTER that,
            // it would wrongly resurrect a link that was just correctly torn down.
            if (linkDef != null && mech.health?.hediffSet != null && !mech.health.hediffSet.HasHediff(linkDef))
            {
                Hediff linkHediff = HediffMaker.MakeHediff(linkDef, mech);
                mech.health.AddHediff(linkHediff);
                HediffComp_MechanoidLink linkComp = linkHediff.TryGetComp<HediffComp_MechanoidLink>();
                if (linkComp != null)
                {
                    linkComp.victim = state.victim;
                    linkComp.architect = state.architect;
                }
            }

            // Restoration succeeded - this is the moment the cost is actually paid. If this
            // kills the victim outright, the bond is severed reentrantly (see comment above) -
            // that's correct and intentional, not a bug to guard against here.
            PayVictimCost(state.victim, state.architect);
        }

        private static void ApplyRestoredBuff(Pawn mech)
        {
            HediffDef restoredDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ArchstructrorRestored");
            if (restoredDef == null || mech.health == null) return;

            Hediff existing = mech.health.hediffSet.GetFirstHediffOfDef(restoredDef);
            if (existing != null) mech.health.RemoveHediff(existing); // refresh rather than stack

            Hediff buff = HediffMaker.MakeHediff(restoredDef, mech);
            mech.health.AddHediff(buff);
        }

        // The actual cost: the victim genuinely catches fire (not three discrete burn wounds -
        // an attached Fire, the same mechanism vanilla incendiary weapons use, so this actually
        // burns over time rather than being three static hits that leave scars and stop), plus a
        // stacking Burning Soul memory, plus the brief stage-2 flash on Avir_Soulmarked (reset
        // back to dormant right after, so it can trigger again next time).
        private const float VictimFireSize = 1.0f;

        private static void PayVictimCost(Pawn victim, Pawn architect)
        {
            if (victim == null || victim.Dead || victim.health == null) return;

            HediffDef soulmarkedDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Soulmarked");
            ThoughtDef burningSoulDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_BurningSoul");

            // Genuinely on fire, not three discrete Burn-type hits - FireUtility.TryAttachFire is
            // the same call vanilla incendiary damage uses to set a pawn ablaze. Not flagged as
            // an "attack" of its own - no one aimed this at them, and no social/guilt fallout
            // should follow from it (the fire's own subsequent damage ticks are ordinary burn
            // damage from the attached Fire, same as walking into any other fire).
            FireUtility.TryAttachFire(victim, VictimFireSize, null);

            if (soulmarkedDef != null && !victim.Dead)
            {
                Hediff markHediff = victim.health.hediffSet.GetFirstHediffOfDef(soulmarkedDef);
                if (markHediff != null)
                {
                    markHediff.Severity = 1f; // stage 1 "scorched" - the visible flash
                    markHediff.Severity = 0f; // back to dormant, ready to trigger again
                }
            }

            if (burningSoulDef != null)
            {
                victim.needs?.mood?.thoughts?.memories?.TryGainMemory(burningSoulDef);
            }

            Messages.Message(victim.LabelShortCap + "'s soulmark burns - something had to pay for what just came back.", victim, MessageTypeDefOf.NegativeEvent, false);
        }

        private static void SeverVictimSide(Pawn victim, Pawn mech)
        {
            if (victim == null) return;

            HediffDef soulmarkedDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Soulmarked");
            Hediff h = soulmarkedDef != null ? victim.health?.hediffSet?.GetFirstHediffOfDef(soulmarkedDef) : null;
            HediffComp_Soulmarked comp = h?.TryGetComp<HediffComp_Soulmarked>();
            comp?.Sever(removeSelf: true);
        }

        // THE OTHER TRIGGER: the marked victim dies. Bond ends outright, no burn, no restore.
        private static bool HandleVictimDeath(Pawn pawn)
        {
            HediffDef soulmarkedDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Soulmarked");
            if (soulmarkedDef == null) return false;

            Hediff h = pawn.health?.hediffSet?.GetFirstHediffOfDef(soulmarkedDef);
            if (h == null) return false; // not soulmarked - normal, not an error

            HediffComp_Soulmarked comp = h.TryGetComp<HediffComp_Soulmarked>();
            comp?.Sever(removeSelf: true);
            return true;
        }
    }
}
