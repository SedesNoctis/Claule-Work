using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    // =====================================================================================
    // THE ORDER OF A SPELL - the same law as the rituals': everything flows from the centre outward, and what comes
    // first feeds what comes after.
    //   Circles: from the heart outward - the circles round the heart first (clockwise from the top, as they stand),
    //     then the circles inside the great ones. They go one after another: each waits until the one before it has
    //     ended (the mage has come up out of the ground or landed, the drawn-in have arrived) and strikes where it
    //     ended - by the mage after a step, by the mage after an aura or a pull, at the target otherwise.
    //     Darkness and Space turn it the other way: counter-clockwise from the top.
    //   Runes in a circle: from the element outward. Verbs work in that order; a drain that has verbs after it feeds
    //     them at once (as the last, it is kept for the next spell).
    // =====================================================================================
    public class MapComponent_SpellChain : MapComponent
    {
        private const int Gap = 20;          // between two circles that end at once: the eye sees the order
        private const int Patience = 900;    // the longest a circle waits for the one before it

        private class Chain : IExposable
        {
            public Pawn caster;
            public SpellCircle root;
            public List<SpellCircle> links;
            public int next;
            public float power;
            public bool storm;
            public IntVec3 wallTo = IntVec3.Invalid;
            public LocalTargetInfo target;
            public int at;
            public bool waitCaster;
            public List<Pawn> waitFor;
            public int giveUp;
            public bool endByMage;
            public Dictionary<SpellCircle, ComboKind> combos;
            /// <summary>Where the caster stood when the spell was let go: a later step aimed at the caster goes back here.</summary>
            public IntVec3 origin = IntVec3.Invalid;
            /// <summary>The further places named for Reach and Thread, in order, and how many are used.</summary>
            public List<IntVec3> hops = new List<IntVec3>();
            public int hopNext;

            public void ExposeData()
            {
                Scribe_Collections.Look(ref hops, "hops", LookMode.Value);
                Scribe_Values.Look(ref hopNext, "hopNext");
                if (hops == null) hops = new List<IntVec3>();
                Scribe_References.Look(ref caster, "caster");
                Scribe_Deep.Look(ref root, "root");
                Scribe_Values.Look(ref next, "next");
                Scribe_Values.Look(ref power, "power", 1f);
                Scribe_Values.Look(ref storm, "storm");
                Scribe_Values.Look(ref wallTo, "wallTo", IntVec3.Invalid);
                Scribe_TargetInfo.Look(ref target, "target");
                Scribe_Values.Look(ref at, "at");
                Scribe_Values.Look(ref waitCaster, "waitCaster");
                Scribe_Collections.Look(ref waitFor, "waitFor", LookMode.Reference);
                Scribe_Values.Look(ref giveUp, "giveUp");
                Scribe_Values.Look(ref endByMage, "endByMage");
                Scribe_Values.Look(ref origin, "origin", IntVec3.Invalid);
                if (Scribe.mode == LoadSaveMode.PostLoadInit && root != null) { links = Order(root); combos = Combos.Pairs(root); }
            }
        }

        private List<Chain> chains = new List<Chain>();

        /// <summary>A chain broken off by a save goes on after the load, from the circle it had reached.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref chains, "chains", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (chains == null) chains = new List<Chain>();
                chains.RemoveAll(c => c == null || c.root == null || c.links == null);
                foreach (Chain c in chains) c.waitFor?.RemoveAll(p => p == null);
            }
        }

        public MapComponent_SpellChain(Map map) : base(map) { }

        /// <summary>Darkness and Space turn the spell the other way: its chain runs counter-clockwise. (So will a
        /// distorted path, once it is made.)</summary>
        public static bool Widdershins(SpellCircle root) =>
            root.All().Any(c => c.element != null && (c.element.kind == RuneKind.Darkness || c.element.kind == RuneKind.Space));

        /// <summary>The circles that act, in the order they act.</summary>
        public static List<SpellCircle> Order(SpellCircle root)
        {
            if (!root.binding) return WeaveSpell.Small(root);
            bool back = Widdershins(root);
            var list = Turn(root.inner, back).Where(c => !c.Great).ToList();
            foreach (SpellCircle g in Turn(root.inner, back).Where(c => c.Great)) list.AddRange(Turn(g.inner, back).Where(c => !c.Great));
            return list.Where(c => c.element != null).ToList();
        }

        /// <summary>The circles of a ring from the top: clockwise as they stand, or counter-clockwise (the top one
        /// still first, then the one to its left).</summary>
        private static IEnumerable<SpellCircle> Turn(List<SpellCircle> ring, bool back)
        {
            if (!back || ring.Count < 2) return ring;
            return new[] { ring[0] }.Concat(Enumerable.Range(1, ring.Count - 1).Reverse().Select(i => ring[i]));
        }

        public static void Start(Pawn caster, SpellCircle root, LocalTargetInfo target, float power)
        {
            var c = new Chain
            {
                caster = caster, root = root, links = Order(root), power = power, target = target,
                storm = false, wallTo = FireMagic.WallTo, combos = Combos.Pairs(root), origin = caster.Position,
                hops = SpellHops.Take(caster)
            };
            // the map is taken before the first circle: a step may carry the mage off it (under the ground, in the air)
            Map map = caster.Map;
            // the first circle at once - a spell of one circle is over in this very moment
            Run(c);
            if (c.next < c.links.Count || Waiting(c)) map?.GetComponent<MapComponent_SpellChain>()?.chains.Add(c);
            else Finish(c);
        }

        private static bool Waiting(Chain c) => c.waitCaster || c.waitFor != null;

        private static void Run(Chain c)
        {
            if (c.next >= c.links.Count) return;
            SpellCircle circle = c.links[c.next++];
            Pawn caster = c.caster;
            if (caster == null || caster.Dead || !caster.Spawned) return;
            float p = c.power * WeaveSpell.ResonanceFactor(WeaveSpell.Resonance(c.root, circle.element)) * Combos.FocusBoost(circle, caster.Map);
            RitualSchema one = circle.AsSchema();
            // two Barriers in the outermost section: the blow is shaped round the caster's own people (weaker for it)
            bool shaped = SpellSculpt.Take(one, out bool aid);   // with a Thread: Selective Aid - to the caster's own only
            // Reach and Thread: what is outward of the pair leaps on to the next place named
            List<RitualSchema> leaps = SpellHops.Cut(ref one);
            // outward of a step: cut off, to act where the caster comes out
            RitualSchema after = SpellDash.Split(ref one);
            if (shaped && after != null) SpellSculpt.Mark(after, aid);
            float pHere = shaped ? p * SpellSculpt.Keeps : p;
            RitualReading r = RitualEvaluator.Read(one);
            SpellDash.Last = null;
            SpellDash.ReturnTo = c.origin;
            AirMagic.Storm = c.storm;
            FireMagic.WallTo = c.wallTo;
            SpellDash.LastPulled = null;
            ComboKind met = c.combos != null && c.combos.TryGetValue(circle, out ComboKind mk) ? mk : ComboKind.None;
            object keepSpell = SpellAura.Spell;
            SpellAura.Spell = c.root;
            using (SpellWards.Of(circle.element?.kind, met, true))
            using (shaped ? SpellSculpt.Spare(aid) : null)
            try
            {
                // a spell landing inside a dome meets it first (Магия-правила.md, part 1)
                if (SpellWards.Strikes(one) && SpellWards.DomeTakes(caster.Map, c.target.Cell, circle.element, met, ref pHere)) { after = null; }
                // the barriers in their turn; two elements that met work as what they made
                else SpellWards.InOrder(caster, one, met, circle.element, c.target, pHere, part => SpellDash.CastPart(caster, part, met, circle.element, c.target, pHere));
            }
            finally { AirMagic.Storm = false; FireMagic.WallTo = IntVec3.Invalid; SpellDash.ReturnTo = IntVec3.Invalid; SpellAura.Spell = keepSpell; }
            SpellDash.Hang(after, met, caster, c.target, p);
            if (leaps != null)
                foreach (RitualSchema leap in leaps)
                {
                    if (caster.Dead || !caster.Spawned) break;
                    IntVec3 from = c.target.Cell;
                    IntVec3 to = c.hopNext < c.hops.Count ? c.hops[c.hopNext++] : from;
                    if (!to.InBounds(caster.Map)) to = from;
                    Leap(caster.Map, from, to, circle.element?.color ?? Color.white);
                    if (shaped) SpellSculpt.Mark(leap, aid);
                    c.target = new LocalTargetInfo(to);
                    SpellDash.Last = null;
                    SpellDash.Hang(leap, met, caster, c.target, p);
                }
            // where this circle ends, the next one strikes
            bool step = r.verbs.Any(v => v.kind == RuneKind.Step);
            bool aura = r.verbs.Any(v => v.kind == RuneKind.Aura);
            int now = Find.TickManager.TicksGame;
            bool pulls = SpellMods.Pulls(one);
            c.waitCaster = step && !pulls && !caster.Spawned;
            c.waitFor = step && pulls && SpellDash.LastPulled != null && SpellDash.LastPulled.Count > 0 ? SpellDash.LastPulled : null;
            c.endByMage = aura || (step && pulls);
            if (!c.waitCaster && !c.endByMage && !step) c.target = new LocalTargetInfo(c.target.Cell);
            if (!c.waitCaster && c.endByMage) c.target = new LocalTargetInfo(caster.Position);
            c.giveUp = now + Patience;
            c.at = now + Gap;
            SpellDash.LastPulled = null;
        }

        /// <summary>The spell leaping on from one place to the next: a trail of light along the way.</summary>
        private static void Leap(Map map, IntVec3 from, IntVec3 to, Color col)
        {
            if (map == null || from == to) return;
            Vector3 a = from.ToVector3Shifted(), b = to.ToVector3Shifted();
            int n = Mathf.Clamp(Mathf.RoundToInt((b - a).magnitude), 2, 30);
            for (int i = 0; i <= n; i++) FleckMaker.ThrowLightningGlow(Vector3.Lerp(a, b, i / (float)n), map, 0.7f);
            FleckMaker.Static(to, map, FleckDefOf.PsycastAreaEffect, 1.6f);
        }

        private static void Finish(Chain c)
        {
            if (c.caster == null || c.caster.Dead || !c.caster.Spawned) return;
            WeaveSpell.AfterCircles(c.caster, c.root, c.target, c.power, c.caster.Map);
        }

        /// <summary>While a chain runs, everyone sees where its next circle will strike - time to raise a ward or run.</summary>
        public override void MapComponentUpdate()
        {
            if (chains.Count == 0 || Find.CurrentMap != map) return;
            foreach (Chain c in chains)
            {
                if (c.next >= c.links.Count || c.caster == null || c.caster.Dead) continue;
                IntVec3 mageAt = c.caster.Spawned ? c.caster.Position : c.target.Cell;
                IntVec3 at = c.waitCaster || c.endByMage ? mageAt : c.target.Cell;
                if (!at.InBounds(map)) continue;
                float widest = 0f;
                Color col = c.links[c.next].element?.color ?? Color.white;
                var cells = SpellPreview.LinkCells(c.caster, c.links[c.next].AsSchema(), at, mageAt, map, col, ref widest);
                if (cells.Count > 0) GenDraw.DrawFieldEdges(cells.Where(x => x.InBounds(map)).Distinct().ToList(), col);
            }
        }

        public override void MapComponentTick()
        {
            if (chains.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = chains.Count - 1; i >= 0; i--)
            {
                Chain c = chains[i];
                if (c.caster == null || c.caster.Dead) { chains.RemoveAt(i); continue; }
                if (Waiting(c))
                {
                    bool done = now >= c.giveUp
                                || (c.waitCaster && c.caster.Spawned)
                                || (c.waitFor != null && c.waitFor.All(p => p.Dead || p.Spawned));
                    if (!done) continue;
                    if (c.waitCaster) c.target = new LocalTargetInfo(c.caster.Position);
                    else if (c.endByMage) c.target = new LocalTargetInfo(c.caster.Position);
                    c.waitCaster = false; c.waitFor = null;
                    c.at = now + Gap;
                    continue;
                }
                if (now < c.at || !c.caster.Spawned || c.caster.Map != map) continue;
                if (c.next >= c.links.Count) { chains.RemoveAt(i); Finish(c); continue; }
                Run(c);
            }
        }
    }
}

