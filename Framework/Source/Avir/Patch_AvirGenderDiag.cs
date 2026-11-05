using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// GENDER PIPELINE TRACER.
    ///
    /// We know the caste gender rule is being lost somewhere between "we ask for a female" and "a
    /// male huntress walks out". We do not know WHERE, and I have guessed wrong about it more than
    /// once. So measure it instead: take the gender at FOUR checkpoints and print all four.
    ///
    ///   [1] PREFIX, Priority.First   - the request as it ARRIVES, before anyone has touched it.
    ///                                  Tells us whether the caller already fixed a gender (the
    ///                                  relation worker does exactly that for parents).
    ///
    ///   [2] PREFIX, Priority.Last    - the request as it ENTERS generation, after every other
    ///                                  mod's prefix has had a go at it. If our Female has vanished
    ///                                  by here, ANOTHER MOD'S PREFIX cleared it.
    ///
    ///   [3] POSTFIX, Priority.First  - the pawn the moment GeneratePawn returns, before any other
    ///                                  mod's postfix runs. If the request said Female at [2] but
    ///                                  the pawn is Male at [3], VANILLA IGNORED THE REQUEST - which
    ///                                  would mean something is overriding it inside generation
    ///                                  (a kindDef fixedGender, a xenotype, a gene).
    ///
    ///   [4] POSTFIX, Priority.Last   - the pawn after every other mod's postfix. If it was Female
    ///                                  at [3] and is Male at [4], SOME OTHER MOD'S POSTFIX flipped
    ///                                  it - and Anthrosonae's takes Pawn& (a ref), so it can even
    ///                                  swap the pawn out entirely.
    ///
    /// Read the line and the culprit names itself:
    ///
    ///   req-in=None  req-out=Female  gen=Female  final=Female   -> working
    ///   req-in=None  req-out=None    gen=Male    final=Male     -> our prefix was stripped [2]
    ///   req-in=None  req-out=Female  gen=Male    final=Male     -> generation ignored it [3]
    ///   req-in=None  req-out=Female  gen=Female  final=Male     -> another mod's postfix [4]
    ///   req-in=Male  req-out=Male    gen=Male    final=Male     -> CORRECT. This is a FATHER.
    ///                                                              The relation worker asked for
    ///                                                              him. Not a bug. Do not "fix".
    ///
    /// Also prints whether the pawn was newly generated or REDRESSED out of the world pawn pool,
    /// because a redressed pawn keeps the gender it was saved with and never passes through the
    /// request at all.
    ///
    /// Only logs Avir castes that HAVE a gender rule, so it is quiet. Toggle with the debug action
    /// "Avir -> Toggle gender tracing".
    /// </summary>
    public static class AvirGenderDiag
    {
        /// <summary>
        /// OFF BY DEFAULT. This was shipped switched ON, which meant it logged and recorded for
        /// every generated pawn in every raid, caravan and trade group for as long as a save ran.
        /// A diagnostic left running is just a cost with no reader.
        ///
        /// Still toggleable from the dev menu: "Avir -> Toggle gender tracing".
        /// </summary>
        public static bool Tracing = false;

        public struct Ctx
        {
            public string kind;
            public Gender wanted;
            public Gender? reqIn;
            public Gender? reqOut;
            public bool forceNew;
            public bool parentScope;   // was GenerateParent on the stack? (the ONLY legitimate override)
        }

        // A stack, not a single field: pawn generation NESTS. Generating a pawn can generate its
        // parents, which generates more pawns. A single field would be clobbered by the inner call.
        public static readonly Stack<Ctx> stack = new Stack<Ctx>();

        public static string G(Gender? g)
        {
            return g.HasValue ? g.Value.ToString() : "None";
        }

        public static void Report(Ctx c, Pawn pawn, Gender atGen, Gender atFinal)
        {
            if (!Tracing || pawn == null) return;

            bool ok = atFinal == c.wanted;

            // THE TRACE USED TO LIE HERE. It called any request that arrived with a gender a
            // "parent request" and printed OK - which is exactly how Sex Matters Redux slipped past
            // it. SMR hands EVERY raider a fixed gender before PawnGenerator is reached, so a male
            // huntress looked like a legitimate father and the log came back clean.
            //
            // A parent request is defined by WHERE we are, not by what we were handed.
            bool isParentRequest = c.parentScope;

            string verdict;
            if (ok) verdict = "OK";
            else if (isParentRequest) verdict = "OK (parent - caller demanded " + G(c.reqIn) + ")";
            else if (!c.reqOut.HasValue) verdict = "*** BROKEN: another mod's PREFIX stripped our FixedGender ***";
            else if (atGen != c.wanted) verdict = "*** BROKEN: generation IGNORED FixedGender (kindDef/xenotype/gene override?) ***";
            else verdict = "*** BROKEN: another mod's POSTFIX flipped the gender after generation ***";

            string line = "[Avir gender] " + pawn.LabelShort + "  " + c.kind
                        + "\n    wants   = " + c.wanted
                        + "\n    req-in  = " + G(c.reqIn) + "   (what the CALLER asked for -"
                        + " a gender here with parent=False means SOMETHING ELSE forced it, e.g. Sex Matters Redux)"
                        + "\n    parent  = " + c.parentScope + "   (is this pawn being built as somebody's FATHER/MOTHER?)"
                        + "\n    req-out = " + G(c.reqOut) + "   (what generation actually received)"
                        + "\n    at-gen  = " + atGen + "   (pawn when GeneratePawn returned)"
                        + "\n    final   = " + atFinal + "   (pawn after every other mod's postfix)"
                        + "\n    newPawn = " + c.forceNew + "   (false = may have been REDRESSED from WorldPawns,"
                        + " which keeps its saved gender and never sees the request)"
                        + "\n    -> " + verdict;

            if (ok || isParentRequest) Log.Message(line);
            else Log.Warning(line);
        }
    }

    // ------------------------------------------------------------------
    // [1] + [2]  the request, before and after every other mod's prefixes
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
                  new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirGenderTrace_In
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(ref PawnGenerationRequest request)
        {
            try
            {
                PawnKindDef kind = request.KindDef;
                if (kind == null || kind.defName == null) return;

                Gender want = Patch_AvirGenderRequest.WantedGender(kind.defName);
                if (want == Gender.None) return;

                AvirGenderDiag.stack.Push(new AvirGenderDiag.Ctx
                {
                    kind = kind.defName,
                    wanted = want,
                    reqIn = request.FixedGender,
                    forceNew = request.ForceGenerateNewPawn,
                    parentScope = Patch_AvirParentGenScope.GeneratingParent,
                });
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
                  new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirGenderTrace_Out
    {
        // Runs LAST among prefixes: the request as generation will actually see it.
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(ref PawnGenerationRequest request)
        {
            try
            {
                if (AvirGenderDiag.stack.Count == 0) return;

                AvirGenderDiag.Ctx c = AvirGenderDiag.stack.Pop();
                c.reqOut = request.FixedGender;
                AvirGenderDiag.stack.Push(c);
            }
            catch { }
        }

        // Runs LAST among postfixes: the pawn after everyone else has finished with it.
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn __result)
        {
            try
            {
                if (AvirGenderDiag.stack.Count == 0) return;

                AvirGenderDiag.Ctx c = AvirGenderDiag.stack.Pop();
                if (__result == null) return;

                Gender atGen = Patch_AvirGenderTrace_AtGen.LastSeen(__result);
                AvirGenderDiag.Report(c, __result, atGen, __result.gender);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // [3]  the pawn the instant GeneratePawn returns, before other postfixes
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
                  new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirGenderTrace_AtGen
    {
        private static readonly Dictionary<int, Gender> seen = new Dictionary<int, Gender>();

        [HarmonyPriority(Priority.First)]
        public static void Postfix(Pawn __result)
        {
            if (__result == null) return;
            seen[__result.thingIDNumber] = __result.gender;
        }

        public static Gender LastSeen(Pawn pawn)
        {
            Gender g;
            if (pawn != null && seen.TryGetValue(pawn.thingIDNumber, out g))
            {
                seen.Remove(pawn.thingIDNumber);
                return g;
            }
            return pawn != null ? pawn.gender : Gender.None;
        }
    }
}
