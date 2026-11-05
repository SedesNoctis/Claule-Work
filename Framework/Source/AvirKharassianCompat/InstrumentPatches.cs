using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirKharassianCompat
{
    /// <summary>The Instrument goes on a slave or on nobody.</summary>
    [HarmonyPatch(typeof(EquipmentUtility), nameof(EquipmentUtility.CanEquip),
        new[] { typeof(Thing), typeof(Pawn), typeof(string), typeof(bool) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    public static class Patch_Instrument_OnlySlaves
    {
        public static void Postfix(Thing thing, Pawn pawn, ref string cantReason, ref bool __result)
        {
            if (!__result || thing == null || pawn == null || thing.def != InstrumentDefOf.Avir_Instrument)
            {
                return;
            }
            if (!pawn.IsSlave)
            {
                __result = false;
                cantReason = "AvirInstrumentOnlySlaves".Translate();
            }
        }
    }

    [HarmonyPatch(typeof(EquipmentUtility), nameof(EquipmentUtility.CanEquip),
        new[] { typeof(Thing), typeof(Pawn) })]
    public static class Patch_Instrument_OnlySlaves_Short
    {
        public static void Postfix(Thing thing, Pawn pawn, ref bool __result)
        {
            if (__result && thing != null && pawn != null
                && thing.def == InstrumentDefOf.Avir_Instrument && !pawn.IsSlave)
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Nobody holds a conversation with an instrument. Insults alone still land - a colonist can
    /// shout at him all he likes - they just do nothing (see the two patches below).
    /// </summary>
    [HarmonyPatch(typeof(SocialInteractionUtility), nameof(SocialInteractionUtility.CanReceiveInteraction))]
    public static class Patch_Instrument_NoConversation
    {
        public static void Postfix(Pawn pawn, InteractionDef interactionDef, ref bool __result)
        {
            if (!__result || interactionDef == null || interactionDef == InteractionDefOf.Insult)
            {
                return;
            }
            if (InstrumentUtility.IsInstrument(pawn))
            {
                __result = false;
            }
        }
    }

    /// <summary>An insult leaves nothing on an instrument: no memory, no grudge.</summary>
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory),
        new[] { typeof(Thought_Memory), typeof(Pawn) })]
    public static class Patch_Instrument_InsultsDoNothing
    {
        private static HashSet<ThoughtDef> insultThoughts;

        private static HashSet<ThoughtDef> InsultThoughts
        {
            get
            {
                if (insultThoughts == null)
                {
                    insultThoughts = new HashSet<ThoughtDef>();
                    foreach (InteractionDef def in DefDatabase<InteractionDef>.AllDefsListForReading)
                    {
                        if (def == InteractionDefOf.Insult || def.defName == "Slight")
                        {
                            if (def.recipientThought != null)
                            {
                                insultThoughts.Add(def.recipientThought);
                            }
                        }
                    }
                    ThoughtDef mood = DefDatabase<ThoughtDef>.GetNamedSilentFail("InsultedMood");
                    if (mood != null)
                    {
                        insultThoughts.Add(mood);
                    }
                }
                return insultThoughts;
            }
        }

        public static bool Prefix(Thought_Memory newThought, Pawn ___pawn)
        {
            if (newThought?.def == null || !InstrumentUtility.IsInstrument(___pawn))
            {
                return true;
            }
            return !InsultThoughts.Contains(newThought.def);
        }
    }

    /// <summary>And no fight comes of it, from either side.</summary>
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.SocialFightChance))]
    public static class Patch_Instrument_NoSocialFight
    {
        public static void Postfix(Pawn initiator, Pawn ___pawn, ref float __result)
        {
            if (__result > 0f && (InstrumentUtility.IsInstrument(___pawn) || InstrumentUtility.IsInstrument(initiator)))
            {
                __result = 0f;
            }
        }
    }

    /// <summary>
    /// Consciousness never falls below 70% on an instrument - the collar keeps a weakened slave
    /// working. No ceiling. A true zero (a destroyed brain) is left alone: the floor must not
    /// make anyone unkillable.
    /// </summary>
    [HarmonyPatch(typeof(PawnCapacityUtility), nameof(PawnCapacityUtility.CalculateCapacityLevel))]
    public static class Patch_Instrument_ConsciousnessFloor
    {
        public const float Floor = 0.70f;

        public static void Postfix(HediffSet diffSet, PawnCapacityDef capacity, ref float __result)
        {
            if (__result <= 0f || __result >= Floor || capacity != PawnCapacityDefOf.Consciousness)
            {
                return;
            }
            HediffDef def = InstrumentDefOf.Avir_Instrumented;
            if (def != null && diffSet?.pawn != null && diffSet.HasHediff(def))
            {
                __result = Floor;
            }
        }
    }
}
