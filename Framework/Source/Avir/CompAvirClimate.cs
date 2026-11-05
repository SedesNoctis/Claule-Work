using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    public class CompProperties_AvirClimate : CompProperties
    {
        /// <summary>Energy budget per second, in the same units vanilla heaters use. Applied in
        /// whichever direction the room needs, which is what makes one comp replace both a heater
        /// and a cooler.</summary>
        public float energyPerSecond = 25f;

        public float defaultTargetTemperature = 21f;
        public float minTargetTemperature = -50f;
        public float maxTargetTemperature = 50f;

        public CompProperties_AvirClimate()
        {
            compClass = typeof(CompAvirClimate);
        }
    }

    /// <summary>
    /// CLIMATE CONTROL WITHOUT THE COOLER'S BAGGAGE.
    ///
    /// A vanilla cooler has to be built into a wall and dumps its heat out the other side, which
    /// means it only works on a room boundary, it heats whatever is outside, and it stops working
    /// when the outside is already hot. That whole apparatus is the thermodynamics of a machine
    /// moving heat around. The pylon is not moving heat; it is deciding what the temperature is.
    ///
    /// So this comp sits anywhere, needs no wall, has no exhaust side, and pushes the room toward
    /// its target in EITHER direction.
    ///
    /// THE MATH IS VANILLA'S OWN. GenTemperature.ControlTemperatureTempChange is the same helper
    /// heaters and coolers use: it divides the energy budget by the room's cell count, clamps
    /// against the distance still to travel so the room cannot overshoot, and returns zero
    /// outright for a room that uses outdoor temperature. Reimplementing that by hand is how you
    /// get a device that heats the open sky.
    ///
    /// The one thing it does NOT do is run both ways from a single call - a positive budget only
    /// heats, a negative one only cools. Hence the sign flip below, which is the entire trick.
    ///
    /// TICKS RARE. Room temperature is not a per-tick quantity and vanilla temperature devices are
    /// rare-tickers too; the pylon holds a 250-tick cadence like everything else on the map.
    /// </summary>
    public class CompAvirClimate : ThingComp
    {
        public CompProperties_AvirClimate Props => (CompProperties_AvirClimate)props;

        private float targetTemperature = 21f;
        private CompPowerTrader powerComp;
        private CompRefuelable refuelableComp;
        private CompFlickable flickableComp;

        public float TargetTemperature
        {
            get => targetTemperature;
            set => targetTemperature = Mathf.Clamp(value, Props.minTargetTemperature, Props.maxTargetTemperature);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad) targetTemperature = Props.defaultTargetTemperature;

            powerComp = parent.TryGetComp<CompPowerTrader>();
            refuelableComp = parent.TryGetComp<CompRefuelable>();
            flickableComp = parent.TryGetComp<CompFlickable>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref targetTemperature, "avirClimateTarget", 21f);
        }

        private bool Running
        {
            get
            {
                if (flickableComp != null && !flickableComp.SwitchIsOn) return false;
                if (refuelableComp != null && !refuelableComp.HasFuel) return false;

                // The pylon GENERATES power rather than drawing it, so a CompPowerTrader here
                // reports negative consumption and PowerOn stays true while it is running. It is
                // checked anyway so that a switched-off or broken pylon stops regulating too.
                if (powerComp != null && !powerComp.PowerOn) return false;

                return true;
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (!parent.Spawned || !Running) return;

            Map map = parent.Map;
            if (map == null) return;

            Room room = parent.Position.GetRoom(map);
            if (room == null || room.UsesOutdoorTemperature) return;

            float current = room.Temperature;
            float gap = targetTemperature - current;
            if (Mathf.Abs(gap) < 0.1f) return;

            // Sign selects the direction; ControlTemperatureTempChange clamps the magnitude so the
            // room approaches the target without crossing it.
            float budget = gap > 0f ? Props.energyPerSecond : -Props.energyPerSecond;

            float change = GenTemperature.ControlTemperatureTempChange(
                parent.Position, map, budget, targetTemperature);

            if (Mathf.Abs(change) > 0.0001f) room.Temperature += change;
        }

        public override string CompInspectStringExtra()
        {
            return "Avir_PylonClimateTarget".Translate(targetTemperature.ToStringTemperature("F0"));
        }

        public override System.Collections.Generic.IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = "Avir_PylonRaiseTarget".Translate(),
                defaultDesc = "Avir_PylonRaiseTargetDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Plus", false),
                action = () => TargetTemperature += 1f,
            };

            yield return new Command_Action
            {
                defaultLabel = "Avir_PylonLowerTarget".Translate(),
                defaultDesc = "Avir_PylonLowerTargetDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Minus", false),
                action = () => TargetTemperature -= 1f,
            };
        }
    }
}
