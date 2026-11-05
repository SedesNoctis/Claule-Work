using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Avir backstories are locked to Avir. Any pawn with the Fearborn gene gets a childhood
    /// (and, if adult, an adulthood) drawn only from the "Avir" spawn categories, whatever their
    /// faction or culture; any pawn WITHOUT Fearborn can never roll one.
    ///
    /// Gender is handled here rather than in XML, since BackstoryDef has no gender field:
    ///   Avir        - either gender
    ///   AvirMale    - male only
    ///   AvirFemale  - female only
    ///
    /// This runs as a postfix on GiveAppropriateBioAndNameTo, which the generator calls BEFORE
    /// it rolls skills - so the swapped-in backstory's skillGains and workDisables apply normally.
    /// </summary>
    [HarmonyPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo))]
    public static class Patch_AvirBackstories
    {
        public const string CategoryAny = "Avir";
        public const string CategoryMale = "AvirMale";
        public const string CategoryFemale = "AvirFemale";

        // The caste categories. These are NOT decoration - a caste backstory carries its caste
        // category and NOTHING else (see Backstories_Avir_Castes.xml), so anything that wants to
        // know "is this one of ours" has to know about them too. It did not, and that was the bug:
        // IsAvirBackstory said no, the hourly sweep in GameComponent_AvirHatchling said "must be
        // faction rubbish, re-roll it", and every caste pawn in the colony had their history
        // rewritten once an hour, forever, while their skills stayed where the last roll left them.
        public const string CategoryTalon = "AvirTalon";
        public const string CategoryArtificer = "AvirArtificer";
        public const string CategoryGuild = "AvirGuild";
        public const string CategoryRegal = "AvirRegal";
        public const string CategoryMatriarch = "AvirMatriarch";
        public const string CategoryPriestess = "AvirPriestess";
        public const string CategoryHuntress = "AvirHuntress";
        public const string CategoryPredator = "AvirPredator";
        public const string CategoryStrider = "AvirStrider";
        public const string CategoryArchitect = "AvirArchitect";
        public const string CategoryArtist = "AvirArtist";

        // A'SCHIALIS LEGION. Shared childhood pool (AvirLegion) plus the two
        // rank-exclusive adulthood pools, mirroring Regal/Matriarch/Priestess.
        public const string CategoryLegion = "AvirLegion";
        public const string CategoryInquisitor = "AvirInquisitor";
        public const string CategoryHighlord = "AvirHighlord";

        // A'SCHIALIS LEGION RANKS. Centurion/Legionnaire used to fall through to
        // the plain AvirTalon (warrior) pool - see Backstories_Avir_LegionRanks.xml.
        // They now have their own shared childhood pool plus a rank-exclusive
        // adulthood pool each, mirroring Inquisitor/Highlord above.
        public const string CategoryLegionChild = "AvirLegionChild";
        public const string CategoryCenturion = "AvirCenturion";
        public const string CategoryLegionnaireRank = "AvirLegionnaire";

        // UNIQUE PAWNS. A dedicated pool for the three named characters (Falleign, Minthirrie,
        // Alpheir). Their backstories carry this category so the hourly re-roll sweep leaves them
        // alone. The pool is never offered to random pawns - it only exists to satisfy the sweep.
        public const string CategoryUnique = "AvirUnique";

        // THE PRIMAL. The wild hunt: their own childhoods (shared by the Matriarch) and adulthoods,
        // the Matriarch's adulthoods apart - see Backstories_Avir_Primal.xml.
        public const string CategoryPrimal = "AvirPrimal";
        public const string CategoryPrimalMatriarch = "AvirPrimalMatriarch";

        /// <summary>Every spawn category that means "this backstory belongs to the Avir".</summary>
        private static readonly HashSet<string> AvirCategories = new HashSet<string>
        {
            CategoryAny, CategoryMale, CategoryFemale,
            CategoryTalon, CategoryArtificer, CategoryGuild,
            CategoryRegal, CategoryMatriarch, CategoryPriestess,
            CategoryHuntress, CategoryPredator, CategoryStrider,
            CategoryArchitect, CategoryArtist,
            CategoryLegion, CategoryInquisitor, CategoryHighlord,
            CategoryLegionChild, CategoryCenturion, CategoryLegionnaireRank,
            CategoryUnique,
            CategoryPrimal, CategoryPrimalMatriarch,
            "AvirArtificer_Shaign", "AvirArtist_Shaign", "AvirArchitect_Shaign",
            "AvirArtificer_Legion", "AvirArtist_Legion", "AvirArchitect_Legion",
        };

        // THE FACTIONS' OWN MAKERS. The A'Shaign and the Legion each have their own artificer, artist and
        // architect adulthoods (Backstories_Avir_MakersShaign.xml / _MakersLegion.xml); a maker of that faction
        // takes one of them half the time, the shared pool the rest.
        private static readonly Dictionary<string, string> MakerCategory = new Dictionary<string, string>
        {
            { "Avir_Artificer", "AvirArtificer" }, { "Avir_Artist", "AvirArtist" }, { "Avir_Architect", "AvirArchitect" },
        };

        private static BackstoryDef FactionMakerAdulthood(Pawn pawn, string kindName)
        {
            if (kindName == null || !MakerCategory.TryGetValue(kindName, out string cat)) return null;
            string f = pawn.Faction?.def?.defName;
            string suffix = f == "Avir_HighNesting" ? "_Shaign" : f == "Avir_AShialis" ? "_Legion" : null;
            if (suffix == null || !Rand.Chance(0.5f)) return null;
            List<BackstoryDef> pool = InCategory(cat + suffix, BackstorySlot.Adulthood, pawn.gender);
            return pool.Count == 0 ? null : pool[Rand.Range(0, pool.Count)];
        }

        private static List<BackstoryDef> childhoodsAny, childhoodsMale, childhoodsFemale;
        private static List<BackstoryDef> adulthoodsAny, adulthoodsMale, adulthoodsFemale;

        public static void Postfix([HarmonyArgument(0)] Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.story == null) return;
                if (!AvirFaction.ShouldUseAvirContentAtGeneration(pawn)) return;

                EnsureCached();

                string kindName = pawn.kindDef != null ? pawn.kindDef.defName : null;

                BackstoryDef childhood = PickAvirBackstory(BackstorySlot.Childhood, pawn.gender, null, kindName);
                if (childhood != null) SetBackstory(pawn.story, "Childhood", "childhood", childhood);

                // Children have no adulthood yet - don't invent one.
                if (pawn.story.Adulthood != null)
                {
                    BackstoryDef adulthood = FactionMakerAdulthood(pawn, kindName)
                                             ?? PickAvirBackstory(BackstorySlot.Adulthood, pawn.gender, childhood, kindName);
                    if (adulthood != null) SetBackstory(pawn.story, "Adulthood", "adulthood", adulthood);
                }

                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Avir backstory assignment failed, keeping the vanilla one: " + e, 0x4A51B3);
            }
        }

        /// <summary>Public entry point used by the hatchling/growth-moment patches.</summary>
        public static BackstoryDef PickAvirBackstory(BackstorySlot slot, Gender gender)
        {
            return PickAvirBackstory(slot, gender, null);
        }

        /// <summary>
        /// Picks a backstory. If a childhood is supplied and it belongs to a LINKED LINE, the
        /// matching adulthood is strongly preferred - so the adult story continues the childhood
        /// rather than contradicting it.
        ///
        /// A line is declared by the defName: Avir_Child_L_Forge pairs with Avir_Adult_L_Forge.
        /// The last-hatched chick who made his own toys grows into the smith who makes the beaks
        /// the circle fights with. The silent hatchling grows into the one nobody hears coming.
        ///
        /// Not guaranteed - a quarter of the time it rolls freely, so a line isn't a rail.
        /// </summary>
        public static BackstoryDef PickAvirBackstory(BackstorySlot slot, Gender gender, BackstoryDef childhood)
        {
            return PickAvirBackstory(slot, gender, childhood, null);
        }

        public static BackstoryDef PickAvirBackstory(BackstorySlot slot, Gender gender,
                                                     BackstoryDef childhood, string kindDefName)
        {
            EnsureCached();
            bool female = gender == Gender.Female;

            // CASTE FIRST. A predator draws from the talon pool and nowhere else.
            List<BackstoryDef> caste = CastePool(kindDefName, slot, gender);

            if (caste != null)
            {
                // A ROLE WITH AN EMPTY POOL DOES NOT GET THE GENERAL POOL. It gets nothing, and a
                // warning in the log (see CastePool). Falling through here is what let a warrior
                // walk out of generation with a stone-setter's childhood.
                if (caste.Count == 0) return null;

                // Still honour the linked line, but only if the linked story is IN this caste.
                if (slot == BackstorySlot.Adulthood && childhood != null && Rand.Chance(0.75f))
                {
                    BackstoryDef linked = LinkedAdulthood(childhood);
                    if (linked != null && caste.Contains(linked)) return linked;
                }

                return caste[Rand.Range(0, caste.Count)];
            }

            if (slot == BackstorySlot.Adulthood && childhood != null && Rand.Chance(0.75f))
            {
                BackstoryDef linked = LinkedAdulthood(childhood);
                if (linked != null) return linked;
            }

            return slot == BackstorySlot.Childhood
                ? PickFrom(childhoodsAny, female ? childhoodsFemale : childhoodsMale)
                : PickFrom(adulthoodsAny, female ? adulthoodsFemale : adulthoodsMale);
        }

        // ============================================================
        // CASTE POOLS
        //
        // THE BUG THIS FIXES: the picker below only ever looked at the GENERAL Avir pools
        // (Avir / AvirMale / AvirFemale). The pawnkind backstoryFilters were correct, but this
        // patch runs afterwards and overwrote their work - so a PREDATOR, an elite killer, was
        // being handed "stone-setter's apprentice" and "bloodstone delver" and walking into a
        // raid with 2 Melee and 11 Mining.
        //
        // Each caste now draws from its OWN pool and nothing else.
        // ============================================================
        // SLOT-AWARE. The huntress and the predator share the ordinary talon CHILDHOOD pool -
        // they were fledglings in the same clutch as everyone else - but their ADULTHOOD pool is
        // theirs alone. That distinction cannot be expressed in XML, because a pawnkind's
        // backstoryFilters apply to both slots at once; add AvirTalon there and her six adulthoods
        // just get diluted into the warrior's twenty. So the split lives here.
        //
        // The warrior is deliberately untouched: he draws the full AvirTalon list in both slots
        // and is the baseline the other two are measured against.
        private static readonly Dictionary<string, string[]> CasteChildhood =
            new Dictionary<string, string[]>
        {
            { "Avir_Predator",   new[] { "AvirTalon", "AvirPredator" } },  // both
            { "Avir_Huntress",   new[] { "AvirTalon", "AvirHuntress" } },  // both
            { "Avir_Strider",    new[] { "AvirTalon", "AvirStrider" } },   // both
            { "Avir_Warrior",    new[] { "AvirTalon" } },
            { "Avir_YoungHunter",new[] { "AvirTalon" } },

            { "Avir_Artificer",  new[] { "AvirArtificer" } },
            { "Avir_GuildMaster",new[] { "AvirGuild" } },

            { "Avir_Matriarch",  new[] { "AvirRegal", "AvirMatriarch" } },
            { "Avir_Priestess",  new[] { "AvirRegal", "AvirPriestess" } },

            // HIGH ARTIFICERS. Exclusive pools, no crossover with anything else at all - not
            // even AvirArtificer. See Backstories_Avir_Architect.xml / _Artist.xml headers.
            { "Avir_Architect",  new[] { "AvirArchitect" } },
            { "Avir_Artist",     new[] { "AvirArtist" } },

            // A'SCHIALIS LEGION. Centurion/Legionnaire now share their OWN
            // childhood pool (AvirLegionChild) - raised toward the line, not the
            // pyre, so deliberately not the same childhoods as Inquisitor/Highlord.
            // Inquisitor/Highlord keep the AvirLegion childhood pool and add their
            // own rank tag, exactly as Matriarch/Priestess do with Regal.
            { "Avir_Centurion",  new[] { "AvirLegionChild" } },
            { "Avir_Legionnaire",new[] { "AvirLegionChild" } },
            { "Avir_Inquisitor", new[] { "AvirLegion", "AvirInquisitor" } },
            { "Avir_Highlord",   new[] { "AvirLegion", "AvirHighlord" } },

            // THE PRIMAL. One childhood pool for all of them.
            { "Avir_PrimalHunter",    new[] { "AvirPrimal" } },
            { "Avir_PrimalHarpy",     new[] { "AvirPrimal" } },
            { "Avir_PrimalMatriarch", new[] { "AvirPrimal" } },
        };

        private static readonly Dictionary<string, string[]> CasteAdulthood =
            new Dictionary<string, string[]>
        {
            { "Avir_Predator",   new[] { "AvirPredator" } },   // HERS/HIS ALONE
            { "Avir_Huntress",   new[] { "AvirHuntress" } },   // HERS ALONE
            { "Avir_Strider",    new[] { "AvirStrider" } },    // ITS ALONE
            { "Avir_Warrior",    new[] { "AvirTalon" } },
            { "Avir_YoungHunter",new[] { "AvirTalon" } },

            { "Avir_Artificer",  new[] { "AvirArtificer" } },
            { "Avir_GuildMaster",new[] { "AvirGuild" } },

            { "Avir_Matriarch",  new[] { "AvirRegal", "AvirMatriarch" } },
            { "Avir_Priestess",  new[] { "AvirRegal", "AvirPriestess" } },

            { "Avir_Architect",  new[] { "AvirArchitect" } },
            { "Avir_Artist",     new[] { "AvirArtist" } },

            // A'SCHIALIS LEGION. Centurion/Legionnaire now draw their own
            // rank-exclusive adulthoods (AvirCenturion / AvirLegionnaire).
            // Inquisitor/Highlord draw their rank-exclusive adulthoods;
            // AvirLegion carries no adulthoods, so it resolves to the rank pool alone,
            // exactly like AvirRegal for Matriarch/Priestess.
            { "Avir_Centurion",  new[] { "AvirCenturion" } },
            { "Avir_Legionnaire",new[] { "AvirLegionnaire" } },
            { "Avir_Inquisitor", new[] { "AvirLegion", "AvirInquisitor" } },
            { "Avir_Highlord",   new[] { "AvirLegion", "AvirHighlord" } },

            { "Avir_PrimalHunter",    new[] { "AvirPrimal" } },
            { "Avir_PrimalHarpy",     new[] { "AvirPrimal" } },
            { "Avir_PrimalMatriarch", new[] { "AvirPrimalMatriarch" } },
        };

        // Hatchlings and player colonists appear in neither table. They draw from everything -
        // they have not been sorted into the circle yet.

        private static bool TryGetCaste(string kindDefName, BackstorySlot slot, out string[] cats)
        {
            cats = null;
            if (kindDefName == null) return false;

            Dictionary<string, string[]> table =
                (slot == BackstorySlot.Childhood) ? CasteChildhood : CasteAdulthood;

            return table.TryGetValue(kindDefName, out cats);
        }

        /// <summary>Every Avir backstory whose spawnCategories include this one.</summary>
        // ============================================================
        // POOL INDEX - built ONCE, then never again.
        //
        // THE PERFORMANCE BUG: InCategory used to walk the ENTIRE BackstoryDef database on every
        // single call, doing up to three linear List.Contains(string) scans per def. In a large
        // modpack that database is 10,000-30,000 backstories. PickAvirBackstory is called at least
        // twice per Avir pawn (childhood, adulthood), and CastePool calls InCategory once per
        // category - so generating one Avir cost roughly four full-database sweeps and millions of
        // string comparisons. The hourly sweep hit the same path again on every live pawn.
        //
        // Now: one pass over the database, ever. Everything after that is a dictionary lookup.
        // Backstory defs do not change at runtime, so the index never needs invalidating.
        // ============================================================
        private static Dictionary<string, List<BackstoryDef>> poolIndex;
        private static Dictionary<string, List<BackstoryDef>> castePoolCache;

        private static string PoolKey(string category, BackstorySlot slot, Gender gender)
        {
            // Gender is baked into the key so the filter is paid once, at build time, not per pick.
            return category + "|" + (slot == BackstorySlot.Childhood ? "C" : "A")
                            + "|" + (gender == Gender.Female ? "F" : "M");
        }

        private static void BuildPoolIndex()
        {
            if (poolIndex != null) return;

            poolIndex = new Dictionary<string, List<BackstoryDef>>();

            foreach (BackstoryDef b in DefDatabase<BackstoryDef>.AllDefsListForReading)
            {
                List<string> cats = b.spawnCategories;
                if (cats == null || cats.Count == 0) continue;

                // Skip the ~99% of a modpack's backstories that are not ours, cheaply.
                bool ours = false;
                for (int i = 0; i < cats.Count; i++)
                {
                    if (cats[i] != null && cats[i].StartsWith("Avir", StringComparison.Ordinal)) { ours = true; break; }
                }
                if (!ours) continue;

                bool maleOnly = cats.Contains(CategoryMale);
                bool femaleOnly = cats.Contains(CategoryFemale);

                for (int i = 0; i < cats.Count; i++)
                {
                    string c = cats[i];
                    if (c == null) continue;

                    if (!femaleOnly) Add(PoolKey(c, b.slot, Gender.Male), b);
                    if (!maleOnly) Add(PoolKey(c, b.slot, Gender.Female), b);
                }
            }
        }

        private static void Add(string key, BackstoryDef b)
        {
            List<BackstoryDef> list;
            if (!poolIndex.TryGetValue(key, out list))
            {
                list = new List<BackstoryDef>();
                poolIndex[key] = list;
            }
            list.Add(b);
        }

        /// <summary>Every Avir backstory in this category, for this slot and gender. O(1).</summary>
        private static List<BackstoryDef> InCategory(string category, BackstorySlot slot, Gender gender)
        {
            BuildPoolIndex();

            List<BackstoryDef> hits;
            return poolIndex.TryGetValue(PoolKey(category, slot, gender), out hits)
                ? hits
                : EmptyPool;
        }

        private static readonly List<BackstoryDef> EmptyPool = new List<BackstoryDef>();

        /// <summary>
        /// The pool a given pawnkind is allowed to draw from - decided by its ROLE, which is what
        /// the pawnkind is. A warrior draws warrior stories because he is a warrior, whatever his
        /// genes say and whatever faction happens to own him this week.
        ///
        /// Null means "this kind has no role - hatchlings and colonists, who have not been sorted
        /// into the circle yet". Only they see the general pool.
        ///
        /// It used to say "return pool.Count > 0 ? pool : null" - an empty caste pool quietly
        /// became the general pool, which is the same leak as the XML one, just spelled in C#.
        /// A caste with no stories is a data bug and must SAY SO, not paper over itself with a
        /// stone-setter's apprentice. So: empty caste pool now returns an empty list, the picker
        /// keeps the vanilla backstory, and there is a warning in the log with the caste's name on it.
        /// </summary>
        private static List<BackstoryDef> CastePool(string kindDefName, BackstorySlot slot, Gender gender)
        {
            if (kindDefName == null) return null;

            string[] cats;
            if (!TryGetCaste(kindDefName, slot, out cats)) return null; // no role - general pool

            // Assembled once per (kind, slot, gender) and kept. Without this, every single pawn
            // paid for a fresh List plus a full copy of its pool.
            if (castePoolCache == null) castePoolCache = new Dictionary<string, List<BackstoryDef>>();

            string key = PoolKey(kindDefName, slot, gender);

            List<BackstoryDef> pool;
            if (castePoolCache.TryGetValue(key, out pool)) return pool;

            if (cats.Length == 1)
            {
                // Common case: hand back the index's own list. No allocation, no copy.
                pool = InCategory(cats[0], slot, gender);
            }
            else
            {
                pool = new List<BackstoryDef>();
                foreach (string c in cats)
                {
                    List<BackstoryDef> part = InCategory(c, slot, gender);
                    for (int i = 0; i < part.Count; i++)
                    {
                        if (!pool.Contains(part[i])) pool.Add(part[i]); // categories can overlap
                    }
                }
            }

            castePoolCache[key] = pool;

            if (pool.Count == 0)
            {
                Log.WarningOnce("[Avir] Caste pool is EMPTY for " + kindDefName + " (" + slot + ", " + gender
                                + ") - categories [" + string.Join(", ", cats) + "] match no backstory. "
                                + "Check the spawnCategories in Defs/BackstoryDefs/. Refusing to fall back "
                                + "to the general pool; the vanilla backstory is being kept instead.",
                                ("AvirCaste" + kindDefName + slot + gender).GetHashCode());
            }

            return pool; // empty list, NOT null - an empty list still means "no general pool for you"
        }

        // ---------- introspection, for AvirDiag ----------

        public static IEnumerable<string> KnownCasteKinds()
        {
            return CasteChildhood.Keys;
        }

        public static int CastePoolSize(string kindDefName, BackstorySlot slot, Gender gender)
        {
            List<BackstoryDef> p = CastePool(kindDefName, slot, gender);
            return p == null ? -1 : p.Count; // -1 = no caste, uses the general pool
        }

        /// <summary>Human-readable answer to "which pool will this pawn draw from, and why".</summary>
        public static string DescribePoolFor(Pawn pawn)
        {
            if (pawn == null || pawn.kindDef == null) return "(no kindDef - general pool)";

            string[] cats;
            if (!TryGetCaste(pawn.kindDef.defName, BackstorySlot.Childhood, out cats))
            {
                return "GENERAL POOL - kindDef '" + pawn.kindDef.defName
                       + "' is not a caste key. If you expected a caste here, THIS IS THE BUG: "
                       + "the dictionary key does not match the pawnkind's defName.";
            }

            int c = CastePoolSize(pawn.kindDef.defName, BackstorySlot.Childhood, pawn.gender);
            int a = CastePoolSize(pawn.kindDef.defName, BackstorySlot.Adulthood, pawn.gender);

            string[] acats;
            TryGetCaste(pawn.kindDef.defName, BackstorySlot.Adulthood, out acats);

            return "CASTE childhood[" + string.Join(",", cats) + "]=" + c
                 + "  adulthood[" + (acats != null ? string.Join(",", acats) : "-") + "]=" + a;
        }

        private const string LineToken = "_L_";

        /// <summary>Finds the adulthood on the same line as this childhood, if there is one.</summary>
        private static BackstoryDef LinkedAdulthood(BackstoryDef childhood)
        {
            if (childhood == null || childhood.defName == null) return null;

            int i = childhood.defName.IndexOf(LineToken);
            if (i < 0) return null;

            string line = childhood.defName.Substring(i + LineToken.Length);
            string wanted = "Avir_Adult" + LineToken + line;

            return DefDatabase<BackstoryDef>.GetNamedSilentFail(wanted);
        }

        public static void SetChildhood(Pawn pawn, BackstoryDef def)
        {
            SetBackstory(pawn.story, "Childhood", "childhood", def);
            pawn.Notify_DisabledWorkTypesChanged();
        }

        public static void SetAdulthood(Pawn pawn, BackstoryDef def)
        {
            SetBackstory(pawn.story, "Adulthood", "adulthood", def);
            pawn.Notify_DisabledWorkTypesChanged();
        }

        // ============================================================
        // THE ONLY SANCTIONED WAY TO MOVE A BACKSTORY ON A LIVE PAWN
        //
        // A backstory is not just a title - it is the skill points that came with it. Swap the
        // title without unwinding the old skillGains and re-applying the new ones and the pawn
        // ends up wearing one history and carrying another's numbers. That is what put a surgeon's
        // plaque on a pawn whose skills never went near a medical bay.
        //
        // Both callers - the pawn-generation finalizer and the hourly sweep - go through here now,
        // so there is no longer a path that changes one without the other.
        // ============================================================

        /// <summary>Swaps a childhood, unwinding the old skill gains and applying the new ones.</summary>
        public static void ReplaceChildhood(Pawn pawn, BackstoryDef newDef)
        {
            if (pawn == null || pawn.story == null || newDef == null) return;
            if (pawn.story.Childhood == newDef) return;

            ApplySkillGains(pawn, pawn.story.Childhood, -1);
            SetChildhood(pawn, newDef);
            ApplySkillGains(pawn, newDef, 1);
        }

        /// <summary>Swaps an adulthood, unwinding the old skill gains and applying the new ones.</summary>
        public static void ReplaceAdulthood(Pawn pawn, BackstoryDef newDef)
        {
            if (pawn == null || pawn.story == null || newDef == null) return;
            if (pawn.story.Adulthood == newDef) return;

            ApplySkillGains(pawn, pawn.story.Adulthood, -1);
            SetAdulthood(pawn, newDef);
            ApplySkillGains(pawn, newDef, 1);
        }

        /// <summary>Adds (sign = 1) or removes (sign = -1) a backstory's skill gains.</summary>
        public static void ApplySkillGains(Pawn pawn, BackstoryDef story, int sign)
        {
            if (pawn == null || pawn.skills == null || story == null || story.skillGains == null) return;

            foreach (SkillGain gain in story.skillGains)
            {
                if (gain == null || gain.skill == null) continue;

                SkillRecord rec = pawn.skills.GetSkill(gain.skill);
                if (rec == null || rec.TotallyDisabled) continue;

                rec.Level = Mathf.Clamp(rec.Level + (gain.amount * sign), 0, 20);
            }
        }

        /// <summary>
        /// True if this backstory belongs to the Avir.
        ///
        /// It must recognise EVERY Avir backstory, caste ones included, because this is the
        /// "leave it alone, it's already ours" guard used by the finalizer and by the hourly
        /// sweep. A false negative here does not fail loudly - it re-rolls the pawn's history,
        /// quietly, over and over, for as long as the colony lives.
        ///
        /// Three ways in, deliberately redundant:
        ///   - the two hatchling placeholders, which carry no spawn category at all
        ///   - any known Avir spawn category, general or caste
        ///   - any spawn category or defName that starts with "Avir", so a caste added tomorrow
        ///     is recognised the day it is written and not the day someone remembers this file
        /// </summary>
        public static bool IsAvirBackstory(BackstoryDef b)
        {
            if (b == null) return false;
            if (b == AvirHatchlingDefOf.Avir_Hatchling || b == AvirHatchlingDefOf.Avir_Nestling) return true;

            if (b.spawnCategories != null)
            {
                foreach (string c in b.spawnCategories)
                {
                    if (c == null) continue;
                    if (AvirCategories.Contains(c)) return true;
                    if (c.StartsWith("Avir", StringComparison.Ordinal)) return true;
                }
            }

            // Belt and braces: our defNames are all Avir_-prefixed. A backstory of ours that
            // somehow lost its categories is still ours and must not be re-rolled.
            return b.defName != null && b.defName.StartsWith("Avir_", StringComparison.Ordinal);
        }

        /// <summary>
        /// True if this backstory is one this pawn's ROLE is allowed to have.
        ///
        /// This is the guard the sweep and the finalizer use now, and it is strictly stronger than
        /// IsAvirBackstory. "Is it ours" was never the right question: a general-pool story IS ours,
        /// so a warrior wearing one passed the check and kept it forever. The right question is
        /// "does it belong to his caste", and a warrior carrying a stone-setter's childhood fails it.
        ///
        /// Roleless kinds - hatchlings, colonists - are allowed any Avir story, so for them this is
        /// just IsAvirBackstory. That is deliberate: they have not been sorted into the circle.
        ///
        /// This is stable, not churning: pool membership is a fixed property of the def. Once a
        /// pawn holds a story from his own pool, this returns true every hour thereafter, forever.
        /// </summary>
        public static bool BelongsToRoleOf(Pawn pawn, BackstoryDef b)
        {
            if (b == null) return false;
            if (!IsAvirBackstory(b)) return false;

            if (pawn == null || pawn.kindDef == null) return true;

            string[] cats;
            if (!TryGetCaste(pawn.kindDef.defName, b.slot, out cats)) return true; // no role

            // The hatchling placeholders are legal for anyone, at any age - they are what a pawn
            // wears before the growth moment hands them a real history.
            if (b == AvirHatchlingDefOf.Avir_Hatchling || b == AvirHatchlingDefOf.Avir_Nestling) return true;

            if (b.spawnCategories == null) return false;

            foreach (string c in cats)
            {
                if (b.spawnCategories.Contains(c)) return true;
            }

            return false;
        }

        private static BackstoryDef PickFrom(List<BackstoryDef> shared, List<BackstoryDef> gendered)
        {
            int total = shared.Count + gendered.Count;
            if (total == 0) return null;

            int roll = Rand.Range(0, total);
            return roll < shared.Count ? shared[roll] : gendered[roll - shared.Count];
        }

        private static void EnsureCached()
        {
            if (childhoodsAny != null) return;

            childhoodsAny = new List<BackstoryDef>();
            childhoodsMale = new List<BackstoryDef>();
            childhoodsFemale = new List<BackstoryDef>();
            adulthoodsAny = new List<BackstoryDef>();
            adulthoodsMale = new List<BackstoryDef>();
            adulthoodsFemale = new List<BackstoryDef>();

            foreach (BackstoryDef b in DefDatabase<BackstoryDef>.AllDefsListForReading)
            {
                if (b.spawnCategories == null) continue;

                bool child = b.slot == BackstorySlot.Childhood;

                if (b.spawnCategories.Contains(CategoryMale))
                    (child ? childhoodsMale : adulthoodsMale).Add(b);
                else if (b.spawnCategories.Contains(CategoryFemale))
                    (child ? childhoodsFemale : adulthoodsFemale).Add(b);
                else if (b.spawnCategories.Contains(CategoryAny))
                    (child ? childhoodsAny : adulthoodsAny).Add(b);
            }

            if (childhoodsAny.Count == 0 && childhoodsMale.Count == 0 && childhoodsFemale.Count == 0)
            {
                Log.Warning("[Avir] No Avir backstories found - check Defs/BackstoryDefs/ is installed.");
            }
        }

        // Writing Pawn_StoryTracker.Childhood/Adulthood has been the stubborn part of this whole
        // mod. The member is not a plain public field, its property setter is not public, and the
        // compiler-generated backing field is not named what you'd guess. So: do not guess. Find
        // the member by TYPE (BackstoryDef) and pick the one whose name mentions child/adult.
        private static FieldInfo childField;
        private static FieldInfo adultField;
        private static bool fieldsResolved;

        private static void ResolveFields()
        {
            if (fieldsResolved) return;
            fieldsResolved = true;

            foreach (FieldInfo f in AccessTools.GetDeclaredFields(typeof(Pawn_StoryTracker)))
            {
                if (f.FieldType != typeof(BackstoryDef)) continue;

                string n = f.Name.ToLowerInvariant();
                if (childField == null && n.Contains("child")) childField = f;
                else if (adultField == null && n.Contains("adult")) adultField = f;
            }

            if (childField == null || adultField == null)
            {
                Log.Warning("[Avir] Could not locate the backstory fields on Pawn_StoryTracker. "
                            + "Avir backstories will not be applied. Fields of type BackstoryDef found: "
                            + string.Join(", ", GetBackstoryFieldNames()));
            }
        }

        private static string[] GetBackstoryFieldNames()
        {
            List<string> names = new List<string>();
            foreach (FieldInfo f in AccessTools.GetDeclaredFields(typeof(Pawn_StoryTracker)))
            {
                if (f.FieldType == typeof(BackstoryDef)) names.Add(f.Name);
            }
            return names.ToArray();
        }

        private static void SetBackstory(Pawn_StoryTracker story, string propertyName, string fieldName, BackstoryDef value)
        {
            ResolveFields();

            FieldInfo target = (propertyName == "Childhood") ? childField : adultField;
            if (target != null)
            {
                target.SetValue(story, value);
                return;
            }

            // Last resort: a non-public property setter, if one happens to exist.
            PropertyInfo prop = AccessTools.Property(typeof(Pawn_StoryTracker), propertyName);
            MethodInfo setter = prop != null ? prop.GetSetMethod(true) : null;
            if (setter != null) setter.Invoke(story, new object[] { value });
        }
    }
}