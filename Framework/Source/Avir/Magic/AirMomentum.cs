using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Air's own rule - momentum: spells of the air cast one after another, within ten seconds of each other,
    /// come quicker and cheaper, 15% a stage, up to three stages; a pause lets the wind drop (all three set in the options). Storm, haze and dust,
    /// being of the air, gather it too.</summary>
    public static class AirMomentum
    {
        public static int Window => Mathf.RoundToInt(AvirMod.Settings.airMomentumSeconds * 60f);
        public static int MaxStage => Mathf.Clamp(AvirMod.Settings.airMomentumStages, 1, 5);
        public static float PerStage => AvirMod.Settings.airMomentumStep;

        public static HediffDef Def => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_AirMomentum");

        public static int Stage(Pawn p)
        {
            Hediff h = Def != null ? p?.health?.hediffSet?.GetFirstHediffOfDef(Def) : null;
            return h == null ? 0 : Mathf.Clamp(Mathf.RoundToInt(h.Severity), 0, MaxStage);
        }

        /// <summary>What a spell of the air costs and takes now: 1 - 15% a stage.</summary>
        public static float Factor(Pawn p, RitualSchema s) => SpellCasting.OfAir(s) ? Mathf.Max(0.1f, 1f - PerStage * Stage(p)) : 1f;

        /// <summary>A spell of the air cast: the wind rises a stage, and holds for ten more seconds.</summary>
        public static void Note(Pawn p, RitualSchema s)
        {
            if (SpellCasting.OfAir(s)) Rise(p);
        }

        public static void Rise(Pawn p)
        {
            if (p == null || Def == null) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Def);
            bool fresh = h == null;
            if (fresh) { h = p.health.AddHediff(Def); h.Severity = 0f; }
            h.Severity = Mathf.Min(MaxStage, h.Severity + 1f);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Window;
            if (p.Spawned) FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 0.8f + 0.3f * h.Severity, MagicFx.AirDust);
        }
    }
}
