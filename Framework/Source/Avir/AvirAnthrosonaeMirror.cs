using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Inherits Anthrosonae's anthrogriff artwork, retextures included. A copy of the Kharassian
    /// AnthrosonaeMirror (which does the same for the anthrodrake) - griffin instead of drake, and
    /// the ears added, since the Avir have griffin ears and the Kharassian have none. The wings are
    /// deliberately NOT mirrored: the Avir wings are their own (Flight_Gene, AvirWings.cs).
    ///
    /// The old approach was an XML patch that wrote Anthrosonae's texture paths into our defs.
    /// That inherits their ORIGINAL art and nothing else: a retexture pack does not overwrite
    /// files, it patches the Anthrosonae defs to point somewhere else entirely - its own folder.
    /// Hardcoding the vanilla-Anthrosonae paths therefore misses every retexture, and if a pack
    /// swaps the drake body for something closer to a human torso, the result is the odd mix of
    /// old and new art that showed up in game.
    ///
    /// This reads the paths out of Anthrosonae's live defs at startup instead, after every XML
    /// patch in the load order has had its say. Whatever a retexture pack put in their defs is
    /// what we copy - one pack or five, whichever won, we follow it.
    ///
    /// Does nothing at all when Anthrosonae is not loaded: the mod keeps its own files.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirAnthrosonaeMirror
    {
        private const string SourceFur = "ATK_Anthrogriff";
        private const string SourceHeadMale = "ATK_Anthrogriff_Average_Male";
        private const string SourceHeadFemale = "ATK_Anthrogriff_Average_Female";
        private const string SourceTailGene = "ATK_AnthrogriffTail";
        private const string SourceEarsGene = "ATK_AnthrogriffEars";

        private const string OurFur = "Avir_GriffinFeathers";
        private const string OurHeadMale = "Avir_Griffin_Head_Male";
        private const string OurHeadFemale = "Avir_Griffin_Head_Female";
        private const string OurTailGene = "Avir_GriffinTail";
        private const string OurEarsGene = "Avir_GriffinEars";

        static AvirAnthrosonaeMirror()
        {
            try
            {
                if (!Mirror())
                {
                    return;
                }

                // Graphics resolved before this point hold the old paths.
                GlobalTextureAtlasManager.rebakeAtlas = true;
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] could not mirror Anthrosonae textures, keeping our own: " + e);
            }
        }

        private static bool Mirror()
        {
            FurDef source = DefDatabase<FurDef>.GetNamedSilentFail(SourceFur);
            FurDef target = DefDatabase<FurDef>.GetNamedSilentFail(OurFur);

            if (source == null || target == null)
            {
                // Anthrosonae absent: our own textures stand, nothing to do and nothing to warn about.
                return false;
            }

            bool any = false;

            // Bodies. The whole table is taken as one, so a pack that retextures only some body
            // types still leaves a consistent set behind.
            FieldInfo paths = AccessTools.Field(typeof(FurDef), "bodyTypeGraphicPaths");
            if (paths != null)
            {
                object value = paths.GetValue(source);
                if (value != null)
                {
                    paths.SetValue(target, value);
                    any = true;
                }
            }

            // Heads.
            any |= MirrorHead(SourceHeadMale, OurHeadMale);
            any |= MirrorHead(SourceHeadFemale, OurHeadFemale);

            // Tail and ears.
            // the tail is our own art now (and a Herald wears two of it): it is no longer mirrored
            // any |= MirrorAttachment(SourceTailGene, OurTailGene);
            any |= MirrorAttachment(SourceEarsGene, OurEarsGene);

            if (any)
            {
                Log.Message("[Avir] Anthrosonae found; mirroring its anthrogriff textures, "
                    + "including whatever retextures are patched into them.");
            }

            return any;
        }

        private static bool MirrorHead(string sourceName, string targetName)
        {
            HeadTypeDef source = DefDatabase<HeadTypeDef>.GetNamedSilentFail(sourceName);
            HeadTypeDef target = DefDatabase<HeadTypeDef>.GetNamedSilentFail(targetName);

            if (source == null || target == null || source.graphicPath.NullOrEmpty())
            {
                return false;
            }

            target.graphicPath = source.graphicPath;
            return true;
        }

        private static bool MirrorAttachment(string sourceName, string targetName)
        {
            GeneDef source = DefDatabase<GeneDef>.GetNamedSilentFail(sourceName);
            GeneDef target = DefDatabase<GeneDef>.GetNamedSilentFail(targetName);

            if (source?.renderNodeProperties == null || target?.renderNodeProperties == null)
            {
                return false;
            }
            if (source.renderNodeProperties.Count == 0 || target.renderNodeProperties.Count == 0)
            {
                return false;
            }

            string texPath = source.renderNodeProperties[0].texPath;
            if (texPath.NullOrEmpty())
            {
                return false;
            }

            List<PawnRenderNodeProperties> nodes = target.renderNodeProperties;
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].texPath = texPath;
            }
            return true;
        }
    }
}
