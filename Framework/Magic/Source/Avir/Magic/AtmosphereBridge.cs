using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>A soft bridge to Atmosphere Overhaul (Acutus), if it is loaded (the author, Fix166: its wind is worth
    /// leaning on). Our winds then move its air: a hurricane's whirls push real wind round, which that mod's own wind
    /// then turns on people, items and roofs. Nothing is needed from it; without it this does nothing.</summary>
    public static class AtmosphereBridge
    {
        private static bool looked;
        private static MethodInfo addWind, addHeat;

        private static void Look()
        {
            if (looked) return;
            looked = true;
            try
            {
                Type t = GenTypes.GetTypeInAnyAssembly("AtmosphereOverhaul.RimWorld.AtmosphereInterface");
                if (t == null) return;
                addWind = t.GetMethod("AddWindImpulse", BindingFlags.Public | BindingFlags.Static);
                addHeat = t.GetMethod("AddTemperatureDelta", BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception) { addWind = addHeat = null; }
        }

        public static bool On { get { Look(); return addWind != null; } }

        /// <summary>A push of wind at a cell, in metres a second.</summary>
        public static void Wind(Map map, IntVec3 c, Vector2 push)
        {
            Look();
            if (addWind == null || map == null || !c.InBounds(map)) return;
            try { addWind.Invoke(null, new object[] { map, c, push }); } catch (Exception) { addWind = null; }
        }

        public static void Heat(Map map, IntVec3 c, float celsius)
        {
            Look();
            if (addHeat == null || map == null || !c.InBounds(map)) return;
            try { addHeat.Invoke(null, new object[] { map, c, celsius }); } catch (Exception) { addHeat = null; }
        }
    }
}
