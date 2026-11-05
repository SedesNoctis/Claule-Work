using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>One arm of one worn artifact that has reached a milestone.</summary>
    public class MilestoneArm
    {
        public CompArtifactSocket socket;
        public ArtifactState state;
        public ArtifactArmDef arm;
        /// <summary>Highest milestone cell reached (3, 7 or 9).</summary>
        public int reached;
        public bool dormant;   // switched off by the player
        public bool active;    // dormant == false and within the per-pawn limit

        public string Key => state.Uid + "|" + arm.defName;
    }

    /// <summary>
    /// Stage 8 - which milestones work on a pawn. Every (artifact, arm) pair that reached cell 3
    /// counts as one milestone arm. At most maxMilestoneArmsPerPawn (3) of them work at once; the
    /// player picks which by re-attuning. Until a pawn has more than the limit nothing needs
    /// choosing - all of them work.
    /// The milestone effects (stages 8b-8e) ask Level() and never look at artifacts themselves.
    /// </summary>
    public static class ArtifactMilestones
    {
        private class Entry
        {
            public int tick = -99999;
            public readonly List<MilestoneArm> arms = new List<MilestoneArm>();
        }

        private static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();
        private static readonly List<MilestoneArm> empty = new List<MilestoneArm>();
        private const int Interval = 250;

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public static void ClearAll() => cache.Clear();

        public static void Invalidate(Pawn p)
        {
            if (p != null) cache.Remove(p.thingIDNumber);
        }

        /// <summary>All milestone arms of the pawn's worn artifacts, in garment order.</summary>
        public static List<MilestoneArm> For(Pawn p)
        {
            if (p == null || S == null) return empty;
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
                Rebuild(p, e.arms);
            }
            return e.arms;
        }

        private static void Rebuild(Pawn p, List<MilestoneArm> list)
        {
            list.Clear();
            List<CompArtifactSocket> worn = ArtifactWearCache.Worn(p);
            for (int i = 0; i < worn.Count; i++)
            {
                ArtifactState st = worn[i].inlaid;
                if (st?.opened == null || !st.HasHeart) continue;
                if (st.charge <= 0f) continue;   // stage 10: an empty artifact gives no milestones
                foreach (KeyValuePair<ArtifactArmDef, int> kv in st.opened.OrderBy(x => x.Key.armNumber))
                {
                    if (!worn[i].Uses(kv.Key)) continue;   // stage 12: armour arms in armour, weapon arms in weapons
                    int reached = st.MilestoneReached(kv.Key);
                    if (reached <= 0) continue;
                    list.Add(new MilestoneArm
                    {
                        socket = worn[i], state = st, arm = kv.Key, reached = reached,
                        dormant = st.dormantArms != null && st.dormantArms.Contains(kv.Key)
                    });
                }
            }
            // Stage 12: armour and weapon arms are counted separately, each up to the limit.
            int limit = S.maxMilestoneArmsPerPawn, usedArmour = 0, usedWeapon = 0;
            foreach (MilestoneArm m in list)
            {
                bool weapon = m.arm.carrier == ArtifactCarrier.Weapon;
                int used = weapon ? usedWeapon : usedArmour;
                m.active = !m.dormant && used < limit;
                if (!m.active) continue;
                if (weapon) usedWeapon++; else usedArmour++;
            }
        }

        /// <summary>Highest active milestone the pawn has in `arm` (0 = none), and the strength of
        /// the artifact that gives it (focus x heart quality).</summary>
        public static int Level(Pawn p, ArtifactArmDef arm, out float strength)
        {
            strength = 0f;
            int best = 0;
            List<MilestoneArm> list = For(p);
            for (int i = 0; i < list.Count; i++)
            {
                MilestoneArm m = list[i];
                if (!m.active || m.arm != arm || m.reached <= best) continue;
                best = m.reached;
                strength = ArtifactFocus.Strength(m.state, arm, p);
            }
            return best;
        }

        public static int Level(Pawn p, ArtifactArmDef arm) => Level(p, arm, out _);

        /// <summary>How many milestone arms do not work because of the limit (not by choice).</summary>
        public static int OverLimit(Pawn p)
        {
            return For(p).Count(m => !m.dormant && !m.active);
        }

        /// <summary>Applies a re-attunement: exactly the arms in `keep` stay awake.</summary>
        public static void ApplyAttunement(Pawn p, HashSet<string> keep)
        {
            foreach (MilestoneArm m in For(p).ToList())
            {
                if (m.state.dormantArms == null) m.state.dormantArms = new List<ArtifactArmDef>();
                m.state.dormantArms.Remove(m.arm);
                if (!keep.Contains(m.Key)) m.state.dormantArms.Add(m.arm);
            }
            // stage 11: ability stones (at most two work)
            foreach (ActiveStone a in ArtifactAbilities.For(p))
                a.stone.dormant = !keep.Contains(a.Key);
            ArtifactAbilities.Sync(p);
            Invalidate(p);
            ArtifactBonusCache.Invalidate(p);
        }

        public static string StatusLine(Pawn p)
        {
            List<MilestoneArm> list = For(p);
            if (list.Count == 0) return null;
            var parts = list.Select(m => m.arm.LabelCap + " " + m.reached + (m.active ? "" : m.dormant ? " (asleep)" : " (over the limit)"));
            string s = "Milestones: " + string.Join(", ", parts);
            int over = OverLimit(p);
            if (over > 0) s += "\nOnly " + S.maxMilestoneArmsPerPawn + " armour arms and " + S.maxMilestoneArmsPerPawn
                               + " weapon arms can give milestones at once - re-attune to choose.";
            return s;
        }
    }

    /// <summary>Pending re-attunements, kept in the save so a job interrupted by loading still
    /// knows what the player chose.</summary>
    public class GameComponent_ArtifactAttunement : GameComponent
    {
        private Dictionary<int, List<string>> pending = new Dictionary<int, List<string>>();
        private List<int> tmpKeys;
        private List<List<string>> tmpValues;

        public GameComponent_ArtifactAttunement(Game game) { }

        public static GameComponent_ArtifactAttunement Get => Current.Game?.GetComponent<GameComponent_ArtifactAttunement>();

        public void Set(Pawn p, IEnumerable<string> keep) => pending[p.thingIDNumber] = keep.ToList();

        public bool TryTake(Pawn p, out HashSet<string> keep)
        {
            keep = null;
            if (!pending.TryGetValue(p.thingIDNumber, out List<string> l)) return false;
            pending.Remove(p.thingIDNumber);
            keep = new HashSet<string>(l);
            return true;
        }

        public bool Has(Pawn p) => pending.ContainsKey(p.thingIDNumber);

        public override void ExposeData()
        {
            // a dictionary of lists does not save directly: flatten to "key=a;b;c"
            List<string> flat = Scribe.mode == LoadSaveMode.Saving
                ? pending.Select(kv => kv.Key + "=" + string.Join(";", kv.Value)).ToList()
                : null;
            Scribe_Collections.Look(ref flat, "pendingAttunements", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                pending = new Dictionary<int, List<string>>();
                if (flat != null)
                    foreach (string line in flat)
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0 || !int.TryParse(line.Substring(0, eq), out int id)) continue;
                        pending[id] = line.Substring(eq + 1).Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
                    }
            }
        }
    }

    /// <summary>The wearer re-attunes its artifacts where it stands - field work, no spot needed.</summary>
    public class JobDriver_ArtifactAttune : JobDriver
    {
        public const int Ticks = 2500;   // 1 in-game hour

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !(GameComponent_ArtifactAttunement.Get?.Has(pawn) ?? false));
            Toil wait = Toils_General.Wait(Ticks);
            wait.WithProgressBarToilDelay(TargetIndex.None);
            wait.handlingFacing = false;
            yield return wait;
            yield return new Toil
            {
                initAction = () =>
                {
                    if (GameComponent_ArtifactAttunement.Get?.TryTake(pawn, out HashSet<string> keep) == true)
                    {
                        ArtifactMilestones.ApplyAttunement(pawn, keep);
                        Messages.Message(pawn.LabelShort + " re-attuned " + pawn.Possessive() + " artifacts.", pawn,
                            MessageTypeDefOf.TaskCompletion, false);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    /// <summary>Choose which milestone arms of a pawn work. Confirming sends the pawn to the
    /// work for one hour where it stands; nothing changes until the work is done.</summary>
    public class Dialog_ArtifactAttune : Window
    {
        private readonly Pawn pawn;
        private readonly HashSet<string> chosen = new HashSet<string>();
        private Vector2 scroll;

        private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        private static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);

        public Dialog_ArtifactAttune(Pawn pawn)
        {
            this.pawn = pawn;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            foreach (MilestoneArm m in ArtifactMilestones.For(pawn))
                if (m.active) chosen.Add(m.Key);
            foreach (ActiveStone a in ArtifactAbilities.For(pawn))
                if (a.active) chosenStones.Add(a.Key);
        }

        private readonly HashSet<string> chosenStones = new HashSet<string>();

        /// <summary>Anything to re-attune: milestone arms or ability stones.</summary>
        public static bool HasAnything(Pawn p) => ArtifactMilestones.For(p).Count > 0 || ArtifactAbilities.For(p).Count > 0;

        public override Vector2 InitialSize => new Vector2(520f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            int limit = s.maxMilestoneArmsPerPawn;
            List<MilestoneArm> list = ArtifactMilestones.For(pawn);

            Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "Re-attune " + pawn.LabelShort);
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            ArtInk.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 44f),
                "Choose up to " + limit + " armour arms and " + limit + " weapon arms whose milestones will work, and which ability stones work "
                + "(armour and weapon counted apart). The others keep their cells and passive bonuses. "
                + "The pawn spends one hour on it, wherever it stands.");
            GUI.color = Color.white;

            List<ActiveStone> stones = ArtifactAbilities.For(pawn);
            int stoneLimit = s.maxActiveAbilitiesPerPawn;
            Rect listRect = new Rect(inRect.x, inRect.y + 84f, inRect.width, inRect.height - 84f - 44f);
            Rect view = new Rect(0f, 0f, listRect.width - 16f, list.Count * 34f + (stones.Count > 0 ? 40f + stones.Count * 34f : 0f));
            Widgets.BeginScrollView(listRect, ref scroll, view);
            float y = 0f;
            foreach (MilestoneArm m in list)
            {
                Rect row = new Rect(0f, y, view.width, 32f);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                bool on = chosen.Contains(m.Key);
                bool was = on;
                string label = m.arm.LabelCap + " - milestone " + m.reached + "   (" + m.socket.parent.LabelNoCount + ")";
                int sameKind = list.Count(x => chosen.Contains(x.Key) && x.arm.carrier == m.arm.carrier);
                Widgets.CheckboxLabeled(row, label, ref on, !on && sameKind >= limit);
                if (on != was)
                {
                    if (on) chosen.Add(m.Key); else chosen.Remove(m.Key);
                }
                y += 34f;
            }
            if (stones.Count > 0)
            {
                y += 6f;
                ArtInk.Label(new Rect(0f, y, view.width, 30f), "Ability stones - up to " + stoneLimit + " in armour and " + stoneLimit + " in weapons work:");
                y += 34f;
                foreach (ActiveStone a in stones)
                {
                    Rect row = new Rect(0f, y, view.width, 32f);
                    if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                    bool on = chosenStones.Contains(a.Key);
                    bool was = on;
                    string label = a.stone.ability.LabelCap + " (" + a.stone.metal?.label + ")   (" + a.socket.parent.LabelNoCount + ")";
                    int sameStones = stones.Count(x => chosenStones.Contains(x.Key) && x.socket.Carrier == a.socket.Carrier);
                    Widgets.CheckboxLabeled(row, label, ref on, !on && sameStones >= stoneLimit);
                    if (on != was)
                    {
                        if (on) chosenStones.Add(a.Key); else chosenStones.Remove(a.Key);
                    }
                    y += 34f;
                }
            }
            Widgets.EndScrollView();

            Rect btn = new Rect(inRect.xMax - 220f, inRect.yMax - 38f, 220f, 36f);
            int armA = list.Count(x => chosen.Contains(x.Key) && x.arm.carrier == ArtifactCarrier.Apparel);
            int armW = list.Count(x => chosen.Contains(x.Key) && x.arm.carrier == ArtifactCarrier.Weapon);
            ArtInk.Label(new Rect(inRect.x, btn.y + 8f, 300f, 24f), "Armour " + armA + "/" + limit + ", weapon " + armW + "/" + limit + ", stones " + chosenStones.Count);
            if (Widgets.ButtonText(btn, "Re-attune"))
            {
                if (TryOrder()) Close();
            }
        }

        private bool TryOrder()
        {
            if (pawn.Map == null || !pawn.Spawned || pawn.Downed)
            {
                Messages.Message(pawn.LabelShort + " cannot re-attune right now.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            GameComponent_ArtifactAttunement.Get?.Set(pawn, chosen.Concat(chosenStones));
            Job job = JobMaker.MakeJob(ArtifactoricsJobDefOf.Avir_ArtifactAttune);
            job.playerForced = true;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }
    }
}
