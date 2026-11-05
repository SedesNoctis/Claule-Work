using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Ritualistics
{
    /// <summary>The storm mantle: what an Eye of the storm leaves in the ritualist who held it to the end.
    /// One pool of energy is both the shield (every hit of external violence is taken 1:1 from it) and
    /// the lightning thrown at foes; it bleeds away in about a day. The strongest shield in the game:
    /// thousands of points against a shield belt's hundred or so.</summary>
    public class Hediff_StormMantle : HediffWithComps
    {
        /// <summary>Mantle energy per unit of energy held in the circle at the end.</summary>
        public const float PerBank = 4f;
        private const float BoltCost = 25f;
        private const int BoltCooldown = 75;
        private const float BoltRange = 24f;
        /// <summary>The mantle is a dome: friends under it are shielded, foes who step in are struck first and fast.</summary>
        public const float DomeRadius = 6f;
        private const int DomeCooldown = 35;
        private const float BoltDamage = 22f;
        /// <summary>Share of the full mantle lost every 250 ticks (about a day in all).</summary>
        private const float Bleed = 0.004f;

        public float energy;
        public float energyMax = 1f;
        private int nextBolt;

        public static readonly HashSet<Pawn> Bearers = new HashSet<Pawn>();

        /// <summary>The ritualist's own artifacts drink first: every worn artifact is filled before the mantle forms.
        /// Returns what is left.</summary>
        public static float FillWorn(Pawn p, float energy)
        {
            foreach (CompArtifactSocket so in ArtifactWearCache.Worn(p))
            {
                ArtifactState st = so.inlaid;
                if (st == null || st.charge >= 0.999f) continue;
                float cap = Mathf.Max(1f, ArtifactCharge.Capacity(st, null));
                float give = Mathf.Min((1f - st.charge) * cap, energy);
                st.charge = Mathf.Min(1f, st.charge + give / cap);
                energy -= give;
                if (energy <= 0f) break;
            }
            return energy;
        }

        public static void Grant(Pawn p, float amount)
        {
            if (p?.health == null || amount <= 0f) return;
            var h = p.health.hediffSet.GetFirstHediffOfDef(RitualDefOf.Avir_StormMantle) as Hediff_StormMantle;
            if (h == null)
            {
                h = (Hediff_StormMantle)HediffMaker.MakeHediff(RitualDefOf.Avir_StormMantle, p);
                p.health.AddHediff(h);
            }
            h.energy += amount;
            h.energyMax = Mathf.Max(h.energyMax, h.energy);
            Bearers.Add(p);
            if (p.Spawned)
            {
                FleckMaker.ThrowLightningGlow(p.DrawPos, p.Map, 3f);
                SoundDefOf.Thunder_OnMap.PlayOneShotOnCamera(p.Map);
                Messages.Message(p.LabelShortCap + " walks out of the circle in a storm mantle (" + Mathf.FloorToInt(h.energy) + ").", p, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        public override bool ShouldRemove => base.ShouldRemove || energy < 1f;

        public override void PostRemoved()
        {
            base.PostRemoved();
            Bearers.Remove(pawn);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            Bearers.Add(pawn);
            int now = Find.TickManager.TicksGame;
            if (pawn.IsHashIntervalTick(250, delta)) energy -= energyMax * Bleed;
            float sev = Mathf.Clamp(energy / Mathf.Max(1f, energyMax), 0.01f, 1f);
            if (Mathf.Abs(Severity - sev) > 0.01f) Severity = sev;
            if (now >= nextBolt && pawn.Spawned && !pawn.Downed && energy > BoltCost)
            {
                Pawn foe = Target(out bool inDome);
                if (foe != null) { Bolt(foe); nextBolt = now + (inDome ? DomeCooldown : BoltCooldown); }
                else nextBolt = now + 20;
            }
        }

        /// <summary>Foes under the dome first (no sight needed - they are inside the storm), then the nearest in sight.</summary>
        private Pawn Target(out bool inDome)
        {
            Map map = pawn.Map;
            inDome = false;
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned)
                if (!q.Dead && !q.Downed && q.HostileTo(pawn) && (q.Position - pawn.Position).LengthHorizontalSquared <= DomeRadius * DomeRadius)
                { inDome = true; return q; }
            Pawn best = null;
            float bestD = BoltRange * BoltRange;
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned)
            {
                if (q.Dead || q.Downed || !q.HostileTo(pawn)) continue;
                float d = (q.Position - pawn.Position).LengthHorizontalSquared;
                if (d > bestD || !GenSight.LineOfSight(pawn.Position, q.Position, map, true)) continue;
                best = q; bestD = d;
            }
            return best;
        }

        private void Bolt(Pawn foe)
        {
            Map map = pawn.Map;
            energy -= BoltCost;
            IntVec3 c = foe.Position;
            // a real strike from the sky where nothing of ours stands close; otherwise a bolt from the mantle itself
            bool clear = !c.Roofed(map) && !map.mapPawns.AllPawnsSpawned.Any(x => x != foe && !x.HostileTo(pawn) && (x.Position - c).LengthHorizontalSquared <= 9);
            if (clear) map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, c));
            else
            {
                FleckMaker.ThrowLightningGlow(foe.DrawPos, map, 2f);
                FleckMaker.ThrowMicroSparks(foe.DrawPos, map);
                SoundDefOf.Thunder_OnMap.PlayOneShot(new TargetInfo(c, map));
            }
            FleckMaker.ThrowLightningGlow(pawn.DrawPos, map, 1.2f);
            foe.TakeDamage(new DamageInfo(DamageDefOf.Burn, BoltDamage, 0.6f, -1f, pawn));
            if (foe.RaceProps.IsMechanoid) foe.TakeDamage(new DamageInfo(DamageDefOf.EMP, 20f, 0f, -1f, pawn));
            else foe.stances?.stunner?.StunFor(40, pawn, false, false);
        }

        /// <summary>Takes the hit from the mantle. True if all of it was taken.</summary>
        public bool Absorb(ref DamageInfo dinfo, Pawn hit)
        {
            float amount = dinfo.Amount;
            if (amount <= 0f || energy <= 0f) return false;
            Map map = pawn.MapHeld;
            if (energy >= amount)
            {
                energy -= amount;
                if (hit.Spawned)
                {
                    SoundDefOf.EnergyShield_AbsorbDamage.PlayOneShot(new TargetInfo(hit.Position, map));
                    FleckMaker.ThrowLightningGlow(hit.DrawPos, map, 1.5f);
                }
                return true;
            }
            dinfo.SetAmount(amount - energy);
            energy = 0f;
            if (pawn.Spawned)
            {
                Effecter e = EffecterDefOf.Shield_Break.Spawn();
                e.Trigger(pawn, pawn);
                e.Cleanup();
            }
            return false;
        }

        public override string LabelInBrackets => Mathf.FloorToInt(energy) + " / " + Mathf.FloorToInt(energyMax);

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (pawn.Faction == Faction.OfPlayer && Find.Selector.SingleSelectedThing == pawn)
                yield return new Gizmo_StormMantle { mantle = this };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref energy, "energy");
            Scribe_Values.Look(ref energyMax, "energyMax", 1f);
            Scribe_Values.Look(ref nextBolt, "nextBolt");
        }
    }

    public class Gizmo_StormMantle : Gizmo
    {
        public Hediff_StormMantle mantle;
        private static readonly Texture2D Full = SolidColorMaterials.NewSolidColorTexture(new Color(0.55f, 0.35f, 0.85f));
        private static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(Color.clear);

        public Gizmo_StormMantle() { Order = -102f; }

        public override float GetWidth(float maxWidth) => 140f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect r = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(r);
            Rect inner = r.ContractedBy(6f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, inner.height / 2f), "Storm mantle");
            Rect bar = new Rect(inner.x, inner.y + inner.height / 2f, inner.width, inner.height / 2f);
            Widgets.FillableBar(bar, mantle.energy / Mathf.Max(1f, mantle.energyMax), Full, Empty, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, Mathf.FloorToInt(mantle.energy) + " / " + Mathf.FloorToInt(mantle.energyMax));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, mantle.def.description);
            return new GizmoResult(GizmoState.Clear);
        }
    }

    /// <summary>The mantle takes hits before the artifact shields and everything else - the bearer's own,
    /// and those of every friend under the dome.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    [HarmonyBefore("avir.artifactorics")]
    public static class Patch_StormMantleAbsorb
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Pawn __instance, ref DamageInfo __0, ref bool __1)
        {
            if (__1 || Hediff_StormMantle.Bearers.Count == 0 || !__instance.Spawned) return true;
            if (__0.Def == null || !__0.Def.ExternalViolenceFor(__instance)) return true;
            foreach (Pawn b in Hediff_StormMantle.Bearers)
            {
                if (b == null || !b.Spawned || b.Map != __instance.Map) continue;
                if (b != __instance && (b.HostileTo(__instance) || (b.Position - __instance.Position).LengthHorizontalSquared > Hediff_StormMantle.DomeRadius * Hediff_StormMantle.DomeRadius)) continue;
                if (b.health.hediffSet.GetFirstHediffOfDef(RitualDefOf.Avir_StormMantle) is Hediff_StormMantle m && m.Absorb(ref __0, __instance))
                {
                    __1 = true;
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Draws the mantle: a violet bubble with flickers of lightning.</summary>
    public class MapComponent_StormMantles : MapComponent
    {
        public MapComponent_StormMantles(Map map) : base(map) { }

        public override void MapComponentUpdate()
        {
            if (Hediff_StormMantle.Bearers.Count == 0 || Find.CurrentMap != map) return;
            foreach (Pawn p in Hediff_StormMantle.Bearers)
            {
                if (p == null || !p.Spawned || p.Map != map) continue;
                var h = p.health?.hediffSet?.GetFirstHediffOfDef(RitualDefOf.Avir_StormMantle) as Hediff_StormMantle;
                if (h == null || h.energy <= 0f) continue;
                float frac = h.energy / Mathf.Max(1f, h.energyMax);
                Vector3 pos = p.DrawPos;
                pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                float pulse = 0.06f * Mathf.Sin(Time.realtimeSinceStartup * 5f + p.thingIDNumber);
                float size = Mathf.Lerp(1.7f, 2.2f, frac) + pulse;
                Color tint = new Color(0.78f, 0.6f, 1f, Mathf.Lerp(0.55f, 1f, frac));
                Material mat = MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent, tint);
                Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.AngleAxis(Time.realtimeSinceStartup * 40f % 360f, Vector3.up), new Vector3(size, 1f, size));
                Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
                Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
                // the dome
                float d = Hediff_StormMantle.DomeRadius * 2f + pulse * 3f;
                Color dome = new Color(0.7f, 0.55f, 1f, Mathf.Lerp(0.18f, 0.4f, frac));
                Matrix4x4 dm = Matrix4x4.TRS(pos, Quaternion.AngleAxis(-Time.realtimeSinceStartup * 12f % 360f, Vector3.up), new Vector3(d, 1f, d));
                Graphics.DrawMesh(MeshPool.plane10, dm, MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent, dome), 0);
            }
        }
    }
}
