using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE ESSENCE RESERVOIR. Replaces the copied OmniBattery entirely - new defName, new class,
    /// no third-party assembly, and no infinite storage.
    ///
    /// WHAT IT ACTUALLY IS: a battery whose fuel is Avir. A Fearborn walks up and gives part of
    /// their own reserve; the reservoir holds it as current. Human technology is beneath the Avir,
    /// but a bench is a bench, so what comes out the other side is ordinary power on ordinary wire.
    ///
    /// THE EXCHANGE RATE, AND WHY IT IS THIS NUMBER:
    ///
    ///     0.01 of reserve  =  100 Wd        (so a full reserve, 1.0, is 10 000 Wd)
    ///
    /// An Avir feeding once a day nets +0.45 from the siphon less 0.10 natural decay = 0.35 of
    /// reserve per day of true surplus, which at this rate is 3500 W sustained. That is one Avir
    /// carrying roughly a wind turbine. It is also exactly what one essence extract is worth to a
    /// pylon (3500 Wd), so the living donor and the dead donor price out the same and the fiction
    /// stays coherent whichever way the player feeds the grid.
    ///
    /// STORAGE IS 50 000 Wd - about five full Avir, or two weeks of a mid-sized base. Large, as
    /// asked. Not infinite, which the thing it replaces was.
    ///
    /// THE ONE-WAY RULE IS LOAD-BEARING. An earlier version of this design let the grid refill
    /// Arcane Hunger as well. That would have made solar panels into an infinite feeding trough
    /// and turned the siphon, the essence farm and the choir upkeep - the entire tension the race
    /// is built around - into decoration. Avir into the grid. Never the reverse.
    /// </summary>
    public class Building_AvirEssenceReservoir : Building
    {
        /// <summary>Watt-days bought by one full point of reserve (i.e. by 1.00, the whole bar).</summary>
        public const float WattDaysPerFullReserve = 10000f;

        /// <summary>A willing colonist keeps this much. Below 0.60 psychic sensitivity starts to
        /// degrade and below 0.45 a choir collapses, so a volunteer stops at the point where
        /// giving more would start costing them their own abilities.</summary>
        public const float VolunteerFloor = 0.60f;

        /// <summary>Prisoners and slaves are drained to nothing. This is the entire mechanical
        /// difference between asking and taking, and it is meant to be visible in the numbers:
        /// an unwilling donor is worth more than twice a willing one.</summary>
        public const float CoercedFloor = 0f;

        /// <summary>Donation is offered whenever the reservoir is below this fraction. Lives on the
        /// BUILDING rather than on pawns - one number the player sets once, instead of a toggle
        /// per Avir.</summary>
        public float refillBelowPct = 0.5f;

        public bool coerceCaptives = true;

        private CompPowerBattery batteryComp;

        public CompPowerBattery Battery => batteryComp ?? (batteryComp = this.TryGetComp<CompPowerBattery>());

        public float StoredPct => Battery != null ? Battery.StoredEnergyPct : 1f;

        public bool WantsDonation => Battery != null && StoredPct < refillBelowPct;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref refillBelowPct, "avirRefillBelowPct", 0.5f);
            Scribe_Values.Look(ref coerceCaptives, "avirCoerceCaptives", true);
        }

        /// <summary>How much of their reserve this pawn may give right now. Zero means "not a
        /// donor" - no gene, nothing spare, or a captive on a machine set to ask rather than take.</summary>
        public float DonatableFrom(Pawn pawn)
        {
            Gene_ArcaneHunger gene = Gene_ArcaneHunger.Of(pawn);
            if (gene == null || !gene.Active) return 0f;

            bool captive = pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony;
            if (captive && !coerceCaptives) return 0f;

            float floor = captive ? CoercedFloor : VolunteerFloor;
            float available = gene.Value - floor;
            if (available <= 0.01f) return 0f;

            // Never take more than the reservoir can actually hold - a donation that spills is a
            // pawn spent for nothing, and the player would have no way to see it happen.
            float roomLeft = Battery != null ? Battery.AmountCanAccept : 0f;
            float maxByRoom = roomLeft / WattDaysPerFullReserve;

            return Mathf.Min(available, maxByRoom);
        }

        /// <summary>Performs the transfer. Called from the job driver once the pawn has arrived.</summary>
        public bool AcceptDonation(Pawn pawn)
        {
            float amount = DonatableFrom(pawn);
            if (amount <= 0.01f) return false;
            if (!Gene_ArcaneHunger.Spend(pawn, amount)) return false;

            Battery?.AddEnergy(amount * WattDaysPerFullReserve);

            bool captive = pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony;
            if (captive)
            {
                // Taking past the point a volunteer would stop is not free. The thought is the
                // only signal the player gets that this is a different act from a donation.
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_DrainedForTheReservoir");
                if (thought != null) pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
            }

            return true;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = "Avir_ReservoirRefillAt".Translate(Mathf.RoundToInt(refillBelowPct * 100f)),
                defaultDesc = "Avir_ReservoirRefillAtDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Plus", false),
                action = () =>
                {
                    refillBelowPct += 0.25f;
                    if (refillBelowPct > 1.001f) refillBelowPct = 0.25f;
                },
            };

            yield return new Command_Toggle
            {
                defaultLabel = "Avir_ReservoirCoerce".Translate(),
                defaultDesc = "Avir_ReservoirCoerceDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ForbidOff", false),
                isActive = () => coerceCaptives,
                toggleAction = () => coerceCaptives = !coerceCaptives,
            };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string extra = "Avir_ReservoirStatus".Translate(
                Mathf.RoundToInt(refillBelowPct * 100f),
                Mathf.RoundToInt(WattDaysPerFullReserve));
            return s.NullOrEmpty() ? extra : s + "\n" + extra;
        }
    }
}
