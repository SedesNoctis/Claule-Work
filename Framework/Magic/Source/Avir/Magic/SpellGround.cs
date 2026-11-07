using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>What the meetings do to the ground itself (Fix228, approved by the author): the floor burns or breaks, soft
    /// ground goes to mud or to marsh, mud dries, ice melts, the ground cracks to bare stone. A change of the land's own kind
    /// passes (MapComponent_TempTerrain); a burnt or broken floor stays burnt or broken, as after any fire.</summary>
    public static class SpellGround
    {
        public static TerrainDef T(string n) => DefDatabase<TerrainDef>.GetNamedSilentFail(n);

        /// <summary>Natural loose ground: soil, sand, gravel, mud, moss - what rain soaks and heat bakes.</summary>
        public static bool Soft(TerrainDef t) => t != null && t.natural && !t.IsWater && !t.layerable && !Lava(t)
            && (t.fertility > 0f || t.defName.Contains("Sand") || t.defName.Contains("Gravel") || t.defName.Contains("Soil") || t.defName.Contains("Mud"));

        /// <summary>Odyssey's lava, or ours (LavaShallow...): not cooled.</summary>
        public static bool Lava(TerrainDef t) => t != null && t.defName.Contains("Lava") && !t.defName.Contains("Cooled");

        private static bool Free(IntVec3 c, Map m) => c.InBounds(m) && c.GetEdifice(m) == null;

        /// <summary>A laid floor that can burn burns (as the game's fire leaves it - burnt planks, a burnt carpet).</summary>
        public static void Burn(IntVec3 c, Map m)
        {
            if (!c.InBounds(m)) return;
            TerrainDef t = c.GetTerrain(m);
            if (t?.burnedDef != null && t.layerable) m.terrainGrid.SetTerrain(c, t.burnedDef);
        }

        /// <summary>A laid floor cracks and is gone; the ground beneath stays.</summary>
        public static void BreakFloor(IntVec3 c, Map m, float chance)
        {
            if (!c.InBounds(m)) return;
            TerrainDef t = c.GetTerrain(m);
            if (t != null && t.layerable && !t.IsWater && Rand.Chance(chance)) m.terrainGrid.RemoveTopLayer(c, false);
        }

        /// <summary>Soft ground to mud for a while.</summary>
        public static void Muddy(IntVec3 c, Map m, int ticks)
        {
            TerrainDef mud = T("Mud");
            if (mud == null || !Free(c, m)) return;
            TerrainDef t = c.GetTerrain(m);
            if (Soft(t) && t != mud) MapComponent_TempTerrain.Of(m)?.Set(c, mud, ticks);
        }

        /// <summary>Soft ground to shallow marsh for a while.</summary>
        public static void Marshy(IntVec3 c, Map m, int ticks)
        {
            TerrainDef marsh = T("Marsh");
            if (marsh == null || !Free(c, m)) return;
            if (Soft(c.GetTerrain(m))) MapComponent_TempTerrain.Of(m)?.Set(c, marsh, ticks);
        }

        /// <summary>Heat: mud and marsh dry (to what the game says they dry to, or soil), ice melts to shallow water.</summary>
        public static void Dry(IntVec3 c, Map m, int ticks)
        {
            if (!Free(c, m)) return;
            TerrainDef t = c.GetTerrain(m);
            if (t == null) return;
            if (t.defName == "Ice") { TerrainDef w = T("WaterShallow"); if (w != null) MapComponent_TempTerrain.Of(m)?.Set(c, w, ticks); return; }
            if (t.defName == "Mud" || t.defName.Contains("Marsh"))
            {
                TerrainDef dry = t.driesTo ?? T("Soil");
                if (dry != null && dry != t) MapComponent_TempTerrain.Of(m)?.SetDry(c, dry, ticks);
            }
        }

        /// <summary>Rain on ice: it melts, slowly.</summary>
        public static void Melt(IntVec3 c, Map m, int ticks)
        {
            if (!Free(c, m)) return;
            TerrainDef t = c.GetTerrain(m), w = T("WaterShallow");
            if (t != null && t.defName == "Ice" && w != null) MapComponent_TempTerrain.Of(m)?.Set(c, w, ticks);
        }

        /// <summary>The ground cracked open to the map's own bare stone, for a while.</summary>
        public static void Crack(IntVec3 c, Map m, int ticks)
        {
            if (!Free(c, m)) return;
            BreakFloor(c, m, 1f);
            if (!Soft(c.GetTerrain(m))) return;
            TerrainDef rough = Find.World.NaturalRockTypesIn(m.Tile).Select(r => r.building?.naturalTerrain).FirstOrDefault(x => x != null);
            if (rough != null) MapComponent_TempTerrain.Of(m)?.Set(c, rough, ticks);
        }

        /// <summary>A scorch on the ground (ash), nothing broken.</summary>
        public static void Scorch(IntVec3 c, Map m)
        {
            if (c.InBounds(m) && c.Standable(m) && ThingDefOf.Filth_Ash != null) FilthMaker.TryMakeFilth(c, m, ThingDefOf.Filth_Ash, 1);
        }

        /// <summary>Water thrown onto lava (Odyssey's or ours): the lava stays, the water flashes to scalding steam (the author, Fix228).</summary>
        public static bool QuenchOnLava(IntVec3 c, Map m)
        {
            if (!c.InBounds(m) || !Lava(c.GetTerrain(m))) return false;
            Gas_ScaldingSteam.Release(c, m, 2);
            FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), m, 1.2f, new Color(1f, 1f, 1f, 0.5f));
            MagicSounds.Play("Steam", c, m, 1f, 15);
            return true;
        }
    }
}
