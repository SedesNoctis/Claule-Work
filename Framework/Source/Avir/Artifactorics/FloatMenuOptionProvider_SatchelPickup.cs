using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>Right-click an item: "Pick up into satchel" - telekinetically, within the bearer's
    /// telekinetic reach, as much as fits. What is taken by order is kept with the bearer.</summary>
    public class FloatMenuOptionProvider_SatchelPickup : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => false;

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn p = context?.FirstSelectedPawn;
            if (p == null || clickedThing == null || clickedThing.def.category != ThingCategory.Item || !clickedThing.def.EverHaulable) return null;
            var inv = p.health?.hediffSet?.GetFirstHediffOfDef(ArtifactEfficiencyDefOf.Avir_TelekineticInventory) as Hediff_TelekineticInventory;
            if (inv == null) return null;
            string label = "Pick up into satchel: " + clickedThing.LabelShort;
            if (inv.FreeKg < clickedThing.GetStatValue(StatDefOf.Mass)) return new FloatMenuOption(label + " (satchel full)", null);
            float reach = 0f;
            AvirTelekinesisUtility.TryGetRadius(p, out reach);
            reach = UnityEngine.Mathf.Max(reach, 2f);
            if (clickedThing.Position.DistanceTo(p.Position) > reach)
                return new FloatMenuOption(label + " (beyond telekinetic reach, " + reach.ToString("0") + " cells)", null);
            return new FloatMenuOption(label, () =>
            {
                int n = inv.PickUp(clickedThing);
                if (n <= 0) Messages.Message("The satchel cannot take it.", p, MessageTypeDefOf.RejectInput, false);
            });
        }
    }
}
