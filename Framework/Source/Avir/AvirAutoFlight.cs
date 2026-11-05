using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// WHEN AN AVIR DECIDES FOR ITSELF TO FLY, AND WHEN TO COME DOWN.
    ///
    /// Kept apart from AvirFlightRules on purpose. That file answers WHERE a pawn may be; this one
    /// answers WHETHER it should be airborne right now. Mixing them is how the mod ended up with
    /// two disagreeing definitions of a bad cell earlier today.
    ///
    /// THE TIMING PROBLEM, WHICH MAXIMUS NAMED AND WHICH SHAPES THIS WHOLE FILE.
    ///
    /// A pawn plans its whole route before it takes a single step. Deciding to fly halfway along is
    /// therefore almost useless - the route was already drawn by something that assumed legs, and
    /// the pawn is already walking the long way round. The decision has to be made BEFORE planning,
    /// which is why ShouldTakeOffFor is called from the StartPath prefix: it fires while the
    /// destination is known and the route is not yet built. Take off there and
    /// Patch_AvirDirectFlight draws a straight line instead.
    /// </summary>
    public static class AvirAutoFlight
    {
        /// <summary>
        /// Below this, no automatic takeoff. Maximus's figure.
        ///
        /// Deliberately far above the threshold for staying airborne: starting a long crossing on
        /// fumes means running out over the middle of it. A pawn already flying is allowed to keep
        /// going lower - it has to reach somewhere it can stand.
        /// </summary>
        public const float MinRestToTakeOff = 0.30f;

        /// <summary>
        /// How far along an already-planned route to look when deciding to take off in mid-walk.
        ///
        /// A pawn moves at most a dozen cells between two sweeps of the flight component, so this
        /// is comfortably far enough to reach the riverbank before the pawn does - which is the
        /// whole point. Taking off ON the bank rather than in midstream is also the only version
        /// of this the player can actually SEE happening.
        /// </summary>
        /// <summary>
        /// HOW LONG AFTER LANDING THIS PAWN REFUSES TO LEAVE THE GROUND AGAIN.
        ///
        /// THE ZIGZAG, WHICH MAXIMUS WATCHED A WHOLE TRADE CARAVAN PERFORM. They walk up into the
        /// mountain, come back out, go in again, come out again, and take longer to leave the map
        /// than any other faction's traders. It is a loop, and it is entirely this mod's doing:
        ///
        ///   1. The walking route crosses something worth flying over. ShouldTakeOffNow says yes.
        ///   2. GameComponent_AvirFlightControl takes off AND asks for a new route. The new one is
        ///      the straight line, which goes through the rock.
        ///   3. The flight ends - land-if-clear, exhaustion, whatever comes first.
        ///   4. The straight line is now unwalkable, so the pawn repaths on legs and goes around.
        ///   5. The route round the outside crosses the same obstacle. Back to step 1.
        ///
        /// Neither half of that is wrong on its own. Taking off to cross rock is correct; landing
        /// once clear is correct; repathing after each is correct. The fault is that nothing ever
        /// told the pawn it had just tried exactly this.
        ///
        /// Ten seconds is chosen to be longer than a crossing and shorter than a journey. A river
        /// is behind the pawn well inside it, so genuine crossings are untouched; a caravan
        /// oscillating against the same mountain hits this on every swing.
        /// </summary>
        private const int RelaunchCooldownTicks = 600;

        /// <summary>
        /// Is the relaunch cooldown still holding this pawn down?
        ///
        /// THE COOLDOWN ONLY APPLIES TO A PAWN THAT IS ACTUALLY GOING SOMEWHERE, and that is not a
        /// refinement - it is the fix for a bug the cooldown itself created.
        ///
        /// Maximus found an A'Shaign trader trying to leave the map, jammed against a wall,
        /// unable to get out. The cooldown was added the day before to stop caravans oscillating
        /// into a mountain, and it does - but written as a flat "no takeoff for ten seconds" it
        /// also refuses the takeoff of a pawn whose ONLY way onward is over the thing in front of
        /// it. Ten seconds of that, then a repath, then ten more seconds. A caravan can sit there
        /// indefinitely.
        ///
        /// The zigzag it was written for is a MOVING pawn: walk, lift, land, walk, lift. A pawn
        /// jammed at a wall is not moving at all. So testing pather.Moving separates the two cases
        /// exactly, with no threshold to tune and no way to strand anybody - a stuck pawn is never
        /// refused, because being stuck is precisely what "not moving" means.
        ///
        /// AvirFlightRescue does not cover this. It only wakes for a pawn standing INSIDE stone;
        /// a pawn standing on a perfectly good cell that simply cannot get anywhere is invisible
        /// to it, which is why the trader could sit there rather than being flown out.
        /// </summary>
        private static bool StillCoolingDown(Pawn pawn)
        {
            if (pawn.pather == null || !pawn.pather.Moving) return false;

            return AvirFlightUtility.TicksSinceLanded(pawn) < RelaunchCooldownTicks;
        }

        private const int TakeoffLookAhead = 25;

        /// <summary>Same question asked of a route already being walked, bounded so that a pawn
        /// crossing the map does not walk a two-hundred cell list every sweep.</summary>
        private const int LandingLookAhead = 20;

        /// <summary>
        /// The full takeoff test, asked once per path request, before the route exists.
        /// </summary>
        public static bool ShouldTakeOffFor(Pawn pawn, IntVec3 target)
        {
            try
            {
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return false;
                if (AvirFlightUtility.IsFlying(pawn)) return false;
                if (!AvirFlightUtility.CarriesFlightGene(pawn)) return false;

                // DRAFTED PAWNS ARE THE PLAYER'S TO POSITION - WITH ONE EXCEPTION, BELOW.
                //
                // The rule exists because a pawn that lifts off on its own wrecks a line the
                // player has just arranged by hand, at the worst possible moment. That reasoning
                // holds for discretionary flight and does not hold for water.
                //
                // MAXIMUS'S CASE, WHICH THE OLD ORDER OF THESE CHECKS GOT WRONG. He drafts a squad
                // and orders it across a river. The order to cross is HIS - automation is not
                // overriding a decision, it is carrying one out. Refusing here did not preserve
                // his formation, it just made his Avir wade while he watched, with the automatic
                // flight toggle on and the pawn fully rested. Nothing about the draft made that
                // the right answer.
                //
                // So the water clause is asked FIRST and the draft veto now guards only the
                // discretionary path beneath it. Rock, marsh and rubble still will not lift a
                // drafted pawn: those are conveniences, and a convenience is exactly the thing
                // that should not rearrange a battle line.
                //
                // Landing is already handled correctly and needs no change: the monitor's
                // AutoLandIfClear only ever ends flights it started, so a drafted Avir comes down
                // of its own accord once past the water, while a flight the player began with the
                // button stays up until the player ends it.
                if (!target.IsValid || target == pawn.Position) return false;

                // WATER IS NOT OPTIONAL. See ShouldCrossWater.
                if (ShouldCrossWater(pawn) && LineCrossesWater(pawn.Map, pawn.Position, target))
                {
                    return true;
                }

                if (pawn.Drafted) return false;

                // JUST CAME DOWN - see RelaunchCooldownTicks. Deliberately placed BELOW the water
                // clause: a mandatory crossing is never refused, so a river is still flown even
                // seconds after landing. Everything discretionary waits.
                if (StillCoolingDown(pawn)) return false;


                if (!AvirFlightUtility.CanTakeOff(pawn)) return false;

                // The player's switch. Water above has already returned; everything from here down
                // is discretionary flight and the switch governs all of it.
                if (!Gene_AvirFlight.AutoAllowed(pawn)) return false;

                if (RestLevel(pawn) < MinRestToTakeOff) return false;

                return LineWorthFlying(pawn.Map, pawn.Position, target);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] auto-takeoff test failed: " + e, 0x4A5261);
                return false;
            }
        }

        /// <summary>
        /// IS THIS PAWN ALLOWED TO MAKE A CROSSING IT CANNOT REFUSE?
        ///
        /// MAXIMUS'S RULE, IN HIS OWN TERMS. An Avir always flies over a river. Not when rested,
        /// not when the switch is on - always. He watched tired Avir wade a river at dawn on their
        /// way to pray at a tree, and he is right that this is absurd for a creature with wings.
        /// A river is at worst about twenty cells; crossing them in the air is not the hard part.
        ///
        /// SO THIS DELIBERATELY IGNORES TWO THINGS the discretionary path checks:
        ///   - Gene_AvirFlight.AutoAllowed, the player's automatic-flight switch. That switch is
        ///     about convenience flying. Wading a river is not convenience, it is a mistake, and
        ///     the switch does not get a vote on mistakes.
        ///   - MinRestToTakeOff (0.25). Replaced by the far lower WaterCrossingRestThreshold.
        ///
        /// AND WHAT IT STILL RESPECTS, so this is not a blanket override: a drafted pawn (checked
        /// by the callers, above), a dead or downed one, one with no wings, and one below the low
        /// water floor. The last is what stops a pawn lifting off with nothing left and being
        /// forced down again in midstream.
        /// </summary>
        private static bool ShouldCrossWater(Pawn pawn)
        {
            return AvirFlightUtility.CanTakeOff(pawn, AvirFlightUtility.WaterCrossingRestThreshold);
        }

        /// <summary>
        /// Water, and ONLY water, on the straight line to the target.
        ///
        /// Kept separate from LineWorthFlying rather than folded into it with a flag: that method
        /// answers three questions at once (unwalkable, water, expensive) and only ONE of them is
        /// the one Maximus made mandatory. Rock and marsh remain discretionary and remain under the
        /// switch. Merging them would have quietly made every obstacle in the game override the
        /// player's setting, which is not what was asked for.
        /// </summary>
        private static bool LineCrossesWater(Map map, IntVec3 from, IntVec3 to)
        {
            int dx = to.x - from.x;
            int dz = to.z - from.z;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dz));
            if (steps <= 0) return false;

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                IntVec3 c = new IntVec3(
                    from.x + (int)Math.Round(dx * t), 0,
                    from.z + (int)Math.Round(dz * t));

                if (!c.InBounds(map)) continue;
                if (AvirFlightTerrain.IsWaterAt(map, c)) return true;
            }

            return false;
        }

        /// <summary>
        /// Water on the remaining route, for the mid-walk check. Same reasoning as LineCrossesWater
        /// - the ROUTE is what catches a ford off to one side that the straight line missed, which
        /// is the case that had Avir wading in the first place.
        /// </summary>
        private static bool RouteCrossesWater(Pawn pawn, Pawn_PathFollower pather, int lookAhead)
        {
            PawnPath path = pather?.curPath;
            if (path == null || !path.Found) return false;

            Map map = pawn.Map;
            if (map == null) return false;

            int look = Math.Min(path.NodesLeftCount - 1, lookAhead);
            for (int i = 1; i <= look; i++)
            {
                IntVec3 c = path.Peek(i);
                if (!c.InBounds(map)) continue;
                if (AvirFlightTerrain.IsWaterAt(map, c)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the straight line to the target crosses anything that makes walking worse than
        /// flying.
        ///
        /// THREE KINDS OF OBSTACLE, NOT ONE. The original rule only asked "is anything impassable
        /// in the way" - so a pawn would happily wade a river because none of it is technically
        /// blocked. That was then widened to a COST test, which caught marsh and rubble but STILL
        /// let rivers through, because 1.6 water is swimmable and its walking cost no longer
        /// reflects how slow the crossing actually is. Water is now named outright, in
        /// AvirFlightTerrain, and no longer depends on any number.
        ///
        /// The line is walked in full rather than sampled, and never stops early: the one cell most
        /// worth knowing about is often the far one.
        /// </summary>
        private static bool LineWorthFlying(Map map, IntVec3 from, IntVec3 to)
        {
            PathGrid grid = map.pathing?.Normal?.pathGrid;
            if (grid == null) return false;

            int dx = to.x - from.x;
            int dz = to.z - from.z;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dz));
            if (steps <= 0) return false;

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                IntVec3 c = new IntVec3(
                    from.x + (int)Math.Round(dx * t), 0,
                    from.z + (int)Math.Round(dz * t));

                if (!c.InBounds(map)) continue;

                if (AvirFlightTerrain.WorthFlyingOver(map, c)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether an airborne pawn has any remaining reason to be airborne.
        ///
        /// NOT UNDER THE PLAYER'S SWITCH, BY MAXIMUS'S INSTRUCTION. A pawn hovering over a
        /// workbench or standing about at a chess table is simply wrong, whatever the switch says.
        /// The switch governs the decision to LEAVE the ground, not the failure to return to it.
        ///
        /// Returns false whenever landing would strand the pawn - inside stone, inside a sealed
        /// room - so this can never drop an Avir into rock. That check is AvirFlightRules, the same
        /// one everything else in the mod uses.
        /// </summary>
        public static bool ShouldLandNow(Pawn pawn)
        {
            try
            {
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return false;
                if (!AvirFlightUtility.IsFlying(pawn)) return false;

                // DRAFTED PAWNS ARE THE PLAYER'S TO POSITION - THE MIRROR OF ShouldTakeOffFor.
                //
                // Takeoff already refuses to lift a drafted pawn, on the grounds that automation
                // must not rearrange a line the player set by hand. Landing has to answer to the
                // same principle, and did not: a drafted pawn holding position is by definition
                // not moving, so the "not travelling, nothing to fly for" test below fired on the
                // very next sweep and folded the wings a tick after the player raised them.
                //
                // Note this is NOT the autoFlightAllowed switch. That switch stays out of landing,
                // as before - a pawn idling at a workbench still comes down whatever it says. This
                // is a narrower rule: while the player holds direct control, only the player takes
                // this pawn out of the air, using the flight button.
                if (pawn.Drafted) return false;

                // Never come down somewhere it cannot stand. This outranks every reason below.
                if (AvirFlightRules.CannotStandAt(pawn.Map, pawn.Position)) return false;

                // AND NEVER INTO WATER, which is not the same rule.
                //
                // CannotStandAt is about stone and sealed vaults: places a pawn must not be left.
                // A river is somewhere a pawn CAN stand, and standing there is exactly the misery
                // this whole change exists to prevent - so an Avir that has just crossed half a
                // river does not land in the middle of it because its job was cancelled mid-flight.
                //
                // Deliberately narrower than the takeoff test: that one also fires on marsh and
                // rubble, and vetoing landing on those as well would leave a pawn hovering over its
                // own boggy field for the length of a harvest job. Water only.
                if (AvirFlightTerrain.IsWaterAt(pawn.Map, pawn.Position)) return false;

                // NOT TRAVELLING: nothing to fly for.
                //
                // This is the case Maximus asked about directly - recreation, hauling at a bench,
                // standing in a queue. A pawn with no route is not going anywhere and does not need
                // wings to do it.
                Pawn_PathFollower pather = pawn.pather;
                if (pather == null || !pather.Moving) return true;

                // Travelling, but over ground it could simply walk. Same reasoning, applied ahead
                // of the pawn instead of underneath it.
                return !RouteStillNeedsWings(pawn, pather);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] auto-landing test failed: " + e, 0x4A5262);
                return false;
            }
        }

        /// <summary>
        /// Looks along the remaining route for anything still worth flying over.
        ///
        /// Bounded rather than exhaustive: a pawn crossing the map should not walk a two-hundred
        /// cell list every time this is asked. Twenty cells is far enough ahead to cover the pawn's
        /// stopping distance several times over, and the question is asked again as it advances.
        /// </summary>
        private static bool RouteStillNeedsWings(Pawn pawn, Pawn_PathFollower pather)
        {
            return RouteNeedsWings(pawn, pather, LandingLookAhead);
        }

        /// <summary>
        /// Shared by both directions. Landing asks with a short horizon - is there any reason LEFT
        /// to be up here - and takeoff asks with a longer one, because it must fire before the pawn
        /// reaches the obstacle rather than once it is standing in it.
        /// </summary>
        private static bool RouteNeedsWings(Pawn pawn, Pawn_PathFollower pather, int lookAhead)
        {
            PawnPath path = pather?.curPath;
            if (path == null || !path.Found) return false;

            Map map = pawn.Map;
            if (map == null) return false;

            int left = path.NodesLeftCount;
            int look = Math.Min(left - 1, lookAhead);

            for (int i = 1; i <= look; i++)
            {
                IntVec3 c = path.Peek(i);
                if (!c.InBounds(map)) continue;

                if (AvirFlightTerrain.WorthFlyingOver(map, c)) return true;
            }

            return false;
        }

        /// <summary>
        /// TAKEOFF IN MID-WALK, asked periodically by GameComponent_AvirFlightControl.
        ///
        /// ShouldTakeOffFor answers the question at the moment a route is REQUESTED, from a
        /// straight line drawn to the destination. That covers most trips and misses two whole
        /// classes of them, both of which Maximus has been watching happen:
        ///
        ///   - the straight line is clear but the ROUTE is not. A ford lies off to one side, so the
        ///     pawn walks a bend that crosses water the line never touched. Nothing ever asks again
        ///     and the pawn wades.
        ///   - the pawn was refused at request time - too tired, drafted, switch off - and the
        ///     circumstances changed while it walked.
        ///
        /// The route itself is therefore consulted as the pawn advances. This is also why takeoff
        /// looks further ahead than landing: the pawn must leave the ground ON the bank, both so
        /// that it never pays for a single cell of water and so that the player can SEE it happen,
        /// which was half the complaint.
        ///
        /// Standing in water is treated as reason enough by itself, with no route required - a pawn
        /// that somehow ended up midstream should get out of it whether it is going anywhere or not.
        /// </summary>
        public static bool ShouldTakeOffNow(Pawn pawn)
        {
            try
            {
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return false;
                if (AvirFlightUtility.IsFlying(pawn)) return false;
                if (!AvirFlightUtility.CarriesFlightGene(pawn)) return false;

                // Drafted pawns are the player's to position - EXCEPT for water, exactly as in
                // ShouldTakeOffFor. The water clause is therefore asked first and the veto guards
                // only the discretionary path below it.
                Pawn_PathFollower pather = pawn.pather;

                // WATER IS NOT OPTIONAL - the switch, the ordinary rest floor and the draft veto
                // do not apply. See ShouldCrossWater.
                if (ShouldCrossWater(pawn))
                {
                    // Already wet. Nothing else needs to be true, and this is the case that gets a
                    // pawn OUT of a river it has already walked into - drafted or not, which is
                    // the situation Maximus photographed.
                    if (AvirFlightTerrain.IsWaterAt(pawn.Map, pawn.Position)) return true;

                    if (pather != null && pather.Moving
                        && RouteCrossesWater(pawn, pather, TakeoffLookAhead))
                    {
                        return true;
                    }
                }

                if (pawn.Drafted) return false;

                // JUST CAME DOWN - see RelaunchCooldownTicks. Deliberately placed BELOW the water
                // clause: a mandatory crossing is never refused, so a river is still flown even
                // seconds after landing. Everything discretionary waits.
                if (StillCoolingDown(pawn)) return false;


                if (!AvirFlightUtility.CanTakeOff(pawn)) return false;
                if (!Gene_AvirFlight.AutoAllowed(pawn)) return false;
                if (RestLevel(pawn) < MinRestToTakeOff) return false;

                if (pather == null || !pather.Moving) return false;

                return RouteNeedsWings(pawn, pather, TakeoffLookAhead);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] mid-walk takeoff test failed: " + e, 0x4A5263);
                return false;
            }
        }

        private static float RestLevel(Pawn pawn)
        {
            Need_Rest rest = pawn.needs?.rest;
            return rest == null ? 1f : rest.CurLevel;
        }
    }
}
