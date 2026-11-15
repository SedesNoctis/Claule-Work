using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir
{
    /// <summary>
    /// THE AVIR'S FLIGHT IN (the author, after Fix271): Avir come down out of the sky on their own wings - not in a drop
    /// pod. No pod lands, nothing is left behind, no roof is broken (they come down only under the open sky), nothing is
    /// struck where they land. A short descent: the wings spread, the shadow grows, dust where they touch down.
    /// Used by the Soar of the player's Avir (landing at a place) and by the raids of Avir factions (like a drop-pod
    /// attack, at the edge or into the colony). Raiders of the A'Shaign Order drink Crimson Rage the moment they land;
    /// the others are tired from the flight (the A'Shai too - the rage outweighs it).
    /// </summary>
    public class AvirFlightIncoming : Skyfaller
    {
        public bool raid;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref raid, "raid");
        }

        protected override void SpawnThings()
        {
            var pawns = innerContainer.OfType<Pawn>().ToList();
            Map map = Map; IntVec3 at = Position;
            base.SpawnThings();
            if (map != null)
            {
                FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 1.6f, new Color(0.75f, 0.7f, 0.6f, 0.6f));
                FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 1.2f, new Color(0.75f, 0.7f, 0.6f, 0.5f));
            }
            foreach (Pawn p in pawns) AvirFlight.Landed(p, raid);
        }
    }

    public static class AvirFlight
    {
        public static ThingDef Incoming => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_FlightIncoming");
        public static HediffDef Tired => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_FlightExhaustion");
        public static ThingDef Rage => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_CrimsonRage");
        /// <summary>The Avir factions: the A'Shaign Order, the A'Schialis Legion (Avir_AShialis), Free Nesting, the Primal Hunt.</summary>
        public static bool AvirFaction(Faction f) => f?.def != null && (f.def.defName == "Avir_HighNesting" || f.def.defName == "Avir_AShialis" || f.def.defName == "Avir_FreeNesting" || f.def.defName == "Avir_Primal");
        public static bool AShai(Faction f) => f?.def?.defName == "Avir_HighNesting";

        /// <summary>A landing cell under the open sky near `near` (never through a roof).</summary>
        public static IntVec3 Spot(IntVec3 near, Map map, int radius = 8)
        {
            if (CellFinder.TryFindRandomCellNear(near, map, radius, c => c.Standable(map) && !c.Roofed(map) && !c.Fogged(map) && c.GetFirstPawn(map) == null, out IntVec3 r)) return r;
            if (CellFinder.TryFindRandomCellNear(near, map, radius * 3, c => c.Standable(map) && !c.Roofed(map), out r)) return r;
            return DropCellFinder.RandomDropSpot(map);
        }

        /// <summary>Everyone (and everything carried) comes down round `center`, each on wings of its own; things
        /// are carried down by the flyers, in their arms.</summary>
        public static void LandAll(IEnumerable<Thing> things, IntVec3 center, Map map, bool raid, int spread = 6)
        {
            ThingDef def = Incoming;
            var list = things.Where(t => t != null).ToList();
            var cargo = list.Where(t => !(t is Pawn)).ToList();
            var pawns = list.OfType<Pawn>().ToList();
            int k = 0;
            foreach (Pawn p in pawns)
            {
                var load = new List<Thing> { p };
                // the cargo is shared among the flyers
                for (int i = k; i < cargo.Count; i += Mathf.Max(1, pawns.Count)) load.Add(cargo[i]);
                k++;
                IntVec3 c = Spot(center, map, spread);
                if (def == null) { foreach (Thing t in load) GenPlace.TryPlaceThing(t, c, map, ThingPlaceMode.Near); Landed(p, raid); continue; }
                var sky = (AvirFlightIncoming)SkyfallerMaker.MakeSkyfaller(def, load);
                sky.raid = raid;
                GenSpawn.Spawn(sky, c, map);
            }
            if (pawns.Count == 0) foreach (Thing t in cargo) GenPlace.TryPlaceThing(t, Spot(center, map, spread), map, ThingPlaceMode.Near);
        }

        /// <summary>On the ground: a raider of the A'Shaign Order drinks Crimson Rage at once; other raiders are tired from
        /// the flight. (The player's flyers were tired already when they took off.)</summary>
        public static void Landed(Pawn p, bool raid)
        {
            if (p == null || p.Dead || !raid || p.RaceProps?.Humanlike != true) return;
            if (AShai(p.Faction) && Rage != null)
            {
                try { Thing dose = ThingMaker.MakeThing(Rage); dose.Ingested(p, 0f); }
                catch (System.Exception e) { Log.Warning("[Avir] crimson rage on landing: " + e.Message); }
            }
            else if (Tired != null && !p.health.hediffSet.HasHediff(Tired)) p.health.AddHediff(Tired);
        }
    }

    public static class AvirFlightDev
    {
        private static void Raid(string faction, string mode)
        {
            Map map = Find.CurrentMap; Faction f = Find.FactionManager.AllFactions.FirstOrDefault(x => x.def.defName == faction);
            if (map == null || f == null) { Messages.Message("No such faction in this game: " + faction, MessageTypeDefOf.RejectInput, false); return; }
            var parms = new IncidentParms { target = map, faction = f, points = 600f, raidStrategy = RaidStrategyDefOf.ImmediateAttack, raidArrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(mode), forced = true };
            IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
        }

        [LudeonTK.DebugAction("Avir", "Flight: A'Shai raid out of the sky (into the colony)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void RaidAShai() => Raid("Avir_HighNesting", "Avir_FlyInCenter");

        [LudeonTK.DebugAction("Avir", "Flight: A'Schialis Legion raid out of the sky (at the edge)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void RaidLegion() => Raid("Avir_AShialis", "Avir_FlyIn");

        [LudeonTK.DebugAction("Avir", "Flight: Free Nesting raid out of the sky (at the edge)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void RaidFree() => Raid("Avir_FreeNesting", "Avir_FlyIn");

        [LudeonTK.DebugAction("Avir", "Flight: selected pawns fly down here", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void LandHere()
        {
            IntVec3 c = Verse.UI.MouseCell(); Map map = Find.CurrentMap;
            var pawns = Find.Selector.SelectedPawns.Where(p => p.Spawned).ToList();
            foreach (Pawn p in pawns) p.DeSpawn();
            AvirFlight.LandAll(pawns.Cast<Thing>(), c, map, false, 4);
        }
    }

    /// <summary>The raid of an Avir faction out of the sky: at the edge of the map, or straight into the colony.</summary>
    public class PawnsArrivalModeWorker_AvirFlyIn : PawnsArrivalModeWorker
    {
        protected virtual bool IntoColony => false;

        public override bool CanUseWith(IncidentParms parms) => base.CanUseWith(parms) && AvirFlight.AvirFaction(parms.faction);

        public override void Arrive(List<Pawn> pawns, IncidentParms parms)
        {
            Map map = (Map)parms.target;
            AvirFlight.LandAll(pawns.Cast<Thing>(), parms.spawnCenter, map, true, IntoColony ? 6 : 10);
        }

        public override void TravellingTransportersArrived(List<ActiveTransporterInfo> transporters, Map map)
        {
            IntVec3 c = IntoColony && DropCellFinder.TryFindRaidDropCenterClose(out IntVec3 close, map, false, false) ? close : DropCellFinder.FindRaidDropCenterDistant(map);
            var things = new List<Thing>();
            foreach (ActiveTransporterInfo t in transporters) { things.AddRange(t.innerContainer); t.innerContainer.Clear(); }
            AvirFlight.LandAll(things, c, map, false);
        }

        public override bool TryResolveRaidSpawnCenter(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!parms.spawnCenter.IsValid)
            {
                if (IntoColony && DropCellFinder.TryFindRaidDropCenterClose(out IntVec3 close, map, false, false)) parms.spawnCenter = close;
                else parms.spawnCenter = DropCellFinder.FindRaidDropCenterDistant(map);
            }
            parms.spawnRotation = Rot4.Random;
            return true;
        }
    }

    public class PawnsArrivalModeWorker_AvirFlyInCenter : PawnsArrivalModeWorker_AvirFlyIn
    {
        protected override bool IntoColony => true;
    }

    /// <summary>The player's Soar landing at a place: on a map that is there - at the cell chosen; on a tile with no
    /// map - the map is made, and they come down near its middle.</summary>
    public class TransportersArrivalAction_AvirLand : TransportersArrivalAction
    {
        public IntVec3 cell = IntVec3.Invalid;
        public TransportersArrivalAction_AvirLand() { }
        public TransportersArrivalAction_AvirLand(IntVec3 cell) { this.cell = cell; }

        public override bool GeneratesMap => true;
        public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile) => Find.WorldObjects.MapParentAt(tile)?.HasMap != true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            Map map = Find.WorldObjects.MapParentAt(tile)?.Map ?? GetOrGenerateMapUtility.GetOrGenerateMap(tile, null);
            if (map == null) return;
            IntVec3 c = cell.IsValid && cell.InBounds(map) ? cell : map.Center;
            var things = new List<Thing>();
            foreach (ActiveTransporterInfo t in transporters) { things.AddRange(t.innerContainer); t.innerContainer.Clear(); }
            AvirFlight.LandAll(things, c, map, false, 4);
            Find.LetterStack.ReceiveLetter("Landed", "The flight came down out of the sky.", LetterDefOf.NeutralEvent, new LookTargets(c, map));
        }
    }

    /// <summary>Where a Soar comes down - the author's two ways and an attack: as a caravan (as before), at a place
    /// (the map is made if it is not there), or straight onto a hostile settlement (at its edge or into it). A panel of
    /// the mod's own, not the game's pop-up menu.</summary>
    public class Dialog_SoarMode : Window
    {
        private readonly PlanetTile tile;
        private readonly System.Action<TransportersArrivalAction> launch;
        public Dialog_SoarMode(PlanetTile tile, System.Action<TransportersArrivalAction> launch)
        {
            this.tile = tile; this.launch = launch;
            doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true; forcePause = true;
        }
        public override Vector2 InitialSize => new Vector2(440f, 330f);

        public override void DoWindowContents(Rect r)
        {
            Text.Font = GameFont.Medium; GUI.color = new Color(0.95f, 0.88f, 0.7f);
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "Where to come down");
            Text.Font = GameFont.Small; GUI.color = Color.white;
            float y = r.y + 44f;
            MapParent mp = Find.WorldObjects.MapParentAt(tile);
            Settlement st = mp as Settlement;
            bool hostile = st != null && st.Faction != null && st.Faction.HostileTo(Faction.OfPlayer);
            if (Row(ref y, r, "As a caravan", "Land on the world map as a caravan, as before.")) { launch(new TransportersArrivalAction_FormCaravan()); Close(); }
            if (hostile)
            {
                if (Row(ref y, r, "Attack - at the edge", "Come down at the edge of their settlement and fight your way in.")) { launch(new TransportersArrivalAction_AttackSettlement(st, DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("Avir_FlyIn"))); Close(); }
                if (Row(ref y, r, "Attack - into it", "Come down straight into their settlement, under the open sky.")) { launch(new TransportersArrivalAction_AttackSettlement(st, DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail("Avir_FlyInCenter"))); Close(); }
            }
            else if (mp?.HasMap == true)
            {
                if (Row(ref y, r, "At a place - choose it", "Come down at a cell you choose on that map (under the open sky)."))
                {
                    Map map = mp.Map; Close();
                    CameraJumper.TryJump(map.Center, map);
                    Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), t => launch(new TransportersArrivalAction_AvirLand(t.Cell)));
                }
            }
            else if (Row(ref y, r, "At a place", "Come down on the land itself: the map is made, and they land near its middle - a flight closes the distance already at the take-off.")) { launch(new TransportersArrivalAction_AvirLand()); Close(); }
        }

        private static bool Row(ref float y, Rect r, string label, string desc)
        {
            Rect b = new Rect(r.x, y, r.width, 52f); y += 58f;
            Widgets.DrawBoxSolid(b, Mouse.IsOver(b) ? new Color(1f, 0.9f, 0.6f, 0.14f) : new Color(0f, 0f, 0f, 0.18f));
            GUI.color = new Color(0.95f, 0.9f, 0.8f); Widgets.Label(new Rect(b.x + 8f, b.y + 4f, b.width - 16f, 22f), label);
            GUI.color = new Color(0.75f, 0.73f, 0.68f); Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(b.x + 8f, b.y + 24f, b.width - 16f, 28f), desc);
            Text.Font = GameFont.Small; GUI.color = Color.white;
            return Widgets.ButtonInvisible(b);
        }
    }
}
