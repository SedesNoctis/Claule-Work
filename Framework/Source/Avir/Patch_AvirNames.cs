using System.Collections.Generic;
using System.Reflection;
using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace Avir
{
    [DefOf]
    public static class AvirNameDefOf
    {
        // The root Avir gene. Anything carrying it is an Avir, whatever its culture.
        public static GeneDef Fearborn;

        public static RulePackDef Avir_NamerPersonMale;

        /// <summary>A'Schialis men: bare stems, no prefix - the prefix is an A'Shaign custom.</summary>
        public static RulePackDef Avir_NamerPersonMaleLegion;
        public static RulePackDef Avir_NamerPersonFemale;
        public static RulePackDef Avir_NamerPersonLast;

        static AvirNameDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirNameDefOf));
        }
    }

    /// <summary>
    /// Any pawn generated with the Fearborn gene is renamed with an Avir name, ignoring the
    /// culture/faction name bank entirely.
    ///
    /// Runs as a PREFIX that skips vanilla generation outright (rather than a postfix that lets
    /// vanilla run first and overwrites its result). Culture_Avian.xml points pawnNameMaker /
    /// pawnNameMakerFemale straight at our own rule packs, so vanilla's own call was generating
    /// (and validating, and sometimes failing to validate - hence the "Could not get new name"
    /// errors in the log) a name THAT WE THREW AWAY every single time. Skipping it outright fixes
    /// the log spam and stops every Avir pawn paying for two name generations instead of one.
    ///
    /// Family lines are preserved: if the game passes a forcedLastName (children, relatives),
    /// that surname is kept and only the given name is Avir-ised.
    /// </summary>
    [HarmonyPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.GeneratePawnName))]
    public static class Patch_AvirNames
    {
        public static bool Prefix(ref Name __result,
                                   [HarmonyArgument(0)] Pawn pawn,
                                   [HarmonyArgument(1)] NameStyle style,
                                   [HarmonyArgument(2)] string forcedLastName)
        {
            try
            {
                if (style != NameStyle.Full) return true;
                if (pawn == null) return true;
                if (!AvirFaction.ShouldUseAvirContentAtGeneration(pawn)) return true;

                Name name = GenerateAvirName(pawn, forcedLastName);
                if (name == null) return true; // our generator failed - let vanilla have a go

                __result = name;
                return false; // skip vanilla entirely, we already have a name
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Avir name generation failed, falling back to the culture name: " + e, 0x4A51B2);
                return true;
            }
        }

        private static HashSet<string> avirSurnames;

        /// <summary>
        /// The set of surnames the Avir namer can produce, used to tell an inherited Avir family
        /// name (keep it) from a leftover human one (replace it).
        ///
        /// RulePackDef's internals are not publicly reachable and their names move between
        /// versions, so this does NOT bind to them at compile time. It reflects the rule strings
        /// out at runtime if it can, and regardless records every surname we actually hand out.
        /// Worst case - reflection finds nothing - the set still fills as Avir get generated, and
        /// the only cost is that a surname inherited across a save reload may be regenerated.
        /// </summary>
        public static bool IsAvirSurname(string last)
        {
            if (last.NullOrEmpty()) return false;

            EnsureSurnamesCached();
            return avirSurnames.Contains(last);
        }

        /// <summary>Remembers a surname we produced, so it counts as a family name later.</summary>
        public static void RegisterSurname(string last)
        {
            if (last.NullOrEmpty()) return;

            EnsureSurnamesCached();
            avirSurnames.Add(last);
        }

        private static void EnsureSurnamesCached()
        {
            if (avirSurnames != null) return;
            avirSurnames = new HashSet<string>();

            try
            {
                RulePackDef pack = AvirNameDefOf.Avir_NamerPersonLast;
                if (pack == null) return;

                // Find the RulePack member whatever it happens to be called, then the string list
                // inside it. Nothing here is bound at compile time.
                object rulePack = null;
                foreach (FieldInfo f in AccessTools.GetDeclaredFields(typeof(RulePackDef)))
                {
                    if (f.FieldType.Name == "RulePack")
                    {
                        rulePack = f.GetValue(pack);
                        break;
                    }
                }

                if (rulePack == null) return;

                foreach (FieldInfo f in AccessTools.GetDeclaredFields(rulePack.GetType()))
                {
                    List<string> rules = f.GetValue(rulePack) as List<string>;
                    if (rules == null) continue;

                    foreach (string rule in rules)
                    {
                        if (rule == null) continue;

                        int arrow = rule.IndexOf("->");
                        if (arrow < 0) continue;

                        string keyword = rule.Substring(0, arrow).Trim();
                        if (keyword != "talent" && keyword != "family") continue;

                        avirSurnames.Add(rule.Substring(arrow + 2).Trim());
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Could not pre-read the Avir surname list (" + e.Message
                                + "); surnames will still be tracked as they are generated.", 0x4A51BB);
            }
        }

        /// <summary>
        /// How many leading letters two names may share before they read as the same word twice.
        ///
        /// Four catches "Kharriss" against "Kharreseign" - the case that prompted this - while
        /// leaving "Kha'Zorraal Vhaskria" alone. Comparison starts AFTER the apostrophe, because
        /// the prefix before it is a caste marker rather than part of the name: half the Avir alive
        /// share "Kha", and rejecting all of them against each other would empty the pool.
        /// </summary>
        private const int SharedRootLength = 4;

        private static bool SharesRoot(string first, string last)
        {
            if (first.NullOrEmpty() || last.NullOrEmpty()) return false;

            // The stem is what follows the apostrophe, when there is one.
            int apostrophe = first.IndexOf('\'');
            string stem = apostrophe >= 0 && apostrophe + 1 < first.Length
                ? first.Substring(apostrophe + 1)
                : first;

            if (stem.Length < SharedRootLength || last.Length < SharedRootLength) return false;

            return string.Equals(stem.Substring(0, SharedRootLength),
                                 last.Substring(0, SharedRootLength),
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A'Schialis, by faction or - while the faction is not yet set - by the pawnkind's home
        /// faction. Male name prefixes are an A'Shaign tradition the Legion does not keep.
        /// </summary>
        public static bool IsLegion(Pawn pawn)
        {
            const string Legion = "Avir_AShialis";
            if (pawn?.Faction?.def != null) return pawn.Faction.def.defName == Legion;
            return pawn?.kindDef?.defaultFactionDef?.defName == Legion;
        }

        public static Name GenerateAvirName(Pawn pawn, string forcedLastName)
        {
            RulePackDef firstPack = (pawn.gender == Gender.Female)
                ? AvirNameDefOf.Avir_NamerPersonFemale
                : (IsLegion(pawn) && AvirNameDefOf.Avir_NamerPersonMaleLegion != null
                    ? AvirNameDefOf.Avir_NamerPersonMaleLegion
                    : AvirNameDefOf.Avir_NamerPersonMale);

            if (firstPack == null || AvirNameDefOf.Avir_NamerPersonLast == null) return null;

            NameTriple fallback = null;

            // Try a few times for a name that isn't already in use this game.
            for (int i = 0; i < 40; i++)
            {
                string first = NameGenerator.GenerateName(firstPack);
                string last = forcedLastName.NullOrEmpty()
                    ? NameGenerator.GenerateName(AvirNameDefOf.Avir_NamerPersonLast)
                    : forcedLastName;

                // The nickname is the given name - Avir answer to what they were called, not to a
                // shortening of it.
                RegisterSurname(last);
                NameTriple candidate = new NameTriple(first, first, last);
                if (fallback == null) fallback = candidate;

                // "Kha'Kharriss Kharreseign". BOTH HALVES GREW FROM THE SAME ROOT, and the loop
                // above had no reason to notice: it only ever asked whether the full name was
                // already taken, and that one was not.
                //
                // The rule pack shares its syllables between the given-name stems and the family
                // list on purpose - it is one language, and a surname that sounds unrelated to
                // every first name would be the worse fault. What it cannot do is put two of them
                // side by side.
                //
                // So the pairing is rejected here rather than the syllables being separated there.
                // It costs one more spin of a loop that already runs up to forty times, and the
                // fallback still returns something if a language this small somehow cannot produce
                // an unrelated pair.
                if (SharesRoot(first, last)) continue;

                if (!candidate.UsedThisGame) return candidate;
            }

            return fallback;
        }
    }
}
