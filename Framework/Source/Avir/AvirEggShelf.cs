using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    // =====================================================================================
    // WHOSE EGG, AND WHERE IT IS KEPT. Every laid egg carries its mother's name; the egg shelf holds eggs and
    // nothing else, and a mother who claims one has her own clutch carried to it at critical priority.
    // The parents live in VEF's CompHumanHatcher (no compile-time reference): read once by reflection.
    // =====================================================================================
    public class CompProperties_AvirEggParents : CompProperties
    {
        public CompProperties_AvirEggParents() { compClass = typeof(CompAvirEggParents); }
    }

    public class CompAvirEggParents : ThingComp
    {
        private static FieldInfo[] pawnFields;
        private static System.Type hatcherType;
        private static bool looked;

        private static void Look()
        {
            if (looked) return;
            looked = true;
            hatcherType = AccessTools.TypeByName("VEF.Genes.CompHumanHatcher");
            if (hatcherType != null)
                pawnFields = hatcherType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                        .Where(f => typeof(Pawn).IsAssignableFrom(f.FieldType)).ToArray();
        }

        /// <summary>The parents the hatcher remembers: mother first (the female, or the "hatcheeParent"), then father.</summary>
        public static void Parents(Thing egg, out Pawn mother, out Pawn father)
        {
            mother = father = null;
            Look();
            if (hatcherType == null || !(egg is ThingWithComps twc)) return;
            ThingComp hatcher = twc.AllComps.FirstOrDefault(c => hatcherType.IsInstanceOfType(c));
            if (hatcher == null || pawnFields == null) return;
            var found = new List<Pawn>();
            foreach (FieldInfo f in pawnFields)
                if (f.GetValue(hatcher) is Pawn p && !found.Contains(p)) found.Add(p);
            mother = found.FirstOrDefault(p => p.gender == Gender.Female)
                     ?? found.FirstOrDefault(p => pawnFields.Any(f => f.Name.IndexOf("hatchee", System.StringComparison.OrdinalIgnoreCase) >= 0 && f.GetValue(hatcher) == p));
            Pawn m = mother;
            father = found.FirstOrDefault(p => p != m);
        }

        public static Pawn Mother(Thing egg) { Parents(egg, out Pawn m, out _); return m; }

        public override string TransformLabel(string label)
        {
            Pawn m = Mother(parent);
            return m == null ? label : label + " (" + m.LabelShort + ")";
        }

        public override string CompInspectStringExtra()
        {
            Parents(parent, out Pawn m, out Pawn f);
            if (m == null && f == null) return null;
            string s = "Laid by: " + (m?.LabelShort ?? "unknown");
            if (f != null) s += "\nFather: " + f.LabelShort;
            return s;
        }
    }

    /// <summary>The shelf can be claimed by one female; she alone stores her eggs on it.</summary>
    public class CompAssignableToPawn_EggShelf : CompAssignableToPawn
    {
        public override IEnumerable<Pawn> AssigningCandidates =>
            parent.Spawned ? parent.Map.mapPawns.FreeColonists.Where(p => p.gender == Gender.Female) : Enumerable.Empty<Pawn>();

        public override AcceptanceReport CanAssignTo(Pawn pawn) =>
            pawn.gender == Gender.Female ? AcceptanceReport.WasAccepted : new AcceptanceReport("Only a mother can claim an egg shelf.");

        protected override string GetAssignmentGizmoLabel() => "Assign mother...";
        protected override string GetAssignmentGizmoDesc() => "Choose the female whose eggs this shelf keeps. Unclaimed, it takes any Avir egg.";
    }

    public class Building_AvirEggShelf : Building_Storage
    {
        public Pawn Owner => GetComp<CompAssignableToPawn>()?.AssignedPawnsForReading.FirstOrDefault();

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            Pawn o = Owner;
            string own = o != null ? "Keeps the eggs of: " + o.LabelShort : "Unclaimed: any Avir egg";
            return s.NullOrEmpty() ? own : s + "\n" + own;
        }
    }

    /// <summary>A claimed shelf takes only its mother's eggs.</summary>
    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.Accepts))]
    public static class Patch_AvirEggShelfAccepts
    {
        public static void Postfix(Building_Storage __instance, Thing t, ref bool __result)
        {
            if (!__result || !(__instance is Building_AvirEggShelf shelf)) return;
            Pawn owner = shelf.Owner;
            if (owner != null && CompAvirEggParents.Mother(t) != owner) __result = false;
        }
    }

    /// <summary>Looks exactly like the vanilla shelf, whatever its texture path is in this version.</summary>
    [StaticConstructorOnStartup]
    public static class AvirEggShelfGraphic
    {
        static AvirEggShelfGraphic()
        {
            ThingDef shelf = DefDatabase<ThingDef>.GetNamedSilentFail("Shelf");
            ThingDef ours = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_EggShelf");
            if (shelf == null || ours == null) return;
            ours.graphicData = shelf.graphicData;
            ours.graphic = shelf.graphic;
            ours.uiIcon = shelf.uiIcon;
            ours.uiIconAngle = shelf.uiIconAngle;
            ours.uiIconOffset = shelf.uiIconOffset;
            ours.uiIconScale = shelf.uiIconScale;
        }
    }
}
