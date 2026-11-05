using System.Collections.Generic;
using System.Linq;
using rjw;
using Verse;

namespace AvirRJW
{
    /// <summary>
    /// Sex-dimorphic anatomy for the Avir (it lives in the Avian Frame gene):
    ///   MALE   -> canine penis + a bare chest + the ordinary human anus
    ///   FEMALE -> the ordinary human vagina, anus and breasts (small ones)
    /// Humanoid Alien Races makes RJW take the Avir for a non-human race, so RJW's own adders fall back on its
    /// "generic" parts; here every generic part is swapped for the human one. The calm harpy has no avian frame
    /// (no beak, no four legs) but the same problem, so she is recognised by her race. Idempotent.
    /// </summary>
    public static class RjwPartEnforcer
    {
        /// <summary>The size a female's breasts are brought down to (the same figure as Source/Avir/AvirNoBreasts.cs).</summary>
        public const float SmallBreasts = 0.15f;

        public static bool HasFrame(Pawn p) =>
            p?.genes != null && AvirRjwDefOf.Avir_AvianFrame != null && p.genes.HasActiveGene(AvirRjwDefOf.Avir_AvianFrame);

        /// <summary>The calm harpy: her own race (a human body with wings), no avian frame gene.</summary>
        public static bool IsCalmHarpy(Pawn p) => p?.def != null && p.def.defName == "Avir_HarpyRace";

        public static bool IsFramedMale(Pawn p) => p != null && p.gender == Gender.Male && HasFrame(p);

        /// <summary>An Avir female (avian frame) or a harpy: given the ordinary human set.</summary>
        public static bool IsHumanFemale(Pawn p) => p != null && p.gender == Gender.Female && (HasFrame(p) || IsCalmHarpy(p));

        private static string Parts(Pawn pawn, BodyPartRecord bpr)
        {
            List<Hediff> l = Genital_Helper.get_PartsHediffList(pawn, bpr);
            if (l == null) return "(null)";
            return string.Join(", ", l.Select(h => h.def.defName + (Genital_Helper.is_sex_part(h) ? "[sex]" : "")));
        }

        /// <summary>Safety net after sexualization / on gene add / on load: puts the human parts where RJW left generic ones.
        /// Never adds a part that is not there (a surgically removed one stays removed).</summary>
        public static void EnforceAvir(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return;
            if (IsHumanFemale(pawn)) { EnforceFemale(pawn, false); return; }
            if (!IsFramedMale(pawn))
            {
                Log.Message($"[AvirRJW] EnforceAvir {pawn.LabelShort}: not an Avir male/female -> leaving RJW default");
                return;
            }
            EnforceMaleGenitals(pawn);
            EnforceMaleChest(pawn);
            GiveHuman(pawn, Genital_Helper.get_anusBPR(pawn), Genital_Helper.generic_anus, Genital_Helper.average_anus, false);
        }

        /// <summary>Female: vagina, anus and breasts - the human ones, the breasts small.</summary>
        public static void EnforceFemale(Pawn pawn, bool addIfEmpty)
        {
            if (pawn == null || pawn.health == null) return;
            GiveHuman(pawn, Genital_Helper.get_genitalsBPR(pawn), Genital_Helper.generic_vagina, Genital_Helper.average_vagina, addIfEmpty);
            GiveHuman(pawn, Genital_Helper.get_anusBPR(pawn), Genital_Helper.generic_anus, Genital_Helper.average_anus, addIfEmpty);
            GiveHuman(pawn, Genital_Helper.get_breastsBPR(pawn), Genital_Helper.generic_breasts, Genital_Helper.average_breasts, addIfEmpty);
            ShrinkHumanBreasts(pawn);
        }

