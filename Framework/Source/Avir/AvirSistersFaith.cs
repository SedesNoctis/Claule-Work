using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE FAITH OF THE SISTERS (stage 2): the celestial diarchy structure and the dualism of light. Defs live in
    /// Compat/Ideology (loaded with Ideology only); everything here looks them up by name and stays silent without
    /// them. Most thoughts touch only the Avir themselves - a human who takes up the Sisters gains nothing by it.
    /// </summary>
    public static class AvirSisters
    {
        public const string StructureName = "Avir_Structure_CelestialDiarchy";

        private static PreceptDef shiai, schial, both;
        private static bool looked;

        private static void Look()
        {
            if (looked) return;
            looked = true;
            shiai = DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_Faith_Shiailism");
            schial = DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_Faith_Schialism");
            both = DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_Faith_Both");
        }

        public static bool IsSisters(Ideo ideo) => ideo?.StructureMeme?.defName == StructureName;

        public static bool Shiailist(Ideo ideo) { Look(); return ideo != null && shiai != null && ideo.HasPrecept(shiai); }
        public static bool Schialist(Ideo ideo) { Look(); return ideo != null && schial != null && ideo.HasPrecept(schial); }
        public static bool BothSisters(Ideo ideo) { Look(); return ideo != null && both != null && ideo.HasPrecept(both); }

        /// <summary>A grown, awake Avir standing under the open sky.</summary>
        public static bool OutdoorsAwake(Pawn p) =>
            p.Spawned && p.Awake() && p.Map != null && !p.Position.Roofed(p.Map) && AvirRhythmUtility.IsAvir(p);

        public static float Glow(Pawn p) => p.Map.skyManager.CurSkyGlow;
    }

    // ------------------------------------------------------------------ mood

    /// <summary>Shiailism: mastery. The best skill decides: 12+, 16+, 20.</summary>
    public class ThoughtWorker_AvirMastery : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (p.skills == null || !AvirRhythmUtility.IsAvir(p) || !p.DevelopmentalStage.Adult()) return false;
            int best = 0;
            foreach (SkillRecord s in p.skills.skills) if (!s.TotallyDisabled && s.Level > best) best = s.Level;
            if (best >= 20) return ThoughtState.ActiveAtStage(2);
            if (best >= 16) return ThoughtState.ActiveAtStage(1);
            if (best >= 12) return ThoughtState.ActiveAtStage(0);
            return false;
        }
    }

    /// <summary>Schialism: order. No leader - stage 0; every single role of the ideoligion held - stage 1.</summary>
    public class ThoughtWorker_AvirOrder : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (!AvirRhythmUtility.IsAvir(p) || !p.DevelopmentalStage.Adult() || p.Faction == null) return false;
            List<Precept_RoleSingle> roles = p.Ideo.PreceptsListForReading.OfType<Precept_RoleSingle>().Where(r => r.Active).ToList();
            Precept_RoleSingle leader = roles.FirstOrDefault(r => r.def.leaderRole);
            if (leader != null && leader.ChosenPawnValue == null) return ThoughtState.ActiveAtStage(0);
            if (roles.Count > 0 && roles.All(r => r.ChosenPawnValue != null)) return ThoughtState.ActiveAtStage(1);
            return false;
        }
    }

    /// <summary>Under the open sun (moonlight followers suffer it, flame followers love it).</summary>
    public class ThoughtWorker_AvirUnderSun : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p) => AvirSisters.OutdoorsAwake(p) && AvirSisters.Glow(p) >= 0.6f;
    }

    /// <summary>Under the night sky.</summary>
    public class ThoughtWorker_AvirUnderNight : ThoughtWorker_Precept
    {
        /// <summary>Stage 0 - in Her night (the open night sky); stage 1 - in Her dark light (a darklight shines where
        /// the pawn stands, indoors or out). Ordinary light costs nothing.</summary>
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (AvirSisters.OutdoorsAwake(p) && AvirSisters.Glow(p) <= 0.3f) return ThoughtState.ActiveAtStage(0);
            if (p.Spawned && p.Awake() && AvirRhythmUtility.IsAvir(p) && DarklightUtility.IsDarklightAt(p.Position, p.Map)) return ThoughtState.ActiveAtStage(1);
            return false;
        }
    }

    /// <summary>Each in her hour: the night-born content under the night sky, the day-born under the sun.</summary>
    public class ThoughtWorker_AvirOwnHour : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (!AvirSisters.OutdoorsAwake(p)) return false;
            float g = AvirSisters.Glow(p);
            bool day = g >= 0.6f, night = g <= 0.3f;
            if (!day && !night) return false;
            bool nocturnal = AvirRhythmUtility.IsNocturnal(p);
            return ThoughtState.ActiveAtStage(nocturnal == night ? 1 : 0);
        }
    }

    /// <summary>A lit fire within six cells: a hearth, a brazier, a torch - anything burning with a flame of its own.</summary>
    public class ThoughtWorker_AvirNearFlame : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (!p.Spawned || !p.Awake() || !AvirRhythmUtility.IsAvir(p)) return false;
            Map map = p.Map;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(p.Position, 6f, true))
            {
                if (!c.InBounds(map)) continue;
                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (!(things[i] is ThingWithComps t) || t.GetComp<CompFireOverlayBase>() == null) continue;
                    CompRefuelable fuel = t.GetComp<CompRefuelable>();
                    if (fuel != null && !fuel.HasFuel) continue;
                    if (GenSight.LineOfSight(p.Position, t.Position, map)) return true;
                }
            }
            return false;
        }
    }

    /// <summary>A grown Avir without an amulet on.</summary>
    public class ThoughtWorker_AvirNoAmulet : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p) =>
            AvirRhythmUtility.IsAvir(p) && p.DevelopmentalStage.Adult() && p.apparel != null && Artifactorics.AmuletDevelopment.WornBy(p) == null;
    }

    // ------------------------------------------------------------------ opinion

    /// <summary>The followers of each Sister hate the other's: shiailists hate those who deny the Younger
    /// (schialists), schialists hate those who kneel to her (shiailists and the followers of both); and all of them
    /// hate the apostates.</summary>
    public class ThoughtWorker_AvirFaithFeud : ThoughtWorker_Precept_Social
    {
        protected override ThoughtState ShouldHaveThought(Pawn p, Pawn other)
        {
            Ideo mine = p.Ideo, theirs = other.Ideo;
            if (mine == null || theirs == null || mine == theirs || !AvirRhythmUtility.IsAvir(p)) return false;
            if (GameComponent_AvirApostasy.IsApostate(theirs) && !GameComponent_AvirApostasy.IsApostate(mine)
                && (AvirSisters.Shiailist(mine) || AvirSisters.Schialist(mine) || AvirSisters.BothSisters(mine))) return true;   // every Sister's follower hates an apostate
            if (AvirSisters.Shiailist(mine)) return AvirSisters.Schialist(theirs) || AvirSisters.BothSisters(theirs);
            if (AvirSisters.Schialist(mine)) return AvirSisters.Shiailist(theirs) || AvirSisters.BothSisters(theirs);
            return false;
        }
    }

    /// <summary>Whatever is not Avir is less. No mood for slaves of other races on the map - only opinion.</summary>
    public class ThoughtWorker_AvirLesserRace : ThoughtWorker_Precept_Social
    {
        protected override ThoughtState ShouldHaveThought(Pawn p, Pawn other) =>
            other.RaceProps.Humanlike && AvirRhythmUtility.IsAvir(p) && !AvirBody.IsAvir(other) && !AvirRhythmUtility.IsAvir(other)
            && !AvirSistersDetails.HarSpeaksForRaces(p.Ideo);
    }

    // ------------------------------------------------------------------ the Sisters themselves

    /// <summary>The two deities of the diarchy are always the Sisters, the Elder first, both female - the random
    /// pick from fixedDeityNameTypes could name one twice or leave one out.</summary>
    [HarmonyPatch(typeof(IdeoFoundation_Deity), nameof(IdeoFoundation_Deity.GenerateDeities))]
    public static class Patch_AvirSistersDeities
    {
        public static void Postfix(IdeoFoundation_Deity __instance)
        {
            MemeDef s = __instance.ideo?.StructureMeme;
            if (s == null || s.defName != AvirSisters.StructureName || s.fixedDeityNameTypes == null) return;
            List<IdeoFoundation_Deity.Deity> list = __instance.DeitiesListForReading;
            for (int i = 0; i < list.Count && i < s.fixedDeityNameTypes.Count; i++)
            {
                list[i].name = s.fixedDeityNameTypes[i].name;
                list[i].type = s.fixedDeityNameTypes[i].type;
                list[i].gender = Gender.Female;
            }
        }
    }

    /// <summary>The Avir worship no one but the Sisters: they cannot be converted to a faith without them. The player's
    /// own faith is the one exception - it converts an Avir into its apostate copy instead (AvirApostasy.cs), which
    /// then shows itself as soon as the apostate stands on a map.</summary>
    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.IdeoConversionAttempt))]
    public static class Patch_AvirSistersNoConversion
    {
        public static bool Prefix(ref Ideo initiatorIdeo, Pawn ___pawn, ref bool __result)
        {
            if (___pawn == null || !AvirRhythmUtility.IsAvir(___pawn) || AvirSisters.IsSisters(initiatorIdeo)) return true;
            Ideo apostate = Faction.OfPlayerSilentFail?.ideos != null && Faction.OfPlayer.ideos.Has(initiatorIdeo)
                ? GameComponent_AvirApostasy.Instance?.CopyOf(initiatorIdeo) : null;
            if (apostate != null)
            {
                initiatorIdeo = apostate;
                return true;
            }
            __result = false;
            return false;
        }

        public static void Postfix(Pawn ___pawn, bool __result)
        {
            if (__result && ___pawn?.Ideo != null && ___pawn.Spawned && GameComponent_AvirApostasy.IsApostate(___pawn.Ideo))
                ___pawn.Ideo.hidden = false;
        }
    }

    /// <summary>Every grown Avir of the Sisters wears an amulet: the NPC factions are dressed by AvirFactionGear;
    /// this gives the player's own (starting colonists, and any adult generated for the player) a traditional
    /// amulet of their light - the A'Shaign's for moonlight, the A'Schialis' for the golden flame.</summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirSistersAmuletOnGeneration
    {
        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn p = __result;
                if (p?.apparel == null || p.Faction == null || !p.Faction.IsPlayer || !p.DevelopmentalStage.Adult()) return;
                if (!AvirRhythmUtility.IsAvir(p) || !AvirSisters.IsSisters(p.Ideo) || Artifactorics.AmuletDevelopment.WornBy(p) != null) return;
                if (p.apparel.WornApparel.Any(a => a.def.apparel.layers.Contains(Artifactorics.AvirAmuletLayerDefOf.Avir_AccessoryLayer))) return;
                PreceptDef flame = DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_Light_Flame");
                string kin = flame != null && p.Ideo.HasPrecept(flame) ? "Avir_AShialis" : "Avir_HighNesting";
                Artifactorics.AvirFactionGearDef d = DefDatabase<Artifactorics.AvirFactionGearDef>.AllDefsListForReading
                    .FirstOrDefault(g => g.faction?.defName == kin);
                if (d == null) return;
                Apparel piece = Artifactorics.AvirFactionGear.MakeAmulet(d.necklace, d.cores, null, QualityCategory.Normal);
                if (piece != null && ApparelUtility.HasPartsToWear(p, piece.def)) p.apparel.Wear(piece, false, false);
            }
            catch (System.Exception e) { Log.WarningOnce("[Avir] Sisters: amulet for a new colonist failed: " + e.Message, 0x51A7E1); }
        }
    }
}
