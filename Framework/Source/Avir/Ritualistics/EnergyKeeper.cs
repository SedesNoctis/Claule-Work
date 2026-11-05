using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    public class CompProperties_EnergyKeeper : CompProperties
    {
        public float capacity = 300f;
        public float chargeRadius = 6f;
        /// <summary>Energy it gives out to what it charges, per rare tick, all together.</summary>
        public float chargeRate = 3f;

        public CompProperties_EnergyKeeper() { compClass = typeof(CompEnergyKeeper); }
    }

    /// <summary>
    /// ENERGY KEEPER. A small light set into the floor that holds energy like a battery: rituals fill it (or draw
    /// from it) by reach or by thread, and it glows while it holds any - in the colour the player picks, or as a
    /// darklight. Around itself it charges artifact buildings and the artifacts colonists wear. Spent, it goes dark.
    /// </summary>
    public class CompEnergyKeeper : ThingComp
    {
        public static readonly HashSet<CompEnergyKeeper> All = new HashSet<CompEnergyKeeper>();

        public float stored;
        public bool chargeOthers = true;

        public CompProperties_EnergyKeeper Props => (CompProperties_EnergyKeeper)props;
        public float Capacity => Props.capacity;
        public bool IsFull => stored >= Capacity - 0.01f;
        public bool Lit => stored > 0.01f;

        public override void PostSpawnSetup(bool respawningAfterLoad) { base.PostSpawnSetup(respawningAfterLoad); All.Add(this); MapComponent_StreamNetwork.Of(parent.Map)?.SetDirty(); }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish) { base.PostDeSpawn(map, mode); All.Remove(this); MapComponent_StreamNetwork.Of(map)?.SetDirty(); }

        public float Give(float amount)
        {
            bool was = Lit;
            float g = Mathf.Clamp(amount, 0f, Capacity - stored);
            stored += g;
            if (was != Lit) Relight();
            return g;
        }

        public float Take(float want)
        {
            bool was = Lit;
            float g = Mathf.Clamp(want, 0f, stored);
            stored -= g;
            if (was != Lit) Relight();
            return g;
        }

        private void Relight()
        {
            if (parent.Spawned) parent.GetComp<CompGlower>()?.UpdateLit(parent.Map);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!chargeOthers || !Lit || !parent.Spawned) return;
            float budget = Mathf.Min(Props.chargeRate, stored);
            Map map = parent.Map;
            float r2 = Props.chargeRadius * Props.chargeRadius;
            // artifacts worn by colonists nearby, the emptiest first
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (budget <= 0.01f) break;
                if ((p.Position - parent.Position).LengthHorizontalSquared > r2) continue;
                bool any = false;
                foreach (Artifactorics.CompArtifactSocket s in Artifactorics.ArtifactWearCache.Worn(p).Where(s => s.inlaid != null && s.inlaid.HasHeart && s.inlaid.charge < 0.999f).OrderBy(s => s.inlaid.charge))
                {
                    float cap = Mathf.Max(1f, Artifactorics.ArtifactCharge.Capacity(s.inlaid, p));
                    float g = Mathf.Min(budget, (1f - s.inlaid.charge) * cap);
                    s.inlaid.charge = Mathf.Min(1f, s.inlaid.charge + g / cap);
                    budget -= Take(g);
                    any = true;
                    if (budget <= 0.01f) break;
                }
                if (any) { Artifactorics.ArtifactMilestones.Invalidate(p); Artifactorics.ArtifactBonusCache.Invalidate(p); }
            }
            // artifact buildings nearby
            foreach (Artifactorics.CompArtifactHost h in Artifactorics.CompArtifactHost.All)
            {
                if (budget <= 0.01f) break;
                if (h.parent.Map != map || (h.parent.Position - parent.Position).LengthHorizontalSquared > r2) continue;
                float g = CompRitualCircle.GiveTo(h.parent, budget);
                budget -= Take(g);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            yield return new Command_Toggle
            {
                defaultLabel = "Charge around",
                defaultDesc = "Give the held energy to artifact buildings and to the artifacts colonists wear within " + Props.chargeRadius.ToString("0") + " cells.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/KeeperCharge", false) ?? TexCommand.ForbidOff,
                isActive = () => chargeOthers,
                toggleAction = () => chargeOthers = !chargeOthers
            };
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action { defaultLabel = "Dev: fill", action = () => Give(Capacity) };
                yield return new Command_Action { defaultLabel = "Dev: empty", action = () => Take(Capacity) };
            }
        }

        public override string CompInspectStringExtra()
        {
            string s = "Energy held: " + stored.ToString("0") + " / " + Capacity.ToString("0") + (Lit ? "" : " - dark");
            StreamNet net = parent.Spawned ? MapComponent_StreamNetwork.Of(parent.Map)?.NetOf(parent) : null;
            if (net != null) s += "\nStream network: " + net.Stored.ToString("0") + " / " + net.Capacity.ToString("0") + " in " + net.Keepers.Count() + " keepers, " + net.nodes.Count + " connected";
            return s;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref stored, "stored");
            Scribe_Values.Look(ref chargeOthers, "chargeOthers", true);
        }
    }

    /// <summary>The keeper's light: only while it holds energy.</summary>
    public class CompGlower_EnergyKeeper : CompGlower
    {
        protected override bool ShouldBeLitNow => base.ShouldBeLitNow && parent.GetComp<CompEnergyKeeper>()?.Lit == true;

        /// <summary>Any colour from the start: the keeper's light is a crystal's, not a coloured lamp, so the
        /// "coloured lights" research does not gate it.</summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                if (g is Command_ColorIcon c && c.Disabled && c.icon == ChangeColorIcon) { c.Disabled = false; c.disabledReason = null; }
                yield return g;
            }
        }

        private static Texture ChangeColorIcon => changeColorIcon ?? (changeColorIcon = ContentFinder<Texture2D>.Get("UI/Commands/ChangeColor"));
        private static Texture changeColorIcon;
    }
}

namespace Avir.Ritualistics
{
    /// <summary>The advanced energy keeper calls the ships only while it holds energy: spent, it is no beacon.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Building_OrbitalTradeBeacon), nameof(Building_OrbitalTradeBeacon.AllPowered))]
    public static class Patch_KeeperBeaconNeedsCharge
    {
        public static void Postfix(ref IEnumerable<Building_OrbitalTradeBeacon> __result)
        {
            __result = __result.Where(b => b.GetComp<CompEnergyKeeper>() is not CompEnergyKeeper k || k.Lit);
        }
    }
}
