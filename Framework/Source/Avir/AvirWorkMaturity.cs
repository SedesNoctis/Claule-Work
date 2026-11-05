using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// The age at which a child may take each work type, per gene. Same XML as Vanilla Races
    /// Expanded - Archon's LifeStageWorkSettingsExtension (a list of LifeStageWorkSettings,
    /// written as &lt;WorkType&gt;age&lt;/WorkType&gt;), read by our own code so Archon is not required.
    /// On Avir_EggLaying.
    /// </summary>
    public class WorkMaturityExtension : DefModExtension
    {
        public List<LifeStageWorkSettings> lifeStageWorkSettings = new List<LifeStageWorkSettings>();
    }

    /// <summary>
    /// EARLY MATURITY. Vanilla reads LifeStageWorkSettings.minAge in four places: whether a work
    /// type is disabled for a child (IsDisabled), the age shown when it is
    /// (PawnUtility.IsWorkTypeDisabledByAge), and the two "you can now do X" checks on a birthday
    /// and on reaching the child life stage. Each read is swapped for AvirWorkMaturity.MinAge,
    /// which answers our gene's age if the pawn has it and vanilla's otherwise.
    ///
    /// Done the way Archon does it (a transpiler on the field load), because these are the exact
    /// spots the number is used - a postfix on IsDisabled alone would enable the work but leave the
    /// birthday letter and the tooltip on the vanilla ages.
    ///
    /// Archon's own gene, if Archon is loaded, works through its own extension and is untouched.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirWorkMaturity
    {
        private static readonly FieldInfo MinAgeField = AccessTools.Field(typeof(LifeStageWorkSettings), "minAge");
        private static readonly MethodInfo MinAgeMethod = AccessTools.Method(typeof(AvirWorkMaturity), nameof(MinAge));
        private static readonly FieldInfo TrackerPawnField = AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");

        static AvirWorkMaturity()
        {
            Harmony harmony = new Harmony("Tenebris.Avir.WorkMaturity");
            Patch(harmony, AccessTools.Method(typeof(LifeStageWorkSettings), nameof(LifeStageWorkSettings.IsDisabled)),
                nameof(Transpile_PawnArg1));
            Patch(harmony, AccessTools.Method(typeof(PawnUtility), nameof(PawnUtility.IsWorkTypeDisabledByAge)),
                nameof(Transpile_PawnArg0));
            Patch(harmony, AccessTools.Method(typeof(Pawn_AgeTracker), "BirthdayBiological"),
                nameof(Transpile_AgeTracker));
            Patch(harmony, AccessTools.Method(typeof(LifeStageWorker_HumanlikeChild), nameof(LifeStageWorker_HumanlikeChild.Notify_LifeStageStarted)),
                nameof(Transpile_PawnArg1));
        }

        private static void Patch(Harmony harmony, MethodInfo target, string transpiler)
        {
            if (target == null)
            {
                Log.Warning("[Avir] Early maturity: a target method was not found (" + transpiler + "); that check keeps vanilla ages.");
                return;
            }
            try
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(AvirWorkMaturity), transpiler));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Early maturity: patching " + target.DeclaringType?.Name + "." + target.Name + " failed: " + e);
            }
        }

        /// <summary>The age for this work type for this pawn: our gene's if it sets one, vanilla's otherwise.</summary>
        public static int MinAge(LifeStageWorkSettings settings, Pawn pawn)
        {
            if (settings == null)
            {
                return 0;
            }
            List<Gene> genes = pawn?.genes?.GenesListForReading;
            if (genes != null)
            {
                for (int i = 0; i < genes.Count; i++)
                {
                    Gene gene = genes[i];
                    if (gene == null || !gene.Active)
                    {
                        continue;
                    }
                    WorkMaturityExtension ext = gene.def.GetModExtension<WorkMaturityExtension>();
                    if (ext?.lifeStageWorkSettings == null)
                    {
                        continue;
                    }
                    for (int j = 0; j < ext.lifeStageWorkSettings.Count; j++)
                    {
                        LifeStageWorkSettings ours = ext.lifeStageWorkSettings[j];
                        if (ours != null && ours.workType == settings.workType)
                        {
                            return ours.minAge;
                        }
                    }
                }
            }
            return settings.minAge;
        }

        // Each transpiler replaces "ldfld minAge" (settings on the stack) with
        // "load the pawn; call MinAge(settings, pawn)". Only the way to the pawn differs.

        public static IEnumerable<CodeInstruction> Transpile_PawnArg0(IEnumerable<CodeInstruction> instructions)
        {
            return Replace(instructions, new[] { new CodeInstruction(OpCodes.Ldarg_0) });
        }

        public static IEnumerable<CodeInstruction> Transpile_PawnArg1(IEnumerable<CodeInstruction> instructions)
        {
            return Replace(instructions, new[] { new CodeInstruction(OpCodes.Ldarg_1) });
        }

        public static IEnumerable<CodeInstruction> Transpile_AgeTracker(IEnumerable<CodeInstruction> instructions)
        {
            return Replace(instructions, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, TrackerPawnField)
            });
        }

        private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, CodeInstruction[] loadPawn)
        {
            foreach (CodeInstruction ins in instructions)
            {
                if (ins.opcode == OpCodes.Ldfld && ins.operand is FieldInfo f && f == MinAgeField)
                {
                    CodeInstruction first = loadPawn[0].Clone();
                    first.labels.AddRange(ins.labels);
                    first.blocks.AddRange(ins.blocks);
                    yield return first;
                    for (int i = 1; i < loadPawn.Length; i++)
                    {
                        yield return loadPawn[i].Clone();
                    }
                    yield return new CodeInstruction(OpCodes.Call, MinAgeMethod);
                    continue;
                }
                yield return ins;
            }
        }
    }
}
