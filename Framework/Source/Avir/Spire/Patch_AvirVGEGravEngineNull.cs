using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Spire
{
    /// <summary>
    /// Vanilla Gravship Expanded compatibility. VGE gathers every ThingDef whose class derives from
    /// Building_GravEngine - the spire cradle is one - and GravEngineTracker.GetGravEngine_ListerThings then asks
    /// ListerThings for each def's minifiedDef without checking it for null. The cradle cannot be minified, so on any
    /// map without a vanilla grav engine (every fresh Avir start) that call throws "Invalid ThingRequest group
    /// Undefined" from the pawn think tree every tick. This replaces that one method with the same search, skipping
    /// a missing minifiedDef (as VGE already does elsewhere).
    /// COMPLETE NO-OP WITHOUT THAT MOD.
    /// </summary>
    public static class Patch_AvirVGEGravEngineNull
    {
        private static FieldInfo defsField;

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type tracker = AccessTools.TypeByName("VanillaGravshipExpanded.GravEngineTracker");
                Type helper = AccessTools.TypeByName("VanillaGravshipExpanded.GravshipHelper");
                if (tracker == null || helper == null) return;
                MethodInfo target = AccessTools.Method(tracker, "GetGravEngine_ListerThings", new[] { typeof(Map) });
                defsField = AccessTools.Field(helper, "GravEngineDefs");
                if (target == null || defsField == null)
                {
                    Log.Warning("[Avir] Vanilla Gravship Expanded found, but GetGravEngine_ListerThings/GravEngineDefs not - spire null-def guard not applied.");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Patch_AvirVGEGravEngineNull), nameof(Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] VGE grav engine guard failed to apply: " + e.Message);
            }
        }

        public static bool Prefix(Map map, ref Building_GravEngine __result)
        {
            __result = null;
            if (map?.listerThings == null) return false;
            ThingDef[] defs = defsField.GetValue(null) as ThingDef[];
            if (defs == null) return false;
            for (int i = 0; i < defs.Length; i++)
            {
                ThingDef def = defs[i];
                if (def == null) continue;
                List<Thing> list = map.listerThings.ThingsOfDef(def);
                if (list.Count > 0) { __result = list[0] as Building_GravEngine; return false; }
                ThingDef mini = def.minifiedDef;
                if (mini != null && mini != ThingDefOf.MinifiedThing)
                {
                    list = map.listerThings.ThingsOfDef(mini);
                    if (list.Count > 0) { __result = list[0] as Building_GravEngine; return false; }
                }
            }
            return false;
        }
    }
}