namespace Avir.Magic
{
    /// <summary>How a spell is cast once its target is chosen. Most spells are held standing: the mage stays put until
    /// the cast is done or broken off (a gizmo, while it lasts). Air is always on the move: a spell of air alone is
    /// cast while the mage walks - the circle travels with them.</summary>
    public static class SpellCasting
    {
        /// <summary>A spell of air alone (every element in it is air).</summary>
        public static bool OfAir(RitualSchema s)
        {
            if (s == null) return false;
            if (s.weave == null) { var els = RitualEvaluator.Read(s).elements; return els.Count > 0 && els.All(e => e.kind == RuneKind.Air); }
            // air alone, or air in its meetings (storm, haze, dust): every circle of the spell is of the air
            var pairs = Combos.Pairs(s.weave);
            var circles = MapComponent_SpellChain.Order(s.weave);
            return circles.Count > 0 && circles.All(c => c.element.kind == RuneKind.Air || (pairs.TryGetValue(c, out ComboKind k) && Combos.WithAir(k)));
        }

        public static void Order(Pawn mage, RitualSchema spell, int slot, LocalTargetInfo target, IntVec3 wallEnd)
        {
            // a step that has nowhere to land is not begun at all: no Arcana is spent on it
            string why = StepBlocked(mage, spell, target);
            if (why != null) { Messages.Message("Cannot cast " + spell.name + ": " + why, mage, MessageTypeDefOf.RejectInput, false); return; }
            if (OfAir(spell))
            {
                Hediff_ArcaneCore core = Mages.Core(mage);
                if (core == null) return;
                MapComponent_AirCasts.Begin(mage, spell.Copy(), slot, target, wallEnd, Spells.CastTicks(core, spell, mage.Position.DistanceTo(target.Cell)));
                return;
            }
            Verse.AI.Job j = Verse.JobMaker.MakeJob(MagicSpellDefOf.Avir_CastSpell, target);
            JobDriver_CastSpell.Pending[j] = new KeyValuePair<RitualSchema, int>(spell.Copy(), slot);
            if (wallEnd.IsValid) JobDriver_CastSpell.WallEnds[j] = wallEnd;
            if (!mage.jobs.TryTakeOrderedJob(j, Verse.AI.JobTag.Misc)) { JobDriver_CastSpell.Pending.Remove(j); JobDriver_CastSpell.WallEnds.Remove(j); }
        }

