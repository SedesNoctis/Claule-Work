using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>Ground a spell left dangerous (Магия-правила.md): magma, scalding steam, a caster's mire. A pawn with no
    /// ward goes round it if there is any other way - it may still cross when there is none; a pawn under a ward walks
    /// straight over it, as over any ground. The cost is laid per pawn on its own path request (the game's path grid is
    /// shared by all), from a grid of the map's dangerous cells rebuilt every two seconds.</summary>
    public class MapComponent_MagicDanger : MapComponent
    {
        public const ushort Magma = 400, Steam = 250, Mire = 150;

        private NativeArray<ushort> a, b;
        private bool onA, any, made;
        private readonly Customizer customizer;

        public MapComponent_MagicDanger(Map map) : base(map) { customizer = new Customizer(this); }

        private class Customizer : PathRequest.IPathGridCustomizer
        {
            private readonly MapComponent_MagicDanger of;
            public Customizer(MapComponent_MagicDanger c) { of = c; }
            public NativeArray<ushort> GetOffsetGrid() => of.onA ? of.a : of.b;
        }

        /// <summary>The cost to lay on this pawn's path: none when nothing on the map is dangerous, or the pawn is warded.</summary>
        public static PathRequest.IPathGridCustomizer For(Pawn p)
        {
            if (p?.Map == null || !AvirMagic.On) return null;
            var c = p.Map.GetComponent<MapComponent_MagicDanger>();
            if (c == null || !c.any || !c.made) return null;
            if (SpellWards.Of(p) != null) return null;   // warded: the ground it fears no more
            return c.customizer;
        }

        private void Make()
        {
            int n = map.cellIndices.NumGridCells;
            a = new NativeArray<ushort>(n, Allocator.Persistent);
            b = new NativeArray<ushort>(n, Allocator.Persistent);
            made = true;
        }

        public override void MapComponentTick()
        {
            if (!AvirMagic.On || (Find.TickManager.TicksGame + map.uniqueID) % 120 != 0) return;
            Rebuild();
        }

        private static ThingDef magma, mire, steam;

        private void Rebuild()
        {
            magma = magma ?? DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Magma");
            mire = mire ?? DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Mire");
            steam = steam ?? DefDatabase<ThingDef>.GetNamedSilentFail("Avir_GasSteam");
            var hot = new List<KeyValuePair<IntVec3, ushort>>();
            void Add(ThingDef d, ushort cost) { if (d != null) foreach (Thing t in map.listerThings.ThingsOfDef(d)) if (t.Spawned) hot.Add(new KeyValuePair<IntVec3, ushort>(t.Position, cost)); }
            Add(magma, Magma); Add(steam, Steam); Add(mire, Mire);
            if (hot.Count == 0 && !any) return;
            if (!made) Make();
            // write the grid not in use, then switch: a path being worked out on the other is not disturbed
            NativeArray<ushort> g = onA ? b : a;
            for (int i = 0; i < g.Length; i++) if (g[i] != 0) g[i] = 0;
            foreach (var kv in hot)
            {
                int i = map.cellIndices.CellToIndex(kv.Key);
                if (i >= 0 && i < g.Length && g[i] < kv.Value) g[i] = kv.Value;
            }
            onA = !onA;
            any = hot.Count > 0;
        }

        public override void MapRemoved()
        {
            base.MapRemoved();
            if (!made) return;
            made = false; any = false;
            if (a.IsCreated) a.Dispose();
            if (b.IsCreated) b.Dispose();
        }
    }

    /// <summary>Every path a pawn asks for: if it has no ward, the dangerous ground of spells is laid on it as cost.</summary>
    [HarmonyPatch]
    public static class Patch_MagicDangerPath
    {
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(PathFinder)).Where(m => m.Name == nameof(PathFinder.CreateRequest)
                && m.GetParameters().Any(p => p.Name == "customizer") && m.GetParameters().Any(p => p.Name == "pawn"));

        public static void Prefix(Pawn pawn, ref PathRequest.IPathGridCustomizer customizer)
        {
            if (customizer != null || pawn == null) return;
            try { customizer = MapComponent_MagicDanger.For(pawn); } catch { }
        }
    }
}
