using System;
using System.Reflection;
using Verse;

namespace Avir
{
    /// <summary>
    /// Mirrors Avir_Combustion's own CE-only "soak everyone nearby in PrometheumSoaked" effect -
    /// but UNVERIFIED. Combustion's own implementation lives in AvirHautsCompat.dll, which I
    /// could inspect directly to confirm its exact CE type/method names. I don't have Combat
    /// Extended's own compiled assembly available to check against here, so this is a best-guess
    /// reflection lookup at CE's commonly-documented hediff for this effect
    /// (CombatExtended.HediffDefOf / a hediff typically named "PrometheumSoaked" or similar).
    ///
    /// If the guessed type/method names are wrong, this silently no-ops (same safe-fallback
    /// behavior Combustion's own code uses when CE isn't installed at all) rather than crashing -
    /// but that also means it could be silently doing nothing even WITH CE installed, if my guess
    /// is off. Worth confirming against a real CE install/decompile before relying on it.
    /// </summary>
    public static class AvirCombatExtendedCompat
    {
        private const float SoakSeverity = 0.7f; // matches Avir_Combustion's own soakSeverity

        private static bool checkedForCE;
        private static Type ceHediffDefOfType;
        private static HediffDef prometheumSoakedHediff;

        public static void TryApplyPrometheumSoak(Map map, IntVec3 center, float radius)
        {
            if (map == null)
            {
                return;
            }

            EnsureLookup();
            if (prometheumSoakedHediff == null)
            {
                return; // CE not present, or the lookup guess didn't resolve - safe no-op either way.
            }

            float radiusSq = radius * radius;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn?.health?.hediffSet == null)
                {
                    continue;
                }

                if ((pawn.Position - center).LengthHorizontalSquared > radiusSq)
                {
                    continue;
                }

                try
                {
                    Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(prometheumSoakedHediff);
                    if (existing != null)
                    {
                        existing.Severity = Math.Max(existing.Severity, SoakSeverity);
                    }
                    else
                    {
                        Hediff hediff = HediffMaker.MakeHediff(prometheumSoakedHediff, pawn);
                        hediff.Severity = SoakSeverity;
                        pawn.health.AddHediff(hediff);
                    }
                }
                catch (Exception)
                {
                    // Any mismatch between this guessed hediff and CE's actual expectations for it
                    // (e.g. required comps CE assumes are already there) - fail safe, skip this pawn.
                }
            }
        }

        private static void EnsureLookup()
        {
            if (checkedForCE)
            {
                return;
            }

            checkedForCE = true;

            try
            {
                ceHediffDefOfType = AccessTools_TypeByName("CombatExtended.HediffDefOf");
                if (ceHediffDefOfType == null)
                {
                    return; // CE not installed.
                }

                FieldInfo field = ceHediffDefOfType.GetField("PrometheumSoaked", BindingFlags.Public | BindingFlags.Static);
                prometheumSoakedHediff = field?.GetValue(null) as HediffDef;

                if (prometheumSoakedHediff == null)
                {
                    // Field name guessed wrong, or CE's field is genuinely named something else -
                    // fall back to a direct def-database lookup by the same guessed name.
                    prometheumSoakedHediff = DefDatabase<HediffDef>.GetNamedSilentFail("PrometheumSoaked");
                }
            }
            catch (Exception)
            {
                prometheumSoakedHediff = null;
            }
        }

        private static Type AccessTools_TypeByName(string name)
        {
            // Small local helper so this file doesn't need a HarmonyLib dependency just for one
            // type lookup - equivalent to HarmonyLib.AccessTools.TypeByName.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name, throwOnError: false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
