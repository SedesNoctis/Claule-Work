using HarmonyLib;
using Verse;

namespace AvirColors
{
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            var harmony = new Harmony("arno.avirgriffincolors");
            harmony.PatchAll();
        }
    }
}
