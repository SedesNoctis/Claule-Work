using System;
using System.Collections.Generic;
using HarmonyLib;
using Kharassian;
using RimWorld;
using Verse;

namespace AvirKharassianCompat
{
    [DefOf]
    public static class InstrumentDefOf
    {
        public static ThingDef Avir_Instrument;
        public static ThingDef Avir_ControlSphere;
        public static HediffDef Avir_Instrumented;

        static InstrumentDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(InstrumentDefOf));
        }
    }

    /// <summary>
    /// The A'Shaign way of keeping slaves, brought to a colony that keeps them the Kharassian way.
    ///
    /// THE INSTRUMENT is the collar. In the book it is "both the motivator and the prison":
    /// a shard of one composite mind, locked round a slave's neck, that leaves him a living but
    /// silent tool. A slave wearing one is an instrument as far as the Kharassian slavery system
    /// is concerned (KharassianSlaveHooks): he never rises or conspires, the ordinary overlord
    /// has no business with him, and he cannot plead, talk or inform.
    ///
    /// THE SPHERE OF CONTROL is what the collars answer to. Its holder - who needs an Avir
    /// amulet or a psylink to grip it at all - drives up to five bound instruments anywhere on
    /// the same map, passively, as if standing over them with the lash. And an Avir holder who
    /// is hungry may feed on them through it.
    /// </summary>
    public static class InstrumentUtility
    {
        public const int MaxBoundPerSphere = 5;

        /// <summary>The tag every Avir amulet carries.</summary>
        public const string AvirAmuletTag = "Avir_Telekinesis";

        public static bool IsInstrument(Pawn pawn)
        {
            return pawn != null
                   && pawn.IsSlave
                   && InstrumentDefOf.Avir_Instrumented != null
                   && pawn.health?.hediffSet != null
                   && pawn.health.hediffSet.HasHediff(InstrumentDefOf.Avir_Instrumented);
        }

        public static Apparel WornCollar(Pawn pawn)
        {
            return WornOfDef(pawn, InstrumentDefOf.Avir_Instrument);
        }

        public static Apparel WornSphere(Pawn pawn)
        {
            return WornOfDef(pawn, InstrumentDefOf.Avir_ControlSphere);
        }

        private static Apparel WornOfDef(Pawn pawn, ThingDef def)
        {
            List<Apparel> worn = pawn?.apparel?.WornApparel;
            if (worn == null || def == null)
            {
                return null;
            }
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def == def)
                {
                    return worn[i];
                }
            }
            return null;
        }

        /// <summary>An Avir amulet round the neck, or a psylink: either lets a pawn grip a sphere.</summary>
        public static bool CanGripSphere(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            if (pawn.HasPsylink)
            {
                return true;
            }
            List<Apparel> worn = pawn.apparel?.WornApparel;
            if (worn == null)
            {
                return false;
            }
            for (int i = 0; i < worn.Count; i++)
            {
                List<string> tags = worn[i].def.apparel?.tags;
                if (tags != null && tags.Contains(AvirAmuletTag))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Holding a sphere it can actually grip, alive and on a map.</summary>
        public static bool SphereActive(Pawn holder)
        {
            return holder != null && !holder.Dead && holder.Spawned
                   && WornSphere(holder) != null && CanGripSphere(holder);
        }
    }

    [StaticConstructorOnStartup]
    public static class InstrumentStartup
    {
        static InstrumentStartup()
        {
            // The door Kharassian leaves open for exactly this.
            KharassianSlaveHooks.InstrumentChecks.Add(InstrumentUtility.IsInstrument);

            // One class at a time, each in its own try/catch - a failed patch costs one patch.
            Harmony harmony = new Harmony("Tenebris.Avir.KharassianCompat");
            foreach (Type type in typeof(InstrumentStartup).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Log.Error("[Avir/Kharassian] Patch class " + type.FullName + " failed and was skipped: " + e);
                }
            }
        }
    }
}
