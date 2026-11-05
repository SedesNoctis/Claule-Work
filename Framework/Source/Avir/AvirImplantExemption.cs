using System;
using System.Collections.Generic;
using Verse;

namespace Avir
{
    /// <summary>Where an exemption applies. The two lists are NOT the same rule.</summary>
    public enum AvirExemptScope
    {
        /// <summary>Survives Patch_AvirBionics.Strip - the Avir will not tear it off at
        /// generation or spawn. Says nothing about fire.</summary>
        GenerationLock,

        /// <summary>Survives Essence of Phoenix. Machinery does not qualify for this one no
        /// matter how culturally acceptable it is.</summary>
        PhoenixFire,

        Both,
    }

    /// <summary>
    /// AN EXEMPTION LIST YOU CAN EDIT WITHOUT REBUILDING THE DLL.
    ///
    /// THE PROBLEM THIS SOLVES. The exemptions used to be hardcoded to psylink, matched by class
    /// and by the literal string "Psylink". That breaks in three separate ways as soon as more
    /// than one link-style implant exists:
    ///
    ///   1. Vanilla's own mechlink is not psylink and was not covered.
    ///   2. A third-party mod adding its own mechlink analogue under some other name is
    ///      invisible to a hardcoded list.
    ///   3. A mod that RENAMES psylink - "arcane link" - defeats a defName string match
    ///      outright.
    ///
    /// Four independent ways to match, any one of which is enough:
    ///
    ///   defNames      exact, case-sensitive. For one specific implant.
    ///   namePatterns  case-insensitive SUBSTRING of the defName. This is the blunt one.
    ///   hediffTags    matches HediffDef.tags. The cleanest hook when a mod provides one -
    ///                 this is how magical tattoos are recognised.
    ///   classNames    matched against hediffClass AND every base class above it. Survives
    ///                 renames completely: a relabelled psylink still derives from
    ///                 Hediff_Psylink, whatever its def is called.
    ///
    /// ON namePatterns AND THE WORD "link", SINCE IT WAS ASKED FOR DIRECTLY. A bare substring
    /// match on "link" is broad. It will catch anything with those four letters in its defName -
    /// a "Blink" teleport implant, a "Linkage" prosthetic, whatever some mod calls its cufflink
    /// joke item. Worth doing anyway, because of the direction the failure runs: a false
    /// positive means an implant is KEPT that arguably should have been stripped. That is a mild
    /// lore inconsistency on one pawn. A false negative means a mechanitor loses their mechlink
    /// on the next map load and their whole mech army with it. Those are not comparable, so the
    /// blunt match is the correct trade - but it is a trade, and it is made deliberately here
    /// rather than by accident.
    /// </summary>
    public class AvirImplantExemptionDef : Def
    {
        public AvirExemptScope scope = AvirExemptScope.Both;
        public List<string> defNames = new List<string>();
        public List<string> namePatterns = new List<string>();
        public List<string> hediffTags = new List<string>();
        public List<string> classNames = new List<string>();
    }

    [StaticConstructorOnStartup]
    public static class AvirImplantExemption
    {
        private static bool built;
        private static readonly List<AvirImplantExemptionDef> lockList = new List<AvirImplantExemptionDef>();
        private static readonly List<AvirImplantExemptionDef> fireList = new List<AvirImplantExemptionDef>();

        // HediffDef -> answer. Resolved once per def per scope; the strip runs on every spawn of
        // every Avir, so this is worth caching.
        private static readonly Dictionary<HediffDef, bool> lockCache = new Dictionary<HediffDef, bool>();
        private static readonly Dictionary<HediffDef, bool> fireCache = new Dictionary<HediffDef, bool>();

        private static void Build()
        {
            if (built) return;
            built = true;

            foreach (AvirImplantExemptionDef d in DefDatabase<AvirImplantExemptionDef>.AllDefsListForReading)
            {
                if (d.scope == AvirExemptScope.GenerationLock || d.scope == AvirExemptScope.Both) lockList.Add(d);
                if (d.scope == AvirExemptScope.PhoenixFire || d.scope == AvirExemptScope.Both) fireList.Add(d);
            }

            Log.Message("[Avir] Implant exemptions loaded: " + lockList.Count
                + " for the generation lock, " + fireList.Count + " for phoenix fire.");
        }

        /// <summary>Exempt from Patch_AvirBionics.Strip.</summary>
        public static bool ExemptFromLock(HediffDef def)
        {
            return Check(def, lockList, lockCache);
        }

        /// <summary>Exempt from Essence of Phoenix.</summary>
        public static bool ExemptFromFire(HediffDef def)
        {
            return Check(def, fireList, fireCache);
        }

        private static bool Check(HediffDef def, List<AvirImplantExemptionDef> rules,
                                  Dictionary<HediffDef, bool> cache)
        {
            if (def == null) return false;
            Build();

            bool cached;
            if (cache.TryGetValue(def, out cached)) return cached;

            bool result = false;
            for (int i = 0; i < rules.Count && !result; i++)
            {
                result = Matches(def, rules[i]);
            }

            cache[def] = result;
            return result;
        }

        private static bool Matches(HediffDef def, AvirImplantExemptionDef rule)
        {
            string name = def.defName ?? "";

            if (rule.defNames != null && rule.defNames.Contains(name)) return true;

            if (rule.namePatterns != null)
            {
                for (int i = 0; i < rule.namePatterns.Count; i++)
                {
                    string p = rule.namePatterns[i];
                    if (!string.IsNullOrEmpty(p)
                        && name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            if (rule.hediffTags != null && def.tags != null)
            {
                for (int i = 0; i < rule.hediffTags.Count; i++)
                {
                    if (def.tags.Contains(rule.hediffTags[i])) return true;
                }
            }

            // Walk the whole inheritance chain, not just the declared class. This is what makes
            // a renamed or reimplemented psylink still register: whatever the def is called, the
            // class above it is still Hediff_Psylink or Hediff_ImplantWithLevel.
            if (rule.classNames != null && rule.classNames.Count > 0 && def.hediffClass != null)
            {
                Type t = def.hediffClass;
                while (t != null)
                {
                    for (int i = 0; i < rule.classNames.Count; i++)
                    {
                        if (string.Equals(t.Name, rule.classNames[i], StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    t = t.BaseType;
                }
            }

            return false;
        }
    }
}
