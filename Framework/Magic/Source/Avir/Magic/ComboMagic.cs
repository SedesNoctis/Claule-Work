using System.Collections.Generic;
using System.Linq;
using Avir.Linarian;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    // =====================================================================================
    // THE MEETING OF ELEMENTS. Two circles of different small elements that stand side by side in the chain (one right
    // after the other, round the heart or inside the same great circle) are no longer two: both take the colour of what
    // their meeting makes, and both work as that new element, each with its own verbs and forms. A circle between them
    // keeps them apart. Each circle meets only once: the chain pairs them from its start.
    //   Fire + Water  = Steam  - scalds; its cloud hides everything in it from sight.
    //   Water + Air   = Storm  - lightning that leaps from foe to foe; the soaked take more.
    //   Fire + Earth  = Magma  - molten ground that burns whoever stands on it, and cools into stone.
    //   Fire + Air    = Haze   - the air shimmers: foes see wrong, miss, and shoot at false shapes of the mage.
    //   Water + Earth = Bog    - a mire: foes sink in it, slower and slower, until it closes over them.
    //   Earth + Air   = Dust   - a cloud that blinds, grinds, and shows the unseen.
    // A new element opens the first time it is made.
    // =====================================================================================
    public enum ComboKind { None, Steam, Storm, Magma, Haze, Bog, Dust }

    public static class Combos
    {
        public static ComboKind Of(RuneKind a, RuneKind b)
        {
            bool Has(RuneKind x, RuneKind y) => (a == x && b == y) || (a == y && b == x);
            if (Has(RuneKind.Fire, RuneKind.Water)) return ComboKind.Steam;
            if (Has(RuneKind.Water, RuneKind.Air)) return ComboKind.Storm;
            if (Has(RuneKind.Fire, RuneKind.Earth)) return ComboKind.Magma;
            if (Has(RuneKind.Fire, RuneKind.Air)) return ComboKind.Haze;
            if (Has(RuneKind.Water, RuneKind.Earth)) return ComboKind.Bog;
            if (Has(RuneKind.Earth, RuneKind.Air)) return ComboKind.Dust;
            return ComboKind.None;
        }

        public static bool WithAir(ComboKind k) => k == ComboKind.Storm || k == ComboKind.Haze || k == ComboKind.Dust;

        public static string Label(ComboKind k)
        {
            switch (k)
            {
                case ComboKind.Steam: return "steam";
                case ComboKind.Storm: return "storm";
                case ComboKind.Magma: return "magma";
                case ComboKind.Haze: return "haze";
                case ComboKind.Bog: return "bog";
                case ComboKind.Dust: return "dust";
                default: return "";
            }
        }

        public static Color ColorOf(ComboKind k)
        {
            switch (k)
            {
                case ComboKind.Steam: return new Color(0.86f, 0.89f, 0.93f);
                case ComboKind.Storm: return new Color(0.42f, 0.55f, 0.95f);
                case ComboKind.Magma: return new Color(1f, 0.36f, 0.08f);
                case ComboKind.Haze: return new Color(1f, 0.78f, 0.45f);
                case ComboKind.Bog: return new Color(0.38f, 0.47f, 0.24f);
                case ComboKind.Dust: return new Color(0.80f, 0.69f, 0.48f);
                default: return Color.white;
            }
        }

        private static bool Small(RuneDef e) => e != null && (e.kind == RuneKind.Fire || e.kind == RuneKind.Water || e.kind == RuneKind.Earth || e.kind == RuneKind.Air);

        /// <summary>Which circles of a spell have met, and what they made. Side by side in the chain and in the same
        /// ring (round the heart, or inside one great circle); paired from the start of the chain, each only once.</summary>
        public static Dictionary<SpellCircle, ComboKind> Pairs(SpellCircle root)
        {
            var map = new Dictionary<SpellCircle, ComboKind>();
            if (root == null) return map;
            List<SpellCircle> order = MapComponent_SpellChain.Order(root);
            for (int i = 0; i + 1 < order.Count; i++)
            {
                SpellCircle a = order[i], b = order[i + 1];
                if (map.ContainsKey(a) || map.ContainsKey(b) || !Small(a.element) || !Small(b.element)) continue;
                if (root.ParentOf(a) != root.ParentOf(b)) continue;
                ComboKind k = Of(a.element.kind, b.element.kind);
                if (k == ComboKind.None) continue;
                map[a] = k; map[b] = k;
            }
            return map;
        }

        public static ComboKind Of(SpellCircle root, SpellCircle c) => root != null && c != null && Pairs(root).TryGetValue(c, out ComboKind k) ? k : ComboKind.None;
    }

    /// <summary>The elements the colony has already made by meeting two others.</summary>
    public class GameComponent_Combos : GameComponent
    {
        public HashSet<ComboKind> known = new HashSet<ComboKind>();
        public GameComponent_Combos(Game g) { }

        public static void Made(ComboKind k, Pawn by)
        {
            var c = Current.Game?.GetComponent<GameComponent_Combos>();
            if (c == null || k == ComboKind.None || c.known.Contains(k)) return;
            c.known.Add(k);
            if (by != null && by.Faction == Faction.OfPlayer)
                Find.LetterStack.ReceiveLetter("A new element: " + Combos.Label(k).CapitalizeFirst(),
                    by.LabelShortCap + " let two elements meet in one spell, and they made something new: " + Combos.Label(k) + ".\n\n" + ComboText.Short(k),
                    LetterDefOf.PositiveEvent, by);
        }

        public override void ExposeData()
        {
            var list = known.ToList();
            Scribe_Collections.Look(ref list, "known", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) known = new HashSet<ComboKind>(list ?? new List<ComboKind>());
        }
    }

    public static class ComboMagic
    {
        public static void Cast(ComboKind k, Pawn caster, RitualSchema s, RitualReading r, LocalTargetInfo target, float power)
        {
            Map map = caster.Map;
            if (map == null) return;
            GameComponent_Combos.Made(k, caster);
            r = SpellMods.Begin(s, r);
            IntVec3 at = target.Cell;
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            float radius = FireMagic.PointRadius(r);
            HediffDef ward = ComboDefOf.Ward(k);
            if (r.barrier > 0 || SpellMods.WardOnMage)
            {
                IntVec3 wat = SpellMods.WardOnMage || forms.Any(f => f.kind == RuneKind.Aura) ? caster.Position : at;
                foreach (Pawn p in MagicFx.PawnsIn(FireMagic.Point(wat, map, Mathf.Max(0.9f, radius - 1f)), map))
                    if (!p.HostileTo(caster)) MagicFx.Give(p, ward, Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power));
                MapComponent_WardsDrawn.Raise(map, wat, Mathf.Max(0.9f, radius - 1f), null, caster, Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power), Combos.ColorOf(k));
            }
            if (forms.Count == 0) { Apply(k, caster, map, verbs, FireMagic.Point(at, map, radius), at, power, r); return; }
            foreach (RuneDef f in forms)
            {
                RitualReading rf = SpellMods.Verb(r, f);
                float fr = FireMagic.PointRadius(rf);
                switch (f.kind)
                {
                    case RuneKind.Aura:
                        Apply(k, caster, map, verbs, FireMagic.Point(caster.Position, map, fr + 1.5f), caster.Position, power, rf);
                        MapComponent_ComboFields.Raise(map, k, caster, caster.Position, fr + 1.5f, power, Mathf.RoundToInt(2500f * power), true);
                        break;
                    case RuneKind.Beam:
                        var line = FireMagic.Beam(caster.Position, at, map, rf.breadth);
                        Beam(k, caster, map, line, power, rf);
                        Apply(k, caster, map, verbs, line, caster.Position, power, rf);
                        break;
                    case RuneKind.Shape:
                        var wall = FireMagic.WallTo.IsValid ? FireMagic.WallLine(at, FireMagic.WallTo, map, FireMagic.WallMax(rf)) : FireMagic.Wall(caster.Position, at, map, 5 + 2 * rf.breadth);
                        Wall(k, caster, map, wall, power, rf, verbs.Any(v => v.kind == RuneKind.Hold));
                        Apply(k, caster, map, verbs.Where(v => v.kind != RuneKind.Hold).ToList(), wall, at, power, rf);
                        break;
                    case RuneKind.Step:
                        RuneKind trail = k == ComboKind.Magma || k == ComboKind.Haze ? RuneKind.Fire : k == ComboKind.Dust || k == ComboKind.Storm ? RuneKind.Air : k == ComboKind.Bog ? RuneKind.Earth : RuneKind.Water;
                        if (rf.thread > 0) { SpellDash.Pull(caster, target, trail, power, fr); break; }
                        if (k == ComboKind.Haze) Decoys(caster, map, new List<IntVec3> { caster.Position }, Mathf.RoundToInt(600f * power));   // a shape left where they stood
                        SpellDash.Travel(caster, at, k, power);
                        break;
                    case RuneKind.Restore: Restore(k, caster, map, FireMagic.Point(at, map, Mathf.Max(0.9f, fr - 1f)), power); break;
                    case RuneKind.Enchant: MagicFx.Enchant(caster, at, map, power, ComboDefOf.Enchant(k)); break;
                    case RuneKind.Summon: Messages.Message("Spirits are not made yet.", MessageTypeDefOf.RejectInput, false); break;
                    case RuneKind.Sense:
                        FireMagic.Sense(caster, map, 35f + 10f * rf.reach, power);
                        var round = FireMagic.Point(caster.Position, map, Mathf.Min(GenRadial.MaxRadialPatternRadius - 1f, 35f + 10f * rf.reach));
                        if (k == ComboKind.Dust) Reveal(round, map);
                        if (k == ComboKind.Magma) Veins(round, map);
                        break;
                    default: if (verbs.Count > 0) Apply(k, caster, map, verbs, FireMagic.Point(at, map, fr), at, power, rf); break;
                }
            }
        }

        // ------------------------------------------------------------------------------------- the verbs

        internal static void Apply(ComboKind k, Pawn caster, Map map, List<RuneDef> verbs, List<IntVec3> cells, IntVec3 from, float power, RitualReading r)
        {
            if (cells.Count == 0) return;
            if (verbs.Count > 0) Reactions.Touch(Reactions.Of(k), cells, map, caster, power);
            foreach (RuneDef v in verbs)
            {
                RitualReading rv = SpellMods.Verb(r, v);
                float amp = 1f + 0.3f * rv.amplify;
                switch (v.kind)
                {
                    case RuneKind.Release: Release(k, caster, map, cells, from, power * amp, rv); break;
                    case RuneKind.Hold:
                        MapComponent_ComboFields.Raise(map, k, caster, Center(cells), Spread(cells), power, Mathf.RoundToInt(1500f * power * (1 + rv.anchor)), false);
                        if (k == ComboKind.Magma) foreach (IntVec3 c in cells) if (Rand.Chance(0.6f)) MagmaPool.Pour(c, map, caster, power, Mathf.RoundToInt(GenDate.TicksPerHour * power * (1 + rv.anchor)), false);
                        if (k == ComboKind.Bog) foreach (IntVec3 c in cells) MirePatch.Spread(c, map, caster, power, Mathf.RoundToInt(GenDate.TicksPerHour * 2f * power * (1 + rv.anchor)));
                        break;
                    case RuneKind.Drain: power = Drain(k, caster, map, cells, verbs, v, power); break;
                    case RuneKind.Lift:
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            if (k == ComboKind.Steam || k == ComboKind.Magma) p.TakeDamage(new DamageInfo(k == ComboKind.Magma ? DamageDefOf.Flame : DamageDefOf.Burn, 5f * power * amp, 0f, -1f, caster));
                            if (k == ComboKind.Storm) WaterMagic.Soak(p, power);
                            if (k == ComboKind.Bog) WaterMagic.Soak(p, power);
                            if (k == ComboKind.Dust) Grind(p, caster, power * amp);
                            if (!p.Dead) MagicFx.Lift(p, caster, power * (k == ComboKind.Storm || k == ComboKind.Dust ? 1.2f : 0.9f) * amp);
                        }
                        Puffs(k, map, cells, 0.5f);
                        break;
                    case RuneKind.Growth: Growth(k, caster, map, cells, power, rv); break;
                    case RuneKind.Purge: Purge(k, caster, map, cells, power); break;
                }
            }
        }

        private static IntVec3 Center(List<IntVec3> cells) => cells.Count == 0 ? IntVec3.Invalid : new IntVec3(Mathf.RoundToInt((float)cells.Average(c => c.x)), 0, Mathf.RoundToInt((float)cells.Average(c => c.z)));
        private static float Spread(List<IntVec3> cells) { IntVec3 c = Center(cells); return Mathf.Max(1.5f, cells.Max(x => x.DistanceTo(c)) + 0.5f); }

        private static void Release(ComboKind k, Pawn caster, Map map, List<IntVec3> cells, IntVec3 from, float power, RitualReading r)
        {
            var foes = MagicFx.PawnsIn(cells, map).Where(p => MagicFx.Foe(p, caster)).ToList();
            switch (k)
            {
                case ComboKind.Steam:
                    // a scalding burst; the cloud stays and hides whoever is in it
                    foreach (Pawn p in foes) p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 8f * power, 0f, -1f, caster));
                    Smoke(map, cells, 1f, true);
                    foreach (IntVec3 c in cells) FireMagic.Extinguish(c, map);
                    break;
                case ComboKind.Storm:
                    // a bolt falls on the point, and leaps on
                    Pawn first = foes.OrderBy(p => p.Position.DistanceToSquared(Center(cells))).FirstOrDefault();
                    if (first != null) AirMagic.Chain(caster, new List<IntVec3> { first.Position }, map, 1 + r.breadth, power, true);
                    foreach (Pawn p in foes) WaterMagic.Soak(p, power);
                    FleckMaker.Static(Center(cells).ToVector3Shifted(), map, FleckDefOf.ExplosionFlash, 4f);
                    MapComponent_SpellSprites.Put(map, "Storm/Bolt_Fork", Center(cells).ToVector3Shifted(), 3.5f, 22);
                    break;
                case ComboKind.Magma:
                    // the ground bursts open: a blow of molten stone, and pools of it left burning
                    foreach (Pawn p in foes)
                    {
                        p.TakeDamage(new DamageInfo(DamageDefOf.Flame, 7f * power, 0f, -1f, caster));
                        if (!p.Dead) p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5f * power, 0f, -1f, caster));
                    }
                    MapComponent_SpellSprites.Put(map, "Magma/Fissure", Center(cells).ToVector3Shifted(), Spread(cells) * 2f, 240, -1f, 0f, 4f);
                    foreach (IntVec3 c in cells) if (Rand.Chance(0.35f)) MagmaPool.Pour(c, map, caster, power, Mathf.RoundToInt(900f * power), false);
                    break;
                case ComboKind.Haze:
                    // a flash of heat: foes dazzled for a while, a little burnt
                    foreach (Pawn p in foes) { p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 3f * power, 0f, -1f, caster)); MagicFx.Give(p, ComboDefOf.Avir_Dazzled, Mathf.RoundToInt(900f * power)); }
                    MapComponent_SpellSprites.Put(map, "Haze/Shimmer", Center(cells).ToVector3Shifted(), Spread(cells) * 2.2f, 90, -1f, 1.5f);
                    FleckMaker.Static(Center(cells).ToVector3Shifted(), map, FleckDefOf.ExplosionFlash, 3f);
                    break;
                case ComboKind.Bog:
                    // a burst of mud: foes knocked down in it, soaked, and the ground left a mire
                    MapComponent_SpellSprites.Put(map, "Bog/Splash", Center(cells).ToVector3Shifted(), Spread(cells) * 1.8f, 70);
                    foreach (Pawn p in foes) MapComponent_SpellSprites.Put(map, "Bog/Splash", p.DrawPos, 1.4f, 50);
                    foreach (IntVec3 c in cells) if (Rand.Chance(0.5f)) MirePatch.Spread(c, map, caster, power, Mathf.RoundToInt(GenDate.TicksPerHour * power));
                    foreach (Pawn p in foes) { WaterMagic.Soak(p, power); MagicFx.Give(p, ComboDefOf.Avir_Mired, Mathf.RoundToInt(600f * power)); MagicFx.Fell(p, Center(cells), 1, map, caster, power, Mathf.RoundToInt(90f * power)); }
                    break;
                case ComboKind.Dust:
                    // a blast of sand: it grinds every bare part, and the dust hangs
                    foreach (Pawn p in foes) { Grind(p, caster, power); MagicFx.Give(p, ComboDefOf.Avir_Dusted, Mathf.RoundToInt(600f * power)); }
                    Reveal(cells, map);
                    MapComponent_SpellSprites.Put(map, "Dust/Cloud", Center(cells).ToVector3Shifted(), Spread(cells) * 2.2f, 150, -1f, 0.3f);
                    break;
            }
            Puffs(k, map, cells, 0.6f);
        }

        private static float Drain(ComboKind k, Pawn caster, Map map, List<IntVec3> cells, List<RuneDef> verbs, RuneDef v, float power)
        {
            float took = 0f;
            foreach (IntVec3 c in cells)
            {
                if (k == ComboKind.Magma) foreach (MagmaPool m in c.GetThingList(map).OfType<MagmaPool>().ToList()) { m.Harden(true); took += 0.2f; }
                if (k == ComboKind.Steam || k == ComboKind.Haze) took += 0.1f * FireMagic.Extinguish(c, map);
            }
            foreach (Pawn p in MagicFx.PawnsIn(cells, map))
            {
                if (!MagicFx.Foe(p, caster)) continue;
                switch (k)
                {
                    case ComboKind.Storm:
                        // the charge drawn out of every machine there
                        if (p.RaceProps.IsMechanoid) { p.TakeDamage(new DamageInfo(DamageDefOf.EMP, 12f * power, 0f, -1f, caster)); took += 0.3f; }
                        break;
                    case ComboKind.Bog:
                    case ComboKind.Dust:
                        // the mire and the dust drink: the body is left parched
                        if (!p.RaceProps.IsFlesh) break;
                        Hediff h = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Parched) ?? p.health.AddHediff(WaterDefOf.Avir_Parched);
                        h.Severity = Mathf.Min(1f, h.Severity + 0.3f * power);
                        took += 0.25f;
                        break;
                    default:
                        p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 3f * power, 0f, -1f, caster));
                        took += 0.15f;
                        break;
                }
            }
            HediffDef keep = k == ComboKind.Storm || k == ComboKind.Dust || k == ComboKind.Haze ? EarthAirDefOf.Avir_StoredAir : k == ComboKind.Magma ? EarthAirDefOf.Avir_StoredStone : WaterDefOf.Avir_StoredWater;
            return MagicFx.Drained(caster, verbs, v, keep, took, power);
        }

        private static void Growth(ComboKind k, Pawn caster, Map map, List<IntVec3> cells, float power, RitualReading r)
        {
            var near = cells.SelectMany(c => GenRadial.RadialCellsAround(c, 2f, true)).Where(c => c.InBounds(map)).Distinct().ToList();
            switch (k)
            {
                case ComboKind.Steam:
                case ComboKind.Storm:
                case ComboKind.Bog:
                    // warm wet air, rain, the wet black earth of a bog: everything green grows
                    foreach (IntVec3 c in near)
                    {
                        Plant plant = c.GetPlant(map);
                        if (plant != null && plant.LifeStage == PlantLifeStage.Growing) plant.Growth = Mathf.Min(1f, plant.Growth + (k == ComboKind.Bog ? 0.2f : 0.12f) * power);
                        if (k == ComboKind.Bog && Rand.Chance(0.3f)) MirePatch.Spread(c, map, caster, power, Mathf.RoundToInt(GenDate.TicksPerHour * power));
                    }
                    if (k == ComboKind.Steam) GenTemperature.PushHeat(Center(cells), map, 60f * power);
                    break;
                case ComboKind.Magma:
                    // the molten ground creeps out
                    foreach (IntVec3 c in near) if (Rand.Chance(0.25f)) MagmaPool.Pour(c, map, caster, power, Mathf.RoundToInt(900f * power), false);
                    break;
                case ComboKind.Haze:
                case ComboKind.Dust:
                    MapComponent_ComboFields.Raise(map, k, caster, Center(cells), Spread(cells) + 2.5f, power, Mathf.RoundToInt(1200f * power), false);
                    break;
            }
            Puffs(k, map, near, 0.15f);
        }

        private static void Purge(ComboKind k, Pawn caster, Map map, List<IntVec3> cells, float power)
        {
            foreach (IntVec3 c in cells)
            {
                if (k != ComboKind.Magma) FireMagic.Extinguish(c, map);
                foreach (Filth f in c.GetThingList(map).OfType<Filth>().ToList()) f.Destroy();
            }
            foreach (Pawn p in MagicFx.PawnsIn(cells, map))
            {
                if (p.HostileTo(caster)) continue;
                foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => x.def == HediffDefOf.ToxicBuildup || x.def == HediffDefOf.FoodPoisoning || (k == ComboKind.Steam || k == ComboKind.Magma) && x.def.makesSickThought).ToList())
                { h.Severity -= 0.2f * power; if (h.Severity <= 0f) p.health.RemoveHediff(h); }
                if (k == ComboKind.Haze || k == ComboKind.Dust) { Hediff wet = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Soaked); if (wet != null) p.health.RemoveHediff(wet); }
            }
            Puffs(k, map, cells, 0.3f);
        }

        // ------------------------------------------------------------------------------------- the forms

        private static void Beam(ComboKind k, Pawn caster, Map map, List<IntVec3> line, float power, RitualReading r)
        {
            Reactions.Touch(Reactions.Of(k), line, map, caster, power);
            var foes = MagicFx.PawnsIn(line, map).Where(p => MagicFx.Foe(p, caster)).ToList();
            switch (k)
            {
                // lightning that leaps from foe to foe
                case ComboKind.Storm: AirMagic.Chain(caster, line, map, 1 + r.breadth + r.amplify, power, true); break;
                // a jet of steam: scalds, and leaves a curtain along its way that nothing is seen through
                case ComboKind.Steam: foreach (Pawn p in foes) p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 6f * power, 0f, -1f, caster)); Smoke(map, line, 1f, true); break;
                // a stream of lava: it burns all on it and sets into a path of rubble
                case ComboKind.Magma:
                    if (line.Count > 1) { Vector3 a0 = line[0].ToVector3Shifted(), a1 = line[line.Count - 1].ToVector3Shifted(); Vector3 d = a1 - a0; MapComponent_SpellSprites.Put(map, "Magma/Fissure", (a0 + a1) / 2f, d.MagnitudeHorizontal() + 1f, 240, d.AngleFlat() - 90f, 0f, 4f); }
                    foreach (IntVec3 c in line) MagmaPool.Pour(c, map, caster, power, Mathf.RoundToInt(600f * power), false);
                    break;
                // a ray of heat: all on it dazzled, the first burnt through
                case ComboKind.Haze:
                    foreach (Pawn p in foes) MagicFx.Give(p, ComboDefOf.Avir_Dazzled, Mathf.RoundToInt(600f * power));
                    Pawn first = foes.OrderBy(p => p.Position.DistanceToSquared(caster.Position)).FirstOrDefault();
                    first?.TakeDamage(new DamageInfo(DamageDefOf.Burn, 10f * power, 0.3f, -1f, caster));
                    break;
                // a jet of mud: knocked down, soaked, stuck
                case ComboKind.Bog: foreach (Pawn p in foes) { MapComponent_SpellSprites.Put(map, "Bog/Splash", p.DrawPos, 1.4f, 50); WaterMagic.Soak(p, power); MagicFx.Give(p, ComboDefOf.Avir_Mired, Mathf.RoundToInt(600f * power)); MagicFx.Fell(p, caster.Position, 1, map, caster, power, 90); } break;
                // a blast of sand along the line
                case ComboKind.Dust: foreach (Pawn p in foes) { Grind(p, caster, power); MagicFx.Give(p, ComboDefOf.Avir_Dusted, Mathf.RoundToInt(600f * power)); } break;
            }
            Puffs(k, map, line, 0.7f);
        }

        private static void Wall(ComboKind k, Pawn caster, Map map, List<IntVec3> wall, float power, RitualReading r, bool hold)
        {
            int life = Mathf.RoundToInt(GenDate.TicksPerHour * 2f * power * (1 + r.anchor + (hold ? 1 : 0)));
            switch (k)
            {
                case ComboKind.Steam: MapComponent_ComboFields.Raise(map, k, caster, IntVec3.Invalid, 0f, power, life / 2, false, wall); break;
                case ComboKind.Storm: MapComponent_ComboFields.Raise(map, k, caster, IntVec3.Invalid, 0f, power, life / 2, false, wall); break;
                case ComboKind.Magma:
                    // a wall of molten stone: it burns, then sets hard - a wall of rock that stays for hours
                    foreach (IntVec3 c in wall) MagmaPool.Pour(c, map, caster, power, 300, true, life);
                    break;
                case ComboKind.Haze:
                    // a row of false shapes of the mage: the foes shoot at them
                    Decoys(caster, map, wall.Where((c, i) => i % 3 == 0).ToList(), life / 4);
                    break;
                case ComboKind.Bog:
                    // a moat of mire, two cells wide
                    foreach (IntVec3 c in wall.SelectMany(c => GenAdj.CellsAdjacentCardinal(c, Rot4.North, IntVec2.One).Concat(new[] { c })).Distinct()) if (Rand.Chance(0.75f)) MirePatch.Spread(c, map, caster, power, life);
                    break;
                case ComboKind.Dust: MapComponent_ComboFields.Raise(map, k, caster, IntVec3.Invalid, 0f, power, life / 2, false, wall); break;
            }
        }

        private static void Restore(ComboKind k, Pawn caster, Map map, List<IntVec3> cells, float power)
        {
            foreach (Pawn p in MagicFx.PawnsIn(cells, map))
            {
                if (p.HostileTo(caster)) continue;
                switch (k)
                {
                    case ComboKind.Steam:
                        // a steam bath: warmth back, poison sweated out, pain eased
                        foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => x.def == HediffDefOf.Hypothermia || x.def == HediffDefOf.ToxicBuildup).ToList()) { h.Severity -= 0.3f * power; if (h.Severity <= 0f) p.health.RemoveHediff(h); }
                        break;
                    case ComboKind.Storm:
                        // a jolt: the stunned and the dazed are woken
                        p.stances?.stunner?.StopStun();
                        foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => x.def == HediffDefOf.Anesthetic || x.def == EarthAirDefOf.Avir_Dizzy || x.def == ComboDefOf.Avir_Dazzled).ToList()) p.health.RemoveHediff(h);
                        break;
                    case ComboKind.Magma:
                        // hot stones laid on: wounds seared shut, the cold driven out
                        foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Bleeding).ToList()) inj.Tended(0.5f, 1f);
                        Hediff hy = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Hypothermia); if (hy != null) p.health.RemoveHediff(hy);
                        break;
                    case ComboKind.Haze:
                        // warm, dry air: the wet and the cold go
                        foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => x.def == HediffDefOf.Hypothermia || x.def == WaterDefOf.Avir_Soaked || x.def == WaterDefOf.Avir_Chilled).ToList()) p.health.RemoveHediff(h);
                        break;
                    case ComboKind.Bog:
                        // a mud poultice: burns and bruises cooled and bound
                        foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => !i.IsTended() && (i.def.defName == "Burn" || i.def.defName == "Bruise" || i.def.defName == "Crush" || i.def.defName == "Crack")).ToList()) inj.Tended(Mathf.Clamp01(0.5f + 0.15f * power), 1f);
                        break;
                    case ComboKind.Dust:
                        // dry dust packed into the wounds: bleeding stops
                        foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Bleeding).ToList()) inj.Tended(0.3f, 1f);
                        break;
                }
                Puffs(k, map, new List<IntVec3> { p.Position }, 1f);
            }
        }

        /// <summary>A step of a meeting: what happens on the way out, before the mage goes.</summary>
        public static void Depart(ComboKind k, Pawn mage, IntVec3 to, float power)
        {
            Map map = mage.Map; IntVec3 from = mage.Position;
            switch (k)
            {
                case ComboKind.Steam: Smoke(map, FireMagic.Point(from, map, 1.9f), 0.8f); break;
                case ComboKind.Storm:
                    // the bolt: every foe on the way is struck as the mage passes
                    var path = GenSight.BresenhamCellsBetween(from, to).Where(c => c.InBounds(map) && c != from).ToList();
                    foreach (Pawn p in MagicFx.PawnsIn(path, map)) if (MagicFx.Foe(p, mage)) { p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 6f * power, 0.3f, -1f, mage)); if (!p.Dead && p.RaceProps.IsMechanoid) p.TakeDamage(new DamageInfo(DamageDefOf.EMP, 8f * power, 0f, -1f, mage)); }
                    FleckMaker.ConnectingLine(from.ToVector3Shifted(), to.ToVector3Shifted(), FleckDefOf.LineEMP, map);
                    FleckMaker.Static(from.ToVector3Shifted(), map, FleckDefOf.ExplosionFlash, 3f);
                    break;
                case ComboKind.Bog:
                    foreach (IntVec3 c in GenSight.BresenhamCellsBetween(from, to)) if (c.InBounds(map) && c != from && c != to && Rand.Chance(0.7f)) MirePatch.Spread(c, map, mage, power, Mathf.RoundToInt(GenDate.TicksPerHour * power));
                    break;
                case ComboKind.Haze: FleckMaker.ThrowHeatGlow(from, map, 2f); break;
                case ComboKind.Dust: Smoke(map, new List<IntVec3> { from }, 0.6f); break;
            }
        }

        /// <summary>A step of a meeting: where the mage comes down, out of the ground or out of the air.</summary>
        public static void Land(ComboKind k, Pawn mage, IntVec3 at, float power)
        {
            Map map = mage.Map;
            if (map == null) return;
            var round = FireMagic.Point(at, map, 2.5f).Where(c => c != at).ToList();
            var foes = MagicFx.PawnsIn(round, map).Where(p => MagicFx.Foe(p, mage)).ToList();
            switch (k)
            {
                case ComboKind.Steam: foreach (Pawn p in foes) p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 6f * power, 0f, -1f, mage)); Smoke(map, round, 0.7f); break;
                case ComboKind.Storm: foreach (Pawn p in foes) MagicFx.Fell(p, at, 1, map, mage, power, Mathf.RoundToInt(90f * power)); FleckMaker.Static(at.ToVector3Shifted(), map, FleckDefOf.ExplosionFlash, 5f); MapComponent_SpellSprites.Put(map, "Storm/Strike", at.ToVector3Shifted(), 3f, 24); break;
                case ComboKind.Magma: foreach (IntVec3 c in FireMagic.Point(at, map, 2.9f).Where(c => c.DistanceTo(at) > 1.5f)) MagmaPool.Pour(c, map, mage, power, Mathf.RoundToInt(900f * power), false); break;
                case ComboKind.Haze: FleckMaker.ThrowHeatGlow(at, map, 2f); break;
                case ComboKind.Bog: MapComponent_SpellSprites.Put(map, "Bog/Splash", at.ToVector3Shifted(), 3.5f, 70); foreach (IntVec3 c in round) if (Rand.Chance(0.75f)) MirePatch.Spread(c, map, mage, power, Mathf.RoundToInt(GenDate.TicksPerHour * power)); break;
                case ComboKind.Dust: foreach (Pawn p in foes) MagicFx.Give(p, ComboDefOf.Avir_Dusted, Mathf.RoundToInt(600f * power)); MapComponent_ComboFields.Raise(map, k, mage, at, 2.5f, power, 600, false); MapComponent_SpellSprites.Put(map, "Dust/Devil", at.ToVector3Shifted(), 3.5f, 90, -1f, -6f); break;
            }
            Puffs(k, map, round, 0.5f);
        }

        /// <summary>What a meeting takes from the mage when the hold slips - on top of its two elements' own prices.</summary>
        public static void Price(ComboKind k, Pawn caster)
        {
            if (caster == null || caster.Dead) return;
            switch (k)
            {
                case ComboKind.Steam: caster.TakeDamage(new DamageInfo(DamageDefOf.Burn, 6f, 0f, -1f, caster)); if (caster.Spawned) Smoke(caster.Map, FireMagic.Point(caster.Position, caster.Map, 2f), 0.8f); break;
                case ComboKind.Storm: if (caster.Spawned) AirMagic.Chain(caster, new List<IntVec3>(), caster.Map, 0, 0f, true); caster.TakeDamage(new DamageInfo(DamageDefOf.Burn, 7f, 0f, -1f, caster)); caster.stances?.stunner?.StunFor(150, caster); if (caster.Spawned) FleckMaker.ThrowLightningGlow(caster.DrawPos, caster.Map, 2f); break;
                case ComboKind.Magma: if (caster.Spawned) MagmaPool.Pour(caster.Position, caster.Map, null, 1f, 300, false); break;
                case ComboKind.Haze: MagicFx.Give(caster, ComboDefOf.Avir_Dazzled, 2500); break;
                case ComboKind.Bog: MagicFx.Give(caster, EarthAirDefOf.Avir_Rooted, 600); WaterMagic.Soak(caster, 1f); MagicFx.Give(caster, ComboDefOf.Avir_Sinking, 2500); caster.health.hediffSet.GetFirstHediffOfDef(ComboDefOf.Avir_Sinking).Severity = 0.25f; break;
                case ComboKind.Dust: MagicFx.Give(caster, EarthAirDefOf.Avir_Breathless, 2500); MagicFx.Give(caster, ComboDefOf.Avir_Dusted, 1200); break;
            }
            Messages.Message(caster.LabelShortCap + " lost hold of the " + Combos.Label(k) + ".", caster, MessageTypeDefOf.NegativeEvent, false);
        }

        // ------------------------------------------------------------------------------------- shared

        /// <summary>Sand ground into every bare part: a few small cuts all over.</summary>
        internal static void Grind(Pawn p, Pawn by, float power)
        {
            for (int i = 0; i < 3 && !p.Dead; i++) p.TakeDamage(new DamageInfo(DamageDefOf.Scratch, 2f * power, 0f, -1f, by));
        }

        /// <summary>Heat felt through the rock: the veins of ore within it are shown, even inside mountains.</summary>
        internal static void Veins(List<IntVec3> cells, Map map)
        {
            int n = 0;
            foreach (IntVec3 c in cells)
            {
                Building b = c.GetEdifice(map);
                if (b == null || b.def.building == null || !b.def.building.isResourceRock) continue;
                map.fogGrid.Unfog(c);
                FleckMaker.ThrowFireGlow(c.ToVector3Shifted(), map, 0.6f);
                n++;
            }
            // and deeper still: what a deep drill would find
            int deep = 0;
            foreach (IntVec3 c in cells)
            {
                if (map.deepResourceGrid.ThingDefAt(c) == null || map.deepResourceGrid.CountAt(c) <= 0) continue;
                deep++;
                if (deep % 3 == 0) FleckMaker.ThrowHeatGlow(c, map, 1.2f);
            }
            if (n > 0 || deep > 0) Messages.Message("The heat in the rock shows " + n + " cells of ore" + (deep > 0 ? ", and " + deep + " cells with ore deep below" : "") + ".", new TargetInfo(cells[0], map), MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>The unseen made seen: dust settles on them.</summary>
        internal static void Reveal(List<IntVec3> cells, Map map)
        {
            foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                foreach (HediffComp_Invisibility inv in p.health.hediffSet.hediffs.OfType<HediffWithComps>().SelectMany(h => h.comps.OfType<HediffComp_Invisibility>()).ToList())
                    inv.BecomeVisible();
        }

        internal static void Smoke(Map map, List<IntVec3> cells, float share, bool scalding = false)
        {
            foreach (IntVec3 c in cells)
            {
                if (!c.InBounds(map) || !Rand.Chance(share)) continue;
                GasUtility.AddGas(c, map, GasType.BlindSmoke, 1.2f);
                if (scalding) Gas_ScaldingSteam.Release(c, map, 1);
            }
        }

        /// <summary>False shapes of the mage: the foes about are drawn to shoot at them (the Linarians' mirages, borrowed).</summary>
        internal static void Decoys(Pawn caster, Map map, List<IntVec3> at, int ticks)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ShadowDecoy");
            if (def == null) return;
            var foes = map.mapPawns.AllPawnsSpawned.Where(q => MagicFx.Foe(q, caster) && q.Position.InHorDistOf(caster.Position, 30f)).ToList();
            foreach (IntVec3 c in at)
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map) != null) continue;
                var d = (Building_ShadowDecoy)ThingMaker.MakeThing(def);
                d.her = caster;
                d.lure = true;
                d.sprite = "Things/Mote/Avir/Magic/Haze/Mirage";
                d.until = Find.TickManager.TicksGame + ticks;
                foreach (Pawn q in foes) d.fooled.Add(q.thingIDNumber);
                GenSpawn.Spawn(d, c, map, Rot4.South);
                if (caster.Faction != null) d.SetFaction(caster.Faction);
                FleckMaker.ThrowHeatGlow(c, map, 1.5f);
            }
        }

        internal static void Puffs(ComboKind k, Map map, List<IntVec3> cells, float share)
        {
            Color col = Combos.ColorOf(k); col.a = 0.55f;
            foreach (IntVec3 c in cells)
            {
                if (!c.InBounds(map) || !Rand.Chance(share)) continue;
                Vector3 v = c.ToVector3Shifted();
                switch (k)
                {
                    case ComboKind.Magma: FleckMaker.ThrowFireGlow(v, map, 1f); FleckMaker.ThrowMicroSparks(v, map); break;
                    case ComboKind.Storm: if (Rand.Chance(0.3f)) FleckMaker.ThrowLightningGlow(v, map, 0.8f); FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1.2f); break;
                    case ComboKind.Haze: FleckMaker.ThrowHeatGlow(c, map, 1.2f); break;
                    default: FleckMaker.ThrowDustPuffThick(v, map, 1.6f, col); break;
                }
            }
        }
    }

    /// <summary>The lasting work of the meetings: clouds of steam and dust, storms that keep striking, a shimmer of
    /// haze - over a place for a while, round the mage (an aura), or along a wall.</summary>
    public class MapComponent_ComboFields : MapComponent
    {
        public class Field : IExposable
        {
            public ComboKind kind; public Pawn caster; public IntVec3 at; public float radius, power; public int until; public bool follow;
            public List<IntVec3> line;
            public void ExposeData()
            {
                Scribe_Values.Look(ref kind, "kind"); Scribe_References.Look(ref caster, "caster"); Scribe_Values.Look(ref at, "at");
                Scribe_Values.Look(ref radius, "radius"); Scribe_Values.Look(ref power, "power", 1f); Scribe_Values.Look(ref until, "until");
                Scribe_Values.Look(ref follow, "follow"); Scribe_Collections.Look(ref line, "line", LookMode.Value);
            }
            public List<IntVec3> Cells(Map m)
            {
                if (line != null && line.Count > 0) return line;
                IntVec3 c = follow && caster != null && caster.Spawned ? caster.Position : at;
                return c.IsValid ? FireMagic.Point(c, m, Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f)) : new List<IntVec3>();
            }
        }

        public List<Field> fields = new List<Field>();
        public MapComponent_ComboFields(Map map) : base(map) { }

        /// <summary>Is a cloud of dust hanging over this cell? (Heat fuses it into glass.)</summary>
        public static bool DustAt(Map map, IntVec3 c)
        {
            var comp = map?.GetComponent<MapComponent_ComboFields>();
            return comp != null && comp.fields.Any(f => f.kind == ComboKind.Dust && f.Cells(map).Contains(c));
        }

        public static void Raise(Map map, ComboKind k, Pawn caster, IntVec3 at, float radius, float power, int ticks, bool follow, List<IntVec3> line = null)
        {
            var c = map?.GetComponent<MapComponent_ComboFields>();
            if (c == null) return;
            if (follow) c.fields.RemoveAll(f => f.follow && f.caster == caster);
            c.fields.Add(new Field { kind = k, caster = caster, at = at, radius = radius, power = power, until = Find.TickManager.TicksGame + ticks, follow = follow, line = line?.ToList() });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref fields, "fields", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && fields == null) fields = new List<Field>();
        }

        public override void MapComponentTick()
        {
            if (fields.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            fields.RemoveAll(f => f.until <= now || (f.follow && (f.caster == null || f.caster.Dead || !f.caster.Spawned || f.caster.Map != map)));
            foreach (Field f in fields)
            {
                var cells = f.Cells(map);
                if (cells.Count == 0) continue;
                if (now % 20 == 0) ComboMagic.Puffs(f.kind, map, cells, 0.08f);
                if (now % 90 == 0)
                {
                    IntVec3 mid = f.follow && f.caster != null && f.caster.Spawned ? f.caster.Position : f.at.IsValid ? f.at : cells[cells.Count / 2];
                    if (f.kind == ComboKind.Haze) MapComponent_SpellSprites.Put(map, "Haze/Shimmer", mid.ToVector3Shifted(), f.radius * 2f + 1f, 120, -1f, 0.8f);
                    if (f.kind == ComboKind.Dust) MapComponent_SpellSprites.Put(map, "Dust/Cloud", cells.RandomElement().ToVector3Shifted(), Rand.Range(2f, 3.5f), 160, -1f, 0.2f);
                }
                if (now % 60 != 0) continue;
                foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                {
                    bool foe = f.caster != null && MagicFx.Foe(p, f.caster);
                    switch (f.kind)
                    {
                        case ComboKind.Steam: if (foe) p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 1.5f * f.power, 0f, -1f, f.caster)); break;
                        case ComboKind.Haze: if (foe) MagicFx.Give(p, ComboDefOf.Avir_Hazed, 90); break;
                        case ComboKind.Dust: if (foe) MagicFx.Give(p, ComboDefOf.Avir_Dusted, 90); break;
                        case ComboKind.Storm: if (foe) WaterMagic.Soak(p, f.power * 0.5f); break;
                        case ComboKind.Magma: if (foe) p.TakeDamage(new DamageInfo(DamageDefOf.Flame, 2f * f.power, 0f, -1f, f.caster)); break;
                        case ComboKind.Bog: if (foe) MagicFx.Give(p, ComboDefOf.Avir_Mired, 90); break;
                    }
                }
                if (f.kind == ComboKind.Steam && now % 180 == 0) ComboMagic.Smoke(map, cells, 0.4f);
                if (f.kind == ComboKind.Dust) ComboMagic.Reveal(cells, map);
                if (f.kind == ComboKind.Steam && f.follow && now % 120 == 0 && f.caster != null && f.caster.Spawned) GasUtility.AddGas(f.caster.Position, map, GasType.BlindSmoke, 1.5f);
                if (f.kind == ComboKind.Storm && now % 120 == 0 && f.caster != null)
                {
                    // the storm strikes again: a bolt on one foe in it, leaping on
                    Pawn hit = MagicFx.PawnsIn(cells, map).Where(p => MagicFx.Foe(p, f.caster)).RandomElementWithFallback();
                    if (hit != null) AirMagic.Chain(f.caster, new List<IntVec3> { hit.Position }, map, 1, f.power * 0.7f, true);
                }
            }
        }
    }

    /// <summary>Molten ground: whoever stands on it burns; when it cools it is rubble - or, poured as a wall, stone.</summary>
    public class MagmaPool : ThingWithComps
    {
        public Pawn owner; public float power = 1f; public int left = 900; public bool setsHard; public int stoneLife = 2500;
        public int life = 900;
        private static Graphic crust;

        /// <summary>The last third of its heat: a dark crust with glowing seams, no longer a pool.</summary>
        public override Graphic Graphic
        {
            get
            {
                if (left > life / 3) return base.Graphic;
                if (crust == null) crust = GraphicDatabase.Get<Graphic_Random>("Things/Avir/Magic/MagmaCrust", ShaderDatabase.Transparent, new Vector2(1.6f, 1.6f), Color.white);
                return crust;
            }
        }

        public static void Pour(IntVec3 c, Map map, Pawn owner, float power, int life, bool setsHard, int stoneLife = 2500)
        {
            if (!c.InBounds(map) || c.Impassable(map) || ComboDefOf.Avir_Magma == null) return;
            if (Reactions.Pools(c, map).Any(t => !(t is MirePatch)))
            {
                // poured onto water: it sets at once into black glass, in a burst of steam
                foreach (Thing t in Reactions.Pools(c, map).Where(t => !(t is MirePatch)).ToList()) t.Destroy();
                ShardGround.Scatter(c, map, ComboDefOf.Avir_Obsidian);
                Gas_ScaldingSteam.Release(c, map, 1);
                return;
            }
            if (c.GetThingList(map).Any(t => t is MirePatch)) Gas_MarshGas.Release(c, map, 1, true);
            MagmaPool old = c.GetThingList(map).OfType<MagmaPool>().FirstOrDefault();
            if (old != null) { old.left = Mathf.Max(old.left, life); old.setsHard |= setsHard; return; }
            var m = (MagmaPool)ThingMaker.MakeThing(ComboDefOf.Avir_Magma);
            m.owner = owner; m.power = power; m.left = life; m.life = Mathf.Max(1, life); m.setsHard = setsHard; m.stoneLife = stoneLife;
            GenSpawn.Spawn(m, c, map);
        }

        public void Harden(bool now)
        {
            if (!Spawned) return;
            Map m = Map; IntVec3 c = Position;
            Destroy();
            FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), m, 1.5f, new Color(0.3f, 0.3f, 0.3f, 0.7f));
            if (setsHard) EarthWall.Raise(c, m, stoneLife); else MagicFx.Rubble(c, m);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner"); Scribe_Values.Look(ref power, "power", 1f); Scribe_Values.Look(ref left, "left", 900);
            Scribe_Values.Look(ref setsHard, "setsHard"); Scribe_Values.Look(ref stoneLife, "stoneLife", 2500); Scribe_Values.Look(ref life, "life", 900);
        }

        protected override void Tick()
        {
            base.Tick();
            if (--left <= 0) { Harden(false); return; }
            if (this.IsHashIntervalTick(20) && Rand.Chance(0.3f)) FleckMaker.ThrowFireGlow(DrawPos, Map, 0.6f);
            if (!this.IsHashIntervalTick(60)) return;
            foreach (Pawn p in Position.GetThingList(Map).OfType<Pawn>().ToList())
            {
                if (p.Dead) continue;
                p.TakeDamage(new DamageInfo(DamageDefOf.Flame, 4f * power, 0f, -1f, owner));
                if (!p.Dead && p.Spawned && Rand.Chance(0.2f)) FireUtility.TryAttachFire(p, 0.3f, owner);
            }
            foreach (Thing t in Position.GetThingList(Map).Where(t => t.def.category == ThingCategory.Item && t.FlammableNow).ToList()) FireUtility.TryAttachFire(t, 0.3f, owner);
        }
    }

    /// <summary>Mire made by a mage: foes in it are gripped, slower and slower; whoever stays in it too long, or falls in
    /// it, is drawn under and chokes. Friends of the mage wade through it.</summary>
    public class MirePatch : ThingWithComps
    {
        public Pawn owner; public float power = 1f; public int left = 5000;

        public static void Spread(IntVec3 c, Map map, Pawn owner, float power, int life)
        {
            if (!c.InBounds(map) || !c.Standable(map) || ComboDefOf.Avir_Mire == null) return;
            if (owner != null && owner.Spawned && owner.Position == c) return;   // never under the mage's own feet
            MirePatch old = c.GetThingList(map).OfType<MirePatch>().FirstOrDefault();
            if (old != null) { old.left = Mathf.Max(old.left, life); return; }
            var m = (MirePatch)ThingMaker.MakeThing(ComboDefOf.Avir_Mire);
            m.owner = owner; m.power = power; m.left = life;
            GenSpawn.Spawn(m, c, map);
            FireMagic.Extinguish(c, map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner"); Scribe_Values.Look(ref power, "power", 1f); Scribe_Values.Look(ref left, "left", 5000);
        }

        protected override void Tick()
        {
            base.Tick();
            if (--left <= 0) { Destroy(); return; }
            if (this.IsHashIntervalTick(90) && Rand.Chance(0.3f)) MapComponent_SpellSprites.Put(Map, "Bog/Bubbles", DrawPos + new Vector3(Rand.Range(-0.3f, 0.3f), 0f, Rand.Range(-0.3f, 0.3f)), 0.7f, 80);
            // a bog breathes: now and then a little marsh gas rises off it
            if (this.IsHashIntervalTick(600) && Rand.Chance(0.12f)) Gas_MarshGas.Release(Position, Map, 1, false);
            if (!this.IsHashIntervalTick(60)) return;
            foreach (Pawn p in Position.GetThingList(Map).OfType<Pawn>().ToList())
            {
                if (p.Dead || owner == null || !MagicFx.Foe(p, owner) || MagicFx.Rooted(p)) continue;
                MagicFx.Give(p, ComboDefOf.Avir_Mired, 150);
                // the longer they stay, the deeper they sink; the fallen go under fast
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(ComboDefOf.Avir_Sinking) ?? p.health.AddHediff(ComboDefOf.Avir_Sinking);
                h.Severity = Mathf.Min(1f, h.Severity + (p.Downed ? 0.08f : 0.025f) * power);
                FleckMaker.Static(Position, Map, FleckDefOf.WaterSplash, 0.9f);
            }
        }
    }

    /// <summary>A living carpet of moss (kept for moss, the focused meeting of water and earth - not cast yet): friends on it mend.</summary>
    public class MossPatch : ThingWithComps
    {
        public Pawn owner; public int left = 60000;

        public static void Grow(IntVec3 c, Map map, Pawn owner, int life)
        {
            ThingDef mossDef = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Moss");
            if (!c.InBounds(map) || !c.Standable(map) || mossDef == null) return;
            MossPatch old = c.GetThingList(map).OfType<MossPatch>().FirstOrDefault();
            if (old != null) { old.left = Mathf.Max(old.left, life); return; }
            var m = (MossPatch)ThingMaker.MakeThing(mossDef);
            m.owner = owner; m.left = life;
            GenSpawn.Spawn(m, c, map);
            FireMagic.Extinguish(c, map);
        }

        /// <summary>A little mending: the freshest wounds close a little.</summary>
        public static void Mend(Pawn p, float amount)
        {
            Hediff_Injury inj = p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.CanHealNaturally() || i.CanHealFromTending()).OrderByDescending(i => i.Severity).FirstOrDefault();
            inj?.Heal(amount);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner"); Scribe_Values.Look(ref left, "left", 60000);
        }

        protected override void Tick()
        {
            base.Tick();
            if ((left -= 1) <= 0) { Destroy(); return; }
            if (!this.IsHashIntervalTick(120)) return;
            FireMagic.Extinguish(Position, Map);
            foreach (Pawn p in Position.GetThingList(Map).OfType<Pawn>().ToList())
            {
                if (p.Dead) continue;
                if (owner != null && MagicFx.Foe(p, owner)) MagicFx.Give(p, ComboDefOf.Avir_Mired, 150);
                else Mend(p, 0.4f);
            }
        }
    }

    /// <summary>The weapons and wards of the meetings, on the blow itself.</summary>
    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class Patch_ComboMelee
    {
        public static void Postfix(Verb_MeleeAttack __instance, bool __result)
        {
            if (!__result || !AvirMagic.On || ComboDefOf.Avir_SteamEnchant == null) return;
            Pawn p = __instance.CasterPawn;
            if (p?.health?.hediffSet == null || !(__instance.CurrentTarget.Thing is Pawn foe) || foe.Dead) return;
            var hs = p.health.hediffSet;
            if (hs.HasHediff(ComboDefOf.Avir_SteamEnchant)) { foe.TakeDamage(new DamageInfo(DamageDefOf.Burn, 5f, 0f, -1f, p)); if (foe.Spawned) ComboMagic.Smoke(foe.Map, new List<IntVec3> { foe.Position }, 1f); }
            if (!foe.Dead && hs.HasHediff(ComboDefOf.Avir_StormEnchant)) { foe.TakeDamage(new DamageInfo(foe.RaceProps.IsMechanoid ? DamageDefOf.EMP : DamageDefOf.Burn, 5f, 0f, -1f, p)); if (!foe.Dead && Rand.Chance(0.2f)) MagicFx.Stun(foe, 60, p); if (foe.Spawned) FleckMaker.ThrowLightningGlow(foe.DrawPos, foe.Map, 0.8f); }
            if (!foe.Dead && hs.HasHediff(ComboDefOf.Avir_MagmaEnchant)) { foe.TakeDamage(new DamageInfo(DamageDefOf.Flame, 6f, 0f, -1f, p)); if (!foe.Dead && foe.Spawned && Rand.Chance(0.3f)) FireUtility.TryAttachFire(foe, 0.3f, p); }
            if (!foe.Dead && hs.HasHediff(ComboDefOf.Avir_HazeEnchant)) MagicFx.Give(foe, ComboDefOf.Avir_Dazzled, 300);
            if (!foe.Dead && hs.HasHediff(ComboDefOf.Avir_BogEnchant)) MagicFx.Give(foe, ComboDefOf.Avir_Mired, 300);
            if (!foe.Dead && hs.HasHediff(ComboDefOf.Avir_DustEnchant)) { ComboMagic.Grind(foe, p, 0.6f); MagicFx.Give(foe, ComboDefOf.Avir_Dusted, 300); }
            // the wards answer whoever strikes their wearer
            var th = foe.health.hediffSet;
            if (p.Dead || !p.Spawned) return;
            if (th.HasHediff(ComboDefOf.Avir_StormWard)) { p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 4f, 0f, -1f, foe)); MagicFx.Stun(p, 60, foe); }
            if (!p.Dead && th.HasHediff(ComboDefOf.Avir_MagmaWard)) p.TakeDamage(new DamageInfo(DamageDefOf.Flame, 4f, 0f, -1f, foe));
            if (!p.Dead && th.HasHediff(ComboDefOf.Avir_DustWard)) MagicFx.Give(p, ComboDefOf.Avir_Dusted, 300);
            if (!p.Dead && th.HasHediff(ComboDefOf.Avir_BogWard)) MagicFx.Give(p, EarthAirDefOf.Avir_Rooted, 120);
        }
    }

    /// <summary>A ward of haze: shots at its wearer often pass through a shape that is not there.</summary>
    [HarmonyPatch(typeof(Bullet), "Impact")]
    public static class Patch_HazeWardMisses
    {
        public static bool Prefix(Bullet __instance, Thing hitThing)
        {
            if (!AvirMagic.On || ComboDefOf.Avir_HazeWard == null || !(hitThing is Pawn p) || p.health?.hediffSet == null) return true;
            if (!p.health.hediffSet.HasHediff(ComboDefOf.Avir_HazeWard) || !Rand.Chance(0.35f)) return true;
            if (__instance.Spawned) { FleckMaker.ThrowHeatGlow(p.Position, p.Map, 1f); MoteMaker.ThrowText(p.DrawPos, p.Map, "Through the haze", 2.5f); __instance.Destroy(); }
            return false;
        }
    }

    /// <summary>A ward of steam: its wearer is wrapped in a little cloud now and then; a ward of moss mends them.</summary>
    public class HediffComp_ComboWard : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = parent.pawn;
            if (!p.Spawned || !p.IsHashIntervalTick(240)) return;
            if (parent.def == ComboDefOf.Avir_SteamWard && p.mindState?.enemyTarget != null) GasUtility.AddGas(p.Position, p.Map, GasType.BlindSmoke, 1.2f);

        }
    }

    public class HediffCompProperties_ComboWard : HediffCompProperties
    {
        public HediffCompProperties_ComboWard() { compClass = typeof(HediffComp_ComboWard); }
    }

    [DefOf]
    public static class ComboDefOf
    {
        public static HediffDef Avir_Dazzled, Avir_Hazed, Avir_Dusted, Avir_Mired, Avir_Sinking;
        public static HediffDef Avir_SteamWard, Avir_StormWard, Avir_MagmaWard, Avir_HazeWard, Avir_BogWard, Avir_DustWard;
        public static HediffDef Avir_SteamEnchant, Avir_StormEnchant, Avir_MagmaEnchant, Avir_HazeEnchant, Avir_BogEnchant, Avir_DustEnchant;
        public static ThingDef Avir_Magma, Avir_Mire, Avir_GasSteam, Avir_GasMarsh, Avir_Obsidian, Avir_Glass;
        static ComboDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ComboDefOf)); }

        public static HediffDef Ward(ComboKind k) => k == ComboKind.Steam ? Avir_SteamWard : k == ComboKind.Storm ? Avir_StormWard : k == ComboKind.Magma ? Avir_MagmaWard : k == ComboKind.Haze ? Avir_HazeWard : k == ComboKind.Bog ? Avir_BogWard : Avir_DustWard;
        public static HediffDef Enchant(ComboKind k) => k == ComboKind.Steam ? Avir_SteamEnchant : k == ComboKind.Storm ? Avir_StormEnchant : k == ComboKind.Magma ? Avir_MagmaEnchant : k == ComboKind.Haze ? Avir_HazeEnchant : k == ComboKind.Bog ? Avir_BogEnchant : Avir_DustEnchant;
    }
}