        /// <summary>Why a spell that opens with a step cannot go where it is aimed (null - it can). Only the first circle is
        /// looked at: the later ones strike where the one before them ended, which is not known before the cast.</summary>
        public static string StepBlocked(Pawn mage, RitualSchema s, LocalTargetInfo target)
        {
            if (mage?.Map == null || s == null) return null;
            RitualSchema first = s;
            if (s.weave != null)
            {
                SpellCircle c = MapComponent_SpellChain.Order(s.weave).FirstOrDefault();
                if (c == null) return null;
                first = c.AsSchema();
            }
            RitualReading r = RitualEvaluator.Read(first);
            if (!r.verbs.Any(v => v.kind == RuneKind.Step)) return null;
            Map map = mage.Map;
            IntVec3 at = target.Cell;
            if (SpellMods.Pulls(first))
                return target.Thing is Pawn || MagicFx.PawnsIn(FireMagic.Point(at, map, 2.5f), map).Any(p => p != mage) ? null : "there is no one there to draw in.";
            if (!at.InBounds(map)) return "the place is off the map.";
            if (at == mage.Position) return "the caster is already there.";
            if (!at.Standable(map)) return "there is no standing room there (a wall, rock or deep water).";
            if (at.Fogged(map)) return "the place is unexplored.";
            return null;
        }

