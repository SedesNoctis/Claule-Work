using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // Basic artifactorics totems (one socket, any artifact): the explosive totem and the scarer.
    // Range grows with the stone's heart quality, strength with its quality and metals (Power).
    // =====================================================================================

    public abstract class CompArtifactTotem : ThingComp, IArtifactHostEffect
    {
        /// <summary>The stones this effect runs on: all of them, or in a constellation only those of the
        /// sockets given to this function.</summary>
        protected CompArtifactHost Host => CompConstellation.HostFor(this) ?? parent.TryGetComp<CompArtifactHost>();
        public abstract float BaseRange { get; }
        public abstract float RangePerQuality { get; }
        /// <summary>Range added by every working stone after the first (multi-socket buildings).</summary>
        public virtual float RangePerStone => 0f;

        public float Range
        {
            get
            {
                CompArtifactHost h = Host;
                if (h == null || h.Working == 0) return 0f;
                float r = BaseRange + RangePerQuality * (int)h.BestQuality + RangePerStone * Mathf.Max(0, h.Working - 1);
                // a constellation reaches further with every socket given to this function
                CompConstellation cons = parent.TryGetComp<CompConstellation>();
                if (cons != null && BaseRange > 0f) r += cons.RangePerSocket * h.Working;
                return r;
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            float r = Range;
            if (r > 0f && r < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(parent.Position, r);
        }

        public abstract string EffectLine();
    }

    public class CompProperties_TotemBlast : CompProperties
    {
        public float baseRange = 6f;
        public float rangePerQuality = 1.5f;
        public float damage = 18f;
        public float blastRadius = 1.4f;
        public int cooldownTicks = 1800;
        public float chargePerShot = 3f;
        public CompProperties_TotemBlast() { compClass = typeof(CompTotemBlast); }
    }

    /// <summary>Strikes a foe in range with a blast of force; the totem itself stays whole.</summary>
    public class CompTotemBlast : CompArtifactTotem
    {
        private int nextShot;
        private CompProperties_TotemBlast P => (CompProperties_TotemBlast)props;
        public override float BaseRange => P.baseRange;
        public override float RangePerQuality => P.rangePerQuality;

        public override void CompTickRare()
        {
            base.CompTickRare();
            Map map = parent.Map;
            CompArtifactHost h = Host;
            if (map == null || h == null || h.Working == 0 || Find.TickManager.TicksGame < nextShot) return;
            float range = Range;
            Faction own = parent.Faction ?? Faction.OfPlayer;
            Pawn target = map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Downed && !p.Dead && p.HostileTo(own)
                            && (p.Position - parent.Position).LengthHorizontal <= range
                            && GenSight.LineOfSight(parent.Position, p.Position, map, true))
                .OrderBy(p => (p.Position - parent.Position).LengthHorizontalSquared)
                .FirstOrDefault();
            if (target == null || !h.Spend(P.chargePerShot)) return;
            float power = h.Power;
            FleckMaker.ThrowLightningGlow(parent.DrawPos, map, 1.2f);
            FleckMaker.ConnectingLine(parent.DrawPos, target.DrawPos, FleckDefOf.PsycastAreaEffect, map, 0.6f);
            int dmg = Mathf.RoundToInt(P.damage * power);
            // our own people beside the foe (a melee scrum): strike the foe alone instead of blasting the ground
            bool friendsNear = map.mapPawns.AllPawnsSpawned.Any(p => p != target && !p.Dead && !p.HostileTo(own)
                                                                     && (p.Position - target.Position).LengthHorizontal <= P.blastRadius + 0.5f);
            if (friendsNear)
            {
                target.TakeDamage(new DamageInfo(DamageDefOf.Blunt, dmg, 0.2f, -1f, parent));
                target.stances?.stagger?.StaggerFor(95);
                FleckMaker.ThrowMicroSparks(target.DrawPos, map);
            }
            else
                GenExplosion.DoExplosion(target.Position, map, P.blastRadius, DamageDefOf.Bomb, parent, dmg, -1f, null, null, null, target);
            nextShot = Find.TickManager.TicksGame + P.cooldownTicks;
        }

        public override string EffectLine()
        {
            CompArtifactHost h = Host;
            if (h == null || h.Working == 0) return "Strikes a foe at range with a blast of force; the totem stays whole. Asleep: no charged artifact.";
            int left = Mathf.Max(0, nextShot - Find.TickManager.TicksGame);
            return "Strikes the nearest foe within " + Range.ToString("0.#") + " cells: blast of " + Mathf.RoundToInt(P.damage * h.Power)
                   + " damage, radius " + P.blastRadius.ToString("0.#") + ", every " + (P.cooldownTicks / 60f).ToString("0") + " s"
                   + (left > 0 ? " (ready in " + (left / 60f).ToString("0") + " s)" : " (ready)") + ". Each blast costs " + P.chargePerShot.ToString("0.#") + " charge.";
        }

        public override string CompInspectStringExtra() => Host?.Working > 0 ? "Blast range " + Range.ToString("0.#") : null;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextShot, "nextShot");
        }
    }

    public class CompProperties_TotemScare : CompProperties
    {
        public float baseRange = 8f;
        public float rangePerQuality = 2f;
        public float chargePerScare = 1f;
        /// <summary>Manhunters flee too from this heart quality on.</summary>
        public QualityCategory manhunterMinQuality = QualityCategory.Excellent;
        public CompProperties_TotemScare() { compClass = typeof(CompTotemScare); }
    }

    /// <summary>Predators hunting in range give up and flee; with a fine enough stone, manhunters too.
    /// Peaceful animals are left alone (hunting and taming are not disturbed).</summary>
    public class CompTotemScare : CompArtifactTotem
    {
        private CompProperties_TotemScare P => (CompProperties_TotemScare)props;
        public override float BaseRange => P.baseRange;
        public override float RangePerQuality => P.rangePerQuality;

        public override void CompTickRare()
        {
            base.CompTickRare();
            Map map = parent.Map;
            CompArtifactHost h = Host;
            if (map == null || h == null || h.Working == 0) return;
            float range = Range;
            bool manhunters = h.BestQuality >= P.manhunterMinQuality;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p.Dead || p.Downed || !p.RaceProps.Animal || p.Faction == Faction.OfPlayer) continue;
                if ((p.Position - parent.Position).LengthHorizontal > range) continue;
                bool hunting = p.CurJobDef == JobDefOf.PredatorHunt;
                bool mad = p.MentalStateDef == MentalStateDefOf.Manhunter || p.MentalStateDef == MentalStateDefOf.ManhunterPermanent;
                if (!hunting && !(mad && manhunters)) continue;
                if (!h.Spend(P.chargePerScare)) return;
                if (mad) p.mindState.mentalStateHandler.CurState?.RecoverFromState();
                IntVec3 dest = CellFinderLoose.GetFleeDest(p, new List<Thing> { parent }, range + 12f);
                if (dest.IsValid && dest != p.Position)
                {
                    Job flee = JobMaker.MakeJob(JobDefOf.Flee, dest, parent);
                    p.jobs.StartJob(flee, JobCondition.InterruptForced);
                }
                FleckMaker.ThrowMicroSparks(p.DrawPos, map);
            }
        }

        public override string EffectLine()
        {
            CompArtifactHost h = Host;
            if (h == null || h.Working == 0) return "Drives off hunting predators. Asleep: no charged artifact.";
            return "Predators hunting within " + Range.ToString("0.#") + " cells give up and flee"
                   + (h.BestQuality >= P.manhunterMinQuality ? "; manhunters too" : "; manhunters too with a " + P.manhunterMinQuality.GetLabel() + " heart or better")
                   + ". Each scare costs " + P.chargePerScare.ToString("0.#") + " charge.";
        }
    }
}
