using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>Light, the second great circle - pure Light only for now (the author, Fix202; Магия-правила.md, «Свет»).
    /// Light has no store: it is drawn from any light and makes itself. Its law is Reflection: every cast throws out
    /// reflectors - shards of light standing round the point as far from it as the caster stands - and every living
    /// reflector of the caster within the spell's range sends one circle of it back into the point. A full set of them
    /// (one per circle) is an echo, half as strong as the cast before it; a reflector over a full set casts the first
    /// circle of the next echo. The price is defocus: the more reflectors, the harder the light is held; a defocused
    /// shot slides along its reflector's line - past the reflector, at times, into whoever stands behind it - and then
    /// every reflector of the caster breaks.
    ///   Release - a flash: dazzles all who see it. Lift - throws nothing up: a fiercer flash, brighter rays, it can
    ///   blind outright (Light alone may). Beam - a ray of light: dazzles along its line, sears where it ends.
    ///   Withering (Drain) - takes the light away: a blind patch, black, where no light reaches; those who cannot see in
    ///   the dark shoot almost blind in it; the blind do not care.</summary>
    public static class LightMagic
    {
        public static readonly Color Gold = new Color(1f, 0.93f, 0.68f);
        /// <summary>The Crystal's own colour (Light meeting Earth, Fix223): a cold prism-white.</summary>
        public static readonly Color CrystalColor = new Color(0.72f, 0.9f, 1f);
        /// <summary>By night Light turns silver-blue (the author, Fix210).</summary>
        public static readonly ColorInt NightGlow = new ColorInt(175, 205, 255, 0), DayGlow = new ColorInt(255, 238, 190, 0);
        public static bool Night(Map m) => m != null && m.skyManager.CurSkyGlow < 0.4f;
        private static HediffDef H(string n) => DefDatabase<HediffDef>.GetNamedSilentFail(n);
        public static HediffDef Dazzle => H("Avir_LightDazzle");
        public static HediffDef DarkZone => H("Avir_LightBlindZone");
        /// <summary>Light's own damage: a burn of light - the skin seared where the light struck.</summary>
        public static DamageDef Sear => DefDatabase<DamageDef>.GetNamedSilentFail("Avir_LightSear") ?? DamageDefOf.Burn;

        public static bool Is(RuneDef e) => e != null && e.kind == RuneKind.Light;
        public static IEnumerable<SpellCircle> Circles(SpellCircle root) => root == null ? Enumerable.Empty<SpellCircle>() : WeaveSpell.Greats(root).Where(g => Is(g.element));

        /// <summary>A circle of the queue that is Light's: a great circle of Light (its own runes), or a small circle
        /// with no element inside one (a small circle of Light, as with Life).</summary>
        public static bool IsLink(SpellCircle root, SpellCircle c)
        {
            if (c == null) return false;
            if (c.Great) return Is(c.element);
            if (c.element != null) return false;
            SpellCircle box = root?.ParentOf(c);
            return box != null && box.Great && Is(box.element);
        }

        /// <summary>The Light circles of a spell in the order they act.</summary>
        public static List<SpellCircle> Links(SpellCircle root) => root == null ? new List<SpellCircle>() : MapComponent_SpellChain.Order(root).Where(c => IsLink(root, c)).ToList();

        /// <summary>A Closing in the first (innermost) section of a great circle of Light keeps the great element pure (the
        /// author, Fix213): the small circles of elements inside it do not meet Light - they work as they are (fire and earth
        /// make magma), and the reflectors echo them too.</summary>
        public static bool Sealed(SpellCircle box) => box != null && Is(box.element) && WeaveSpell.SealedGreat(box);

        /// <summary>The great circle of Light a circle stands in (the nearest great circle round it), or null.</summary>
        public static SpellCircle BoxOf(SpellCircle root, SpellCircle c)
        {
            for (SpellCircle b = root?.ParentOf(c); b != null && !b.binding; b = root.ParentOf(b)) if (b.Great) return Is(b.element) ? b : null;
            return null;
        }

        /// <summary>Light meeting Earth - the Crystal (the author, Fix217): a great circle of Light, not sealed, with a small circle
        /// of Earth inside. The first such Earth goes into the Light and makes it Crystal; the other circles stay themselves
        /// (Light + Earth + Earth + Fire = Crystal + magma) and echo.</summary>
        public static bool Crystal(SpellCircle box) => box != null && box.Great && Is(box.element) && !Sealed(box)
                                                       && box.inner.Any(k => !k.Great && k.element != null && k.element.kind == RuneKind.Earth);
        public static SpellCircle Absorbed(SpellCircle box) => Crystal(box) ? box.inner.First(k => !k.Great && k.element != null && k.element.kind == RuneKind.Earth) : null;
        public static bool AnyCrystal(SpellCircle root) => Circles(root).Any(Crystal);
        /// <summary>Does this circle act within a Crystal (its own great circle of Light, or the one round it)?</summary>
        public static bool InCrystal(SpellCircle root, SpellCircle c) => c != null && Crystal(c.Great ? c : BoxOf(root, c));

        /// <summary>A small circle of an element that echoes (the author, Fix219): any inside a great circle of Light - sealed or
        /// not - the Earth that went into a Crystal aside. The reflectors are concentrators: they send back everything the
        /// great circle of Light holds, in the usual order (the small circles first, clockwise, then Light's own runes).</summary>
        public static bool SealedIn(SpellCircle root, SpellCircle c)
        {
            if (c == null || c.Great || c.element == null || Is(c.element)) return false;
            SpellCircle box = BoxOf(root, c);
            return box != null && Absorbed(box) != c;
        }

        /// <summary>The echo is being cast now (no crystals grow from echoes - the author: the field must not be spammed).</summary>
        public static bool Echoing;

        /// <summary>Everything the reflectors send back, in the order it acts: the circles of Light and the elements sealed in it.</summary>
        public static List<SpellCircle> EchoCircles(SpellCircle root) => root == null ? new List<SpellCircle>() : MapComponent_SpellChain.Order(root).Where(c => IsLink(root, c) || SealedIn(root, c)).ToList();

        /// <summary>One shot of an echo: a circle of Light, or an element sealed in Light (cast from the reflector).</summary>
        public static void EchoShot(Pawn caster, SpellCircle root, SpellCircle c, IntVec3 from, IntVec3 to, float power, Map map)
        {
            bool keep = Echoing; Echoing = true;
            try
            {
                if (IsLink(root, c)) CastCircle(caster, c, from, to, power, map, root);
                else MapComponent_SpellChain.CastEcho(caster, root, c, from, to, power);
            }
            finally { Echoing = keep; }
        }
        public static bool Any(SpellCircle root) => root != null && root.Working.Any(c => Is(c.element));

        public static bool Verb(RuneKind k) => k == RuneKind.Release || k == RuneKind.Lift || k == RuneKind.Beam || k == RuneKind.Drain;

        public static float Radius(SpellCircle g) => 2f + 1.5f * g.OwnRunes.Count(x => x.kind == RuneKind.Breadth);
        public static bool Bright(SpellCircle g) => g.OwnRunes.Any(x => x.kind == RuneKind.Lift);
        public static float Range(SpellCircle root) => 16f + 10f * WeaveSpell.Reach(root) + (AnyCrystal(root) ? CrystalReach : 0f);
        /// <summary>The Crystal keeps Earth's longer reach (the author, Fix217) - not its stone underfoot.</summary>
        public const float CrystalReach = 8f;

        // ------------------------------------------------------------------ one circle of Light

        /// <summary>A circle of Light strikes `at`; its light comes from `from` (the caster, or a reflector).</summary>
        public static void CastCircle(Pawn caster, SpellCircle g, IntVec3 from, IntVec3 at, float power, Map map, SpellCircle root = null)
        {
            if (caster == null || g == null || map == null || !at.InBounds(map)) return;
            bool crystal = InCrystal(root, g);
            // the element at work, for the wards it meets: Light's own passes a ward of Light, the others' put it out
            using (SpellWards.Of(RuneKind.Light, ComboKind.None, true)) CastOwn(caster, g, from, at, power, map, crystal);
            if (crystal && !Echoing) Grow(caster, g, from, at, power, map);
        }

        private static void CastOwn(Pawn caster, SpellCircle g, IntVec3 from, IntVec3 at, float power, Map map, bool crystal)
        {
            LightBarrier(caster, g, at, power, map, crystal);
            power *= 1f + 0.3f * g.OwnRunes.Count(x => x.kind == RuneKind.Amplify);
            float r = Radius(g);
            bool shaped = SpellSculpt.Of(g), aid = SpellSculpt.AidOf(g);
            bool any = false;
            foreach (RuneDef rune in g.OwnRunes)
            {
                switch (rune.kind)
                {
                    case RuneKind.Release: Flash(caster, at, r, 0.35f * power, false, shaped, aid, map, 7f * power); any = true; break;
                    case RuneKind.Lift: Flash(caster, at, r, 0.75f * power, true, shaped, aid, map); any = true; break;
                    case RuneKind.Beam: Ray(caster, from, at, 0.45f * power, Bright(g), shaped, aid, map); any = true; break;
                    case RuneKind.Drain: MapComponent_LightZones.Darken(map, at, r + 0.5f, Mathf.RoundToInt(GenDate.TicksPerHour * Mathf.Clamp(power, 0.3f, 3f))); any = true; break;
                    case RuneKind.Growth: Mend(caster, at, r, power, shaped, aid, map); any = true; break;
                }
            }
            if (!any && !g.OwnRunes.Any(x => x.kind == RuneKind.Barrier)) Glint(map, at.ToVector3Shifted(), 1.4f, false);
        }

        public static HediffDef Ward => H("Avir_LightWard");
        public static HediffDef CrystalWard => H("Avir_CrystalWard");
        public static HediffDef Vigor => H("Avir_LightVigor");
        public static DamageDef Shard => DefDatabase<DamageDef>.GetNamedSilentFail("Avir_CrystalShard") ?? DamageDefOf.Cut;

        /// <summary>Light + Growth (the author, Fix217): healing, as fantasy has it - the wounds of whom it reaches close, and the
        /// light gives strength back (rest, a lighter step for a while). Whom - by the outer edge, as everywhere.</summary>
        public static void Mend(Pawn caster, IntVec3 at, float r, float power, bool shaped, bool aid, Map map)
        {
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().ToList())
            {
                if (!Reached(p, caster, shaped, aid) || !GenSight.LineOfSight(at, p.Position, map, true)) continue;
                LifeMagic.HealInjuries(p, 8f * power);
                if (p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Min(1f, p.needs.rest.CurLevel + 0.12f * power);
                MagicFx.Give(p, Vigor, Mathf.RoundToInt(GenDate.TicksPerHour * 3f * Mathf.Clamp(power, 0.3f, 2f)));
            }
            MapComponent_SpellSprites.PutTintedGrowing(map, Night(map) ? "Light/Flare_Night" : "Aura_Light", new Color(0.85f, 1f, 0.8f), at.ToVector3Shifted(), r * 2f, 50, 0.3f);
            LightGlow.At(map, at, true, 120);
        }

        // ------------------------------------------------------------------ the Crystal grows from the forms (Fix217)

        /// <summary>The forms of a circle in a Crystal grow crystals (the author): a wall - a wall of crystals, a cell between
        /// them; a beam or a release - unstable crystals that answer once and burst into shards; a hold - a cage of crystals
        /// round whom it holds. Only the mage's own cast grows them - never an echo.</summary>
        public static void Grow(Pawn caster, SpellCircle c, IntVec3 from, IntVec3 at, float power, Map map)
        {
            if (map == null || c == null) return;
            var runes = c.OwnRunes.ToList();
            int breadth = runes.Count(x => x.kind == RuneKind.Breadth);
            ThingDef crys = Def("Avir_LightCrystal"), wild = Def("Avir_LightCrystalUnstable"), cage = Def("Avir_LightCrystalCage");
            if (runes.Any(x => x.kind == RuneKind.Shape) && crys != null)
            {
                var wall = FireMagic.Wall(from.IsValid ? from : caster.Position, at, map, 5 + 2 * breadth);
                for (int i = 0; i < wall.Count; i += 2) Building_LightReflector.RaiseAt(caster, map, wall[i], crys);
            }
            if (runes.Any(x => x.kind == RuneKind.Beam) && wild != null)
            {
                var line = GenSight.BresenhamCellsBetween(from.IsValid ? from : caster.Position, at).Where(x => x.InBounds(map)).ToList();
                for (int i = 3; i < line.Count; i += 3) RaiseUnstable(caster, map, line[i], line[i - 1], power);
            }
            if (runes.Any(x => x.kind == RuneKind.Release) && wild != null)
                for (int i = 0, n = Rand.RangeInclusive(2, 3); i < n; i++)
                {
                    IntVec3 cell = at + GenRadial.RadialPattern[Rand.RangeInclusive(1, 20)];
                    if (cell.InBounds(map) && GenSight.LineOfSight(at, cell, map, true)) RaiseUnstable(caster, map, cell, at, power);
                }
            if (runes.Any(x => x.kind == RuneKind.Hold) && cage != null)
            {
                int held = 0;
                foreach (Pawn p in GenRadial.RadialDistinctThingsAround(at, map, Radius(c), true).OfType<Pawn>().ToList())
                {
                    if (held++ >= 3) break;
                    foreach (IntVec3 n in GenAdj.CellsAdjacent8Way(p)) Building_LightReflector.RaiseAt(caster, map, n, cage, Mathf.RoundToInt(GenDate.TicksPerHour * Mathf.Clamp(power, 0.4f, 2f)));
                }
            }
        }

        private static ThingDef Def(string n) => DefDatabase<ThingDef>.GetNamedSilentFail(n);

        /// <summary>An unstable crystal grows (the author, Fix220): its growing cuts whoever stands next to it; grown inside someone,
        /// it cuts them hard, throws them off - and bursts at once.</summary>
        public static void RaiseUnstable(Pawn caster, Map map, IntVec3 c, IntVec3 from, float power)
        {
            if (map == null || !c.InBounds(map) || !c.Walkable(map) || c.GetEdifice(map) != null) return;
            var inside = c.GetThingList(map).OfType<Pawn>().Where(p => !p.Dead).ToList();
            if (inside.Count > 0)
            {
                foreach (Pawn p in inside)
                {
                    for (int i = 0; i < 3; i++) p.TakeDamage(new DamageInfo(Shard, Rand.Range(5f, 9f) * Mathf.Clamp(power, 0.6f, 1.6f), ShardPen, -1f, caster));
                    if (!p.Dead && p.Spawned) SpellHurl.Throw(p, from == c ? c + GenAdj.AdjacentCells.RandomElement() : from, 2, map, caster, power);
                }
                ShardsAt(map, c, c.ToVector3Shifted(), caster);
                return;
            }
            foreach (IntVec3 n in GenAdj.AdjacentCells.Select(d => c + d).Where(x => x.InBounds(map)))
                foreach (Pawn p in n.GetThingList(map).OfType<Pawn>().Where(p => !p.Dead).ToList())
                    p.TakeDamage(new DamageInfo(Shard, Rand.Range(2f, 4f), ShardPen, -1f, caster));
            Building_LightReflector.RaiseAt(caster, map, c, Def("Avir_LightCrystalUnstable"));
        }

        /// <summary>A crystal bursting: real shards, not magic - they cut whoever stands close and can see the crystal (wards meet
        /// them as plain blows; a ward of Crystal stops them, plain wards of Light or Earth pay double).</summary>
        public static void Shards(Building_LightReflector r, Pawn by)
        {
            if (r?.Map != null) ShardsAt(r.Map, r.Position, r.DrawPos, by);
        }

        public static void ShardsAt(Map map, IntVec3 c, Vector3 draw, Pawn by)
        {
            if (map == null || !c.InBounds(map)) return;
            // a burst of shards, as a grenade bursts (the author, Fix223): with its sound and its blast, cutting whoever it reaches
            GenExplosion.DoExplosion(c, map, 2.6f, Shard, by, Mathf.RoundToInt(Rand.Range(7f, 11f)), ShardPen, DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_Bomb"));
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(c, map, 2.6f, true).OfType<Pawn>().ToList())
                if (!p.Dead && GenSight.LineOfSight(c, p.Position, map, true)) p.TakeDamage(new DamageInfo(Shard, Rand.Range(3f, 6f), ShardPen, -1f, by));
            MapComponent_SpellSprites.PutTinted(map, "Light/CrystalShatter", Color.white, draw, 2.6f, 30);
            for (int i = 0; i < 4; i++) FleckMaker.ThrowMicroSparks(draw + Rand.InsideUnitCircleVec3 * 0.6f, map);
        }

        /// <summary>While the point is chosen: a line from every reflector in reach into it (a ray, if the spell has a Beam)
        /// - the player sees each echo that will come and can cross them (the author, Fix210).</summary>
        public static void PreviewEchoes(Pawn mage, SpellCircle root, IntVec3 at, Map map)
        {
            if (map == null || root == null || !at.InBounds(map) || !Any(root)) return;
            float range = Range(root);
            var links = EchoCircles(root);
            var beams = links.Where(g => g.OwnRunes.Any(x => x.kind == RuneKind.Beam)).ToList();
            int breadth = beams.Count > 0 ? beams.Max(g => g.OwnRunes.Count(x => x.kind == RuneKind.Breadth)) : 0;
            var cells = new List<IntVec3>();
            int n = 0;
            foreach (Building_LightReflector r in Building_LightReflector.Node(mage, map))
            {
                if (r.Position.DistanceTo(at) > range || !GenSight.LineOfSight(r.Position, at, map, true)) continue;
                n++;
                GenDraw.DrawLineBetween(r.TrueCenter(), at.ToVector3Shifted(), SimpleColor.Yellow, 0.2f);
                // the echo's own beam, as wide as the cast's (a stream of lava from every reflector - Fix218)
                if (beams.Count > 0) cells.AddRange(FireMagic.Beam(r.Position, at, map, breadth).Where(c => c.InBounds(map) && c != r.Position));
            }
            if (cells.Count > 0) GenDraw.DrawFieldEdges(cells.Distinct().ToList(), new Color(1f, 0.9f, 0.55f));
        }

        // ------------------------------------------------------------------ the Barrier reflects (Fix207, the author)

        /// <summary>A flying spell stopped by a ward of Light flies back the way it came - to where it was cast from.</summary>
        public static void BounceBolt(Map map, IntVec3 at, Pawn caster, RitualSchema spell, float power)
        {
            if (map == null || caster == null || spell == null) return;
            IntVec3 back = caster.Spawned && caster.Map == map ? caster.Position : at;
            if (back == at) return;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(SpellBolt.Flies(spell, out bool beam) && beam ? "Avir_SpellBeam" : "Avir_SpellBall");
            if (def == null) return;
            Glint(map, at.ToVector3Shifted(), 1.6f, true);
            var bolt = (Projectile_Spell)GenSpawn.Spawn(def, at, map);
            bolt.mage = caster; bolt.spell = spell.Copy(); bolt.power = power; bolt.tint = Spells.ColorOf(spell); bolt.mirrored = true;
            bolt.Launch(null, at.ToVector3Shifted(), new LocalTargetInfo(back), new LocalTargetInfo(back), ProjectileHitFlags.IntendedTarget | ProjectileHitFlags.NonTargetWorld, true);
        }

        /// <summary>A blow a ward of Light stopped on its wearer goes back along the line to whoever struck: the first one
        /// standing on that line takes it (the caster, or whoever is in the way).</summary>
        public static void MirrorBlow(Pawn wearer, DamageInfo d, float amount)
        {
            if (wearer?.Map == null || amount < 0.5f || !(d.Instigator is Pawn src) || src == wearer || !src.Spawned || src.Map != wearer.Map) return;
            Map map = wearer.Map;
            IntVec3 a = wearer.Position, b = src.Position;
            Vector3 dir = (b - a).ToVector3(); dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            IntVec3 far = (a.ToVector3Shifted() + dir.normalized * Mathf.Max(dir.magnitude, 12f)).ToIntVec3();
            Pawn hit = null; IntVec3 end = far;
            foreach (IntVec3 c in GenSight.BresenhamCellsBetween(a, far))
            {
                if (c == a) continue;
                if (!c.InBounds(map) || c.Impassable(map)) { end = c; break; }
                hit = c.GetThingList(map).OfType<Pawn>().FirstOrDefault(x => x != wearer && !x.Dead);
                if (hit != null) { end = c; break; }
            }
            RayLine(map, wearer.DrawPos, end.ToVector3Shifted(), true, 18);
            if (hit != null) SpellShock.Queue(hit, new DamageInfo(d.Def, amount, d.ArmorPenetrationInt, -1f, wearer));
        }

        /// <summary>The Barrier of Light (Fix206), as everywhere: the last rune of the circle - a ward on the caster, who
        /// then carries light round him (the only way a Light mage glows himself); anywhere else - a dome of light where
        /// the spell strikes (round the caster, with an Aura), lighting the ground under it.</summary>
        private static void LightBarrier(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map, bool crystal = false)
        {
            bool shaped = SpellSculpt.Of(g);
            var runes = g.sections.Select((sec, i) => new { sec, i }).SelectMany(x => (x.sec.runes ?? new List<RuneDef>()).Where(r => r != null).Select(r => new { r, outer = x.i == g.sections.Count - 1 }))
                .Where(x => !(shaped && x.outer && (x.r.kind == RuneKind.Barrier || x.r.kind == RuneKind.Thread))).Select(x => x.r).ToList();
            if (!runes.Any(x => x.kind == RuneKind.Barrier)) return;
            int amp = runes.Count(x => x.kind == RuneKind.Amplify);
            int ticks = Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power);
            if (runes.Last().kind == RuneKind.Barrier)
            {
                SpellWards.Lay(caster, crystal ? CrystalWard ?? Ward : Ward, power, amp, ticks);
                Glint(map, caster.DrawPos, 2f, false);
                return;
            }
            bool aura = runes.Any(x => x.kind == RuneKind.Aura);
            float r = Mathf.Max(1.4f, Radius(g) - 0.5f);
            var d = MapComponent_WardsDrawn.RaiseWard(map, aura ? caster.Position : at, r, g.element ?? DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Light"), ComboKind.None, caster, ticks, power, amp);
            if (d != null && aura) d.follow = caster;
            if (d != null && crystal && ContentFinder<Texture2D>.Get("Things/Mote/Avir/Magic/Dome_Crystal", false) != null) d.tex = "Dome_Crystal";
            LightGlow g2 = LightGlow.At(map, aura ? caster.Position : at, r >= 3f, ticks);
            if (g2 != null && aura) g2.follows = caster;
        }

        /// <summary>Whom the light reaches: by the outer edge, as everywhere - two Barriers spare one's own, two Barriers
        /// and a Thread turn it to one's own; unshaped, everyone who sees it, the caster too.</summary>
        private static bool Reached(Pawn p, Pawn caster, bool shaped, bool aid) => p != null && !p.Dead && (!shaped || (aid ? SpellSculpt.Own(p, caster) : !SpellSculpt.Own(p, caster)));

        /// <summary>Can it see the light at all (the blind are not dazzled; one already dazzled still is).</summary>
        public static bool Sees(Pawn p) => p?.health?.capacities != null && (p.health.capacities.GetLevel(PawnCapacityDefOf.Sight) > 0.05f || (Dazzle != null && p.health.hediffSet.HasHediff(Dazzle)));

        /// <summary>How hard Light hits a body's eyes: modules add their own (the Avir's night-born eyes suffer more, Fix215).</summary>
        public static readonly List<System.Func<Pawn, float>> Sensitivity = new List<System.Func<Pawn, float>>();
        public static float SensitivityOf(Pawn p) { float f = EyeFactor(p); foreach (var s in Sensitivity) f *= s(p); return f; }

        /// <summary>Light is the strongest blinder (the author, Fix221-222): living eyes and bionic ones take it whole (each with its
        /// stun - StunOf); archotech ones bear half, and no stun. Averaged over the eyes the body has (no eyes - it is never dazzled at all).</summary>
        public static float EyeFactor(Pawn p)
        {
            var hs = p?.health?.hediffSet;
            if (hs == null) return 1f;
            var eyes = hs.GetNotMissingParts().Where(x => x.def.tags.Contains(BodyPartTagDefOf.SightSource)).ToList();
            if (eyes.Count == 0) return 1f;
            float sum = 0f;
            foreach (BodyPartRecord e in eyes)
            {
                Hediff added = hs.hediffs.FirstOrDefault(h => h.Part == e && h is Hediff_AddedPart);
                if (added == null) sum += 1f;
                else if (added.def.defName.IndexOf("Archotech", System.StringComparison.OrdinalIgnoreCase) >= 0) sum += 0.5f;
                else sum += 1f;
            }
            return sum / eyes.Count;
        }

        /// <summary>Combat Extended counts (the author, Fix221): crystal shards cut deep - in CE as a rifle round goes (mm), else
        /// half of the armour; searing light gets through heat armour well.</summary>
        private static bool? ce;
        public static bool CE => ce ?? (ce = ModsConfig.IsActive("CETeam.CombatExtended")).Value;
        public static float ShardPen => CE ? 8f : 0.5f;
        public static float SearPen => 0.6f;

        /// <summary>A burn of light lies on the skin - never inside the body (Fix224: it used to burn livers and hearts).</summary>
        public static DamageInfo SearOn(float amount, Pawn by)
        {
            var d = new DamageInfo(Sear, amount, SearPen, -1f, by);
            d.SetBodyRegion(BodyPartHeight.Undefined, BodyPartDepth.Outside);
            return d;
        }

        /// <summary>Light burns the eyes that look at it (the author, Fix224): living eyes are burnt; bionic ones are wrecked - the
        /// light floods their sensors; archotech ones it only tries at, seldom.</summary>
        public static void EyeBurn(Pawn p, float sev, Pawn by)
        {
            if (p == null || p.Dead || !Sees(p) || Sear == null || sev <= 0f) return;
            if (!Rand.Chance(Mathf.Clamp01(0.2f + 0.45f * sev))) return;
            var hs = p.health.hediffSet;
            var eyes = hs.GetNotMissingParts().Where(x => x.def.tags.Contains(BodyPartTagDefOf.SightSource)).ToList();
            if (eyes.Count == 0) return;
            BodyPartRecord e = eyes.RandomElement();
            Hediff added = hs.hediffs.FirstOrDefault(h => h.Part == e && h is Hediff_AddedPart);
            float dmg;
            if (added == null) dmg = 2f + 6f * sev;
            else if (added.def.defName.IndexOf("Archotech", System.StringComparison.OrdinalIgnoreCase) >= 0) { if (!Rand.Chance(0.12f)) return; dmg = 3f + 6f * sev; }
            else dmg = 6f + 14f * sev;
            p.TakeDamage(new DamageInfo(Sear, dmg, 99f, -1f, by, e));
        }   // heat armour reads the same scale with or without CE

        public static void DazzlePawn(Pawn p, float severity)
        {
            severity *= SensitivityOf(p);
            HediffDef d = Dazzle;
            if (d == null || p == null || p.Dead || !Sees(p) || severity <= 0f) return;
            if (SpellWards.Stops(p, true)) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(d) ?? p.health.AddHediff(d);
            h.Severity = Mathf.Min(1f, h.Severity + severity);
            // and the stun of it, by the eyes (the author, Fix222)
            int stun = Mathf.RoundToInt(StunOf(p) * severity);
            if (stun > 10 && p.Spawned) p.stances?.stunner?.StunFor(stun, null, false);
        }

        /// <summary>Ticks of stun for a whole measure of dazzle (the author, Fix222): living eyes - the most of the plain ones;
        /// bionic - less; archotech - none at all. A module may say more (the night-born Avir - the longest).</summary>
        public static readonly List<System.Func<Pawn, float>> StunOverride = new List<System.Func<Pawn, float>>();
        public static float StunOf(Pawn p)
        {
            foreach (var f in StunOverride) { float v = f(p); if (v >= 0f) return v; }
            var hs = p?.health?.hediffSet;
            if (hs == null) return 0f;
            var eyes = hs.GetNotMissingParts().Where(x => x.def.tags.Contains(BodyPartTagDefOf.SightSource)).ToList();
            if (eyes.Count == 0) return 0f;
            float sum = 0f;
            foreach (BodyPartRecord e in eyes)
            {
                Hediff added = hs.hediffs.FirstOrDefault(x => x.Part == e && x is Hediff_AddedPart);
                if (added == null) sum += 200f;
                else if (added.def.defName.IndexOf("Archotech", System.StringComparison.OrdinalIgnoreCase) >= 0) sum += 0f;
                else sum += 110f;
            }
            return sum / eyes.Count;
        }

        /// <summary>A flash: dazzles whoever sees it; `dmg` - and sears whom it reaches (Release does, Lift does not).</summary>
        public static void Flash(Pawn caster, IntVec3 at, float r, float sev, bool bright, bool shaped, bool aid, Map map, float dmg = 0f)
        {
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().ToList())
                if (Reached(p, caster, shaped, aid) && GenSight.LineOfSight(at, p.Position, map, true))
                {
                    float near = 1f - 0.4f * p.Position.DistanceTo(at) / Mathf.Max(1f, r);
                    DazzlePawn(p, sev * near);
                    if (dmg > 0f) { p.TakeDamage(SearOn(dmg * near, caster)); EyeBurn(p, sev * near, caster); }
                }
            Glint(map, at.ToVector3Shifted(), r * 2f, bright);
        }

        public static void Ray(Pawn caster, IntVec3 from, IntVec3 to, float sev, bool bright, bool shaped, bool aid, Map map)
        {
            var cells = GenSight.BresenhamCellsBetween(from, to).Where(c => c.InBounds(map) && c != from).ToList();
            var hit = new HashSet<Pawn>();
            foreach (IntVec3 c in cells)
            {
                foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList())
                    if (hit.Add(p) && Reached(p, caster, shaped, aid))
                    {
                        DazzlePawn(p, sev);
                        // the ray burns whoever stands in it, not only at its end (the author, Fix210)
                        if (c != to) { p.TakeDamage(SearOn(3f + 7f * sev, caster)); EyeBurn(p, sev, caster); }
                    }
                if (c.Impassable(map) && c != to) { to = c; break; }   // a wall takes the ray
            }
            // where it ends, it sears
            foreach (Pawn p in to.GetThingList(map).OfType<Pawn>().ToList())
                if (Reached(p, caster, shaped, aid)) { p.TakeDamage(SearOn(4f + 10f * sev, caster)); EyeBurn(p, sev * 1.3f, caster); }
            RayLine(map, from.ToVector3Shifted(), to.ToVector3Shifted(), bright);
            Glint(map, to.ToVector3Shifted(), 1.6f, bright);
        }

        // ------------------------------------------------------------------ seen

        public static void Glint(Map map, Vector3 at, float size, bool bright)
        {
            if (map == null) return;
            bool night = Night(map);
            MapComponent_SpellSprites.PutTintedGrowing(map, night ? "Light/Flare_Night" : "Aura_Light", bright || night ? Color.white : Gold, at, size * (bright ? 1.3f : 1f), bright ? 40 : 28, 0.5f);
            FleckMaker.ThrowLightningGlow(at, map, bright ? 2.2f : 1.3f);
            if (bright) FleckMaker.ThrowLightningGlow(at, map, 1.6f);
            LightGlow.At(map, at.ToIntVec3(), size >= 4f || bright, bright ? 150 : 100);
        }

        /// <summary>A ray of light from one point to another; brighter and wider under a Lift.</summary>
        public static void RayLine(Map map, Vector3 from, Vector3 to, bool bright, int life = 26)
        {
            Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (map == null || len < 0.3f) return;
            float w = bright ? 0.9f : 0.55f;
            bool night = Night(map);
            MapComponent_SpellSprites.PutAbs(map, night ? "Things/Mote/Avir/Magic/Light/Beam_Night" : "Things/Projectile/Magic/Beam_Light", bright || night ? Color.white : Gold, (from + to) / 2f, w, len, life, d.AngleFlat());
            // a ray lights the ground along it while it shines
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 5f));
            for (int i = 0; i <= n; i++) LightGlow.At(map, Vector3.Lerp(from, to, i / (float)n).ToIntVec3(), bright, life + 60);
        }

        // ------------------------------------------------------------------ reflection

        /// <summary>The sun on the caster: a cast of Light is quicker under it (the author: «при Солнце прокаст быстрей»).</summary>
        public static float SunFactor(Pawn p, RitualSchema s)
        {
            if (p?.Map == null || s?.weave == null || !Any(s.weave)) return 1f;
            if (p.Position.Roofed(p.Map)) return 1f;
            return p.Map.skyManager.CurSkyGlow > 0.6f ? 0.75f : 1f;
        }

        /// <summary>A spell with Light in it has been cast: new reflectors stand up round the point, and every living
        /// reflector of the caster within range sends a circle back into it.</summary>
        public static void Reflect(Pawn caster, SpellCircle root, IntVec3 aim, IntVec3 casterAt, float power, int castBorn = -1)
        {
            Map map = caster?.Map;
            if (map == null || !aim.InBounds(map)) return;
            var links = EchoCircles(root);
            if (links.Count == 0) return;
            Hediff_ArcaneCore core = Mages.Core(caster);
            // the new ones: one per circle, and one more by chance as the mage grows in Light
            int stage = core != null ? Arcane.Stage(core, RuneKind.Light) : 0;
            bool crystal = AnyCrystal(root);
            // a Crystal raises fewer, sturdier shards, and rarely one more (the author)
            int n = crystal ? Mathf.Max(1, (links.Count + 1) / 2) + (Rand.Chance(0.04f * stage) ? 1 : 0) : links.Count + (Rand.Chance(0.12f * stage) ? 1 : 0);
            float dist = Mathf.Max(3f, casterAt.IsValid ? casterAt.DistanceTo(aim) : 6f);
            // the node already standing - the caster's and their fellow Light mages' - sends its circles back, in a random
            // order (the author); the reflectors this cast raises answer only the next cast (the author: balance)
            float range = Range(root);
            // a reflector that cannot see the point through a wall reflects nothing (the author, Fix217)
            // what this very cast grew (crystals of its forms) answers only the next one (the author, Fix223)
            var refl = Building_LightReflector.Node(caster, map).Where(x => (castBorn < 0 || x.born < castBorn) && x.Position.DistanceTo(aim) <= range && GenSight.LineOfSight(x.Position, aim, map, true)).InRandomOrder().ToList();
            ThingDef kind = DefDatabase<ThingDef>.GetNamedSilentFail(crystal ? "Avir_LightCrystal" : "Avir_LightReflector");
            for (int i = 0; i < n; i++) Building_LightReflector.Raise(caster, map, aim, dist, kind);
            if (refl.Count == 0) return;
            MapComponent_LightEchoes.Queue(caster, root, links, refl, aim, power, map);
        }

        /// <summary>The chance one echo shot loses focus: the more reflectors held at once, the harder (the author: from
        /// the hold; Магия-правила.md «Цена Света»).</summary>
        public static float DefocusChance(Pawn caster, SpellCircle root, int held)
        {
            if (Spells.DevHold == 1) return 0f;
            if (Spells.DevHold == -1) return 1f;
            Hediff_ArcaneCore core = Mages.Core(caster);
            float slip = core != null ? MageClass.SlipFactor(core, new RitualSchema { weave = root }) : 1f;
            return Mathf.Clamp(0.012f * held * slip, 0f, 0.8f);
        }

        /// <summary>The node weighs on the hold (the author, Fix217): every reflector standing (the works' crystals aside) makes a
        /// spell of Light harder to hold.</summary>
        public static float NodeUnrest(Pawn p, RitualSchema s) => p?.Map != null && s?.weave != null && Any(s.weave) ? 0.012f * Building_LightReflector.Held(p.Map) : 0f;

        /// <summary>A lost hold on Light: the focus goes at once - every reflector of the node breaks in a flash; the more there
        /// were, the worse for the mage.</summary>
        public static void Slip(Pawn caster, SpellCircle root, IntVec3 aim, float power)
        {
            if (caster?.Map == null || !Any(root)) return;
            int n = Building_LightReflector.Held(caster.Map);
            // the hold lost (the author, Fix223): the node does not just fall - every reflector strikes wild first, as in defocus
            var node = Building_LightReflector.Node(caster, caster.Map).Where(r => !r.Work).ToList();
            var links = EchoCircles(root);
            if (links.Count > 0 && node.Count > 0 && aim.IsValid) MapComponent_LightEchoes.Wild(caster, root, links, node, aim, power, caster.Map);
            else Building_LightReflector.BreakAll(node, caster, true);
            DazzlePawn(caster, 0.35f + 0.03f * n);
        }

        // ------------------------------------------------------------------ in words

        public static string Text(SpellCircle g)
        {
            var bits = new List<string>();
            foreach (RuneDef r in g.OwnRunes)
                switch (r.kind)
                {
                    case RuneKind.Release: bits.Add("a flash - sears and dazzles all who see it"); break;
                    case RuneKind.Lift: bits.Add("a fierce flash - nothing is thrown up, the light can blind outright"); break;
                    case RuneKind.Beam: bits.Add("a ray of light - dazzles along its line, sears where it ends"); break;
                    case RuneKind.Barrier: if (!SpellSculpt.Of(g) || g.sections.Count == 0 || !g.sections[g.sections.Count - 1].runes.Contains(r)) bits.Add("a ward of light - on the caster as the circle's last rune (they carry light round them), a glowing dome anywhere else; what it stops goes back the way it came"); break;
                    case RuneKind.Growth: bits.Add("healing - the wounds of whom it reaches close, and the light gives strength back"); break;
                    case RuneKind.Drain: bits.Add("the light taken away - a blind patch; those who cannot see in the dark shoot almost blind in it"); break;
                    default: if (r.role == RuneRole.Verb && !LifeMagic.Side(r.kind) && r.kind != RuneKind.Amplify && r.kind != RuneKind.Breadth) bits.Add(r.label + " - not made for Light yet"); break;
                }
            return "Light: " + (bits.Count > 0 ? string.Join(", then ", bits) : "a glint only - write Release, Lift, Beam or Withering");
        }

        public const string ReflectionText = "Every cast of Light raises reflectors round the point - one per circle of Light, one more now and then for the skilled - as far from the point as the caster stands, on any side, behind the caster too. They burn for a few hours and light the ground. Each living reflector of the caster within range sends one circle back into the point: a full set is an echo at half the strength of the cast before it; a reflector over a full set casts the first circle of the next echo. A reflector can be broken; the foes' mages' reflectors are foes too.";
        public const string DefocusText = "The more reflectors held at once, the harder the light is to keep in focus. A shot that loses it slides along its reflector's line - beyond the reflector at times, into whoever stands behind it - and then the node breaks like a prism: each reflector strikes once more, its own circle at full echo strength, toward the point but split and thrown aside - past it at times, into the caster - and shatters. The more were gathered, the worse it is.";
    }

    /// <summary>Where a spell's lines start: the mage, or - for an echo of Light - its reflector (Fix213).</summary>
    public static class SpellOrigin
    {
        public static IntVec3 From = IntVec3.Invalid;
        public static IntVec3 Of(Pawn caster) => From.IsValid ? From : caster?.Position ?? IntVec3.Invalid;
    }

    // ====================================================================== the reflectors

    public class Building_LightReflector : Building, IAttackTarget
    {
        public Pawn caster;
        public int dies = -1;
        public const int Life = GenDate.TicksPerHour * 4;

        Thing IAttackTarget.Thing => this;
        public LocalTargetInfo TargetCurrentlyAimingAt => LocalTargetInfo.Invalid;
        public float TargetPriorityFactor => 0.6f;
        public bool ThreatDisabled(IAttackTargetSearcher disabledFor) => !Spawned;

        /// <summary>When it is free to send the next shot: the casts of a node go one after another (the author).</summary>
        public int busyUntil;
        /// <summary>When it stood up.</summary>
        public int born;
        public override void SpawnSetup(Map map, bool respawningAfterLoad) { base.SpawnSetup(map, respawningAfterLoad); if (!respawningAfterLoad) born = Find.TickManager.TicksGame; }

        // its kind, by its def (Fix217)
        public bool IsCrystal => def.defName.StartsWith("Avir_LightCrystal");
        public bool Unstable => def.defName == "Avir_LightCrystalUnstable";
        public bool Work => def.defName == "Avir_LightCrystalStore" || def.defName == "Avir_LightCrystalSun";
        public bool Sun => def.defName == "Avir_LightCrystalSun";
        /// <summary>How much weaker each echo is: a plain reflector halves it, a crystal keeps three quarters (the author).</summary>
        public float Decay => IsCrystal ? 0.75f : 0.5f;
        /// <summary>Faceted: a crystal may send its circle twice (the author: not every time, not all).</summary>
        public bool Faceted => IsCrystal && !Work && !Unstable;

        /// <summary>The reflectors that weigh on the hold - all but the works' crystals.</summary>
        public static int Held(Map map) => All(map).Count(r => !r.Work);

        public static IEnumerable<Building_LightReflector> All(Map map) =>
            map == null ? Enumerable.Empty<Building_LightReflector>() : map.listerThings.AllThings.OfType<Building_LightReflector>().Where(r => r.Spawned && !r.Destroyed);

        /// <summary>The node a caster casts into: every reflector on the map - their own, their fellows', the foes' too
        /// (the author: reflectors are a game of mirrors - one node for all).</summary>
        public static IEnumerable<Building_LightReflector> Node(Pawn caster, Map map) => All(map);

        /// <summary>A new reflector `dist` from the point, on any side.</summary>
        public static Building_LightReflector Raise(Pawn caster, Map map, IntVec3 at, float dist, ThingDef def = null)
        {
            def = def ?? DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LightReflector");
            if (def == null) return null;
            for (int tries = 0; tries < 16; tries++)
            {
                float a = Rand.Range(0f, 360f);
                Vector3 v = at.ToVector3Shifted() + Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward * dist * Rand.Range(0.85f, 1.15f);
                IntVec3 c = v.ToIntVec3();
                // never behind a wall: the reflector stands where the point can see it (the author, Fix210)
                if (!Free(c, map) || !GenSight.LineOfSight(at, c, map, true)) continue;
                var r = (Building_LightReflector)ThingMaker.MakeThing(def);
                r.caster = caster;
                r.dies = Find.TickManager.TicksGame + Life;
                GenSpawn.Spawn(r, c, map, WipeMode.VanishOrMoveAside);
                if (caster?.Faction != null) r.SetFaction(caster.Faction);
                LightMagic.Glint(map, r.DrawPos, 1.4f, false);
                return r;
            }
            return null;
        }

        /// <summary>A crystal grown on a given cell (a form of the Crystal, a work of the mage).</summary>
        public static Building_LightReflector RaiseAt(Pawn caster, Map map, IntVec3 c, ThingDef def, int life = -1)
        {
            if (def == null || map == null || !Free(c, map) || c.GetFirstPawn(map) != null) return null;
            var r = (Building_LightReflector)ThingMaker.MakeThing(def);
            r.caster = caster;
            r.dies = Find.TickManager.TicksGame + (life > 0 ? life : r.Unstable ? GenDate.TicksPerHour : r.Work ? GenDate.TicksPerDay * 5 : Life);
            GenSpawn.Spawn(r, c, map, WipeMode.VanishOrMoveAside);
            if (caster?.Faction != null) r.SetFaction(caster.Faction);
            FleckMaker.ThrowLightningGlow(r.DrawPos, map, 0.8f);
            return r;
        }

        /// <summary>Crystals stand against blasts; nothing of them burns.</summary>
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (IsCrystal && dinfo.Def != null && (dinfo.Def.isExplosive || dinfo.Def == DamageDefOf.Bomb)) dinfo.SetAmount(dinfo.Amount * 0.3f);
            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        /// <summary>An unstable crystal after its one answer: it bursts into shards.</summary>
        public void Burst(Pawn by)
        {
            if (!Spawned) return;
            LightMagic.Shards(this, by ?? caster);
            Destroy(DestroyMode.Vanish);
        }

        private static bool Free(IntVec3 c, Map map) => c.InBounds(map) && c.Walkable(map) && c.GetEdifice(map) == null
            && !c.GetThingList(map).Any(t => t is Building || t.def.category == ThingCategory.Item);

        public static void BreakAll(IEnumerable<Building_LightReflector> which, Pawn caster, bool flash)
        {
            foreach (Building_LightReflector r in which.ToList())
            {
                if (r.Map == null) continue;
                if (flash) LightMagic.Flash(caster, r.Position, 2f, 0.2f, false, false, false, r.Map);
                if (r.IsCrystal) { LightMagic.Shards(r, caster); r.Destroy(DestroyMode.Vanish); continue; }
                MapComponent_SpellSprites.PutTinted(r.Map, LightMagic.Night(r.Map) ? "Light/Shatter_Night" : "Light/Shatter", Color.white, r.DrawPos, 2.2f, 35);
                r.Destroy(DestroyMode.KillFinalize);
            }
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!this.IsHashIntervalTick(250, delta)) return;
            if (dies > 0 && Find.TickManager.TicksGame >= dies) { LightMagic.Glint(Map, DrawPos, 1f, false); Destroy(DestroyMode.Vanish); return; }
            if (Work) { WorkTick(); return; }
            if (caster == null || caster.Dead || caster.Destroyed) { Destroy(DestroyMode.Vanish); return; }
            bool night = LightMagic.Night(Map);
            if (night != wasNight) { wasNight = night; CompGlower gl = GetComp<CompGlower>(); if (gl != null) gl.GlowColor = night ? LightMagic.NightGlow : LightMagic.DayGlow; }
        }

        private bool wasNight, lit = true;
        private static Material nightMat;

        /// <summary>The works' crystals (Fix217): a light crystal shines at all hours (Light feeds on itself); a sun crystal burns
        /// by day like a sun lamp - plants grow under it - and goes out at night.</summary>
        private void WorkTick()
        {
            if (!Sun) return;
            CompGlower gl = GetComp<CompGlower>();
            if (gl == null) return;
            bool day = Map.skyManager.CurSkyGlow > 0.4f;
            if (day == lit) return;
            lit = day;
            if (day) Map.glowGrid.RegisterGlower(gl); else Map.glowGrid.DeRegisterGlower(gl);
        }

        /// <summary>By night the shard is silver.</summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (IsCrystal || !LightMagic.Night(Map)) { base.DrawAt(drawLoc, flip); return; }
            if (nightMat == null) nightMat = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Light/Reflector_Night", ShaderDatabase.Transparent);
            float s = def.graphicData.drawSize.x;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawLoc, Quaternion.identity, new Vector3(s, 1f, s)), nightMat, 0);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string mine = (Work ? "Raised by " : "Reflector of ") + (caster?.LabelShort ?? "no one") + (dies > 0 ? ", fades in " + (dies - Find.TickManager.TicksGame).ToStringTicksToPeriod() : "")
                          + (Work ? "\nA reflector too: it answers every spell of Light at a steady share of its strength, and never loses focus." : IsCrystal ? "\nCrystal: echoes at three quarters, may echo twice." : "")
                          + (Unstable ? "\nUnstable: answers once and bursts into shards." : "") + (Sun ? (lit ? "\nBurning with the day." : "\nDark until morning.") : "");
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref caster, "caster");
            Scribe_Values.Look(ref dies, "dies", -1);
            Scribe_Values.Look(ref born, "born");
        }
    }

    // ====================================================================== light on the ground

    /// <summary>Light lights the map (the author): its flashes, rays and reflectors - not the Light mage himself, unless a
    /// Barrier is on him (Fix205). A small unseen glower that fades by itself.</summary>
    public class LightGlow : ThingWithComps
    {
        public int dies;
        public Pawn follows;

        public static LightGlow At(Map map, IntVec3 c, bool big, int ticks)
        {
            if (map == null || !c.InBounds(map)) return null;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(big ? "Avir_LightGlowBig" : "Avir_LightGlowSmall");
            if (def == null) return null;
            var g = (LightGlow)ThingMaker.MakeThing(def);
            g.dies = Find.TickManager.TicksGame + ticks;
            GenSpawn.Spawn(g, c, map, WipeMode.Vanish);
            if (LightMagic.Night(map)) { CompGlower gl = g.GetComp<CompGlower>(); if (gl != null) gl.GlowColor = LightMagic.NightGlow; }
            return g;
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!this.IsHashIntervalTick(30, delta)) return;
            if (Find.TickManager.TicksGame >= dies) { Destroy(DestroyMode.Vanish); return; }
            if (follows != null && (!follows.Spawned || follows.Map != Map || follows.Dead)) { Destroy(DestroyMode.Vanish); return; }
            if (follows != null && follows.Position != Position)
            {
                // the light goes with the one it follows
                Map m = Map; IntVec3 to = follows.Position; bool big = def.defName.EndsWith("Big"); int left = dies - Find.TickManager.TicksGame; Pawn who = follows;
                Destroy(DestroyMode.Vanish);
                LightGlow n = At(m, to, big, left);
                if (n != null) n.follows = who;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref dies, "dies");
            Scribe_References.Look(ref follows, "follows");
        }
    }

    /// <summary>A ward of Light keeps light round its wearer (Fix206).</summary>
    public class HediffCompProperties_LightGlow : HediffCompProperties { public HediffCompProperties_LightGlow() { compClass = typeof(HediffComp_LightGlow); } }

    public class HediffComp_LightGlow : HediffComp
    {
        private LightGlow glow;
        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = parent.pawn;
            if (p == null || !p.Spawned || !p.IsHashIntervalTick(30)) return;
            if (glow != null && glow.Spawned && glow.Map == p.Map && glow.Position == p.Position) { glow.dies = Find.TickManager.TicksGame + 200; return; }
            if (glow != null && glow.Spawned) glow.Destroy(DestroyMode.Vanish);
            glow = LightGlow.At(p.Map, p.Position, false, 200);
        }
        public override void CompPostPostRemoved() { if (glow != null && glow.Spawned) glow.Destroy(DestroyMode.Vanish); }
    }

    // ====================================================================== the echoes, shot by shot

    public class MapComponent_LightEchoes : MapComponent
    {
        private class Shot { public Pawn caster; public SpellCircle root, circle; public Building_LightReflector from; public IntVec3 aim; public float power; public int at, echo, held; public List<Building_LightReflector> node; public bool wild; }
        private readonly List<Shot> shots = new List<Shot>();
        public MapComponent_LightEchoes(Map map) : base(map) { }

        public static void Queue(Pawn caster, SpellCircle root, List<SpellCircle> links, List<Building_LightReflector> refl, IntVec3 aim, float power, Map map)
        {
            var me = map.GetComponent<MapComponent_LightEchoes>();
            if (me == null) return;
            int n = links.Count, now = Find.TickManager.TicksGame, works = 0;
            int held = refl.Count(r => !r.Work);
            for (int i = 0; i < refl.Count; i++)
            {
                int echo = i / n + 1;   // 1 - the first echo, at half the cast (three quarters from a crystal)
                // a reflector busy with another mage's cast answers this one after it
                int at = Mathf.Max(now + 18 + 9 * i, refl[i].busyUntil + 12);
                refl[i].busyUntil = at;
                // the works' crystals answer at a steady 15% of the circle, less for each more of them (the author, Fix217)
                // so do the unstable ones (the author, Fix220): a share, no facets - and they burst after
                float p = refl[i].Unstable ? power * 0.15f : refl[i].Work ? power * 0.15f * Mathf.Pow(0.85f, works++) : power * Mathf.Pow(refl[i].Decay, echo);
                me.shots.Add(new Shot { caster = caster, root = root, circle = links[i % n], from = refl[i], aim = aim, power = p, at = at, echo = echo, held = held, node = refl });
                // a faceted crystal may send its circle twice
                if (refl[i].Faceted && Rand.Chance(0.2f))
                    me.shots.Add(new Shot { caster = caster, root = root, circle = links[i % n], from = refl[i], aim = aim, power = p, at = at + 8, echo = echo, held = held, node = refl });
            }
        }

        /// <summary>Defocus (the author, Fix211): before they break, every reflector of the node fires once - each its own
        /// circle (or the one that went astray), in a random direction, at full echo strength. The more were gathered,
        /// the worse it is on a great spell.</summary>
        /// <summary>The node going wild at a lost hold: each reflector its circle, toward the point but thrown aside (unstable ones
        /// anywhere), and shatters.</summary>
        public static void Wild(Pawn caster, SpellCircle root, List<SpellCircle> links, List<Building_LightReflector> node, IntVec3 aim, float power, Map map)
        {
            var me = map.GetComponent<MapComponent_LightEchoes>();
            if (me == null) return;
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < node.Count; i++)
            {
                var s = new Shot { caster = caster, root = root, circle = links[i % links.Count], from = node[i], aim = aim, power = power * (node[i].Unstable ? 0.15f : node[i].Decay), held = node.Count, node = node };
                me.Scatter(s, now + 6 * i, new List<Building_LightReflector> { node[i] }, node[i].Unstable);
            }
        }

        private void Scatter(Shot s, int now, List<Building_LightReflector> only = null, bool anyWay = false)
        {
            var node = only ?? Building_LightReflector.Node(s.caster, map).Where(r => !r.Work && !r.IsCrystal).ToList();
            var pending = shots.Where(x => !x.wild && x.from != null).GroupBy(x => x.from).ToDictionary(g => g.Key, g => g.First());
            shots.RemoveAll(x => !x.wild && x.from != null && node.Contains(x.from));
            int i = 0;
            foreach (Building_LightReflector r in node.InRandomOrder())
            {
                if (!r.Spawned) continue;
                Shot own = pending.TryGetValue(r, out Shot p) ? p : s;
                // a broken prism (the author, Fix212): the ray still goes toward the centre, but split and thrown aside - like a
                // burning-glass's beam fanning out; it may run past the centre and on, into the caster behind it
                float dist = Mathf.Max(5f, r.Position.DistanceTo(s.aim));
                Vector3 toward = (s.aim.ToVector3Shifted() - r.TrueCenter()); toward.y = 0f;
                if (toward.sqrMagnitude < 0.01f) toward = Vector3.forward;
                float spread = Rand.Range(-1f, 1f); spread = anyWay ? Rand.Range(0f, 360f) : Mathf.Sign(spread) * spread * spread * 55f;   // most near the line, some far off it
                Vector3 dir = Quaternion.AngleAxis(spread, Vector3.up) * toward.normalized;
                dist *= Rand.Range(0.6f, 2.1f);
                IntVec3 land = r.Position;
                // the stray ray goes until a wall takes it
                for (float k = 1f; k <= dist; k += 1f)
                {
                    IntVec3 c = (r.TrueCenter() + dir * k).ToIntVec3();
                    if (!c.InBounds(map)) break;
                    land = c;
                    if (c.Impassable(map)) break;
                }
                shots.Add(new Shot { caster = own.caster ?? s.caster, root = own.root ?? s.root, circle = own.circle ?? s.circle, from = r, aim = land, power = Mathf.Max(own.power, s.power), at = now + 4 + 5 * i++, wild = true, node = s.node });
            }
        }

        public override void MapComponentTick()
        {
            if (shots.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < shots.Count; i++)
            {
                Shot s = shots[i];
                if (now < s.at) continue;
                shots.RemoveAt(i--);
                if (s.caster == null || s.caster.Dead || s.from == null || !s.from.Spawned || s.from.Map != map) continue;
                if (s.wild)
                {
                    // a reflector gone wild: it strikes once, anywhere, and shatters
                    LightMagic.RayLine(map, s.from.DrawPos, s.aim.ToVector3Shifted(), true);
                    LightMagic.EchoShot(s.caster, s.root, s.circle, s.from.Position, s.aim, s.power, map);
                    Building_LightReflector.BreakAll(new[] { s.from }, s.caster, false);
                    continue;
                }
                IntVec3 src = s.from.Position, land = s.aim;
                // a wall raised between since: no reflection
                if (!GenSight.LineOfSight(src, s.aim, map, true)) continue;
                bool lost = !s.from.Work && Rand.Chance(LightMagic.DefocusChance(s.caster, s.root, s.held));
                if (lost)
                {
                    // the shot slides along its reflector's line - beyond the reflector at times (the mirror)
                    Vector3 a = s.aim.ToVector3Shifted(), b = src.ToVector3Shifted();
                    float t = Rand.Chance(0.5f) ? Rand.Range(1.3f, 1.9f) : Rand.Range(-0.4f, 0.8f);
                    IntVec3 c = Vector3.LerpUnclamped(a, b, t).ToIntVec3();
                    if (c.InBounds(map)) land = c;
                }
                LightMagic.RayLine(map, src.ToVector3Shifted(), land.ToVector3Shifted(), LightMagic.Bright(s.circle));
                LightMagic.EchoShot(s.caster, s.root, s.circle, src, land, s.power, map);
                if (s.from.Unstable && s.from.Spawned && !shots.Any(x => x.from == s.from)) { s.from.Burst(s.caster); continue; }
                if (lost && s.from.Unstable && s.from.Spawned)
                {
                    // an unstable crystal losing focus aims anywhere at all, with its own strength, and bursts (the author, Fix221)
                    Scatter(s, now, new List<Building_LightReflector> { s.from }, true);
                    continue;
                }
                if (lost && s.from.IsCrystal && s.from.Spawned)
                {
                    // a crystal loses its own focus alone (the author, Fix217): only it goes wild and breaks, the rest hold
                    if (s.caster.Faction == Faction.OfPlayer)
                        Messages.Message("A crystal lost the focus of " + s.caster.LabelShort + "'s light: it strikes wild and shatters.", new TargetInfo(land, map), MessageTypeDefOf.NegativeEvent, false);
                    Scatter(s, now, new List<Building_LightReflector> { s.from });
                    continue;
                }
                if (lost)
                {
                    if (s.caster.Faction == Faction.OfPlayer)
                        Messages.Message(s.caster.LabelShortCap + " lost the focus of the light: every reflector of the node strikes wild, every way, and shatters.", new TargetInfo(land, map), MessageTypeDefOf.ThreatSmall, false);
                    Scatter(s, now);
                    return;
                }
            }
        }
    }

    // ====================================================================== the blind patches of Withering

    public class MapComponent_LightZones : MapComponent
    {
        private class Zone : IExposable
        {
            public IntVec3 c; public float r; public int until;
            public void ExposeData() { Scribe_Values.Look(ref c, "c"); Scribe_Values.Look(ref r, "r"); Scribe_Values.Look(ref until, "until"); }
        }
        private List<Zone> zones = new List<Zone>();
        private static Material blot;
        public MapComponent_LightZones(Map map) : base(map) { }

        public static MapComponent_LightZones Of(Map m) => m?.GetComponent<MapComponent_LightZones>();

        public static void Darken(Map map, IntVec3 at, float r, int ticks)
        {
            var me = Of(map);
            if (me == null || !at.InBounds(map)) return;
            me.zones.Add(new Zone { c = at, r = r, until = Find.TickManager.TicksGame + Mathf.Max(600, ticks) });
            me.Dirty();
            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, r, new Color(0.05f, 0.05f, 0.08f, 0.7f));
        }

        public bool Dark(IntVec3 c)
        {
            for (int i = 0; i < zones.Count; i++) if (c.DistanceToSquared(zones[i].c) <= zones[i].r * zones[i].r) return true;
            return false;
        }
        public bool AnyZone => zones.Count > 0;
        public void Clear() { var old = zones; zones = new List<Zone>(); foreach (Zone z in old) foreach (IntVec3 c in GenRadial.RadialCellsAround(z.c, z.r + 1f, true)) if (c.InBounds(map)) map.glowGrid.DirtyCell(c); }

        private void Dirty()
        {
            foreach (Zone z in zones) foreach (IntVec3 c in GenRadial.RadialCellsAround(z.c, z.r + 1f, true)) if (c.InBounds(map)) map.glowGrid.DirtyCell(c);
        }

        /// <summary>Who cannot see in the dark shoots almost blind in it; the blind - and those with dark sight - do not care.</summary>
        public static bool Hurts(Pawn p) => p != null && !p.Dead && (p.genes == null || p.genes.AffectedByDarkness)
                                            && p.health.capacities.GetLevel(PawnCapacityDefOf.Sight) > 0.05f;

        public override void MapComponentTick()
        {
            if (zones.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0) return;
            var gone = zones.Where(z => now >= z.until).ToList();
            if (gone.Count > 0) { zones.RemoveAll(z => now >= z.until); foreach (Zone z in gone) foreach (IntVec3 c in GenRadial.RadialCellsAround(z.c, z.r + 1f, true)) if (c.InBounds(map)) map.glowGrid.DirtyCell(c); }
            HediffDef d = LightMagic.DarkZone;
            if (d == null) return;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
                if (Dark(p.Position) && Hurts(p)) MagicFx.Give(p, d, 120);
        }

        public override void MapComponentUpdate()
        {
            if (zones.Count == 0 || Find.CurrentMap != map) return;
            if (blot == null) blot = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Light/Blot", ShaderDatabase.Transparent);
            float y = AltitudeLayer.MoteOverhead.AltitudeFor();
            foreach (Zone z in zones)
            {
                Vector3 pos = z.c.ToVector3Shifted(); pos.y = y;
                float s = z.r * 2.4f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s, 1f, s)), blot, 0);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref zones, "zones", LookMode.Deep);
            if (zones == null) zones = new List<Zone>();
        }
    }

    /// <summary>No light reaches a blind patch: whatever lamp or sun is near, the ground there is dark.</summary>
    [HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.GroundGlowAt))]
    public static class Patch_LightZonesGlow
    {
        private static readonly AccessTools.FieldRef<GlowGrid, Map> mapOf = AccessTools.FieldRefAccess<GlowGrid, Map>("map");
        public static void Postfix(GlowGrid __instance, IntVec3 c, ref float __result)
        {
            if (__result <= 0f) return;
            var z = MapComponent_LightZones.Of(mapOf(__instance));
            if (z != null && z.AnyZone && z.Dark(c)) __result = 0f;
        }
    }

    public static class LightDev
    {
        [LudeonTK.DebugAction("Magic", "Light: flash here (r3)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void FlashHere() => LightMagic.Flash(null, Verse.UI.MouseCell(), 3f, 0.5f, false, false, false, Find.CurrentMap);

        [LudeonTK.DebugAction("Magic", "Light: fierce flash here (Lift, r3)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void FierceHere() => LightMagic.Flash(null, Verse.UI.MouseCell(), 3f, 1f, true, false, false, Find.CurrentMap);

        [LudeonTK.DebugAction("Magic", "Light: blind patch here (r4, 1 h)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void BlindHere() => MapComponent_LightZones.Darken(Find.CurrentMap, Verse.UI.MouseCell(), 4f, GenDate.TicksPerHour);

        [LudeonTK.DebugAction("Magic", "Light: reflector for pawn here", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void ReflectorFor(Pawn p) => Building_LightReflector.Raise(p, p.Map, p.Position, 5f);

        [LudeonTK.DebugAction("Magic", "Light: six reflectors round pawn (to test echoes)", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void SixFor(Pawn p) { for (int i = 0; i < 6; i++) Building_LightReflector.Raise(p, p.Map, p.Position, 7f); }

        [LudeonTK.DebugAction("Magic", "Light: four crystals round pawn", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void CrystalsFor(Pawn p) { for (int i = 0; i < 4; i++) Building_LightReflector.Raise(p, p.Map, p.Position, 7f, DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LightCrystal")); }

        [LudeonTK.DebugAction("Magic", "Light: unstable crystal here (bursts)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void BurstHere()
        {
            var r = Building_LightReflector.RaiseAt(null, Find.CurrentMap, Verse.UI.MouseCell(), DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LightCrystalUnstable"));
            r?.Burst(null);
        }

        [LudeonTK.DebugAction("Magic", "Light: light crystal here (work)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void StoreHere() => Building_LightReflector.RaiseAt(null, Find.CurrentMap, Verse.UI.MouseCell(), DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LightCrystalStore"));

        [LudeonTK.DebugAction("Magic", "Light: sun crystal here (work)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void SunHere() => Building_LightReflector.RaiseAt(null, Find.CurrentMap, Verse.UI.MouseCell(), DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LightCrystalSun"));

        [LudeonTK.DebugAction("Magic", "Light: break pawn's reflectors", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void BreakFor(Pawn p) => Building_LightReflector.BreakAll(Building_LightReflector.Node(p, p.Map), p, true);

        [LudeonTK.DebugAction("Magic", "Light: clear blind patches", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void ClearZones() { var z = MapComponent_LightZones.Of(Find.CurrentMap); if (z != null) { z.Clear(); } }
    }
}
