using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // The subspace satchel as a full extension of the inventory (amulet, Efficiency 9, capacitor).
    //  - Its free kilos are added to the bearer's carrying capacity: the inventory simply holds more,
    //    and everything vanilla does with an inventory (eating from it, drug policy, medicine stock,
    //    caravans, trade) works with the satchel as it is.
    //  - Its free kilos also widen what the bearer carries in its hands: one trip with a whole load
    //    of logs to a blueprint or a work table instead of many (up to a stack).
    //  - Food in a satchel bearer's inventory, and in the satchel, does not rot.
    //  - Food, drugs and medicine the satchel holds come out into the inventory when they are needed.
    // =====================================================================================
    public static class SatchelInventory
    {
        public static Hediff_TelekineticInventory Of(Pawn p) =>
            p?.health?.hediffSet?.GetFirstHediffOfDef(ArtifactEfficiencyDefOf.Avir_TelekineticInventory) as Hediff_TelekineticInventory;

        /// <summary>Food when hungry, the drug policy's carried drugs, the medicine stock: out of the
        /// satchel into the inventory, where the game looks for them.</summary>
        public static void ServeConsumables(Pawn p, Hediff_TelekineticInventory inv)
        {
            ThingOwner bag = inv.container;
            ThingOwner<Thing> pack = p.inventory?.innerContainer;
            if (bag == null || pack == null || bag.Count == 0) return;

            // food: one portion when hungry and the pack has none
            Need_Food food = p.needs?.food;
            if (food != null && food.CurCategory >= HungerCategory.Hungry && !pack.InnerListForReading.Any(t => t.def.IsNutritionGivingIngestible && FoodUtility.WillEat(p, t, p, true, false)))
            {
                Thing best = null;
                for (int i = 0; i < bag.Count; i++)
                {
                    Thing t = bag[i];
                    if (!t.def.IsNutritionGivingIngestible || t.def.IsDrug || !FoodUtility.WillEat(p, t, p, true, false)) continue;
                    if (best == null || t.def.ingestible.preferability > best.def.ingestible.preferability) best = t;
                }
                if (best != null)
                    bag.TryTransferToContainer(best, pack, Mathf.Max(1, FoodUtility.WillIngestStackCountOf(p, best.def, best.GetStatValue(StatDefOf.Nutrition))));
            }

            // drugs the policy wants carried
            DrugPolicy policy = p.drugs?.CurrentPolicy;
            if (policy != null)
                for (int i = 0; i < policy.Count; i++)
                {
                    DrugPolicyEntry e = policy[i];
                    if (e?.drug == null || e.takeToInventory <= 0) continue;
                    TopUp(bag, pack, e.drug, e.takeToInventory);
                }

            // the medicine stock (Gear tab)
            if (p.inventoryStock != null && InventoryStockGroupDefOf.Medicine != null)
            {
                ThingDef med = p.inventoryStock.GetDesiredThingForGroup(InventoryStockGroupDefOf.Medicine);
                int want = p.inventoryStock.GetDesiredCountForGroup(InventoryStockGroupDefOf.Medicine);
                if (med != null && want > 0) TopUp(bag, pack, med, want);
            }
        }

        private static void TopUp(ThingOwner bag, ThingOwner pack, ThingDef def, int want)
        {
            int need = want - pack.TotalStackCountOfDef(def);
            for (int i = bag.Count - 1; i >= 0 && need > 0; i--)
            {
                Thing t = bag[i];
                if (t.def != def) continue;
                int n = Mathf.Min(need, t.stackCount);
                need -= n;
                bag.TryTransferToContainer(t, pack, n);
            }
        }

        /// <summary>Things in a satchel bearer's inventory keep (the satchel's own contents never tick).</summary>
        public static bool Keeps(Thing t) =>
            t?.ParentHolder is Pawn_InventoryTracker it && it.pawn != null && Of(it.pawn) != null;
    }

    /// <summary>The satchel's free kilos are carrying capacity: the inventory holds more.</summary>
    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    public static class Patch_SatchelCapacity
    {
        public static void Postfix(Pawn p, System.Text.StringBuilder explanation, ref float __result)
        {
            Hediff_TelekineticInventory inv = SatchelInventory.Of(p);
            if (inv == null) return;
            inv.baseCapacity = __result;
            float bonus = Mathf.Max(0f, inv.capacityKg - inv.MassCarried);
            if (bonus <= 0f) return;
            __result += bonus;
            if (explanation != null)
            {
                if (explanation.Length > 0) explanation.AppendLine();
                explanation.Append("  - subspace satchel: +").Append(bonus.ToString("0.#")).Append(" kg");
            }
        }
    }

    /// <summary>The satchel widens the hands too: what does not fit in them rides in the satchel,
    /// so a bearer takes a whole stack of heavy things to a blueprint or a bill in one trip.</summary>
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.MaxStackSpaceEver))]
    public static class Patch_SatchelHands
    {
        public static void Postfix(Pawn_CarryTracker __instance, ThingDef td, ref int __result)
        {
            if (td == null || __result >= td.stackLimit) return;
            Hediff_TelekineticInventory inv = SatchelInventory.Of(__instance.pawn);
            if (inv == null) return;
            float per = td.BaseMass;
            if (per <= 0f) return;
            int extra = Mathf.FloorToInt(inv.FreeKg / per);
            if (extra > 0) __result = Mathf.Min(td.stackLimit, __result + extra);
        }
    }

    /// <summary>No rot in a satchel bearer's inventory.</summary>
    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.CompTickInterval))]
    public static class Patch_SatchelNoRotInterval
    {
        public static bool Prefix(CompRottable __instance) => !SatchelInventory.Keeps(__instance.parent);
    }

    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.CompTickRare))]
    public static class Patch_SatchelNoRotRare
    {
        public static bool Prefix(CompRottable __instance) => !SatchelInventory.Keeps(__instance.parent);
    }
}
