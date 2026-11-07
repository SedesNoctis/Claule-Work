using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Sorcerers run in the blood (the author, Fix157): a child of two sorcerers is always a sorcerer. It inherits
    /// a tenth of its parents' experience together in every school, and its element comes from one of its sorcerer
    /// forebears at random - a parent, or an elder of the line (grandparents and further) - so sorcery runs in dynasties.
    /// Looked for among the young on every map once in a while, so any way of birth is caught.</summary>
    public class GameComponent_SorcererBlood : GameComponent
    {
        private HashSet<int> seen = new HashSet<int>();
        public GameComponent_SorcererBlood(Game g) { }

        public override void GameComponentTick()
        {
            if (!AvirMagic.On || Find.TickManager.TicksGame % 2500 != 0) return;
            foreach (Map m in Find.Maps)
                foreach (Pawn p in m.mapPawns.AllPawns.Where(x => x.RaceProps.Humanlike && x.ageTracker != null && x.ageTracker.AgeBiologicalYearsFloat < 1f).ToList())
                {
                    if (seen.Contains(p.thingIDNumber)) continue;
                    seen.Add(p.thingIDNumber);
                    Inherit(p);
                }
        }

        public static bool IsSorcerer(Pawn p) => p?.story?.traits?.GetTrait(MagicDefOf.Avir_ArcaneGift)?.Degree == Mages.Born;

        /// <summary>The sorcerer forebears of a pawn: parents first, then their parents and further up.</summary>
        public static List<Pawn> Forebears(Pawn p, int depth = 4)
        {
            var list = new List<Pawn>();
            var frontier = new List<Pawn> { p };
            for (int d = 0; d < depth && frontier.Count > 0; d++)
            {
                var up = frontier.SelectMany(x => x.relations?.DirectRelations.Where(r => r.def == PawnRelationDefOf.Parent).Select(r => r.otherPawn) ?? Enumerable.Empty<Pawn>()).Where(x => x != null && !list.Contains(x)).Distinct().ToList();
                list.AddRange(up);
                frontier = up;
            }
            return list;
        }

        public static void Inherit(Pawn child)
        {
            if (child == null || Mages.IsMage(child)) return;
            var parents = child.relations?.DirectRelations.Where(r => r.def == PawnRelationDefOf.Parent).Select(r => r.otherPawn).Where(x => x != null).ToList();
            if (parents == null || parents.Count < 2 || !parents.All(IsSorcerer)) return;
            Mages.Gift(child, Mages.Born);
            Hediff_ArcaneCore c = Mages.Core(child);
            if (c == null) return;
            // a tenth of both parents' experience together, school by school
            foreach (RuneKind k in parents.SelectMany(x => Mages.Core(x)?.xp.Keys ?? Enumerable.Empty<RuneKind>()).Distinct().ToList())
            {
                float sum = parents.Sum(x => MageClass.XpIn(Mages.Core(x), k));
                if (sum > 0f) c.xp[k] = Mathf.Max(MageClass.XpIn(c, k), 0.1f * sum);
            }
            // the element of one of the line's sorcerers, at random
            var line = Forebears(child).Where(IsSorcerer).Select(x => Mages.Core(x)?.path).Where(x => x != null).ToList();
            if (line.Count > 0) { c.path = line.RandomElement(); c.Note("Born to " + c.path.label + ", as the blood remembers."); }
            if (child.Faction == Faction.OfPlayer)
                Find.LetterStack.ReceiveLetter("A sorcerer born: " + child.LabelShort,
                    child.LabelShortCap + " is the child of two sorcerers, and the gift came with the blood: a sorcerer from the cradle" + (c.path != null ? ", of " + c.path.label : "") + ", with a tenth of the parents' learning in every school.",
                    LetterDefOf.PositiveEvent, child);
        }

        public override void ExposeData()
        {
            var l = seen.ToList();
            Scribe_Collections.Look(ref l, "sorcererBloodSeen", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) seen = new HashSet<int>(l ?? new List<int>());
        }
    }

    public static class SorcererBloodDev
    {
        [LudeonTK.DebugAction("Magic", "Sorcerer blood: inherit now (a child of two sorcerers)", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void InheritNow(Pawn p) => GameComponent_SorcererBlood.Inherit(p);
    }
}
