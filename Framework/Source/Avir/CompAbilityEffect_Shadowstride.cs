using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Same shape as AvirHautsCompat's CompProperties_Rend/CompProperties_Combustion (an
    /// arcaneHungerCost field, gated in Valid(), spent in Apply()) - kept here in core Avir
    /// instead of the HautsFramework compat layer since Shadowstride doesn't need anything
    /// HautsFramework-specific, and amulet-granted abilities should work with or without that
    /// framework installed.
    /// </summary>
    public class CompProperties_Shadowstride : CompProperties_AbilityEffect
    {
        /// <summary>Fraction of the Arcane Hunger bar spent per cast (e.g. 0.10 = 10%).</summary>
        public float arcaneHungerCost = 0.10f;

        /// <summary>The Living Shadow hediff applied to the caster on a successful cast.</summary>
        public HediffDef livingShadowHediff;

        public CompProperties_Shadowstride()
        {
            compClass = typeof(CompAbilityEffect_Shadowstride);
        }
    }

    public class CompAbilityEffect_Shadowstride : CompAbilityEffect
    {
        public new CompProperties_Shadowstride Props => (CompProperties_Shadowstride)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            Pawn caster = parent.pawn;
            if (caster == null)
            {
                return false;
            }

            if (!AvirTelekinesisUtility.TryGetRadius(caster, out _))
            {
                // Not currently wearing an Avir amulet at all - shouldn't normally happen since
                // the ability is only granted while worn, but this is a defensive backstop for
                // the brief window right after the amulet comes off before the ability is synced
                // away.
                if (throwMessages)
                {
                    Messages.Message("Avir_ShadowstrideNoAmulet".Translate(caster.Named("PAWN")), caster, MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            if (!Gene_ArcaneHunger.HasAtLeast(caster, Props.arcaneHungerCost))
            {
                if (throwMessages)
                {
                    Messages.Message("Avir_ShadowstrideNotEnoughHunger".Translate(caster.Named("PAWN")), caster, MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent.pawn;
            if (caster == null || Props.livingShadowHediff == null)
            {
                return;
            }

            if (!Gene_ArcaneHunger.Spend(caster, Props.arcaneHungerCost))
            {
                // Shouldn't happen (Valid() already checked), but never apply the effect for free.
                return;
            }

            Hediff hediff = HediffMaker.MakeHediff(Props.livingShadowHediff, caster);
            caster.health.AddHediff(hediff);
        }
    }
}
