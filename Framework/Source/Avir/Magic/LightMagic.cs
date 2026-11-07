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
        public static bool Any(SpellCircle root) => root != null && root.Working.Any(c => Is(c.element));

        public static bool Verb(RuneKind k) => k == RuneKind.Release || k == RuneKind.Lift || k == RuneKind.Beam || k == RuneKind.Drain;

        public static float Radius(SpellCircle g) => 2f + 1.5f * g.OwnRunes.Count(x => x.kind == RuneKind.Breadth);
        public static bool Bright(SpellCircle g) => g.OwnRunes.Any(x => x.kind == RuneKind.Lift);
        public static float Range(SpellCircle root) => 16f + 10f * WeaveSpell.Reach(root);

        // ------------------------------------------------------------------ one circle of Light

        /// <summary>A circle of Light strikes `at`; its light comes from `from` (the caster, or a reflector).</summary>
        public static void CastCircle(Pawn caster, SpellCircle g, IntVec3 from, IntVec3 at, float power, Map map)
        {
            if (caster == null || g == null || map == null || !at.InBounds(map)) return;
            // the element at work, for the wards it meets: Light's own passes a ward of Light, the others' put it out
            using (SpellWards.Of(RuneKind.Light, ComboKind.None, true)) CastOwn(caster, g, from, at, power, map);
        }

        private static void CastOwn(Pawn caster, SpellCircle g, IntVec3 from, IntVec3 at, float power, Map map)
        {
            LightBarrier(caster, g, at, power, map);
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
                }
            }
            if (!any && !g.OwnRunes.Any(x => x.kind == RuneKind.Barrier)) Glint(map, at.ToVector3Shifted(), 1.4f, false);
        }

        public static HediffDef Ward => H("Avir_LightWard");

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
        private static void LightBarrier(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map)
        {
            bool shaped = SpellSculpt.Of(g);
            var runes = g.sections.Select((sec, i) => new { sec, i }).SelectMany(x => (x.sec.runes ?? new List<RuneDef>()).Where(r => r != null).Select(r => new { r, outer = x.i == g.sections.Count - 1 }))
                .Where(x => !(shaped && x.outer && (x.r.kind == RuneKind.Barrier || x.r.kind == RuneKind.Thread))).Select(x => x.r).ToList();
            if (!runes.Any(x => x.kind == RuneKind.Barrier)) return;
            int amp = runes.Count(x => x.kind == RuneKind.Amplify);
            int ticks = Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power);
            if (runes.Last().kind == RuneKind.Barrier)
            {
                SpellWards.Lay(caster, Ward, power, amp, ticks);
                Glint(map, caster.DrawPos, 2f, false);
                return;
            }
            bool aura = runes.Any(x => x.kind == RuneKind.Aura);
            float r = Mathf.Max(1.4f, Radius(g) - 0.5f);
            var d = MapComponent_WardsDrawn.RaiseWard(map, aura ? caster.Position : at, r, g.element ?? DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Light"), ComboKind.None, caster, ticks, power, amp);
            if (d != null && aura) d.follow = caster;
            LightGlow g2 = LightGlow.At(map, aura ? caster.Position : at, r >= 3f, ticks);
            if (g2 != null && aura) g2.follows = caster;
        }

        /// <summary>Whom the light reaches: by the outer edge, as everywhere - two Barriers spare one's own, two Barriers
        /// and a Thread turn it to one's own; unshaped, everyone who sees it, the caster too.</summary>
        private static bool Reached(Pawn p, Pawn caster, bool shaped, bool aid) => p != null && !p.Dead && (!shaped || (aid ? SpellSculpt.Own(p, caster) : !SpellSculpt.Own(p, caster)));

        /// <summary>Can it see the light at all (the blind are not dazzled; one already dazzled still is).</summary>
        public static bool Sees(Pawn p) => p?.health?.capacities != null && (p.health.capacities.GetLevel(PawnCapacityDefOf.Sight) > 0.05f || (Dazzle != null && p.health.hediffSet.HasHediff(Dazzle)));

        public static void DazzlePawn(Pawn p, float severity)
        {
            HediffDef d = Dazzle;
            if (d == null || p == null || p.Dead || !Sees(p) || severity <= 0f) return;
            if (SpellWards.Stops(p, true)) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(d) ?? p.health.AddHediff(d);
            h.Severity = Mathf.Min(1f, h.Severity + severity);
        }

        /// <summary>A flash: dazzles whoever sees it; `dmg` - and sears whom it reaches (Release does, Lift does not).</summary>
        public static void Flash(Pawn caster, IntVec3 at, float r, float sev, bool bright, bool shaped, bool aid, Map map, float dmg = 0f)
        {
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().ToList())
                if (Reached(p, caster, shaped, aid) && GenSight.LineOfSight(at, p.Position, map, true))
                {
                    float near = 1f - 0.4f * p.Position.DistanceTo(at) / Mathf.Max(1f, r);
                    DazzlePawn(p, sev * near);
                    if (dmg > 0f) p.TakeDamage(new DamageInfo(Sear, dmg * near, 0f, -1f, caster));
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
                    if (hit.Add(p) && Reached(p, caster, shaped, aid)) DazzlePawn(p, sev);
                if (c.Impassable(map) && c != to) { to = c; break; }   // a wall takes the ray
            }
            // where it ends, it sears
            foreach (Pawn p in to.GetThingList(map).OfType<Pawn>().ToList())
                if (Reached(p, caster, shaped, aid)) p.TakeDamage(new DamageInfo(Sear, 4f + 10f * sev, 0f, -1f, caster));
            RayLine(map, from.ToVector3Shifted(), to.ToVector3Shifted(), bright);
            Glint(map, to.ToVector3Shifted(), 1.6f, bright);
        }

        // ------------------------------------------------------------------ seen

        public static void Glint(Map map, Vector3 at, float size, bool bright)
        {
            if (map == null) return;
            MapComponent_SpellSprites.PutTintedGrowing(map, "Aura_Light", bright ? Color.white : Gold, at, size * (bright ? 1.3f : 1f), bright ? 40 : 28, 0.5f);
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
            MapComponent_SpellSprites.PutAbs(map, "Things/Projectile/Magic/Beam_Light", bright ? Color.white : Gold, (from + to) / 2f, w, len, life, d.AngleFlat());
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
        public static void Reflect(Pawn caster, SpellCircle root, IntVec3 aim, IntVec3 casterAt, float power)
        {
            Map map = caster?.Map;
            if (map == null || !aim.InBounds(map)) return;
            var links = Links(root);
            if (links.Count == 0) return;
            Hediff_ArcaneCore core = Mages.Core(caster);
            // the new ones: one per circle, and one more by chance as the mage grows in Light
            int stage = core != null ? Arcane.Stage(core, RuneKind.Light) : 0;
            int n = links.Count + (Rand.Chance(0.12f * stage) ? 1 : 0);
            float dist = Mathf.Max(3f, casterAt.IsValid ? casterAt.DistanceTo(aim) : 6f);
            // the node already standing - the caster's and their fellow Light mages' - sends its circles back, in a random
            // order (the author); the reflectors this cast raises answer only the next cast (the author: balance)
            float range = Range(root);
            var refl = Building_LightReflector.Node(caster, map).Where(x => x.Position.DistanceTo(aim) <= range).InRandomOrder().ToList();
            for (int i = 0; i < n; i++) Building_LightReflector.Raise(caster, map, aim, dist);
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

        /// <summary>A lost hold on Light: the focus goes at once - every reflector of the caster breaks in a flash.</summary>
        public static void Slip(Pawn caster, SpellCircle root)
        {
            if (caster?.Map == null || !Any(root)) return;
            Building_LightReflector.BreakAll(Building_LightReflector.Node(caster, caster.Map), caster, true);
            DazzlePawn(caster, 0.4f);
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
                    case RuneKind.Drain: bits.Add("the light taken away - a blind patch; those who cannot see in the dark shoot almost blind in it"); break;
                    default: if (r.role == RuneRole.Verb && !LifeMagic.Side(r.kind) && r.kind != RuneKind.Amplify && r.kind != RuneKind.Breadth) bits.Add(r.label + " - not made for Light yet"); break;
                }
            return "Light: " + (bits.Count > 0 ? string.Join(", then ", bits) : "a glint only - write Release, Lift, Beam or Withering");
        }

        public const string ReflectionText = "Every cast of Light raises reflectors round the point - one per circle of Light, one more now and then for the skilled - as far from the point as the caster stands, on any side, behind the caster too. They burn for a few hours and light the ground. Each living reflector of the caster within range sends one circle back into the point: a full set is an echo at half the strength of the cast before it; a reflector over a full set casts the first circle of the next echo. A reflector can be broken; the foes' mages' reflectors are foes too.";
        public const string DefocusText = "The more reflectors held at once, the harder the light is to keep in focus. A shot that loses it slides along its reflector's line - beyond the reflector at times, into whoever stands behind it - and then every reflector of the caster breaks.";
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

        public static IEnumerable<Building_LightReflector> All(Map map) =>
            map == null ? Enumerable.Empty<Building_LightReflector>() : map.listerThings.AllThings.OfType<Building_LightReflector>().Where(r => r.Spawned && !r.Destroyed);

        /// <summary>The node a caster casts into: every reflector on the map - their own, their fellows', the foes' too
        /// (the author: reflectors are a game of mirrors - one node for all).</summary>
        public static IEnumerable<Building_LightReflector> Node(Pawn caster, Map map) => All(map);

        /// <summary>A new reflector `dist` from the point, on any side.</summary>
        public static Building_LightReflector Raise(Pawn caster, Map map, IntVec3 at, float dist)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LightReflector");
            if (def == null) return null;
            for (int tries = 0; tries < 16; tries++)
            {
                float a = Rand.Range(0f, 360f);
                Vector3 v = at.ToVector3Shifted() + Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward * dist * Rand.Range(0.85f, 1.15f);
                IntVec3 c = v.ToIntVec3();
                if (!Free(c, map)) continue;
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

        private static bool Free(IntVec3 c, Map map) => c.InBounds(map) && c.Walkable(map) && c.GetEdifice(map) == null
            && !c.GetThingList(map).Any(t => t is Building || t.def.category == ThingCategory.Item);

        public static void BreakAll(IEnumerable<Building_LightReflector> which, Pawn caster, bool flash)
        {
            foreach (Building_LightReflector r in which.ToList())
            {
                if (r.Map == null) continue;
                if (flash) LightMagic.Flash(caster, r.Position, 2f, 0.2f, false, false, false, r.Map);
                MapComponent_SpellSprites.PutTinted(r.Map, "Light/Shatter", Color.white, r.DrawPos, 2.2f, 35);
                r.Destroy(DestroyMode.KillFinalize);
            }
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!this.IsHashIntervalTick(250, delta)) return;
            if (dies > 0 && Find.TickManager.TicksGame >= dies) { LightMagic.Glint(Map, DrawPos, 1f, false); Destroy(DestroyMode.Vanish); return; }
            if (caster == null || caster.Dead || caster.Destroyed) { Destroy(DestroyMode.Vanish); }
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string mine = "Reflector of " + (caster?.LabelShort ?? "no one") + (dies > 0 ? ", fades in " + (dies - Find.TickManager.TicksGame).ToStringTicksToPeriod() : "");
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref caster, "caster");
            Scribe_Values.Look(ref dies, "dies", -1);
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
        private class Shot { public Pawn caster; public SpellCircle root, circle; public Building_LightReflector from; public IntVec3 aim; public float power; public int at, echo, held; public List<Building_LightReflector> node; }
        private readonly List<Shot> shots = new List<Shot>();
        public MapComponent_LightEchoes(Map map) : base(map) { }

        public static void Queue(Pawn caster, SpellCircle root, List<SpellCircle> links, List<Building_LightReflector> refl, IntVec3 aim, float power, Map map)
        {
            var me = map.GetComponent<MapComponent_LightEchoes>();
            if (me == null) return;
            int n = links.Count, now = Find.TickManager.TicksGame;
            for (int i = 0; i < refl.Count; i++)
            {
                int echo = i / n + 1;   // 1 - the first echo, at half the cast
                // a reflector busy with another mage's cast answers this one after it
                int at = Mathf.Max(now + 18 + 9 * i, refl[i].busyUntil + 12);
                refl[i].busyUntil = at;
                me.shots.Add(new Shot { caster = caster, root = root, circle = links[i % n], from = refl[i], aim = aim, power = power * Mathf.Pow(0.5f, echo), at = at, echo = echo, held = refl.Count, node = refl });
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
                IntVec3 src = s.from.Position, land = s.aim;
                bool lost = Rand.Chance(LightMagic.DefocusChance(s.caster, s.root, s.held));
                if (lost)
                {
                    // the shot slides along its reflector's line - beyond the reflector at times (the mirror)
                    Vector3 a = s.aim.ToVector3Shifted(), b = src.ToVector3Shifted();
                    float t = Rand.Chance(0.5f) ? Rand.Range(1.3f, 1.9f) : Rand.Range(-0.4f, 0.8f);
                    IntVec3 c = Vector3.LerpUnclamped(a, b, t).ToIntVec3();
                    if (c.InBounds(map)) land = c;
                }
                LightMagic.RayLine(map, src.ToVector3Shifted(), land.ToVector3Shifted(), LightMagic.Bright(s.circle));
                LightMagic.CastCircle(s.caster, s.circle, src, land, s.power, map);
                if (lost)
                {
                    if (s.caster.Faction == Faction.OfPlayer)
                        Messages.Message(s.caster.LabelShortCap + " lost the focus of the light: the echo went astray, and the whole node of reflectors broke.", new TargetInfo(land, map), MessageTypeDefOf.NegativeEvent, false);
                    var broken = new HashSet<Building_LightReflector>(s.node);
                    Building_LightReflector.BreakAll(broken, s.caster, false);
                    shots.RemoveAll(x => x.from == null || broken.Contains(x.from) || !x.from.Spawned);
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

        [LudeonTK.DebugAction("Magic", "Light: break pawn's reflectors", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void BreakFor(Pawn p) => Building_LightReflector.BreakAll(Building_LightReflector.Node(p, p.Map), p, true);

        [LudeonTK.DebugAction("Magic", "Light: clear blind patches", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void ClearZones() { var z = MapComponent_LightZones.Of(Find.CurrentMap); if (z != null) { z.Clear(); } }
    }
}
