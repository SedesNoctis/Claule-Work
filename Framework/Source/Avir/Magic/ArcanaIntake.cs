using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Where Arcana comes from, besides the map's stream (Магия-правила.md): meditation (beside a lit sanctuary
    /// three times as fast - the altar), and the artifacts a mage wears - their charge drawn back into the core, at half.
    /// An Avir does not swell with what is past the reserve: the core fills to its edge, and the rest goes into the
    /// essence (the author's rule), with the essence's own falling-off.</summary>
    public static class ArcanaIntake
    {
        /// <summary>What a share of Arcana is worth as essence (raw, before the essence's falling-off).</summary>
        public const float EssencePerArcana = 0.002f;
        /// <summary>Of an artifact's charge, what comes back as Arcana.</summary>
        public const float FromArtifact = 0.5f;

        public static bool Avir(Pawn p) => AvirEssence.GeneOf(p) != null;

        /// <summary>Arcana taken in on purpose (meditation, artifacts).</summary>
        public static void Take(Hediff_ArcaneCore c, float add)
        {
            if (c == null || add <= 0f) return;
            if (!Avir(c.pawn)) { c.Add(add); return; }
            float room = Mathf.Max(0f, c.Capacity - c.arcana);
            float into = Mathf.Min(add, room);
            c.Add(into);
            if (add > into) AvirEssence.Gain(c.pawn, (add - into) * EssencePerArcana);
        }

        /// <summary>How much charge (in Arcana) the mage's worn artifacts hold.</summary>
        public static float Held(Pawn p) =>
            ArtifactWearCache.Worn(p).Where(s => s.inlaid != null && s.inlaid.HasHeart).Sum(s => s.inlaid.charge * ArtifactCharge.Capacity(s.inlaid, p));

        /// <summary>Draw the worn artifacts' charge into the core: up to the edge of the reserve (an Avir - all of it,
        /// the rest into essence). Returns the Arcana taken.</summary>
        public static float Absorb(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null) return 0f;
            bool avir = Avir(p);
            float got = 0f;
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
            {
                ArtifactState st = s.inlaid;
                if (st == null || !st.HasHeart || st.charge <= 0.001f) continue;
                float cap = Mathf.Max(1f, ArtifactCharge.Capacity(st, p));
                float want = avir ? float.MaxValue : Mathf.Max(0f, c.Capacity - c.arcana) / FromArtifact;
                if (want <= 0.01f) break;
                float units = Mathf.Min(st.charge * cap, want);
                st.charge = Mathf.Max(0f, st.charge - units / cap);
                Take(c, units * FromArtifact);
                got += units * FromArtifact;
            }
            if (got > 0f)
            {
                ArtifactMilestones.Invalidate(p);
                ArtifactBonusCache.Invalidate(p);
                c.Note("Drew " + got.ToString("0") + " Arcana from the artifacts.");
                if (p.Spawned) FleckMaker.ThrowLightningGlow(p.DrawPos, p.Map, 1f);
            }
            return got;
        }

        /// <summary>A point of the path put into the reserve: +10% of the first reserve, for good (the author's choice for
        /// points not wanted on a path).</summary>
        public const float PerPoint = 0.1f;

        public static bool Invest(Hediff_ArcaneCore c)
        {
            if (c == null || c.points <= 0) return false;
            c.points--;
            c.reservePoints++;
            c.Note("Put a point of the path into the reserve.");
            return true;
        }
    }
}
