using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // AVIR TRADITIONAL AMULET - stage 2: development.
    //
    //   - The amulet learns like an artifact (same experience curve), but only from the skills of
    //     its caste and subclass (AmuletCasteDef.xpSkills + AmuletSubclassDef.xpSkills).
    //   - Each subclass of the caste is a branch of 18 stones, milestones at 3/9/12/15/18. The
    //     amulet's own subclass is the main branch; the caste's other branches cost more.
    //   - Every opened stone gives its branch's stats, scaled by (1 + focus) x core quality.
    //   - Every opened stone also grows the amulet itself: telekinetic reach and satchel, the
    //     maker caste furthest. That is the "side section" of the development sheet.
    // Milestone effects and the active/passive slot come in stage 3; their texts are shown now.
    // =====================================================================================

    /// <summary>All the numbers of amulet development. Exactly one def: Avir_AmuletSettings.</summary>
    public class AmuletSettingsDef : Def
    {
        public int stonesPerBranch = 18;
        /// <summary>A'Shaign amulets learn slowly from every other skill too: this share of the
        /// artifacts' cut (1 - pawnShare), e.g. 0.25 x 20% = 5% of the gain.</summary>
        public float ashaignOffPathXpShare = 0.5f;
        /// <summary>Telekinetic helping hands, by telekinesis level 1-5: how many extra targets
        /// and at what share of the wearer's own speed (0 = not yet).</summary>
        public List<float> tkHelperCleanCount = new List<float> { 1f, 2f, 3f, 4f, 5f };
        public List<float> tkHelperCleanSpeed = new List<float> { 0.70f, 0.825f, 0.95f, 1.075f, 1.20f };
        public List<float> tkHelperPlantCount = new List<float> { 0f, 0f, 1f, 1f, 1f };
        public List<float> tkHelperPlantSpeed = new List<float> { 0f, 0f, 0.80f, 0.90f, 1.00f };
        public List<float> tkHelperMineCount = new List<float> { 0f, 0f, 1f, 1f, 1f };
        public List<float> tkHelperMineSpeed = new List<float> { 0f, 0f, 0.60f, 0.70f, 0.80f };
        /// <summary>Cells: the helping hands work no farther than this (and the amulet's reach).</summary>
        public float tkHelperMaxReach = 12f;
        /// <summary>Development sheet: stones per ring of the labyrinth.</summary>
        public int rowStones = 3;
        public List<int> milestoneStones = new List<int> { 3, 9, 12, 15, 18 };
        public int stoneCost = 1;
        public int milestoneCost = 2;
        /// <summary>Points to awaken the main path's slot (active or passive).</summary>
        public int slotCost = 3;
        /// <summary>The telekinesis circle: point cost of each socket, and what each socket adds
        /// (x necklace quality) - like the artifact's telekinesis circle.</summary>
        public List<int> tkSocketCosts = new List<int> { 1, 2, 3, 4, 5 };
        /// <summary>What each socket adds, in order: every next socket gives more.</summary>
        public List<float> tkSocketReach = new List<float> { 5f, 6f, 7f, 8f, 10f };
        public List<float> tkSocketSatchel = new List<float> { 5f, 6f, 7f, 8f, 10f };

        public float TkReach(int level) { float t = 0f; for (int i = 0; i < level && i < tkSocketReach.Count; i++) t += tkSocketReach[i]; return t; }
        public float TkSatchel(int level) { float t = 0f; for (int i = 0; i < level && i < tkSocketSatchel.Count; i++) t += tkSocketSatchel[i]; return t; }
        /// <summary>Price multiplier for the caste's branches other than the amulet's subclass.</summary>
        public int offBranchCostFactor = 2;
        /// <summary>Telekinetic reach (cells) and satchel (kg) per opened stone, any branch.</summary>
        public float radiusPerStone = 0.25f;
        public float satchelPerStone = 0.25f;
        /// <summary>The maker caste's amulet grows this many times faster.</summary>
        public float makerGrowthFactor = 2f;
        public AmuletCasteDef makerCaste;
        /// <summary>Architect 12: extra satchel.</summary>
        public float largerPocketKg = 15f;
        /// <summary>Architect's slot (wide reach): telekinetic reach multiplier.</summary>
        public float wideReachFactor = 1.25f;
        /// <summary>Adjacent paths: each stone opened on both of two neighbouring paths adds this
        /// to the strength of both (min of the two counts).</summary>
        public float adjacencyPerStone = 0.005f;
        /// <summary>Adjacent combinations wake when both paths reach this stone, double at the second.</summary>
        public int comboNode = 9;
        public int comboUpgrade = 18;
        /// <summary>A'Shaign battle telekinesis: any warrior path at this stone. No damage - every chance
        /// is base x wearer's psychic sensitivity x path strength, capped; knockdown also / body size.</summary>
        public int battleTelekinesisNode = 9;
        public float tkChanceCap = 0.35f;
        /// <summary>Knockdown on big bodies: x 1/body size, not below this.</summary>
        public float tkTargetFloor = 0.25f;
        /// <summary>Telekinetic volley bond: all battle telekinesis chances x this.</summary>
        public float tkVolleyFactor = 1.5f;
        public float tkDeflectChance = 0.08f;
        public int tkDeflectCooldown = 120;
        public float tkWrestChance = 0.10f;
        public int tkWrestCooldown = 600, tkWrestImmunity = 1200;
        public float tkDisarmShooterChance = 0.05f;
        public int tkDisarmShooterCooldown = 900, tkDisarmShooterImmunity = 1800;
        public float tkKnockdownChance = 0.06f;
        public int tkKnockdownCooldown = 720, tkKnockdownImmunity = 900, tkKnockdownTicks = 120;

        private static AmuletSettingsDef main;
        public static AmuletSettingsDef Main => main ?? (main = DefDatabase<AmuletSettingsDef>.GetNamedSilentFail("Avir_AmuletSettings"));

        public bool IsMilestone(int stone) => milestoneStones.Contains(stone);
    }

    public class AmuletComboStat
    {
        public StatDef stat;
        public float offset;
        public float factor;
    }

    /// <summary>A bond of two neighbouring paths: wakes when both reach comboNode (9), doubles at
    /// comboUpgrade (18). Works only where the two paths stand side by side in the amulet's ring;
    /// ashaiOnly bonds join the castes and exist only in the one pattern of the A'Shaign.</summary>
    public class AmuletComboDef : Def
    {
        public AmuletSubclassDef a;
        public AmuletSubclassDef b;
        public bool ashaiOnly;
        /// <summary>Raises the A'Shaign battle telekinesis chances (settings: tkVolleyFactor).</summary>
        public bool volley;
        /// <summary>Subspace satchel kg, x rank.</summary>
        public float satchelKg;
        public List<AmuletComboStat> stats = new List<AmuletComboStat>();

        public bool Joins(AmuletSubclassDef x, AmuletSubclassDef y) => (a == x && b == y) || (a == y && b == x);
    }

    public static class AmuletDevelopment
    {
        private static AmuletSettingsDef S => AmuletSettingsDef.Main;
        private static ArtifactoricsSettingsDef AS => ArtifactoricsSettingsDef.Main;

        /// <summary>The paths of this amulet: its caste's subclasses, or for an A'Shaign amulet the
        /// main path and the two chosen beside it.</summary>
        public static List<AmuletSubclassDef> Branches(CompAvirAmulet a)
        {
            if (a == null) return new List<AmuletSubclassDef>();
            if (a.IsMulticlass || a.IsCustom)
            {
                // the A'Shaign write one pattern of every path; the main one leads it, the rest follow
                // round the ring in its order, so neighbours stay neighbours on the sheet
                List<AmuletSubclassDef> ring = Ring(a);
                int i = Mathf.Max(0, ring.IndexOf(a.subclass));
                return ring.Skip(i).Concat(ring.Take(i)).ToList();
            }
            return a.caste?.Subclasses ?? new List<AmuletSubclassDef>();
        }

        /// <summary>The ring the paths stand in: the caste's subclasses, or for the A'Shaign every
        /// path in a fixed order (warriors, governors, artificers) closed into one circle.</summary>
        public static List<AmuletSubclassDef> Ring(CompAvirAmulet a)
        {
            if (a == null) return new List<AmuletSubclassDef>();
            if (a.IsCustom) return new List<AmuletSubclassDef>(a.Props.branches);
            if (a.IsMulticlass) return AmuletCasteDef.All.SelectMany(c => c.Subclasses).ToList();
            return a.caste?.Subclasses ?? new List<AmuletSubclassDef>();
        }

        public static List<AmuletSubclassDef> Neighbours(CompAvirAmulet a, AmuletSubclassDef b)
        {
            var res = new List<AmuletSubclassDef>();
            List<AmuletSubclassDef> ring = Ring(a);
            int i = ring.IndexOf(b);
            if (i < 0 || ring.Count < 2) return res;
            res.Add(ring[(i + 1) % ring.Count]);
            AmuletSubclassDef prev = ring[(i - 1 + ring.Count) % ring.Count];
            if (!res.Contains(prev)) res.Add(prev);
            return res;
        }

        public static bool Adjacent(CompAvirAmulet a, AmuletSubclassDef x, AmuletSubclassDef y) =>
            x != y && Neighbours(a, x).Contains(y);

        /// <summary>Common coefficient of adjacent development: +adjacencyPerStone per stone open on
        /// both this path and a neighbour.</summary>
        public static float AdjacencyBonus(CompAvirAmulet a, AmuletSubclassDef b)
        {
            if (a == null || S == null) return 0f;
            int mine = Opened(a, b);
            if (mine <= 0) return 0f;
            int shared = 0;
            foreach (AmuletSubclassDef n in Neighbours(a, b)) shared += Mathf.Min(mine, Opened(a, n));
            return shared * S.adjacencyPerStone;
        }

        public static int SharedStones(CompAvirAmulet a, AmuletSubclassDef x, AmuletSubclassDef y) =>
            Mathf.Min(Opened(a, x), Opened(a, y));

        /// <summary>0 - asleep, 1 - awake, 2 - doubled.</summary>
        public static int ComboRank(CompAvirAmulet a, AmuletComboDef c)
        {
            if (a == null || c?.a == null || c.b == null || S == null) return 0;
            if (c.ashaiOnly && !a.IsMulticlass) return 0;
            if (!HasBranch(a, c.a) || !HasBranch(a, c.b) || !Adjacent(a, c.a, c.b)) return 0;
            int n = SharedStones(a, c.a, c.b);
            return n >= S.comboUpgrade ? 2 : n >= S.comboNode ? 1 : 0;
        }

        /// <summary>The bond between two neighbouring paths of this amulet, if any.</summary>
        public static AmuletComboDef ComboOf(CompAvirAmulet a, AmuletSubclassDef x, AmuletSubclassDef y)
        {
            foreach (AmuletComboDef c in DefDatabase<AmuletComboDef>.AllDefsListForReading)
                if (c.Joins(x, y) && (!c.ashaiOnly || a.IsMulticlass)) return c;
            return null;
        }

        public static IEnumerable<(AmuletComboDef combo, int rank)> ActiveCombos(CompAvirAmulet a)
        {
            foreach (AmuletComboDef c in DefDatabase<AmuletComboDef>.AllDefsListForReading)
            {
                int r = ComboRank(a, c);
                if (r > 0) yield return (c, r);
            }
        }

        public static bool HasVolley(CompAvirAmulet a) => ActiveCombos(a).Any(x => x.combo.volley);

        public static bool HasBranch(CompAvirAmulet a, AmuletSubclassDef b) => b != null && Branches(a).Contains(b);

        /// <summary>Does the amulet carry any path of this caste (for the caste's effects).</summary>
        public static bool HasCasteBranch(CompAvirAmulet a, AmuletCasteDef c) => a != null && Branches(a).Any(b => b.caste == c);

        /// <summary>Works for this wearer: an A'Shaign amulet is dead on anyone but an Avir.</summary>
        public static bool WorksFor(CompAvirAmulet a, Pawn p) =>
            a == null || !a.AvirOnly || ArtifactFocus.HasFearborn(p);

        /// <summary>A milestone's price: the first (stone 3) costs 1, the others as set in the mod settings.</summary>
        public static int MilestoneCost(int stone)
        {
            AvirSettings o = AvirMod.Settings;
            if (stone <= 3) return 1;
            if (o == null) return S?.milestoneCost ?? 2;
            return stone <= 9 ? o.milestone9Cost : stone <= 12 ? o.milestone12Cost : stone <= 15 ? o.milestone15Cost : o.milestone18Cost;
        }

        public static int StoneCost(CompAvirAmulet a, AmuletSubclassDef branch, int stone)
        {
            if (S == null) return 1;
            int c = S.IsMilestone(stone) ? MilestoneCost(stone) : S.stoneCost;
            if (a.subclass != null && branch != a.subclass) c *= AvirMod.Settings?.offBranchCostFactor ?? S.offBranchCostFactor;
            return c;
        }

        public static int PointsEarned(CompAvirAmulet a) => AS == null ? 0 : AS.PointsForXp(a.xpEarned);

        public static int PointsSpent(CompAvirAmulet a) => a.spentLedger >= 0 ? a.spentLedger : PointsSpentAtPrices(a);

        /// <summary>Books a purchase at its price (before the purchase changes what is open).</summary>
        private static void Book(CompAvirAmulet a, int cost)
        {
            if (a.spentLedger < 0) a.spentLedger = PointsSpentAtPrices(a);
            a.spentLedger += cost;
        }

        /// <summary>What everything open would cost at today's prices.</summary>
        public static int PointsSpentAtPrices(CompAvirAmulet a)
        {
            if (a.opened == null) return 0;
            int sum = 0;
            foreach (KeyValuePair<AmuletSubclassDef, int> kv in a.opened)
                for (int n = 1; n <= kv.Value; n++) sum += StoneCost(a, kv.Key, n);
            if (a.slotOpen && S != null) sum += S.slotCost;
            if (S != null) for (int i = 0; i < a.tkLevel && i < S.tkSocketCosts.Count; i++) sum += S.tkSocketCosts[i];
            return sum;
        }

        public static int PointsAvailable(CompAvirAmulet a) => Mathf.Max(0, PointsEarned(a) - PointsSpent(a));

        public static int Opened(CompAvirAmulet a, AmuletSubclassDef b) =>
            a.opened != null && b != null && a.opened.TryGetValue(b, out int n) ? n : 0;

        public static int TotalOpened(CompAvirAmulet a) => a.opened?.Values.Sum() ?? 0;

        public static void NextPointProgress(CompAvirAmulet a, out float have, out float need)
        {
            have = 0f; need = 1f;
            if (AS == null) return;
            int n = PointsEarned(a);
            have = a.xpEarned - AS.XpForPoints(n);
            need = AS.XpForPoint(n + 1);
        }

        /// <summary>Why the next stone of this branch cannot open now, or null.</summary>
        public static string CannotOpenReason(CompAvirAmulet a, AmuletSubclassDef b)
        {
            if (a.caste == null && !a.IsMulticlass && !a.IsCustom) return "choose the caste first";
            if (a.subclass == null) return "choose the subclass first";
            if (!HasBranch(a, b)) return "not a path of this amulet";
            int next = Opened(a, b) + 1;
            if (S == null || next > S.stonesPerBranch) return "this path is complete";
            int cost = StoneCost(a, b, next);
            if (PointsAvailable(a) < cost) return "needs " + cost + " point" + (cost == 1 ? "" : "s") + ", " + PointsAvailable(a) + " free";
            return null;
        }

        public static bool TryOpenNext(CompAvirAmulet a, AmuletSubclassDef b)
        {
            if (CannotOpenReason(a, b) != null) return false;
            if (a.opened == null) a.opened = new Dictionary<AmuletSubclassDef, int>();
            Book(a, StoneCost(a, b, Opened(a, b) + 1));
            a.opened[b] = Opened(a, b) + 1;
            if (a.parent is Apparel ap && ap.Wearer != null) AmuletBonusCache.Invalidate(ap.Wearer);
            return true;
        }

        // ---------------------------------------------------------------- the amulet itself

        private static float Growth(CompAvirAmulet a) =>
            S == null ? 0f : TotalOpened(a) * (MainCaste(a) != null && MainCaste(a) == S.makerCaste ? S.makerGrowthFactor : 1f);

        /// <summary>The caste of the amulet, or of an A'Shaign amulet's main path.</summary>
        public static AmuletCasteDef MainCaste(CompAvirAmulet a) => a?.caste ?? a?.subclass?.caste;

        /// <summary>Necklace quality: how well the amulet's telekinesis grows (the same scale as the
        /// core's quality on the paths, Awful x0.8 .. Legendary x1.2).</summary>
        public static float NecklaceQualityFactor(CompAvirAmulet a) => AS?.HeartQualityFactor(a.necklaceQuality) ?? 1f;

        public static float RadiusBonus(CompAvirAmulet a) => a == null || S == null ? 0f
            : (Growth(a) * S.radiusPerStone + S.TkReach(a.tkLevel)) * NecklaceQualityFactor(a);

        /// <summary>Why the next telekinesis socket cannot be opened (null - it can).</summary>
        public static string TkBlockReason(CompAvirAmulet a)
        {
            if (S == null || a == null) return "no settings";
            if (a.tkLevel >= S.tkSocketCosts.Count) return "the circle is full";
            int cost = S.tkSocketCosts[a.tkLevel];
            if (PointsAvailable(a) < cost) return "needs " + cost + " point" + (cost == 1 ? "" : "s");
            return null;
        }

        public static bool TryOpenTk(CompAvirAmulet a)
        {
            if (TkBlockReason(a) != null) return false;
            Book(a, S.tkSocketCosts[a.tkLevel]);
            a.tkLevel++;
            return true;
        }
        public static float SatchelBonus(CompAvirAmulet a)
        {
            if (a == null || S == null) return 0f;
            float kg = (Growth(a) * S.satchelPerStone + S.TkSatchel(a.tkLevel)) * NecklaceQualityFactor(a);
            // architect 12 - larger pocket
            if (HasBranch(a, AvirAmuletMakerDefOf.Avir_Subclass_Architect) && Opened(a, AvirAmuletMakerDefOf.Avir_Subclass_Architect) >= 12) kg += S.largerPocketKg;
            // bonds of neighbouring paths (guild ledger)
            foreach (var (c, rank) in ActiveCombos(a)) kg += c.satchelKg * rank;
            return kg;
        }

        // ---------------------------------------------------------------- strength

        /// <summary>Strength of a branch on this wearer: (1 + focus) x core quality x (1 + adjacency).</summary>
        public static float Strength(CompAvirAmulet a, AmuletSubclassDef b, Pawn wearer)
        {
            float f = AmuletFocus.Fixed(a.necklaceMetal, a.coreMetal, b) + PsychicPart(a, b, wearer);
            float q = AS?.HeartQualityFactor(a.coreQuality) ?? 1f;
            return Mathf.Max(0f, 1f + f) * q * (1f + AdjacencyBonus(a, b));
        }

        /// <summary>Essentium: the amulet's own subclass; tisserium: every branch. No penalty below
        /// the threshold - amulets have no weaknesses.</summary>
        public static float PsychicPart(CompAvirAmulet a, AmuletSubclassDef b, Pawn wearer)
        {
            ArtifactMetalDef core = a.coreMetal;
            if (core?.psychic == null || wearer == null) return 0f;
            if (!core.amuletPsychicAllSubclasses && b != a.subclass) return 0f;
            float sens = ArtifactFocus.Sensitivity(wearer);
            if (!core.psychic.IsActive(sens, ArtifactFocus.HasFearborn(wearer))) return 0f;
            return core.psychic.baseBonus + core.psychic.Steps(sens) * core.psychic.perStepBonus;
        }

        /// <summary>Opened stones of a branch on the pawn's amulet (0 if the amulet is of another
        /// caste) and that branch's strength. Milestones work on any branch of the caste.</summary>
        public static int Level(Pawn p, AmuletSubclassDef branch, out float strength)
        {
            strength = 0f;
            CompAvirAmulet a = WornBy(p);
            if (a == null || branch == null || !HasBranch(a, branch) || !WorksFor(a, p)) return 0;
            int n = Opened(a, branch);
            if (n > 0) strength = Strength(a, branch, p);
            return n;
        }

        public static int Level(Pawn p, AmuletSubclassDef branch) => Level(p, branch, out _);

        /// <summary>The amulet's own subclass is this one (its slot works).</summary>
        public static bool SlotIs(Pawn p, AmuletSubclassDef branch)
        {
            CompAvirAmulet a = WornBy(p);
            return branch != null && a != null && a.subclass == branch && a.slotOpen && WorksFor(a, p);
        }

        /// <summary>Why the slot cannot be awakened now (null - it can).</summary>
        public static string SlotBlockReason(CompAvirAmulet a)
        {
            if (a?.subclass == null) return "choose the main path first";
            if (a.subclass.slotLabel.NullOrEmpty()) return "this path has no slot";
            if (a.slotOpen) return "the slot is already awake";
            if (PointsAvailable(a) < (S?.slotCost ?? 3)) return "needs " + (S?.slotCost ?? 3) + " points";
            return null;
        }

        public static bool TryOpenSlot(CompAvirAmulet a)
        {
            if (SlotBlockReason(a) != null) return false;
            Book(a, S?.slotCost ?? 3);
            a.slotOpen = true;
            if ((a.parent as Apparel)?.Wearer is Pawn w) AmuletBonusCache.Invalidate(w);
            return true;
        }

        private static readonly Dictionary<long, (int tick, Pawn best, int level, float k)> bestCache = new Dictionary<long, (int, Pawn, int, float)>();

        /// <summary>The colonist with the highest level of this branch on a map (or on every player
        /// home map when map is null). Cached for 250 ticks.</summary>
        public static Pawn Best(Map map, AmuletSubclassDef branch, out int level, out float strength)
        {
            level = 0; strength = 0f;
            if (branch == null || Find.Maps == null) return null;
            int now = Find.TickManager?.TicksGame ?? 0;
            long key = ((long)(map?.uniqueID ?? -1) << 32) | (uint)branch.shortHash;
            if (bestCache.TryGetValue(key, out var c) && now - c.tick < 250 && (c.best == null || !c.best.Dead))
            { level = c.level; strength = c.k; return c.best; }
            Pawn best = null;
            foreach (Map m in Find.Maps)
            {
                if (map != null ? m != map : !m.IsPlayerHome) continue;
                foreach (Pawn p in m.mapPawns.FreeColonistsSpawned)
                {
                    int l = Level(p, branch, out float k);
                    if (l > level) { level = l; strength = k; best = p; }
                }
            }
            bestCache[key] = (now, best, level, strength);
            return best;
        }

        /// <summary>The amulet a pawn wears (developed or not), or null.</summary>
        public static CompAvirAmulet WornBy(Pawn p)
        {
            if (p?.apparel == null || Avir.Linarian.Linarian.Is(p)) return null;
            List<Apparel> worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                CompAvirAmulet c = worn[i].TryGetComp<CompAvirAmulet>();
                if (c != null && c.HasCore) return c;
            }
            return null;
        }

        /// <summary>Does this skill teach the amulet? Caste skills, plus the subclass's own.</summary>
        public static bool Teaches(CompAvirAmulet a, SkillDef skill)
        {
            if (a == null || skill == null) return false;
            // A'Shaign: every path is theirs, but they learn only through the main one and its caste -
            // the depth everywhere is paid for in time, with points
            if (a.IsMulticlass || a.IsCustom)
                return a.subclass != null && (a.subclass.xpSkills.Contains(skill) || a.subclass.caste.xpSkills.Contains(skill));
            if (a.caste == null) return false;
            if (a.caste.xpSkills.Contains(skill)) return true;
            return a.subclass != null && a.subclass.xpSkills.Contains(skill);
        }
    }

    /// <summary>Stats from the opened stones, per pawn, rebuilt at most every 250 ticks.</summary>
    public static class AmuletBonusCache
    {
        private class Entry
        {
            public int tick = -99999;
            public readonly Dictionary<StatDef, ArtifactBonusCache.Mod> mods = new Dictionary<StatDef, ArtifactBonusCache.Mod>();
        }

        private static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();
        private const int Interval = 250;
        public static readonly HashSet<StatDef> AffectedStats = new HashSet<StatDef>();

        public static void Invalidate(Pawn p)
        {
            if (p != null) cache.Remove(p.thingIDNumber);
        }

        public static bool TryGet(Pawn p, StatDef stat, out ArtifactBonusCache.Mod mod)
        {
            mod = default;
            CompAvirAmulet a = AmuletDevelopment.WornBy(p);
            if (a?.opened == null || a.opened.Count == 0) return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!cache.TryGetValue(p.thingIDNumber, out Entry e))
            {
                if (cache.Count > 2000) cache.Clear();
                e = new Entry();
                cache[p.thingIDNumber] = e;
            }
            if (now - e.tick >= Interval || now < e.tick)
            {
                e.tick = now;
                e.mods.Clear();
                foreach (KeyValuePair<AmuletSubclassDef, int> kv in a.opened)
                {
                    if (kv.Value <= 0) continue;
                    float str = AmuletDevelopment.Strength(a, kv.Key, p);
                    float k = str * kv.Value;
                    foreach (ArtifactCellStat cs in kv.Key.cellStats)
                    {
                        if (cs.stat == null) continue;
                        e.mods.TryGetValue(cs.stat, out ArtifactBonusCache.Mod m);
                        m.offset += cs.offsetPerCell * k;
                        m.factor += cs.factorPerCell * k;
                        e.mods[cs.stat] = m;
                    }
                    foreach (AmuletMilestoneStat ms in kv.Key.milestoneStats)
                    {
                        if (ms.stat == null || kv.Value < ms.node) continue;
                        e.mods.TryGetValue(ms.stat, out ArtifactBonusCache.Mod m);
                        m.offset += ms.offset * str;
                        m.factor += ms.factor * str;
                        e.mods[ms.stat] = m;
                    }
                }
                // bonds of neighbouring paths: x rank, x the weaker path's strength
                foreach (var (c, rank) in AmuletDevelopment.ActiveCombos(a))
                {
                    float str = Mathf.Min(AmuletDevelopment.Strength(a, c.a, p), AmuletDevelopment.Strength(a, c.b, p)) * rank;
                    foreach (AmuletComboStat cs in c.stats)
                    {
                        if (cs.stat == null) continue;
                        e.mods.TryGetValue(cs.stat, out ArtifactBonusCache.Mod m);
                        m.offset += cs.offset * str;
                        m.factor += cs.factor * str;
                        e.mods[cs.stat] = m;
                    }
                }
            }
            return e.mods.TryGetValue(stat, out mod);
        }
    }

    public class StatPart_AvirAmulet : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn p)) return;
            if (!AmuletBonusCache.TryGet(p, parentStat, out ArtifactBonusCache.Mod m)) return;
            val += m.offset;
            val *= Mathf.Max(0f, 1f + m.factor);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is Pawn p)) return null;
            if (!AmuletBonusCache.TryGet(p, parentStat, out ArtifactBonusCache.Mod m)) return null;
            if (Mathf.Abs(m.offset) < 0.0001f && Mathf.Abs(m.factor) < 0.0001f) return null;
            CompAvirAmulet a = AmuletDevelopment.WornBy(p);
            return "Avir amulet (" + a.parent.LabelNoCount + "): "
                   + (m.offset != 0f ? parentStat.ValueToString(m.offset, ToStringNumberSense.Offset) + " " : "")
                   + (m.factor != 0f ? "x" + (1f + m.factor).ToStringPercent() : "");
        }
    }

    [StaticConstructorOnStartup]
    public static class AmuletStatPartInjector
    {
        static AmuletStatPartInjector()
        {
            foreach (AmuletSubclassDef s in DefDatabase<AmuletSubclassDef>.AllDefsListForReading)
            {
                foreach (ArtifactCellStat cs in s.cellStats)
                    if (cs.stat != null && cs.stat != StatDefOf.PsychicSensitivity) AmuletBonusCache.AffectedStats.Add(cs.stat);
                foreach (AmuletMilestoneStat ms in s.milestoneStats)
                    if (ms.stat != null && ms.stat != StatDefOf.PsychicSensitivity) AmuletBonusCache.AffectedStats.Add(ms.stat);
            }
            foreach (AmuletComboDef c in DefDatabase<AmuletComboDef>.AllDefsListForReading)
                foreach (AmuletComboStat cs in c.stats)
                    if (cs.stat != null && cs.stat != StatDefOf.PsychicSensitivity) AmuletBonusCache.AffectedStats.Add(cs.stat);
            foreach (StatDef stat in AmuletBonusCache.AffectedStats)
            {
                if (stat.parts == null) stat.parts = new List<StatPart>();
                if (stat.parts.Any(x => x is StatPart_AvirAmulet)) continue;
                stat.parts.Add(new StatPart_AvirAmulet { parentStat = stat });
            }
        }
    }
}
