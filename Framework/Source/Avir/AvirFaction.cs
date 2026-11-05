using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Who counts as "an Avir" for the purposes of names, backstories and traits.
    ///
    /// Originally this was gene-only: HasActiveGene(Fearborn). That is too narrow. The Avir
    /// backstories are now also reachable through a FactionDef's backstoryFilters (see
    /// Avir_FreeNesting), which is the vanilla-native route and works on pawns with no Avir genes
    /// at all - a plain human raised in a nesting is still a nesting-raised human.
    ///
    /// So the test is: does this pawn carry Fearborn, OR does it belong to an Avir faction?
    ///
    /// To make it FACTION-ONLY (no gene check at all), delete the Fearborn clause from
    /// ShouldUseAvirContent below - the faction check alone is enough.
    /// </summary>
    public static class AvirFaction
    {
        /// <summary>Any FactionDef whose defName begins with this is treated as an Avir faction.</summary>
        public const string FactionPrefix = "Avir_";

        /// <summary>
        /// True for any pawn whose KIND is one of ours (Avir_YoungHunter, Avir_Colonist...),
        /// regardless of who owns them.
        ///
        /// This is the case the faction check missed: a young hunter captured, recruited, or
        /// spawned into someone else's colony still has kindDef Avir_YoungHunter but belongs to
        /// a faction whose defName does not start with "Avir_" - so the gate said "not an Avir"
        /// and they kept their human name, human backstory and human traits.
        /// </summary>
        public static bool IsAvirPawnKind(Pawn pawn)
        {
            if (pawn == null || pawn.kindDef == null) return false;
            return pawn.kindDef.defName.StartsWith(FactionPrefix);
        }

        /// <summary>
        /// Same "Avir_" convention as IsAvirFaction, but for call sites that only have a
        /// FactionDef in hand (e.g. PawnGroupMakerParms.faction.def in a pawn-group patch),
        /// not a Pawn. Use this instead of comparing against a single named faction
        /// (AvirDefOf.Avir_HighNesting etc.) so every current and future Avir_-prefixed
        /// faction - HighNesting, AShialis, FreeNesting, ... - is covered by the same fix.
        /// </summary>
        /// <summary>The wild Avir of the Primal Hunt: never dressed, never armed, never given artifacts.</summary>
        public static bool IsPrimal(Pawn pawn) =>
            pawn?.Faction?.def?.defName == "Avir_Primal" || (pawn?.kindDef?.defName?.StartsWith("Avir_Primal") ?? false);

        public static bool IsAvirFactionDef(FactionDef def)
        {
            return def != null && def.defName != null && def.defName.StartsWith(FactionPrefix);
        }

        public static bool IsAvirFaction(Pawn pawn)
        {
            if (pawn == null) return false;

            Faction f = pawn.Faction;
            if (f == null || f.def == null) return false;

            return f.def.defName.StartsWith(FactionPrefix);
        }

        public static bool HasFearborn(Pawn pawn)
        {
            return pawn != null
                && pawn.genes != null
                && pawn.genes.HasActiveGene(AvirNameDefOf.Fearborn);
        }

        /// <summary>
        /// THE GATE AND THE POOL ARE TWO DIFFERENT QUESTIONS. Keep them apart.
        ///
        ///   GATE (this method) - "does this pawn get Avir content at all?"
        ///                        gene OR faction OR kind. Deliberately broad.
        ///
        ///   POOL (Patch_AvirBackstories.CastePool) - "WHICH Avir content?"
        ///                        the pawn's KIND, and nothing else. The kind IS the role:
        ///                        Avir_Warrior draws warrior stories because he is a warrior,
        ///                        whatever his genes say and whoever owns him this week.
        ///
        /// The gene never touches the pool. It only decides whether the pawn is an Avir at all.
        ///
        /// TO GO FACTION-ONLY: delete the HasFearborn clause below. Be aware of what that costs -
        /// an Avir-gened pawn generated into a NON-Avir faction with a vanilla kindDef (a refugee,
        /// a wanderer-joins, a starting colonist under a plain Colonist kind) fails both remaining
        /// clauses and walks in with a human name and a human backstory. That was the original bug
        /// the gene clause was added to fix. Faction-only is correct for ROLE; it is not sufficient
        /// as a gate, which is why it stays.
        /// </summary>
        /// <summary>
        /// True for a pawn belonging to the PLAYER, or to the Free Nesting player faction.
        ///
        /// The gear patches skip these entirely. A starting Avir colonist is meant to be NAKED
        /// and empty-handed - that is a deliberate design choice, not an oversight, and no
        /// integration gets to overrule it. Names, backstories and traits still apply; only
        /// the wardrobe is hands-off.
        /// </summary>
        public static bool IsPlayerAvir(Pawn pawn)
        {
            if (pawn == null) return false;

            if (pawn.Faction != null && pawn.Faction.IsPlayer) return true;

            if (pawn.kindDef != null && pawn.kindDef.defName == "Avir_Colonist") return true;

            if (pawn.Faction != null && pawn.Faction.def != null
                && pawn.Faction.def.defName == "Avir_FreeNesting") return true;

            return false;
        }

        /// <summary>
        /// THE RUNTIME GATE. "Is this pawn an Avir?" - a question about the pawn, never about who
        /// happens to own them today.
        ///
        /// WHAT WENT WRONG, AND IT WAS THE FACTION CLAUSE. This used to read
        /// `HasFearborn || IsAvirFaction || IsAvirPawnKind`, and the player's own colony is
        /// Avir_FreeNesting - a defName starting with "Avir_", so IsAvirFaction is TRUE for
        /// everyone in it. A captured prisoner's Faction is set to the capturing faction, so the
        /// moment a yttakin space refugee was taken prisoner she became, by this test, an Avir. The
        /// hourly sweep in GameComponent_AvirHatchling then did exactly what it was built to do:
        /// noticed her backstories were not from an Avir pool and replaced them. Her name went the
        /// same way. Nothing malfunctioned - the gate answered the wrong question, correctly.
        ///
        /// The same clause converted every recruited outsider in an Avir colony, not just
        /// prisoners. It was only ever visible on the prisoner because her species is written on
        /// the sheet next to the stolen history.
        ///
        /// So the faction clause is gone from the runtime answer. Genes and kind are what make an
        /// Avir, and both travel with the pawn: a young hunter captured by pirates keeps kindDef
        /// Avir_YoungHunter and is still handled, which was the case the faction clause was
        /// originally added for and which the kind clause already covered.
        /// </summary>
        public static bool ShouldUseAvirContent(Pawn pawn)
        {
            if (pawn == null) return false;
            return HasFearborn(pawn) || IsAvirPawnKind(pawn);
        }

        /// <summary>
        /// THE GENERATION-TIME GATE, and the only place the faction may still speak.
        ///
        /// PawnGenerator assigns genes AFTER names and backstories, so at the hook where those are
        /// set, HasFearborn is false for an Avir who certainly is one. The faction is the only
        /// evidence available that early - that is the whole reason the clause existed - so it is
        /// kept HERE, where the pawn is being built, and nowhere else.
        ///
        /// Two guards make it safe even here:
        ///
        ///   A PRISONER, SLAVE OR GUEST IS NEVER MADE ONE OF US BY CAPTURE. This is the direct fix
        ///   for the refugee. Being held by a faction is not membership of it.
        ///
        ///   GENES OUTRANK THE FACTION WHEN THEY EXIST. A pawn already carrying active genes has
        ///   had its species decided; if Fearborn is not among them, the answer is no whatever
        ///   faction is generating it. Only a pawn whose genes have not been rolled yet - the case
        ///   this method exists for - falls through to the faction.
        /// </summary>
        public static bool ShouldUseAvirContentAtGeneration(Pawn pawn)
        {
            if (pawn == null) return false;

            // Settled by the pawn itself. No need to look at anything else.
            if (ShouldUseAvirContent(pawn)) return true;

            if (!IsAvirFaction(pawn)) return false;

            // Held, not belonging.
            if (pawn.IsPrisoner || pawn.IsSlave) return false;
            if (pawn.guest != null && pawn.guest.HostFaction != null) return false;

            // Genes already rolled and Fearborn is not among them: this is somebody else's
            // species, generated by an Avir faction. ShouldUseAvirContent above already returned
            // false, so reaching here with genes present is a definite no.
            if (pawn.genes != null && pawn.genes.GenesListForReading != null
                && pawn.genes.GenesListForReading.Count > 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// True only for a pawn actually IN the player's own faction - not "should use Avir
        /// content" (which includes foreign Avir too), specifically "is this someone the player
        /// controls". Used to throttle expensive per-tick work (HediffComp_BeakConditioning,
        /// HediffComp_ClawConditioning) for visiting/trading/raiding Avir who aren't the player's
        /// own colonists - a large trade caravan of foreign Avir shouldn't cost the same
        /// continuous tick budget as an equal number of actual colonists, since the player never
        /// interacts with a visitor's claw conditioning or beak tier closely enough to need
        /// full-rate updates for it.
        /// </summary>
        public static bool IsPlayerFactionPawn(Pawn pawn)
        {
            return pawn?.Faction != null && pawn.Faction.IsPlayer;
        }
    }
}
