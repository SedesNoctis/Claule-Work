using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FlightGene
{
    public class CompProperties_AbilityFlyToTile : CompProperties_AbilityEffect
    {
        public int maxTileDistance = 30;
        public HediffDef exhaustionHediff;
        public int cooldownTicks = 2500;

        public CompProperties_AbilityFlyToTile()
        {
            compClass = typeof(CompAbilityEffect_FlyToTile);
        }
    }

    /// <summary>
    /// A world-tile flight: opens the world map, lets the caster pick a destination within range,
    /// then launches them there using the game's transport-pod travel (drop-pod style). Cooldown
    /// and the exhaustion hediff are applied only when an actual launch happens, so cancelling the
    /// targeter is free.
    /// </summary>
    public class CompAbilityEffect_FlyToTile : CompAbilityEffect
    {
        private new CompProperties_AbilityFlyToTile Props => (CompProperties_AbilityFlyToTile)props;
        public CompProperties_AbilityFlyToTile FlyProps => Props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent?.pawn;
            if (caster == null || !caster.Spawned || caster.Map == null)
            {
                return;
            }

            int sourceTile = caster.Map.Tile;

            // a drafted flight soars together: every other selected, drafted Avir that can soar joins in.
            // Several selected casters all fire this - only the first opens the world map.
            if (groupTick == Find.TickManager.TicksGame) return;
            groupTick = Find.TickManager.TicksGame;
            group = Flight(caster);

            CameraJumper.TryJump(CameraJumper.GetWorldTarget(caster));
            Find.WorldSelector.ClearSelection();

            // If the build errors on this call, it's a 1.6 signature mismatch - send me the error.
            Find.WorldTargeter.BeginTargeting(
                (GlobalTargetInfo t) => ChoseTile(caster, sourceTile, t),
                true,    // canTargetTiles
                null,    // mouse attachment
                true,    // close world tab when finished
                null,    // onUpdate
                (GlobalTargetInfo t) => TargetLabel(sourceTile, t));
        }

        private static int groupTick = -1;
        private static List<Pawn> group = new List<Pawn>();

        /// <summary>The caster and, when it is drafted, every selected drafted pawn on its map that can soar too.</summary>
        private static List<Pawn> Flight(Pawn caster)
        {
            var list = new List<Pawn> { caster };
            if (!caster.Drafted) return list;
            foreach (Pawn p in Find.Selector.SelectedPawns)
            {
                if (p == caster || !p.Spawned || p.Map != caster.Map || !p.Drafted || p.Downed) continue;
                if (p.abilities?.abilities?.Any(a => a.def == caster.abilities?.abilities?.FirstOrDefault(x => x.CompOfType<CompAbilityEffect_FlyToTile>() != null)?.def) != true) continue;
                list.Add(p);
            }
            return list;
        }

        private string TargetLabel(int sourceTile, GlobalTargetInfo t)
        {
            if (!t.IsValid || t.Tile < 0)
            {
                return null;
            }
            int dist = Find.WorldGrid.TraversalDistanceBetween(sourceTile, t.Tile);
            if (dist > Props.maxTileDistance)
            {
                return "Too far (" + dist + " > " + Props.maxTileDistance + ")";
            }
            return dist + " / " + Props.maxTileDistance + " tiles";
        }

        private bool ChoseTile(Pawn caster, int sourceTile, GlobalTargetInfo target)
        {
            if (!target.IsValid || target.Tile < 0)
            {
                return false;
            }

            int destTile = target.Tile;
            if (destTile == sourceTile)
            {
                Messages.Message("Already at this tile.", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            int dist = Find.WorldGrid.TraversalDistanceBetween(sourceTile, destTile);
            if (dist > Props.maxTileDistance)
            {
                Messages.Message("That tile is too far to reach in one flight.", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!caster.Spawned)
            {
                return false;
            }

            LaunchTo(group.Count > 0 && group.Contains(caster) ? group : new List<Pawn> { caster }, sourceTile, destTile);
            return true;
        }

        private void LaunchTo(List<Pawn> flyers, int sourceTile, int destTile)
        {
            // Pull the flight off the map into one transporter payload (1.6 transporter system): every
            // flyer, and the chicks on their backs - they arrive as one caravan.
            ActiveTransporterInfo podInfo = new ActiveTransporterInfo();
            podInfo.openDelay = 110;
            foreach (Pawn flyer in flyers.Where(f => f != null && f.Spawned).ToList())
            {
                List<Pawn> chicks = Avir.AvirChicks.ChicksOf.TryGetValue(flyer, out List<Pawn> l) ? l.Where(c => c != null && c.Spawned).ToList() : new List<Pawn>();
                foreach (Pawn chick in chicks)
                {
                    chick.DeSpawn(DestroyMode.Vanish);
                    podInfo.innerContainer.TryAddOrTransfer(chick);
                }
                flyer.DeSpawn(DestroyMode.Vanish);
                podInfo.innerContainer.TryAddOrTransfer(flyer);
                if (Props.exhaustionHediff != null && flyer.health != null) flyer.health.AddHediff(Props.exhaustionHediff);
            }

            TravellingTransporters pods = (TravellingTransporters)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.TravellingTransporters);
            pods.Tile = sourceTile;
            pods.SetFaction(Faction.OfPlayer);
            pods.destinationTile = destTile;
            pods.arrivalAction = new TransportersArrivalAction_FormCaravan();
            Find.WorldObjects.Add(pods);
            pods.AddTransporter(podInfo, true);

            // No cooldown (it caused a stuck gizmo when the pawn despawns mid-cast). The deterrent
            // is the exhaustion hediff, given to every flyer above.
            group = new List<Pawn>();
        }
    }
}
