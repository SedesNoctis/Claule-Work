using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    // =====================================================================================
    // THE SHADOW, REWORKED. Not a matter of the mind: nothing here asks a mind's will.
    //   Distortion of time: the moving are thrown back to where they stood eight seconds ago, every ten - and time
    //     does not always put them back whole. Her zone's passive: foes' minds fail, and now and then they are
    //     thrown back hours.
    //   Theft of years: foes in her zone age; every ten years stolen heal her, then give her a year back. Set aside,
    //     her own age is kept, and later taken back.
    //   Step into shadow: the dark is open to her eyes; and where she steps out of it, her other arts may sing at
    //     once, for nothing.
    //   Fog and flare: one or the other.
    // =====================================================================================
    public static class LinarianTime
    {
        private const int H = GenDate.TicksPerHour;

        private static HediffDef loop, drift, drain;
        public static HediffDef Loop => loop ?? (loop = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_TimeLoop"));
        public static HediffDef Drift => drift ?? (drift = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_TimeDrift"));
        public static HediffDef Drain => drain ?? (drain = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_YearsDrain"));

        public static HediffDef MindIll() =>
            Rand.Bool ? HediffDefOf.Dementia : DefDatabase<HediffDef>.GetNamedSilentFail("Alzheimers") ?? HediffDefOf.Dementia;

        public static void GiveMindIll(Pawn q)
        {
            HediffDef d = MindIll();
            BodyPartRecord brain = q.health.hediffSet.GetBrain();
            if (d == null || brain == null || q.health.hediffSet.HasHediff(d)) return;
            q.health.AddHediff(d, brain);
        }

        // ---------------------------------------------------------------- distortion of time

        public static void Distortion(Pawn her, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = 0;
            int ticks = LinarianZone.Ticks(H / 2f, g);
            MapComponent_TimeFolds.Of(her.Map)?.Add(her, c, r, ticks);   // the fold stays where she cast it, seen, and ages whoever is caught at its end
            foreach (Pawn q in LinarianZone.FoesAt(her, c, r))
            {
                LinarianHediffs.Add(q, Loop, ticks, 1f, her);
                n++;
            }
            foreach (Pawn a in LinarianZone.OwnAt(her, c, r).Concat(new[] { her })) LinarianHediffs.Add(a, LinarianHediffs.Avir_TimeHaste, LinarianZone.Ticks(1800, g));
            LinarianTactics.Notify(her, n > 0 ? "time folds round " + n + "." : "time folds round no one.");
        }

        /// <summary>Thrown back in time: injuries that never happened, a dropped weapon, a failing mind.</summary>
        public static void Thrown(Pawn q, IntVec3 to, float worst)
        {
            if (!to.IsValid || !to.InBounds(q.Map) || !to.Standable(q.Map) || to == q.Position) return;
            FleckMaker.ThrowSmoke(q.DrawPos, q.Map, 0.8f);
            q.pather?.StopDead();
            q.Position = to;
            q.Notify_Teleported(true, false);
            FleckMaker.Static(q.Position, q.Map, FleckDefOf.PsycastAreaEffect, 1.2f);
            if (Rand.Chance(0.12f * worst))
            {
                BodyPartRecord part = q.health.hediffSet.GetNotMissingParts().Where(p => p.depth == BodyPartDepth.Outside).RandomElementWithFallback();
                if (part != null) q.TakeDamage(new DamageInfo(Rand.Bool ? DamageDefOf.Cut : DamageDefOf.Blunt, Rand.Range(3f, 8f), 0f, -1f, null, part));
            }
            if (!q.Dead && q.equipment?.Primary != null && Rand.Chance(0.08f * worst)) q.equipment.TryDropEquipment(q.equipment.Primary, out ThingWithComps _, q.Position);
            if (!q.Dead && q.RaceProps.Humanlike && Rand.Chance(0.03f * worst)) GiveMindIll(q);
        }

        // ---------------------------------------------------------------- theft of years

        public static void Theft(Pawn her, Gene_LinarianSong g)
        {
            int n = LinarianTactics.Count(g, 1), taken = 0;
            foreach (Pawn q in LinarianZone.InHerZone(her, g).Where(q => q != her && !q.Dead && q.RaceProps.Humanlike && q.HostileTo(her) && q.ageTracker != null)
                         .OrderBy(q => (q.Position - her.Position).LengthHorizontalSquared).ToList())
            {
                if (taken >= n) break;
                LinarianHediffs.Add(q, Drain, LinarianZone.Ticks(H * 2f, g), 1f, her);
                taken++;
            }
            LinarianTactics.Notify(her, taken > 0 ? "she drinks the years of " + taken + "." : "no one in her reach has years to give.");
        }

        /// <summary>A year drunk from someone: ten of them heal her, then make her a year younger.</summary>
        public static void Drink(Pawn her, float years)
        {
            Gene_LinarianSong g = Linarian.Song(her);
            if (g == null) return;
            g.stolenYears += years;
            while (g.stolenYears >= 10f)
            {
                g.stolenYears -= 10f;
                if (her.health.hediffSet.hediffs.Any(h => h is Hediff_Injury)) LinarianShadow.Heal(her, 60f);
                else LinarianShadow.Younger(her, 1f);
                if (her.Spawned) Avir.Ritualistics.RitualFx.Mote(her.DrawPos, her.Map, 1.6f, new Color(0.7f, 0.6f, 0.9f));
            }
        }

        /// <summary>Her age, set aside - or taken back. Younger than she set it aside: she grows into it and keeps what those
        /// years would have taught. Older: she sheds the years, forgets what they taught, and is whole again.</summary>
        public static void Preserve(Pawn her, Gene_LinarianSong g)
        {
            if (her.ageTracker == null) return;
            if (g.ageReserve < 0)
            {
                g.ageReserve = her.ageTracker.AgeBiologicalTicks;
                LinarianTactics.Notify(her, "she sets her years aside: " + her.ageTracker.AgeBiologicalYears + ".");
                return;
            }
            long diff = g.ageReserve - her.ageTracker.AgeBiologicalTicks;
            float years = diff / (float)GenDate.TicksPerYear;
            her.ageTracker.AgeBiologicalTicks = g.ageReserve;
            g.ageReserve = -1;
            float xp = 200f * Mathf.Abs(years);
            if (her.skills != null && xp > 0f)
                foreach (SkillRecord s in her.skills.skills.Where(s => !s.TotallyDisabled)) s.Learn(years > 0f ? xp : -xp, true);
            if (years < 0f)
                foreach (Hediff_Injury i in her.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList()) her.health.RemoveHediff(i);
            if (her.Spawned) Avir.Ritualistics.RitualFx.Mote(her.DrawPos, her.Map, 2f, new Color(0.7f, 0.6f, 0.9f));
            LinarianTactics.Notify(her, years >= 0f ? "she takes her years back - " + years.ToString("0.0") + " of them, and all they taught." : "she sheds " + (-years).ToString("0.0") + " years: whole again, and forgetting.");
        }

        // ---------------------------------------------------------------- step into shadow

        private static readonly (string node, string tactic)[] gifts =
        {
            ("L1", "L4"), ("D2", "D2"), ("C3", "C3"), ("W4", "W4"), ("W2", "W2"), ("S3", "S3"), ("G3", "G3")
        };

        /// <summary>Stepping out of the dark, her other arts may sing at once, round where she stands, for nothing: one of
        /// seven by lot (only those she has), and once in seventy all of them.</summary>
        public static void ShadowStep(Pawn her, Gene_LinarianSong g)
        {
            if (!g.Has("T1") || !her.Spawned) return;
            IEnumerable<(string node, string tactic)> sing = Rand.Chance(1f / 70f) ? gifts : new[] { gifts.RandomElement() };
            foreach (var (node, tactic) in sing.ToList())
            {
                if (!g.Has(node)) continue;
                LocalTargetInfo target = her.Position;
                if (tactic == "D2")
                {
                    float r = Mathf.Max(6f, LinarianZone.Area(tactic, g));
                    Pawn q = LinarianZone.FoesAt(her, her.Position, r).Where(LinarianTactics.Takeable).FirstOrDefault();
                    if (q == null) continue;
                    target = q;
                }
                try { LinarianActs.Do(her, g, tactic, target); }
                catch (System.Exception e) { Log.Warning("[Avir] shadow step gift " + tactic + ": " + e.Message); }
            }
        }

        /// <summary>The dark is open to her eyes: every unlit place of the land is out of the fog.</summary>
        public static void DarkSight(Map map)
        {
            if (!MapComponent_LinarianSong.HeraldsOn(map).Any(h => h.Faction == Faction.OfPlayer && !h.Dead && Linarian.Song(h)?.Has("T1") == true)) return;
            var guarded = new HashSet<Room>();
            foreach (Thing t in map.listerThings.AllThings.Where(t => t is TriggerUnfogged).ToList())
            {
                Room room = t.GetRoom();
                if (room != null) guarded.Add(room);
            }
            foreach (IntVec3 c in map.AllCells)
            {
                if (!map.fogGrid.IsFogged(c) || map.glowGrid.GroundGlowAt(c) >= 0.3f) continue;
                if (c.GetFirstThing<TriggerUnfogged>(map) != null) continue;
                Room room = c.GetRoom(map);
                if (room != null && guarded.Contains(room)) continue;   // the ancient dangers are not to be woken from afar
                map.fogGrid.Unfog(c);
            }
        }

        // ---------------------------------------------------------------- fog or flare

        public static void FogOrFlare(Pawn her, Gene_LinarianSong g, IntVec3 c, float r)
        {
            Map map = her.Map;
            if (!g.flareMode)
            {
                MapComponent_BlackFog.Of(map)?.Raise(c, Mathf.Max(8f, r), H * 6);
                LinarianTactics.Notify(her, "a black fog rises.");
                return;
            }
            LinarianShadow.Flare(map);
            int hour = GenLocalDate.HourOfDay(map);
            int ticks = Mathf.Max(1, hour >= 6 ? 30 - hour : 6 - hour) * H;
            int n = 0;
            foreach (Pawn m in map.mapPawns.AllPawnsSpawned.Where(m => m.RaceProps.IsMechanoid && !m.Dead).ToList())
            {
                m.stances?.stunner?.StunFor(ticks, her, false, false);
                n++;
            }
            LinarianTactics.Notify(her, "the sun flares: every light dies until dawn" + (n > 0 ? ", and " + n + " machines fall still." : "."));
        }

        public static void Tick(Map map, int now)
        {
            if (now % 2500 == 1250) DarkSight(map);
        }
    }

    // ---------------------------------------------------------------- the hediffs' comps

    /// <summary>Folded time: every ten seconds the moving are thrown back to where they stood eight seconds ago.</summary>
    public class HediffCompProperties_TimeLoop : HediffCompProperties
    {
        public HediffCompProperties_TimeLoop() { compClass = typeof(HediffComp_TimeLoop); }
    }

    public class HediffComp_TimeLoop : HediffComp
    {
        private readonly List<IntVec3> trail = new List<IntVec3>();

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = Pawn;
            if (!p.Spawned || p.Downed) return;
            if (p.IsHashIntervalTick(60))
            {
                trail.Add(p.Position);
                if (trail.Count > 10) trail.RemoveAt(0);
            }
            if (p.IsHashIntervalTick(600) && trail.Count >= 9 && p.pather?.Moving == true)
            {
                IntVec3 back = trail[trail.Count - 9];
                trail.Clear();
                LinarianTime.Thrown(p, back, 1f);
            }
        }
    }

    /// <summary>Her zone's passive: the mind fails, and now and then the body is thrown back hours.</summary>
    public class HediffCompProperties_TimeDrift : HediffCompProperties
    {
        public HediffCompProperties_TimeDrift() { compClass = typeof(HediffComp_TimeDrift); }
    }

    public class HediffComp_TimeDrift : HediffComp
    {
        public IntVec3 anchor = IntVec3.Invalid;
        public int jumpAt;

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref anchor, "anchor", IntVec3.Invalid);
            Scribe_Values.Look(ref jumpAt, "jumpAt");
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = Pawn;
            if (!p.Spawned || !p.IsHashIntervalTick(250)) return;
            int now = Find.TickManager.TicksGame;
            if (!anchor.IsValid) { anchor = p.Position; jumpAt = now + Rand.Range(GenDate.TicksPerHour, GenDate.TicksPerHour * 6); return; }
            if (p.RaceProps.Humanlike && Rand.Chance(0.002f)) LinarianTime.GiveMindIll(p);   // about one in twenty every hour
            if (now < jumpAt || p.Downed) return;
            LinarianTime.Thrown(p, anchor, 0.5f);
            anchor = p.Position;
            jumpAt = now + Rand.Range(GenDate.TicksPerHour, GenDate.TicksPerHour * 6);
        }
    }

    /// <summary>Years drunk away, a few at a time; with her mirages, a skeleton stands by now and then.</summary>
    public class HediffCompProperties_YearsDrain : HediffCompProperties
    {
        public HediffCompProperties_YearsDrain() { compClass = typeof(HediffComp_YearsDrain); }
    }

    public class HediffComp_YearsDrain : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = Pawn;
            if (!p.IsHashIntervalTick(250) || p.ageTracker == null) return;
            const float years = 0.4f;
            p.ageTracker.AgeBiologicalTicks += (long)(years * GenDate.TicksPerYear);
            Pawn her = parent.TryGetComp<HediffComp_SongSource>()?.herald;
            if (her != null && !her.Dead) LinarianTime.Drink(her, years);
            if (p.RaceProps.Humanlike && !p.Dead && Rand.Chance(0.04f)) LinarianTime.GiveMindIll(p);   // the mind does not keep up with the years
            if (!p.Spawned) return;
            FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 1f, new Color(0.75f, 0.72f, 0.65f));
            if (her != null && Linarian.Song(her)?.Has("G3") == true && Rand.Chance(0.12f)
                && CellFinder.TryFindRandomCellNear(p.Position, p.Map, 2, c => c.Standable(p.Map) && c.GetEdifice(p.Map) == null, out IntVec3 at))
            {
                Building_ShadowDecoy d = Building_ShadowDecoy.SpawnOne(her, at, p.Map, Find.TickManager.TicksGame + GenDate.TicksPerHour / 2, false);
                if (d != null) d.skeletonOf = p;
            }
        }
    }

    // ---------------------------------------------------------------- the eternal night: wild Singers sleep too

    public static class EternalNightRest
    {
        /// <summary>Under the eternal night a wild Singer still keeps her day: through the real midday she rests.</summary>
        public static bool RealMidday(Map map)
        {
            if (!LinarianShadow.EternalNightOn(map)) return false;
            int h = GenLocalDate.HourOfDay(map);
            return h >= 10 && h < 16;
        }
    }

    /// <summary>The folds of time she leaves: seen on the land while they last (a violet ring, a slow swirl), every foe
    /// inside caught in the loop; when a fold closes, whoever is still inside ages by 35 to 1500 years at once - and
    /// most do not survive it.</summary>
    public class MapComponent_TimeFolds : MapComponent
    {
        public class Fold : IExposable
        {
            public IntVec3 at;
            public float radius;
            public int until;
            public Pawn her;
            public void ExposeData()
            {
                Scribe_Values.Look(ref at, "at");
                Scribe_Values.Look(ref radius, "radius");
                Scribe_Values.Look(ref until, "until");
                Scribe_References.Look(ref her, "her");
            }
        }

        private List<Fold> folds = new List<Fold>();
        private static readonly Color Violet = new Color(0.62f, 0.45f, 1f, 0.9f);
        private static readonly Color Swirl = new Color(0.45f, 0.3f, 0.9f, 0.22f);

        public MapComponent_TimeFolds(Map map) : base(map) { }

        public static MapComponent_TimeFolds Of(Map m) => m?.GetComponent<MapComponent_TimeFolds>();

        public void Add(Pawn her, IntVec3 at, float r, int ticks) =>
            folds.Add(new Fold { at = at, radius = r, until = Find.TickManager.TicksGame + ticks, her = her });

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref folds, "timeFolds", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && folds == null) folds = new List<Fold>();
        }

        public override void MapComponentTick()
        {
            if (folds.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0) return;
            foreach (Fold f in folds.ToList())
            {
                if (f.her == null || f.her.Dead) { folds.Remove(f); continue; }
                List<Pawn> inside = LinarianZone.FoesAt(f.her, f.at, f.radius);
                if (now < f.until)
                {
                    foreach (Pawn q in inside) LinarianHediffs.Add(q, LinarianTime.Loop, 120, 1f, f.her);   // whoever walks in is caught
                    continue;
                }
                folds.Remove(f);
                Close(f, inside);
            }
        }

        private void Close(Fold f, List<Pawn> inside)
        {
            int dust = 0, aged = 0;
            foreach (Pawn q in inside.Where(q => !q.Dead && q.ageTracker != null))
            {
                float years = Rand.Range(35f, 1500f);
                q.ageTracker.AgeBiologicalTicks += (long)(years * GenDate.TicksPerYear);
                float life = q.RaceProps.lifeExpectancy;
                if (q.ageTracker.AgeBiologicalYearsFloat > life * 1.25f || Rand.Chance(Mathf.Clamp01((q.ageTracker.AgeBiologicalYearsFloat - life) / (life * 0.5f))))
                {
                    if (q.Spawned) FleckMaker.ThrowDustPuffThick(q.DrawPos, q.Map, 2f, new Color(0.75f, 0.72f, 0.65f));
                    q.Kill(null, null);
                    dust++;
                    continue;
                }
                if (q.RaceProps.Humanlike && Rand.Chance(0.5f)) LinarianTime.GiveMindIll(q);
                aged++;
            }
            if (map != null) FleckMaker.Static(f.at, map, FleckDefOf.PsycastAreaEffect, f.radius);
            if (dust + aged > 0)
                LinarianTactics.Notify(f.her, "time closes over the fold: " + (dust > 0 ? dust + " turn to dust" : "") + (dust > 0 && aged > 0 ? ", " : "") + (aged > 0 ? aged + " grow old in a breath" : "") + ".");
        }

        public override void MapComponentUpdate()
        {
            if (folds.Count == 0 || Find.CurrentMap != map || RimWorld.Planet.WorldRendererUtility.WorldSelected) return;
            float t = Time.realtimeSinceStartup;
            foreach (Fold f in folds)
            {
                GenDraw.DrawRadiusRing(f.at, f.radius, Violet);
                Vector3 c = f.at.ToVector3Shifted();
                LinarianRivals.Glow(c, f.radius * 2.2f * (0.96f + 0.04f * Mathf.Sin(t * 1.3f)), Swirl);
                LinarianRivals.Glow(c, f.radius * 1.2f * (0.9f + 0.1f * Mathf.Sin(t * 2.1f + 1f)), Swirl);
            }
        }
    }
}
