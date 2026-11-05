using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>One change to a xenotype's gene list when Gendered Genes 2 is loaded.</summary>
    public class GenderedGeneSwap
    {
        /// <summary>Gene to take out. Empty = this is an addition.</summary>
        public string from;
        /// <summary>Gene to put in (a "_GG2_MaleOnly" / "_GG2_FemaleOnly" one).</summary>
        public string to;
    }

    /// <summary>On a XenotypeDef: the gene changes to make when Gendered Genes 2 is loaded.</summary>
    public class GenderedGenesSwaps : DefModExtension
    {
        public List<GenderedGeneSwap> swaps = new List<GenderedGeneSwap>();
    }

    /// <summary>
    /// SOFT COMPATIBILITY WITH GENDERED GENES 2, the same way as with Anthrosonae: no dependency,
    /// nothing changes without it.
    ///
    /// Gendered Genes makes its gendered copies of genes ("Aggression_Aggressive_GG2_MaleOnly") in
    /// code at startup. XML is read before that, so a XenotypeDef cannot name them. Our xenotypes
    /// list the ungendered genes instead, plus a GenderedGenesSwaps extension, and once loading has
    /// finished - after every mod's startup code, Gendered Genes included - this swaps them in.
    /// A swap whose target gene does not exist (Gendered Genes, or the mod behind the gene, not
    /// loaded) is skipped and the original gene stays.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirGenderedGenes
    {
        static AvirGenderedGenes()
        {
            LongEventHandler.ExecuteWhenFinished(Apply);
        }

        private static void Apply()
        {
            try
            {
                foreach (XenotypeDef xeno in DefDatabase<XenotypeDef>.AllDefsListForReading)
                {
                    GenderedGenesSwaps ext = xeno.GetModExtension<GenderedGenesSwaps>();
                    if (ext?.swaps == null || xeno.genes == null)
                    {
                        continue;
                    }

                    int applied = 0;
                    foreach (GenderedGeneSwap swap in ext.swaps)
                    {
                        GeneDef to = swap?.to.NullOrEmpty() == false ? DefDatabase<GeneDef>.GetNamedSilentFail(swap.to) : null;
                        if (to == null || xeno.genes.Contains(to))
                        {
                            continue;
                        }
                        if (!swap.from.NullOrEmpty())
                        {
                            int i = xeno.genes.FindIndex(g => g != null && g.defName == swap.from);
                            if (i < 0)
                            {
                                continue;
                            }
                            xeno.genes[i] = to;
                        }
                        else
                        {
                            xeno.genes.Add(to);
                        }
                        applied++;
                    }

                    if (applied > 0)
                    {
                        Log.Message("[Avir] Gendered Genes found: " + applied + " gendered gene(s) applied to the " + xeno.label + " xenotype.");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Could not apply gendered genes to xenotypes: " + e);
            }
        }
    }
}