namespace Avir.Magic
{
    /// <summary>The meetings of elements in words.</summary>
    public static class ComboText
    {
        public static string Short(ComboKind k)
        {
            switch (k)
            {
                case ComboKind.Steam: return "Fire and water side by side make steam: it scalds, and its cloud hides everyone in it - no shot goes in or out.";
                case ComboKind.Storm: return "Water and air side by side make a storm: lightning that leaps from foe to foe, worse for the soaked, and machines are struck hardest.";
                case ComboKind.Magma: return "Fire and earth side by side make magma: molten ground that burns whoever stands on it and cools into stone.";
                case ComboKind.Haze: return "Fire and air side by side make haze: the air shimmers, foes are dazzled and miss, and they shoot at false shapes of the mage.";
                case ComboKind.Bog: return "Water and earth side by side make a bog: a mire that grips foes, slower and slower - and whoever stays in it too long, or falls in it, is drawn under and chokes.";
                case ComboKind.Dust: return "Earth and air side by side make dust: a cloud that blinds and grinds, and shows the unseen.";
                default: return "";
            }
        }

        private static string Verb(ComboKind k, RuneKind v)
        {
            switch (v)
            {
                case RuneKind.Release:
                    return k == ComboKind.Steam ? "bursts scalding, and the cloud stays" : k == ComboKind.Storm ? "falls as a bolt that leaps on" : k == ComboKind.Magma ? "breaks the ground open: molten stone strikes and pools"
                         : k == ComboKind.Haze ? "flashes with heat: foes dazzled" : k == ComboKind.Bog ? "bursts in mud: foes knocked down in a mire" : "blasts sand: every bare part ground, the dust hangs";
                case RuneKind.Hold:
                    return k == ComboKind.Magma ? "lies as a field of magma" : k == ComboKind.Bog ? "lies as a mire that draws foes under" : k == ComboKind.Storm ? "hangs as a storm that keeps striking"
                         : k == ComboKind.Steam ? "hangs as a cloud of steam" : k == ComboKind.Haze ? "hangs as a shimmer foes cannot aim through" : "hangs as a cloud of dust";
                case RuneKind.Drain:
                    return k == ComboKind.Storm ? "draws the charge out of machines (EMP)" : k == ComboKind.Magma ? "draws the heat out: the magma sets at once" : k == ComboKind.Bog || k == ComboKind.Dust ? "drinks the water out of foes" : "draws the heat out of fires and foes";
                case RuneKind.Lift:
                    return "throws foes up" + (k == ComboKind.Magma ? " on a spout of lava" : k == ComboKind.Steam ? " on a geyser of steam" : k == ComboKind.Dust ? " in a spout of sand" : k == ComboKind.Storm ? " in a whirl of rain" : k == ComboKind.Bog ? " on a geyser of mud" : " on rising heat");
                case RuneKind.Growth:
                    return k == ComboKind.Magma ? "creeps outward" : k == ComboKind.Haze || k == ComboKind.Dust ? "spreads wide and hangs" : k == ComboKind.Bog ? "spreads its black wet earth - everything green grows" : "makes everything green grow";
                case RuneKind.Purge:
                    return "cleans: fire, filth and poison" + (k == ComboKind.Haze || k == ComboKind.Dust ? ", and the wet dried" : "");
                default: return "";
            }
        }

