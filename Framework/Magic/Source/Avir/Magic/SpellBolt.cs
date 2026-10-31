using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Spells that fly: a ball (a point struck) or a beam. They leave the mage, cross the ground and can be
    /// stopped on the way - a wall, or a shield that turns shots aside - before they take effect where they land.</summary>
    public static class SpellBolt
    {
        private static IEnumerable<RitualReading> Parts(RitualSchema s) =>
            s.weave != null ? WeaveSpell.Small(s.weave).Where(c => c.element != null).Select(c => RitualEvaluator.Read(c.AsSchema()))
                            : new[] { RitualEvaluator.Read(s) };

        /// <summary>A spell flies when every circle of it strikes a point or a line, and does something there.</summary>
        public static bool Flies(RitualSchema s, out bool beam)
        {
            beam = false;
            bool any = false;
            foreach (RitualReading r in Parts(s))
            {
                var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
                if (forms.Any(f => f.kind != RuneKind.Beam)) return false;
                if (!r.verbs.Any(v => v.role == RuneRole.Verb)) continue;
                if (forms.Any()) beam = true;
                any = true;
            }
            return any;
        }

        public static bool Walls(RitualSchema s) => Parts(s).Any(r => r.verbs.Any(v => v.kind == RuneKind.Shape));

        public static void Launch(Pawn caster, RitualSchema s, LocalTargetInfo target, float power, bool beam)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(beam ? "Avir_SpellBeam" : "Avir_SpellBall");
            if (def == null) { Spells.Land(caster, s, target, power); return; }
            var bolt = (Projectile_Spell)GenSpawn.Spawn(def, caster.Position, caster.Map);
            bolt.mage = caster; bolt.spell = s.Copy(); bolt.power = power; bolt.tint = Spells.ColorOf(s);
            bolt.Launch(caster, caster.DrawPos, target, target, ProjectileHitFlags.IntendedTarget | ProjectileHitFlags.NonTargetWorld, true);
        }
    }

    /// <summary>A ward in the way of a flying spell (Магия-правила.md, part 1). Another element: the ward puts it out at
    /// the cost of charge - or, too weak, breaks, and the spell flies on. Its own element: it passes,
    /// and the ward lies on the blow when it lands. Fire on a ward of fire, magma or steam: both go out. It never throws
    /// the spell back.</summary>
    public static class Ward
    {
        public static bool Quench(IntVec3 c, Map map, Pawn caster, RitualSchema spell, float power)
        {
            if (map == null || !c.InBounds(map)) return false;
            if (map.GetComponent<MapComponent_WardsDrawn>()?.Quench(c, caster) == true) return true;
            foreach (Pawn p in c.GetThingList(map).OfType<Pawn>())
            {
                // the caster's own side is flown over, as any shot is; a foe's ward stands in the way
                if (p == caster || p.Dead || (caster != null && !p.HostileTo(caster))) continue;
                Hediff_SpellWard w = SpellWards.On(p);
                if (w == null) continue;
                RuneKind[] inc = Elements(spell), mine = w.Parts;
                if (mine.Length > 0 && inc.Length > 0 && inc.All(k => mine.Contains(k)))
                {
                    if (!(w.Fiery && inc.Contains(RuneKind.Fire))) return false;   // its own element: through, stronger on landing
                    w.Fall("Ward and spell put out");
                    return true;
                }
                float need = 12f * power;
                if (w.energy >= need) { w.Spend(need); Spark(p, map, "Spell put out"); return true; }
                w.Fall("Ward broken");
                return false;
            }
            return false;
        }

        private static RuneKind[] Elements(RitualSchema s)
        {
            if (s == null) return new RuneKind[0];
            if (s.weave != null)
            {
                SpellCircle first = MapComponent_SpellChain.Order(s.weave).FirstOrDefault();
                ComboKind k = Combos.Of(s.weave, first);
                if (k != ComboKind.None) return Combos.Parts(k);
                return first?.element != null ? new[] { first.element.kind } : new RuneKind[0];
            }
            RuneDef el = Spells.Element(s);
            return el != null ? new[] { el.kind } : new RuneKind[0];
        }

        private static void Spark(Pawn p, Map map, string text)
        {
            FleckMaker.ThrowLightningGlow(p.DrawPos, map, 1.2f);
            FleckMaker.ThrowMicroSparks(p.DrawPos, map);
            MoteMaker.ThrowText(p.DrawPos, map, text, 3f);
        }
    }

    public class Projectile_Spell : Projectile
    {
        public Pawn mage;
        public RitualSchema spell;
        public float power = 1f;
        public Color tint = Color.white;
        private bool Beam => def.defName == "Avir_SpellBeam";
        private Material mat;

        /// <summary>Its element's own art (Things/Projectile/Magic/Ball_Fire, Beam_Water...) when it is drawn; until then
        /// the plain glow, in the element's colour.</summary>
        public override Material DrawMat
        {
            get
            {
                if (mat != null) return mat;
                RuneDef el = spell != null ? Spells.Element(spell) : null;
                string own = el != null ? "Things/Projectile/Magic/" + (Beam ? "Beam_" : "Ball_") + el.kind : null;
                if (own != null && ContentFinder<Texture2D>.Get(own, false) != null) mat = MaterialPool.MatFrom(own, ShaderDatabase.Transparent, Color.white);
                else mat = MaterialPool.MatFrom(def.graphicData.texPath, ShaderDatabase.MoteGlow, el != null ? Color.Lerp(el.color, Color.white, 0.2f) : tint);
                return mat;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref mage, "wizard");
            Scribe_Deep.Look(ref spell, "spell");
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_Values.Look(ref tint, "tint", Color.white);
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!Spawned) return;
            // a ward in its way puts it out: the spell is spent, the ward is gone, nothing passes and nothing grows
            if (Position != mage?.Position && Ward.Quench(Position, Map, mage, spell, power)) { Destroy(); return; }
            // a trail behind it, of its own element: embers for fire, spray for water, a faint haze of its colour for the rest
            RuneDef el = spell != null ? Spells.Element(spell) : null;
            if (!this.IsHashIntervalTick(Beam ? 1 : 2, delta)) return;
            if (el == null || el.kind == RuneKind.Fire) { FleckMaker.ThrowFireGlow(DrawPos, Map, Beam ? 0.55f : 0.9f); if (Rand.Chance(0.3f)) FleckMaker.ThrowMicroSparks(DrawPos, Map); }
            else if (el.kind == RuneKind.Water) { if (Rand.Chance(Beam ? 0.6f : 0.8f)) FleckMaker.Static(DrawPos.ToIntVec3(), Map, FleckDefOf.WaterRipple, Beam ? 0.7f : 1.1f); }
            else FleckMaker.ThrowDustPuffThick(DrawPos, Map, Beam ? 0.6f : 1f, new Color(el.color.r, el.color.g, el.color.b, 0.5f));
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            IntVec3 at = Position;
            Vector3 where = DrawPos;
            base.Impact(hitThing, blockedByShield);
            if (map == null) return;
            if (blockedByShield)
            {
                // a shield turned it aside: it breaks into sparks and nothing more
                FleckMaker.ThrowMicroSparks(where, map);
                FleckMaker.ThrowLightningGlow(where, map, 0.8f);
                return;
            }
            if (mage == null || spell == null || mage.Map != map) return;
            Spells.Land(mage, spell, at, power);
        }
    }
}
