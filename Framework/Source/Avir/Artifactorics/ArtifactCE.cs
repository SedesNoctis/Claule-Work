using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    //  COMBAT EXTENDED LINK (stage 12e)
    //
    //  CE replaces the vanilla melee attack with its own: hit -> dodge -> parry -> riposte ->
    //  crit. Parry chance = clamp01(0.2 + defender MeleeParryChance - attacker MeleeParryChance x
    //  (1 + weapon MeleeCounterParryBonus)). Armour and penetration use CE's own scale.
    //  Nothing here references the CE assembly: every hook looks its target up by name and stays
    //  off when CE is not loaded.
    //    - Parry 3: + to the wielder's CE parry stat (our own melee parry roll is off under CE);
    //      the parry cells give CE parry too (Compat/CombatExtended patch).
    //    - Parry 7: every CE parry made with an artifact weapon strikes back.
    //    - Parry 9: deflecting shots stays ours.
    //    - Precision 3: CE's hit, dodge and parry checks never stop the first strike.
    //    - Piercing (edge, precision) multiplies the blow's own penetration instead of adding.
    //    - Mental combat 3: armour cuts the mental part by penetration / armour.
    //    - "Strongest strike" (dash, whirl, riposte, phantom reach) reads CE's tool penetration.
    // =====================================================================================

    public static class ArtifactCE
    {
        private static bool? active;
        private static FieldInfo toolSharp, toolBlunt;
        private static StatDef parryStat;

        public static bool Active
        {
            get
            {
                if (active == null)
                {
                    active = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE") != null;
                    if (active.Value)
                    {
                        Type tool = AccessTools.TypeByName("CombatExtended.ToolCE");
                        toolSharp = tool != null ? AccessTools.Field(tool, "armorPenetrationSharp") : null;
                        toolBlunt = tool != null ? AccessTools.Field(tool, "armorPenetrationBlunt") : null;
                    }
                }
                return active.Value;
            }
        }

        /// <summary>Armour penetration of a blow our own code deals: the vanilla share, or under CE its own scale
        /// (millimetres for sharp and heat, MPa for blunt) - CE reads a plain DamageInfo's penetration on its scale.</summary>
        public static float Pen(float vanilla, float ce) => Active ? ce : vanilla;

        public static StatDef ParryStat
        {
            get
            {
                if (parryStat == null && Active) parryStat = DefDatabase<StatDef>.GetNamedSilentFail("MeleeParryChance");
                return parryStat;
            }
        }

        /// <summary>CE penetration of a tool for this damage, or -1 when unknown.</summary>
        public static float ToolPenetration(Tool t, DamageDef def)
        {
            if (!Active || t == null) return -1f;
            bool sharp = def?.armorCategory == DamageArmorCategoryDefOf.Sharp;
            FieldInfo f = sharp ? toolSharp : toolBlunt;
            if (f == null || !f.DeclaringType.IsInstanceOfType(t)) return -1f;
            try { return Convert.ToSingle(f.GetValue(t)); }
            catch { return -1f; }
        }
    }

    /// <summary>Parry 3 under CE: a bonus to the wielder's CE parry stat.</summary>
    public class StatPart_ArtifactCEParry : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            float b = Bonus(req);
            if (b > 0f) val += b;
        }

        public override string ExplanationPart(StatRequest req)
        {
            float b = Bonus(req);
            return b > 0f ? "Artifact weapon (parry): +" + b.ToStringPercent() : null;
        }

        private static float Bonus(StatRequest req)
        {
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            if (s == null || !(req.Thing is Pawn p)) return 0f;
            if (ArtifactWeapon.Lvl(p, ArtifactWeaponDefOf.ArtifactArm_Parry, out float k) < 3) return 0f;
            return Mathf.Min(s.parryChanceMax, s.parryChance * k);
        }
    }

    [StaticConstructorOnStartup]
    public static class ArtifactCEStatInjector
    {
        static ArtifactCEStatInjector()
        {
            StatDef st = ArtifactCE.ParryStat;
            if (st == null) return;
            if (st.parts == null) st.parts = new List<StatPart>();
            if (!st.parts.Any(x => x is StatPart_ArtifactCEParry)) st.parts.Add(new StatPart_ArtifactCEParry { parentStat = st });
            Log.Message("[Avir] Artifactorics: linked to Combat Extended (parry, first strike, penetration).");
        }
    }

    // ------------------------------------------------------------------ hooks into CE's melee verb

    [HarmonyPatch]
    public static class Patch_ArtifactCE_HitChance
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            target = ArtifactCE.Active ? AccessTools.Method("CombatExtended.Verb_MeleeAttackCE:GetHitChance") : null;
            return target != null;
        }

        public static MethodBase TargetMethod() => target;

        public static void Postfix(Verb_MeleeAttack __instance, LocalTargetInfo __0, ref float __result)
        {
            if (Patch_ArtifactFirstStrike.Applies(__instance, __0)) __result = 1f;
        }
    }

    [HarmonyPatch]
    public static class Patch_ArtifactCE_DodgeChance
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            target = ArtifactCE.Active ? AccessTools.Method("CombatExtended.Verb_MeleeAttackCE:GetDodgeChance") : null;
            return target != null;
        }

        public static MethodBase TargetMethod() => target;

        public static void Postfix(Verb_MeleeAttack __instance, Pawn __0, ref float __result)
        {
            if (Patch_ArtifactFirstStrike.Applies(__instance, __0)) __result = 0f;
        }
    }

    [HarmonyPatch]
    public static class Patch_ArtifactCE_CanDoParry
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            target = ArtifactCE.Active ? AccessTools.Method("CombatExtended.Verb_MeleeAttackCE:CanDoParry") : null;
            return target != null;
        }

        public static MethodBase TargetMethod() => target;

        public static void Postfix(Verb_MeleeAttack __instance, Pawn __0, ref bool __result)
        {
            if (__result && Patch_ArtifactFirstStrike.Applies(__instance, __0)) __result = false;
        }
    }

    /// <summary>Parry 7: a CE parry made with an artifact weapon strikes back.</summary>
    [HarmonyPatch]
    public static class Patch_ArtifactCE_DoParry
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            target = ArtifactCE.Active ? AccessTools.Method("CombatExtended.Verb_MeleeAttackCE:DoParry") : null;
            return target != null;
        }

        public static MethodBase TargetMethod() => target;

        public static void Postfix(Verb_MeleeAttack __instance, Pawn __0, bool __2)
        {
            Pawn attacker = __instance?.CasterPawn;
            Pawn defender = __0;
            if (__2 || attacker == null || defender == null || attacker.Dead || defender.Dead || defender.Downed) return;
            if (ArtifactWeapon.Lvl(defender, ArtifactWeaponDefOf.ArtifactArm_Parry, out _) < 7) return;
            ThingWithComps w = defender.equipment?.Primary;
            if (w?.Socket()?.inlaid == null) return;
            if (!ArtifactWeapon.BestStrike(defender, w, out DamageDef d, out float dmg, out float pen)) return;
            ArtifactWeapon.Text(defender, "Riposte", new Color(0.9f, 0.85f, 0.6f));
            ArtifactWeapon.Extra(attacker, d, dmg, pen, defender, w.def);
        }
    }
}
