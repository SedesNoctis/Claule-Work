using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    // =====================================================================================
    // SPELLS. A spell is written like a ritual (the same editor, the same runes) and kept in the mage's own book.
    // Up to five are held ready - three at first; each is a button on the mage. Casting: the mage stands, the spell's
    // circle lit under them in its colour, for a moment (a sorcerer quickly, a mage slowly - a mage needs cover); then
    // the Arcana is paid and the spell does what its runes say. Composure against the spell's instability: a poor hold
    // can lose it - the Arcana spent, a burn, a rush of greatness.
    // What is made now: the pulse (release) shaped by its element; holding; drawing out; growth and restoring;
    // the ward (barrier); the step. The other forms come with their own table.
    // =====================================================================================
    public class MageBook : IExposable
    {
        public List<RitualSchema> spells = new List<RitualSchema>();
        public RitualSchema[] ready = new RitualSchema[5];
        public int[] cooldown = new int[5];   // game tick when the place is free again
        public int open = 3;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref spells, "spells", LookMode.Deep);
            List<RitualSchema> r = ready?.ToList();
            Scribe_Collections.Look(ref r, "ready", LookMode.Deep);
            List<int> cd = cooldown?.ToList();
            Scribe_Collections.Look(ref cd, "cooldown", LookMode.Value);
            Scribe_Values.Look(ref open, "open", 3);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (spells == null) spells = new List<RitualSchema>();
                ready = new RitualSchema[5]; cooldown = new int[5];
                if (r != null) for (int i = 0; i < 5 && i < r.Count; i++) ready[i] = r[i];
                if (cd != null) for (int i = 0; i < 5 && i < cd.Count; i++) cooldown[i] = cd[i];
            }
        }
    }

    public static class Spells
    {
        public static float Cost(RitualSchema s)
        {
            // in units, not tens: a small circle costs 2 + 1 a rune; medium x1.5, large x2; every element past the first +3
            if (s.weave != null) return WeaveSpell.Cost(s.weave);
            RitualReading r = RitualEvaluator.Read(s);
            int n = s.AllRunes.Count();
            float size = s.size == RitualCircleSize.Large ? 2f : s.size == RitualCircleSize.Medium ? 1.5f : 1f;
            return (2f + n) * size + 3f * Mathf.Max(0, r.elements.Count - 1);
        }

        public static int CastTicks(Hediff_ArcaneCore c, RitualSchema s) => CastTicks(c.Sorcerer, s);

        /// <summary>Sorcerer quick, mage slow; a larger circle longer; each reach rune a little longer (+5%).</summary>
        public static int CastTicks(bool sorcerer, RitualSchema s) => s.weave != null ? WeaveSpell.CastTicks(sorcerer, s.weave) : Mathf.RoundToInt((sorcerer ? 60f : 150f) * (1f + 0.25f * (int)s.size) * (1f + 0.05f * RitualEvaluator.Read(s).reach));

        /// <summary>The map's stream, by tier (0 calm ... 4 cataclysm), if it is set to strengthen mages.</summary>
        public static int FlowTier(Map m) => AvirMod.Settings.flowStrengthensMages && m != null ? MapComponent_ExternalFlow.TierIndex(MapComponent_ExternalFlow.Of(m)?.Total ?? 0f) : 0;

        /// <summary>Chance the mage keeps hold of this spell.</summary>
        public static float HoldChance(Pawn p, RitualSchema s)
        {
            float risk = s.weave != null ? WeaveSpell.Risk(s.weave) : RitualEvaluator.Read(s).Risk(1f, 1f, 0f);
            float comp = GameComponent_AvirComposure.For(p, s, null);
            return risk > comp ? Mathf.Clamp01(1f - (risk - comp) * 0.4f) : 1f;
        }
        public static int Cooldown(RitualSchema s) => Mathf.RoundToInt(GenDate.TicksPerHour * 0.25f * (1f + (int)s.size));
        public static float Range(RitualSchema s) => 16f + 10f * (s.weave != null ? WeaveSpell.Reach(s.weave) : RitualEvaluator.Read(s).reach);
        public static float Power(RitualSchema s) => RitualCircles.PowerFactor(s.size) * (1f + 0.3f * RitualEvaluator.Read(s).amplify);
        public static RuneDef Element(RitualSchema s) => s.weave != null ? (s.weave.element ?? s.weave.All().Select(c => c.element).FirstOrDefault(e => e != null)) : RitualEvaluator.Read(s).elements.FirstOrDefault();

        public static Color ColorOf(RitualSchema s) => s.weave != null ? (Element(s)?.color ?? new Color(0.9f, 0.8f, 0.6f)) : RitualAspects.ColorOfRitual(s);

        public static string Describe(RitualSchema s)
        {
            RitualReading r = RitualEvaluator.Read(s);
            var parts = new List<string>();
            RuneDef el = r.elements.FirstOrDefault();
            if (s.weave != null || FireMagic.Is(el)) return SpellText.Explain(s).main + "\nArcana " + Cost(s).ToString("0") + ", range " + Range(s).ToString("0");
            foreach (RuneDef v in r.verbs) parts.Add(v.LabelCap + (Made(v.kind) ? "" : " (not made yet)"));
            return (el != null ? el.LabelCap + " - " : "") + string.Join(", ", parts) + "\nArcana " + Cost(s).ToString("0") + ", range " + Range(s).ToString("0");
        }

        public static bool Made(RuneKind k) => k == RuneKind.Release || k == RuneKind.Hold || k == RuneKind.Drain || k == RuneKind.Growth
                                               || k == RuneKind.Restore || k == RuneKind.Barrier || k == RuneKind.Step || k == RuneKind.Purge;

        /// <summary>The spell takes effect.</summary>
        public static void Resolve(Pawn caster, Hediff_ArcaneCore core, RitualSchema s, LocalTargetInfo target)
        {
            Map map = caster.Map;
            if (map == null) return;
            RitualReading r = RitualEvaluator.Read(s);
            float cost = Cost(s);
            if (core.arcana < cost) { Messages.Message(caster.LabelShortCap + " has not enough Arcana for " + s.name + ".", caster, MessageTypeDefOf.RejectInput, false); return; }
            core.Add(-cost);
            // the hold: composure against the spell's instability
            float risk = s.weave != null ? WeaveSpell.Risk(s.weave) : r.Risk(1f, 1f, 0f);
            float comp = GameComponent_AvirComposure.For(caster, s, null);
            if (risk > comp && Rand.Chance((risk - comp) * 0.4f))
            {
                caster.TakeDamage(new DamageInfo(DamageDefOf.Burn, 4f + 10f * (risk - comp), 0f, -1f, caster));
                Hediff g = caster.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness) ?? caster.health.AddHediff(MagicDefOf.Avir_ArcaneGreatness);
                g.Severity = Mathf.Min(1f, g.Severity + 0.08f);
                FleckMaker.ThrowMicroSparks(caster.DrawPos, map);
                Messages.Message(caster.LabelShortCap + " lost the hold on " + s.name + ": the Arcana burnt through.", caster, MessageTypeDefOf.NegativeEvent, false);
                if (s.weave != null) WeaveSpell.Slip(caster, s.weave);
                else if (FireMagic.Is(r.elements.FirstOrDefault())) FireMagic.Raging(caster);
                return;
            }
            GameComponent_AvirComposure.Performed(caster, cost, risk, comp, GameComponent_AvirComposure.KeyOf(s));
            float power = Power(s);
            int tier = FlowTier(map);
            if (tier > 0)
            {
                // the stream strengthens the spell - and presses greatness on the mage
                power *= 1f + 0.15f * tier;
                Hediff gr = caster.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness) ?? caster.health.AddHediff(MagicDefOf.Avir_ArcaneGreatness);
                gr.Severity = Mathf.Min(1f, gr.Severity + 0.02f * tier);
            }
            RuneDef el = r.elements.FirstOrDefault();
            if (s.weave != null) { WeaveSpell.Resolve(caster, s, target, power); return; }
            if (FireMagic.Is(el)) { FireMagic.Cast(caster, s, r, target, power); return; }
            IntVec3 at = target.Cell;
            float radius = 1.5f + 1.5f * r.breadth;
            foreach (RuneDef v in r.verbs)
            {
                switch (v.kind)
                {
                    case RuneKind.Release: Pulse(caster, at, map, radius, power, el); break;
                    case RuneKind.Hold:
                        foreach (Pawn q in PawnsAt(at, map, radius + 0.5f)) if (q.HostileTo(caster)) q.stances?.stunner?.StunFor(Mathf.RoundToInt(180f * power), caster);
                        FleckMaker.Static(at, map, FleckDefOf.PsycastAreaEffect, radius + 1f);
                        break;
                    case RuneKind.Drain:
                        foreach (Pawn q in PawnsAt(at, map, radius))
                        {
                            if (q == caster || !q.HostileTo(caster)) continue;
                            if (q.needs?.rest != null) q.needs.rest.CurLevel -= 0.15f * power;
                            Hediff_ArcaneCore qc = Mages.Core(q);
                            float took = qc != null ? Mathf.Min(qc.arcana, 15f * power) : 6f * power;
                            if (qc != null) qc.Add(-took);
                            core.Add(took);
                            FleckMaker.ConnectingLine(q.DrawPos, caster.DrawPos, FleckDefOf.LineEMP, map);
                        }
                        break;
                    case RuneKind.Growth:
                    case RuneKind.Restore:
                        foreach (Pawn q in PawnsAt(at, map, Mathf.Max(0.9f, radius - 1f))) if (!q.HostileTo(caster)) Heal(q, 10f * power);
                        FleckMaker.ThrowMetaIcon(at, map, FleckDefOf.HealingCross);
                        break;
                    case RuneKind.Purge:
                        foreach (Pawn q in PawnsAt(at, map, radius)) if (!q.HostileTo(caster) && q.InMentalState && !q.InAggroMentalState) q.MentalState.RecoverFromState();
                        break;
                    case RuneKind.Barrier:
                        foreach (Pawn q in PawnsAt(at, map, Mathf.Max(0.9f, radius - 1f)))
                        {
                            if (q.HostileTo(caster) || MagicSpellDefOf.Avir_ArcaneWard == null) continue;
                            Hediff w = q.health.AddHediff(MagicSpellDefOf.Avir_ArcaneWard);
                            w.Severity = Mathf.Clamp01(0.4f * power);
                            if (el != null) WardElement.Set(q, el);
                        }
                        break;
                    case RuneKind.Step:
                        if (at.Standable(map) && caster.Spawned)
                        {
                            FleckMaker.ThrowSmoke(caster.DrawPos, map, 1.2f);
                            caster.Position = at; caster.Notify_Teleported(true, false);
                            FleckMaker.ThrowSmoke(caster.DrawPos, map, 1.2f);
                        }
                        break;
                }
            }
        }

        private static IEnumerable<Pawn> PawnsAt(IntVec3 at, Map map, float radius) =>
            GenRadial.RadialDistinctThingsAround(at, map, radius, true).OfType<Pawn>().Where(p => !p.Dead).ToList();

        private static void Heal(Pawn p, float amount)
        {
            foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.CanHealNaturally() || i.CanHealFromTending()).ToList())
            {
                if (amount <= 0f) break;
                float h = Mathf.Min(amount, inj.Severity);
                inj.Heal(h); amount -= h;
            }
        }

        /// <summary>The pulse, shaped by its element. Ward rule: a ward of the same element lets it through, stronger.</summary>
        private static void Pulse(Pawn caster, IntVec3 at, Map map, float radius, float power, RuneDef el)
        {
            int dmg = Mathf.RoundToInt(10f * power);
            string k = el?.kind.ToString();
            DamageDef d = DamageDefOf.Bomb;
            float fire = 0f;
            switch (k)
            {
                case "Fire": d = DamageDefOf.Flame; fire = 0.6f; break;
                case "Water": d = DamageDefOf.Extinguish; dmg = Mathf.RoundToInt(dmg * 0.5f); break;
                case "Earth": d = DamageDefOf.Blunt; dmg = Mathf.RoundToInt(dmg * 1.3f); break;
                case "Air": d = DamageDefOf.Blunt; break;
                case "Light": d = DamageDefOf.Burn; break;
                case "Time": d = DamageDefOf.Stun; break;
                case "Life": d = DamageDefOf.Cut; break;
                case "Darkness": d = DamageDefOf.Psychic; break;
                case "Space": d = DamageDefOf.Blunt; break;
            }
            GenExplosion.DoExplosion(at, map, radius, d, caster, dmg, -1f, null, null, null, null, null, 0f, 1, null, null, 255,
                false, null, 0f, 1, fire, false, null, new List<Thing> { caster });
            if (k == "Water" || k == "Air" || k == "Space" || k == "Time")
                foreach (Pawn q in PawnsAt(at, map, radius)) if (q != caster && q.HostileTo(caster)) q.stances?.stunner?.StunFor(Mathf.RoundToInt(60f * power), caster);
            if (k == "Darkness")
                foreach (Pawn q in PawnsAt(at, map, radius)) if (q != caster && q.HostileTo(caster) && q.RaceProps.Humanlike && !q.InMentalState) q.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, null, true);
            if (k == "Life")
                foreach (Pawn q in PawnsAt(at, map, radius)) if (!q.HostileTo(caster)) Heal(q, 4f * power);
        }
    }

    [DefOf]
    public static class MagicSpellDefOf
    {
        public static JobDef Avir_CastSpell;
        public static HediffDef Avir_ArcaneWard;
        static MagicSpellDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(MagicSpellDefOf)); }
    }

    /// <summary>A ward's element: remembered for the rule that a spell of the same element passes through it, stronger (the rule comes with the forms).</summary>
    public static class WardElement
    {
        private static readonly Dictionary<int, RuneDef> of = new Dictionary<int, RuneDef>();
        public static void Set(Pawn p, RuneDef el) { of[p.thingIDNumber] = el; }
        public static RuneDef Get(Pawn p) => of.TryGetValue(p.thingIDNumber, out RuneDef e) ? e : null;
    }

    /// <summary>The cast: the mage stands, the spell's circle lit beneath, then it takes effect.</summary>
    public class JobDriver_CastSpell : JobDriver
    {
        public RitualSchema spell;
        public int slot = -1;
        public static readonly HashSet<JobDriver_CastSpell> Casting = new HashSet<JobDriver_CastSpell>();
        /// <summary>The spell a cast job was ordered with, until its driver takes it.</summary>
        public static readonly Dictionary<Job, KeyValuePair<RitualSchema, int>> Pending = new Dictionary<Job, KeyValuePair<RitualSchema, int>>();
        private int started;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref spell, "spell");
            Scribe_Values.Look(ref slot, "slot", -1);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (spell == null && Pending.TryGetValue(job, out var p)) { spell = p.Key; slot = p.Value; Pending.Remove(job); }
            Hediff_ArcaneCore core = Mages.Core(pawn);
            this.FailOn(() => core == null || spell == null);
            Toil cast = Toils_General.Wait(core != null && spell != null ? Spells.CastTicks(core, spell) : 60);
            cast.initAction = () => { started = Find.TickManager.TicksGame; Casting.Add(this); pawn.rotationTracker.FaceTarget(job.targetA); };
            cast.AddFinishAction(() => Casting.Remove(this));
            cast.WithProgressBarToilDelay(TargetIndex.None);
            cast.handlingFacing = true;
            yield return cast;
            yield return Toils_General.Do(() =>
            {
                Spells.Resolve(pawn, core, spell, job.targetA);
                MageBook b = core.Book;
                if (slot >= 0 && slot < 5) b.cooldown[slot] = Find.TickManager.TicksGame + Spells.Cooldown(spell);
            });
        }

        public float Progress => spell == null ? 0f : Mathf.Clamp01((Find.TickManager.TicksGame - started) / (float)Mathf.Max(1, Spells.CastTicks(Mages.Core(pawn), spell)));
    }

    /// <summary>Draws the circles of the spells being cast, under their casters.</summary>
    public class MapComponent_SpellCircles : MapComponent
    {
        public MapComponent_SpellCircles(Map map) : base(map) { }

        public override void MapComponentUpdate()
        {
            if (!AvirMagic.On || JobDriver_CastSpell.Casting.Count == 0) return;
            foreach (JobDriver_CastSpell d in JobDriver_CastSpell.Casting)
            {
                if (d.pawn?.Map != map || d.spell == null) continue;
                SpellSigil.Draw(d.pawn.DrawPos, d.spell, 0.4f + 0.6f * d.Progress, 1.6f + 0.6f * (int)d.spell.size);
            }
        }
    }

    /// <summary>A spell's circle in the world: rings and its runes, glowing in the spell's colour.</summary>
    public static class SpellSigil
    {
        public static void Draw(Vector3 at, RitualSchema s, float alpha, float radius)
        {
            Color c = Spells.ColorOf(s);
            Color col = new Color(Mathf.Min(1f, c.r * 1.2f + 0.1f), Mathf.Min(1f, c.g * 1.2f + 0.1f), Mathf.Min(1f, c.b * 1.2f + 0.1f), alpha);
            at.y = AltitudeLayer.MoteLow.AltitudeFor();
            Material ring = MaterialPool.MatFrom(RitualTex.Ring, ShaderDatabase.MoteGlow, col);
            float t = Time.realtimeSinceStartup;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.AngleAxis(t * 15f % 360f, Vector3.up), new Vector3(radius * 2f, 1f, radius * 2f)), ring, 0);
            int n = Mathf.Max(1, s.rings.Count);
            for (int i = 1; i < n; i++)
            {
                float rr = radius * (0.3f + 0.7f * i / n);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at + new Vector3(0f, 0.001f, 0f), Quaternion.identity, new Vector3(rr * 2f, 1f, rr * 2f)), ring, 0);
            }
            // runes: section by section, outward
            for (int i = 0; i < s.rings.Count; i++)
            {
                var runes = s.rings[i].runes.Where(x => x != null).ToList();
                float rr = i == 0 && runes.Count == 1 ? 0f : radius * (0.3f + 0.7f * (i + 0.5f) / n);
                for (int k = 0; k < runes.Count; k++)
                {
                    float ang = (360f / runes.Count) * k + (i % 2 == 0 ? t * 10f : -t * 10f);
                    Vector3 p = at + Quaternion.AngleAxis(ang, Vector3.up) * Vector3.forward * rr;
                    p.y = at.y + 0.002f;
                    Material m = MaterialPool.MatFrom(runes[k].Glow, ShaderDatabase.MoteGlow, col);
                    float sz = radius * 0.42f;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p, Quaternion.identity, new Vector3(sz, 1f, sz)), m, 0);
                }
            }
        }
    }

    /// <summary>A spell held ready: a button on the mage.</summary>
    public class Command_Spell : Command
    {
        public Pawn mage;
        public int slot;
        public RitualSchema spell;

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            var parms = new TargetingParameters { canTargetLocations = true, canTargetPawns = true, canTargetSelf = true, canTargetBuildings = false };
            Find.Targeter.BeginTargeting(parms, t =>
            {
                if (!mage.Spawned) return;
                if (t.Cell.DistanceTo(mage.Position) > Spells.Range(spell)) { Messages.Message("Too far for this spell.", MessageTypeDefOf.RejectInput, false); return; }
                Job j = JobMaker.MakeJob(MagicSpellDefOf.Avir_CastSpell, t);
                JobDriver_CastSpell.Pending[j] = new KeyValuePair<RitualSchema, int>(spell.Copy(), slot);
                if (!mage.jobs.TryTakeOrderedJob(j, JobTag.Misc)) JobDriver_CastSpell.Pending.Remove(j);
            }, mage, null, null);
        }

        public override void GizmoUpdateOnMouseover()
        {
            base.GizmoUpdateOnMouseover();
            if (mage.Spawned) GenDraw.DrawRadiusRing(mage.Position, Spells.Range(spell));
        }
    }
}
