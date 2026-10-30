using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    // =====================================================================================
    // ARLIA. A teacher's voice in the margins of ritualistics - black and crimson, in italics, signed. She speaks in the
    // ritual windows (in a free corner) and, for what happens out on the land, in a free corner of the screen. Asked
    // the same again and again, she grows sharper (the lines of a situation are in order of her patience). In her
    // mode the gaps in a drawing are circled in red ink. The player may send her away - and call her back.
    // The lines are the author's own (and, where marked in the docs, translated from her Russian).
    // =====================================================================================
    public static class Arlia
    {
        public static bool On => AvirMod.Settings == null || AvirMod.Settings.arliaOn;

        /// <summary>Her name as she signs it.</summary>
        public const string Name = "Arlie";

        /// <summary>Her plaque in a ritual window - only in the topmost one, never twice.</summary>
        public static void DrawPlaqueIn(Window w, Rect area, int corner)
        {
            Window top = Find.WindowStack.Windows.LastOrDefault(x => x is Dialog_RitualBook || x is Dialog_RitualEditor || x is Dialog_RitualCodex);
            if (top == w) DrawPlaque(area, corner);
        }

        private static readonly Dictionary<string, string[]> lines = new Dictionary<string, string[]>
        {
            { "off", new[] { "They grow up so fast!", "Oh, you'll be back.", "Yes-yes, I prefer silence anyway." } },
            { "on", new[] { "Back, already?", "So soon?", "Did you miss me?" } },
            // the editor
            { "nothing", new[] { "A blank circle. Very bold. Very useless.", "Write something. Anything. I won't do it for you." } },
            { "hole", new[] { "You left a hole. Rituals hate holes almost as much as I do.", "Something is missing here. Your attention, mostly." } },
            { "full", new[] { "It is full. The circles aren't as ambitious as I am.", "No room left. Erase something - preferably the worst idea." } },
            { "noverb", new[] { "Lovely pattern. It does nothing.", "All form and no action. Remind you of anyone?", "Nonsense built on nonsense.", "Must I humiliate this?", "The greatest sacrifice of my time." } },
            { "focusonly", new[] { "So much energy - and for what?" } },
            { "storm", new[] { "Don't ring the bells.", "Not afraid of thunder, are you?" } },
            { "stormaway", new[] { "And there it goes - up and away. Close it, or the sky keeps your work." } },
            { "eye", new[] { "Now that is ambition. Your ritualist walks out wearing a storm. Or does not walk out at all." } },
            { "dome", new[] { "A dome. Defensive. Unimaginative. Effective." } },
            { "trap", new[] { "A cage. Charming. Now wait for something stupid to step in." } },
            { "trapopen", new[] { "A cage with the door left open. Do you expect the prey to be polite?", "Yes, of course, the prey will get inside simply out of politeness." } },
            { "selfdet", new[] { "Oh, is the point here to cause a destruction? Very good. Very lovely." } },
            { "dust", new[] { "The Becoming of Dust! Try it!" } },
            { "vortex", new[] { "Nothing to fill and something to spin. You will get a vortex - and then you will get a mop." } },
            { "noseal", new[] { "Turn it into what, exactly? The seal, dork. The seal." } },
            { "sealsame", new[] { "Lead into lead. Groundbreaking." } },
            { "sealtwo", new[] { "One seal is read. The second is decoration and an expensive decoration at that." } },
            { "sealalone", new[] { "A seal with no transmutation is a signature on an empty page." } },
            { "emptycomp", new[] { "Are you trying to create air now?", "The point is to fill the hole, you know." } },
            { "unstable", new[] { "Good chances... Now, try it.", "You ever heard a whip? It slaps." } },
            { "bigger", new[] { "This needs a bigger circle. Size does matter - here, at least." } },
            { "works", new[] { "This will work. Most likely." } },
            // the mage's path, and what she makes of each choice
            { "pathopen", new[] { "The mage's path. One place is empty - the element that will be theirs. Choose well; they will not let you forget it." } },
            { "pathFire", new[] { "Fire. Of course. They were always the one standing too close to the hearth." } },
            { "pathWater", new[] { "Water. Patient, cold, quietly certain they are right. Fitting." } },
            { "pathEarth", new[] { "Earth. Slow to start, impossible to stop. Like their arguments." } },
            { "pathAir", new[] { "Air. Light, quick, never where you left them. I approve. Mostly." } },
            { "pathLight", new[] { "Light. They will see everything - and tell everyone about it." } },
            { "pathTime", new[] { "Time. They will be late to everything and blame it on the craft." } },
            { "pathLife", new[] { "Life. Someone in this colony has to care. Apparently it is them." } },
            { "pathDarkness", new[] { "Darkness. Every meaning at once. I would sleep with one eye open - near them." } },
            { "pathSpace", new[] { "Space. They will move the furniture from across the room. Just to prove they can." } },
            { "hidden", new[] { "Oh, you too see how this could work, huh?" } },
            { "known", new[] { "Oh, look, you've discovered a circle!", "Impressive skills. Care to open a book now?" } },
            { "saved", new[] { "Yes, yes, keep it.", "Greedy, aren't you?" } },
            // out on the land
            { "cantdraw", new[] { "Sorry, do you actually have any idea how \"drawing\" works?", "Do you require a ritual or just a mush of colors?" } },
            { "drawnpoor", new[] { "Oh, poor job. The usual.", "Very lovely. Is that an owl?", "Your lackey lacks brains." } },
            { "drawnfine", new[] { "Oh, I see you have standards.", "Precise painting. Now, let's see if it's of worth." } },
            { "nodraw", new[] { "Are you trying to play pretend?", "Must I even explain it?", "DRAW THE RITUAL." } },
            { "trapperform", new[] { "You don't know what you're doing, do you?", "You don't want to do that. Well, maybe you do." } },
            { "downed", new[] { "Pathetic, really.", "No composure, no talent, no ambitions.", "Impressive display of weakness." } },
            { "backlash", new[] { "A lash across the back. Or not quite the back. You get the idea.", "I was impatient too, once. I had brains, though." } },
            { "washed", new[] { "Water doesn't do an ink justice.", "Your painting is ruined; try to engrave it, if you love it so much." } },
            { "spinning", new[] { "You spin it round, round, see it, round round.", "This mess is going wide now." } },
            { "success", new[] { "Success. Good.", "Finally, results!" } },
            { "failure", new[] { "Are you surprised?", "Ah, good old practice." } },
            { "caught", new[] { "Prey in the garden; the prey in the garden.", "Fish in the net." } },
            { "flow", new[] { "Good time for a ritual, don't you think?", "Watch out: the terror in the sky." } },
            { "bells", new[] { "Don't you dare to build bells!" } },
            { "rune", new[] { "Ah, that one. Listen closely.", "Good choice. Or at least an interesting one." } },
            { "unknownrune", new[] { "Curious? You should be." } },
            { "fresh", new[] { "A clean sheet. Don't waste it.", "Fresh chalk, fresh mistakes. Try not to make too many." } },
            { "pin", new[] { "Oh, do you actually have an idea?", "Yes, initiative is good for studies." } },
            { "lessons", new[] { "In need of a master, huh?", "Pay attention now, I'd rather not repeat myself.", "It's good to know your limits." } },
            { "emptybook", new[] { "An empty book. Fitting." } },
            // secrets
            { "secret", new[] { "Well, well. You found one of mine.", "A secret? Oh, you thought that was a \"secret\"? Well. I suppose I could have mentioned it earlier." } },
        };

        private static readonly Dictionary<string, int> count = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> lastAt = new Dictionary<string, float>();
        private static string shownKey, shown;
        private static float shownAt = -100f;
        private const float Show = 7f, Patience = 90f;
        private static float showFor = Show;

        /// <summary>She says her piece about this - sharper each time she is made to repeat it. A passing remark can be
        /// given a shorter time on the page.</summary>
        public static void Say(string key, float seconds = 0f)
        {
            if (!On || key == null || !lines.TryGetValue(key, out string[] l) || l.Length == 0) return;
            float now = Time.realtimeSinceStartup;
            if (key == shownKey && now - shownAt < 3f) return;   // she is still saying it
            int n = lastAt.TryGetValue(key, out float t) && now - t < Patience && count.TryGetValue(key, out int c) ? c + 1 : 0;
            count[key] = n;
            lastAt[key] = now;
            shownKey = key;
            shown = l[Mathf.Min(n, l.Length - 1)];
            shownAt = now;
            showFor = seconds > 0f ? seconds : Show;
        }

        /// <summary>Sent away or called back: she has the last word either way.</summary>
        public static void Toggle()
        {
            if (AvirMod.Settings == null) return;
            if (AvirMod.Settings.arliaOn) { Say("off"); AvirMod.Settings.arliaOn = false; }
            else { AvirMod.Settings.arliaOn = true; Say("on"); }
            AvirMod.Settings.Write();
        }

        private static float Alpha
        {
            get
            {
                float age = Time.realtimeSinceStartup - shownAt;
                if (shown == null || age > showFor) return 0f;
                return age > showFor - 0.6f ? (showFor - age) / 0.6f : 1f;
            }
        }

        public static bool Speaking => Alpha > 0f;

        private static Texture2D plaqueTex;
        private static bool plaqueLooked;
        private static readonly Color Back = new Color(0.07f, 0.02f, 0.03f, 0.93f);
        private static readonly Color Crimson = new Color(0.62f, 0.06f, 0.09f);
        private static readonly Color Text_ = new Color(0.93f, 0.86f, 0.84f);

        // her plaque art, in the picture's pixels: the feather and the torn, burnt end keep their shape; the page between widens
        private static readonly float[] ArlieCuts = { 0f, 720f, 1580f, 1983f };
        private static readonly bool[] ArlieStretch = { false, true, false };
        private const float ArlieTextL = 450f, ArlieTextR = 150f, ArlieTextT = 172f, ArlieTextB = 505f;

        /// <summary>Her plaque, in the corner given (0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right of the area).</summary>
        public static void DrawPlaque(Rect area, int corner)
        {
            float a = Alpha;
            if (a <= 0f) return;
            float w = Mathf.Min(500f, area.width - 16f);
            float h = NoteHeight(shown, w);
            float x = corner % 2 == 0 ? area.x + 8f : area.xMax - w - 8f;
            float y = corner < 2 ? area.y + 8f : area.yMax - h - 8f;
            Note(new Rect(x, y, w, h), shown, a);
        }

        private static Texture2D Plaque
        {
            get
            {
                if (!plaqueLooked) { plaqueLooked = true; plaqueTex = ContentFinder<Texture2D>.Get("UI/Ritualistics/ArliePlaque", false); }
                return plaqueTex;
            }
        }

        private const float NoteTall = 150f;

        /// <summary>Large letters while the plaque stays modest; smaller ones for many words.</summary>
        private static GameFont NoteFont(string text, float w)
        {
            GameFont keep = Text.Font;
            Text.Font = GameFont.Medium;
            float h = RawHeight(text, w);
            Text.Font = keep;
            return h <= NoteTall ? GameFont.Medium : GameFont.Small;
        }

        private static float RawHeight(string text, float w)
        {
            string body = "<i>" + text + "</i>";
            if (Plaque == null) return Text.CalcHeight(body, w - 24f) + 40f;
            float s = 0.17f, h = 0f;
            for (int i = 0; i < 3; i++)
            {
                float tw = w - (ArlieTextL + ArlieTextR) * s;
                h = Mathf.Max(110f, (Text.CalcHeight(body, tw) + 30f) * Plaque.height / (ArlieTextB - ArlieTextT));
                s = h / Plaque.height;
            }
            return h;
        }

        /// <summary>How tall her plaque stands for these words at this width.</summary>
        public static float NoteHeight(string text, float w)
        {
            GameFont keep = Text.Font;
            Text.Font = NoteFont(text, w);
            float h = RawHeight(text, w);
            Text.Font = keep;
            return h;
        }

        /// <summary>Her plaque with these words, in this rect (as tall as NoteHeight says).</summary>
        public static void Note(Rect pr, string text, float a = 1f)
        {
            GameFont keepF = Text.Font; TextAnchor keepA = Text.Anchor; Color keepC = GUI.color;
            string body = "<i>" + text + "</i>";
            if (Plaque != null)
            {
                float s = pr.height / Plaque.height;
                GUI.color = new Color(1f, 1f, 1f, a);
                PlaqueArt.Draw(pr, Plaque, ArlieCuts, ArlieStretch);
                Rect page = new Rect(pr.x + ArlieTextL * s, pr.y + ArlieTextT * s, pr.width - (ArlieTextL + ArlieTextR) * s, (ArlieTextB - ArlieTextT) * s);
                Text.Font = NoteFont(text, pr.width); Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = new Color(Text_.r, Text_.g, Text_.b, a);
                Widgets.Label(new Rect(page.x, page.y, page.width, page.height - 26f), body);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperRight;
                GUI.color = new Color(1f, 0.66f, 0.7f, a);
                Widgets.Label(new Rect(page.x, page.yMax - 26f, page.width - 380f * s, 26f), "- " + Name);
            }
            else
            {
                GUI.color = new Color(Back.r, Back.g, Back.b, Back.a * a);
                GUI.DrawTexture(pr, BaseContent.WhiteTex);
                GUI.color = new Color(Crimson.r, Crimson.g, Crimson.b, a);
                Widgets.DrawBox(pr, 2);
                GUI.DrawTexture(new Rect(pr.x, pr.y, 4f, pr.height), BaseContent.WhiteTex);
                Text.Font = NoteFont(text, pr.width); Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = new Color(Text_.r, Text_.g, Text_.b, a);
                Widgets.Label(new Rect(pr.x + 14f, pr.y + 8f, pr.width - 24f, pr.height - 34f), body);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.LowerRight;
                GUI.color = new Color(Crimson.r * 1.6f, Crimson.g * 1.6f, Crimson.b * 1.6f, a);
                Widgets.Label(new Rect(pr.x, pr.y, pr.width - 10f, pr.height - 4f), "- " + Name);
            }
            Text.Font = keepF; Text.Anchor = keepA; GUI.color = keepC;
        }

        // ---------------------------------------------------------------- the red ink circle

        private static Texture2D ink;

        /// <summary>A hand-drawn red ink circle, made once in memory: uneven, thicker where the pen pressed, its
        /// end running a little past its start.</summary>
        public static Texture2D InkCircle
        {
            get
            {
                if (ink != null) return ink;
                const int n = 128;
                ink = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int i = 0; i < px.Length; i++) px[i] = new Color(0.62f, 0.05f, 0.07f, 0f);
                const float start = 0.6f, sweep = Mathf.PI * 2f + 0.55f;
                for (int s = 0; s <= 900; s++)
                {
                    float f = s / 900f, th = start + sweep * f;
                    float rad = n * (0.40f + 0.022f * Mathf.Sin(3f * th + 1.1f) + 0.012f * Mathf.Sin(7f * th) + 0.03f * f);
                    float thick = n * (0.022f + 0.012f * Mathf.Sin(2f * th + 0.4f)) * Mathf.Clamp01(Mathf.Min(f, 1f - f) * 12f + 0.25f);
                    float cx = n / 2f + Mathf.Cos(th) * rad, cy = n / 2f + Mathf.Sin(th) * rad;
                    int r0 = Mathf.CeilToInt(thick) + 1;
                    for (int dx = -r0; dx <= r0; dx++)
                        for (int dy = -r0; dy <= r0; dy++)
                        {
                            int x = Mathf.RoundToInt(cx) + dx, y = Mathf.RoundToInt(cy) + dy;
                            if (x < 0 || y < 0 || x >= n || y >= n) continue;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float aa = Mathf.Clamp01(thick - d + 0.5f) * 0.9f;
                            int idx = y * n + x;
                            if (aa > px[idx].a) px[idx].a = aa;
                        }
                }
                ink.SetPixels(px);
                ink.Apply();
                return ink;
            }
        }

        public static void Circle(Rect r)
        {
            if (!On) return;
            Color keep = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, InkCircle, ScaleMode.ScaleToFit);
            GUI.color = keep;
        }

        // ---------------------------------------------------------------- what she makes of a drawing

        /// <summary>The situation a drawing is in, as she would point it out (null: nothing to say).</summary>
        public static string Judge(RitualSchema s, RitualReading r, CompRitualCircle circle)
        {
            if (!s.AllRunes.Any()) return Fresh ? "fresh" : "nothing";
            if (s.preset?.defName == Avir.Magic.MagePath.Preset && r.elements.Count == 0) return "pathopen";
            if (s.rings.Any(x => x.runes.Count < x.Slots)) return "hole";
            if (r.SelfDetonation) return "selfdet";
            float calm = r.Risk(1f, 1f, 0f);
            if (calm >= 1.2f) return "dust";
            if (circle != null && !s.Fits(circle.Size)) return "bigger";
            bool transmutes = r.Has(RuneKind.Transmute);
            int seals = s.components.Count(c => c.kind == ComponentKind.Seal);
            if (!transmutes && s.components.Any(c => c.kind == ComponentKind.Seal || c.kind == ComponentKind.Material)) return "sealalone";
            if (transmutes && r.Seal == null && !r.ToCurrent) return "noseal";
            if (seals > 1) return "sealtwo";
            RitualComponent seal = s.components.FirstOrDefault(c => c.kind == ComponentKind.Seal);
            if (seal?.thing != null && s.components.Any(c => c.kind == ComponentKind.Material && c.thing == seal.thing)) return "sealsame";
            if (s.components.Any(c => c.kind == ComponentKind.None)) return "emptycomp";
            if (r.pattern != null && r.pattern.defName == "Avir_Ritual_TrapIncomplete") return "trapopen";
            if (r.trap) return "trap";
            if (r.Eye) return "eye";
            if (r.Storm && r.closing == 0) return "stormaway";
            if (r.Storm) return "storm";
            if (r.Dome) return "dome";
            if (r.RawFlow) return "focusonly";
            if (r.verbs.Count == 0) return "noverb";
            if (r.VortexRisk) return "vortex";
            if (calm >= 0.85f) return "unstable";
            if (CompRitualCircle.IsPath(r)) return "path" + r.elements[0].kind;
            if (r.pattern != null) return r.pattern.hidden && !RitualDiscoveries.Known(r.pattern) ? "hidden" : "known";
            return "works";
        }

        /// <summary>Her word on each ritual of the book, shown under its details.</summary>
        // her words, written by the author; a ritual without them shows only what she has to say about its trouble
        private static readonly Dictionary<string, string> about = new Dictionary<string, string>
        {
            { "Avir_Ritual_Charge", "Feeding your trinkets. Two runes - even you can't spill it. Probably." },
            { "Avir_Ritual_ChargeAll", "The same feeding, only wider: everything within reach gets a sip. Generous of you." },
            { "Avir_Ritual_Cleanse", "Back to how it was made, every mistake undone. If only people worked that way." },
            { "Avir_Ritual_Transfer", "Everything one vessel knows, poured into another. The first one forgets. Such is generosity." },
            { "Avir_Ritual_FreeTransfer", "Only the spare - what nobody uses anyway. Polite pouring. Boring, but polite." },
            { "Avir_Ritual_Hold", "Foes stand still while it streams. They object, of course. Rudely." },
            { "Avir_Ritual_Trap", "An old experiment of mine. Amusing - but not for a ritualist." },
            { "Avir_Ritual_EyeOfStorm", "A storm locked into one ritualist. Either they walk out wearing it, or they don't walk out. Ambitious. I approve." },
            { "Avir_Ritual_Crystallize", "Catches a pylon's spare charge as crystals. Picking up what others drop - thrifty." },
            { "Avir_Ritual_CrystallizeSmall", "The small one. Draw it right beside the pylon. Closer. No, closer." },
            { "Avir_Ritual_SilverToGold", "The first transmutation everyone learns. Everyone. Even you will manage." },
            { "Avir_Ritual_SteelToPlasteel", "Less glamorous than gold, more useful. Like most good things." },
            { "Avir_Ritual_GreatTransmutation", "Any metal into gold - and more of it than you laid down, if you draw it well. If." },
            { "Avir_Ritual_StrikeAfar", "It gathers, then strikes anywhere you point. Point carefully." },
            { "Avir_Ritual_Shove", "A wave that throws everyone back. For when talking fails. It usually does." },
            { "Avir_Ritual_Sweep", "Clutter to storage, snow and filth away, for a whole day. Yes, a cleaning ritual. Even greatness needs a tidy floor." },
            { "Avir_Ritual_Harvest", "Plants ripen, wounds close, the soil grows richer. Gardening - but with ambition." },
            { "Avir_Ritual_ChargingStation", "Threads to your batteries, filled for a day. Very practical. Almost dull." },
            { "Avir_Ritual_Current", "Energy turned into plain current. Someone has to keep the lights on." },
            { "Avir_Ritual_Reforge", "Remakes an artifact from the heart out. Large circle only. Why? Because it's better this way." },
            { "Avir_Ritual_Infusion", "Days of use poured into an artifact in an hour. Slow, safe, and quite boring." },
            { "Avir_Ritual_BarrierDome", "A dome against everything outside. Hide well - it won't hold forever." },
            { "Avir_Ritual_TrapIncomplete", "A cage with the door left open. Do finish it." },
            { "Avir_Ritual_TrapFeeding", "A cage that feeds its guest. Hospitality, in a manner of speaking." },
            { "Avir_Ritual_TrapMilking", "It drains the prisoner into your batteries. Efficient. Somewhat rude." },
            { "Avir_Ritual_TrapPulse", "The prisoner becomes a turret. Waste not." },
            { "Avir_Ritual_KindleCore", "Spire business. Learn how they are raised first - then come back and wake its heart." },
            { "Avir_Ritual_SelfDetonation", "It goes off in the hands of whoever holds it. An easy mistake. An unforgettable one." },
            { "Avir_Ritual_MagePath", "The mage's path. One place is left empty - write the element that will be theirs. Choose well: they will never let you forget it." },
            { "Avir_Ritual_EndlessVortex", "Nothing to fill and nothing to close it. It spins, and grows, and does not stop. Bring a mop." },
        };

        public static string About(RitualPresetDef p) => p != null && about.TryGetValue(p.defName, out string s) ? s : null;

        private static string lastJudged;

        /// <summary>A new sheet nothing has been written on yet.</summary>
        public static bool Fresh;

        /// <summary>One of her lines for this situation, the same one for the same ritual.</summary>
        public static string LineFor(string key, string seed)
        {
            if (key == null || !lines.TryGetValue(key, out string[] l) || l.Length == 0) return null;
            return l[Mathf.Abs((seed ?? "").GetHashCode()) % l.Length];
        }

        /// <summary>Called by the editor each repaint: she speaks when the drawing's situation changes.</summary>
        public static void Watch(RitualSchema s, RitualReading r, CompRitualCircle circle)
        {
            if (!On || Event.current.type != EventType.Repaint) return;
            string key = Judge(s, r, circle);
            if (key == lastJudged) return;
            lastJudged = key;
            Say(key);
        }

        public static void Forget() => lastJudged = null;

        /// <summary>What the end of a ritual means to her.</summary>
        public static void Ended(string why)
        {
            if (why == null) return;
            if (why.StartsWith("complete") || why.StartsWith("released") || why.StartsWith("the artifact is reforged") || why.StartsWith("the storm is sealed")) Say("success");
            else if (why.Contains("fizzled") || why.Contains("went off in") || why.StartsWith("cannot")) Say("failure");
        }
    }

    /// <summary>Her plaque out on the land: a free corner of the screen (the upper left), when no ritual window speaks for her.</summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class Patch_ArliaOnScreen
    {
        public static void Postfix()
        {
            if (!Arlia.Speaking || Current.ProgramState != ProgramState.Playing) return;
            if (Find.WindowStack.Windows.Any(w => w is Dialog_RitualEditor || w is Dialog_RitualBook || w is Dialog_RitualCodex)) return;
            Arlia.DrawPlaque(new Rect(0f, 40f, Verse.UI.screenWidth * 0.5f, 300f), 0);
        }
    }

    /// <summary>The game's own refusals she has something to add to.</summary>
    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) })]
    public static class Patch_ArliaOnRefusals
    {
        public static void Postfix(string text)
        {
            if (text == null || !Arlia.On) return;
            if (text.StartsWith("A ritual circle cannot") || text.StartsWith("Another ritual circle lies") || text.StartsWith("The circle must lie whole")) Arlia.Say("cantdraw");
            else if (text.Contains("nothing drawn yet")) Arlia.Say("nodraw");
            else if (text.Contains("a Trap is not performed")) Arlia.Say("trapperform");
        }
    }

    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) })]
    public static class Patch_ArliaOnRefusalsAt
    {
        public static void Postfix(string text) => Patch_ArliaOnRefusals.Postfix(text);
    }
}
