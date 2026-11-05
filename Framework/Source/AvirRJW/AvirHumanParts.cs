using RimWorld;
using Verse;

namespace AvirRJW
{
    /// <summary>Pawns already in a save carry RJW's generic parts: when the game is loaded each Avir female / harpy gets
    /// the human vagina, anus and small breasts instead, each framed male the human anus. Only generic parts are
    /// replaced and nothing is added where a part is missing, so it can run on every load.</summary>
    public class GameComponent_AvirHumanParts : GameComponent
    {
        public GameComponent_AvirHumanParts(Game game) { }

        public override void FinalizeInit()
        {
            if (!ModsConfig.IsActive("rim.job.world")) return;
            foreach (Pawn p in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
            {
                if (p?.health == null) continue;
                if (RjwPartEnforcer.IsHumanFemale(p) || RjwPartEnforcer.IsFramedMale(p))
                    RjwPartEnforcer.EnforceAvir(p);
            }
        }
    }
}