        /// <summary>Is this mage holding a cast right now (standing, or air on the move)?</summary>
        public static bool Busy(Pawn p) => p.CurJobDef == MagicSpellDefOf.Avir_CastSpell || MapComponent_AirCasts.Of(p) != null;

        /// <summary>Break off the cast: the spell is lost, and half of its Arcana with it.</summary>
        public static void BreakOff(Pawn p)
        {
            RitualSchema s = (p.jobs?.curDriver as JobDriver_CastSpell)?.spell ?? MapComponent_AirCasts.Of(p)?.spell;
            Hediff_ArcaneCore core = Mages.Core(p);
            if (s != null && core != null) core.Add(-Spells.Cost(s) * 0.5f);
            if (p.CurJobDef == MagicSpellDefOf.Avir_CastSpell) p.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
            MapComponent_AirCasts.Cancel(p);
            Messages.Message(p.LabelShortCap + " lets the spell go - half its Arcana is spent for nothing.", p, MessageTypeDefOf.SilentInput, false);
        }
    }

    /// <summary>Spells of air, cast on the move: the mage walks, fights, does what they will - the circle goes with them
    /// and the spell is loosed when it is ready, at the target it was aimed at.</summary>
    public class MapComponent_AirCasts : MapComponent
    {
        public class Cast : IExposable
        {
            public Pawn mage; public RitualSchema spell; public int slot; public LocalTargetInfo target; public IntVec3 wallEnd = IntVec3.Invalid;
            public int started, duration;
            public float Progress => Mathf.Clamp01((Find.TickManager.TicksGame - started) / (float)Mathf.Max(1, duration));
            public void ExposeData()
            {
                Scribe_References.Look(ref mage, "caster"); Scribe_Deep.Look(ref spell, "spell"); Scribe_Values.Look(ref slot, "slot");
                Scribe_TargetInfo.Look(ref target, "target"); Scribe_Values.Look(ref wallEnd, "wallEnd", IntVec3.Invalid);
                Scribe_Values.Look(ref started, "started"); Scribe_Values.Look(ref duration, "duration");
            }
        }

