using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>ONE GAME'S STATE DOES NOT OUTLIVE THE GAME (Fix254, found on the audit of the whole mod).
    ///
    /// The mod keeps a great deal between ticks in static tables keyed by a thing's id, a pawn, a map: when an amulet's
    /// deflection is ready again, when a flyer was last pulled out of a wall, who was rolled for a ward, what an artifact's
    /// bonuses came to, where a Linarian last hunted. About a hundred and twenty of them. Static tables belong to the
    /// program, not to a save: loading another save - or the same one from an hour earlier - left them all standing.
    ///
    /// What that did: every "ready again at tick X" written late in a game stood in the FUTURE of an earlier save, so the
    /// thing it gated was dead until the game's clock caught up - days, sometimes (an amulet that would not deflect, a
    /// rescue from stone that would not come, a cooldown that never ended). Caches stamped with a later tick read as fresh
    /// for ever and kept another timeline's answer. Ids are given out again from save to save, so one game's entry fell on
    /// another game's pawn.
    ///
    /// The cure is the plainest one: before a game is loaded or started, every such table is emptied - exactly the state
    /// the program has when it has just been opened, which every one of these tables already has to cope with. Done BEFORE
    /// anything of the new game is read (a prefix on the game's own load and start), never after: a table that a saved
    /// component fills while loading is filled after this, and keeps what it read.
    ///
    /// What is NOT touched: tables whose values are loaded art or defs (graphics, textures, materials), and anything not
    /// declared readonly (a few fields here only point at another mod's own set).</summary>
    public static class AvirStaticReset
    {
        private static readonly Type[] Keys = { typeof(int), typeof(long), typeof(Pawn), typeof(Thing), typeof(Map), typeof(Job), typeof(IntVec3) };
        private static List<FieldInfo> tables;

        private static bool OwnKey(Type t) => Keys.Contains(t) || typeof(Thing).IsAssignableFrom(t) || t.Namespace != null && t.Namespace.StartsWith("Avir") && !typeof(Def).IsAssignableFrom(t) && t.IsClass;

        public static List<FieldInfo> Tables()
        {
            if (tables != null) return tables;
            var found = new List<FieldInfo>();
            Type[] types;
            try { types = typeof(AvirStaticReset).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (Type t in types)
            {
                if (t.IsGenericTypeDefinition || t.ContainsGenericParameters) continue;
                FieldInfo[] fields;
                try { fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                catch (Exception) { continue; }
                foreach (FieldInfo f in fields)
                {
                    Type ft = f.FieldType;
                    if (!f.IsInitOnly || !ft.IsGenericType || f.IsDefined(typeof(ThreadStaticAttribute), false)) continue;
                    Type g = ft.GetGenericTypeDefinition();
                    Type[] a = ft.GetGenericArguments();
                    bool own = (g == typeof(Dictionary<,>) || g == typeof(HashSet<>)) && OwnKey(a[0]);
                    if (!own && g == typeof(List<>) && a[0].IsGenericType && a[0].GetGenericTypeDefinition() == typeof(KeyValuePair<,>) && OwnKey(a[0].GetGenericArguments()[0])) own = true;
                    if (!own) continue;
                    if (g == typeof(Dictionary<,>) && (typeof(UnityEngine.Object).IsAssignableFrom(a[1]) || typeof(Def).IsAssignableFrom(a[1]) || typeof(Graphic).IsAssignableFrom(a[1]))) continue;
                    found.Add(f);
                }
            }
            tables = found;
            return tables;
        }

        public static int Clear(string why)
        {
            int n = 0, t = 0;
            foreach (FieldInfo f in Tables())
            {
                try
                {
                    object v = f.GetValue(null);
                    if (v == null) continue;
                    if (v is ICollection c) { if (c.Count == 0) continue; n += c.Count; }
                    MethodInfo clear = v.GetType().GetMethod("Clear", Type.EmptyTypes);
                    if (clear == null) continue;
                    clear.Invoke(v, null); t++;
                }
                catch (Exception e) { Log.WarningOnce("[Avir] static reset: " + f.DeclaringType?.Name + "." + f.Name + " - " + (e.InnerException ?? e).Message, f.GetHashCode() ^ 0x5E7); }
            }
            try { Avir.Magic.SpellDash.LastPulled = null; } catch (Exception) { }
            if (n > 0) Log.Message("[Avir] " + why + ": " + n + " entries of the last game's state cleared from " + t + " tables.");
            return n;
        }

        [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
        public static class Patch_BeforeLoad { public static void Prefix() => Clear("loading a game"); }

        [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
        public static class Patch_BeforeNew { public static void Prefix() => Clear("starting a game"); }

        [DebugAction("Avir", "Audit: static tables cleared with each game (log)", allowedGameStates = AllowedGameStates.Playing)]
        private static void DevList()
        {
            Log.Message("[Avir] tables of one game's state (" + Tables().Count + "), emptied before a game is loaded or started:\n" +
                string.Join("\n", Tables().OrderBy(f => f.DeclaringType.FullName).Select(f => "  " + f.DeclaringType.Name + "." + f.Name + " = " + ((f.GetValue(null) as ICollection)?.Count.ToString() ?? "?"))));
        }
    }
}
