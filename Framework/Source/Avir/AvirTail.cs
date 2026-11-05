using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE TAIL. As mobile as the wings, and prehensile: an Avir holds on with it while its paws work -
    /// to a beam while building, to a branch while picking or felling, around a load while carrying. It
    /// accents their speech (the beak speaks), and lets them rest well on any floor, hanging or curled.
    /// Every bonus follows the tail's health; a lost tail gives none.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirTail
    {
        static AvirTail()
        {
            Add(StatDefOf.ConstructionSpeed, 1.10f, "Prehensile tail (holds on while the paws build)");
            Add(StatDefOf.PlantWorkSpeed, 1.10f, "Prehensile tail (holds the branch while the paws work)");
            Add(StatDefOf.CarryingCapacity, 1.15f, "Prehensile tail (carries part of the load)");
            Add(StatDefOf.SocialImpact, 1.15f, "Tail (accents the speech)");
        }

        private static void Add(StatDef stat, float factor, string label)
        {
            if (stat == null) return;
            if (stat.parts == null) stat.parts = new System.Collections.Generic.List<StatPart>();
            stat.parts.Add(new StatPart_AvirTail { parentStat = stat, factor = factor, label = label });
        }

        /// <summary>0 - no tail (or lost), 1 - whole.</summary>
        public static float Health(Pawn p)
        {
            if (AvirBody.Tail == null || p?.health?.hediffSet == null || p.RaceProps?.body == null) return 0f;
            BodyPartRecord tail = p.RaceProps.body.AllParts.FirstOrDefault(r => r.def == AvirBody.Tail);
            if (tail == null || p.health.hediffSet.PartIsMissing(tail)) return 0f;
            return Mathf.Clamp01(p.health.hediffSet.GetPartHealth(tail) / Mathf.Max(1f, tail.def.GetMaxHealth(p)));
        }
    }

    public class StatPart_AvirTail : StatPart
    {
        public float factor = 1.1f;
        public string label = "Tail";

        private float For(StatRequest req) => req.Thing is Pawn p ? 1f + (factor - 1f) * AvirTail.Health(p) : 1f;

        public override void TransformValue(StatRequest req, ref float val) => val *= For(req);

        public override string ExplanationPart(StatRequest req)
        {
            float f = For(req);
            return Mathf.Approximately(f, 1f) ? null : label + ": x" + f.ToString("0.00");
        }
    }

    /// <summary>On a floor or the bare ground an Avir rests as well as in a bed - hanging by the tail or
    /// curled around it. In a bed it changes nothing.</summary>
    [HarmonyPatch(typeof(Need_Rest), nameof(Need_Rest.TickResting))]
    public static class Patch_AvirTailRest
    {
        private static readonly AccessTools.FieldRef<Need, Pawn> PawnOf = AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        public static void Prefix(Need_Rest __instance, ref float restEffectiveness)
        {
            if (restEffectiveness >= 1f) return;
            Pawn p = PawnOf(__instance);
            if (p == null || p.CurrentBed() != null) return;
            float t = AvirTail.Health(p);
            if (t > 0f) restEffectiveness = Mathf.Max(restEffectiveness, Mathf.Lerp(restEffectiveness, 1f, t));
        }
    }
}
