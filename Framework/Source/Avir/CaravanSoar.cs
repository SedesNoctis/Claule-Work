using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FlightGene
{
    /// <summary>
    /// SOAR FROM A CARAVAN. A caravan with Avir who can fly gets the Soar command on the world map - no camp
    /// to set up first. The flyers take wing with the chicks of their family on their backs (two each).
    ///   - Someone who cannot fly (a human, a wingless or clipped Avir) stays: the caravan splits, the ones who
    ///     stay keep the animals and the cargo they carry; the flyers take only what is on themselves.
    ///   - Nobody who can lead a caravan stays (only animals, prisoners): they are left behind and lost. The
    ///     flyers grab what cargo they can carry, the most precious first; the rest is lost with them.
    ///     The player is told exactly what is lost before the flight.
    ///   - A chick nobody can carry is never left alone: the flight is refused.
    /// </summary>
    public static class CaravanSoar
    {
        private static Texture2D icon;

        public static CompAbilityEffect_FlyToTile SoarOf(Pawn p) =>
            p?.abilities?.abilities?.Select(a => a.CompOfType<CompAbilityEffect_FlyToTile>()).FirstOrDefault(c => c != null);

        public static bool CanFly(Pawn p) =>
            p != null && !p.Dead && !p.Downed && p.HostFaction == null && SoarOf(p) != null
            && !Avir.AvirWingParts.Clipped(p) && p.health.capacities.CapableOf(PawnCapacityDefOf.Moving);

        private class Plan
        {
            public List<Pawn> flyers = new List<Pawn>();
            public List<Pawn> chicks = new List<Pawn>();
            public List<Pawn> stay = new List<Pawn>();
            public bool groundHolds;                      // the ones who stay can go on as a caravan
            public List<(Thing thing, int count)> take = new List<(Thing, int)>();
            public List<Thing> lostCargo = new List<Thing>();
            public string refusal;
            public int range;
        }

        private static Plan Make(Caravan caravan)
        {
            var plan = new Plan();
            List<Pawn> all = caravan.PawnsListForReading.ToList();
            plan.flyers = all.Where(CanFly).ToList();
            if (plan.flyers.Count == 0) { plan.refusal = "No one here can fly."; return plan; }
            plan.range = plan.flyers.Min(f => SoarOf(f).FlyProps.maxTileDistance);

            // chicks onto family backs, two each
            var load = plan.flyers.ToDictionary(f => f, f => 0);
            foreach (Pawn c in all.Where(p => !plan.flyers.Contains(p) && Avir.AvirChicks.IsChick(p) && p.HostFaction == null))
            {
                Pawn back = plan.flyers.FirstOrDefault(f => load[f] < Avir.AvirChicks.MaxChicks && Avir.AvirChicks.Family(f, c));
                if (back == null) continue;
                load[back]++;
                plan.chicks.Add(c);
            }
            plan.stay = all.Where(p => !plan.flyers.Contains(p) && !plan.chicks.Contains(p)).ToList();
            plan.groundHolds = plan.stay.Any(p => CaravanUtility.IsOwner(p, caravan.Faction) && !Avir.AvirChicks.IsChick(p));
            if (!plan.groundHolds)
            {
                List<Pawn> babies = plan.stay.Where(p => p.RaceProps.Humanlike && p.HostFaction == null && p.Faction == caravan.Faction).ToList();
                if (babies.Count > 0)
                {
                    plan.refusal = "No one could carry " + string.Join(", ", babies.Select(b => b.LabelShort)) + " - they cannot be left behind.";
                    return plan;
                }
                // grab what can be carried, the most precious per kilo first
                float free = plan.flyers.Sum(f => MassUtility.Capacity(f)) - plan.flyers.Sum(f => MassUtility.InventoryMass(f)) - plan.chicks.Sum(c => c.GetStatValue(StatDefOf.Mass));
                var cargo = plan.stay.SelectMany(p => p.inventory?.innerContainer?.ToList() ?? new List<Thing>())
                                     .OrderByDescending(t => t.MarketValue / Mathf.Max(0.01f, t.GetStatValue(StatDefOf.Mass))).ToList();
                foreach (Thing t in cargo)
                {
                    float m = Mathf.Max(0.0001f, t.GetStatValue(StatDefOf.Mass));
                    int n = Mathf.Min(t.stackCount, Mathf.FloorToInt(free / m));
                    if (n > 0) { plan.take.Add((t, n)); free -= n * m; }
                    if (n < t.stackCount) plan.lostCargo.Add(t);
                }
            }
            return plan;
        }

        public static IEnumerable<Gizmo> Gizmos(Caravan caravan)
        {
            if (caravan == null || !caravan.IsPlayerControlled || !caravan.PawnsListForReading.Any(CanFly)) yield break;
            if (icon == null) icon = ContentFinder<Texture2D>.Get("Avir/AbilityIcons/Avir_SoarIcon", false) ?? BaseContent.BadTex;
            yield return new Command_Action
            {
                defaultLabel = "Soar",
                defaultDesc = "Those who can fly take wing to a distant tile, the chicks of their family on their backs. Whoever cannot fly stays behind as a caravan with the animals and the cargo; if no one who can lead a caravan stays, the animals and the cargo the flyers cannot carry are lost. Afterwards, three hours of hard hunger and fatigue.",
                icon = icon,
                action = () => Target(caravan)
            };
        }

        private static void Target(Caravan caravan)
        {
            Plan first = Make(caravan);
            if (first.refusal != null) { Messages.Message(first.refusal, MessageTypeDefOf.RejectInput, false); return; }
            PlanetTile from = caravan.Tile;
            Find.WorldTargeter.BeginTargeting(t =>
            {
                if (!t.IsValid || !t.Tile.Valid) return false;
                if (t.Tile == from) { Messages.Message("Already at this tile.", MessageTypeDefOf.RejectInput, false); return false; }
                int range = first.range;
                if (Find.WorldGrid.TraversalDistanceBetween(from, t.Tile) > range) { Messages.Message("That tile is too far to reach in one flight.", MessageTypeDefOf.RejectInput, false); return false; }
                PlanetTile dest = t.Tile;
                Plan plan = Make(caravan);
                if (plan.refusal != null) { Messages.Message(plan.refusal, MessageTypeDefOf.RejectInput, false); return false; }
                string warn = Warning(plan);
                if (warn == null) Launch(caravan, plan, dest);
                else Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(warn, () => { if (!caravan.Destroyed) Launch(caravan, Make(caravan), dest); }, true));
                return true;
            }, true, null, true, null, t =>
            {
                if (!t.IsValid || !t.Tile.Valid) return null;
                int d = Find.WorldGrid.TraversalDistanceBetween(from, t.Tile);
                return d > first.range ? "Too far (" + d + " > " + first.range + ")" : d + " / " + first.range + " tiles";
            });
        }

        private static string Warning(Plan plan)
        {
            if (plan.stay.Count == 0) return null;
            var sb = new StringBuilder();
            if (plan.groundHolds)
            {
                sb.AppendLine("The caravan splits. Staying behind (they cannot fly), with the animals and the cargo they carry:");
                sb.AppendLine("  " + string.Join(", ", plan.stay.Select(p => p.LabelShort)));
                return sb.ToString();
            }
            sb.AppendLine("No one who can lead a caravan stays behind. Left behind and lost:");
            sb.AppendLine("  " + string.Join(", ", plan.stay.Select(p => p.LabelShort)));
            if (plan.lostCargo.Count > 0)
            {
                float value = plan.lostCargo.Sum(t => t.MarketValue * (t.stackCount - plan.take.Where(x => x.thing == t).Sum(x => x.count)));
                sb.AppendLine("Cargo the flyers cannot carry, lost (" + value.ToString("0") + " silver worth):");
                sb.AppendLine("  " + string.Join(", ", plan.lostCargo.Take(12).Select(t => t.LabelCapNoCount)) + (plan.lostCargo.Count > 12 ? "..." : ""));
            }
            sb.Append("Soar anyway?");
            return sb.ToString();
        }

        private static void Launch(Caravan caravan, Plan plan, PlanetTile dest)
        {
            if (plan.refusal != null || plan.flyers.Count == 0) return;
            PlanetTile from = caravan.Tile;
            var info = new ActiveTransporterInfo { openDelay = 110 };
            foreach ((Thing thing, int count) in plan.take) info.innerContainer.TryAddOrTransfer(thing, count);
            foreach (Pawn p in plan.flyers.Concat(plan.chicks))
            {
                caravan.RemovePawn(p);
                if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p);
                info.innerContainer.TryAddOrTransfer(p);
                CompAbilityEffect_FlyToTile soar = SoarOf(p);
                if (soar != null && plan.flyers.Contains(p) && soar.FlyProps.exhaustionHediff != null) p.health.AddHediff(soar.FlyProps.exhaustionHediff);
            }
            if (!plan.groundHolds)
            {
                foreach (Pawn p in plan.stay)
                {
                    caravan.RemovePawn(p);
                    if (p.HostFaction != null) p.guest?.SetGuestStatus(null);
                    if (p.RaceProps.Animal && p.Faction != null) p.SetFaction(null);
                    if (!Find.WorldPawns.Contains(p)) Find.WorldPawns.PassToWorld(p);
                }
            }
            if (!caravan.Destroyed && caravan.PawnsListForReading.Count == 0) caravan.Destroy();

            var pods = (TravellingTransporters)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.TravellingTransporters);
            pods.Tile = from;
            pods.SetFaction(Faction.OfPlayer);
            pods.destinationTile = dest;
            pods.arrivalAction = new TransportersArrivalAction_FormCaravan();
            Find.WorldObjects.Add(pods);
            pods.AddTransporter(info, false);
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class Patch_CaravanSoar
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Caravan __instance)
        {
            foreach (Gizmo g in gizmos) yield return g;
            foreach (Gizmo g in CaravanSoar.Gizmos(__instance)) yield return g;
        }
    }
}
