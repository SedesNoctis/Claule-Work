using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    // =====================================================================================
    // External flow (concept §6): the energy of everything on the map, in "%" of psychic sensitivity.
    // Calm below 375 000%; above it the map draws storms (the weather side comes in a later slice).
    // Composure: how well a pawn holds the energies - poor composure makes rituals collapse.
    // =====================================================================================
    public class MapComponent_ExternalFlow : MapComponent
    {
        /// <summary>The tiers of the stream: calm below Calm, then restless, stormy, tempest, cataclysm. Raised fifteenfold
        /// (one Avir caravan coming in pushed the old scale halfway to the top).</summary>
        public const float Scale = 15f;
        public const float Calm = 25000f * Scale, Restless = 50000f * Scale, Stormy = 100000f * Scale, Tempest = 250000f * Scale;
        private const int Interval = 600;

        /// <summary>Current flow (%), and what running rituals add by the weight of their deed.</summary>
        public float flow;
        public float deeds;

        public MapComponent_ExternalFlow(Map map) : base(map) { }

        public static MapComponent_ExternalFlow Of(Map m) => m?.GetComponent<MapComponent_ExternalFlow>();

        public float Total => devFlow >= 0f ? devFlow : flow + deeds;
        /// <summary>Dev mode: the flow held at this value (-1 - off).</summary>
        public float devFlow = -1f;

        public static string Tier(float f) =>
            f < Calm ? "calm" : f < Restless ? "restless" : f < Stormy ? "stormy" : f < Tempest ? "tempest" : "cataclysm";

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % Interval == 0)
            {
                Recount();
                deeds = Mathf.Max(0f, deeds * 0.5f);   // a deed's pull fades unless a ritual keeps adding it
            }
            if (now % 2500 == 1250) FlowWeather.Hour(map, this);
        }

        public int lastTier;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastTier, "lastTier");
            Scribe_Values.Look(ref devFlow, "devFlow", -1f);
        }

        public static int TierIndex(float f) => f < Calm ? 0 : f < Restless ? 1 : f < Stormy ? 2 : f < Tempest ? 3 : 4;

        public void Recount()
        {
            float f = 0f;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || !p.RaceProps.Humanlike) continue;
                f += p.GetStatValue(StatDefOf.PsychicSensitivity) * 100f * (Avir.Magic.AvirMagic.On && Avir.Magic.Mages.Core(p) != null ? Mathf.Max(1f, AvirMod.Settings.mageFlowWeight) : 1f);
                if (p.psychicEntropy != null && p.HasPsylink) f += p.psychicEntropy.CurrentPsyfocus * 1000f;
                foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p)) f += (s.inlaid?.charge ?? 0f) * 1000f;
            }
            foreach (Thing t in map.listerThings.ThingsOfDef(ArtifactoricsDefOf.Avir_Artifact))
                f += (t.TryGetComp<CompArtifact>()?.state?.charge ?? 0f) * 1000f;
            foreach (CompArtifactHost h in CompArtifactHost.All)
                if (h.parent.Map == map)
                    foreach (ArtifactState st in h.slots) if (st != null) f += st.charge * 1000f;
            ThingDef battery = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_CrystalBattery");
            if (battery != null)
                foreach (Thing t in map.listerThings.ThingsOfDef(battery))
                    f += (t.TryGetComp<CompCrystalBattery>()?.Stored ?? 0f) * 10f * t.stackCount;
            f += Count(map, "Essentium") * 10f + Count(map, "BloodstoneTisserium") * 30f;
            // weather: storms are full of energy
            // psychic drones pour dark energy into the map; a soothe calms it
            f += 2000f * PsychicField.DroneSteps(map, Gender.Male) + 2000f * PsychicField.DroneSteps(map, Gender.Female);
            if (PsychicField.Soothed(map, null)) f *= 0.85f;
            // psychic pylons hold what they caught; crystals hold it too
            foreach (CompPsychicPylon py in CompPsychicPylon.All) if (py.parent.Map == map) f += py.charge;
            f += Count(map, "Avir_PsyCrystal") * 100f;
            string w = map.weatherManager?.curWeather?.defName ?? "";
            if (w.Contains("Thunderstorm")) f += w.StartsWith("Dry") ? 8000f : 5000f;
            else if (w.Contains("Snow") && w.Contains("Hard")) f += 3000f;
            // a barrier pulls the flow by itself
            foreach (BarrierDome d in BarrierDome.All) if (d.map == map) f += 200f * d.radius;
            flow = f;
        }

        private static int Count(Map map, string defName)
        {
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (d == null) return 0;
            int n = 0;
            foreach (Thing t in map.listerThings.ThingsOfDef(d)) n += t.stackCount;
            return n;
        }
    }

    /// <summary>What the external flow does to the map, hour by hour (concept §6, tiers of pressure).</summary>
    public static class FlowWeather
    {
        private static readonly string[] Names = { "calm", "restless", "stormy", "tempest", "cataclysm" };

        public static void Hour(Map map, MapComponent_ExternalFlow ext)
        {
            int tier = MapComponent_ExternalFlow.TierIndex(ext.Total);
            if (tier != ext.lastTier)
            {
                if (tier > ext.lastTier && tier >= 1) Arlia.Say(tier >= 4 ? "bells" : "flow");
                if (tier >= 3 && tier > ext.lastTier)
                    Find.LetterStack.ReceiveLetter("The stream: " + Names[tier],
                        "The external stream over this map has risen to " + ext.Total.ToString("0") + "% - " + Names[tier] + ". "
                        + (tier == 3 ? "Energy storms: lightning seeks out artifact buildings, artifacts lose their charge, sensitive minds suffer."
                                     : "Cataclysm: the storm will not end, the cold comes, lightning falls everywhere. Spend or move the energy - or shield what matters.")
                        + "\n\nCharged artifacts, batteries, essentium, psychic crystals, psycasters and running rituals all add to it.", LetterDefOf.ThreatBig);
                else if (tier >= 1 && tier > ext.lastTier)
                    Messages.Message("The external stream is " + Names[tier] + " (" + ext.Total.ToString("0") + "%): the weather turns.", MessageTypeDefOf.CautionInput, false);
                else if (tier < ext.lastTier)
                    Messages.Message("The external stream eases: " + Names[tier] + ".", MessageTypeDefOf.NeutralEvent, false);
                ext.lastTier = tier;
            }
            if (tier == 0) return;
            // storms and blizzards
            float stormChance = tier == 1 ? 0.04f : tier == 2 ? 0.08f : tier == 3 ? 0.15f : 1f;
            if (Rand.Chance(stormChance)) Storm(map);
            // cold snaps (the ice storm of the book: hard snow under a cold snap)
            float coldChance = tier == 2 ? 0.02f : tier == 3 ? 0.05f : tier == 4 ? 0.25f : 0f;
            if (Rand.Chance(coldChance)) Condition(map, GameConditionDefOf.ColdSnap, 60000);
            if (tier == 4 && Rand.Chance(0.04f)) Condition(map, GameConditionDefOf.Flashstorm, 15000);
            // a tempest calls up the howling storm (the incident brings it too, the more often the stronger the flow)
            float howl = tier == 3 ? 0.01f : tier == 4 ? 0.04f : 0f;
            if (Rand.Chance(howl)) Avir.North.GameCondition_HowlingStorm.Start(map, Rand.RangeInclusive(2, tier == 4 ? 21 : 10));
            // lightning drawn to energy
            int strikes = tier == 2 ? (Rand.Chance(0.25f) ? 1 : 0) : tier == 3 ? Rand.RangeInclusive(0, 2) : tier == 4 ? Rand.RangeInclusive(2, 5) : 0;
            for (int i = 0; i < strikes; i++) Strike(map, tier);
            if (tier >= 3)
            {
                // artifacts lose charge (an hour of a tempest takes 5%, a cataclysm 10%)
                float loss = tier == 3 ? 0.05f : 0.1f;
                foreach (CompArtifactHost h in CompArtifactHost.All)
                    if (h.parent.Map == map && !BarrierDome.Covers(map, h.parent.Position))
                        foreach (ArtifactState st in h.slots) if (st != null) st.charge = Mathf.Max(0f, st.charge - loss * Rand.Range(0.5f, 1.5f));
                // sensitive minds suffer
                HediffDef pressure = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_FlowPressure");
                float threshold = tier == 3 ? 1.5f : 1f;
                if (pressure != null)
                    foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
                    {
                        if (p.Dead || !p.RaceProps.Humanlike || p.GetStatValue(StatDefOf.PsychicSensitivity) < threshold || PsychicShelter.Shielded(p, true)) continue;
                        if (BarrierDome.Covers(map, p.Position)) continue;   // a barrier keeps the storm of the stream off
                        Hediff h = p.health.hediffSet.GetFirstHediffOfDef(pressure) ?? p.health.AddHediff(pressure);
                        h.Severity = Mathf.Min(1f, h.Severity + (tier == 3 ? 0.08f : 0.15f));
                    }
            }
        }

        private static void Storm(Map map)
        {
            string cur = map.weatherManager.curWeather?.defName ?? "";
            if (cur.Contains("Thunderstorm") || (cur.Contains("Snow") && cur.Contains("Hard"))) return;
            bool cold = map.mapTemperature.OutdoorTemp < 0f;
            WeatherDef w = DefDatabase<WeatherDef>.GetNamedSilentFail(cold ? "SnowHard" : "RainyThunderstorm");
            if (w != null) map.weatherManager.TransitionTo(w);
        }

        private static void Condition(Map map, GameConditionDef def, int ticks)
        {
            if (def == null || map.gameConditionManager.ConditionIsActive(def)) return;
            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(def, ticks));
        }

        /// <summary>A stormy flow strikes near the sources of energy; a tempest strikes the artifact buildings themselves; a cataclysm anywhere.</summary>
        private static void Strike(Map map, int tier)
        {
            var sources = new List<Thing>();
            foreach (CompArtifactHost h in CompArtifactHost.All) if (h.parent.Map == map && h.parent.Spawned) sources.Add(h.parent);
            foreach (CompPsychicPylon py in CompPsychicPylon.All) if (py.parent.Map == map && py.parent.Spawned) sources.Add(py.parent);
            IntVec3 cell;
            if (sources.Count > 0 && (tier < 4 || Rand.Chance(0.5f)))
            {
                Thing src = sources.RandomElement();
                if (tier >= 3 && !src.Position.Roofed(map)) cell = src.Position;
                else if (!CellFinder.TryFindRandomCellNear(src.Position, map, 6, c => !c.Roofed(map), out cell)) return;
            }
            else if (!CellFinder.TryFindRandomCell(map, c => !c.Roofed(map) && !c.Fogged(map), out cell)) return;
            // the barrier takes the strike on itself
            if (BarrierDome.Covers(map, cell)) { FleckMaker.ThrowLightningGlow(cell.ToVector3Shifted(), map, 2f); return; }
            map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, cell));
        }
    }

    /// <summary>Psychic drones and soothes as the rituals feel them.</summary>
    public static class PsychicField
    {
        /// <summary>0 none, 1 low ... 4 extreme: the strongest drone on this map for this gender.</summary>
        public static int DroneSteps(Map map, Gender g)
        {
            PsychicDroneLevel l = map.gameConditionManager.GetHighestPsychicDroneLevelFor(g, map);
            switch (l)
            {
                case PsychicDroneLevel.BadLow: return 1;
                case PsychicDroneLevel.BadMedium: return 2;
                case PsychicDroneLevel.BadHigh: return 3;
                case PsychicDroneLevel.BadExtreme: return 4;
                default: return 0;
            }
        }

        /// <summary>A psychic soothe over this map, for this gender (null - for anyone).</summary>
        public static bool Soothed(Map map, Gender? g)
        {
            foreach (GameCondition c in Conditions(map))
            {
                if (c?.def == null || !c.def.defName.Contains("PsychicSoothe")) continue;
                if (g == null) return true;
                var field = HarmonyLib.AccessTools.Field(c.GetType(), "gender");
                if (field == null || (Gender)field.GetValue(c) == g.Value) return true;
            }
            return false;
        }

        private static IEnumerable<GameCondition> Conditions(Map map)
        {
            foreach (GameCondition c in map.gameConditionManager.ActiveConditions) yield return c;
            if (Find.World?.gameConditionManager != null)
                foreach (GameCondition c in Find.World.gameConditionManager.ActiveConditions) yield return c;
        }
    }

    /// <summary>Composure: traits and mood. Iron will holds, a volatile mind spills.</summary>
    public class StatPart_Composure : StatPart
    {
        /// <summary>Per degree of the trait (a trait without degrees counts as 1).</summary>
        private static readonly Dictionary<string, float> TraitShift = new Dictionary<string, float>
        {
            { "Nerves", 0.10f },              // iron-willed +0.2, steadfast +0.1, nervous -0.1, volatile -0.2
            { "NaturalMood", 0.08f },         // sanguine +0.16, optimist +0.08, pessimist -0.08, depressive -0.16
            { "Neurotic", -0.08f },           // neurotic -0.08, very neurotic -0.16: an anxious mind spills the flow
            { "PsychicSensitivity", -0.08f }, // hypersensitive -0.16 ... dull +0.08, deaf +0.16: the flow sweeps the sensitive
            { "Psychopath", 0.12f },          // nothing in them for the flow to grip
            { "Ascetic", 0.1f },              // a disciplined mind
            { "TooSmart", -0.08f },           // a racing mind loses the chain
            { "Wimp", -0.1f },
            { "Pyromaniac", -0.1f },
            { "Masochist", 0.05f },           // pain does not break their hold
        };

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn p)) return;
            val += Shift(p);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is Pawn p)) return null;
            float s = Shift(p) - GameComponent_AvirComposure.ExperienceShift(p) - GameComponent_AvirComposure.MeditationShift(p) + GameComponent_AvirComposure.Strain(p);
            var sb = new System.Text.StringBuilder();
            if (Mathf.Abs(s) >= 0.001f) sb.AppendLine("Traits, mood, pliability of belief, psychic drone or soothe, psylink and intellect: " + (s >= 0 ? "+" : "") + s.ToStringPercent());
            float xp = GameComponent_AvirComposure.ExperienceShift(p);
            if (xp >= 0.001f) sb.AppendLine("Ritual practice (" + GameComponent_AvirComposure.Practice(p).ToString("0") + "): +" + xp.ToStringPercent());
            float strain = GameComponent_AvirComposure.Strain(p);
            if (strain >= 0.001f) sb.AppendLine("Strain of rituals today (sleep or meditation takes it off): -" + strain.ToStringPercent());
            float med = GameComponent_AvirComposure.MeditationShift(p);
            if (med >= 0.001f) sb.AppendLine("Calm (" + GameComponent_AvirComposure.MeditationHours(p).ToString("0.#") + " h of meditation in three days): +" + med.ToStringPercent());
            return sb.Length == 0 ? null : sb.ToString().TrimEndNewlines();
        }

        private static float Shift(Pawn p)
        {
            float s = 0f;
            if (p.story?.traits != null)
                foreach (Trait t in p.story.traits.allTraits)
                    if (TraitShift.TryGetValue(t.def.defName, out float per)) s += per * (t.Degree == 0 ? 1 : t.Degree);
            Need_Mood mood = p.needs?.mood;
            if (mood != null)
            {
                if (mood.CurLevel < 0.2f) s -= 0.3f;
                else if (mood.CurLevel < 0.35f) s -= 0.15f;
                else if (mood.CurLevel > 0.8f) s += 0.1f;
            }
            if (p.InMentalState) s -= 0.5f;
            // a pliable mind is a poor vessel: the easier the pawn loses its certainty, the worse its composure
            if (ModsConfig.IdeologyActive && StatDefOf.CertaintyLossFactor != null)
                s -= 0.3f * (p.GetStatValue(StatDefOf.CertaintyLossFactor) - 1f);
            // a natural conductor (horns) steadies the one who holds magic
            if (Avir.Magic.AvirMagic.On && Avir.Magic.MagicConductors.Horned(p)) s += Avir.Magic.MagicConductors.HornComposure;
            // the Avir: ambition rushes the flow - their base composure is 5%, not 25%
            if (p.genes?.Xenotype?.defName?.StartsWith("Avir") == true) s -= 0.20f;
            // psychic drones shake the mind, a soothe steadies it - as far as the pawn hears them
            Map m = p.MapHeld;
            if (m != null)
            {
                float hear = Mathf.Clamp(p.GetStatValue(StatDefOf.PsychicSensitivity), 0f, 2f);
                if (!PsychicShelter.Shielded(p, true)) s -= 0.08f * PsychicField.DroneSteps(m, p.gender) * hear;
                if (PsychicField.Soothed(m, p.gender) && !PsychicShelter.Shielded(p, false)) s += 0.1f * hear;
            }
            // a psycaster is used to holding energies; a trained mind keeps the chain clear
            s += 0.04f * p.GetPsylinkLevel();
            int intel = p.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            if (intel > 8) s += 0.01f * (intel - 8);
            // trained: the energy held in rituals over a life, and calm from meditation
            s += GameComponent_AvirComposure.ExperienceShift(p) + GameComponent_AvirComposure.MeditationShift(p) - GameComponent_AvirComposure.Strain(p);
            return s;
        }
    }

    [DefOf]
    public static class RitualDefOf
    {
        public static StatDef Avir_Composure;
        public static WorkTypeDef Avir_Ritualism;
        public static JobDef Avir_DrawRitual;
        public static JobDef Avir_PerformRitual;
        public static JobDef Avir_PerformRitualAuto;
        public static JobDef Avir_ClearRitualCircle;
        public static HediffDef Avir_RitualBacklash;
        public static HediffDef Avir_RitualHeld;
        public static HediffDef Avir_StormMantle;
        static RitualDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(RitualDefOf)); }
    }
}
