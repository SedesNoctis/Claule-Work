using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    [DefOf]
    public static class AvirDefOf
    {
        public static GeneDef Avir_Prideful;
        public static GeneDef Avir_PsychicNature;
        // PredatorMindset was merged into Fearborn (AvirGeneMerge.cs).
        public static GeneDef Fearborn;
        public static TraitDef Avir_PridefulTrait;
        public static ThoughtDef Avir_PridefulApparelQuality;
        public static ThoughtDef Avir_PridefulNudity;
        public static JobDef Avir_CarrySleeperToBed;
        public static JobDef Avir_TelekineticHaul;
        // Needed by AvirChoirState.ExposeData: a save written before choirs became def-driven
        // has no def node, and the amulet choir is the only thing it can have been.
        public static AvirChoirDef Avir_Choir_Silversong;
        public static HediffDef Avir_InTheChoir;
        public static HediffDef Avir_TheSong;
        public static HediffDef Avir_EcstaticFlames;
        public static ThoughtDef Avir_MelodyIsOngoing;
        public static ResearchProjectDef Avir_ApexAlloycraft;
        public static ResearchProjectDef Avir_Cloudweaving;
        public static ResearchProjectDef Avir_PinnacleOfConstruction;
        public static FactionDef Avir_HighNesting;
        public static PawnKindDef Avir_Strider;
        public static PawnKindDef Avir_Huntress;
        public static PawnKindDef Avir_Priestess;
        public static PawnKindDef Avir_YoungHunter;
        public static PawnKindDef Avir_Predator;
        public static PawnKindDef Avir_Artificer;

        // A'Schialis Legion - only defined when Vanilla Memes Expanded is active (see
        // Compat/VMemes/Defs/FactionDefs/Faction_AShialis.xml). MayRequire keeps
        // DefOfHelper from logging errors when VME isn't loaded; the fields simply stay
        // null, and Patch_Avir_TraderCaravanGuards.AddGuards already no-ops on a null kind.
        [MayRequire("VanillaExpanded.VMemesE")]
        public static FactionDef Avir_AShialis;
        [MayRequire("VanillaExpanded.VMemesE")]
        public static PawnKindDef Avir_Centurion;
        [MayRequire("VanillaExpanded.VMemesE")]
        public static PawnKindDef Avir_Legionnaire;
        [MayRequire("VanillaExpanded.VMemesE")]
        public static PawnKindDef Avir_Inquisitor;

        // TraitDef, not ThoughtDef. ThoughtWorkers_AvirTraits_Extra.cs calls
        // other.story.traits.HasTrait(AvirDefOf.Avir_ManyMated), and the def itself is a
        // <TraitDef> in Traits_Avir_Fearborn_Extra.xml. (Thoughts_Avir_Traits_Extra.xml has a
        // separate ThoughtDef called Avir_ManyMatedJudgement - similar name, different thing.)
        public static TraitDef Avir_ManyMated;

        static AvirDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirDefOf));
        }
    }
}
