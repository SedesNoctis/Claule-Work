using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>What a written ritual will do: three layers - the runes' numbers summed, the chain read
    /// from the centre outward (which verbs, in what order, what modifies them), and the table of
    /// special patterns (named rituals such as the Trap).</summary>
    public class RitualReading
    {
        public RitualSchema schema;
        public List<RuneDef> verbs = new List<RuneDef>();
        public List<RuneDef> elements = new List<RuneDef>();   // runes of magic's elements: the nature of what is done
        public int amplify, reach, breadth, anchor, safety, closing, focus, barrier, thread;
        public float cost, instability, actWeight;
        public RitualPresetDef pattern;     // a special pattern, if the drawing matches one
        public bool trap;                   // a cage of safety runes: the Trap (see trapKind)
        public TrapKind trapKind;
        public bool unimplemented;          // a verb whose effect is not made yet

        public bool Has(RuneKind k) => verbs.Any(v => v.kind == k);

        /// <summary>What a transmutation makes: the first seal among the component circles.</summary>
        public ThingDef Seal => schema?.components.FirstOrDefault(c => c.kind == ComponentKind.Seal && Transmutation.Allowed(c.thing))?.thing;

        /// <summary>What it works on: the material circles (none - any allowed metal in the circle).</summary>
        public List<ThingDef> Inputs => schema?.components.Where(c => c.kind == ComponentKind.Material && Transmutation.Allowed(c.thing)).Select(c => c.thing).ToList() ?? new List<ThingDef>();

        /// <summary>Share of the value that comes through a transmutation: a weak circle loses much, a strong one none.
        /// Past 100% the circle gives more than it takes: a medium circle up to 150%, a large one up to 200%; a small one never.</summary>
        public float TransmuteYield(float drawQuality) => Mathf.Clamp(0.45f + 0.25f * Power(drawQuality), 0.3f, MaxYield);

        /// <summary>The most a circle of this size can give back: small 100%, medium 150%, large 200%.</summary>
        public float MaxYield => schema == null ? 1f : schema.size == RitualCircleSize.Large ? 2f : schema.size == RitualCircleSize.Medium ? 1.5f : 1f;

        /// <summary>Barrier + Anchor and no verb: a dome that keeps projectiles out while the energy flows.</summary>
        public bool Dome => verbs.Count == 0 && barrier > 0 && anchor > 0 && !trap;

        /// <summary>Release with the performer as its conduit (no reach), and neither barrier nor safety to stop it.</summary>
        public bool SelfDetonation => Has(RuneKind.Release) && reach == 0 && barrier == 0 && safety == 0;

        /// <summary>Lift and Release together: the blast throws foes back instead of tearing them.</summary>
        public bool Throw => Has(RuneKind.Release) && Has(RuneKind.Lift);

        /// <summary>Filling with nothing closing it and the map's flow pouring in: if nothing can be filled, it never ends.</summary>
        public bool VortexRisk => Has(RuneKind.Fill) && closing == 0 && focus > 0 && !trap;

        /// <summary>Transmutation without a seal: the energy itself is changed - into current for electric batteries.</summary>
        public bool ToCurrent => Has(RuneKind.Transmute) && Seal == null && (thread > 0 || breadth > 0);

        /// <summary>How far a reach rune carries the effect: anywhere on the map.</summary>
        public float ReachRange => 99999f;

        /// <summary>No verb, but focus runes: the flow is drawn in and has nowhere to go.</summary>
        public bool RawFlow => verbs.Count == 0 && focus > 0 && !trap && !Dome;

        /// <summary>Raw flow drawn hard enough to call a storm over the map.</summary>
        public bool Storm => RawFlow && focus >= StormFocus;
        public const int StormFocus = 6;

        /// <summary>The Eye of the storm: a large storm circle sealed by closing runes - the flow is
        /// locked into the ritualist instead of pouring away, and they leave the circle in a storm mantle.</summary>
        public bool Eye => Storm && closing > 0 && schema.size == RitualCircleSize.Large;

        public float DomeRadius => 5f + 3f * (int)schema.size + 4f * breadth;

        /// <summary>Pull of the external flow per focus rune, at the calm threshold.</summary>
        public const float FocusPull = 0.04f;

        /// <summary>What it asks of composure: the runes, the pull of the map's flow (flowRatio = flow / calm),
        /// raw power beyond what a body takes easily, and energy held with nowhere to go.</summary>
        public float Risk(float flowRatio, float drawQuality, float held) =>
            instability + focus * FocusPull * flowRatio + Mathf.Max(0f, Power(drawQuality) - 1.5f) * 0.2f + (RawFlow ? held * 0.0001f : 0f);

        /// <summary>Power multiplier: size x amplification; leaks without a closing rune.</summary>
        public float Power(float drawQuality) =>
            RitualCircles.PowerFactor(schema.size) * (1f + 0.3f * amplify) * Mathf.Max(0.2f, drawQuality) * Efficiency;

        /// <summary>Share of the energy that does work: a ritual without a closing rune leaks.</summary>
        public float Efficiency => closing > 0 ? 1f : 0.7f;

        /// <summary>Safety runes cut the flow (and keep the ritualist alive).</summary>
        public float FlowCap => safety > 0 ? Mathf.Pow(0.75f, safety) : 1f;

        /// <summary>What the ritual will do, in plain words, step by step.</summary>
        public string Plain()
        {
            var sb = new StringBuilder();
            float area = RitualCircles.Cells(schema.size) / 2f + breadth * 4f;
            string where = "within " + area.ToString("0.#") + " cells of the circle";
            if (schema.AllRunes.Count() == 0) return "Nothing is written yet. Pick a section in the circle, then click runes on the left to write them into it.";
            if (reach > 0 && !trap) where = "within " + area.ToString("0.#") + " cells of a point you choose anywhere on the map";
            if (trap)
            {
                sb.AppendLine("• Becomes a cage: once armed, any living foe who steps into the circle - or a prisoner or slave of yours who rises - is held there until the drawing is gone, it dies or it breaks out. Engraved, it drags in the foes who pass within " + CompRitualCircle.AttractReach.ToString("0") + " cells of it.");
                switch (trapKind)
                {
                    case TrapKind.Incomplete: sb.AppendLine("• Incomplete: the focus rune lets the map's stream in - the prisoner is fed by it (no hunger, no tiredness) while the map holds any stream, and the cage draws a little of it."); break;
                    case TrapKind.Feeding: sb.AppendLine("• Feeding: you feed the prisoner along the thread from your batteries and artifact buildings - it lasts as long as you pay."); break;
                    case TrapKind.Pulse:
                        sb.AppendLine("• Pulsing: the prisoner becomes a turret. While foes are within " + CompRitualCircle.PulseRange.ToString("0") + " cells, every few seconds a tenth of its strength is torn out and thrown at them as a wave - burning and stunning the living, striking machines like an EMP. At nine tenths it is dropped, drained, and the cage opens. It barely tries to break out.");
                        break;
                    case TrapKind.Milking:
                        sb.AppendLine("• Milking: the prisoner's strength streams along the thread into your batteries, artifact buildings or electric batteries.");
                        sb.AppendLine(focus > 0 ? "• With the focus rune the map's stream refills it: almost endless, but it pulls the stream up." : "• Without a focus rune it runs dry: the prisoner collapses, the cage opens and it can be taken.");
                        break;
                }
            }
            else if (Dome)
                sb.AppendLine("• Raises a barrier dome " + (reach > 0 ? "over the chosen point" : "over the circle") + " (radius " + DomeRadius.ToString("0") + "): shots from outside stop on it. The energy keeps it whole; the barrier itself pulls the map's stream.");
            else if (verbs.Count == 0)
            {
                if (RawFlow)
                {
                    sb.AppendLine("• Draws the map's energy in and holds it - there is no verb to spend it on.");
                    if (Storm) sb.AppendLine("• Calls a thunderstorm and throws lightning around the circle.");
                    if (Eye) sb.AppendLine("• At the end the storm is sealed into the ritualist: their artifacts are filled and they walk out in a storm mantle (a shield dome that strikes foes).");
                }
                else sb.AppendLine("• Has no verb: nothing will happen. Add a verb rune (filling, purging, holding...).");
            }
            for (int i = 0; i < verbs.Count; i++)
            {
                RuneDef v = verbs[i];
                string step = (verbs.Count > 1 ? (i + 1) + ". " : "• ");
                if (!v.implemented) { sb.AppendLine(step + v.LabelCap + ": its effect is not made yet - this step is skipped."); continue; }
                switch (v.kind)
                {
                    case RuneKind.Fill:
                        sb.AppendLine(step + "Charges every artifact, artifact building, crystal battery and psycaster's psyfocus " + where + ", until all are full.");
                        if (focus > 0) sb.AppendLine("   With focus, every artifact and amulet there snatches experience out of the map's stream: up to " + (400 * focus) + " each at the calm threshold, more the stormier the map - and it pulls the stream up.");
                        break;
                    case RuneKind.Purge: sb.AppendLine(step + "Purges every artifact and amulet " + where + ": back as it was made, every point returned, charge full."); break;
                    case RuneKind.Transfer:
                        bool purgeAfter = verbs.Skip(i + 1).Any(x => x.kind == RuneKind.Purge);
                        sb.AppendLine(step + "Moves experience from one artifact or amulet in the circle into another you choose" + (purgeAfter ? " - all of it (the purge after it resets the source)." : " - only the free experience, not what stands behind opened cells."));
                        break;
                    case RuneKind.Hold: sb.AppendLine(step + "Holds living foes " + where + " stunned while the energy lasts."); break;
                    case RuneKind.Drain:
                        sb.AppendLine(step + "Draws energy out of what is " + where + ": psychic pylons (caught as crystals at the centre) and the strength of foes and strangers - they tire and at last collapse"
                                      + (thread > 0 ? "; threaded batteries and artifact buildings give their charge too" : "") + ". What it takes feeds the steps after it" + (i == verbs.Count - 1 ? " (as the last step, it is caught as crystals)." : "."));
                        break;
                    case RuneKind.Release:
                        if (Throw) sb.AppendLine(step + "Gathers energy for a while, then lets it out at once " + (reach > 0 ? "at the chosen point" : "around the circle") + ": a telekinetic wave throws everyone back and stuns them.");
                        else sb.AppendLine(step + "Gathers energy for a while, then lets it out at once: a blast " + (reach > 0 ? "at the chosen point" : "around the circle") + " - the more energy, the wider and harder.");
                        if (SelfDetonation) sb.AppendLine("   ! The performer is the conduit and nothing holds the blast back: it goes off in their hands. Self-detonation.");
                        else if (reach == 0 && barrier == 0 && safety > 0) sb.AppendLine("   The safety rune throws the performer clear before it goes off.");
                        if (barrier > 0) sb.AppendLine("   The barrier spares your people and buildings.");
                        break;
                    case RuneKind.Lift:
                        if (Throw) sb.AppendLine(step + "Lifts what the release throws: the blast becomes a wave that throws back.");
                        else sb.AppendLine(step + "Telekinesis " + where + ": loose things go to the best storage for them, snow and filth are swept away.");
                        break;
                    case RuneKind.Growth: sb.AppendLine(step + "Plants " + where + " grow and ripen, your people and animals there heal" + (anchor > 0 ? "; anchored, the soil itself grows richer." : ".")); break;
                    case RuneKind.Reforge:
                        sb.AppendLine(step + "Reforges the artifact laid in the circle - you choose: its heart one quality step up, its heart into the metal a seal names, or its spiral reforged "
                                      + "(every cell closes and every point returns, burnt ones too, and it scoops more experience out of the map's stream - more with focus and amplification). Needs a large circle.");
                        break;
                    case RuneKind.Infuse:
                        sb.AppendLine(step + "Infuses the artifact laid in the circle: the ritual's energy turns into its experience (about one for one). "
                                      + "Its spiral is not touched. At most one point of development a ritual (two in a large circle). Needs a medium or large circle.");
                        break;
                    case RuneKind.Transmute:
                        ThingDef seal = Seal;
                        var ins = Inputs;
                        if (seal == null && ToCurrent) sb.AppendLine(step + "Changes the energy itself into current: electric batteries " + (thread > 0 ? "on the threaded grids" : where) + " are charged while the energy streams.");
                        else if (seal == null) sb.AppendLine(step + "Transmutes - but no seal names what into. Put a seal (of gold, plasteel...) into a component circle; or, with a thread or breadth, it turns the energy into current.");
                        else sb.AppendLine(step + "Transmutes " + (ins.Count == 0 ? "any metal or jade" : string.Join(", ", ins.Select(d => d.label))) + " lying in the circle into " + seal.label
                                           + ", by value: about " + TransmuteYield(1f).ToStringPercent() + " of it comes through with a good drawing (a " + RitualCircles.Label(schema.size) + " circle reaches " + MaxYield.ToStringPercent() + " at most).");
                        break;
                    case RuneKind.Blood:
                        switch (CompRitualCircle.BloodModeOf(this))
                        {
                            case BloodMode.Unbind: sb.AppendLine(step + "Beside the purge: takes blood magic back from the one you choose. The purge touches nothing else."); break;
                            case BloodMode.Healing: sb.AppendLine(step + "Beside growth: one life after another (prisoners or downed strangers in the circle) is given to close the wounds of your people and animals in it - scars too."); break;
                            case BloodMode.Sacrifice: sb.AppendLine(step + "Beside another verb: whenever the ritual's energy runs low, a life in the circle (prisoners or downed strangers) feeds it - far more than a crystal - and pulls the stream up."); break;
                            default:
                                sb.AppendLine(step + "Wakes blood magic in the one you choose (the performer or a colonist standing in the circle). First the circle takes "
                                              + CompRitualCircle.BloodLives + " lives - the colony's prisoners or downed strangers inside it, one after another by the performer's hand; then the rite must be carried to its end. Broken off, the blood spilled is lost.");
                                break;
                        }
                        break;
                    case RuneKind.Shroud:
                        switch (CompRitualCircle.ShroudModeOf(this))
                        {
                            case ShroudMode.Unveil: sb.AppendLine(step + "Beside the purge: lifts the shroud from the priestess you choose. The purge touches nothing else."); break;
                            case ShroudMode.Darkness: sb.AppendLine(step + "Darkness " + where + " for two hours (anchored - a day): foes in it go half blind, your people in it are harder to strike."); break;
                            default: sb.AppendLine(step + "Makes a priestess of the shroud of the woman you choose (the performer or one standing in the circle). The shroud is drawn only in darkness: while the circle stands in light, nothing goes in."); break;
                        }
                        break;
                    default: sb.AppendLine(step + v.LabelCap + "."); break;
                }
            }
            if (amplify > 0) sb.AppendLine("• Stronger: x" + (1f + 0.3f * amplify).ToString("0.0#") + " energy.");
            if (anchor > 0) sb.AppendLine("• Anchored: repeats for up to a day while energy streams.");
            if (focus > 0) sb.AppendLine("• Takes energy from the map as well (focus x" + focus + ") - more energy, more danger.");
            if (safety > 0) sb.AppendLine("• Safe: if it breaks, the ritualist is thrown clear instead of burnt (energy x" + FlowCap.ToString("0.00") + ").");
            if (thread > 0 && !trap) sb.AppendLine("• Thread: up to " + (3 * thread) + " things anywhere on the map are bound to the circle (choose them on the circle) - filling reaches them, drawing out takes from them.");
            if (barrier > 0 && !Dome) sb.AppendLine("• Barrier: a blast of this ritual spares your people and buildings.");
            if (closing == 0 && !trap) sb.AppendLine("• Not closed: 30% of the energy is lost. A closing rune stops it.");
            if (VortexRisk) sb.AppendLine("• ! Filling, unclosed, with the stream pouring in: if there is nothing to fill, the stream never stops - an endless vortex.");
            sb.AppendLine();
            sb.Append("Energy comes from the performer and colonists standing in the circle (psychic sensitivity, it tires them; an Avir's wings hold twice as much, clipped wings half), crystals and Avir feathers laid in the circle (feathers give twice as much to a filling ritual)" + (focus > 0 ? " and the map's stream." : "."));
            return sb.ToString();
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            if (pattern != null) sb.AppendLine("Pattern: " + pattern.LabelCap);
            string asp = RitualAspects.Describe(RitualAspects.Of(schema));
            if (asp != null) sb.AppendLine(asp);
            if (elements.Count > 0) sb.AppendLine("Elements: " + string.Join(", ", elements.GroupBy(e => e).Select(g => g.Key.LabelCap + (g.Count() > 1 ? " x" + g.Count() : ""))) + " - their effects in a ritual are not made yet; for now they set its colour.");
            if (trap) sb.AppendLine("A closed cage of safety runes: whatever living thing steps in is held.");
            if (RawFlow)
            {
                sb.AppendLine("No verb, only focus: the stream is drawn in and has nowhere to go. It pours out into the map (the external stream rises) "
                              + "and weighs heavier on the ritualist with every moment it is held.");
                if (Storm) sb.AppendLine("So much focus calls a storm: thunder over the map, lightning around the circle - each strike vents some of the held energy.");
                if (Eye) sb.AppendLine("Sealed by closing runes in a large circle: the storm is locked into the ritualist. If they hold to the end, they walk out in a storm mantle - "
                                       + "a shield of the gathered energy that throws lightning at every foe.");
                else if (Storm && closing == 0) sb.AppendLine("Nothing seals it: the storm pours away into the sky. Closing runes in a large circle would lock it into the ritualist.");
                else sb.AppendLine("With " + StormFocus + " or more focus runes it would call a storm.");
            }
            else if (Dome) sb.AppendLine("Barrier and anchor with no verb: a dome against shots from outside.");
            else if (verbs.Count == 0 && !trap) sb.AppendLine("No verb: the energy only streams - nothing will happen, or it will leak.");
            if (trap && trapKind != TrapKind.Full) sb.AppendLine("Trap: " + trapKind.ToString().ToLower());
            if (SelfDetonation) sb.AppendLine("Self-detonation: the release goes off in the performer's hands.");
            if (VortexRisk) sb.AppendLine("May become an endless vortex if there is nothing to fill.");
            if (reach > 0) sb.AppendLine("Reach: a chosen point anywhere on the map");
            if (thread > 0) sb.AppendLine("Thread: up to " + (3 * thread) + " bound things");
            for (int i = 0; i < verbs.Count; i++)
                sb.AppendLine((i + 1) + ". " + verbs[i].LabelCap + (verbs[i].implemented ? "" : " (not made yet)"));
            if (amplify > 0) sb.AppendLine("Amplified x" + (1f + 0.3f * amplify).ToString("0.0#"));
            if (breadth > 0) sb.AppendLine("Breadth +" + breadth);
            if (anchor > 0) sb.AppendLine("Anchored: lasts while energy streams");
            if (focus > 0) sb.AppendLine("Draws the external stream (focus x" + focus + ")");
            if (safety > 0) sb.AppendLine("Safety: stream x" + FlowCap.ToString("0.00") + ", the ritualist is thrown clear before it breaks");
            sb.AppendLine(closing > 0 ? "Closed: no leaks" : "Not closed: 30% leaks out");
            sb.Append("Instability " + instability.ToString("0.00") + ", weight of the deed " + actWeight.ToString("0"));
            return sb.ToString().TrimEnd();
        }
    }

    public enum TrapKind { None, Full, Incomplete, Feeding, Milking, Pulse }

    public static class RitualEvaluator
    {
        public static RitualReading Read(RitualSchema s)
        {
            var r = new RitualReading { schema = s };
            if (s == null) return r;
            // the chain: rings from the centre outward
            foreach (RitualRing ring in s.rings)
                foreach (RuneDef rune in ring.runes)
                {
                    if (rune == null) continue;
                    r.cost += rune.cost;
                    r.instability += rune.instability;
                    r.actWeight += rune.actWeight;
                    switch (rune.kind)
                    {
                        case RuneKind.Amplify: r.amplify++; break;
                        case RuneKind.Reach: r.reach++; break;
                        case RuneKind.Breadth: r.breadth++; break;
                        case RuneKind.Anchor: r.anchor++; break;
                        case RuneKind.Safety: r.safety++; break;
                        case RuneKind.Closing: r.closing++; break;
                        case RuneKind.Focus: r.focus++; break;
                        case RuneKind.Barrier: r.barrier++; break;
                        case RuneKind.Thread: r.thread++; break;
                        case RuneKind.Fire: case RuneKind.Water: case RuneKind.Earth: case RuneKind.Air: case RuneKind.Light:
                        case RuneKind.Time: case RuneKind.Life: case RuneKind.Darkness: case RuneKind.Space:
                            r.elements.Add(rune); break;
                        default:
                            r.verbs.Add(rune);
                            if (!rune.implemented) r.unimplemented = true;
                            break;
                    }
                }
            r.instability += (int)s.size * 0.15f;
            // the Trap: medium or large, nothing but safety and closing runes, at least one safety
            int all = s.AllRunes.Count();
            r.trapKind = TrapOf(r, s, all);
            r.trap = r.trapKind != TrapKind.None;
            // special patterns: the same runes, ring by ring, as a preset (hidden ones are found this way)
            foreach (RitualPresetDef p in RitualPresetDef.All)
                if (Same(p, s)) { r.pattern = p; break; }
            if (r.pattern == null && r.trap)
                r.pattern = DefDatabase<RitualPresetDef>.GetNamedSilentFail(r.trapKind == TrapKind.Full ? "Avir_Ritual_Trap" : "Avir_Ritual_Trap" + r.trapKind);
            if (r.pattern == null && r.SelfDetonation) r.pattern = DefDatabase<RitualPresetDef>.GetNamedSilentFail("Avir_Ritual_SelfDetonation");
            if (r.pattern == null && r.Dome) r.pattern = DefDatabase<RitualPresetDef>.GetNamedSilentFail("Avir_Ritual_BarrierDome");
            // the Eye of the storm: a large storm circle sealed by closing runes
            if (r.pattern == null && r.Eye)
                r.pattern = DefDatabase<RitualPresetDef>.GetNamedSilentFail("Avir_Ritual_EyeOfStorm");
            return r;
        }

        /// <summary>The Trap and its variants: a medium or large cage of safety runes (closing ones allowed), and
        /// only - one focus (incomplete); filling along a thread (feeding); drawing out along a thread, perhaps
        /// with one focus (milking).</summary>
        private static TrapKind TrapOf(RitualReading r, RitualSchema s, int all)
        {
            if (s.size == RitualCircleSize.Small || r.safety == 0 || all < 3) return TrapKind.None;
            int rest = all - r.safety - r.closing;
            if (rest == 0) return TrapKind.Full;
            if (rest == 1 && r.focus == 1) return TrapKind.Incomplete;
            if (rest == 1 && r.verbs.Count == 1 && r.verbs[0].kind == RuneKind.Release) return TrapKind.Pulse;
            if (r.verbs.Count == 1 && r.thread > 0)
            {
                if (r.verbs[0].kind == RuneKind.Fill && rest == 1 + r.thread) return TrapKind.Feeding;
                if (r.verbs[0].kind == RuneKind.Drain && r.focus <= 1 && rest == 1 + r.thread + r.focus) return TrapKind.Milking;
            }
            return TrapKind.None;
        }

        /// <summary>Same pattern: the same size and, ring by ring, the same runes (order inside a ring free).</summary>
        public static bool Same(RitualPresetDef p, RitualSchema s)
        {
            if (p.size != s.size || p.rings.Count != s.rings.Count) return false;
            for (int i = 0; i < p.rings.Count; i++)
            {
                var a = p.rings[i].runes.Select(x => x.defName).OrderBy(x => x).ToList();
                var b = s.rings[i].runes.Where(x => x != null).Select(x => x.defName).OrderBy(x => x).ToList();
                if (!a.SequenceEqual(b)) return false;
            }
            return true;
        }
    }
}
