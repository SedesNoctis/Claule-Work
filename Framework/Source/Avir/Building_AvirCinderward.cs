using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE CINDERWARD. Replaces the copied GlobalFireExtinguisher: own class, own def, no
    /// third-party assembly, and a finite hit point total instead of nine nines.
    ///
    /// ONE IDEA, TWO DIRECTIONS. The ward decides what burns. Hostile flame it swallows; filth it
    /// sets alight. Those read as opposites but they are the same authority applied twice, which is
    /// what makes it one building rather than two bolted together.
    ///
    /// RADIUS RATHER THAN MAP-WIDE, and that is a deliberate step down from what it replaced.
    /// Cleaning in RimWorld is real colonist hours, and room cleanliness feeds surgery odds, food
    /// poisoning and mood. Deleting it everywhere would remove a whole system rather than automate
    /// a chore. A ward covers its circle; a colony that wants more builds more.
    ///
    /// IT IGNORES THE HOME AREA ON PURPOSE. The usual vanilla answer to "what does the colony
    /// consider its own" is the home zone, but the specific complaint that prompted this was mined-
    /// out tunnels going filthy - and those are frequently outside it. Anything inside the radius
    /// gets cleaned whether the colony has claimed it or not.
    ///
    /// FILTH IS THINNED, NOT DELETED. Filth.ThinFilth() removes one layer of thickness, so a
    /// bloodstain fades over several passes while a boot print goes at once. That costs nothing
    /// extra and makes the machine visibly working rather than teleporting dirt out of existence.
    /// </summary>
    public class Building_AvirCinderward : Building
    {
        public const float CleanRadius = 75f;

        /// <summary>Watt-days per layer of filth removed. Small, but it means a filthy base is a
        /// line on the power budget rather than a free service.</summary>
        public const float WattDaysPerLayer = 0.4f;

        /// <summary>Layers cleared per sweep. Caps the work done in one tick so a newly built ward
        /// in a squalid base does not clear thousands of layers in a single frame.</summary>
        public const int LayersPerSweep = 40;

        private CompPowerTrader powerComp;
        private CompFlickable flickableComp;

        public CompPowerTrader Power => powerComp ?? (powerComp = this.TryGetComp<CompPowerTrader>());

        private bool Running
        {
            get
            {
                if (flickableComp != null && !flickableComp.SwitchIsOn) return false;
                return Power == null || Power.PowerOn;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = this.TryGetComp<CompPowerTrader>();
            flickableComp = this.TryGetComp<CompFlickable>();
        }

        public override void TickRare()
        {
            base.TickRare();
            if (!Spawned || Map == null || !Running) return;

            SuppressFires();
            BurnFilth();
        }

        /// <summary>
        /// Puts out fires inside the radius.
        ///
        /// Fire is a Thing of def Fire, so this is a lister query rather than a cell walk - the
        /// alternative would be scanning 17,000 cells every rare tick to find the two that are
        /// alight.
        /// </summary>
        private void SuppressFires()
        {
            List<Thing> fires = Map.listerThings.ThingsOfDef(ThingDefOf.Fire);
            float rSq = CleanRadius * CleanRadius;

            for (int i = fires.Count - 1; i >= 0; i--)
            {
                Thing fire = fires[i];
                if (fire == null || fire.Destroyed) continue;
                if ((fire.Position - Position).LengthHorizontalSquared > rSq) continue;

                fire.Destroy(DestroyMode.Vanish);
            }
        }

        private void BurnFilth()
        {
            PowerNet net = Power?.PowerNet;
            float rSq = CleanRadius * CleanRadius;
            int layers = 0;

            // Walks the map's own filth list rather than the radius cell by cell. The list is
            // maintained by the game as filth spawns and despawns, so this is a short walk over
            // things that actually exist instead of a survey of empty ground.
            List<Thing> filthList = Map.listerThings.ThingsInGroup(ThingRequestGroup.Filth);

            for (int i = filthList.Count - 1; i >= 0 && layers < LayersPerSweep; i--)
            {
                Filth filth = filthList[i] as Filth;
                if (filth == null || filth.Destroyed) continue;
                if ((filth.Position - Position).LengthHorizontalSquared > rSq) continue;

                filth.ThinFilth();
                layers++;
            }

            if (layers <= 0 || net == null) return;

            // Charged against stored energy for the same reason the loom is: a variable load
            // computed from how dirty the base happens to be would swing the grid wildly, and a
            // cleaning machine should not be able to brown out a colony.
            float cost = layers * WattDaysPerLayer;
            if (net.CurrentStoredEnergy() <= 0f) return;

            float fraction = Mathf.Min(1f, cost / net.CurrentStoredEnergy());
            for (int i = 0; i < net.batteryComps.Count; i++)
            {
                CompPowerBattery battery = net.batteryComps[i];
                if (battery == null) continue;
                float take = Mathf.Min(battery.StoredEnergy, battery.StoredEnergy * fraction);
                if (take > 0f) battery.DrawPower(take);
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(Position, CleanRadius);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string extra = "Avir_CinderwardStatus".Translate(Mathf.RoundToInt(CleanRadius));
            return s.NullOrEmpty() ? extra : s + "\n" + extra;
        }
    }
}
