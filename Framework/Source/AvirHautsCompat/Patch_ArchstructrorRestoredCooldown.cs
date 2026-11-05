using RimWorld;
using UnityEngine;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Same technique as Avir.PsychicNaturePatches.CooldownPostfix, and for the same reason
    /// stated in that file's own comment: there is no working pawn-level cooldown stat to hang
    /// a statFactor on, so this is a Harmony postfix on VerbProperties.AdjustedCooldownTicks
    /// instead. Registered manually (string-based AccessTools lookup) from
    /// AvirHautsCompatHarmony.cs, exactly like the root mod registers the Psychic Nature one -
    /// NOT attribute-based, since PsychicNaturePatches already proved this specific method
    /// needs the flexible object[] arg-scan rather than named parameters.
    ///
    /// Unlike the melee-only Psychic Nature version, this one does NOT filter to
    /// IsMeleeAttack - "lowered cooldown for both melee and ranged" from the spec applies
    /// unconditionally to any verb cooldown this method governs, for any pawn carrying
    /// Avir_ArchstructrorRestored.
    /// </summary>
    public static class ArchstructrorRestoredPatches
    {
        private const float CooldownFactor = 0.75f;

        private static HediffDef restoredDef;
        private static bool lookedUp;

        public static void CooldownPostfix(VerbProperties __instance, ref int __result, object[] __args)
        {
            if (__instance == null || __result <= 0) return;

            Pawn pawn = FindPawn(__args);
            if (pawn == null || pawn.health?.hediffSet == null) return;

            EnsureLookedUp();
            if (restoredDef == null) return;

            if (!pawn.health.hediffSet.HasHediff(restoredDef)) return;

            __result = Mathf.Max(1, Mathf.RoundToInt(__result * CooldownFactor));
        }

        private static Pawn FindPawn(object[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is Pawn p) return p;
            }
            return null;
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            restoredDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ArchstructrorRestored");
        }
    }
}
