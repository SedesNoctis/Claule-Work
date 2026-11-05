using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // THE ESSENTIUM PROJECTOR (Apogee, concept 4.4). A 3x3 turret: a fixed base and a turning top.
    // Its three stones (inlaid afterwards, Excellent hearts or better) decide what it is:
    //  - gold: gold rims, essentium hearts - charges by day, fires a golden beam that sets targets
    //    alight; with all three stones the beam can incinerate (long cooldown, heavy charge);
    //  - silver: silver rims in an arch, essentium at the sides and tisserium in the middle -
    //    charges by night, fires moonfire: mind fog, a ruined aim, and the target forgets its shooter.
    // It fires in an arc, but only at what it can see. A gunner (Shooting) mans it; a tuner standing
    // beside it (psychic sensitivity) speeds the charge and strengthens the beam.
    // =====================================================================================

    public enum ProjectorKind { None, Gold, Silver }

    [DefOf]
    public static class ProjectorDefOf
    {
        public static HediffDef Avir_Moonfire;
        public static JobDef Avir_TuneProjector;
        public static ArtifactMetalDef ArtifactMetal_Gold;
        public static ArtifactMetalDef ArtifactMetal_Silver;
        public static ArtifactMetalDef ArtifactMetal_Essentium;
        public static ArtifactMetalDef ArtifactMetal_Tisserium;
        static ProjectorDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ProjectorDefOf)); }
    }

    public class CompProperties_EssentiumProjector : CompProperties
    {
        public float chargePerLevel = 100f;
        /// <summary>Charge gained per rare tick per level when its light (day / night) is up.</summary>
        public float chargeRate = 0.6f;
        public float shotCost = 8f;
        public float incinerateCost = 40f;
        public int incinerateCooldown = 3600;
        public CompProperties_EssentiumProjector() { compClass = typeof(CompEssentiumProjector); }
    }

    public class CompEssentiumProjector : ThingComp, IArtifactHostEffect, IArtifactSocketRule
    {
        public float charge;
        public int nextIncinerate;
        public Pawn tuner;
        public int tunedUntil;

        private CompProperties_EssentiumProjector P => (CompProperties_EssentiumProjector)props;
        private CompArtifactHost Host => parent.TryGetComp<CompArtifactHost>();

        /// <summary>Gold or silver, by its rims: one pattern only (three gold rims with essentium, or three
        /// silver rims - essentium at the sides, tisserium in the middle).</summary>
        public ProjectorKind Kind
        {
            get
            {
                CompArtifactHost h = Host;
                if (h == null) return ProjectorKind.None;
                int gold = 0, silver = 0;
                for (int i = 0; i < h.slots.Count; i++)
                {
                    if (!h.Has(i)) continue;
                    if (h.slots[i].rimMetal == ProjectorDefOf.ArtifactMetal_Gold) gold++;
                    else if (h.slots[i].rimMetal == ProjectorDefOf.ArtifactMetal_Silver) silver++;
                }
                return gold == 0 && silver == 0 ? ProjectorKind.None : gold >= silver ? ProjectorKind.Gold : ProjectorKind.Silver;
            }
        }

        /// <summary>Day: the sun is up enough for the golden beam; the rest is moonfire's time.</summary>
        public bool IsDay => parent.Map != null && GenCelestial.CurCelestialSunGlow(parent.Map) >= 0.4f;

        /// <summary>Stones that fit the pattern of its kind and work: 0..3.</summary>
        public int Level => LevelOf(Kind);

        public int LevelOf(ProjectorKind k)
        {
            CompArtifactHost h = Host;
            if (h == null || k == ProjectorKind.None) return 0;
            int n = 0;
            for (int i = 0; i < h.slots.Count; i++)
            {
                if (!h.Works(i)) continue;
                ArtifactState s = h.slots[i];
                if (s.heartQuality < QualityCategory.Excellent) continue;
                if (k == ProjectorKind.Gold && s.rimMetal == ProjectorDefOf.ArtifactMetal_Gold && s.heartMetal == ProjectorDefOf.ArtifactMetal_Essentium) n++;
                else if (k == ProjectorKind.Silver && s.rimMetal == ProjectorDefOf.ArtifactMetal_Silver
                         && s.heartMetal == (i == 1 ? ProjectorDefOf.ArtifactMetal_Tisserium : ProjectorDefOf.ArtifactMetal_Essentium)) n++;
            }
            return n;
        }

        /// <summary>Gold: gold rim, essentium heart in any socket. Silver: silver rim, tisserium in the middle, essentium at the sides.</summary>
        public bool SocketAccepts(int socket, ArtifactMetalDef rim, ArtifactMetalDef heart)
        {
            // no mixing: the rims already in (or on their way) set the pattern
            CompArtifactHost h = Host;
            if (h != null)
                for (int i = 0; i < h.slots.Count; i++)
                {
                    if (i == socket) continue;
                    ArtifactMetalDef other = h.slots[i]?.rimMetal ?? (i < h.pendingInlay.Count ? h.pendingInlay[i]?.TryGetComp<CompArtifact>()?.rimMetal : null);
                    if (other != null && other != rim) return false;
                }
            if (rim == ProjectorDefOf.ArtifactMetal_Gold) return heart == ProjectorDefOf.ArtifactMetal_Essentium;
            if (rim == ProjectorDefOf.ArtifactMetal_Silver) return heart == (socket == 1 ? ProjectorDefOf.ArtifactMetal_Tisserium : ProjectorDefOf.ArtifactMetal_Essentium);
            return false;
        }

        public int arcMode = 1;
        public static readonly float[] ArcWidths = { 2f, 5f, 9f };
        public static readonly float[] ArcCurves = { 0.3f, 0.9f, 1.6f };
        private static readonly string[] ArcLabels = { "narrow", "medium", "wide" };

        public float MaxCharge => P.chargePerLevel * Level;
        public bool Tuned => tuner != null && Find.TickManager.TicksGame <= tunedUntil && tuner.Spawned && !tuner.Downed;
        public float TunerSensitivity => Tuned ? Mathf.Clamp(tuner.GetStatValue(StatDefOf.PsychicSensitivity), 0f, 3f) : 0f;

        /// <summary>Damage multiplier: stone power, and a tuner's sensitivity.</summary>
        public float Strength => Mathf.Max(0.5f, Host?.Power ?? 1f) * (1f + 0.25f * TunerSensitivity);

        public bool LightUp
        {
            get
            {
                Map map = parent.Map;
                if (map == null) return false;
                float glow = GenCelestial.CurCelestialSunGlow(map);
                ProjectorKind k = Kind;
                return k == ProjectorKind.Gold ? glow >= 0.5f : k == ProjectorKind.Silver && glow <= 0.3f;
            }
        }

        /// <summary>Both fire at any hour on stored charge; moonfire under the sun is weaker (MoonfireDayFactor).</summary>
        public bool CanFire => Level > 0 && charge >= P.shotCost;
        public const float MoonfireDayFactor = 0.5f;
        public bool IncinerateReady => Kind == ProjectorKind.Gold && Level >= 3 && charge >= P.incinerateCost && Find.TickManager.TicksGame >= nextIncinerate;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (Level == 0) { charge = Mathf.Min(charge, MaxCharge); return; }
            if (LightUp) charge = Mathf.Min(MaxCharge, charge + P.chargeRate * Level * (1f + TunerSensitivity));
        }

        /// <summary>One shot: what it costs. True for an incinerating shot.</summary>
        public bool SpendShot()
        {
            if (IncinerateReady)
            {
                charge -= P.incinerateCost;
                nextIncinerate = Find.TickManager.TicksGame + P.incinerateCooldown;
                return true;
            }
            charge = Mathf.Max(0f, charge - P.shotCost);
            return false;
        }

        public string EffectLine()
        {
            ProjectorKind k = Kind;
            int lv = Level;
            if (k == ProjectorKind.None || lv == 0)
                return "Needs its stones: Excellent hearts or better. Gold - gold rims with essentium hearts (charges by day, burning beam). "
                       + "Silver - silver rims in an arch: essentium at the sides, tisserium in the middle (charges by night, moonfire).";
            string s = (k == ProjectorKind.Gold ? "Golden beam" : "Moonfire") + ", level " + lv + " of 3. Charge " + Mathf.FloorToInt(charge) + " / " + Mathf.FloorToInt(MaxCharge)
                       + (k == ProjectorKind.Gold ? ", fills by day under the open sky." : ", fills by night.");
            s += k == ProjectorKind.Gold
                ? " Burns and sets alight" + (lv >= 3 ? "; every " + (P.incinerateCooldown / 60) + " s a shot incinerates (" + P.incinerateCost + " charge)." : ".")
                : " Fogs the mind, ruins the aim, makes the target forget who shot it.";
            s += " Strength x" + Strength.ToString("0.00") + (Tuned ? " (tuned)" : " - a tuner beside it charges faster and hits harder") + ".";
            return s;
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            ProjectorKind k = Kind;
            int lv = Level;
            if (lv == 0) return "No fitting stones: it cannot fire.";
            sb.Append(k == ProjectorKind.Gold ? "Golden beam" : "Moonfire").Append(", level ").Append(lv);
            if (k == ProjectorKind.Silver && IsDay) sb.Append(" - weaker under the sun");
            sb.Append("\nCharge ").Append(Mathf.FloorToInt(charge)).Append(" / ").Append(Mathf.FloorToInt(MaxCharge)).Append(LightUp ? " (charging)" : " (waiting for its light)");
            if (Tuned) sb.Append("\nTuned by ").Append(tuner.LabelShort);
            if (k == ProjectorKind.Gold && lv >= 3)
                sb.Append(IncinerateReady ? "\nIncineration ready" : "\nIncineration in " + Mathf.Max(0, nextIncinerate - Find.TickManager.TicksGame).ToStringTicksToPeriod());
            return sb.ToString();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            yield return new Command_Action
            {
                defaultLabel = "Arc: " + ArcLabels[arcMode],
                defaultDesc = "How wide the beam sweeps across its target: narrow (2 cells, the damage packed tight), medium (5) or wide (9, spread thin). Click to change.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/BeamArc", false) ?? ContentFinder<Texture2D>.Get("UI/Commands/ViewRange", false) ?? BaseContent.BadTex,
                action = () => arcMode = (arcMode + 1) % 3
            };
            if (Prefs.DevMode) yield return new Command_Action { defaultLabel = "Dev: fill charge", action = () => charge = MaxCharge };
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charge, "charge");
            Scribe_Values.Look(ref nextIncinerate, "nextIncinerate");
            Scribe_Values.Look(ref arcMode, "arcMode", 1);
            Scribe_References.Look(ref tuner, "tuner");
            Scribe_Values.Look(ref tunedUntil, "tunedUntil");
        }
    }

    /// <summary>The projector's shot: a beam blast that sweeps across the target in an arc and burns every
    /// cell it crosses (and their neighbours, at half). It can miss: then the sweep lands beside the target.
    /// Gold burns and sets alight; silver is moonfire. No charge - no shot.</summary>
    public class Verb_EssentiumBeam : Verb_ShootBeam
    {
        private static readonly AccessTools.FieldRef<Verb_ShootBeam, MoteDualAttached> MoteRef =
            AccessTools.FieldRefAccess<Verb_ShootBeam, MoteDualAttached>("mote");

        private ProjectorKind kind;
        private float strength = 1f;
        private bool incinerate;

        private CompEssentiumProjector Proj => caster?.TryGetComp<CompEssentiumProjector>();

        public override bool Available() => base.Available() && Proj?.CanFire == true;

        public static Color ColorOf(ProjectorKind k) => k == ProjectorKind.Silver ? new Color(0.72f, 0.84f, 1f) : new Color(1f, 0.82f, 0.38f);

        public override void WarmupComplete()
        {
            CompEssentiumProjector p = Proj;
            if (p == null || !p.CanFire) { Reset(); return; }
            kind = p.Kind;
            strength = p.Strength * (kind == ProjectorKind.Silver && p.IsDay ? CompEssentiumProjector.MoonfireDayFactor : 1f);
            ApplyArc(p);
            incinerate = p.SpendShot();
            // the aim: a miss throws the sweep a few cells off the target
            float hit = ShotReport.HitReportFor(caster, this, currentTarget).AimOnTargetChance;
            if (!Rand.Chance(hit))
            {
                Map map = caster.Map;
                IntVec3 miss = currentTarget.Cell + GenRadial.RadialPattern[Rand.RangeInclusive(9, 36)];
                if (miss.InBounds(map)) currentTarget = new LocalTargetInfo(miss);
            }
            base.WarmupComplete();
            MoteDualAttached m = MoteRef(this);
            if (m != null) m.instanceColor = ColorOf(kind);
        }

        protected override bool TryCastShot()
        {
            Map map = caster.Map;
            EssentiumBeamShot.Set(kind, strength, incinerate);
            bool r;
            try { r = base.TryCastShot(); }
            finally { EssentiumBeamShot.Clear(); }
            if (map != null)
            {
                Vector3 at = InterpolatedPosition;
                FleckMaker.ThrowLightningGlow(at, map, incinerate ? 2.2f : 1.2f);
                // the sweep scorches the ground it crosses
                IntVec3 cell = at.ToIntVec3();
                if (cell.InBounds(map) && Rand.Chance(kind == ProjectorKind.Silver ? 0.12f : incinerate ? 0.6f : 0.3f))
                    EssentiumFire.StartIn(kind == ProjectorKind.Silver ? EssentiumFireDefOf.Avir_SilverFire : EssentiumFireDefOf.Avir_GoldFire,
                        cell, map, kind == ProjectorKind.Silver ? 0.3f : 0.6f, caster);
                // the last step of an incinerating sweep bursts into flame
                if (incinerate && burstShotsLeft <= 1)
                {
                    GenExplosion.DoExplosion(at.ToIntVec3(), map, 2.9f, DamageDefOf.Flame, caster, Mathf.RoundToInt(30f * strength));
                    Messages.Message("The golden beam incinerates its target.", new TargetInfo(at.ToIntVec3(), map), MessageTypeDefOf.NeutralEvent, false);
                }
            }
            return r;
        }

        private bool ownProps;
        private static readonly System.Reflection.MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");

        /// <summary>The chosen arc (narrow, medium, wide) sets how far the sweep spreads across the target.</summary>
        private void ApplyArc(CompEssentiumProjector p)
        {
            if (p == null) return;
            if (!ownProps) { verbProps = (VerbProperties)Clone.Invoke(verbProps, null); ownProps = true; }
            verbProps.beamWidth = CompEssentiumProjector.ArcWidths[p.arcMode];
            verbProps.beamCurvature = CompEssentiumProjector.ArcCurves[p.arcMode];
        }

        public override void DrawHighlight(LocalTargetInfo target)
        {
            ApplyArc(Proj);
            base.DrawHighlight(target);
        }

        public override void OnGUI(LocalTargetInfo target)
        {
            base.OnGUI(target);
            if (caster == null || !target.IsValid || !CanHitTarget(target)) return;
            Widgets.MouseAttachedLabel("Hit chance: " + ShotReport.HitReportFor(caster, this, target).AimOnTargetChance.ToStringPercent(), 0f, 14f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref kind, "beamKind");
            Scribe_Values.Look(ref strength, "beamStrength", 1f);
            Scribe_Values.Look(ref incinerate, "beamIncinerate");
        }
    }

    /// <summary>What the beam now crossing a cell carries: read by its damage worker.</summary>
    public static class EssentiumBeamShot
    {
        public static bool active;
        public static ProjectorKind kind;
        public static float strength = 1f;
        public static bool incinerate;
        public static void Set(ProjectorKind k, float s, bool inc) { active = true; kind = k; strength = s; incinerate = inc; }
        public static void Clear() { active = false; strength = 1f; incinerate = false; }
    }

    /// <summary>The beam's burn. Gold: full strength, sets alight. Silver: weaker burn and moonfire - mind fog,
    /// a ruined aim, and the target forgets who shot it. Under Combat Extended no armour stops it.</summary>
    public class DamageWorker_EssentiumBeam : DamageWorker_AddInjury
    {
        private static readonly bool CE = ModsConfig.IsActive("CETeam.CombatExtended");

        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            bool silver = EssentiumBeamShot.active && EssentiumBeamShot.kind == ProjectorKind.Silver;
            float mult = EssentiumBeamShot.active ? EssentiumBeamShot.strength * (silver ? 0.45f : 1f) * (EssentiumBeamShot.incinerate ? 2.5f : 1f) : 1f;
            var d = new DamageInfo(dinfo.Def, dinfo.Amount * mult, CE ? 9999f : dinfo.ArmorPenetrationInt, dinfo.Angle, dinfo.Instigator,
                dinfo.HitPart, dinfo.Weapon, dinfo.Category, dinfo.IntendedTarget);
            if (CE) d.SetIgnoreArmor(true);
            DamageResult res = base.Apply(d, victim);
            if (victim is Pawn p && !p.Dead)
            {
                if (silver) Moonfire(p, dinfo.Instigator);
                else if (Rand.Chance(EssentiumBeamShot.incinerate ? 1f : 0.5f)) p.TryAttachFire(EssentiumBeamShot.incinerate ? 1f : 0.4f, dinfo.Instigator);
            }
            else if (!silver && victim != null && victim.Spawned && victim.CanEverAttachFire() && Rand.Chance(0.3f))
                victim.TryAttachFire(0.4f, dinfo.Instigator);
            return res;
        }

        private static void Moonfire(Pawn p, Thing launcher)
        {
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(ProjectorDefOf.Avir_Moonfire) ?? p.health.AddHediff(ProjectorDefOf.Avir_Moonfire);
            h.Severity = Mathf.Min(1f, h.Severity + 0.35f * EssentiumBeamShot.strength);
            // it forgets who shot it
            if (p.mindState != null)
            {
                if (p.mindState.enemyTarget == launcher || p.mindState.enemyTarget is Building) p.mindState.enemyTarget = null;
                p.mindState.lastAttackedTarget = LocalTargetInfo.Invalid;
            }
            if (launcher != null && p.CurJob != null && p.CurJob.targetA.Thing == launcher) p.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
    }

    // ---------------------------------------------------------------- the tuner

    public class WorkGiver_TuneProjector : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.allBuildingsColonist.Where(b => b.TryGetComp<CompEssentiumProjector>() != null);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !PotentialWorkThingsGlobal(pawn).Any();

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompEssentiumProjector p = t.TryGetComp<CompEssentiumProjector>();
            if (p == null || p.Level == 0 || t.IsForbidden(pawn)) return false;
            if (p.Tuned && p.tuner != pawn) return false;
            if (pawn.GetStatValue(StatDefOf.PsychicSensitivity) < 0.5f) return false;
            // worth it: charging under its light, or a fight on the map
            bool fight = pawn.Map.attackTargetsCache.TargetsHostileToColony.Any(x => x.Thing is Pawn hp && !hp.Downed);
            if (!fight && (!p.LightUp || p.charge >= p.MaxCharge - 0.5f)) return false;
            return pawn.CanReach(t, PathEndMode.Touch, Danger.Some);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(ProjectorDefOf.Avir_TuneProjector, t);
    }

    public class JobDriver_TuneProjector : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil tune = ToilMaker.MakeToil("TuneProjector");
            tune.defaultCompleteMode = ToilCompleteMode.Delay;
            tune.defaultDuration = 2500;
            tune.WithProgressBarToilDelay(TargetIndex.A);
            tune.tickIntervalAction = delta =>
            {
                CompEssentiumProjector p = job.targetA.Thing?.TryGetComp<CompEssentiumProjector>();
                if (p == null) { EndJobWith(JobCondition.Incompletable); return; }
                p.tuner = pawn;
                p.tunedUntil = Find.TickManager.TicksGame + 30;
                pawn.rotationTracker.FaceTarget(job.targetA);
            };
            yield return tune;
        }
    }
}
