using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Colours the wing gene's render graphic so the wings track the pawn's coat: the Avir's own
    /// griffin feathers first (Gene_AvirFeathers), Anthrosonae's fur gene second, the built-in
    /// palette last.
    ///
    /// IMPORTANT - why this does NOT read FurColorDef:
    /// A FurColorDef is a *preset*. Its primaryColor/secondaryColor are static def values shared
    /// by every pawn using that preset. Anthrosonae's "Change fur colors" dialog therefore cannot
    /// and does not edit the def - it writes live colours onto the pawn's FurGene instance. So
    /// reading the def gives you the preset the pawn was ROLLED with, not the colour it is
    /// currently WEARING. Rerolling a preset looks like it works; hand-picking a colour looks like
    /// it does nothing. That was the original bug.
    ///
    /// Instead we hand our graphic to FurGene.GetGraphicOverriden - the same public method
    /// Anthrosonae's own fur render path uses (see AvirColors.PawnRenderNode_Fur_GraphicFor_
    /// CompatPatch). Whatever colours the fur is actually displaying, preset or hand-picked, that
    /// method applies them. The wings cannot drift out of sync with the fur, because they ask the
    /// same object the same question.
    ///
    /// Everything Anthrosonae-related is late-bound reflection. There is no compile-time reference
    /// to Anthrosonae.dll in this file or in Avir.csproj - that is what lets the wing gene live in
    /// the mod root and work for players without Anthrosonae, rather than being locked behind
    /// Compat/Anthrosonae.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNode), "GraphicFor")]
    public static class Patch_AvirWings_GraphicFor
    {
        private const string WingGeneDefName = "Flight_Gene";   // wings, flight and soar are one gene now

        // ---- Anthrosonae handles, resolved once. All null when Anthrosonae isn't loaded. ----

        private static readonly Type FurGeneType = AccessTools.TypeByName("Anthrosonae.FurGene");

        // FurGene.GetGraphicOverriden(Graphic, PawnRenderNode, Pawn) -> Graphic
        private static readonly MethodInfo GetGraphicOverridenMethod =
            FurGeneType != null ? AccessTools.Method(FurGeneType, "GetGraphicOverriden") : null;

        // Will that method actually accept the node we hold? Anthrosonae calls it with a
        // PawnRenderNode_Fur; if its signature is narrowed to that subclass, passing our plain
        // wing PawnRenderNode would throw. Check the signature once rather than per-frame.
        private static readonly bool GetGraphicOverridenUsable = CheckSignature();

        private static bool loggedFallback;

        private static bool CheckSignature()
        {
            if (GetGraphicOverridenMethod == null)
            {
                return false;
            }

            ParameterInfo[] ps = GetGraphicOverridenMethod.GetParameters();
            return ps.Length == 3
                && typeof(Graphic).IsAssignableFrom(ps[0].ParameterType)
                && ps[1].ParameterType.IsAssignableFrom(typeof(PawnRenderNode))
                && ps[2].ParameterType.IsAssignableFrom(typeof(Pawn));
        }

        public static void Postfix(PawnRenderNode __instance, Pawn pawn, ref Graphic __result)
        {
            if (__result == null || pawn == null)
            {
                return;
            }

            if (__instance.gene == null || __instance.gene.def.defName != WingGeneDefName)
            {
                return; // not our gene - leave every other render node completely alone
            }

            // Build the wing graphic ourselves, because we're choosing the TEXTURE PATH (which
            // feather pattern this pawn has) as well as the colour. RimWorld derives a mask's
            // filename from its texture's path (X_south.png -> X_southm.png), so each pattern
            // needs its own folder holding a full base+mask set - there's no way to keep one base
            // texture and swap only the mask.
            //
            // CutoutComplex is forced here rather than declared in XML. Masks are only sampled by
            // a two-colour shader, and PawnRenderNodeProperties' shader field name is
            // version-dependent (Corvyia's own def carries a comment admitting theirs is a guess).
            // Requesting the graphic under CutoutComplex loads the _m masks regardless of what the
            // def says - which is why Genes_Avir_Wings.xml carries no <shaderType> tag at all.
            string path = WingPatterns.PathFor(pawn);

            // the new art is bigger than the old: folded a little, spread a lot (a real wingspan)
            Vector2 size = __result.drawSize * (path.Contains("/Spread") ? WingPatterns.SpreadScale : WingPatterns.FoldedScale);
            Graphic wings = GraphicDatabase.Get<Graphic_Multi>(
                path,
                ShaderDatabase.CutoutComplex,
                size,
                Color.white,
                Color.white);

            __result = ApplyFurColors(wings, __instance, pawn);
        }

        private static Graphic ApplyFurColors(Graphic wings, PawnRenderNode node, Pawn pawn)
        {
            // ---- Path 0: the Avir's own griffin feathers (Gene_AvirFeathers) ----
            // The wings are part of the same coat, so they wear its live colours - rolled or
            // hand-picked in the feather window - back first, breast second, exactly as the body
            // does. Checked before Anthrosonae: an Avir with its own feathers never needs theirs.
            Gene_AvirFeathers feathers = Gene_AvirFeathers.For(pawn);
            if (feathers != null && feathers.colorsInitialized)
            {
                Color one = feathers.wingColor ?? feathers.colorOne;   // the wings may wear a colour of their own
                Color two = feathers.wingTipColor ?? feathers.colorTwo ?? one;
                if (pawn.Drawer?.renderer?.CurRotDrawMode == RotDrawMode.Rotting)
                {
                    one = PawnRenderUtility.GetRottenColor(one);
                    two = PawnRenderUtility.GetRottenColor(two);
                }
                return wings.GetColoredVersion(ShaderDatabase.CutoutComplex, one, two);
            }

            object furGene = FindFurGene(pawn);

            // ---- Path 1: ask the fur gene to colour it, exactly as it colours the fur ----
            // The ONLY path that survives a manual recolour, because the live colours live on the
            // gene instance, not on any def.
            if (furGene != null && GetGraphicOverridenUsable)
            {
                try
                {
                    if (GetGraphicOverridenMethod.Invoke(furGene, new object[] { wings, node, pawn })
                        is Graphic tinted)
                    {
                        return tinted;
                    }
                }
                catch (Exception e)
                {
                    if (!loggedFallback)
                    {
                        loggedFallback = true;
                        Log.Warning("[Avir] FurGene.GetGraphicOverriden threw while colouring Avir "
                                  + "wings; falling back to the built-in palette. Wings will not "
                                  + "follow hand-picked fur colours. " + e.Message);
                    }
                }
            }

            // ---- Path 2: no Anthrosonae, or a pawn with no fur gene ----
            AvirWingColor picked = AvirPalette.PickFor(pawn);
            if (picked == null)
            {
                return wings;
            }

            return wings.GetColoredVersion(ShaderDatabase.CutoutComplex, picked.primary, picked.secondary);
        }

        private static object FindFurGene(Pawn pawn)
        {
            if (FurGeneType == null || pawn.genes == null)
            {
                return null;
            }

            return pawn.genes.GenesListForReading
                .FirstOrDefault(g => g != null && FurGeneType.IsInstanceOfType(g));
        }
    }

    /// <summary>
    /// The four feather-mask patterns. Each folder under Textures/Avir/Wings/ holds a
    /// complete base+mask set for all three facings; they share identical base art and
    /// differ only in where the mask's red (primary) region gives way to green (secondary).
    ///
    ///   Classic - original band, transition around mid-wing
    ///   Crested - transition high; smallest primary region
    ///   Mantled - transition low; primary covers most of the wing, green only at the tips
    ///   Swept   - like Mantled but the transition runs diagonally, following the feather sweep
    ///
    /// The pick is seeded off the pawn's ID and mixed with a DIFFERENT constant than the
    /// colour pick in AvirPalette, so pattern and colour vary independently instead of
    /// being locked together (otherwise every white-winged Avir would share one pattern).
    /// Stable across saves and reloads.
    /// </summary>
    public static class WingPatterns
    {
        public static readonly string[] Patterns = { "Classic", "Crested", "Mantled", "Swept" };
        public static readonly string[] PatternLabels = { "classic band", "high crest", "tips only", "swept" };

        private const string Root = "Avir/Wings/";
        private const string Leaf = "/Avir_Wings";
        public const float FoldedScale = 1f, SpreadScale = 1f;   // size is the node's drawSize; the art carries the difference

        /// <summary>Spread (in motion, drafted, flying) or folded (at rest, and always for a chick); the
        /// A'Shacsaign wear membranous wings instead of feathered ones.</summary>
        private static bool? heraldArt;
        /// <summary>The Heralds' four wings are drawn once their art is in; until then they wear the Avir ones.</summary>
        private static bool HeraldArt => heraldArt ?? (heraldArt = ContentFinder<Texture2D>.Get(Root + "Linarian/Folded" + Leaf + "_south", false) != null).Value;

        public static string PathFor(Pawn pawn)
        {
            bool spread = Spread(pawn);
            if (pawn != null && HeraldArt && Linarian.Linarian.Is(pawn))
                return Root + "Linarian/" + (spread ? "Spread" : "Folded") + Leaf;
            if (pawn != null && Patch_AvirAShacsaignSpawn.IsSubrace(pawn))
                return Root + "Shaksai/" + (spread ? "Spread" : "Folded") + Leaf;
            int index = 0;
            if (pawn != null)
            {
                // Different multiplier from AvirPalette's, so a pawn's pattern is uncorrelated with its colour.
                uint h = (uint)pawn.thingIDNumber * 2246822519u;
                index = (int)(h % (uint)Patterns.Length);
                int chosen = Gene_AvirFeathers.For(pawn)?.wingPattern ?? -1;     // picked in the feather window
                if (chosen >= 0 && chosen < Patterns.Length) index = chosen;
            }
            return Root + (spread ? "Spread/" : "Folded/") + Patterns[index] + Leaf;
        }

        public static bool Spread(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed) return false;
            if (p.DevelopmentalStage.Juvenile() || p.DevelopmentalStage.Baby() || p.DevelopmentalStage.Newborn()) return false;
            // spread only in flight; folded otherwise
            return FlightGene.AvirFlightUtility.IsFlying(p);
        }

        private static readonly Dictionary<int, bool> shown = new Dictionary<int, bool>();

        /// <summary>A few times a second: when a pawn's wings should open or fold, its graphics are redrawn.</summary>
        public static void Check(Pawn p)
        {
            bool now = Spread(p);
            if (shown.TryGetValue(p.thingIDNumber, out bool was) && was == now) return;
            shown[p.thingIDNumber] = now;
            p.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    /// <summary>The males' wings are larger, the females' smaller.</summary>
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    public static class Patch_AvirWingsByGender
    {
        public const float Male = 1.1f, Female = 0.85f;

        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (node?.gene?.def?.defName != "Flight_Gene" || parms.pawn == null) return;
            float k = parms.pawn.gender == Gender.Female ? Female : parms.pawn.gender == Gender.Male ? Male : 1f;
            __result = new Vector3(__result.x * k, __result.y, __result.z * k);
        }
    }

    [HarmonyPatch(typeof(Pawn), "TickInterval")]
    public static class Patch_AvirWingsOpenFold
    {
        private static GeneDef wingGene;

        public static void Postfix(Pawn __instance, int delta)
        {
            if (!__instance.IsHashIntervalTick(15, delta) || __instance.genes == null || !__instance.Spawned) return;
            if (wingGene == null) wingGene = DefDatabase<GeneDef>.GetNamedSilentFail("Flight_Gene");
            if (wingGene == null || !__instance.genes.HasActiveGene(wingGene)) return;
            WingPatterns.Check(__instance);
        }
    }

    public class AvirWingColor
    {
        public Color primary;
        public Color secondary;
        public float weight;
        public Gender restrictedTo = Gender.None; // None = any gender

        public AvirWingColor(int pr, int pg, int pb, int sr, int sg, int sb, float weight,
                             Gender restrictedTo = Gender.None)
        {
            primary = new Color(pr / 255f, pg / 255f, pb / 255f);
            secondary = new Color(sr / 255f, sg / 255f, sb / 255f);
            this.weight = weight;
            this.restrictedTo = restrictedTo;
        }
    }

    /// <summary>
    /// Fallback palette, used ONLY when the pawn has neither griffin feathers (Gene_AvirFeathers)
    /// nor an Anthrosonae fur gene to ask. Mirrors Compat/Anthrosonae/Defs/FurColorDefs/
    /// AvirGriffinFurColors.xml one-for-one (same RGB, same weights, same two female-only
    /// entries). Duplicated here on purpose: this list has to work when those defs aren't
    /// loaded at all.
    ///
    /// If you retune the palette, change it in ALL THREE places - here, there, and
    /// Defs/FeatherColorDefs/FeatherColors_Avir.xml - or fur and wings will drift apart.
    /// </summary>
    public static class AvirPalette
    {
        private static readonly List<AvirWingColor> Colors = new List<AvirWingColor>
        {
            new AvirWingColor(166, 166, 166, 250, 250, 250, 35f),                   // white
            new AvirWingColor(110, 110, 110, 166, 166, 166, 25f),                   // grey
            new AvirWingColor(178, 118,  87, 237, 227, 217, 10f),                   // brown
            new AvirWingColor( 61,  61,  61, 110, 110, 110, 10f),                   // dark
            new AvirWingColor(221, 213, 198, 249, 248, 246,  3f),                   // golden
            new AvirWingColor( 82,  82,  82, 221, 213, 198,  6.5f),                 // black & gold
            new AvirWingColor(184, 163, 142, 238, 236, 231,  6.5f),                 // pale brown
            new AvirWingColor(231, 222, 229, 250, 250, 250,  2f, Gender.Female),    // pink
            new AvirWingColor(238, 231, 238, 250, 250, 250,  2f, Gender.Female),    // lilac
        };

        /// <summary>
        /// Weighted pick, filtered by gender, seeded off the pawn's ID so the same
        /// pawn always gets the same wings. Called from a render path, so it must not
        /// touch Rand's global state - hence the manual seeded walk instead of
        /// TryRandomElementByWeight.
        /// </summary>
        public static AvirWingColor PickFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            Gender gender = pawn.gender;

            List<AvirWingColor> pool = Colors
                .Where(c => c.restrictedTo == Gender.None || c.restrictedTo == gender)
                .ToList();

            if (pool.Count == 0)
            {
                pool = Colors; // safety net; shouldn't happen with the list above
            }

            float total = pool.Sum(c => c.weight);
            if (total <= 0f)
            {
                return pool[0];
            }

            // Deterministic 0..1 value from the pawn's ID. Mixed with a constant so
            // wing colour doesn't correlate with any other ID-seeded roll on the pawn.
            float roll = (float)((pawn.thingIDNumber * 2654435761u) % 100000u) / 100000f;

            float cursor = roll * total;
            for (int i = 0; i < pool.Count; i++)
            {
                cursor -= pool[i].weight;
                if (cursor <= 0f)
                {
                    return pool[i];
                }
            }

            return pool[pool.Count - 1];
        }
    }
}
