using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // AVIR TRADITIONAL AMULET - stage 3: the steward caste (priest, guild trader, guild master,
    // inquisitor). Numbers at branch strength 1, provisional. Stat-only milestones (conversion,
    // suppression, negotiation) live in the subclass XML as milestoneStats; the Ideology ones
    // simply do not load without Ideology.
    //
    //   priest      9 rite            - rituals the priest takes part in: +8% quality
    //              12 peacemaker      - social fights near the priest (12 cells): x0.1 chance
    //              18 last rite       - a colonist on the priest's map who would die of wounds is
    //                                   kept alive, unconscious, bleeding stopped; once in 3 days
    //               slot: decree      - an ally neither breaks nor falls to pain for a day; 3 days
    //   trader      3 trader's eye    - buying what the colony lacks: -5%
    //               9 supplies        - a caravan with the trader: 15% faster, carries 15% more
    //              12 redistribution  - buying what the colony lacks: a further -10%
    //              15 guild network   - an extra trade caravan from a friendly faction every ~8 days
    //              18 stores          - trader stock: +50% food and medicine
    //               slot: weighed load - pawns of the trader's caravan carry 20% more
    //   guild master 3 word of the clutch - goodwill gains +25%
    //               9 patron          - quest rewards +20%
    //              12 guild charter   - goodwill does not drift down by itself
    //              15 mentor          - colonists within 9 cells learn 15% faster
    //              18 union of clutches - a threat at home: chance allies send fighters (5 days)
    //               slot: guarantor   - every 2 days the best builder or crafter: +30% work 12 h
    //   inquisitor  3 gaze            - recruitment and enslavement wear prisoners down 25% faster
    //              12 summary justice - its executions upset nobody
    //              15 keeper of faith - its own certainty falls half as fast
    //              18 law of the clutch - slave rebellions and prison breaks half as often, and
    //                                   rage, berserk and tantrum breaks too
    //               slot: sentence    - enemy: stun 3-5 s; prisoner: -resistance, -will; slave:
    //                                   +suppression; once in 7 days per target; 4 h
    // "What the colony lacks": food below 10 days, medicine below 2 per colonist.
    // =====================================================================================

    [DefOf]
    public static class AvirAmuletStewardDefOf
    {
        public static AmuletSubclassDef Avir_Subclass_Priest;
        public static AmuletSubclassDef Avir_Subclass_Trader;
        public static AmuletSubclassDef Avir_Subclass_Guildmaster;
        public static AmuletSubclassDef Avir_Subclass_Inquisitor;
        public static HediffDef Avir_AmuletLastRite;
        public static HediffDef Avir_AmuletDecree;
        public static HediffDef Avir_AmuletMentored;
        public static HediffDef Avir_AmuletGuarantor;
        public static HediffDef Avir_AmuletSentenced;
        static AvirAmuletStewardDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(AvirAmuletStewardDefOf)); }
    }

    public static class AmuletSteward
    {
        public const float RiteBonus = 0.08f;
        public const float PeacemakerRadius = 12f, PeacemakerFactor = 0.1f;
        public const int LastRiteRest = 3 * GenDate.TicksPerDay;
        public const float EyeDiscount = 0.05f, RedistributionDiscount = 0.10f, MaxDiscount = 0.5f;
        public const float SuppliesSpeed = 0.15f, SuppliesMass = 0.15f, WeighedLoad = 0.20f;
        public const int NetworkInterval = 8 * GenDate.TicksPerDay;
        public const float StoresFactor = 0.5f;
        public const float GoodwillBonus = 0.25f, PatronBonus = 0.20f;
        public const float MentorRadius = 9f;
        public const int UnionRest = 5 * GenDate.TicksPerDay;
        public const float UnionChance = 0.5f;
        public const int GuarantorInterval = 2 * GenDate.TicksPerDay;
        public const float GazeBonus = 0.25f;
        public const float FaithResist = 0.5f;
        public const float LawFactor = 2f;   // MTB x2 = half as often

        internal static int lastRiteReady;

        private static AmuletSubclassDef Priest => AvirAmuletStewardDefOf.Avir_Subclass_Priest;
        private static AmuletSubclassDef Trader => AvirAmuletStewardDefOf.Avir_Subclass_Trader;

        /// <summary>Food below 10 days, or medicine below 2 per colonist, on any home map.</summary>
        public static bool Lacks(ThingDef def)
        {
            if (def == null) return false;
            Map map = Find.AnyPlayerHomeMap;
            if (map == null) return false;
            int colonists = Mathf.Max(1, map.mapPawns.FreeColonistsCount);
            if (def.IsNutritionGivingIngestible && def.ingestible.HumanEdible)
                return map.resourceCounter.TotalHumanEdibleNutrition < colonists * 1.6f * 10f;
            if (def.IsMedicine)
            {
                int meds = map.resourceCounter.AllCountedAmounts.Where(kv => kv.Key.IsMedicine).Sum(kv => kv.Value);
                return meds < colonists * 2;
            }
            return false;
        }

        public static float PriceFactor(Pawn negotiator, ThingDef def)
        {
            int l = AmuletDevelopment.Level(negotiator, Trader, out float k);
            if (l < 3 || !Lacks(def)) return 1f;
            float d = EyeDiscount * k + (l >= 12 ? RedistributionDiscount * k : 0f);
            return 1f - Mathf.Min(MaxDiscount, d);
        }

        /// <summary>Caravan-wide trader bonuses: supplies (9) of any trader, weighed load of a trader whose own path it is.</summary>
        public static void CaravanTrader(IEnumerable<Pawn> pawns, out float supplies, out bool weighed)
        {
            supplies = 0f; weighed = false;
            foreach (Pawn p in pawns)
            {
                if (AmuletDevelopment.Level(p, Trader, out float k) >= 9) supplies = Mathf.Max(supplies, k);
                if (AmuletDevelopment.SlotIs(p, Trader)) weighed = true;
            }
        }

        public static bool PriestNear(Pawn p, int level, float radius)
        {
            if (p?.Map == null) return false;
            foreach (Pawn o in p.Map.mapPawns.FreeColonistsSpawned)
                if (AmuletDevelopment.Level(o, Priest) >= level && (o.Position - p.Position).LengthHorizontalSquared <= radius * radius)
                    return true;
            return false;
        }
    }

    // ---------------------------------------------------------------- priest

    [HarmonyPatch(typeof(RitualOutcomeEffectWorker_FromQuality), "GetQuality")]
    public static class Patch_AmuletRite
    {
        public static void Postfix(LordJob_Ritual jobRitual, ref float __result)
        {
            List<Pawn> pawns = jobRitual?.lord?.ownedPawns;
            if (pawns == null) return;
            float best = 0f;
            foreach (Pawn p in pawns)
                if (AmuletDevelopment.Level(p, AvirAmuletStewardDefOf.Avir_Subclass_Priest, out float k) >= 9) best = Mathf.Max(best, k);
            if (best > 0f) __result = Mathf.Min(1f, __result + AmuletSteward.RiteBonus * best);
        }
    }

    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.SocialFightChance))]
    public static class Patch_AmuletPeacemaker
    {
        private static readonly AccessTools.FieldRef<Pawn_InteractionsTracker, Pawn> pawnRef =
            AccessTools.FieldRefAccess<Pawn_InteractionsTracker, Pawn>("pawn");

        public static void Postfix(Pawn_InteractionsTracker __instance, Pawn initiator, ref float __result)
        {
            if (__result <= 0f) return;
            Pawn self = pawnRef(__instance);
            if (AmuletSteward.PriestNear(self, 12, AmuletSteward.PeacemakerRadius) || AmuletSteward.PriestNear(initiator, 12, AmuletSteward.PeacemakerRadius))
                __result *= AmuletSteward.PeacemakerFactor;
        }
    }

    /// <summary>Priest 18 - last rite: a colonist dying of wounds is kept alive, once in 3 days.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_AmuletLastRite
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn __instance, DamageInfo? __0)
        {
            Pawn p = __instance;
            try
            {
                if (p?.Map == null || p.Dead || !p.IsColonist || p.health == null) return true;
                if (__0.HasValue && __0.Value.Def == DamageDefOf.ExecutionCut) return true;
                // wounds only: a blow, or bleeding out - not hunger, age or disease
                bool wounds = __0.HasValue || p.health.hediffSet.BleedRateTotal > 0f
                              || p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss) != null;
                if (!wounds) return true;
                int now = Find.TickManager.TicksGame;
                if (now < AmuletSteward.lastRiteReady) return true;
                if (AmuletDevelopment.Best(p.Map, AvirAmuletStewardDefOf.Avir_Subclass_Priest, out int lvl, out _) == null || lvl < 18) return true;
                if (p.health.ShouldBeDeadFromRequiredCapacity() != null || p.health.ShouldBeDeadFromLethalDamageThreshold()) return true;

                // stop the bleeding and pull every lethal condition back from the edge
                foreach (Hediff h in p.health.hediffSet.hediffs.ToList())
                {
                    if (h.Bleeding) h.Tended(0.6f, 1f);
                    if (h.def.lethalSeverity > 0f && h.Severity >= h.def.lethalSeverity * 0.9f) h.Severity = h.def.lethalSeverity * 0.85f;
                }
                if (p.health.ShouldBeDead()) return true;   // could not be saved after all
                p.health.AddHediff(AvirAmuletStewardDefOf.Avir_AmuletLastRite);
                AmuletSteward.lastRiteReady = now + AmuletSteward.LastRiteRest;
                Messages.Message(p.LabelShort + " was held back from death by the last rite.", p, MessageTypeDefOf.PositiveEvent, false);
                return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Last rite error: " + e, 0x4176ab01);
                return true;
            }
        }
    }

    // ---------------------------------------------------------------- trader

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    public static class Patch_AmuletTraderPrice
    {
        public static void Postfix(Tradeable __instance, TradeAction action, ref float __result)
        {
            if (action != TradeAction.PlayerBuys || TradeSession.playerNegotiator == null) return;
            __result *= AmuletSteward.PriceFactor(TradeSession.playerNegotiator, __instance.ThingDef);
        }
    }

    [HarmonyPatch(typeof(CaravanTicksPerMoveUtility), nameof(CaravanTicksPerMoveUtility.GetTicksPerMove), new[] { typeof(Caravan), typeof(System.Text.StringBuilder) })]
    public static class Patch_AmuletCaravanSpeed
    {
        public static void Postfix(Caravan caravan, ref int __result)
        {
            if (caravan?.PawnsListForReading == null) return;
            AmuletSteward.CaravanTrader(caravan.PawnsListForReading, out float k, out _);
            if (k > 0f) __result = Mathf.Max(1, Mathf.RoundToInt(__result / (1f + AmuletSteward.SuppliesSpeed * k)));
        }
    }

    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    public static class Patch_AmuletCaravanMass
    {
        public static void Postfix(Pawn p, ref float __result)
        {
            Caravan c = p?.GetCaravan();
            if (c == null || __result <= 0f) return;
            AmuletSteward.CaravanTrader(c.PawnsListForReading, out float k, out bool weighed);
            float f = 1f + (k > 0f ? AmuletSteward.SuppliesMass * k : 0f) + (weighed ? AmuletSteward.WeighedLoad : 0f);
            __result *= f;
        }
    }

    [HarmonyPatch(typeof(ThingSetMaker_TraderStock), "Generate")]
    public static class Patch_AmuletTraderStores
    {
        public static void Postfix(List<Thing> outThings)
        {
            if (outThings == null || Current.ProgramState != ProgramState.Playing) return;
            if (AmuletDevelopment.Best(null, AvirAmuletStewardDefOf.Avir_Subclass_Trader, out int lvl, out _) == null || lvl < 18) return;
            foreach (Thing t in outThings.ToList())
            {
                ThingDef d = t.def;
                if (!(d.IsMedicine || (d.IsNutritionGivingIngestible && d.ingestible.HumanEdible)) || d.stackLimit <= 1) continue;
                int extra = Mathf.CeilToInt(t.stackCount * AmuletSteward.StoresFactor);
                if (extra <= 0) continue;
                Thing more = ThingMaker.MakeThing(d, t.Stuff);
                more.stackCount = extra;
                outThings.Add(more);
            }
        }
    }

    // ---------------------------------------------------------------- guild master

    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Patch_AmuletGoodwill
    {
        public static bool Prefix(Faction __instance, Faction other, ref int goodwillChange, HistoryEventDef reason, ref bool __result)
        {
            if (goodwillChange == 0 || other == null) return true;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || (__instance != player && other != player)) return true;
            Faction them = __instance == player ? other : __instance;
            AmuletSubclassDef gm = AvirAmuletStewardDefOf.Avir_Subclass_Guildmaster;
            if (AmuletDevelopment.Best(null, gm, out int lvl, out float k) == null) return true;

            if (reason == HistoryEventDefOf.ReachNaturalGoodwill)
            {
                // 12 - guild charter: goodwill with friends does not fall by itself
                if (lvl >= 12 && goodwillChange < 0 && !them.HostileTo(player)) { __result = false; return false; }
                return true;
            }
            if (lvl >= 3 && goodwillChange > 0)
                goodwillChange = Mathf.CeilToInt(goodwillChange * (1f + AmuletSteward.GoodwillBonus * k));
            return true;
        }
    }

    [HarmonyPatch(typeof(RewardsGenerator), nameof(RewardsGenerator.Generate), new[] { typeof(RewardsGeneratorParams), typeof(float) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
    public static class Patch_AmuletPatron
    {
        public static void Prefix(ref RewardsGeneratorParams parms)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (AmuletDevelopment.Best(null, AvirAmuletStewardDefOf.Avir_Subclass_Guildmaster, out int lvl, out float k) == null || lvl < 9) return;
            parms.rewardValue *= 1f + AmuletSteward.PatronBonus * k;
        }
    }

    // ---------------------------------------------------------------- inquisitor

    /// <summary>Inquisitor 3 - the warden's talks wear prisoners down faster.</summary>
    public static class Patch_AmuletGaze
    {
        public static void Prefix(Pawn initiator, Pawn recipient, out (float res, float will) __state)
        {
            __state = (recipient?.guest?.resistance ?? 0f, recipient?.guest?.will ?? 0f);
        }

        public static void Postfix(Pawn initiator, Pawn recipient, (float res, float will) __state)
        {
            if (recipient?.guest == null) return;
            if (AmuletDevelopment.Level(initiator, AvirAmuletStewardDefOf.Avir_Subclass_Inquisitor, out float k) < 3) return;
            float f = AmuletSteward.GazeBonus * k;
            float dr = __state.res - recipient.guest.resistance;
            float dw = __state.will - recipient.guest.will;
            if (dr > 0f) recipient.guest.resistance = Mathf.Max(0f, recipient.guest.resistance - dr * f);
            if (dw > 0f) recipient.guest.will = Mathf.Max(0f, recipient.guest.will - dw * f);
        }
    }

    [StaticConstructorOnStartup]
    public static class AmuletStewardPatcher
    {
        static AmuletStewardPatcher()
        {
            var h = new Harmony("Tenebris.Avir.AmuletSteward");
            foreach (string type in new[] { "RimWorld.InteractionWorker_RecruitAttempt", "RimWorld.InteractionWorker_EnslaveAttempt" })
            {
                var m = AccessTools.Method(AccessTools.TypeByName(type), "Interacted");
                if (m != null)
                    h.Patch(m, prefix: new HarmonyMethod(typeof(Patch_AmuletGaze), nameof(Patch_AmuletGaze.Prefix)),
                        postfix: new HarmonyMethod(typeof(Patch_AmuletGaze), nameof(Patch_AmuletGaze.Postfix)));
            }
        }
    }

    [HarmonyPatch(typeof(ThoughtUtility), nameof(ThoughtUtility.GiveThoughtsForPawnExecuted))]
    public static class Patch_AmuletSummaryJustice
    {
        public static bool Prefix(Pawn executioner) =>
            AmuletDevelopment.Level(executioner, AvirAmuletStewardDefOf.Avir_Subclass_Inquisitor) < 12;
    }

    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.IdeoConversionAttempt))]
    public static class Patch_AmuletKeeperOfFaith
    {
        private static readonly AccessTools.FieldRef<Pawn_IdeoTracker, Pawn> pawnRef = AccessTools.FieldRefAccess<Pawn_IdeoTracker, Pawn>("pawn");

        public static void Prefix(Pawn_IdeoTracker __instance, ref float certaintyReduction)
        {
            if (AmuletDevelopment.Level(pawnRef(__instance), AvirAmuletStewardDefOf.Avir_Subclass_Inquisitor) >= 15)
                certaintyReduction *= AmuletSteward.FaithResist;
        }
    }

    public static class AmuletLaw
    {
        public static bool On(Map map) =>
            map != null && AmuletDevelopment.Best(map, AvirAmuletStewardDefOf.Avir_Subclass_Inquisitor, out int l, out _) != null && l >= 18;

        private static HashSet<MentalBreakDef> criminal;
        public static HashSet<MentalBreakDef> Criminal => criminal ?? (criminal = new HashSet<MentalBreakDef>(
            new[] { "Berserk", "MurderousRage", "Tantrum", "TargetedTantrum", "BedroomTantrum", "Slaughterer", "Jailbreaker", "SadisticRage", "CorpseObsession" }
                .Select(n => DefDatabase<MentalBreakDef>.GetNamedSilentFail(n)).Where(d => d != null)));
    }

    [HarmonyPatch(typeof(SlaveRebellionUtility), nameof(SlaveRebellionUtility.InitiateSlaveRebellionMtbDays))]
    public static class Patch_AmuletLawSlaves
    {
        public static void Postfix(Pawn pawn, ref float __result) { if (__result > 0f && AmuletLaw.On(pawn?.MapHeld)) __result *= AmuletSteward.LawFactor; }
    }

    [HarmonyPatch(typeof(PrisonBreakUtility), nameof(PrisonBreakUtility.InitiatePrisonBreakMtbDays))]
    public static class Patch_AmuletLawPrison
    {
        public static void Postfix(Pawn pawn, ref float __result) { if (__result > 0f && AmuletLaw.On(pawn?.MapHeld)) __result *= AmuletSteward.LawFactor; }
    }

    [HarmonyPatch(typeof(MentalBreakWorker), nameof(MentalBreakWorker.CommonalityFor))]
    public static class Patch_AmuletLawBreaks
    {
        public static void Postfix(MentalBreakWorker __instance, Pawn pawn, ref float __result)
        {
            if (__result > 0f && AmuletLaw.Criminal.Contains(__instance.def) && AmuletLaw.On(pawn?.MapHeld)) __result /= AmuletSteward.LawFactor;
        }
    }

    // ---------------------------------------------------------------- timers: network, mentor, union, guarantor

    public class GameComponent_AvirAmuletSteward : GameComponent
    {
        private int networkNext = -1, unionReady, guarantorNext = -1;
        private readonly HashSet<int> threatened = new HashSet<int>();

        public GameComponent_AvirAmuletSteward(Game game) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref networkNext, "networkNext", -1);
            Scribe_Values.Look(ref unionReady, "unionReady");
            Scribe_Values.Look(ref guarantorNext, "guarantorNext", -1);
            Scribe_Values.Look(ref AmuletSteward.lastRiteReady, "lastRiteReady");
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0) return;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                Mentor(map);
                Union(map, now);
                if (now % 2500 == 0) Guarantor(map, now);
            }
            if (now % 2500 == 0) Network(now);
        }

        private static void Mentor(Map map)
        {
            AmuletSubclassDef gm = AvirAmuletStewardDefOf.Avir_Subclass_Guildmaster;
            foreach (Pawn m in map.mapPawns.FreeColonistsSpawned)
            {
                if (AmuletDevelopment.Level(m, gm) < 15 || m.Downed) continue;
                foreach (Pawn o in map.mapPawns.FreeColonistsSpawned)
                    if (o != m && (o.Position - m.Position).LengthHorizontalSquared <= AmuletSteward.MentorRadius * AmuletSteward.MentorRadius)
                        AmuletElder.GiveTimed(o, AvirAmuletStewardDefOf.Avir_AmuletMentored, 150);
            }
        }

        private void Union(Map map, int now)
        {
            bool threat = GenHostility.AnyHostileActiveThreatToPlayer(map);
            if (!threat) { threatened.Remove(map.uniqueID); return; }
            if (!threatened.Add(map.uniqueID) || now < unionReady) return;
            if (AmuletDevelopment.Best(map, AvirAmuletStewardDefOf.Avir_Subclass_Guildmaster, out int lvl, out float k) == null || lvl < 18) return;
            if (!Rand.Chance(Mathf.Min(1f, AmuletSteward.UnionChance * k))) return;
            Faction ally = Find.FactionManager.AllFactionsVisible
                .Where(f => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction && f.PlayerRelationKind == FactionRelationKind.Ally).RandomElementWithFallback();
            if (ally == null) return;
            IncidentParms parms = new IncidentParms { target = map, faction = ally, points = StorytellerUtility.DefaultThreatPointsNow(map) };
            if (IncidentDefOf.RaidFriendly.Worker.TryExecute(parms)) unionReady = now + AmuletSteward.UnionRest;
        }

        private void Guarantor(Map map, int now)
        {
            if (guarantorNext >= 0 && now < guarantorNext) return;
            bool any = map.mapPawns.FreeColonistsSpawned.Any(p => AmuletDevelopment.SlotIs(p, AvirAmuletStewardDefOf.Avir_Subclass_Guildmaster));
            if (!any) return;
            Pawn best = map.mapPawns.FreeColonistsSpawned.Where(p => p.skills != null && !p.Downed)
                .OrderByDescending(p => Mathf.Max(p.skills.GetSkill(SkillDefOf.Construction).Level, p.skills.GetSkill(SkillDefOf.Crafting).Level))
                .FirstOrDefault();
            if (best == null) return;
            AmuletElder.GiveTimed(best, AvirAmuletStewardDefOf.Avir_AmuletGuarantor, GenDate.TicksPerHour * 12);
            guarantorNext = now + AmuletSteward.GuarantorInterval;
            Messages.Message(best.LabelShort + " works under the guild master's guarantee.", best, MessageTypeDefOf.PositiveEvent, false);
        }

        private void Network(int now)
        {
            if (networkNext >= 0 && now < networkNext) return;
            Pawn t = AmuletDevelopment.Best(null, AvirAmuletStewardDefOf.Avir_Subclass_Trader, out int lvl, out float k);
            if (t == null || lvl < 15) { networkNext = -1; return; }
            if (networkNext < 0) { networkNext = now + Mathf.RoundToInt(AmuletSteward.NetworkInterval / Mathf.Max(0.5f, k)); return; }
            networkNext = now + Mathf.RoundToInt(AmuletSteward.NetworkInterval / Mathf.Max(0.5f, k));
            Map map = t.Map;
            Faction f = Find.FactionManager.AllFactionsVisible
                .Where(x => !x.IsPlayer && !x.defeated && !x.HostileTo(Faction.OfPlayer) && !x.def.caravanTraderKinds.NullOrEmpty()).RandomElementWithFallback();
            if (map == null || f == null) return;
            IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(new IncidentParms { target = map, faction = f });
        }
    }

    // ---------------------------------------------------------------- slot abilities

    public class CompProperties_AmuletDecree : CompProperties_AbilityEffect
    {
        public CompProperties_AmuletDecree() { compClass = typeof(CompAbilityEffect_AmuletDecree); }
    }

    public class CompAbilityEffect_AmuletDecree : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!(target.Thing is Pawn t) || t.HostileTo(parent.pawn) || !t.RaceProps.Humanlike)
            {
                if (throwMessages) Messages.Message("A decree is for one of the clutch.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (target.Thing is Pawn t) AmuletElder.GiveTimed(t, AvirAmuletStewardDefOf.Avir_AmuletDecree, GenDate.TicksPerDay);
        }
    }

    public class CompProperties_AmuletSentence : CompProperties_AbilityEffect
    {
        public CompProperties_AmuletSentence() { compClass = typeof(CompAbilityEffect_AmuletSentence); }
    }

    public class CompAbilityEffect_AmuletSentence : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn t = target.Thing as Pawn;
            bool ok = t != null && (t.HostileTo(parent.pawn) || t.IsPrisonerOfColony || t.IsSlaveOfColony);
            if (!ok)
            {
                if (throwMessages) Messages.Message("Only an enemy, a prisoner or a slave can be sentenced.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (t.health.hediffSet.HasHediff(AvirAmuletStewardDefOf.Avir_AmuletSentenced))
            {
                if (throwMessages) Messages.Message(t.LabelShort + " was sentenced less than 7 days ago.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (!(target.Thing is Pawn t)) return;
            float k = Mathf.Max(1f, AmuletDevelopment.Strength(AmuletDevelopment.WornBy(parent.pawn), AvirAmuletStewardDefOf.Avir_Subclass_Inquisitor, parent.pawn));
            if (t.IsPrisonerOfColony && t.guest != null)
            {
                t.guest.resistance = Mathf.Max(0f, t.guest.resistance - 4f * k);
                t.guest.will = Mathf.Max(0f, t.guest.will - 0.5f * k);
            }
            else if (t.IsSlaveOfColony)
            {
                Need_Suppression n = t.needs?.TryGetNeed<Need_Suppression>();
                if (n != null) n.CurLevel = Mathf.Min(1f, n.CurLevel + 0.3f * k);
            }
            else t.stances?.stunner?.StunFor(Mathf.RoundToInt(Mathf.Min(300f, 180f * k)), parent.pawn);
            AmuletElder.GiveTimed(t, AvirAmuletStewardDefOf.Avir_AmuletSentenced, GenDate.TicksPerDay * 7);
            FleckMaker.ThrowMicroSparks(t.DrawPos, t.Map);
        }
    }
}
