using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    // Applies Harmony patches at startup AND injects the Prideful StatPart into PsychicSensitivity.
    // The injection is idempotent: if Patches/Avir_StatPatches.xml is still present and already
    // added the part, this skips it, so the apparel effect is never counted twice.
    [StaticConstructorOnStartup]
    public static class AvirHarmony
    {
        static AvirHarmony()
        {
            // FIRST LINE OF THE MOD. AvirDiag used to announce itself from its own
            // [StaticConstructorOnStartup] and nothing came out - which told us the file was not
            // in the built assembly. This class demonstrably runs (its patches work), so the
            // banner goes here. If you do not see AVIR BUILD in the log, the DLL is stale.
            AvirDiag.Boot();

            Harmony harmony = new Harmony("yourname.avir");

            // Stat-part injection first, so it always runs even if a later patch has trouble.
            InjectPridefulStatPart();
            InjectPsychicNatureStatParts();
            InjectTelekineticFocusStatPart();
            InjectChoirStatParts();
            InjectShadowsongWinePriceStatPart();

            // One class at a time: PatchAll stops at the first patch that throws and silently skips
            // every class after it - which is how the wing colours and the chick commands went missing.
            PatchEachClass(harmony);

            // The two non-stat Psychic Nature effects (melee cooldown, raid cost). Done via
            // AccessTools string lookups so a missing method just warns instead of aborting.
            ApplyPsychicNaturePatches(harmony);

            // Puts airborne Avir on RimWorld 1.6's own flying path grid, plus the roof rule.
            // String-based lookups inside, so a future engine rename warns and degrades to
            // "flight does not affect routing" rather than aborting startup.

            // ONE LEVER FOR FLIGHT, REPLACING EVERYTHING THAT CAME BEFORE IT.
            //
            // Patch_AvirFlightPathing is no longer applied, and the roof-blocking half of what is
            // now Patch_AvirFlightSpeed is disabled. Both patched VEF's gates one at a time, which
            // left the router and the mover holding different opinions - deadlock in one direction,
            // Ancient Danger in the other. The gate below decides once per path and enforces it
            // through VEF's own PhasingPawns set, so all eight gates agree by construction.
            //
            // Patch_AvirFlightAuto stays for the take-off itself: it decides WHEN to fly, while the
            // gate decides WHERE flight is permitted to carry them.
            FlightGene.Patch_AvirPhasingGate.Apply(harmony);
            FlightGene.Patch_AvirFlightAuto.Apply(harmony);

            // THE SPEED CLAMP. Separate concern from where flight may go, and unregistering it
            // along with the old roof rule is what made crossings crawl again.
            //
            // Without it a phasing pawn pays the CELL'S OWN path cost to enter, and a wall's is
            // 10000 - ten thousand ticks for one step it is supposedly passing through freely.
            // VEF zeroes the wall costs the PATHFINDER uses, never this one, and this one is what
            // the pawn actually moves at.
            FlightGene.Patch_AvirFlightSpeed.Apply(harmony);

            // Closes the small number of places flight must not reach, on the ROUTE rather than on
            // a straight line the pawn was never going to walk. Must come after the gate, which
            // owns the rule this consults.
            FlightGene.Patch_AvirFlightRouteBlock.Apply(harmony);

            // DIRECT FLIGHT. Registered AFTER the route block deliberately: when a direct line is
            // taken, no path request is created at all, so the route block simply never runs for
            // that trip. It still guards every trip that falls back to the engine's pathfinder.
            FlightGene.Patch_AvirDirectFlight.Apply(harmony);

            // ARRIVAL. Direct flight ends its line on a legal cell; this stops the pawn being told
            // it has already arrived several cells short of it, inside the wall. Registered after
            // direct flight because it exists to protect the route direct flight draws.
            FlightGene.Patch_AvirFlightArrival.Apply(harmony);

            // MAP EDGE. The trapped-visitor trigger asks a different question than CanReach, on a
            // different method, and never saw the flight system at all. Without this, any Avir
            // caravan that flies in over a ridge is reported as walled in and starts mining out.
            FlightGene.Patch_AvirMapEdgeReach.Apply(harmony);
            // The same trigger, closed at the signal itself (Patch_AvirTrappedTrigger): a trader that came in on the wing
            // is not announced as trapped even when the reachability patch above misses its method.
            FlightGene.Patch_AvirTrappedTrigger.Apply(harmony);

            // Cross-kind courtship: kin-drawn refuse outsiders outright, ordinary Avir manage a
            // quarter of the usual odds, outkind Avir are unaffected.
            Avir.Patch_AvirRomanceKind.Apply(harmony);

            // The Avian culture is globally selectable, and Core's Ancients declare no
            // allowedCultures - so the ancient danger could roll it and hand Avir names to human
            // soldiers. This puts it back where it belongs.
            Avir.Patch_AvirCulture.Apply(harmony);

            // ABSOLUTE, at the naming itself. The culture patch above stops the problem being
            // created; this stops it appearing from ideos that already hold Avian - old saves,
            // factions generated seasons ago, anything carried over.
            Avir.Patch_AvirNameLock.Apply(harmony);

            // The same hole in a different wall: vanilla falls back to the WHOLE backstory
            // database when a pawnkind's filters come up empty, and every Avir backstory is in it.
            Avir.Patch_AvirBackstoryLock.Apply(harmony);

            // Everybody's Different decides Fat from weight and build, not from the frame gene -
            // so the frame lock could never have stopped it. Answered at that mod's own source.
            Avir.Patch_AvirNeverHeavyset.Apply(harmony);

            // Vanilla Gravship Expanded asks for the spire cradle's (null) minifiedDef - guarded at its source.
            Avir.Spire.Patch_AvirVGEGravEngineNull.Apply(harmony);

            // Essence extract can only be made inside a prison cell, and vanilla reserves anything
            // ingestible lying in one for the prisoners. One predicate blocks both drinking and
            // hauling; corrected there rather than in three places.
            Avir.Patch_AvirNotPrisonerFood.Apply(harmony);

            // Pair bonding: courtship gating, certainty past the threshold, and the answer given to
            // anyone who courts an Avir wrongly.
            Avir.Patch_AvirPairBondRomance.Apply(harmony);
            Avir.Patch_AvirBondGrief.Apply(harmony);
            Avir.Patch_AvirBondMarriage.Apply(harmony);
            Avir.Patch_AvirBondResurrection.Apply(harmony);
            Avir.Patch_AvirBondHarm.Apply(harmony);

            // Quarrels bruise instead of killing. Sits next to the bond patch because both hook
            // Pawn_HealthTracker.PreApplyDamage - if one of them ever stops binding, check the
            // other first.
            Avir.Patch_AvirSocialFightRestraint.Apply(harmony);
            Avir.Patch_AvirBondAbandoned.Apply(harmony);
            Avir.Patch_AvirBondBlindToScars.Apply(harmony);

            // No manual-preen patch here any more. The preen order lives in
            // FloatMenuOptionProvider_AvirPreen, which 1.6 discovers by itself - a right-click
            // entry where Maximus asked for it, instead of a gizmo with no texture eating a slot
            // in an already crowded command bar.

            // Pylon: extends roof support for pylons only (never removes it for anyone), and
            // halves the counted wealth of everything standing under one.
            Patch_AvirPylonRoof.Apply(harmony);
            Patch_AvirPylonWealth.Apply(harmony);

            // Soft integration with [XND] Nocturnal Animals: lets the "induce nocturnal
            // cycle" operation flip an individual animal's body clock. No-op if XND is absent.
            NocturnalSurgeryPatch.Apply(harmony);

            // Makes telekinesis-amulet wearers invisible to Hauler's Dream's own yield-routing,
            // so loose items land on the ground for the amulet instead of straight into the
            // pawn's pocket. No-op if Hauler's Dream isn't installed.
            Patch_AvirHaulersDreamCompat.Apply(harmony);

            AvirDiag.BootFinished();
        }

        // Only classes that actually carry [HarmonyPatch] go through Harmony here. Everything else
        // (job drivers, comps, and the patch classes registered by hand in their own guarded
        // static ctors) used to be fed to the class processor too, which "failed" on them with
        // "Undefined target method" / "Non-static method requires a target" - pure log noise.
        private static bool IsAttributePatch(Type t)
        {
            if (t.IsDefined(typeof(HarmonyPatch), true)) return true;
            foreach (System.Reflection.MethodInfo m in t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                if (m.IsDefined(typeof(HarmonyPatch), true)) return true;
            return false;
        }

        private static void PatchEachClass(Harmony harmony)
        {
            int failed = 0;
            foreach (Type t in AccessTools.GetTypesFromAssembly(typeof(AvirHarmony).Assembly))
            {
                if (!IsAttributePatch(t)) continue;
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                }
                catch (Exception e)
                {
                    failed++;
                    Log.Warning("[Avir] patch " + t.FullName + " skipped (the rest still apply): " + (e.InnerException ?? e).Message);
                }
            }
            if (failed > 0) Log.Message("[Avir] " + failed + " patch class(es) skipped - usually a mod they serve is not loaded.");
        }

        // Attaches StatPart_PsychicNature to each stat the gene scales. Any stat that isn't
        // present (e.g. VacuumResistance without Odyssey) is simply skipped.
        private static void InjectPsychicNatureStatParts()
        {
            // Move speed: +0.25% per step, MULTIPLICATIVE, so it stays sane at very high psy.
            InjectPsychicPart("MoveSpeed",
                new StatPart_PsychicNature { asFactor = true, perStep = 0.0025f, effectLabel = "Psychic nature (move)" });

            // Melee dodge: +1.5%/step in sunlight, +2.5%/step in darkness, CAPPED at +50% either way.
            InjectPsychicPart("MeleeDodgeChance",
                new StatPart_PsychicNature { perStep = 0.015f, perStepDark = 0.025f, lightConditional = true, maxAbsOffset = 0.5f, effectLabel = "Psychic nature (dodge)" });

            // Immunity gain: +1% per step, added on top of the 25% floor set in the GeneDef.
            InjectPsychicPart("ImmunityGainSpeed",
                new StatPart_PsychicNature { perStep = 0.01f, effectLabel = "Psychic nature (immunity)" });

            // Vacuum resistance (Odyssey-only stat; skipped silently if absent): +1.5% per step.
            InjectPsychicPart("VacuumResistance",
                new StatPart_PsychicNature { perStep = 0.015f, effectLabel = "Psychic nature (vacuum)" });

            // Carry capacity: +1% per step, MULTIPLICATIVE.
            InjectPsychicPart("CarryingCapacity",
                new StatPart_PsychicNature { asFactor = true, perStep = 0.015f, effectLabel = "Psychic nature (carry)" });

            // Vanilla Expanded's mass carry capacity (what a pawn carries in its inventory on the map): the same as above.
            InjectPsychicPart("VEF_MassCarryCapacity",
                new StatPart_PsychicNature { asFactor = true, perStep = 0.015f, effectLabel = "Psychic nature (carry)" });
            InjectPsychicPart("MassCarryCapacity",
                new StatPart_PsychicNature { asFactor = true, perStep = 0.015f, effectLabel = "Psychic nature (carry)" });

            // Global learning factor: +0.5% per step.
            InjectPsychicPart("GlobalLearningFactor",
                new StatPart_PsychicNature { perStep = 0.005f, effectLabel = "Psychic nature (learning)" });

            // Rest fall rate: -0.25% per step (tire/sleep-need builds slower).
            InjectPsychicPart("RestFallRateFactor",
                new StatPart_PsychicNature { perStep = -0.0025f, effectLabel = "Psychic nature (rest)" });

            // Social impact: +2% per step.
            InjectPsychicPart("SocialImpact",
                new StatPart_PsychicNature { perStep = 0.02f, effectLabel = "Psychic nature (social)" });

            // Injury healing factor: +0.25% per step.
            InjectPsychicPart("InjuryHealingFactor",
                new StatPart_PsychicNature { perStep = 0.0025f, effectLabel = "Psychic nature (healing)" });

            // Damage protection depends on the combat system in use:
            if (ModsConfig.IsActive("CETeam.CombatExtended"))
            {
                // CE: real Sharp/Blunt/Heat armour, each a flat base that itself grows +0.25%
                // per step. Base + scaling are computed together in the part so ONLY this natural
                // armour scales - never the pawn's worn apparel.
                //   Sharp = mm RHA (base 2.25, ~= Feathered Fur's 1.25 nudged up a little)
                //   Blunt = MPa      (base 1.8; not restated by design - tune freely)
                //   Heat  = fraction (base 0.02 = 2%, deliberately small so Avir aren't fire-proof)
                // perStep raised 0.25% -> 1.75% (confirmed via calculator pass: lands nude Avir
                // at 500% natural sensitivity, no Choir, right at ~4mm Sharp armor floor).
                InjectPsychicPart("ArmorRating_Sharp",
                    new StatPart_PsychicNature { baseValue = 2.25f, perStep = 0.0175f, effectLabel = "Psychic nature (sharp armour)" });
                InjectPsychicPart("ArmorRating_Blunt",
                    new StatPart_PsychicNature { baseValue = 1.8f, perStep = 0.0175f, effectLabel = "Psychic nature (blunt armour)" });
                InjectPsychicPart("ArmorRating_Heat",
                    new StatPart_PsychicNature { baseValue = 0.02f, perStep = 0.0175f, effectLabel = "Psychic nature (heat armour)" });
            }
            else
            {
                // Vanilla: universal damage reduction, -0.25% per step.
                InjectPsychicPart("IncomingDamageFactor",
                    new StatPart_PsychicNature { perStep = -0.0025f, effectLabel = "Psychic nature (protection)" });
            }
        }

        private static void InjectPsychicPart(string statDefName, StatPart_PsychicNature part)
        {
            StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            if (stat == null)
            {
                return; // stat not present in this install (e.g. no Odyssey) - nothing to do.
            }

            if (stat.parts == null)
            {
                stat.parts = new List<StatPart>();
            }

            // Idempotent: don't add a Psychic Nature part to the same stat twice.
            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is StatPart_PsychicNature)
                {
                    return;
                }
            }

            part.parentStat = stat;
            stat.parts.Add(part);
        }

        private static void InjectShadowsongWinePriceStatPart()
        {
            StatDef stat = StatDefOf.MarketValue;
            if (stat == null)
            {
                return;
            }

            if (stat.parts == null)
            {
                stat.parts = new List<StatPart>();
            }

            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is StatPart_ShadowsongWinePrice)
                {
                    return;
                }
            }

            stat.parts.Add(new StatPart_ShadowsongWinePrice { parentStat = stat });
        }

        private static void InjectTelekineticFocusStatPart()
        {            StatDef stat = StatDefOf.WorkSpeedGlobal;
            if (stat == null)
            {
                return;
            }

            if (stat.parts == null)
            {
                stat.parts = new List<StatPart>();
            }

            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is StatPart_AvirTelekineticFocus)
                {
                    return;
                }
            }

            stat.parts.Add(new StatPart_AvirTelekineticFocus { parentStat = stat });
        }

        private static void InjectChoirStatParts()
        {
            InjectChoirPart("VEF_RangedDodgeChance", new StatPart_AvirChoirDodge());
            InjectChoirPart(StatDefOf.PsychicSensitivity.defName, new StatPart_AvirChoirSensitivity());
        }

        private static void InjectChoirPart(string statDefName, StatPart part)
        {
            StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            if (stat == null)
            {
                return;
            }

            if (stat.parts == null)
            {
                stat.parts = new List<StatPart>();
            }

            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i].GetType() == part.GetType())
                {
                    return;
                }
            }

            part.parentStat = stat;
            stat.parts.Add(part);
        }

        private static void ApplyPsychicNaturePatches(Harmony harmony)
        {
            TryPatchPostfix(harmony, "Verse.VerbProperties:AdjustedCooldownTicks",
                nameof(PsychicNaturePatches.CooldownPostfix),
                "melee-cooldown");

            TryPatchPostfix(harmony, "RimWorld.StorytellerUtility:DefaultThreatPointsNow",
                nameof(PsychicNaturePatches.RaidPostfix),
                "raid-cost");

            TryPatchPostfix(harmony, "Verse.PawnCapacitiesHandler:GetLevel",
                nameof(PsychicNaturePatches.ConsciousnessPostfix),
                "consciousness");
        }

        private static void TryPatchPostfix(Harmony harmony, string targetMethod, string postfixName, string label)
        {
            try
            {
                var method = AccessTools.Method(targetMethod);
                if (method == null)
                {
                    Log.Warning("[Avir] Could not find " + targetMethod + "; Psychic Nature " + label + " effect is inactive.");
                    return;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(PsychicNaturePatches), postfixName));
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Failed to patch " + targetMethod + " for Psychic Nature " + label + ": " + e);
            }
        }

        private static void InjectPridefulStatPart()
        {
            StatDef stat = StatDefOf.PsychicSensitivity;
            if (stat == null)
            {
                Log.Warning("[Avir] PsychicSensitivity stat not found; Prideful apparel effect not attached.");
                return;
            }

            if (stat.parts == null)
            {
                stat.parts = new List<StatPart>();
            }

            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is StatPart_PridefulApparel)
                {
                    return;
                }
            }

            stat.parts.Add(new StatPart_PridefulApparel { parentStat = stat });
        }
    }

    /// <summary>
    /// Pawns with the Prideful gene (or trait) have their expectation band bumped up one level.
    /// The pawn argument is bound by position (HarmonyArgument(0)) so the patch applies no matter
    /// what the vanilla method names its parameter.
    /// </summary>
    [HarmonyPatch(typeof(ExpectationsUtility), nameof(ExpectationsUtility.CurrentExpectationFor), new Type[] { typeof(Pawn) })]
    public static class Patch_ExpectationsUtility_CurrentExpectationFor
    {
        public static void Postfix([HarmonyArgument(0)] Pawn p, ref ExpectationDef __result)
        {
            if (__result == null || p == null)
            {
                return;
            }

            bool prideful = (p.genes != null && p.genes.HasActiveGene(AvirDefOf.Avir_Prideful))
                            || (p.story != null && p.story.traits != null
                                && p.story.traits.HasTrait(AvirDefOf.Avir_PridefulTrait));
            if (!prideful)
            {
                return;
            }

            // Find the expectation with the smallest order strictly greater than the current one.
            ExpectationDef next = null;
            List<ExpectationDef> all = DefDatabase<ExpectationDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ExpectationDef d = all[i];
                if (d.order > __result.order && (next == null || d.order < next.order))
                {
                    next = d;
                }
            }

            if (next != null)
            {
                __result = next;
            }
        }
    }
}
