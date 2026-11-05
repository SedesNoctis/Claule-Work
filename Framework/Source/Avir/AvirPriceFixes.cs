using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Prices the Avir set that something else rewrote after the defs were read (another mod's patch or price pass:
    /// Avir medicine kept showing its old value). Set once every mod has loaded; the log says what it found.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirPriceFixes
    {
        static AvirPriceFixes()
        {
            Ensure("Avir_Medicine", 70f);
        }

        private static void Ensure(string defName, float value)
        {
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (d == null) return;
            float was = d.GetStatValueAbstract(StatDefOf.MarketValue);
            if (UnityEngine.Mathf.Abs(was - value) < 0.5f) return;
            d.SetStatBaseValue(StatDefOf.MarketValue, value);
            Log.Message("[Avir] " + defName + " was priced " + was.ToString("0.#") + " after loading (another mod?); set back to " + value.ToString("0") + ".");
        }
    }
}
