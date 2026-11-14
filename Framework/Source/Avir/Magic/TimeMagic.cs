using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>
    /// TIME - a great element (the author, Fix269-270). Its circle holds others; the target decides how they act:
    ///  - aimed at a PLACE: they lie there as a TRAP, unseen by the foe (the player sees it faintly), and act when a foe
    ///    comes near - on that foe, who crumbles too. It lasts hours, by the cast's power;
    ///  - aimed at a FOE: they act at once, and the foe takes Time's CRUMBLING (sudden age on the body and its parts);
    ///  - its own Growth: what they lay on bodies lasts longer (x1.5 for each Growth).
    /// Its own verbs: Release - crumbling at the point (bodies and parts aged, things worn, growing plants ahead, ripe
    /// crops rotten); Restoration - REWIND: fresh wounds close, the crumbling leaves friends, things get back their wear.
    /// Foresight comes later (the author).
    /// </summary>
    public static class TimeMagic
    {
        public static readonly Color TimeColor = new Color(0.88f, 0.74f, 0.48f);
        public const float LongerPer = 0.5f;

        public static bool Is(RuneDef e) => e != null && e.kind == RuneKind.Time;
        public static bool Any(SpellCircle root) => root != null && root.All().Any(c => c.Great && Is(c.element));

        /// <summary>A circle of Time acting by its own runes: the great one, or a small one with no element inside it.</summary>
        public static bool IsLink(SpellCircle root, SpellCircle c)
        {
            if (c == null) return false;
            if (c.Great) return Is(c.element);
            if (c.element != null) return false;
            SpellCircle box = root?.ParentOf(c);
            return box != null && box.Great && Is(box.element);
        }

        /// <summary>The great circle of Time that holds this one (itself, if it is one); the nearest.</summary>
        public static SpellCircle BoxOf(SpellCircle root, SpellCircle c)
        {
            if (root == null || c == null) return null;
            if (c.Great && Is(c.element)) return c;
            for (SpellCircle k = root.ParentOf(c); k != null; k = root.ParentOf(k))
                if (k.Great && Is(k.element)) return k;
            return null;
        }

        public static float LongerOf(SpellCircle box) => 1f + LongerPer * box.OwnRunes.Count(r => r.kind == RuneKind.Growth);

        /// <summary>The foe a spell is aimed at: the pawn targeted, or a hostile one standing on the cell.</summary>
        public static Pawn FoeAt(Pawn caster, LocalTargetInfo t)
        {
            if (caster?.Map == null || !t.IsValid) return null;
            if (t.Pawn is Pawn p && !p.Dead && p.HostileTo(caster)) return p;
            return t.Cell.InBounds(caster.Map) ? t.Cell.GetThingList(caster.Map).OfType<Pawn>().FirstOrDefault(x => !x.Dead && x.HostileTo(caster)) : null;
        }

        /// <summary>While a circle in a box of Time "longer" acts: what it lays on bodies lasts this much longer.</summary>
        public static float Longer = 1f;

        public static string Text(SpellCircle box)
        {
            float l = LongerOf(box);
            return "Time round its circles: aimed at a place, they lie there as a trap, unseen by the foe, and act when one comes near; aimed at a foe, they act at once and the foe crumbles with sudden age" + (l > 1.001f ? "; what they lay on bodies lasts x" + l.ToString("0.#") : "");
        }

        // ------------------------------------------------------------------ its own verbs

        public static HediffDef Wither => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_TimeWither");

        public static void CastCircle(Pawn caster, SpellCircle circle, IntVec3 at, float power, Map map)
        {
            if (map == null || !at.InBounds(map)) return;
            var own = circle.OwnRunes.ToList();
            float r = 1.9f + 1.5f * own.Count(x => x.kind == RuneKind.Breadth);
            float p = power * (1f + 0.3f * own.Count(x => x.kind == RuneKind.Amplify));
            if (own.Any(x => x.kind == RuneKind.Release)) Crumble(caster, at, r, p, map);
            if (own.Any(x => x.kind == RuneKind.Restore)) Rewind(caster, at, r, p, map);
        }

        /// <summary>CRUMBLING (the author: "time's harm is a crumbling - a part loses its use from sudden age; chronomancers
        /// strike with ageing, the pawn and its parts").</summary>
        public static void Crumble(Pawn caster, IntVec3 at, float r, float p, Map map)
        {
            Dust(map, at, r);
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(at, map, r, true).ToList())
            {
                if (t.Destroyed) continue;
                if (t is Pawn pawn)
                {
                    if (pawn == caster || !MagicFx.Foe(pawn, caster) || SpellWards.Stops(pawn, true)) continue;
                    AgeBody(pawn, p, caster);
                    continue;
                }
                if (t is Plant pl)
                {
                    if (pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.25f * p);
                    else if (!pl.def.plant.IsTree && pl.def.plant.harvestedThingDef != null && Rand.Chance(0.3f * p)) pl.Kill();
                    continue;
                }
                if (t.def.useHitPoints && (t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Building))
                {
                    int dmg = Mathf.Max(1, Mathf.RoundToInt(t.MaxHitPoints * 0.12f * p));
                    t.HitPoints = Mathf.Max(1, t.HitPoints - dmg);
                }
            }
        }

        /// <summary>A body aged: two or three of its parts worn by sudden years (they work the worse; it mends over days),
        /// and the whole of it older.</summary>
        public static void AgeBody(Pawn pawn, float p, Pawn caster)
        {
            if (Wither == null || pawn.Dead) return;
            var parts = pawn.health.hediffSet.GetNotMissingParts().Where(x => x.depth == BodyPartDepth.Outside && x.def != BodyPartDefOf.Torso && !x.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource)).ToList();
            int n = Mathf.Clamp(Mathf.RoundToInt(1f + p * 1.5f), 1, 4);
            foreach (BodyPartRecord part in parts.InRandomOrder().Take(n))
            {
                Hediff h = pawn.health.hediffSet.hediffs.FirstOrDefault(x => x.def == Wither && x.Part == part);
                if (h == null) { h = HediffMaker.MakeHediff(Wither, pawn, part); h.Severity = 0f; pawn.health.AddHediff(h, part); }
                h.Severity = Mathf.Min(1f, h.Severity + 0.25f * p);
            }
            if (pawn.RaceProps.Humanlike && pawn.ageTracker != null)
                pawn.ageTracker.AgeBiologicalTicks += (long)(GenDate.TicksPerYear * 0.5f * p);
            if (pawn.Spawned) FleckMaker.ThrowDustPuffThick(pawn.DrawPos, pawn.Map, 1.2f, new Color(TimeColor.r, TimeColor.g, TimeColor.b, 0.6f));
        }

        /// <summary>REWIND: the last moments undone for friends - fresh wounds close, the crumbling leaves them, and things
        /// get back what they lost.</summary>
        public static void Rewind(Pawn caster, IntVec3 at, float r, float p, Map map)
        {
            Dust(map, at, r);
            int fresh = Mathf.RoundToInt(1800f * p);   // wounds younger than half a minute at power 1
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(at, map, r, true).ToList())
            {
                if (t is Pawn pawn)
                {
                    if (pawn.Dead || (caster != null && pawn.HostileTo(caster))) continue;
                    foreach (Hediff_Injury inj in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.ageTicks < fresh && !i.IsPermanent()).ToList())
                        inj.Heal(inj.Severity);
                    foreach (Hediff w in pawn.health.hediffSet.hediffs.Where(h => h.def == Wither).ToList())
                    { w.Severity -= 0.5f * p; if (w.Severity <= 0.01f) pawn.health.RemoveHediff(w); }
                    continue;
                }
                if (t.def.useHitPoints && t.Faction == caster?.Faction && t.HitPoints < t.MaxHitPoints)
                    t.HitPoints = Mathf.Min(t.MaxHitPoints, t.HitPoints + Mathf.RoundToInt(t.MaxHitPoints * 0.15f * p));
            }
        }

        private static void Dust(Map map, IntVec3 at, float r)
        {
            Color c = new Color(TimeColor.r, TimeColor.g, TimeColor.b, 0.5f);
            foreach (IntVec3 x in GenRadial.RadialCellsAround(at, r, true))
                if (x.InBounds(map) && Rand.Chance(0.35f)) FleckMaker.ThrowDustPuffThick(x.ToVector3Shifted(), map, Rand.Range(0.6f, 1.1f), c);
        }
    }

    /// <summary>Longer: what a circle in a box of Time "longer" lays on a body lasts longer.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_TimeLonger
    {
        public static void Postfix(Hediff hediff)
        {
            if (TimeMagic.Longer <= 1.001f || hediff == null) return;
            if (hediff.TryGetComp<HediffComp_Disappears>() is HediffComp_Disappears d) d.ticksToDisappear = Mathf.RoundToInt(d.ticksToDisappear * TimeMagic.Longer);
        }
    }

    // ------------------------------------------------------------------ traps of Time

    public class TimeTrap : IExposable
    {
        public Pawn caster;
        public SpellCircle root;
        public int from, to;          // which links of the spell's order it holds
        public IntVec3 at;
        public float radius, power;
        public int until;

        public void ExposeData()
        {
            Scribe_References.Look(ref caster, "caster");
            Scribe_Deep.Look(ref root, "root");
            Scribe_Values.Look(ref from, "from");
            Scribe_Values.Look(ref to, "to");
            Scribe_Values.Look(ref at, "at");
            Scribe_Values.Look(ref radius, "radius");
            Scribe_Values.Look(ref power, "power");
            Scribe_Values.Look(ref until, "until");
        }
    }

    public class MapComponent_TimeTraps : MapComponent
    {
        public List<TimeTrap> traps = new List<TimeTrap>();
        public const int MaxPerCaster = 5;
        public MapComponent_TimeTraps(Map map) : base(map) { }
        public static MapComponent_TimeTraps Of(Map m) => m?.GetComponent<MapComponent_TimeTraps>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref traps, "timeTraps", LookMode.Deep);
            if (traps == null) traps = new List<TimeTrap>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) traps.RemoveAll(t => t == null || t.root == null);
        }

        public void Lay(TimeTrap t)
        {
            var mine = traps.Where(x => x.caster == t.caster).ToList();
            if (mine.Count >= MaxPerCaster) traps.Remove(mine.OrderBy(x => x.until).First());
            traps.Add(t);
        }

        public override void MapComponentTick()
        {
            if (traps.Count == 0 || Find.TickManager.TicksGame % 30 != 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = traps.Count - 1; i >= 0; i--)
            {
                TimeTrap t = traps[i];
                if (t.caster == null || t.caster.Dead || now > t.until) { traps.RemoveAt(i); continue; }
                // asleep while its caster is away from the map: what it holds is the caster's own magic
                if (!t.caster.Spawned || t.caster.Map != map) continue;
                Pawn foe = GenRadial.RadialDistinctThingsAround(t.at, map, t.radius, true).OfType<Pawn>().FirstOrDefault(p => !p.Dead && p.HostileTo(t.caster));
                if (foe == null) continue;
                traps.RemoveAt(i);
                FleckMaker.ThrowDustPuffThick(t.at.ToVector3Shifted(), map, 2f, new Color(TimeMagic.TimeColor.r, TimeMagic.TimeColor.g, TimeMagic.TimeColor.b, 0.7f));
                MapComponent_SpellChain.Spring(t.caster, t.root, t.from, t.to, new LocalTargetInfo(foe), t.power, 0);
            }
        }

        public override void MapComponentUpdate()
        {
            if (traps.Count == 0 || Find.CurrentMap != map) return;
            // the player sees their own traps faintly; the foe does not
            foreach (TimeTrap t in traps)
                if (t.caster?.Faction == Faction.OfPlayer) GenDraw.DrawRadiusRing(t.at, t.radius, new Color(TimeMagic.TimeColor.r, TimeMagic.TimeColor.g, TimeMagic.TimeColor.b, 0.35f));
        }
    }

    public static class TimeDev
    {
        [LudeonTK.DebugAction("Magic", "Time: crumble here (power 1)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Crumble() => TimeMagic.Crumble(null, Verse.UI.MouseCell(), 2.9f, 1f, Find.CurrentMap);

        [LudeonTK.DebugAction("Magic", "Time: rewind here (power 1)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Rewind() => TimeMagic.Rewind(null, Verse.UI.MouseCell(), 2.9f, 1f, Find.CurrentMap);

        [LudeonTK.DebugAction("Magic", "Time: traps on this map (log)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Traps()
        {
            var c = MapComponent_TimeTraps.Of(Find.CurrentMap);
            Messages.Message(c == null || c.traps.Count == 0 ? "No traps of Time here." : string.Join("; ", c.traps.Select(t => t.caster?.LabelShort + " at " + t.at + ", " + (t.until - Find.TickManager.TicksGame).ToStringTicksToPeriod() + " left")), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
