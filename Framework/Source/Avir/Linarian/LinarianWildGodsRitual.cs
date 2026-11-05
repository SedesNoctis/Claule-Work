using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian
{
    // =====================================================================================
    // THE CALL OF THE WILD GODS - an ideoligion ritual at a campfire, meant to be kept every fifteen days.
    // Called sooner, it goes worse. Quality: the open sky over the fire, how many came, how beautiful the place is,
    // and how long the night has had to rest since the last call (a fire in a roofless room counts all three).
    //   A Singer on the land: her aria deepens by how well it went (every Singer there, the player's too).
    //   None: a good call may bring a kind one, who stays while she is worshipped. A poor one brings no one -
    //   or a Singer of the Primal Hunt: a cruel goddess, who sings the colony to her and does it harm.
    // =====================================================================================
    public abstract class RitualOutcomeComp_WildGodsBase : RitualOutcomeComp_Quality
    {
        public override bool Applies(LordJob_Ritual ritual) => true;

        protected abstract float CountAt(Precept_Ritual ritual, TargetInfo target);
        protected abstract string Shown(float n);

        public override float Count(LordJob_Ritual ritual, RitualOutcomeComp_Data data) => CountAt(ritual.Ritual, ritual.selectedTarget);

        public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget, RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
        {
            if (!ritualTarget.IsValid || ritualTarget.Map == null) return null;
            float n = CountAt(ritual, ritualTarget);
            float q = curve.Evaluate(n);
            return new QualityFactor
            {
                label = label.CapitalizeFirst(),
                count = Shown(n),
                qualityChange = ExpectedOffsetDesc(q >= 0f, q),
                quality = q,
                positive = q >= 0f,
                priority = 3f
            };
        }
    }

    /// <summary>The open sky over the fire: the share of unroofed ground around it.</summary>
    public class RitualOutcomeComp_WildGodsOpenSky : RitualOutcomeComp_WildGodsBase
    {
        protected override float CountAt(Precept_Ritual ritual, TargetInfo t)
        {
            if (t.Map == null) return 0f;
            int all = 0, open = 0;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(t.Cell, 4.9f, true))
            {
                if (!c.InBounds(t.Map)) continue;
                all++;
                if (!c.Roofed(t.Map)) open++;
            }
            return all == 0 ? 0f : (float)open / all;
        }

        protected override string Shown(float n) => n.ToStringPercent();
    }

    /// <summary>How beautiful the place of the call is.</summary>
    public class RitualOutcomeComp_WildGodsBeauty : RitualOutcomeComp_WildGodsBase
    {
        protected override float CountAt(Precept_Ritual ritual, TargetInfo t) => t.Map == null ? 0f : BeautyUtility.AverageBeautyPerceptible(t.Cell, t.Map);

        protected override string Shown(float n) => n.ToString("0.#");
    }

    /// <summary>Days since the last call: the wild gods want to be missed.</summary>
    public class RitualOutcomeComp_WildGodsRested : RitualOutcomeComp_WildGodsBase
    {
        protected override float CountAt(Precept_Ritual ritual, TargetInfo t) =>
            ritual == null || ritual.lastFinishedTick < 0 ? 15f : Mathf.Min(15f, (Find.TickManager.TicksGame - ritual.lastFinishedTick) / (float)GenDate.TicksPerDay);

        protected override string Shown(float n) => n >= 15f ? "15+" : n.ToString("0.#") + " / 15";
    }

    public class RitualOutcomeEffectWorker_WildGods : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_WildGods() { }
        public RitualOutcomeEffectWorker_WildGods(RitualOutcomeEffectDef def) : base(def) { }

        protected override void ApplyExtraOutcome(Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual, RitualOutcomePossibility outcome, out string extraOutcomeDesc, ref LookTargets letterLookTargets)
        {
            extraOutcomeDesc = null;
            Map map = jobRitual.Map;
            if (map == null) return;
            int i = outcome.positivityIndex;   // -2 terrible .. 2 best
            float q = i <= -2 ? 0.1f : i == -1 ? 0.35f : i == 1 ? 0.7f : i >= 2 ? 1f : 0.5f;
            List<Pawn> singers = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && Linarian.Is(p)).ToList();
            if (singers.Any())
            {
                foreach (Pawn her in singers) Linarian.Song(her)?.Learn(2400f * q);
                extraOutcomeDesc = "An Ar-Lin'Arie on this land heard the call. Her aria deepens.";
                return;
            }
            float kind = i <= -2 ? 0f : i == -1 ? 0.15f : i == 1 ? 0.55f : 0.9f;
            float cruel = i <= -2 ? 0.5f : i == -1 ? 0.3f : i == 1 ? 0.05f : 0f;
            float roll = Rand.Value;
            if (roll < kind)
            {
                Pawn came = LinarianPresence.Arrive(map, false);
                HeraldRecord r = came == null ? null : LinarianPresence.Record(came);
                if (r == null) return;
                r.habits = new List<string> { LinarianManners.Kind };
                r.devoted = true;
                extraOutcomeDesc = "Something answered. It walks at the edge of the firelight now, and it does not mean you harm.";
            }
            else if (roll < kind + cruel)
            {
                Pawn came = LinarianPresence.Arrive(map, true);
                HeraldRecord r = came == null ? null : LinarianPresence.Record(came);
                if (r == null) return;
                if (r.habits == null) r.habits = new List<string>();
                if (!r.habits.Contains(LinarianManners.Cult)) r.habits.Add(LinarianManners.Cult);
                extraOutcomeDesc = "Something answered. The beasts went quiet all at once, and no one at the fire wants to say what they felt looking back at them from the dark.";
            }
            else extraOutcomeDesc = "The night kept its silence.";
        }
    }

    // ---------------------------------------------------------------- kneeling to the wild goddess

    /// <summary>A campfire - but the button shows only while a wild Singer walks the map and someone free there has
    /// not yet knelt to her.</summary>
    public class RitualObligationTargetWorker_KneelCampfire : RitualObligationTargetWorker_ThingDef
    {
        public RitualObligationTargetWorker_KneelCampfire() { }
        public RitualObligationTargetWorker_KneelCampfire(RitualObligationTargetFilterDef def) : base(def) { }

        protected override RitualTargetUseReport CanUseTargetInternal(TargetInfo target, RitualObligation obligation)
        {
            if (!base.CanUseTargetInternal(target, obligation).canUse || target.Map == null) return false;
            Pawn her = WildGods.Present(target.Map);
            return her != null && target.Map.mapPawns.FreeColonistsSpawned.Any(c => !c.IsSlave && !Linarian.Is(c) && !WildGods.Accepted(c, her));
        }

        public override IEnumerable<string> GetTargetInfos(RitualObligation obligation)
        {
            yield return "a campfire, while a wild Ar-Lin'Arie walks the land";
        }
    }

    /// <summary>The one who sings: free, not a Singer herself; the moral guide first, then whoever speaks best.</summary>
    public class RitualRole_WildGoddessSinger : RitualRoleColonist
    {
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            if (!base.AppliesToPawn(p, out reason, selectedTarget, ritual, assignments, precept, skipReason)) return false;
            if (p.IsSlave || Linarian.Is(p))
            {
                reason = skipReason ? null : (Linarian.Is(p) ? "An Ar-Lin'Arie does not sing to herself." : "A slave's voice is not heard by the gods.");
                return false;
            }
            return true;
        }

        protected override int PawnDesirability(Pawn pawn) =>
            base.PawnDesirability(pawn) + (pawn.Ideo?.GetRole(pawn)?.def == PreceptDefOf.IdeoRole_Moralist ? 1000 : 0);
    }

    /// <summary>Everyone free who knelt to the end is hers. A ritual broken off takes no one.</summary>
    public class RitualOutcomeEffectWorker_KneelWildGoddess : RitualOutcomeEffectWorker
    {
        public RitualOutcomeEffectWorker_KneelWildGoddess() { }
        public RitualOutcomeEffectWorker_KneelWildGoddess(RitualOutcomeEffectDef def) : base(def) { }

        public override bool ShowQuality => false;

        public override void Apply(float progress, Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual)
        {
            if (jobRitual.cancelled || progress < 0.95f) return;
            Map map = jobRitual.Map;
            Pawn her = WildGods.Present(map);
            if (her == null)
            {
                Messages.Message("They knelt to an empty dark: the wild goddess was no longer there to see it.", jobRitual.selectedTarget, MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            List<Pawn> knelt = totalPresence.Keys.ToList();
            Pawn singer = jobRitual.assignments?.FirstAssignedPawn("singer");
            if (singer != null && !knelt.Contains(singer)) knelt.Add(singer);
            ThoughtDef together = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_KneltTogether");
            int n = 0;
            foreach (Pawn p in knelt)
            {
                if (p == null || p.Dead || p.IsSlave || p.IsPrisoner || !p.RaceProps.Humanlike || Linarian.Is(p)) continue;
                WildGods.Accept(p, her, false);
                if (together != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(together);
                n++;
            }
            if (n == 0) return;
            Linarian.Song(her)?.Learn(300f * n);   // every knee bent makes her stronger at once
            string by = singer != null ? singer.LabelShortCap + " sang to the dark, and " : "";
            Find.LetterStack.ReceiveLetter("Knelt to the wild goddess",
                by + n + " knelt at the fire and are hers now. Somewhere beyond the light, something winged settles, as if it had been waiting for exactly this.",
                LetterDefOf.RitualOutcomePositive, new LookTargets(knelt.Where(p => p.Spawned)));
        }
    }

    // ---------------------------------------------------------------- Leader: Goddess

    /// <summary>Leader: Goddess. The mightiest Singer among the faithful leads them, and no one else.</summary>
    public static class Goddess
    {
        public static PreceptDef Leader => DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_Leader_Goddess");

        /// <summary>With worship, the ideoligion's leadership passes to the Goddess, whatever it was before.</summary>
        public static void TakeIntoIdeo(Ideo ideo)
        {
            PreceptDef leader = Leader;
            if (ideo == null || leader == null) return;
            if (!ideo.HasPrecept(leader))
            foreach (Precept p in ideo.PreceptsListForReading.Where(p => p.def.issue != null && p.def.issue == leader.issue).ToList()) ideo.RemovePrecept(p);
            if (!ideo.HasPrecept(leader)) ideo.AddPrecept(PreceptMaker.MakePrecept(leader), true);
            // and the call of the wild gods comes with it
            PreceptDef call = DefDatabase<PreceptDef>.GetNamedSilentFail("Avir_Ritual_CallWildGods");
            RitualPatternDef pattern = DefDatabase<RitualPatternDef>.GetNamedSilentFail("Avir_CallWildGods");
            if (call != null && !ideo.HasPrecept(call)) ideo.AddPrecept(PreceptMaker.MakePrecept(call), true, null, pattern);
            Enforce();
        }

        public static bool Leads(Pawn p) => p.Ideo?.GetRole(p)?.def.leaderRole == true;

        public static Pawn Mightiest(IEnumerable<Pawn> pawns) =>
            pawns.Where(p => !p.Dead && Linarian.Is(p) && !p.IsPrisoner && !p.IsSlave).OrderByDescending(p => Linarian.Song(p)?.Power ?? 0f).FirstOrDefault();

        private static bool KeepsTheSisters(Ideo i) => i?.memes != null && i.memes.Any(m => m.defName == "Avir_Structure_CelestialDiarchy");

        /// <summary>Hands the leadership of the colony's ideoligion to the Goddess, if it has one.</summary>
        public static void Enforce()
        {
            Faction f = Faction.OfPlayerSilentFail;
            Ideo ideo = f?.ideos?.PrimaryIdeo;
            if (ideo == null || Leader == null || !ideo.HasPrecept(Leader)) return;
            // a Singer who keeps the faith of the Sisters is not taken from it: the crown goes to the mightiest of the rest
            Pawn her = Mightiest(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Where(p => p.Ideo == ideo || !KeepsTheSisters(p.Ideo)));
            if (her == null) return;
            Precept_RoleSingle role = ideo.PreceptsListForReading.OfType<Precept_RoleSingle>().FirstOrDefault(r => r.def.leaderRole);
            if (role == null || role.ChosenPawnSingle() == her) return;
            if (her.Ideo != ideo) her.ideo?.SetIdeo(ideo);
            role.Assign(her, true);
            Messages.Message(her.LabelShortCap + " is the Goddess now. She leads, because she is the mightiest of the Ar-Lin'Arie - as the faithful would have it.", her, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    public class GameComponent_WildGods : GameComponent
    {
        public GameComponent_WildGods(Game game) { }

        /// <summary>Favour of the wild gods: offerings made with no Singer on the land make one likelier to come.</summary>
        public float favour;

        /// <summary>The secret faith of the wild Singers (AvirFaith.WildSong).</summary>
        public Ideo wildSong;

        public static GameComponent_WildGods Instance => Current.Game?.GetComponent<GameComponent_WildGods>();

        public override void FinalizeInit() => WildGods.EnsureRituals();

        // once a day is plenty: the crown also changes hands at once when worship is taken up or a Singer joins
        public override void GameComponentTick()
        {
            if (!ModsConfig.IdeologyActive) return;
            int t = Find.TickManager.TicksGame;
            if (t % GenDate.TicksPerDay == 1234) Goddess.Enforce();
            if (t % GenDate.TicksPerHour == 777) WildGods.EnsureRituals();   // a new ideoligion may have come since
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref favour, "favour");
            Scribe_References.Look(ref wildSong, "wildSong");
        }
    }

    // ---------------------------------------------------------------- what the Singers think of it

    /// <summary>A Singer among those who worship the wild gods: she likes it, naturally.</summary>
    public class ThoughtWorker_SingerWorshipped : ThoughtWorker
    {
        // worshipped as a goddess: the one who leads them, or one who is not of the colony at all
        protected override ThoughtState CurrentStateInternal(Pawn p) =>
            Linarian.Is(p) && WildGods.Worships(p) && (p.Faction != Faction.OfPlayer || Goddess.Leads(p)) ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
    }

    /// <summary>One of the faithful's own: worshipped, and still one of the colony - not its head.</summary>
    public class ThoughtWorker_SingerAmongFaithful : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p) =>
            Linarian.Is(p) && WildGods.Worships(p) && p.Faction == Faction.OfPlayer && !Goddess.Leads(p) ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
    }

    /// <summary>A Singer among those who reject the wild gods.</summary>
    public class ThoughtWorker_SingerRejected : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p) =>
            Linarian.Is(p) && WildGods.Rejected != null && p.Ideo?.HasPrecept(WildGods.Rejected) == true ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
    }

    /// <summary>A Singer joining the colony may take the crown at once.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class Patch_GoddessJoins
    {
        public static void Postfix(Pawn __instance, Faction newFaction)
        {
            if (newFaction != null && newFaction.IsPlayer && ModsConfig.IdeologyActive && Linarian.Is(__instance)) Goddess.Enforce();
        }
    }

    // ---------------------------------------------------------------- the unique reward: an offering to the gods

    /// <summary>An offering to the gods: a Singer on the land grows stronger; with none, one becomes likelier to come.</summary>
    public class RitualAttachableOutcomeEffectWorker_OfferingToGods : RitualAttachableOutcomeEffectWorker
    {
        public override void Apply(Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual, RitualOutcomePossibility outcome, out string extraOutcomeDesc, ref LookTargets letterLookTargets)
        {
            extraOutcomeDesc = null;
            Map map = jobRitual.Map;
            if (map == null) return;
            bool best = outcome.BestPositiveOutcome(jobRitual);
            List<Pawn> singers = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && Linarian.Is(p)).ToList();
            if (singers.Any())
            {
                foreach (Pawn her in singers) Linarian.Song(her)?.Learn(best ? 2500f : 1500f);
                extraOutcomeDesc = "The gods took the offering. An Ar-Lin'Arie on this land grows stronger.";
                return;
            }
            GameComponent_WildGods c = GameComponent_WildGods.Instance;
            if (c == null) return;
            c.favour = Mathf.Min(4f, c.favour + (best ? 1.5f : 1f));
            extraOutcomeDesc = "The gods took the offering. One of them is likelier to come to this land now.";
        }
    }

    // ---------------------------------------------------------------- the structure of the wild gods

    /// <summary>Who may be born to the wild gods: the Primal Hunt, and rarely the wild and cruel tribes of the rim -
    /// the rough, the savage, the cannibal, the impids.</summary>
    [StaticConstructorOnStartup]
    public static class WildGodsStructure
    {
        static WildGodsStructure()
        {
            MemeDef m = DefDatabase<MemeDef>.GetNamedSilentFail("Avir_Structure_WildGods");
            if (m == null) return;
            if (m.factionWhitelist == null) m.factionWhitelist = new List<FactionDef>();
            foreach (FactionDef f in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                string n = f.defName;
                bool wild = n == "Avir_Primal" || n == "TribeRough" || n == "TribeSavage" || n == "TribeCannibal"
                    || n.IndexOf("impid", System.StringComparison.OrdinalIgnoreCase) >= 0 && f.techLevel <= TechLevel.Neolithic;
                if (wild && !m.factionWhitelist.Contains(f)) m.factionWhitelist.Add(f);
            }
        }
    }
}
