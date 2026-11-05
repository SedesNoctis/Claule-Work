using RimWorld.Planet;
using Verse;

namespace AvirRJW
{
    // Enforces on add (dev tools / xenogerm). The Harmony hooks in Patches.cs cover normal
    // pawn generation. Only males are actively changed; females keep RJW's default anatomy.
    //
    // CHARACTER EDITOR FIX: when Character Editor loads a saved pawn it clears every hediff,
    // re-adds the genes (PostAdd fires here) and only THEN restores the saved hediffs - which
    // already include the canine penis and flat chest. Enforcing at gene-add gave the male a
    // second set. So PostAdd only acts on a pawn that already lives somewhere (spawned, carried,
    // in a caravan or a world pawn), never on one being assembled.
    public class Gene_AvirAnatomy : Gene
    {
        public override void PostAdd()
        {
            base.PostAdd();
            if (!InWorld(pawn)) return;
            RjwPartEnforcer.EnforceAvir(pawn);
        }

        public static bool InWorld(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.SpawnedOrAnyParentSpawned) return true;
            if (pawn.IsCaravanMember()) return true;
            return Find.World?.worldPawns != null && Find.WorldPawns.Contains(pawn);
        }
    }
}