        public List<Cast> casts = new List<Cast>();

        public MapComponent_AirCasts(Map map) : base(map) { }

        public static Cast Of(Pawn p) => p?.Map?.GetComponent<MapComponent_AirCasts>()?.casts.FirstOrDefault(c => c.mage == p);

        public static void Begin(Pawn mage, RitualSchema spell, int slot, LocalTargetInfo target, IntVec3 wallEnd, int duration)
        {
            var comp = mage.Map?.GetComponent<MapComponent_AirCasts>();
            if (comp == null) return;
            comp.casts.RemoveAll(c => c.mage == mage);
            comp.casts.Add(new Cast { mage = mage, spell = spell, slot = slot, target = target, wallEnd = wallEnd, started = Find.TickManager.TicksGame, duration = duration });
        }

        public static void Cancel(Pawn p) => p?.Map?.GetComponent<MapComponent_AirCasts>()?.casts.RemoveAll(c => c.mage == p);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref casts, "casts", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { if (casts == null) casts = new List<Cast>(); casts.RemoveAll(c => c?.mage == null || c.spell == null); }
        }

        public override void MapComponentTick()
        {
            if (casts.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = casts.Count - 1; i >= 0; i--)
            {
                Cast c = casts[i];
                if (c.mage == null || c.mage.Dead || c.mage.Downed || !c.mage.Spawned || c.mage.Map != map || c.mage.InMentalState) { casts.RemoveAt(i); continue; }
                if (now - c.started < c.duration) continue;
                casts.RemoveAt(i);
                Hediff_ArcaneCore core = Mages.Core(c.mage);
                if (core == null) continue;
                Spells.Resolve(c.mage, core, c.spell, c.target, c.wallEnd);
                if (c.slot >= 0 && c.slot < 5) core.Book.cooldown[c.slot] = now + Spells.Cooldown(c.spell);
            }
        }

