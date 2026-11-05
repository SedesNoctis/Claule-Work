using System.Linq;
using Verse;

namespace AvirColors
{
    [StaticConstructorOnStartup]
    public static class DiagnosticDump
    {
        static DiagnosticDump()
        {
            var geneDef = DefDatabase<GeneDef>.GetNamedSilentFail("ATK_AnthrogriffFur");
            if (geneDef == null)
            {
                Log.Warning("[AvirGriffinColors] DIAGNOSTIC: GeneDef 'ATK_AnthrogriffFur' not found at all.");
                return;
            }

            var extension = geneDef.GetModExtension<Anthrosonae.FurColors>();
            if (extension?.allowedFurColors == null)
            {
                Log.Warning("[AvirGriffinColors] DIAGNOSTIC: ATK_AnthrogriffFur has no Anthrosonae.FurColors modExtension (or allowedFurColors is null).");
                return;
            }

            var names = extension.allowedFurColors.Select(c => $"{c.defName}({c.GetType().Name})").ToList();
            Log.Warning($"[AvirGriffinColors] DIAGNOSTIC: ATK_AnthrogriffFur.allowedFurColors ({names.Count} entries): {string.Join(", ", names)}");
        }
    }
}
