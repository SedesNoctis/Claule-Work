using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    // =====================================================================================
    // THE WILD GODS. A colony may come to worship a Singer - by its own choice, kneeling at a fire, or because a hidden
    // one has been singing to its sleepers night after night until they belong to her.
    //   The mark: "accepted her": glad while she lives and walks near; she never touches them; she can no longer be
    //     tamed or recruited - she is served, not owned; her death breaks every one of them.
    //   The ideoligion: the colony's ideoligion takes "the wild gods: worshipped" (with Ideology).
    //   The call of the wild gods: an ideoligion ritual at a campfire (see LinarianWildGodsRitual.cs).
    //   Leader: Goddess - the mightiest Singer among them leads the ideoligion.
    //   Her faithful feed her: when she is hungry, one of them walks out in their sleep with food (and still rests).
    // =====================================================================================
    public static class WildGods
    {
        private static HediffDef mark;
        public static HediffDef Mark => mark ?? (mark = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WildGodsMark"));
        public static PreceptDef Worshipped => DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_WildGods_Worshipped");
        public static PreceptDef Rejected => DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_WildGods_Rejected");

        public static Pawn GoddessOf(Pawn c) => LinarianSource.Of(c, Mark);

        public static bool Accepted(Pawn c, Pawn her) => her != null && GoddessOf(c) == her;

        public static bool Worships(Pawn c) => Worshipped != null && c.Ideo?.HasPrecept(Worshipped) == true;

        /// <summary>A Singer on the map who is not the player's - the one to kneel to.</summary>
        public static Pawn Present(Map map) =>
            map?.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !p.Dead && p.Faction != Faction.OfPlayer && Linarian.Is(p) && !p.IsPrisoner);

        public static void Accept(Pawn c, Pawn her, bool forced)
        {
            if (c == null || her == null || Mark == null || Accepted(c, her)) return;
            LinarianSource.Add(c, Mark, her, -1);
            if (forced) Messages.Message(c.LabelShortCap + " speaks of the wild goddess as if they had always known her.", c, MessageTypeDefOf.NegativeEvent, false);
            else Messages.Message(c.LabelShortCap + " has knelt to the wild goddess.", c, MessageTypeDefOf.NeutralEvent, false);
            TakeIntoIdeo(c.Ideo);
        }

        /// <summary>The ideoligion learns to worship the wild gods (and forgets rejecting them).</summary>
        public static void TakeIntoIdeo(Ideo ideo)
        {
            if (ideo == null || Worshipped == null || ideo.HasPrecept(Worshipped)) return;
            TakeStructure(ideo);
            foreach (Precept p in ideo.PreceptsListForReading.Where(p => p.def == Rejected).ToList()) ideo.RemovePrecept(p);
            ideo.AddPrecept(PreceptMaker.MakePrecept(Worshipped), true);
            Goddess.TakeIntoIdeo(ideo);
        }

        /// <summary>Only the structure of the ideoligion becomes the wild gods' - its name, precepts, rituals, style and
        /// culture all stay as they were. They are not taken into her people; she is taken into theirs.</summary>
        public static void TakeStructure(Ideo ideo)
        {
            MemeDef wild = DefDatabase<MemeDef>.GetNamedSilentFail("Avir_Structure_WildGods");
            MemeDef old = ideo.StructureMeme;
            if (wild == null || old == wild) return;
            if (old != null) ideo.memes.Remove(old);
            ideo.memes.Add(wild);
            ideo.SortMemesInDisplayOrder();
            if (ideo.foundation is IdeoFoundation_Deity fd) fd.DeitiesListForReading.Clear();   // gods without names
            // blood for the gods, as the structure has it - added beside the rituals they already keep
            foreach (string pat in new[] { "SacrificePrisoner", "SacrificeAnimal" })
            {
                RitualPatternDef pattern = DefDatabase<RitualPatternDef>.GetNamedSilentFail(pat);
                PreceptDef festival = DefDatabase<PreceptDef>.GetNamedSilentFail("Festival");
                if (pattern == null || festival == null || ideo.PreceptsListForReading.OfType<Precept_Ritual>().Any(r => r.sourcePattern == pattern)) continue;
                ideo.AddPrecept(PreceptMaker.MakePrecept(festival), true, null, pattern);
            }
            ideo.RecachePrecepts();
            Find.LetterStack.ReceiveLetter("The wild gods", ideo.name + " now kneels to the wild gods. Its name, its customs and its rites remain its own - only now there is a goddess walking the land, and she is listened to.", LetterDefOf.NeutralEvent);
        }

        /// <summary>Every ideoligion of the colony keeps the hidden kneeling ritual (its button shows only while a wild
        /// Singer is on the map), and the call of the wild gods can always be started at will.</summary>
        public static void EnsureRituals()
        {
            if (!ModsConfig.IdeologyActive || Faction.OfPlayerSilentFail?.ideos == null) return;
            PreceptDef kneel = DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_Ritual_KneelWildGoddess");
            RitualPatternDef kneelPattern = DefDatabase<RitualPatternDef>.GetNamedSilentFail("Avir_KneelWildGoddess");
            PreceptDef call = DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_Ritual_CallWildGods");
            foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos.ToList())
            {
                if (kneel != null && kneelPattern != null && !ideo.HasPrecept(kneel)) ideo.AddPrecept(PreceptMaker.MakePrecept(kneel), true, null, kneelPattern);
                foreach (Precept_Ritual r in ideo.PreceptsListForReading.OfType<Precept_Ritual>())
                    if (r.def == call && !r.isAnytime) r.isAnytime = true;
            }
        }

        /// <summary>Her faithful feed her: one asleep walks out with food while she is hungry.</summary>
        public static bool Feed(HeraldRecord r, Map map)
        {
            Pawn her = r.her;
            Need_Food food = her.needs?.food;
            if (food == null || food.CurLevelPercentage > 0.6f || r.peckTarget != null || r.raidTarget != null || her.CurJobDef == JobDefOf.Hunt) return false;
            Pawn c = map.mapPawns.FreeColonistsSpawned.Where(x => Accepted(x, her) && LinarianTactics.Asleep(x) && !x.Downed && x.MentalStateDef == null).RandomElementWithFallback();
            Thing meal = LinarianManners.Loot(map, false);
            return c != null && meal != null && LinarianManners.Bear(c, her, meal, r.lair.IsValid ? r.lair : her.Position, GenDate.TicksPerHour * 4);
        }
    }

    /// <summary>Glad while their goddess lives near.</summary>
    public class ThoughtWorker_WildGoddessNear : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Pawn her = WildGods.GoddessOf(p);
            return her != null && !her.Dead && her.MapHeld == p.MapHeld ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
        }
    }

    /// <summary>The precept: a Singer on the map - glad, or disgusted.</summary>
    public class ThoughtWorker_Precept_WildGods : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p) =>
            !Linarian.Is(p) && p.MapHeld != null && MapComponent_LinarianSong.HeraldsOn(p.MapHeld).Any() ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
    }

    /// <summary>Her death breaks every one who knelt to her.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_WildGoddessDies
    {
        public static void Prefix(Pawn __instance, out List<Pawn> __state)
        {
            __state = null;
            if (WildGods.Mark == null || !Linarian.Is(__instance)) return;
            __state = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Where(c => WildGods.Accepted(c, __instance)).ToList();
        }

        public static void Postfix(Pawn __instance, List<Pawn> __state)
        {
            if (__state == null || !__instance.Dead) return;
            ThoughtDef grief = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_WildGoddessDead");
            foreach (Pawn c in __state)
            {
                if (grief != null) c.needs?.mood?.thoughts?.memories?.TryGainMemory(grief);
                foreach (Hediff h in c.health.hediffSet.hediffs.Where(h => h.def == WildGods.Mark).ToList()) c.health.RemoveHediff(h);
                if (c.Spawned) c.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Sad, "the wild goddess is dead", true);
            }
        }
    }

    /// <summary>At a fire: kneel to the wild goddess.</summary>
    public class FloatMenuOptionProvider_WildGods : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn p = context?.FirstSelectedPawn;
            // with Ideology, kneeling is a ritual at the fire (Avir_KneelWildGoddess); this is only for the colonies without it
            if (ModsConfig.IdeologyActive) yield break;
            if (p == null || clickedThing?.def.defName != "Campfire" || !p.IsColonistPlayerControlled || Linarian.Is(p)) yield break;
            Pawn her = WildGods.Present(p.Map);
            JobDef kneel = DefDatabase<JobDef>.GetNamedSilentFail("Avir_KneelWildGoddess");
            if (her != null && p.Map.mapPawns.FreeColonistsSpawned.Any(c => !c.IsSlave && !Linarian.Is(c) && !WildGods.Accepted(c, her)))
            {
                if (!KneelGathering.CanStart(p.Map)) yield return new FloatMenuOption("Kneel to the wild goddess (they are gathering already)", null);
                else yield return new FloatMenuOption("Kneel to the wild goddess (the whole colony)", () => KneelGathering.Start(p, clickedThing, her));
            }
        }
    }

    public class JobDriver_KneelWildGoddess : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil wait = Toils_General.Wait(900, TargetIndex.A);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;
            yield return Toils_General.Do(() => WildGods.Accept(pawn, WildGods.Present(pawn.Map), false));
        }
    }
}

namespace Avir.Linarian
{
    /// <summary>The A'Shaign and the A'Schialis are born rejecting the wild gods - no one else can simply choose to.</summary>
    [HarmonyPatch]
    public static class Patch_WildGodsRejectedByOrders
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(IdeoGenerator), nameof(IdeoGenerator.GenerateIdeo));
        static bool Prepare() => TargetMethod() != null;

        public static void Postfix(Ideo __result, IdeoGenerationParms parms)
        {
            string f = parms.forFaction?.defName;
            if (__result == null || WildGods.Rejected == null) return;
            bool order = f == "Avir_HighNesting" || f == "Avir_AShialis";
            if (order && !__result.HasPrecept(WildGods.Rejected)) __result.AddPrecept(PreceptMaker.MakePrecept(WildGods.Rejected), true);
            if (!order) foreach (Precept p in __result.PreceptsListForReading.Where(p => p.def == WildGods.Rejected).ToList()) __result.RemovePrecept(p);
        }
    }
}
