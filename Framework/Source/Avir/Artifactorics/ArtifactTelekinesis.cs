using System.Collections.Generic;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>
    /// Stage 9 - the telekinesis circle. Each artifact has five sockets opened with its points
    /// (1, 1, 2, 2, 3); every socket is a level. Only the best artifact on a pawn counts. Its
    /// radius is added to the radius of the pawn's telekinetic amulet, and a pawn without an
    /// amulet gets telekinesis from the artifact alone. It is the same telekinesis the amulets
    /// give: AvirTelekinesisUtility.TryGetRadius asks here, so hauling, bench feeding, building
    /// deliveries and Hauler's Dream all see it.
    /// </summary>
    public static class ArtifactTelekinesis
    {
        public static int BestLevel(Pawn p, out Thing source)
        {
            source = null;
            if (p?.apparel == null) return 0;
            List<CompArtifactSocket> worn = ArtifactWearCache.Worn(p);
            int best = 0;
            for (int i = 0; i < worn.Count; i++)
            {
                ArtifactState st = worn[i].inlaid;
                if (st == null || !st.HasHeart || st.tkLevel <= best) continue;
                best = st.tkLevel;
                source = worn[i].parent;
            }
            return best;
        }

        public static float Radius(Pawn p)
        {
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            if (s == null) return 0f;
            return s.TelekinesisRadius(BestLevel(p, out _));
        }
    }
}
