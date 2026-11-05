using System.Linq;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    public class CompProperties_Artifact : CompProperties
    {
        public CompProperties_Artifact()
        {
            compClass = typeof(CompArtifact);
        }
    }

    /// <summary>
    /// Everything an artifact remembers, as one portable block. It lives in the artifact item
    /// (CompArtifact) while the gem is loose, and moves into the armour (CompArtifactSocket) when
    /// it is inlaid - so experience, points and opened cells (later stages) always travel with
    /// the gem. Saved as flat keys so it reads the same wherever it sits.
    ///
    /// Quality is split (decided Oct 2, 2026): the jeweller's work and the designer's work are
    /// two separate qualities. Which stat each one drives is decided in the effects stage
    /// (proposed: setting -> charge capacity, heart -> focus strength).
    /// </summary>
    public class ArtifactState : IExposable
    {
        public ArtifactMetalDef rimMetal;
        public ArtifactMetalDef heartMetal;
        public QualityCategory settingQuality = QualityCategory.Normal;
        public QualityCategory heartQuality = QualityCategory.Normal;
        public string settingMaker;
        public string heartMaker;

        // ---- development (stage 4) ----
        /// <summary>Experience the wearer EARNED while this artifact was worn (the full amount,
        /// before the pawn's share was taken). Points are bought with it.</summary>
        public float xpEarned;
        /// <summary>Opened cells per arm. Cells open in order, so the value is "cells 1..n".</summary>
        public Dictionary<ArtifactArmDef, int> opened = new Dictionary<ArtifactArmDef, int>();
        /// <summary>Points that burnt (foreign extraction). They stay spent and are never refunded.</summary>
        public int burntPoints;

        // ---- milestones (stage 8) ----
        /// <summary>Identity of this artifact, kept through inlay and extraction (the state is
        /// cloned, the uid goes with it).</summary>
        public string uid;
        /// <summary>Arms whose milestones the player switched off by re-attuning. They keep their
        /// cells and passive bonuses; only the milestone effects sleep.</summary>
        public List<ArtifactArmDef> dormantArms = new List<ArtifactArmDef>();

        public string Uid
        {
            get
            {
                if (uid.NullOrEmpty()) uid = System.Guid.NewGuid().ToString("N").Substring(0, 12);
                return uid;
            }
        }

        /// <summary>Highest milestone cell reached in `arm` (0 when none).</summary>
        public int MilestoneReached(ArtifactArmDef arm)
        {
            if (S == null) return 0;
            int open = OpenedIn(arm), best = 0;
            foreach (int m in S.milestoneNodes)
                if (m <= open && m > best) best = m;
            return best;
        }

        public bool HasHeart => heartMetal != null;

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public ArtifactState Clone()
        {
            var c = (ArtifactState)MemberwiseClone();
            c.opened = opened != null ? new Dictionary<ArtifactArmDef, int>(opened) : new Dictionary<ArtifactArmDef, int>();
            c.dormantArms = dormantArms != null ? new List<ArtifactArmDef>(dormantArms) : new List<ArtifactArmDef>();
            c.stone = stone?.Clone();
            return c;
        }

        public int OpenedIn(ArtifactArmDef arm)
        {
            return arm != null && opened != null && opened.TryGetValue(arm, out int n) ? n : 0;
        }

        /// <summary>Points owned in total (earned by experience).</summary>
        public int PointsEarned => S == null ? 0 : S.PointsForXp(xpEarned);

        /// <summary>Points in opened cells plus burnt points.</summary>
        public int PointsSpent
        {
            get
            {
                if (S == null) return burntPoints;
                int t = burntPoints + TelekinesisPointsSpent;
                if (opened != null)
                    foreach (KeyValuePair<ArtifactArmDef, int> kv in opened)
                        for (int i = 1; i <= kv.Value; i++) t += S.NodeCost(i);
                return t;
            }
        }

        public int PointsAvailable => Mathf.Max(0, PointsEarned - PointsSpent);

        // ---- telekinesis circle (stage 9) ----
        /// <summary>Opened sockets of the telekinesis circle (0..5); each one is a level.</summary>
        public int tkLevel;

        // ---- charge (stage 10) ----
        /// <summary>Share of the capacity left, 0..1.</summary>
        public float charge = 1f;

        // ---- ability stone (stage 11) ----
        public AbilityStoneState stone;

        public int TelekinesisPointsSpent
        {
            get
            {
                if (S == null) return 0;
                int t = 0;
                for (int i = 1; i <= tkLevel; i++) t += S.TelekinesisSlotCost(i);
                return t;
            }
        }

        public string CannotOpenTelekinesisReason()
        {
            if (S == null) return "no settings";
            if (!HasHeart) return "the artifact has no heart";
            int next = tkLevel + 1;
            if (next > S.telekinesisSlotCosts.Count) return "the circle is complete";
            int cost = S.TelekinesisSlotCost(next);
            if (PointsAvailable < cost) return "needs " + cost + " point" + (cost == 1 ? "" : "s");
            return null;
        }

        public bool TryOpenTelekinesis()
        {
            if (CannotOpenTelekinesisReason() != null) return false;
            tkLevel++;
            ArtifactBonusCache.ClearAll();
            return true;
        }

        /// <summary>Progress toward the next point: (have, need) in experience inside the current step.</summary>
        public void NextPointProgress(out float have, out float need)
        {
            int n = PointsEarned;
            float baseXp = S.XpForPoints(n);
            need = S.XpForPoint(n + 1);
            have = Mathf.Clamp(xpEarned - baseXp, 0f, need);
        }

        /// <summary>Why the next cell of `arm` cannot open, or null if it can.</summary>
        public string CannotOpenReason(ArtifactArmDef arm)
        {
            if (S == null || arm == null) return "no settings";
            if (!HasHeart) return "the artifact has no heart";
            if (arm.requiresPsychicHeart && !heartMetal.IsPsychic) return "opens only with an essentium or tisserium heart";
            int next = OpenedIn(arm) + 1;
            if (next > S.NodeCount) return "the arm is complete";
            if (opened != null)
                foreach (KeyValuePair<ArtifactArmDef, int> kv in opened)
                    if (kv.Value > 0 && arm.Excludes(kv.Key)) return "closed by " + kv.Key.label;
            int cost = S.NodeCost(next);
            if (PointsAvailable < cost) return "needs " + cost + " point" + (cost == 1 ? "" : "s");
            return null;
        }

        public bool TryOpenNext(ArtifactArmDef arm)
        {
            if (CannotOpenReason(arm) != null) return false;
            if (opened == null) opened = new Dictionary<ArtifactArmDef, int>();
            opened[arm] = OpenedIn(arm) + 1;
            ArtifactBonusCache.ClearAll();
            return true;
        }

        /// <summary>Experience that went into the opened cells (burnt points are counted as the
        /// first, cheapest points - they are gone either way).</summary>
        public float InvestedXp => S == null ? 0f : S.XpForPoints(PointsSpent) - S.XpForPoints(burntPoints);

        /// <summary>Full reset: every cell closes; resetRefund of the invested experience comes
        /// back as points, the rest is lost. Burnt points stay burnt.</summary>
        public void ResetCells()
        {
            if (S == null) return;
            float lost = InvestedXp * (1f - S.resetRefund);
            xpEarned = Mathf.Max(0f, xpEarned - lost);
            opened?.Clear();
            tkLevel = 0;
            ArtifactBonusCache.ClearAll();
        }

        /// <summary>Extraction by anyone but an Avir: the listed cells (7, 8, 9) of every arm are
        /// lost and their points burn - spent for good, never refunded. Cells open in order, so
        /// everything from the first lost cell onward goes.</summary>
        public void LoseForeignExtractionCells()
        {
            if (S == null || opened == null || S.nodesLostOnForeignExtraction.NullOrEmpty()) return;
            int first = S.nodesLostOnForeignExtraction.Min();
            foreach (ArtifactArmDef arm in opened.Keys.ToList())
            {
                int n = opened[arm];
                if (n < first) continue;
                for (int i = first; i <= n; i++) burntPoints += S.NodeCost(i);
                opened[arm] = first - 1;
            }
            ArtifactBonusCache.ClearAll();
        }

        /// <summary>Credits experience the wearer earned. Returns how many new points appeared.</summary>
        public int AddXp(float xp)
        {
            if (xp <= 0f || S == null) return 0;
            int before = PointsEarned;
            xpEarned += xp * (AvirMod.Settings?.artifactXpFactor ?? 1f);
            return PointsEarned - before;
        }

        private List<ArtifactArmDef> tmpArms;
        private List<int> tmpCounts;

        /// <summary>The player's own name for it (null - the usual label).</summary>
        public string customName;

        public void ExposeData()
        {
            Scribe_Values.Look(ref customName, "customName");
            Scribe_Defs.Look(ref rimMetal, "rimMetal");
            Scribe_Defs.Look(ref heartMetal, "heartMetal");
            Scribe_Values.Look(ref settingQuality, "settingQuality", QualityCategory.Normal);
            Scribe_Values.Look(ref heartQuality, "heartQuality", QualityCategory.Normal);
            Scribe_Values.Look(ref settingMaker, "settingMaker");
            Scribe_Values.Look(ref heartMaker, "heartMaker");
            Scribe_Values.Look(ref xpEarned, "xpEarned", 0f);
            Scribe_Values.Look(ref burntPoints, "burntPoints", 0);
            Scribe_Values.Look(ref tkLevel, "tkLevel", 0);
            Scribe_Values.Look(ref charge, "charge", 1f);
            Scribe_Deep.Look(ref stone, "stone");
            Scribe_Values.Look(ref uid, "uid");
            Scribe_Collections.Look(ref dormantArms, "dormantArms", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.Saving && opened != null)
            {
                foreach (ArtifactArmDef k in opened.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList()) opened.Remove(k);
            }
            Scribe_Collections.Look(ref opened, "openedCells", LookMode.Def, LookMode.Value, ref tmpArms, ref tmpCounts);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (opened == null) opened = new Dictionary<ArtifactArmDef, int>();
                if (dormantArms == null) dormantArms = new List<ArtifactArmDef>();
                dormantArms.RemoveAll(a => a == null);
                opened.RemoveAll(kv => kv.Key == null);
            }
        }

        public string ProgressLine()
        {
            if (S == null) return "";
            NextPointProgress(out float have, out float need);
            string t = "Development: " + PointsAvailable + " free point" + (PointsAvailable == 1 ? "" : "s")
                       + ", " + PointsSpent + " spent" + (burntPoints > 0 ? " (" + burntPoints + " burnt)" : "")
                       + ". Next point " + have.ToString("N0") + " / " + need.ToString("N0") + " xp.";
            return t;
        }

        public string CellsLine()
        {
            if (opened == null || opened.Count == 0) return "No cells opened.";
            return string.Join(", ", opened.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key.carrier).ThenBy(kv => kv.Key.armNumber)
                .Select(kv => kv.Key.LabelCap + " " + kv.Value + "/" + S.NodeCount));
        }

        public string ShortLabel()
        {
            if (rimMetal == null) return "";
            if (!HasHeart) return rimMetal.label + " rim, " + settingQuality.GetLabel();
            // the quality of both halves, in the label itself: a trader's list, a stockpile, a caravan show it
            return heartMetal.label + " heart " + heartQuality.GetLabel() + ", " + rimMetal.label + " rim " + settingQuality.GetLabel();
        }

        public string InspectLines()
        {
            var sb = new StringBuilder();
            if (rimMetal != null)
            {
                sb.Append("Rim: ").Append(rimMetal.LabelCap).Append(", setting ").Append(settingQuality.GetLabel());
                if (!settingMaker.NullOrEmpty()) sb.Append(" (by ").Append(settingMaker).Append(")");
            }
            if (HasHeart)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("Heart: ").Append(heartMetal.LabelCap).Append(", ").Append(heartQuality.GetLabel());
                if (!heartMaker.NullOrEmpty()) sb.Append(" (by ").Append(heartMaker).Append(")");
            }
            if (HasHeart && S != null && (xpEarned > 0f || PointsSpent > 0))
            {
                sb.AppendLine();
                sb.Append(ProgressLine());
            }
            if (HasHeart)
            {
                sb.AppendLine();
                sb.Append("Charge: ").Append(charge.ToStringPercent()).Append(charge <= 0f ? " - empty, milestones do not work" : "");
            }
            if (stone?.ability != null)
            {
                sb.AppendLine();
                sb.Append("Ability stone: ").Append(stone.Label).Append(", ").Append(stone.quality.GetLabel());
            }
            return sb.ToString();
        }
    }

    /// <summary>The loose artifact (or a setting still waiting for its heart).</summary>
    public class CompArtifact : ThingComp
    {
        public ArtifactState state = new ArtifactState();

        public ArtifactMetalDef rimMetal { get => state.rimMetal; set => state.rimMetal = value; }
        public ArtifactMetalDef heartMetal { get => state.heartMetal; set => state.heartMetal = value; }
        public QualityCategory settingQuality { get => state.settingQuality; set => state.settingQuality = value; }
        public QualityCategory heartQuality { get => state.heartQuality; set => state.heartQuality = value; }
        public string settingMaker { get => state.settingMaker; set => state.settingMaker = value; }
        public string heartMaker { get => state.heartMaker; set => state.heartMaker = value; }

        public bool HasHeart => state.HasHeart;

        /// <summary>A setting is only an artifact once the rim is in - without a rim it does not work.</summary>
        public bool IsWorking => rimMetal != null && heartMetal != null;

        public void CopyFrom(CompArtifact other)
        {
            state = other.state.Clone();
        }

        /// <summary>Items that appear without being crafted (dev spawn, traders, loot) get a valid
        /// random rim - and a heart of another metal if they are finished artifacts - so they never
        /// exist half-defined.</summary>
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (rimMetal == null)
            {
                rimMetal = ArtifactoricsDatabase.Metals
                    .Where(m => m.allowedAsRim && ArtifactCraftUtility.SettingRecipe(m) != null)
                    .RandomElementWithFallback();
            }
            if (heartMetal == null && parent.def == ArtifactoricsDefOf.Avir_Artifact)
            {
                heartMetal = ArtifactoricsDatabase.Metals
                    .Where(m => m.allowedAsHeart && m != rimMetal && ArtifactCraftUtility.HeartRecipe(m) != null)
                    .RandomElementWithFallback();
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (state == null) state = new ArtifactState();
            state.ExposeData();
        }

        public override string TransformLabel(string label)
        {
            string s = state.ShortLabel();
            if (!state.customName.NullOrEmpty()) label = state.customName;
            return s.NullOrEmpty() ? label : label + " (" + s + ")";
        }

        public override bool AllowStackWith(Thing other)
        {
            return false;
        }

        public override string CompInspectStringExtra()
        {
            string s = state.InspectLines();
            if (!HasHeart && rimMetal != null) s += "\nNo heart yet - not an artifact until one is set.";
            return s;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (rimMetal != null) yield return Dialog_ArtifactRename.Gizmo(() => state.customName, n => state.customName = n);
            if (parent.def == ArtifactoricsDefOf.Avir_Artifact && HasHeart && rimMetal != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Development",
                    defaultDesc = "Open this artifact's spiral: read its arms, invest points, reset.",
                    icon = ArtifactoricsTex.IconDevelop,
                    action = () => Dialog_ArtifactSpiral.Open(parent)
                };
            }
            // Only a real setting (rim in, no heart yet) offers this - never a finished artifact.
            if (parent.def == ArtifactoricsDefOf.Avir_ArtifactSetting && rimMetal != null && !HasHeart
                && parent.Spawned && parent.Map != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Set heart...",
                    defaultDesc = "Choose a heart metal for this setting and order the work at the nearest artificer's table. "
                                  + "Setting a heart needs intellectual work.",
                    icon = ArtifactoricsTex.IconSetHeart,
                    action = () =>
                    {
                        Building_ArtificerSpot spot = ArtifactCraftUtility.NearestSpot(parent);
                        if (spot == null)
                        {
                            Messages.Message("No artificer's table on this map.", parent, MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        Find.WindowStack.Add(new Dialog_ArtifactCreation(spot, this));
                    }
                };
            }
        }
    }

    /// <summary>Artifact and setting items: drawn with the rim tinted by the rim metal and, once
    /// set, the gem in the colour of the heart metal. Textures are pre-made per combination:
    /// Things/Item/Artifactorics/ArtifactSetting_{rim} and Artifact_{heart}_{rim}.</summary>
    public class Thing_Artifact : ThingWithComps
    {
        private static readonly Dictionary<string, Graphic> graphics = new Dictionary<string, Graphic>();
        private const string Dir = "Things/Item/Artifactorics/";
        private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        /// <summary>The picture of an artifact of these metals (for menus): its own texture, or the default one.</summary>
        public static Texture2D IconFor(ArtifactState st)
        {
            string path = st?.heartMetal == null || st.rimMetal == null || st.heartMetal.texKey.NullOrEmpty() || st.rimMetal.texKey.NullOrEmpty()
                ? "Things/Item/Artifactorics/Artifact_Gold_Silver"
                : Dir + "Artifact_" + st.heartMetal.texKey + "_" + st.rimMetal.texKey;
            if (!icons.TryGetValue(path, out Texture2D t))
                icons[path] = t = ContentFinder<Texture2D>.Get(path, false) ?? ContentFinder<Texture2D>.Get("Things/Item/Artifactorics/Artifact_Gold_Silver", false);
            return t;
        }

        public override Graphic Graphic
        {
            get
            {
                CompArtifact c = GetComp<CompArtifact>();
                if (c?.rimMetal == null || c.rimMetal.texKey.NullOrEmpty()) return base.Graphic;
                string path = c.heartMetal == null || c.heartMetal.texKey.NullOrEmpty()
                    ? Dir + "ArtifactSetting_" + c.rimMetal.texKey
                    : Dir + "Artifact_" + c.heartMetal.texKey + "_" + c.rimMetal.texKey;
                if (!graphics.TryGetValue(path, out Graphic g))
                {
                    if (ContentFinder<Texture2D>.Get(path, false) == null)
                    {
                        g = null;   // no texture for this pair: fall back to the def's own picture
                    }
                    else
                    {
                        GraphicData d = def.graphicData;
                        g = GraphicDatabase.Get<Graphic_Single>(path, d.shaderType?.Shader ?? ShaderDatabase.Cutout,
                            d.drawSize, Color.white);
                    }
                    graphics[path] = g;
                }
                return g ?? base.Graphic;
            }
        }
    }
}
