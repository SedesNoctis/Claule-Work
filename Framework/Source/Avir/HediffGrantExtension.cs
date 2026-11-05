using Verse;

namespace Avir
{
    /// <summary>
    /// Attach to a GeneDef (via modExtensions) to make Gene_GrantHediff grant and keep
    /// the named hediff while the gene is present.
    /// </summary>
    public class HediffGrantExtension : DefModExtension
    {
        public HediffDef hediffDef;
    }
}
