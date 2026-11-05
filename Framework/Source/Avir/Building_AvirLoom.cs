using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>One queued job. Kept minimal on purpose - everything else is derivable.</summary>
    public class AvirLoomOrder : IExposable
    {
        public ThingDef thingDef;
        public ThingDef stuffDef;
        public QualityCategory quality = QualityCategory.Normal;
        public int targetCount = 1;
        public bool maintain;
        public bool toStockpile = true;
        public bool paused;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref quality, "quality", QualityCategory.Normal);
            Scribe_Values.Look(ref targetCount, "targetCount", 1);
            Scribe_Values.Look(ref maintain, "maintain");
            Scribe_Values.Look(ref toStockpile, "toStockpile", true);
            Scribe_Values.Look(ref paused, "paused");
        }
    }

    /// <summary>
    /// THE PATTERN LOOM, WRITTEN FROM SCRATCH.
    ///
    /// This is not a reskin of anything. The layout of its window borrows the shape that works -
    /// category tree, filtered list, detail panel, order queue - because that shape is not anyone's
    /// property and half the games ever made use it. The code is ours, the textures are ours, and
    /// no third-party assembly is involved.
    ///
    /// WHAT IT COSTS. Power, scaled by market value and by quality, drawn as a steady load across
    /// the whole job rather than as one impossible spike. The formula lives in CostWattDays and is
    /// deliberately one line: anyone tuning balance should be able to read it without tracing
    /// anything.
    ///
    /// WHAT IT MAY NOT MAKE is in AvirCrafterFilter, driven by XML. The short version: no Avir
    /// materials, nothing made OF Avir materials, no essence drugs, no psylink or hemogen, no
    /// silver or gold, no archotech, no quest-only items. Components stay craftable by decision.
    ///
    /// WHY THE ESSENCE DRUGS MATTER MORE THAN THEY LOOK. Essence extract fuels the pylon and the
    /// reservoir. If the loom could print it, the entire essence economy - the siphon, the farm,
    /// the choir upkeep - collapses on the day this building is finished. That exclusion is
    /// load-bearing, not flavour.
    /// </summary>
    public class Building_AvirLoom : Building
    {
        /// <summary>
        /// Watt-days per point of market value. One of the two balance levers, and the one to reach
        /// for when the loom feels too cheap or too expensive rather than too slow.
        /// </summary>
        public const float WattDaysPerValue = 0.8f;

        /// <summary>
        /// THE LOOM DOES NOT PULL A LOAD - IT SPENDS STORED ENERGY.
        ///
        /// This is how the building this replaces did it, and it is the only way to have a fixed
        /// craft time and a cost proportional to the order at the same time. Charging the cost as a
        /// continuous draw makes the wattage cost divided by time, so a large order in a fixed
        /// window asks for an impossible number - 3000 steel in an hour is 109,000 W, and an
        /// unfixed version of the same mistake produced the 6,562,550 W reading earlier.
        ///
        /// Instead the loom idles at IdlePowerDraw and buys its progress straight out of the grid's
        /// batteries. The grid sees a withdrawal, not a load. That makes duration and cost fully
        /// independent, which is what lets the time below be a flat constant.
        ///
        /// The withdrawal walks PowerNet.batteryComps and calls DrawPower on each, which is exactly
        /// what the building this replaces did. PowerNet.ChangeStoredEnergy would be the tidier
        /// call and is NOT USABLE: it is private. Reading a method's body in the IL says nothing
        /// about whether it can be called - that mistake cost a compile round here.
        ///
        /// CONSEQUENCE WORTH KNOWING: the loom now needs BATTERIES, not generation. A colony with
        /// 20,000 W of pylons and no storage cannot run it at all, and the failure is reported
        /// rather than silent.
        /// </summary>
        public const float CraftTicks = 2500f;

        public const float IdlePowerDraw = 50f;

        public List<AvirLoomOrder> orders = new List<AvirLoomOrder>();

        private float progressTicks;
        private CompPowerTrader powerComp;
        private CompRefuelable fuelComp;

        public CompPowerTrader Power => powerComp ?? (powerComp = this.TryGetComp<CompPowerTrader>());

        /// <summary>The Feignallite tank. Only transmutations touch it; ordinary weaving costs
        /// power alone.</summary>
        public CompRefuelable Tank => fuelComp ?? (fuelComp = this.TryGetComp<CompRefuelable>());

        public AvirLoomOrder Current
        {
            get
            {
                for (int i = 0; i < orders.Count; i++)
                {
                    AvirLoomOrder o = orders[i];
                    if (o?.thingDef == null || o.paused) continue;
                    if (o.maintain && CountOnMap(o.thingDef) >= o.targetCount) continue;
                    if (!o.maintain && o.targetCount <= 0) continue;
                    return o;
                }

                return null;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = this.TryGetComp<CompPowerTrader>();
            fuelComp = this.TryGetComp<CompRefuelable>();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref orders, "avirLoomOrders", LookMode.Deep);
            Scribe_Values.Look(ref progressTicks, "avirLoomProgress", 0f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (orders == null) orders = new List<AvirLoomOrder>();

                // An order whose def left with an uninstalled mod becomes a null that would throw on
                // the next window draw. Drop them quietly - a save that will not open because someone
                // unsubscribed from a furniture mod is a far worse outcome than a lost order.
                orders.RemoveAll(o => o?.thingDef == null);
            }
        }

        public int CountOnMap(ThingDef def)
        {
            if (Map == null || def == null) return 0;
            return Map.resourceCounter.GetCount(def)
                   + Map.listerThings.ThingsOfDef(def).Count;
        }

        public static float CostWattDays(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            if (def == null) return 0f;
            float value = def.GetStatValueAbstract(StatDefOf.MarketValue, stuff);
            if (value < 1f) value = 1f;

            float cost = value * WattDaysPerValue * QualityMultiplier(quality);

            // Transmutation is priced per BATCH, not per unit, and costs extra on top: rearranging
            // matter is a different act from holding a shape in mind.
            AvirTransmuteEntry t = AvirTransmute.EntryFor(def);
            if (t != null) cost = cost * t.count * t.powerMultiplier;

            return cost;
        }

        /// <summary>Flat. Deliberately ignores the item, the quality and the order size - that is
        /// the whole point of charging cost against stored energy instead of as a load.</summary>
        public static float WorkTicks(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            return CraftTicks;
        }

        /// <summary>Quality is priced steeply and on purpose. A loom that turned out legendary work
        /// for a little extra power would make every crafting pawn in the colony pointless.</summary>
        public static float QualityMultiplier(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return 0.5f;
                case QualityCategory.Poor: return 0.75f;
                case QualityCategory.Normal: return 1f;
                case QualityCategory.Good: return 1.8f;
                case QualityCategory.Excellent: return 3.5f;
                case QualityCategory.Masterwork: return 8f;
                case QualityCategory.Legendary: return 20f;
                default: return 1f;
            }
        }

        /// <summary>The loom's standing load. Constant: the real cost is withdrawn from batteries
        /// at completion, not drawn continuously.</summary>
        public float CurrentDrawWatts => IdlePowerDraw;

        public float ProgressPercent
        {
            get
            {
                AvirLoomOrder o = Current;
                if (o == null) return 0f;
                return Mathf.Clamp01(progressTicks / CraftTicks);
            }
        }

        public override void TickRare()
        {
            base.TickRare();

            AvirLoomOrder order = Current;

            if (Power != null) Power.PowerOutput = -CurrentDrawWatts;

            if (order == null)
            {
                progressTicks = 0f;
                StallReason = null;
                return;
            }

            if (Power != null && !Power.PowerOn) return;

            // PAID AS IT GOES, NOT ON COMPLETION.
            //
            // Charging only at the end meant the hour of work was free and the withdrawal happened
            // at one instant: spend the batteries on something else during that hour and the loom
            // sat at 100% waiting, having consumed nothing. Worse, an order the colony could never
            // afford waited there forever without saying so.
            //
            // Now each slice of progress is bought before it is credited. Run out of stored energy
            // and progress simply stops where it is - which is both what a real machine does and
            // what the player can act on.
            float total = TotalCostOf(order);
            float share = total * (GenTicks.TickRareInterval / CraftTicks);

            PowerNet net = Power?.PowerNet;
            if (net == null)
            {
                StallReason = "Avir_LoomNoGrid".Translate();
                return;
            }

            if (net.CurrentStoredEnergy() < share)
            {
                StallReason = "Avir_LoomNeedsPower".Translate(
                    Mathf.RoundToInt(total), Mathf.RoundToInt(net.CurrentStoredEnergy()));
                return;
            }

            DrawFromBatteries(net, share);
            StallReason = null;

            progressTicks += GenTicks.TickRareInterval;
            if (progressTicks < CraftTicks) return;
            progressTicks = 0f;

            Produce(order);
        }

        /// <summary>Null unless the current job is stalled, in which case it explains why. Shown in
        /// the inspect string, because a machine that silently does nothing is the worst possible
        /// failure mode for a building this expensive.</summary>
        public string StallReason { get; private set; }

        /// <summary>How many units the current order still wants. Transmutation is one batch.</summary>
        public static int UnitsFor(AvirLoomOrder order, Building_AvirLoom loom)
        {
            if (order?.thingDef == null) return 0;
            if (AvirTransmute.IsTransmutable(order.thingDef)) return 1;

            return order.maintain
                ? Mathf.Max(1, order.targetCount - (loom?.CountOnMap(order.thingDef) ?? 0))
                : Mathf.Max(1, order.targetCount);
        }

        /// <summary>Whole cost of the current craft, which is what gets spread across its hour.</summary>
        public float TotalCostOf(AvirLoomOrder order)
        {
            if (order?.thingDef == null) return 0f;
            float unit = CostWattDays(order.thingDef, order.stuffDef, order.quality);
            return AvirTransmute.IsTransmutable(order.thingDef)
                ? unit
                : unit * UnitsFor(order, this);
        }

        /// <summary>Watt-days sitting in the batteries this loom is wired to.</summary>
        public float StoredOnNet =>
            Power?.PowerNet != null ? Power.PowerNet.CurrentStoredEnergy() : 0f;

        /// <summary>
        /// Takes watt-days out of the net's batteries.
        ///
        /// Spread proportionally rather than draining one battery at a time, so a bank discharges
        /// evenly and no single cell empties while its neighbours stay full. The caller has already
        /// checked that the total is available, but the per-battery take is clamped to what each
        /// actually holds anyway - floating-point drift across a dozen cells is otherwise enough to
        /// push one of them slightly negative.
        /// </summary>
        private static void DrawFromBatteries(PowerNet net, float amount)
        {
            if (net?.batteryComps == null || amount <= 0f) return;

            float stored = net.CurrentStoredEnergy();
            if (stored <= 0f) return;

            float fraction = Mathf.Min(1f, amount / stored);

            for (int i = 0; i < net.batteryComps.Count; i++)
            {
                CompPowerBattery battery = net.batteryComps[i];
                if (battery == null) continue;

                float take = Mathf.Min(battery.StoredEnergy, battery.StoredEnergy * fraction);
                if (take > 0f) battery.DrawPower(take);
            }
        }

        private void Produce(AvirLoomOrder order)
        {
            AvirTransmuteEntry t = AvirTransmute.EntryFor(order.thingDef);
            int units = UnitsFor(order, this);

            // Power was already paid across the hour. Feignallite is taken here instead, at the
            // moment of completion, because it is a discrete material rather than a flow - taking
            // fractions of it per tick would leave the tank holding meaningless remainders.
            if (t != null && !AvirTransmute.TryConsume(Tank, t.feignallite))
            {
                StallReason = "Avir_LoomNeedsFeignallite".Translate(
                    t.feignallite, AvirTransmute.AvailableIn(Tank));

                // Held at full so the finished work is not thrown away; it completes the moment the
                // Feignallite arrives. The power for this craft is already spent either way.
                progressTicks = CraftTicks;
                return;
            }

            StallReason = null;
            int outputCount = t != null ? t.count : units;

            // Emitted in stack-sized pieces: 3000 steel becomes forty stacks of 75, not three
            // thousand single units on the floor. Quality is set on EVERY piece - setting it only
            // on the first was a real bug, since quality items have a stackLimit of 1 and the whole
            // batch after the first came out with none.
            int left = outputCount;
            while (left > 0)
            {
                Thing piece = ThingMaker.MakeThing(order.thingDef, order.stuffDef);
                piece.stackCount = Mathf.Min(left, piece.def.stackLimit);
                left -= piece.stackCount;

                piece.TryGetComp<CompQuality>()?.SetQuality(order.quality, ArtGenerationContext.Colony);
                GenPlace.TryPlaceThing(piece, Position, Map, ThingPlaceMode.Near);
            }

            if (!order.maintain)
            {
                order.targetCount -= units;
                if (order.targetCount <= 0) orders.Remove(order);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = "Avir_LoomOpen".Translate(),
                defaultDesc = "Avir_LoomOpenDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Avir/Crafter/CloseX", false),
                action = () => Find.WindowStack.Add(new UI.Dialog_AvirLoom(this)),
            };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();

            AvirLoomOrder o = Current;
            if (!StallReason.NullOrEmpty() && o != null)
            {
                return s.NullOrEmpty() ? StallReason : s + "\n" + StallReason;
            }

            string extra = o == null
                ? "Avir_LoomIdle".Translate(orders.Count)
                : "Avir_LoomWorking".Translate(
                    o.thingDef.LabelCap,
                    Mathf.RoundToInt(ProgressPercent * 100f),
                    Mathf.RoundToInt(TotalCostOf(o) * ProgressPercent));

            return s.NullOrEmpty() ? extra : s + "\n" + extra;
        }
    }
}
