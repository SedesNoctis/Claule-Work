using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Artifactorics;
using Avir.Ritualistics;

namespace Avir.Magic
{
    // =====================================================================================
    // MAGIC, THE FOUNDATION. A mage carries the arcane gift (a trait): it keeps the memory of an arcane core in the
    // brain, which holds the Arcana. Held well, the Arcana sits between two edges - the band. Above it, greatness swells
    // (the faster, the poorer the mage's composure) and, held there long, it breaks out: in pulses (overflow), or - with
    // poor composure - into the mind (influx). Below it, the mage grows hollow, but never ordinary.
    // A natural conductor (horns) steadies the mage and widens the band, at the cost of what the core can hold; an
    // artifact worn steadies a little and holds a little less too. Everything here asks AvirMagic.On first.
    // =====================================================================================
    [DefOf]
    public static class MagicDefOf
    {
        public static TraitDef Avir_ArcaneGift;
        public static HediffDef Avir_ArcaneCore;
        public static HediffDef Avir_ArcaneGreatness;
        public static HediffDef Avir_ArcaneHollow;
        public static MentalStateDef Avir_ArcaneOverflow;
        public static MentalStateDef Avir_ArcaneInflux;
        static MagicDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(MagicDefOf)); }
    }

    public static class Mages
    {
        public const int Learned = 1, Born = 2;

        public static bool IsMage(Pawn p) => AvirMagic.On && p?.story?.traits != null && p.story.traits.HasTrait(MagicDefOf.Avir_ArcaneGift);

        public static Hediff_ArcaneCore Core(Pawn p) => p?.health?.hediffSet?.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneCore) as Hediff_ArcaneCore;

        /// <summary>Makes the pawn a mage: the gift, and a core of this level in the brain.</summary>
        public static Hediff_ArcaneCore Gift(Pawn p, int degree, int level = 1, float fill = 0.5f)
        {
            if (p?.story?.traits == null) return null;
            Trait old = p.story.traits.GetTrait(MagicDefOf.Avir_ArcaneGift);
            if (old != null && old.Degree != degree) { p.story.traits.RemoveTrait(old); old = null; }
            if (old == null) p.story.traits.GainTrait(new Trait(MagicDefOf.Avir_ArcaneGift, degree, true));
            Hediff_ArcaneCore c = Core(p) ?? GrowCore(p, level);
            if (c != null) { c.level = Mathf.Max(c.level, level); c.arcana = c.Capacity * fill; GameComponent_ArcaneMemory.Remember(p, c.level); }
            return c;
        }

        public static Hediff_ArcaneCore GrowCore(Pawn p, int level)
        {
            BodyPartRecord brain = p?.health?.hediffSet?.GetBrain();
            if (brain == null) return null;
            var c = (Hediff_ArcaneCore)HediffMaker.MakeHediff(MagicDefOf.Avir_ArcaneCore, p, brain);
            c.level = Mathf.Max(1, level);
            p.health.AddHediff(c, brain);
            return c;
        }
    }

    /// <summary>What steadies a mage and what it costs: horns (any - other mods' too), and artifacts worn.</summary>
    public static class MagicConductors
    {
        // a horn by its name: "Horn" as a word or a capitalised part of a name (MiniHorns, Horn_Large, horns), never "thorn"
        private static readonly System.Text.RegularExpressions.Regex HornName =
            new System.Text.RegularExpressions.Regex(@"(^|[^A-Za-z])([Hh]orns?|[Aa]ntlers?)([^a-z]|$)|(Horns?|Antlers?)([^a-z]|$)");

        public static bool IsHorn(string name) => !string.IsNullOrEmpty(name) && HornName.IsMatch(name);

        /// <summary>Horns of any kind: a gene whose name or label says horn, or a body part named so (other mods' races).</summary>
        public static bool Horned(Pawn p)
        {
            if (p?.genes != null)
                foreach (Gene g in p.genes.GenesListForReading)
                    if (g.Active && (IsHorn(g.def.defName) || IsHorn(g.def.label))) return true;
            return p?.RaceProps?.body != null && p.RaceProps.body.AllParts.Any(r => (IsHorn(r.def.defName) || IsHorn(r.def.label)) && !p.health.hediffSet.PartIsMissing(r));
        }

        public static int Artifacts(Pawn p)
        {
            int n = 0;
            if (p?.apparel != null) n += p.apparel.WornApparel.Count(a => a.TryGetComp<CompArtifact>() != null);
            if (p?.equipment != null) n += p.equipment.AllEquipmentListForReading.Count(e => e.TryGetComp<CompArtifact>() != null);
            return n;
        }

        /// <summary>What the core can hold, against a mage with no conductor at all.</summary>
        public static float CapacityFactor(Pawn p) => Mathf.Max(0.4f, (Horned(p) ? 0.7f : 1f) * Mathf.Pow(0.85f, Artifacts(p)));

        /// <summary>Physical conductors carry part of the Arcana outside the body: the feeling of greatness swells slower
        /// (horns -15%, each worn or held artifact -15%, never below half).</summary>
        public static float PrideFactor(Pawn p) => Mathf.Max(0.5f, 1f - (Horned(p) ? 0.15f : 0f) - 0.15f * Artifacts(p));

        /// <summary>How much wider the band is for what steadies the mage.</summary>
        public static float BandWidening(Pawn p) => (Horned(p) ? 0.1f : 0f) + Mathf.Min(0.1f, 0.05f * Artifacts(p));

        /// <summary>The natural composure of a horned body.</summary>
        public const float HornComposure = 0.15f;
    }

    public class Hediff_ArcaneCore : HediffWithComps
    {
        public int level = 1;
        public float arcana;
        public int outsideTicks;     // how long the Arcana has stood outside its band, above or below
        public float target = 0.5f;  // where the mage keeps it: past this, the excess is let out (vent)
        public bool vent = true;
        private int acc, lastGreatStage = -1;

        private MageBook book;
        public MageBook Book => book ?? (book = new MageBook());
        /// <summary>The mage's element - the path a ritual gives (null: none yet).</summary>
        public RuneDef path;
        /// <summary>What happened to the mage lately, newest last: "tick|text".</summary>
        public List<string> log = new List<string>();
        public float ventedToArtifacts;
        /// <summary>The mage's spells went into the colony's book when the mage became the colony's.</summary>
        public bool shared;

        public void Note(string text)
        {
            if (log == null) log = new List<string>();
            log.Add(Find.TickManager.TicksGame + "|" + text);
            while (log.Count > 6) log.RemoveAt(0);
        }

        public bool Sorcerer => pawn.story?.traits?.GetTrait(MagicDefOf.Avir_ArcaneGift)?.Degree == Mages.Born;

        /// <summary>The core's calm reserve - not a ceiling: Arcana can be held past it, and then greatness grows.
        /// A sorcerer's reserve is half again as deep.</summary>
        public float Capacity => 100f * level * MagicConductors.CapacityFactor(pawn) * (Sorcerer ? 1.5f : 1f);
        public float Fill => Capacity <= 0f ? 0f : arcana / Capacity;

        public float Composure => pawn.GetStatValue(RitualDefOf.Avir_Composure);

        /// <summary>The band, as shares of the capacity: wider with composure and with what steadies the mage.</summary>
        public void Band(out float lo, out float hi)
        {
            float c = Mathf.Max(0f, Composure), w = MagicConductors.BandWidening(pawn);
            lo = Mathf.Max(0.06f, 0.25f - 0.15f * Mathf.Clamp01(c) - w);
            // a mage's composure keeps thoughts of their own greatness further and further off - past the reserve, but never
            // all the way; a sorcerer's hardly does (their composure goes into holding spells instead)
            hi = Sorcerer ? 0.65f + 0.1f * Mathf.Clamp01(c) + w : 0.65f + 0.6f * (1f - Mathf.Exp(-1.3f * c)) + w;
        }

        /// <summary>How readily greatness swells past the band: a sorcerer always gives way to it; a mage less and less
        /// the steadier they are - never not at all.</summary>
        public float Pride => (Sorcerer ? 1.25f - 0.15f * Mathf.Clamp01(Composure) : 0.15f + 1.35f * Mathf.Exp(-1.5f * Mathf.Max(0f, Composure))) * MagicConductors.PrideFactor(pawn);

        /// <summary>No ceiling: Arcana may be held past the reserve (only never below nothing).</summary>
        public void Add(float amount) => arcana = Mathf.Max(0f, arcana + amount);

        public override string LabelInBrackets => "level " + level + " · Arcana " + Mathf.FloorToInt(arcana) + " / " + Mathf.FloorToInt(Capacity);

        public static readonly Color MageColor = new Color(0.35f, 0.55f, 1f), SorcererColor = new Color(1f, 0.76f, 0.25f);
        public override Color LabelColor => Sorcerer ? SorcererColor : MageColor;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref level, "level", 1);
            Scribe_Values.Look(ref arcana, "arcana");
            Scribe_Values.Look(ref outsideTicks, "outsideTicks");
            Scribe_Values.Look(ref target, "target", 0.5f);
            Scribe_Values.Look(ref vent, "vent", true);
            Scribe_Deep.Look(ref book, "book");
            Scribe_Defs.Look(ref path, "path");
            Scribe_Collections.Look(ref log, "log", LookMode.Value);
            Scribe_Values.Look(ref ventedToArtifacts, "ventedToArtifacts");
            Scribe_Values.Look(ref shared, "shared");
            if (log == null) log = new List<string>();
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            acc += delta;
            if (acc < 250) return;
            int dt = acc; acc = 0;
            if (!AvirMagic.On || pawn.Dead) return;
            // a mage who is the colony's now: their spells go into the colony's book
            if (!shared && pawn.Faction == Faction.OfPlayer && !pawn.IsPrisoner)
            {
                shared = true;
                int n = Book.spells.Count(s => GameComponent_SpellLibrary.Add(s));
                if (n > 0) Messages.Message(pawn.LabelShortCap + "'s spells (" + n + ") are written into the colony's book.", pawn, MessageTypeDefOf.PositiveEvent, false);
            }
            float hours = dt / (float)GenDate.TicksPerHour;
            // the map's stream seeps into a mage a little on its own
            Map m = pawn.MapHeld;
            if (m != null)
            {
                MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(m);
                int tier = ext == null ? 0 : MapComponent_ExternalFlow.TierIndex(ext.Total);
                // only up to the reserve: past it Arcana is taken in on purpose, never by itself
                if (tier > 0 && arcana < Capacity) Add(Mathf.Min(Capacity - arcana, Capacity * 0.02f * tier * hours));
            }
            if (vent) MagicVent.Vent(this, hours);
            Band(out float lo, out float hi);
            float f = Fill;
            Hediff great = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness);
            Hediff hollow = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneHollow);
            if (f > hi)
            {
                // too much: greatness swells, the faster the poorer the hold
                float over = (f - hi) / Mathf.Max(0.05f, hi);
                if (great == null) great = pawn.health.AddHediff(MagicDefOf.Avir_ArcaneGreatness);
                great.Severity = Mathf.Min(1f, great.Severity + 0.06f * hours * (0.5f + over) * Pride);
                outsideTicks += dt;
            }
            else if (great != null)
            {
                great.Severity -= 0.05f * hours;
                if (great.Severity <= 0.001f) pawn.health.RemoveHediff(great);
            }
            if (f < lo)
            {
                float under = (lo - f) / Mathf.Max(0.01f, lo);
                if (hollow == null) hollow = pawn.health.AddHediff(MagicDefOf.Avir_ArcaneHollow);
                hollow.Severity = Mathf.Clamp01(under);
                outsideTicks += dt;
            }
            else if (hollow != null) pawn.health.RemoveHediff(hollow);
            int gs = great == null || great.ShouldRemove ? -1 : great.CurStageIndex;
            if (gs != lastGreatStage) { lastGreatStage = gs; pawn.Notify_DisabledWorkTypesChanged(); }
            if (f >= lo && f <= hi) outsideTicks = Mathf.Max(0, outsideTicks - dt * 2);
            // held above the band too long: it breaks out - outwards, or (a weak hold) into the mind
            if (f > hi && outsideTicks > GenDate.TicksPerDay / 2 && pawn.Spawned && !pawn.InMentalState && !pawn.Downed && Rand.Chance(0.04f))
            {
                MentalStateDef def = Composure < 0.3f ? MagicDefOf.Avir_ArcaneInflux : MagicDefOf.Avir_ArcaneOverflow;
                if (pawn.mindState.mentalStateHandler.TryStartMentalState(def, null, true)) outsideTicks = 0;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (!AvirMagic.On || pawn.Faction != Faction.OfPlayer) yield break;
            if (Find.Selector.NumSelected == 1) yield return new Gizmo_Arcana { core = this };
            if (SpellCasting.Busy(pawn))
                yield return new Command_Action
                {
                    defaultLabel = "Break off the spell",
                    defaultDesc = "Let the spell being cast go. It is lost, and half of its Arcana with it.",
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false) ?? BaseContent.BadTex,
                    action = () => SpellCasting.BreakOff(pawn)
                };
            if (Prefs.DevMode)
            {
                // the dice of the hold, right on the caster: for trying a spell held or slipped
                yield return new Command_Toggle
                {
                    defaultLabel = "DEV: hold 100%", defaultDesc = "Every spell is held, whatever the risk. Applies to all casters.",
                    icon = TexCommand.ForbidOff, isActive = () => Spells.DevHold == 1, toggleAction = () => Spells.DevHold = Spells.DevHold == 1 ? 0 : 1
                };
                yield return new Command_Toggle
                {
                    defaultLabel = "DEV: hold 0%", defaultDesc = "Every spell slips, whatever the risk. Applies to all casters.",
                    icon = TexCommand.ForbidOn, isActive = () => Spells.DevHold == -1, toggleAction = () => Spells.DevHold = Spells.DevHold == -1 ? 0 : -1
                };
            }
            MageBook b = Book;
            for (int i = 0; i < b.open && i < 5; i++)
            {
                RitualSchema s = b.ready[i];
                if (s == null) continue;
                RuneDef icon = Spells.Element(s) ?? RitualEvaluator.Read(s).verbs.FirstOrDefault();
                var cmd = new Command_Spell
                {
                    mage = pawn, slot = i, spell = s,
                    defaultLabel = s.name,
                    defaultDesc = Spells.Describe(s),
                    icon = icon?.Glow ?? BaseContent.BadTex,
                    defaultIconColor = Spells.ColorOf(s),
                    Order = -100f + i
                };
                int left = b.cooldown[i] - Find.TickManager.TicksGame;
                if (left > 0) cmd.Disable("Ready again in " + left.ToStringTicksToPeriod() + ".");
                else if (arcana < Spells.Cost(s)) cmd.Disable("Not enough Arcana (" + Spells.Cost(s).ToString("0") + ").");
                else if (!pawn.Spawned || pawn.Downed) cmd.Disable("Cannot cast now.");
                yield return cmd;
            }
        }
    }

    /// <summary>The Arcana on the pawn: a bar with its band marked and the level the mage keeps it at. Click the bar to set
    /// that level; the small square lets the excess out on its own (into artifacts, else into the air).</summary>
    public class Gizmo_Arcana : Gizmo
    {
        public Hediff_ArcaneCore core;
        private static readonly Texture2D MageFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.16f, 0.32f, 0.92f));
        private static readonly Texture2D SorcFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.93f, 0.68f, 0.12f));
        private static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(new Color(0.07f, 0.07f, 0.1f));
        private static readonly Texture2D Gold = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 0.84f, 0.35f));
        private static readonly Texture2D Black = SolidColorMaterials.NewSolidColorTexture(new Color(0.05f, 0.04f, 0.03f));
        private static readonly Texture2D White = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 1f, 1f, 0.85f));

        public Gizmo_Arcana() { Order = -101f; }

        public override float GetWidth(float maxWidth)
        {
            Texture2D art = core != null && core.Sorcerer ? ArtSorc : ArtMage;
            return art != null ? Wide : 180f;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect r = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            bool sorc = core.Sorcerer;
            Texture2D art = sorc ? ArtSorc : ArtMage;
            if (art != null) { DrawArt(r, art, sorc); return new GizmoResult(GizmoState.Clear); }
            Widgets.DrawWindowBackground(r);
            Rect inner = r.ContractedBy(5f);
            Text.Font = GameFont.Tiny;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 22f, 24f), sorc ? "Arcana - sorcerer" : "Arcana - wizard");
            Text.Font = GameFont.Tiny;
            Rect tog = new Rect(inner.xMax - 16f, inner.y + 3f, 15f, 15f);
            Widgets.DrawBoxSolid(tog, core.vent ? (sorc ? new Color(0.93f, 0.68f, 0.12f) : new Color(0.16f, 0.32f, 0.92f)) : new Color(0.2f, 0.2f, 0.2f));
            Widgets.DrawBox(tog);
            if (Widgets.ButtonInvisible(tog)) core.vent = !core.vent;
            TooltipHandler.TipRegion(tog, core.vent ? "Letting the excess out on its own - above the white mark. Click to hold it all." : "Holding it all. Click to let the excess out on its own.");
            Rect bar = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
            Widgets.FillableBar(bar, Mathf.Clamp01(core.Fill), sorc ? SorcFill : MageFill, Empty, false);
            core.Band(out float lo, out float hi);
            Texture2D mark = sorc ? Black : Gold;
            GUI.DrawTexture(new Rect(bar.x + bar.width * lo - 1f, bar.y, 2f, bar.height), mark);
            GUI.DrawTexture(new Rect(bar.x + bar.width * hi - 1f, bar.y, 2f, bar.height), mark);
            if (core.vent) GUI.DrawTexture(new Rect(bar.x + bar.width * core.target - 1f, bar.y - 3f, 2f, bar.height + 3f), White);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, Mathf.FloorToInt(core.arcana) + " / " + Mathf.FloorToInt(core.Capacity));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (Mouse.IsOver(bar) && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                core.target = Mathf.Clamp01((Event.current.mousePosition.x - bar.x) / bar.width);
                core.vent = true;
                Event.current.Use();
            }
            TooltipHandler.TipRegion(bar, () => Tip(lo, hi), core.pawn.thingIDNumber ^ 0x51A7);
            return new GizmoResult(GizmoState.Clear);
        }

        // the plaques, when their art is present: a frame with an open slot for the bar (slot: 12-88% across, 52-82% down)
        private static Texture2D artMage, artSorc;
        private static bool looked;
        private static Texture2D ArtMage { get { Look(); return artMage; } }
        private static Texture2D ArtSorc { get { Look(); return artSorc; } }
        private static void Look()
        {
            if (looked) return;
            looked = true;
            artMage = ContentFinder<Texture2D>.Get("UI/Magic/ArcanaMage", false);
            artSorc = ContentFinder<Texture2D>.Get("UI/Magic/ArcanaSorcerer", false);
        }

        // drawn wider than the picture: only the plain lengths of the frame stretch, the ornaments keep their shape
        private const float Wide = 236f;
        private static readonly float[] CutsMage = { 0f, 200f, 400f, 520f, 720f, 921f }, CutsSorc = { 0f, 165f, 225f, 690f, 750f, 911f };
        private static readonly bool[] Stretch = { false, true, false, true, false };

        private void DrawArt(Rect r, Texture2D art, bool sorc)
        {
            float[] cuts = sorc ? CutsSorc : CutsMage;
            float X(float frac) => PlaqueArt.MapX(r, art, cuts, Stretch, frac * art.width);
            // the window of each plaque, as the art has it
            float x0 = X(sorc ? 0.127f : 0.130f), x1 = X(sorc ? 0.873f : 0.870f);
            Rect slot = sorc ? new Rect(x0, r.y + r.height * 0.568f, x1 - x0, r.height * 0.212f)
                             : new Rect(x0, r.y + r.height * 0.590f, x1 - x0, r.height * 0.225f);
            Widgets.FillableBar(slot, Mathf.Clamp01(core.Fill), sorc ? SorcFill : MageFill, Empty, false);
            core.Band(out float lo, out float hi);
            Texture2D mark = sorc ? Black : Gold;
            GUI.DrawTexture(new Rect(slot.x + slot.width * lo - 1f, slot.y, 2f, slot.height), mark);
            GUI.DrawTexture(new Rect(slot.x + slot.width * hi - 1f, slot.y, 2f, slot.height), mark);
            if (core.vent) GUI.DrawTexture(new Rect(slot.x + slot.width * core.target - 1f, slot.y - 2f, 2f, slot.height + 4f), White);
            PlaqueArt.Draw(r, art, cuts, Stretch);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.95f, 0.92f, 0.86f);
            float mid = r.y + r.height * (sorc ? 0.415f : 0.43f);
            Widgets.Label(new Rect(X(0.24f), mid - 11f, X(0.76f) - X(0.24f), 22f), Mathf.FloorToInt(core.arcana) + " / " + Mathf.FloorToInt(core.Capacity));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            if (Mouse.IsOver(slot) && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                core.target = Mathf.Clamp01((Event.current.mousePosition.x - slot.x) / slot.width);
                core.vent = true;
                Event.current.Use();
            }
            if (Mouse.IsOver(r) && Event.current.type == EventType.MouseDown && Event.current.button == 1) { core.vent = !core.vent; Event.current.Use(); }
            TooltipHandler.TipRegion(r, () => Tip(lo, hi) + "\n\nRight-click: " + (core.vent ? "hold it all (stop letting the excess out)." : "let the excess out again."), core.pawn.thingIDNumber ^ 0x51A7);
        }

        private string Tip(float lo, float hi)
        {
            Pawn p = core.pawn;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Arcana: " + Mathf.FloorToInt(core.arcana) + " of " + Mathf.FloorToInt(core.Capacity) + " (core level " + core.level + ").");
            sb.AppendLine("Safe between " + lo.ToStringPercent() + " and " + hi.ToStringPercent() + " (the marks) - wider with composure ("
                          + core.Composure.ToStringPercent() + ") and with what steadies the " + (core.Sorcerer ? "sorcerer" : "wizard") + ".");
            if (core.vent) sb.AppendLine("Kept at " + core.target.ToStringPercent() + " (the white mark): the excess goes into worn artifacts, then artifacts nearby, else into the air.");
            sb.AppendLine("Click the bar to set where it is kept.");
            if (MagicConductors.Horned(p)) sb.AppendLine("Horns: natural composure +" + MagicConductors.HornComposure.ToStringPercent() + ", but the core holds less.");
            int a = MagicConductors.Artifacts(p);
            if (a > 0) sb.AppendLine("Artifacts worn: " + a + " - steadier, but the core holds less.");
            sb.AppendLine();
            sb.AppendLine("Above the band greatness swells - and forbids work beneath the caster; held there long, the Arcana breaks out - in pulses, or (with a poor hold) into the mind.");
            sb.AppendLine("Below it the caster grows hollow, never ordinary.");
            sb.Append("Filled by meditation (more where the stream is strong), by a filling ritual in whose reach the caster stands, and slowly by the stream itself.");
            return sb.ToString();
        }
    }

    /// <summary>The excess let out: into the charge of artifacts worn, then of artifacts within a few cells, else into
    /// the air - a quiet telekinetic shimmer. A tenth of the core an hour, at most.</summary>
    public static class MagicVent
    {
        private const float PerHour = 0.1f, Reach = 6f;

        public static void Vent(Hediff_ArcaneCore c, float hours)
        {
            float excess = c.arcana - c.target * c.Capacity;
            if (excess <= 0.01f) return;
            float give = Mathf.Min(excess, c.Capacity * PerHour * hours);
            Pawn p = c.pawn;
            foreach (ArtifactState st in Targets(p))
            {
                if (give <= 0.001f) break;
                if (st.charge >= 0.999f) continue;
                float cap = Mathf.Max(1f, ArtifactCharge.Capacity(st, null));
                float g = Mathf.Min(give, (1f - st.charge) * cap);
                st.charge = Mathf.Min(1f, st.charge + g / cap);
                give -= g; c.arcana -= g;
                c.ventedToArtifacts += g;
            }
            if (c.ventedToArtifacts >= 10f) { c.Note("Let " + Mathf.FloorToInt(c.ventedToArtifacts) + " Arcana out into artifacts."); c.ventedToArtifacts = 0f; }
            if (give > 0.001f)
            {
                c.arcana -= give;   // into the air
                if (p.Spawned && Rand.Chance(0.3f)) FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 1.2f, new Color(0.7f, 0.65f, 1f, 0.5f));
            }
            c.arcana = Mathf.Max(0f, c.arcana);
        }

        private static IEnumerable<ArtifactState> Targets(Pawn p)
        {
            foreach (CompArtifactSocket so in ArtifactWearCache.Worn(p)) if (so.inlaid != null) yield return so.inlaid;
            if (p.apparel != null) foreach (Apparel a in p.apparel.WornApparel) if (a.TryGetComp<CompArtifact>()?.state is ArtifactState s) yield return s;
            if (p.equipment != null) foreach (ThingWithComps e in p.equipment.AllEquipmentListForReading) if (e.TryGetComp<CompArtifact>()?.state is ArtifactState s) yield return s;
            if (!p.Spawned) yield break;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(p.Position, p.Map, Reach, true))
            {
                if (t == p) continue;
                if (t.TryGetComp<CompArtifact>()?.state is ArtifactState s1) yield return s1;
                if (t.TryGetComp<CompArtifactHost>() is CompArtifactHost h) foreach (ArtifactState s2 in h.slots) if (s2 != null) yield return s2;
            }
        }
    }

    /// <summary>Overflow: wandering, pulses of Arcana break out and burn what is near, until it sinks within its band.</summary>
    public class MentalState_ArcaneOverflow : MentalState
    {
        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (!pawn.Spawned) return;
            Hediff_ArcaneCore c = Mages.Core(pawn);
            if (c == null) { RecoverFromState(); return; }
            c.Band(out float lo, out float hi);
            if (c.Fill <= (lo + hi) / 2f) { RecoverFromState(); return; }
            if (pawn.IsHashIntervalTick(300, delta))
            {
                c.Add(-c.Capacity * 0.08f);
                FleckMaker.Static(pawn.DrawPos, pawn.Map, FleckDefOf.PsycastAreaEffect, 2.5f);
                GenExplosion.DoExplosion(pawn.Position, pawn.Map, 2.2f, DamageDefOf.Burn, pawn, 4, -1f, null, null, null, null, null, 0f, 1, null, null, 0,
                    false, null, 0f, 1, 0f, false, null, new List<Thing> { pawn });
            }
            if (pawn.IsHashIntervalTick(120, delta) && (pawn.CurJobDef != JobDefOf.GotoWander))
            {
                if (RCellFinder.TryFindRandomCellNearWith(pawn.Position, c2 => c2.Standable(pawn.Map) && pawn.CanReach(c2, PathEndMode.OnCell, Danger.Deadly), pawn.Map, out IntVec3 to, 4, 10))
                    pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.GotoWander, to), JobCondition.InterruptForced);
            }
        }
    }

    /// <summary>Influx: the Arcana turns into the mind - the mage stands rapt, greatness swelling, the Arcana sinking
    /// into it until it is back within the band.</summary>
    public class MentalState_ArcaneInflux : MentalState
    {
        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (!pawn.Spawned) return;
            Hediff_ArcaneCore c = Mages.Core(pawn);
            if (c == null) { RecoverFromState(); return; }
            c.Band(out float lo, out float hi);
            if (c.Fill <= (lo + hi) / 2f) { RecoverFromState(); return; }
            if (pawn.IsHashIntervalTick(250, delta))
            {
                c.Add(-c.Capacity * 0.04f);
                Hediff g = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness) ?? pawn.health.AddHediff(MagicDefOf.Avir_ArcaneGreatness);
                g.Severity = Mathf.Min(1f, g.Severity + 0.03f);
                if (Rand.Chance(0.3f)) FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.PsycastAreaEffect);
            }
            if (pawn.IsHashIntervalTick(120, delta) && pawn.CurJobDef != JobDefOf.Wait_MaintainPosture)
                pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 600), JobCondition.InterruptForced);
        }
    }

    public class ThoughtWorker_Greatness : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirMagic.On) return ThoughtState.Inactive;
            Hediff h = p.health?.hediffSet?.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness);
            return h == null ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(h.Severity >= 0.7f ? 2 : h.Severity >= 0.35f ? 1 : 0);
        }
    }

    public class ThoughtWorker_Hollow : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirMagic.On) return ThoughtState.Inactive;
            Hediff h = p.health?.hediffSet?.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneHollow);
            return h == null ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(h.Severity >= 0.8f ? 2 : h.Severity >= 0.4f ? 1 : 0);
        }
    }

    /// <summary>The gift remembers the core: lost, it grows back at the level it had.</summary>
    public class GameComponent_ArcaneMemory : GameComponent
    {
        private Dictionary<Pawn, int> levels = new Dictionary<Pawn, int>();
        private List<Pawn> keys; private List<int> vals;
        private static GameComponent_ArcaneMemory instance;

        public GameComponent_ArcaneMemory(Game game) { instance = this; }

        public static void Remember(Pawn p, int level) { if (instance != null && p != null) instance.levels[p] = level; }

        private Dictionary<Pawn, float> exposure = new Dictionary<Pawn, float>();
        private List<Pawn> eKeys; private List<float> eVals;

        public static float Expose(Pawn p, float amount)
        {
            if (instance == null || p == null) return 0f;
            float v = (instance.exposure.TryGetValue(p, out float o) ? o : 0f) + amount;
            instance.exposure[p] = v;
            return v;
        }

        public static float Exposure(Pawn p) => instance != null && p != null && instance.exposure.TryGetValue(p, out float v) ? v : 0f;

        public override void GameComponentTick()
        {
            SpellShock.Flush();
            if (!AvirMagic.On || Find.TickManager.TicksGame % 2500 != 0) return;
            foreach (Map m in Find.Maps)
                foreach (Pawn p in m.mapPawns.AllPawnsSpawned.ToList())
                {
                    if (!Mages.IsMage(p) || p.Dead) continue;
                    Hediff_ArcaneCore c = Mages.Core(p);
                    if (c != null) { levels[p] = c.level; continue; }
                    c = Mages.GrowCore(p, levels.TryGetValue(p, out int l) ? l : 1);
                    if (c != null && p.Faction == Faction.OfPlayer)
                        Messages.Message(p.LabelShortCap + "'s arcane core has grown back - the gift remembered it.", p, MessageTypeDefOf.NeutralEvent, false);
                }
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) levels.RemoveAll(kv => kv.Key == null || kv.Key.Destroyed);
            if (Scribe.mode == LoadSaveMode.Saving) exposure.RemoveAll(kv => kv.Key == null || kv.Key.Destroyed);
            Scribe_Collections.Look(ref levels, "arcaneLevels", LookMode.Reference, LookMode.Value, ref keys, ref vals);
            Scribe_Collections.Look(ref exposure, "arcaneExposure", LookMode.Reference, LookMode.Value, ref eKeys, ref eVals);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (levels == null) levels = new Dictionary<Pawn, int>(); levels.RemoveAll(kv => kv.Key == null);
                if (exposure == null) exposure = new Dictionary<Pawn, float>(); exposure.RemoveAll(kv => kv.Key == null);
                instance = this;
            }
        }
    }

    /// <summary>Rarely, a person is born with magic (the Avir and the Singers have their own).</summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_BornWithMagic
    {
        public const float Chance = 0.01f;

        public static void Postfix(Pawn __result)
        {
            if (!AvirMagic.On || __result?.RaceProps == null || !__result.RaceProps.Humanlike || __result.story?.traits == null) return;
            if (__result.genes?.Xenotype?.defName?.StartsWith("Avir") == true) return;
            if (!Rand.Chance(Chance)) return;
            Mages.Gift(__result, Mages.Born, 1, Rand.Range(0.35f, 0.65f));
            if (__result.Faction != Faction.OfPlayer) MagePath.Stranger(__result);
        }
    }

    public static class MagicDev
    {
        private const string Cat = "Avir magic";

        private static bool Ready() { if (AvirMagic.On) return true; Messages.Message("Magic is off (mod settings; it takes effect after a restart).", MessageTypeDefOf.RejectInput, false); return false; }

        [DebugAction(Cat, "Make wizard (learned)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Learned(Pawn p) { if (Ready()) Mages.Gift(p, Mages.Learned); }

        [DebugAction(Cat, "Make sorcerer (born)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Born(Pawn p) { if (Ready()) Mages.Gift(p, Mages.Born); }

        [DebugAction(Cat, "Arcana full", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Full(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) c.arcana = c.Capacity; }

        [DebugAction(Cat, "Arcana empty", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Empty(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) c.arcana = 0f; }

        [DebugAction(Cat, "Spells ready again (cooldowns off)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Ready(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) for (int i = 0; i < 5; i++) c.Book.cooldown[i] = 0; }

        [DebugAction(Cat, "Greatness and hollow away", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Calm(Pawn p)
        {
            foreach (HediffDef d in new[] { MagicDefOf.Avir_ArcaneGreatness, MagicDefOf.Avir_ArcaneHollow })
            { Hediff h = p.health.hediffSet.GetFirstHediffOfDef(d); if (h != null) p.health.RemoveHediff(h); }
            Hediff_ArcaneCore c = Mages.Core(p); if (c != null) c.outsideTicks = 0;
        }

        [DebugAction(Cat, "Set path...", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SetPath(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null) return;
            var opts = DefDatabase<RuneDef>.AllDefsListForReading.Where(r => r.role == RuneRole.Element && r.magic)
                .Select(r => new FloatMenuOption(r.LabelCap + " (with all its spells)", () => { c.path = null; MagePath.Grant(p, r, 1f); })).ToList();
            opts.Add(new FloatMenuOption("No path", () => c.path = null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        [DebugAction(Cat, "Make a stranger's spell (into the book)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StrangerSpell(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) c.Book.spells.Add(SpellGen.Make(c.path ?? MagePath.Made.FirstOrDefault(), p)); }

        [DebugAction(Cat, "Throw it back (3 cells, from the north)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevThrow(Pawn p) => SpellHurl.Throw(p, p.Position + IntVec3.North * 3, 3, p.Map);

        [DebugAction(Cat, "Knock it down (3 s)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevFell(Pawn p) => SpellHurl.Fell(p, p.Position + IntVec3.North, 1, p.Map, null, 1f, 180);

        [DebugAction(Cat, "Throw it up (fall, power 1)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevLift(Pawn p) => SpellHurl.Lift(p, null, 1f);

        [DebugAction(Cat, "Throw it up (fall, power 3)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevLiftHard(Pawn p) => SpellHurl.Lift(p, null, 3f);

        [DebugAction(Cat, "Draw it to the nearest colonist (fire)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevPullFire(Pawn p) => DevPull(p, RuneKind.Fire);

        [DebugAction(Cat, "Draw it to the nearest colonist (water)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevPullWater(Pawn p) => DevPull(p, RuneKind.Water);

        [DebugAction(Cat, "Draw it to the nearest colonist (earth)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevPullEarth(Pawn p) => DevPull(p, RuneKind.Earth);

        [DebugAction(Cat, "Draw it to the nearest colonist (air)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevPullAir(Pawn p) => DevPull(p, RuneKind.Air);

        private static void DevPull(Pawn p, RuneKind el)
        {
            Pawn mage = p.Map.mapPawns.FreeColonistsSpawned.Where(c => c != p).OrderBy(c => c.Position.DistanceToSquared(p.Position)).FirstOrDefault();
            if (mage != null) SpellDash.Pull(mage, p, el, 1f, 2f);
        }

        [DebugAction(Cat, "Field of steam here (r 4, 30 s)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevFieldSteam() => DevField(ComboKind.Steam);

        [DebugAction(Cat, "Field of storm here (r 4, 30 s)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevFieldStorm() => DevField(ComboKind.Storm);

        [DebugAction(Cat, "Field of magma here (r 4, 30 s)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevFieldMagma() => DevField(ComboKind.Magma);

        [DebugAction(Cat, "Field of haze here (r 4, 30 s)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevFieldHaze() => DevField(ComboKind.Haze);

        [DebugAction(Cat, "Field of bog here (r 4, 30 s)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevFieldBog() => DevField(ComboKind.Bog);

        [DebugAction(Cat, "Field of dust here (r 4, 30 s)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevFieldDust() => DevField(ComboKind.Dust);

        private static void DevField(ComboKind k)
        {
            Map map = Find.CurrentMap; IntVec3 c = Verse.UI.MouseCell();
            Pawn by = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (k == ComboKind.Magma) foreach (IntVec3 x in FireMagic.Point(c, map, 2.5f)) MagmaPool.Pour(x, map, by, 1f, 1800, false);
            else if (k == ComboKind.Bog) foreach (IntVec3 x in FireMagic.Point(c, map, 2.5f)) MirePatch.Spread(x, map, by, 1f, 5000);
            else MapComponent_ComboFields.Raise(map, k, by, c, 4f, 1f, 1800, false);
        }

        [DebugAction(Cat, "Marsh gas here (light it with fire)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevMarsh() { Map map = Find.CurrentMap; foreach (IntVec3 x in FireMagic.Point(Verse.UI.MouseCell(), map, 2.5f)) Gas_MarshGas.Release(x, map, 1, false); }

        [DebugAction(Cat, "Scalding steam here", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevSteam() { Gas_ScaldingSteam.Release(Verse.UI.MouseCell(), Find.CurrentMap, 3); }

        [DebugAction(Cat, "Lightning into the water here (runs through it)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevConduct() { Map map = Find.CurrentMap; Reactions.Conduct(Verse.UI.MouseCell(), map, map.mapPawns.FreeColonistsSpawned.FirstOrDefault(), 1f); }

        [DebugAction(Cat, "Heat this spot (reactions)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevHeat() { Map map = Find.CurrentMap; Reactions.Touch(Brings.Heat | Brings.Fire, FireMagic.Point(Verse.UI.MouseCell(), map, 2.5f), map, null, 1f); }

        [DebugAction(Cat, "Water this spot (reactions)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevWater() { Map map = Find.CurrentMap; Reactions.Touch(Brings.Water, FireMagic.Point(Verse.UI.MouseCell(), map, 2.5f), map, null, 1f); }

        [DebugAction(Cat, "Ward: lay (choose element, strength)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevLayWard()
        {
            var wards = new List<HediffDef> { FireDefOf.Avir_FireWard, WaterDefOf.Avir_WaterWard, EarthAirDefOf.Avir_EarthWard, EarthAirDefOf.Avir_AirWard,
                ComboDefOf.Avir_SteamWard, ComboDefOf.Avir_StormWard, ComboDefOf.Avir_MagmaWard, ComboDefOf.Avir_HazeWard, ComboDefOf.Avir_BogWard, ComboDefOf.Avir_DustWard };
            var opts = new List<DebugMenuOption>();
            foreach (HediffDef w in wards.Where(x => x != null))
                foreach (int amp in new[] { 0, 2, 4 })
                {
                    HediffDef wd = w; int a = amp;
                    opts.Add(new DebugMenuOption(w.label + (a == 0 ? "" : " (Amplify ×" + a + ")"), DebugMenuOptionMode.Tool, () =>
                    {
                        Pawn p = Verse.UI.MouseCell().GetFirstPawn(Find.CurrentMap);
                        if (p != null) SpellWards.Lay(p, wd, 1f, a, 30000);
                    }));
                }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        [DebugAction(Cat, "Ward: strike with an element (direct, 20)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevHitWard()
        {
            var opts = new List<DebugMenuOption>();
            foreach (RuneKind k in new[] { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air })
            {
                RuneKind kk = k;
                opts.Add(new DebugMenuOption(k.ToString(), DebugMenuOptionMode.Tool, () => DevStrike(kk, ComboKind.None)));
            }
            foreach (ComboKind c in new[] { ComboKind.Steam, ComboKind.Storm, ComboKind.Magma, ComboKind.Haze, ComboKind.Bog, ComboKind.Dust })
            {
                ComboKind cc = c;
                opts.Add(new DebugMenuOption(Combos.Label(c), DebugMenuOptionMode.Tool, () => DevStrike(null, cc)));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        private static void DevStrike(RuneKind? el, ComboKind k)
        {
            Pawn p = Verse.UI.MouseCell().GetFirstPawn(Find.CurrentMap);
            if (p == null) return;
            float before = p.health.summaryHealth.SummaryHealthPercent;
            using (SpellWards.Of(el, k, true)) p.TakeDamage(new DamageInfo(el == RuneKind.Fire || k == ComboKind.Magma || k == ComboKind.Steam ? DamageDefOf.Flame : DamageDefOf.Blunt, 20f));
            Messages.Message(p.LabelShortCap + ": health " + before.ToStringPercent() + " -> " + p.health.summaryHealth.SummaryHealthPercent.ToStringPercent()
                + (SpellWards.On(p) is Hediff_SpellWard w ? ", ward " + w.energy.ToString("0") + "/" + w.energyMax.ToString("0") : ", no ward"), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "Spreading here (Growth of a meeting)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevCreep()
        {
            var opts = new List<DebugMenuOption>();
            foreach (ComboKind c in new[] { ComboKind.Magma, ComboKind.Bog, ComboKind.Steam, ComboKind.Storm })
            {
                ComboKind cc = c;
                opts.Add(new DebugMenuOption(Combos.Label(c), DebugMenuOptionMode.Tool, () =>
                    MapComponent_Creep.Start(Find.CurrentMap, cc, null, Verse.UI.MouseCell(), 0.5f, 6f, 1f, 2500)));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        [DebugAction(Cat, "Wind at their back +1 (air momentum)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevMomentum(Pawn p) => AirMomentum.Rise(p);

        [DebugAction(Cat, "Raging flame on this wizard or sorcerer", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevRaging(Pawn p) => FireMagic.Raging(p);

        [DebugAction(Cat, "Stone at hand here (count for earth, magma, bog, dust)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevStone()
        {
            Map map = Find.CurrentMap; IntVec3 c = Verse.UI.MouseCell();
            Messages.Message("Stone at hand: earth " + EarthStone.Units(map, c, ComboKind.None).ToString("0.#") + ", magma " + EarthStone.Units(map, c, ComboKind.Magma).ToString("0.#")
                + ", bog " + EarthStone.Units(map, c, ComboKind.Bog).ToString("0.#") + ", dust " + EarthStone.Units(map, c, ComboKind.Dust).ToString("0.#") + " (of 10)", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "Spell rubble here (stone for earth)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevRubble() { Map map = Find.CurrentMap; foreach (IntVec3 c in FireMagic.Point(Verse.UI.MouseCell(), map, 1.5f)) MagicFx.Rubble(c, map); }

        [DebugAction(Cat, "Lost hold on this wizard or sorcerer (choose element)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevSlip()
        {
            var opts = new List<DebugMenuOption>
            {
                new DebugMenuOption("Fire", DebugMenuOptionMode.Tool, () => { Pawn p = Verse.UI.MouseCell().GetFirstPawn(Find.CurrentMap); if (p != null) FireMagic.Raging(p); }),
                new DebugMenuOption("Water", DebugMenuOptionMode.Tool, () => { Pawn p = Verse.UI.MouseCell().GetFirstPawn(Find.CurrentMap); if (p != null) WaterMagic.Chilled(p); }),
                new DebugMenuOption("Earth", DebugMenuOptionMode.Tool, () => { Pawn p = Verse.UI.MouseCell().GetFirstPawn(Find.CurrentMap); if (p != null) EarthMagic.Petrified(p); }),
                new DebugMenuOption("Air", DebugMenuOptionMode.Tool, () => { Pawn p = Verse.UI.MouseCell().GetFirstPawn(Find.CurrentMap); if (p != null) AirMagic.Winded(p); }),
            };
            foreach (ComboKind c in new[] { ComboKind.Steam, ComboKind.Storm, ComboKind.Magma, ComboKind.Haze, ComboKind.Bog, ComboKind.Dust })
            {
                ComboKind cc = c;
                opts.Add(new DebugMenuOption(Combos.Label(c), DebugMenuOptionMode.Tool, () => { Pawn p = Verse.UI.MouseCell().GetFirstPawn(Find.CurrentMap); if (p != null) ComboMagic.Price(cc, p); }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        [DebugAction(Cat, "Fire smoke here (CE smoke if present)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevSmoke() { Map map = Find.CurrentMap; foreach (IntVec3 c in FireMagic.Point(Verse.UI.MouseCell(), map, 2f)) FireSmoke.Puff(c, map, 1f); }

        [DebugAction(Cat, "Hold: always (100%) - toggle", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevHoldAlways() { Spells.DevHold = Spells.DevHold == 1 ? 0 : 1; Messages.Message("Spell hold: " + (Spells.DevHold == 1 ? "always held (100%)" : "by the dice"), MessageTypeDefOf.NeutralEvent, false); }

        [DebugAction(Cat, "Hold: never (0%) - toggle", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevHoldNever() { Spells.DevHold = Spells.DevHold == -1 ? 0 : -1; Messages.Message("Spell hold: " + (Spells.DevHold == -1 ? "always slips (0%)" : "by the dice"), MessageTypeDefOf.NeutralEvent, false); }

        [DebugAction(Cat, "Test spells into this caster's book (every element, CE checks)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevTestSpells(Pawn p)
        {
            Hediff_ArcaneCore core = Mages.Core(p);
            if (core == null) { Messages.Message(p.LabelShort + " is not a caster.", MessageTypeDefOf.RejectInput, false); return; }
            RuneDef R(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);
            RitualSchema Spell(string name, params string[] circles)
            {
                // each circle: "Element:rune,rune|rune,rune" - sections split by |
                var root = new SpellCircle { binding = true };
                foreach (string c in circles)
                {
                    string[] parts = c.Split(':');
                    var k = new SpellCircle { element = R(parts[0]) };
                    foreach (string sec in parts[1].Split('|'))
                    {
                        var runes = sec.Split(',').Select(x => R(x.Trim())).Where(x => x != null).ToList();
                        k.sections.Add(new RitualRing { runes = runes, slots = Mathf.Max(1, runes.Count) });
                    }
                    root.inner.Add(k);
                }
                return new RitualSchema { name = name, weave = root };
            }
            var list = new List<RitualSchema>
            {
                Spell("TEST Fire blow", "Fire:Release,Amplify,Amplify,Amplify,Amplify"),
                Spell("TEST Water blow + lift", "Water:Release,Amplify,Amplify,Amplify,Amplify|Lift"),
                Spell("TEST Earth blow + lift", "Earth:Release,Amplify,Amplify,Amplify,Amplify|Lift"),
                Spell("TEST Air gust + lift", "Air:Release,Amplify,Amplify,Amplify,Amplify|Lift"),
                Spell("TEST Water drain", "Water:Drain"),
                Spell("TEST Ward on self (fire)", "Fire:Barrier"),
                Spell("TEST Steam", "Fire:Release", "Water:Release"),
                Spell("TEST Storm", "Water:Release", "Air:Release"),
                Spell("TEST Magma", "Fire:Release,Amplify,Amplify", "Earth:Release"),
                Spell("TEST Haze", "Fire:Release", "Air:Release"),
                Spell("TEST Bog hold", "Water:Hold", "Earth:Hold"),
                Spell("TEST Dust", "Earth:Release", "Air:Release"),
            };
            foreach (RitualSchema s in list) core.Book.spells.Add(s);
            Messages.Message(list.Count + " test spells written into " + p.LabelShort + "'s book.", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "Weave a ward into clothes (water)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevWeave(Pawn p) => Hediff_WardCharm.Weave(p, DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Water"), ComboKind.None, 1f, 0);

        [DebugAction(Cat, "Release of fire here (burning ground flares)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevFlares() { Map map = Find.CurrentMap; IntVec3 c = Verse.UI.MouseCell(); FireMagic.Flares(null, map, c, new List<IntVec3> { c }, 6.4f, 1f); }

        [DebugAction(Cat, "Body as magic sees it", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevBody(Pawn p)
        {
            var b = SpellBody.Of(p);
            Messages.Message(p.LabelShort + ": weight " + b.weight.ToString("0") + " kg (x" + SpellBody.Heavy(p).ToString("0.00") + ")" + (b.sealed_ ? ", sealed" : "") + (b.metalArmour ? ", metal armour" : "")
                + ", flesh " + b.flesh.ToStringPercent() + ", eyes dazzle x" + b.eyeDazzle.ToString("0.0#") + (b.eyeSeesThrough > 0f ? " (sees through mirages " + b.eyeSeesThrough.ToStringPercent() + ")" : "")
                + ", legs x" + b.legsLand.ToString("0.0#") + (b.pegLeg ? ", peg leg" : "") + (b.woodAny ? ", wood" : "") + (b.bionicLimb ? ", bionic limb" : ""), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "Ward trace to log - toggle", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevWardTrace() { SpellWards.Trace = !SpellWards.Trace; Messages.Message("Ward trace: " + (SpellWards.Trace ? "on (see the log)" : "off"), MessageTypeDefOf.NeutralEvent, false); }

        [DebugAction(Cat, "Magma wall here (sets into stone)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevMagmaWall() { Map map = Find.CurrentMap; IntVec3 c = Verse.UI.MouseCell(); for (int i = -2; i <= 2; i++) MagmaPool.Pour(c + new IntVec3(i, 0, 0), map, null, 1f, 300, true, 5000); }

        [DebugAction(Cat, "Give the six meeting spells (into the book)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevMeetings(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) foreach (RitualSchema s in SpellExamples.Meetings()) c.Book.spells.Add(s); }

        [DebugAction(Cat, "Root it (8 s)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevRoot(Pawn p) => MagicFx.Give(p, EarthAirDefOf.Avir_Rooted, 480);

        [DebugAction(Cat, "Open hand places (all 5)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void OpenHand(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) c.Book.open = 5; }

        [DebugAction(Cat, "Arcana +25%", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Plus(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); c?.Add(c.Capacity * 0.25f); }

        [DebugAction(Cat, "Arcana -25%", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Minus(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); c?.Add(-c.Capacity * 0.25f); }

        [DebugAction(Cat, "Core level +1", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LevelUp(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) { c.level++; GameComponent_ArcaneMemory.Remember(p, c.level); } }

        [DebugAction(Cat, "Remove the core (the gift grows it back)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RemoveCore(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) p.health.RemoveHediff(c); }

        [DebugAction(Cat, "Magic exposure +500 (show total)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Expo(Pawn p) { if (!Ready()) return; MagicExposure.Exposed(p, 500f); Messages.Message(p.LabelShort + ": exposure " + GameComponent_ArcaneMemory.Exposure(p).ToString("0") + " / " + MagicExposure.Threshold.ToString("0"), MessageTypeDefOf.NeutralEvent, false); }

        [DebugAction(Cat, "Half a day above the band (breakdown soon)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Outside(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) { c.arcana = c.Capacity; c.outsideTicks = GenDate.TicksPerDay; } }
    }
}

namespace Avir.Magic
{
    /// <summary>How the Arcana is filled. For now: meditation, a quarter of the core an hour, more where the stream is strong.</summary>
    public static class MagicFill
    {
        public static void Meditated(Verse.Pawn p, int ticks)
        {
            if (!AvirMagic.On) return;
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null) return;
            int tier = 0;
            if (p.MapHeld != null) { var ext = Avir.Ritualistics.MapComponent_ExternalFlow.Of(p.MapHeld); tier = ext == null ? 0 : Avir.Ritualistics.MapComponent_ExternalFlow.TierIndex(ext.Total); }
            float add = c.Capacity * 0.25f * (1f + 0.5f * tier) * ticks / (float)RimWorld.GenDate.TicksPerHour;
            // past the reserve the core takes it in slowly
            if (c.arcana >= c.Capacity) add *= 0.5f;
            // beside a lit sanctuary: three times as fast, drawn from its store
            CompSanctuary s = CompSanctuary.Near(p);
            if (s != null) add += s.Draw(add * 2f);
            c.Add(add);
        }
    }
}

namespace Avir.Magic
{
    /// <summary>A person long under living magic takes it in: every ritual stood in leaves its measure; past a threshold,
    /// each further one may make a mage of them (learned). The Avir and the Singers have their own magic.</summary>
    public static class MagicExposure
    {
        public const float Threshold = 1500f, ChanceAfter = 0.3f;

        public static void Exposed(Verse.Pawn p, float amount)
        {
            if (!AvirMagic.On || p?.story?.traits == null || !p.RaceProps.Humanlike || Mages.IsMage(p)) return;
            if (p.genes?.Xenotype?.defName?.StartsWith("Avir") == true) return;
            float total = GameComponent_ArcaneMemory.Expose(p, amount);
            if (total < Threshold || !Verse.Rand.Chance(ChanceAfter)) return;
            Mages.Gift(p, Mages.Learned, 1, 0.3f);
            if (p.Faction == RimWorld.Faction.OfPlayer)
                Find.LetterStack.ReceiveLetter("Magic taken in: " + p.LabelShort,
                    p.LabelShortCap + " has stood in living magic long enough to take it in. " + p.LabelShortCap + " is a wizard now - an arcane core has formed in the brain, and its Arcana must be kept within its edges.",
                    RimWorld.LetterDefOf.PositiveEvent, p);
        }
    }
}
