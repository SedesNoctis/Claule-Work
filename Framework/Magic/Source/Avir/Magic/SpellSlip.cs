using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>A lost hold is paid by all round the mage, not by the mage alone: the element the mage could not hold
    /// breaks loose where they stand. Friend and foe alike; a ward of that element keeps off what it keeps off.</summary>
    public static class SpellSlip
    {
        private static List<Pawn> Round(Pawn caster, float radius, bool withMage)
        {
            Map map = caster.Map;
            if (map == null) return new List<Pawn>();
            return GenRadial.RadialDistinctThingsAround(caster.Position, map, radius, true).OfType<Pawn>()
                .Where(p => !p.Dead && (withMage || p != caster)).ToList();
        }

        /// <summary>Water: a cold wave from the mage - everyone within four cells soaked and chilled, pools all round.</summary>
        public static void Water(Pawn caster)
        {
            if (!caster.Spawned) return;
            Map map = caster.Map;
            using (SpellWards.Of(RuneKind.Water, ComboKind.None, false))
                foreach (Pawn p in Round(caster, 4f, true))
                {
                    WaterMagic.Soak(p, 1f);
                    if (p != caster && !SpellWards.Stops(p, true)) MagicFx.Give(p, WaterDefOf.Avir_Chilled, Mathf.RoundToInt(GenDate.TicksPerHour));
                }
            foreach (IntVec3 c in GenRadial.RadialCellsAround(caster.Position, 3.5f, true)) if (Rand.Chance(0.4f)) WaterMagic.Puddle(c, map);
            FleckMaker.Static(caster.Position, map, FleckDefOf.WaterSplash, 4f);
        }

        /// <summary>Earth: the ground shudders - everyone within four cells knocked down, rubble round them.</summary>
        public static void Earth(Pawn caster)
        {
            if (!caster.Spawned) return;
            Map map = caster.Map;
            using (SpellWards.Of(RuneKind.Earth, ComboKind.None, false))
                foreach (Pawn p in Round(caster, 4f, false)) MagicFx.Fell(p, caster.Position, 0, map, caster, 1f, 150);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(caster.Position, 3.5f, true)) if (Rand.Chance(0.3f)) MagicFx.Rubble(c, map);
            FleckMaker.ThrowDustPuffThick(caster.DrawPos, map, 3f, MagicFx.EarthDust);
        }

        /// <summary>Air: a gust bursts every way - everyone within four cells thrown away from the mage.</summary>
        public static void Air(Pawn caster)
        {
            if (!caster.Spawned) return;
            Map map = caster.Map;
            using (SpellWards.Of(RuneKind.Air, ComboKind.None, false))
                foreach (Pawn p in Round(caster, 4f, false)) MagicFx.Push(p, caster.Position, 3, map);
            FleckMaker.ThrowDustPuffThick(caster.DrawPos, map, 3f, MagicFx.AirDust);
        }

        /// <summary>The meetings, each in its own way.</summary>
        public static void Combo(ComboKind k, Pawn caster)
        {
            if (!caster.Spawned) return;
            Map map = caster.Map;
            IntVec3 at = caster.Position;
            using (SpellWards.Of(null, k, false))
                switch (k)
                {
                    case ComboKind.Steam:
                        // a scalding cloud rolls out of the mage
                        foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 2.9f, true)) if (c.InBounds(map) && Rand.Chance(0.6f)) Gas_ScaldingSteam.Release(c, map, 1);
                        ComboMagic.Smoke(map, FireMagic.Point(at, map, 3f), 0.8f);
                        break;
                    case ComboKind.Storm:
                        // the storm strikes at random round the mage - three bolts, whoever they find
                        foreach (Pawn p in Round(caster, 6f, true).InRandomOrder().Take(3))
                        {
                            MapComponent_SpellSprites.Bolt(map, p.DrawPos + new Vector3(0f, 0f, 6f), p.DrawPos);
                            p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 7f, 0f, -1f, caster));
                            MagicFx.Stun(p, 120, caster);
                        }
                        FleckMaker.ThrowLightningGlow(caster.DrawPos, map, 2.5f);
                        break;
                    case ComboKind.Magma:
                        // the ground cracks open round the mage and lava wells up
                        foreach (IntVec3 c in FireMagic.RagingCells(at, map).Where(c => c.DistanceTo(at) <= 4f).Take(8)) MagmaPool.Pour(c, map, null, 1f, 600, false);
                        MapComponent_SpellSprites.Put(map, "Magma/Fissure", caster.DrawPos, 5f, 240, -1f, 0f, 4f);
                        break;
                    case ComboKind.Haze:
                        // a flash of heat: everyone within five cells dazzled
                        foreach (Pawn p in Round(caster, 5f, true)) MagicFx.Give(p, ComboDefOf.Avir_Dazzled, 1500);
                        FleckMaker.Static(at, map, FleckDefOf.ExplosionFlash, 5f);
                        break;
                    case ComboKind.Bog:
                        // the ground goes soft all round: a mire that takes anyone (it has no master)
                        foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 3.5f, true)) if (Rand.Chance(0.7f)) MirePatch.Spread(c, map, null, 1f, Mathf.RoundToInt(GenDate.TicksPerHour));
                        MirePatch.Spread(at, map, null, 1f, Mathf.RoundToInt(GenDate.TicksPerHour));
                        break;
                    case ComboKind.Dust:
                        // a dust storm round the mage: everyone within five cells blinded with it, short of breath
                        foreach (Pawn p in Round(caster, 5f, true)) { MagicFx.Give(p, ComboDefOf.Avir_Dusted, 1200); MagicFx.Give(p, EarthAirDefOf.Avir_Breathless, 900); }
                        MapComponent_SpellSprites.Put(map, "Dust/Cloud", caster.DrawPos, 8f, 300, -1f, 0.2f);
                        break;
                }
            Messages.Message(caster.LabelShortCap + " lost hold of the " + Combos.Label(k) + " - it broke loose on everyone round.", caster, MessageTypeDefOf.NegativeEvent, false);
        }
    }
}
