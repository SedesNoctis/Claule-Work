using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Walk to the styling station, then open the feather colour picker. Copied from the Kharassian
    /// JobDriver_ChangeScaleColor. Mirrors Anthrosonae's
    /// JobDriver_ChangeFur; the station is an Ideology building, hence the ModLister check.
    /// </summary>
    public class JobDriver_ChangeFeatherColor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (!ModLister.CheckIdeology("Styling station"))
            {
                yield break;
            }

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell)
                .FailOnDespawnedOrNull(TargetIndex.A);

            yield return Toils_General.Do(delegate
            {
                Gene_AvirFeathers gene = Gene_AvirFeathers.For(pawn);
                if (gene != null)
                {
                    Find.WindowStack.Add(new Window_AvirFeatherColors(gene));
                }
            });
        }
    }

    /// <summary>
    /// Adds "Change feather colour" next to "Change style" on the styling station. Registered by
    /// AvirFeatherHarmony, NOT by attribute: AvirHarmony.PatchAll stops at the first patch that
    /// fails, so new patches stay out of its walk.
    /// </summary>
    public static class Patch_StylingStation_FeatherColor
    {
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> options,
            Pawn selPawn, Building_StylingStation __instance)
        {
            foreach (FloatMenuOption option in options)
            {
                yield return option;

                if (option.Label == "ChangeStyle".Translate().CapitalizeFirst()
                    && Gene_AvirFeathers.For(selPawn) != null)
                {
                    yield return FloatMenuUtility.DecoratePrioritizedTask(
                        new FloatMenuOption("Change feather colour", delegate
                        {
                            selPawn.jobs.TryTakeOrderedJob(
                                JobMaker.MakeJob(AvirFeatherDefOf.Avir_ChangeFeatherColor, __instance),
                                JobTag.Misc);
                        }), selPawn, __instance);
                }
            }
        }
    }
}

namespace Avir
{
    [DefOf]
    public static class AvirFeatherDefOf
    {
        // Styling-station job that opens the feather colour picker.
        public static JobDef Avir_ChangeFeatherColor;

        static AvirFeatherDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirFeatherDefOf));
        }
    }
}
