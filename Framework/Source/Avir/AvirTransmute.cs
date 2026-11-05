using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>One transmutation: so much Feignallite becomes so many of something.</summary>
    public class AvirTransmuteEntry
    {
        /// <summary>defName of the product. A string, not a ThingDef, for the same reason the
        /// blocklist uses strings: PsylinkNeuroformer needs Royalty and Mechlink needs Biotech, and
        /// naming an absent one must cost nothing rather than logging an error.</summary>
        public string thing;

        /// <summary>Feignallite consumed per craft. Hard-capped at MaxFeignallitePerCraft.</summary>
        public int feignallite = 10;

        /// <summary>How many come out per craft.</summary>
        public int count = 1;

        /// <summary>Multiplies the power cost the loom would otherwise charge. Transmutation is not
        /// weaving a shape, it is rearranging matter, and it should cost more.</summary>
        public float powerMultiplier = 2f;
    }

    /// <summary>
    /// TRANSMUTATION. The exception to the blocklist, and the reason silver and gold are not simply
    /// forbidden.
    ///
    /// Printing currency out of electricity ends trade, caravans and any reason to leave the map.
    /// But making it out of Feignallite is a different act: Feignallite is finite, it is mined and
    /// grown rather than generated, and every unit spent here is a unit not spent on warp blades or
    /// on the Core. The loom stops being a printing press and becomes an exchange rate.
    ///
    /// THE 300 CEILING IS A HARD RULE, enforced in code and not just in the data below. At 18
    /// silver a unit, 300 Feignallite is 5400 of value committed to a single craft - enough that
    /// the expensive entries hurt, and low enough that no entry can quietly become a money pump.
    ///
    /// Anything listed here is craftable even if it also appears in the exclusion file: an entry
    /// here is a deliberate, priced exception and overrides the blanket ban.
    /// </summary>
    public class AvirTransmuteDef : Def
    {
        public List<AvirTransmuteEntry> entries = new List<AvirTransmuteEntry>();
    }

    [StaticConstructorOnStartup]
    public static class AvirTransmute
    {
        public const int MaxFeignallitePerCraft = 300;

        private static Dictionary<ThingDef, AvirTransmuteEntry> table;
        private static ThingDef feignallite;

        public static ThingDef Feignallite =>
            feignallite ?? (feignallite = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Feignallite"));

        private static void Build()
        {
            if (table != null) return;
            table = new Dictionary<ThingDef, AvirTransmuteEntry>();

            int clamped = 0;

            foreach (AvirTransmuteDef d in DefDatabase<AvirTransmuteDef>.AllDefsListForReading)
            {
                if (d.entries == null) continue;
                foreach (AvirTransmuteEntry e in d.entries)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(e.thing);
                    if (def == null) continue;

                    // The ceiling is enforced here rather than trusted to the XML, so that editing
                    // the data can never lift it by accident.
                    if (e.feignallite > MaxFeignallitePerCraft)
                    {
                        e.feignallite = MaxFeignallitePerCraft;
                        clamped++;
                    }

                    if (e.feignallite < 1) e.feignallite = 1;
                    if (e.count < 1) e.count = 1;

                    table[def] = e;
                }
            }

            if (clamped > 0)
            {
                Log.Warning($"[Avir] {clamped} transmutation entries exceeded the "
                            + $"{MaxFeignallitePerCraft} Feignallite ceiling and were clamped.");
            }
        }

        public static bool IsTransmutable(ThingDef def)
        {
            if (def == null) return false;
            Build();
            return table.ContainsKey(def);
        }

        public static AvirTransmuteEntry EntryFor(ThingDef def)
        {
            if (def == null) return null;
            Build();
            return table.TryGetValue(def, out AvirTransmuteEntry e) ? e : null;
        }

        /// <summary>
        /// Feignallite sitting in a loom's own tank.
        ///
        /// AN INTERNAL TANK RATHER THAN THE MAP'S STOCKPILES, AND THE REASON IS HAULING. Pulling
        /// straight off the map meant the loom silently teleported material out of a stockpile
        /// across the base, with no job, no hauler and nothing for the player to see going wrong.
        /// A CompRefuelable tank gets vanilla's whole refuelling apparatus for free: colonists
        /// carry Feignallite to the building, the fuel bar shows on the map, auto-refuel has a
        /// threshold, and the material is visibly committed before it is spent.
        ///
        /// It also paces the machine. At a 500 capacity against a 300 ceiling, one expensive
        /// transmutation empties most of the tank and the next has to wait on a hauler.
        /// </summary>
        public static int AvailableIn(CompRefuelable tank)
        {
            return tank == null ? 0 : Mathf.FloorToInt(tank.Fuel);
        }

        /// <summary>
        /// Spends fuel from the tank.
        ///
        /// Takes NOTHING unless the whole amount is there. A partial take would consume the
        /// material and produce no product, which reads to the player as the building quietly
        /// eating their stock - the single most confusing thing a machine can do.
        /// </summary>
        public static bool TryConsume(CompRefuelable tank, int amount)
        {
            if (tank == null || amount <= 0) return false;
            if (tank.Fuel < amount) return false;
            tank.ConsumeFuel(amount);
            return true;
        }
    }
}
