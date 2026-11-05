using System;
using System.Reflection;
using Anthrosonae;
using HarmonyLib;
using Verse;

namespace AvirColors
{
    /// <summary>
    /// CHARACTER EDITOR + ANTHROSONAE FUR COLOR PRESERVATION.
    ///
    /// When CE loads a preset it re-adds genes, firing FurGene.PostAdd() which
    /// does a random color roll. CE then restores the saved colorTwo directly
    /// onto the gene but never calls ApplyColors(), so the renderer keeps
    /// showing the random color.
    ///
    /// Fix: a GameComponent whose FinalizeInit() applies the CE hooks. This
    /// fires reliably after every mod assembly is loaded — unlike static ctors
    /// or Game method patches which can fire before CE is in the AppDomain.
    /// </summary>
    public class GameComponent_CEFurFix : GameComponent
    {
        private static bool applied;

        public GameComponent_CEFurFix(Game game) { }

        public override void FinalizeInit()
        {
            if (applied) return;
            applied = true;

            if (!ModsConfig.IsActive("void.charactereditor")) return;

            try
            {
                Assembly ceAsm = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "CharacterEditor") { ceAsm = a; break; }

                if (ceAsm == null)
                {
                    Log.Warning("[AvirGriffinColors] CE assembly not found in FinalizeInit.");
                    return;
                }

                var h = new Harmony("arno.avirgriffincolors.ce.hooks");
                int count = 0;

                // CEditor.OnPostAddGene fires after CE restores a gene's saved
                // data (colorTwo etc.) from a preset slot.
                Type cEditorType = ceAsm.GetType("CharacterEditor.CEditor");
                if (cEditorType != null)
                {
                    MethodInfo opag = AccessTools.Method(cEditorType, "OnPostAddGene");
                    if (opag != null)
                    {
                        h.Patch(opag, postfix: new HarmonyMethod(
                            typeof(GameComponent_CEFurFix), nameof(OnPostAddGene_Postfix)));
                        count++;
                    }
                }

                // PresetGene.LoadAllModifications is the other candidate.
                Type presetGeneType = ceAsm.GetType("CharacterEditor.PresetGene");
                if (presetGeneType != null)
                {
                    MethodInfo lam = AccessTools.Method(presetGeneType, "LoadAllModifications");
                    if (lam != null)
                    {
                        h.Patch(lam, postfix: new HarmonyMethod(
                            typeof(GameComponent_CEFurFix), nameof(LoadAllModifications_Postfix)));
                        count++;
                    }
                }

                Log.Message($"[AvirGriffinColors] CE fur color fix active ({count} hook(s)).");
            }
            catch (Exception e)
            {
                Log.Warning("[AvirGriffinColors] CE fur fix FinalizeInit failed: " + e.Message);
            }
        }

        // ---- hooks ----

        public static void OnPostAddGene_Postfix(object __instance)
        {
            try
            {
                // Get pawn from CEditor.Pawn property.
                MethodInfo getPawn = AccessTools.Method(__instance.GetType(), "get_Pawn");
                Pawn pawn = getPawn?.Invoke(__instance, null) as Pawn;
                ApplyFurColors(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirGriffinColors] OnPostAddGene: " + e.Message, 0xAC51E5);
            }
        }

        public static void LoadAllModifications_Postfix(object __0)
        {
            try
            {
                // __0 is the Gene being loaded. If it's a FurGene, apply directly.
                if (__0 is FurGene fg) { fg.ApplyColors(); return; }
                // Fallback: selected pawn.
                ApplyFurColors(Find.Selector?.SingleSelectedThing as Pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirGriffinColors] LoadAllModifications: " + e.Message, 0xAC51E4);
            }
        }

        private static void ApplyFurColors(Pawn pawn)
        {
            if (pawn?.genes == null) return;
            foreach (Gene g in pawn.genes.GenesListForReading)
                if (g is FurGene fg) fg.ApplyColors();
        }
    }
}
