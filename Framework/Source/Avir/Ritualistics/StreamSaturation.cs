using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>
    /// STREAM SATURATION: the pawn's own charge of the stream. 100% is a pawn as it is; a ritual that draws on it
    /// empties it, a ritual that closes its energy in leaves the surplus in its performer - with no ceiling.
    /// Full of the stream, a pawn is psychically stronger and holds it less steadily; emptied, it is dulled.
    /// It settles back to 100% over about half a day.
    /// </summary>
    public class Hediff_StreamSaturation : HediffWithComps
    {
        /// <summary>Energy units per 1% of saturation: a pawn at 100% holds this × 100.</summary>
        public const float UnitsPerPercent = 3f;

        public float level = 1f;
        private float notified = 1f;
        private HediffStage stage;
        private float stageFor = -1f;

        public override HediffStage CurStage
        {
            get
            {
                if (stage == null || Mathf.Abs(stageFor - level) > 0.01f)
                {
                    stageFor = level;
                    float above = Mathf.Max(0f, level - 1f), below = Mathf.Max(0f, 1f - level);
                    stage = new HediffStage { statOffsets = new List<StatModifier>() };
                    stage.statOffsets.Add(new StatModifier { stat = StatDefOf.PsychicSensitivity, value = 0.5f * above - 0.8f * below });
                    if (above > 0f && RitualDefOf.Avir_Composure != null)
                        stage.statOffsets.Add(new StatModifier { stat = RitualDefOf.Avir_Composure, value = -0.05f * above });
                }
                return stage;
            }
        }

        public override string LabelInBrackets => level.ToStringPercent("0");
        public override bool ShouldRemove => level == 1f;
        public override Color LabelColor => level >= 1f ? new Color(0.78f, 0.55f, 1f) : new Color(0.55f, 0.50f, 0.65f);

        public override string Description =>
            base.Description + "\n\nNow: " + level.ToStringPercent("0") + " (" + (level * 100f * UnitsPerPercent).ToString("0") + " energy).";

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn.IsHashIntervalTick(250, delta))
            {
                level += (1f - level) * 0.005f;
                if (Mathf.Abs(level - 1f) < 0.01f) level = 1f;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref level, "level", 1f);
        }

        // ------------------------------------------------------------------ the pawn's charge

        public static HediffDef Def => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StreamSaturation");

        public static float Level(Pawn p) =>
            (p?.health?.hediffSet?.GetFirstHediffOfDef(Def) as Hediff_StreamSaturation)?.level ?? 1f;

        /// <summary>Energy units the pawn still has to give.</summary>
        public static float Held(Pawn p) => Level(p) * 100f * UnitsPerPercent;

        /// <summary>Adds (or, negative, takes) energy units; returns what was actually taken when taking.</summary>
        public static float Change(Pawn p, float units)
        {
            if (p?.health == null || Def == null || Mathf.Abs(units) < 0.0001f) return 0f;
            var h = p.health.hediffSet.GetFirstHediffOfDef(Def) as Hediff_StreamSaturation;
            float cur = h?.level ?? 1f;
            float next = Mathf.Max(0f, cur + units / (100f * UnitsPerPercent));
            if (next == cur) return 0f;
            if (h == null)
            {
                h = (Hediff_StreamSaturation)HediffMaker.MakeHediff(Def, p);
                h.level = next;
                h.notified = next;
                p.health.AddHediff(h);
            }
            else
            {
                h.level = next;
                // the health tab and stats are told only at each 5% step - a ritual changes this every tick
                if (Mathf.Abs(h.notified - next) > 0.05f) { h.notified = next; p.health.Notify_HediffChanged(h); }
            }
            return (cur - next) * 100f * UnitsPerPercent;
        }
    }
}
