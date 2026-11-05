using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    // =====================================================================================
    //  STAGE 12b - WEAPON MILESTONES
    //
    //  Everything runs from three hooks:
    //    - Pawn.PreApplyDamage (prefix): the wielder's blow is shaped (piercing, extra damage,
    //      the mental part) and the defender's own milestones answer (parry, rampart, venom).
    //    - Pawn.PostApplyDamage (postfix): what follows a landed blow (stagger, echo, healing...).
    //    - Pawn.Kill (postfix): kills (storm of blades, silent kill, feast, necrosis).
    //  A "blow" is damage whose instigator wields an inlaid melee weapon and whose weapon is that
    //  weapon. Secondary hits made here (echo, cleave, riposte, interception) are plain damage:
    //  they never start effects of their own.
    //  Numbers: ArtifactoricsSettings.xml. Chances and amounts x arm strength, radii x sqrt.
    // =====================================================================================

    [DefOf]
    public static class ArtifactWeaponDefOf
    {
        public static ArtifactArmDef ArtifactArm_Edge;
        public static ArtifactArmDef ArtifactArm_Crush;
        public static ArtifactArmDef ArtifactArm_Resonance;
        public static ArtifactArmDef ArtifactArm_MentalCombat;
        public static ArtifactArmDef ArtifactArm_Tempo;
        public static ArtifactArmDef ArtifactArm_Precision;
        public static ArtifactArmDef ArtifactArm_SneakStrike;
        public static ArtifactArmDef ArtifactArm_BattleRage;
        public static ArtifactArmDef ArtifactArm_Parry;
        public static ArtifactArmDef ArtifactArm_Rampart;
        public static ArtifactArmDef ArtifactArm_Thirst;
        public static ArtifactArmDef ArtifactArm_Venom;

        public static HediffDef Avir_MentalWound;
        public static HediffDef Avir_ArtifactVenom;
        public static HediffDef Avir_ArtifactDeepCut;
        public static HediffDef Avir_ArtifactVanish;
        public static HediffDef Avir_PhantomReach;

        static ArtifactWeaponDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactWeaponDefOf)); }
    }

    [DefOf]
    public static class ArtifactWeaponAbilityDefOf
    {
        public static AbilityDef Avir_PhantomReach;

        static ArtifactWeaponAbilityDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactWeaponAbilityDefOf)); }
    }

    /// <summary>Short-lived combat state of one wielder (not saved: it lasts seconds).</summary>
    public class WeaponCombatState
    {
        public int lastTargetId = -1;
        public int sameTargetHits;
        public int lastHitTick = -99999;
        public int totalHits;
        public int momentumHits;
        public int stormUntil = -1;
        public int flurryUntil = -1;
        public int rage;
        public int lastCombatTick = -99999;
        public bool frenzy;
        public readonly HashSet<int> struck = new HashSet<int>();
    }

    public static class ArtifactWeapon
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        private static readonly Dictionary<int, WeaponCombatState> states = new Dictionary<int, WeaponCombatState>();

        /// <summary>Above 0 while this code deals secondary damage: such hits start nothing.</summary>
        public static int extraDepth;

        public static WeaponCombatState State(Pawn p, bool create = true)
        {
            if (p == null) return null;
            if (!states.TryGetValue(p.thingIDNumber, out WeaponCombatState s) && create)
            {
                if (states.Count > 2000) states.Clear();
                s = new WeaponCombatState();
                states[p.thingIDNumber] = s;
            }
            return s;
        }

        public static Dictionary<int, WeaponCombatState> AllStates => states;

        public static int Lvl(Pawn p, ArtifactArmDef arm, out float k)
        {
            k = 0f;
            if (p == null || arm == null) return 0;
            int l = ArtifactMilestones.Level(p, arm, out k);
            if (l <= 0) k = 0f;
            return l;
        }

        public static float Sqrt(float k) => Mathf.Sqrt(Mathf.Max(0f, k));

        /// <summary>Is this damage a blow of a wielded artifact weapon?</summary>
        public static bool IsBlow(DamageInfo d, out Pawn attacker)
        {
            attacker = d.Instigator as Pawn;
            if (attacker == null || extraDepth > 0 || d.Def == null) return false;
            if (d.Def.isRanged || d.Def.isExplosive) return false;
            ThingWithComps w = attacker.equipment?.Primary;
            if (w == null || d.Weapon != w.def) return false;
            CompArtifactSocket s = w.Socket();
            return s?.inlaid != null && s.inlaid.HasHeart;
        }

        public static bool IsMeleeFrom(DamageInfo d, out Pawn attacker)
        {
            attacker = d.Instigator as Pawn;
            return attacker != null && d.Def != null && !d.Def.isRanged && !d.Def.isExplosive;
        }

        /// <summary>Deals plain secondary damage that starts no effects of its own.</summary>
        public static void Extra(Thing victim, DamageDef def, float amount, float pen, Pawn by, ThingDef weapon = null)
        {
            if (victim == null || victim.Destroyed || amount <= 0f || def == null) return;
            extraDepth++;
            try { victim.TakeDamage(new DamageInfo(def, amount, pen, -1f, by, null, weapon)); }
            finally { extraDepth--; }
        }

        public static void Stun(Pawn v, Thing by, float seconds)
        {
            if (v == null || v.Dead || !v.Spawned || v.stances?.stunner == null || seconds <= 0f) return;
            v.stances.stunner.StunFor(Mathf.RoundToInt(seconds * 60f), by, false, true);
        }

        public static void Text(Thing t, string s, Color c)
        {
            if (t?.Spawned == true) MoteMaker.ThrowText(t.DrawPos, t.Map, s, c, 1.9f);
        }

        /// <summary>The weapon's strongest strike: damage type, amount and armour penetration.</summary>
        public static bool BestStrike(Pawn p, ThingWithComps w, out DamageDef def, out float dmg, out float pen)
        {
            def = null; dmg = 0f; pen = 0f;
            if (w?.def?.tools == null) return false;
            foreach (Tool t in w.def.tools)
            {
                ManeuverDef m = t.Maneuvers.FirstOrDefault();
                DamageDef dd = m?.verb?.meleeDamageDef;
                if (dd == null) continue;
                float a = t.AdjustedBaseMeleeDamageAmount(w, dd);
                if (a <= dmg) continue;
                dmg = a; def = dd;
                float ce = ArtifactCE.ToolPenetration(t, dd);   // Combat Extended keeps its own penetration
                pen = ce >= 0f ? ce : t.armorPenetration >= 0f ? t.armorPenetration : a * 0.015f;
            }
            if (def == null) return false;
            dmg *= p.GetStatValue(StatDefOf.MeleeDamageFactor);
            return true;
        }

        public static void MarkCombat(Pawn p)
        {
            WeaponCombatState s = State(p);
            s.lastCombatTick = Find.TickManager.TicksGame;
        }

        public static void AddRage(Pawn p)
        {
            if (Lvl(p, ArtifactWeaponDefOf.ArtifactArm_BattleRage, out _) < 3) return;
            WeaponCombatState s = State(p);
            s.rage = Mathf.Min(S.rageMaxStacks, s.rage + 1);
            s.lastCombatTick = Find.TickManager.TicksGame;
        }

        /// <summary>Heals injuries by `amount` hit points, worst first.</summary>
        public static void HealInjuries(Pawn p, float amount)
        {
            if (p?.health?.hediffSet == null || amount <= 0f) return;
            List<Hediff> hs = p.health.hediffSet.hediffs;
            for (int i = hs.Count - 1; i >= 0 && amount > 0f; i--)
            {
                if (!(hs[i] is Hediff_Injury inj) || inj.IsPermanent()) continue;
                float h = Mathf.Min(amount, inj.Severity);
                inj.Heal(h);
                amount -= h;
            }
        }

        /// <summary>Mends worn armour: `hp` points to the most damaged piece, or a share of each.</summary>
        public static void MendMostDamaged(Pawn p, int hp)
        {
            Apparel worst = null; float frac = 1f;
            foreach (Apparel a in p.apparel?.WornApparel ?? Enumerable.Empty<Apparel>())
            {
                if (!a.def.useHitPoints || a.HitPoints >= a.MaxHitPoints) continue;
                float f = (float)a.HitPoints / a.MaxHitPoints;
                if (f < frac) { frac = f; worst = a; }
            }
            if (worst != null) worst.HitPoints = Mathf.Min(worst.MaxHitPoints, worst.HitPoints + hp);
        }

        public static void MendAllShare(Pawn p, float share)
        {
            foreach (Apparel a in p.apparel?.WornApparel ?? Enumerable.Empty<Apparel>())
            {
                if (!a.def.useHitPoints || a.HitPoints >= a.MaxHitPoints) continue;
                a.HitPoints = Mathf.Min(a.MaxHitPoints, a.HitPoints + Mathf.Max(1, Mathf.RoundToInt(a.MaxHitPoints * share)));
            }
        }

        /// <summary>Armour the mental part meets at Mental combat 3: the best armour over the torso.</summary>
        public static float MentalArmorFactor(Pawn victim, DamageInfo d)
        {
            StatDef st = d.Def.armorCategory?.armorRatingStat;
            if (st == null || victim.apparel == null) return 1f;
            BodyPartRecord core = victim.RaceProps.body.corePart;
            float best = victim.GetStatValue(st);
            foreach (Apparel a in victim.apparel.WornApparel)
                if (a.def.apparel.CoversBodyPart(core)) best = Mathf.Max(best, a.GetStatValue(st));
            if (ArtifactCE.Active)
            {
                // CE scale: armour and penetration are both large numbers - compare them as a ratio
                if (best <= 0.001f) return 1f;
                return Mathf.Clamp(d.ArmorPenetrationInt / best, S.mentalArmorFloor, 1f);
            }
            float eff = Mathf.Max(0f, best - d.ArmorPenetrationInt);
            return Mathf.Clamp(1f - eff, S.mentalArmorFloor, 1f);
        }

        public static void AddMentalWound(Pawn victim, float amount, Pawn by)
        {
            if (amount <= 0f || victim?.health == null) return;
            Hediff h = victim.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponDefOf.Avir_MentalWound);
            if (h == null)
            {
                h = HediffMaker.MakeHediff(ArtifactWeaponDefOf.Avir_MentalWound, victim);
                h.Severity = amount;
                victim.health.AddHediff(h);
            }
            else h.Severity += amount;
            victim.health.hediffSet.DirtyPainCache();
            if (victim.Spawned) FleckMaker.ThrowMicroSparks(victim.DrawPos, victim.Map);
        }

        public static void ApplyVenom(Pawn victim, Pawn by, int lvl, float k)
        {
            ApplyVenomRaw(victim, S.venomBuildup * k, lvl >= 7 ? S.numbSlow * k : 0f,
                lvl >= 9 ? S.necrosisBonus * k : 0f, lvl >= 9 ? S.necrosisRadius * Sqrt(k) : 0f, lvl, k);
        }

        public static void ApplyVenomRaw(Pawn victim, float buildup, float slow, float necrosis, float spread, int lvl, float k)
        {
            if (victim == null || victim.Dead || victim.health == null) return;
            float res = Mathf.Clamp01(victim.GetStatValue(StatDefOf.ToxicResistance));
            float amt = buildup * (1f - res);
            if (amt > 0f) HealthUtility.AdjustSeverity(victim, HediffDefOf.ToxicBuildup, amt);
            var v = victim.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponDefOf.Avir_ArtifactVenom) as Hediff_ArtifactVenom;
            if (v == null)
            {
                v = (Hediff_ArtifactVenom)HediffMaker.MakeHediff(ArtifactWeaponDefOf.Avir_ArtifactVenom, victim);
                victim.health.AddHediff(v);
            }
            v.ticksLeft = Mathf.RoundToInt(S.venomSeconds * 60f);
            v.slow = Mathf.Max(v.slow, Mathf.Min(0.5f, slow));
            v.necrosis = Mathf.Max(v.necrosis, necrosis);
            v.spreadRadius = Mathf.Max(v.spreadRadius, spread);
            v.level = Mathf.Max(v.level, lvl);
            v.strength = Mathf.Max(v.strength, k);
            v.Refresh();
        }

        public static float ReachOf(Pawn p)
        {
            if (Lvl(p, ArtifactWeaponDefOf.ArtifactArm_MentalCombat, out float k) < 9) return 0f;
            float sens = ArtifactFocus.Sensitivity(p);
            return Mathf.Min(S.mentalReachMax, S.mentalReachBase * k * sens);
        }

        /// <summary>Mental combat 9: the blade's illusion strikes `target` wherever it stands.</summary>
        public static bool PhantomStrike(Pawn p, Pawn target)
        {
            ThingWithComps w = p.equipment?.Primary;
            if (target == null || target.Dead || !target.Spawned || target.Map != p.Map) return false;
            if (!BestStrike(p, w, out DamageDef def, out float dmg, out float pen)) return false;
            if (!ArtifactCharge.Spend(p, ArtifactWeaponDefOf.ArtifactArm_MentalCombat, S.mentalReachCharge, true)) return false;
            FleckMaker.Static(target.TrueCenter(), target.Map, FleckDefOf.PsycastAreaEffect, 1.2f);
            FleckMaker.Static(p.TrueCenter(), p.Map, FleckDefOf.PsycastAreaEffect, 0.7f);
            target.TakeDamage(new DamageInfo(def, dmg, pen, -1f, p, null, w.def));
            MarkCombat(p);
            return true;
        }
    }

    // ------------------------------------------------------------------ hooks

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_ArtifactWeaponPre
    {
        private static readonly AccessTools.StructFieldRef<DamageInfo, float> penRef =
            AccessTools.StructFieldRefAccess<DamageInfo, float>("armorPenetrationInt");

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        /// <summary>What the postfix needs from this blow.</summary>
        public static Pawn pendingVictim;
        public static float pendingMental;
        public static float pendingBleedBefore;
        public static bool pendingAssassin;

        [HarmonyPriority(Priority.High + 10)]
        public static bool Prefix(Pawn __instance, ref DamageInfo __0, ref bool __1)
        {
            pendingVictim = null;
            if (S == null || __1 || __instance?.health == null || __0.Def == null) return true;
            Pawn victim = __instance;
            if (!__0.Def.ExternalViolenceFor(victim)) return true;

            // ---------------- the defender's own milestones ----------------
            if (ArtifactWeapon.extraDepth == 0 && __0.Instigator is Pawn foe && foe != victim && !victim.Downed)
            {
                int parry = ArtifactWeapon.Lvl(victim, ArtifactWeaponDefOf.ArtifactArm_Parry, out float pk);
                if (parry >= 3)
                {
                    // under Combat Extended melee parrying is CE's own (see ArtifactCE); only shots stay ours
                    bool melee = !ArtifactCE.Active && !__0.Def.isRanged && !__0.Def.isExplosive && foe.Spawned && victim.Spawned
                                 && foe.Position.AdjacentTo8WayOrInside(victim.Position);
                    bool shot = parry >= 9 && __0.Def.isRanged && !__0.Def.isExplosive;
                    float chance = melee ? Mathf.Min(S.parryChanceMax, S.parryChance * pk)
                                 : shot ? Mathf.Min(S.deflectChanceMax, S.deflectChance * pk) : 0f;
                    if (chance > 0f && Rand.Chance(chance))
                    {
                        ArtifactWeapon.Text(victim, melee ? "Parried" : "Deflected", new Color(0.9f, 0.85f, 0.6f));
                        if (victim.Spawned) FleckMaker.ThrowMicroSparks(victim.DrawPos, victim.Map);
                        ArtifactWeapon.MarkCombat(victim);
                        if (melee && parry >= 7 && ArtifactWeapon.BestStrike(victim, victim.equipment?.Primary, out DamageDef rd, out float ra, out float rp))
                            ArtifactWeapon.Extra(foe, rd, ra, rp, victim, victim.equipment.Primary.def);   // riposte
                        __1 = true;
                        return false;
                    }
                }
            }

            // Rampart 3 / 9 near the victim
            if (ArtifactWeapon.extraDepth == 0 && victim.Faction != null && victim.Spawned)
                MapComponent_ArtifactWeapon.ApplyRampart(victim, ref __0);

            // Venom 9 - necrosis: every wound on a poisoned body is worse
            var venom = victim.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponDefOf.Avir_ArtifactVenom) as Hediff_ArtifactVenom;
            if (venom != null && venom.necrosis > 0f) __0.SetAmount(__0.Amount * (1f + venom.necrosis));

            // ---------------- the wielder's blow ----------------
            if (!ArtifactWeapon.IsBlow(__0, out Pawn a)) return true;
            WeaponCombatState st = ArtifactWeapon.State(a);
            int now = Find.TickManager.TicksGame;
            st.lastCombatTick = now;
            float mult = 1f, pen = 0f;
            bool ignoreArmor = false;

            int edge = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Edge, out float ek);
            if (edge >= 3) pen += S.edgePierce[edge >= 7 && S.edgePierce.Count > 1 ? 1 : 0] * ek;
            if (edge >= 9 && Rand.Chance(Mathf.Min(0.5f, S.edgeSeverChance * ek)))
            {
                mult *= 2f; ignoreArmor = true;
                ArtifactWeapon.Text(victim, "Severed", new Color(1f, 0.55f, 0.45f));
            }

            int prec = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Precision, out float pk2);
            if (prec >= 7) { pen += S.precisionPierce * pk2; mult *= 1f + S.precisionDamage * pk2; }
            if (prec >= 9 && !ignoreArmor && Rand.Chance(Mathf.Min(0.5f, S.precisionPerfectChance * pk2)))
            {
                mult *= 2f; ignoreArmor = true;
                ArtifactWeapon.Text(victim, "Perfect strike", new Color(1f, 0.9f, 0.5f));
            }

            int sneak = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_SneakStrike, out float sk);
            bool assassin = false;
            if (sneak >= 3 && !st.struck.Contains(victim.thingIDNumber))
            {
                bool unaware = victim.mindState?.enemyTarget != a && (victim.CurJob == null || !victim.CurJob.AnyTargetIs(a));
                if (unaware) mult *= 1f + S.sneakFirstBlood * sk;
            }
            if (sneak >= 9 && a.IsPsychologicallyInvisible())
            {
                mult *= 1f + S.sneakAssassinBonus * sk;
                assassin = true;
            }

            int rage = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_BattleRage, out float rk);
            if (rage >= 3 && st.rage > 0) mult *= 1f + st.rage * S.rageStackDamage * rk;

            if (mult != 1f) __0.SetAmount(__0.Amount * mult);
            if (pen > 0f)
                penRef(ref __0) = ArtifactCE.Active ? __0.ArmorPenetrationInt * (1f + pen)   // CE: share of the blow's own penetration
                                                    : __0.ArmorPenetrationInt + pen;
            if (ignoreArmor) __0.SetIgnoreArmor(true);

            // Mental combat: part of the blow goes to the mind instead (not against the psychically deaf)
            float mental = 0f;
            int mind = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_MentalCombat, out _);
            if (mind >= 3 && (victim.RaceProps.Humanlike || victim.RaceProps.Animal)
                && ArtifactFocus.Sensitivity(victim) > 0.01f)
            {
                mental = __0.Amount * S.mentalShare;
                __0.SetAmount(__0.Amount - mental);
                if (mind < 7) mental *= ArtifactWeapon.MentalArmorFactor(victim, __0);
            }

            pendingVictim = victim;
            pendingMental = mental;
            pendingAssassin = assassin;
            pendingBleedBefore = edge >= 7 ? victim.health.hediffSet.BleedRateTotal : -1f;
            st.struck.Add(victim.thingIDNumber);
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_ArtifactWeaponPost
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public static void Postfix(Pawn __instance, DamageInfo __0, float __1)
        {
            if (S == null || __instance == null) return;
            Pawn victim = __instance;

            // Battle rage: blows taken feed the rage too
            if (ArtifactWeapon.extraDepth == 0 && __0.Instigator is Pawn && __0.Def != null && __0.Def.ExternalViolenceFor(victim))
                ArtifactWeapon.AddRage(victim);

            if (Patch_ArtifactWeaponPre.pendingVictim != victim) return;
            Patch_ArtifactWeaponPre.pendingVictim = null;
            if (!ArtifactWeapon.IsBlow(__0, out Pawn a)) return;
            float mental = Patch_ArtifactWeaponPre.pendingMental;
            float bleedBefore = Patch_ArtifactWeaponPre.pendingBleedBefore;
            bool assassin = Patch_ArtifactWeaponPre.pendingAssassin;
            float dealt = __1 + mental;
            WeaponCombatState st = ArtifactWeapon.State(a);
            int now = Find.TickManager.TicksGame;

            // same-target streak (resonance, momentum)
            if (st.lastTargetId != victim.thingIDNumber || now - st.lastHitTick > S.tempoMomentumResetSeconds * 60f)
            {
                st.lastTargetId = victim.thingIDNumber;
                st.sameTargetHits = 0;
                st.momentumHits = 0;
            }
            st.sameTargetHits++;
            st.momentumHits++;
            st.totalHits++;
            st.lastHitTick = now;
            ArtifactWeapon.AddRage(a);

            // ---- Mental combat ----
            int mind = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_MentalCombat, out float mk);
            if (mental > 0f && !victim.Dead)
            {
                ArtifactWeapon.AddMentalWound(victim, mental, a);
                if (mind >= 7 && victim.psychicEntropy != null)
                    victim.psychicEntropy.OffsetPsyfocusDirectly(-mental * S.mentalPsyfocusPerDamage * mk);
            }

            // ---- Edge 7: deep cuts ----
            int edge = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Edge, out float ek);
            if (edge >= 7 && bleedBefore >= 0f && !victim.Dead)
            {
                float extra = (victim.health.hediffSet.BleedRateTotal - bleedBefore) * S.edgeBleedExtra * ek;
                if (extra > 0.0001f)
                {
                    Hediff dc = victim.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponDefOf.Avir_ArtifactDeepCut);
                    if (dc == null)
                    {
                        dc = HediffMaker.MakeHediff(ArtifactWeaponDefOf.Avir_ArtifactDeepCut, victim);
                        dc.Severity = extra;
                        victim.health.AddHediff(dc);
                    }
                    else dc.Severity += extra;
                    victim.health.hediffSet.DirtyCache();
                }
            }

            // ---- Crush ----
            int crush = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Crush, out float ck);
            if (crush >= 3 && !victim.Dead && victim.stances?.stagger != null)
                victim.stances.stagger.StaggerFor(Mathf.RoundToInt(S.crushStaggerSeconds * 60f * ck), 0.5f);
            if (crush >= 7 && victim.apparel != null && victim.apparel.WornApparel.Count > 0 && dealt > 0f)
            {
                BodyPartRecord core = victim.RaceProps.body.corePart;
                Apparel hit = victim.apparel.WornApparel.Where(x => x.def.apparel.CoversBodyPart(core)).RandomElementWithFallback()
                              ?? victim.apparel.WornApparel.RandomElement();
                ArtifactWeapon.Extra(hit, DamageDefOf.Blunt, dealt * S.crushArmorShare * ck, 0f, a);
            }
            if (crush >= 9 && S.crushShockEvery > 0 && st.totalHits % S.crushShockEvery == 0 && victim.MapHeld != null)
            {
                Map map = victim.MapHeld;
                IntVec3 c = victim.PositionHeld;
                float r = S.crushShockRadius * ArtifactWeapon.Sqrt(ck);
                FleckMaker.Static(c.ToVector3Shifted(), map, FleckDefOf.PsycastAreaEffect, r * 1.4f);
                foreach (Pawn e in GenRadial.RadialDistinctThingsAround(c, map, r, true).OfType<Pawn>().ToList())
                    if (e != a && e.HostileTo(a)) ArtifactWeapon.Stun(e, a, S.crushShockStunSeconds * ck);
            }

            // ---- Resonance ----
            int res = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Resonance, out float rk);
            if (res >= 3 && S.resonanceEvery > 0 && st.sameTargetHits % S.resonanceEvery == 0 && dealt > 0f)
            {
                float echo = dealt * S.resonanceEchoShare * rk;
                var targets = new List<Pawn> { victim };
                if (res >= 7 && victim.MapHeld != null)
                {
                    float r = S.resonanceJumpRadius * ArtifactWeapon.Sqrt(rk);
                    Pawn next = GenRadial.RadialDistinctThingsAround(victim.PositionHeld, victim.MapHeld, r, true).OfType<Pawn>()
                        .Where(e => e != victim && e != a && !e.Dead && e.HostileTo(a))
                        .OrderBy(e => e.Position.DistanceToSquared(victim.PositionHeld)).FirstOrDefault();
                    if (next != null) targets.Add(next);
                }
                foreach (Pawn t in targets)
                {
                    if (t.Spawned) FleckMaker.ThrowMicroSparks(t.DrawPos, t.Map);
                    if (!t.Dead) ArtifactWeapon.Extra(t, DamageDefOf.Blunt, echo, 0f, a);
                    if (res >= 9 && !t.Dead)
                    {
                        ArtifactWeapon.Stun(t, a, S.resonanceStunSeconds * rk);
                        ArtifactWeapon.Extra(t, DamageDefOf.EMP, S.resonanceEmpDamage * rk, 0f, a);
                    }
                }
                ArtifactWeapon.Text(victim, "Echo", new Color(0.7f, 0.85f, 1f));
            }

            // ---- Tempo ----
            int tempo = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Tempo, out float tk);
            if (tempo >= 3 && Rand.Chance(Mathf.Min(S.tempoFlurryMax, S.tempoFlurryChance * tk))) st.flurryUntil = now + 2;

            // ---- Sneak strike 9 / vanish ends with the blow ----
            if (assassin && !victim.Dead) ArtifactWeapon.Stun(victim, a, S.sneakAssassinStunSeconds);
            if (a.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponDefOf.Avir_ArtifactVanish) is Hediff_WeaponTimed van) van.ticksLeft = 0;

            // ---- Battle rage 9: wrath ----
            int rage = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_BattleRage, out float bk);
            if (rage >= 9 && st.rage >= S.rageMaxStacks && victim.MapHeld != null && dealt > 0f)
            {
                foreach (Pawn e in GenRadial.RadialDistinctThingsAround(victim.PositionHeld, victim.MapHeld, 1.5f, true).OfType<Pawn>().ToList())
                    if (e != victim && e != a && !e.Dead && e.HostileTo(a))
                        ArtifactWeapon.Extra(e, __0.Def, dealt * S.wrathShare, __0.ArmorPenetrationInt, a, __0.Weapon);
            }

            // ---- Thirst ----
            int thirst = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Thirst, out float hk);
            if (thirst >= 3 && dealt > 0f)
            {
                float heal = dealt * S.thirstLifesteal * hk;
                if (thirst >= 7 && a.health.summaryHealth.SummaryHealthPercent < 0.5f) heal *= 2f;
                ArtifactWeapon.HealInjuries(a, heal);
                ArtifactWeapon.MendMostDamaged(a, Mathf.Max(1, Mathf.RoundToInt(S.thirstArmorMendPerHit * hk)));
            }

            // ---- Venom ----
            int ven = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Venom, out float vk);
            if (ven >= 3 && !victim.Dead) ArtifactWeapon.ApplyVenom(victim, a, ven, vk);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_ArtifactWeaponKill
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public static void Prefix(Pawn __instance, out IntVec3 __state)
        {
            __state = __instance?.PositionHeld ?? IntVec3.Invalid;
        }

        public static void Postfix(Pawn __instance, DamageInfo? __0, IntVec3 __state)
        {
            if (S == null || __0 == null || !ArtifactWeapon.IsBlow(__0.Value, out Pawn a) || a.Dead) return;
            Pawn victim = __instance;
            int now = Find.TickManager.TicksGame;
            WeaponCombatState st = ArtifactWeapon.State(a);

            // Tempo 9 - storm of blades
            if (ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Tempo, out float tk) >= 9)
            {
                st.flurryUntil = now + 2;
                st.stormUntil = now + Mathf.RoundToInt(S.tempoStormSeconds * 60f * tk);
            }

            // Sneak strike 7 - silent kill
            if (ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_SneakStrike, out float sk) >= 7 && a.health != null)
            {
                var h = a.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponDefOf.Avir_ArtifactVanish) as Hediff_WeaponTimed;
                if (h == null)
                {
                    h = (Hediff_WeaponTimed)HediffMaker.MakeHediff(ArtifactWeaponDefOf.Avir_ArtifactVanish, a);
                    a.health.AddHediff(h);
                }
                h.ticksLeft = Mathf.RoundToInt(S.sneakVanishSeconds * 60f * sk);
            }

            // Thirst 9 - feast
            if (ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Thirst, out float hk) >= 9)
            {
                ArtifactState w = a.equipment?.Primary?.Socket()?.inlaid;
                if (w != null) w.charge = Mathf.Min(1f, w.charge + S.feastCharge * hk);
                ArtifactWeapon.MendAllShare(a, S.feastArmorMend * hk);
                ArtifactWeapon.Text(a, "Feast", new Color(0.85f, 0.2f, 0.25f));
            }

            // Venom 9 - necrosis spreads from a poisoned body
            Map map = a.Map;
            int ven = ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Venom, out float vk);
            if (ven >= 9 && map != null && __state.IsValid)
            {
                float r = S.necrosisRadius * ArtifactWeapon.Sqrt(vk);
                FleckMaker.Static(__state.ToVector3Shifted(), map, FleckDefOf.PsycastAreaEffect, r);
                foreach (Pawn e in GenRadial.RadialDistinctThingsAround(__state, map, r, true).OfType<Pawn>().ToList())
                    if (e != victim && e != a && !e.Dead && e.HostileTo(a)) ArtifactWeapon.ApplyVenom(e, a, ven, vk);
            }
        }
    }

    /// <summary>Tempo / Precision 3: the first strike on a new target always lands.</summary>
    [HarmonyPatch(typeof(Verb_MeleeAttack), "GetNonMissChance")]
    public static class Patch_ArtifactFirstStrikeHit
    {
        public static void Postfix(Verb_MeleeAttack __instance, LocalTargetInfo __0, ref float __result)
        {
            if (Patch_ArtifactFirstStrike.Applies(__instance, __0)) __result = 1f;
        }
    }

    [HarmonyPatch(typeof(Verb_MeleeAttack), "GetDodgeChance")]
    public static class Patch_ArtifactFirstStrikeDodge
    {
        public static void Postfix(Verb_MeleeAttack __instance, LocalTargetInfo __0, ref float __result)
        {
            if (Patch_ArtifactFirstStrike.Applies(__instance, __0)) __result = 0f;
        }
    }

    public static class Patch_ArtifactFirstStrike
    {
        public static bool Applies(Verb_MeleeAttack verb, LocalTargetInfo target)
        {
            Pawn a = verb?.CasterPawn;
            if (a == null || !(target.Thing is Pawn t)) return false;
            ThingWithComps w = a.equipment?.Primary;
            if (w == null || verb.EquipmentSource != w) return false;
            if (ArtifactWeapon.Lvl(a, ArtifactWeaponDefOf.ArtifactArm_Precision, out _) < 3) return false;
            WeaponCombatState st = ArtifactWeapon.State(a, false);
            return st == null || !st.struck.Contains(t.thingIDNumber);
        }
    }

    /// <summary>Thirst 7 - vitality: the wielder bleeds at half the rate.</summary>
    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.BleedRateTotal), MethodType.Getter)]
    public static class Patch_ArtifactVitalityBleed
    {
        public static void Postfix(HediffSet __instance, ref float __result)
        {
            if (__result <= 0f || MapComponent_ArtifactWeapon.vitality.Count == 0) return;
            if (__instance.pawn != null && MapComponent_ArtifactWeapon.vitality.Contains(__instance.pawn))
                __result *= ArtifactoricsSettingsDef.Main?.vitalityBleedFactor ?? 0.5f;
        }
    }

    // ------------------------------------------------------------------ stats

    /// <summary>Momentum, storm of blades and frenzy: attack speed, move speed and pain.</summary>
    public class StatPart_ArtifactWeaponCombat : StatPart
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        private static bool Get(StatRequest req, out Pawn p, out WeaponCombatState st)
        {
            p = req.Thing as Pawn;
            st = p != null ? ArtifactWeapon.State(p, false) : null;
            return st != null && S != null;
        }

        private float Factor(Pawn p, WeaponCombatState st, out string why)
        {
            why = null;
            int now = Find.TickManager?.TicksGame ?? 0;
            float f = 1f;
            if (parentStat == StatDefOf.MeleeCooldownFactor)
            {
                int tempo = ArtifactWeapon.Lvl(p, ArtifactWeaponDefOf.ArtifactArm_Tempo, out float tk);
                if (tempo >= 7 && st.momentumHits > 1 && now - st.lastHitTick < S.tempoMomentumResetSeconds * 60f)
                {
                    float m = Mathf.Min(S.tempoMomentumMax * Mathf.Max(1f, tk), S.tempoMomentumPerHit * tk * (st.momentumHits - 1));
                    f *= Mathf.Max(0.3f, 1f - m);
                    why = "momentum";
                }
                if (st.stormUntil > now) { f /= 1f + S.tempoStormSpeed; why = "storm of blades"; }
                if (st.frenzy)
                {
                    ArtifactWeapon.Lvl(p, ArtifactWeaponDefOf.ArtifactArm_BattleRage, out float rk);
                    f /= 1f + S.frenzySpeed * rk;
                    why = "frenzy";
                }
            }
            else if (parentStat == StatDefOf.MoveSpeed)
            {
                if (st.stormUntil > now) { f *= 1f + S.tempoStormSpeed; why = "storm of blades"; }
            }
            return f;
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!Get(req, out Pawn p, out WeaponCombatState st)) return;
            if (parentStat == StatDefOf.PainShockThreshold)
            {
                if (st.frenzy) val += 10f;
                return;
            }
            val *= Factor(p, st, out _);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!Get(req, out Pawn p, out WeaponCombatState st)) return null;
            if (parentStat == StatDefOf.PainShockThreshold) return st.frenzy ? "Frenzy: pain does not drop the wielder" : null;
            float f = Factor(p, st, out string why);
            return why == null ? null : "Artifact weapon (" + why + "): x" + f.ToStringPercent();
        }
    }

    [StaticConstructorOnStartup]
    public static class ArtifactWeaponStatInjector
    {
        static ArtifactWeaponStatInjector()
        {
            foreach (StatDef s in new[] { StatDefOf.MeleeCooldownFactor, StatDefOf.MoveSpeed, StatDefOf.PainShockThreshold })
            {
                if (s.parts == null) s.parts = new List<StatPart>();
                if (!s.parts.Any(x => x is StatPart_ArtifactWeaponCombat)) s.parts.Add(new StatPart_ArtifactWeaponCombat { parentStat = s });
            }
        }
    }

    // ------------------------------------------------------------------ hediffs

    public class Hediff_MentalWound : HediffWithComps
    {
        private float sens = 1f;
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public override float PainOffset => Severity * (S?.mentalPainPerDamage ?? 0.01f) * sens;

        public override string LabelInBrackets => Severity.ToString("0");

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            sens = ArtifactFocus.Sensitivity(pawn);
        }

        public override void Tick()
        {
            base.Tick();
            if (!pawn.IsHashIntervalTick(60)) return;
            sens = ArtifactFocus.Sensitivity(pawn);
            int now = Find.TickManager.TicksGame;
            if (now - pawn.mindState?.lastHarmTick > 600)
            {
                Severity -= S?.mentalHealPerSecond ?? 2f;
                pawn.health.hediffSet.DirtyPainCache();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sens, "sens", 1f);
        }
    }

    public class Hediff_ArtifactDeepCut : HediffWithComps
    {
        public override float BleedRate => Severity;

        public override void Tick()
        {
            base.Tick();
            if (!pawn.IsHashIntervalTick(250)) return;
            // stops once nothing else bleeds (wounds tended); otherwise slowly closes
            float other = 0f;
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
                if (h != this) other += h.BleedRate;
            if (other <= 0.0001f) Severity = 0f;
            else Severity *= 0.97f;
            pawn.health.hediffSet.DirtyCache();
        }
    }

    public class Hediff_WeaponTimed : HediffWithComps
    {
        public int ticksLeft;

        public override bool ShouldRemove => base.ShouldRemove || ticksLeft <= 0;

        public override void Tick()
        {
            base.Tick();
            ticksLeft--;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksLeft, "ticksLeft");
        }
    }

    public class Hediff_ArtifactVenom : HediffWithComps
    {
        public int ticksLeft;
        public float slow, necrosis, spreadRadius, strength;
        public int level;
        private HediffStage stage;

        public override bool ShouldRemove => base.ShouldRemove || ticksLeft <= 0;

        public void Refresh()
        {
            stage = null;
        }

        public override HediffStage CurStage
        {
            get
            {
                if (stage == null)
                {
                    stage = new HediffStage { becomeVisible = true };
                    if (slow > 0f)
                    {
                        stage.statFactors = new List<StatModifier>
                        {
                            new StatModifier { stat = StatDefOf.MoveSpeed, value = 1f - slow },
                            new StatModifier { stat = StatDefOf.MeleeCooldownFactor, value = 1f + slow },
                        };
                    }
                }
                return stage;
            }
        }

        public override string LabelInBrackets => necrosis > 0f ? "necrosis" : slow > 0f ? "numbing" : null;

        public override void Tick()
        {
            base.Tick();
            ticksLeft--;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksLeft, "ticksLeft");
            Scribe_Values.Look(ref slow, "slow");
            Scribe_Values.Look(ref necrosis, "necrosis");
            Scribe_Values.Look(ref spreadRadius, "spreadRadius");
            Scribe_Values.Look(ref strength, "strength");
            Scribe_Values.Look(ref level, "level");
        }
    }

    /// <summary>Carries the phantom reach's autocast switch.</summary>
    public class Hediff_PhantomReach : HediffWithComps
    {
        public bool autocast = true;

        public override string LabelInBrackets => ArtifactWeapon.ReachOf(pawn).ToString("0.#") + " cells" + (autocast ? ", auto" : "");

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (pawn.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = "Auto phantom reach",
                defaultDesc = "Strike enemies through walls on its own while fighting (reach "
                              + ArtifactWeapon.ReachOf(pawn).ToString("0.#") + " cells). Enemies close enough for an ordinary blow are left to the blade.",
                icon = ContentFinder<Texture2D>.Get("UI/Artifactorics/Node_Purchased"),
                isActive = () => autocast,
                toggleAction = () => autocast = !autocast
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref autocast, "autocast", true);
        }
    }

    // ------------------------------------------------------------------ ability

    public class CompProperties_PhantomReach : CompProperties_AbilityEffect
    {
        public CompProperties_PhantomReach() { compClass = typeof(CompAbilityEffect_PhantomReach); }
    }

    public class CompAbilityEffect_PhantomReach : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent.pawn;
            if (!(target.Thing is Pawn t) || t == caster || t.Dead) return false;
            float reach = ArtifactWeapon.ReachOf(caster);
            if (t.Position.DistanceTo(caster.Position) > reach)
            {
                if (throwMessages) Messages.Message("Out of reach (" + reach.ToString("0.#") + " cells).", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (target.Thing is Pawn t) ArtifactWeapon.PhantomStrike(parent.pawn, t);
        }
    }

    // ------------------------------------------------------------------ per-map upkeep

    /// <summary>Rampart holders, vitality, frenzy, rage decay, flurries, the phantom reach and
    /// its autocast, and the rampart barrier (through the shield manager).</summary>
    public class MapComponent_ArtifactWeapon : MapComponent
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public struct Holder
        {
            public Pawn pawn;
            public int level;
            public float k;
        }

        public static readonly List<Holder> rampart = new List<Holder>();
        public static readonly HashSet<Pawn> vitality = new HashSet<Pawn>();

        public MapComponent_ArtifactWeapon(Map map) : base(map) { }

        public static void ApplyRampart(Pawn victim, ref DamageInfo d)
        {
            if (rampart.Count == 0) return;
            float best = 0f;
            Pawn interceptor = null;
            for (int i = 0; i < rampart.Count; i++)
            {
                Holder h = rampart[i];
                Pawn p = h.pawn;
                if (p.Map != victim.Map || p.Faction != victim.Faction || p.Dead || p.Downed) continue;
                float r = S.rampartRadius * ArtifactWeapon.Sqrt(h.k);
                float dist = p.Position.DistanceTo(victim.Position);
                if (dist <= r) best = Mathf.Max(best, Mathf.Min(S.rampartReductionMax, S.rampartReduction * h.k));
                if (h.level >= 9 && p != victim && dist <= 1.5f && interceptor == null) interceptor = p;
            }
            if (best > 0f) d.SetAmount(d.Amount * (1f - best));
            if (interceptor != null && d.Amount > 0f)
            {
                float moved = d.Amount * S.interceptShare;
                d.SetAmount(d.Amount - moved);
                ArtifactWeapon.Extra(interceptor, d.Def, moved, d.ArmorPenetrationInt, d.Instigator as Pawn);
            }
        }

        public override void MapComponentTick()
        {
            if (S == null) return;
            int now = Find.TickManager.TicksGame;

            // flurries: the cooldown after the blow is cut short
            foreach (KeyValuePair<int, WeaponCombatState> kv in ArtifactWeapon.AllStates)
            {
                if (kv.Value.flurryUntil < now) continue;
                Pawn p = FindPawn(kv.Key);
                if (p?.stances?.curStance is Stance_Cooldown c && c.ticksLeft > 1)
                {
                    c.ticksLeft = 1;
                    kv.Value.flurryUntil = -1;
                }
            }

            if (now % 60 != 0) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            rampart.RemoveAll(h => h.pawn.Map == map || h.pawn.Map == null || h.pawn.Destroyed);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                vitality.Remove(p);
                ThingWithComps w = p.equipment?.Primary;
                bool armed = w != null && w.Socket()?.inlaid != null;
                WeaponCombatState st = ArtifactWeapon.State(p, false);
                if (!armed)
                {
                    if (st != null) { st.frenzy = false; st.rage = 0; }
                    SyncReach(p, false);
                    continue;
                }

                int rl = ArtifactWeapon.Lvl(p, ArtifactWeaponDefOf.ArtifactArm_Rampart, out float rk);
                if (rl >= 3 && !p.Dead && !p.Downed) rampart.Add(new Holder { pawn = p, level = rl, k = rk });
                if (ArtifactWeapon.Lvl(p, ArtifactWeaponDefOf.ArtifactArm_Thirst, out _) >= 7) vitality.Add(p);

                if (st != null)
                {
                    // rage fades out of combat
                    if (st.rage > 0 && now - st.lastCombatTick > S.rageDecaySeconds * 60f) st.rage--;
                    st.frenzy = ArtifactWeapon.Lvl(p, ArtifactWeaponDefOf.ArtifactArm_BattleRage, out _) >= 7
                                && p.health.summaryHealth.SummaryHealthPercent < S.frenzyHealth;
                    if (now - st.lastCombatTick > 1200) st.struck.Clear();
                }

                bool reach = ArtifactWeapon.Lvl(p, ArtifactWeaponDefOf.ArtifactArm_MentalCombat, out _) >= 9;
                SyncReach(p, reach);
                if (reach) Autocast(p);
            }
        }

        private Pawn FindPawn(int id)
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i].thingIDNumber == id) return pawns[i];
            return null;
        }

        private static void SyncReach(Pawn p, bool want)
        {
            if (p.health == null) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponDefOf.Avir_PhantomReach);
            if (want && h == null) p.health.AddHediff(ArtifactWeaponDefOf.Avir_PhantomReach);
            else if (!want && h != null) p.health.RemoveHediff(h);
            if (p.abilities == null) return;
            bool has = p.abilities.GetAbility(ArtifactWeaponAbilityDefOf.Avir_PhantomReach) != null;
            if (want && !has) p.abilities.GainAbility(ArtifactWeaponAbilityDefOf.Avir_PhantomReach);
            else if (!want && has) p.abilities.RemoveAbility(ArtifactWeaponAbilityDefOf.Avir_PhantomReach);
        }

        private static void Autocast(Pawn p)
        {
            if (p.Dead || p.Downed || p.InMentalState && p.Faction == Faction.OfPlayer) return;
            if (p.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponDefOf.Avir_PhantomReach) is Hediff_PhantomReach hr
                && !hr.autocast && p.Faction == Faction.OfPlayer) return;
            if (!ArtifactCharge.InCombat(p) && !p.mindState.anyCloseHostilesRecently) return;
            Ability ab = p.abilities?.GetAbility(ArtifactWeaponAbilityDefOf.Avir_PhantomReach);
            if (ab == null || ab.OnCooldown) return;
            if (p.stances?.stunner != null && p.stances.stunner.Stunned) return;
            float reach = ArtifactWeapon.ReachOf(p);
            if (reach < 1.5f) return;
            Pawn best = null; float bestD = float.MaxValue;
            foreach (Pawn e in GenRadial.RadialDistinctThingsAround(p.Position, p.Map, reach, true).OfType<Pawn>())
            {
                if (e == p || e.Dead || e.Downed || !e.HostileTo(p)) continue;
                float dist = e.Position.DistanceTo(p.Position);
                if (dist <= 1.5f) continue;   // within blade reach: the ordinary blow handles it
                if (p.mindState.enemyTarget == e) { best = e; break; }
                if (dist < bestD) { bestD = dist; best = e; }
            }
            if (best == null) return;
            if (ArtifactWeapon.PhantomStrike(p, best)) ab.StartCooldown(ab.def.cooldownTicksRange.RandomInRange);
        }
    }
}
