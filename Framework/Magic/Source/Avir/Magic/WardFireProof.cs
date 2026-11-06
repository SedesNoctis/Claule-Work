using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>A fiery ward (fire, magma, steam, haze - any ward with fire in it) keeps its wearer out of fire's reach
    /// (the author, Fix149): they do not catch fire, the lava of the Odyssey and the caster's own magma do not burn them,
    /// they do not flee a large fire nor stop to beat out the flames on their way - they walk through. A lava ward.</summary>
    public static class FireProof
    {
        public static bool Of(Pawn p) => p != null && AvirMagic.On && SpellWards.Layers(p).Any(w => w.Energy > 0f && w.Fiery);

        /// <summary>Fire from the world, not a blow aimed by someone: a burning cell, lava ground, a pool of magma.</summary>
        public static bool Ambient(Pawn p, DamageInfo d)
        {
            if (d.Def != DamageDefOf.Flame && d.Def != DamageDefOf.Burn) return false;
            if (d.Instigator == null || d.Instigator is Fire) return true;
            if (!p.Spawned) return false;
            TerrainDef t = p.Position.GetTerrain(p.Map);
            return (t != null && t.burnDamage > 0) || p.Position.GetThingList(p.Map).Any(x => x is MagmaPool);
        }
    }

    [HarmonyPatch(typeof(FireUtility), nameof(FireUtility.TryAttachFire))]
    public static class Patch_FireProof_Attach
    {
        public static bool Prefix(Thing t) => !(t is Pawn p && FireProof.Of(p));
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_FireProof_Damage
    {
        public static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!(__instance is Pawn p) || !FireProof.Ambient(p, dinfo) || !FireProof.Of(p)) return true;
            __result = new DamageWorker.DamageResult();
            return false;
        }
    }

    [HarmonyPatch(typeof(FleeUtility), nameof(FleeUtility.FleeLargeFireJob))]
    public static class Patch_FireProof_Flee
    {
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (!FireProof.Of(pawn)) return true;
            __result = null;
            return false;
        }
    }

    /// <summary>On the way through fire a warded pawn does not stop to beat it out (a firefighting job still does).</summary>
    [HarmonyPatch(typeof(Pawn_NativeVerbs), nameof(Pawn_NativeVerbs.TryBeatFire))]
    public static class Patch_FireProof_BeatFire
    {
        private static readonly AccessTools.FieldRef<Pawn_NativeVerbs, Pawn> pawnOf = AccessTools.FieldRefAccess<Pawn_NativeVerbs, Pawn>("pawn");
        public static bool Prefix(Pawn_NativeVerbs __instance, ref bool __result)
        {
            Pawn p = pawnOf(__instance);
            if (p == null || p.CurJobDef == JobDefOf.BeatFire || !FireProof.Of(p)) return true;
            __result = false;
            return false;
        }
    }
}
