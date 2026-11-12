using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avir.Ritualistics;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>What the magic remembers between ticks in static tables belongs to one game (Fix252, found on the audit): who
    /// was tended today, which sicknesses a mage of Pestilence holds, where clay was drawn, whose way lies through a gate.
    /// Loading another save left it all in place - keyed by ids and ticks that mean something else there (a held sickness'
    /// id fell on a stranger's wound and silenced it; "not again for a day" counted from a tick still ahead). The game's
    /// own component is made anew for every game started or loaded, before its maps: it clears every such table.
    /// Only tables keyed by a game's own things are cleared (ids, pawns, things, maps, jobs, spells, cells) - never the
    /// caches of defs, textures and names, nor the hooks other parts register.</summary>
    public class GameComponent_MagicReset : GameComponent
    {
        private static readonly Type[] Keys = { typeof(int), typeof(long), typeof(Pawn), typeof(Thing), typeof(Map), typeof(Job), typeof(RitualSchema), typeof(SpellCircle), typeof(IntVec3) };
        private static List<FieldInfo> tables;

        public GameComponent_MagicReset(Game game) { Clear(); }

        public static List<FieldInfo> Tables()
        {
            if (tables != null) return tables;
            tables = new List<FieldInfo>();
            foreach (Type t in typeof(GameComponent_MagicReset).Assembly.GetTypes())
            {
                if (t.Namespace != "Avir.Magic" || t.IsGenericTypeDefinition) continue;
                foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    Type ft = f.FieldType;
                    if (!ft.IsGenericType || f.IsDefined(typeof(ThreadStaticAttribute), false)) continue;
                    Type g = ft.GetGenericTypeDefinition();
                    Type[] a = ft.GetGenericArguments();
                    bool own = (g == typeof(Dictionary<,>) || g == typeof(HashSet<>)) && Keys.Contains(a[0]);
                    // a list of pairs led by a pawn (blows waiting their turn)
                    if (!own && g == typeof(List<>) && a[0].IsGenericType && a[0].GetGenericTypeDefinition() == typeof(KeyValuePair<,>) && Keys.Contains(a[0].GetGenericArguments()[0])) own = true;
                    if (!own) continue;
                    // not a game's state: what a value holds is loaded art or a def
                    if (g == typeof(Dictionary<,>) && (typeof(UnityEngine.Object).IsAssignableFrom(a[1]) || typeof(Def).IsAssignableFrom(a[1]) || typeof(Graphic).IsAssignableFrom(a[1]))) continue;
                    tables.Add(f);
                }
            }
            return tables;
        }

        public static int Clear()
        {
            int n = 0;
            try
            {
                foreach (FieldInfo f in Tables())
                {
                    object v = f.GetValue(null);
                    if (v == null) continue;
                    MethodInfo clear = v.GetType().GetMethod("Clear", Type.EmptyTypes);
                    if (clear == null) continue;
                    if (v is ICollection c && c.Count > 0) n += c.Count;
                    clear.Invoke(v, null);
                }
                SpellDash.LastPulled = null;
            }
            catch (Exception e) { Log.Warning("[Avir] magic reset: " + e); }
            return n;
        }

        [DebugAction("Magic", "Audit: static tables cleared with each game (log)", allowedGameStates = AllowedGameStates.Playing)]
        private static void DevList()
        {
            Log.Message("[Avir] tables of one game's state (" + Tables().Count + "), cleared when a game is started or loaded:\n" +
                string.Join("\n", Tables().Select(f => "  " + f.DeclaringType.Name + "." + f.Name + " = " + ((f.GetValue(null) as ICollection)?.Count.ToString() ?? "?"))));
        }
    }
}

namespace Avir.Magic
{
    /// <summary>A map's component, found once and kept (Fix252): the game looks a component up by walking the whole list of
    /// them, and some of ours are asked for on every pawn's every tick (which way a caster faces), on every look at how
    /// lit a cell is. One map is asked about many times running, so the last answer is kept.</summary>
    public static class MapComp<T> where T : MapComponent
    {
        private static Map last; private static T comp;
        public static T Of(Map m)
        {
            if (m == null) return null;
            if (m == last && comp != null) return comp;
            last = m; comp = m.GetComponent<T>();
            return comp;
        }
    }
}
