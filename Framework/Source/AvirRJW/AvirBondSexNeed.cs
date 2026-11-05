using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using rjw;
using Verse;

namespace AvirRJW
{
    /// <summary>
    /// NO SEX NEED UNTIL THE BOND.
    ///
    /// An Avir's body does not run its reproductive systems until it has bonded. In RJW terms:
    /// an unbonded Avir has no Sex need, no sex drive, and is never treated as wanting anything.
    /// Once bonded the need appears, and sex drive is multiplied - 2.5x males, 3.5x females by
    /// default, tuned on the Avir_PairBond gene (bondedLibidoMale / bondedLibidoFemale).
    ///
    /// WHY REMOVING THE NEED IS NOT ENOUGH ON ITS OWN. RJW's xxx.need_sex answers 0 - FRUSTRATED -
    /// for a pawn with no Sex need at all. Every RJW decision that asks "is this pawn frustrated"
    /// (masturbation, quickies, hookups, prisoner rape) would then fire constantly on exactly the
    /// pawns that should want nothing. So need_sex is also answered for them: 3, satisfied.
    ///
    /// "Bonded" is a living love partner - the same test the pair bond itself uses. A widow goes
    /// back to having nothing, which is what the book says happens.
    ///
    /// No reference to Avir.dll: the gene is found by name and the tuning read off its extension
    /// by field name, so the two assemblies cannot get out of step on load order.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirBondSexNeed
    {
        public const int Satisfied = 3;

        private static GeneDef pairBond;
        private static TraitDef splinter;
        private static float maleFactor = 2.5f;
        private static float femaleFactor = 3.5f;

        static AvirBondSexNeed()
        {
            if (!ModsConfig.IsActive("rim.job.world")) return;
            try
            {
                Init();
            }
            catch (Exception e)
            {
                Log.Error("[AvirRJW] bond sex-need setup failed: " + e);
            }
        }

        private static void Init()
        {
            pairBond = DefDatabase<GeneDef>.GetNamedSilentFail("Avir_PairBond");
            splinter = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_ManyMated");
            if (pairBond == null)
            {
                Log.Warning("[AvirRJW] Avir_PairBond not found; Sex need is not bond-gated.");
                return;
            }
            ReadTuning();

            Harmony h = new Harmony("user.avirrjw.bondsexneed");
            Patch(h, AccessTools.Method(typeof(Pawn_NeedsTracker), "ShouldHaveNeed"), nameof(ShouldHaveNeed_Postfix));
            Patch(h, AccessTools.Method(typeof(xxx), nameof(xxx.need_sex)), nameof(NeedSex_Postfix));
            Patch(h, AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation)), nameof(AddDirectRelation_Postfix));

