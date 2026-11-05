using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Traits
{
    /// <summary>
    /// What used to come from HautsFramework (its TraitGrantedStuff): a trait that hands its bearer abilities, by
    /// degree. Kept with the same XML shape (grantedAbilities: degree -> abilities), so the trait defs only changed
    /// the class name. Granted the moment the trait is gained, taken back when it is lost, and checked again whenever
    /// the pawn is spawned (old saves, pawns generated before this, anything that slipped past).
    /// </summary>
    public class TraitGrantedAbilities : DefModExtension
    {
        public Dictionary<int, List<AbilityDef>> grantedAbilities = new Dictionary<int, List<AbilityDef>>();

        public List<AbilityDef> For(int degree) =>
            grantedAbilities != null && grantedAbilities.TryGetValue(degree, out List<AbilityDef> l) ? l : null;

        public static void Grant(Pawn p, Trait t)
        {
            if (p?.abilities == null || t?.def == null || t.Suppressed) return;
            List<AbilityDef> list = t.def.GetModExtension<TraitGrantedAbilities>()?.For(t.Degree);
            if (list == null) return;
            foreach (AbilityDef a in list)
                if (a != null && p.abilities.GetAbility(a) == null) p.abilities.GainAbility(a);
        }

        public static void Take(Pawn p, Trait t)
        {
            if (p?.abilities == null || t?.def == null) return;
            List<AbilityDef> list = t.def.GetModExtension<TraitGrantedAbilities>()?.For(t.Degree);
            if (list == null) return;
            foreach (AbilityDef a in list)
                if (a != null && p.abilities.GetAbility(a) != null) p.abilities.RemoveAbility(a);
        }

        public static void GrantAll(Pawn p)
        {
            if (p?.story?.traits == null || p.abilities == null) return;
            foreach (Trait t in p.story.traits.allTraits) Grant(p, t);
        }
    }

    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.GainTrait))]
    public static class Patch_TraitGrantedAbilities_Gain
    {
        public static void Postfix(Trait trait, Pawn ___pawn) => TraitGrantedAbilities.Grant(___pawn, trait);
    }

    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.RemoveTrait))]
    public static class Patch_TraitGrantedAbilities_Remove
    {
        public static void Postfix(Trait trait, Pawn ___pawn) => TraitGrantedAbilities.Take(___pawn, trait);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_TraitGrantedAbilities_Spawn
    {
        public static void Postfix(Pawn __instance) => TraitGrantedAbilities.GrantAll(__instance);
    }
}
