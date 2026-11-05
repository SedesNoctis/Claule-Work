using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    [DefOf]
    public static class ArtifactCommandDefOf
    {
        public static HediffDef Avir_ArtifactResolve;
        public static HediffDef Avir_ArtifactSecondWind;

        static ArtifactCommandDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactCommandDefOf)); }
    }

    /// <summary>
    /// Stage 8b+ - the shield and aura milestones beyond the plain shields.
    ///   Shields 7   knockback: a melee hit taken fully by the shield stuns the attacker briefly.
    ///   Shields 9   banner: when the own shield breaks, a pulse stuns enemies around the bearer;
    ///               recovery beyond a full shield turns into armour, spent by the next hit.
    ///   Command 3   reprisal: when an ally's aura shield breaks, the attacker nearby is stunned.
    ///   Command 7   focus and resolve: allies in the aura aim better, hit more in melee, endure
    ///               more pain and break less easily.
    ///   Command 9   united line: a hit on one aura shield is shared by every shield of the same
    ///               aura; second wind: once a day an ally falling in the aura gets up again for
    ///               10 s with a full aura shield.
    /// </summary>
    public static class ArtifactCommand
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;
        private static readonly Dictionary<int, int> secondWindUsed = new Dictionary<int, int>();

        private static void Stun(Pawn victim, Thing by, float seconds)
        {
            if (victim == null || victim.Dead || !victim.Spawned || victim.stances?.stunner == null) return;
            victim.stances.stunner.StunFor(Mathf.RoundToInt(seconds * 60f), by, false, true);
        }

        public static void Knockback(Pawn bearer, Thing instigator)
        {
            if (!(instigator is Pawn attacker) || !attacker.Spawned || attacker.Map != bearer.Map) return;
            if (!attacker.Position.AdjacentTo8WayOrInside(bearer.Position)) return;   // melee only
            Stun(attacker, bearer, S?.knockbackStunSeconds ?? 0.5f);
        }

        public static void Reprisal(Pawn ally, Thing instigator)
        {
            if (!(instigator is Pawn attacker) || !attacker.Spawned || attacker.Map != ally.Map) return;
            if (attacker.Position.DistanceTo(ally.Position) > (S?.reprisalRadius ?? 3f)) return;
            Stun(attacker, ally, S?.reprisalStunSeconds ?? 1.5f);
        }

        public static void BannerPulse(Pawn bearer)
        {
            if (!bearer.Spawned) return;
            Map map = bearer.Map;
            float r = S?.bannerRadius ?? 3f;
            FleckMaker.Static(bearer.TrueCenter(), map, FleckDefOf.PsycastAreaEffect, r);
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == bearer || p.Dead || !p.HostileTo(bearer)) continue;
                if (p.Position.DistanceTo(bearer.Position) > r) continue;
                Stun(p, bearer, S?.bannerStunSeconds ?? 2f);
            }
        }

        /// <summary>United line: spreads a hit over the other shields of the same aura and returns
        /// what is left for the struck shield itself.</summary>
        public static float ShareAcrossLine(Hediff_ArtifactShield struck, float amount)
        {
            var others = new List<Hediff_ArtifactShield>();
            foreach (Pawn p in ArtifactShieldManager.Shielded)
            {
                if (p == struck.pawn || !p.Spawned || p.Map != struck.pawn.Map) continue;
                if (p.health.hediffSet.GetFirstHediffOfDef(ArtifactHediffDefOf.Avir_ArtifactAuraShield) is Hediff_ArtifactShield s
                    && s.giverId == struck.giverId && !s.broken && s.energy > 0f)
                    others.Add(s);
            }
            if (others.Count == 0) return amount;
            float share = amount / (others.Count + 1);
            foreach (Hediff_ArtifactShield o in others)
            {
                float take = Mathf.Min(o.energy, share);
                o.energy -= take;
                amount -= take;
                if (o.energy <= 0.01f) o.Break(null);
            }
            return amount;
        }

        /// <summary>Command 7: keeps the focus-and-resolve aura on an ally.</summary>
        public static void RefreshResolve(Pawn p, int ticks)
        {
            HediffDef def = ArtifactCommandDefOf.Avir_ArtifactResolve;
            if (p.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_ArtifactTimed h)
            {
                h.ticksLeft = Mathf.Max(h.ticksLeft, ticks);
                return;
            }
            var n = (Hediff_ArtifactTimed)HediffMaker.MakeHediff(def, p);
            n.ticksLeft = ticks;
            n.level = 7;
            p.health.AddHediff(n);
        }

        /// <summary>Command 9: a pawn about to fall inside a united aura gets up again.</summary>
        public static void TrySecondWind(Pawn p)
        {
            if (p == null || p.Dead || S == null) return;
            if (!(p.health.hediffSet.GetFirstHediffOfDef(ArtifactHediffDefOf.Avir_ArtifactAuraShield) is Hediff_ArtifactShield aura)
                || !aura.unitedLine)
                return;
            int now = Find.TickManager.TicksGame;
            if (secondWindUsed.TryGetValue(p.thingIDNumber, out int last) && now - last < GenDate.TicksPerDay) return;
            secondWindUsed[p.thingIDNumber] = now;

            aura.broken = false;
            aura.energy = aura.energyMax;
            var h = (Hediff_ArtifactTimed)HediffMaker.MakeHediff(ArtifactCommandDefOf.Avir_ArtifactSecondWind, p);
            h.ticksLeft = Mathf.RoundToInt(S.secondWindSeconds * 60f);
            h.level = 9;
            p.health.AddHediff(h);
            if (p.Spawned)
            {
                FleckMaker.Static(p.TrueCenter(), p.Map, FleckDefOf.PsycastAreaEffect, 1.5f);
                Messages.Message(p.LabelShort + " gets up again - second wind.", p, MessageTypeDefOf.PositiveEvent, false);
            }
        }
    }

    /// <summary>Second wind hooks the moment a pawn goes down. The method is private and its name
    /// is checked at startup: if it is missing, the patch is skipped instead of breaking Harmony.</summary>
    [HarmonyPatch]
    public static class Patch_ArtifactSecondWind
    {
        private static MethodBase target;
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> healthPawn =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        public static bool Prepare()
        {
            target = AccessTools.Method(typeof(Pawn_HealthTracker), "MakeDowned");
            if (target == null) Log.Warning("[Avir] Artifactorics: Pawn_HealthTracker.MakeDowned not found - second wind disabled.");
            return target != null;
        }

        public static MethodBase TargetMethod() => target;

        public static void Postfix(Pawn_HealthTracker __instance)
        {
            Pawn p = healthPawn(__instance);
            if (p == null || p.Dead || !p.Downed) return;
            ArtifactCommand.TrySecondWind(p);
        }
    }
}
