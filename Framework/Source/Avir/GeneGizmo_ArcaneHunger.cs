using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir
{
    /// <summary>
    /// The arcane hunger bar, with two small permission checkboxes tucked into its header instead
    /// of sitting as separate gizmos on the command bar. This is exactly how vanilla handles the
    /// sanguophage "hemogen packs allowed" tickbox - GeneGizmo_ResourceHemogen does the same
    /// thing to the same header - so it should read as native rather than as clutter.
    ///
    ///   [ eye ]     hunt wild animals on your own
    ///   [ flask ]   drink stored essence extract
    ///
    /// Both default to OFF. The draggable feed-threshold marker below the bar is untouched and
    /// still comes from the base class, as does the silver bar colour, which is still declared in
    /// Gene_ArcaneHunger.BarColor rather than here.
    ///
    /// ---------------------------------------------------------------------------------------
    /// SHAPE OF THE BASE CLASS, confirmed against the 1.6 compiler:
    ///
    ///   - GeneGizmo_Resource is ABSTRACT. It leaves two members of Gizmo_Slider unimplemented,
    ///     so every concrete resource gizmo has to supply them itself. Vanilla's
    ///     GeneGizmo_ResourceHemogen does exactly the same thing; this class mirrors it.
    ///       * DraggingBar - backed by a STATIC field on purpose. Only one bar in the whole UI
    ///         can be under the mouse at a time, and vanilla treats it as global state.
    ///       * GetTooltip() - the hover text for the bar.
    ///   - the constructor really is (Gene_Resource, List&lt;IGeneResourceDrain&gt;, Color, Color),
    ///     which is what Gene_Resource.GetGizmos() constructs reflectively from
    ///     resourceGizmoType.
    ///   - DrawHeader(Rect, ref bool) really is protected virtual.
    ///
    /// GetTooltip below deliberately touches only Gene_Resource.Value, GeneDef.LabelCap and
    /// GeneDef.resourceDescription - all of which this mod already uses elsewhere - rather than
    /// the ValueForDisplay / MaxForDisplay helpers vanilla's own tooltip reaches for, so there is
    /// nothing left in this file that depends on an unverified signature.
    /// ---------------------------------------------------------------------------------------
    /// </summary>
    public class GeneGizmo_ArcaneHunger : GeneGizmo_Resource
    {
        private const float BoxSize = 24f;

        /// How washed-out a toggle icon looks while its permission is off.
        private static readonly Color OffTint = new Color(1f, 1f, 1f, 0.28f);

        // ============ TOGGLE ICONS ============
        // Resolved on first draw rather than from a [StaticConstructorOnStartup] field
        // initializer. Lazy resolution removes every question about whether textures were loaded
        // yet when this type's static constructor happened to run, and costs one dictionary
        // lookup, once, ever.
        //
        // Each icon reports itself to the log exactly once, which is also the fastest way to tell
        // three otherwise identical situations apart:
        //   - "toggle icon: <path>"        -> new build is live and the texture resolved
        //   - "toggle icon NOT FOUND"      -> new build is live, the path is wrong
        //   - neither line appears at all  -> the running DLL is older than this file

        private static bool iconsResolved;
        private static Texture2D extractIcon;
        private static Texture2D wildHuntIcon;

        private static void EnsureIcons()
        {
            if (iconsResolved) return;
            iconsResolved = true;

            // The extract's ThingDef texPath points at the FOLDER, because the item draws through
            // Graphic_StackCount and picks _a/_b/_c from inside it. A UI texture needs a single
            // file, so the plain one is tried first and a stack variant second.
            extractIcon = FindIcon("extract-drinking",
                "Things/Item/Drugs/Avir_EssenceExtract/Avir_EssenceExtract",
                "Things/Item/Drugs/Avir_EssenceExtract/Avir_EssenceExtract_c",
                "Things/Item/Drugs/Avir_EssenceExtract/Avir_EssenceExtract_a",
                "Things/Item/Drugs/Avir_EssenceExtract");

            wildHuntIcon = FindIcon("wild-hunting",
                "Avir/GeneIcons/Avir_EssenceSiphon",
                "Avir/GeneIcons/Avir_EssenceReaver",
                "Avir/GeneIcons/Avir_ArcaneHunger");
        }

        private static Texture2D FindIcon(string label, params string[] candidatePaths)
        {
            for (int i = 0; i < candidatePaths.Length; i++)
            {
                Texture2D tex = ContentFinder<Texture2D>.Get(candidatePaths[i], false);
                if (tex != null)
                {
                    Log.Message("[Avir] " + label + " toggle icon: " + candidatePaths[i]);
                    return tex;
                }
            }

            Log.Warning("[Avir] " + label + " toggle icon NOT FOUND, falling back to a plain checkbox. Tried: "
                + string.Join(" | ", candidatePaths));
            return null;
        }

        // Kept locally rather than reaching for the base class's own field, so this file depends
        // on as little of the base type's shape as possible.
        private readonly Gene_ArcaneHunger hunger;

        // Global, like vanilla's: at most one slider in the interface is being dragged at a time,
        // so this does not need to be per-gizmo and must not be per-pawn.
        private static bool draggingBarShared;

        protected override bool DraggingBar
        {
            get { return draggingBarShared; }
            set { draggingBarShared = value; }
        }

        public GeneGizmo_ArcaneHunger(Gene_Resource gene, List<IGeneResourceDrain> drainGenes, Color barColor, Color barHighlightColor)
            : base(gene, drainGenes, barColor, barHighlightColor)
        {
            hunger = gene as Gene_ArcaneHunger;
        }

        protected override string GetTooltip()
        {
            if (hunger == null) return string.Empty;

            string text = hunger.def.LabelCap + ": " + hunger.Value.ToStringPercent();

            if (!hunger.def.resourceDescription.NullOrEmpty())
                text += "\n\n" + hunger.def.resourceDescription;

            // Spell the two permissions out here as well as on the checkboxes, so the state is
            // readable without hunting for which box is ticked.
            text += "\n\n" + "Drink essence extract: " + OnOff(hunger.extractDrinkingAllowed)
                  + "\n" + "Hunt wild animals: " + OnOff(hunger.wildFeedingAllowed);

            return text;
        }

        private static string OnOff(bool value)
        {
            return value ? "yes" : "no";
        }

        protected override void DrawHeader(Rect headerRect, ref bool mouseOverElement)
        {
            // If this gizmo ever ends up on something that is not an Avir hunger gene, just draw
            // the plain bar rather than throwing every frame.
            if (hunger == null)
            {
                base.DrawHeader(headerRect, ref mouseOverElement);
                return;
            }

            EnsureIcons();

            DrawToggle(ref headerRect, ref mouseOverElement, ref hunger.extractDrinkingAllowed,
                extractIcon, "Avir_ToggleExtract", "Drink essence extract",
                "When on, this Avir will fetch and drink stored essence extract once its arcane hunger falls below the marker.\n\nOff by default: extract is expensive and is also a building ingredient.");

            DrawToggle(ref headerRect, ref mouseOverElement, ref hunger.wildFeedingAllowed,
                wildHuntIcon, "Avir_ToggleWild", "Hunt wild animals",
                "When on, this Avir will hunt down and drain wild animals on its own once its arcane hunger falls below the marker.\n\nColony livestock, pets and bonded animals are never targeted, with or without this.\n\nFeeding on flagged prisoners and on downed enemies happens regardless of this setting.");

            base.DrawHeader(headerRect, ref mouseOverElement);
        }

        /// <summary>
        /// Draws one toggle at the right edge of the header and shrinks the header to match.
        ///
        /// Not a Widgets.Checkbox with custom textures: that helper takes a separate "checked"
        /// and "unchecked" image, and there is only one drawing per concept here. Instead the
        /// single icon is drawn at full strength when the permission is on and faded when it is
        /// off, which is the same read vanilla gives its hemogen-pack toggle, and an invisible
        /// button over the top handles the click.
        /// </summary>
        private static void DrawToggle(ref Rect headerRect, ref bool mouseOverElement, ref bool value,
            Texture2D icon, string tipKey, string tipTitle, string tipBody)
        {
            Rect box = new Rect(headerRect.xMax - BoxSize, headerRect.y, BoxSize, BoxSize);

            if (icon == null)
            {
                // Texture missing for any reason - fall back to a plain tickbox rather than
                // leaving an invisible control the player cannot see.
                Widgets.Checkbox(box.position, ref value, BoxSize, false, true);
            }
            else
            {
                Color previous = GUI.color;
                GUI.color = value ? Color.white : OffTint;
                GUI.DrawTexture(box, icon);
                GUI.color = previous;

                if (Widgets.ButtonInvisible(box))
                {
                    value = !value;
                    (value ? SoundDefOf.Tick_High : SoundDefOf.Tick_Low).PlayOneShotOnCamera();
                }
            }

            if (Mouse.IsOver(box))
            {
                Widgets.DrawHighlight(box);
                TooltipHandler.TipRegion(box, new TipSignal(
                    () => tipTitle.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + tipBody,
                    tipKey.GetHashCode()));
                mouseOverElement = true;
            }

            headerRect.xMax -= BoxSize;
        }
    }
}
