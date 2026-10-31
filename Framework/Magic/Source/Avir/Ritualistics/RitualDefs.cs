using System.Collections.Generic;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    // =====================================================================================
    // RITUALISTICS - data (concept: Ритуалистика-концепт.md).
    // A rune is a set of numbers in XML (RuneDef); a ritual is a circle of 2-4 rings ("sections",
    // read from the centre outward), each holding 1-3 runes. Presets are XML too; the player's own
    // rituals are files in a folder, like ideoligions.
    // =====================================================================================

    public enum RuneRole { Verb, Modifier, Flow, Element, Form }

    /// <summary>What a rune does - the evaluator switches on this.</summary>
    public enum RuneKind
    {
        Fill, Transfer, Purge, Reforge, Transmute, Release, Hold, Drain, Lift, Growth,
        Amplify, Reach, Breadth, Anchor,
        Safety, Closing, Focus, Barrier, Thread,
        Infuse, Blood, Shroud,
        // magic: the elements, and the forms of a spell
        Fire, Water, Earth, Air, Light, Time, Life, Darkness, Space,
        Aura, Enchant, Step, Restore, Beam, Shape, Summon, Sense
    }

    public enum RitualCircleSize { Small, Medium, Large }

    public class RuneDef : Def
    {
        public RuneRole role;
        public RuneKind kind;
        /// <summary>Texture name: UI/Ritualistics/Runes/Ink|Glow/Rune_{glyph}.</summary>
        public string glyph;
        /// <summary>Energy units the rune asks of the ritual (verbs) or adds to its rate (focus).</summary>
        public float cost;
        /// <summary>Instability the rune adds (negative - calms the flow).</summary>
        public float instability;
        /// <summary>"Weight of the deed": external flow this rune draws by itself while the ritual runs.</summary>
        public float actWeight;
        /// <summary>The research that opens the rune (null - open from the start of ritualistics).</summary>
        public ResearchProjectDef research;
        /// <summary>False: shown and described, but its effect is not made yet.</summary>
        public bool implemented = true;
        public int uiOrder;
        /// <summary>A rune of magic: it exists only with the magic system on.</summary>
        public bool magic;
        /// <summary>An element's colour: a ritual or spell with elements takes on their blend.</summary>
        public Color color = Color.white;

        private Texture2D ink, glow;
        public Texture2D Ink => ink ?? (ink = ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Ink/Rune_" + glyph, false) ?? BaseContent.BadTex);
        public Texture2D Glow => glow ?? (glow = ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Glow/Rune_" + glyph, false) ?? BaseContent.BadTex);

        public bool Unlocked => research == null || research.IsFinished;

        public static List<RuneDef> All => DefDatabase<RuneDef>.AllDefsListForReading.Where(r => !r.magic || Avir.Magic.AvirMagic.On).OrderBy(r => r.uiOrder).ToList();
    }

    /// <summary>A section of the circle: the player chooses how many slots it has (1 up to the circle's
    /// limit) and writes runes into them.</summary>
    public class RitualRing
    {
        public List<RuneDef> runes = new List<RuneDef>();
        /// <summary>Slots drawn in this section (0 in XML presets: as many as runes).</summary>
        public int slots;
        public int Slots => Mathf.Max(1, Mathf.Max(slots, runes.Count));
        public RitualRing Copy() => new RitualRing { runes = new List<RuneDef>(runes), slots = slots };
    }

    /// <summary>A component circle beside the main circle - a cell of its own, and what lies on it works:
    /// a material the ritual works on, a seal that names what it makes, fuel (crystals, Avir feathers, a
    /// crystal battery) that feeds it, or an artifact that strengthens it.</summary>
    public enum ComponentKind { None, Material, Seal, Crystal, Feathers, Battery, Artifact }

    public class RitualComponent
    {
        public ComponentKind kind;
        public ThingDef thing;
        /// <summary>How many of the material lie in this circle (a Material circle only; at most a stack). The haulers keep the
        /// cell topped up to this between runs. Saved as a third field - "Material:Silver:60" - and older files read as 1.</summary>
        public int amount = 1;
        public RitualComponent Copy() => new RitualComponent { kind = kind, thing = thing, amount = amount };
        public string Pack() => kind + ":" + (thing?.defName ?? "") + (amount > 1 ? ":" + amount : "");
        public static RitualComponent Unpack(string s)
        {
            var c = new RitualComponent();
            if (s.NullOrEmpty()) return c;
            string[] p = s.Split(':');
            System.Enum.TryParse(p[0], out c.kind);
            if (p.Length > 1 && !p[1].NullOrEmpty()) c.thing = DefDatabase<ThingDef>.GetNamedSilentFail(p[1]);
            if (p.Length > 2 && int.TryParse(p[2], out int n) && n > 0) c.amount = n;
            return c;
        }
        public string Label
        {
            get
            {
                switch (kind)
                {
                    case ComponentKind.None: return "empty";
                    case ComponentKind.Crystal: return "psychic crystals (fuel)";
                    case ComponentKind.Feathers: return "Avir feathers (fuel)";
                    case ComponentKind.Battery: return "crystal battery (fuel)";
                    case ComponentKind.Artifact: return "an artifact (strengthens)";
                    case ComponentKind.Seal: return "seal of " + (thing?.label ?? "?");
                    default: return (thing?.label ?? "?") + " (material)" + (amount > 1 ? " x" + amount : "");
                }
            }
        }

        /// <summary>What lies on this circle in the world (for its icon).</summary>
        public ThingDef Expected
        {
            get
            {
                switch (kind)
                {
                    case ComponentKind.Crystal: return PsychicDefOf.Avir_PsyCrystal;
                    case ComponentKind.Feathers: return DefDatabase<ThingDef>.GetNamedSilentFail("Leather_AvirFeatheredFur");
                    case ComponentKind.Battery: return DefDatabase<ThingDef>.GetNamedSilentFail("Avir_CrystalBattery");
                    case ComponentKind.Artifact: return Avir.Artifactorics.ArtifactoricsDefOf.Avir_Artifact;
                    default: return thing;
                }
            }
        }

        public Texture2D Icon => Expected?.uiIcon;
    }

    /// <summary>What transmutation may work on: metals and jade - never essentium, tisserium or feignallite.</summary>
    public static class Transmutation
    {
        private static List<ThingDef> all;
        public static List<ThingDef> Materials => all ?? (all = DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.category == ThingCategory.Item && d.BaseMarketValue > 0f && Allowed(d)
                        && (d.stuffProps?.categories?.Contains(StuffCategoryDefOf.Metallic) == true || d.defName == "Jade"))
            .OrderBy(d => d.BaseMarketValue).ToList());

        public static bool Allowed(ThingDef d)
        {
            if (d == null) return false;
            string n = d.defName;
            return !n.Contains("Essentium") && !n.Contains("Tisserium") && !n.Contains("Feignallite");
        }
    }

    public static class RitualCircles
    {
        /// <summary>Most component circles beside a circle of this size.</summary>
        public static int Components(RitualCircleSize s) => s == RitualCircleSize.Small ? 3 : s == RitualCircleSize.Medium ? 5 : 7;
        /// <summary>Most sections a circle of this size can hold.</summary>
        public static int Rings(RitualCircleSize s) => s == RitualCircleSize.Small ? 2 : s == RitualCircleSize.Medium ? 3 : 4;
        /// <summary>Most slots one section can hold.</summary>
        public static int RunesPerRing(RitualCircleSize s) => s == RitualCircleSize.Small ? 2 : s == RitualCircleSize.Medium ? 3 : 4;
        public static int Cells(RitualCircleSize s) => s == RitualCircleSize.Small ? 3 : s == RitualCircleSize.Medium ? 5 : 7;
        public static float PowerFactor(RitualCircleSize s) => s == RitualCircleSize.Small ? 1f : s == RitualCircleSize.Medium ? 1.6f : 2.4f;
        public static string Label(RitualCircleSize s) => s == RitualCircleSize.Small ? "small" : s == RitualCircleSize.Medium ? "medium" : "large";
    }

    /// <summary>A written ritual: size and rings. Presets carry one; the player's files hold one each.</summary>
    public class RitualSchema : IExposable
    {
        public string name = "Untitled ritual";
        public RitualCircleSize size = RitualCircleSize.Small;
        public List<RitualRing> rings = new List<RitualRing>();
        public List<RitualComponent> components = new List<RitualComponent>();
        /// <summary>The preset it came from, if any (descriptions).</summary>
        public RitualPresetDef preset;
        /// <summary>A spell's circles, if it is written as circles round circles (the spell editor). Null for rituals.</summary>
        public SpellCircle weave;
        /// <summary>A spell's own sign, as the player made it (null: its element on a dark disc).</summary>
        public SpellIcon icon;
        /// <summary>Who wrote it: a mage's name, "path" for the ready spells of a path, empty if not known.</summary>
        public string author;

        public IEnumerable<RuneDef> AllRunes => rings.SelectMany(r => r.runes).Where(r => r != null);

        public bool Fits(RitualCircleSize circle) => (int)size <= (int)circle;

        public bool AllUnlocked => AllRunes.All(r => r.Unlocked);

        /// <summary>Can be drawn now: every rune researched (dev mode draws anything).</summary>
        public bool Drawable => Prefs.DevMode || AllUnlocked;

        public RitualSchema Copy()
        {
            var c = new RitualSchema { name = name, size = size, preset = preset };
            foreach (RitualRing r in rings) c.rings.Add(r.Copy());
            foreach (RitualComponent k in components) c.components.Add(k.Copy());
            c.weave = weave?.Copy();
            c.icon = icon?.Copy();
            c.author = author;
            return c;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "Untitled ritual");
            Scribe_Values.Look(ref size, "size", RitualCircleSize.Small);
            Scribe_Defs.Look(ref preset, "preset");
            Scribe_Deep.Look(ref weave, "weave");
            Scribe_Deep.Look(ref icon, "icon");
            Scribe_Values.Look(ref author, "author");
            // one list per ring
            List<string> packed = null;
            // "slots|rune,rune" per section (older files: just the runes)
            if (Scribe.mode == LoadSaveMode.Saving)
                packed = rings.Select(r => r.Slots + "|" + string.Join(",", r.runes.Where(x => x != null).Select(x => x.defName))).ToList();
            Scribe_Collections.Look(ref packed, "rings", LookMode.Value);
            List<string> comps = null;
            if (Scribe.mode == LoadSaveMode.Saving) comps = components.Select(k => k.Pack()).ToList();
            Scribe_Collections.Look(ref comps, "components", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) components = comps?.Select(RitualComponent.Unpack).ToList() ?? new List<RitualComponent>();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                rings = new List<RitualRing>();
                if (packed != null)
                    foreach (string raw in packed)
                    {
                        string line = raw ?? "";
                        int slots = 0, bar = line.IndexOf('|');
                        if (bar >= 0) { int.TryParse(line.Substring(0, bar), out slots); line = line.Substring(bar + 1); }
                        rings.Add(new RitualRing
                        {
                            slots = slots,
                            runes = line.Split(',').Where(n => !n.NullOrEmpty())
                                .Select(n => DefDatabase<RuneDef>.GetNamedSilentFail(n)).Where(d => d != null).ToList()
                        });
                    }
            }
        }
    }

    /// <summary>A ritual given to the player (readable in the ritual book), or a hidden one found by drawing it.</summary>
    public class RitualPresetDef : Def
    {
        public RitualCircleSize size = RitualCircleSize.Small;
        public List<RitualRing> rings = new List<RitualRing>();
        /// <summary>Not in the book until the player draws the same pattern (Trap...).</summary>
        public bool hidden;
        public int uiOrder;
        /// <summary>A rite the book shows only once this is researched (the spire's own, for one).</summary>
        public ResearchProjectDef researchPrerequisite;
        /// <summary>A sanctuary's own rite: not in the ritual book, only in the sanctuary's list.</summary>
        public bool sanctuary;

        public bool Researched => researchPrerequisite == null || researchPrerequisite.IsFinished;

        /// <summary>Component circles, as "Kind:defName" (Material:Silver, Seal:Gold, Crystal:).</summary>
        public List<string> components = new List<string>();

        public RitualSchema Schema() => new RitualSchema
        {
            name = label.CapitalizeFirst(),
            size = size,
            preset = this,
            rings = rings.Select(r => r.Copy()).ToList(),
            components = components.Select(RitualComponent.Unpack).ToList()
        };

        public bool Available => (defName != Avir.Magic.MagePath.Preset || Avir.Magic.AvirMagic.On) && (Prefs.DevMode || Schema().AllUnlocked && Researched) && (!hidden || RitualDiscoveries.Known(this) || Prefs.DevMode);

        public static List<RitualPresetDef> All => DefDatabase<RitualPresetDef>.AllDefsListForReading.OrderBy(p => p.uiOrder).ToList();
    }

    /// <summary>Hidden rituals the colony has found (per save).</summary>
    public class GameComponent_RitualDiscoveries : GameComponent
    {
        public List<RitualPresetDef> known = new List<RitualPresetDef>();
        public GameComponent_RitualDiscoveries(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref known, "known", LookMode.Def);
            if (known == null) known = new List<RitualPresetDef>();
        }
    }

    public static class RitualDiscoveries
    {
        private static GameComponent_RitualDiscoveries C => Current.Game?.GetComponent<GameComponent_RitualDiscoveries>();
        public static bool Known(RitualPresetDef p) => C?.known.Contains(p) == true;

        public static void Learn(RitualPresetDef p)
        {
            GameComponent_RitualDiscoveries c = C;
            if (c == null || p == null || c.known.Contains(p)) return;
            c.known.Add(p);
            Find.LetterStack.ReceiveLetter("Hidden ritual: " + p.LabelCap, "A pattern long forgotten answers the drawing: " + p.LabelCap + ".\n\n" + RitualText.Desc(p),
                RimWorld.LetterDefOf.PositiveEvent);
        }
    }
}

namespace Avir.Ritualistics
{
    /// <summary>A def's description without the mod name some mods (info-card helpers) append to every description -
    /// in the codex, where every rune is ours, it only repeats "Avir - Apex Predator" between the entries.</summary>
    public static class RitualText
    {
        public static string Desc(Verse.Def d)
        {
            string s = d?.description ?? "";
            string mod = d?.modContentPack?.Name;
            if (mod.NullOrEmpty()) return s;
            var lines = new System.Collections.Generic.List<string>(s.Split('\n'));
            while (lines.Count > 0)
            {
                string last = System.Text.RegularExpressions.Regex.Replace(lines[lines.Count - 1], "<[^>]*>", "").Trim();
                if (last.Length == 0 || last == mod) lines.RemoveAt(lines.Count - 1);
                else break;
            }
            return string.Join("\n", lines);
        }
    }
}
