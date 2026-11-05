using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    public class CompProperties_Choir : CompProperties_AbilityEffect
    {
        /// <summary>Which kind of choir this ability opens. Everything that used to be a
        /// constant - radius, cost, hediffs, duration, thresholds - now comes from here, so a
        /// new hymn is an AbilityDef plus an AvirChoirDef and no C# at all.</summary>
        public AvirChoirDef choirDef;

        public CompProperties_Choir()
        {
            compClass = typeof(CompAbilityEffect_Choir);
        }
    }

    public class CompAbilityEffect_Choir : CompAbilityEffect
    {
        public new CompProperties_Choir Props => (CompProperties_Choir)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            Pawn caster = parent.pawn;
            if (caster == null || Props.choirDef == null)
            {
                return false;
            }

            // Re-clicking your own active Choir always turns it off for free - only starting a
            // NEW one is gated on Arcane Hunger. On a timed hymn this does NOT refund the
            // cooldown: RimWorld starts an ability cooldown at cast, and cancelling early is a
            // decision to stop singing, not an undo.
            if (Props.choirDef.cancellable
                && Current.Game.GetComponent<GameComponent_AvirChoir>()?.IsLeadingChoir(caster, Props.choirDef) == true)
            {
                return true;
            }

            if (!Gene_ArcaneHunger.HasAtLeast(caster, Props.choirDef.castCost))
            {
                if (throwMessages)
                {
                    Messages.Message("Avir_ChoirNotEnoughHunger".Translate(caster.Named("PAWN")), caster, MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent.pawn;
            if (caster == null || Props.choirDef == null)
            {
                return;
            }

            GameComponent_AvirChoir choirManager = Current.Game.GetComponent<GameComponent_AvirChoir>();
            if (choirManager == null)
            {
                return;
            }

            if (Props.choirDef.cancellable && choirManager.IsLeadingChoir(caster, Props.choirDef))
            {
                // Toggle off - the caster's own willing choice, no cost, no penalty.
                choirManager.EndChoirLedBy(caster, Props.choirDef, applyPenalty: false);
                return;
            }

            if (!Gene_ArcaneHunger.Spend(caster, Props.choirDef.castCost))
            {
                return;
            }

            choirManager.StartChoir(caster, Props.choirDef);
        }
    }
}
