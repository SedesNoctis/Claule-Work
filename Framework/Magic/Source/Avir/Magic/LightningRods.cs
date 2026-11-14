using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>
    /// THE LIGHTNING ROD (the author, after Fix266: "cheap, out of any metal - if the storm strikes their power and the
    /// rods are dear, they will hate me"). A pole of any metal, a minor research of the Middle Ages. Every strike of the
    /// sky within nine cells goes into it and down into the ground: no charge runs on to the wires, the batteries or the
    /// people. Switched "into the grid", the rod sends the strike to the batteries of its net instead; what they cannot
    /// take goes into the ground (never a short circuit). A caster of the Storm standing near the rod drinks a little
    /// Arcana from each strike. Strikes a Stormcaller aims are not drawn.
    /// </summary>
    public class Building_LightningRod : Building
    {
        public const float Reach = 9f, Feed = 800f, ArcanaPer = 4f;
        public bool toGrid;
        public int taken;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref toGrid, "toGrid");
            Scribe_Values.Look(ref taken, "taken");
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (Faction != Faction.OfPlayer) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = toGrid ? "Into the grid" : "Into the ground",
                defaultDesc = "Into the ground (as built): every strike goes down the pole and is gone - nothing reaches the wires.\n\nInto the grid: a strike charges the batteries of the net this rod touches (" + Feed.ToString("0") + " Wd a strike); what they cannot take goes into the ground. Never a short circuit.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/TogglePower", false) ?? BaseContent.BadTex,
                isActive = () => toGrid,
                toggleAction = () => toGrid = !toGrid
            };
            if (DebugSettings.ShowDevGizmos)
                yield return new Command_Action { defaultLabel = "DEV: strike the sky here", action = () => Map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(Map, Position)) };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string add = (toGrid ? "Into the grid" : "Into the ground") + "; strikes taken: " + taken + (Position.Roofed(Map) ? "\nUnder a roof - the sky cannot reach it." : "");
            return s.NullOrEmpty() ? add : s + "\n" + add;
        }

        /// <summary>A strike of the sky taken by the rod.</summary>
        public void Take()
        {
            taken++;
            FleckMaker.ThrowLightningGlow(DrawPos, Map, 1.6f);
            FleckMaker.ThrowMicroSparks(DrawPos, Map);
            if (toGrid && GetComp<CompPower>()?.PowerNet is PowerNet net)
            {
                float left = Feed;
                foreach (CompPowerBattery b in net.batteryComps)
                {
                    if (left <= 0f) break;
                    float give = Mathf.Min(left, b.AmountCanAccept);
                    if (give <= 0f) continue;
                    b.AddEnergy(give); left -= give;
                    FleckMaker.ThrowMicroSparks(b.parent.TrueCenter(), Map);
                }
            }
            // a caster of the Storm near the pole drinks of it
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(Position, Map, 3f, true).OfType<Pawn>())
            {
                Hediff_ArcaneCore c = Mages.Core(p);
                if (c != null && MagicWork.Knows(c, RuneKind.Water) && MagicWork.Knows(c, RuneKind.Air)) { c.Add(ArcanaPer); MoteMaker.ThrowText(p.DrawPos, Map, "+" + ArcanaPer.ToString("0") + " Arcana", new Color(0.7f, 0.85f, 1f)); }
            }
        }
    }

    public static class LightningRods
    {
        /// <summary>A strike aimed by a Stormcaller: not drawn to a rod, not moved out of the eye.</summary>
        public static bool Aimed;

        public static Building_LightningRod Nearest(Map map, IntVec3 at)
        {
            Building_LightningRod best = null; float bd = 999f;
            foreach (Building b in map.listerBuildings.allBuildingsColonist.Concat(map.listerBuildings.allBuildingsNonColonist))
            {
                if (!(b is Building_LightningRod r) || r.Position.Roofed(map)) continue;
                float d = r.Position.DistanceTo(at);
                if (d <= Building_LightningRod.Reach && d < bd) { bd = d; best = r; }
            }
            return best;
        }

        public static Building_LightningRod At(Map map, IntVec3 c) => c.InBounds(map) ? c.GetEdifice(map) as Building_LightningRod : null;
    }
}
