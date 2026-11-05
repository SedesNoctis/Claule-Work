using Verse;

namespace AvirColors
{
    /// <summary>
    /// Extends Anthrosonae's FurColorDef with a gender restriction.
    /// Gender.None means "available to any gender" (the default).
    /// This field is only understood by our own Harmony patch
    /// (FurGene_SetFurColor_Patch) - vanilla Anthrosonae code has no
    /// concept of gender-restricted presets.
    /// </summary>
    public class GriffinFurColorDef : Anthrosonae.FurColorDef
    {
        public Gender allowedGender = Gender.None;
    }
}
