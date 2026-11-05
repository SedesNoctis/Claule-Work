using System.Collections.Generic;
using Verse;

namespace Avir
{
    /// <summary>
    /// One active Choir. Not a Def - a plain runtime record, owned and ticked by
    /// GameComponent_AvirChoir. IExposable so an in-progress Choir survives a save/load
    /// instead of leaving members stuck with a hediff nothing is maintaining anymore.
    /// </summary>
    public class AvirChoirState : IExposable
    {
        public AvirChoirDef def;
        public Pawn caster;
        public List<Pawn> members = new List<Pawn>();
        public List<Pawn> optedOut = new List<Pawn>();
        public List<Pawn> enemyTargets = new List<Pawn>();
        public int fearbornCount;
        public int nextDrainAbsTick;

        /// <summary>Absolute tick this choir ends itself. Only meaningful when def.durationTicks
        /// is above zero; sustained choirs leave it at zero and are never checked against it.</summary>
        public int endAbsTick;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_References.Look(ref caster, "caster");
            Scribe_Collections.Look(ref members, "members", LookMode.Reference);
            Scribe_Collections.Look(ref optedOut, "optedOut", LookMode.Reference);
            Scribe_Collections.Look(ref enemyTargets, "enemyTargets", LookMode.Reference);
            Scribe_Values.Look(ref fearbornCount, "fearbornCount");
            Scribe_Values.Look(ref nextDrainAbsTick, "nextDrainAbsTick");
            Scribe_Values.Look(ref endAbsTick, "endAbsTick");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (members == null) members = new List<Pawn>();
                if (optedOut == null) optedOut = new List<Pawn>();
                if (enemyTargets == null) enemyTargets = new List<Pawn>();

                // SAVES MADE BEFORE CHOIRS BECAME DEF-DRIVEN HAVE NO def NODE AT ALL, and there
                // was only ever one kind of choir to be in the middle of, so an amulet choir is
                // the only thing this can have been. Without this, an old save with a live choir
                // loads a state whose def is null and the very first GameComponentTick
                // dereferences it - and the singers would be left holding Avir_InTheChoir with
                // nothing maintaining it, which is precisely the failure ExposeData exists to
                // prevent.
                if (def == null) def = AvirDefOf.Avir_Choir_Silversong;
            }
        }
    }
}