        /// <summary>The generic part on this body part (if any) is taken off and the human one put in its place. With
        /// addIfEmpty a bare part gets the human one as well; a part that already holds something else (a human one,
        /// an implant) is left alone.</summary>
        public static void GiveHuman(Pawn pawn, BodyPartRecord bpr, HediffDef generic, HediffDef human, bool addIfEmpty)
        {
            if (pawn?.health == null || bpr == null || human == null) return;
            if (pawn.health.hediffSet.PartIsMissing(bpr)) return;
            List<Hediff> onPart = Genital_Helper.get_PartsHediffList(pawn, bpr);
            List<Hediff> drop = onPart.Where(h => generic != null && h.def == generic).ToList();
            foreach (Hediff h in drop) pawn.health.RemoveHediff(h);
            bool occupied = onPart.Count > drop.Count;
            if (!occupied && (addIfEmpty || drop.Count > 0))
                pawn.health.AddHediff(SexPartAdder.MakePart(human, pawn, bpr), bpr);
        }

        /// <summary>Her human breasts are brought down to a small size (the part's own forced size).</summary>
        public static void ShrinkHumanBreasts(Pawn pawn)
        {
            BodyPartRecord bpr = Genital_Helper.get_breastsBPR(pawn);
            if (bpr == null) return;
            foreach (Hediff h in Genital_Helper.get_PartsHediffList(pawn, bpr))
            {
                if (h.def != Genital_Helper.average_breasts) continue;
                HediffComp_SexPart comp = h.TryGetComp<HediffComp_SexPart>();
                if (comp != null && comp.GetSize() > SmallBreasts) comp.ForceSize(SmallBreasts);
            }
        }

        public static void EnforceMaleGenitals(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.gender != Gender.Male) return;

            BodyPartRecord bpr = Genital_Helper.get_genitalsBPR(pawn);
            if (bpr == null || pawn.health.hediffSet.PartIsMissing(bpr)) return;

            HediffDef want = Genital_Helper.canine_penis;
            if (want == null) return;

            Log.Message($"[AvirRJW] MaleGenitals {pawn.LabelShort} BEFORE=[{Parts(pawn, bpr)}]");
            RemoveSexPartsExcept(pawn, bpr, want);
            if (!pawn.health.hediffSet.HasHediff(want))
                pawn.health.AddHediff(SexPartAdder.MakePart(want, pawn, bpr), bpr);
            Log.Message($"[AvirRJW] MaleGenitals {pawn.LabelShort} AFTER=[{Parts(pawn, bpr)}]");
        }

        public static void EnforceMaleChest(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.gender != Gender.Male) return;

            BodyPartRecord bpr = Genital_Helper.get_breastsBPR(pawn);
            if (bpr == null || pawn.health.hediffSet.PartIsMissing(bpr)) return;

            HediffDef flat = Genital_Helper.featureless_chest;
            Log.Message($"[AvirRJW] MaleChest {pawn.LabelShort} BEFORE=[{Parts(pawn, bpr)}]");
            RemoveSexPartsExcept(pawn, bpr, flat);
            if (flat != null && !pawn.health.hediffSet.HasHediff(flat))
                pawn.health.AddHediff(flat, bpr);
            Log.Message($"[AvirRJW] MaleChest {pawn.LabelShort} AFTER=[{Parts(pawn, bpr)}]");
        }

        private static void RemoveSexPartsExcept(Pawn pawn, BodyPartRecord bpr, HediffDef keep)
        {
            List<Hediff> onPart = Genital_Helper.get_PartsHediffList(pawn, bpr);
            if (onPart == null) return;
            // Removes every other sex part AND every extra copy of the kept one (one stays),
            // so a pawn that already got doubled is cleaned the next time this runs.
            bool kept = false;
            List<Hediff> remove = new List<Hediff>();
            for (int i = 0; i < onPart.Count; i++)
            {
                Hediff h = onPart[i];
                if (h == null) continue;
                if (h.def == keep)
                {
                    if (kept) remove.Add(h);
                    kept = true;
                }
                else if (Genital_Helper.is_sex_part(h))
                {
                    remove.Add(h);
                }
            }
            for (int i = 0; i < remove.Count; i++) pawn.health.RemoveHediff(remove[i]);
        }
    }
}
