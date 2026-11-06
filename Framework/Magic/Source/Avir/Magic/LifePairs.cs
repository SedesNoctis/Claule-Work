using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The meetings of Life as systems (the author's design, Магия-правила.md 0.297). Each has its own way, and
    /// its own answer to every rune; none of them chooses friend or foe by itself - only the circle's Barriers do.
    ///   Wild Flame - living bombs: a spark sown in a body bursts when it touches another, and leaps on.
    ///   Blood - the body is a pool, as water's pools are: its blood is drunk or given back, it is thrown up or pressed
    ///           down, paralysed; the mage of Blood mends their own blood quickest of all.
    ///   Emerald - stone remembers life: a crust grows on the living, to a statue that turns into an emerald.
    ///   Pestilence - turned round: Withering drinks the sicknesses, Restoration lays a fertile poison fog.</summary>
    public static partial class LifeMagic
    {
        private static HediffDef Seeded => H("Avir_WildSeed");
        private static HediffDef CrustDef => H("Avir_StoneCrust");
        private static HediffDef CocoonDef => H("Avir_StoneCocoon");
        private static HediffDef BloodLock => H("Avir_BloodLock");

        // ================================================================== Wild Flame: living bombs

        /// <summary>Sows a spark (or, mending, a smouldering heart) in a body. Water does not put it out.</summary>
        public static void Seed(Pawn p, Pawn owner, float power, bool heart, int jumps)
        {
            if (Seeded == null || p == null || p.Dead) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Seeded) ?? p.health.AddHediff(Seeded);
            HediffComp_WildSeed c = h.TryGetComp<HediffComp_WildSeed>();
            if (c == null) return;
            c.owner = owner; c.power = Mathf.Max(c.power, power); c.heart = heart; c.jumps = Mathf.Max(c.jumps, jumps); c.Renew();
        }

        /// <summary>A burst of living flame round a cell: petals of green-gold fire, not an explosion of fire.</summary>
        public static void Burst(IntVec3 at, Map map, Pawn owner, float power, bool heart, Pawn carrier)
        {
            if (map == null) return;
            for (int i = 0; i < 6; i++) FleckMaker.ThrowFireGlow(at.ToVector3Shifted() + new Vector3(Rand.Range(-0.8f, 0.8f), 0f, Rand.Range(-0.8f, 0.8f)), map, 0.7f);
            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 1.4f, new Color(0.85f, 0.85f, 0.3f, 0.6f));
            float got = 0f;
            foreach (Pawn q in GenRadial.RadialDistinctThingsAround(at, map, 1.5f, true).OfType<Pawn>().Where(Living).ToList())
            {
                if (q.Dead) continue;
                float amt = (q == carrier ? 0.4f : 1f) * power;
                if (heart) { HealInjuries(q, amt); AddRegen(q, 0.05f); }
                else { q.TakeDamage(new DamageInfo(DamageDefOf.Burn, amt, 1f, -1f, owner)); got += amt; }
            }
            Hediff_ArcaneCore core = owner != null ? Mages.Core(owner) : null;
            if (core != null && got > 0f) core.AddVita(got * 0.5f);
        }

        // ================================================================== Blood: the body is a pool

        /// <summary>Withering in Blood drinks part of the blood of the reached.</summary>
        public static float Suck(Pawn p, float dmg)
        {
            if (!p.RaceProps.IsFlesh) return 0f;
            Hediff bl = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss) ?? p.health.AddHediff(HediffDefOf.BloodLoss);
            float d = Mathf.Min(0.6f - bl.Severity, dmg * 0.012f);
            if (d <= 0f) return 0f;
            bl.Severity += d;
            if (p.Spawned && Rand.Chance(0.5f)) FilthMaker.TryMakeFilth(p.Position, p.Map, p.RaceProps.BloodDef ?? ThingDefOf.Filth_Blood);
            return d * 120f;
        }

        /// <summary>The mage of Blood mends their own blood quickest of all - out of the store (the author).</summary>
        public static void BloodMend(Hediff_ArcaneCore core, float hours)
        {
            Hediff bl = core.pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bl == null || core.vita < 1f) return;
            float d = Mathf.Min(bl.Severity, 0.3f * hours, core.vita / 60f);
            bl.Severity -= d; core.vita -= d * 60f;
            if (bl.Severity <= 0.005f) core.pawn.health.RemoveHediff(bl);
        }

        // ================================================================== Emerald: stone remembers life

        public static void Crust(Pawn p, float amount)
        {
            if (CrustDef == null || p == null || p.Dead || amount <= 0f) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(CrustDef) ?? p.health.AddHediff(CrustDef);
            h.Severity = Mathf.Min(1f, (h.Severity <= 0.011f ? 0f : h.Severity) + amount);
        }

        // ================================================================== Pestilence: Withering drinks sickness

        /// <summary>The mage of Pestilence does not suffer poison at all (the author): every sickness, toxin, poison and
        /// rot that would grow in them is drunk into their store instead - warded or not. The rot stink too.</summary>
        public static bool PoisonLike(Hediff h) => h != null && !(h is Hediff_Injury) && h.def.isBad && (Pestilence.Sickness(h) || h.def == HediffDefOf.ToxicBuildup
            || h.def == HediffDefOf.FoodPoisoning || h.def.defName == "Avir_FertilePoison" || h.def.defName.Contains("Rot") || h.def.defName.Contains("Toxic") || h.def.defName.Contains("Poison"));

        public static void PestImmune(Hediff_ArcaneCore core)
        {
            Pawn p = core.pawn;
            foreach (Hediff h in p.health.hediffSet.hediffs.Where(PoisonLike).ToList())
            {
                if (core.carried.Contains(h.loadID)) continue;   // a sickness carried on purpose (the druids' way) stays
                core.AddVita(h.Severity * 60f);
                p.health.RemoveHediff(h);
            }
        }

        public static float Drink(Pawn p, float power)
        {
            float got = 0f;
            foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => (Pestilence.Sickness(h) || h.def.defName == "Avir_FertilePoison" || h.def == HediffDefOf.FoodPoisoning)).ToList())
            {
                float d = Mathf.Min(h.Severity, 0.2f * power);
                h.Severity -= d; got += 60f * d;
                if (h.Severity <= 0.01f && p.health.hediffSet.hediffs.Contains(h)) p.health.RemoveHediff(h);
            }
            return got;
        }

        // ================================================================== the meetings' answers to each rune: taking

        private static float PairTakeNow(Pawn caster, Hediff_ArcaneCore core, SpellCircle g, Act act, IntVec3 at, float r, float power, Map map, ref float got)
        {
            float more = 0f;
            List<Pawn> area = Reached(caster, g, Area(at, r, map));
            switch (pair)
            {
                case RuneKind.Fire:
                    // whatever burns near feeds it
                    more += 3f * GenRadial.RadialDistinctThingsAround(at, map, r + 1f, true).Count(t => t.def == ThingDefOf.Fire || (t is Pawn q && q.IsBurning()));
                    if (act.verb == RuneKind.Purge)
                        // every spark of this mage near: bursts now
                        foreach (Pawn q in Area(at, r * 3f, map).ToList())
                        {
                            Hediff h = Seeded != null ? q.health.hediffSet.GetFirstHediffOfDef(Seeded) : null;
                            HediffComp_WildSeed c = h?.TryGetComp<HediffComp_WildSeed>();
                            if (c != null && c.owner == caster) { Burst(q.Position, map, caster, c.power, c.heart, q); q.health.RemoveHediff(h); }
                        }
                    if (act.verb == RuneKind.Growth)
                        foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map) && c.GetPlant(map) != null))
                            MapComponent_WildSeeds.Of(map)?.Sow(c, caster, 6f * power, false);
                    if (act.verb == RuneKind.Step) Seed(caster, caster, 6f * power, false, 3);
                    if (act.verb == RuneKind.Hold) foreach (Pawn q in area) MagicFx.Give(q, H("Avir_Entangled"), Mathf.RoundToInt(600f * power));
                    break;
                case RuneKind.Water:
                    // spilled blood near is drunk up
                    foreach (Thing t in GenRadial.RadialDistinctThingsAround(at, map, r + 1f, true).Where(t => t is Filth && t.def.defName.Contains("Blood")).ToList()) { more += 3f; t.Destroy(); }
                    switch (act.verb)
                    {
                        case RuneKind.Release: foreach (Pawn q in area) MagicFx.Stun(q, Mathf.RoundToInt(120f * power), caster); break;
                        case RuneKind.Beam: foreach (Pawn q in Reached(caster, g, Line(caster, at, map, out _)).Where(q => q != caster)) MagicFx.Stun(q, Mathf.RoundToInt(180f * power), caster); break;
                        case RuneKind.Hold: foreach (Pawn q in area) MagicFx.Give(q, BloodLock, Mathf.RoundToInt(360f * power)); break;
                        case RuneKind.Lift: foreach (Pawn q in area.Where(q => q != caster)) { if (act.pin) SpellHurl.Press(q, caster, power, false); else SpellHurl.Lift(q, caster, power); } break;
                    }
                    break;
                case RuneKind.Earth:
                    // half of the taking is sealed in an emerald at the spot
                    float half = got * 0.5f; got -= half;
                    if (act.verb == RuneKind.Growth) { half += got; got = 0f; }   // the green turned to stone: all of it into emeralds
                    if (half > 0.5f) Emerald.Charge(at, map, half);
                    if (act.verb == RuneKind.Hold) { Pawn one = area.OrderBy(q => q.Position.DistanceToSquared(at)).FirstOrDefault(); if (one != null) Crust(one, 0.5f * power); }
                    if (act.verb == RuneKind.Purge)
                        // the crusts shattered: shards strike round each, the life in them sealed in stone
                        foreach (Pawn q in area.ToList())
                        {
                            Hediff cr = CrustDef != null ? q.health.hediffSet.GetFirstHediffOfDef(CrustDef) : null;
                            if (cr == null) continue;
                            foreach (Pawn o in Area(q.Position, 2f, map).Where(o => o != q && Living(o)).ToList()) o.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 6f * cr.Severity * power, 0.3f, -1f, caster));
                            Emerald.Charge(q.Position, map, 20f * cr.Severity);
                            FleckMaker.ThrowDustPuffThick(q.DrawPos, map, 2f, new Color(0.3f, 0.65f, 0.45f, 0.7f));
                            q.health.RemoveHediff(cr);
                        }
                    break;
                case RuneKind.Air:
                    // growth on a sickness swells it - with Withering after, it is then drunk the fuller
                    if (act.verb == RuneKind.Growth) foreach (Pawn q in area) Swell(q, 0.15f * power);
                    break;
            }
            return more;
        }

        private static void Swell(Pawn p, float d)
        {
            foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => Pestilence.Sickness(h) || h.def.defName == "Avir_FertilePoison").ToList()) h.Severity = Mathf.Min(h.def.maxSeverity, h.Severity + d);
        }

        // ================================================================== giving

        /// <summary>Wild Flame and Pestilence give in their own way: smouldering hearts that mend whom they touch; a fertile
        /// poison fog that lies on the ground - harm given, not mending.</summary>
        private static bool GiveOwn(Pawn caster, Hediff_ArcaneCore core, SpellCircle g, Act act, IntVec3 at, float r, float power, Map map)
        {
            if (pair == RuneKind.Fire)
            {
                List<Pawn> who = act.verb == RuneKind.Beam ? Reached(caster, g, Line(caster, at, map, out _)) : Reached(caster, g, Area(at, act.verb == None ? Mathf.Max(1.5f, r - 1f) : r, map));
                if (act.verb == RuneKind.Growth)
                {
                    var cells = GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map) && c.GetPlant(map) != null).ToList();
                    float fg = Pay(core, cells.Count * 0.5f); if (fg <= 0f) return false;
                    foreach (IntVec3 c in cells) MapComponent_WildSeeds.Of(map)?.Sow(c, caster, 6f * power * fg, true);
                    return true;
                }
                if (who.Count == 0) return true;
                float f = Pay(core, who.Count * 4f);
                if (f <= 0f) return false;
                foreach (Pawn p in who) Seed(p, caster, 8f * power * f, true, act.verb == RuneKind.Release ? 2 : 3);
                if (act.verb == RuneKind.Hold) foreach (Pawn p in who) MagicFx.Give(p, H("Avir_Entangled"), Mathf.RoundToInt(600f * power));
                return true;
            }
            // Pestilence: the fog
            float rr = act.verb == RuneKind.Release ? r + 1f : act.verb == None ? Mathf.Max(1.5f, r - 1f) : r;
            List<IntVec3> fog = act.verb == RuneKind.Beam ? GenSight.BresenhamCellsBetween(caster.Position, at).Where(c => c.InBounds(map) && c != caster.Position).ToList()
                : GenRadial.RadialCellsAround(at, rr, true).Where(c => c.InBounds(map)).ToList();
            if (act.verb == RuneKind.Growth)
            {
                // growth on the sicknesses of the reached: they swell
                var who = Reached(caster, g, Area(at, r, map));
                float fs = Pay(core, who.Count * 2f); if (fs <= 0f) return false;
                foreach (Pawn p in who) Swell(p, 0.2f * power * fs);
                return true;
            }
            if (act.verb == RuneKind.Purge)
            {
                // the fogs near lifted
                MapComponent_PestFogs.Of(map)?.Lift(at, r);
                return true;
            }
            float f2 = Pay(core, fog.Count * 0.4f);
            if (f2 <= 0f) return false;
            int ticks = Mathf.RoundToInt(GenDate.TicksPerHour * (act.verb == RuneKind.Hold ? 3f : 1f) * power * f2);
            MapComponent_PestFogs.Of(map)?.Lay(fog, ticks, power * f2, caster);
            return true;
        }

        private static void PairGiveNow(Pawn caster, Act act, List<Pawn> who, IntVec3 at, float r, float pw, Map map)
        {
            var temp = MapComponent_TempTerrain.Of(map);
            switch (pair)
            {
                case RuneKind.Water:
                    // the blood given back; held - it clots, the bleeding stops
                    foreach (Pawn p in who)
                    {
                        Hediff bl = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
                        if (bl != null) { bl.Severity -= 0.15f * pw; if (bl.Severity <= 0.01f) p.health.RemoveHediff(bl); }
                        if (act.verb == RuneKind.Hold) foreach (Hediff_Injury i in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Bleeding).ToList()) i.Tended(0.6f, 1f);
                    }
                    break;
                case RuneKind.Earth:
                    if (act.verb == RuneKind.Hold) foreach (Pawn p in who) MagicFx.Give(p, CocoonDef, Mathf.RoundToInt(600f * pw));
                    else if (act.verb == RuneKind.Purge) foreach (Pawn p in who) { Hediff cr = CrustDef != null ? p.health.hediffSet.GetFirstHediffOfDef(CrustDef) : null; if (cr != null) p.health.RemoveHediff(cr); }
                    else if (act.verb == RuneKind.Release || act.verb == RuneKind.Growth || act.verb == None)
                        foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map) && map.terrainGrid.TerrainAt(c).fertility > 0f && !map.terrainGrid.TerrainAt(c).IsWater)) temp?.Set(c, ArcaneWorks.ArcaneSoilRich, GenDate.TicksPerDay * 5);
                    break;
            }
        }
    }

    // ================================================================== the spark in a body

    public class HediffCompProperties_WildSeed : HediffCompProperties { public HediffCompProperties_WildSeed() { compClass = typeof(HediffComp_WildSeed); } }

    /// <summary>A spark of Wild Flame in a body (or a smouldering heart). It bursts when its carrier touches another - the
    /// one beside, or the one it strikes - and leaps on into them, weaker each time. Water does not put it out: in water
    /// the carrier steams, and the steam scalds them all the same.</summary>
    public class HediffComp_WildSeed : HediffComp
    {
        public Pawn owner; public float power; public bool heart; public int jumps;
        private int until, check;
        public void Renew() => until = Find.TickManager.TicksGame + GenDate.TicksPerHour / 2;
        public override bool CompShouldRemove => Find.TickManager.TicksGame > until && until > 0;
        public override string CompLabelInBracketsExtra => heart ? "heart" : "spark";

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            check -= delta;
            if (check > 0) return;
            check = 30;
            Pawn p = parent.pawn;
            if (p == null || !p.Spawned || p.Dead) return;
            Map map = p.Map;
            if (Rand.Chance(0.3f)) FleckMaker.ThrowFireGlow(p.DrawPos, map, 0.4f);
            if (!heart && map.terrainGrid.TerrainAt(p.Position).IsWater)
            {
                Gas_ScaldingSteam.Release(p.Position, map, 1);
                p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 2f, 0f, -1f, owner));
                return;
            }
            Pawn touched = GenAdj.CellsAdjacent8Way(p).Where(c => c.InBounds(map)).SelectMany(c => c.GetThingList(map).OfType<Pawn>())
                .FirstOrDefault(q => q != p && !q.Dead && q.RaceProps.IsFlesh && !q.health.hediffSet.HasHediff(parent.def));
            if (touched == null) return;
            LifeMagic.Burst(touched.Position, map, owner, power, heart, p);
            if (jumps > 0 && !touched.Dead) LifeMagic.Seed(touched, owner, power * 0.7f, heart, jumps - 1);
            if (!p.Dead) p.health.RemoveHediff(parent);
        }

        public override void CompExposeData()
        {
            Scribe_References.Look(ref owner, "owner");
            Scribe_Values.Look(ref power, "power"); Scribe_Values.Look(ref heart, "heart"); Scribe_Values.Look(ref jumps, "jumps"); Scribe_Values.Look(ref until, "until");
        }
    }

    /// <summary>Sparks sown in plants: a bush bursts when someone walks into it.</summary>
    public class MapComponent_WildSeeds : MapComponent
    {
        private class S { public Pawn owner; public float power; public bool heart; public int until; }
        private readonly Dictionary<IntVec3, S> seeds = new Dictionary<IntVec3, S>();
        public MapComponent_WildSeeds(Map map) : base(map) { }
        public static MapComponent_WildSeeds Of(Map m) => m?.GetComponent<MapComponent_WildSeeds>();
        public void Sow(IntVec3 c, Pawn owner, float power, bool heart) => seeds[c] = new S { owner = owner, power = power, heart = heart, until = Find.TickManager.TicksGame + GenDate.TicksPerDay };

        public override void MapComponentTick()
        {
            if (seeds.Count == 0 || Find.TickManager.TicksGame % 30 != 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (var kv in seeds.ToList())
            {
                IntVec3 c = kv.Key; S s = kv.Value;
                if (now > s.until || c.GetPlant(map) == null) { seeds.Remove(c); continue; }
                if (Rand.Chance(0.05f)) FleckMaker.ThrowFireGlow(c.ToVector3Shifted(), map, 0.3f);
                if (!GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)).Concat(new[] { c }).Where(x => x.InBounds(map)).Any(x => x.GetFirstPawn(map) is Pawn q && q.RaceProps.IsFlesh)) continue;
                LifeMagic.Burst(c, map, s.owner, s.power, s.heart, null);
                if (!s.heart) c.GetPlant(map)?.Destroy();
                seeds.Remove(c);
            }
        }
    }

    // ================================================================== Emerald: the crust and the statue

    public class HediffCompProperties_StoneCrust : HediffCompProperties { public HediffCompProperties_StoneCrust() { compClass = typeof(HediffComp_StoneCrust); } }

    /// <summary>Stone growing on the living (the author, Fix132): the body slows, hardens and grows quiet as it thickens -
    /// metabolism, speech, hands and legs all fall away - and the whole body takes the colour of the stone. Full, it is a
    /// living statue: it stands (or lies, if it was caught lying) for days without falling, hunger at a tenth; after three
    /// days the stone begins to let go. Short of full it falls away slowly.</summary>
    public class HediffComp_StoneCrust : HediffComp
    {
        private int statueTicks, check;
        public const int StatueDays = 3;
        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            check -= delta;
            if (check > 0) return;
            int dt = 250 - check; check = 250;
            Pawn p = parent.pawn;
            if (p == null || p.Dead) return;
            if (parent.Severity < 0.999f) { statueTicks = 0; severityAdjustment -= 0.3f * dt / GenDate.TicksPerDay; return; }
            statueTicks += dt;
            // a statue does not act and does not fall: held still where it is
            if (p.Spawned) { p.stances?.stunner?.StunFor(320, null, false, false); p.pather?.StopDead(); }
            if (p.Spawned && Rand.Chance(0.1f)) FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 0.6f, new Color(0.3f, 0.65f, 0.45f, 0.5f));
            if (statueTicks >= GenDate.TicksPerDay * StatueDays) severityAdjustment -= 0.05f;
        }
        public override void CompExposeData() { Scribe_Values.Look(ref statueTicks, "statueTicks"); }
    }

    /// <summary>The stone's colour over the whole body, deeper as the crust thickens.</summary>
    [HarmonyLib.HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    public static class Patch_StoneCrustTint
    {
        private static HediffDef crust;
        private static readonly Color Stone = new Color(0.5f, 0.72f, 0.58f);
        public static void Postfix(Pawn ___pawn, ref PawnDrawParms __result)
        {
            if (___pawn?.health?.hediffSet == null) return;
            crust = crust ?? DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StoneCrust");
            Hediff h = crust != null ? ___pawn.health.hediffSet.GetFirstHediffOfDef(crust) : null;
            if (h == null) return;
            float s = Mathf.Clamp01(h.Severity);
            Color t = __result.tint;
            Color stone = new Color(t.r * Stone.r, t.g * Stone.g, t.b * Stone.b, t.a);
            __result.tint = Color.Lerp(t, stone, 0.35f + 0.65f * s);
        }
    }

    // ================================================================== Pestilence: fertile poison fogs

    /// <summary>Fogs of fertile poison laid by Pestilence's Restoration: the ground under them is fertile for three days;
    /// the poison is the place's own - whoever is in it takes it in fast, and it keeps growing, slower, after they leave;
    /// it ends in a paralytic abasia for days. The fog does not know friend or foe - only its own mage is untouched by it.</summary>
    public class MapComponent_PestFogs : MapComponent
    {
        private class F { public HashSet<IntVec3> cells; public int until; public float power; public Pawn owner; }
        private readonly List<F> fogs = new List<F>();
        public MapComponent_PestFogs(Map map) : base(map) { }
        public static MapComponent_PestFogs Of(Map m) => m?.GetComponent<MapComponent_PestFogs>();
        private static HediffDef Poison => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_FertilePoison");

        public void Lay(List<IntVec3> cells, int ticks, float power, Pawn owner = null)
        {
            fogs.Add(new F { cells = new HashSet<IntVec3>(cells), until = Find.TickManager.TicksGame + ticks, power = power, owner = owner });
            var temp = MapComponent_TempTerrain.Of(map);
            foreach (IntVec3 c in cells)
            {
                TerrainDef t = map.terrainGrid.TerrainAt(c);
                if (t.fertility > 0f && t.fertility < 1.2f && !t.IsWater) temp?.Set(c, ArcaneWorks.ArcaneSoil, GenDate.TicksPerDay * 3);
            }
        }

        public void Lift(IntVec3 at, float r) => fogs.RemoveAll(f => f.cells.Any(c => c.DistanceTo(at) <= r));

        public override void MapComponentTick()
        {
            if (fogs.Count == 0 || Find.TickManager.TicksGame % 60 != 0) return;
            int now = Find.TickManager.TicksGame;
            fogs.RemoveAll(f => f.until <= now);
            foreach (F f in fogs)
            {
                foreach (IntVec3 c in f.cells.InRandomOrder().Take(Mathf.Max(2, f.cells.Count / 6)))
                    if (c.InBounds(map)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.6f, new Color(0.72f, 0.78f, 0.32f, 0.35f));
                if (Poison == null) continue;
                foreach (IntVec3 c in f.cells)
                {
                    if (!c.InBounds(map)) continue;
                    // the mage of Pestilence does not suffer from their own poison, warded or not (the author)
                    foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().Where(p => !p.Dead && p.RaceProps.IsFlesh && p != f.owner).ToList())
                    {
                        Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Poison) ?? p.health.AddHediff(Poison);
                        h.Severity = Mathf.Min(1f, (h.Severity <= 0.011f ? 0f : h.Severity) + 0.03f * f.power);
                    }
                }
            }
        }
    }

    public class HediffCompProperties_FertilePoison : HediffCompProperties { public HediffCompProperties_FertilePoison() { compClass = typeof(HediffComp_FertilePoison); } }

    /// <summary>The poison keeps growing on its own, slower, out of the fog; at full it becomes a paralytic abasia.</summary>
    public class HediffComp_FertilePoison : HediffComp
    {
        private int check;
        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            check -= delta;
            if (check > 0) return;
            int dt = 250 - check; check = 250;
            Pawn p = parent.pawn;
            if (p == null || p.Dead) return;
            severityAdjustment += 0.25f * dt / GenDate.TicksPerDay;
            if (parent.Severity + severityAdjustment < 1f) return;
            HediffDef ab = DefDatabase<HediffDef>.GetNamedSilentFail("ParalyticAbasia") ?? DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Abasia");
            if (ab != null && !p.health.hediffSet.HasHediff(ab)) p.health.AddHediff(ab);
            p.health.RemoveHediff(parent);
        }
    }
}
