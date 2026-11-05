using System.Collections.Generic;
using Avir;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    public class CompProperties_Shroudgates : CompProperties_AbilityEffect
    {
        public float groupRadius = 3.5f;
        public float arcaneHungerCost = 0.30f;

        public CompProperties_Shroudgates()
        {
            this.compClass = typeof(CompAbilityEffect_Shroudgates);
        }
    }

    public class CompAbilityEffect_Shroudgates : CompAbilityEffect
    {
        public new CompProperties_Shroudgates Props => (CompProperties_Shroudgates)this.props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages)) return false;

            Pawn caster = this.parent.pawn;
            if (!Gene_ArcaneHunger.HasAtLeast(caster, this.Props.arcaneHungerCost))
            {
                if (throwMessages)
                {
                    Messages.Message(
                        caster.LabelShortCap + " doesn't have enough arcane hunger to cast this.",
                        caster, MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = this.parent.pawn;
            Map map = caster.Map;
            if (map == null) return;

            if (!Gene_ArcaneHunger.Spend(caster, this.Props.arcaneHungerCost)) return;

            List<Pawn> group = new List<Pawn>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(caster.Position, this.Props.groupRadius, true))
            {
                if (!cell.InBounds(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn p && p.Faction == caster.Faction && !p.Dead)
                    {
                        group.Add(p);
                    }
                }
            }

            if (!group.Contains(caster)) group.Add(caster);

            Find.WindowStack.Add(new Dialog_ShroudgatesDestination(group));
        }
    }
}
