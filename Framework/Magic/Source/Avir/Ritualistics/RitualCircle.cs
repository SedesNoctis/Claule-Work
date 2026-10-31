using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Ritualistics
{
    // =====================================================================================
    // THE RITUAL CIRCLE (slice 1). A chalk circle on the floor, 3x3 / 5x5 / 7x7. A ritual is chosen
    // from the ritual book, drawn by a colonist (Intellectual + Artistic decide its quality), then
    // performed: a colonist stands in the centre and the energy flows - from the performer and anyone
    // standing in the circle, and from the external flow when focus runes draw it.
    // Slice 1 verbs: Fill (charge), Transfer (experience), Purge (reset), Hold; the Trap pattern.
    // Chalk under the open sky is washed away by rain and snow.
    // =====================================================================================
    public class CompProperties_RitualCircle : CompProperties
    {
        public RitualCircleSize size = RitualCircleSize.Small;
        public CompProperties_RitualCircle() { compClass = typeof(CompRitualCircle); }
    }

    [StaticConstructorOnStartup]
    public static class RitualTex
    {
        /// <summary>The line of a section (and the circle's rim): the artist's ring if there is one, else a plain drawn one.</summary>
        public static readonly Texture2D Ring = ContentFinder<Texture2D>.Get("UI/Ritualistics/Circle/SectionRing", false) ?? MakeRing(512, 0.965f);
        /// <summary>The circle's outer rim only (a thin ornamental band: ticks and diamonds); the plain ring if there is no art.
        /// Section lines and the shading stay the plain Ring: an ornament repeated on every line is only noise.</summary>
        public static readonly Texture2D Rim = ContentFinder<Texture2D>.Get("UI/Ritualistics/Circle/RimRing", false) ?? Ring;
        /// <summary>Where the rim art's ornament band begins, as a share of the texture's radius (its outer edge is 1).
        /// The band is drawn OUTSIDE the sections - the texture is enlarged by RimGrow so that its inner edge meets the last
        /// section line - and so never runs through the rune frames. Measured on the shipped art (0.88); if the art is
        /// redrawn, set this to where its ornament starts (1 - band thickness).</summary>
        public const float RimInnerArt = 0.88f;
        public static readonly float RimInner = Rim == Ring ? 1f : RimInnerArt;
        public static float RimGrow => 1f / RimInner;
        public static readonly Texture2D Disc = MakeRing(128, 0f);

        private static Texture2D MakeRing(int size, float hole)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float c = (size - 1) / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01((1f - d) * size * 0.5f) * (hole <= 0f ? 1f : Mathf.Clamp01((d - hole) * size * 0.5f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }

    public partial class CompRitualCircle : ThingComp
    {
        public RitualSchema schema;
        /// <summary>The ritual actually drawn on the floor (null - nothing drawn yet).</summary>
        public RitualSchema drawn;
        public float drawQuality;
        public float washed;                 // rain on open chalk, 0..1
        public bool armed;                   // a Trap waits
        public List<Pawn> held = new List<Pawn>();
        /// <summary>The pulsing Trap: how many tenths of each prisoner's strength it has spent.</summary>
        public Dictionary<Pawn, int> pulsesTaken = new Dictionary<Pawn, int>();
        private List<Pawn> pulseKeys; private List<int> pulseVals;
        private int pulseCooldown;

        // a ritual in progress
        public Pawn performer;
        public int verbIndex;
        public float bank;                   // energy gathered, not yet spent
        public float spentTotal;
        public Thing transferFrom, transferTo;
        private int runTicks;
        private float crystalProgress;      // Drain: caught energy not yet a whole crystal

        public CompProperties_RitualCircle Props => (CompProperties_RitualCircle)props;
        public RitualCircleSize Size => Props.size;
        public bool Drawn => drawn != null && drawQuality > 0f;
        public bool NeedsDrawing => schema != null && (drawn == null || !SameSchema(drawn, schema) || engraved != engraveNext);
        private RitualReading readingCache;
        private RitualSchema readingOf;
        public RitualReading Reading
        {
            get
            {
                if (readingCache == null || readingOf != drawn) { readingCache = RitualEvaluator.Read(drawn); readingOf = drawn; }
                return readingCache;
            }
        }

        /// <summary>Where the ritual acts: the circle, or the point a reach rune carries it to.</summary>
        public IntVec3 Center => target.IsValid && Drawn && Reading.reach > 0 ? target : parent.Position;
        public IntVec3 target = IntVec3.Invalid;
        private int stepTicks;
        public bool Running => performer != null || conduit != null;
        private static bool SameSchema(RitualSchema a, RitualSchema b) =>
            a.size == b.size && a.rings.Count == b.rings.Count && a.rings.Select((r, i) => r.runes.SequenceEqual(b.rings[i].runes)).All(x => x);

        public float Radius => RitualCircles.Cells(Size) / 2f;

        public IEnumerable<IntVec3> Cells => GenRadial.RadialCellsAround(parent.Position, Radius, true).Where(c => c.InBounds(parent.Map));
        public bool Inside(IntVec3 c) => (c - parent.Position).LengthHorizontalSquared <= Radius * Radius;

        /// <summary>Work done on the drawing in progress (kept when the ritualist is called away).</summary>
        public float drawWork;

        /// <summary>Work a drawing takes: long - every rune and component is set out by hand, a bigger circle more so.</summary>
        public float DrawWorkNeeded
        {
            get
            {
                if (schema == null) return 1f;
                return (2500f + 1500f * schema.AllRunes.Count() + 800f * schema.components.Count(c => c.kind != ComponentKind.None))
                       * (1f + 0.25f * (int)schema.size) * (engraveNext ? EngraveWork : 1f);
            }
        }

        public void Assign(RitualSchema s, bool engrave = false)
        {
            drawWork = 0f;
            schema = s?.Copy();
            engraveNext = engrave;
            armed = false;
            Messages.Message(schema == null ? "The circle is cleared." : "\"" + schema.name + "\" will be " + (engrave ? "engraved into the floor" : "drawn into the circle") + " by a ritualist.",
                parent, MessageTypeDefOf.SilentInput, false);
        }

        /// <summary>Dev mode: the chosen ritual is on the floor at once, quality 100%.</summary>
        public void DevDrawNow()
        {
            if (schema == null) return;
            drawn = schema.Copy(); drawQuality = 1f; washed = 0f; vortex = 0f; engraved = engraveNext; drawWork = 0f;
            RitualReading r = Reading;
            if (r.pattern != null && r.pattern.hidden) RitualDiscoveries.Learn(r.pattern);
        }

        // ---------------------------------------------------------------- drawing (by the job)

        public void FinishDrawing(Pawn by)
        {
            drawWork = 0f;
            if (schema == null) return;
            drawn = schema.Copy();
            washed = 0f;
            vortex = 0f;
            engraved = engraveNext;
            int intel = by.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            int art = by.skills?.GetSkill(SkillDefOf.Artistic)?.Level ?? 0;
            // an artist alone cannot set it out, an intellectual alone cannot draw it well
            // an artist alone cannot set it out, an intellectual alone cannot draw it well
            drawQuality = Mathf.Clamp(0.25f + 0.035f * intel + 0.03f * art + Rand.Range(-0.08f, 0.08f), 0.2f, 1.6f);
            if (engraved) drawQuality = Mathf.Min(1.7f, drawQuality + EngraveQuality);
            RitualReading r = Reading;
            if (r.pattern != null && r.pattern.hidden) RitualDiscoveries.Learn(r.pattern);
            Messages.Message(by.LabelShort + " drew \"" + drawn.name + "\" (quality " + drawQuality.ToStringPercent() + ").", parent, MessageTypeDefOf.PositiveEvent, false);
            GameComponent_AvirComposure.Drew(by);
            if (by.Faction == Faction.OfPlayer) { if (drawQuality < 0.5f) Arlia.Say("drawnpoor"); else if (drawQuality >= 0.9f) Arlia.Say("drawnfine"); }
        }

        // ---------------------------------------------------------------- performing (by the job)

        public string CannotPerform(Pawn p)
        {
            if (!Drawn) return "nothing drawn yet";
            RitualReading r = Reading;
            if (r.trap) return "a Trap is not performed - arm it";
            if (r.verbs.Count == 0 && !r.RawFlow && !r.Dome) return "no verb rune: nothing would happen";
            if (r.Has(RuneKind.Reforge) && Size != RitualCircleSize.Large) return "reforging needs a large circle";
            if (r.Has(RuneKind.Infuse) && !IsPath(r) && Size == RitualCircleSize.Small) return "infusion needs a medium or large circle";
            if ((r.Has(RuneKind.Blood) || r.Has(RuneKind.Shroud)) && Size == RitualCircleSize.Small) return "an awakening needs a medium or large circle";
            if (drawn?.preset?.defName == Avir.Magic.MagePath.Preset && r.elements.Count == 0) return "write the caster's element into the empty place";
            if (IsPath(r)) { string pw = PathCircle(r, Size); if (pw != null) return pw; }
            if (vortex > 0f) return "an endless vortex spins in it - erase the chalk first";
            if (r.verbs.Count > 0 && r.verbs.All(v => !v.implemented)) return "its effect is not made yet";
            if (r.Has(RuneKind.Transmute) && r.Seal == null && !r.ToCurrent) return "no seal: put a seal of what to make into a component circle";
            if (Running && performer != p) return performer.LabelShort + " is performing it";
            if (p != null && (p.Downed || p.Dead)) return "cannot";
            return MissingMaterial();   // a ritual that works on something does not begin without it
        }

        public void Begin(Pawn p, IntVec3 at, Thing from = null, Thing to = null, int mode = 0)
        {
            performer = p;
            target = at;
            reforgeMode = mode;
            stepTicks = 0;
            dryTicks = 0;
            verbIndex = 0;
            bank = 0f;
            spentTotal = 0f;
            transmuteCarry = 0f;
            runTicks = 0;
            transferFrom = from;
            transferTo = to;
            grabbed.Clear();
            ResetAwakening();
        }

        public void End(string why)
        {
            infusedXp = 0f; infusedPointsAtStart = -1;
            // a ritual allowed to run on its own rests an hour before it is begun again
            if (Running) autoRestUntil = Find.TickManager.TicksGame + AutoRest;
            // what is left: a closed ritual keeps it in its performer (no ceiling); an open one spills it into the map
            if (performer != null && bank > 0.01f)
            {
                RitualReading left = drawn != null ? Reading : null;
                if (left != null && left.closing > 0 && !left.RawFlow) Hediff_StreamSaturation.Change(performer, bank);
                else
                {
                    MapComponent_ExternalFlow spill = parent.Map != null ? MapComponent_ExternalFlow.Of(parent.Map) : null;
                    if (spill != null) spill.deeds += bank * RawSpill;
                }
                bank = 0f;
            }
            // living magic leaves its mark on those who stood in it: in time, they take it in
            if (spentTotal > 0f && drawn != null && parent.Map != null && Avir.Magic.AvirMagic.On)
                foreach (Pawn q in Sources().ToList()) Avir.Magic.MagicExposure.Exposed(q, spentTotal * (q == performer ? 1f : 0.5f));
            // what passed through the performer trains its composure
            if (performer != null && spentTotal > 0f && drawn != null)
            {
                MapComponent_ExternalFlow fx = parent.Map != null ? MapComponent_ExternalFlow.Of(parent.Map) : null;
                float rk = Reading.Risk(fx == null ? 0f : fx.Total / MapComponent_ExternalFlow.Calm, drawQuality, 0f) * Avir.Linarian.LinarianRitual.RiskFactor(performer);
                GameComponent_AvirComposure.Performed(performer, spentTotal, rk, GameComponent_AvirComposure.For(performer, drawn, this), GameComponent_AvirComposure.KeyOf(drawn));
            }
            if (why != null && (conduit != null || performer?.Faction == Faction.OfPlayer))
            {
                Messages.Message("Ritual \"" + drawn?.name + "\": " + why, parent, MessageTypeDefOf.NeutralEvent, false);
                Arlia.Ended(why);
            }
            performer = null;
            conduit = null;
            transferFrom = transferTo = null;
            DropDome();
        }

        /// <summary>One step of the ritual; false when it is over.</summary>
        public bool RitualTick(Pawn p, int delta)
        {
            if (performer != p || !Drawn) return false;
            return Step(delta);
        }

        /// <summary>One step, with a pawn or a building as the conduit.</summary>
        private bool Step(int delta)
        {
            Pawn p = performer;
            RitualReading r = Reading;
            runTicks += delta;
            Map map = parent.Map;
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map);

            // energy in: the performer, everyone standing in the circle, the external flow through focus runes
            float rate = 0f;
            foreach (Pawn src in Sources())
            {
                // an Avir's wings are the vessel of its energy: whole wings give twice as much, clipped ones half
                float wings = !Avir.AvirWingParts.HasWingGene(src) ? 1f : Avir.AvirWingParts.Clipped(src) ? 0.5f : 2f;
                float give = src.GetStatValue(StatDefOf.PsychicSensitivity) * 0.05f * delta * wings * Avir.Linarian.LinarianRitual.GiveFactor(src, p);
                // what a pawn gives comes out of its own charge of the stream: emptied, it has nothing more
                give = Hediff_StreamSaturation.Change(src, -give);
                if (give <= 0f) continue;
                rate += give;
                // giving tires, but slowly: a strong Avir emptied its rest in half an hour - now at most a quarter an hour
                if (src.needs?.rest != null)
                    src.needs.rest.CurLevel = Mathf.Max(0f, src.needs.rest.CurLevel - Mathf.Min(give * 0.0004f, 0.0001f * delta));
            }
            if (r.focus > 0 && ext != null) rate += r.focus * (ext.Total / MapComponent_ExternalFlow.Calm) * 0.03f * delta;
            // a building or artifact as the conduit gives of its own charge
            if (conduit != null) rate += TakeFrom(conduit, 0.05f * delta);
            // crystals laid in the circle are sources too: one breaks open whenever the bank runs low
            if (bank < CrystalEnergy && runTicks % 120 < delta) BreakCrystal();
            // the Blood rune beside another verb: a life in the circle feeds the ritual when it runs low
            BloodFeed(r, delta);
            rate *= r.FlowCap;
            // artifacts laid in their component circles strengthen the whole flow
            float gained = rate * r.Power(drawQuality) * (1f + ArtifactBoost(delta));
            bank += gained;
            if (ext != null && r.actWeight > 0f) ext.deeds = Mathf.Max(ext.deeds, r.actWeight);
            // raw flow: what is drawn in has nowhere to go and pours out into the map
            if (r.RawFlow && ext != null) ext.deeds += gained * RawSpill;

            // risk, checked every 10 seconds
            if (runTicks % 600 < delta && Backlash(p, r, ext)) return false;

            if (parent.IsHashIntervalTick(90, delta)) FleckMaker.ThrowLightningGlow(parent.DrawPos, map, 0.6f + 0.3f * (int)Size);
            // motes of the flow drift up out of the drawing while it works
            if (parent.IsHashIntervalTick(30, delta)) RitualFx.Mote(parent.DrawPos + Rand.InsideUnitCircleVec3 * Radius * 0.9f, map, Rand.Range(0.35f, 0.7f), AspectColor);

            if (r.RawFlow) return RawFlowTick(p, r, ext, delta);
            if (r.Dome) return DomeTick(r, ext, delta);

            // an anchored ritual lasts a day at most
            if (r.anchor > 0 && !engraved && runTicks >= AnchorTicks) { End("the anchor's day is over - chalk holds an anchored ritual a day at most."); return false; }

            // the verbs, in order of the rings
            stepTicks += delta;
            while (verbIndex < r.verbs.Count)
            {
                RuneDef v = r.verbs[verbIndex];
                bool done;
                switch (v.kind)
                {
                    case RuneKind.Fill: done = DoFill(r); if (vortex > 0f) return false; break;
                    case RuneKind.Purge: done = r.Has(RuneKind.Blood) || r.Has(RuneKind.Shroud) || DoPurge(r); break;   // beside blood or shroud it takes the gift back instead
                    case RuneKind.Transfer: done = DoTransfer(r); break;
                    case RuneKind.Hold: done = DoHold(r, delta); break;
                    case RuneKind.Drain: done = DoDrain(r, delta); break;
                    case RuneKind.Transmute: done = r.Seal == null ? DoCurrent(r, delta) : DoTransmute(r); if (!Running) return false; break;
                    case RuneKind.Release: done = DoRelease(r); if (!Running) return false; break;
                    case RuneKind.Lift: done = r.Throw || DoLift(r, delta); break;
                    case RuneKind.Growth: done = DoGrowth(r, delta); break;
                    case RuneKind.Reforge: done = DoReforge(r); if (!Running) return false; break;
                    case RuneKind.Infuse: done = IsPath(r) ? DoPath(r, delta) : DoInfuse(r, delta); if (!Running) return false; break;
                    case RuneKind.Blood: done = DoBlood(r, delta); if (!Running) return false; break;
                    case RuneKind.Shroud: done = DoShroud(r, delta); if (!Running) return false; break;
                    default: done = true; break;   // not made yet: passes
                }
                if (!done) return true;
                verbIndex++;
                stepTicks = 0;
            }
            // a self-sustaining engraving does its work and rests until it is needed again
            if (conduit != null) { End(null); return false; }
            if (r.anchor > 0) { verbIndex = 0; return true; }   // anchored: again while energy flows
            End("complete.");
            return false;
        }

        private IEnumerable<Pawn> Sources()
        {
            if (performer != null) yield return performer;
            foreach (Pawn q in parent.Map.mapPawns.FreeColonistsSpawned)
                if (q != performer && !q.Downed && Inside(q.Position) && !held.Contains(q)) yield return q;
        }

        /// <summary>Poor composure against an unstable ritual: collapse, burns, at worst a blast.
        /// A safety rune throws the ritualist clear first.</summary>
        private bool Backlash(Pawn p, RitualReading r, MapComponent_ExternalFlow ext)
        {
            float risk = r.Risk(ext == null ? 0f : ext.Total / MapComponent_ExternalFlow.Calm, drawQuality, bank) * (p != null ? Avir.Linarian.LinarianRitual.RiskFactor(p) : 1f);
            float composure = p != null ? GameComponent_AvirComposure.For(p, drawn, this) : ConduitComposure;
            if (DevNoBacklash || (p != null && Avir.Linarian.LinarianRitual.NoBacklash(p)) || risk <= composure || !Rand.Chance((risk - composure) * 0.4f)) return false;
            if (p == null || p.Faction == Faction.OfPlayer) Arlia.Say("backlash");
            Map map = parent.Map;
            if (p == null)
            {
                // a thing as the conduit: it takes the backlash itself
                Thing c = conduit;
                FleckMaker.ThrowLightningGlow(c.DrawPos, map, 2f);
                if (c.def.useHitPoints) c.TakeDamage(new DamageInfo(DamageDefOf.Burn, 20f + 40f * (risk - composure)));
                End("the stream broke through its conduit.");
                return true;
            }
            if (r.safety > 0)
            {
                IntVec3 out1 = CellFinder.RandomClosewalkCellNear(parent.Position, map, (int)Radius + 3, c => !Inside(c));
                p.Position = out1;
                p.Notify_Teleported(true, false);
                p.stances?.stunner?.StunFor(180, null, false, false);
                End("the stream broke - the safety rune threw " + p.LabelShort + " clear.");
                return true;
            }
            Hediff h = HediffMaker.MakeHediff(RitualDefOf.Avir_RitualBacklash, p);
            h.Severity = Mathf.Clamp(risk - composure, 0.1f, 1f);
            p.health.AddHediff(h);
            GameComponent_AvirComposure.Lashed(p, h.Severity);
            if (risk - composure > 0.8f)
            {
                float extra = r.RawFlow ? Mathf.Min(bank / 150f, 6f) : 0f;
                GenExplosion.DoExplosion(parent.Position, map, 1.9f + (int)Size + extra, DamageDefOf.Bomb, null, 20 + 15 * (int)Size + Mathf.RoundToInt(extra * 5f),
                    ignoredThings: r.barrier > 0 ? Spared(map, p) : null);
                if (r.Storm) StrikeAround(map, r, 2 + r.focus / 3);
                End("the stream collapsed in a blast.");
            }
            else End("the stream collapsed - " + p.LabelShort + " is burnt by it.");
            return true;
        }

        // ---------------------------------------------------------------- raw flow (no verb, only focus)

        /// <summary>External flow (%) each unit of held energy pushes into the map.</summary>
        private const float RawSpill = 40f;
        /// <summary>A raw ritual without an anchor breaks up on its own after an hour.</summary>
        private const int RawDuration = 2500;

        private bool RawFlowTick(Pawn p, RitualReading r, MapComponent_ExternalFlow ext, int delta)
        {
            if (p == null) { End("raw stream needs a living conduit."); return false; }
            Map map = parent.Map;
            if (r.Storm && runTicks % 250 < delta)
            {
                // the storm comes once the ritual has drawn for a few seconds
                if (runTicks >= 600)
                {
                    string cur = map.weatherManager.curWeather?.defName ?? "";
                    if (!cur.Contains("Thunderstorm"))
                    {
                        WeatherDef storm = DefDatabase<WeatherDef>.GetNamedSilentFail("RainyThunderstorm");
                        if (storm != null)
                        {
                            map.weatherManager.TransitionTo(storm);
                            Messages.Message("The ritual circle calls a storm.", parent, MessageTypeDefOf.NeutralEvent, false);
                        }
                    }
                }
                // lightning: each strike vents some of the held energy
                if (Rand.Chance(Mathf.Min(0.9f, 0.12f * r.focus / RitualReading.StormFocus + bank / 2000f)))
                {
                    StrikeAround(map, r, 1);
                    bank = Mathf.Max(0f, bank - 40f);
                }
            }
            int limit = r.anchor > 0 ? 60000 : RawDuration;
            if (runTicks >= limit)
            {
                if (r.Eye && bank > 50f)
                {
                    float left = Hediff_StormMantle.FillWorn(p, bank * Hediff_StormMantle.PerBank);
                    Hediff_StormMantle.Grant(p, left);
                    StrikeAround(map, r, 3);
                    bank = 0f;
                    End("the storm is sealed into " + p.LabelShort + ".");
                    return false;
                }
                if (r.Storm && bank > 40f) StrikeAround(map, r, Mathf.Min(8, Mathf.RoundToInt(bank / 80f)));
                bank = 0f;
                End("the held stream dispersed.");
                return false;
            }
            return true;
        }

        /// <summary>Lightning around the circle: outside it, never on a roof.</summary>
        private void StrikeAround(Map map, RitualReading r, int count)
        {
            float near = Radius + 3f, far = Radius + 6f + r.focus * 1.5f;
            for (int i = 0; i < count; i++)
            {
                if (!CellFinder.TryFindRandomCellNear(parent.Position, map, Mathf.CeilToInt(far),
                        c => !c.Roofed(map) && (c - parent.Position).LengthHorizontal >= near, out IntVec3 cell)) return;
                map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, cell));
            }
        }

        // ---------------------------------------------------------------- verbs

        private float Reach(RitualReading r) => Radius + r.breadth * 4f;
        public const int AnchorTicks = 60000;

        private IEnumerable<ArtifactState> ChargeTargets(RitualReading r)
        {
            Map map = parent.Map;
            float reach = Reach(r);
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(Center, map, reach, true).Concat(Threaded))
            {
                if (t.TryGetComp<CompArtifact>()?.state is ArtifactState s1) yield return s1;
                if (t.TryGetComp<CompArtifactHost>() is CompArtifactHost h) foreach (ArtifactState s2 in h.slots) if (s2 != null) yield return s2;
                if (t is Pawn pw && pw.Faction == Faction.OfPlayer) foreach (CompArtifactSocket so in ArtifactWearCache.Worn(pw)) if (so.inlaid != null) yield return so.inlaid;
            }
        }

        private bool DoFill(RitualReading r)
        {
            // a sanctuary under the circle drinks first: the binding fills it with its element
            if (Avir.Magic.AvirMagic.On && parent.TryGetComp<Avir.Magic.CompSanctuary>() is Avir.Magic.CompSanctuary sanct && sanct.WantsPour)
            {
                float sg = sanct.Pour(bank);
                bank -= sg; spentTotal += sg;
                if (bank <= 0.0001f) return false;
            }
            // spire cores: a dead sphere kindles from the energy poured into it; a kindled one charges
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), true).Concat(Threaded))
            {
                Avir.Spire.CompSpireCore core = t.TryGetComp<Avir.Spire.CompSpireCore>();
                if (core == null || (core.kindled && core.Missing <= 0.01f)) continue;
                float g = core.Pour(bank, r, drawQuality, performer);
                bank -= g; spentTotal += g;
                if (bank <= 0.0001f) return false;
            }
            // mages in reach: their cores drink first (magic on)
            if (Avir.Magic.AvirMagic.On)
                foreach (Pawn mp in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), true).OfType<Pawn>().ToList())
                {
                    if (mp.Faction != parent.Faction && mp.Faction != Faction.OfPlayer) continue;
                    Avir.Magic.Hediff_ArcaneCore mc = Avir.Magic.Mages.Core(mp);
                    if (mc == null) continue;
                    // no ceiling to a core: a ritual pours in up to one reserve's worth for each mage
                    float mg = Mathf.Min(bank, mc.Capacity);
                    mc.Add(mg);
                    bank -= mg; spentTotal += mg;
                    if (bank <= 0.0001f) return false;
                }
            foreach (ArtifactState st in ChargeTargets(r))
            {
                if (st.charge >= 0.999f) continue;
                float cap = Mathf.Max(1f, ArtifactCharge.Capacity(st, null));
                float need = (1f - st.charge) * cap;
                float give = Mathf.Min(need, bank);
                st.charge = Mathf.Min(1f, st.charge + give / cap);
                bank -= give; spentTotal += give;
                if (bank <= 0.0001f) return false;
            }
            // batteries
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), true).Concat(Threaded))
            {
                // a core hearth: mended if cracked, else charged (one unit of ritual energy is WdPerUnit of current)
                Avir.Spire.CompCoreHearth hearth = t.TryGetComp<Avir.Spire.CompCoreHearth>();
                if (hearth != null)
                {
                    if (!hearth.cracked && hearth.AmountCanAccept < 0.5f) continue;
                    float hg = GiveTo(t, bank);
                    bank -= hg; spentTotal += hg;
                    if (bank <= 0.0001f) return false;
                    continue;
                }
                CompEnergyKeeper k = t.TryGetComp<CompEnergyKeeper>();
                if (k != null && !k.IsFull)
                {
                    float kg = k.Give(bank);
                    bank -= kg; spentTotal += kg;
                    if (bank <= 0.0001f) return false;
                    continue;
                }
                CompCrystalBattery b = t.TryGetComp<CompCrystalBattery>();
                if (b == null || b.IsFull) continue;
                float need = CompCrystalBattery.Capacity - b.Stored;
                float give = Mathf.Min(need, bank);
                b.stored = b.Stored + give;
                bank -= give; spentTotal += give;
                if (bank <= 0.0001f) return false;
            }
            // psycasters: their psyfocus
            if (!FillPsyfocus(r)) return false;
            // with focus, artifacts and amulets in reach snatch experience out of the map's flow
            if (r.focus > 0 && !GrabXp(r)) return false;
            // nothing was there to fill at all, unclosed, with the flow pouring in: the vortex
            if (r.VortexRisk && spentTotal <= 0.001f && stepTicks > 600) { StartVortex(); return true; }
            if (r.VortexRisk && spentTotal <= 0.001f) return false;
            return true;   // everything is full: "a ritual works while there is something to fill"
        }

        private const float PurgeCost = 30f;

        private bool DoPurge(RitualReading r)
        {
            if (!PurgeMinds(r)) return false;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), true))
            {
                foreach (object v in Vessels(t))
                {
                    if (!NeedsPurge(v)) continue;
                    if (bank < PurgeCost) return false;
                    bank -= PurgeCost; spentTotal += PurgeCost;
                    Purge(v);
                    FleckMaker.ThrowLightningGlow(t.DrawPos, parent.Map, 1f);
                }
            }
            return true;
        }

        /// <summary>Artifacts and amulets a thing holds: a loose artifact, a garment's or weapon's
        /// socket, an amulet, and everything a pawn standing here wears.</summary>
        public static IEnumerable<object> Vessels(Thing t)
        {
            if (t.TryGetComp<CompArtifact>()?.state is ArtifactState s1) yield return s1;
            if (t.TryGetComp<CompArtifactSocket>()?.inlaid is ArtifactState s2) yield return s2;
            if (t.TryGetComp<CompAvirAmulet>() is CompAvirAmulet a && a.HasCore) yield return a;
            if (t is Pawn p)
            {
                if (p.apparel != null) foreach (Apparel ap in p.apparel.WornApparel) foreach (object v in Vessels(ap)) yield return v;
                if (p.equipment?.Primary != null) foreach (object v in Vessels(p.equipment.Primary)) yield return v;
            }
        }

        public static string VesselLabel(object v, Thing holder) =>
            v is CompAvirAmulet a ? a.parent.LabelShortCap : holder.LabelShortCap + "'s artifact";

        private static bool NeedsPurge(object v) =>
            v is ArtifactState s ? s.opened.Count > 0 || s.charge < 1f : v is CompAvirAmulet a && (a.opened?.Count > 0 || a.tkLevel > 0 || a.slotOpen);

        /// <summary>Back to how it was made, every point returned, charge full.</summary>
        public static void Purge(object v)
        {
            if (v is ArtifactState s)
            {
                s.opened.Clear();
                s.burntPoints = 0;
                s.dormantArms.Clear();
                s.charge = 1f;
                ArtifactBonusCache.ClearAll();
            }
            else if (v is CompAvirAmulet a)
            {
                a.opened?.Clear();
                a.slotOpen = false;
                a.tkLevel = 0;
                a.spentLedger = 0;
            }
        }

        public static float Xp(object v) => v is ArtifactState s ? s.xpEarned : v is CompAvirAmulet a ? a.xpEarned : 0f;
        private static void SetXp(object v, float x) { if (v is ArtifactState s) s.xpEarned = x; else if (v is CompAvirAmulet a) a.xpEarned = x; }

        /// <summary>Experience that stands behind no opened cell.</summary>
        public static float FreeXp(object v)
        {
            ArtifactoricsSettingsDef S = ArtifactoricsSettingsDef.Main;
            if (S == null) return 0f;
            int spent = v is ArtifactState s ? s.PointsSpent : v is CompAvirAmulet a ? AmuletDevelopment.PointsSpent(a) : 0;
            return Mathf.Max(0f, Xp(v) - S.XpForPoints(spent));
        }

        private object fromVessel, toVessel;

        private bool DoTransfer(RitualReading r)
        {
            if (fromVessel == null || toVessel == null)
            {
                fromVessel = transferFrom == null ? null : Vessels(transferFrom).FirstOrDefault();
                toVessel = transferTo == null ? null : Vessels(transferTo).FirstOrDefault();
                if (fromVessel == null || toVessel == null || fromVessel == toVessel) { End("nothing to transfer between."); return true; }
            }
            // with a purge rune after it, everything moves (the source is purged); otherwise only the free experience
            bool purgeAfter = r.verbs.Skip(verbIndex + 1).Any(x => x.kind == RuneKind.Purge);
            float amount = purgeAfter ? Xp(fromVessel) : FreeXp(fromVessel);
            if (amount <= 1f) { fromVessel = toVessel = null; return true; }
            float cost = amount / 1000f;
            if (bank < cost) return false;
            bank -= cost; spentTotal += cost;
            SetXp(fromVessel, Xp(fromVessel) - amount);
            SetXp(toVessel, Xp(toVessel) + amount);
            if (purgeAfter) Purge(fromVessel);
            Messages.Message(amount.ToString("N0") + " experience streamed from " + transferFrom.LabelShortCap + " into " + transferTo.LabelShortCap + ".",
                parent, MessageTypeDefOf.PositiveEvent, false);
            fromVessel = toVessel = null;
            return true;
        }

        // ---------------------------------------------------------------- crystals and the Drain rune

        /// <summary>Energy one crystal of psychic energy gives a ritual.</summary>
        public const float CrystalEnergy = 150f;
        /// <summary>Pylon charge one crystal holds (only part of the drained charge is caught).</summary>
        private const float ChargePerCrystal = 100f;

        /// <summary>Energy one unit of Avir feathers gives a ritual.</summary>
        public const float FeatherEnergy = 6f;

        private void BreakCrystal()
        {
            Thing c = FuelCells(ComponentKind.Crystal).SelectMany(x => x.GetThingList(parent.Map)).FirstOrDefault(t => t.def == PsychicDefOf.Avir_PsyCrystal);
            if (c == null) { BurnFeathers(); return; }
            Thing one = c.SplitOff(1);
            one.Destroy();
            bank += CrystalEnergy;
            FleckMaker.ThrowLightningGlow(c.Spawned ? c.DrawPos : parent.DrawPos, parent.Map, 1f);
        }

        /// <summary>Avir feathers laid in the circle hold a trace of their owner's psychic flow: ten at a time
        /// burn away into the ritual when its energy runs low.</summary>
        private void BurnFeathers()
        {
            ThingDef feathers = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_AvirFeatheredFur");
            if (feathers == null) return;
            Thing f = FuelCells(ComponentKind.Feathers).SelectMany(x => x.GetThingList(parent.Map)).FirstOrDefault(t => t.def == feathers);
            if (f == null) { DrawBatteries(); return; }
            int n = Mathf.Min(10, f.stackCount);
            f.SplitOff(n).Destroy();
            // the wings' own energy goes most readily back into artifacts: a filling ritual gets twice as much
            bank += n * FeatherEnergy * (Reading.Has(RuneKind.Fill) ? 2f : 1f);
            FleckMaker.ThrowLightningGlow(parent.DrawPos, parent.Map, 0.8f);
        }

        /// <summary>Draws the charge out of psychic pylons in reach; part of it is caught as crystals at the centre.</summary>
        private bool DoDrain(RitualReading r, int delta)
        {
            float reach = Reach(r);
            IntVec3 at = Center;
            // the living and the threaded give theirs first; it feeds the steps after (or, as the last step, crystals)
            bool tookAny = DrainLiving(r, delta) | DrainThreads(r, delta);
            CompPsychicPylon py = CompPsychicPylon.All.FirstOrDefault(x => x.parent.Map == parent.Map && x.charge > 1f
                                                                    && (x.parent.Position - at).LengthHorizontal <= reach);
            if (py == null) return !tookAny && r.anchor == 0;
            float cost = 0.3f * delta;
            if (bank < cost) return false;
            bank -= cost; spentTotal += cost;
            float took = py.Take(2f * delta);
            // a better drawing catches more of it
            crystalProgress += took * Mathf.Clamp(drawQuality, 0.2f, 1.2f) * 0.5f;
            if (parent.IsHashIntervalTick(60, delta)) FleckMaker.ThrowLightningGlow(py.parent.DrawPos, parent.Map, 0.8f);
            while (crystalProgress >= ChargePerCrystal)
            {
                crystalProgress -= ChargePerCrystal;
                Thing c = ThingMaker.MakeThing(PsychicDefOf.Avir_PsyCrystal);
                GenPlace.TryPlaceThing(c, parent.Position, parent.Map, ThingPlaceMode.Near);
            }
            return false;
        }

        /// <summary>Value of the material taken in and not yet turned into whole units (pooled across every input).</summary>
        public float transmuteCarry;

        /// <summary>Material lying in the circle becomes what the seal names, by value; a strong circle loses nothing.
        /// Every input is pooled: three stacks of steel, or steel, silver and jade together, go into the one result - what
        /// one input lacks for a whole unit the next one makes up. Only what is left after the last whole unit is lost.</summary>
        private bool DoTransmute(RitualReading r)
        {
            ThingDef target = r.Seal;
            if (target == null) return true;
            Map map = parent.Map;
            List<ThingDef> inputs = r.Inputs;
            float outValue = Mathf.Max(0.01f, target.BaseMarketValue);
            Thing src = FuelCells(ComponentKind.Material).SelectMany(c => c.GetThingList(map)).FirstOrDefault(t =>
                t.def.category == ThingCategory.Item && t.def != target && Transmutation.Allowed(t.def)
                && (inputs.Count > 0 ? inputs.Contains(t.def) : Transmutation.Materials.Contains(t.def)));
            if (src == null)
            {
                transmuteCarry = 0f;   // nothing left: the remainder is lost into the stream
                // an anchor repeats the ritual while energy flows, but with no material left there is nothing to repeat:
                // the ritualist would stand in the circle until the anchor's day ran out
                if (r.anchor > 0 && performer != null) End("no material left to transmute.");
                return true;
            }
            float yield = r.TransmuteYield(drawQuality);
            float per = Mathf.Max(0.0001f, src.def.BaseMarketValue * yield);
            // a handful at a time: enough (with what is carried) for up to five units of the result
            int take = Mathf.Clamp(Mathf.CeilToInt((5f * outValue - transmuteCarry) / per), 1, src.stackCount);
            float pooled = transmuteCarry + take * per;
            int outs = Mathf.FloorToInt(pooled / outValue + 0.0001f);
            float cost = outValue * Mathf.Max(1, outs) / 25f;
            if (bank < cost) return false;
            bank -= cost; spentTotal += cost;
            src.SplitOff(take).Destroy();
            transmuteCarry = pooled - outs * outValue;
            if (outs > 0)
            {
                Thing made = ThingMaker.MakeThing(target);
                made.stackCount = outs;
                GenPlace.TryPlaceThing(made, parent.Position, map, ThingPlaceMode.Near);
            }
            FleckMaker.ThrowLightningGlow(parent.DrawPos, map, 1.2f);
            return false;
        }

        private bool DoHold(RitualReading r, int delta)
        {
            Faction own = parent.Faction ?? Faction.OfPlayer;
            bool any = false;
            foreach (Pawn q in parent.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (q.Dead || !q.HostileTo(own) || !q.RaceProps.IsFlesh || (q.Position - Center).LengthHorizontal > Reach(r)) continue;
                any = true;
                q.stances?.stunner?.StunFor(120, performer, false, false);
            }
            float cost = 0.02f * delta;
            if (bank < cost) return false;
            bank -= cost; spentTotal += cost;
            return !any && r.anchor == 0;
        }

        // ---------------------------------------------------------------- the Trap and the weather

        public override void CompTickRare()
        {
            base.CompTickRare();
            Map map = parent.Map;
            if (map == null) return;
            // chalk under the open sky is washed away
            if (Drawn && !engraved && !parent.Position.Roofed(map) && (map.weatherManager.RainRate > 0.05f || map.weatherManager.SnowRate > 0.05f))
            {
                washed += 250f / 2500f;   // an hour of rain or snow erases it
                if (washed >= 1f)
                {
                    drawn = null; drawQuality = 0f; armed = false;
                    ReleaseAll("the rain washed the chalk away");
                    if (Running) End("the rain washed the chalk away.");
                    Messages.Message("Rain and snow washed the ritual circle away.", parent, MessageTypeDefOf.NegativeEvent, false);
                    Arlia.Say("washed");
                }
            }
            TrapTick();
            VortexTick();
            SelfSustainTick();
        }

        private void TrapTick()
        {
            if (!armed || !Drawn || !Reading.trap) { if (held.Count > 0) ReleaseAll("the cage is open"); return; }
            Map map = parent.Map;
            Faction own = parent.Faction ?? Faction.OfPlayer;
            // whoever living walks in is held: only the circle's own cells are looked at, as a vanilla trap does
            foreach (IntVec3 c in GenRadial.RadialCellsAround(parent.Position, Radius, true))
            {
                if (!c.InBounds(map)) continue;
                List<Thing> things = map.thingGrid.ThingsListAtFast(c);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Pawn q && !q.Dead && q.RaceProps.IsFlesh && Catches(q, own) && !held.Contains(q))
                    {
                        held.Add(q);
                        if (!q.health.hediffSet.HasHediff(RitualDefOf.Avir_RitualHeld)) q.health.AddHediff(RitualDefOf.Avir_RitualHeld);
                        Messages.Message(q.LabelShortCap + " is caught in the Trap.", q, MessageTypeDefOf.PositiveEvent, false);
                        Arlia.Say("caught");
                    }
            }
            // an engraved Trap draws in the foes that pass near
            if (engraved) Attract(map, own);
            for (int i = held.Count - 1; i >= 0; i--)
            {
                Pawn q = held[i];
                if (q == null || q.Dead || !q.Spawned || q.Map != map) { Release(q, null); continue; }
                // the full Trap shuts the flow in: the prisoner is not drained, it is only held
                q.stances?.stunner?.StunFor(300, null, false, false);
                TrapVariantTick(q, Reading);
                if (!held.Contains(q)) continue;
                if (!Inside(q.Position)) { q.Position = parent.Position; q.Notify_Teleported(true, false); }
                // the prisoner presses on the cage: a better drawing holds longer; a pulsing cage drains it too much to try
                float tries = 0.002f / Mathf.Max(0.2f, drawQuality) * (Reading.trapKind == TrapKind.Pulse ? 0.25f : 1f);
                if (Rand.Chance(tries)) Release(q, q.LabelShortCap + " broke out of the Trap.");
            }
        }

        /// <summary>Foes - and the colony's own prisoners and slaves when they rise.</summary>
        private static bool Catches(Pawn q, Faction own) =>
            q.HostileTo(own)
            || (q.IsPrisonerOfColony && PrisonBreakUtility.IsPrisonBreaking(q))
            || (q.IsSlaveOfColony && SlaveRebellionUtility.IsRebelling(q));

        public const float AttractReach = 6f;

        /// <summary>Foes within a few cells of an engraved, armed Trap are dragged a step towards it each rare tick.</summary>
        private void Attract(Map map, Faction own)
        {
            float reach = Radius + AttractReach;
            foreach (IAttackTarget at in map.attackTargetsCache.TargetsHostileToFaction(own).ToList())
            {
                if (!(at.Thing is Pawn q) || !q.Spawned || q.Dead || q.Downed || !q.RaceProps.IsFlesh || held.Contains(q)) continue;
                float d = (q.Position - parent.Position).LengthHorizontal;
                if (d > reach || d <= Radius || !Rand.Chance(0.5f)) continue;
                IntVec3 step = q.Position + new IntVec3(System.Math.Sign(parent.Position.x - q.Position.x), 0, System.Math.Sign(parent.Position.z - q.Position.z));
                if (!step.InBounds(map) || !step.Standable(map)) continue;
                q.pather?.StopDead();
                q.Position = step;
                q.Notify_Teleported(false, false);
                RitualFx.Mote(q.DrawPos, map, 0.5f);
            }
        }

        private void Release(Pawn q, string msg)
        {
            held.Remove(q);
            pulsesTaken.Remove(q);
            Hediff h = q?.health?.hediffSet?.GetFirstHediffOfDef(RitualDefOf.Avir_RitualHeld);
            if (h != null) q.health.RemoveHediff(h);
            if (msg != null) Messages.Message(msg, q, MessageTypeDefOf.ThreatSmall, false);
        }

        private void ReleaseAll(string why)
        {
            foreach (Pawn q in held.ToList()) Release(q, q != null ? q.LabelShortCap + " is free: " + why + "." : null);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            ReleaseAll("the circle is gone");
        }

        // ---------------------------------------------------------------- UI

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = schema == null ? "Choose ritual..." : "Ritual: " + schema.name,
                defaultDesc = "Open the ritual book: read the rituals you know and choose one to draw into this circle.",
                icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Fill")?.Ink ?? BaseContent.BadTex,
                action = () => Find.WindowStack.Add(new Dialog_RitualBook(this))
            };
            if (Drawn && Reading.trap)
                yield return new Command_Toggle
                {
                    defaultLabel = "Arm the Trap",
                    defaultDesc = "While armed, any living foe that steps into the circle is held in it. The full Trap shuts the stream in: the prisoner is not drained, only held, until the chalk is washed away, it dies or it breaks out.",
                    icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Safety")?.Ink ?? BaseContent.BadTex,
                    isActive = () => armed,
                    toggleAction = () => armed = !armed
                };
            else if (Drawn)
            {
                var cmd = new Command_Action
                {
                    defaultLabel = Running ? "Stop ritual" : "Begin ritual...",
                    defaultDesc = "Begin with the conduit chosen in the ritual book, or choose one here: a colonist, or an artifact, crystal battery, artifact building or psychic pylon at the centre of the circle (it gives its own charge). The performer stands in the centre; everyone standing in the circle gives energy too.",
                    icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Focus")?.Ink ?? BaseContent.BadTex,
                    action = Running ? (System.Action)(() => { performer?.jobs?.EndCurrentJob(JobCondition.InterruptForced); End("stopped."); })
                        : () => { if (chosenPerformer == null && chosenThing == null) BeginMenu(); else { string w = BeginChosen(); if (w != null) Messages.Message("Cannot begin: " + w + ".", parent, MessageTypeDefOf.RejectInput, false); } }
                };
                string why = Running ? null : CannotPerform(null);
                if (why != null) cmd.Disable(why);
                yield return cmd;
            }
            foreach (Gizmo g in AutoGizmos()) yield return g;
            foreach (Gizmo g in MoreGizmos()) yield return g;
            if (Prefs.DevMode)
            {
                yield return new Command_Action { defaultLabel = "Dev: draw now", action = DevDrawNow };
                foreach (Gizmo g in DevGizmos()) yield return g;
                yield return new Command_Action { defaultLabel = "Dev: learn hidden rituals", action = () => { foreach (RitualPresetDef p in RitualPresetDef.All.Where(x => x.hidden)) RitualDiscoveries.Learn(p); } };
            }
        }

        private void BeginMenu()
        {
            RitualReading r = Reading;
            var opts = new List<FloatMenuOption>();
            foreach (Pawn p in parent.Map.mapPawns.FreeColonistsSpawned.Where(x => !x.Downed && !x.Drafted))
            {
                Pawn pp = p;
                string label = p.LabelShort + " (sensitivity " + p.GetStatValue(StatDefOf.PsychicSensitivity).ToStringPercent()
                               + ", composure " + GameComponent_AvirComposure.For(p, drawn, this).ToStringPercent() + ")";
                opts.Add(new FloatMenuOption(label, () => Prepare(pp)));
            }
            // a thing of energy at the centre can hold the ritual instead: an artifact, a crystal battery, an artifact building, a pylon
            string cannot = CannotSustain(false);
            foreach (Thing t in ConduitCandidates())
            {
                Thing tt = t;
                string label = "Through " + t.LabelShort + " (its own charge, composure " + ConduitComposure.ToStringPercent() + ")";
                opts.Add(cannot == null
                    ? new FloatMenuOption(label, () => { chosenThing = tt; chosenPerformer = null; string w = BeginChosen(); if (w != null) Messages.Message("Cannot begin: " + w + ".", parent, MessageTypeDefOf.RejectInput, false); })
                    : new FloatMenuOption(label + ": " + cannot, null));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("No free colonist, and no artifact or crystal battery at the centre", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void TransferMenu(Pawn p, IntVec3 at)
        {
            var holders = GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Radius, true)
                .Where(t => Vessels(t).Any()).ToList();
            var opts = new List<FloatMenuOption>();
            foreach (Thing a in holders)
                foreach (Thing b in holders)
                {
                    if (a == b) continue;
                    Thing aa = a, bb = b;
                    object va = Vessels(a).First();
                    opts.Add(new FloatMenuOption("From " + VesselLabel(va, a) + " (" + Xp(va).ToString("N0") + " xp, free " + FreeXp(va).ToString("N0") + ") into " + VesselLabel(Vessels(b).First(), b),
                        () => Order(p, at, aa, bb, 0)));
                }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("Put two amulets or artifacts into the circle (or stand their wearers in it)", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private void Order(Pawn p, IntVec3 at, Thing from, Thing to, int mode)
        {
            Begin(p, at, from, to, mode);
            Job job = JobMaker.MakeJob(RitualDefOf.Avir_PerformRitual, parent);
            if (!p.jobs.TryTakeOrderedJob(job, JobTag.Misc)) End(null);
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            sb.Append(RitualCircles.Label(Size).CapitalizeFirst()).Append(" ritual circle");
            StreamNet net = parent.Spawned ? Net : null;
            if (net != null) sb.Append(" (stream network: ").Append(net.Stored.ToString("0")).Append(" / ").Append(net.Capacity.ToString("0")).Append(")");
            if (schema == null) sb.Append(": no ritual chosen");
            else
            {
                sb.Append(": ").Append(schema.name);
                if (NeedsDrawing) sb.Append(drawWork > 0f ? " - drawn " + (drawWork / DrawWorkNeeded).ToStringPercent() : " - waits to be drawn");
                else sb.Append(engraved ? " - engraved, quality " : " - drawn in chalk, quality ").Append(drawQuality.ToStringPercent());
                if (NeedsDrawing && engraveNext) sb.Append(" (engraving)");
            }
            if (washed > 0f && Drawn) sb.Append("\nThe chalk is washing away: ").Append(washed.ToStringPercent());
            if (Drawn && !Running)
            {
                string lack = MissingMaterial();
                if (lack != null) sb.Append("\nWaits for material: ").Append(lack);
            }
            if (armed) sb.Append("\nTrap armed").Append(held.Count > 0 ? ", holding " + held.Count : "");
            if (Running)
            {
                sb.Append("\nPerformed by ").Append(performer?.LabelShort ?? conduit.LabelShort);
                if (Reading.RawFlow) sb.Append(", energy held ").Append(bank.ToString("0"));
                else sb.Append(", energy spent ").Append(spentTotal.ToString("0"));
            }
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(parent.Map);
            if (ext != null) sb.Append("\n").Append(("External stream: " + ext.Total.ToString("0") + "% (" + MapComponent_ExternalFlow.Tier(ext.Total) + ")").Colorize(FlowGauge.ColorOf(ext.Total)));
            return sb.ToString();
        }

        private RitualSchema aspectFor;
        private Color aspectColor;

        /// <summary>The ritual's colour: the blend of its aspects (cached per drawing).</summary>
        public Color AspectColor
        {
            get
            {
                RitualSchema s = drawn ?? schema;
                if (s != aspectFor) { aspectFor = s; aspectColor = RitualAspects.ColorOfRitual(s); }
                return aspectColor;
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            DrawMore();
            Vector3 c = parent.DrawPos;
            c.y = AltitudeLayer.FloorEmplacement.AltitudeFor() + 0.01f;
            RitualSchema s = drawn ?? schema;
            bool live = Running || held.Count > 0;
            float alpha = drawn != null ? 0.85f * (1f - washed * 0.7f) : 0.25f;
            // chalk is white; an engraving is cut dark into the stone
            Color asp = AspectColor;
            Color chalk = live ? new Color(asp.r, asp.g, asp.b, alpha) : drawn != null && engraved ? new Color(0.18f, 0.16f, 0.15f, 0.9f) : new Color(0.92f, 0.9f, 0.85f, alpha);
            bool carved = drawn != null && engraved && FlowTex.Engraved != null && !live;
            Material ringMat = carved ? MaterialPool.MatFrom(FlowTex.Engraved, ShaderDatabase.Transparent, Color.white)
                                      : MaterialPool.MatFrom(RitualTex.Ring, ShaderDatabase.Transparent, chalk);
            float R = Radius * 0.96f;
            DrawRing(c, R * 2f, ringMat);
            // the ornamental rim lies just outside the last section (not on an engraved circle: the groove has its own look)
            if (!carved && RitualTex.Rim != RitualTex.Ring)
                DrawRing(c, R * 2f * RitualTex.RimGrow, MaterialPool.MatFrom(RitualTex.Rim, ShaderDatabase.Transparent, chalk));
            int n = s != null && s.rings.Count > 0 ? s.rings.Count : RitualCircles.Rings(Size);
            float core = R * 0.28f;
            for (int i = 0; i < n; i++)
            {
                float rr = core + (R - core) * i / n;
                if (i > 0) DrawRing(c, rr * 2f, ringMat);
            }
            DrawRing(c, core * 2f, ringMat);
            if (s == null) return;
            Color glyph = live ? new Color(Mathf.Min(1f, asp.r * 1.2f + 0.1f), Mathf.Min(1f, asp.g * 1.2f + 0.1f), Mathf.Min(1f, asp.b * 1.2f + 0.1f), alpha) : carved ? new Color(0.10f, 0.09f, 0.085f, 0.92f) : chalk;
            Color minor = new Color(glyph.r, glyph.g, glyph.b, glyph.a * (carved ? 0.85f : 0.7f));
            // the core seal under the performer
            DrawGlyph(c, core * 1.6f, RitualOrnament.CoreGlow, new Color(glyph.r, glyph.g, glyph.b, glyph.a * 0.75f));
            for (int i = 0; i < n; i++)
            {
                List<RuneDef> runes = i < s.rings.Count ? s.rings[i].runes : new List<RuneDef>();
                int per = i < s.rings.Count ? s.rings[i].Slots : 1;
                float inner = core + (R - core) * i / n, outer = core + (R - core) * (i + 1) / n;
                // the frame stays inside its section, clear of the ring lines
                float mid = (inner + outer) / 2f, size = Mathf.Min(RitualOrnament.GlyphSize(inner, outer, per, 1.2f), (outer - inner) * RitualOrnament.FrameInBand);
                for (int k = 0; k < per; k++)
                {
                    // the same layout as the book, mirrored into the world (z grows upward on screen)
                    float div = RitualOrnament.SlotAngle(i, k, per) + 180f / per;
                    if (per > 1) DrawGlyph(At(c, mid, div), RitualOrnament.ConnectorSize(inner, outer, per) * 0.85f, RitualOrnament.Connector(i, k, per, true), minor, div);
                    Vector3 pos = At(c, mid, RitualOrnament.SlotAngle(i, k, per));
                    DrawGlyph(pos, size, RitualOrnament.FrameGlow, k < runes.Count ? minor : new Color(glyph.r, glyph.g, glyph.b, glyph.a * 0.45f));
                    if (k < runes.Count) DrawRune(pos, size * 0.66f, runes[k], glyph, carved, live);
                }
            }
            // the component circles beside the main one
            int nc = s.components.Count;
            for (int k = 0; k < nc; k++)
            {
                RitualComponent comp = s.components[k];
                // each component circle is a cell of its own: what lies on it works
                Vector3 pos = ComponentCell(k, s).ToVector3Shifted(); pos.y = c.y;
                float d = 0.92f;
                DrawRing(pos, d, ringMat);
                bool sealC = comp.kind == ComponentKind.Seal;
                DrawGlyph(pos, d * (sealC ? 1.15f : 0.95f), sealC ? RitualOrnament.SealGlow : RitualOrnament.FrameGlow, minor);
                // the sign of what belongs here: faint while the cell is empty
                Texture2D icon = comp.Icon;
                bool laid = comp.kind != ComponentKind.Seal && comp.kind != ComponentKind.None && drawn != null && parent.Spawned && Holding(k) != null;
                if (icon != null && !laid) DrawGlyph(pos, d * 0.5f, icon, new Color(1f, 1f, 1f, comp.kind == ComponentKind.Seal ? alpha : alpha * 0.4f));
            }
        }

        /// <summary>A rune on the floor, readable: chalk and live runes get a dark edge under them,
        /// carved ones a lit lip beside the groove.</summary>
        private static void DrawRune(Vector3 pos, float size, RuneDef rune, Color col, bool carved, bool live)
        {
            Vector3 up = new Vector3(0f, -0.0005f, 0f);
            if (carved)
            {
                // the lit lower-right lip of the groove, then the groove itself
                DrawGlyph(pos + up + new Vector3(size * 0.035f, 0f, -size * 0.035f), size, rune.Glow, new Color(0.78f, 0.76f, 0.72f, 0.55f));
                DrawGlyph(pos, size, rune.Ink, col);
                return;
            }
            // a dark edge so a pale glyph reads on any ground
            DrawGlyph(pos + up, size * 1.06f, rune.Ink, new Color(0f, 0f, 0f, 0.55f * col.a));
            DrawGlyph(pos, size, rune.Glow, live ? col : new Color(1f, 0.98f, 0.94f, col.a));
        }

        private static Vector3 At(Vector3 c, float radius, float screenAngle)
        {
            float a = -screenAngle * Mathf.Deg2Rad;
            return c + new Vector3(Mathf.Cos(a) * radius, 0.001f, Mathf.Sin(a) * radius);
        }

        private static void DrawGlyph(Vector3 pos, float size, Texture2D tex, Color col, float turn = 0f) =>
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.AngleAxis(turn, Vector3.up), new Vector3(size, 1f, size)),
                MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, col), 0);

        private static void DrawRing(Vector3 c, float d, Material m) =>
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(d, 1f, d)), m, 0);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref schema, "schema");
            Scribe_Deep.Look(ref drawn, "drawn");
            Scribe_Values.Look(ref drawQuality, "drawQuality");
            Scribe_Values.Look(ref drawWork, "drawWork");
            Scribe_Values.Look(ref washed, "washed");
            Scribe_Values.Look(ref armed, "armed");
            Scribe_Collections.Look(ref held, "held", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.Saving) pulsesTaken.RemoveAll(kv => kv.Key == null || kv.Key.Destroyed);
            Scribe_Collections.Look(ref pulsesTaken, "pulsesTaken", LookMode.Reference, LookMode.Value, ref pulseKeys, ref pulseVals);
            Scribe_Values.Look(ref pulseCooldown, "pulseCooldown");
            Scribe_References.Look(ref performer, "performer");
            Scribe_References.Look(ref transferFrom, "transferFrom");
            Scribe_References.Look(ref transferTo, "transferTo");
            Scribe_Values.Look(ref verbIndex, "verbIndex");
            Scribe_Values.Look(ref bank, "bank");
            Scribe_Values.Look(ref spentTotal, "spentTotal");
            Scribe_Values.Look(ref transmuteCarry, "transmuteCarry");
            Scribe_Values.Look(ref runTicks, "runTicks");
            Scribe_Values.Look(ref crystalProgress, "crystalProgress");
            Scribe_Values.Look(ref engraved, "engraved");
            Scribe_Values.Look(ref engraveNext, "engraveNext");
            Scribe_Values.Look(ref chiselPending, "chiselPending");
            Scribe_Values.Look(ref chiselWork, "chiselWork");
            Scribe_Values.Look(ref selfSustain, "selfSustain");
            ExposeAuto();
            Scribe_References.Look(ref conduit, "conduit");
            ExposeMore();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && held == null) held = new List<Pawn>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pulsesTaken == null) pulsesTaken = new Dictionary<Pawn, int>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) held.RemoveAll(x => x == null);
        }
    }
}
