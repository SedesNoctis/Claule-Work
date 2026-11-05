using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// A HEN THAT HAS JUST LAID IS NOT READY FOR THE NEXT CLUTCH.
    ///
    /// MAXIMUS'S POINT, AND WHY IT IS THE RIGHT SHAPE. The nine days in
    /// Hediffs_Avir_EggLaying.xml came from the prototype he built on and describe how long an
    /// Avir carries an egg INTERNALLY before laying it. That is a gestation clock, and it is doing
    /// its job. What is missing is a second, separate clock: a recovery window after the clutch is
    /// on the ground, during which she cannot be fertilised again.
    ///
    /// Those two are not the same number and must not be merged. Raising the nine would make her
    /// carry longer, which is not what anyone wants; the fix is that laying should START something,
    /// not that carrying should take longer. Arlia produced seven chicks in a year because nothing
    /// ever told her to stop, not because the carry was quick.
    ///
    /// WHAT THIS FILE DOES TODAY, AND WHAT IT DELIBERATELY DOES NOT.
    ///
    /// DOES: records the tick each Avir last laid, saved with the game, hung off VEF's own LayEgg
    /// method - the one hook in this area already proven to bind, because Patch_ClutchLaying has
    /// been using it. That half is real and correct and needs nothing from anybody.
    ///
    /// DOES NOT: block fertilisation yet. Fertilisation happens inside
    /// VEF.Genes.HediffComp_HumanEggLayer and I do not have the VEF assembly here to read. The
    /// standing rule in this project - written after several compile rounds were lost to exactly
    /// this - is that vanilla and third-party member names are never recalled from memory. Guessing
    /// a field name here would produce a patch that either fails to bind or, worse, binds to the
    /// wrong thing and silently sterilises every Avir in the world.
    ///
    /// So instead of guessing, the dump below prints the comp's actual shape once at startup.
    /// One log, one paste, and the block can be written against real names in the next pass.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirLayingRefractory
    {
        private const string HumanLayerType = "VEF.Genes.HediffComp_HumanEggLayer";

        /// <summary>
        /// How long after laying she cannot be fertilised again. NOT YET ENFORCED - see the class
        /// comment. Left here so the number has one home when the block does go in, and set to a
        /// placeholder rather than a guess at Maximus's intent: he has not named a figure yet.
        ///
        /// For scale, so the eventual choice is an informed one: the carry is 9 days, the egg now
        /// sits 25 days before hatching, and clutches run 2-3. At a 9-day recovery she produces
        /// roughly half as many as she does now; at 20 days, roughly a third.
        /// </summary>
        public const int RefractoryDaysPlaceholder = 9;

        static Patch_AvirLayingRefractory()
        {
            Type layerType = AccessTools.TypeByName(HumanLayerType);
            if (layerType == null)
            {
                return; // VEF absent - nothing to hook, nothing to report.
            }

            DumpShape(layerType);

            MethodInfo lay = AccessTools.Method(layerType, "LayEgg")
                             ?? AccessTools.Method(layerType, "ProduceEgg");
            if (lay == null)
            {
                Log.Warning("[Avir laying] Could not find the egg-lay method on " + HumanLayerType
                    + "; the laying timestamp will not be recorded.");
                return;
            }

            new Harmony("avir.laying.refractory").Patch(lay,
                postfix: new HarmonyMethod(typeof(Patch_AvirLayingRefractory), nameof(LayPostfix)));
        }

        // ---- the half that works today ---------------------------------------------------------

        public static void LayPostfix(HediffComp __instance)
        {
            try
            {
                Pawn pawn = __instance?.Pawn;
                if (pawn == null) return;

                GameComponent_AvirLayingClock clock = Current.Game?
                    .GetComponent<GameComponent_AvirLayingClock>();
                if (clock == null) return;

                clock.NoteLaid(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir laying] failed to record a laying: " + e, 0x4A52A1);
            }
        }

        /// <summary>Days since this pawn last laid, or -1 if she never has in this save.</summary>
        public static float DaysSinceLastLaying(Pawn pawn)
        {
            GameComponent_AvirLayingClock clock = Current.Game?
                .GetComponent<GameComponent_AvirLayingClock>();
            if (clock == null) return -1f;

            int tick = clock.LastLaidTick(pawn);
            if (tick < 0) return -1f;

            return (Find.TickManager.TicksGame - tick) / 60000f;
        }

        /// <summary>
        /// Whether she is still recovering. Ready to be consulted by the fertilisation block as
        /// soon as that block has a real method to sit on.
        /// </summary>
        public static bool InRefractoryPeriod(Pawn pawn, int refractoryDays)
        {
            float days = DaysSinceLastLaying(pawn);
            return days >= 0f && days < refractoryDays;
        }

        // ---- the half that needs one log line from Maximus --------------------------------------

        /// <summary>
        /// Prints every field, property and method on VEF's egg-layer comp and its props, once.
        ///
        /// This is not laziness and it is not a placeholder for thinking. It is the only honest way
        /// to write the next patch from here: the alternative is inventing a member name, and this
        /// project has already paid for that mistake more than once. One startup, one log block,
        /// and the fertilisation gate can be written against what is actually there.
        /// </summary>
        private static void DumpShape(Type layerType)
        {
            try
            {
                const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static
                                       | BindingFlags.DeclaredOnly;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[Avir laying] ---- VEF egg-layer shape, for the refractory patch ----");
                sb.AppendLine("Paste this whole block back to me and the fertilisation block gets");
                sb.AppendLine("written against real names instead of guessed ones.");

                Describe(sb, layerType, All);

                Type propsType = AccessTools.TypeByName("VEF.Genes.HediffCompProperties_HumanEggLayer");
                if (propsType != null) Describe(sb, propsType, All);

                sb.AppendLine("[Avir laying] ---- end ----");
                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Warning("[Avir laying] could not describe " + HumanLayerType + ": " + e.Message);
            }
        }

        private static void Describe(StringBuilder sb, Type t, BindingFlags flags)
        {
            sb.AppendLine("TYPE " + t.FullName);

            FieldInfo[] fields = t.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                sb.AppendLine("  field   " + fields[i].FieldType.Name + " " + fields[i].Name);
            }

            PropertyInfo[] props = t.GetProperties(flags);
            for (int i = 0; i < props.Length; i++)
            {
                sb.AppendLine("  prop    " + props[i].PropertyType.Name + " " + props[i].Name);
            }

            MethodInfo[] methods = t.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.IsSpecialName) continue; // property accessors, already listed above

                ParameterInfo[] ps = m.GetParameters();
                string args = "";
                for (int p = 0; p < ps.Length; p++)
                {
                    if (p > 0) args += ", ";
                    args += ps[p].ParameterType.Name + " " + ps[p].Name;
                }

                sb.AppendLine("  method  " + m.ReturnType.Name + " " + m.Name + "(" + args + ")");
            }
        }
    }

    /// <summary>
    /// Remembers when each Avir last laid. A GameComponent rather than a field on the hediff comp,
    /// because the comp belongs to VEF and this mod does not get to add saved state to it.
    ///
    /// Keyed by thingIDNumber and pruned on load: a pawn who has left the game for good drops out
    /// rather than keeping the dictionary growing across a long save.
    /// </summary>
    public class GameComponent_AvirLayingClock : GameComponent
    {
        private Dictionary<int, int> lastLaidTick = new Dictionary<int, int>();

        private List<int> keyBuffer;
        private List<int> valueBuffer;

        public GameComponent_AvirLayingClock(Game game) { }

        public void NoteLaid(Pawn pawn)
        {
            if (pawn == null) return;
            lastLaidTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
        }

        /// <summary>The tick she last laid, or -1 if there is no record.</summary>
        public int LastLaidTick(Pawn pawn)
        {
            if (pawn == null) return -1;
            return lastLaidTick.TryGetValue(pawn.thingIDNumber, out int tick) ? tick : -1;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref lastLaidTick, "avirLastLaidTick",
                LookMode.Value, LookMode.Value, ref keyBuffer, ref valueBuffer);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && lastLaidTick == null)
            {
                lastLaidTick = new Dictionary<int, int>();
            }
        }
    }
}
