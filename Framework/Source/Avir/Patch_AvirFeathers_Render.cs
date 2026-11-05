using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    // Render hooks for the feather colour system - a copy of the Kharassian scale render hooks
    // (Patch_KharassianScales_Render.cs) with the names changed. Every one bails out unless the
    // pawn actually carries Gene_AvirFeathers, so Anthrosonae pawns, Kharassian and everything
    // else are untouched and all three systems can run at once.
    //
    // Three node kinds have to be caught separately because RimWorld builds them from different
    // classes: the body fur node, the head node, and generic attachment nodes (ears, tail).
    //
    // NO [HarmonyPatch] ATTRIBUTES. AvirHarmony.PatchAll stops at the first patch that throws and
    // silently skips everything after it in the walk; the Kharassian lost a gizmo that way. These
    // are registered one at a time, each in its own try/catch, by AvirFeatherHarmony below.

    public static class Patch_Feathers_FurGraphic
    {
        public static void Postfix(PawnRenderNode_Fur __instance, Pawn pawn, ref Graphic __result)
        {
            if (__result == null || pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Dessicated)
            {
                return;
            }
            if (__instance.gene is Gene_AvirFeathers gene)
            {
                __result = gene.GetGraphicOverriden(__result, __instance, pawn);
            }
        }
    }

    public static class Patch_Feathers_HeadGraphic
    {
        public static void Postfix(PawnRenderNode_Head __instance, Pawn pawn, ref Graphic __result)
        {
            if (__result == null || pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Dessicated)
            {
                return;
            }
            Gene_AvirFeathers gene = Gene_AvirFeathers.For(pawn);
            if (gene != null)
            {
                __result = gene.GetGraphicOverriden(__result, __instance, pawn);
            }
        }
    }

    public static class Patch_Feathers_AttachmentGraphic
    {
        public static void Postfix(PawnRenderNode __instance, Pawn pawn, ref Graphic __result)
        {
            if (__result == null
                || __instance.gene == null
                || __instance is PawnRenderNode_Fur
                || __instance is PawnRenderNode_Head
                || __instance.Props.colorType == PawnRenderNodeProperties.AttachmentColorType.Custom)
            {
                return;
            }
            if (!AvirFeatherHarmony.IsFeatherAttachment(__instance.gene.def))
            {
                return;
            }
            Gene_AvirFeathers gene = Gene_AvirFeathers.For(pawn);
            if (gene != null)
            {
                __result = gene.GetGraphicOverriden(__result, __instance, pawn);
            }
        }
    }

    // Body. The head and the attachments were coming out right while the body stayed a plain
    // uncoloured human torso, which means vanilla was not swapping in the fur body texture for
    // our gene. This forces it: the graphic is rebuilt straight from the gene's FurDef, in the
    // gene's own two colours.
    //
    // Hooked on PawnRenderNode.GraphicsFor (the list the node's primary graphic is taken from),
    // not on PawnRenderNode_Body.GraphicFor itself: another mod (Isekai WorldBoss) patches that
    // method in a way Harmony can no longer rebuild, so any further patch on it fails.
    public static class Patch_Feathers_BodyGraphic
    {
        public static IEnumerable<Graphic> Postfix(
            IEnumerable<Graphic> __result, PawnRenderNode __instance, Pawn pawn)
        {
            bool first = true;
            foreach (Graphic g in __result)
            {
                if (first)
                {
                    first = false;
                    yield return Replace(__instance, pawn, g);
                    continue;
                }
                yield return g;
            }
        }

        private static Graphic Replace(PawnRenderNode node, Pawn pawn, Graphic graphic)
        {
            if (!(node is PawnRenderNode_Body body) || graphic == null || pawn == null)
            {
                return graphic;
            }
            if (pawn.Drawer?.renderer?.CurRotDrawMode == RotDrawMode.Dessicated)
            {
                return graphic;
            }
            Gene_AvirFeathers gene = Gene_AvirFeathers.For(pawn);
            if (gene == null)
            {
                return graphic;
            }
            return gene.BodyGraphicFor(body, pawn, graphic) ?? graphic;
        }
    }

    // Shader: body and head nodes come back as plain Cutout, which can only carry one colour.
    // Without this the back colour never shows and only the belly colour is drawn.
    public static class Patch_Feathers_Shader
    {
        public static void Postfix(PawnRenderNode __instance, Pawn pawn, ref Shader __result)
        {
            if (__result == null
                || __instance.Props.colorType == PawnRenderNodeProperties.AttachmentColorType.Custom
                || __instance.gene?.def.endogeneCategory == EndogeneCategory.Headbone)
            {
                return;
            }

            bool ours = __instance is PawnRenderNode_Body
                || __instance is PawnRenderNode_Head
                || __instance is PawnRenderNode_Fur
                || (__instance.gene != null && AvirFeatherHarmony.IsFeatherAttachment(__instance.gene.def));

            if (!ours)
            {
                return;
            }

            Gene_AvirFeathers gene = Gene_AvirFeathers.For(pawn);
            if (gene != null)
            {
                __result = gene.GetShaderOverriden(__result, __instance);
            }
        }
    }

    // Newly generated pawns get their skinColorOverride pushed before they are first drawn.
    public static class Patch_Feathers_PawnGeneration
    {
        public static void Postfix(ref Pawn __result)
        {
            Gene_AvirFeathers gene = Gene_AvirFeathers.For(__result);
            if (gene != null)
            {
                gene.ApplyColors();
            }
        }
    }

    /// <summary>
    /// THE white-belly bug.
    ///
    /// Pawn_GeneTracker.EnsureCorrectSkinColorOverride is vanilla's own bookkeeping: it walks the
    /// pawn's active genes looking for one that declares skinIsHairColor or a skinColorOverride of
    /// its own, and if it finds NEITHER it sets story.skinColorOverride back to null. The scale
    /// gene declares neither - it carries its colours in its own fields - so vanilla considered
    /// the override unexplained and wiped it.
    ///
    /// That method runs on every gene add and remove. The scale gene's PostAdd therefore set the
    /// belly colour correctly and the very next gene the xenotype added threw it away, which is
    /// why a freshly created Kharassian came out with the first colour right and the second one
    /// blank, and why re-applying the xenotype or opening the dev colour picker "fixed" it: both
    /// end by writing the override again, with nothing left to run after them.
    ///
    /// The postfix simply puts it back, which makes the gene the last word on its own colour no
    /// matter what order things are added in - generation, Character Editor, surgery, loading a
    /// save.
    /// </summary>
    public static class Patch_Feathers_KeepSkinColorOverride
    {
        public static void Postfix(Pawn_GeneTracker __instance)
        {
            Pawn pawn = __instance?.pawn;
            if (pawn?.story == null)
            {
                return;
            }

            Gene_AvirFeathers gene = Gene_AvirFeathers.For(pawn);
            if (gene == null || !gene.colorsInitialized || !gene.colorTwo.HasValue)
            {
                return;
            }

            if (pawn.story.skinColorOverride != gene.colorTwo)
            {
                pawn.story.skinColorOverride = gene.colorTwo;
            }
        }
    }
}

