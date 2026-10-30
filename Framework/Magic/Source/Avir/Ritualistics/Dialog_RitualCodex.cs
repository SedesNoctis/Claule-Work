using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Ritualistics
{
    /// <summary>The codex, on its own large page: every rune on the left as its glyph, the chosen one on the right in
    /// large letters - in plain words by default (Arlia's own short version), or in full. Runes not yet researched are
    /// shown faded.</summary>
    public class Dialog_RitualCodex : Window
    {
        private static Color Ink => FlowStyle.Ink;
        private static Color InkSoft => FlowStyle.InkSoft;
        private static Color Gold => FlowStyle.Gold;

        private RuneDef shown;
        private Vector2 scroll, textScroll;
        private readonly Map map;

        private static readonly Dictionary<string, string> simple = new Dictionary<string, string>
        {
            { "Fill", "Pours energy into whatever is in the circle: artifacts, batteries, a psycaster's focus. Feeding, basically." },
            { "Transfer", "Takes experience from one artifact and gives it to another. Robbing a rich to pay for the wicked, in ink." },
            { "Purge", "Returns an artifact or amulet to how it was made. Every point back, every mistake forgotten." },
            { "Reforge", "Remakes the artifact in the circle - a better heart, another metal, a new spiral. Large circle only. Why? Because it's better this way." },
            { "Infuse", "Turns the ritual's energy into an artifact's experience. Slow, safe, and quite boring." },
            { "Transmute", "Turns one material into another, by value. Needs a seal to know what it is making." },
            { "Release", "Gathers energy, then lets it all out at once. Usually loudly. Usually near someone." },
            { "Hold", "Keeps something in place while the ritual runs. Things, people, the odd regret." },
            { "Drain", "Pulls energy out of what is around - pylons, foes, strangers. They tire. You do not." },
            { "Lift", "Makes the release throw things instead of tearing them. Gentler. Slightly." },
            { "Growth", "Makes things grow - plants, wounds closing, the occasional ego." },
            { "Amplify", "More of everything the verbs do. Including the mistakes." },
            { "Reach", "Lets the ritual act on a point you choose, anywhere on the land." },
            { "Breadth", "Makes the ritual act wider around the circle." },
            { "Anchor", "Keeps the ritual going for as long as energy streams in." },
            { "Safety", "Calms the flow; less chance it bites. Closed into a ring, it becomes a cage." },
            { "Closing", "Seals the ritual so nothing leaks out. Essential for storms and ambitions." },
            { "Focus", "Draws the stream of the land into the circle. Too much, and the sky answers." },
            { "Barrier", "Shields against what comes from outside. With an anchor and no verb - a dome." },
            { "Thread", "Carries the ritual along connections - power grids, for one - to places far from the circle." },
            // magic
            { "Fire", "Fire. Everyone's first love. Try not to make it your last." },
            { "Water", "Water. Patient, heavy, everywhere. Unlike your attention." },
            { "Earth", "Earth. Slow, stubborn, reliable. I see why you like it." },
            { "Air", "Air. Push, lift, steal a breath. Light work for light minds - no offence." },
            { "Light", "Light. It shows everything, flaws included. Yours especially." },
            { "Time", "Time. Ask nicely and it bends. Ask rudely and it ages you." },
            { "Life", "Life. Take from one, give to another. Someone always pays - make sure it isn't you." },
            { "Darkness", "Darkness. Every reading at once. Weave it into anything and see what it becomes - carefully." },
            { "Space", "Space. Weigh it, catch it, pull. If you can fly, you already half know it." },
            { "Aura", "An aura. You, but louder - all around you." },
            { "Enchant", "Lay the element on a blade, a body, a friend. Not on yourself first, please." },
            { "Step", "A step. Getting from here to there without the boring part in between." },
            { "Restore", "Putting things back the way they were. If only it worked on reputations." },
            { "Beam", "A beam. Hold it, aim it, keep holding. Most let go too early." },
            { "Shape", "Reshape the ground, the walls, the world. Start small. Smaller." },
            { "Summon", "Calling something forth. It answers. Whether it listens is another matter." },
            { "Sense", "Seeing what others miss. Quietly. The rarest talent - I would know." },
        };

        /// <summary>A rune in plain words (Arlie's short version), or its full description.</summary>
        public static string Plain(RuneDef r) => simple.TryGetValue(r.kind.ToString(), out string s) ? s : RitualText.Desc(r);

        public Dialog_RitualCodex(Map map)
        {
            this.map = map;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            shown = RuneDef.All.FirstOrDefault(r => r.Unlocked) ?? RuneDef.All.FirstOrDefault();
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1100f, Verse.UI.screenWidth - 60f), Mathf.Min(760f, Verse.UI.screenHeight - 60f));

        private bool simpleHas(RuneDef r) => simple.ContainsKey(r.kind.ToString());
        private static string simple_(RuneDef r) => simple[r.kind.ToString()];

        public static bool Pinned(RuneDef r) => AvirMod.Settings?.codexPinned != null && AvirMod.Settings.codexPinned.Contains(r.defName);

        public static void SetPinned(RuneDef r, bool on)
        {
            if (AvirMod.Settings == null) return;
            AvirMod.Settings.codexPinned.Remove(r.defName);
            if (on) AvirMod.Settings.codexPinned.Add(r.defName);
            AvirMod.Settings.Write();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private static bool Simple => AvirMod.Settings == null || AvirMod.Settings.codexSimple;

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(map);
            Rect inner = ParchmentFrame.Draw(box).ContractedBy(8f);
            Rect left = new Rect(inner.x, inner.y, 300f, inner.height);
            Rect right = new Rect(left.xMax + 24f, inner.y, inner.width - 324f, inner.height);

            RitualArt.Panel(left.ExpandedBy(6f)); RitualArt.Panel(right.ExpandedBy(6f));
            if (RitualArt.HasPanel) { left = left.ContractedBy(10f); right = right.ContractedBy(14f); }
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(left.x, left.y, left.width, 34f), "Codex");
            Text.Font = GameFont.Small;
            List<RuneDef> all = RuneDef.All;
            Rect area = new Rect(left.x, left.y + 40f, left.width, left.height - 40f);
            Rect view = new Rect(0f, 0f, area.width - 16f, all.Count * 58f);
            RitualScroll.Begin(area, ref scroll, view);
            float y = 0f;
            foreach (RuneDef r in all)
            {
                Rect row = new Rect(0f, y, view.width, 54f);
                bool on = r == shown;
                if (on && !RitualArt.Selected(row)) { Widgets.DrawBoxSolid(row, new Color(Gold.r, Gold.g, Gold.b, 0.2f)); GUI.color = Gold; Widgets.DrawBox(row, 1); }
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                GUI.color = r.Unlocked ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(new Rect(row.x + 4f, row.y + 3f, 48f, 48f), FlowStyle.Pick(r.Ink, r.Glow), ScaleMode.ScaleToFit);
                GUI.color = r.Unlocked ? Ink : FlowStyle.InkGrey;
                Text.Anchor = TextAnchor.MiddleLeft;
                ArtInk.Label(new Rect(row.x + 60f, row.y, row.width - 64f, row.height), r.LabelCap + (r.Unlocked ? "" : "  (not researched)") + (Pinned(r) ? "  - pinned" : ""));
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(row))
                {
                    shown = r;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    Arlia.Say(r.Unlocked ? "rune" : "unknownrune", 2.5f);   // a passing word; her real one is on the page
                }
                y += 58f;
            }
            Widgets.EndScrollView();

            // the chosen rune, large
            if (shown != null)
            {
                bool pinned = Pinned(shown);
                Rect pin = new Rect(right.xMax - 176f, right.y, 176f, 32f);
                if (RitualUI.Button(pin, pinned ? "Unpin" : "Pin to the editor", true, pinned)) { SetPinned(shown, !pinned); if (!pinned) Arlia.Say("pin"); }
                TooltipHandler.TipRegion(pin, "A pinned rune stands first in the editor's list of runes, with its short explanation under its name.");
                if (Arlia.On)
                {
                    Rect again = new Rect(pin.x - 186f, right.y, 176f, 32f);
                    if (RitualUI.Button(again, "Lessons again", true))
                    {
                        AvirMod.Settings.arliaIntroBook = false; AvirMod.Settings.arliaIntroEditor = false; AvirMod.Settings.Write();
                        Arlia.Say("lessons");
                    }
                    TooltipHandler.TipRegion(again, Arlia.Name + " walks you through the ritual book and the editor again, the next time each is opened.");
                }
                GUI.color = shown.Unlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                Rect glyph = new Rect(right.x + 20f, right.y + 60f, 140f, 140f);
                FlowStyle.Backing(glyph.ExpandedBy(18f));
                GUI.DrawTexture(glyph, FlowStyle.Pick(shown.Ink, shown.Glow), ScaleMode.ScaleToFit);
                GUI.color = Ink; Text.Font = GameFont.Medium;
                ArtInk.Label(new Rect(glyph.xMax + 30f, glyph.y + 16f, right.width - 190f, 40f), shown.LabelCap + (pinned ? "  (pinned)" : ""));
                Text.Font = GameFont.Small; GUI.color = InkSoft;
                ArtInk.Label(new Rect(glyph.xMax + 30f, glyph.y + 58f, right.width - 190f, 80f),
                    shown.role.ToString().CapitalizeFirst() + (shown.Unlocked ? "" : " - not researched yet" + (shown.research != null ? ": " + shown.research.LabelCap : ""))
                    + "\nInstability " + shown.instability.ToString("+0.00;-0.00") + (shown.actWeight > 0f ? ", weight of the deed " + shown.actWeight.ToString("0") : ""));

                // what it does: her words or the full ones, then the rest
                // the full description; under it, her word on it (on her plaque, if she is on); then where the book uses it
                string main = RitualText.Desc(shown);
                string her = Arlia.On && simpleHas(shown) ? simple_(shown) : null;
                var more = new System.Text.StringBuilder();
                if (!shown.implemented) more.AppendLine("Its effect is not made yet.").AppendLine();
                var used = RitualPresetDef.All.Where(pr => (!pr.hidden || RitualDiscoveries.Known(pr)) && pr.rings.Any(rg => rg.runes.Contains(shown))).Select(pr => pr.LabelCap.ToString()).ToList();
                more.AppendLine("IN THE BOOK");
                more.Append(used.Count == 0 ? "No ritual of the book uses it yet - it is yours to place." : string.Join(", ", used) + ".");
                string rest = more.ToString();
                Rect tArea = new Rect(right.x, glyph.yMax + 36f, right.width, right.yMax - glyph.yMax - 40f);
                float w = tArea.width - 20f;
                Text.Font = GameFont.Medium;
                float h1 = Text.CalcHeight(main, w);
                Text.Font = GameFont.Small;
                float hn = her != null ? Arlia.NoteHeight(her, Mathf.Min(w, 520f)) + 16f : 0f;
                float h2 = Text.CalcHeight(rest, w);
                Rect tView = new Rect(0f, 0f, w, h1 + 20f + hn + h2);
                RitualScroll.Begin(tArea, ref textScroll, tView);
                Text.Font = GameFont.Medium; GUI.color = Ink;
                ArtInk.Label(new Rect(0f, 0f, w, h1), main);
                if (her != null) Arlia.Note(new Rect(0f, h1 + 16f, Mathf.Min(w, 520f), hn - 16f), her);
                Text.Font = GameFont.Small; GUI.color = InkSoft;
                ArtInk.Label(new Rect(0f, h1 + 20f + hn, w, h2), rest);
                Widgets.EndScrollView();
            }
            // a passing remark: up by the rune's name, clear of her note on the page
            Arlia.DrawPlaqueIn(this, new Rect(right.x + right.width * 0.42f, right.y + 36f, right.width * 0.58f, 170f), 1);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            FlowStyle.End();
        }
    }
}
