using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AVIR ARE NEVER STOUT.
    ///
    /// Compatibility with Everybody's Different (astryl.EverybodysDifferent), which gives every
    /// pawn one of nine heritable frame genes crossing stature (short / average / tall) against
    /// build (slight / average / stout). The Avir are the apex predator - lean, honed, built for
    /// the kill - and there is no version of that which is also bulky.
    ///
    /// STATURE IS LEFT ENTIRELY ALONE. A short Avir and a towering Avir are both perfectly good
    /// Avir, and the mod's whole appeal here is that they vary in size. Only the build axis is
    /// clamped, and only at its top end: Stout becomes Mid, Slight is untouched.
    ///
    ///     EBD_Frame_ShortStout  ->  EBD_Frame_ShortMid
    ///     EBD_Frame_MidStout    ->  EBD_Frame_MidMid
    ///     EBD_Frame_TallStout   ->  EBD_Frame_TallMid
    ///
    /// COMPLETE NO-OP WITHOUT THAT MOD. Every def is resolved once by GetNamedSilentFail; if the
    /// map comes back empty the substitution function returns its argument unchanged and the
    /// sweeps exit on the first line.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirFrameLock
    {
        private static Dictionary<GeneDef, GeneDef> stoutToMid;
        private static bool resolved;

        /// <summary>
        /// Guards the sweeps against re-entering through the AddGene prefix. Cheap, and it means
        /// the substitution never has to reason about a list that is being mutated underneath it.
        /// </summary>
        private static bool sweeping;

        private static readonly string[][] Pairs =
        {
            new[] { "EBD_Frame_ShortStout", "EBD_Frame_ShortMid" },
            new[] { "EBD_Frame_MidStout",   "EBD_Frame_MidMid"   },
            new[] { "EBD_Frame_TallStout",  "EBD_Frame_TallMid"  },
        };

        /// <summary>True for the three stout frames, so the object-overload hook can leave on one
        /// dictionary lookup for every other gene in the game.</summary>
        public static bool IsStoutFrame(GeneDef def)
        {
            return def != null && Map.ContainsKey(def);
        }

        private static Dictionary<GeneDef, GeneDef> Map
        {
            get
            {
                if (resolved) return stoutToMid;
                resolved = true;
                stoutToMid = new Dictionary<GeneDef, GeneDef>();

                for (int i = 0; i < Pairs.Length; i++)
                {
                    GeneDef from = DefDatabase<GeneDef>.GetNamedSilentFail(Pairs[i][0]);
                    GeneDef to = DefDatabase<GeneDef>.GetNamedSilentFail(Pairs[i][1]);
                    if (from != null && to != null) stoutToMid[from] = to;
                }

                if (stoutToMid.Count > 0)
                    Log.Message("[Avir] Everybody's Different detected - Fearborn pawns locked out of "
                        + stoutToMid.Count + " stout frames.");

                return stoutToMid;
            }
        }

        /// <summary>Returns the lean equivalent of a stout frame for a Fearborn pawn, or the
        /// original def for anything else.</summary>
        public static GeneDef Substitute(Pawn pawn, GeneDef def)
        {
            if (def == null || Map.Count == 0) return def;
            if (!AvirFaction.HasFearborn(pawn)) return def;

            GeneDef lean;
            return Map.TryGetValue(def, out lean) ? lean : def;
        }

        /// <summary>
        /// Fixes a pawn who already has a stout frame. Only needed where the substitution could
        /// not run at the point of addition - see the two callers.
        ///
        /// Remove-then-add rather than substitute, because by this point the Gene object already
        /// exists and its def cannot be swapped out from under it. Xenogene status is carried
        /// across so an implanted frame stays implanted.
        /// </summary>
        public static void EnforceLeanFrame(Pawn pawn)
        {
            if (sweeping) return;
            if (pawn == null || pawn.genes == null || Map.Count == 0) return;
            if (!AvirFaction.HasFearborn(pawn)) return;

            List<Gene> snapshot = null;
            List<Gene> live = pawn.genes.GenesListForReading;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] == null || live[i].def == null) continue;
                if (!Map.ContainsKey(live[i].def)) continue;

                if (snapshot == null) snapshot = new List<Gene>();
                snapshot.Add(live[i]);
            }
            if (snapshot == null)
            {
                // NOTHING TO SWAP IS NOT NOTHING TO DO. A pawn can carry an unobjectionable frame
                // gene - or none at all - and still be rendered Fat because something set the body
                // directly. The body check has to run on that path too, or the exact case Maximus
                // photographed slips through the early return.
                EnforceLeanBody(pawn);
                return;
            }

            sweeping = true;
            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    Gene g = snapshot[i];
                    GeneDef lean = Map[g.def];
                    bool xeno = pawn.genes.Xenogenes.Contains(g);

                    pawn.genes.RemoveGene(g);
                    pawn.genes.AddGene(lean, xeno);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Frame lock sweep failed on " + pawn.LabelShortCap + ": " + e);
            }
            finally
            {
                sweeping = false;
            }

            EnforceLeanBody(pawn);
        }

        /// <summary>
        /// THE BODY ITSELF, NOT THE GENE THAT WAS SUPPOSED TO SET IT.
        ///
        /// Swapping the frame gene is only half a guarantee. Notify_GenesChanged rebuilds
        /// story.bodyType from a gene ONLY when that gene declares a bodyType of its own - read out
        /// of its IL - so a mod that assigns the body directly, in its own generation patch, leaves
        /// a stout Avir stout no matter how carefully the genes are corrected.
        ///
        /// That is not a hypothetical: an Avir was standing there round-bellied with its gear not
        /// rendering, because apparel textures have no Fat variant to draw on.
        ///
        /// So the invariant is stated about the thing that actually matters: AN AVIR IS NEVER FAT,
        /// whatever put it there and whichever mod did it. The graphics are marked dirty too, or the
        /// pawn goes on being drawn with the body it no longer has until something else happens to
        /// refresh it.
        ///
        /// HULK IS DELIBERATELY LEFT ALONE. An earlier version of this clamped it as well, on my
        /// own reasoning that an apex predator is lean rather than bulky. That was me designing the
        /// species over Maximus's head - a heavy Avir is a considered part of the design and it is
        /// not mine to remove. Only the belly goes.
        ///
        /// Children and babies are left entirely alone: their body types are a developmental stage,
        /// not a build.
        /// </summary>
        public static void EnforceLeanBody(Pawn pawn)
        {
            try
            {
                if (pawn?.story == null || !AvirFaction.HasFearborn(pawn)) return;

                BodyTypeDef current = pawn.story.bodyType;
                if (current == null) return;
                if (current != BodyTypeDefOf.Fat) return;

                // Not a build - a life stage. Leave them be.
                if (current == BodyTypeDefOf.Baby || current == BodyTypeDefOf.Child) return;
                if (pawn.DevelopmentalStage.Juvenile()) return;

                BodyTypeDef replacement = pawn.gender == Gender.Female
                    ? BodyTypeDefOf.Female
                    : BodyTypeDefOf.Male;

                pawn.story.bodyType = replacement;

                if (pawn.Drawer?.renderer != null) pawn.Drawer.renderer.SetAllGraphicsDirty();

                if (!announcedBody)
                {
                    announcedBody = true;
                    Log.Message("[Avir] " + pawn.LabelShortCap + " was carrying the "
                        + current.defName + " body type; Avir are never fat. Set to " + replacement.defName
                        + ". Gear renders on this one.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Frame lock body correction failed: " + e, 0x4A5275);
            }
        }

        private static bool announcedBody;
    }

    /// <summary>
    /// THE MAIN HOOK, AND IT REWRITES RATHER THAN REPAIRS.
    ///
    /// The original version of this patch was a POSTFIX that removed the stout gene and added the
    /// lean one afterwards. That works, but it means the wrong gene is genuinely on the pawn for
    /// a moment - long enough for Everybody's Different's own hooks to fire on it, recompute a
    /// height and weight, and then have to be undone - and every removal re-entered this same
    /// patch through the AddGene it performs.
    ///
    /// A prefix that rewrites the argument never puts the stout gene on the pawn at all. Nothing
    /// to undo, nothing to re-enter, no flicker.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GeneTracker), "AddGene", new Type[] { typeof(GeneDef), typeof(bool) })]
    public static class Patch_AvirFrameLock_AddGeneDef
    {
        public static void Prefix(Pawn_GeneTracker __instance, ref GeneDef geneDef)
        {
            try { geneDef = AvirFrameLock.Substitute(__instance.pawn, geneDef); }
            catch (Exception e) { Log.WarningOnce("[Avir] Frame lock prefix failed: " + e, 0x4A5215); }
        }
    }

    /// <summary>
    /// THE OTHER AddGene, AND ITS ABSENCE IS WHY STOUT AVIR KEPT APPEARING.
    ///
    /// Pawn_GeneTracker has two: AddGene(GeneDef, bool) and AddGene(Gene, bool) - both confirmed in
    /// the assembly's metadata. Only the first was patched. Anything handing over an already-built
    /// Gene object went straight past the lock: inheritance from parents, gene copying, xenogerm
    /// implantation, and any mod that constructs the Gene itself before handing it over.
    ///
    /// A pawn whose frame arrived by that route was never checked at all, which is exactly the
    /// stout Strider on Maximus's screen.
    ///
    /// The substitution cannot rewrite the argument here - the Gene is already constructed around
    /// its def - so this one lets the call through and corrects the pawn afterwards. EnforceLeanFrame
    /// is idempotent and guards its own re-entry, so a postfix is safe where a prefix is not.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GeneTracker), "AddGene", new Type[] { typeof(Gene), typeof(bool) })]
    public static class Patch_AvirFrameLock_AddGeneObject
    {
        public static void Postfix(Pawn_GeneTracker __instance, Gene gene)
        {
            try
            {
                if (gene?.def == null) return;

                // Only wake up for the three defs this is about. Every other gene in the game -
                // and there are hundreds, added constantly - leaves on this line.
                if (!AvirFrameLock.IsStoutFrame(gene.def)) return;

                AvirFrameLock.EnforceLeanFrame(__instance.pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Frame lock object-overload postfix failed: " + e, 0x4A5274);
            }
        }
    }

    /// <summary>
    /// GENE ORDER IS THE REASON THIS SECOND HOOK EXISTS. The prefix above can only recognise a
    /// Fearborn pawn if Fearborn is already on them - and during generation there is no guarantee
    /// the frame gene arrives second. Whenever it arrives first, the prefix sees an ordinary pawn
    /// and lets the stout frame through.
    ///
    /// So generation gets a sweep afterwards, when the full gene set is known. Priority.Last so
    /// it runs after every other mod's generation postfixes rather than before them.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirFrameLock_GeneratePawn
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn __result)
        {
            AvirFrameLock.EnforceLeanFrame(__result);
        }
    }

    /// <summary>
    /// One pass when a save loads, so adding this to an existing colony immediately corrects any
    /// Avir who rolled stout before it existed.
    ///
    /// Walks Find.Maps and the world pawn pool by hand rather than using PawnsFinder. That is not
    /// stylistic: PawnsFinder's members have been renamed between versions and one of them has
    /// already broken this mod once this session
    /// (AllMapsCaravansAndTravelingTransporters_Alive_FreeColonists, CS0117). MapPawns.AllPawns
    /// and WorldPawns.AllPawnsAliveOrDead have not moved in years.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_AvirFrameLock_FinalizeInit
    {
        public static void Postfix()
        {
            try
            {
                List<Pawn> all = AvirPhoenixUtil.AllBondCandidates();
                for (int i = 0; i < all.Count; i++)
                {
                    AvirFrameLock.EnforceLeanFrame(all[i]);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Frame lock load sweep failed: " + e);
            }
        }
    }
}
