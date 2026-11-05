using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>
    /// Avir feathers in primitive buildings are an alternative way to charge their stones - never a requirement:
    /// no "out of fuel", nothing brought unless the player sets a target.
    /// </summary>
    public class CompRefuelable_OptionalFeathers : CompRefuelable
    {
        public override string CompInspectStringExtra()
        {
            string s = "Avir feathers (optional - they charge the stones): " + Fuel.ToStringDecimalIfSmall() + " / " + Props.fuelCapacity.ToStringDecimalIfSmall();
            if (Props.targetFuelLevelConfigurable && TargetFuelLevel > 0f) s += "\n" + "ConfiguredTargetFuelLevel".Translate(TargetFuelLevel.ToStringDecimalIfSmall());
            return s;
        }
    }

    /// <summary>The temperature attuners have no power: vanilla's line would end with an empty line (and log an error).</summary>
    public class CompTempControl_Unpowered : CompTempControl
    {
        public override string CompInspectStringExtra() => base.CompInspectStringExtra()?.TrimEnd();
    }

    /// <summary>The essentium projector throws a beam, not bullets: vanilla's turret radius would find no gun verb.</summary>
    public class PlaceWorker_ShowBeamRadius : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            VerbProperties v = (checkingDef as ThingDef)?.building?.turretGunDef?.Verbs?.FirstOrDefault();
            if (v != null && v.range > 0f) GenDraw.DrawRadiusRing(loc, Mathf.Min(v.range, GenRadial.MaxRadialPatternRadius - 1f));
            return true;
        }
    }
}
