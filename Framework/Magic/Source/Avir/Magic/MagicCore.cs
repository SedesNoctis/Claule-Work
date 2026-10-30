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

        public float Capacity => 100f * level * MagicConductors.CapacityFactor(pawn);
        public float Fill => Capacity <= 0f ? 0f : arcana / Capacity;

        public float Composure => pawn.GetStatValue(RitualDefOf.Avir_Composure);

        /// <summary>The band, as shares of the capacity: wider with composure and with what steadies the mage.</summary>
        public void Band(out float lo, out float hi)
        {
            float c = Mathf.Clamp01(Composure), w = MagicConductors.BandWidening(pawn);
            lo = Mathf.Max(0.06f, 0.25f - 0.15f * c - w);
            hi = Mathf.Min(0.96f, 0.65f + 0.25f * c + w);
        }

        public void Add(float amount) => arcana = Mathf.Clamp(arcana + amount, 0f, Capacity);

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
            arcana = Mathf.Min(arcana, Capacity);
            // the map's stream seeps into a mage a little on its own
            Map m = pawn.MapHeld;
            if (m != null)
            {
                MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(m);
                int tier = ext == null ? 0 : MapComponent_ExternalFlow.TierIndex(ext.Total);
                if (tier > 0) Add(Capacity * 0.02f * tier * hours);
            }
            if (vent) MagicVent.Vent(this, hours);
            Band(out float lo, out float hi);
            float f = Fill;
            Hediff great = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness);
            Hediff hollow = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneHollow);
            if (f > hi)
            {
                // too much: greatness swells, the faster the poorer the hold
                float over = (f - hi) / Mathf.Max(0.01f, 1f - hi);
                if (great == null) great = pawn.health.AddHediff(MagicDefOf.Avir_ArcaneGreatness);
                great.Severity = Mathf.Min(1f, great.Severity + 0.06f * hours * (0.5f + over) * (1.5f - Mathf.Clamp01(Composure)));
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
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 22f, 24f), sorc ? "Arcana - sorcerer" : "Arcana - mage");
            Text.Font = GameFont.Tiny;
            Rect tog = new Rect(inner.xMax - 16f, inner.y + 3f, 15f, 15f);
            Widgets.DrawBoxSolid(tog, core.vent ? (sorc ? new Color(0.93f, 0.68f, 0.12f) : new Color(0.16f, 0.32f, 0.92f)) : new Color(0.2f, 0.2f, 0.2f));
            Widgets.DrawBox(tog);
            if (Widgets.ButtonInvisible(tog)) core.vent = !core.vent;
            TooltipHandler.TipRegion(tog, core.vent ? "Letting the excess out on its own - above the white mark. Click to hold it all." : "Holding it all. Click to let the excess out on its own.");
            Rect bar = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
            Widgets.FillableBar(bar, core.Fill, sorc ? SorcFill : MageFill, Empty, false);
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
            Widgets.FillableBar(slot, core.Fill, sorc ? SorcFill : MageFill, Empty, false);
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
                          + core.Composure.ToStringPercent() + ") and with what steadies the " + (core.Sorcerer ? "sorcerer" : "mage") + ".");
            if (core.vent) sb.AppendLine("Kept at " + core.target.ToStringPercent() + " (the white mark): the excess goes into worn artifacts, then artifacts nearby, else into the air.");
            sb.AppendLine("Click the bar to set where it is kept.");
            if (MagicConductors.Horned(p)) sb.AppendLine("Horns: natural composure +" + MagicConductors.HornComposure.ToStringPercent() + ", but the core holds less.");
            int a = MagicConductors.Artifacts(p);
            if (a > 0) sb.AppendLine("Artifacts worn: " + a + " - steadier, but the core holds less.");
            sb.AppendLine();
            sb.AppendLine("Above the band greatness swells - and forbids work beneath the mage; held there long, the Arcana breaks out - in pulses, or (with a poor hold) into the mind.");
            sb.AppendLine("Below it the mage grows hollow, never ordinary.");
            sb.Append("Filled by meditation (more where the stream is strong), by a filling ritual in whose reach the mage stands, and slowly by the stream itself.");
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

        [DebugAction(Cat, "Make mage (learned)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Learned(Pawn p) { if (Ready()) Mages.Gift(p, Mages.Learned); }

        [DebugAction(Cat, "Make mage (born)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
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
                    p.LabelShortCap + " has stood in living magic long enough to take it in. " + p.LabelShortCap + " is a mage now - an arcane core has formed in the brain, and its Arcana must be kept within its edges.",
                    RimWorld.LetterDefOf.PositiveEvent, p);
        }
    }
}
