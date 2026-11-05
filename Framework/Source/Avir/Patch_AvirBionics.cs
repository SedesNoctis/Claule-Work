using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE AVIR DO NOT WEAR OTHER PEOPLE'S MACHINERY.
    ///
    /// A bionic arm is a thing a ground-bound species built because it could not grow a better
    /// one. The Avir grew an essentium beak that sharpens itself on whatever it kills, hollow
    /// bones, and an immune system propped up entirely by psychic sensitivity. To bolt a
    /// factory-stamped limb onto that is, to them, a downgrade wearing a badge.
    ///
    /// Archotech parts are the exception - and not because they are better, though they are. It
    /// is because the Avir MAKE them. A species that began as psychic light folded into flesh does
    /// not find archotech mysterious; it finds it familiar.
    ///
    /// BUT NOT ARCHOTECH LIMBS. See Patch_AvirLimbLock. An archotech arm destroys the essentium
    /// claws to install itself exactly as a bionic one does; it is a better hook, and a hook is
    /// still not a claw. Archotech survives on an Avir only where it is not a limb - an eye, an
    /// organ, a spine.
    ///
    /// So:
    ///   - every non-archotech implant, prosthetic and bionic is STRIPPED at generation
    ///   - archotech is kept, unless it sits on a limb, in which case it comes off too
    ///   - the senior castes may be born with an archotech EYE. Never an arm, never a leg.
    ///   - the Avir's OWN parts (beak, claws, anything Avir_*) are never touched
    ///
    /// This applies to EVERY Avir, both factions, the player's Free Nesting included. An Avir
    /// hatches with what an Avir hatches with, whoever owns them.
    ///
    /// It fires ONLY at generation. Once a colonist is yours, what you install in them by surgery
    /// afterwards is entirely your business and nothing here interferes. The lock is on what they
    /// ARRIVE with, not on what they may become. Archotech is the one thing still granted at
    /// generation to the faction's senior castes only - your pawns don't get a free one.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirBionics
    {
        /// <summary>Chance a senior Avir has grown up around enough archotech to be wearing some.</summary>
        private static float ArchotechChanceFor(string kind)
        {
            if (kind == "Avir_Matriarch") return 0.35f;
            if (kind == "Avir_Predator")  return 0.25f;
            if (kind == "Avir_Priestess") return 0.20f;
            if (kind == "Avir_GuildMaster") return 0.15f;
            return 0f;
        }

        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || pawn.health == null) return;

                // GENES, NOT PAWNKIND - AND THIS IS WHY HATCHLINGS WERE BORN WITH MACHINERY.
                //
                // The test used to be IsAvirPawnKind, which asks whether the defName starts with
                // "Avir_". Every Avir the WORLD generates satisfies that. A chick hatched in the
                // player's own colony does not: a colony-born pawn carries an ordinary colonist
                // pawnkind, so neither this lock nor the spawn-time one ever looked at it, and
                // whatever installed a shoulder turret and a bionic leg on a newborn was left
                // completely alone.
                //
                // ShouldUseAvirContent asks the only question that actually means "is this an
                // Avir": genes, or an Avir pawnkind. Both hold at this point - GeneratePawn has
                // finished assigning genes by the time this postfix runs - and the gene half
                // travels with the pawn wherever it was born.
                //
                // Same mistake as the backstory gate earlier: identity read off a name instead of
                // off the creature.
                if (!AvirFaction.ShouldUseAvirContent(pawn)) return;

                // FREE NESTING IS NO LONGER EXEMPT.
                //
                // This used to early-return on IsPlayerAvir, so a Free Nesting Avir - your own
                // colonists included - could still be GENERATED wearing someone else's machinery,
                // handed to them by a scenario, a hediff mod, or anything else that installs at
                // generation time. That is the same downgrade wearing a badge whoever owns them.
                // An Avir hatches with what an Avir hatches with.
                //
                // THIS DOES NOT TOUCH SURGERY. The patch is a postfix on GeneratePawn and fires
                // exactly once, at the moment the pawn is created. Once a colonist is yours, what
                // you choose to install in them afterwards is entirely your business and nothing
                // here interferes with it. The lock is on what they ARRIVE with, not on what they
                // may become.
                // TOTAL. No archotech grant. No exceptions. See the note on Strip().
                Strip(pawn);

                // ...with exactly one thing granted afterwards rather than stripped: a magical
                // tattoo, on a small minority. Rolled AFTER Strip so the two can never race -
                // Strip already exempts tattoos, but ordering it this way means the grant is
                // the last word regardless of what the exemption list looks like later.
                MaybeGrantTattoo(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Bionic lock failed: " + e, 0x4A51C6);
            }
        }

        /// <summary>True if this hediff is archotech - by def name, and by the part it drops.</summary>
        private static bool IsArchotech(HediffDef def)
        {
            if (def == null) return false;

            if (def.defName != null && def.defName.IndexOf("Archotech", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Some mods name the hediff something else but drop an archotech part when removed.
            if (def.spawnThingOnRemoved != null)
            {
                ThingDef t = def.spawnThingOnRemoved;

                if (t.defName != null && t.defName.IndexOf("Archotech", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (t.techLevel == TechLevel.Archotech) return true;
            }

            return false;
        }

        /// <summary>Ours. Beaks, claws, feathered fur - never touched.</summary>
        private static bool IsAvirOwn(HediffDef def)
        {
            return def != null && def.defName != null && def.defName.StartsWith("Avir_");
        }

        /// <summary>
        /// Things that are technically Hediff_Implant but are not somebody else's machinery.
        ///
        /// THE LIST IS NO LONGER IN THIS FILE. It is XML now - see
        /// Defs/MiscDefs/ImplantExemptions_Avir.xml. Add new exemptions there.
        /// </summary>
        private static bool IsExempt(Hediff h)
        {
            if (h == null || h.def == null) return false;

            // EVERYTHING NOW LIVES IN Defs/MiscDefs/ImplantExemptions_Avir.xml.
            //
            // This method used to hardcode psylink by class and by the literal string "Psylink",
            // with tattoos added next to it. That approach broke as soon as there was more than
            // one link-shaped implant in play: vanilla's mechlink was never covered, a
            // third-party mechlink analogue was invisible, and a mod that RELABELS psylink as
            // "arcane link" defeated the string match outright.
            //
            // AvirImplantExemption reads the XML, matches on defName / substring / hediff tag /
            // class chain, and caches per def. Adding a new link mod is one line of XML and no
            // rebuild. The class-chain match is what makes a renamed psylink still register.
            return AvirImplantExemption.ExemptFromLock(h.def);
        }

        /// <summary>
        /// Removes non-archotech implants AND GIVES THE PAWN BACK THE ORIGINAL LIMB.
        ///
        /// THIS WAS A BAD BUG. The first version called RemoveHediff() on the bionic. But an
        /// installed bionic leg has already DESTROYED the natural leg - the hediff IS the leg.
        /// Removing it left the pawn with nothing there at all. That is why hunters were turning
        /// up with no legs, no arms, no hands, and why the log said:
        ///
        ///     "Ka-Scarria tried to wear Boots but he has no body parts required to wear it."
        ///
        /// He had no feet. I had taken them.
        ///
        /// RestorePart() is the correct call: it removes the added part AND regrows the original
        /// body part underneath, which is exactly what "this Avir never had a bionic" should mean.
        /// </summary>
        /// <summary>Public entry point for GameComponent_AvirBionicsSweep's periodic safety-net
        /// pass - same Strip() logic, callable from outside this class without changing its
        /// existing generation-time behavior at all.</summary>
        public static void SweepStrip(Pawn pawn)
        {
            Strip(pawn);
        }

        private static void Strip(Pawn pawn)
        {
            for (int guard = 0; guard < 30; guard++)
            {
                Hediff target = null;

                foreach (Hediff h in pawn.health.hediffSet.hediffs)
                {
                    if (!(h is Hediff_AddedPart) && !(h is Hediff_Implant)) continue;

                    // Their own parts - beak, claws, anything Avir_* - stay, always.
                    if (IsAvirOwn(h.def)) continue;

                    // PSYLINK IS NOT MACHINERY AND MUST SURVIVE.
                    //
                    // Hediff_Psylink derives from Hediff_Implant, so a blanket implant strip
                    // eats it - and Patch_AvirPsycasts deliberately grants Matriarchs and
                    // Priestesses seven to eighteen levels of it. Stripping that would silently
                    // undo the single most important thing about the senior castes, and it would
                    // do it in a file that has nothing to do with psycasts.
                    if (IsExempt(h)) continue;

                    // WIDENED BACK TO EVERYTHING.
                    //
                    // This used to check Patch_AvirLimbLock.IsRestrictedCategory and only strip
                    // limbs, eyes, jaws and hearts. That narrowing is what let a bionic kidney
                    // and a reprocessor stomach ride along on an A'Schialis huntress: neither
                    // part is in those four categories, so neither was ever looked at.
                    //
                    // The rule the Avir actually have is simpler than four categories. They do
                    // not wear other people's machinery. Anywhere on the body, any tech level,
                    // archotech included. If it is not theirs, it comes off.
                    //
                    // THIS WILL FIGHT OTHER IMPLANT MODS, and that is the intended outcome
                    // rather than a side effect - it is the whole point of the lock. It still
                    // fires only at generation and at spawn, never on surgery, so anything the
                    // player installs in their own colonist afterwards is untouched.
                    target = h;
                    break;
                }

                if (target == null) return;

                if (target.Part != null)
                {
                    // Regrow the limb. Do NOT simply delete the bionic.
                    pawn.health.RestorePart(target.Part, null, false);
                }
                else
                {
                    // Whole-body implant with no part (rare). Nothing to regrow.
                    pawn.health.RemoveHediff(target);
                }
            }
        }


        // ============================================================
        //  MAGICAL TATTOOS - the one implant an Avir may be born wearing
        // ============================================================
        public const string TattooTag = "Maal_Tattoo";

        /// <summary>Chance an Avir arrives already inked. Deliberately low - it should read as
        /// a personal choice some of them made, not as faction uniform.</summary>
        private const float TattooChance = 0.06f;

        /// <summary>
        /// Used by the GRANT path only. The exemption side is handled by
        /// AvirImplantExemption.ExemptFromLock via Avir_Exempt_Tattoos in the XML - this stays
        /// because MaybeGrantTattoo needs to build a pool and check for an existing tattoo,
        /// which is a different question from "may this survive the strip".
        /// </summary>
        public static bool IsTattoo(HediffDef def)
        {
            if (def == null) return false;
            if (def.tags != null && def.tags.Contains(TattooTag)) return true;
            return def.defName != null && def.defName.StartsWith("Maal_Tattoo");
        }

        private static List<HediffDef> tattooPool;
        private static bool tattooPoolBuilt;

        /// <summary>
        /// Every tattoo def in the game, resolved once. Empty when the tattoo mod is absent,
        /// which makes the whole feature self-disabling with no def references anywhere in XML.
        /// </summary>
        private static List<HediffDef> TattooPool
        {
            get
            {
                if (tattooPoolBuilt) return tattooPool;
                tattooPoolBuilt = true;
                tattooPool = new List<HediffDef>();

                foreach (HediffDef d in DefDatabase<HediffDef>.AllDefsListForReading)
                {
                    if (IsTattoo(d)) tattooPool.Add(d);
                }

                if (tattooPool.Count > 0)
                    Log.Message("[Avir] Magical Tattoos detected - " + tattooPool.Count + " designs available to Avir pawns.");

                return tattooPool;
            }
        }

        /// <summary>
        /// Rolls a single tattoo onto a freshly generated Avir.
        ///
        /// ONE ONLY. The mod's own install recipes carry
        /// incompatibleWithHediffTags: Maal_Tattoo, so a pawn is never meant to wear two; this
        /// checks for an existing one and bails rather than stacking.
        ///
        /// Hatchlings are skipped. Whatever the tattoos mean to the Avir, they are not
        /// something you are given before you can decide.
        ///
        /// A BODY PART IS MANDATORY, AND I GOT THIS WRONG THE FIRST TIME.
        ///
        /// The original version added the hediff with no BodyPartRecord, on the reading that
        /// tattoos are whole-body implants because the individual install recipes declare no
        /// appliedOnFixedBodyParts. They do not - their ABSTRACT PARENT does:
        ///
        ///     &lt;RecipeDef Abstract="True" Name="Maal_Surgery"&gt;
        ///       &lt;appliedOnFixedBodyParts&gt;Torso, Head, Leg, Arm&lt;/appliedOnFixedBodyParts&gt;
        ///
        /// Every Install_Maal_* recipe inherits that; a few narrow it to Torso alone. So the
        /// hediff class is Hediff_Implant, whose PostAdd asserts on a null Part, and generation
        /// logged one error per tattooed pawn:
        ///
        ///     Maal_Tattoo_Defence has null Part. It should be set before PostAdd.
        ///
        /// The part is now READ OFF THE INSTALL RECIPE rather than assumed - find the RecipeDef
        /// whose addsHediff is this tattoo, take its appliedOnFixedBodyParts, and pick one the
        /// pawn actually has. Per-tattoo, so the ones restricted to Torso stay on the torso.
        /// </summary>
        private static void MaybeGrantTattoo(Pawn pawn)
        {
            if (pawn.kindDef != null && pawn.kindDef.defName == "Avir_Hatchling") return;
            if (!Rand.Chance(TattooChance)) return;

            List<HediffDef> pool = TattooPool;
            if (pool.Count == 0) return;

            foreach (Hediff existing in pawn.health.hediffSet.hediffs)
            {
                if (IsTattoo(existing.def)) return;
            }

            HediffDef pick = pool[Rand.Range(0, pool.Count)];

            BodyPartRecord part = PickTattooPart(pawn, pick);
            if (part == null) return;   // no legal site on this body - skip rather than log an error

            pawn.health.AddHediff(HediffMaker.MakeHediff(pick, pawn, part), part);
        }

        // defName of the tattoo hediff -> the body part defs its install recipe allows.
        private static Dictionary<HediffDef, List<BodyPartDef>> tattooSites;

        /// <summary>
        /// Where this specific tattoo is allowed to go, straight out of its own install recipe.
        /// Built once by walking every RecipeDef with an addsHediff that is a tattoo.
        /// </summary>
        private static List<BodyPartDef> SitesFor(HediffDef tattoo)
        {
            if (tattooSites == null)
            {
                tattooSites = new Dictionary<HediffDef, List<BodyPartDef>>();
                foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
                {
                    if (r.addsHediff == null || !IsTattoo(r.addsHediff)) continue;
                    if (r.appliedOnFixedBodyParts == null || r.appliedOnFixedBodyParts.Count == 0) continue;
                    tattooSites[r.addsHediff] = r.appliedOnFixedBodyParts;
                }
            }

            List<BodyPartDef> sites;
            return tattooSites.TryGetValue(tattoo, out sites) ? sites : null;
        }

        private static BodyPartRecord PickTattooPart(Pawn pawn, HediffDef tattoo)
        {
            List<BodyPartDef> sites = SitesFor(tattoo);
            List<BodyPartRecord> usable = new List<BodyPartRecord>();

            if (sites != null)
            {
                foreach (BodyPartRecord bp in pawn.health.hediffSet.GetNotMissingParts())
                {
                    if (sites.Contains(bp.def)) usable.Add(bp);
                }
            }

            if (usable.Count > 0) return usable[Rand.Range(0, usable.Count)];

            // Fallback for a tattoo whose recipe could not be found at all - the torso is the
            // one site every variant of this mod's recipes allows.
            foreach (BodyPartRecord bp in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (bp.def == BodyPartDefOf.Torso) return bp;
            }
            return null;
        }

        private static void MaybeGrantArchotech(Pawn pawn)
        {
            string kind = pawn.kindDef != null ? pawn.kindDef.defName : "";

            float chance = ArchotechChanceFor(kind);
            if (chance <= 0f || !Rand.Chance(chance)) return;

            // ArchotechArm and ArchotechLeg USED TO BE HERE. They are gone, and must stay gone:
            // granting one would install the very thing Patch_AvirLimbLock exists to forbid, and
            // the strip above would only tear it straight back off. An eye is not a claw, so the
            // eye stays. Looked up silently - no Royalty/Ideology, no problem, and no error if a
            // mod has renamed it.
            List<string> options = new List<string> { "ArchotechEye" };

            for (int attempts = 0; attempts < 4; attempts++)
            {
                string pick = options[Rand.Range(0, options.Count)];

                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(pick);
                if (def == null) continue;

                BodyPartRecord part = FindPartFor(pawn, def);
                if (part == null) continue;

                // Already has something there? Leave it.
                if (pawn.health.hediffSet.PartIsMissing(part)) continue;
                if (pawn.health.hediffSet.HasDirectlyAddedPartFor(part)) continue;

                pawn.health.AddHediff(HediffMaker.MakeHediff(def, pawn, part), part);
                return;
            }
        }

        /// <summary>Finds a body part this implant is actually allowed to go on.</summary>
        private static BodyPartRecord FindPartFor(Pawn pawn, HediffDef def)
        {
            if (def.spawnThingOnRemoved == null) return null;

            RecipeDef install = null;

            foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (r.addsHediff != def) continue;
                if (r.appliedOnFixedBodyParts == null || r.appliedOnFixedBodyParts.Count == 0) continue;

                install = r;
                break;
            }

            if (install == null) return null;

            List<BodyPartRecord> candidates = new List<BodyPartRecord>();

            foreach (BodyPartDef bp in install.appliedOnFixedBodyParts)
            {
                foreach (BodyPartRecord rec in pawn.RaceProps.body.AllParts)
                {
                    if (rec.def == bp) candidates.Add(rec);
                }
            }

            if (candidates.Count == 0) return null;

            return candidates[Rand.Range(0, candidates.Count)];
        }
    }

    /// <summary>
    /// Patch_AvirBionics above only fires on PawnGenerator.GeneratePawn(PawnGenerationRequest) -
    /// fine for the player's own colonists and most raiders, but trader/visitor caravan pawns
    /// evidently generate through a different path this doesn't reliably catch (confirmed:
    /// freshly-arrived A'Schialis traders showing up with an Archotech eye, a Mono arm-blade, an
    /// Archotech heart - all still-restricted categories that should never have survived
    /// generation). GameComponent_AvirBionicsSweep is a once-a-day safety net for this same gap,
    /// but "once a day" means a trader can visit, do their business, and leave again before it
    /// ever runs.
    ///
    /// This closes the gap at the actual moment it matters: the instant ANY pawn spawns onto ANY
    /// map, for ANY reason (caravan arrival, raid, wanderer join, respawn after load), check if
    /// they're Avir-kind and strip on the spot. Cheap for the overwhelming majority of spawns
    /// (anything non-Avir exits after one cheap check), and catches visitors/traders the moment
    /// they're actually visible rather than up to a day late.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_AvirBionicsOnSpawn
    {
        public static void Postfix(Pawn __instance, bool respawningAfterLoad)
        {
            try
            {
                if (__instance == null || __instance.health == null || __instance.Dead) return;

                // ---- GATE 1: NOT ON LOAD. ----
                //
                // THIS WAS THE BUG. SpawnSetup fires for every pawn every time a save is opened,
                // so this postfix was re-running the full strip on load - and taking off anything
                // the player had installed by surgery since the last session. A colonist given a
                // bionic eye on Monday lost it on Tuesday when the game was reopened, silently,
                // with no message. respawningAfterLoad is exactly the flag that distinguishes
                // "this pawn just arrived" from "this pawn is being rebuilt from the save", and
                // it was simply never read.
                if (respawningAfterLoad) return;

                // Genes or pawnkind, for the same reason as the generation postfix above: an Avir
                // is an Avir whatever its pawnkind happens to be called.
                if (!AvirFaction.ShouldUseAvirContent(__instance)) return;

                // ---- GATE 2: NEVER THE PLAYER'S OWN. ----
                //
                // The generation-time postfix on GeneratePawn still applies to everyone,
                // player included - an Avir arrives with what an Avir arrives with, whoever
                // owns them. But once a pawn is YOURS, what you put in them is yours to decide,
                // and this hook has no business firing again afterwards. That was the promise
                // the class docstring made and this is what actually keeps it.
                //
                // The trader/visitor gap this patch was written for is untouched: those pawns
                // are not player-faction, so they are still stripped the moment they walk onto
                // the map.
                if (AvirFaction.IsPlayerAvir(__instance)) return;

                Patch_AvirBionics.SweepStrip(__instance);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Bionic lock on-spawn check failed: " + e, 0x4A51C7);
            }
        }
    }
}
