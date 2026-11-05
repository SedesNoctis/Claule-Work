using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Spire
{
    /// <summary>
    /// SPIRE CORE. A great engraved sphere that carries energy: made at the artificer's tables from essentium and
    /// precious metals as a dead sphere, then kindled by a ritual - Filling poured into it in a circle. Its strengths
    /// are read from the ritual that kindled it: the open "kindling of a core" gives an ordinary core, a ritualist's
    /// own scheme a stronger one (amplify - power and capacity, closing - efficiency, barrier - ward, breadth - reach).
    /// Up to nine are sunk into the Cradle of the Spire.
    /// </summary>
    public class CompSpireCore : ThingComp
    {
        public const float KindleCost = 3000f;

        public bool kindled;
        public float progress;
        public float capacity, power, ward, reach, efficiency, stored;

        public float Missing => Mathf.Max(0f, capacity - stored);

        /// <summary>Energy poured into a dead sphere; once it is enough the core kindles from the ritual's reading.</summary>
        public float Pour(float amount, RitualReading r, float drawQuality, Pawn by)
        {
            if (kindled) return Charge(amount);
            float g = Mathf.Min(amount, KindleCost - progress);
            progress += g;
            if (progress >= KindleCost - 0.01f) Kindle(r, drawQuality, by);
            return g;
        }

        public float Charge(float amount)
        {
            float g = Mathf.Clamp(amount, 0f, Missing);
            stored += g;
            return g;
        }

        public float Take(float want)
        {
            float g = Mathf.Clamp(want, 0f, stored);
            stored -= g;
            return g;
        }

        private void Kindle(RitualReading r, float q, Pawn by)
        {
            kindled = true;
            float size = r != null ? RitualCircles.PowerFactor(r.schema.size) : 1f;
            int amplify = r?.amplify ?? 0, closing = r?.closing ?? 0, barrier = r?.barrier ?? 0, breadth = r?.breadth ?? 0;
            q = Mathf.Max(0.2f, q);
            power = size * (1f + 0.3f * amplify) * q;
            capacity = 2000f * power;
            efficiency = closing > 0 ? Mathf.Min(1f, 0.7f + 0.1f * closing) : 0.5f;
            ward = 1f + 0.5f * barrier;
            reach = 1f + 0.25f * breadth;
            stored = 0f;
            if (parent.Spawned)
            {
                RitualFx.Blast(parent.DrawPos, parent.Map, 1.5f);
                Messages.Message("A spire core is kindled: power " + power.ToString("0.00") + ", capacity " + capacity.ToString("0") + ", efficiency " + efficiency.ToStringPercent()
                                 + ", ward x" + ward.ToString("0.0") + ", reach x" + reach.ToString("0.00") + ".", parent, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        public override string TransformLabel(string label) => kindled ? label : label + " (dead sphere)";

        public override string CompInspectStringExtra() =>
            !kindled
                ? "A dead sphere: pour " + KindleCost.ToString("0") + " energy into it in a ritual of filling to kindle it (" + (progress / KindleCost).ToStringPercent() + ")."
                : "Energy " + stored.ToString("0") + " / " + capacity.ToString("0") + "\nPower " + power.ToString("0.00") + ", efficiency " + efficiency.ToStringPercent()
                  + ", ward x" + ward.ToString("0.0") + ", reach x" + reach.ToString("0.00");

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref kindled, "kindled");
            Scribe_Values.Look(ref progress, "progress");
            Scribe_Values.Look(ref capacity, "capacity");
            Scribe_Values.Look(ref power, "power");
            Scribe_Values.Look(ref ward, "ward");
            Scribe_Values.Look(ref reach, "reach");
            Scribe_Values.Look(ref efficiency, "efficiency");
            Scribe_Values.Look(ref stored, "stored");
        }
    }
}