        public override void MapComponentUpdate()
        {
            if (casts.Count == 0 || Find.CurrentMap != map) return;
            foreach (Cast c in casts)
            {
                if (c.mage == null || !c.mage.Spawned) continue;
                SpellSigil.Draw(c.mage.DrawPos, c.spell, 0.4f + 0.6f * c.Progress, 1.6f + 0.6f * (int)c.spell.size);
                if (c.target.IsValid) SpellPreview.Draw(c.mage, c.spell, c.wallEnd.IsValid ? new LocalTargetInfo(c.wallEnd) : c.target, false);
            }
        }
    }
}

namespace Avir.Magic
{
    /// <summary>The sections of a mage's book: by what a spell does, by its element, by who wrote it.</summary>
    public static class SpellSections
    {
        private static readonly string[] Effects = { "Strike", "Control", "Movement", "Defence", "Healing", "Weapons", "Senses", "Work and land", "Unfinished" };

        public static string Of(RitualSchema s, int by, Pawn mage)
        {
            if (by == 1) return Element(s);
            if (by == 2) return Author(s, mage);
            return Effect(s);
        }

        public static int Order(string key, int by)
        {
            if (by == 0) { int i = System.Array.IndexOf(Effects, key); return i < 0 ? 99 : i; }
            if (by == 2) return key == "From the path" ? 0 : key.StartsWith("Written by") ? 1 : 3;
            return key == "Mixed" ? 98 : key == "No element" ? 99 : 0;
        }

        private static List<RuneDef> Runes(RitualSchema s) =>
            s.weave != null ? s.weave.Working.SelectMany(c => c.OwnRunes).ToList() : s.AllRunes.ToList();

        public static string Effect(RitualSchema s)
        {
            var kinds = Runes(s).Select(r => r.kind).ToList();
            bool Has(RuneKind k) => kinds.Contains(k);
            if (Has(RuneKind.Step)) return "Movement";
            if (Has(RuneKind.Barrier)) return "Defence";
            if (Has(RuneKind.Restore)) return "Healing";
            if (Has(RuneKind.Enchant)) return "Weapons";
            if (Has(RuneKind.Sense)) return "Senses";
            if (Has(RuneKind.Release) || Has(RuneKind.Lift) || Has(RuneKind.Beam) || Has(RuneKind.Drain)) return "Strike";
            if (Has(RuneKind.Hold) || Has(RuneKind.Shape)) return "Control";
            if (Has(RuneKind.Growth) || Has(RuneKind.Purge)) return "Work and land";
            return "Unfinished";
        }

        public static string Element(RitualSchema s)
        {
            if (s.weave == null) { RuneDef e = Spells.Element(s); return e != null ? e.LabelCap.ToString() : "No element"; }
            var pairs = Combos.Pairs(s.weave);
            var names = MapComponent_SpellChain.Order(s.weave).Select(c => pairs.TryGetValue(c, out ComboKind k) ? Combos.Label(k).CapitalizeFirst() : c.element.LabelCap.ToString()).Distinct().ToList();
            return names.Count == 0 ? "No element" : names.Count == 1 ? names[0] : "Mixed";
        }

        public static string Author(RitualSchema s, Pawn mage)
        {
            if (s.author == "path" || (s.author.NullOrEmpty() && MagePath.Made.Any(el => MagePath.Examples(el).Any(x => x.name == s.name)))) return "From the path";
            if (s.author.NullOrEmpty()) return "Not known";
            return "Written by " + s.author;
        }
    }
}
