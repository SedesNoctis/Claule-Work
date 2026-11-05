using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    [DefOf]
    public static class ArtifactArmDefOf
    {
        public static ArtifactArmDef ArtifactArm_Mobility;
        public static ArtifactArmDef ArtifactArm_Shields;
        public static ArtifactArmDef ArtifactArm_Learning;
        public static ArtifactArmDef ArtifactArm_Efficiency;
        public static ArtifactArmDef ArtifactArm_Swiftness;
        public static ArtifactArmDef ArtifactArm_Command;
        public static ArtifactArmDef ArtifactArm_Unseen;

        static ArtifactArmDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactArmDefOf)); }
    }

    [DefOf]
    public static class ArtifactHediffDefOf
    {
        public static HediffDef Avir_ArtifactShield;
        public static HediffDef Avir_ArtifactAuraShield;

        static ArtifactHediffDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactHediffDefOf)); }
    }

    /// <summary>
    /// Stage 8b - the streaming shield (Shields 3/7/9) and the shield aura (Command 6.1).
    /// The Shields path's own shield absorbs ranged hits only (shots, arrows, blasts); the Command aura absorbs every hit
    /// of external violence, melee too. It starts recovering right after a hit -
    /// unlike a shield belt it never waits - except when it is broken to zero: then it recovers
    /// only once the pawn has taken no damage for brokenShieldRegenDelaySeconds (5 s), the shared
    /// rule of spec 6.2. It does not block shooting and takes no apparel slot.
    /// Strength (capacity) and regeneration are both scaled by the arm strength: focus x heart
    /// quality, so essentium and tisserium shields can be very strong. Capacity has a ceiling,
    /// regeneration does not.
    /// </summary>
    public class Hediff_ArtifactShield : HediffWithComps
    {
        public float energy;
        public float energyMax = 1f;
        public float regenPerSecond;
        public int lastHitTick = -99999;
        public bool broken;
        /// <summary>Aura shields fade when the giver stops refreshing them.</summary>
        public int expiresAtTick = -1;
        public Color tint = new Color(0.55f, 0.75f, 1f);
        [Unsaved] public int auraStamp = -1;

        public bool IsAura => def == ArtifactHediffDefOf.Avir_ArtifactAuraShield;

        private static float DelayTicks => (ArtifactoricsSettingsDef.Main?.brokenShieldRegenDelaySeconds ?? 5f) * 60f;

        public override bool ShouldRemove
        {
            get
            {
                if (base.ShouldRemove) return true;
                return IsAura && expiresAtTick >= 0 && Find.TickManager.TicksGame > expiresAtTick;
            }
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            int now = Find.TickManager.TicksGame;
            if (broken)
            {
                if (now - lastHitTick < DelayTicks) return;
                broken = false;
                if (pawn.Spawned) SoundDefOf.EnergyShield_Reset.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            }
            if (energy < energyMax) energy = Mathf.Min(energyMax, energy + regenPerSecond * delta / 60f);
            else UpdateOverflow(delta);
            if (!IsAura)
            {
                float sev = 0.001f + OverflowFraction * 0.998f;
                if (Mathf.Abs(Severity - sev) > 0.01f) Severity = sev;
            }
        }

        public override string LabelInBrackets
        {
            get
            {
                string s = Mathf.FloorToInt(energy) + " / " + Mathf.FloorToInt(energyMax);
                return broken ? s + ", broken" : s;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                return base.TipStringExtra + "\nRecovers " + regenPerSecond.ToString("0.#") + " per second, starting right after a hit. "
                       + "Broken to zero, it waits until " + pawn.LabelShort + " has taken no damage for "
                       + (DelayTicks / 60f).ToString("0.#") + " s.";
            }
        }

        /// <summary>Takes what it can from the hit. Returns true if the whole hit was absorbed.</summary>
        public bool Absorb(ref DamageInfo dinfo)
        {
            int now = Find.TickManager.TicksGame;
            lastHitTick = now;
            overflow = 0f;   // Shields 9: the extra armour is spent by any hit
            if (broken || energy <= 0f) return false;
            float amount = dinfo.Amount;
            if (amount <= 0f) return false;
            Map map = pawn.MapHeld;

            // Command 9 - united line: the hit is shared by every shield of the same aura
            if (IsAura && unitedLine && giverId >= 0)
                amount = ArtifactCommand.ShareAcrossLine(this, amount);

            if (!IsAura) ArtifactCharge.Spend(pawn, ArtifactArmDefOf.ArtifactArm_Shields,
                Mathf.Min(energy, amount) * (ArtifactoricsSettingsDef.Main?.chargePerShieldDamage ?? 0.1f));
            if (energy >= amount)
            {
                energy -= amount;
                if (pawn.Spawned)
                {
                    SoundDefOf.EnergyShield_AbsorbDamage.PlayOneShot(new TargetInfo(pawn.Position, map));
                    FleckMaker.Static(pawn.TrueCenter(), map, FleckDefOf.ExplosionFlash, 2f);
                }
                if (!IsAura && level >= 7) ArtifactCommand.Knockback(pawn, dinfo.Instigator);
                if (energy <= 0.01f) Break(dinfo.Instigator);
                return true;
            }
            dinfo.SetAmount(amount - energy);
            energy = 0f;
            Break(dinfo.Instigator);
            return false;
        }

        public void Break(Thing instigator)
        {
            energy = 0f;
            broken = true;
            if (!pawn.Spawned) return;
            Effecter e = EffecterDefOf.Shield_Break.Spawn();
            e.Trigger(pawn, pawn);
            e.Cleanup();
            if (IsAura && reprisal) ArtifactCommand.Reprisal(pawn, instigator);   // Command 3 (not the rampart barrier)
            else if (level >= 9) ArtifactCommand.BannerPulse(pawn);           // Shields 9
        }

        // ---- Shields 9: recovery beyond full turns into armour, lost at the next hit ----
        public float overflow;

        /// <summary>Milestone that gave the own shield (3, 7, 9); 0 for an aura shield.</summary>
        public int level;
        /// <summary>Aura only: who gives it, and whether the giver has Command 9.</summary>
        public int giverId = -1;
        public bool unitedLine;
        /// <summary>Aura from Command (3+): breaking it stuns the attacker. False for the rampart barrier.</summary>
        public bool reprisal = true;

        public void UpdateOverflow(int delta)
        {
            if (IsAura || level < 9 || broken || energy < energyMax - 0.01f) return;
            float cap = energyMax * (ArtifactoricsSettingsDef.Main?.overflowShareOfShield ?? 0.2f);
            overflow = Mathf.Min(cap, overflow + regenPerSecond * delta / 60f);
        }

        /// <summary>0..1 share of the overflow ceiling, drives the armour stages.</summary>
        public float OverflowFraction
        {
            get
            {
                float cap = energyMax * (ArtifactoricsSettingsDef.Main?.overflowShareOfShield ?? 0.2f);
                return cap > 0f ? overflow / cap : 0f;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (pawn.Faction == Faction.OfPlayer && Find.Selector.SingleSelectedThing == pawn)
                yield return new Gizmo_ArtifactShield { shield = this };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref energy, "energy");
            Scribe_Values.Look(ref energyMax, "energyMax", 1f);
            Scribe_Values.Look(ref regenPerSecond, "regenPerSecond");
            Scribe_Values.Look(ref lastHitTick, "lastHitTick", -99999);
            Scribe_Values.Look(ref broken, "broken");
            Scribe_Values.Look(ref expiresAtTick, "expiresAtTick", -1);
            Scribe_Values.Look(ref tint, "tint", new Color(0.55f, 0.75f, 1f));
            Scribe_Values.Look(ref overflow, "overflow");
            Scribe_Values.Look(ref level, "level");
            Scribe_Values.Look(ref giverId, "giverId", -1);
            Scribe_Values.Look(ref unitedLine, "unitedLine");
            Scribe_Values.Look(ref reprisal, "reprisal", true);
        }
    }

    /// <summary>Energy bar, like the shield belt's. Without a shield it shows any charge given to it (a mage's ward).</summary>
    public class Gizmo_ArtifactShield : Gizmo
    {
        public Hediff_ArtifactShield shield;
        public string title;
        public Func<float> cur, max;
        public Func<string> tip;
        public Color fill = new Color(0.2f, 0.45f, 0.75f);
        private Texture2D fillTex;
        private Color fillOf;
        private static readonly Texture2D Full = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.45f, 0.75f));
        private static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(Color.clear);

        public Gizmo_ArtifactShield() { Order = -101f; }

        public override float GetWidth(float maxWidth) => 140f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect r = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(r);
            Rect inner = r.ContractedBy(6f);
            Text.Font = GameFont.Tiny;
            if (shield == null)
            {
                float c = cur?.Invoke() ?? 0f, m = max?.Invoke() ?? 1f;
                if (fillTex == null || fillOf != fill) { fillTex = SolidColorMaterials.NewSolidColorTexture(fill); fillOf = fill; }
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, inner.height / 2f), title);
                Rect b = new Rect(inner.x, inner.y + inner.height / 2f, inner.width, inner.height / 2f);
                Widgets.FillableBar(b, m > 0f ? c / m : 0f, fillTex, Empty, false);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(b, Mathf.CeilToInt(c) + " / " + Mathf.CeilToInt(m));
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                if (tip != null) TooltipHandler.TipRegion(r, tip());
                return new GizmoResult(GizmoState.Clear);
            }
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, inner.height / 2f),
                shield.IsAura ? "Shield aura" : "Artifact shield");
            Rect bar = new Rect(inner.x, inner.y + inner.height / 2f, inner.width, inner.height / 2f);
            Widgets.FillableBar(bar, shield.energyMax > 0f ? shield.energy / shield.energyMax : 0f, Full, Empty, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, Mathf.FloorToInt(shield.energy) + " / " + Mathf.FloorToInt(shield.energyMax) + (shield.broken ? " (broken)" : ""));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, shield.TipStringExtra.Trim());
            return new GizmoResult(GizmoState.Clear);
        }
    }

    /// <summary>Hits reach artifact shields before anything else: own shield first, then aura.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_ArtifactShieldAbsorb
    {
        public static bool Prefix(Pawn __instance, ref DamageInfo __0, ref bool __1)
        {
            if (__1) return false;   // already parried (stage 12b)
            if (__instance?.health?.hediffSet == null) return true;
            if (!ArtifactShieldManager.HasShield(__instance)) return true;
            if (__0.Def == null || !__0.Def.ExternalViolenceFor(__instance)) return true;

            List<Hediff> hs = __instance.health.hediffSet.hediffs;
            Hediff_ArtifactShield own = null, aura = null;
            for (int i = 0; i < hs.Count; i++)
            {
                if (hs[i] is Hediff_ArtifactShield s)
                {
                    if (s.IsAura) aura = s; else own = s;
                }
            }
            // the Shields path's own shield stops only what flies (shots, arrows, blasts); a blow in melee goes through it.
            // The Command aura, spread thinner over everyone, takes both.
            bool melee = !__0.Def.isRanged && !__0.Def.isExplosive;
            if (own != null && !melee && own.Absorb(ref __0)) { __1 = true; return false; }
            // Shields 7: the blow goes through the shield, but whoever struck it is still stunned for a moment
            if (own != null && melee && own.level >= 7 && !own.broken && own.energy > 0f) ArtifactCommand.Knockback(__instance, __0.Instigator);
            if (aura != null && aura.Absorb(ref __0)) { __1 = true; return false; }
            return true;
        }
    }

    /// <summary>Keeps shield hediffs in step with milestones and aura givers, and draws the bubbles.</summary>
    public class ArtifactShieldManager : MapComponent
    {
        private const int Interval = 30;
        private static readonly HashSet<Pawn> shielded = new HashSet<Pawn>();
        private readonly List<Pawn> givers = new List<Pawn>();
        private static Material bubble;

        public ArtifactShieldManager(Map map) : base(map) { }

        public static bool HasShield(Pawn p) => shielded.Contains(p);

        public static IEnumerable<Pawn> Shielded => shielded;

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public override void MapComponentTick()
        {
            if (S == null || Find.TickManager.TicksGame % Interval != 0) return;
            int now = Find.TickManager.TicksGame;
            givers.Clear();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.apparel == null || ArtifactWearCache.Worn(p).Count == 0)
                {
                    RemoveOwn(p);
                    continue;
                }
                // own streaming shield
                int lvl = ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Shields, out float str);
                if (lvl > 0 && !p.Dead) EnsureOwn(p, lvl, str);
                else RemoveOwn(p);

                // shield aura givers
                if (ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Command) >= 3 && !p.Downed && !p.Dead) givers.Add(p);
            }

            // Rampart 7 / 9 (stage 12b): the weapon radiates a barrier over allies close by
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn giver = pawns[i];
                if (giver.Dead || giver.Downed || giver.equipment?.Primary?.Socket()?.inlaid == null) continue;
                int rl = ArtifactMilestones.Level(giver, ArtifactWeaponDefOf.ArtifactArm_Rampart, out float rk);
                if (rl < 7) continue;
                int idx = Mathf.Min(rl >= 9 ? 1 : 0, S.rampartBarrierCapacity.Count - 1);
                float cap = S.rampartBarrierCapacity[idx] * rk;
                float regen = S.rampartBarrierRegenPerSecond * rk;
                float rr = S.rampartBarrierRadius * Mathf.Sqrt(Mathf.Max(0f, rk));
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn p = pawns[j];
                    if (p.Dead || p.Faction == null || p.Faction != giver.Faction) continue;
                    if ((p.Position - giver.Position).LengthHorizontalSquared > rr * rr) continue;
                    RefreshAura(p, giver, cap, regen, now, false, false);
                }
            }

            for (int g = 0; g < givers.Count; g++)
            {
                Pawn giver = givers[g];
                int cmd = ArtifactMilestones.Level(giver, ArtifactArmDefOf.ArtifactArm_Command, out float str);
                float cap = S.auraShieldCapacity * str;
                float regen = S.auraShieldRegenPerSecond * str;
                float r2 = S.auraShieldRadius * S.auraShieldRadius;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p.Dead || p.Faction == null || p.Faction != giver.Faction) continue;
                    if ((p.Position - giver.Position).LengthHorizontalSquared > r2) continue;
                    RefreshAura(p, giver, cap, regen, now, cmd >= 9, true);
                    if (cmd >= 7) ArtifactCommand.RefreshResolve(p, Interval * 4);   // Command 7
                }
            }

            shielded.RemoveWhere(p => p.Destroyed || p.health?.hediffSet == null
                                      || !p.health.hediffSet.HasHediff(ArtifactHediffDefOf.Avir_ArtifactShield)
                                      && !p.health.hediffSet.HasHediff(ArtifactHediffDefOf.Avir_ArtifactAuraShield));
        }

        private static Color TintFor(Thing giver)
        {
            if (giver is Pawn p)
            {
                foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
                    if (s.inlaid?.heartMetal != null) return s.inlaid.heartMetal.gemColor;
            }
            else
            {
                CompArtifactHost h = giver?.TryGetComp<CompArtifactHost>();
                if (h != null) foreach (ArtifactState st in h.slots) if (st?.heartMetal != null) return st.heartMetal.gemColor;
            }
            return new Color(0.55f, 0.75f, 1f);
        }

        /// <summary>An aura shield from a building (a constellation's shield function): lasts until the next refresh.</summary>
        public static void RefreshAuraFromBuilding(Pawn p, Thing giver, float cap, float regen, int lastsTicks)
        {
            int now = Find.TickManager.TicksGame;
            RefreshAura(p, giver, cap, regen, now, false, false);
            if (p.health.hediffSet.GetFirstHediffOfDef(ArtifactHediffDefOf.Avir_ArtifactAuraShield) is Hediff_ArtifactShield h)
                h.expiresAtTick = Mathf.Max(h.expiresAtTick, now + lastsTicks);
        }

        private static void EnsureOwn(Pawn p, int lvl, float str)
        {
            int idx = lvl >= 9 ? 2 : lvl >= 7 ? 1 : 0;
            float cap = Mathf.Min(S.shieldCapacityCeiling, S.shieldCapacity[Mathf.Min(idx, S.shieldCapacity.Count - 1)] * str);
            float regen = S.shieldRegenPerSecond[Mathf.Min(idx, S.shieldRegenPerSecond.Count - 1)] * str;
            var h = p.health.hediffSet.GetFirstHediffOfDef(ArtifactHediffDefOf.Avir_ArtifactShield) as Hediff_ArtifactShield;
            if (h == null)
            {
                h = (Hediff_ArtifactShield)HediffMaker.MakeHediff(ArtifactHediffDefOf.Avir_ArtifactShield, p);
                h.energy = 0f;   // a fresh shield fills up; it does not appear full
                p.health.AddHediff(h);
            }
            h.energyMax = cap;
            h.regenPerSecond = regen;
            h.level = lvl;
            h.tint = TintFor(p);
            if (h.energy > cap) h.energy = cap;
            shielded.Add(p);
        }

        private static void RemoveOwn(Pawn p)
        {
            Hediff h = p.health?.hediffSet?.GetFirstHediffOfDef(ArtifactHediffDefOf.Avir_ArtifactShield);
            if (h != null) p.health.RemoveHediff(h);
        }

        private static void RefreshAura(Pawn p, Thing giver, float cap, float regen, int now, bool united, bool reprisal)
        {
            var h = p.health.hediffSet.GetFirstHediffOfDef(ArtifactHediffDefOf.Avir_ArtifactAuraShield) as Hediff_ArtifactShield;
            if (h == null)
            {
                h = (Hediff_ArtifactShield)HediffMaker.MakeHediff(ArtifactHediffDefOf.Avir_ArtifactAuraShield, p);
                h.energy = 0f;
                p.health.AddHediff(h);
            }
            // several givers: within one refresh the strongest wins; a new refresh starts over,
            // so a strong giver walking away does not leave its numbers behind
            if (h.auraStamp != now || cap > h.energyMax)
            {
                if (h.auraStamp == now) h.regenPerSecond = Mathf.Max(h.regenPerSecond, regen);
                else h.regenPerSecond = regen;
                h.auraStamp = now;
                h.energyMax = cap;
                h.giverId = giver.thingIDNumber;
                h.unitedLine = united;
                h.reprisal = reprisal;
                h.tint = TintFor(giver);
            }
            else
            {
                h.regenPerSecond = Mathf.Max(h.regenPerSecond, regen);
            }
            h.expiresAtTick = now + Interval * 4;
            if (h.energy > h.energyMax) h.energy = h.energyMax;
            shielded.Add(p);
        }

        /// <summary>As a vanilla shield belt: the bubble shows only in a fight - drafted, berserk, an enemy, or hit in
        /// the last few seconds - so it does not hang around pawns sleeping, eating or meditating.</summary>
        public static bool ShowBubble(Pawn p, int lastHitTick)
        {
            if (p.Dead || p.Downed) return false;
            if (p.Drafted || p.InAggroMentalState) return true;
            if (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer) && !p.IsPrisoner) return true;
            return Find.TickManager.TicksGame - lastHitTick < 1000;
        }

        public override void MapComponentUpdate()
        {
            if (shielded.Count == 0 || Find.CurrentMap != map) return;
            if (bubble == null) bubble = MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent, Color.white);
            foreach (Pawn p in shielded)
            {
                if (!p.Spawned || p.Map != map) continue;
                float frac = 0f; Color tint = Color.white; int lastHit = -99999;
                foreach (Hediff h in p.health.hediffSet.hediffs)
                {
                    if (h is Hediff_ArtifactShield s && s.energyMax > 0f && s.energy > 0f)
                    {
                        float f = s.energy / s.energyMax;
                        if (f > frac) { frac = f; tint = s.tint; }
                        if (s.lastHitTick > lastHit) lastHit = s.lastHitTick;
                    }
                }
                if (frac <= 0f || !ShowBubble(p, lastHit)) continue;
                Vector3 pos = p.DrawPos;
                pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                // brighter and a little larger than at first: the bubble was hard to see
                float size = Mathf.Lerp(1.45f, 1.8f, frac);
                Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, 1f, size));
                tint = Color.Lerp(tint, Color.white, 0.25f);
                tint.a = Mathf.Lerp(0.55f, 0.95f, frac);
                Material mat = MaterialPool.MatFrom("Other/ShieldBubble", ShaderDatabase.Transparent, tint);
                Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
                Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);   // twice: the vanilla bubble texture is faint
            }
        }
    }
}
