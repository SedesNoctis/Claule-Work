using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// What the loom is forbidden to make, as data.
    ///
    /// SAME SHAPE AS ImplantExemptions_Avir.xml AND FOR THE SAME REASON: adding a rule should be a
    /// line of XML, not a rebuild. The scopes below are independent and ANY of them matching is
    /// enough to hide an item.
    ///
    /// FOUR SCOPES, BECAUSE FOUR DIFFERENT QUESTIONS:
    ///   excludedDefs        this exact thing
    ///   excludedCategories  this whole branch of the category tree, subtrees included
    ///   excludedStuff       anything MADE OF this - a Cloudcloth duster is a vanilla def that
    ///                       appears in no list, so blocking the material is a separate rule from
    ///                       blocking the item, and forgetting it leaves the hole wide open
    ///   excludedMods        everything from a packageId
    /// </summary>
    public class AvirCrafterExclusionDef : Def
    {
        /// EVERYTHING HERE IS A STRING, NOT A TYPED Def REFERENCE, AND THAT IS DELIBERATE.
        ///
        /// A List<ThingDef> makes RimWorld resolve every entry at load and log a cross-reference
        /// error for any it cannot find. But a blocklist has to name things from DLCs and mods that
        /// may not be installed - PsylinkNeuroformer needs Royalty, HemogenPack needs Biotech - and
        /// a player without Royalty would get a red error for a rule that was never going to matter
        /// to them. Resolving by name with GetNamedSilentFail means an absent def is simply a rule
        /// with nothing to apply to, which is the correct behaviour.
        ///
        /// The cost is that a TYPO is also silent. Use the debug action to print what resolved.
        public List<string> excludedDefs = new List<string>();
        public List<string> excludedCategories = new List<string>();
        public List<string> excludedStuff = new List<string>();
        public List<string> excludedMods = new List<string>();
    }

    [StaticConstructorOnStartup]
    public static class AvirCrafterFilter
    {
        private static HashSet<ThingDef> defs;
        private static HashSet<ThingCategoryDef> categories;
        private static HashSet<ThingDef> stuff;
        private static HashSet<string> mods;

        private static void Build()
        {
            if (defs != null) return;

            defs = new HashSet<ThingDef>();
            categories = new HashSet<ThingCategoryDef>();
            stuff = new HashSet<ThingDef>();
            mods = new HashSet<string>();

            int resolved = 0, missing = 0;

            foreach (AvirCrafterExclusionDef d in DefDatabase<AvirCrafterExclusionDef>.AllDefsListForReading)
            {
                Resolve(d.excludedDefs, defs, ref resolved, ref missing);
                Resolve(d.excludedStuff, stuff, ref resolved, ref missing);

                if (d.excludedMods != null)
                {
                    foreach (string m in d.excludedMods) mods.Add(m.ToLowerInvariant());
                }

                if (d.excludedCategories == null) continue;
                foreach (string name in d.excludedCategories)
                {
                    ThingCategoryDef cat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(name);
                    if (cat?.ThisAndChildCategoryDefs == null) { missing++; continue; }

                    // Listing a parent covers everything beneath it. ThingCategoryDef already
                    // exposes that closure, so there is no reason to walk the tree by hand and get
                    // the recursion subtly wrong.
                    foreach (ThingCategoryDef c in cat.ThisAndChildCategoryDefs) categories.Add(c);
                    resolved++;
                }
            }

            Log.Message($"[Avir] Loom exclusions: {resolved} rules resolved, {missing} named nothing "
                        + "(absent DLC or mod, or a typo).");
        }

        private static void Resolve(List<string> names, HashSet<ThingDef> into, ref int ok, ref int bad)
        {
            if (names == null) return;
            foreach (string n in names)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(n);
                if (def != null) { into.Add(def); ok++; }
                else bad++;
            }
        }

        /// <summary>
        /// Rules that will never want changing, kept in code rather than in the XML so nobody can
        /// switch them off by accident. Every one of these produces a broken item rather than an
        /// overpowered one.
        /// </summary>
        private static bool StructurallyImpossible(ThingDef def)
        {
            if (def == null) return true;
            if (def.category != ThingCategory.Item) return true;
            if (def.IsCorpse) return true;
            if (def.destroyOnDrop) return true;
            if (def.BaseMarketValue <= 0f) return true;
            if (def.thingClass == null) return true;
            if (typeof(MinifiedThing).IsAssignableFrom(def.thingClass)) return true;
            if (typeof(UnfinishedThing).IsAssignableFrom(def.thingClass)) return true;
            if (def.isFrameInt || def.IsBlueprint) return true;
            return false;
        }

        public static bool CanCraft(ThingDef def)
        {
            if (StructurallyImpossible(def)) return false;

            Build();

            // A TRANSMUTATION ENTRY OVERRIDES THE BLOCKLIST. Silver and gold are forbidden as
            // things woven out of electricity and permitted as things exchanged for Feignallite -
            // the ban is on the printing press, not on the metal. An entry in the transmute file is
            // a deliberate, priced exception, so it wins.
            if (AvirTransmute.IsTransmutable(def)) return true;

            if (defs.Contains(def)) return false;

            if (def.modContentPack != null
                && mods.Contains(def.modContentPack.PackageId.ToLowerInvariant()))
            {
                return false;
            }

            if (def.thingCategories != null)
            {
                for (int i = 0; i < def.thingCategories.Count; i++)
                {
                    if (categories.Contains(def.thingCategories[i])) return false;
                }
            }

            return true;
        }

        /// <summary>Separate question from CanCraft. A perfectly craftable vanilla item becomes
        /// forbidden the moment the player picks an Avir material for it.</summary>
        public static bool CanUseStuff(ThingDef stuffDef)
        {
            if (stuffDef == null) return true;
            Build();
            return !stuff.Contains(stuffDef);
        }

        /// <summary>
        /// Is this one of the materials the colony grows, mines or weaves for itself?
        ///
        /// Reads the excludedStuff list, which already names exactly those - Feignallite,
        /// Cloudcloth, Bloodstone Tisserium, Essentium and the two hides. The dissolution engine
        /// needs the question because a material the colony can produce must never pay the full
        /// rate: at 50 Wd per point of value, Feignallite alone would be worth 900 Wd a unit and
        /// a Feignallite farm would out-earn every other power source in the game.
        /// </summary>
        public static bool IsAvirMaterial(ThingDef def)
        {
            if (def == null) return false;
            Build();
            return stuff.Contains(def);
        }
    }
}