        private static string Step(ComboKind k)
        {
            switch (k)
            {
                case ComboKind.Steam: return "The mage scatters into a cloud and drifts low to the target - nothing can reach them on the way; there the cloud bursts and scalds everyone round.";
                case ComboKind.Storm: return "The mage rides a bolt of lightning - the fastest of all journeys, but seen: every foe on the way is struck, and where they land the thunder knocks those round off their feet.";
                case ComboKind.Magma: return "The mage goes through the ground as lava: a glowing crack runs above; where they come up, a ring of magma round them (none under the mage).";
                case ComboKind.Haze: return "The mage shimmers and is gone from sight for the journey; a false shape of them stays where they stood, and foes go on shooting at it.";
                case ComboKind.Bog: return "The mage sinks into the ground as into water and comes up at the target; the ground on the way and round where they come up turns to mire.";
                default: return "The mage spins up in a dust devil, over whatever stands between, and lands in a cloud that blinds everyone round.";
            }
        }

        public static SpellEffect Explain(ComboKind k, RitualSchema s)
        {
            var e = new SpellEffect();
            RitualReading r = RitualEvaluator.Read(s);
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).Select(v => v.kind).Distinct().ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            string what = string.Join(", ", verbs.Select(v => Verb(k, v)).Where(x => x.Length > 0));
            string name = Combos.Label(k).CapitalizeFirst();
            RuneDef form = forms.FirstOrDefault();
            switch (form?.kind)
            {
                case RuneKind.Restore:
                    e.main = k == ComboKind.Steam ? "A steam bath for friends at the target: the cold and the poison sweated out." : k == ComboKind.Storm ? "A jolt: friends there are woken from stun, daze and drugged sleep."
                           : k == ComboKind.Magma ? "Hot stones: bleeding wounds seared shut, the cold driven out." : k == ComboKind.Haze ? "Warm dry air: the wet and the cold leave friends there."
                           : k == ComboKind.Bog ? "A mud poultice: burns and bruises cooled and bound." : "Dry dust packed into the wounds: bleeding stops."; break;
                case RuneKind.Enchant:
                    e.main = "A weapon of " + Combos.Label(k) + ": " + (k == ComboKind.Steam ? "blows scald and leave a puff of cloud" : k == ComboKind.Storm ? "blows jolt (machines worse), sometimes stun"
                           : k == ComboKind.Magma ? "blows burn and may set the foe alight" : k == ComboKind.Haze ? "blows dazzle" : k == ComboKind.Bog ? "blows clog the foe with mud - slow" : "blows grind and blind") + "."; break;
                case RuneKind.Step:
                    e.main = SpellMods.Pulls(s) ? "The one at the target is drawn to the mage through " + Combos.Label(k) + "." : Step(k); break;
                case RuneKind.Shape:
                    e.main = k == ComboKind.Magma ? "A wall of magma between two points: it burns, then sets into stone that stands for hours." : k == ComboKind.Haze ? "A row of false shapes of the mage between two points: foes shoot at them."
                           : k == ComboKind.Bog ? "A moat of mire between two points, two cells wide: foes wade, and those who stay are drawn under." : k == ComboKind.Storm ? "A fence of lightning between two points: it strikes whoever is in it."
                           : "A curtain of " + Combos.Label(k) + " between two points" + (k == ComboKind.Steam ? ": nothing is seen through it" : ": whoever is in it can hardly see") + "."; break;
                case RuneKind.Sense:
                    e.main = k == ComboKind.Dust ? "Dust settles on everything: the mage feels everyone round about - and the unseen are shown."
                           : k == ComboKind.Magma ? "The heat in the rock: the mage feels everyone round about - and the veins of ore inside the rock are shown, even in mountains."
                           : "The mage feels everyone round about, through walls."; break;
                case RuneKind.Summon: e.main = "Spirits are not made yet."; break;
                case RuneKind.Aura:
                    string aura = k == ComboKind.Steam ? "a cloak of steam: the mage is hidden in it, no shot goes in or out" : k == ComboKind.Storm ? "a crown of storm: lightning strikes foes who come near"
                                : k == ComboKind.Magma ? "heat: foes close to the mage burn" : k == ComboKind.Haze ? "a shimmer: foes near the mage miss" : k == ComboKind.Bog ? "the ground round the mage a mire for foes" : "a sandstorm: foes near miss, the unseen are shown";
                    e.main = (what.Length > 0 ? name + " " + what + " round the mage; and " : "Round the mage, for a while: ") + aura + ".";
                    break;
                default:
                    string where = form?.kind == RuneKind.Beam ? (k == ComboKind.Storm ? "as lightning leaping from foe to foe along the line" : k == ComboKind.Magma ? "as a stream of lava along the line" : k == ComboKind.Haze ? "as a ray of heat along the line" : k == ComboKind.Bog ? "as a jet of mud along the line" : "along a line to the target") : "at the point";
                    e.main = what.Length > 0 ? name + " " + what + " - " + where + "." : form?.kind == RuneKind.Beam ? name + " " + where + "." : name + " with nothing to do: give it a verb or a form.";
                    break;
            }
            e.Add(name + ": two elements met", Short(k));
            if (r.barrier > 0) e.Add("Ward of " + Combos.Label(k), k == ComboKind.Steam ? "A little cloud wraps the wearer when foes are near." : k == ComboKind.Storm ? "Whoever strikes the wearer is jolted and stunned."
                : k == ComboKind.Magma ? "A crust: blows bite less, and whoever strikes the wearer is burnt." : k == ComboKind.Haze ? "Bullets often pass through a shape that is not there."
                : k == ComboKind.Bog ? "Whoever strikes the wearer is stuck where they stand for a moment." : "Whoever strikes the wearer gets dust in the eyes.");
            return e;
        }
    }
}
