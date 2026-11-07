using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    // =====================================================================================
    // SECOND-ORDER MEETINGS (Магия-второй-порядок.md, the author's rules): two circles meet into a pair (X); a third small
    // circle beside them that has no pair of its own meets X in turn -> Y. Order decides: the pair formed first, then the
    // third. X with one of its own elements (A-B-A) is X tipped to that side with that element's own effect; X with the
    // third element is something new. All three circles work as X (its whole rune grammar) and Y adds its signature to each
    // part: the substance it leaves and the one rule of its own, expressed through the runes of the part (Release, Beam,
    // Growth, Hold, Shape, Aura, Lift, Drain, Purge, Restore...). Auras are two-sided: they help one's own as well.
    // =====================================================================================
    public enum SecondKind
    {
        None,
        Inferno, Boil, Geyser, Typhoon,          // steam + fire / water / earth / air
        Lava, Volcano, Obsidian, Ash,            // magma + fire / earth / water / air
        Deluge, Hurricane, BallLightning, Mudflow, // storm + water / air / fire / earth
        Swelter, Thermal, Rainbow, DryWind,      // haze + fire / air / water / earth
        Quag, Silt, MarshGas, MarshHaze,         // bog + water / earth / fire / air
        Rockfall, Emery, BurningSand, Clay       // dust + earth / air / fire / water
    }

    public static class SecondOrder
    {
        /// <summary>The second-order meeting being cast now (SpellChain.Run sets it for the circle).</summary>
        public static SecondKind Now;

        public static SecondKind Of(ComboKind x, RuneKind c)
        {
            switch (x)
            {
                case ComboKind.Steam: return c == RuneKind.Fire ? SecondKind.Inferno : c == RuneKind.Water ? SecondKind.Boil : c == RuneKind.Earth ? SecondKind.Geyser : SecondKind.Typhoon;
                case ComboKind.Magma: return c == RuneKind.Fire ? SecondKind.Lava : c == RuneKind.Earth ? SecondKind.Volcano : c == RuneKind.Water ? SecondKind.Obsidian : SecondKind.Ash;
                case ComboKind.Storm: return c == RuneKind.Water ? SecondKind.Deluge : c == RuneKind.Air ? SecondKind.Hurricane : c == RuneKind.Fire ? SecondKind.BallLightning : SecondKind.Mudflow;
                case ComboKind.Haze: return c == RuneKind.Fire ? SecondKind.Swelter : c == RuneKind.Air ? SecondKind.Thermal : c == RuneKind.Water ? SecondKind.Rainbow : SecondKind.DryWind;
                case ComboKind.Bog: return c == RuneKind.Water ? SecondKind.Quag : c == RuneKind.Earth ? SecondKind.Silt : c == RuneKind.Fire ? SecondKind.MarshGas : SecondKind.MarshHaze;
                case ComboKind.Dust: return c == RuneKind.Earth ? SecondKind.Rockfall : c == RuneKind.Air ? SecondKind.Emery : c == RuneKind.Fire ? SecondKind.BurningSand : SecondKind.Clay;
            }
            return SecondKind.None;
        }

        /// <summary>Ash on the ground: +20% fertility for 10 days (weaker than arcane soil, which it is not).</summary>
        public const float AshBonus = 0.2f;
        public const int AshDays = 10;

        public static ComboKind PairOf(SecondKind y)
        {
            switch (y)
            {
                case SecondKind.Inferno: case SecondKind.Boil: case SecondKind.Geyser: case SecondKind.Typhoon: return ComboKind.Steam;
                case SecondKind.Lava: case SecondKind.Volcano: case SecondKind.Obsidian: case SecondKind.Ash: return ComboKind.Magma;
                case SecondKind.Deluge: case SecondKind.Hurricane: case SecondKind.BallLightning: case SecondKind.Mudflow: return ComboKind.Storm;
                case SecondKind.Swelter: case SecondKind.Thermal: case SecondKind.Rainbow: case SecondKind.DryWind: return ComboKind.Haze;
                case SecondKind.Quag: case SecondKind.Silt: case SecondKind.MarshGas: case SecondKind.MarshHaze: return ComboKind.Bog;
                case SecondKind.Rockfall: case SecondKind.Emery: case SecondKind.BurningSand: case SecondKind.Clay: return ComboKind.Dust;
            }
            return ComboKind.None;
        }

        /// <summary>The element that met the pair to make this.</summary>
        public static RuneKind Third(SecondKind y)
        {
            switch (y)
            {
                case SecondKind.Inferno: case SecondKind.Lava: case SecondKind.BallLightning: case SecondKind.Swelter: case SecondKind.MarshGas: case SecondKind.BurningSand: return RuneKind.Fire;
                case SecondKind.Boil: case SecondKind.Obsidian: case SecondKind.Deluge: case SecondKind.Rainbow: case SecondKind.Quag: case SecondKind.Clay: return RuneKind.Water;
                case SecondKind.Geyser: case SecondKind.Volcano: case SecondKind.Mudflow: case SecondKind.DryWind: case SecondKind.Silt: case SecondKind.Rockfall: return RuneKind.Earth;
                default: return RuneKind.Air;
            }
        }

        /// <summary>The second-order meetings of a spell, circle by circle.</summary>
        public static Dictionary<SpellCircle, SecondKind> Of(SpellCircle root) => Seconds(root, Combos.Pairs(root, false));

        public static string Label(SecondKind y)
        {
            switch (y)
            {
                case SecondKind.Inferno: return "inferno"; case SecondKind.Boil: return "boil"; case SecondKind.Geyser: return "geyser"; case SecondKind.Typhoon: return "typhoon";
                case SecondKind.Lava: return "lava"; case SecondKind.Volcano: return "volcano"; case SecondKind.Obsidian: return "obsidian"; case SecondKind.Ash: return "ash";
                case SecondKind.Deluge: return "deluge"; case SecondKind.Hurricane: return "hurricane"; case SecondKind.BallLightning: return "ball lightning"; case SecondKind.Mudflow: return "mudflow";
                case SecondKind.Swelter: return "swelter"; case SecondKind.Thermal: return "thermal"; case SecondKind.Rainbow: return "rainbow"; case SecondKind.DryWind: return "dry wind";
                case SecondKind.Quag: return "quagmire"; case SecondKind.Silt: return "silt"; case SecondKind.MarshGas: return "marsh gas"; case SecondKind.MarshHaze: return "marsh haze";
                case SecondKind.Rockfall: return "rockfall"; case SecondKind.Emery: return "emery"; case SecondKind.BurningSand: return "burning sand"; case SecondKind.Clay: return "clay";
            }
            return "";
        }

        public static string Text(SecondKind y)
        {
            switch (y)
            {
                case SecondKind.Inferno: return "Dry heat, no steam: it dries everything in reach, strips the wet and water wards, and cooks those sealed in armour twice as fast (the heat has nowhere to go).";
                case SecondKind.Boil: return "Boiling water: everything wet in reach boils - the soaked and those standing in water are scalded every second; the dry are spared. Held, it is a cauldron.";
                case SecondKind.Geyser: return "Boiling mud and steam burst from the ground in turn, throwing whoever stands over them. Beside a natural geyser it is stronger.";
                case SecondKind.Typhoon: return "A whirl of boiling rain that walks, raining under itself: it soaks first and then the boiling rain bites the soaked half again as hard (its own wound, from above); now and then a jet strikes and throws.";
                case SecondKind.Lava: return "Lava: it runs further and burns harder than magma, and before it is gone it cools into magma, which then goes its own way.";
                case SecondKind.Volcano: return "Fire inside stone: the ground cracks, and a heartbeat later stones and magma burst up from below - where armour is thinnest. With Lift a basalt pillar rises under the foe and holds them up there until it falls.";
                case SecondKind.Obsidian: return "Magma quenched: a burst of steam and black glass, sharp and brittle.";
                case SecondKind.Ash: return "A scorching cloud: it blinds and chokes the unsealed, and the ash it lets fall feeds the ground a little (+20% fertility for ten days).";
                case SecondKind.Deluge: return "Weather on the spot: rain over the place for a while - everything wet, fires out, lightning through the pools.";
                case SecondKind.Hurricane: return "Wind: it throws everyone, breaks what is light, and knocks shots out of the air - an aura of it shields one's own. Every throw in the spell flies half again as far and strikes harder; every Lift goes 1.6 times higher, and the fall is as much harder. It is unstable: it leaves whirls behind (two; one when held; three with Growth) that wander for a while and spin whoever they catch round - friend or foe, all but the one who made them.";
                case SecondKind.BallLightning: return "Slow globes that seek the foes and burst in a discharge - metal on the body takes half again as much. Along a beam, one globe rolls and strikes every foe it passes.";
                case SecondKind.Mudflow: return "A wave of mud: it carries foes away and buries the fallen.";
                case SecondKind.Swelter: return "Exhausting heat: foes tire fast and suffer the heat; one's own beside the caster do not.";
                case SecondKind.Thermal: return "A rising warm current: shots through it rise and miss, one's own move lighter in it.";
                case SecondKind.Rainbow: return "A rainbow mist: foes are dazzled and confused; one's own cheer up at the sight.";
                case SecondKind.DryWind: return "The enemy of water: it dries the soaked, the pools and the mire, and parches the foes.";
                case SecondKind.Quag: return "The bog, deeper and quicker: the heavy sink first.";
                case SecondKind.Silt: return "Silt chokes mechanisms - turrets jam, machines stall, joints of armour and bionics clog. Held, it sets and holds the caught.";
                case SecondKind.MarshGas: return "Marsh gas: it does not burn by itself - another spark sets it off. Breathed, it presses on the lungs (not deadly); a gas mask or a sealed suit keeps it out.";
                case SecondKind.MarshHaze: return "A marsh haze: it poisons the foes who breathe it and hides one's own inside it.";
                case SecondKind.Rockfall: return "Stones from above: a blow to head and shoulders that shakes through armour, and rubble left for cover.";
                case SecondKind.Emery: return "It wears away: armour and clothes lose their strength. An aura of it hones one's own blades instead.";
                case SecondKind.BurningSand: return "Hot grains stick and burn until water washes them off.";
                case SecondKind.Clay: return "Clay binds arms and legs and sets into a crust - a foe can be taken alive.";
            }
            return "";
        }

        /// <summary>Every meeting of the second order has its own colour, not a shade of its pair's (the author: each school
        /// and sub-school - its own colour).</summary>
        public static Color ColorOf(SecondKind y, ComboKind x, RuneKind c) => ColorOf(y);
        public static Color ColorOf(SecondKind y)
        {
            switch (y)
            {
                case SecondKind.Inferno: return new Color(1f, 0.15f, 0.02f);       // scarlet heat
                case SecondKind.Boil: return new Color(0.45f, 0.85f, 0.95f);       // hot cyan
                case SecondKind.Geyser: return new Color(0.55f, 0.75f, 0.65f);     // steaming sage
                case SecondKind.Typhoon: return new Color(0.2f, 0.6f, 0.75f);      // sea teal
                case SecondKind.Lava: return new Color(1f, 0.85f, 0.2f);           // white-gold
                case SecondKind.Volcano: return new Color(0.55f, 0.08f, 0.05f);    // dark crimson
                case SecondKind.Obsidian: return new Color(0.38f, 0.2f, 0.55f);    // black-violet glass
                case SecondKind.Ash: return new Color(0.58f, 0.56f, 0.55f);        // ash grey
                case SecondKind.Deluge: return new Color(0.15f, 0.35f, 0.85f);     // rain blue
                case SecondKind.Hurricane: return new Color(0.75f, 0.88f, 0.98f);  // pale sky
                case SecondKind.BallLightning: return new Color(0.65f, 0.45f, 1f); // electric violet
                case SecondKind.Mudflow: return new Color(0.45f, 0.3f, 0.15f);     // mud brown
                case SecondKind.Swelter: return new Color(1f, 0.55f, 0.2f);        // heat orange
                case SecondKind.Thermal: return new Color(1f, 0.92f, 0.65f);       // warm cream
                case SecondKind.Rainbow: return new Color(1f, 0.45f, 0.85f);       // magenta
                case SecondKind.DryWind: return new Color(0.85f, 0.68f, 0.3f);     // ochre
                case SecondKind.Quag: return new Color(0.18f, 0.3f, 0.12f);        // deep marsh green
                case SecondKind.Silt: return new Color(0.55f, 0.5f, 0.38f);        // grey silt
                case SecondKind.MarshGas: return new Color(0.68f, 0.85f, 0.2f);    // acid green
                case SecondKind.MarshHaze: return new Color(0.42f, 0.55f, 0.48f);  // murky grey-green
                case SecondKind.Rockfall: return new Color(0.45f, 0.42f, 0.4f);    // stone grey
                case SecondKind.Emery: return new Color(0.72f, 0.75f, 0.82f);      // steel
                case SecondKind.BurningSand: return new Color(0.95f, 0.42f, 0.32f);// coral-hot sand
                case SecondKind.Clay: return new Color(0.68f, 0.38f, 0.22f);       // terracotta
            }
            return Color.white;
        }

        /// <summary>The third circles of a spell and what they made with their pair (pairs from Combos.Pairs, each third
        /// circle also put there with its pair's kind, so it works as the pair).</summary>
        public static Dictionary<SpellCircle, SecondKind> Seconds(SpellCircle root, Dictionary<SpellCircle, ComboKind> pairs)
        {
            // read in order (Combos.Read, Fix159): the third after the pair, then the circles of its elements strengthen it
            var map = new Dictionary<SpellCircle, SecondKind>();
            foreach (Combos.Meeting m in Combos.Read(root))
                if (m.second != SecondKind.None) foreach (SpellCircle c in m.circles) map[c] = m.second;
            return map;
        }

        /// <summary>How many circles past the three strengthen this meeting in this spell (set by SpellChain.Run).</summary>
        public static int Extra;

        // ------------------------------------------------------------------ the signature at cast

        public static bool Sealed(Pawn p) => SpellBody.Of(p).sealedFull;
        public static bool Masked(Pawn p) => Sealed(p) || p.GetStatValue(StatDefOf.ToxicEnvironmentResistance) >= 0.5f;
        public static HediffDef H(string n) => DefDatabase<HediffDef>.GetNamedSilentFail(n);

        private class Ctx
        {
            public Pawn caster; public Map map; public IntVec3 at; public float radius, power; public int breadth; public List<IntVec3> cells; public List<Pawn> foes, own;
            public bool release, hold, growth, lift, drain, purge, restore, aura, beam, shape, enchant;
        }

        /// <summary>While the pair under a second-order meeting is cast as its background (Fix191).</summary>
        public static bool Background;
        public const float BackgroundShare = 0.33f;

        /// <summary>Does this part strike as a second-order meeting (the same rule as Apply: a meeting, and a deed)?</summary>
        public static bool Takes(RitualReading r)
        {
            if (Now == SecondKind.None || r == null) return false;
            var v = new HashSet<RuneKind>(r.verbs.Select(x => x.kind));
            return v.Contains(RuneKind.Release) || v.Contains(RuneKind.Beam) || v.Contains(RuneKind.Hold) || v.Contains(RuneKind.Growth) || v.Contains(RuneKind.Lift)
                || v.Contains(RuneKind.Drain) || v.Contains(RuneKind.Purge) || v.Contains(RuneKind.Aura) || v.Contains(RuneKind.Shape) || v.Contains(RuneKind.Enchant);
        }

        /// <summary>A part of a second-order meeting has been cast: its signature, through its runes.</summary>
        public static void Apply(Pawn caster, RitualSchema part, RitualReading r, LocalTargetInfo target, float power)
        {
            SecondKind y = Now;
            Map map = caster?.Map;
            if (y == SecondKind.None || map == null || r == null) return;
            var ctx = new Ctx { caster = caster, map = map, at = target.Cell, power = power * (1.25f + 0.25f * Extra) };
            var v = new HashSet<RuneKind>(r.verbs.Select(x => x.kind));
            ctx.release = v.Contains(RuneKind.Release); ctx.hold = v.Contains(RuneKind.Hold); ctx.growth = v.Contains(RuneKind.Growth); ctx.lift = v.Contains(RuneKind.Lift);
            ctx.drain = v.Contains(RuneKind.Drain); ctx.purge = v.Contains(RuneKind.Purge); ctx.restore = v.Contains(RuneKind.Restore);
            ctx.aura = v.Contains(RuneKind.Aura); ctx.beam = v.Contains(RuneKind.Beam); ctx.shape = v.Contains(RuneKind.Shape); ctx.enchant = v.Contains(RuneKind.Enchant);
            // the meeting works only through a deed: a Barrier alone (a ward) or a Step has nothing for it to work through,
            // and a mending lets through only the meetings that have a mending of their own (the author, Fix149: a ward
            // spell poured lava round its caster)
            bool acts = ctx.release || ctx.beam || ctx.hold || ctx.growth || ctx.lift || ctx.drain || ctx.purge || ctx.aura || ctx.shape || ctx.enchant;
            if (!acts && (!ctx.restore || (y != SecondKind.Boil && y != SecondKind.Geyser && y != SecondKind.Mudflow))) return;
            ctx.breadth = r.breadth;
            ctx.radius = FireMagic.PointRadius(r) * (ctx.growth ? 1.5f : 1f);
            if (ctx.aura) ctx.cells = FireMagic.Point(caster.Position, map, SpellAura.Pulsing ? SpellAura.PulseRadius : ctx.radius + 1.5f).ToList();   // the mage's own cell too (Own Blast; Fix190)
            else if (ctx.beam) ctx.cells = FireMagic.Beam(SpellOrigin.Of(caster), ctx.at, map, r.breadth).Where(c => c != SpellOrigin.Of(caster)).ToList();
            else if (ctx.shape) ctx.cells = FireMagic.Wall(SpellOrigin.Of(caster), ctx.at, map, 5 + 2 * r.breadth);
            else ctx.cells = FireMagic.Point(ctx.at, map, ctx.radius);
            ctx.cells = ctx.cells.Where(c => c.InBounds(map)).ToList();
            List<Pawn> all = ctx.cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().Where(p => !p.Dead).ToList();
            // and those the spell has already thrown or lifted over these cells: what the meeting does to them waits till they land
            var cellSet = new HashSet<IntVec3>(ctx.cells);
            foreach (SpellHurl f in SpellHurl.Flying.ToList())
                if (f.Spawned && f.Map == map && f.FlyingPawn != null && !all.Contains(f.FlyingPawn) && (cellSet.Contains(f.DestinationPos.ToIntVec3()) || cellSet.Contains(f.Position))) all.Add(f.FlyingPawn);
            ctx.foes = all.Where(p => MagicFx.Foe(p, caster)).ToList();
            ctx.own = all.Where(p => SpellSculpt.Own(p, caster)).ToList();
            SpellLore.Learn("Second:" + y);
            // it shows: its name over the place once a cast, and its colour over the ground it takes
            if (signedAt != Find.TickManager.TicksGame) { signedAt = Find.TickManager.TicksGame; MoteMaker.ThrowText(ctx.at.ToVector3Shifted(), map, Label(y).CapitalizeFirst() + "!", ColorOf(y), 3.5f); }
            { Color sc = ColorOf(y); sc.a = 0.55f; foreach (IntVec3 x in ctx.cells) if (Rand.Chance(0.25f)) FleckMaker.ThrowDustPuffThick(x.ToVector3Shifted(), map, 1.1f, sc); }
            try { Do(y, ctx); }
            catch (Exception e) { Log.ErrorOnce("[Avir] second-order " + y + ": " + e, 0x5EC0 + (int)y); }
        }

        private static int signedAt = -1;
        private static SecondFields F(Map m) => m.GetComponent<SecondFields>();
        private static int Ticks(float hours, float p) => Mathf.RoundToInt(GenDate.TicksPerHour * hours * Mathf.Max(0.5f, p));
        private static void Burn(Pawn p, float amt, Pawn by) => p.TakeDamage(new DamageInfo(DamageDefOf.Burn, amt, 0f, -1f, by));
        private static void Blunt(Pawn p, float amt, Pawn by) => p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, amt, 0f, -1f, by));
        private static void Heat(Pawn p, float sev) => HealthUtility.AdjustSeverity(p, HediffDefOf.Heatstroke, sev);
        private static bool Wet(Pawn p) => (WaterDefOf.Avir_Soaked != null && p.health.hediffSet.HasHediff(WaterDefOf.Avir_Soaked)) || (p.Spawned && Reactions.Wet(p.Position, p.Map));
        private static void Dry(Pawn p) { Hediff w = WaterDefOf.Avir_Soaked != null ? p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Soaked) : null; if (w != null) p.health.RemoveHediff(w); }
        private static void Puff(Ctx c, Color col) { foreach (IntVec3 x in c.cells) if (Rand.Chance(0.3f)) FleckMaker.ThrowDustPuffThick(x.ToVector3Shifted(), c.map, 0.9f, col); }
        private static void Field(Ctx c, SecondKind y, int ticks) => F(c.map)?.Add(y, c.cells, ticks, c.caster, c.power);

        private static void Do(SecondKind y, Ctx c)
        {
            Pawn by = c.caster;
            switch (y)
            {
                case SecondKind.Inferno:
                    foreach (Pawn p in c.foes) { Burn(p, 4f * c.power, by); Heat(p, 0.06f * c.power * (Sealed(p) ? 2f : 1f)); MagicFx.Give(p, H("Avir_HeatTrap"), 600); Dry(p); }
                    foreach (Pawn p in c.own) if (c.aura) { Dry(p); MagicFx.Give(p, H("Avir_Dried"), Ticks(1f, c.power)); }
                    foreach (IntVec3 x in c.cells) foreach (Thing t in Reactions.Pools(x, c.map).ToList()) t.Destroy();
                    if (c.hold || c.shape) Field(c, y, Ticks(1f, c.power));
                    Puff(c, new Color(1f, 0.7f, 0.4f, 0.5f));
                    break;
                case SecondKind.Boil:
                    foreach (Pawn p in c.foes) { if (Wet(p) || c.beam || c.lift) Burn(p, (c.beam ? 6f : 3f) * c.power, by); }
                    foreach (Pawn p in c.own) if (c.restore) { foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.Hypothermia).ToList()) p.health.RemoveHediff(h); }
                    Field(c, y, c.hold ? Ticks(1f, c.power) : 900);
                    Puff(c, new Color(1f, 1f, 1f, 0.5f));
                    break;
                case SecondKind.Geyser:
                    {
                        float near = c.map.listerThings.ThingsOfDef(ThingDefOf.SteamGeyser).Any(g => g.Position.DistanceTo(c.at) < 10f) ? 1.3f : 1f;
                        int n = c.hold ? 8 : c.beam ? Mathf.Max(3, c.cells.Count / 3) : c.growth ? 6 : 3;
                        var spots = c.beam ? c.cells.Where((x, i) => i % 3 == 0).Take(n).ToList() : c.cells.InRandomOrder().Take(n).ToList();
                        int i0 = 0;
                        foreach (IntVec3 s in spots)
                        {
                            IntVec3 at = s; float pw = c.power * near;
                            F(c.map)?.Later(30 + 40 * i0++, () => Erupt(c.map, at, pw, by, c.restore));
                        }
                        break;
                    }
                case SecondKind.Typhoon:
                    F(c.map)?.AddMoving(y, c.at, (c.at - by.Position).ToVector3().normalized, c.hold ? 0f : 1f, Ticks(0.12f, c.power), by, c.power);
                    break;
                case SecondKind.Lava:
                    {
                        var where = c.beam ? FireMagic.Beam(by.Position, c.at + (c.at - by.Position), c.map, c.breadth).Where(x => x.InBounds(c.map) && x != by.Position).ToList()
                                  : c.shape ? c.cells : FireMagic.Point(c.aura ? by.Position : c.at, c.map, c.radius * 2f).ToList();   // round the mage too (Own Blast; Fix190)
                        int life = c.hold ? 5000 : 3000;
                        foreach (IntVec3 x in where) if (Rand.Chance(c.aura ? 0.4f : 0.7f)) MagmaPool.Pour(x, c.map, by, c.power * 1.3f, life, c.shape, 2500, true);   // it sets into basalt only when shaped
                        // the magma the pair poured turns to lava too - the whole place is lava, not magma with a rim
                        foreach (MagmaPool mp in c.cells.SelectMany(x => x.GetThingList(c.map).OfType<MagmaPool>()).ToList()) mp.MakeLava(life);
                        foreach (Pawn p in c.foes) MagicFx.Give(p, H("Avir_LavaMired"), 300);
                        break;
                    }
                case SecondKind.Volcano:
                    {
                        if (c.lift)
                        {
                            foreach (Pawn p in c.foes) MagicFx.Give(p, H("Avir_Pillared"), Mathf.RoundToInt(600f * c.power));
                            break;
                        }
                        if (c.shape)
                        {
                            ThingDef basalt = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Basalt");
                            foreach (IntVec3 x in c.cells) if (basalt != null && ArcaneWorks.Clear(c.map, x)) GenSpawn.Spawn(basalt, x, c.map);
                            break;
                        }
                        int n = c.hold ? 6 : c.growth ? 8 : 5;
                        var spots = c.beam ? c.cells.Where((x, i) => i % 2 == 0).ToList() : c.cells.InRandomOrder().Take(n).ToList();
                        int i0 = 0;
                        foreach (IntVec3 s in spots)
                        {
                            IntVec3 at = s; float pw = c.power;
                            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), c.map, 1f, MagicFx.EarthDust);
                            F(c.map)?.Later(60 + 25 * i0++, () => Burst(c.map, at, pw, by));
                        }
                        foreach (Pawn p in c.own) if (c.aura) MagicFx.Give(p, EarthAirDefOf.Avir_Rooted, 300);
                        // the ground stays torn (Fix185): a rift at the heart pours magma a while, and some bursts leave their own
                        if (c.release && !c.beam)
                        {
                            IntVec3 heart = c.at; float pw0 = c.power;
                            F(c.map)?.Later(90, () => Rift(c.map, heart, pw0, by, Mathf.Clamp(Mathf.RoundToInt(10f * pw0), 5, 30), 1.6f));
                        }
                        int r0 = 0;
                        foreach (IntVec3 s in spots)
                            if (s != c.at && Rand.Chance(0.3f) && r0++ < 2) { IntVec3 at = s; float pw = c.power; F(c.map)?.Later(150 + 25 * r0, () => Rift(c.map, at, pw, by, Mathf.Clamp(Mathf.RoundToInt(5f * pw), 3, 15), 1f)); }
                        break;
                    }
                case SecondKind.Obsidian:
                    foreach (IntVec3 x in c.cells) { if (Rand.Chance(0.5f)) ShardGround.Scatter(x, c.map, ComboDefOf.Avir_Obsidian); if (Rand.Chance(0.2f)) Gas_ScaldingSteam.Release(x, c.map, 1); }
                    foreach (Pawn p in c.foes) p.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f * c.power, 0.4f, -1f, by));
                    break;
                case SecondKind.Ash:
                    foreach (Pawn p in c.foes) if (!Sealed(p)) { MagicFx.Give(p, H("Avir_Ashen"), Ticks(0.5f, c.power)); HealthUtility.AdjustSeverity(p, HediffDefOf.ToxicBuildup, 0.03f * c.power); }
                    {
                        // ash is a plain fertiliser, not arcane soil: a small bonus to the ground's own fertility (the author)
                        var fert = MapComponent_LifeFertility.Of(c.map);
                        foreach (IntVec3 x in c.cells) { TerrainDef t = c.map.terrainGrid.TerrainAt(x); if (t.fertility > 0f && !t.IsWater) fert?.Add(x, AshBonus, AshDays); }
                    }
                    Field(c, y, Ticks(0.5f, c.power));
                    break;
                case SecondKind.Deluge:
                    Field(c, y, Ticks(c.hold ? 4f : 2f, c.power));
                    break;
                case SecondKind.Hurricane:
                    // a lift alone: the bodies are up in it already (the lift itself is stronger under a hurricane); with Release
                    // too the wind flings them as well - the throw waits till they come down
                    if (!c.lift || c.release) foreach (Pawn p in c.foes) SpellHurl.Throw(p, c.aura ? by.Position : c.at, Mathf.RoundToInt(4f * c.power), c.map, by, c.power);
                    foreach (Building b in c.cells.Select(x => x.GetEdifice(c.map)).Where(b => b != null && b.def.useHitPoints && b.MaxHitPoints <= 300).Distinct().ToList())
                        b.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 15f * c.power, 0f, -1f, by));
                    Field(c, y, c.hold || c.aura || c.shape ? Ticks(0.5f, c.power) : 600);
                    // unstable (the author, Fix166): it leaves whirls behind that wander and spin whoever they catch round
                    {
                        int whirls = c.hold || c.aura ? 1 : c.growth ? 3 : 2;
                        for (int i = 0; i < whirls; i++)
                        {
                            IntVec3 w0 = c.cells.Count > 0 ? c.cells.RandomElement() : c.at;
                            F(c.map)?.AddWhirl(w0, by, c.power, c.hold ? 1800 : 900);
                        }
                        if (!AtmosphereBridge.Throws)
                        {
                            // our own wind (Fix169): a blast out from the middle - things fly, people are thrown, light walls break
                            float rad = c.cells.Count > 0 ? c.cells.Max(x => x.DistanceTo(c.aura ? by.Position : c.at)) + 1.5f : 4f;
                            MapComponent_MagicWind.Blow(c.map, MapComponent_MagicWind.Shape.Out, (c.aura ? by.Position : c.at).ToVector3Shifted(), rad, MapComponent_MagicWind.HurricaneStrength(c.power, c.map), 70, by, default, true, MapComponent_MagicWind.HurricaneMax);   // a hurricane: three times the wind of plain air (the author, Fix174)
                        }
                        if (AtmosphereBridge.On)
                        {
                            // a blast of real wind out from the middle, pushed again and again for a second and a half so it builds
                            // up past that mod's throwing speed (it takes ~12 m/s a push)
                            var cs = c.cells.ToList(); IntVec3 mid = c.aura ? by.Position : c.at; Map m0 = c.map; float pw = c.power;
                            for (int k2 = 0; k2 < 6; k2++)
                                F(m0)?.Later(1 + 15 * k2, () => { foreach (IntVec3 x in cs) { Vector3 o = x.ToVector3Shifted() - mid.ToVector3Shifted(); if (o.sqrMagnitude > 0.1f) AtmosphereBridge.Wind(m0, x, new Vector2(o.x, o.z).normalized * Mathf.Min(AtmosphereBridge.MaxPush, MapComponent_MagicWind.HurricaneStrength(pw, m0) / 6f)); } });
                        }
                    }
                    break;
                case SecondKind.BallLightning:
                    {
                        int n = c.beam ? 1 : 2 + (c.growth ? 2 : 0);
                        if (c.beam)
                        {
                            // the conductor: one globe rolls along the line and strikes each foe it passes, each a little weaker
                            int i0 = 0;
                            foreach (Pawn p in c.foes.OrderBy(p => p.Position.DistanceTo(by.Position)))
                            { Pawn q = p; float pw = c.power * Mathf.Pow(0.85f, i0); F(c.map)?.Later(20 + 20 * i0++, () => Zap(q, pw, by)); }
                        }
                        else if (c.hold || c.aura || c.shape) Field(c, y, Ticks(0.3f, c.power));
                        else
                        {
                            List<Pawn> targets = c.map.mapPawns.AllPawnsSpawned.Where(p => MagicFx.Foe(p, by) && p.Position.DistanceTo(c.at) < 10f).OrderBy(p => p.Position.DistanceTo(c.at)).Take(n).ToList();
                            int i0 = 0;
                            foreach (Pawn p in targets) { Pawn q = p; float pw = c.power; F(c.map)?.Later(60 + 45 * i0++, () => Zap(q, pw, by)); }
                        }
                        break;
                    }
                case SecondKind.Mudflow:
                    foreach (Pawn p in c.foes)
                    {
                        IntVec3 from = c.beam ? by.Position : c.aura ? by.Position : c.at;
                        SpellHurl.Throw(p, from, Mathf.RoundToInt((c.beam ? 6f : 3f) * c.power), c.map, by, c.power, 90);
                        if (c.hold || Rand.Chance(0.5f)) MagicFx.Bury(p, c.power);
                    }
                    foreach (IntVec3 x in c.cells) if (Rand.Chance(0.25f)) MirePatch.Spread(x, c.map, by, c.power, 1500);
                    foreach (Pawn p in c.own) if (c.restore) { Hediff b = H("Avir_Buried") != null ? p.health.hediffSet.GetFirstHediffOfDef(H("Avir_Buried")) : null; if (b != null) p.health.RemoveHediff(b); }
                    break;
                case SecondKind.Swelter:
                    foreach (Pawn p in c.foes) { if (p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Max(0f, p.needs.rest.CurLevel - 0.15f * c.power); Heat(p, 0.03f * c.power * (Sealed(p) ? 1.5f : 1f)); }
                    if (c.drain && by.needs?.rest != null) by.needs.rest.CurLevel = Mathf.Min(1f, by.needs.rest.CurLevel + 0.05f * c.foes.Count);
                    if (c.hold || c.aura || c.shape) Field(c, y, Ticks(c.aura ? 0.5f : 2f, c.power));
                    break;
                case SecondKind.Thermal:
                    foreach (Pawn p in c.own) MagicFx.Give(p, H("Avir_Updraft"), Ticks(0.5f, c.power));
                    Field(c, y, Ticks(c.hold ? 3f : 0.5f, c.power));
                    break;
                case SecondKind.Rainbow:
                    foreach (Pawn p in c.foes) MagicFx.Give(p, ComboDefOf.Avir_Dazzled, Mathf.RoundToInt(900f * c.power * SpellBody.Of(p).eyeDazzle));
                    {
                        ThoughtDef seen = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_SawRainbow");
                        foreach (Pawn p in c.map.mapPawns.FreeColonistsSpawned.Where(p => p.Position.DistanceTo(c.at) < 20f)) if (seen != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(seen);
                    }
                    Puff(c, new Color(0.9f, 0.6f, 1f, 0.5f));
                    break;
                case SecondKind.DryWind:
                    foreach (Pawn p in c.foes.Concat(c.own).Distinct()) Dry(p);
                    foreach (Pawn p in c.foes) if (!Sealed(p)) MagicFx.Give(p, H("Avir_Parched"), Ticks(0.5f, c.power));
                    foreach (IntVec3 x in c.cells) foreach (Thing t in Reactions.Pools(x, c.map).ToList()) t.Destroy();
                    break;
                case SecondKind.Quag:
                    foreach (IntVec3 x in c.cells) if (Rand.Chance(0.8f)) MirePatch.Spread(x, c.map, by, c.power * 1.3f, c.hold ? 6000 : 3500);
                    foreach (Pawn p in c.foes) { MagicFx.Give(p, ComboDefOf.Avir_Mired, 600); if (Sealed(p)) MagicFx.Give(p, ComboDefOf.Avir_SinkingSealed, 600); }
                    break;
                case SecondKind.Silt:
                    foreach (Pawn p in c.foes)
                    {
                        if (p.RaceProps.IsMechanoid) MagicFx.Stun(p, Mathf.RoundToInt(180f * c.power), by);
                        MagicFx.Give(p, H(c.hold ? "Avir_SiltBound" : "Avir_Silted"), Ticks(c.hold ? 0.25f : 1f, c.power));
                        // silt in the action: a ranged weapon up to industrial make jams (not a spacer's, not ultratech)
                        ThingWithComps gun = p.equipment?.Primary;
                        if (Patch_SiltJams.Jams(gun?.def)) MagicFx.Give(p, H("Avir_SiltJammed"), Ticks(0.5f, c.power));
                    }
                    foreach (Building b in c.cells.Select(x => x.GetEdifice(c.map)).Where(b => b is Building_Turret).Distinct().ToList())
                        b.TakeDamage(new DamageInfo(DamageDefOf.EMP, 20f * c.power, 0f, -1f, by));
                    Puff(c, new Color(0.45f, 0.4f, 0.3f, 0.6f));
                    break;
                case SecondKind.MarshGas:
                    foreach (IntVec3 x in c.cells) if (Rand.Chance(0.6f)) Gas_MarshGas.Release(x, c.map, 2, false);
                    foreach (Pawn p in c.foes) if (!Masked(p)) MagicFx.Give(p, H("Avir_MarshLungs"), Ticks(0.5f, c.power));
                    break;
                case SecondKind.MarshHaze:
                    Field(c, y, Ticks(c.hold ? 2f : 0.75f, c.power));
                    break;
                case SecondKind.Rockfall:
                    {
                        ThingDef chunk = Find.World.NaturalRockTypesIn(c.map.Tile).Select(rk => rk.building.mineableThing).FirstOrDefault(x => x != null);
                        foreach (Pawn p in c.foes) { Blunt(p, 8f * c.power, by); MagicFx.Fell(p, p.Position, 0, c.map, by, c.power, 120); }
                        foreach (IntVec3 x in c.cells.InRandomOrder().Take(c.beam ? Mathf.Max(1, Mathf.RoundToInt(c.cells.Count * 0.15f)) : 1))   // cut down (the author, Fix164) if (chunk != null && ArcaneWorks.Clear(c.map, x)) GenSpawn.Spawn(chunk, x, c.map);
                        Puff(c, MagicFx.EarthDust);
                        break;
                    }
                case SecondKind.Emery:
                    foreach (Pawn p in c.foes)
                    {
                        foreach (Apparel a in p.apparel?.WornApparel.ToList() ?? new List<Apparel>())
                            a.HitPoints = Mathf.Max(1, a.HitPoints - Mathf.RoundToInt(a.MaxHitPoints * 0.12f * c.power));
                        MagicFx.Give(p, H("Avir_Abraded"), Ticks(0.5f, c.power));
                    }
                    if (c.aura || c.enchant) foreach (Pawn p in c.own) MagicFx.Give(p, H("Avir_Honed"), Ticks(1f, c.power));
                    if (c.hold || c.shape || c.aura) Field(c, y, Ticks(0.3f, c.power));
                    Puff(c, new Color(0.8f, 0.7f, 0.5f, 0.6f));
                    break;
                case SecondKind.BurningSand:
                    foreach (Pawn p in c.foes) MagicFx.Give(p, H("Avir_BurningSand"), Ticks(0.3f, c.power));
                    Puff(c, new Color(1f, 0.55f, 0.25f, 0.6f));
                    break;
                case SecondKind.Clay:
                    foreach (Pawn p in c.foes)
                    {
                        Hediff h = p.health.hediffSet.GetFirstHediffOfDef(H("Avir_Clayed")) ?? (H("Avir_Clayed") != null ? p.health.AddHediff(H("Avir_Clayed")) : null);
                        if (h != null) h.Severity = Mathf.Min(1f, h.Severity + (c.hold ? 0.5f : 0.3f) * c.power);
                    }
                    Puff(c, new Color(0.7f, 0.45f, 0.3f, 0.6f));
                    break;
            }
        }

        private static void Erupt(Map map, IntVec3 at, float power, Pawn by, bool spring)
        {
            if (map == null || !at.InBounds(map)) return;
            Gas_ScaldingSteam.Release(at, map, 2);
            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 2f, new Color(0.9f, 0.9f, 0.9f, 0.7f));
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(at, map, 1.5f, true).OfType<Pawn>().ToList())
            {
                if (spring && by != null && !p.HostileTo(by)) { SpellHeal.Mend(p, 3f * power); continue; }
                if (by != null && !MagicFx.Foe(p, by)) continue;
                Burn(p, 4f * power * (Sealed(p) ? 0.7f : SpellBody.Of(p).metalArmour ? 1f : 1.4f), by);
                MagicFx.Fell(p, at, 1, map, by, power, 90);
            }
        }

        /// <summary>A rift (Fix185): once a second it wells up - magma on it and beside it, a glow, sparks - for some seconds.</summary>
        private static void Rift(Map map, IntVec3 at, float power, Pawn by, int seconds, float size)
        {
            if (map == null || !at.InBounds(map) || seconds <= 0 || Find.Maps.IndexOf(map) < 0) return;
            MapComponent_SpellSprites.Put(map, "Magma/Fissure", at.ToVector3Shifted(), 1.6f * size, 75, -1f, 0f, 2.5f);
            FleckMaker.ThrowFireGlow(at.ToVector3Shifted(), map, 1.2f * size);
            if (Rand.Chance(0.5f)) FleckMaker.ThrowMicroSparks(at.ToVector3Shifted(), map);
            MagmaPool.Pour(at, map, by, power, 1500, true);
            IntVec3 side = at + GenAdj.AdjacentCells.RandomElement();
            if (Rand.Chance(0.6f) && side.InBounds(map)) MagmaPool.Pour(side, map, by, power, 1200, true);
            foreach (Pawn p in at.GetThingList(map).OfType<Pawn>().ToList())
                if (by == null || MagicFx.Foe(p, by)) p.TakeDamage(new DamageInfo(DamageDefOf.Flame, 3f * power, 0f, -1f, by));
            F(map)?.Later(60, () => Rift(map, at, power, by, seconds - 1, size));
        }

        /// <summary>The typhoon's own blow (the author, Fix190): boiling rain - not steam's scald. It soaks first, and on the
        /// soaked it bites half again as hard; it falls from above (head and shoulders first); a sealed suit keeps most of
        /// it off. Now and then a jet of the whirl strikes like a fist and throws.</summary>
        public static void BoilingRain(Pawn p, float power, Pawn by, Map map, IntVec3 centre)
        {
            if (p == null || p.Dead) return;
            bool wet = p.health.hediffSet.HasHediff(WaterDefOf.Avir_Soaked);
            WaterMagic.Soak(p, power);
            DamageDef rain = DefDatabase<DamageDef>.GetNamedSilentFail("Avir_BoilingRain") ?? DamageDefOf.Burn;
            BodyPartRecord top = p.health.hediffSet.GetNotMissingParts().Where(b => b.height == BodyPartHeight.Top && b.depth == BodyPartDepth.Outside).RandomElementWithFallback();
            p.TakeDamage(new DamageInfo(rain, 1.6f * power * (wet ? 1.5f : 1f) * (Sealed(p) ? 0.4f : 1f), 0f, -1f, by, top));
            if (!p.Dead && Rand.Chance(0.25f))
            {
                p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 3f * power, 0f, -1f, by));
                if (!p.Dead && !SpellWards.HoldsWind(p)) SpellHurl.Throw(p, centre, 2, map, by, power);
            }
        }

        private static void Burst(Map map, IntVec3 at, float power, Pawn by)
        {
            if (map == null || !at.InBounds(map)) return;
            FleckMaker.ThrowFireGlow(at.ToVector3Shifted(), map, 1.5f);
            MagmaPool.Pour(at, map, by, power, 1500, true);
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(at, map, 1.5f, true).OfType<Pawn>().ToList())
            {
                if (by != null && !MagicFx.Foe(p, by)) continue;
                // from below: the legs and the body's underside - and the shock through armour (earth's 40%)
                BodyPartRecord leg = p.health.hediffSet.GetNotMissingParts().Where(x => x.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore)).RandomElementWithFallback();
                p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 9f * power, 0.6f, -1f, by, leg));
                MagicFx.Fell(p, at, 1, map, by, power, 120);
            }
        }

        private static void Zap(Pawn p, float power, Pawn by)
        {
            if (p == null || p.Dead || !p.Spawned) return;
            var b = SpellBody.Of(p);
            if (by != null && by.Spawned) MapComponent_SpellSprites.Bolt(p.Map, by.DrawPos, p.DrawPos, 14);
            FleckMaker.ThrowLightningGlow(p.DrawPos, p.Map, 1.5f);
            if (p.RaceProps.IsMechanoid) { p.TakeDamage(new DamageInfo(DamageDefOf.EMP, 12f * power, 0f, -1f, by)); return; }
            float metal = b.metalArmour ? 1.5f : 1f, bio = b.bionicLimb ? 1.25f : 1f;
            Burn(p, 6f * power * metal * bio, by);
            if (p.Spawned) Reactions.Conduct(p.Position, p.Map, by, power * 0.6f);   // the globe's charge runs on (Fix182)
            MagicFx.Stun(p, 60, by);
            if (b.bionicLimb && Rand.Chance(0.5f)) MagicFx.Give(p, H("Avir_Glitched"), 300);
        }
    }

    /// <summary>What second-order meetings leave on the ground for a while, and what they do later.</summary>
    public class SecondFields : MapComponent
    {
        public class Fld { public SecondKind y; public HashSet<IntVec3> cells; public int until; public Pawn owner; public float power; public Vector3 dir; public float speed; public Vector3 center; public Vector3 mid; public float rad; }
        public readonly List<Fld> fields = new List<Fld>();
        private readonly List<KeyValuePair<int, Action>> later = new List<KeyValuePair<int, Action>>();
        public SecondFields(Map map) : base(map) { }

        /// <summary>A field laid where the same caster's field of the same kind already lies is not a second one:
        /// it renews and widens the first (an aura striking again and again keeps one field, and one sheet on the ground).</summary>
        public void Add(SecondKind y, List<IntVec3> cells, int ticks, Pawn owner, float power)
        {
            int until = Find.TickManager.TicksGame + ticks;
            Fld same = fields.FirstOrDefault(f => f.y == y && f.owner == owner && f.speed <= 0f && cells.Any(f.cells.Contains));
            if (same != null)
            {
                same.cells.UnionWith(cells); same.until = Mathf.Max(same.until, until); same.power = Mathf.Max(same.power, power); same.rad = 0f;
                return;
            }
            fields.Add(new Fld { y = y, cells = new HashSet<IntVec3>(cells), until = until, owner = owner, power = power });
        }

        public void AddMoving(SecondKind y, IntVec3 at, Vector3 dir, float speed, int ticks, Pawn owner, float power) =>
            fields.Add(new Fld { y = y, center = at.ToVector3Shifted(), dir = dir, speed = speed, cells = new HashSet<IntVec3>(GenRadial.RadialCellsAround(at, 2.5f, true)), until = Find.TickManager.TicksGame + ticks, owner = owner, power = power });

        /// <summary>A whirl left by a hurricane: it wanders, and spins whoever it catches round its middle.</summary>
        public void AddWhirl(IntVec3 at, Pawn owner, float power, int ticks)
        {
            float a = Rand.Range(0f, 360f);
            fields.Add(new Fld { y = SecondKind.Hurricane, center = at.ToVector3Shifted(), dir = Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward, speed = 0.7f, cells = new HashSet<IntVec3>(GenRadial.RadialCellsAround(at, 2f, true).Where(x => x.InBounds(map))), until = Find.TickManager.TicksGame + ticks, owner = owner, power = power });
        }

        /// <summary>A whirl wanders (shared with the sand spout, Fix184): it turns this way and that, and is turned back by
        /// walls and the map's edge. lure - a place it leans towards, if any.</summary>
        public static void Wander(Map map, ref Vector3 center, ref Vector3 dir, float speed, IntVec3 lure = default)
        {
            dir = Quaternion.AngleAxis(Rand.Range(-50f, 50f), Vector3.up) * dir;
            if (lure.IsValid && lure != default(IntVec3)) { Vector3 to = lure.ToVector3Shifted() - center; to.y = 0f; if (to.sqrMagnitude > 0.5f) dir = Vector3.Slerp(dir, to.normalized, 0.5f).normalized; }
            Vector3 next = center + dir * speed;
            IntVec3 nc = next.ToIntVec3();
            if (!nc.InBounds(map) || nc.Impassable(map)) { dir = -dir; next = center + dir * speed; nc = next.ToIntVec3(); }
            if (nc.InBounds(map) && !nc.Impassable(map)) center = next;
        }

        /// <summary>Spun round: thrown a little along the whirl's turning, not out of it.</summary>
        private void Spin(Fld f, Pawn p)
        {
            Vector3 off = p.DrawPos - f.center; off.y = 0f;
            Vector3 tan = off.sqrMagnitude < 0.05f ? Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up) * Vector3.forward : new Vector3(-off.z, 0f, off.x).normalized;
            IntVec3 from = (p.DrawPos - tan * 1.5f + off.normalized * 0.4f).ToIntVec3();
            SpellHurl.Throw(p, from, 2, map, f.owner, f.power * 0.5f);
        }

        // boiling puddles left by a typhoon (Fix191)
        private readonly Dictionary<IntVec3, int> boiling = new Dictionary<IntVec3, int>();
        private readonly Dictionary<IntVec3, Pawn> boilBy = new Dictionary<IntVec3, Pawn>();
        private readonly Dictionary<IntVec3, float> boilPower = new Dictionary<IntVec3, float>();
        private void Boil(int now)
        {
            if (boiling.Count == 0) return;
            DamageDef rain = DefDatabase<DamageDef>.GetNamedSilentFail("Avir_BoilingRain") ?? DamageDefOf.Burn;
            foreach (IntVec3 c in boiling.Keys.ToList())
            {
                if (boiling[c] <= now || !Reactions.Pools(c, map).Any()) { boiling.Remove(c); boilBy.Remove(c); boilPower.Remove(c); continue; }
                Vector3 v = c.ToVector3Shifted();
                if (Rand.Chance(0.5f)) MapComponent_SpellSprites.PutTintedGrowing(map, "Wave/Ripple", new Color(0.8f, 0.95f, 1f), v + new Vector3(Rand.Range(-0.25f, 0.25f), 0f, Rand.Range(-0.25f, 0.25f)), 0.5f, 40, 1.4f);
                if (Rand.Chance(0.35f)) FleckMaker.ThrowDustPuffThick(v, map, 0.7f, new Color(1f, 1f, 1f, 0.35f));
                if (Rand.Chance(0.12f)) Gas_ScaldingSteam.Release(c, map, 1);
                boilBy.TryGetValue(c, out Pawn by); float pw = boilPower.TryGetValue(c, out float q) ? q : 1f;
                foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList())
                {
                    if (p.Dead) continue;
                    if (by != null && !MagicFx.Foe(p, by)) continue;
                    BodyPartRecord foot = p.health.hediffSet.GetNotMissingParts().Where(b => b.height == BodyPartHeight.Bottom && b.depth == BodyPartDepth.Outside).RandomElementWithFallback();
                    p.TakeDamage(new DamageInfo(rain, 1.2f * pw * (SecondOrder.Sealed(p) ? 0.4f : 1f), 0f, -1f, by, foot));
                }
            }
        }

        public void Later(int ticks, Action a) => later.Add(new KeyValuePair<int, Action>(Find.TickManager.TicksGame + ticks, a));

        public static Fld At(Map m, IntVec3 c, SecondKind y) => m?.GetComponent<SecondFields>()?.fields.FirstOrDefault(f => f.y == y && f.cells.Contains(c));

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (later.Count > 0)
                foreach (var kv in later.Where(kv => kv.Key <= now).ToList())
                {
                    later.Remove(kv);
                    try { kv.Value(); } catch (Exception e) { Log.ErrorOnce("[Avir] second-order later: " + e, 0x5EC1); }
                }
            if (fields.Count == 0 && boiling.Count == 0) return;
            fields.RemoveAll(f => f.until <= now);
            // the whirls blow real wind round themselves in Atmosphere Overhaul's air, four times a second, so it builds up
            if (now % 15 == 0 && AtmosphereBridge.Throws)
                foreach (Fld w in fields.Where(x => x.y == SecondKind.Hurricane && x.speed > 0f))
                    foreach (IntVec3 x in w.cells)
                    {
                        Vector3 o = x.ToVector3Shifted() - w.center; o.y = 0f;
                        Vector3 v = o.sqrMagnitude < 0.05f ? Vector3.zero : (new Vector3(-o.z, 0f, o.x).normalized * 0.85f - o.normalized * 0.3f);   // round, and a little inward
                        AtmosphereBridge.Wind(map, x, new Vector2(v.x, v.z) * Mathf.Min(AtmosphereBridge.MaxPush, MapComponent_MagicWind.HurricaneStrength(w.power, map, true) / 5f));
                    }
            if (now % 60 != 0) return;
            Boil(now);
            foreach (Fld f in fields)
            {
                if (f.y == SecondKind.Typhoon && f.speed > 0f)
                {
                    f.center += f.dir * f.speed;
                    IntVec3 c0 = f.center.ToIntVec3();
                    f.cells = new HashSet<IntVec3>(GenRadial.RadialCellsAround(c0, 2.5f, true).Where(x => x.InBounds(map)));
                }
                if (f.y == SecondKind.Typhoon)
                {
                    // its rain lies in puddles, and they boil a while: steam over them, a scald to the feet that wade (Fix191)
                    foreach (IntVec3 x in f.cells.InRandomOrder().Take(3))
                        if (x.Standable(map) && !Reactions.Wet(x, map)) { WaterMagic.Puddle(x, map); boiling[x] = Find.TickManager.TicksGame + 1500; boilBy[x] = f.owner; boilPower[x] = f.power; }
                        else if (Reactions.Pools(x, map).Any()) { boiling[x] = Find.TickManager.TicksGame + 1500; boilBy[x] = f.owner; boilPower[x] = f.power; }
                }
                if (f.y == SecondKind.Typhoon)
                    foreach (IntVec3 x in f.cells.InRandomOrder().Take(6))
                    {
                        FleckMaker.Static(x, map, FleckDefOf.WaterSplash, Rand.Range(0.5f, 0.9f));
                        if (Rand.Chance(0.25f)) FleckMaker.ThrowDustPuffThick(x.ToVector3Shifted(), map, 0.6f, new Color(1f, 1f, 1f, 0.25f));   // it boils where it falls
                    }
                if (f.y == SecondKind.Hurricane && f.speed > 0f)
                {
                    // a whirl wanders: it turns this way and that, and is turned back by walls and the map's edge
                    Wander(map, ref f.center, ref f.dir, f.speed);
                    f.cells = new HashSet<IntVec3>(GenRadial.RadialCellsAround(f.center.ToIntVec3(), 2f, true).Where(x => x.InBounds(map)));

                    foreach (IntVec3 x in f.cells.InRandomOrder().Take(3)) FleckMaker.ThrowDustPuffThick(x.ToVector3Shifted(), map, 1.1f, new Color(0.8f, 0.86f, 0.92f, 0.6f));
                    if (!AtmosphereBridge.Throws) MapComponent_MagicWind.Blow(map, MapComponent_MagicWind.Shape.Round, f.center, 2.6f, MapComponent_MagicWind.HurricaneStrength(f.power, map, true), 64, f.owner, default, true, MapComponent_MagicWind.HurricaneMax);
                }
                foreach (IntVec3 c in f.cells)
                {
                    if (!c.InBounds(map)) continue;
                    List<Thing> here = c.GetThingList(map);
                    if (Rand.Chance(0.12f)) Visual(f, c);
                    foreach (Pawn p in here.OfType<Pawn>().ToList())
                    {
                        bool foe = f.owner == null || MagicFx.Foe(p, f.owner);
                        switch (f.y)
                        {
                            case SecondKind.Hurricane: break;   // a whirl's spinning is its wind's (MagicWind, or Atmosphere Overhaul's)   // the whirl spares no one but its maker
                            case SecondKind.Inferno: if (foe) HealthUtility.AdjustSeverity(p, HediffDefOf.Heatstroke, 0.02f * f.power * (SecondOrder.Sealed(p) ? 2f : 1f)); break;
                            case SecondKind.Boil: if (foe && (p.health.hediffSet.HasHediff(WaterDefOf.Avir_Soaked) || Reactions.Wet(c, map))) p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 1.5f * f.power * (SecondOrder.Sealed(p) ? 0.4f : 1.4f), 0f, -1f, f.owner)); break;
                            case SecondKind.Typhoon: if (foe) SecondOrder.BoilingRain(p, f.power, f.owner, map, f.center.ToIntVec3()); break;
                            case SecondKind.Ash: if (foe && !SecondOrder.Sealed(p)) MagicFx.Give(p, SecondOrder.H("Avir_Ashen"), 300); break;
                            case SecondKind.Deluge: WaterMagic.Soak(p, f.power); break;
                            case SecondKind.Swelter: if (foe && p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Max(0f, p.needs.rest.CurLevel - 0.01f * f.power); else if (!foe) MagicFx.Give(p, SecondOrder.H("Avir_Updraft"), 120); break;
                            case SecondKind.MarshHaze: if (foe && !SecondOrder.Masked(p)) HealthUtility.AdjustSeverity(p, HediffDefOf.ToxicBuildup, 0.006f * f.power); break;
                            case SecondKind.Emery: if (foe) foreach (Apparel a in p.apparel?.WornApparel.ToList() ?? new List<Apparel>()) a.HitPoints = Mathf.Max(1, a.HitPoints - 1); else MagicFx.Give(p, SecondOrder.H("Avir_Honed"), 300); break;
                            case SecondKind.BallLightning: if (foe && Rand.Chance(0.15f)) { p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 4f * f.power * (SpellBody.Of(p).metalArmour ? 1.5f : 1f), 0f, -1f, f.owner)); FleckMaker.ThrowLightningGlow(p.DrawPos, map, 1f); } break;
                        }
                    }
                    if (f.y == SecondKind.Deluge) foreach (Thing t in here.Where(t => t is Fire).ToList()) if (!t.Destroyed) t.Destroy();
                }
            }
        }

        // a field of the bog lies on the ground as a slow-turning sheet (Field_<Kind>.png), fading as it ends
        private static readonly Dictionary<SecondKind, Material> fieldMats = new Dictionary<SecondKind, Material>();
        private static Material FieldMat(SecondKind y)
        {
            if (fieldMats.TryGetValue(y, out Material m)) return m;
            string path = "Things/Mote/Avir/Magic/Field_" + y;
            m = ContentFinder<Texture2D>.Get(path, false) != null ? MaterialPool.MatFrom(path, ShaderDatabase.Transparent, Color.white) : null;
            fieldMats[y] = m;
            return m;
        }

        // a whirl that walks is drawn as its own whirl (the author's Aura_<Kind>): the hurricane's white, the typhoon's sea-teal
        // (Fix190: the typhoon had no picture of its own - grey puffs, and was taken for steam)
        private static readonly Dictionary<SecondKind, Material> auraMats = new Dictionary<SecondKind, Material>();
        private static Material AuraMat(SecondKind y)
        {
            if (auraMats.TryGetValue(y, out Material m)) return m;
            string path = "Things/Mote/Avir/Magic/Aura_" + y;
            m = ContentFinder<Texture2D>.Get(path, false) != null ? MaterialPool.MatFrom(path, ShaderDatabase.Transparent, Color.white) : null;
            auraMats[y] = m;
            return m;
        }
        private static bool Whirls(Fld f) => f.y == SecondKind.Hurricane && f.speed > 0f || f.y == SecondKind.Typhoon;

        // a typhoon's own rain (Fix190): drops falling under it, drawn by code - each its own place and pace, looping
        private static Material rain;
        private void Rain(Fld f, float t, int now)
        {
            if (rain == null) rain = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Wind/Rain", ShaderDatabase.MoteGlow, Color.white);
            var block = new MaterialPropertyBlock();
            float fade = Mathf.Clamp01((f.until - now) / 300f);
            int drops = f.y == SecondKind.Deluge ? Mathf.Clamp(Mathf.RoundToInt(f.rad * f.rad * 4f), 30, 160) : 46;
            for (int i = 0; i < drops; i++)
            {
                float h1 = Mathf.Repeat(i * 0.6180339f, 1f), h2 = Mathf.Repeat(i * 0.3819660f + 0.17f, 1f), h3 = Mathf.Repeat(i * 0.7548776f, 1f);
                float ang = h1 * 6.2831853f, rr = Mathf.Sqrt(h2) * f.rad * 0.9f;
                float ph = Mathf.Repeat(t * (1.3f + 0.7f * h3) + h3, 1f);
                Vector3 p = f.mid + new Vector3(Mathf.Cos(ang) * rr - 0.25f * ph, 0f, Mathf.Sin(ang) * rr + 1.1f * (1f - ph) - 0.3f);
                p.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                block.SetColor(ShaderPropertyIDs.Color, new Color(0.75f, 0.9f, 1f, 0.55f * Mathf.Sin(ph * Mathf.PI) * fade));
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p, Quaternion.AngleAxis(12f, Vector3.up), new Vector3(0.07f, 1f, 0.5f)), rain, 0, null, 0, block);
            }
        }

        public override void MapComponentUpdate()
        {
            if (fields.Count == 0 || Find.CurrentMap != map) return;
            float t = Time.realtimeSinceStartup;
            int now = Find.TickManager.TicksGame;
            foreach (Fld f in fields)
            {
                if (f.cells == null || f.cells.Count == 0) continue;
                // a hurricane shows where it rages (Fix185): its edge, as long as it lasts
                if (f.y == SecondKind.Hurricane && f.speed <= 0f) GenDraw.DrawFieldEdges(f.cells.ToList(), new Color(0.55f, 0.75f, 1f, 0.6f));
                Material m = FieldMat(f.y) ?? (Whirls(f) ? AuraMat(f.y) : null);
                if (m == null && f.y != SecondKind.Deluge) continue;
                if (f.rad <= 0f || f.speed > 0f)
                {
                    Vector3 sum = Vector3.zero; foreach (IntVec3 c in f.cells) sum += c.ToVector3Shifted();
                    Vector3 mid = sum / f.cells.Count; float r = 0f;
                    foreach (IntVec3 c in f.cells) r = Mathf.Max(r, (c.ToVector3Shifted() - mid).MagnitudeHorizontal());
                    f.mid = mid; f.rad = r + 0.8f;
                }
                Vector3 pos = f.mid; pos.y = AltitudeLayer.MoteLow.AltitudeFor() - 0.01f;
                var block = new MaterialPropertyBlock();
                Color col = f.y == SecondKind.Typhoon ? new Color(0.45f, 0.85f, 0.95f) : Color.white; col.a = 0.55f * Mathf.Clamp01((f.until - now) / 600f) + 0.05f * Mathf.Sin(t * 1.3f);
                block.SetColor(ShaderPropertyIDs.Color, col);
                float size = f.rad * 2f;
                float turn = f.y == SecondKind.Hurricane && f.speed > 0f ? t * 140f : f.y == SecondKind.Typhoon ? t * 95f : t * 4f;   // a whirl spins fast
                if (m != null) Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.AngleAxis((turn + f.mid.x * 13f) % 360f, Vector3.up), new Vector3(size, 1f, size)), m, 0, null, 0, block);
                if (f.y == SecondKind.Typhoon || f.y == SecondKind.Deluge) Rain(f, t, now);
            }
        }

        /// <summary>Each field its own look (the author, Fix192: every meeting strikes with its own and looks its own - not
        /// the grey puffs of steam). Drawn from the sprites we have, tinted and moved; the art for them is in the prompts.</summary>
        private void Visual(Fld f, IntVec3 c)
        {
            Vector3 v = c.ToVector3Shifted() + new Vector3(Rand.Range(-0.3f, 0.3f), 0f, Rand.Range(-0.3f, 0.3f));
            Color col = SecondOrder.ColorOf(f.y);
            switch (f.y)
            {
                // steam's: the inferno roars - shimmering orange heat, sparks flung up; the boil bubbles white and rings
                case SecondKind.Inferno:
                    MapComponent_SpellSprites.PutTinted(map, "Haze/Shimmer", new Color(1f, 0.55f, 0.2f, 0.8f), v, 1.6f, 70, -1f, 1.2f);
                    FleckMaker.ThrowMicroSparks(v, map); FleckMaker.ThrowHeatGlow(c, map, 1f); break;
                case SecondKind.Boil:
                    MapComponent_SpellSprites.PutTinted(map, "Bog/Bubbles", new Color(0.92f, 0.97f, 1f, 0.9f), v, 0.8f, 60);
                    MapComponent_SpellSprites.PutTintedGrowing(map, "Wave/Ripple", new Color(0.9f, 0.97f, 1f), v, 0.7f, 45, 1.5f);
                    if (Rand.Chance(0.4f)) FleckMaker.ThrowDustPuffThick(v, map, 0.7f, new Color(1f, 1f, 1f, 0.3f)); break;
                case SecondKind.Geyser: case SecondKind.Typhoon:
                    FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 0.6f); break;
                // magma's: ash falls in flakes, dark and slow
                case SecondKind.Ash:
                    MapComponent_SpellSprites.PutTinted(map, "Ash/Ash_" + "ABC"[Rand.Range(0, 3)], new Color(0.35f, 0.33f, 0.32f, 0.85f), v, 0.9f, 110, Rand.Range(0f, 360f), 0.6f);
                    if (Rand.Chance(0.3f)) FleckMaker.ThrowSmoke(v, map, 1f); break;
                case SecondKind.Lava: case SecondKind.Volcano:
                    FleckMaker.ThrowFireGlow(v, map, 0.8f); if (Rand.Chance(0.3f)) FleckMaker.ThrowMicroSparks(v, map); break;
                case SecondKind.Obsidian:
                    MapComponent_SpellSprites.PutTinted(map, "Haze/Shimmer", new Color(0.55f, 0.4f, 0.75f, 0.6f), v, 0.9f, 50, -1f, 0.8f); break;
                // storm's: the deluge's own rain is drawn over it (Rain); here - splashes and rings
                case SecondKind.Deluge:
                    FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 0.7f);
                    MapComponent_SpellSprites.PutTintedGrowing(map, "Wave/Ripple", new Color(0.6f, 0.8f, 1f), v, 0.8f, 50, 1.6f); break;
                case SecondKind.Hurricane:
                    MapComponent_SpellSprites.PutTinted(map, "Wind/Streak", new Color(0.8f, 0.9f, 1f, 0.75f), v, 1.6f, 25, Rand.Range(0f, 360f), 0f, 4f); break;
                case SecondKind.Mudflow:
                    MapComponent_SpellSprites.PutTinted(map, "Bog/Splash", new Color(0.45f, 0.32f, 0.2f, 0.9f), v, 1.1f, 50);
                    MapComponent_SpellSprites.PutTintedGrowing(map, "Wave/Ripple", new Color(0.55f, 0.4f, 0.25f), v, 0.8f, 50, 1.5f); break;
                case SecondKind.BallLightning:
                    FleckMaker.ThrowLightningGlow(v, map, 0.8f); if (Rand.Chance(0.25f)) MapComponent_SpellSprites.Put(map, "Second/BallLightning", v, 1.4f, 40, -1f, 4f); break;
                // haze's: the swelter shimmers orange, the thermal lifts pale gold wisps, the dry wind drives ochre grit
                case SecondKind.Swelter:
                    MapComponent_SpellSprites.PutTinted(map, "Haze/Shimmer", new Color(1f, 0.7f, 0.35f, 0.7f), v, 1.8f, 90, -1f, 0.8f);
                    FleckMaker.ThrowHeatGlow(c, map, 0.8f); break;
                case SecondKind.Thermal:
                    MapComponent_SpellSprites.PutTintedGrowing(map, "Wind/Wisp", new Color(1f, 0.92f, 0.65f), v, 0.9f, 60, 1.8f); break;
                case SecondKind.DryWind:
                    MapComponent_SpellSprites.PutTinted(map, "Wind/Streak", new Color(0.85f, 0.68f, 0.42f, 0.8f), v, 1.4f, 30, 90f + Rand.Range(-15f, 15f), 0f, 4f);
                    if (Rand.Chance(0.4f)) FleckMaker.ThrowDustPuffThick(v, map, 0.8f, new Color(0.8f, 0.65f, 0.4f, 0.4f)); break;
                case SecondKind.Rainbow:
                    if (Rand.Chance(0.15f)) MapComponent_SpellSprites.Put(map, "Second/Rainbow", v, 3f, 120, Rand.Range(0f, 360f), 0f, 2f); else FleckMaker.ThrowDustPuffThick(v, map, 0.8f, new Color(0.95f, 0.9f, 1f, 0.35f)); break;
                // bog's: silt clouds the water grey-brown, marsh gas and haze have their own sheets
                case SecondKind.Silt: case SecondKind.Quag:
                    MapComponent_SpellSprites.PutTintedGrowing(map, "Wave/Ripple", new Color(0.55f, 0.5f, 0.38f), v, 0.9f, 60, 1.4f);
                    if (Rand.Chance(0.3f)) MapComponent_SpellSprites.PutTinted(map, "Bog/Bubbles", new Color(0.6f, 0.55f, 0.4f, 0.8f), v, 0.6f, 60); break;
                case SecondKind.MarshGas: case SecondKind.MarshHaze:
                    FleckMaker.ThrowSmoke(v, map, 1.2f); break;
                // dust's: emery grinds - sand streaks and the sparks of the grinding; burning sand glows and smokes; clay
                case SecondKind.Emery:
                    MapComponent_SpellSprites.PutTinted(map, "Wind/Streak", new Color(0.9f, 0.82f, 0.62f, 0.85f), v, 1.1f, 20, Rand.Range(0f, 360f), 0f, 4f);
                    if (Rand.Chance(0.5f)) FleckMaker.ThrowMicroSparks(v, map); break;
                case SecondKind.BurningSand:
                    FleckMaker.ThrowDustPuffThick(v, map, 0.8f, new Color(1f, 0.55f, 0.25f, 0.45f));
                    FleckMaker.ThrowFireGlow(v, map, 0.5f); break;
                case SecondKind.Clay:
                    MapComponent_SpellSprites.PutTinted(map, "Bog/Bubbles", new Color(0.7f, 0.42f, 0.3f, 0.9f), v, 0.7f, 60);
                    FleckMaker.ThrowDustPuffThick(v, map, 0.6f, new Color(0.65f, 0.4f, 0.3f, 0.35f)); break;
                case SecondKind.Rockfall:
                    FleckMaker.ThrowDustPuffThick(v, map, 1f, MagicFx.EarthDust); break;
                default: FleckMaker.ThrowDustPuffThick(v, map, 0.8f, new Color(col.r, col.g, col.b, 0.4f)); break;
            }
        }
    }

    /// <summary>The fields that bend shots: a hurricane knocks them down, a thermal lifts them off, a marsh haze hides one's own.</summary>
    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_SecondFieldsAim
    {
        private static readonly System.Reflection.FieldInfo dist = AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");
        public static void Postfix(Thing caster, LocalTargetInfo target, ref ShotReport __result)
        {
            Map m = caster?.Map;
            if (m == null || dist == null || !target.IsValid || m.GetComponent<SecondFields>()?.fields.Count == 0) return;
            float f = 1f;
            if (SecondFields.At(m, target.Cell, SecondKind.Hurricane) != null) f *= 0.5f;
            if (SecondFields.At(m, target.Cell, SecondKind.Thermal) != null || SecondFields.At(m, caster.Position, SecondKind.Thermal) != null) f *= 0.6f;
            var haze = SecondFields.At(m, target.Cell, SecondKind.MarshHaze);
            if (haze != null && target.Thing is Pawn tp && haze.owner != null && !tp.HostileTo(haze.owner)) f *= 0.6f;
            if (f >= 0.999f) return;
            object boxed = __result;
            dist.SetValue(boxed, (float)dist.GetValue(boxed) * f);
            __result = (ShotReport)boxed;
        }
    }

    /// <summary>A gun choked with silt does not fire until it is cleared (the hediff wears off).</summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.Available))]
    public static class Patch_SiltJams
    {
        private static HediffDef jam;
        /// <summary>Silt jams an action, not a string: a firearm (a gun that shoots bullets) of industrial make or older.
        /// Bows, crossbows, slings and thrown things have nothing for it to choke; spacer and ultratech arms shrug it off.</summary>
        public static bool Jams(ThingDef d)
        {
            if (d == null || !d.IsRangedWeapon || d.techLevel > TechLevel.Industrial) return false;
            DamageDef dmg = d.Verbs?.FirstOrDefault(v => !v.IsMeleeAttack)?.defaultProjectile?.projectile?.damageDef;
            return dmg != null && (dmg == DamageDefOf.Bullet || dmg.defName.IndexOf("Bullet", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public static void Postfix(Verb __instance, ref bool __result)
        {
            if (!__result || __instance?.verbProps == null || __instance.verbProps.IsMeleeAttack) return;
            Pawn p = __instance.CasterPawn;
            ThingWithComps gun = __instance.EquipmentSource;
            if (p?.health?.hediffSet == null || !Jams(gun?.def)) return;
            jam = jam ?? DefDatabase<HediffDef>.GetNamedSilentFail("Avir_SiltJammed");
            if (jam != null && p.health.hediffSet.HasHediff(jam)) __result = false;
        }
    }

    /// <summary>Burning sand: it burns until water washes it off.</summary>
    public class HediffComp_BurningSand : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = parent.pawn;
            if (!p.IsHashIntervalTick(120)) return;
            if ((WaterDefOf.Avir_Soaked != null && p.health.hediffSet.HasHediff(WaterDefOf.Avir_Soaked)) || (p.Spawned && Reactions.Wet(p.Position, p.Map))) { severityAdjustment = -1f; return; }
            p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 1.2f, 0f, -1f));
        }
    }
    public class HediffCompProperties_BurningSand : HediffCompProperties { public HediffCompProperties_BurningSand() { compClass = typeof(HediffComp_BurningSand); } }

    /// <summary>On the basalt pillar: when it ends, the pillar falls - and so does the one on it.</summary>
    public class HediffComp_Pillar : HediffComp
    {
        public override void CompPostPostRemoved()
        {
            Pawn p = parent.pawn;
            if (p == null || p.Dead || !p.Spawned) return;
            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 6f * SpellBody.Heavy(p), 0f, -1f));
            MagicFx.Fell(p, p.Position, 0, p.Map, null, 1f, 120);
            FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 2f, MagicFx.EarthDust);
        }
    }
    public class HediffCompProperties_Pillar : HediffCompProperties { public HediffCompProperties_Pillar() { compClass = typeof(HediffComp_Pillar); } }
}
