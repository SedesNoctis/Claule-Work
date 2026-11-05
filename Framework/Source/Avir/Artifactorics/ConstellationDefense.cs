using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // CONSTELLATIONS IN DEFENCE, AND THE OPEN-AIR CLIMATE.
    //   Shield aura  - colonists of the faction within reach get the aura shield (the Command one: it takes shots
    //                  and melee blows alike), stronger with more stones and a finer constellation.
    //   Barrier      - a dome like the spire's barrier, smaller: shots from outside stop on its edge while it holds.
    //                  Its radius grows with stones and quality, never past the spire barrier's least (14 cells).
    //   Open air     - the hearth and temperature functions also hold a patch of the open air: the cells within
    //                  reach read the temperature they keep (pawns, eggs, plants feel it) - with or without
    //                  Vanilla Temperature Expanded.
    // =====================================================================================

    public class CompShieldAuraTotem : CompArtifactAura
    {
        public const float MaxRange = 14f;
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        private float Reach => Mathf.Min(MaxRange, Range);
        private float Cap(CompArtifactHost h) => (S?.auraShieldCapacity ?? 75f) * h.Power * (1f + 0.25f * Mathf.Max(0, h.Working - 1));
        private float Regen(CompArtifactHost h) => (S?.auraShieldRegenPerSecond ?? 3f) * h.Power;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h) || parent.Faction == null) return;
            float r = Reach;
            bool any = false;
            foreach (Pawn p in parent.Map.mapPawns.SpawnedPawnsInFaction(parent.Faction))
            {
                if (p.Dead || p.health?.hediffSet == null || (p.Position - parent.Position).LengthHorizontalSquared > r * r) continue;
                ArtifactShieldManager.RefreshAuraFromBuilding(p, parent, Cap(h), Regen(h), 400);
                any = true;
            }
            if (any) h.Spend(A.chargePerUse);
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            if (Reach > 0f && Reach < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(parent.Position, Reach);
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Shields the colony's people around it.")
            : "Colonists within " + Reach.ToString("0.#") + " cells carry an aura shield of " + Cap(h).ToString("0") + ", recovering " + Regen(h).ToString("0.#")
              + " a second. It takes shots and melee blows alike.";
    }

    public class CompBarrierTotem : CompArtifactAura
    {
        public const float MaxRadius = 14f;   // the spire barrier's least
        public float hp = -1f;
        [Unsaved] private BarrierDome dome;
        private static readonly Color Tint = new Color(0.78f, 0.62f, 1f);

        private float Radius => Mathf.Clamp(Range, 2f, MaxRadius);
        private float MaxHp(CompArtifactHost h) => 200f * h.Working * h.Power;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h)) { Drop(); return; }
            float max = MaxHp(h);
            if (hp < 0f) hp = max;
            if (hp < max) { hp = Mathf.Min(max, hp + max * 0.04f); h.Spend(A.chargePerUse); }
            // shattered, it rises again once it has gathered half its strength
            if (dome == null && hp >= max * 0.5f)
            {
                dome = new BarrierDome { map = parent.Map, center = parent.Position, radius = Radius, hit = Hit, group = this };
                BarrierDome.All.Add(dome);
            }
            if (dome != null) dome.radius = Radius;
        }

        private bool Hit(Projectile p)
        {
            float dmg = Mathf.Max(1f, p.DamageAmount);
            if (hp >= dmg) { hp -= dmg; return true; }
            hp = 0f;
            Drop();
            Messages.Message(parent.LabelCap + ": the barrier breaks.", parent, MessageTypeDefOf.NegativeEvent, false);
            return false;
        }

        private void Drop()
        {
            if (dome == null) return;
            BarrierDome.All.Remove(dome);
            dome = null;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            Drop();
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (dome == null || !Awake(out CompArtifactHost h)) return;
            Vector3 c = parent.Position.ToVector3Shifted(); c.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float hurt = 1f - Mathf.Clamp01(hp / Mathf.Max(1f, MaxHp(h)));
            float a = 0.14f + 0.03f * Mathf.Sin(Find.TickManager.TicksGame / 90f) + 0.22f * hurt;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(dome.radius * 2f, 1f, dome.radius * 2f)),
                MaterialPool.MatFrom(FlowTex.Dome ?? BaseContent.WhiteTex, ShaderDatabase.Transparent, new Color(Tint.r, Tint.g, Tint.b, Mathf.Clamp01(a))), 0);
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            if (Radius < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(parent.Position, Radius);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hp, "barrierHp", -1f);
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Raises a barrier dome.")
            : "A barrier dome of radius " + Radius.ToString("0.#") + ": shots from outside stop on it while it holds (" + Mathf.Max(0f, hp).ToString("0") + " / "
              + MaxHp(h).ToString("0") + "). Broken, it rises again at half strength.";
    }

    // ---------------------------------------------------------------- the open air

    /// <summary>Patches of open air held at a temperature: the cells within a working hearth or temperature function's
    /// reach, outdoors. Refreshed every rare tick by the building; an entry lapses if it is not.</summary>
    public static class OutdoorClimate
    {
        private class Entry { public Thing src; public IntVec3 center; public float radius, low, high, maxShift; public int until; }

        private class MapData
        {
            public readonly List<Entry> entries = new List<Entry>();
            public readonly Dictionary<int, Entry> cells = new Dictionary<int, Entry>();
            public bool dirty;
        }

        private static readonly Dictionary<Map, MapData> data = new Dictionary<Map, MapData>();

        /// <summary>Hold the open air within radius between low and high (equal - exactly that), moving it by at most maxShift.</summary>
        public static void Hold(Thing src, float radius, float low, float high, float maxShift)
        {
            Map map = src.Map;
            if (map == null || radius <= 0f) return;
            if (!data.TryGetValue(map, out MapData d)) data[map] = d = new MapData();
            int now = Find.TickManager.TicksGame;
            Entry e = d.entries.Find(x => x.src == src);
            if (e == null) { e = new Entry { src = src }; d.entries.Add(e); d.dirty = true; }
            if (e.center != src.Position || Mathf.Abs(e.radius - radius) > 0.01f) d.dirty = true;
            e.center = src.Position; e.radius = Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f);
            e.low = low; e.high = high; e.maxShift = maxShift; e.until = now + 400;
            if (d.entries.RemoveAll(x => x.until < now || !x.src.Spawned) > 0) d.dirty = true;
        }

        public static bool Any => data.Count > 0;

        public static bool TryAdjust(IntVec3 c, Map map, ref float temp)
        {
            if (!data.TryGetValue(map, out MapData d) || d.entries.Count == 0) return false;
            if (d.dirty) Rebuild(map, d);
            if (!d.cells.TryGetValue(map.cellIndices.CellToIndex(c), out Entry e)) return false;
            if (e.until < Find.TickManager.TicksGame || !c.UsesOutdoorTemperature(map)) return false;
            float target = Mathf.Clamp(temp, e.low, e.high);
            temp += Mathf.Clamp(target - temp, -e.maxShift, e.maxShift);
            return true;
        }

        private static void Rebuild(Map map, MapData d)
        {
            d.cells.Clear();
            foreach (Entry e in d.entries)
                foreach (IntVec3 c in GenRadial.RadialCellsAround(e.center, e.radius, true))
                    if (c.InBounds(map)) d.cells[map.cellIndices.CellToIndex(c)] = e;
            d.dirty = false;
        }
    }

    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.TryGetTemperatureForCell))]
    public static class Patch_OutdoorClimate
    {
        public static void Postfix(IntVec3 c, Map map, ref float tempResult, bool __result)
        {
            if (!__result || !OutdoorClimate.Any || map == null) return;
            OutdoorClimate.TryAdjust(c, map, ref tempResult);
        }
    }
}
