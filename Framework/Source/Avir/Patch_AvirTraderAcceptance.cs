using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// WHAT THE STRIDERS WILL TAKE OFF YOUR HANDS.
    ///
    /// WHY THIS IS C# AND NOT XML. A TraderKindDef's buy list is not a list - it is a SIDE
    /// EFFECT. TraderKindDef.WillTrade walks the stockGenerators and asks each one
    /// HandlesThingDef(td); anything at least one of them claims is tradeable. So XML can only
    /// ever express "buys this", never "buys everything in this category EXCEPT". The rules
    /// below - no weapons, no apparel, no food, no sub-archotech implants - are exclusions, and
    /// there is no way to write an exclusion into a stock generator list. Hence a postfix.
    ///
    /// The stock generators in TraderKinds_Avir.xml still control what the trader SELLS. This
    /// only narrows what it will BUY, and it only ever narrows: it never makes a trader accept
    /// something its generators did not already handle.
    ///
    /// THE TWO ECONOMIES
    ///
    /// Shared by both (the strider's standing brief):
    ///     exotics, precious metals, medicine, slaves, drugs,
    ///     archotech implants and archotech goods.
    ///
    /// Refused by both:
    ///     weapons, apparel, food, and any implant below archotech tier.
    ///
    /// A'Shaign only - the High Nesting trades its own work and its own harvest:
    ///     Feignallite, Crimson Rage, Bloodstone Tisserium, the essences,
    ///     blueberries and blueberry cookies. The berries are a geography thing:
    ///     A'Shaign territory grows them and A'Schialis territory does not.
    ///
    /// A'Schialis only - the Legion's calling card:
    ///     Essence of Phoenix.
    ///
    /// Forbidden to the Legion outright, even though the A'Shaign trade them freely:
    ///     Bloodstone Tisserium, Shadowsong Wine, Crimson Rage.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirTraderAcceptance
    {
        static Patch_AvirTraderAcceptance()
        {
            Harmony harmony = new Harmony("Avir.TraderAcceptance");
            harmony.Patch(AccessTools.Method(typeof(TraderKindDef), nameof(TraderKindDef.WillTrade)),
                          postfix: new HarmonyMethod(typeof(Patch_AvirTraderAcceptance), nameof(WillTradePostfix)));
        }

        // ---- which of our trader kinds is this ----
        private const string AShaignCaravan = "Caravan_Avir_Striders";
        private const string AShaignBase = "Base_Avir_Striders";
        private const string LegionCaravan = "Caravan_AShialis_Striders";
        private const string LegionBase = "Base_AShialis_Striders";

        private static bool IsAvirTrader(TraderKindDef d)
        {
            if (d == null || d.defName == null) return false;
            return d.defName == AShaignCaravan || d.defName == AShaignBase
                || d.defName == LegionCaravan || d.defName == LegionBase;
        }

        private static bool IsLegion(TraderKindDef d)
        {
            return d.defName == LegionCaravan || d.defName == LegionBase;
        }

        // ---- named goods, resolved lazily so a missing def is simply never matched ----
        private static readonly string[] AShaignOnly =
        {
            "Avir_Feignallite", "Avir_CrimsonRage", "BloodstoneTisserium",
            "Avir_EssenceExtract", "Avir_Blueberries", "Avir_BlueberryCookies",
        };

        private static readonly string[] LegionOnly = { "Avir_PhoenixEssence" };

        // Barred from the Legion even where the general rules would allow them.
        private static readonly string[] LegionForbidden =
        {
            "BloodstoneTisserium", "Avir_ShadowsongWine", "Avir_CrimsonRage",
        };

        private static HashSet<ThingDef> aShaignOnly, legionOnly, legionForbidden;
        private static bool resolved;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            aShaignOnly = Build(AShaignOnly);
            legionOnly = Build(LegionOnly);
            legionForbidden = Build(LegionForbidden);
        }

        private static HashSet<ThingDef> Build(string[] names)
        {
            HashSet<ThingDef> set = new HashSet<ThingDef>();
            for (int i = 0; i < names.Length; i++)
            {
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(names[i]);
                if (d != null) set.Add(d);
            }
            return set;
        }

        public static void WillTradePostfix(TraderKindDef __instance, ThingDef td, ref bool __result)
        {
            try
            {
                if (!__result || td == null || !IsAvirTrader(__instance)) return;
                Resolve();

                bool legion = IsLegion(__instance);

                // 1. Faction-exclusive goods, checked first - these override the general rules
                //    in both directions.
                if (legion)
                {
                    if (legionForbidden.Contains(td)) { __result = false; return; }
                    if (aShaignOnly.Contains(td)) { __result = false; return; }
                    if (legionOnly.Contains(td)) return;   // the Legion's own, always accepted
                }
                else
                {
                    if (legionOnly.Contains(td)) { __result = false; return; }
                    if (aShaignOnly.Contains(td)) return;  // High Nesting goods, always accepted
                }

                // The Avir's own goods pass the "no food" refusal: their medicine, milk, alchemy fruits.
                if (td.defName == "Avir_Medicine" || td.defName == "Milk" || td.defName == "Avir_BirdMilk" || (td.thingCategories != null && td.thingCategories.Any(c => c.defName == "Avir_AlchemyFruits"))) return;

                // Artifacts and amulets are the Avir's own craft: every strider takes them, apparel or not.
                if (td.HasComp(typeof(Avir.Artifactorics.CompArtifact)) || td.HasComp(typeof(Avir.Artifactorics.CompAvirAmulet))) return;

                // 2. The universal refusals. A strider carries nothing it cannot use and does
                //    not haul other people's kit across a continent.
                if (td.IsWeapon) { __result = false; return; }
                if (td.IsApparel) { __result = false; return; }
                if (td.IsIngestible && !td.IsDrug && !IsMedicine(td)) { __result = false; return; }

                // 3. Implants: archotech only. Everything from a peg leg to a bionic eye is
                //    refused - the Avir have no use for other people's machinery, and the
                //    Phoenix would burn it out anyway.
                if (IsBodyPart(td) && td.techLevel < TechLevel.Archotech) { __result = false; return; }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Trader acceptance postfix failed: " + e, 0x4A5212);
            }
        }

        private static bool IsMedicine(ThingDef td)
        {
            return td.IsMedicine
                || (td.thingCategories != null && td.thingCategories.Contains(ThingCategoryDefOf.Medicine));
        }

        private static bool IsBodyPart(ThingDef td)
        {
            if (td.thingCategories == null) return false;
            for (int i = 0; i < td.thingCategories.Count; i++)
            {
                ThingCategoryDef c = td.thingCategories[i];
                while (c != null)
                {
                    if (c.defName == "BodyParts" || c.defName == "BodyPartsArtificial") return true;
                    c = c.parent;
                }
            }
            return false;
        }
    }
}
