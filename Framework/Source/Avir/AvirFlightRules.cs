using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// THE ONE ANSWER TO "MAY A PAWN STAND HERE".
    ///
    /// Before this file the mod held two different answers. The phasing gate asked about roofs and
    /// rooms; HediffComp_FlightMonitor asked Patch_AvirFlightPathing.CellNeedsFlight, which is
    /// nothing but !Walkable. Those are different sets of cells - the floor of an unopened Ancient
    /// Danger is walkable and sealed, a boulder in open air is unwalkable and not sealed - so the
    /// monitor and the gate disagreed about half the map. That disagreement is the same shape as
    /// the router/mover deadlock that cost the previous session: two parts of the mod enforcing
    /// different rules on one pawn.
    ///
    /// Everything now calls CannotStandAt. Nothing else decides.
    ///
    /// THE RULE, as agreed:
    ///
    ///     impassable cell (built wall, natural rock, anything Traversability.Impassable) -> no
    ///     enclosed room with no opening in its roof                                       -> no
    ///     everything else                                                                 -> yes
    ///
    /// Crossing either kind of cell in flight is still allowed and always was. This is only about
    /// where a journey may END.
    /// </summary>
    public static class AvirFlightRules
    {
        /// <summary>
        /// Inside solid matter. Rock and built wall are the same thing here, by Maximus's explicit
        /// decision - there is deliberately no separation of natural stone from masonry, because
        /// every rule that needed one turned into two rules that could drift apart.
        /// </summary>
        public static bool IsWall(Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map)) return false;

            // The path grid is the authority the pathfinder itself uses, so asking it keeps this
            // answer and the pathfinder's answer identical by construction. PathGrid.Walkable
            // already accounts for impassable edifices.
            PathGrid grid = map.pathing?.Normal?.pathGrid;
            if (grid == null) return false;

            return !grid.Walkable(c);
        }

        /// <summary>
        /// An interior nobody has opened. Kept exactly as the gate had it - that logic was correct
        /// and had simply never been reached, because AvirFlightUtility.CarriesFlightGene was
        /// returning false for every pawn in the game.
        /// </summary>
        public static bool IsSealed(Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map)) return false;

            RoofDef roof = c.GetRoof(map);

            // Nothing overhead: never sealed, whatever the walls around it look like. A small
            // roofless pen reads as an interior to the room system and is not one.
            if (roof == null) return false;

            Room room = c.GetRoom(map);
            bool enclosed = room != null && room.ProperRoom && !room.PsychologicallyOutdoors;

            if (enclosed)
            {
                // A ROOM WITH A WAY IN IS NOT SEALED, WHATEVER ITS CEILING LOOKS LIKE.
                //
                // This used to ask only `room.OpenRoofCount <= 0` — a hole in the ROOF. That reads
                // as "sealed" for any ordinary bedroom, workshop or cave mouth with an intact
                // ceiling, no matter how many doors it has. The consequence was a pawn who lived
                // in a room she was forbidden to land in:
                //
                //   - landing INSIDE was refused, because the cell sat under an unbroken roof;
                //   - taking off from inside was allowed, because the cell OUTSIDE has no roof at
                //     all and passes on the first check;
                //   - crossing walls in flight was always legal, only ENDING in one was not.
                //
                // So she rose from bed, flew out through the wall, stood there, walked back in
                // through the door, lay down, and did it again. Two doors on the room changed
                // nothing, because doors were never consulted.
                //
                // The rule the roof check was reaching for is "nobody has opened this yet", and the
                // honest test for that is whether there is a WAY IN. An Ancient Danger before it is
                // breached has none: solid shell, no door, no gap. That case still returns true
                // here and flight still refuses to drop anyone inside it.
                //
                // Doors count even when closed and even when locked. A closed door is an entrance
                // that happens to be shut; the vault this rule exists for has no door at all.
                if (room.OpenRoofCount > 0) return false;

                foreach (Region reg in room.Regions)
                {
                    List<RegionLink> links = reg.links;
                    if (links == null) continue;

                    for (int i = 0; i < links.Count; i++)
                    {
                        Region other = links[i].GetOtherRegion(reg);
                        if (other == null) continue;

                        // A link leading anywhere outside this room is an entrance: a doorway, a
                        // door region, or a plain gap in the wall. Any one of them is enough.
                        if (other.Room != room) return false;
                    }
                }

                return true;
            }

            // Roofed and belonging to no room. Two different things land here.
            //
            // A WALL is the shell, not the space, and has no room by definition. IsWall covers it
            // and it is not sealed.
            //
            // AN UNDISCOVERED INTERIOR also has no room, because the region system builds none for
            // ground nobody has seen. A fogged vault therefore looks exactly like a wall, and the
            // old blanket "roomless is fine" waved it through - which is why a pawn could fly IN
            // and then not fly OUT: entering was judged before the room existed, leaving after.
            Building edifice = c.GetEdifice(map);
            if (edifice != null && edifice.def.passability == Traversability.Impassable) return false;

            // HOTFIX (see FLIGHT_HOTFIX_NOTES.md): the paragraph above only ever justified this
            // branch for GROUND NOBODY HAS SEEN. The code never actually checked that - it fired
            // for every roofed cell with no ProperRoom, seen or not. A revealed open-sided roofed
            // area (a porch, a broken room, a room that never formed its own Room object) has no
            // ProperRoom either and was being waved through into "sealed", which is exactly the
            // "any roofed room reads as bricked up" symptom being fixed here.
            //
            // Restoring the actual fog test makes the branch do only what its own comment always
            // claimed: revealed ground is never treated as an unopened interior, whatever its
            // Room status. Fogged ground keeps the old behaviour untouched.
            if (!c.Fogged(map)) return false;

            // Bare rock and open cave under natural stone are fine. Only a constructed ceiling over
            // unseen empty floor is an interior somebody sealed.
            return !roof.isNatural;
        }

        /// <summary>
        /// THE SHELL OF A BUILDING. Wall, door frame, carved rock face - anything solid that stands
        /// between the open air and a roofed room somebody made.
        ///
        /// WHY THIS EXISTS, AND WHY IsSealed WAS NEVER GOING TO COVER IT. IsSealed answers "has
        /// anyone opened this yet", and it deliberately says NO the moment a room has a way in -
        /// that fix was correct and is what lets an Avir land in her own bedroom. But the mod then
        /// had no rule at all against crossing the WALL of a room that does have a door. So a
        /// mountain colony with walls, doors and a roof was flown into through the rock, which is
        /// precisely the thing flight was never meant to do.
        ///
        /// The rule is now the one Maximus stated: crossing open-air obstacles is flight; crossing
        /// the skin of a building is walking through walls. A shell cell is closed to flight in
        /// BOTH directions - an Avir inside leaves by the door too.
        ///
        /// WHAT IS DELIBERATELY NOT A SHELL. Solid mountain with nothing hollow beside it: a rock
        /// cell only counts if it touches a roofed room, so a hillside, a boulder field or a
        /// hundred cells of unbroken stone are still crossed on the wing exactly as before. That
        /// distinction is what keeps flight useful while making buildings solid.
        ///
        /// CHEAP BY CONSTRUCTION, because this is asked for every cell of the map on a timer.
        /// A solid cell in deep rock has no walkable neighbour, so it leaves on eight array reads
        /// with no room lookup at all - and deep rock is most of what this loop sees on Maximus's
        /// maps. Only cells actually touching open floor pay for a room query.
        /// </summary>
        public static bool IsEnclosureShell(Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map)) return false;

            PathGrid grid = map.pathing?.Normal?.pathGrid;
            if (grid == null) return false;

            // Only solid things can be a shell. A door is walkable, so a door is never a shell -
            // which is the point: the door stays the way in.
            if (grid.Walkable(c)) return false;

            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 n = c + GenAdj.AdjacentCells[i];
                if (!n.InBounds(map)) continue;

                // Array read. Filters out the whole interior of a mountain before anything
                // expensive runs.
                if (!grid.Walkable(n)) continue;

                // Open sky on the other side is not an interior, whatever the walls look like.
                if (n.GetRoof(map) == null) continue;

                Room room = n.GetRoom(map);
                if (room == null) continue;
                if (!room.ProperRoom || room.PsychologicallyOutdoors) continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// EVERYTHING FLIGHT MAY NOT PASS THROUGH, in one predicate.
        ///
        /// Two separate reasons, kept separate because they mean different things and one of them
        /// is about to be argued over again someday:
        ///
        ///   IsSealed          - an interior nobody has opened. An Ancient Danger before it is
        ///                       breached. Nothing may enter, by any means.
        ///   IsEnclosureShell  - the wall of a building that DOES have a door. Not forbidden
        ///                       ground; forbidden ROUTE. Use the door.
        ///
        /// Callers that ask "may flight carry a pawn through here" use this. Callers that ask
        /// "may a pawn stop here" use CannotStandAt, which is a different question and must stay
        /// one: a wall is crossable-in-principle but never standable, while the open floor of a
        /// roofed room is standable but only reachable through its door.
        /// </summary>
        public static bool BlocksFlight(Map map, IntVec3 c)
        {
            return IsSealed(map, c) || IsEnclosureShell(map, c);
        }

        /// <summary>Nowhere a journey may end, and nowhere flight may be switched off.</summary>
        public static bool CannotStandAt(Map map, IntVec3 c)
        {
            return IsWall(map, c) || IsSealed(map, c);
        }

        /// <summary>
        /// Where a rescued pawn is put down: the nearest cell with NO ROOF at all.
        ///
        /// Deliberately stricter than "anywhere legal". Open sky is guaranteed to be outside the
        /// structure; the next legal cell might be the next chamber of the same tomb, which would
        /// mean rescuing a pawn from an Ancient Danger into an Ancient Danger.
        ///
        /// Returns IntVec3.Invalid when nothing is found inside the radius. Callers must handle
        /// that rather than assuming success.
        /// </summary>
        public static IntVec3 NearestOpenSky(Map map, IntVec3 from, float radius)
        {
            if (map == null) return IntVec3.Invalid;

            int count = GenRadial.NumCellsInRadius(radius);
            for (int i = 0; i < count; i++)
            {
                IntVec3 c = from + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;
                if (c.GetRoof(map) != null) continue;
                if (CannotStandAt(map, c)) continue;
                if (!c.Standable(map)) continue;

                return c;
            }

            return IntVec3.Invalid;
        }

        /// <summary>
        /// Fallback for the flying escape: the nearest cell it is legal to stand on, roof or not.
        ///
        /// A pawn flying out under its own power does not need open sky - it needs somewhere to
        /// land, and its own colony has a roof over most of it. Only the emergency teleport insists
        /// on open sky.
        /// </summary>
        public static IntVec3 NearestFooting(Map map, IntVec3 from, float radius)
        {
            if (map == null) return IntVec3.Invalid;

            int count = GenRadial.NumCellsInRadius(radius);
            for (int i = 0; i < count; i++)
            {
                IntVec3 c = from + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;
                if (CannotStandAt(map, c)) continue;
                if (!c.Standable(map)) continue;

                return c;
            }

            return IntVec3.Invalid;
        }
    }
}
