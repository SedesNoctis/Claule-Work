using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Ritualistics
{
    /// <summary>
    /// What the haulers lay in a ritual stays there. A component circle's cell is not a stockpile, so a crystal or
    /// a material laid on it is "unstored" and the next hauler took it straight back to storage - and the filling
    /// hauler brought it again, for ever. The same for the satchel, the subspace controller, the crane and the
    /// cleaning ritual, which all gather loose things. Held: a thing on a drawn ritual's component cell that is the
    /// one that circle expects, anything in the main circle of a running ritual (except what a transmutation makes),
    /// and an artifact or crystal battery laid at the centre to stand as the ritual's conduit. Also what an Avir
    /// artificer's round table has gathered beside itself for its bills.
    /// </summary>
    public static class RitualHold
    {
        private static readonly Dictionary<int, List<CompRitualCircle>> circles = new Dictionary<int, List<CompRitualCircle>>();
        private static readonly Dictionary<int, int> builtAt = new Dictionary<int, int>();

        private static List<CompRitualCircle> Circles(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (!circles.TryGetValue(map.uniqueID, out var list) || !builtAt.TryGetValue(map.uniqueID, out int at) || now - at >= 60 || now < at)
            {
                list = map.listerBuildings.allBuildingsColonist.Select(b => b.TryGetComp<CompRitualCircle>())
                    .Where(c => c != null && c.Drawn).ToList();
                circles[map.uniqueID] = list;
                builtAt[map.uniqueID] = now;
            }
            return list;
        }

        public static bool Held(Thing t)
        {
            if (t == null || !t.Spawned || t.def.category != ThingCategory.Item) return false;
            Map map = t.Map;
            List<CompRitualCircle> list = Circles(map);
            if (list.Count == 0) return HeldByTable(t, map);
            IntVec3 pos = t.Position;
            foreach (CompRitualCircle c in list)
            {
                if (c.parent == null || !c.parent.Spawned || c.parent.Map != map) continue;
                float near = c.Radius * 1.22f + 4f;
                if ((pos - c.parent.Position).LengthHorizontalSquared > near * near) continue;
                RitualSchema s = c.drawn;
                if (s == null) continue;
                for (int k = 0; k < s.components.Count; k++)
                    if (c.ComponentCell(k) == pos && c.Holding(k) == t) return true;
                if (c.Running && c.Inside(pos) && t.def != c.Reading?.Seal) return true;
                // what lies at the centre to hold the ritual (an artifact, a crystal battery) stays there for it
                if (c.chosenThing == t || c.reforgeThing == t || (!c.Running && c.ConduitCandidates().Contains(t))) return true;
            }
            return HeldByTable(t, map);
        }

        // ---------------- the Avir artificer's round table gathers its bills' ingredients by telekinesis; what it
        // has gathered beside itself stays there for the work (the bearer's satchel used to carry it straight back)
        private static readonly Dictionary<int, List<Avir.Artifactorics.CompArtificerTable>> tables = new Dictionary<int, List<Avir.Artifactorics.CompArtificerTable>>();
        private static readonly Dictionary<int, int> tablesAt = new Dictionary<int, int>();

        private static bool HeldByTable(Thing t, Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (!tables.TryGetValue(map.uniqueID, out var list) || !tablesAt.TryGetValue(map.uniqueID, out int at) || now - at >= 60 || now < at)
            {
                list = map.listerBuildings.allBuildingsColonist.Select(b => b.TryGetComp<Avir.Artifactorics.CompArtificerTable>())
                    .Where(c => c != null && c.Stones > 0).ToList();
                tables[map.uniqueID] = list;
                tablesAt[map.uniqueID] = now;
            }
            if (list.Count == 0) return false;
            foreach (Avir.Artifactorics.CompArtificerTable c in list)
            {
                if (!(c.parent is Building_WorkTable table) || !table.Spawned) continue;
                if ((t.Position - table.Position).LengthHorizontalSquared > 3.9f * 3.9f) continue;
                foreach (Bill bill in table.BillStack.Bills)
                {
                    if (!(bill is Bill_Production) || bill.suspended || bill.recipe?.ingredients == null || !bill.ShouldDoNow()) continue;
                    foreach (IngredientCount ing in bill.recipe.ingredients)
                        if (ing.filter.Allows(t) && bill.IsFixedOrAllowedIngredient(t)) return true;
                }
            }
            return false;
        }

        /// <summary>Is this pawn performing a ritual now.</summary>
        public static bool Performing(Pawn p)
        {
            if (p?.Map == null) return false;
            foreach (CompRitualCircle c in Circles(p.Map)) if (c.performer == p) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ListerHaulables), "ShouldBeHaulable")]
    public static class Patch_RitualHold_Lister
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (__result && RitualHold.Held(t)) __result = false;
        }
    }

    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast_NewTemp))]
    public static class Patch_RitualHold_Haul
    {
        // a player's own order still moves it
        public static void Postfix(Thing t, bool forced, ref bool __result)
        {
            if (__result && !forced && RitualHold.Held(t)) __result = false;
        }
    }
}
