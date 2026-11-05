using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>The Free Nesting is pure Avir: a grown member of it is never generated as a harpy (the race's
    /// xenotype list allows harpies, and Humanoid Alien Races could pick one, e.g. for the starting colonists).
    /// Harpies born into the colony are untouched - those come from their mothers.</summary>
    [HarmonyPatch(typeof(PawnGenerator), "GenerateGenes")]
    public static class Patch_FreeNestingPureAvir
    {
        public static void Postfix(Pawn pawn, PawnGenerationRequest request)
        {
            if (pawn?.genes == null || !AvirHarpyRules.IsHarpy(pawn.genes.Xenotype)) return;
            FactionDef f = request.Faction?.def ?? pawn.Faction?.def;
            if (f == null || f.defName != "Avir_FreeNesting") return;
            if ((request.AllowedDevelopmentalStages & DevelopmentalStage.Newborn) != 0) return;
            XenotypeDef avir = DefDatabase<XenotypeDef>.GetNamedSilentFail("Avir");
            if (avir == null) return;
            // swap the harpy genome for the Avir one; the hair and skin colours rolled for the pawn stay
            var harpy = new HashSet<GeneDef>();
            foreach (XenotypeDef x in DefDatabase<XenotypeDef>.AllDefs)
                if (AvirHarpyRules.IsHarpy(x)) harpy.AddRange(x.genes);
            foreach (Gene g in new List<Gene>(pawn.genes.Endogenes))
                if (harpy.Contains(g.def) && !avir.genes.Contains(g.def)) pawn.genes.RemoveGene(g);
            foreach (GeneDef d in avir.genes)
                if (!pawn.genes.HasEndogene(d)) pawn.genes.AddGene(d, false);
            pawn.genes.SetXenotypeDirect(avir);
        }
    }

    public static class AvirHarpyRules
    {
        public static bool IsHarpy(XenotypeDef x) => x != null && (x.defName == "Avir_HarpyWild" || x.defName == "Avir_HarpyCalm");
    }

    /// <summary>Harpies have wings and a tail but not the Avir coat (feathered fur, griffin head). Harpies already
    /// in a save got the gene before it was taken out of their xenotypes; on load it is removed from them.</summary>
    public class GameComponent_AvirHarpyCoat : GameComponent
    {
        public GameComponent_AvirHarpyCoat(Game game) { }

        public override void FinalizeInit()
        {
            GeneDef fur = DefDatabase<GeneDef>.GetNamedSilentFail("Avir_FeatheredFur");
            if (fur == null) return;
            int n = 0;
            foreach (Pawn p in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
            {
                if (p?.genes == null || !AvirHarpyRules.IsHarpy(p.genes.Xenotype)) continue;
                foreach (Gene g in new List<Gene>(p.genes.Endogenes))
                    if (g.def == fur) { p.genes.RemoveGene(g); n++; }
            }
            if (n > 0) Log.Message("[Avir] took the Avir coat off " + n + " harpies (it was never theirs).");
        }
    }
}
