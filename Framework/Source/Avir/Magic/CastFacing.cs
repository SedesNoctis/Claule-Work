using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Magic
{
    /// <summary>A caster looks at what they cast at (the author, Fix151): a caster of the air walking with a spell in
    /// their hands, and a standing caster whose target moves - both keep turning to it till the spell is let go.</summary>
    [HarmonyPatch(typeof(Pawn_RotationTracker), nameof(Pawn_RotationTracker.UpdateRotation))]
    public static class Patch_CastFacing
    {
        private static readonly AccessTools.FieldRef<Pawn_RotationTracker, Pawn> pawnOf = AccessTools.FieldRefAccess<Pawn_RotationTracker, Pawn>("pawn");

        public static void Postfix(Pawn_RotationTracker __instance)
        {
            Pawn p = pawnOf(__instance);
            if (p == null || !p.Spawned || !AvirMagic.On || p.Downed) return;
            LocalTargetInfo t = LocalTargetInfo.Invalid;
            if (p.CurJobDef == MagicSpellDefOf.Avir_CastSpell) t = p.CurJob.targetA;
            else { var c = MapComponent_AirCasts.Of(p); if (c != null) t = c.target; }
            if (!t.IsValid || t.Cell == p.Position) return;
            __instance.FaceCell(t.Cell);
        }
    }
}
