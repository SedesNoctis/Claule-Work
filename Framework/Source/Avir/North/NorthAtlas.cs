using UnityEngine;
using Verse;

namespace Avir.North
{
    /// <summary>
    /// The plant shader sways only the plants printed from the static texture atlas (a texture under 512 px goes into
    /// it). A def's own graphic is put there by the game; the variants it does not know of - the seasonal crowns, the
    /// closed canopies, the night forms - are put there here, before the atlas is baked.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class NorthAtlas
    {
        static NorthAtlas()
        {
            int n = 0;
            foreach (Texture2D t in ContentFinder<Texture2D>.GetAllInFolder("Things/Plant"))
                if (t != null && t.name.StartsWith("Avir_") && GlobalTextureAtlasManager.TryInsertStatic(TextureAtlasGroup.Plant, t))
                    n++;
            if (Prefs.DevMode) Log.Message("[Avir north] " + n + " plant textures queued for the atlas (so they sway in the wind).");
        }
    }
}