            StatDef freq = DefDatabase<StatDef>.GetNamedSilentFail("SexFrequency");
            if (freq != null)
            {
                if (freq.parts == null) freq.parts = new List<StatPart>();
                StatPart_AvirBond part = new StatPart_AvirBond { parentStat = freq };
                freq.parts.Add(part);
            }
        }

        private static void Patch(Harmony h, MethodInfo target, string postfix)
        {
            if (target == null)
            {
                Log.Warning("[AvirRJW] bond sex-need: hook for " + postfix + " not found.");
                return;
            }
            try
            {
                h.Patch(target, postfix: new HarmonyMethod(typeof(AvirBondSexNeed), postfix));
            }
            catch (Exception e)
            {
                Log.Error("[AvirRJW] bond sex-need: could not patch " + target.Name + ": " + e);
            }
        }

        private static void ReadTuning()
        {
            if (pairBond.modExtensions == null) return;
            foreach (DefModExtension ext in pairBond.modExtensions)
            {
                if (ext == null || ext.GetType().Name != "AvirPairBondExtension") continue;
                Traverse t = Traverse.Create(ext);
                if (t.Field("bondedLibidoMale").FieldExists()) maleFactor = t.Field("bondedLibidoMale").GetValue<float>();
                if (t.Field("bondedLibidoFemale").FieldExists()) femaleFactor = t.Field("bondedLibidoFemale").GetValue<float>();
            }
        }

        // ------------------------------------------------------------------ who

        /// <summary>
        /// The gene, without the splinter-hearted trait. She keeps the ordinary Sex need and the
        /// ordinary sex drive, bonded or not.
        /// </summary>
        public static bool HasGene(Pawn pawn)
        {
            if (pairBond == null || pawn?.genes == null || !pawn.genes.HasActiveGene(pairBond)) return false;
            return splinter == null || pawn.story?.traits == null || !pawn.story.traits.HasTrait(splinter);
        }

        public static bool IsBonded(Pawn pawn)
        {
            return pawn?.relations != null && LovePartnerRelationUtility.HasAnyLovePartner(pawn, false);
        }

        /// <summary>An Avir whose body has not woken: the gene, and no living mate.</summary>
        public static bool IsDormant(Pawn pawn)
        {
            return HasGene(pawn) && !IsBonded(pawn);
        }

        public static float BondedFactor(Pawn pawn)
        {
            float f = pawn.gender == Gender.Female ? femaleFactor : maleFactor;
            return f > 0f ? f : 1f;
        }

        // ------------------------------------------------------------------ patches

        public static void ShouldHaveNeed_Postfix(NeedDef nd, Pawn ___pawn, ref bool __result)
        {
            if (!__result || nd == null || nd.defName != "Sex") return;
            try
            {
                if (IsDormant(___pawn)) __result = false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirRJW] ShouldHaveNeed check failed: " + e, 0x4A52B0);
            }
        }

        public static void NeedSex_Postfix(Pawn pawn, ref int __result)
        {
            if (__result == Satisfied) return;
            try
            {
                if (IsDormant(pawn)) __result = Satisfied;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirRJW] need_sex check failed: " + e, 0x4A52B1);
            }
        }

        /// <summary>The bond forming is the moment the need should appear - not up to an hour later.</summary>
        public static void AddDirectRelation_Postfix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
        {
            if (def == null || !LovePartnerRelationUtility.IsLovePartnerRelation(def)) return;
            Refresh(Traverse.Create(__instance).Field("pawn").GetValue<Pawn>());
            Refresh(otherPawn);
        }

        public static void Refresh(Pawn pawn)
        {
            try
            {
                // Any pawn with the gene, splinter-hearted included: one whose need was taken
                // before she had the trait has to get it back.
                if (pawn == null || pawn.Dead || pawn.needs == null || pairBond == null
                    || pawn.genes == null || !pawn.genes.HasActiveGene(pairBond)) return;
                bool has = pawn.needs.AllNeeds.Any(n => n.def.defName == "Sex");
                bool should = !IsDormant(pawn);
                if (has != should) pawn.needs.AddOrRemoveNeedsAsAppropriate();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirRJW] need refresh failed: " + e, 0x4A52B2);
            }
        }
    }

    /// <summary>
    /// Shows in the sex drive tooltip, so a player can see why an Avir's drive is zero or high.
    /// </summary>
    public class StatPart_AvirBond : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn pawn) || !AvirBondSexNeed.HasGene(pawn)) return;
            val *= AvirBondSexNeed.IsBonded(pawn) ? AvirBondSexNeed.BondedFactor(pawn) : 0f;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is Pawn pawn) || !AvirBondSexNeed.HasGene(pawn)) return null;
            return AvirBondSexNeed.IsBonded(pawn)
                ? "Avir pair bond: x" + AvirBondSexNeed.BondedFactor(pawn).ToStringPercent()
                : "Avir, unbonded: x0%";
        }
    }

    /// <summary>
    /// The slow net under the immediate hook: a mate dying, a breakup, a resurrection, a save
    /// loaded with the need in the wrong state. Hourly, Avir only.
    /// </summary>
    public class GameComponent_AvirBondSexNeed : GameComponent
    {
        public GameComponent_AvirBondSexNeed(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 != 0) return;
            try
            {
                List<Map> maps = Find.Maps;
                for (int m = 0; m < maps.Count; m++)
                {
                    IReadOnlyList<Pawn> pawns = maps[m].mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        if (pawns[i].RaceProps.Humanlike) AvirBondSexNeed.Refresh(pawns[i]);
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirRJW] bond sex-need sweep failed: " + e, 0x4A52B3);
            }
        }
    }
}
