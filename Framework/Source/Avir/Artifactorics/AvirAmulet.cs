using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // AVIR TRADITIONAL AMULET (Oct 4, 2026) - stage 1: the item.
    //
    // One amulet instead of a shelf of near-duplicates. Made like an artifact, in two works at the
    // artificer's table:
    //   necklace - gold (A'Schialis) or silver (A'Shaign), crafting + artistic;
    //   core     - set into a finished necklace, intellectual; the CASTE is chosen with it and
    //              never changes. Gold and silver cannot be a core; the core may not share the
    //              necklace's metal; tisserium (the A'Shaign metal) needs a silver necklace.
    // Metals push the caste's subclass paths: the necklace gives the same bonus to every path
    // (religious metal), the core its own table. Positive only - an amulet has no weaknesses.
    // The subclass is chosen when the amulet's development is first opened (stage 2).
    // =====================================================================================

    /// <summary>A strict Avir caste: Elder, Steward, Maker.</summary>
    public class AmuletCasteDef : Def
    {
        public int uiOrder;
        public string iconPath;
        /// <summary>Skills that teach every amulet of this caste (elders: melee and shooting).</summary>
        public List<SkillDef> xpSkills = new List<SkillDef>();
        /// <summary>The amulet's telekinesis helps with cleaning, plants and mining (junior castes).</summary>
        public bool telekineticHelpers;

        public List<AmuletSubclassDef> Subclasses =>
            DefDatabase<AmuletSubclassDef>.AllDefsListForReading.Where(s => s.caste == this).OrderBy(s => s.uiOrder).ToList();

        public static List<AmuletCasteDef> All =>
            DefDatabase<AmuletCasteDef>.AllDefsListForReading.OrderBy(c => c.uiOrder).ToList();
    }

    /// <summary>A specialisation inside a caste (hunter, guard, warrior...).</summary>
    public class AmuletSubclassDef : Def
    {
        public AmuletCasteDef caste;
        public int uiOrder;
        /// <summary>Optional people the subclass belongs to, shown in its name (Inquisitor - A'Schialis).</summary>
        [MustTranslate] public string people;
        /// <summary>Its name among the A'Shaign (the inquisitor is a justiciar there); empty - the same.</summary>
        [MustTranslate] public string ashaignLabel;

        /// <summary>The name this path goes by on this amulet: the A'Shaign one on an A'Shaign amulet.</summary>
        public string LabelFor(CompAvirAmulet a) =>
            !ashaignLabel.NullOrEmpty() && a != null && (a.IsMulticlass || (a.necklaceMetal != null && a.necklaceMetal.MulticlassWith(a.coreMetal))) ? ashaignLabel : label;

        public string LabelCapFor(CompAvirAmulet a) => LabelFor(a).CapitalizeFirst();
        /// <summary>Skills that teach the amulet on top of the caste's, once this subclass is chosen.</summary>
        public List<SkillDef> xpSkills = new List<SkillDef>();
        /// <summary>Stats of every opened stone of this branch, scaled by its strength.</summary>
        public List<ArtifactCellStat> cellStats = new List<ArtifactCellStat>();
        /// <summary>Stat bonuses a milestone brings, from its stone on (x strength). Use MayRequire
        /// for DLC stats.</summary>
        public List<AmuletMilestoneStat> milestoneStats = new List<AmuletMilestoneStat>();
        /// <summary>Milestone texts (stones 3, 9, 12, 15, 18). Effects: stage 3.</summary>
        public List<ArtifactMilestoneInfo> milestones = new List<ArtifactMilestoneInfo>();
        /// <summary>The subclass's slot: an active or a passive like the ability stones. Effect: stage 3.</summary>
        [MustTranslate] public string slotLabel;
        [MustTranslate] public string slotDescription;
        public bool slotPassive;
        /// <summary>Ability granted while this is the amulet's own subclass (active slots).</summary>
        public AbilityDef slotAbility;
        /// <summary>Emblem of the slot (active or passive), shown on the development sheet.</summary>
        public string slotIconPath;
        private Texture2D slotIcon;
        private bool slotIconLoaded;
        public Texture2D SlotIcon
        {
            get
            {
                if (!slotIconLoaded) { slotIconLoaded = true; if (!slotIconPath.NullOrEmpty()) slotIcon = ContentFinder<Texture2D>.Get(slotIconPath, false); }
                return slotIcon;
            }
        }

        public ArtifactMilestoneInfo MilestoneAt(int stone) => milestones?.FirstOrDefault(m => m.node == stone);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (caste == null) yield return "no caste";
        }
    }

    /// <summary>A stat bonus a milestone adds once its stone is open (x branch strength).</summary>
    public class AmuletMilestoneStat
    {
        public int node;
        public StatDef stat;
        public float offset;
        public float factor;
    }

    /// <summary>"This core metal pushes that subclass by this much." Short form like the arm focus.</summary>
    public class AmuletFocusEntry
    {
        public AmuletSubclassDef subclass;
        public float factor;

        public void LoadDataFromXmlCustom(System.Xml.XmlNode xmlRoot)
        {
            DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, "subclass", xmlRoot.Name);
            factor = ParseHelper.FromString<float>(xmlRoot.FirstChild.Value);
        }
    }

    public class CompProperties_AvirAmulet : CompProperties
    {
        public CompProperties_AvirAmulet() { compClass = typeof(CompAvirAmulet); }

        // ---- named amulets (Silversong, Flameguard...): a traditional amulet remade ----
        /// <summary>Always the A'Shaign pattern - every path, whatever the necklace.</summary>
        public bool multiclass;
        /// <summary>Always this caste (Flameguard - the warrior's way).</summary>
        public AmuletCasteDef fixedCaste;
        /// <summary>Its own set of paths, in ring order (Goldensight - inquisitor and the warrior's).</summary>
        public List<AmuletSubclassDef> branches = new List<AmuletSubclassDef>();
        /// <summary>The main path, set for good.</summary>
        public AmuletSubclassDef fixedMain;
        /// <summary>The traditional amulet it is made from must be on this necklace.</summary>
        public ArtifactMetalDef requiredNecklace;
        /// <summary>...and of this caste (or of none yet).</summary>
        public AmuletCasteDef requiredCaste;

        public bool IsNamed => multiclass || fixedCaste != null || branches.Count > 0 || requiredNecklace != null;
    }

    /// <summary>What an amulet (or its bare necklace) is made of, and for which caste.</summary>
    public class CompAvirAmulet : ThingComp
    {
        public ArtifactMetalDef necklaceMetal;
        public ArtifactMetalDef coreMetal;
        public AmuletCasteDef caste;
        public AmuletSubclassDef subclass;   // stage 2: chosen when development is first opened
        /// <summary>A'Shaign (multiclass) amulets: the two paths beside the main one, from any caste.</summary>
        public List<AmuletSubclassDef> extraBranches = new List<AmuletSubclassDef>();

        public CompProperties_AvirAmulet Props => (CompProperties_AvirAmulet)props;

        /// <summary>An A'Shaign amulet: multiclass necklace (or a named A'Shaign amulet) and no caste of its own.</summary>
        public bool IsMulticlass => caste == null && !IsCustom && (Props.multiclass || (necklaceMetal != null && necklaceMetal.MulticlassWith(coreMetal)));

        /// <summary>A named amulet with its own set of paths (Goldensight).</summary>
        public bool IsCustom => Props.branches.Count > 0;
        public QualityCategory necklaceQuality = QualityCategory.Normal;
        public QualityCategory coreQuality = QualityCategory.Normal;
        public string necklaceMaker;
        public string coreMaker;
        // stage 2: development
        public float xpEarned;
        /// <summary>Points spent, booked at the price paid (-1 - not booked yet: counted from the open stones). Counting
        /// them anew at today's prices made a debt whenever a price rose - an A'Shaign path chosen later turned the open
        /// stones into dearer off-path ones, a setting changed - and the next earned point vanished into it.</summary>
        public int spentLedger = -1;
        /// <summary>Warrior 3: leap at melee targets by itself (can be switched off).</summary>
        public bool autoLeap = true;
        /// <summary>The main path's slot (active or passive) is awake: points were put into it.</summary>
        public bool slotOpen;
        /// <summary>The telekinesis circle: sockets opened with points (reach and satchel).</summary>
        public int tkLevel;
        /// <summary>The player's own name for the amulet (null - the usual label).</summary>
        public string customName;
        public Dictionary<AmuletSubclassDef, int> opened;
        private List<AmuletSubclassDef> tmpKeys;
        private List<int> tmpValues;

        public bool HasCore => coreMetal != null;

        public Pawn Wearer() => (parent as Apparel)?.Wearer;

        /// <summary>Only an Avir can wear it: an A'Shaign amulet (silver with essentium or tisserium),
        /// or an essentium core (the A'Schialis gold-essentium amulet too).</summary>
        public bool AvirOnly => IsMulticlass
            || (necklaceMetal != null && necklaceMetal.amuletAvirOnly) || (coreMetal != null && coreMetal.amuletAvirOnly);

        public int AddXp(float xp)
        {
            int before = AmuletDevelopment.PointsEarned(this);
            xpEarned += xp * (AvirMod.Settings?.amuletXpFactor ?? 1f);
            return AmuletDevelopment.PointsEarned(this) - before;
        }

        private Gizmo DevelopGizmo() => new Command_Action
        {
            defaultLabel = "Amulet development",
            defaultDesc = "Open the development of this amulet: its caste, its subclass, and the stones of every path of the caste.",
            icon = ArtifactoricsTex.IconDevelop,
            action = () => Find.WindowStack.Add(new Dialog_AmuletDevelopment(this))
        };

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetWornGizmosExtra()) yield return g;
            Pawn wearer = (parent as Apparel)?.Wearer;
            if (HasCore && wearer?.Faction == Faction.OfPlayer)
            {
                yield return Dialog_ArtifactRename.Gizmo(() => customName, n => customName = n);
                // development: the pawn's Artifacts tab
                if (AmuletElder.Level(wearer, AvirAmuletElderDefOf.Avir_Subclass_Warrior) >= 3)
                    yield return new Command_Toggle
                    {
                        defaultLabel = "Predator's leap",
                        defaultDesc = "Leap at a melee target 3-6 cells away instead of running to it.",
                        icon = ArtifactoricsTex.IconDevelop,
                        isActive = () => autoLeap,
                        toggleAction = () => autoLeap = !autoLeap
                    };
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (HasCore && parent.Spawned) yield return DevelopGizmo();
            if (necklaceMetal != null && parent.Spawned) yield return Dialog_ArtifactRename.Gizmo(() => customName, n => customName = n);
        }

        /// <summary>A necklace or amulet that did not come from the artificer's table (dev mode,
        /// traders, rewards) gets metals of its own: a random necklace, and a random core that may
        /// sit on it. The caste stays open - it is chosen on the development sheet.</summary>
        public override void PostPostMake()
        {
            base.PostPostMake();
            GiveMetalsIfMissing();
        }

        private void GiveMetalsIfMissing()
        {
            if (necklaceMetal != null) return;
            List<ArtifactMetalDef> necks = ArtifactoricsDatabase.Metals.Where(m => m.allowedAsNecklace).ToList();
            if (necks.Count == 0) return;
            necklaceMetal = necks.RandomElement();
            QualityCategory q = parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
            necklaceQuality = q;
            if (Props.requiredNecklace != null) necklaceMetal = Props.requiredNecklace;
            if (parent.def == AvirAmuletDefOf.Avir_TraditionalAmulet || Props.IsNamed)
            {
                List<ArtifactMetalDef> cores = ArtifactoricsDatabase.Metals
                    .Where(m => AvirAmuletCraft.CoreRecipe(m) != null && AvirAmuletCraft.CoreBlockReason(necklaceMetal, m) == null).ToList();
                if (cores.Count > 0) { coreMetal = cores.RandomElement(); coreQuality = q; }
            }
            ApplyNamedRules();
        }

        /// <summary>A named amulet's own rules: the A'Shaign pattern, a set caste, its own paths, a set main path.</summary>
        public void ApplyNamedRules()
        {
            if (!Props.IsNamed) return;
            if (Props.multiclass || IsCustom) caste = null;
            if (Props.fixedCaste != null) caste = Props.fixedCaste;
            if (Props.fixedMain != null) subclass = Props.fixedMain;
            if (subclass != null && caste != null && subclass.caste != caste) subclass = null;
        }

        /// <summary>The whole state of the traditional amulet a named one was made from.</summary>
        public void CopyAllFrom(CompAvirAmulet o)
        {
            necklaceMetal = o.necklaceMetal; necklaceQuality = o.necklaceQuality; necklaceMaker = o.necklaceMaker;
            coreMetal = o.coreMetal; coreQuality = o.coreQuality; coreMaker = o.coreMaker;
            caste = o.caste; subclass = o.subclass;
            extraBranches = new List<AmuletSubclassDef>(o.extraBranches ?? new List<AmuletSubclassDef>());
            xpEarned = o.xpEarned; spentLedger = o.spentLedger; autoLeap = o.autoLeap; slotOpen = o.slotOpen; tkLevel = o.tkLevel;
            opened = o.opened != null ? new Dictionary<AmuletSubclassDef, int>(o.opened) : null;
            customName = o.customName;
            ApplyNamedRules();
        }

        /// <summary>Named amulet just made: take over the traditional amulet it was made from.</summary>
        public override void Notify_RecipeProduced(Pawn pawn)
        {
            base.Notify_RecipeProduced(pawn);
            if (pawn != null && RecipeWorker_NamedAmulet.pending.TryGetValue(pawn.thingIDNumber, out CompAvirAmulet from))
            {
                RecipeWorker_NamedAmulet.pending.Remove(pawn.thingIDNumber);
                CopyAllFrom(from);
                SyncQuality();
            }
        }

        /// <summary>The quality the game sees (label, price, trade) is the necklace's and the core's together,
        /// the midpoint of the two rounded down - a legendary necklace on a poor core is not a legendary amulet.</summary>
        public QualityCategory Combined =>
            HasCore ? (QualityCategory)(((int)necklaceQuality + (int)coreQuality) / 2) : necklaceQuality;

        public void SyncQuality()
        {
            if (necklaceMetal == null) return;
            CompQuality q = parent.TryGetComp<CompQuality>();
            if (q == null || q.Quality == Combined) return;
            // nobody of the colony made it (a trader's, a raider's, a starting one): its own quality comp was rolled
            // by the game after the metals were given - the halves take that one
            if (necklaceMaker == null && coreMaker == null) { necklaceQuality = q.Quality; if (HasCore) coreQuality = q.Quality; return; }
            q.SetQuality(Combined, null);
        }

        public void CopyFrom(CompAvirAmulet o)
        {
            necklaceMetal = o.necklaceMetal;
            necklaceQuality = o.necklaceQuality;
            necklaceMaker = o.necklaceMaker;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref necklaceMetal, "necklaceMetal");
            Scribe_Defs.Look(ref coreMetal, "coreMetal");
            Scribe_Defs.Look(ref caste, "caste");
            Scribe_Defs.Look(ref subclass, "subclass");
            Scribe_Collections.Look(ref extraBranches, "extraBranches", LookMode.Def);
            if (extraBranches == null) extraBranches = new List<AmuletSubclassDef>();
            Scribe_Values.Look(ref necklaceQuality, "necklaceQuality", QualityCategory.Normal);
            Scribe_Values.Look(ref coreQuality, "coreQuality", QualityCategory.Normal);
            Scribe_Values.Look(ref necklaceMaker, "necklaceMaker");
            Scribe_Values.Look(ref coreMaker, "coreMaker");
            Scribe_Values.Look(ref xpEarned, "xpEarned");
            Scribe_Values.Look(ref spentLedger, "spentLedger", -1);
            Scribe_Values.Look(ref autoLeap, "autoLeap", true);
            Scribe_Values.Look(ref slotOpen, "slotOpen");
            Scribe_Values.Look(ref tkLevel, "tkLevel");
            Scribe_Values.Look(ref customName, "customName");
            Scribe_Collections.Look(ref opened, "opened", LookMode.Def, LookMode.Value, ref tmpKeys, ref tmpValues);
            // amulets spawned before their metals were given on creation (dev mode, traders)
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                GiveMetalsIfMissing();
                SyncQuality();
                // an old save: book what is open, and forgive the debt the old counting may have made
                if (spentLedger < 0 && opened != null && opened.Count > 0)
                    spentLedger = Mathf.Min(AmuletDevelopment.PointsSpentAtPrices(this), AmuletDevelopment.PointsEarned(this));
            }
        }

        public override string TransformLabel(string label)
        {
            if (!customName.NullOrEmpty()) return customName + " (" + LabelBody(label) + ")";
            return LabelBody(label);
        }

        private string LabelBody(string label)
        {
            if (necklaceMetal == null || Props.IsNamed) return label;
            if (!HasCore) return necklaceMetal.label + " " + label;
            return coreMetal.label + " " + label + " on " + necklaceMetal.label
                   + (caste != null ? " (" + caste.label + ")" : IsMulticlass ? " (A'Shaign)" : "");

        }

        public override string CompInspectStringExtra()
        {
            if (necklaceMetal == null) return null;
            var sb = new StringBuilder();
            sb.Append("Necklace: ").Append(necklaceMetal.LabelCap).Append(", ").Append(necklaceQuality.GetLabel());
            if (HasCore)
            {
                sb.Append("\nCore: ").Append(coreMetal.LabelCap).Append(", ").Append(coreQuality.GetLabel());
                if (IsMulticlass)
                {
                    sb.Append("\nA'Shaign: every path of every caste");
                    if (subclass != null) sb.Append(" - ").Append(string.Join(", ", new[] { subclass }.Concat(extraBranches).Select(b => b.LabelFor(this))));
                }
                else if (IsCustom)
                {
                    sb.Append("\nPaths: ").Append(string.Join(", ", Props.branches.Select(b => b.LabelFor(this))));
                    if (subclass != null) sb.Append(" - main: ").Append(subclass.LabelFor(this));
                }
                else
                {
                    sb.Append("\nCaste: ").Append(caste != null ? caste.LabelCap.ToString() : "not chosen");
                    if (subclass != null) sb.Append(" - ").Append(subclass.LabelCap);
                }
            }
            else sb.Append("\nNo core yet: an Avir sets one at the artificer's table.");
            if (HasCore && AvirOnly) sb.Append("\nOnly an Avir can wear it.");
            return sb.ToString();
        }

        public override string GetDescriptionPart()
        {
            if (!HasCore || caste == null) return null;
            return "Paths of the " + caste.label + " caste:\n" + AmuletFocus.PreviewText(necklaceMetal, coreMetal, caste);
        }
    }

    /// <summary>Focus of an amulet: what its metals give each subclass path.</summary>
    public static class AmuletFocus
    {
        /// <summary>Fixed part (no psychic rule) of the focus on one subclass path.</summary>
        public static float Fixed(ArtifactMetalDef necklace, ArtifactMetalDef core, AmuletSubclassDef sub)
        {
            float f = necklace?.amuletAllBonus ?? 0f;
            if (core != null)
            {
                f += core.amuletAllBonus;
                foreach (AmuletFocusEntry e in core.amuletFocus)
                    if (e.subclass == sub && e.factor > 0f) f += e.factor;
            }
            return f;
        }

        /// <summary>Per subclass of the caste, both metals together; psychic rule in words.</summary>
        public static string PreviewText(ArtifactMetalDef necklace, ArtifactMetalDef core, AmuletCasteDef caste)
        {
            if (caste == null) return "";
            var sb = new StringBuilder();
            foreach (AmuletSubclassDef s in caste.Subclasses)
            {
                float f = Fixed(necklace, core, s);
                sb.Append("  ").Append(s.LabelCap).Append(' ').Append(ArtifactFocus.Signed(f)).Append('\n');
            }
            if (core?.psychic != null)
                sb.Append(PsychicText(core));
            return sb.ToString().TrimEnd();
        }

        public static string PsychicText(ArtifactMetalDef core)
        {
            ArtifactPsychicFocus ps = core.psychic;
            string to = core.amuletPsychicAllSubclasses ? "every path of the caste" : "the subclass chosen for the amulet";
            string cond = ps.thresholdWithFearborn > 0f
                ? "from " + ps.thresholdWithFearborn.ToStringPercent() + " psychic sensitivity with Fearborn, " + ps.thresholdWithoutFearborn.ToStringPercent() + " without"
                : "for any Fearborn wearer; without Fearborn from " + ps.thresholdWithoutFearborn.ToStringPercent() + " psychic sensitivity";
            return "  Psychic: " + ArtifactFocus.Signed(ps.baseBonus) + " to " + to + ", and " + ArtifactFocus.Signed(ps.perStepBonus)
                   + " for every " + ps.stepSize.ToStringPercent() + " of sensitivity above " + ps.stepsFrom.ToStringPercent()
                   + ". Works " + cond + "; below that it simply gives nothing.";
        }
    }

    /// <summary>Lookups for the amulet recipes.</summary>
    public static class AvirAmuletCraft
    {
        private static Dictionary<ArtifactMetalDef, RecipeDef> necklaceRecipes;
        private static Dictionary<ArtifactMetalDef, RecipeDef> coreRecipes;

        private static void EnsureCache()
        {
            if (necklaceRecipes != null) return;
            necklaceRecipes = new Dictionary<ArtifactMetalDef, RecipeDef>();
            coreRecipes = new Dictionary<ArtifactMetalDef, RecipeDef>();
            foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                ArtifactRecipeExtension ext = r.GetModExtension<ArtifactRecipeExtension>();
                if (ext?.metal == null) continue;
                if (ext.step == ArtifactCraftStep.Necklace) necklaceRecipes[ext.metal] = r;
                else if (ext.step == ArtifactCraftStep.AmuletCore) coreRecipes[ext.metal] = r;
            }
        }

        public static RecipeDef NecklaceRecipe(ArtifactMetalDef m)
        {
            EnsureCache();
            return m != null && necklaceRecipes.TryGetValue(m, out RecipeDef r) ? r : null;
        }

        public static RecipeDef CoreRecipe(ArtifactMetalDef m)
        {
            EnsureCache();
            return m != null && coreRecipes.TryGetValue(m, out RecipeDef r) ? r : null;
        }

        /// <summary>Why this core may not go on this necklace, or null.</summary>
        public static string CoreBlockReason(ArtifactMetalDef necklace, ArtifactMetalDef core)
        {
            if (core == null) return "no core";
            if (!core.allowedAsAmuletCore) return core.label + " is a necklace metal";
            if (necklace != null && core == necklace) return "same metal as the necklace";
            if (necklace != null && !necklace.amuletCoresOnly.NullOrEmpty() && !necklace.amuletCoresOnly.Contains(core))
                return "the " + necklace.label + " necklace takes only " + string.Join(" or ", necklace.amuletCoresOnly.Select(m => m.label));
            if (core.amuletNeedsNecklace != null && necklace != null && necklace != core.amuletNeedsNecklace)
                return "needs a " + core.amuletNeedsNecklace.label + " necklace";
            return null;
        }
    }

    [DefOf]
    public static class AvirAmuletDefOf
    {
        public static ThingDef Avir_AmuletNecklace;
        public static ThingDef Avir_TraditionalAmulet;
        static AvirAmuletDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(AvirAmuletDefOf)); }
    }

    /// <summary>One grey amulet picture with a mask (AvirAmulet + AvirAmulet_m): red is the
    /// necklace, tinted with its metal; green is the core, tinted with the core's colour. A bare
    /// necklace shows an empty, dark setting.</summary>
    internal static class AvirAmuletGraphics
    {
        public const string TexPath = "Things/Item/Artifactorics/AvirAmulet";
        /// <summary>The bare necklace: the same picture with the stones taken out.</summary>
        public const string NecklacePath = "Things/Item/Artifactorics/AvirAmuletNecklace";
        private static readonly Color EmptySetting = new Color(0.22f, 0.22f, 0.24f);
        private static readonly Dictionary<long, Graphic> graphics = new Dictionary<long, Graphic>();
        private static bool? haveTex;

        public static Graphic For(Thing t, Graphic fallback)
        {
            CompAvirAmulet c = t.TryGetComp<CompAvirAmulet>();
            if (c?.necklaceMetal == null) return fallback;
            if (haveTex == null) haveTex = ContentFinder<Texture2D>.Get(TexPath, false) != null;
            if (haveTex == false) return fallback;
            long key = ((long)c.necklaceMetal.shortHash << 32) | (uint)(c.coreMetal?.shortHash ?? 0) | ((long)t.def.shortHash << 48);
            if (!graphics.TryGetValue(key, out Graphic g))
            {
                Color two = c.coreMetal != null ? c.coreMetal.gemColor : EmptySetting;
                string path = c.coreMetal == null && ContentFinder<Texture2D>.Get(NecklacePath, false) != null ? NecklacePath : TexPath;
                g = GraphicDatabase.Get<Graphic_Single>(path, ShaderDatabase.CutoutComplex, t.def.graphicData.drawSize, c.necklaceMetal.RimColor, two);
                graphics[key] = g;
            }
            return g;
        }

        /// <summary>The core as its own layer (tinted with the core metal, plain Cutout), printed over
        /// the amulet: the two-colour mask alone did not show the core's colour in game.</summary>
        public const string GemPath = "Things/Item/Artifactorics/AvirAmulet_Gem";
        private static readonly Dictionary<int, Graphic> gems = new Dictionary<int, Graphic>();
        private static bool? haveGem;

        public static Graphic GemFor(Thing t)
        {
            CompAvirAmulet c = t.TryGetComp<CompAvirAmulet>();
            if (c?.coreMetal == null) return null;
            if (haveGem == null) haveGem = ContentFinder<Texture2D>.Get(GemPath, false) != null;
            if (haveGem == false) return null;
            if (!gems.TryGetValue(c.coreMetal.shortHash, out Graphic g))
            {
                g = GraphicDatabase.Get<Graphic_Single>(GemPath, ShaderDatabase.Cutout, t.def.graphicData.drawSize, c.coreMetal.gemColor);
                gems[c.coreMetal.shortHash] = g;
            }
            return g;
        }

        public static void PrintGem(Thing t, SectionLayer layer)
        {
            Graphic g = GemFor(t);
            if (g == null) return;
            Vector3 pos = t.DrawPos;
            pos.y += 0.002f;
            Printer_Plane.PrintPlane(layer, pos, g.drawSize, g.MatSingle);
        }
    }

    public class Thing_AvirAmuletNecklace : ThingWithComps
    {
        public override Graphic Graphic => AvirAmuletGraphics.For(this, base.Graphic);
        public override Color DrawColor
        {
            get => GetComp<CompAvirAmulet>()?.necklaceMetal?.RimColor ?? base.DrawColor;
            set => base.DrawColor = value;
        }
    }

    public class Apparel_AvirAmulet : Apparel
    {

        public override Graphic Graphic => AvirAmuletGraphics.For(this, base.Graphic);
        public override Color DrawColor
        {
            get => GetComp<CompAvirAmulet>()?.necklaceMetal?.RimColor ?? base.DrawColor;
            set => base.DrawColor = value;
        }
        public override Color DrawColorTwo => GetComp<CompAvirAmulet>()?.coreMetal?.gemColor ?? base.DrawColorTwo;

        public override void Print(SectionLayer layer)
        {
            base.Print(layer);
            AvirAmuletGraphics.PrintGem(this, layer);
        }
    }

    /// <summary>Jeweller's half: the necklace.</summary>
    public class RecipeWorker_AmuletNecklace : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            ArtifactRecipeExtension ext = recipe.GetModExtension<ArtifactRecipeExtension>();
            if (ext?.metal == null || billDoer?.Map == null) return;
            Thing neck = ThingMaker.MakeThing(AvirAmuletDefOf.Avir_AmuletNecklace);
            CompAvirAmulet c = neck.TryGetComp<CompAvirAmulet>();
            c.necklaceMetal = ext.metal;
            c.necklaceQuality = ArtifactCraftUtility.RollQuality(billDoer, ArtifactCraftUtility.SettingLevel(billDoer));
            c.necklaceMaker = billDoer.LabelShort;
            c.SyncQuality();
            billDoer.skills?.Learn(SkillDefOf.Artistic, recipe.workAmount * 0.1f);
            ArtifactCraftUtility.Place(neck, billDoer);
            Messages.Message(billDoer.LabelShort + " finished " + neck.LabelNoCount + " (" + c.necklaceQuality.GetLabel() + ").",
                neck, MessageTypeDefOf.TaskCompletion, false);
        }
    }

    /// <summary>The core goes into a necklace; the caste comes from the bill.</summary>
    public class RecipeWorker_AmuletCore : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            ArtifactRecipeExtension ext = recipe.GetModExtension<ArtifactRecipeExtension>();
            if (ext?.metal == null || billDoer?.Map == null) return;

            // The necklace was consumed a moment ago; destroyed things keep their comps.
            CompAvirAmulet from = ingredients.Select(t => t.TryGetComp<CompAvirAmulet>()).FirstOrDefault(c => c != null && !c.HasCore);
            if (from == null)
            {
                Log.Error("[Avir] Amulet core recipe " + recipe.defName + " finished without a necklace among its ingredients.");
                return;
            }

            Thing amulet = ThingMaker.MakeThing(AvirAmuletDefOf.Avir_TraditionalAmulet);
            CompAvirAmulet c2 = amulet.TryGetComp<CompAvirAmulet>();
            c2.CopyFrom(from);
            c2.coreMetal = ext.metal;
            c2.caste = (billDoer.CurJob?.bill as Bill_AmuletCore)?.caste;   // null from the vanilla bills tab: chosen later
            c2.coreQuality = ArtifactCraftUtility.RollQuality(billDoer, ArtifactCraftUtility.HeartLevel(billDoer));
            c2.coreMaker = billDoer.LabelShort;
            c2.SyncQuality();
            ArtifactCraftUtility.Place(amulet, billDoer);
            Messages.Message(billDoer.LabelShort + " set the core: " + amulet.LabelNoCount + " (core " + c2.coreQuality.GetLabel() + ").",
                amulet, MessageTypeDefOf.PositiveEvent, false);
        }
    }

    /// <summary>Named amulets are remade from a traditional amulet: the product takes over its
    /// metals, qualities, caste, path, stones and experience (Notify_RecipeProduced).</summary>
    public class RecipeWorker_NamedAmulet : RecipeWorker
    {
        internal static readonly Dictionary<int, CompAvirAmulet> pending = new Dictionary<int, CompAvirAmulet>();

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            CompAvirAmulet from = ingredients.Select(t => t.TryGetComp<CompAvirAmulet>()).FirstOrDefault(c => c != null && c.HasCore);
            if (billDoer != null && from != null) pending[billDoer.thingIDNumber] = from;
        }
    }

    /// <summary>A core order: takes only a necklace of the chosen metal, and remembers the caste.</summary>
    public class Bill_AmuletCore : Bill_ProductionWithUft
    {
        public ArtifactMetalDef requiredNecklace;
        public AmuletCasteDef caste;

        public Bill_AmuletCore() { }

        public Bill_AmuletCore(RecipeDef recipe, ArtifactMetalDef necklace, AmuletCasteDef caste) : base(recipe, null)
        {
            requiredNecklace = necklace;
            this.caste = caste;
        }

        public override string LabelCap => base.LabelCap + (caste != null ? " (" + caste.label + ")" : "");

        public override bool IsFixedOrAllowedIngredient(Thing t)
        {
            CompAvirAmulet c = t.TryGetComp<CompAvirAmulet>();
            if (c != null && requiredNecklace != null && c.necklaceMetal != requiredNecklace) return false;
            return base.IsFixedOrAllowedIngredient(t);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref requiredNecklace, "requiredNecklace");
            Scribe_Defs.Look(ref caste, "caste");
        }
    }

    /// <summary>An A'Shaign amulet (silver necklace) is for the Avir alone.</summary>
    public static class AvirAmuletWear
    {
        public static bool Refuses(Thing t, Pawn p)
        {
            CompAvirAmulet c = t?.TryGetComp<CompAvirAmulet>();
            return c != null && c.AvirOnly && p != null && !ArtifactFocus.HasFearborn(p);
        }
    }

    [HarmonyPatch(typeof(Apparel), nameof(Apparel.PawnCanWear))]
    public static class Patch_AvirAmuletPawnCanWear
    {
        public static void Postfix(Apparel __instance, Pawn pawn, ref bool __result)
        {
            if (__result && AvirAmuletWear.Refuses(__instance, pawn)) __result = false;
        }
    }

    [HarmonyPatch(typeof(EquipmentUtility), nameof(EquipmentUtility.CanEquip), new[] { typeof(Thing), typeof(Pawn), typeof(string), typeof(bool) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    public static class Patch_AvirAmuletCanEquip
    {
        public static void Postfix(Thing thing, Pawn pawn, ref string cantReason, ref bool __result)
        {
            if (!__result || !AvirAmuletWear.Refuses(thing, pawn)) return;
            __result = false;
            cantReason = "only an Avir can wear an A'Shaign amulet";
        }
    }

    /// <summary>Any core order, from any source (vanilla bills tab too), refuses a necklace that
    /// already has a core, one of its own metal, and the wrong necklace for tisserium.</summary>
    [HarmonyPatch(typeof(Bill), nameof(Bill.IsFixedOrAllowedIngredient), new[] { typeof(Thing) })]
    public static class Patch_AmuletCoreIngredient
    {
        public static void Postfix(Bill __instance, Thing __0, ref bool __result)
        {
            if (!__result || __0 == null) return;
            if (__0.def == AvirAmuletDefOf.Avir_TraditionalAmulet)
            {
                // a named amulet takes only a finished traditional amulet of its necklace (and caste)
                CompProperties_AvirAmulet named = __instance.recipe?.ProducedThingDef?.GetCompProperties<CompProperties_AvirAmulet>();
                if (named == null || !named.IsNamed) return;
                CompAvirAmulet a = __0.TryGetComp<CompAvirAmulet>();
                if (a == null || !a.HasCore
                    || (named.requiredNecklace != null && a.necklaceMetal != named.requiredNecklace)
                    || (named.requiredCaste != null && a.caste != null && a.caste != named.requiredCaste)
                    || (named.multiclass && !a.IsMulticlass))   // a named A'Shaign amulet: silver with essentium or tisserium
                    __result = false;
                return;
            }
            if (__0.def != AvirAmuletDefOf.Avir_AmuletNecklace) return;
            ArtifactRecipeExtension ext = __instance.recipe?.GetModExtension<ArtifactRecipeExtension>();
            if (ext == null || ext.step != ArtifactCraftStep.AmuletCore) return;
            CompAvirAmulet c = __0.TryGetComp<CompAvirAmulet>();
            if (c == null) return;
            if (c.HasCore || AvirAmuletCraft.CoreBlockReason(c.necklaceMetal, ext.metal) != null) __result = false;
        }
    }
}
