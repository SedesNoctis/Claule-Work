using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Ritualistics
{
    // =====================================================================================
    // TRANSMUTATION, as a recipe: from the ritual editor. What the ritual makes (the seal) and everything it is made
    // from - any number of material circles, the same material more than once (a stack each) or different ones. All
    // of it goes into the one thing the seal names, by value, pooled: three stacks of steel, or steel, silver and jade
    // together, for one resource. Each input is a component circle of the ritual (its own cell, filled by the haulers).
    // =====================================================================================
    public class Dialog_TransmuteRecipe : Window
    {
        private readonly RitualSchema s;
        private readonly int maxComponents;
        private readonly Map map;
        private Vector2 scroll;
        private readonly Dictionary<RitualComponent, string> bufs = new Dictionary<RitualComponent, string>();

        public Dialog_TransmuteRecipe(RitualSchema s, int maxComponents, Map map)
        {
            this.s = s;
            this.maxComponents = maxComponents;
            this.map = map;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = true;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(820f, Verse.UI.screenWidth - 60f), Mathf.Min(620f, Verse.UI.screenHeight - 60f));

        private RitualComponent Seal => s.components.FirstOrDefault(c => c.kind == ComponentKind.Seal);
        private List<RitualComponent> Inputs => s.components.Where(c => c.kind == ComponentKind.Material).ToList();

        /// <summary>A component circle for a new part: an empty one if there is, else a new one while there is room.</summary>
        private RitualComponent FreeCircle()
        {
            RitualComponent free = s.components.FirstOrDefault(c => c.kind == ComponentKind.None);
            if (free != null) return free;
            if (s.components.Count >= maxComponents) return null;
            free = new RitualComponent();
            s.components.Add(free);
            return free;
        }

        private bool HasRoom => s.components.Any(c => c.kind == ComponentKind.None) || s.components.Count < maxComponents;

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(map);
            Rect r = ParchmentFrame.Draw(box).ContractedBy(10f);
            Text.Font = GameFont.Medium; GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "Transmutation");
            Text.Font = GameFont.Small;
            float y = r.y + 40f;

            RitualReading rd = RitualEvaluator.Read(s);
            if (!rd.Has(RuneKind.Transmute))
            {
                GUI.color = FlowStyle.Light ? new Color(1f, 0.6f, 0.55f) : new Color(0.65f, 0.15f, 0.12f);
                Widgets.Label(new Rect(r.x, y, r.width, 24f), "Write a transmutation rune into the circle - without it nothing here is read.");
                y += 28f;
            }

            // what it makes
            GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(r.x, y + 6f, 150f, 24f), "Makes:");
            RitualComponent seal = Seal;
            Rect sealR = new Rect(r.x + 150f, y, 300f, 34f);
            if (seal?.thing != null) { GUI.color = Color.white; Widgets.ThingIcon(new Rect(sealR.x, sealR.y + 3f, 28f, 28f), seal.thing); }
            if (RitualUI.Button(new Rect(sealR.x + 34f, sealR.y, 260f, 34f), seal?.thing != null ? seal.thing.LabelCap.ToString() : "choose what it makes...", seal != null || HasRoom))
                MaterialMenu(d =>
                {
                    RitualComponent sc = Seal ?? FreeCircle();
                    if (sc == null) return;
                    sc.kind = ComponentKind.Seal; sc.thing = d; sc.amount = 1;
                });
            y += 44f;

            // what it is made from
            GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(r.x, y, r.width, 24f), "Made from (each a material circle with its own cell; at most a stack in each):");
            y += 28f;
            List<RitualComponent> ins = Inputs;
            Rect list = new Rect(r.x, y, r.width, r.yMax - y - 120f);
            Rect view = new Rect(0f, 0f, list.width - 16f, Mathf.Max(list.height, ins.Count * 40f));
            RitualScroll.Begin(list, ref scroll, view);
            float ry = 0f;
            if (ins.Count == 0)
            {
                GUI.color = FlowStyle.InkSoft;
                Widgets.Label(new Rect(0f, 0f, view.width, 48f), "No material circles: the ritual takes any metal or jade lying in the main circle.");
            }
            RitualComponent remove = null;
            foreach (RitualComponent m in ins)
            {
                Rect row = new Rect(0f, ry, view.width, 36f);
                RitualUI.Card(row, false);
                if (m.thing != null) { GUI.color = Color.white; Widgets.ThingIcon(new Rect(row.x + 4f, row.y + 4f, 28f, 28f), m.thing); }
                RitualComponent mm = m;
                if (RitualUI.Button(new Rect(row.x + 38f, row.y + 2f, 190f, 32f), m.thing?.LabelCap.ToString() ?? "choose...", true))
                    MaterialMenu(d => { mm.thing = d; mm.amount = Mathf.Clamp(mm.amount, 1, d.stackLimit); bufs.Remove(mm); });
                Amount(m, new Rect(row.x + 236f, row.y + 2f, 400f, 32f));
                if (RitualUI.Button(new Rect(row.xMax - 36f, row.y + 2f, 32f, 32f), "x", true)) remove = m;
                ry += 40f;
            }
            Widgets.EndScrollView();
            if (remove != null) s.components.Remove(remove);

            float by = list.yMax + 6f;
            if (RitualUI.Button(new Rect(r.x, by, 220f, 34f), "+ Add a material", HasRoom))
            {
                RitualComponent c = FreeCircle();
                if (c != null)
                {
                    c.kind = ComponentKind.Material;
                    c.thing = Inputs.LastOrDefault(x => x != c)?.thing ?? Transmutation.Materials.FirstOrDefault(d => d != Seal?.thing);
                    c.amount = Mathf.Max(1, c.thing?.stackLimit ?? 1);
                }
            }
            GUI.color = FlowStyle.InkSoft; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(r.x + 230f, by + 2f, r.width - 230f, 34f),
                "Component circles: " + s.components.Count(c => c.kind != ComponentKind.None) + " of " + maxComponents + " used (a larger circle holds more).");
            Text.Font = GameFont.Small;

            // the sum
            GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(r.x, by + 42f, r.width - 200f, 50f), Summary(rd));
            if (RitualUI.Button(new Rect(r.xMax - 180f, r.yMax - 44f, 180f, 42f), "Done")) Close();
            GUI.color = Color.white;
            FlowStyle.End();
        }

        private string Summary(RitualReading rd)
        {
            ThingDef target = Seal?.thing;
            List<RitualComponent> ins = Inputs.Where(m => m.thing != null && m.thing != target).ToList();
            if (target == null) return "Choose what the ritual makes.";
            if (ins.Count == 0) return "Makes " + target.label + " from whatever metal or jade lies in the main circle.";
            float value = ins.Sum(m => m.thing.BaseMarketValue * Mathf.Max(1, m.amount));
            float good = rd.TransmuteYield(1f);
            int outs = Mathf.FloorToInt(value * good / Mathf.Max(0.01f, target.BaseMarketValue));
            int best = Mathf.FloorToInt(value * rd.MaxYield / Mathf.Max(0.01f, target.BaseMarketValue));
            return "All of it, " + value.ToStringMoney() + " in value, goes into " + target.label + ": about " + outs + " with a good drawing ("
                   + good.ToStringPercent() + "), up to " + best + " from the strongest circle of this size (" + rd.MaxYield.ToStringPercent() + "). What is left after the last whole unit is lost into the stream.";
        }

        private void Amount(RitualComponent m, Rect row)
        {
            int max = Mathf.Max(1, m.thing?.stackLimit ?? 1);
            int amount = Mathf.Clamp(m.amount, 1, max);
            if (!bufs.TryGetValue(m, out string buf)) buf = amount.ToString();
            int before = amount;
            float x = row.x;
            if (RitualUI.Button(new Rect(x, row.y, 44f, 32f), "-10")) amount = Mathf.Max(1, amount - 10);
            if (RitualUI.Button(new Rect(x + 48f, row.y, 32f, 32f), "-")) amount = Mathf.Max(1, amount - 1);
            Widgets.TextFieldNumeric(new Rect(x + 84f, row.y + 1f, 64f, 30f), ref amount, ref buf, 1f, max);
            if (RitualUI.Button(new Rect(x + 152f, row.y, 32f, 32f), "+")) amount = Mathf.Min(max, amount + 1);
            if (RitualUI.Button(new Rect(x + 188f, row.y, 44f, 32f), "+10")) amount = Mathf.Min(max, amount + 10);
            if (RitualUI.Button(new Rect(x + 236f, row.y, 70f, 32f), "stack")) amount = max;
            amount = Mathf.Clamp(amount, 1, max);
            if (amount != before) buf = amount.ToString();
            bufs[m] = buf;
            m.amount = amount;
            GUI.color = FlowStyle.InkSoft; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(x + 312f, row.y + 8f, 90f, 24f), "of " + max);
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        private static void MaterialMenu(System.Action<ThingDef> pick)
        {
            var opts = new List<FloatMenuOption>();
            foreach (ThingDef d in Transmutation.Materials)
            {
                ThingDef dd = d;
                opts.Add(new FloatMenuOption(d.LabelCap + " (" + d.BaseMarketValue.ToStringMoney() + " each)", () => { pick(dd); SoundDefOf.Click.PlayOneShotOnCamera(); },
                    dd.uiIcon, Color.white));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }
    }
}
