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
        public bool termed;   // the plaques already named by the almanac (SpellTerms)
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
                d += "\nCast - " + Caster.LabelShortCap + ", " + (c.Sorcerer ? "sorcerer" : "caster") + ": " + sec(Spells.CastTicks(c, s))
                   + " · hold " + Spells.HoldChance(Caster, s).ToStringPercent();
            else d += "\nCast: sorcerer " + sec(Spells.CastTicks(true, s)) + " · caster " + sec(Spells.CastTicks(false, s));
            return d;
        }

        public static SpellEffect ExplainSchema(RitualSchema s) => ExplainCore(s);

        /// <summary>Two or more forms or verbs in one circle: in what order they act. Forms go one after another, as
        /// their runes are written from the centre outward; each carries every verb of the circle, in the same order.</summary>
        public static string Turns(RitualSchema s)
        {
            // a step cuts the circle: what is outward of it acts where the caster comes out
            RitualSchema head = s.Copy();
            RitualSchema tail = SpellDash.Split(ref head);
            if (tail != null)
            {
                string now = Turns(head), later = Turns(tail);
                var tr = RitualEvaluator.Read(tail);
                string acts = string.Join(", then ", tr.verbs.Select(x => x.label));
                return (now != null ? now + "\n" : "") + "Outward of the step - where the caster comes out (or where the one drawn in arrives): " + acts + "."
                    + (tr.verbs.Any(v => v.kind == RuneKind.Shape) ? " A shaping there rises as a ring round the caster." : "")
                    + (tr.verbs.Any(v => v.kind == RuneKind.Step) ? " A second step takes the caster back where they cast from." : "");
            }
            RitualReading r = RitualEvaluator.Read(s);
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            if (forms.Count + verbs.Count < 2) return null;
            string list(List<RuneDef> l) => string.Join(", then ", l.Select(x => x.label));
            if (forms.Count == 0) return "At the target, in turn: " + list(verbs) + ".";
            if (verbs.Count == 0) return "In turn: " + list(forms) + ".";
            return (forms.Count > 1 ? "The forms in turn: " + list(forms) + "; each carries" : forms[0].LabelCap + " carries") + " the verbs in turn: " + list(verbs) + "."
                + (forms.Any(f => f.kind == RuneKind.Step) ? " (A step: its verbs strike where the caster comes out.)" : "");
        }

        /// <summary>The reading is heavy (the chain, what met, where each modifier sits) and lists and the codex ask
        /// for it every frame: it is kept half a second per spell, and each caller gets its own copy to write on.</summary>
        private static readonly Dictionary<RitualSchema, KeyValuePair<float, SpellEffect>> read = new Dictionary<RitualSchema, KeyValuePair<float, SpellEffect>>();

        public static SpellEffect Explain(RitualSchema s)
        {
            float now = Time.realtimeSinceStartup;
            if (!read.TryGetValue(s, out var kv) || now - kv.Key > 0.5f)
            {
                // two Barriers on a circle's outer edge shape its blow: read without them, and say so
                RitualSchema rs = SpellSculpt.Stripped(s, out int nShaped, out int nPlain);
                SpellEffect e0 = rs.weave != null ? WeaveSpell.Explain(rs.weave) : ExplainCore(rs);
                if (nShaped > 0) e0.Add(nPlain > 0 ? "Spares its own - in part" : "Spares its own", "Two Barriers in the outermost section of a circle shape its blow round the caster's own people: it strikes foes only, "
                    + "and is " + (1f - SpellSculpt.Keeps).ToStringPercent() + " weaker for it. Each circle needs its own pair" + (nPlain > 0 ? " - here " + nPlain + " circle(s) without it strike everyone." : ".")
                    + " What is left on the ground (magma, mire, held fields) does not know its own.");
                if (s.weave != null)
                {
                    string wear = string.Join("\n", s.weave.Working.Select(k => SpellWear.Explain(k)).Where(x => x != null));
                    if (wear.Length > 0) e0.Add("Wear of repeats", "The same verb in one circle wears out: the second acts at half, the third at a quarter; past that, one more at a quarter for every section beyond the first, the rest do nothing (and still cost Arcana). Spread different verbs over the sections instead.\n" + wear);
                }
                string thread = SpellTerms.ThreadHere(s);
                if (thread != null) e0.Add("The Thread", thread);
                if (nShaped == 0 && s.weave != null && s.weave.Working.Any(k => k.OwnRunes.Any(x => x.role == RuneRole.Verb || x.role == RuneRole.Form)))
                    e0.Add("Strikes everyone", "Without two Barriers in the outermost section of a circle, its blow strikes whoever stands in it - friend or foe; only a ward keeps them.");
                e0.details = Details(s);
                if (SpellCasting.OfAir(s)) e0.Add("Momentum", "A spell of the air: cast on the move. Cast another of the air within " + AvirMod.Settings.airMomentumSeconds.ToString("0") + " seconds and it comes "
                    + AvirMod.Settings.airMomentumStep.ToStringPercent() + " quicker and cheaper - up to " + AirMomentum.MaxStage + " stages; a pause lets the wind drop.");
                if (s.weave != null)
                    foreach (ComboKind ck in Combos.Pairs(s.weave).Values.Distinct())
                    {
                        string inh = ck == ComboKind.Steam ? "From water: the wet ground round the target feeds it (+4% a wet cell, up to +40%), and the pools there boil off as scalding clouds. From fire: a lost hold scalds everyone round."
                            : ck == ComboKind.Storm ? "From water: wet ground feeds it, and on it the bolt leaps further. From air: cast on the move, momentum, many targets. A lost hold: bolts strike at random round the caster."
                            : ck == ComboKind.Magma ? "From earth: it takes up the magma and basalt at hand - stronger, cheaper - and casts slowly. From fire: a lost hold opens lava all round."
                            : ck == ComboKind.Haze ? "From air: cast on the move, momentum; every mirage of the caster near the target lets the verbs out too, at half strength. From fire: a lost hold dazzles everyone round."
                            : ck == ComboKind.Bog ? "From earth: it takes up the mire and pools at hand - stronger, cheaper - and casts slowly. From water: it lies long. A lost hold: a masterless mire round the caster."
                            : "From earth: it lifts the rubble, chunks and shards at hand - stronger, cheaper. From air: cast on the move, momentum. A lost hold: a dust storm round the caster.";
                        e0.Add(Combos.Label(ck).CapitalizeFirst() + ": inherited", inh);
                    }
                if (s.weave != null && s.weave.All().Any(Combos.Closed))
                    e0.Add("Closed", "A circle with the closing rune keeps its energy in and its element pure: it does not meet its neighbours, whatever they are - fire stays fire beside water. Its neighbours may still meet others.");
                bool focused = s.weave != null ? s.weave.All().Any(c => c.OwnRunes.Any(x => x.kind == RuneKind.Focus)) : RitualEvaluator.Read(s).focus > 0;
                if (focused) e0.Add("Focus", "The focus rune draws the map's stream into its circle: +20% strength for each, and +10% more for each tier of the stream - but the stream makes it harder to hold.");
                bool shaped = s.weave != null ? s.weave.All().Any(c => c.OwnRunes.Any(x => x.kind == RuneKind.Shape)) : RitualEvaluator.Read(s).verbs.Any(v => v.kind == RuneKind.Shape);
                if (shaped) e0.Add("Ring", "Shaping struck at the caster - after a step, from an aura, or cast at their own feet - raises the wall as a ring round them, three cells out (+1 for each Breadth): whoever is inside is shut in with the caster. Between two chosen points it stands as a line.");
                ComboKind? stone = EarthStone.KindOf(s);
                if (stone.HasValue) e0.Add("Stone at hand", "Earth's rule - stone remains. At its target the spell takes up the " + EarthStone.Ground(stone.Value) + " lying within three cells: +5% strength and 3% less Arcana for each, up to +50% and -30%. Loose stone is flung and gone; rock, basalt and walls stay and give half.");
                if ((s.weave != null && WeaveSpell.Heavy(s.weave)) || (s.weave == null && EarthMagic.Is(Spells.Element(s)))) e0.Add("Heavy", "Earth is heavy: this spell takes ×" + AvirMod.Settings.earthCastFactor.ToString("0.0#") + " as long to cast.");
                if (s.weave != null && Combos.Pairs(s.weave).ContainsValue(ComboKind.Storm)) e0.Add("Many targets", "A storm is not aimed at one: besides its target it strikes the 2 nearest foes round it (+1 for each Breadth), a quarter weaker - unless it is a step, a wall, an aura, an enchanting or a restoring.");
                if (read.Count > 300) read.Clear();
                kv = new KeyValuePair<float, SpellEffect>(now, e0);
                read[s] = kv;
            }
            var e = new SpellEffect { main = kv.Value.main, details = kv.Value.details };
            e.extras.AddRange(kv.Value.extras);
            return e;
        }

        private static SpellEffect ExplainCore(RitualSchema s)
        {
            SpellEffect e = ExplainPlain(s);
            string turns = Turns(s);
            if (turns != null) e.extras.Insert(0, new KeyValuePair<string, string>("In turn", turns));
            List<string> by = SpellMods.Bindings(s);
            if (by.Count > 0) e.extras.Insert(0, new KeyValuePair<string, string>("By their place", string.Join("\n", by.Select(x => "• " + x))));
            return e;
        }

        private static SpellEffect ExplainPlain(RitualSchema s)
        {
            RitualReading r = RitualEvaluator.Read(s);
            RuneDef el = r.elements.FirstOrDefault();
            var e = new SpellEffect();
            if (el == null) { e.main = "No element yet: write one (fire, water, earth or air) - it is what the spell is made of."; return e; }
            if (SpellMods.Pulls(s) && (WaterMagic.Is(el) || EarthMagic.Is(el) || AirMagic.Is(el) || FireMagic.Is(el)))
            {
                SpellEffect pulled = WaterMagic.Is(el) ? Water(r) : EarthMagic.Is(el) ? Earth(r) : AirMagic.Is(el) ? Air(r) : Fire(r);
                pulled.main = Pulled(el.kind);
                pulled.extras.Insert(0, new KeyValuePair<string, string>("The step turned round", "The thread turns the step: not the caster goes, but the one at the target is carried to the caster. A friend comes unharmed; a foe was not ready for it. Whoever the earth roots cannot be moved."));
                return pulled;
            }
            if (WaterMagic.Is(el)) return Water(r);
            if (EarthMagic.Is(el)) return Earth(r);
            if (AirMagic.Is(el)) return Air(r);
            if (!FireMagic.Is(el)) { e.main = el.LabelCap + " is not made yet - for now it only strikes a plain pulse at the point."; return e; }
            return Fire(r);
        }

        /// <summary>A drain feeds the verbs written after it (outward of it) at once; as the last verb it is kept.</summary>
        private static string DrainNote(List<RuneDef> verbs, string what, string el) =>
            MagicFx.Later(verbs, verbs.First(v => v.kind == RuneKind.Drain))
                ? "The " + what + " taken goes at once into the verbs written after the drain - they strike harder."
                : "The " + what + " taken is kept by the caster: their next " + el + " spell is stronger. (Write a verb after the drain, and it is fed into that verb at once.)";

        private static string Pulled(RuneKind el)
        {
            switch (el)
            {
                case RuneKind.Fire: return "Draws one at the target to the caster through flame: fire bursts where they stood and burns them, but at the caster's end it is only light - they land at the caster's feet, knocked down.";
                case RuneKind.Water: return "An undertow: every foe at the target is swept to the caster - soaked, struck and knocked off their feet when the water sets them down.";
                case RuneKind.Air: return "The wind snatches one at the target and carries them to the caster over anything in the way: they land reeling - slow, clumsy and shooting wild for a while.";
                case RuneKind.Earth: return "The ground swallows one at the target - anywhere the caster can reach - and throws them up beside the caster, stunned senseless for several seconds.";
                default: return "";
            }
        }

        private static SpellEffect Fire(RitualReading r)
        {
            var e = new SpellEffect();
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            RuneDef form = forms.FirstOrDefault();
            float radius = FireMagic.PointRadius(r);
            string what = Verbs(verbs, Verb);
            switch (form?.kind)
            {
                case RuneKind.Restore: e.main = "Cauterizes: bleeding wounds of friends at the target are seared shut, as if tended."; break;
                case RuneKind.Enchant: e.main = "Sets a friend's weapon burning (the caster's, if no friend stands there): blows add flame, shots may set foes alight."; break;
                case RuneKind.Summon: e.main = "Spirits are not made yet."; break;
                case RuneKind.Sense: e.main = "The caster feels every warm body within " + (25 + 10 * r.reach) + " cells - through walls and darkness - for a while."; break;
                case RuneKind.Step: e.main = "The caster leaps to the target; along the way the fire " + (verbs.Count > 0 ? what : "stays held on the trail") + "."; break;
                default:
                    if (verbs.Count == 0) { e.main = r.barrier > 0 ? "A dome of flame over the target: whoever stands inside is warded, spells landing in it are put out." : "Fire with nothing to do: add a verb (release, hold, drain, lift, growth, purge) or a form."; break; }
                    string where = form?.kind == RuneKind.Aura ? "all around the caster (radius " + (radius + 1.5f).ToString("0.#") + ")"
                        : form?.kind == RuneKind.Beam ? "along a line to the target - a wall stops it"
                        : form?.kind == RuneKind.Shape ? "as a wall up to " + FireMagic.WallMax(r) + " cells long - its two ends chosen with two clicks"
                        : "at the chosen point (radius " + radius.ToString("0.#") + ")";
                    e.main = Cap(what) + " - " + where + ".";
                    break;
            }
            if (forms.Count > 1) e.Add("Several forms", "Each form works on its own: the spell happens once for each.");
            Repeats(e, verbs);
            if (r.barrier > 0) e.Add("Ward of flame", SpellWards.Note("flame", true, null, "Flames on the body go out.", r.amplify));
            if (r.amplify > 0 && verbs.Any(v => v.kind == RuneKind.Release)) e.Add("Explosion", "Amplified release bursts: a flame explosion of radius " + (1.9f + r.amplify).ToString("0.#") + ".");
            else if (r.amplify > 0) e.Add("Stronger", "Amplification: +" + (30 * r.amplify) + "% power.");
            if (verbs.Any(v => v.kind == RuneKind.Purge) && (form?.kind == RuneKind.Aura || r.anchor > 0))
                e.Add("Ground that takes no fire", "The purged ground will not take fire for some hours: flames that reach it die.");
            if (verbs.Any(v => v.kind == RuneKind.Hold)) e.Add("Warms friends", "Friends by the held fire bear the cold far better for a day.");
            if (verbs.Any(v => v.kind == RuneKind.Drain)) e.Add("Stores heat", DrainNote(verbs, "heat", "fire"));
            if (form?.kind == RuneKind.Restore && verbs.Any(v => v.kind == RuneKind.Purge)) e.Add("Burns infection", "Infection and parasites are burnt back.");
            if (form?.kind == RuneKind.Restore && verbs.Any(v => v.kind == RuneKind.Growth)) e.Add("Better cautery", "The seared wounds hold better.");
            if (r.breadth > 0) e.Add("Wider", "Breadth: a wider area or a broader line - and each one makes the cast 8% longer.");
            if (r.reach > 0) e.Add("Further", "Reach: the spell can be cast further.");
            e.Add("Price: raging flame", "If the hold slips, the fire around the caster breaks out of control for a while - fire keeps starting at their feet.");
            return e;
        }

        private static SpellEffect Water(RitualReading r)
        {
            var e = new SpellEffect();
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            RuneDef form = forms.FirstOrDefault();
            float radius = FireMagic.PointRadius(r);
            string what = Verbs(verbs, WaterVerb);
            switch (form?.kind)
            {
                case RuneKind.Restore: e.main = "Washes the wounds of friends at the target and binds them - the bleeding stops."; break;
                case RuneKind.Enchant: e.main = "Wraps a friend's weapon in water (the caster's, if no friend stands there): blows soak the foe, and every blow on a soaked one weakens them further."; break;
                case RuneKind.Summon: e.main = "Spirits are not made yet."; break;
                case RuneKind.Sense: e.main = "There is water in every living thing: the caster feels them all within " + (25 + 10 * r.reach) + " cells, through walls - the unseen too, a ripple where they stand."; break;
                case RuneKind.Step: e.main = "The caster rides a wave to the target: it strikes whoever stands in its way and carries them on - wider and harder with breadth" + (verbs.Count > 0 ? "; and the water " + what : "") + "."; break;
                case RuneKind.Shape: e.main = "The ground between two chosen points (up to " + FireMagic.WallMax(r) + " cells) turns to shallow water for a few hours: a moat - slow to cross, and fire will not burn there."; break;
                default:
                    if (verbs.Count == 0) { e.main = r.barrier > 0 ? "A dome of water over the target: whoever stands inside is warded, spells landing in it are put out." : "Water with nothing to do: add a verb or a form."; break; }
                    string where = form?.kind == RuneKind.Aura ? "all round the caster - and a mist stays round them for a while: foes in it are slowed and shoot poorly, fire does not start, and this happens again every few seconds"
                                 : form?.kind == RuneKind.Beam ? "in a jet to the target"
                                 : "at the point, " + radius.ToString("0.#") + " cells round";
                    e.main = Cap(what) + " - " + where + ".";
                    break;
            }
            Repeats(e, verbs);
            if (r.barrier > 0) e.Add("Ward of water", SpellWards.Note("water", false, "being soaked", "Every fire within three cells of the wearer goes out.", r.amplify));
            if (verbs.Any(v => v.kind == RuneKind.Release) && r.amplify > 0) e.Add("Further back", "Amplified, the burst throws foes further.");
            if (verbs.Any(v => v.kind == RuneKind.Drain)) e.Add("Keeps the water", DrainNote(verbs, "water", "water"));
            if (r.breadth > 0) e.Add("Wider", "Breadth: a wider area or a broader jet - and each one makes the cast 8% longer.");
            if (r.reach > 0) e.Add("Further", "Reach: the spell can be cast further.");
            e.Add("Price: chilled", "If the hold slips, the water soaks the caster instead - slower, and the cold bites hard for a few hours.");
            return e;
        }

        private static SpellEffect Earth(RitualReading r)
        {
            var e = new SpellEffect();
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            RuneDef form = forms.FirstOrDefault();
            float radius = FireMagic.PointRadius(r);
            string what = Verbs(verbs, EarthVerb);
            switch (form?.kind)
            {
                case RuneKind.Restore: e.main = "Binds the wounds of friends at the target in stone, like splints, and covers them in stone skin for an hour."; break;
                case RuneKind.Enchant: e.main = "Makes a friend's weapon heavy with stone (the caster's, if no friend stands there): blows hit harder and now and then knock the foe senseless."; break;
                case RuneKind.Summon: e.main = "Spirits are not made yet."; break;
                case RuneKind.Sense: e.main = "Every step is felt through the ground: the caster knows where everyone walks within " + (35 + 10 * r.reach) + " cells, even through walls and the unseen."; break;
                case RuneKind.Step: e.main = "The caster sinks into the ground and comes up anywhere on the map - only a crack of dust runs above. Where they come out the ground bursts: foes round about are thrown aside and stunned" + (verbs.Count > 0 ? "; and there, round the caster, the earth " + what : "") + "."; break;
                case RuneKind.Shape: e.main = "Stone walls rise between two chosen points (up to " + FireMagic.WallMax(r) + " cells): they stop feet and shots and crumble after a few hours" + (verbs.Any(v => v.kind != RuneKind.Hold) ? "; and along them the earth " + Verbs(verbs.Where(v => v.kind != RuneKind.Hold).ToList(), EarthVerb) : "") + "."; break;
                default:
                    if (verbs.Count == 0) { e.main = r.barrier > 0 ? "A dome of earth over the target: whoever stands inside is warded, spells landing in it are put out." : "Earth with nothing to do: add a verb or a form."; break; }
                    string where = form?.kind == RuneKind.Aura ? "all round the caster - and the ground stays theirs for a while: friends in it wear stone skin, foes wade as through mud, and this happens again every few seconds"
                                 : form?.kind == RuneKind.Beam ? "along a crack that runs to the target and throws down whoever stands on it"
                                 : "at the point, " + radius.ToString("0.#") + " cells round";
                    e.main = Cap(what) + " - " + where + ".";
                    break;
            }
            if (forms.Count > 1) e.Add("Several forms", "Each form works on its own: the spell happens once for each.");
            Repeats(e, verbs);
            if (r.barrier > 0) e.Add("Ward of earth", SpellWards.Note("earth", false, "being rooted", null, r.amplify));
            if (r.amplify > 0) e.Add("Heavier", "Amplification: the blows of stone are harder.");
            if (verbs.Any(v => v.kind == RuneKind.Drain)) e.Add("Keeps the weight", DrainNote(verbs, "weight", "earth"));
            if (form?.kind == RuneKind.Shape && (r.anchor > 0 || verbs.Any(v => v.kind == RuneKind.Hold))) e.Add("Stands longer", "Holding and anchors keep the stone up longer.");
            if (r.breadth > 0) e.Add("Wider", "Breadth: a wider area or a broader crack - and each one makes the cast 8% longer.");
            if (r.reach > 0) e.Add("Further", "Reach: the spell can be cast further.");
            if (verbs.Any(v => v.kind == RuneKind.Lift)) e.Add("Bodies thrown", "Whoever is thrown up hangs out of reach of any blow or shot, then falls: the legs take it first, bones may crack, a bad fall kills. Under a roof they strike the ceiling too. Big beasts are too heavy to lift - they are knocked down instead.");
            e.Add("Price: stone-bound", "If the hold slips, the stone takes the caster instead: they stand stunned for a moment and their limbs are stiff for a couple of hours.");
            return e;
        }

        private static string EarthVerb(RuneDef v)
        {
            switch (v.kind)
            {
                case RuneKind.Release: return "shakes, striking foes and throwing them to the ground";
                case RuneKind.Hold: return "grips the feet of foes: they cannot take a step, though their hands are still free";
                case RuneKind.Drain: return "pours weight into foes' limbs and keeps some for the caster";
                case RuneKind.Lift: return "bursts up in stone spikes that toss foes into the air";
                case RuneKind.Growth: return "grows rich, and the plants grow faster";
                case RuneKind.Purge: return "smothers fires and swallows filth";
                default: return v.label;
            }
        }

        private static SpellEffect Air(RitualReading r)
        {
            var e = new SpellEffect();
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            RuneDef form = forms.FirstOrDefault();
            float radius = FireMagic.PointRadius(r);
            string what = Verbs(verbs, AirVerb);
            switch (form?.kind)
            {
                case RuneKind.Restore: e.main = "A second wind for friends at the target: tiredness lifts, breath comes back, poison in the blood thins."; break;
                case RuneKind.Enchant: e.main = "Wraps a friend's weapon in wind (the caster's, if no friend stands there): every blow throws the foe back."; break;
                case RuneKind.Summon: e.main = "Spirits are not made yet."; break;
                case RuneKind.Sense: e.main = "The wind brings every sound: the caster hears everyone within " + (30 + 10 * r.reach) + " cells, through walls - the unseen too."; break;
                case RuneKind.Step: e.main = "The wind lifts the caster high and carries them to the target over anything in the way - walls, water, foes. They land on a gust that throws close foes aside" + (verbs.Count > 0 ? "; and there, round the caster, the air " + what : "") + "."; break;
                case RuneKind.Shape: e.main = "A wall of wind between two chosen points (up to " + FireMagic.WallMax(r) + " cells): foes who step into it are thrown back the way they came. It blows for a couple of hours."; break;
                default:
                    if (verbs.Count == 0)
                    {
                        if (form?.kind == RuneKind.Beam) { e.main = "A chained blow: the wind strikes the first foe on the line and leaps on to " + (1 + r.breadth + r.amplify) + " more nearby, throwing each one on. With water in the spell it is a storm, and the blow becomes lightning."; break; }
                        e.main = r.barrier > 0 ? "A dome of air over the target: whoever stands inside is warded, spells landing in it are put out." : "Air with nothing to do: add a verb or a form."; break;
                    }
                    string where = form?.kind == RuneKind.Aura ? "all round the caster - and a whirlwind stays round them for a while: foes in it are slowed and shoot poorly, and those who come close are thrown back"
                                 : form?.kind == RuneKind.Beam ? "along the line - and with it a chained blow that leaps from foe to foe (" + (2 + r.breadth + r.amplify) + " in all; lightning, in a storm with water)"
                                 : "at the point, " + radius.ToString("0.#") + " cells round";
                    e.main = Cap(what) + " - " + where + ".";
                    break;
            }
            if (forms.Count > 1) e.Add("Several forms", "Each form works on its own: the spell happens once for each.");
            Repeats(e, verbs);
            if (r.barrier > 0) e.Add("Ward of air", SpellWards.Note("air", false, "dizziness", "Whoever strikes the wearer in close combat is thrown back.", r.amplify));
            if (verbs.Any(v => v.kind == RuneKind.Release) && r.amplify > 0) e.Add("Further back", "Amplified, the gust throws foes further.");
            if (verbs.Any(v => v.kind == RuneKind.Release)) e.Add("Feeds fire", "Fires the gust passes over flare up - careful near your own walls.");
            if (verbs.Any(v => v.kind == RuneKind.Drain)) e.Add("Keeps the breath", DrainNote(verbs, "breath", "air"));
            if (r.breadth > 0) e.Add("Wider", "Breadth: a wider area, a broader line, more jumps of lightning - and each one makes the cast 8% longer.");
            if (r.reach > 0) e.Add("Further", "Reach: the spell can be cast further.");
            if (verbs.Any(v => v.kind == RuneKind.Lift)) e.Add("The fall", "Whoever is thrown up hangs out of reach of any blow or shot, then falls: the legs take it first, bones may crack, a bad fall kills. Under a roof they strike the ceiling too.");
            e.Add("Price: winded", "If the hold slips, the wind tears the breath out of the caster: a moment stunned, and an hour short of breath.");
            return e;
        }

        private static string AirVerb(RuneDef v)
        {
            switch (v.kind)
            {
                case RuneKind.Release: return "blows in a gust that throws foes back";
                case RuneKind.Hold: return "stands still and thin: foes can hardly breathe and go slowly through it, fires starve";
                case RuneKind.Drain: return "is pulled out of foes' lungs and kept by the caster";
                case RuneKind.Lift: return "rises in an updraft that throws foes high - the fall breaks bones";
                case RuneKind.Growth: return "spreads wide, pushing foes further round";
                case RuneKind.Purge: return "clears the air: small flames blown out, every gas (smoke, toxic gas) blown away, and friends breathe out what gas they took in";
                default: return v.label;
            }
        }

        private static string WaterVerb(RuneDef v)
        {
            switch (v.kind)
            {
                case RuneKind.Release: return "bursts, striking foes, throwing them back and soaking them";
                case RuneKind.Hold: return "lies there as standing water that slows foes and keeps fire away";
                case RuneKind.Drain: return "draws the water out of foes and leaves them weak";
                case RuneKind.Lift: return "rises as a geyser that throws foes up - and they fall hard";
                case RuneKind.Growth: return "spreads, and the plants drink";
                case RuneKind.Purge: return "washes away fire and filth, and anaesthetic and poisons out of friends";
                default: return v.label;
            }
        }

        private static string Verb(RuneDef v)
        {
            switch (v.kind)
            {
                case RuneKind.Release: return "sets fire and burns foes";
                case RuneKind.Hold: return "lays held fire that never spreads: it burns foes crossing it and bites at their legs (they go slowly), and smoulders";
                case RuneKind.Drain: return "takes the heat out of fires";
                case RuneKind.Lift: return "raises tall flames";
                case RuneKind.Growth: return "spreads fire further and faster";
                case RuneKind.Purge: return "puts fire out and burns away infection";
                default: return v.label;
            }
        }

        /// <summary>The verbs in words, each said once: a verb written several times is named once, with how many.</summary>
        private static string Verbs(List<RuneDef> verbs, System.Func<RuneDef, string> say)
        {
            var parts = new List<string>();
            foreach (var g in verbs.GroupBy(v => v.kind))
                parts.Add(say(g.First()) + (g.Count() > 1 ? " (x" + g.Count() + ")" : ""));
            return string.Join(", then ", parts);
        }

        /// <summary>A plaque for each verb written more than once: it strikes again, each time.</summary>
        private static void Repeats(SpellEffect e, List<RuneDef> verbs)
        {
            foreach (var g in verbs.GroupBy(v => v.kind).Where(g => g.Count() > 1))
                e.Add(g.First().LabelCap + " x" + g.Count(), "Written " + g.Count() + " times, it happens " + g.Count() + " times in turn.");
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
            SpellTerms.Apply(e);
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

        /// <summary>The plaque chosen by a click: its entry is read in the codex beside, not in a floating note.</summary>
        public static string Selected;

        /// <summary>The entry of the chosen plaque in this reading, if one is chosen.</summary>
        public static KeyValuePair<string, string>? Entry(SpellEffect e)
        {
            if (e == null) return null;
            SpellTerms.Apply(e);
            foreach (var c in e.extras) if (c.Key == Selected) return c;
            return null;
        }

        public static void Draw(Rect r, SpellEffect e, Color col, string title = "THE MAIN EFFECT")
        {
            SpellTerms.Apply(e);
            Color keep = GUI.color;
            if (RitualArt.HasCard) RitualArt.CardAt(r);
            else { Widgets.DrawBoxSolid(r, new Color(0.95f, 0.88f, 0.7f, 0.9f)); }
            Rect inner = r.ContractedBy(14f);
            GUI.color = new Color(col.r * 0.7f, col.g * 0.5f, col.b * 0.4f, 1f);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y - 4f, inner.width, 24f), title);
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
                bool on = Selected == c.Key;
                Widgets.DrawBoxSolid(chip, price ? new Color(0.55f, 0.15f, 0.08f, 0.92f) : new Color(col.r * 0.32f, col.g * 0.32f, col.b * 0.32f, 0.92f));
                if (on) { GUI.color = new Color(1f, 0.85f, 0.45f, 1f); Widgets.DrawBox(chip, 2); }
                else if (Mouse.IsOver(chip)) { GUI.color = new Color(1f, 0.95f, 0.8f, 0.6f); Widgets.DrawBox(chip, 1); }
                GUI.color = Color.white; Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(chip, c.Key);
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(chip)) Selected = on ? null : c.Key;
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
            var s = new RitualSchema { name = name, size = RitualCircleSize.Small, author = "path" };
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

        /// <summary>Two circles side by side, their elements meeting: one spell for each of the six.</summary>
        public static List<RitualSchema> Meetings()
        {
            RitualSchema Two(string name, string a, string av, string b, string bv)
            {
                RitualSchema one = Make(name, new[] { a, av });
                RitualSchema two = Make(name, new[] { b, bv });
                var root = SpellCircle.Bound(one.weave);
                root.inner.Add(two.weave);
                return new RitualSchema { name = name, size = RitualCircleSize.Small, weave = root, author = "path" };
            }
            return new List<RitualSchema>
            {
                Two("Scalding cloud", "Fire", "Release", "Water", "Release"),
                Two("Thunderhead", "Water", "Hold", "Air", "Hold"),
                Two("Eruption", "Fire", "Release", "Earth", "Release"),
                Two("Shimmer", "Fire", "Hold", "Air", "Hold"),
                Two("Mire", "Water", "Hold", "Earth", "Hold"),
                Two("Sandblast", "Earth", "Release", "Air", "Release"),
            };
        }

        public static List<RitualSchema> Earth() => new List<RitualSchema>
        {
            Make("Tremor", new[] { "Earth", "Release" }, new[] { "Breadth" }),
            Make("Fissure", new[] { "Earth", "Release" }, new[] { "Beam" }),
            Make("Stone wall", new[] { "Earth", "Hold" }, new[] { "Shape" }),
            Make("Rooting", new[] { "Earth", "Hold" }),
            Make("Weight of stone", new[] { "Earth", "Drain" }),
            Make("Stone spikes", new[] { "Earth", "Lift" }),
            Make("Rich soil", new[] { "Earth", "Growth" }, new[] { "Breadth" }),
            Make("Firm ground", new[] { "Earth", "Hold" }, new[] { "Aura" }),
            Make("Through the earth", new[] { "Earth" }, new[] { "Step" }),
            Make("Dragged under", new[] { "Earth" }, new[] { "Step", "Thread" }),
            Make("Stone splints", new[] { "Earth" }, new[] { "Restore" }),
            Make("Weapon of earth", new[] { "Earth" }, new[] { "Enchant" }),
            Make("Tremorsense", new[] { "Earth" }, new[] { "Sense" }),
            Make("Ward of earth", new[] { "Earth", "Barrier" }),
        };

        public static List<RitualSchema> Air() => new List<RitualSchema>
        {
            Make("Gust", new[] { "Air", "Release" }, new[] { "Breadth" }),
            Make("Chained gust", new[] { "Air" }, new[] { "Beam" }),
            Make("Snatched by the wind", new[] { "Air" }, new[] { "Step", "Thread" }),
            Make("Wall of wind", new[] { "Air", "Release" }, new[] { "Shape" }),
            Make("Still air", new[] { "Air", "Hold" }),
            Make("Stolen breath", new[] { "Air", "Drain" }),
            Make("Updraft", new[] { "Air", "Lift" }),
            Make("Whirlwind", new[] { "Air", "Release" }, new[] { "Aura" }),
            Make("Clear air", new[] { "Air", "Purge" }, new[] { "Breadth" }),
            Make("On the wind", new[] { "Air" }, new[] { "Step" }),
            Make("Second wind", new[] { "Air" }, new[] { "Restore" }),
            Make("Weapon of air", new[] { "Air" }, new[] { "Enchant" }),
            Make("Carried sound", new[] { "Air" }, new[] { "Sense" }),
            Make("Ward of air", new[] { "Air", "Barrier" }),
        };

        public static List<RitualSchema> Water() => new List<RitualSchema>
        {
            Make("Tidal burst", new[] { "Water", "Release" }, new[] { "Breadth" }),
            Make("Water jet", new[] { "Water", "Release" }, new[] { "Beam" }),
            Make("Water curtain", new[] { "Water", "Hold" }, new[] { "Shape" }),
            Make("Standing water", new[] { "Water", "Hold" }, new[] { "Breadth" }),
            Make("Draw the water", new[] { "Water", "Drain" }),
            Make("Geyser", new[] { "Water", "Lift" }),
            Make("Cleansing rain", new[] { "Water", "Purge" }, new[] { "Aura" }),
            Make("Ride the wave", new[] { "Water", "Release" }, new[] { "Step" }),
            Make("Undertow", new[] { "Water" }, new[] { "Step", "Thread" }),
            Make("Washing", new[] { "Water" }, new[] { "Restore" }),
            Make("Weapon of water", new[] { "Water" }, new[] { "Enchant" }),
            Make("Ward of water", new[] { "Water", "Barrier" }),
        };

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
            Make("Warmth sense", new[] { "Fire" }, new[] { "Sense" }),
            Make("Ward of flame", new[] { "Fire", "Barrier" }),
            Make("Drawn through flame", new[] { "Fire" }, new[] { "Step", "Thread" }),
        };
    }
}
