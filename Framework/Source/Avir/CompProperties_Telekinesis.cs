using Verse;

namespace Avir
{
    /// <summary>
    /// Attached to each amulet tier via XML with a different radius. Nothing else about the
    /// tiers differs mechanically - the whole upgrade curve lives in this one number, plus
    /// whatever the RecipeDefs charge to make the next tier.
    /// </summary>
    public class CompProperties_Telekinesis : CompProperties
    {
        // The side length of the square the pawn can reach with this amulet (e.g. 10 = "10x10").
        public float radius = 10f;

        // General work speed multiplier granted just by wearing this amulet (1.0 = no effect,
        // 1.10 = +10%). Applied via StatPart_AvirTelekineticFocus on WorkSpeedGlobal - see that
        // class for how it reads this value off whichever Avir amulet the pawn currently wears.
        public float workSpeedFactor = 1f;

        // Purely cosmetic - shown in the comp's inspect/tip text so players can tell tiers apart
        // at a glance without reading the item description.
        public string tierLabel = "Telekinetic Focus";

        // Subspace satchel the amulet opens on its wearer (the same telekinetic inventory as the
        // Efficiency 9 artifact milestone), in kg. 0 = none. When an artifact gives one too, the
        // larger wins. Set per tier in the amulet XML.
        public float satchelCapacityKg = 0f;

        public CompProperties_Telekinesis()
        {
            compClass = typeof(Comp_Telekinesis);
        }
    }
}
