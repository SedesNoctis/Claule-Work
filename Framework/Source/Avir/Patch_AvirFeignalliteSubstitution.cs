using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Feignallite as a material substitute in recipes - the same idea as Raw Matter's
    /// RecipeFuckery, but driven by a BLACKLIST rather than applying to everything.
    ///
    /// RULE: Feignallite is added to an ingredient's filter only if NONE of the things that
    /// ingredient accepts are blacklisted. A single blacklisted entry skips the whole recipe,
    /// not just that one line - otherwise a recipe like "cloth 50 + steel 20" would let
    /// Feignallite in through the steel line, and the textile ban could be walked around
    /// sideways within the same recipe.
    ///
    /// BLOCKED (categories):
    ///   Textiles, Medicine, Drugs - under Manufactured
    ///   PlantMatter               - under Raw Resources
    /// BLOCKED (specific things):
    ///   WoodLog, Timber (Medieval Overhaul), Essentium, Tisserium and the other Avir materials
    ///
    /// SKIPPED: all of artifactorics - recipes of the artificer's table and any amulet recipe.
    ///
    /// Everything else is allowed - Feignallite works as a component for anything not listed.
    ///
    /// Missing defs (Timber when Medieval Overhaul isn't installed) are simply ignored -
    /// GetNamedSilentFail won't crash the game over them.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirFeignalliteSubstitution
    {
        // ---- blocked CATEGORIES ----
        private static readonly string[] BlockedCategoryDefNames =
        {
            "Textiles",     // cloth, leathers, synthread, devilstrand
            "Medicine",     // every medicine tier
            "Drugs",        // all drugs and pharmaceuticals
            "PlantMatter",  // raw plant material
            "Corpses",      // shred/butcher/cremate style recipes
        };

        // ---- blocked SPECIFIC things ----
        private static readonly string[] BlockedThingDefNames =
        {
            // wood - vanilla and Medieval Overhaul
            "WoodLog",
            "Timber",
            "DGE_Timber",

            // Avir materials: earned through work, not swapped out for stone
            "Essentium",
            "BloodstoneTisserium",
            "Cloudcloth",
            "Leather_AvirFeatheredFur",
            "Avir_EssenceExtract",

            // Feignallite itself - so it never substitutes for itself
            "Avir_Feignallite",
        };

        private static bool IsArtifactorics(RecipeDef recipe)
        {
            if (recipe.GetModExtension<Artifactorics.ArtifactRecipeExtension>() != null) return true;
            if (recipe.AllRecipeUsers.Any(u => u.defName == "Avir_ArtificerSpot" || u.defName == "Avir_ArtificerRoundTable")) return true;
            ThingDef product = recipe.ProducedThingDef;
            return product != null && product.GetCompProperties<CompProperties_Telekinesis>() != null;
        }

        static Patch_AvirFeignalliteSubstitution()
        {
            // Global toggle from Options -> Mod settings -> Avir. Checked once here, which is
            // why changing it needs a restart: recipe filters are rebuilt at load time and
            // there's nothing sensible to un-patch mid-game.
            if (AvirMod.Settings != null && !AvirMod.Settings.feignalliteSubstitution)
            {
                Log.Message("[Avir] Feignallite substitution disabled in mod settings - skipped.");
                return;
            }

            ThingDef feignallite = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Feignallite");
            if (feignallite == null)
            {
                Log.Warning("[Avir] Avir_Feignallite not found - substitution system did not run.");
                return;
            }

            HashSet<ThingDef> blocked = BuildBlockedSet();

            int touchedRecipes = 0;
            int touchedIngredients = 0;
            int repopulatedFixedFilters = 0; // see FIX 2026-09-13 below

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.ingredients == null || recipe.ingredients.Count == 0) continue;

                // Recipes where the product is made OUT OF the ingredient (a steel wall, cloth
                // apparel) are skipped. Feignallite has no stuffProps, so it can't legally be a
                // Stuff material, and the product would end up with an invalid material.
                // Replicator checks this same field, almost certainly for the same reason.
                if (recipe.productHasIngredientStuff) continue;

                // Artifactorics never takes feignallite (Oct 4, 2026): nothing made at the
                // artificer's table (artifacts, stones, amulet necklaces and cores, named amulets)
                // and no amulet made anywhere. Its unfinished things are made of the metal, and
                // feignallite, which is no stuff, left them without one.
                if (IsArtifactorics(recipe)) continue;

                // First pass over the WHOLE recipe: does any ingredient accept a blocked thing?
                bool recipeHasBlocked = false;
                foreach (IngredientCount ing in recipe.ingredients)
                {
                    if (ing?.filter == null) continue;
                    if (ing.filter.AllowedThingDefs.Any(blocked.Contains))
                    {
                        recipeHasBlocked = true;
                        break;
                    }
                }
                if (recipeHasBlocked) continue;

                bool changedThisRecipe = false;
                foreach (IngredientCount ing in recipe.ingredients)
                {
                    if (ing?.filter == null) continue;

                    List<ThingDef> allowed = ing.filter.AllowedThingDefs.ToList();
                    if (allowed.Count == 0) continue;
                    if (allowed.Contains(feignallite)) continue;

                    // Structural guard: substitution only makes sense where the ingredient is
                    // BULK MATERIAL - something measured in units and kept in stacks. Steel,
                    // plasteel and components stack to 75; a corpse, a weapon or a piece of
                    // apparel has stackLimit 1 and is a discrete object, not a material.
                    //
                    // This is what keeps feignallite out of shred / butcher / smelt / disassemble
                    // recipes across every mod at once, without matching on recipe names.
                    // Name matching would have been fragile - it would catch "Shred android" and
                    // miss "Salvage", "Dismantle", "Butcher", or anything in another language.
                    if (!allowed.All(IsBulkMaterial)) continue;

                    ing.filter.SetAllow(feignallite, true);
                    touchedIngredients++;
                    changedThisRecipe = true;
                }

                if (changedThisRecipe)
                {
                    // fixedIngredientFilter is not just an ingredient restriction for the
                    // crafting check - Dialog_BillConfig / ITab_Bills also use it as the
                    // "universe" list it draws checkboxes for in the bill's ingredient panel.
                    // A recipe with zero entries there reads as "unrestricted" for crafting
                    // purposes, but reads as "nothing to list" for that panel, which is why an
                    // affected bill's config dialog can show an empty checkbox column even
                    // though the recipe itself still knows about its original materials.
                    //
                    // Previously we left it alone whenever it was empty, to avoid an earlier bug
                    // (SetAllow on an empty filter turned "anything goes" into "exactly
                    // Feignallite and nothing else"). That earlier fix was correct as far as it
                    // went, but "leave it alone" isn't the same as "leave it correct" - an empty
                    // filter is still empty, and still renders as an empty checklist.
                    //
                    // HOTFIX: when it's empty (not null), repopulate it with the union of what
                    // every ingredient line on this recipe now allows - the original materials
                    // plus Feignallite - instead of leaving it blank. That keeps it exactly as
                    // unrestricted as before (nothing not already reachable through
                    // ingredients[].filter gets added), while giving the bill panel a real list
                    // to draw checkboxes for. A non-empty fixedIngredientFilter is left on the
                    // original path: just add Feignallite alongside whatever it already allowed.
                    //
                    // defaultIngredientFilter is still never touched. That's what gets copied
                    // into a new bill's own (checked/unchecked) selection on creation, and
                    // that part was never empty here - it's the universe list, not the
                    // selection, that was the problem.
                    if (recipe.fixedIngredientFilter != null)
                    {
                        // FIX 2026-09-13: always union, never just "add Feignallite".
                        //
                        // An ingredient line that allows exactly ONE ThingDef is a FIXED
                        // ingredient, and the engine accepts it without ever consulting
                        // fixedIngredientFilter:
                        //
                        //   if (ing.IsFixedIngredient && ing.filter.Allows(t)) return true;
                        //   return recipe.fixedIngredientFilter.Allows(t) && bill...Allows(t);
                        //
                        // The moment we add Feignallite to such a line it holds two defs, stops
                        // being fixed, and falls through to fixedIngredientFilter - which, for
                        // that material, was never populated, because it never had to be. The
                        // original material silently becomes unusable and vanishes from the
                        // bill's checkbox list.
                        //
                        // Medieval Overhaul's "Refine iron ingots into steel ingots" is the case
                        // that surfaced this: 60 iron ingot + 60 coal chunks, fixedIngredientFilter
                        // holding only Metallics. Coal chunks became unreachable with thousands in
                        // storage. Any recipe in any mod with a single-material line outside its
                        // fixedIngredientFilter breaks the same way.
                        //
                        // The old code only unioned when the filter was EMPTY, so this recipe took
                        // the other branch and got Feignallite alone. Unioning unconditionally adds
                        // nothing that wasn't already reachable through ingredients[].filter, and
                        // covers both shapes at once.
                        HashSet<ThingDef> union = new HashSet<ThingDef>();
                        foreach (IngredientCount ic in recipe.ingredients)
                        {
                            if (ic?.filter == null) continue;
                            foreach (ThingDef d in ic.filter.AllowedThingDefs)
                                union.Add(d);
                        }
                        foreach (ThingDef d in union)
                            recipe.fixedIngredientFilter.SetAllow(d, true);

                        repopulatedFixedFilters++;
                    }

                    touchedRecipes++;
                }
            }

            Log.Message("[Avir] Feignallite registered as a substitute in " + touchedRecipes
                + " recipes (" + touchedIngredients + " ingredient lines, "
                + repopulatedFixedFilters + " fixedIngredientFilters unioned back up).");
        }

        /// <summary>
        /// True for things that behave like bulk material: stackable, not a corpse, not a
        /// discrete object. This is the structural test that keeps feignallite out of
        /// destruction and salvage recipes (shred, butcher, smelt, dismantle) regardless of
        /// what those recipes are called or which mod they come from.
        /// </summary>
        private static bool IsBulkMaterial(ThingDef def)
        {
            if (def == null) return false;
            if (def.stackLimit <= 1) return false;   // corpses, weapons, apparel, bionics
            if (def.IsCorpse) return false;          // belt and braces
            return true;
        }

        private static HashSet<ThingDef> BuildBlockedSet()
        {
            HashSet<ThingDef> blocked = new HashSet<ThingDef>();

            foreach (string catName in BlockedCategoryDefNames)
            {
                ThingCategoryDef cat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(catName);
                if (cat == null) continue; // category may not exist - harmless
                foreach (ThingDef def in cat.DescendantThingDefs)
                    blocked.Add(def);
            }

            foreach (string thingName in BlockedThingDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(thingName);
                if (def != null) blocked.Add(def);
            }

            return blocked;
        }
    }
}
