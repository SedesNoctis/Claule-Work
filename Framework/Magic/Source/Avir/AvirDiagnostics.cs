using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LudeonTK;   // DebugAction lives here on 1.5+. If this line fails to compile, delete the
                  // AvirDebugActions class at the bottom of this file and this using with it -
                  // the logging above does not depend on it.
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// DIAGNOSTICS.
    ///
    /// The reason "I checked the logs, they give nothing" was not evidence of anything: this mod
    /// never logged on success. A silent log was equally consistent with
    ///
    ///     (a) the patch running perfectly,
    ///     (b) the patch never running,
    ///     (c) the ASSEMBLY NOT BEING REBUILT AT ALL and the game still running last week's DLL.
    ///
    /// You cannot tell those apart from silence, and I have been guessing between them. So stop
    /// guessing. This file makes the mod say out loud, every startup and every Avir pawn:
    ///
    ///   - which build of the DLL is actually loaded (BuildStamp - if this line is missing from
    ///     your log, the new assembly is NOT loaded and nothing else here matters)
    ///   - whether the backstory patches actually attached to their target methods
    ///   - the exact size of every caste pool, per slot, per gender - the numbers I have been
    ///     asserting from a script instead of reading out of the running game
    ///   - for each Avir pawn generated: kindDef, faction, xenotype, WHICH gate clause let it in
    ///     (or that it was rejected), which pool it drew from, and what it ended up with
    ///
    /// Verbose per-pawn tracing is ON by default. Turn it off with the debug action once the
    /// problem is found - it is noisy by design.
    /// </summary>
    public static class AvirDiag
    {
        // Bump this every time you rebuild. If the string in your log does not match the string
        // in the source you just edited, you are running a stale DLL and that is the whole bug.
        public const string BuildStamp = "AVIR-2026-10-30-MAGIC38";

        // OFF by default. When this was on, every generated pawn wrote a line to the log AND
        // did a File.AppendAllText - a disk open, write and close PER PAWN. In a modpack that
        // generates pawns in batches, that alone is a visible stall. Turn it on from the debug
        // menu (Avir -> Toggle verbose logging) when you actually need a trace.
        public static bool Verbose = false;

        private const string Tag = "[Avir] ";

        // Called from AvirHarmony's static constructor - a class we KNOW runs, because its
        // Harmony patches are visibly working in your game. Nothing here relies on this file
        // having its own startup hook.
        //
        // Log.Error, not Log.Message: red, and it survives a log drowning in RJW chatter.
        public static void Boot()
        {
            try
            {
                Line("=====================================================");
                Line("AVIR BUILD: " + BuildStamp);
                Line("Assembly:   " + typeof(AvirDiag).Assembly.GetName().Name + " @ " + SafeLocation());
                Line("If you cannot see this line in the log, the DLL is STALE and nothing else matters.");
                Line("=====================================================");
                Log.Error(Tag + "AVIR BUILD " + BuildStamp + " loaded. See " + LogPath() + " for the full report.");
            }
            catch (Exception e) { Log.Error(Tag + "Boot failed: " + e); }
        }

        public static void BootFinished()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                ReportPatchStatus();
                ReportPools();
            });
        }

        // ------------------------------------------------------------------
        // FILE SINK. The in-game log is unusable when another mod is printing
        // hundreds of lines a second. Everything also goes to disk.
        // ------------------------------------------------------------------
        private static string logPath;

        public static string LogPath()
        {
            if (logPath == null)
            {
                try
                {
                    logPath = System.IO.Path.Combine(GenFilePaths.ConfigFolderPath, "Avir_diagnostics.log");
                    System.IO.File.WriteAllText(logPath,
                        "Avir diagnostics - " + BuildStamp + " - " + DateTime.Now + Environment.NewLine);
                }
                catch { logPath = "(file sink unavailable)"; }
            }
            return logPath;
        }

        private static void Line(string s)
        {
            Log.Message(Tag + s);
            try
            {
                string p = LogPath();
                if (p != null && p.StartsWith("(") == false)
                    System.IO.File.AppendAllText(p, s + Environment.NewLine);
            }
            catch { }
        }

        private static string SafeLocation()
        {
            try { return typeof(AvirDiag).Assembly.Location; }
            catch { return "(location unavailable)"; }
        }

        // ------------------------------------------------------------------
        // 1. DID THE PATCHES ATTACH?
        // ------------------------------------------------------------------
        private static void ReportPatchStatus()
        {
            try
            {
                CheckPatched(typeof(PawnGenerator), "GeneratePawn",
                             "Patch_AvirPawnFinalize (backstories, traits, names)");
                CheckPatched(typeof(PawnBioAndNameGenerator), "GiveAppropriateBioAndNameTo",
                             "Patch_AvirBackstories (early backstory pass)");
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "patch status check failed: " + e.Message);
            }
        }

        private static void CheckPatched(Type type, string methodName, string label)
        {
            bool found = false;

            foreach (MethodBase m in Harmony.GetAllPatchedMethods())
            {
                if (m.DeclaringType == type && m.Name == methodName) { found = true; break; }
            }

            if (found) Line("PATCH OK   " + type.Name + "." + methodName + "  -> " + label);
            else
            {
                Line("PATCH MISSING on " + type.Name + "." + methodName + " - " + label + " IS NOT RUNNING.");
                Log.Error(Tag + "PATCH MISSING on " + type.Name + "." + methodName + " - " + label);
            }
        }

        // ------------------------------------------------------------------
        // 2. WHAT IS ACTUALLY IN EACH POOL, IN THE RUNNING GAME?
        // ------------------------------------------------------------------
        private static void ReportPools()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(Tag + "CASTE POOL AUDIT (counted from the live DefDatabase):");

                foreach (string kind in Patch_AvirBackstories.KnownCasteKinds())
                {
                    foreach (Gender g in new[] { Gender.Male, Gender.Female })
                    {
                        int c = Patch_AvirBackstories.CastePoolSize(kind, BackstorySlot.Childhood, g);
                        int a = Patch_AvirBackstories.CastePoolSize(kind, BackstorySlot.Adulthood, g);

                        string flag = (c == 0 || a == 0) ? "   <<<< EMPTY POOL - THIS IS A DATA BUG" : "";
                        sb.AppendLine(string.Format("{0}  {1,-18} {2,-6} childhood {3,3}   adulthood {4,3}{5}",
                                                    Tag, kind, g, c, a, flag));
                    }
                }

                // Also confirm the pawnkinds we think exist actually exist. A caste key that
                // matches no PawnKindDef means the dictionary is keyed on a name the game never
                // uses - the pool would never be selected and the pawn falls to the general pool.
                foreach (string kind in Patch_AvirBackstories.KnownCasteKinds())
                {
                    if (DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) == null)
                    {
                        sb.AppendLine(Tag + "  !! caste key '" + kind + "' matches NO PawnKindDef. "
                                      + "Its pool can never be selected.");
                    }
                }

                Line(sb.ToString().TrimEnd());
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "pool audit failed: " + e);
            }
        }

        // ------------------------------------------------------------------
        // 3. PER-PAWN ROUTING TRACE
        // ------------------------------------------------------------------

        /// <summary>Called at the very top of the pawn finalizer, BEFORE the gate.</summary>
        public static void TracePawn(Pawn pawn, string stage)
        {
            if (!Verbose || pawn == null) return;

            try
            {
                string kind = pawn.kindDef != null ? pawn.kindDef.defName : "(null kindDef)";
                string fac = (pawn.Faction != null && pawn.Faction.def != null)
                             ? pawn.Faction.def.defName : "(no faction)";
                string xeno = (pawn.genes != null && pawn.genes.Xenotype != null)
                              ? pawn.genes.Xenotype.defName : "(no xenotype)";

                bool gene = AvirFaction.HasFearborn(pawn);
                bool fact = AvirFaction.IsAvirFaction(pawn);
                bool pkind = AvirFaction.IsAvirPawnKind(pawn);
                bool gate = gene || fact || pkind;

                string story = "(no story)";
                if (pawn.story != null)
                {
                    story = Name(pawn.story.Childhood) + " / " + Name(pawn.story.Adulthood);
                }

                string pool = pkind || fact || gene
                    ? Patch_AvirBackstories.DescribePoolFor(pawn)
                    : "(gate failed - no Avir pool consulted)";

                Line(stage + ": " + pawn.LabelShort
                            + "\n    kindDef  = " + kind
                            + "\n    faction  = " + fac
                            + "\n    xenotype = " + xeno
                            + "\n    gate     = " + (gate ? "PASS" : "*** FAIL - THIS PAWN GETS NO AVIR CONTENT ***")
                            + "  (gene=" + gene + " faction=" + fact + " kind=" + pkind + ")"
                            + "\n    pool     = " + pool
                            + "\n    story    = " + story);
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "trace failed: " + e.Message);
            }
        }

        /// <summary>Called whenever a backstory is actually swapped.</summary>
        public static void TraceSwap(Pawn pawn, string slot, BackstoryDef from, BackstoryDef to, string why)
        {
            if (!Verbose || pawn == null) return;

            Line("SWAP " + slot + " on " + pawn.LabelShort + " [" + why + "]"
                        + "\n    from " + Name(from) + "  cats=" + Cats(from)
                        + "\n    to   " + Name(to) + "  cats=" + Cats(to));
        }

        private static string Name(BackstoryDef b)
        {
            return b == null ? "(none)" : (b.defName + " \"" + b.title + "\"");
        }

        private static string Cats(BackstoryDef b)
        {
            if (b == null || b.spawnCategories == null) return "(none)";
            return "[" + string.Join(",", b.spawnCategories.ToArray()) + "]";
        }
    }

    /// <summary>
    /// Dev-mode actions. Select a pawn, open the debug menu, and ask the mod why that pawn is
    /// what it is. Beats reading a wall of generation spam.
    /// </summary>
    public static class AvirDebugActions
    {
        [DebugAction("Avir", "Diagnose selected pawn", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiagnoseSelected()
        {
            foreach (object o in Find.Selector.SelectedObjects)
            {
                Pawn p = o as Pawn;
                if (p != null) AvirDiag.TracePawn(p, "DIAGNOSE");
            }
        }

        [DebugAction("Avir", "Re-roll backstories on selected", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RerollSelected()
        {
            foreach (object o in Find.Selector.SelectedObjects)
            {
                Pawn p = o as Pawn;
                if (p == null || p.story == null) continue;

                string kind = p.kindDef != null ? p.kindDef.defName : null;

                BackstoryDef c = Patch_AvirBackstories.PickAvirBackstory(
                    BackstorySlot.Childhood, p.gender, null, kind);
                if (c != null) Patch_AvirBackstories.ReplaceChildhood(p, c);

                if (p.story.Adulthood != null)
                {
                    BackstoryDef a = Patch_AvirBackstories.PickAvirBackstory(
                        BackstorySlot.Adulthood, p.gender, p.story.Childhood, kind);
                    if (a != null) Patch_AvirBackstories.ReplaceAdulthood(p, a);
                }

                AvirDiag.TracePawn(p, "AFTER RE-ROLL");
            }
        }

        [DebugAction("Avir", "Toggle gender tracing", allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.PlayingOnMap)]
        private static void ToggleGenderTracing()
        {
            AvirGenderDiag.Tracing = !AvirGenderDiag.Tracing;
            Log.Message("[Avir] gender tracing = " + AvirGenderDiag.Tracing);
        }

        [DebugAction("Avir", "Toggle verbose logging", allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.PlayingOnMap)]
        private static void ToggleVerbose()
        {
            AvirDiag.Verbose = !AvirDiag.Verbose;
            Log.Message("[Avir] verbose logging = " + AvirDiag.Verbose);
        }
    }
}
