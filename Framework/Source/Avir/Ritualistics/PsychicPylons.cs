using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avir.Artifactorics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Ritualistics
{
    // =====================================================================================
    // PSYCHIC STORMS (drones and soothes) as a resource.
    // The psychic pylon charges itself from a storm and, while it is charged, shields everyone of
    // ours in its zone from it; it gives electricity while the storm lasts. Its charge bleeds away
    // afterwards - a ritual with the Drain rune can catch part of it as crystallized psychic energy.
    // Crystals feed rituals, make storm charms (one pawn ignores storms until the storm passes) and
    // load the mood resonator, which pulses calm onto friends or dread onto foes anywhere on the map.
    // =====================================================================================

    public enum StormFilter { All, Drones, Soothes }

    [DefOf]
    public static class PsychicDefOf
    {
        public static ThingDef Avir_PsyCrystal;
        public static HediffDef Avir_StormCharm;
        public static HediffDef Avir_PsychicDread;
        public static ThoughtDef Avir_PulseCalm;
        public static ThoughtDef Avir_PulseDread;
        static PsychicDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(PsychicDefOf)); }
    }

    /// <summary>Who is shielded from which storm.</summary>
    public static class PsychicShelter
    {
        public static bool Shielded(Pawn p, bool drone)
        {
            if (p == null) return false;
            if (p.health?.hediffSet?.HasHediff(PsychicDefOf.Avir_StormCharm) == true) return true;
            Map map = p.MapHeld;
            if (map == null || CompPsychicPylon.All.Count == 0 || p.HostileTo(Faction.OfPlayer)) return false;
            foreach (CompPsychicPylon py in CompPsychicPylon.All)
                if (py.parent.Map == map && py.Shields(drone) && (p.PositionHeld - py.parent.Position).LengthHorizontalSquared <= py.Range * py.Range)
                    return true;
            return false;
        }

        /// <summary>Drone steps over this map (0..4) and soothes, as the pylon hears them.</summary>
        public static int StormSteps(Map map, out bool soothe)
        {
            soothe = PsychicField.Soothed(map, null);
            return Mathf.Max(PsychicField.DroneSteps(map, Gender.Male), PsychicField.DroneSteps(map, Gender.Female));
        }
    }

    /// <summary>The drone and soothe moods do not reach shielded pawns.</summary>
    [HarmonyPatch(typeof(ThoughtWorker), nameof(ThoughtWorker.CurrentState))]
    public static class Patch_PsychicShelterThoughts
    {
        private static readonly Dictionary<ThoughtDef, int> Kind = new Dictionary<ThoughtDef, int>();

        public static void Postfix(ThoughtWorker __instance, Pawn p, ref ThoughtState __result)
        {
            if (!__result.Active) return;
            ThoughtDef def = __instance.def;
            if (!Kind.TryGetValue(def, out int k))
            {
                string n = def.defName;
                k = n.Contains("PsychicDrone") ? 1 : n.Contains("PsychicSoothe") ? 2 : 0;
                Kind[def] = k;
            }
            if (k != 0 && PsychicShelter.Shielded(p, k == 1)) __result = ThoughtState.Inactive;
        }
    }

    // ---------------------------------------------------------------- the pylon

    public class CompProperties_PsychicPylon : CompProperties
    {
        public float maxCharge = 1000f;
        /// <summary>Charge gained per rare tick, per drone step (a soothe counts as 2).</summary>
        public float chargePerStep = 4f;
        public float baseRange = 8f;
        public float rangePerQuality = 1f;
        public float rangePerStone = 3f;
        public CompProperties_PsychicPylon() { compClass = typeof(CompPsychicPylon); }
    }

    public class CompPsychicPylon : ThingComp, IArtifactHostEffect
    {
        public float charge;
        public StormFilter filter = StormFilter.All;

        public static readonly HashSet<CompPsychicPylon> All = new HashSet<CompPsychicPylon>();
        public CompProperties_PsychicPylon Props => (CompProperties_PsychicPylon)props;
        private CompArtifactHost Host => parent.TryGetComp<CompArtifactHost>();

        public float MaxCharge => Props.maxCharge * (Host?.BuildingQualityFactor ?? 1f);

        /// <summary>The zone: wider with inlaid stones (their quality and number).</summary>
        public float Range
        {
            get
            {
                CompArtifactHost h = Host;
                float r = Props.baseRange;
                if (h != null && h.Working > 0) r += Props.rangePerQuality * (int)h.BestQuality + Props.rangePerStone * h.Working;
                return r;
            }
        }

        public bool Takes(bool drone) => filter == StormFilter.All || (drone ? filter == StormFilter.Drones : filter == StormFilter.Soothes);

        /// <summary>A storm it takes is over the map right now.</summary>
        public bool StormNow(out int steps)
        {
            steps = 0;
            if (parent.Map == null) return false;
            int d = PsychicShelter.StormSteps(parent.Map, out bool soothe);
            if (d > 0 && Takes(true)) steps = d;
            if (soothe && Takes(false)) steps = Mathf.Max(steps, 2);
            return steps > 0;
        }

        public bool Shields(bool drone) => charge > 0f && Takes(drone) && StormNow(out _);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            All.Add(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            All.Remove(this);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            All.Add(this);
            if (StormNow(out int steps))
            {
                charge = Mathf.Min(MaxCharge, charge + Props.chargePerStep * steps);
                if (Rand.Chance(0.3f)) FleckMaker.ThrowLightningGlow(parent.DrawPos + new Vector3(0f, 0f, 0.6f), parent.Map, 0.7f);
            }
            else if (charge > 0f) charge = Mathf.Max(0f, charge - MaxCharge / 240f);   // bleeds away in about a day
        }

        /// <summary>Taken by a Drain ritual.</summary>
        public float Take(float amount)
        {
            float t = Mathf.Min(charge, amount);
            charge -= t;
            return t;
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (Range < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(parent.Position, Range);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Takes: " + FilterLabel(filter),
                defaultDesc = "Which psychic storms the pylon takes: it charges from them, shields its zone from them and gives power while they last. "
                              + "A storm it does not take passes through - for example, let a soothe through and block only drones.",
                icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Barrier")?.Ink ?? BaseContent.BadTex,
                action = () =>
                {
                    filter = filter == StormFilter.All ? StormFilter.Drones : filter == StormFilter.Drones ? StormFilter.Soothes : StormFilter.All;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            };
            if (Prefs.DevMode)
                yield return new Command_Action { defaultLabel = "Dev: fill charge", action = () => charge = MaxCharge };
        }

        public string EffectLine() =>
            "Takes " + FilterLabel(filter) + ". Zone " + Range.ToString("0.#") + " cells: while a storm it takes is over the map and it holds charge, "
            + "everyone of ours in the zone is shielded from it; it gives up to 1000 W meanwhile. Charge " + Mathf.FloorToInt(charge) + " / " + Mathf.FloorToInt(MaxCharge) + ".";

        public static string FilterLabel(StormFilter f) => f == StormFilter.All ? "all psychic storms" : f == StormFilter.Drones ? "drones only" : "soothes only";

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            sb.Append("Psychic charge: ").Append(Mathf.FloorToInt(charge)).Append(" / ").Append(Mathf.FloorToInt(MaxCharge));
            sb.Append("\nTakes ").Append(FilterLabel(filter)).Append(", zone ").Append(Range.ToString("0.#")).Append(" cells");
            if (StormNow(out int steps)) sb.Append(charge > 0f ? "\nA storm is over the map: charging, shielding the zone, giving power." : "\nA storm is over the map: charging.");
            else if (charge > 0f) sb.Append("\nNo storm: the charge bleeds away. A Drain ritual can catch part of it as crystals.");
            return sb.ToString();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charge, "charge");
            Scribe_Values.Look(ref filter, "filter", StormFilter.All);
        }
    }

    /// <summary>Electricity while a storm the pylon takes is over the map.</summary>
    public class CompPowerPlant_PsychicPylon : CompPowerPlant
    {
        protected override float DesiredPowerOutput
        {
            get
            {
                CompPsychicPylon py = parent.TryGetComp<CompPsychicPylon>();
                if (py == null || !py.StormNow(out int steps)) return 0f;
                return base.DesiredPowerOutput * Mathf.Min(2f, steps / 2f);
            }
        }
    }

    // ---------------------------------------------------------------- the storm charm

    /// <summary>Clipped onto a pawn: no psychic storm reaches them. It crumbles once the storm it met has passed.</summary>
    public class Hediff_StormCharm : HediffWithComps
    {
        public bool metStorm;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!pawn.IsHashIntervalTick(250, delta) || pawn.MapHeld == null) return;
            bool storm = PsychicShelter.StormSteps(pawn.MapHeld, out bool soothe) > 0 || soothe;
            if (storm) metStorm = true;
            else if (metStorm)
            {
                if (pawn.Faction == Faction.OfPlayer)
                    Messages.Message("The storm has passed: " + pawn.LabelShort + "'s storm charm crumbles to dust.", pawn, MessageTypeDefOf.NeutralEvent, false);
                pawn.health.RemoveHediff(this);
            }
        }

        public override string LabelInBrackets => metStorm ? "holding a storm off" : "waiting for a storm";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref metStorm, "metStorm");
        }
    }

    public class CompUseEffect_StormCharm : CompUseEffect
    {
        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            if (!usedBy.health.hediffSet.HasHediff(PsychicDefOf.Avir_StormCharm))
                usedBy.health.AddHediff(PsychicDefOf.Avir_StormCharm);
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p) =>
            p.health.hediffSet.HasHediff(PsychicDefOf.Avir_StormCharm) ? "already wears a storm charm" : base.CanBeUsedBy(p);
    }

    // ---------------------------------------------------------------- the mood resonator

    public class CompProperties_MoodResonator : CompProperties
    {
        public float baseRadius = 4f;
        public float radiusPerQuality = 0.5f;
        public float radiusPerStone = 1.5f;
        public CompProperties_MoodResonator() { compClass = typeof(CompMoodResonator); }
    }

    /// <summary>Spends one crystal per pulse, anywhere on the map: calm onto our people, dread onto foes.</summary>
    public class CompMoodResonator : ThingComp, IArtifactHostEffect
    {
        private CompProperties_MoodResonator Props => (CompProperties_MoodResonator)props;
        private CompArtifactHost Host => parent.TryGetComp<CompArtifactHost>();
        private CompRefuelable Fuel => parent.TryGetComp<CompRefuelable>();

        public float Radius
        {
            get
            {
                CompArtifactHost h = Host;
                float r = Props.baseRadius;
                if (h != null && h.Working > 0) r += Props.radiusPerQuality * (int)h.BestQuality + Props.radiusPerStone * h.Working;
                return r;
            }
        }

        /// <summary>Strength: the inlaid stones' power, 1 without them.</summary>
        public float Strength => Mathf.Max(1f, Host?.Power ?? 1f);

        private bool Loaded => Fuel != null && Fuel.Fuel >= 1f;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;
            Command_Action calm = new Command_Action
            {
                defaultLabel = "Pulse of calm...",
                defaultDesc = "Spend one crystal: everyone of ours within " + Radius.ToString("0.#") + " cells of the chosen spot feels a wave of calm (good mood for a day), and dread is lifted from them.",
                icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Growth")?.Ink ?? BaseContent.BadTex,
                action = () => Target(false)
            };
            Command_Action dread = new Command_Action
            {
                defaultLabel = "Pulse of dread...",
                defaultDesc = "Spend one crystal: every foe within " + Radius.ToString("0.#") + " cells of the chosen spot is struck by dread - a heavy mood for a day, a weaker hold against breaking, "
                              + "and a chance to break at once if their mood is already low.",
                icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Release")?.Ink ?? BaseContent.BadTex,
                action = () => Target(true)
            };
            if (!Loaded) { calm.Disable("no crystals loaded"); dread.Disable("no crystals loaded"); }
            yield return calm;
            yield return dread;
        }

        private void Target(bool dread)
        {
            var parms = new TargetingParameters { canTargetLocations = true, canTargetPawns = true, canTargetBuildings = true, canTargetItems = true };
            PulseFx.Aiming = this;
            PulseFx.AimingDread = dread;
            Find.Targeter.BeginTargeting(parms, t => Pulse(t.Cell, dread));
        }

        public void Pulse(IntVec3 at, bool dread)
        {
            Map map = parent.Map;
            if (map == null || !Loaded || !at.InBounds(map)) return;
            Fuel.ConsumeFuel(1f);
            float r = Radius, s = Strength;
            FleckMaker.Static(at, map, FleckDefOf.PsycastAreaEffect, r * 2f);
            PulseFx.Add(map, at, r, dread);
            SoundDefOf.PsycastPsychicPulse.PlayOneShot(new TargetInfo(at, map));
            int n = 0;
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (q.Dead || q.needs?.mood == null || (q.Position - at).LengthHorizontalSquared > r * r) continue;
                bool foe = q.HostileTo(Faction.OfPlayer);
                if (dread && foe)
                {
                    q.needs.mood.thoughts.memories.TryGainMemory(PsychicDefOf.Avir_PulseDread);
                    Hediff h = q.health.hediffSet.GetFirstHediffOfDef(PsychicDefOf.Avir_PsychicDread) ?? q.health.AddHediff(PsychicDefOf.Avir_PsychicDread);
                    h.Severity = Mathf.Min(1f, h.Severity + 0.5f * s);
                    MentalBreaker mb = q.mindState?.mentalBreaker;
                    if (mb != null && q.needs.mood.CurLevel < mb.BreakThresholdMinor + 0.1f && Rand.Chance(Mathf.Min(0.6f, 0.25f * s)))
                        mb.TryDoRandomMoodCausedMentalBreak();
                    n++;
                }
                else if (!dread && !foe)
                {
                    q.needs.mood.thoughts.memories.TryGainMemory(PsychicDefOf.Avir_PulseCalm);
                    Hediff h = q.health.hediffSet.GetFirstHediffOfDef(PsychicDefOf.Avir_PsychicDread);
                    if (h != null) q.health.RemoveHediff(h);
                    n++;
                }
            }
            Messages.Message((dread ? "A pulse of dread struck " : "A pulse of calm reached ") + n + (n == 1 ? " pawn." : " pawns."), new TargetInfo(at, map), MessageTypeDefOf.NeutralEvent, false);
        }

        public string EffectLine() =>
            "Each crystal is one pulse to any spot on the map: radius " + Radius.ToString("0.#") + " cells, strength x" + Strength.ToString("0.00")
            + ". Calm onto our people, or dread onto foes. Crystals loaded: " + Mathf.FloorToInt(Fuel?.Fuel ?? 0f) + ".";

        public override string CompInspectStringExtra() =>
            "Pulse radius " + Radius.ToString("0.#") + " cells, strength x" + Strength.ToString("0.00");
    }

    /// <summary>The pulse's reach: a ring under the mouse while choosing the spot, and a visible wave
    /// over the whole area when it goes off.</summary>
    public class PulseFx : MapComponent
    {
        public static CompMoodResonator Aiming;
        public static bool AimingDread;
        private struct Wave { public Map map; public Vector3 at; public float radius; public float start; public bool dread; }
        private static readonly List<Wave> Waves = new List<Wave>();
        private const float Seconds = 1.6f;

        public PulseFx(Map map) : base(map) { }

        public static void Add(Map map, IntVec3 at, float radius, bool dread) =>
            Waves.Add(new Wave { map = map, at = at.ToVector3Shifted(), radius = radius, start = Time.realtimeSinceStartup, dread = dread });

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            if (Aiming != null)
            {
                if (!Find.Targeter.IsTargeting || Aiming.parent.Map != map) Aiming = null;
                else
                {
                    IntVec3 c = Verse.UI.MouseCell();
                    if (c.InBounds(map))
                    {
                        GenDraw.DrawRadiusRing(c, Aiming.Radius, AimingDread ? new Color(0.85f, 0.3f, 1f) : new Color(0.5f, 0.85f, 1f));
                        // who it would reach
                        foreach (Pawn q in map.mapPawns.AllPawnsSpawned)
                            if (!q.Dead && q.needs?.mood != null && (q.Position - c).LengthHorizontalSquared <= Aiming.Radius * Aiming.Radius
                                && q.HostileTo(Faction.OfPlayer) == AimingDread)
                                GenDraw.DrawTargetHighlight(q);
                    }
                }
            }
            float now = Time.realtimeSinceStartup;
            for (int i = Waves.Count - 1; i >= 0; i--)
            {
                Wave w = Waves[i];
                float t = (now - w.start) / Seconds;
                if (t >= 1f || w.map == null) { Waves.RemoveAt(i); continue; }
                if (w.map != map) continue;
                // a ring that runs out to the edge of the area, and the area itself, fading
                Color col = w.dread ? new Color(0.65f, 0.2f, 0.95f) : new Color(0.45f, 0.85f, 1f);
                float grow = Mathf.Min(1f, t * 1.6f);
                Vector3 pos = w.at; pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Material ring = MaterialPool.MatFrom(RitualTex.Ring, ShaderDatabase.Transparent, new Color(col.r, col.g, col.b, (1f - t) * 0.9f));
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(w.radius * 2f * grow, 1f, w.radius * 2f * grow)), ring, 0);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(w.radius * 2f, 1f, w.radius * 2f)), ring, 0);
                Material disc = MaterialPool.MatFrom(RitualTex.Disc, ShaderDatabase.Transparent, new Color(col.r, col.g, col.b, (1f - t) * 0.22f));
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(w.radius * 2f, 1f, w.radius * 2f)), disc, 0);
            }
        }
    }

    /// <summary>Dread from a pulse: a weaker hold against breaking; with low mood it may break on its own.</summary>
    public class Hediff_PsychicDread : HediffWithComps
    {
        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!pawn.IsHashIntervalTick(250, delta)) return;
            Severity -= 0.02f;   // fades in about half a day
            MentalBreaker mb = pawn.mindState?.mentalBreaker;
            if (mb == null || pawn.InMentalState || pawn.needs?.mood == null) return;
            if (pawn.needs.mood.CurLevel < mb.BreakThresholdMinor && Rand.Chance(0.04f * Severity)) mb.TryDoRandomMoodCausedMentalBreak();
        }
    }
}
