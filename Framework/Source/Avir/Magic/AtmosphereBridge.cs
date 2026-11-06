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
        private static MethodInfo addWind, addHeat, addMoist, addMist, getWind;

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
                addMoist = t.GetMethod("AddMoistureDelta", BindingFlags.Public | BindingFlags.Static);
                addMist = t.GetMethod("AddMistDelta", BindingFlags.Public | BindingFlags.Static);
                getWind = t.GetMethod("TryGetWind", BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception) { addWind = addHeat = addMoist = addMist = null; }
        }

        public static bool On { get { Look(); return addWind != null; } }

        /// <summary>Its wind does the throwing (the setting): our own spinning and throwing by the hurricane's whirls is left to it.</summary>
        public static bool Throws => On && (AvirMod.Settings == null || AvirMod.Settings.atmoWind);
        public static float Scale => AvirMod.Settings?.atmoWindScale ?? 1f;
        /// <summary>That mod takes a push of at most about 12 m/s at a time (its PatchSafeVelocityImpulse); stronger wind is
        /// built up by pushing again and again. Its own thresholds: staggers from 20 m/s, fells from 28, throws from 40.</summary>
        public const float MaxPush = 12f;

        /// <summary>Its wind at a cell, metres a second (Fix188: our gases that are things ride it - it moves only the game's
        /// gas grid itself).</summary>
        public static bool WindAt(Map map, IntVec3 c, out Vector2 w)
        {
            Look();
            w = Vector2.zero;
            if (getWind == null || map == null || !c.InBounds(map)) return false;
            try
            {
                object[] args = { map, c, Vector2.zero };
                if (!(bool)getWind.Invoke(null, args)) return false;
                w = (Vector2)args[2];
                return true;
            }
            catch (Exception) { getWind = null; return false; }
        }

        /// <summary>A push of wind at a cell, in metres a second.</summary>
        public static void Wind(Map map, IntVec3 c, Vector2 push)
        {
            Look();
            if (addWind == null || map == null || !c.InBounds(map)) return;
            try { addWind.Invoke(null, new object[] { map, c, push }); } catch (Exception) { addWind = null; }
        }

        /// <summary>Water vapour into the air of a cell (its own unit: vapour).</summary>
        public static void Moisture(Map map, IntVec3 c, float vapor)
        {
            Look();
            if (addMoist == null || map == null || !c.InBounds(map)) return;
            try { addMoist.Invoke(null, new object[] { map, c, vapor }); } catch (Exception) { addMoist = null; }
        }

        /// <summary>Mist in a cell: it hides, and that mod lets it spoil shots.</summary>
        public static void Mist(Map map, IntVec3 c, float mist)
        {
            Look();
            if (addMist == null || map == null || !c.InBounds(map)) return;
            try { addMist.Invoke(null, new object[] { map, c, mist }); } catch (Exception) { addMist = null; }
        }

        /// <summary>A room's air brought to a temperature. That mod keeps every cell's air itself, so a room's temperature
        /// set straight would be undone by it; with it, the change goes into its air instead.</summary>
        public static void RoomTemp(Room r, float target)
        {
            if (r == null) return;
            float delta = target - r.Temperature;
            r.Temperature = target;
            if (!On || Mathf.Abs(delta) < 0.01f) return;
            Map map = r.Map;
            foreach (IntVec3 c in r.Cells) Heat(map, c, delta);
        }

        /// <summary>What an element's touch does to the air (Reactions.Touch): heat warms it, water wets it, both - mist.</summary>
        public static void Touch(Map map, IntVec3 c, bool hot, bool water, float power)
        {
            if (!On) return;
            if (hot) Heat(map, c, 6f * power);
            if (water) Moisture(map, c, 1.5f * power);
            if (hot && water) Mist(map, c, 0.5f * power);         // steam: a cloud that hangs and spoils aim
            else if (water) Mist(map, c, 0.12f * power);
        }

        public static void Heat(Map map, IntVec3 c, float celsius)
        {
            Look();
            if (addHeat == null || map == null || !c.InBounds(map)) return;
            try { addHeat.Invoke(null, new object[] { map, c, celsius }); } catch (Exception) { addHeat = null; }
        }
    }
}
