using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE DISSOLUTION ENGINE. Replaces the copied MatterEnergyConverter: own class, own def, no
    /// third-party assembly, and a finite battery instead of a million watt-days.
    ///
    /// IT IS THE LOOM RUN BACKWARDS, and that only became worth building once the loom started
    /// buying its work out of STORED energy. Batteries are the constraint now, so a building that
    /// fills them has a job. The three of them close a loop:
    ///
    ///     reservoir    Avir      -> power
    ///     dissolution  matter    -> power
    ///     loom         power     -> matter
    ///
    /// NO HAULING, WHICH IS THE WHOLE POINT. The thing it replaces was a transporter: you loaded it
    /// and it ate what was inside. This one erases matching things where they lie, inside its
    /// radius. Rotting corpses on a battlefield and tainted rags after a raid are precisely the
    /// items nobody wants to spend a colonist-hour carrying anywhere.
    ///
    /// THE FILTER IS VANILLA'S OWN. Implementing IStoreSettingsParent and listing ITab_Storage on
    /// the def gives the exact stockpile filter panel the player already knows - categories, hit
    /// points, quality, rottenness - for no custom UI at all. That matters more than usual here:
    /// this building DELETES things, and a bespoke filter would be a bespoke way to lose your gear.
    ///
    /// TWO RATES, AND THE SPLIT IS STRUCTURAL RATHER THAN COSMETIC. Material the colony cannot
    /// produce - corpses, above all - pays 50 Wd per point of market value, enough that a colony
    /// can abandon conventional generators entirely. Anything it CAN produce pays 0.4, below what
    /// the loom charged to make it, so weaving-then-dissolving is a loss and there is no cycle to
    /// farm. See the constants.
    /// </summary>
    public class Building_AvirDissolution : Building, IStoreSettingsParent
    {
        /// <summary>
        /// Reach, in cells. 100 covers most of a standard 250x250 map from a central position -
        /// this is meant to keep a whole settlement tidy, not a courtyard.
        ///
        /// The first version used 24 and looked broken: the engine sat in a clearing with nothing
        /// eligible inside its circle and reported nothing, so "it does not work" and "there is
        /// nothing in range" were indistinguishable. Hence both the larger radius AND the target
        /// count in the inspect string below.
        /// </summary>
        public const float Radius = 100f;

        /// <summary>
        /// Watt-days returned per point of market value for things the loom CANNOT make.
        ///
        /// Set above the loom's own 0.8 on purpose: this is meant to be a colony's power source
        /// rather than a disposal fee, and the material it runs on - corpses, ruined gear, raid
        /// leavings - arrives on its own schedule and cannot be manufactured.
        /// </summary>
        public const float WattDaysPerValue = 50f;

        /// <summary>
        /// Rate for anything the loom is able to produce. THIS EXISTS TO CLOSE A PERPETUAL MOTION
        /// MACHINE and it is not a balance preference.
        ///
        /// A flat rate against the loom's 0.8 makes weaving-then-dissolving free money. At 50 it is
        /// not a trickle but a torrent: one one-hour order of 3000 steel would cost 4,560 Wd and
        /// return 285,000.
        ///
        /// So the payout is split by PROVENANCE, not by item. Anything the colony can produce for
        /// itself pays back less than it cost to make, and the loop is a loss at every scale.
        /// Everything else - corpses above all - pays the full rate, which is exactly the material
        /// this building was built for.
        ///
        /// "Can produce" covers three things, and missing any one of them reopens the hole:
        ///   - what the loom can weave
        ///   - what the loom can transmute, since CanCraft already returns true for those
        ///   - the Avir materials themselves, which the loom is forbidden to make precisely
        ///     because the colony grows them. At 50 Wd a point, Feignallite would be worth 900 Wd
        ///     a unit and a Feignallite farm would beat every power source in the game.
        /// </summary>
        public const float WattDaysPerValueCraftable = 0.4f;

        /// <summary>Things dissolved per sweep. Without a cap, switching this on beside a hundred
        /// corpses would resolve the whole field in one tick and hand over the energy instantly.
        /// Six per rare tick is roughly one a second - fast enough to clear a battlefield in a few
        /// minutes, slow enough to watch and cancel.</summary>
        public const int PerSweep = 6;

        private StorageSettings settings;
        private CompPowerBattery batteryComp;
        private CompFlickable flickableComp;

        public CompPowerBattery Battery => batteryComp ?? (batteryComp = this.TryGetComp<CompPowerBattery>());

        public bool StorageTabVisible => true;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            batteryComp = this.TryGetComp<CompPowerBattery>();
            flickableComp = this.TryGetComp<CompFlickable>();
        }

        public override void PostMake()
        {
            base.PostMake();
            settings = new StorageSettings(this);

            // Starts from the def's fixed filter, which the XML sets to rotten corpses and tainted
            // apparel. An engine that begins life willing to eat anything would delete a colony's
            // stockpiles the moment it was switched on.
            if (def.building?.defaultStorageSettings != null)
            {
                settings.CopyFrom(def.building.defaultStorageSettings);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref settings, "avirDissolutionSettings", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && settings == null)
            {
                settings = new StorageSettings(this);
            }
        }

        public StorageSettings GetStoreSettings() => settings;

        public StorageSettings GetParentStoreSettings() => def.building?.fixedStorageSettings;

        public void Notify_SettingsChanged()
        {
        }

        private bool Running
        {
            get
            {
                if (flickableComp != null && !flickableComp.SwitchIsOn) return false;
                return Battery != null && Battery.AmountCanAccept > 0f;
            }
        }

        /// <summary>
        /// Single source of truth for "will this be dissolved". Used by both the sweep and the
        /// inspect string, so the count the player reads can never disagree with what the machine
        /// actually does.
        /// </summary>
        private bool Eligible(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned) return false;
            if ((thing.Position - Position).LengthHorizontalSquared > Radius * Radius) return false;

            // Never touch what a colonist is carrying or what is reserved for a job already in
            // flight - the thing would vanish out of their hands mid-haul.
            if (thing.ParentHolder is Pawn_InventoryTracker) return false;
            if (Map.reservationManager.IsReservedByAnyoneOf(thing, Faction)) return false;

            return settings != null && settings.AllowedToAccept(thing);
        }

        /// <summary>Full rate for what the loom cannot make, reduced rate for what it can. See the
        /// constants for why this is not optional.</summary>
        public static float RateFor(ThingDef def)
        {
            if (def == null) return WattDaysPerValueCraftable;

            bool colonyCanMakeIt = AvirCrafterFilter.CanCraft(def)
                                   || AvirCrafterFilter.IsAvirMaterial(def);

            return colonyCanMakeIt ? WattDaysPerValueCraftable : WattDaysPerValue;
        }

        private int EligibleCount()
        {
            if (!Spawned || Map == null || settings == null) return 0;

            List<Thing> candidates = Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            int n = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (Eligible(candidates[i])) n++;
            }

            return n;
        }

        public override void TickRare()
        {
            base.TickRare();
            if (!Spawned || Map == null || !Running || settings == null) return;

            int done = 0;

            // HaulableEver rather than every Thing: this must never consider buildings, plants or
            // pawns, and the group already excludes them.
            List<Thing> candidates = Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);

            for (int i = candidates.Count - 1; i >= 0 && done < PerSweep; i--)
            {
                Thing thing = candidates[i];
                if (!Eligible(thing)) continue;

                // AddEnergy clamps to what the battery can still take, so a full battery quietly
                // wastes the difference rather than erroring - which is why Running refuses to
                // sweep at all once there is no room left.
                Battery.AddEnergy(thing.MarketValue * thing.stackCount * RateFor(thing.def));
                thing.Destroy(DestroyMode.Vanish);
                done++;
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(Position, Radius);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            // The target count is the diagnostic that was missing. "Nothing in range" and "this
            // building is broken" looked identical before, and the difference is the first thing
            // anyone needs to know.
            string extra = "Avir_DissolutionStatus".Translate(
                Mathf.RoundToInt(Radius),
                WattDaysPerValue.ToString("0.0#"),
                WattDaysPerValueCraftable.ToString("0.0#"),
                EligibleCount());

            if (Battery != null && Battery.AmountCanAccept <= 0f)
            {
                extra += "\n" + "Avir_DissolutionFull".Translate();
            }

            return s.NullOrEmpty() ? extra : s + "\n" + extra;
        }
    }
}
