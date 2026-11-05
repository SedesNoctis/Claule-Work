using System;
using System.Collections.Generic;
using HarmonyLib;
using FacialAnimation;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.FacialAnimationCompat
{
    /// <summary>
    /// [NL] Facial Animation + "anthrosonae FacialAnimations": Avir get the anthrogriff face,
    /// not a human one.
    ///
    /// Facial Animation draws its own head instead of the vanilla one and picks it by gene: each
    /// of its head and mouth defs lists the genes it belongs to (targetGeneDefs), and a pawn whose
    /// genes match none of them gets a human head. The addon lists only Anthrosonae's
    /// ATK_AnthrogriffFur for its griffin head, while the Avir head and coat come from our own
    /// gene, Avir_FeatheredFur - so the Avir fell through to the human heads.
    ///
    /// Here Avir_FeatheredFur is added to the addon's griffin head and griffin mouth, which keeps
    /// whichever texture a retexture pack patched into those defs. And as the addon does for
    /// Anthrosonae's fur gene, the head is redrawn in the feather colours (two-tone, mask-driven),
    /// and redrawn again whenever the feathers are recoloured.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirFacialAnimation
    {
        public const string OurGene = "Avir_FeatheredFur";
        private static readonly string[] HeadDefs = { "ATK_FA_anthrosonae_Anthrogriff_Head" };
        private static readonly string[] MouthDefs = { "Anthrogriff_Mouth_Invisible" };

        private static readonly AccessTools.FieldRef<ControllerBaseComp<FacialAnimation.HeadTypeDef, HeadShapeDef>,
            Dictionary<NLFacialAnimationLayerType, NLGraphic_Collection<HeadShapeDef>>> GraphicList =
            AccessTools.FieldRefAccess<ControllerBaseComp<FacialAnimation.HeadTypeDef, HeadShapeDef>,
                Dictionary<NLFacialAnimationLayerType, NLGraphic_Collection<HeadShapeDef>>>("graphicList");

        static AvirFacialAnimation()
        {
            try
            {
                int n = 0;
                foreach (string d in HeadDefs) n += AddGene(DefDatabase<FacialAnimation.HeadTypeDef>.GetNamedSilentFail(d));
                foreach (string d in MouthDefs) n += AddGene(DefDatabase<MouthTypeDef>.GetNamedSilentFail(d));
                new Harmony("Tenebris.Avir.FacialAnimation").PatchAll(typeof(AvirFacialAnimation).Assembly);
                Log.Message("[Avir] Facial Animation: griffin face linked to Avir (" + n + " defs).");
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Facial Animation link failed: " + e);
            }
        }

        private static int AddGene(FaceTypeDef def)
        {
            if (def == null) return 0;
            if (def.targetGeneDefs == null) def.targetGeneDefs = new List<string>();
            if (!def.targetGeneDefs.Contains(OurGene)) def.targetGeneDefs.Add(OurGene);
            return 1;
        }

        /// <summary>The head layer redrawn in the feather colours.</summary>
        public static void Recolor(HeadControllerComp comp)
        {
            Pawn pawn = comp?.parent as Pawn;
            if (pawn == null || pawn.IsMutant) return;
            Gene_AvirFeathers gene = Gene_AvirFeathers.For(pawn);
            if (gene == null || !gene.colorsInitialized) return;
            FacialAnimation.HeadTypeDef face = comp.FaceType;
            if (face?.targetGeneDefs == null || !face.targetGeneDefs.Contains(gene.def.defName)) return;
            Dictionary<NLFacialAnimationLayerType, NLGraphic_Collection<HeadShapeDef>> list = GraphicList(comp);
            if (list == null) return;

            Color c1 = gene.colorOne;
            Color c2 = gene.colorTwo ?? c1;
            string tex = GraphicHelper.GetTexPathPrefix(face, pawn.gender);
            string mask = GraphicHelper.GetMaskPathPrefix(face, pawn.gender);
            if (tex.NullOrEmpty()) return;
            var param = new NLGraphicParam("Map/CutoutComplex", tex, mask, c1, c2);
            list[NLFacialAnimationLayerType.Basic] = (NLGraphic_Collection<HeadShapeDef>)GraphicDatabase.Get<NLGraphic_Collection<HeadShapeDef>>(param.ToXML());
        }
    }

    [HarmonyPatch(typeof(HeadControllerComp), nameof(HeadControllerComp.LoadTextures))]
    public static class Patch_AvirFA_LoadTextures
    {
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(HeadControllerComp __instance)
        {
            try { AvirFacialAnimation.Recolor(__instance); }
            catch (Exception e) { Log.ErrorOnce("[Avir] Facial Animation head colours failed: " + e, 0x41564641); }
        }
    }

    /// <summary>
    /// The eyelids of an Avir face. A lid with a "_cover" texture (the Experimentals and UNAGI packs) is drawn as a piece
    /// of the HEAD texture cut out in the lid's shape - but the head under it is the addon's small griffin head, not the
    /// Avir head that shows, so at the Avir eyes that piece is mostly empty and the eye showed through above the lid
    /// line. Here the cover is drawn solid instead: the lid's own cover shape, filled with the main feather colour, so
    /// a closing lid always hides the eye it covers. A plain lid (no cover texture) is tinted with the feathers too.
    /// </summary>
    [HarmonyPatch(typeof(LidControllerComp), nameof(LidControllerComp.LoadTextures))]
    public static class Patch_AvirFA_Lids
    {
        private static readonly AccessTools.FieldRef<ControllerBaseComp<LidTypeDef, LidShapeDef>,
            Dictionary<NLFacialAnimationLayerType, NLGraphic_Collection<LidShapeDef>>> LidGraphics =
            AccessTools.FieldRefAccess<ControllerBaseComp<LidTypeDef, LidShapeDef>,
                Dictionary<NLFacialAnimationLayerType, NLGraphic_Collection<LidShapeDef>>>("graphicList");

        [HarmonyPriority(Priority.Low)]
        public static void Postfix(LidControllerComp __instance)
        {
            try
            {
                Pawn pawn = __instance?.parent as Pawn;
                if (pawn == null || pawn.IsMutant) return;
                Gene_AvirFeathers gene = Gene_AvirFeathers.For(pawn);
                if (gene == null || !gene.colorsInitialized) return;
                FacialAnimation.HeadTypeDef head = pawn.GetComp<HeadControllerComp>()?.FaceType;
                if (head?.targetGeneDefs == null || !head.targetGeneDefs.Contains(gene.def.defName)) return;
                LidTypeDef lid = __instance.FaceType;
                var list = LidGraphics(__instance);
                if (lid == null || list == null) return;

                Color c1 = gene.colorOne;
                if (list.ContainsKey(NLFacialAnimationLayerType.Cover))
                {
                    string coverShape = GraphicHelper.GetTexPathPrefix(lid, pawn.gender) + "_cover";
                    var param = new NLGraphicParam("Map/Cutout", coverShape, null, c1, c1);
                    list[NLFacialAnimationLayerType.Cover] = (NLGraphic_Collection<LidShapeDef>)GraphicDatabase.Get<NLGraphic_Collection<LidShapeDef>>(param.ToXML());
                }
                else if (list.ContainsKey(NLFacialAnimationLayerType.Basic))
                {
                    string tex = GraphicHelper.GetTexPathPrefix(lid, pawn.gender);
                    var param = new NLGraphicParam(GraphicHelper.GetSkinShader(pawn, lid), null, GraphicHelper.GetMaskPathPrefix(lid, pawn.gender), c1, c1);
                    if (!string.IsNullOrEmpty(lid.optTexPath))
                    {
                        param.texPaths.Add(string.Format(tex, "normal"));
                        param.texPaths.Add(GraphicHelper.GetOptTexPathPrefix(lid, pawn.gender));
                    }
                    else param.texPaths.Add(tex);
                    list[NLFacialAnimationLayerType.Basic] = (NLGraphic_Collection<LidShapeDef>)GraphicDatabase.Get<NLGraphic_Collection<LidShapeDef>>(param.ToXML());
                }
            }
            catch (Exception e) { Log.ErrorOnce("[Avir] Facial Animation lid colours failed: " + e, 0x41564643); }
        }
    }

    /// <summary>Feathers recoloured (styling station, colour picker, birth): reload the face.</summary>
    [HarmonyPatch(typeof(Gene_AvirFeathers), nameof(Gene_AvirFeathers.ApplyColors))]
    public static class Patch_AvirFA_ApplyColors
    {
        public static void Postfix(Gene_AvirFeathers __instance)
        {
            try
            {
                __instance?.pawn?.GetComp<HeadControllerComp>()?.SetDirty();
                __instance?.pawn?.GetComp<LidControllerComp>()?.SetDirty();
            }
            catch (Exception e) { Log.ErrorOnce("[Avir] Facial Animation refresh failed: " + e, 0x41564642); }
        }
    }
}
