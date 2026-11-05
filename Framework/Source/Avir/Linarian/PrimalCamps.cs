using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace Avir.Linarian
{
    // =====================================================================================
    // THE PRIMAL CAMPS. The Primal Hunt builds nothing: a camp is a stretch of the dusk woods where they sleep on the
    // ground, with no bed, no store, no fire - only the dead, people and beasts, at every stage of rot. They settle
    // only in the twilight and the burning forests.
    // =====================================================================================
    public static class PrimalCamps
    {
        public static bool IsPrimal(Faction f) => f?.def?.defName == "Avir_Primal";

        public static void Generate(IntVec3 c, Map map, Faction faction, bool pawns)
        {
            CellRect rect = CellRect.CenteredOn(c, 14).ClipInsideMap(map);
            int count = Rand.RangeInclusive(5, 11);
            List<PawnKindDef> beasts = map.Biome.AllWildAnimals.Where(a => a.RaceProps.Animal).ToList();
            for (int i = 0; i < count; i++)
            {
                if (!rect.TryFindRandomCell(out IntVec3 at, x => x.Standable(map) && x.GetFirstPawn(map) == null)) continue;
                PawnKindDef kind = Rand.Chance(0.45f) || beasts.Count == 0
                    ? new[] { PawnKindDefOf.Villager, PawnKindDefOf.Drifter, PawnKindDefOf.SpaceRefugee }.RandomElement()
                    : beasts.RandomElement();
                Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true));
                GenSpawn.Spawn(p, at, map);
                p.Kill(null, null);
                Corpse corpse = p.Corpse;
                CompRottable rot = corpse?.GetComp<CompRottable>();
                if (rot != null) rot.RotProgress = Rand.Range(0f, rot.PropsRot.TicksToDessicated * 1.1f);
                corpse?.SetForbidden(false, false);
                for (int b = 0; b < 3; b++) FilthMaker.TryMakeFilth(at + GenRadial.RadialPattern[Rand.Range(0, 9)], map, ThingDefOf.Filth_Blood);
            }
            if (!pawns || faction == null) return;
            var lord = LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, c, 180000), map);
            int n = Rand.RangeInclusive(4, 8);
            for (int i = 0; i < n; i++)
            {
                PawnKindDef kind = i == 0 && faction.def.fixedLeaderKinds?.Count > 0 ? faction.def.fixedLeaderKinds[0] : faction.def.basicMemberKind;
                if (kind == null || !rect.TryFindRandomCell(out IntVec3 at, x => x.Standable(map))) continue;
                Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, map.Tile));
                GenSpawn.Spawn(p, at, map);
                lord.AddPawn(p);
            }
        }
    }

    [HarmonyPatch(typeof(GenStep_Settlement), "ScatterAt")]
    public static class Patch_PrimalCamp
    {
        public static bool Prefix(IntVec3 c, Map map, GenStep_Settlement __instance)
        {
            Faction f = __instance.overrideFaction ?? (map.ParentFaction != Faction.OfPlayer ? map.ParentFaction : null);
            if (!PrimalCamps.IsPrimal(f)) return true;
            PrimalCamps.Generate(c, map, f, __instance.generatePawns);
            return false;
        }
    }

    // where they settle: AvirSettlementBiomeExtension on the faction (Faction_Primal.xml), honoured under Faction Control too
}
