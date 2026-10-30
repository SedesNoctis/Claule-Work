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

    public class Projectile_Spell : Projectile
    {
        public Pawn mage;
        public RitualSchema spell;
        public float power = 1f;
        public Color tint = Color.white;
        private bool Beam => def.defName == "Avir_SpellBeam";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref mage, "mage");
            Scribe_Deep.Look(ref spell, "spell");
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_Values.Look(ref tint, "tint", Color.white);
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!Spawned) return;
            // a trail behind it: embers for a ball, a running line of light for a beam
            if (Beam) FleckMaker.ThrowFireGlow(DrawPos, Map, 0.55f);
            else if (this.IsHashIntervalTick(2, delta)) { FleckMaker.ThrowFireGlow(DrawPos, Map, 0.9f); if (Rand.Chance(0.3f)) FleckMaker.ThrowMicroSparks(DrawPos, Map); }
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
