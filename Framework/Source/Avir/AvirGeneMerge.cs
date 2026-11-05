using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// OLD GENES INTO NEW ONES. Several Avir genes were folded together:
    ///
    ///   Avir_ClutchLaying, Avir_EarlyHatch  -> Avir_EggLaying
    ///   Avir_Wings, Avir_Soar               -> Flight_Gene      (the wings gene)
    ///   Avir_Anatomy (RJW), Quadrupede      -> Avir_AvianFrame
    ///   Avir_BatlikeSight                   -> Avir_NightSight
    ///   Avir_EssenceSiphon                  -> Avir_ArcaneEssence
    ///   Avir_EssenceAttuned                 -> Avir_EssenceGlutton
    ///   PredatorMindset                     -> Fearborn
    ///   Avir_GriffinFeathers                -> Avir_FeatheredFur (its gene class changed too)
    ///
    /// Without this a save made before the merge would lose those genes on load ("could not
    /// resolve"), and a pawn that had only Avir_Wings would come out wingless. So:
    ///
    ///   1. NAMES. Every place the game reads a GeneDef name from a save, a custom xenotype file,
    ///      a genepack or a xenogerm goes through BackCompatibility.BackCompatibleDefName; a
    ///      postfix there answers the new name for the old one.
    ///
    ///   2. DOUBLES. A pawn that had Flight_Gene, Avir_Wings and Avir_Soar now has Flight_Gene three
    ///      times. After load the extras are taken out of the lists directly - not through
    ///      RemoveGene, whose PostRemove would strip abilities and hediffs the surviving copy still
    ///      needs. Same for the GeneDef lists of custom xenotypes and gene sets.
    ///
    ///   3. WRONG CLASS. A saved Avir_Wings was a plain Gene; Flight_Gene is a Gene_AvirFlight. If no
    ///      copy of the right class is left, the gene is re-added properly once loading finishes.
    ///
    ///   4. MISSING ABILITIES. A pawn that had Flight but not Soar gains Flight_Soar, and the reverse.
    ///
    /// All of it is a no-op on a save that never had the old genes. Safe to delete this file once
    /// nobody plays a pre-merge save any more.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirGeneMerge
    {
        /// <summary>Old defName -> the gene it now lives in.</summary>
        public static readonly Dictionary<string, string> Merged = new Dictionary<string, string>
        {
            { "Avir_ClutchLaying", "Avir_EggLaying" },
            { "Avir_EarlyHatch",   "Avir_EggLaying" },
            { "Avir_Wings",        "Flight_Gene" },
            { "Avir_Soar",         "Flight_Gene" },
            { "Avir_Anatomy",      "Avir_AvianFrame" },
            { "Quadrupede",        "Avir_AvianFrame" },
            { "Avir_BatlikeSight", "Avir_NightSight" },
            { "Avir_EssenceSiphon",  "Avir_ArcaneEssence" },
            { "Avir_EssenceAttuned", "Avir_EssenceGlutton" },
            { "PredatorMindset",     "Fearborn" },
            { "Avir_GriffinFeathers", "Avir_FeatheredFur" },
        };

        private static HashSet<string> survivors;

        private static HashSet<string> Survivors
        {
            get
            {
                if (survivors == null)
                {
                    survivors = new HashSet<string>(Merged.Values);
                }
                return survivors;
            }
        }

        /// <summary>Called from the mod constructor: touching the class runs its static
        /// constructor (and so the patches) right away, long before StaticConstructorOnStartup.</summary>
        public static void EnsureEarly() { }

        static AvirGeneMerge()
        {
            Harmony harmony = new Harmony("Tenebris.Avir.GeneMerge");
            Patch(harmony, AccessTools.Method(typeof(BackCompatibility), nameof(BackCompatibility.BackCompatibleDefName)), nameof(DefNamePostfix));
            Patch(harmony, AccessTools.Method(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.ExposeData)), nameof(GeneTrackerPostfix));
            Patch(harmony, AccessTools.Method(typeof(CustomXenotype), nameof(CustomXenotype.ExposeData)), nameof(CustomXenotypePostfix));
            Patch(harmony, AccessTools.Method(typeof(GeneSet), nameof(GeneSet.ExposeData)), nameof(GeneSetPostfix));
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodInfo target, string postfix)
        {
            if (target == null)
            {
                Log.Warning("[Avir] Gene merge: a back-compat hook was not found (" + postfix + "); pre-merge saves may lose merged genes.");
                return;
            }
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(AvirGeneMerge), postfix));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Gene merge: failed to patch " + target.Name + ": " + e);
            }
        }

        // ------------------------------------------------------------------ 1. names

        public static void DefNamePostfix(Type defType, ref string __result)
        {
            if (__result == null || defType != typeof(GeneDef))
            {
                return;
            }
            if (Merged.TryGetValue(__result, out string merged))
            {
                __result = merged;
            }
        }

        // ------------------------------------------------------------------ 2-4. pawns

        public static void GeneTrackerPostfix(Pawn_GeneTracker __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance?.pawn == null)
            {
                return;
            }
            try
            {
                FixPawn(__instance);
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Gene merge: could not tidy the genes of " + __instance.pawn.LabelShort + ": " + e);
            }
        }

        private static void FixPawn(Pawn_GeneTracker tracker)
        {
            Pawn pawn = tracker.pawn;
            List<Gene> removed = new List<Gene>();
            List<KeyValuePair<GeneDef, bool>> readd = new List<KeyValuePair<GeneDef, bool>>();

            DedupeList(tracker.Xenogenes, true, removed, readd);
            DedupeList(tracker.Endogenes, false, removed, readd);

            if (removed.Count > 0)
            {
                // Nothing may stay overridden by a gene that no longer exists.
                List<Gene> all = tracker.GenesListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].overriddenByGene != null && removed.Contains(all[i].overriddenByGene))
                    {
                        all[i].overriddenByGene = null;
                    }
                }
                Traverse.Create(tracker).Field("cachedGenes").SetValue(null);
                Traverse.Create(tracker).Method("CheckForOverrides").GetValue();
            }

            bool hasSurvivor = false;
            List<Gene> genes = tracker.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                if (Survivors.Contains(genes[i].def.defName))
                {
                    hasSurvivor = true;
                    break;
                }
            }
            if (readd.Count == 0 && !hasSurvivor)
            {
                return;
            }

            // Abilities and re-adds have to wait until the whole pawn - ability tracker included -
            // has finished loading.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    if (pawn.genes == null)
                    {
                        return;
                    }
                    for (int i = 0; i < readd.Count; i++)
                    {
                        GeneDef def = readd[i].Key;
                        bool xeno = readd[i].Value;
                        List<Gene> list = xeno ? pawn.genes.Xenogenes : pawn.genes.Endogenes;
                        if (!list.Any(g => g.def == def))
                        {
                            pawn.genes.AddGene(def, xeno);
                        }
                    }
                    GrantMissingAbilities(pawn);
                }
                catch (Exception e)
                {
                    Log.Error("[Avir] Gene merge: could not finish " + pawn.LabelShort + ": " + e);
                }
            });
        }

        /// <summary>
        /// Keeps one gene per def. For the merged genes the keeper is a copy of the right class;
        /// if there is none, every copy goes and the def is queued to be added again properly.
        /// </summary>
        private static void DedupeList(List<Gene> list, bool xeno, List<Gene> removed, List<KeyValuePair<GeneDef, bool>> readd)
        {
            if (list == null || list.Count == 0)
            {
                return;
            }
            Dictionary<GeneDef, Gene> keep = new Dictionary<GeneDef, Gene>();
            HashSet<GeneDef> needsReadd = new HashSet<GeneDef>();

            for (int i = 0; i < list.Count; i++)
            {
                Gene g = list[i];
                if (g?.def == null)
                {
                    continue;
                }
                // Only the merged genes are touched; anything else, doubled or not, is not ours.
                if (!Survivors.Contains(g.def.defName))
                {
                    continue;
                }
                bool rightClass = g.def.geneClass == null || g.def.geneClass.IsInstanceOfType(g);
                if (keep.ContainsKey(g.def))
                {
                    continue;
                }
                if (rightClass)
                {
                    keep[g.def] = g;
                }
                else
                {
                    needsReadd.Add(g.def);
                }
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                Gene g = list[i];
                if (g?.def == null || !Survivors.Contains(g.def.defName))
                {
                    continue;
                }
                if (keep.TryGetValue(g.def, out Gene kept) && kept == g)
                {
                    continue;
                }
                if (keep.ContainsKey(g.def) || needsReadd.Contains(g.def))
                {
                    removed.Add(g);
                    list.RemoveAt(i);
                }
            }

            foreach (GeneDef def in needsReadd)
            {
                if (!keep.ContainsKey(def))
                {
                    readd.Add(new KeyValuePair<GeneDef, bool>(def, xeno));
                }
            }
        }

        private static void GrantMissingAbilities(Pawn pawn)
        {
            if (pawn.abilities == null || pawn.genes == null)
            {
                return;
            }
            List<Gene> genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                Gene g = genes[i];
                if (!g.Active || g.def.abilities.NullOrEmpty() || !Survivors.Contains(g.def.defName))
                {
                    continue;
                }
                for (int j = 0; j < g.def.abilities.Count; j++)
                {
                    AbilityDef ability = g.def.abilities[j];
                    if (pawn.abilities.GetAbility(ability) == null)
                    {
                        pawn.abilities.GainAbility(ability);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ 2. def lists

        public static void CustomXenotypePostfix(CustomXenotype __instance)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit && __instance?.genes != null)
            {
                DedupeDefs(__instance.genes);
            }
        }

        public static void GeneSetPostfix(GeneSet __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance == null)
            {
                return;
            }
            List<GeneDef> genes = Traverse.Create(__instance).Field("genes").GetValue<List<GeneDef>>();
            if (genes != null && DedupeDefs(genes))
            {
                Traverse.Create(__instance).Method("DirtyCache").GetValue();
            }
        }

        private static bool DedupeDefs(List<GeneDef> defs)
        {
            HashSet<GeneDef> seen = new HashSet<GeneDef>();
            int before = defs.Count;
            defs.RemoveAll(d => d != null && Survivors.Contains(d.defName) && !seen.Add(d));
            return defs.Count != before;
        }
    }
}
