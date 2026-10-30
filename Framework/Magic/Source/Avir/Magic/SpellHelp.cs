using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>What a spell does, for the player: one plain sentence for its core, and small plaques for what else it does.</summary>
    public class SpellEffect
    {
        public string main;
        public string details;   // Arcana, range, cast, hold - for the mage writing it, if known
        public List<KeyValuePair<string, string>> extras = new List<KeyValuePair<string, string>>();   // plaque, its tooltip
        public void Add(string chip, string tip) => extras.Add(new KeyValuePair<string, string>(chip, tip));
    }

    public static class SpellText
    {
        /// <summary>The mage whose book the editor was opened from (null - from elsewhere).</summary>
        public static Pawn Caster;

        public static string Details(RitualSchema s)
        {
            string sec(int t) => (t / 60f).ToString("0.#") + " s";
            string d = "Arcana " + Spells.Cost(s).ToString("0.#") + " · range " + Spells.Range(s).ToString("0") + " cells";
            Hediff_ArcaneCore c = Caster != null ? Mages.Core(Caster) : null;
            if (c != null)
                d += "\nCast - " + Caster.LabelShortCap + ", " + (c.Sorcerer ? "sorcerer" : "mage") + ": " + sec(Spells.CastTicks(c, s))
                   + " · hold " + Spells.HoldChance(Caster, s).ToStringPercent();
            else d += "\nCast: sorcerer " + sec(Spells.CastTicks(true, s)) + " · mage " + sec(Spells.CastTicks(false, s));
            return d;
        }

        public static SpellEffect ExplainSchema(RitualSchema s) => ExplainCore(s);

        public static SpellEffect Explain(RitualSchema s)
        {
            SpellEffect e0 = s.weave != null ? WeaveSpell.Explain(s.weave) : ExplainCore(s);
            e0.details = Details(s);
            return e0;
        }

        private static SpellEffect ExplainCore(RitualSchema s)
        {
            RitualReading r = RitualEvaluator.Read(s);
            RuneDef el = r.elements.FirstOrDefault();
            var e = new SpellEffect();
            if (el == null) { e.main = "No element yet: write one (fire, for now) - it is what the spell is made of."; return e; }
            if (!FireMagic.Is(el)) { e.main = el.LabelCap + " is not made yet - for now it only strikes a plain pulse at the point."; return e; }
            return Fire(r);
        }

        private static SpellEffect Fire(RitualReading r)
        {
            var e = new SpellEffect();
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            RuneDef form = forms.FirstOrDefault();
            float radius = FireMagic.PointRadius(r);
            string what = string.Join(", then ", verbs.Select(Verb));
            switch (form?.kind)
            {
                case RuneKind.Restore: e.main = "Cauterizes: bleeding wounds of friends at the target are seared shut, as if tended."; break;
                case RuneKind.Enchant: e.main = "Sets a friend's weapon burning (the mage's, if no friend stands there): blows add flame, shots may set foes alight."; break;
                case RuneKind.Summon: e.main = "Calls a fire wisp at the target: for a while it throws flame at foes within 7 cells."; break;
                case RuneKind.Sense: e.main = "The mage feels every warm body within " + (25 + 10 * r.reach) + " cells - through walls and darkness - for a while."; break;
                case RuneKind.Step: e.main = "The mage leaps to the target; along the way the fire " + (verbs.Count > 0 ? what : "stays held on the trail") + "."; break;
                default:
                    if (verbs.Count == 0) { e.main = r.barrier > 0 ? "A ward of flame on friends at the target." : "Fire with nothing to do: add a verb (release, hold, drain, lift, growth, purge) or a form."; break; }
                    string where = form?.kind == RuneKind.Aura ? "all around the mage (radius " + (radius + 1.5f).ToString("0.#") + ")"
                        : form?.kind == RuneKind.Beam ? "along a line to the target - a wall stops it"
                        : form?.kind == RuneKind.Shape ? "as a wall up to " + FireMagic.WallMax(r) + " cells long - its two ends chosen with two clicks"
                        : "at the chosen point (radius " + radius.ToString("0.#") + ")";
                    e.main = Cap(what) + " - " + where + ".";
                    break;
            }
            if (forms.Count > 1) e.Add("Several forms", "Each form works on its own: the spell happens once for each.");
            if (r.barrier > 0) e.Add("Ward of flame", "Friends at the target (the mage, if cast around them) get a ward: fire and burns do not pass it, and flames on the body go out.");
            if (r.amplify > 0 && verbs.Any(v => v.kind == RuneKind.Release)) e.Add("Explosion", "Amplified release bursts: a flame explosion of radius " + (1.9f + r.amplify).ToString("0.#") + ".");
            else if (r.amplify > 0) e.Add("Stronger", "Amplification: +" + (30 * r.amplify) + "% power.");
            if (verbs.Any(v => v.kind == RuneKind.Purge) && (form?.kind == RuneKind.Aura || r.anchor > 0))
                e.Add("Ground that takes no fire", "The purged ground will not take fire for some hours: flames that reach it die.");
            if (verbs.Any(v => v.kind == RuneKind.Hold)) e.Add("Warms friends", "Friends by the held fire bear the cold far better for a day.");
            if (verbs.Any(v => v.kind == RuneKind.Drain)) e.Add("Stores heat", "The heat taken is kept by the mage: their next fire spell is stronger by it.");
            if (form?.kind == RuneKind.Restore && verbs.Any(v => v.kind == RuneKind.Purge)) e.Add("Burns infection", "Infection and parasites are burnt back.");
            if (form?.kind == RuneKind.Restore && verbs.Any(v => v.kind == RuneKind.Growth)) e.Add("Better cautery", "The seared wounds hold better.");
            if (r.breadth > 0) e.Add("Wider", "Breadth: a wider area or a broader line.");
            if (r.reach > 0) e.Add("Further", "Reach: the spell can be cast further.");
            e.Add("Price: raging flame", "If the hold slips, the fire around the mage breaks out of control for a while - fire keeps starting at their feet.");
            return e;
        }

        private static string Verb(RuneDef v)
        {
            switch (v.kind)
            {
                case RuneKind.Release: return "sets fire and burns foes";
                case RuneKind.Hold: return "lays held fire that never spreads, burns foes standing in it and smoulders";
                case RuneKind.Drain: return "takes the heat out of fires";
                case RuneKind.Lift: return "raises tall flames";
                case RuneKind.Growth: return "spreads fire further and faster";
                case RuneKind.Purge: return "puts fire out and burns away infection";
                default: return v.label;
            }
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }

    /// <summary>A rune on the parchment: in plain ink like every other rune there (pale fire, light, life or time would
    /// vanish if drawn in their own colours); where the page is lit by the stream, its glowing form.</summary>
    public static class SpellGlyph
    {
        public static void Draw(Rect r, RuneDef rune)
        {
            if (rune == null) return;
            Color keep = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, FlowStyle.Pick(rune.Ink, rune.Glow), ScaleMode.ScaleToFit);
            GUI.color = keep;
        }

        /// <summary>A colour dark enough to read as ink on the parchment.</summary>
        public static Color Ink(Color c) => new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 1f);
    }

    /// <summary>The effect plaque in the spell editor: the core in one sentence, the rest as small plaques.</summary>
    public static class EffectPlaque
    {
        public static float Height(SpellEffect e, float w)
        {
            Text.Font = GameFont.Small;
            float h = 30f + Text.CalcHeight(e.main, w - 28f) + 8f;
            if (!e.details.NullOrEmpty()) { Text.Font = GameFont.Small; h += Text.CalcHeight(e.details, w - 28f) + 4f; Text.Font = GameFont.Small; }
            if (e.extras.Count > 0) h += Rows(e, w - 28f) * 32f + 6f;
            return h + 10f;
        }

        private static int Rows(SpellEffect e, float w)
        {
            Text.Font = GameFont.Small;
            int rows = 1; float x = 0f;
            foreach (var c in e.extras)
            {
                float cw = Text.CalcSize(c.Key).x + 16f;
                if (x + cw > w && x > 0f) { rows++; x = 0f; }
                x += cw + 6f;
            }
            Text.Font = GameFont.Small;
            return rows;
        }

        public static void Draw(Rect r, SpellEffect e, Color col)
        {
            Color keep = GUI.color;
            if (RitualArt.HasCard) RitualArt.CardAt(r);
            else { Widgets.DrawBoxSolid(r, new Color(0.95f, 0.88f, 0.7f, 0.9f)); }
            Rect inner = r.ContractedBy(14f);
            GUI.color = new Color(col.r * 0.7f, col.g * 0.5f, col.b * 0.4f, 1f);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y - 4f, inner.width, 24f), "THE MAIN EFFECT");
            GUI.color = new Color(0.22f, 0.14f, 0.08f, 1f);
            float th = Text.CalcHeight(e.main, inner.width);
            Widgets.Label(new Rect(inner.x, inner.y + 20f, inner.width, th), e.main);
            float x = inner.x, y = inner.y + 26f + th;
            if (!e.details.NullOrEmpty())
            {
                Text.Font = GameFont.Small; GUI.color = new Color(0.34f, 0.22f, 0.12f, 1f);
                float dh = Text.CalcHeight(e.details, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, dh), e.details);
                y += dh + 4f; Text.Font = GameFont.Small;
            }
            Text.Font = GameFont.Small;
            foreach (var c in e.extras)
            {
                float cw = Text.CalcSize(c.Key).x + 16f;
                if (x + cw > inner.xMax && x > inner.x) { x = inner.x; y += 32f; }
                Rect chip = new Rect(x, y, cw, 28f);
                bool price = c.Key.StartsWith("Price");
                Widgets.DrawBoxSolid(chip, price ? new Color(0.55f, 0.15f, 0.08f, 0.92f) : new Color(col.r * 0.32f, col.g * 0.32f, col.b * 0.32f, 0.92f));
                GUI.color = Color.white; Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(chip, c.Key);
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(chip, c.Value);
                x += cw + 6f;
            }
            Text.Font = GameFont.Small;
            GUI.color = keep;
        }
    }

    /// <summary>Ready spells that already work - to cast at once, and to read how they are written.</summary>
    public static class SpellExamples
    {
        private static RuneDef R(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);

        private static RitualSchema Make(string name, params string[][] rings)
        {
            var s = new RitualSchema { name = name, size = RitualCircleSize.Small };
            foreach (string[] ring in rings)
            {
                var rr = new RitualRing();
                foreach (string n in ring) { RuneDef d = R(n); if (d != null) rr.runes.Add(d); }
                rr.slots = Mathf.Max(1, rr.runes.Count);
                s.rings.Add(rr);
            }
            // written as one circle: its element in the centre, the rest in its sections
            s.weave = SpellCircle.FromRings(s);
            s.rings.Clear();
            return s;
        }

        public static List<RitualSchema> Fire() => new List<RitualSchema>
        {
            Make("Fire lance", new[] { "Fire", "Release" }, new[] { "Beam" }),
            Make("Fireball", new[] { "Fire", "Release" }, new[] { "Amplify" }),
            Make("Wall of flame", new[] { "Fire", "Lift" }, new[] { "Shape" }),
            Make("Hearth", new[] { "Fire", "Hold" }, new[] { "Aura" }),
            Make("Firebreak", new[] { "Fire", "Purge" }, new[] { "Aura" }),
            Make("Take the heat", new[] { "Fire", "Drain" }, new[] { "Breadth" }),
            Make("Wildfire", new[] { "Fire", "Growth" }, new[] { "Breadth" }),
            Make("Flame step", new[] { "Fire", "Release" }, new[] { "Step" }),
            Make("Cautery", new[] { "Fire" }, new[] { "Restore", "Purge" }),
            Make("Burning blade", new[] { "Fire" }, new[] { "Enchant" }),
            Make("Fire wisp", new[] { "Fire" }, new[] { "Summon" }),
            Make("Warmth sense", new[] { "Fire" }, new[] { "Sense" }),
            Make("Ward of flame", new[] { "Fire", "Barrier" }),
        };
    }
}
