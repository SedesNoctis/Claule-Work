using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Shared helpers for the two patches that act at the moment a pawn is born or hatched:
    /// Patch_AvirHarpyBirth and Patch_AvirAShacsaignInheritance. Kept in one place so the
    /// hatchling search cannot drift between them.
    /// </summary>
    public static class AvirBirthUtility
    {
        /// <summary>
        /// Locates the pawn that Hatch just spawned: a child of both recorded parents who is not
        /// either of them. Searches the map the parents are on, which is where a hatching happens.
        /// </summary>
        public static Pawn FindFreshChick(Pawn parentA, Pawn parentB)
        {
            Pawn anchor = parentA ?? parentB;
            if (anchor == null) return null;

            Map map = anchor.MapHeld;
            if (map == null) return null;

            // IReadOnlyList, matching how every other file in this mod reads AllPawnsSpawned.
            System.Collections.Generic.IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = all.Count - 1; i >= 0; i--) // newest first
            {
                Pawn p = all[i];
                if (p == parentA || p == parentB) continue;
                if (p.relations == null) continue;
                if (!IsChildOf(p, parentA) && !IsChildOf(p, parentB)) continue;
                if (p.ageTracker == null || p.ageTracker.AgeBiologicalTicks > 60000) continue;
                return p;
            }
            return null;
        }

        private static bool IsChildOf(Pawn child, Pawn parent)
        {
            if (parent == null || child.relations == null) return false;
            return child.GetMother() == parent || child.GetFather() == parent;
        }
    }
}
