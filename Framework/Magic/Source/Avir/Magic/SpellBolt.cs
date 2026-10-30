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

    /// <summary>A ward (the barrier rune: fire's ward, the arcane ward) puts out a spell that flies into the one who
    /// wears it - and goes out itself. It does not throw the spell back.</summary>
    public static class Ward
    {
        private static bool ComboWard(HediffDef d) => d != null && (d == ComboDefOf.Avir_SteamWard || d == ComboDefOf.Avir_StormWard || d == ComboDefOf.Avir_MagmaWard
                                                                  || d == ComboDefOf.Avir_HazeWard || d == ComboDefOf.Avir_MossWard || d == ComboDefOf.Avir_DustWard);

        public static bool Quench(IntVec3 c, Map map, Pawn caster)
        {
            if (map == null || !c.InBounds(map)) return false;
            if (map.GetComponent<MapComponent_WardsDrawn>()?.Quench(c, caster) == true) return true;
            foreach (Pawn p in c.GetThingList(map).OfType<Pawn>())
            {
                // the caster's own side is flown over, as any shot is; a foe's ward stops it
                if (p == caster || p.Dead || (caster != null && !p.HostileTo(caster))) continue;
                Hediff w = p.health.hediffSet.hediffs.FirstOrDefault(h => h.def == FireDefOf.Avir_FireWard || h.def == WaterDefOf.Avir_WaterWard || h.def == EarthAirDefOf.Avir_EarthWard || h.def == EarthAirDefOf.Avir_AirWard || ComboWard(h.def) || h.def == MagicSpellDefOf.Avir_ArcaneWard);
                if (w == null) continue;
                p.health.RemoveHediff(w);
                FleckMaker.ThrowLightningGlow(p.DrawPos, map, 1.2f);
                FleckMaker.ThrowMicroSparks(p.DrawPos, map);
                MoteMaker.ThrowText(p.DrawPos, map, "Ward spent", 3f);
                return true;
            }
            return false;
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
            Scribe_References.Look(ref mage, "mage");
            Scribe_Deep.Look(ref spell, "spell");
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_Values.Look(ref tint, "tint", Color.white);
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!Spawned) return;
            // a ward in its way puts it out: the spell is spent, the ward is gone, nothing passes and nothing grows
            if (Position != mage?.Position && Ward.Quench(Position, Map, mage)) { Destroy(); return; }
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
