using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// A named two-colour feather preset. Copied from the Kharassian scale preset
    /// (KharassianScaleColorDef), itself ported from Anthrosonae's FurColorDef (Willow, Erin,
    /// Taranchuk). Primary is the back/wing colour, secondary the breast/underside; equal values
    /// give a flat single-colour coat.
    ///
    /// One addition over the Kharassian version: allowedGender. The Avir palette has colours only
    /// daughters are born with (the same rule as AvirColors.GriffinFurColorDef and AvirPalette).
    /// Gender.None = anyone. It only filters the birth roll; the picker shows every colour.
    /// </summary>
    public class AvirFeatherColorDef : Def
    {
        public Color primaryColor = Color.white;
        public Color? secondaryColor;
        public float selectionWeight = 1f;
        public int displayOrder;
        // Keep a colour out of one channel's swatch palette in the picker.
        public bool blacklistPrimary, blacklistSecondary;
        public Gender allowedGender = Gender.None;
    }

    /// <summary>The list of presets a feather gene is allowed to roll. Attached to the GeneDef.</summary>
    public class FeatherColorsExtension : DefModExtension
    {
        public List<AvirFeatherColorDef> allowedColors = new List<AvirFeatherColorDef>();
    }

    /// <summary>
    /// Griffin feather gene. A copy of the Kharassian scale gene (Gene_KharassianScales) with the
    /// names changed and a gender filter on the roll: rolls one preset from its own allowed list
    /// when added, remembers it across saves, and recolours the body, head, ears and tail to
    /// match - and the Avir wings, which ask this gene for their colours (AvirWings.cs). A fixed
    /// preset list is the whole point: without it the coat takes hair colour and you get rainbow
    /// griffins.
    ///
    /// Structure follows Anthrosonae's FurGene, with the styling-station job and the colour
    /// picker window ported alongside (JobDriver_ChangeFeatherColor, Window_AvirFeatherColors).
    /// No dependency on Anthrosonae at all.
    ///
    /// It is the gene class of Avir_FeatheredFur: the look and the coat are one gene. It extends
    /// Gene_GrantHediff so the coat's shedding hediff (HediffGrantExtension on the def) is still
    /// granted on add and removed on remove - base.PostAdd below does that before the colour roll.
    /// </summary>
    public class Gene_AvirFeathers : Gene_GrantHediff
    {

        public AvirFeatherColorDef featherColor;

        /// <summary>
        /// True once this gene has colours of its own - rolled at birth or picked by hand.
        /// The roll used to be triggered by "featherColor == null", but a hand-picked colour
        /// deliberately clears featherColor (it is no longer one of the presets), so every
        /// redraw after Accept re-rolled a random preset over the chosen colours.
        /// </summary>
        public bool colorsInitialized;
        public Color colorOne = Color.white;
        public Color? colorTwo;
        /// <summary>The wings' own colour, picked in the feather window (null - they wear the back's colour).</summary>
        public Color? wingColor;
        /// <summary>The colour of the wing tips (null - the breast's colour), and the tip pattern (-1 - by the pawn's id).</summary>
        public Color? wingTipColor;
        public int wingPattern = -1;

        public static Gene_AvirFeathers For(Pawn pawn, bool activeOnly = true)
        {
            if (pawn?.genes == null)
            {
                return null;
            }
            return pawn.genes.GenesListForReading.OfType<Gene_AvirFeathers>()
                .FirstOrDefault(g => g.Active || !activeOnly);
        }

        public override void PostAdd()
        {
            base.PostAdd();
            RollColor();
        }

        private void RollColor()
        {
            List<AvirFeatherColorDef> allowed = def.GetModExtension<FeatherColorsExtension>()?.allowedColors;
            if (allowed == null || allowed.Count == 0)
            {
                return;
            }
            // Daughters-only colours stay off sons. If a gene's list somehow leaves nothing for
            // this pawn, fall back to the whole list rather than leaving the coat uncoloured.
            Gender gender = pawn?.gender ?? Gender.None;
            List<AvirFeatherColorDef> pool = allowed
                .Where(c => c.allowedGender == Gender.None || c.allowedGender == gender)
                .ToList();
            if (pool.Count == 0)
            {
                pool = allowed;
            }

            if (pool.TryRandomElementByWeight(c => c.selectionWeight, out AvirFeatherColorDef picked))
            {
                SetColor(picked);
            }
        }

        public void SetColor(AvirFeatherColorDef colorDef)
        {
            featherColor = colorDef;
            colorOne = colorDef.primaryColor;
            colorTwo = colorDef.secondaryColor;
            colorsInitialized = true;
            ApplyColors();
        }

        public void ApplyColors()
        {
            if (pawn?.story == null)
            {
                return;
            }
            pawn.story.skinColorOverride = colorTwo;

            // EnsureGraphicsInitialized first: dirtying a render tree that was never built
            // throws "Node is null - you must call EnsureGraphicsInitialized()" out of the
            // parallel pre-draw pass.
            if (pawn.Spawned || pawn.ParentHolder != null)
            {
                pawn.Drawer?.renderer?.EnsureGraphicsInitialized();
            }
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        /// <summary>
        /// Rebuilds a render node's graphic in the preset's colours. Two distinct colours need
        /// the CutoutComplex shader (mask-driven two-tone); one colour, or a hair-coloured
        /// attachment, falls back to plain Cutout.
        /// </summary>
        public Graphic GetGraphicOverriden(Graphic original, PawnRenderNode source, Pawn forPawn)
        {
            if (!colorsInitialized)
            {
                RollColor();
            }

            Color color1 = PostProcess(source, colorOne);
            // No belly colour means a flat hide in the back colour, NOT a white one. Pure white
            // reads as "the texture failed to load" and was exactly what a Kharassian's chin,
            // torso and tail-tip came out as whenever the second colour was not set yet (the
            // Kharassian bug this code was fixed against; kept for the Avir).
            Color color2 = colorTwo.HasValue ? PostProcess(source, colorTwo.Value) : color1;

            if (colorTwo.HasValue && !color1.IndistinguishableFrom(color2))
            {
                bool furGeneHead = source is PawnRenderNode_Head
                    && forPawn.story?.headType?.requiredGenes != null
                    && forPawn.story.headType.requiredGenes.Any(g => typeof(Gene_AvirFeathers).IsAssignableFrom(g.geneClass));

                if (original.Shader == ShaderTypeDefOf.CutoutComplex.Shader || furGeneHead)
                {
                    return GraphicDatabase.Get<Graphic_Multi>(original.path,
                        ShaderTypeDefOf.CutoutComplex.Shader, Vector2.one, color1, color2);
                }
            }

            if (source.Props.colorType == PawnRenderNodeProperties.AttachmentColorType.Hair)
            {
                return GraphicDatabase.Get<Graphic_Multi>(original.path,
                    ShaderTypeDefOf.Cutout.Shader, Vector2.one, color1);
            }

            return GraphicDatabase.Get<Graphic_Multi>(original.path,
                ShaderTypeDefOf.Cutout.Shader, Vector2.one, color2);
        }

        /// <summary>
        /// The body graphic: the fur's own torso texture when the game failed to resolve it,
        /// always in the gene's two colours.
        /// </summary>
        public Graphic BodyGraphicFor(PawnRenderNode source, Pawn forPawn, Graphic current)
        {
            if (forPawn?.story == null)
            {
                return null;
            }

            // Vanilla does not reliably resolve the fur body for this gene - when it does not,
            // the pawn is drawn with the plain human torso. So the fur's own body path is
            // substituted in that case, and left alone when the resolved path is already in the
            // fur's folder, whatever body type it names.
            //
            // This is safe for Character Editor only because the body files are called
            // Naked_<BodyType>: CE finds "Naked_" in the path and substitutes body type names
            // into it to build its list. With any other naming the list comes out empty and the
            // selector is dead - which is exactly what happened before the rename.
            string path = current?.path;
            string furPath = def.fur?.GetFurBodyGraphicPath(forPawn);

            if (!furPath.NullOrEmpty())
            {
                int slash = furPath.LastIndexOf('/');
                string furFolder = slash > 0 ? furPath.Substring(0, slash + 1) : null;

                if (path.NullOrEmpty() || furFolder == null || !path.StartsWith(furFolder))
                {
                    path = furPath;
                }
            }

            if (path.NullOrEmpty())
            {
                return null;
            }

            if (!colorsInitialized)
            {
                RollColor();
            }

            Color color1 = PostProcess(source, colorOne);
            Color color2 = colorTwo.HasValue ? PostProcess(source, colorTwo.Value) : color1;

            return GraphicDatabase.Get<Graphic_Multi>(path,
                ShaderTypeDefOf.CutoutComplex.Shader, Vector2.one, color1, color2);
        }

        /// <summary>
        /// Two distinct colours can only be drawn through the CutoutComplex shader, which reads
        /// the texture's mask file. RimWorld hands the body and head nodes a plain Cutout shader,
        /// so without this the second colour silently wins and the first does nothing.
        /// </summary>
        public Shader GetShaderOverriden(Shader original, PawnRenderNode source)
        {
            if (!colorsInitialized)
            {
                return original;
            }

            Color color1 = PostProcess(source, colorOne);
            Color color2 = colorTwo.HasValue ? PostProcess(source, colorTwo.Value) : color1;

            if (colorTwo.HasValue && !color1.IndistinguishableFrom(color2)
                && (original == ShaderTypeDefOf.CutoutComplex.Shader
                    || source is PawnRenderNode_Body
                    || source is PawnRenderNode_Head
                    || source is PawnRenderNode_Fur))
            {
                return ShaderTypeDefOf.CutoutComplex.Shader;
            }

            return original;
        }

        private Color PostProcess(PawnRenderNode source, Color color)
        {
            color *= source.Props.colorRGBPostFactor;
            if (pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Rotting)
            {
                color = PawnRenderUtility.GetRottenColor(color);
            }
            return color;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (!DebugSettings.ShowDevGizmos)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "DEV: Change feathers",
                action = () => Find.WindowStack.Add(new Window_AvirFeatherColors(this))
            };

            // Diagnostic for the "torso is human again" problem: prints every value the body
            // swap depends on, so the broken link can be named instead of guessed at.
            yield return new Command_Action
            {
                defaultLabel = "DEV: Report body path",
                action = () =>
                {
                    string furOnGene = def.fur != null ? def.fur.defName : "NULL";
                    string furOnPawn = pawn.story?.furDef != null ? pawn.story.furDef.defName : "NULL";
                    string furPath = def.fur != null ? def.fur.GetFurBodyGraphicPath(pawn) : "NULL";
                    string bodyType = pawn.story?.bodyType != null ? pawn.story.bodyType.defName : "NULL";

                    string drawn = "NULL";
                    try
                    {
                        Graphic g = pawn.Drawer?.renderer?.BodyGraphic;
                        drawn = g != null ? g.path : "NULL";
                    }
                    catch (Exception e)
                    {
                        drawn = "threw: " + e.Message;
                    }

                    string overriddenBy = "none";
                    try
                    {
                        if (!Active && pawn.genes != null)
                        {
                            List<Gene> all = pawn.genes.GenesListForReading;
                            for (int i = 0; i < all.Count; i++)
                            {
                                Gene other = all[i];
                                if (other == this || other.def?.exclusionTags == null || def.exclusionTags == null)
                                {
                                    continue;
                                }
                                foreach (string tag in other.def.exclusionTags)
                                {
                                    if (def.exclusionTags.Contains(tag) && other.Active)
                                    {
                                        overriddenBy = other.def.defName + " (tag: " + tag + ")";
                                        break;
                                    }
                                }
                                if (overriddenBy != "none")
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        overriddenBy = "check threw: " + e.Message;
                    }

                    Log.Message("[Avir] body report for " + pawn.LabelShortCap
                        + "\n  gene active: " + Active
                        + "\n  overridden by: " + overriddenBy
                        + "\n  fur on gene def: " + furOnGene
                        + "\n  fur on pawn story: " + furOnPawn
                        + "\n  body type: " + bodyType
                        + "\n  fur body path: " + furPath
                        + "\n  graphic actually drawn: " + drawn);
                }
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref featherColor, "featherColor");
            Scribe_Values.Look(ref colorsInitialized, "colorsInitialized");
            Scribe_Values.Look(ref colorOne, "colorOne");
            Scribe_Values.Look(ref colorTwo, "colorTwo");
            Scribe_Values.Look(ref wingColor, "wingColor");
            Scribe_Values.Look(ref wingTipColor, "wingTipColor");
            Scribe_Values.Look(ref wingPattern, "wingPattern", -1);
        }
    }
}
