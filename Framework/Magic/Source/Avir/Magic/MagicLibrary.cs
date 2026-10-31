using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avir.Artifactorics;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Magic
{
    // =====================================================================================
    // THE PATH, THE COLONY'S BOOK, THE SPELLS OF STRANGERS.
    // A mage's path is the element a ritual gives them (the path ritual: infusion, the element, closing). It decides the
    // first spells they get - how many, by how well the ritual went. Strangers who are mages walk a path and carry spells
    // of their own (a known one, or one of their own making); once such a mage is the colony's, their spells go into the
    // colony's book. Any spell can be kept: in the colony's book, in files beside the mod (every game sees them), or both.
    // =====================================================================================

    /// <summary>The colony's book of spells: every mage of the colony may take from it.</summary>
    public class GameComponent_SpellLibrary : GameComponent
    {
        public List<RitualSchema> spells = new List<RitualSchema>();

        public GameComponent_SpellLibrary(Game game) { }

        public static GameComponent_SpellLibrary Get => Current.Game?.GetComponent<GameComponent_SpellLibrary>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref spells, "spells", LookMode.Deep);
            if (spells == null) spells = new List<RitualSchema>();
        }

        public static bool Has(RitualSchema s) => Get != null && Get.spells.Any(x => x.name == s.name);

        public static bool Add(RitualSchema s)
        {
            if (Get == null || s == null || Has(s)) return false;
            Get.spells.Add(s.Copy());
            return true;
        }
    }

    /// <summary>Spells kept in files beside the mod, in "Spells" next to the rituals: every game sees them.</summary>
    public static class SpellFiles
    {
        public const string Extension = ".spell";

        public static string Folder
        {
            get
            {
                string path = Path.Combine(RitualFiles.Folder, "Spells");
                try { if (!Directory.Exists(path)) Directory.CreateDirectory(path); } catch { }
                return path;
            }
        }

        private static string PathFor(string name)
        {
            string s = name ?? "Untitled spell";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return Path.Combine(Folder, (s.NullOrEmpty() ? "Untitled spell" : s) + Extension);
        }

        public static bool Save(RitualSchema schema)
        {
            RitualSchema s = schema.Copy();
            try { SafeSaver.Save(PathFor(s.name), "AvirSpell", () => Scribe_Deep.Look(ref s, "schema")); return true; }
            catch (Exception e) { Log.Error("[Avir] could not save spell \"" + schema.name + "\": " + e); return false; }
        }

        public static List<RitualSchema> LoadAll()
        {
            var list = new List<RitualSchema>();
            try
            {
                foreach (string f in Directory.GetFiles(Folder, "*" + Extension).OrderBy(x => x))
                {
                    RitualSchema s = RitualFiles.Load(f);
                    if (s != null) list.Add(s);
                }
            }
            catch (Exception e) { Log.Warning("[Avir] could not list spell files: " + e.Message); }
            return list;
        }

        public static void Delete(string name)
        {
            try { string p = PathFor(name); if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }

    public static class SpellKeep
    {
        /// <summary>Keeps a spell where the mod settings say: the colony's book, the files, or both.</summary>
        public static void Keep(RitualSchema s)
        {
            int where = AvirMod.Settings?.spellKeep ?? 0;
            var said = new List<string>();
            if (where != 1) { if (GameComponent_SpellLibrary.Add(s)) said.Add("into the colony's book"); else said.Add("already in the colony's book"); }
            if (where != 0 && SpellFiles.Save(s)) said.Add("into the files beside the mod");
            Messages.Message(s.name + " kept: " + string.Join(", ", said) + ".", MessageTypeDefOf.PositiveEvent, false);
        }
    }

    public static class MagePath
    {
        public const string Preset = "Avir_Ritual_MagePath";
        public const float Need = 300f;

        /// <summary>The ready spells of a path (fire and water, for now).</summary>
        public static List<RitualSchema> Examples(RuneDef el) => el == null ? new List<RitualSchema>() : el.kind == RuneKind.Fire ? SpellExamples.Fire() : el.kind == RuneKind.Water ? SpellExamples.Water() : el.kind == RuneKind.Earth ? SpellExamples.Earth() : el.kind == RuneKind.Air ? SpellExamples.Air() : new List<RitualSchema>();

        /// <summary>Elements whose spells are made: the paths a stranger may walk for now.</summary>
        public static List<RuneDef> Made => DefDatabase<RuneDef>.AllDefsListForReading.Where(r => r.role == RuneRole.Element && r.magic && Examples(r).Count > 0).ToList();

        public static string CannotTake(Pawn who)
        {
            if (!Mages.IsMage(who)) return "not a wizard";
            Hediff_ArcaneCore c = Mages.Core(who);
            if (c == null) return "no arcane core";
            if (c.path != null) return "already walks the path of " + c.path.label;
            return null;
        }

        /// <summary>The path given: the element, and of its spells as many as the ritual earned (all at its best).</summary>
        public static int Grant(Pawn who, RuneDef el, float share)
        {
            Hediff_ArcaneCore c = Mages.Core(who);
            if (c == null) return 0;
            c.path = el;
            c.Note("Took the path of " + el.label + ".");
            var ex = Examples(el).Where(x => !c.Book.spells.Any(y => y.name == x.name)).InRandomOrder().ToList();
            int n = ex.Count == 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(share * ex.Count), 1, ex.Count);
            foreach (RitualSchema s in ex.Take(n)) c.Book.spells.Add(s);
            return n;
        }

        /// <summary>A mage among strangers: a path, and a spell - a known one, or one of their own making.</summary>
        public static void Stranger(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null || c.path != null) return;
            List<RuneDef> made = Made;
            if (made.Count == 0) return;
            c.path = made.RandomElement();
            if (Rand.Chance(0.5f)) { RitualSchema known = Examples(c.path).RandomElementWithFallback(); if (known != null) c.Book.spells.Add(known); }
            else c.Book.spells.Add(SpellGen.Make(c.path, p));
        }
    }

    /// <summary>A spell a stranger made for themselves: an element, a verb or two, perhaps a form and a modifier.</summary>
    public static class SpellGen
    {
        private static RuneDef R(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);

        public static RitualSchema Make(RuneDef el, Pawn owner)
        {
            string[] verbs = { "Release", "Hold", "Drain", "Lift", "Growth", "Purge" };
            string[] forms = { "Beam", "Aura", "Shape", "Step" };
            string[] mods = { "Amplify", "Breadth", "Reach" };
            var c = new SpellCircle { element = el };
            var first = new RitualRing();
            RuneDef v = R(verbs.RandomElement());
            if (v != null) first.runes.Add(v);
            if (Rand.Chance(0.3f)) { RuneDef v2 = R(verbs.RandomElement()); if (v2 != null) first.runes.Add(v2); }
            first.slots = first.runes.Count;
            c.sections.Add(first);
            var second = new RitualRing();
            RuneDef form = Rand.Chance(0.6f) ? R(forms.RandomElement()) : null;
            if (form != null) second.runes.Add(form);
            if (Rand.Chance(0.5f)) { RuneDef m = R(mods.RandomElement()); if (m != null) second.runes.Add(m); }
            if (second.runes.Count > 0) { second.slots = second.runes.Count; c.sections.Add(second); }
            string what = (form != null ? form.label + " of " : "") + (v != null ? v.label : el.label);
            var s = new RitualSchema { name = (owner != null ? owner.LabelShort + "'s " : "") + what, size = RitualCircleSize.Small, author = owner?.LabelShort };
            s.weave = SpellCircle.Bound(c);
            return s;
        }
    }

    /// <summary>The colony's book and the files: take a spell into this mage's book, or forget it.</summary>
    public class Dialog_SpellLibrary : Window
    {
        private readonly Pawn mage;
        private Vector2 scroll;
        private int tab;   // 0 the colony's book, 1 the files
        private List<RitualSchema> files;

        public Dialog_SpellLibrary(Pawn mage)
        {
            this.mage = mage;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(980f, Verse.UI.screenWidth - 60f), Mathf.Min(760f, Verse.UI.screenHeight - 60f));

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(null);
            Rect inner = ParchmentFrame.Draw(box).ContractedBy(14f);
            GUI.color = FlowStyle.Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(inner.x, inner.y, inner.width, 34f), "Kept spells");
            Text.Font = GameFont.Small;
            if (RitualUI.Button(new Rect(inner.x, inner.y + 40f, 220f, 32f), "The colony's book", true, tab == 0)) { tab = 0; scroll = Vector2.zero; }
            if (RitualUI.Button(new Rect(inner.x + 228f, inner.y + 40f, 220f, 32f), "Files (every game)", true, tab == 1)) { tab = 1; scroll = Vector2.zero; files = SpellFiles.LoadAll(); }
            List<RitualSchema> list = tab == 0 ? GameComponent_SpellLibrary.Get?.spells ?? new List<RitualSchema>() : files ?? (files = SpellFiles.LoadAll());
            Rect area = new Rect(inner.x, inner.y + 84f, inner.width, inner.height - 84f);
            const float h = 96f;
            Rect view = new Rect(0f, 0f, area.width - 16f, Mathf.Max(1, list.Count) * (h + 6f));
            RitualScroll.Begin(area, ref scroll, view);
            if (list.Count == 0)
            {
                GUI.color = FlowStyle.InkSoft;
                ArtInk.Label(new Rect(0f, 0f, view.width, 60f), tab == 0 ? "Nothing here yet. A wizard who joins the colony brings their spells here; and any spell can be kept here from a wizard's book."
                                                                       : "No spell files yet. Keep a spell with the files chosen in the mod settings (Magic).");
            }
            float y = 0f;
            Hediff_ArcaneCore core = mage != null ? Mages.Core(mage) : null;
            foreach (RitualSchema s in list.ToList())
            {
                Rect row = new Rect(0f, y, view.width, h);
                if (RitualArt.HasCard) RitualArt.CardAt(row); else Widgets.DrawBoxSolid(row, new Color(0.93f, 0.85f, 0.7f, 0.85f));
                Rect body = row.ContractedBy(10f);
                SpellIconArt.Draw(new Rect(body.x, body.y, 52f, 52f), s);
                GUI.color = FlowStyle.Ink;
                ArtInk.Label(new Rect(body.x + 62f, body.y - 2f, body.width - 300f, 24f), s.name);
                GUI.color = FlowStyle.InkSoft;
                ArtInk.Label(new Rect(body.x + 62f, body.y + 22f, body.width - 300f, 50f), SpellText.Explain(s).main.Truncate((body.width - 300f) * 2f - 20f));
                bool has = core != null && core.Book.spells.Any(x => x.name == s.name);
                if (core != null && RitualUI.Button(new Rect(body.xMax - 270f, body.y + 20f, 150f, 34f), has ? "In the book" : "Into the book", !has))
                { core.Book.spells.Add(s.Copy()); SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                if (RitualUI.Button(new Rect(body.xMax - 110f, body.y + 20f, 110f, 34f), "Forget"))
                {
                    if (tab == 0) GameComponent_SpellLibrary.Get?.spells.Remove(s);
                    else { SpellFiles.Delete(s.name); files = SpellFiles.LoadAll(); }
                }
                y += h + 6f;
            }
            Widgets.EndScrollView();
            GUI.color = Color.white;
            FlowStyle.End();
        }
    }
}
