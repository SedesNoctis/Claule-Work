using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // CONSTELLATIONS - Avir multi-artifacts. Crafted like sculptures at the round table (Crafting and
    // Artistic, feignallite), installed as round platforms set into the ground: 1x1 with 3 sockets,
    // 3x3 with 5, 5x5 with 7. Each socket is given a function - any effect of the other artifact
    // buildings - and the same function may take several sockets (it grows stronger). Changing the
    // functions retunes it: every stone is drained, and an attuner charges them again.
    // Quality matters far more than for ordinary buildings.
    // =====================================================================================

    public class CompProperties_Constellation : CompProperties
    {
        /// <summary>Power factor by quality, Awful ... Legendary.</summary>
        public List<float> qualityFactors = new List<float> { 0.9f, 1f, 1.1f, 1.2f, 1.35f, 1.55f, 1.8f };
        /// <summary>Cells of reach every socket given to a function adds to it.</summary>
        public float rangePerSocket = 3f;
        public CompProperties_Constellation() { compClass = typeof(CompConstellation); }
    }

    public class CompConstellation : ThingComp
    {
        /// <summary>Per socket: the function (effect class name), or "" for none.</summary>
        public List<string> assigned = new List<string>();
        private CompProperties_Constellation P => (CompProperties_Constellation)props;

        private readonly Dictionary<string, CompArtifactHost> views = new Dictionary<string, CompArtifactHost>();
        private int viewsTick = -1;

        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { nameof(CompHearthStone), "Hearth - snow, dirt, rough warmth" },
            { nameof(CompGrowthTotem), "Growth - plants grow faster" },
            { nameof(CompCalmBell), "Calm - colonists feel calm" },
            { nameof(CompMendingStone), "Mending - repairs buildings" },
            { nameof(CompHealingStone), "Healing - the sick heal faster" },
            { nameof(CompWorkPylon), "Craft - workbenches work faster" },
            { nameof(CompTkCrane), "Hauling - lifts things to storage" },
            { nameof(CompDragWarden), "Drag - foes are slowed" },
            { nameof(CompObserver), "Watch - fewer fights, sees the unseen" },
            { nameof(CompTempAttuner), "Temperature - exact, as you set it" },
            { nameof(CompTotemBlast), "Blast - strikes foes at range" },
            { nameof(CompTotemScare), "Scare - drives predators off" },
            { nameof(CompShieldAuraTotem), "Shields - an aura shield on the colony's people" },
            { nameof(CompBarrierTotem), "Barrier - a dome that stops shots from outside" },
        };

        private static readonly HashSet<string> PowerPerSocket = new HashSet<string>
            { nameof(CompGrowthTotem), nameof(CompCalmBell), nameof(CompMendingStone), nameof(CompTotemBlast), nameof(CompTotemScare), nameof(CompHealingStone) };

        private CompArtifactHost Real => parent.TryGetComp<CompArtifactHost>();
        public int Sockets => Real?.Props.sockets ?? 0;
        public float RangePerSocket => P.rangePerSocket;

        public float QualityFactor
        {
            get
            {
                if (!parent.TryGetQuality(out QualityCategory q)) return 1f;
                int i = (int)q;
                return i >= 0 && i < P.qualityFactors.Count ? P.qualityFactors[i] : 1f;
            }
        }

        public IEnumerable<CompArtifactTotem> Functions => parent.AllComps.OfType<CompArtifactTotem>();

        public static string Key(ThingComp c) => c.GetType().Name;

        public string FunctionLabel(int socket)
        {
            Ensure();
            string k = socket < assigned.Count ? assigned[socket] : "";
            return k.NullOrEmpty() ? "none" : Labels.TryGetValue(k, out string l) ? l : k;
        }

        public bool Assigned(CompArtifactTotem c) { Ensure(); return assigned.Contains(Key(c)); }

        private void Ensure()
        {
            while (assigned.Count < Sockets) assigned.Add("");
        }

        /// <summary>The stones of the sockets given to this function, as a host of their own.</summary>
        public static CompArtifactHost HostFor(CompArtifactTotem c)
        {
            CompConstellation cons = c.parent.TryGetComp<CompConstellation>();
            return cons?.View(Key(c));
        }

        private CompArtifactHost View(string key)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now != viewsTick) { views.Clear(); viewsTick = now; }
            if (views.TryGetValue(key, out CompArtifactHost v)) return v;
            CompArtifactHost real = Real;
            if (real == null) return null;
            Ensure();
            v = new CompArtifactHost { parent = parent, props = real.props };
            int given = 0;
            for (int i = 0; i < real.slots.Count && i < assigned.Count; i++)
                if (assigned[i] == key) { given++; if (real.slots[i] != null) v.slots.Add(real.slots[i]); }
            // these grow with their stones' quality only - here they also grow +20% a socket after the first
            float boost = PowerPerSocket.Contains(key) ? 1f + 0.2f * Mathf.Max(0, given - 1) : 1f;
            v.qualityOverride = QualityFactor * boost;
            views[key] = v;
            return v;
        }

        /// <summary>New functions: every stone is drained; an attuner charges them again.</summary>
        public void Retune(List<string> next)
        {
            assigned = new List<string>(next);
            views.Clear();
            CompArtifactHost real = Real;
            if (real != null) foreach (ArtifactState s in real.slots) if (s != null) s.charge = 0f;
            Messages.Message(parent.LabelCap + " is retuned: its stones are drained and wait for an attuner.", parent, MessageTypeDefOf.NeutralEvent, false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Functions...",
                defaultDesc = "Give each socket a function. The same function may take several sockets and grows stronger with each. Changing them retunes the constellation: its stones are drained and an attuner charges them again.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/ConstellationFunctions", false) ?? ArtifactoricsTex.StoneOpened ?? BaseContent.BadTex,
                action = () => Find.WindowStack.Add(new Dialog_ConstellationFunctions(this))
            };
        }

        public override string CompInspectStringExtra()
        {
            Ensure();
            var groups = assigned.Where(a => !a.NullOrEmpty()).GroupBy(a => a).Select(g => (Labels.TryGetValue(g.Key, out string l) ? l.Split(' ')[0] : g.Key) + (g.Count() > 1 ? " x" + g.Count() : ""));
            string list = string.Join(", ", groups);
            return "Constellation, quality x" + QualityFactor.ToString("0.00") + (list.NullOrEmpty() ? "; no function chosen." : "; functions: " + list + ".");
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref assigned, "assigned", LookMode.Value);
            if (assigned == null) assigned = new List<string>();
        }
    }

    public class Dialog_ConstellationFunctions : Window
    {
        private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        private static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);
        private static readonly Color Gold = new Color(0.70f, 0.50f, 0.12f);
        private readonly CompConstellation cons;
        private readonly List<string> pending;

        public Dialog_ConstellationFunctions(CompConstellation cons)
        {
            this.cons = cons;
            pending = new List<string>(cons.assigned);
            while (pending.Count < cons.Sockets) pending.Add("");
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(860f, Mathf.Max(560f, 200f + 52f * cons.Sockets));

        private int selected;
        private Vector2 fnScroll;

        private static string LabelOf(string key) =>
            key.NullOrEmpty() ? "(no function)" : CompConstellation.Labels.TryGetValue(key, out string l) ? l : key;

        /// <summary>Sockets on the left, the functions as a list on the right: pick a socket, then click its function.</summary>
        public override void DoWindowContents(Rect box)
        {
            Rect r = Avir.Ritualistics.ParchmentFrame.Draw(box).ContractedBy(6f);
            GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), cons.parent.LabelCap + " - functions");
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = InkSoft;
            ArtInk.Label(new Rect(r.x, r.y + 34f, r.width, 40f), "Pick a socket on the left, then its function in the list on the right. A function given several sockets is as strong as their stones together, and every socket adds 3 cells to its reach. Quality of the constellation: x" + cons.QualityFactor.ToString("0.00") + ".");
            CompArtifactHost real = cons.parent.TryGetComp<CompArtifactHost>();
            selected = Mathf.Clamp(selected, 0, pending.Count - 1);
            float top = r.y + 80f, bottom = r.yMax - 80f;
            float leftW = r.width * 0.46f;
            Rect left = new Rect(r.x, top, leftW, bottom - top);
            Rect right = new Rect(left.xMax + 12f, top, r.xMax - left.xMax - 12f, bottom - top);

            // the sockets
            float y = left.y;
            for (int i = 0; i < pending.Count; i++)
            {
                Rect row = new Rect(left.x, y, left.width, 46f);
                bool sel = i == selected;
                Widgets.DrawBoxSolid(row, sel ? new Color(0.85f, 0.62f, 0.2f, 0.32f) : Mouse.IsOver(row) ? new Color(1f, 0.9f, 0.6f, 0.22f) : new Color(1f, 0.92f, 0.7f, 0.14f));
                GUI.color = new Color(Gold.r, Gold.g, Gold.b, sel ? 0.95f : 0.5f); Widgets.DrawBox(row, sel ? 2 : 1);
                ArtifactState st = real != null && i < real.slots.Count ? real.slots[i] : null;
                GUI.color = Ink;
                ArtInk.Label(new Rect(row.x + 10f, row.y + 3f, row.width - 20f, 22f), "Socket " + (i + 1) + ":  " + LabelOf(pending[i]));
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                ArtInk.Label(new Rect(row.x + 10f, row.y + 24f, row.width - 20f, 20f),
                    st == null ? "empty - inlay a stone" : (st.heartMetal?.label ?? "?") + " heart, " + st.heartQuality.GetLabel() + ", power x" + CompArtifactHost.StonePower(st).ToString("0.00"));
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(row)) { selected = i; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += 50f;
            }

            // the functions
            var keys = new List<string> { "" };
            foreach (CompArtifactTotem f in cons.Functions)
            {
                string key = CompConstellation.Key(f);
                if (!keys.Contains(key)) keys.Add(key);
            }
            Widgets.DrawBoxSolid(right, new Color(1f, 0.92f, 0.7f, 0.12f));
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.5f); Widgets.DrawBox(right, 1);
            Rect view = new Rect(0f, 0f, right.width - 20f, keys.Count * 32f);
            Widgets.BeginScrollView(right.ContractedBy(4f), ref fnScroll, view);
            for (int k = 0; k < keys.Count; k++)
            {
                Rect row = new Rect(0f, k * 32f, view.width, 30f);
                bool on = pending[selected] == keys[k];
                int uses = keys[k].NullOrEmpty() ? 0 : pending.Count(p => p == keys[k]);
                Widgets.DrawBoxSolid(row, on ? new Color(0.85f, 0.62f, 0.2f, 0.35f) : Mouse.IsOver(row) ? new Color(1f, 0.9f, 0.6f, 0.25f) : Color.clear);
                GUI.color = on ? Ink : InkSoft;
                ArtInk.Label(new Rect(row.x + 8f, row.y + 5f, row.width - 60f, 24f), LabelOf(keys[k]));
                if (uses > 0)
                {
                    Text.Anchor = TextAnchor.MiddleRight; Text.Font = GameFont.Tiny;
                    ArtInk.Label(new Rect(row.xMax - 60f, row.y, 52f, row.height), "x" + uses);
                    Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                }
                if (Widgets.ButtonInvisible(row)) { pending[selected] = keys[k]; SoundDefOf.Tick_Low.PlayOneShotOnCamera(); }
            }
            Widgets.EndScrollView();

            bool changed = !pending.SequenceEqual(cons.assigned.Concat(Enumerable.Repeat("", Mathf.Max(0, pending.Count - cons.assigned.Count))));
            GUI.color = InkSoft; Text.Font = GameFont.Tiny;
            ArtInk.Label(new Rect(r.x, r.yMax - 70f, r.width, 24f), changed ? "Applying retunes it: every stone is drained, and an attuner charges them again." : "No change.");
            Text.Font = GameFont.Small;
            if (Button(new Rect(r.center.x - 150f, r.yMax - 42f, 300f, 38f), changed ? "Retune" : "Close"))
            {
                if (changed) cons.Retune(pending);
                Close();
            }
        }

        private static bool Button(Rect r, string label)
        {
            bool over = Mouse.IsOver(r);
            Texture2D tex = !over ? ArtifactoricsTex.ButtonNormal : Input.GetMouseButton(0) ? ArtifactoricsTex.ButtonPressed : ArtifactoricsTex.ButtonHover;
            GUI.color = Color.white;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = Ink;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(r)) { SoundDefOf.Click.PlayOneShotOnCamera(); return true; }
            return false;
        }
    }
}
