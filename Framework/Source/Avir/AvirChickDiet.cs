using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// WHAT A CHICK EATS. Chick meatballs are for Avir and harpy babies only - nobody else takes them.
    /// An Avir or wild-harpy baby, in turn, takes nothing but blueberries (and the cookies made of them),
    /// bird milk, the meatballs and the breast (suckling is not an item and never passes through here).
    /// </summary>
    public static class AvirChickDiet
    {
        public const string Meatballs = "Avir_ChickMeatballs";

        private static readonly HashSet<string> ChickFood = new HashSet<string>
        {
            "Avir_Blueberries", "Avir_BlueberryCookies", "Avir_BirdMilk", Meatballs
        };

        public static bool IsChick(Pawn p) =>
            p != null && p.DevelopmentalStage.Baby() && AvirBody.IsAvir(p);

        /// <summary>Calm harpies are raised among people and eat what their babies are given.</summary>
        public static bool IsPickyChick(Pawn p) =>
            IsChick(p) && p.genes?.Xenotype?.defName != "Avir_HarpyCalm";

        public static bool Allows(Pawn p, ThingDef food)
        {
            if (food == null) return true;
            if (food.defName == Meatballs) return IsChick(p);
            if (IsPickyChick(p)) return ChickFood.Contains(food.defName);
            return true;
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat), new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    public static class Patch_AvirChickDietThing
    {
        public static void Postfix(Pawn p, Thing food, ref bool __result)
        {
            if (__result && !AvirChickDiet.Allows(p, food?.def)) __result = false;
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat), new[] { typeof(Pawn), typeof(ThingDef), typeof(Pawn), typeof(bool), typeof(bool) })]
    public static class Patch_AvirChickDietDef
    {
        public static void Postfix(Pawn p, ThingDef food, ref bool __result)
        {
            if (__result && !AvirChickDiet.Allows(p, food)) __result = false;
        }
    }
    /// <summary>
    /// The feeding tab lists one shared roster of baby foods for every baby. For an Avir chick it shows only what the
    /// chick will actually take (the rest is refused by WillEat anyway); for any other baby the meatballs are left out.
    /// The roster is a private static list behind ITab_Pawn_Feeding.BabyConsumableFoods - it is swapped for the
    /// chick's own while the tab draws and put back right after.
    /// </summary>
    // FillTab has two overloads (the instance override and this static one) - the name alone left Harmony guessing
    [HarmonyPatch(typeof(ITab_Pawn_Feeding), nameof(ITab_Pawn_Feeding.FillTab),
        new[] { typeof(Pawn), typeof(UnityEngine.Rect), typeof(UnityEngine.Vector2), typeof(UnityEngine.Vector2), typeof(List<Pawn>) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Normal })]
    public static class Patch_AvirChickDietFeedingTab
    {
        private static readonly FieldInfo Roster = AccessTools.Field(typeof(ITab_Pawn_Feeding), "babyConsumableFoods");

        public static void Prefix(Pawn baby, out List<ThingDef> __state)
        {
            __state = null;
            if (Roster == null || baby == null) return;
            List<ThingDef> all = ITab_Pawn_Feeding.BabyConsumableFoods;
            List<ThingDef> mine = all.Where(d => AvirChickDiet.Allows(baby, d)).ToList();
            if (mine.Count == 0 || mine.Count == all.Count) return;
            __state = all;
            Roster.SetValue(null, mine);
        }

        public static void Finalizer(List<ThingDef> __state)
        {
            if (__state != null) Roster.SetValue(null, __state);
        }
    }
}
