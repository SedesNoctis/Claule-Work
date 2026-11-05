using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    // =====================================================================================
    // THE QUIET NEST DRAUGHT (defs keep the old name Heartwing). A thick white draught the Avir make of hushleaf. A female who drinks it is barely
    // fertile for ten days (a tenth); a male gets no such thing - only three times the desire. Drunk again before
    // three days have passed, it may leave her white-barren for good - which only the Phoenix's essence undoes.
    // It works on anyone, not only the Avir. Fertility decides conception (and fertilized eggs); with RJW the
    // male's fire raises its SexFrequency too (Patches/Avir_Heartwing_RJW.xml).
    // =====================================================================================

    [DefOf]
    public static class HeartwingDefOf
    {
        public static HediffDef Avir_HeartwingHush;
        public static HediffDef Avir_HeartwingFire;
        public static HediffDef Avir_WhiteBarrenness;
        static HeartwingDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(HeartwingDefOf)); }
    }

    public class IngestionOutcomeDoer_HeartwingDraught : IngestionOutcomeDoer
    {
        /// <summary>Severity left when a new dose comes within three days of the last (it falls 0.1 a day).</summary>
        public float overuseAbove = 0.7f;
        public float barrenChance = 0.35f;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (pawn?.health == null) return;
            if (pawn.gender == Gender.Female)
            {
                Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(HeartwingDefOf.Avir_HeartwingHush);
                if (h != null && h.Severity > overuseAbove && Rand.Chance(barrenChance)
                    && !pawn.health.hediffSet.HasHediff(HeartwingDefOf.Avir_WhiteBarrenness))
                {
                    pawn.health.AddHediff(HeartwingDefOf.Avir_WhiteBarrenness);
                    if (PawnUtility.ShouldSendNotificationAbout(pawn))
                        Find.LetterStack.ReceiveLetter("White barrenness",
                            pawn.LabelShort + " drank the quiet nest again too soon. It has left her white-barren: she can no longer conceive. Only the Phoenix's essence can undo it.",
                            LetterDefOf.NegativeEvent, pawn);
                }
                if (h == null) h = pawn.health.AddHediff(HeartwingDefOf.Avir_HeartwingHush);
                h.Severity = 1f;
            }
            else
            {
                HediffDef half = AvirBirdMilk.HalfFire;            // the raw leaf's half fire gives way to the full one
                Hediff weak = half == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(half);
                if (weak != null) pawn.health.RemoveHediff(weak);
                Hediff f = pawn.health.hediffSet.GetFirstHediffOfDef(HeartwingDefOf.Avir_HeartwingFire) ?? pawn.health.AddHediff(HeartwingDefOf.Avir_HeartwingFire);
                f.Severity = 1f;
            }
        }
    }

    /// <summary>The draught's fire in a male: three times the desire (lovin comes three times as often).</summary>
    [HarmonyPatch(typeof(LovePartnerRelationUtility), "LovinMtbSinglePawnFactor")]
    public static class Patch_HeartwingFire
    {
        public static void Postfix(Pawn pawn, ref float __result)
        {
            if (pawn?.health?.hediffSet != null && HeartwingDefOf.Avir_HeartwingFire != null
                && pawn.health.hediffSet.HasHediff(HeartwingDefOf.Avir_HeartwingFire))
                __result /= 3f;
        }
    }
}

namespace Avir
{
    /// <summary>A stack picture by bands of the stack: the first picture up to a fifth of the stack limit, the second
    /// up to three fifths, the last above (for 25: 1-5, 6-15, 16-25). Vanilla shows the middle one for
    /// everything between 2 and limit-1.</summary>
    public class Graphic_StackBands : Graphic_StackCount
    {
        /// <summary>Only the band textures (..._a, _b, _c) count: a stray file left in the folder from an older version
        /// sorted first and stood in for the small stack and the icon.</summary>
        public override void Init(GraphicRequest req)
        {
            base.Init(req);
            if (subGraphics == null) return;
            Graphic[] bands = subGraphics.Where(g => g?.path != null && System.Text.RegularExpressions.Regex.IsMatch(g.path, "_[a-z]$")).ToArray();
            if (bands.Length > 0) subGraphics = bands;
        }

        public override Graphic SubGraphicFor(Thing thing)
        {
            if (subGraphics == null || subGraphics.Length < 3 || thing == null) return base.SubGraphicFor(thing);
            int limit = UnityEngine.Mathf.Max(1, thing.def.stackLimit);
            float f = thing.stackCount / (float)limit;
            int i = f <= 0.2f + 1e-4f ? 0 : f <= 0.6f + 1e-4f ? 1 : 2;
            return subGraphics[UnityEngine.Mathf.Min(i, subGraphics.Length - 1)];
        }

        public override Graphic GetColoredVersion(UnityEngine.Shader newShader, UnityEngine.Color newColor, UnityEngine.Color newColorTwo) =>
            GraphicDatabase.Get<Graphic_StackBands>(path, newShader, drawSize, newColor, newColorTwo, data);
    }
}
