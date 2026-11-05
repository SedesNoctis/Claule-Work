using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// THE FAITH OF THE SISTERS - details: the hunt, other races, the Primal, the factions' own ideoligions.
    ///   the hunt (essential): the bigger and more dangerous the prey, the better the hunt; a hunted person is
    ///     better still; field and herd work shames (a potted plant is not a field, a pet is not a herd, and
    ///     slaughter is only the end of a hunt);
    ///   other races: lesser (-20 opinion), equals (nothing), prey (opinion of a non-Avir is always 0 - unless the
    ///     Avir is even-handed or kind - and killing a wild one or a stranger of a neutral faction is the sweetest
    ///     thing there is);
    ///   the Primal Hunt: no Lords, only a Matriarch or a Patriarch;
    ///   the factions' ideoligions: name, members, description and the exact precepts, set after generation
    ///     (requiredMemes can force memes, but the game picks precepts at random).
    /// </summary>
    public static class AvirSistersDetails
    {
        /// <summary>Natures that can still come to care for outsiders: prey-minded opinion does not touch them
        /// (the even-handed, the kind, and the outkind who can love beyond the species).</summary>
        public static readonly HashSet<string> PreyExemptTraits = new HashSet<string> { "Avir_CircleMinded", "Kind", "Avir_Outkind" };

        public static bool PreyExempt(Pawn p) =>
            p?.story?.traits != null && p.story.traits.allTraits.Any(t => !t.Suppressed && PreyExemptTraits.Contains(t.def.defName));

        /// <summary>How big a hunt it was: by the prey's combat power (0 small, 1 good, 2 great, 3 apex).</summary>
        public static int HuntStage(Pawn prey)
        {
            float cp = prey?.kindDef?.combatPower ?? 0f;
            return cp >= 200f ? 3 : cp >= 100f ? 2 : cp >= 40f ? 1 : 0;
        }

        public static bool Has(Ideo ideo, string preceptDefName) =>
            ideo != null && ideo.PreceptsListForReading.Any(p => p.def.defName == preceptDefName);

        public static bool IsPrimal(Ideo ideo) => ideo != null && ideo.memes.Any(m => m.defName == "Avir_PrimalHunt");

        /// <summary>Humanoid Alien Races' own "alien races" precept, when set to anything but neutral, already speaks for
        /// other races - our lesser-races opinion steps aside so the two do not stack.</summary>
        public static bool HarSpeaksForRaces(Ideo ideo) =>
            ideo != null && ideo.PreceptsListForReading.Any(p => p.def.defName.StartsWith("HAR_AlienRaces_") && p.def.defName != "HAR_AlienRaces_Standard");
    }

    /// <summary>When each pawn last hunted and last did field or herd work (ticks), kept with the game.</summary>
    public class GameComponent_AvirHunt : GameComponent
    {
        public static GameComponent_AvirHunt Instance;
        private Dictionary<int, int> lastHunt = new Dictionary<int, int>();
        private Dictionary<int, int> lastFarm = new Dictionary<int, int>();

        public GameComponent_AvirHunt(Game game) { Instance = this; }

        public void Hunted(Pawn p) => lastHunt[p.thingIDNumber] = Find.TickManager.TicksGame;
        public void Farmed(Pawn p) => lastFarm[p.thingIDNumber] = Find.TickManager.TicksGame;

        public int TicksSinceHunt(Pawn p)
        {
            int now = Find.TickManager.TicksGame;
            if (!lastHunt.TryGetValue(p.thingIDNumber, out int t)) { lastHunt[p.thingIDNumber] = now; return 0; }
            return now - t;
        }

        public bool FarmedRecently(Pawn p) =>
            lastFarm.TryGetValue(p.thingIDNumber, out int t) && Find.TickManager.TicksGame - t < GenDate.TicksPerDay;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref lastHunt, "avirLastHunt", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastFarm, "avirLastFarm", LookMode.Value, LookMode.Value);
            if (lastHunt == null) lastHunt = new Dictionary<int, int>();
            if (lastFarm == null) lastFarm = new Dictionary<int, int>();
            Instance = this;
        }
    }


    public class ThoughtWorker_AvirFarmerShame : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p) =>
            AvirRhythmUtility.IsAvir(p) && GameComponent_AvirHunt.Instance != null && GameComponent_AvirHunt.Instance.FarmedRecently(p);
    }

    /// <summary>Field and herd work: sowing and harvesting in a growing zone (a pot is no field), milking, shearing.</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_AvirFarmerShame
    {
        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition, Pawn ___pawn)
        {
            Job job = __instance.curJob;
            if (condition != JobCondition.Succeeded || job == null || ___pawn?.Map == null || !AvirRhythmUtility.IsAvir(___pawn)) return;
            JobDef d = job.def;
            bool field = (d == JobDefOf.Sow || d == JobDefOf.Harvest || d == JobDefOf.HarvestDesignated)
                         && job.targetA.Cell.IsValid && job.targetA.Cell.GetZone(___pawn.Map) is Zone_Growing;
            bool herd = d == JobDefOf.Milk || d == JobDefOf.Shear;   // slaughter is the end of a hunt, not herding
            if ((field || herd) && AvirSistersDetails.Has(___pawn.Ideo, "Avir_Hunt_Essential")) GameComponent_AvirHunt.Instance?.Farmed(___pawn);
        }
    }

    /// <summary>A kill by an Avir of the hunt: a wild animal or a wild man is a good hunt; for the prey-minded, a wild
    /// man or a stranger of a neutral faction "never saw it coming".</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_AvirHuntKill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, out object[] __state)
        {
            __state = null;
            if (!(dinfo?.Instigator is Pawn killer) || killer == __instance || !AvirRhythmUtility.IsAvir(killer) || killer.Ideo == null) return;
            Faction vf = __instance.Faction;
            bool wild = vf == null;
            bool neutralStranger = vf != null && !vf.IsPlayer && killer.Faction != null && vf != killer.Faction && !vf.HostileTo(killer.Faction);
            __state = new object[] { killer, wild, neutralStranger };
        }

        public static void Postfix(Pawn __instance, object[] __state)
        {
            if (__state == null || !__instance.Dead) return;
            Pawn killer = (Pawn)__state[0];
            bool wild = (bool)__state[1], neutralStranger = (bool)__state[2];
            if (killer.needs?.mood == null) return;
            bool humanlike = __instance.RaceProps.Humanlike;
            bool person = humanlike && !AvirBody.IsAvir(__instance);
            if (AvirSistersDetails.Has(killer.Ideo, "Avir_Hunt_Essential"))
            {
                // a person of another race is the finest hunt there is, whoever they fought for
                if (person)
                {
                    ThoughtDef hp = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_HuntedPerson");
                    if (hp != null) killer.needs.mood.thoughts.memories.TryGainMemory(ThoughtMaker.MakeThought(hp, AvirSistersDetails.HuntStage(__instance) >= 2 ? 1 : 0));
                    GameComponent_AvirHunt.Instance?.Hunted(killer);
                }
                else if (wild && __instance.RaceProps.Animal)
                {
                    ThoughtDef good = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_GoodHunt");
                    if (good != null) killer.needs.mood.thoughts.memories.TryGainMemory(ThoughtMaker.MakeThought(good, AvirSistersDetails.HuntStage(__instance)));
                    GameComponent_AvirHunt.Instance?.Hunted(killer);
                }
            }
            if (person && (wild || neutralStranger) && AvirSistersDetails.Has(killer.Ideo, "Avir_OtherRaces_Prey"))
            {
                ThoughtDef saw = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_PreyNeverSaw");
                if (saw != null) killer.needs.mood.thoughts.memories.TryGainMemory(saw);
            }
        }
    }

    /// <summary>Prey: one thinks nothing of it - an Avir of the prey-minded faith holds every non-Avir at exactly 0
    /// (the even-handed and the kind excepted).</summary>
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.OpinionOf))]
    public static class Patch_AvirPreyOpinion
    {
        public static void Postfix(Pawn other, Pawn ___pawn, ref int __result)
        {
            if (__result == 0 || other == null || ___pawn == null || !ModsConfig.IdeologyActive) return;
            if (!other.RaceProps.Humanlike || AvirBody.IsAvir(other) || !AvirRhythmUtility.IsAvir(___pawn) || AvirSistersDetails.PreyExempt(___pawn)) return;
            if (AvirSistersDetails.Has(___pawn.Ideo, "Avir_OtherRaces_Prey")) __result = 0;
        }
    }

    /// <summary>The Avir amulets are desired apparel only in the faith of the Sisters: hidden from every other
    /// ideoligion's apparel list.</summary>
    [HarmonyPatch(typeof(PreceptWorker_Apparel), nameof(PreceptWorker_Apparel.ShouldSkipThing))]
    public static class Patch_AvirAmuletDesireOnlySisters
    {
        public static void Postfix(Ideo ideo, ThingDef thingDef, ref bool __result)
        {
            if (__result || thingDef?.comps == null) return;
            if (thingDef.comps.Any(c => c is Artifactorics.CompProperties_AvirAmulet) && !AvirSisters.IsSisters(ideo)) __result = true;
        }
    }

    /// <summary>Night hunter: the vanilla darkness-combat offsets, but only for an Avir whose own rhythm is the night -
    /// a day-born Avir of the same faith fights as well in the light as in the dark.</summary>
    public class ConditionalStatAffecter_AvirNocturnal : ConditionalStatAffecter
    {
        public override string Label => "night-born";

        public override bool Applies(StatRequest req) => req.Thing is Pawn p && AvirRhythmUtility.IsNocturnal(p);
    }

    // ------------------------------------------------------------------ dating aliens (Humanoid Alien Races)
    // The Avir faiths hold dating aliens "horrible". That is the Avir's own disgust: a human of the faith, or an
    // outkind Avir, neither feels it nor refuses a shared bed for it.

    public static class AvirAlienDating
    {
        public static bool Spared(Pawn p) =>
            p != null && AvirSisters.IsSisters(p.Ideo) && (!AvirRhythmUtility.IsAvir(p) || AvirRomanceKind.IsOutkind(p));
    }

    [HarmonyPatch(typeof(ThoughtUtility), nameof(ThoughtUtility.CanGetThought))]
    public static class Patch_AvirAlienDatingThought
    {
        public static void Postfix(Pawn pawn, ThoughtDef def, ref bool __result)
        {
            if (__result && def != null && def.defName == "HAR_AlienDating_Horrible" && AvirAlienDating.Spared(pawn)) __result = false;
        }
    }

    [HarmonyPatch(typeof(PreceptComp_UnwillingToDo), nameof(PreceptComp_UnwillingToDo.MemberWillingToDo))]
    public static class Patch_AvirAlienDatingBed
    {
        public static void Postfix(HistoryEvent ev, ref bool __result)
        {
            if (__result || ev.def?.defName != "HAR_AlienDating_SharedBed") return;
            if (ev.args.TryGetArg(HistoryEventArgsNames.Doer, out Pawn doer) && AvirAlienDating.Spared(doer)) __result = true;
        }
    }

    // ------------------------------------------------------------------ the factions' own ideoligions

    /// <summary>On a FactionDef: its ideoligion's name, members, description and the precepts it must hold (entries for
    /// mods that are not loaded are dropped by MayRequire).</summary>
    public class AvirFactionIdeo : DefModExtension
    {
        public string ideoName;
        public string adjective;
        public string memberName;
        [MustTranslate] public string description;
        public List<PreceptDef> precepts = new List<PreceptDef>();
    }

    [HarmonyPatch(typeof(IdeoGenerator), nameof(IdeoGenerator.GenerateIdeo))]
    public static class Patch_AvirFactionIdeo
    {
        public static void Postfix(IdeoGenerationParms parms, Ideo __result)
        {
            AvirFactionIdeo ext = parms.forFaction?.GetModExtension<AvirFactionIdeo>();
            if (ext == null || __result == null) return;
            try { Apply(__result, ext); }
            catch (System.Exception e) { Log.Warning("[Avir] faction ideoligion for " + parms.forFaction.defName + ": " + e.Message); }
        }

        public static void Apply(Ideo ideo, AvirFactionIdeo ext)
        {
            foreach (PreceptDef d in ext.precepts)
            {
                if (d == null || ideo.HasPrecept(d)) continue;
                if (d.issue != null && !d.issue.allowMultiplePrecepts)
                    foreach (Precept old in ideo.PreceptsListForReading.Where(p => p.def.issue == d.issue).ToList())
                        ideo.RemovePrecept(old);
                ideo.AddPrecept(PreceptMaker.MakePrecept(d), true);
            }
            if (!ext.ideoName.NullOrEmpty()) ideo.name = ext.ideoName;
            if (!ext.adjective.NullOrEmpty()) ideo.adjective = ext.adjective;
            if (!ext.memberName.NullOrEmpty()) ideo.memberName = ext.memberName;
            if (!ext.description.NullOrEmpty()) { ideo.description = ext.description; ideo.descriptionLocked = true; }
            ideo.RecachePrecepts();
        }
    }
}
