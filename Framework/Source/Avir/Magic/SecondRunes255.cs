using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>A wall of raw clay (Fix255, the Clay's Wall): raised at once by a caster of clay, it slumps away in two days -
    /// unless fire bakes it. Heat on it (fire beside it, a flame that strikes it) bakes it a little at a time; baked through,
    /// it becomes a plain brick wall of our bricks, and stays.</summary>
    public class Building_ClayWall : Building
    {
        public int born = -1;
        public float baked;
        public const int Lasts = GenDate.TicksPerDay * 2;
        public const float BakedThrough = 30f;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (born < 0) born = Find.TickManager.TicksGame;
        }

        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostApplyDamage(dinfo, totalDamageDealt);
            if (Destroyed || dinfo.Def == null) return;
            if (dinfo.Def == DamageDefOf.Flame || dinfo.Def == DamageDefOf.Burn) Bake(dinfo.Amount);
        }

        // a wall of clay does not burn - fire only bakes it
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PreApplyDamage(ref dinfo, out absorbed);
            if (dinfo.Def == DamageDefOf.Flame || dinfo.Def == DamageDefOf.Burn) { Bake(dinfo.Amount); absorbed = true; }
        }

        public override void TickRare()
        {
            base.TickRare();
            if (!Spawned) return;
            // fire beside it bakes it
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(this))
                if (c.InBounds(Map) && c.GetThingList(Map).Any(t => t is Fire)) { Bake(2f); if (Destroyed) return; }
            if (Map.mapTemperature.OutdoorTemp > 200f || Position.GetTemperature(Map) > 200f) { Bake(2f); if (Destroyed) return; }
            if (Find.TickManager.TicksGame - born > Lasts)
            {
                // it slumps away: a heap of mud where it stood
                Map m = Map; IntVec3 at = Position;
                Destroy(DestroyMode.Vanish);
                FilthMaker.TryMakeFilth(at, m, ThingDefOf.Filth_Dirt, 2);
                FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), m, 1.2f, new Color(0.6f, 0.4f, 0.28f, 0.6f));
            }
        }

        public void Bake(float heat)
        {
            if (Destroyed || !Spawned) return;
            baked += heat;
            if (Rand.Chance(0.3f)) FleckMaker.ThrowSmoke(DrawPos, Map, 0.7f);
            if (baked < BakedThrough) return;
            ThingDef brick = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Brick");
            Map m = Map; IntVec3 at = Position; Faction f = Faction;
            Destroy(DestroyMode.Vanish);
            if (brick == null || ThingDefOf.Wall == null) return;
            Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, brick);
            if (f != null) wall.SetFaction(f);
            GenSpawn.Spawn(wall, at, m);
            FleckMaker.ThrowFireGlow(at.ToVector3Shifted(), m, 1f);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            int left = Lasts - (Find.TickManager.TicksGame - born);
            string mine = "Baked " + Mathf.Clamp01(baked / BakedThrough).ToStringPercent() + " (fire bakes it into brick)" + (left > 0 ? "; slumps away in " + left.ToStringTicksToPeriod() : "");
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref born, "born", -1);
            Scribe_Values.Look(ref baked, "baked");
        }
    }

    /// <summary>Developer actions for the remade meetings of three (Fix255).</summary>
    public static class SecondDev255
    {
        private static SecondFields F => Find.CurrentMap?.GetComponent<SecondFields>();
        private static Pawn Mage => Find.CurrentMap?.mapPawns.FreeColonistsSpawned.FirstOrDefault();
        private static List<IntVec3> Round(float r) => GenRadial.RadialCellsAround(Verse.UI.MouseCell(), r, true).Where(x => x.InBounds(Find.CurrentMap)).ToList();

        [DebugAction("Magic", "Rainbow: a held bow from the first colonist to here", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Bow()
        {
            SecondFields f = F; Pawn m = Mage; if (f == null || m == null) return;
            IntVec3 to = Verse.UI.MouseCell();
            f.Add(SecondKind.Rainbow, GenSight.BresenhamCellsBetween(m.Position, to).SelectMany(x => GenRadial.RadialCellsAround(x, 1.5f, true)).Where(x => x.InBounds(m.Map)).Distinct().ToList(), GenDate.TicksPerHour * 2, m, 1f);
            SecondFields.Fld fl = f.fields[f.fields.Count - 1];
            fl.held = true; fl.arcFrom = m.Position.ToVector3Shifted(); fl.arcTo = to.ToVector3Shifted();
        }

        [DebugAction("Magic", "Marsh haze: held and creeping, here", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Haze()
        {
            SecondFields f = F; if (f == null) return;
            f.Add(SecondKind.MarshHaze, Round(3.5f), GenDate.TicksPerHour * 2, Mage, 1f);
            SecondFields.Fld fl = f.fields[f.fields.Count - 1];
            fl.held = true; fl.grow = true; fl.startCells = fl.cells.Count;
        }

        [DebugAction("Magic", "Dry wind: a held drought here (2h)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Drought()
        {
            SecondFields f = F; if (f == null) return;
            f.Add(SecondKind.DryWind, Round(4.5f), GenDate.TicksPerHour * 2, Mage, 1f);
            f.fields[f.fields.Count - 1].held = true;
        }

        [DebugAction("Magic", "Clay: a raw clay wall (5 cells) here", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Wall()
        {
            Map map = Find.CurrentMap; ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ClayWall"); if (map == null || d == null) return;
            IntVec3 c = Verse.UI.MouseCell();
            for (int i = -2; i <= 2; i++) { IntVec3 x = c + new IntVec3(i, 0, 0); if (x.InBounds(map) && x.Standable(map) && x.GetFirstPawn(map) == null) GenSpawn.Spawn(d, x, map); }
        }

        [DebugAction("Magic", "Clay: bake the clay wall under the mouse", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BakeHere() => (Verse.UI.MouseCell().GetEdifice(Find.CurrentMap) as Building_ClayWall)?.Bake(Building_ClayWall.BakedThrough);
    }
}
