using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Same fix CompAbilityEffect_Combustion already applies to blood filth (see
    /// Source/AvirHautsCompat/CompAbilityEffect_Combustion.cs's BoostBloodFlammability) - most
    /// filth has low/no Flammability by default, so fire "catches" on it technically and then
    /// dies instantly with nothing to actually burn. Reusing vanilla's real Filth_OilSmear
    /// instead of inventing a new custom Filth ThingDef (lower risk of a malformed def), boosted
    /// once at load to be genuinely flammable, then spawned at every Purify blast point so fire
    /// has real fuel to catch and spread through rather than fizzling on scorched ground.
    /// </summary>
    public static class AvirOilFuelUtility
    {
        private const float MinFlammability = 2.0f;

        private static bool boosted;
        private static ThingDef oilSmearDef;

        private static void EnsureBoosted()
        {
            if (boosted)
            {
                return;
            }

            boosted = true;
            oilSmearDef = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_OilSmear");
            if (oilSmearDef == null)
            {
                return;
            }

            StatDef flammabilityStat = DefDatabase<StatDef>.GetNamedSilentFail("Flammability");
            if (flammabilityStat == null)
            {
                return;
            }

            if (oilSmearDef.statBases == null)
            {
                oilSmearDef.statBases = new List<StatModifier>();
            }

            StatModifier existing = oilSmearDef.statBases.Find(s => s.stat == flammabilityStat);
            if (existing != null)
            {
                if (existing.value < MinFlammability)
                {
                    existing.value = MinFlammability;
                }
            }
            else
            {
                oilSmearDef.statBases.Add(new StatModifier { stat = flammabilityStat, value = MinFlammability });
            }
        }

        /// <summary>Lays down oil-smear fuel at "cell" so fire placed there has something
        /// genuine to burn, instead of relying only on whatever was already there.</summary>
        public static void SpawnFuelAt(Map map, IntVec3 cell)
        {
            EnsureBoosted();
            if (oilSmearDef == null || map == null || !cell.InBounds(map))
            {
                return;
            }

            FilthMaker.TryMakeFilth(cell, map, oilSmearDef, 1);
        }
    }
}
