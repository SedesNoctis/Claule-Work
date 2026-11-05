using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// SHADOWSONG WINE ARRIVES GOOD OR BETTER.
    ///
    /// WHY THIS IS NOT IN THE TRADER XML. StockGenerator has no quality field - not
    /// StockGenerator_SingleDef, not any of them. Quality on trader stock is rolled inside
    /// StockGeneratorUtility.TryMakeForStock, which hands every item the same
    /// QualityUtility.GenerateQualityTraderItem() spread: mostly Normal, occasionally Good, and
    /// a Legendary roughly never. A strider hauling wine across a continent does not fill the
    /// packs with Normal, so the roll is overridden after the fact.
    ///
    /// Good .. Legendary, weighted toward the bottom of that band: Legendary should still feel
    /// like a find.
    ///
    /// SCOPE, STATED PLAINLY. TryMakeForStock is shared by every trader in the game and does
    /// not know which TraderKindDef asked, so this raises the floor on Shadowsong Wine WHEREVER
    /// a trader generates it - including an unrelated trader that happened to pick it up
    /// through a Drugs category generator. Given the wine is an Avir item that mostly reaches
    /// the world through Avir hands, the collateral is small and consistent rather than wrong.
    /// If it should be strictly Avir-only, the hook has to move up to the stock-assembly layer
    /// and gets considerably messier; say the word.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirTraderStockQuality
    {
        static Patch_AvirTraderStockQuality()
        {
            // NAMES WHAT IT BOUND TO, BECAUSE THE OLD CODE COULD FAIL SILENTLY.
            //
            // Maximus ran 77 in-game days on a fresh save and the striders still arrived with
            // Normal wine. My first explanation - that the stock was generated before the rule
            // existed - was simply wrong, and he was right to reject it.
            //
            // The most likely remaining cause is right here: this loop takes the FIRST public
            // static method named TryMakeForStock whose first parameter is a ThingDef, and
            // reflection does not promise an order. If the engine has more than one such method,
            // this could have been binding to a twin that trader stock never goes through - and
            // it would have logged nothing at all, which is exactly what was observed.
            //
            // So: count the candidates and print the one chosen. If the log shows more than one
            // candidate, that is the answer and we bind by full signature instead of by guess.
            MethodInfo target = null;
            int candidates = 0;

            foreach (MethodInfo m in typeof(StockGeneratorUtility)
                     .GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "TryMakeForStock") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(ThingDef))
                {
                    candidates++;
                    if (target == null) target = m;
                }
            }

            if (target == null)
            {
                Log.Warning("[Avir] StockGeneratorUtility.TryMakeForStock not found - wine quality floor skipped.");
                return;
            }

            string signature = target.ReturnType.Name + " " + target.Name + "(";
            ParameterInfo[] chosen = target.GetParameters();
            for (int i = 0; i < chosen.Length; i++)
            {
                if (i > 0) signature += ", ";
                signature += chosen[i].ParameterType.Name + " " + chosen[i].Name;
            }
            signature += ")";

            Log.Message("[Avir wine] Quality floor bound to " + signature
                + " - " + candidates + " candidate(s) matched. "
                + (candidates > 1
                    ? "MORE THAN ONE: the wrong one may have been chosen. Report this line."
                    : "Single match, so this is the right method."));

            new Harmony("Avir.TraderStockQuality").Patch(target,
                postfix: new HarmonyMethod(typeof(Patch_AvirTraderStockQuality), nameof(Postfix)));
        }

        private static ThingDef wine;
        private static bool resolved;

        private static bool loggedAlive;
        private static bool loggedSawWine;
        private static bool loggedNoWineDef;

        private static ThingDef Wine
        {
            get
            {
                if (!resolved)
                {
                    resolved = true;
                    wine = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ShadowsongWine");
                }
                return wine;
            }
        }

        /// <summary>Good, Excellent, Masterwork, Legendary - front-weighted.</summary>
        private static QualityCategory RollTraderWineQuality()
        {
            float r = Rand.Value;
            if (r < 0.45f) return QualityCategory.Good;
            if (r < 0.78f) return QualityCategory.Excellent;
            if (r < 0.95f) return QualityCategory.Masterwork;
            return QualityCategory.Legendary;
        }

        public static void Postfix(ThingDef thingDef, ref IEnumerable<Thing> __result)
        {
            try
            {
                // THREE QUESTIONS, THREE SEPARATE LOG LINES, EACH ONCE.
                //
                // Silence is a bad diagnostic - it looks the same whether the hook never ran, ran
                // but never saw wine, or saw wine and was overruled afterwards. These separate
                // those three cases in a single caravan instead of another 77 days.
                if (!loggedAlive)
                {
                    loggedAlive = true;
                    Log.Message("[Avir wine] Hook is live. First item generated for stock: "
                        + (thingDef != null ? thingDef.defName : "null")
                        + ". If no '[Avir wine] saw wine' line ever follows, trader wine is NOT "
                        + "coming through this method and the rule is hooked in the wrong place.");
                }

                if (Wine == null)
                {
                    if (!loggedNoWineDef)
                    {
                        loggedNoWineDef = true;
                        Log.Warning("[Avir wine] Avir_ShadowsongWine not found in the def database "
                            + "- the quality floor can never fire. This is the whole bug if so.");
                    }
                    return;
                }

                if (thingDef == null || thingDef != Wine || __result == null) return;

                // The original returns a lazy iterator. Materialise it before touching anything,
                // or the mutation happens on a sequence that gets re-enumerated later.
                List<Thing> things = new List<Thing>(__result);

                int noComp = 0;
                int alreadyGood = 0;
                int raised = 0;

                for (int i = 0; i < things.Count; i++)
                {
                    CompQuality q = things[i].TryGetComp<CompQuality>();
                    if (q == null) { noComp++; continue; }
                    if (q.Quality >= QualityCategory.Good) { alreadyGood++; continue; }
                    q.SetQuality(RollTraderWineQuality(), ArtGenerationContext.Outsider);
                    raised++;
                }
                __result = things;

                if (!loggedSawWine)
                {
                    loggedSawWine = true;
                    Log.Message("[Avir wine] saw wine: " + things.Count + " item(s), "
                        + raised + " raised to Good or better, "
                        + alreadyGood + " already there, "
                        + noComp + " with NO quality component.");

                    if (noComp > 0)
                    {
                        Log.Warning("[Avir wine] Some wine arrived with no quality component at all "
                            + "- nothing can set a quality on those, and they will always read as "
                            + "the default. That would be a def problem, not a patch problem.");
                    }

                    if (raised > 0)
                    {
                        Log.Message("[Avir wine] The rule DID fire and DID raise the quality here. "
                            + "If Normal wine still reaches the trade window after this line, "
                            + "something downstream is regenerating or replacing the stock and "
                            + "this is the wrong layer to be fixing it at.");
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Trader wine quality postfix failed: " + e, 0x4A5213);
            }
        }
    }
}