namespace Avir
{
    /// <summary>
    /// Registers the feather render hooks and the styling-station option, one patch at a time,
    /// each in its own try/catch - the same approach as KharassianHarmony. A patch that fails
    /// costs that patch alone and names itself in the log.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirFeatherHarmony
    {
        public const string HarmonyId = "Tenebris.Avir.Feathers";

        /// <summary>Attachment genes that take the feather colours: ears and tail.</summary>
        private static readonly HashSet<string> Attachments = new HashSet<string>
        {
            "Avir_GriffinEars",
            "Avir_GriffinTail",
        };

        public static bool IsFeatherAttachment(GeneDef def)
        {
            return def != null && Attachments.Contains(def.defName);
        }

        static AvirFeatherHarmony()
        {
            Harmony harmony = new Harmony(HarmonyId);
            int failed = 0;

            failed += Postfix(harmony, AccessTools.Method(typeof(PawnRenderNode_Fur), "GraphicFor"),
                typeof(Patch_Feathers_FurGraphic));
            failed += Postfix(harmony, AccessTools.Method(typeof(PawnRenderNode_Head), "GraphicFor"),
                typeof(Patch_Feathers_HeadGraphic));
            failed += Postfix(harmony, AccessTools.Method(typeof(PawnRenderNode), "GraphicFor"),
                typeof(Patch_Feathers_AttachmentGraphic));
            failed += Postfix(harmony, AccessTools.Method(typeof(PawnRenderNode), "GraphicsFor"),
                typeof(Patch_Feathers_BodyGraphic));
            failed += Postfix(harmony, AccessTools.Method(typeof(PawnRenderNode), "ShaderFor"),
                typeof(Patch_Feathers_Shader));
            failed += Postfix(harmony, AccessTools.Method(typeof(PawnGenerator), "GeneratePawn",
                new[] { typeof(PawnGenerationRequest) }), typeof(Patch_Feathers_PawnGeneration), Priority.Normal);
            failed += Postfix(harmony, AccessTools.Method(typeof(Pawn_GeneTracker), "EnsureCorrectSkinColorOverride"),
                typeof(Patch_Feathers_KeepSkinColorOverride));
            failed += Postfix(harmony, AccessTools.Method(typeof(Building_StylingStation), "GetFloatMenuOptions"),
                typeof(Patch_StylingStation_FeatherColor), Priority.Normal);

            if (failed > 0)
            {
                Log.Warning("[Avir] " + failed + " feather patch(es) could not be applied; see the errors above.");
            }
        }

        private static int Postfix(Harmony harmony, MethodInfo target, Type patchClass, int priority = Priority.Last)
        {
            if (target == null)
            {
                Log.Error("[Avir] Feather patch " + patchClass.Name + ": target method not found.");
                return 1;
            }
            try
            {
                HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(patchClass, "Postfix"))
                {
                    priority = priority
                };
                harmony.Patch(target, postfix: postfix);
                return 0;
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Feather patch " + patchClass.Name + " on " + target.DeclaringType?.Name + "."
                    + target.Name + " failed: " + e);
                return 1;
            }
        }
    }
}
