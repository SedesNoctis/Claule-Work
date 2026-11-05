using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Artifactorics
{
    // =====================================================================================
    //  STAGE 12d - BURNING SPEAR
    //
    //  A weapon stone that works only in Avir spears (the list is in the ability's XML). Its
    //  energy is fixed when the stone is made and depends on the metal:
    //    essentium  - golden light (strong burns, sets on fire), healing flame (burns enemies,
    //                 heals allies it strikes)
    //    tisserium  - absorbing spear (burns, and what it takes heals the wielder or a wounded
    //                 ally nearby)
    //  Each energy has its own projectile and burst colour. Numbers are at power x1.
    // =====================================================================================

    public class SpearEnergyDef : Def
    {
        /// <summary>The stone metal this energy belongs to.</summary>
        public ArtifactMetalDef metal;
        public ThingDef projectile;
        public Color color = Color.white;
        public DamageDef damageDef;
        public float damage = 12f;
        public float armorPenetration = 0.2f;
        /// <summary>Chance (x power) to set the target on fire.</summary>
        public float igniteChance;
        public float fireSize = 0.4f;
        /// <summary>Extra burn hit (share of the damage) - golden light's strong burns.</summary>
        public float extraBurnShare;
        /// <summary>A non-hostile pawn struck is healed by this much (x power) instead of hurt.</summary>
        public float healAlly;
        /// <summary>Share of the damage dealt that heals the wielder, or a wounded ally near it.</summary>
        public float absorbShare;
        public float absorbAllyRadius = 8f;
        /// <summary>Moonflame (the A'Shaign): the mind of the one struck is clouded for this long (x power)...</summary>
        public HediffDef mindFogHediff;
        public float mindFogSeconds;
        /// <summary>...it loses its aim, and forgets who struck it: it lets go of the wielder as its target.</summary>
        public bool forgetStriker;
        public int uiOrder;

        public bool Heals => healAlly > 0f;

        public static IEnumerable<SpearEnergyDef> For(ArtifactMetalDef m)
        {
            return DefDatabase<SpearEnergyDef>.AllDefsListForReading.Where(e => e.metal == m).OrderBy(e => e.uiOrder);
        }

        public string NumbersText()
        {
            var parts = new List<string> { (damageDef?.label ?? "damage") + " " + damage.ToString("0") };
            if (igniteChance > 0f) parts.Add(igniteChance.ToStringPercent() + " to set on fire");
            if (extraBurnShare > 0f) parts.Add("+" + extraBurnShare.ToStringPercent() + " as a second burn");
            if (healAlly > 0f) parts.Add("allies struck are healed by " + healAlly.ToString("0"));
            if (mindFogSeconds > 0f) parts.Add("the mind of the one struck is clouded for " + mindFogSeconds.ToString("0.#") + " s");
            if (forgetStriker) parts.Add("it loses its aim and forgets who struck it");
            if (absorbShare > 0f) parts.Add(absorbShare.ToStringPercent() + " of the damage heals the wielder or a wounded ally");
            return string.Join(", ", parts);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (metal == null) yield return "no metal";
            if (projectile == null) yield return "no projectile";
            if (damageDef == null) yield return "no damageDef";
        }
    }

    [DefOf]
    public static class ArtifactSpearDefOf
    {
        public static ArtifactAbilityDef ArtifactAbility_BurningSpear;

        static ArtifactSpearDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactSpearDefOf)); }
    }

    public class CompAbilityEffect_BurningSpear : CompAbilityEffect_ArtifactStone
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (target.Thing == parent.pawn) return false;
            ActiveStone a = Source;
            if (a != null && a.stone.energy == null)
            {
                if (throwMessages) Messages.Message("This stone has no energy.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            SpearEnergyDef e = a.stone.energy;
            if (e?.projectile == null || caster.Map == null) return;
            var proj = (Projectile_SpearEnergy)GenSpawn.Spawn(e.projectile, caster.Position, caster.Map);
            proj.energy = e;
            proj.power = power;
            ThingWithComps w = caster.equipment?.Primary;
            proj.Launch(caster, caster.DrawPos, target, target, ProjectileHitFlags.IntendedTarget, false, w, null);
            SpawnBurst(caster.DrawPos, caster.Map, e.color, 0.8f);
        }

        public static void SpawnBurst(Vector3 at, Map map, Color c, float scale)
        {
            if (map == null) return;
            FleckCreationData d = FleckMaker.GetDataStatic(at, map, FleckDefOf.ExplosionFlash, scale);
            d.instanceColor = c;
            map.flecks.CreateFleck(d);
            FleckCreationData g = FleckMaker.GetDataStatic(at, map, FleckDefOf.PsycastAreaEffect, scale * 0.8f);
            g.instanceColor = c;
            map.flecks.CreateFleck(g);
        }
    }

    /// <summary>The energy bolt. Its damage and effects come from the energy and the stone's power.</summary>
    public class Projectile_SpearEnergy : Projectile
    {
        public SpearEnergyDef energy;
        public float power = 1f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref energy, "energy");
            Scribe_Values.Look(ref power, "power", 1f);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            Vector3 at = ExactPosition;
            Pawn caster = launcher as Pawn;
            // a shot of the glaive's spear fire: the energy and power come from the stone, weaker than the focused bolt
            if (energy == null && caster != null)
            {
                AbilityStoneState stone = SpearMode.Stone(caster.equipment?.Primary, out ArtifactState st);
                if (stone != null)
                {
                    energy = stone.energy;
                    power = ArtifactAbilities.Power(caster, st, stone) * (ArtifactoricsSettingsDef.Main?.spearShotPower ?? 0.5f);
                }
            }
            if (energy != null && map != null)
            {
                CompAbilityEffect_BurningSpear.SpawnBurst(at, map, energy.color, 1.6f);
                FleckMaker.ThrowMicroSparks(at, map);
                if (hitThing != null && !blockedByShield) Strike(hitThing, caster, map);
            }
            base.Impact(hitThing, blockedByShield);
        }

        public static float DamagePower(float power) => power <= 1f ? power : Mathf.Min(2.5f, 1f + Mathf.Log(power));

        private void Strike(Thing hit, Pawn caster, Map map)
        {
            Pawn victim = hit as Pawn;
            // healing flame: allies are mended, not burnt
            if (energy.Heals && victim != null && caster != null && !victim.HostileTo(caster))
            {
                ArtifactWeapon.HealInjuries(victim, energy.healAlly * power);
                MoteMaker.ThrowText(victim.DrawPos, map, "+" + (energy.healAlly * power).ToString("0"), energy.color, 2f);
                return;
            }
            // the stone's power multiplies quality, metal, sensitivity and development - left raw it reached x15 and a bolt
            // burnt through anything: damage now grows slowly past x1 and stops at x2.5
            float dmg = energy.damage * DamagePower(power);
            // no weapon on the hit: the bolt is not a blade blow, so the weapon milestones do not shape it
            var dinfo = new DamageInfo(energy.damageDef, dmg, energy.armorPenetration, ExactRotation.eulerAngles.y, caster, null, null);
            DamageWorker.DamageResult res = hit.TakeDamage(dinfo);
            float dealt = res?.totalDamageDealt ?? 0f;
            if (energy.extraBurnShare > 0f && !hit.Destroyed)
            {
                DamageWorker.DamageResult r2 = hit.TakeDamage(new DamageInfo(DamageDefOf.Burn, dmg * energy.extraBurnShare, energy.armorPenetration, -1f, caster, null, null));
                dealt += r2?.totalDamageDealt ?? 0f;
            }
            if (energy.igniteChance > 0f && !hit.Destroyed && Rand.Chance(Mathf.Min(0.9f, energy.igniteChance * power)))
                FireUtility.TryAttachFire(hit, energy.fireSize, caster);
            // moonflame: the war of the A'Shaign is waged in the mind and in secret
            if (victim != null && !victim.Dead && caster != null && victim.HostileTo(caster))
            {
                if (energy.mindFogHediff != null && energy.mindFogSeconds > 0f)
                    AmuletElder.GiveTimed(victim, energy.mindFogHediff, Mathf.RoundToInt(energy.mindFogSeconds * power * 60f));
                if (energy.forgetStriker)
                {
                    if (victim.stances?.curStance is Stance_Warmup) victim.stances.CancelBusyStanceSoft();
                    if (victim.mindState != null && victim.mindState.enemyTarget == caster) victim.mindState.enemyTarget = null;
                    Job j = victim.CurJob;
                    if (j != null && (j.targetA.Thing == caster || j.targetB.Thing == caster))
                        victim.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
            if (energy.absorbShare > 0f && dealt > 0f && caster != null && !caster.Dead)
            {
                Pawn heal = caster;
                if (!caster.health.hediffSet.hediffs.Any(h => h is Hediff_Injury inj && !inj.IsPermanent()))
                {
                    heal = caster.Map?.mapPawns.SpawnedPawnsInFaction(caster.Faction ?? Faction.OfPlayer)
                        .Where(p => p != caster && !p.Dead && p.Position.DistanceTo(caster.Position) <= energy.absorbAllyRadius
                                    && p.health.hediffSet.hediffs.Any(h => h is Hediff_Injury inj && !inj.IsPermanent()))
                        .OrderBy(p => p.health.summaryHealth.SummaryHealthPercent).FirstOrDefault();
                }
                if (heal != null)
                {
                    ArtifactWeapon.HealInjuries(heal, dealt * energy.absorbShare);
                    if (heal.Spawned) FleckMaker.ThrowMicroSparks(heal.DrawPos, heal.Map);
                }
            }
        }
    }

    // ================================================================== spear fire (ranged mode)

    /// <summary>The glaive's spear fire: while its artifact holds a burning spear stone, the mode is
    /// on and there is charge, the glaive is a ranged weapon - aimed and fired like any gun
    /// (Shooting, distance, cover, weather), each shot costing a little charge. Otherwise it is the
    /// melee weapon it always was (vanilla falls back to melee when the ranged verb is unavailable).
    /// Brawlers do not get the mode switched on by themselves.</summary>
    public static class SpearMode
    {
        public static AbilityStoneState Stone(ThingWithComps w, out ArtifactState st)
        {
            st = null;
            CompArtifactSocket sock = w?.Socket();
            ArtifactState s = sock?.inlaid;
            AbilityStoneState stone = s?.stone;
            ArtifactAbilityDef spear = ArtifactSpearDefOf.ArtifactAbility_BurningSpear;
            if (stone == null || stone.ability != spear || stone.energy == null || stone.dormant || !s.HasHeart) return null;
            if (!spear.onlyWeapons.NullOrEmpty() && !spear.onlyWeapons.Contains(w.def)) return null;
            st = s;
            return stone;
        }

        public static bool IsBrawler(Pawn p) => p?.story?.traits != null && p.story.traits.HasTrait(TraitDefOf.Brawler);

        public static bool IsOn(ThingWithComps w, Pawn p)
        {
            AbilityStoneState stone = Stone(w, out _);
            return stone != null && (stone.rangedModeSet ? stone.rangedMode : !IsBrawler(p));
        }

        public static bool CanFire(ThingWithComps w, Pawn p)
        {
            if (!IsOn(w, p)) return false;
            Stone(w, out ArtifactState st);
            return st != null && st.charge >= (ArtifactoricsSettingsDef.Main?.spearShotCharge ?? 0.0075f);
        }

        public static bool HasSpearVerb(ThingDef d) => d?.Verbs != null && d.Verbs.Any(v => v.verbClass == typeof(Verb_SpearFire));
    }

    /// <summary>The glaive's ranged verb (in its def). Works only in spear fire.</summary>
    public class Verb_SpearFire : Verb_Shoot
    {
        public override ThingDef Projectile => SpearMode.Stone(EquipmentSource, out _)?.energy?.projectile ?? base.Projectile;

        public override bool Available() => base.Available() && SpearMode.CanFire(EquipmentSource, CasterPawn);

        protected override bool TryCastShot()
        {
            bool shot = base.TryCastShot();
            if (shot && SpearMode.Stone(EquipmentSource, out ArtifactState st) != null)
            {
                st.charge = Mathf.Max(0f, st.charge - (ArtifactoricsSettingsDef.Main?.spearShotCharge ?? 0.0075f));
                if (st.charge <= 0f && CasterPawn != null) ArtifactMilestones.Invalidate(CasterPawn);
            }
            return shot;
        }
    }

    /// <summary>The switch, on the wielder's gizmos.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_SpearFireGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            if (__instance?.Faction != Faction.OfPlayer || !__instance.IsColonistPlayerControlled) yield break;
            ThingWithComps w = __instance.equipment?.Primary;
            AbilityStoneState stone = SpearMode.Stone(w, out ArtifactState st);
            if (stone == null || !SpearMode.HasSpearVerb(w.def)) yield break;
            Pawn p = __instance;
            yield return new Command_Toggle
            {
                defaultLabel = "Spear fire",
                defaultDesc = "The glaive fires " + stone.energy.label + " as a ranged weapon: aimed like a gun (Shooting), "
                              + (ArtifactoricsSettingsDef.Main?.spearShotCharge ?? 0.0075f).ToStringPercent("0.##") + " of the artifact's charge a shot. "
                              + "Off, or with no charge left, it is a melee weapon again. Charge now: " + st.charge.ToStringPercent() + ".",
                icon = ContentFinder<Texture2D>.Get("UI/Artifactorics/Abilities/BurningSpear", false) ?? BaseContent.BadTex,
                isActive = () => SpearMode.IsOn(w, p),
                toggleAction = () => { stone.rangedModeSet = true; stone.rangedMode = !SpearMode.IsOn(w, p); }
            };
        }
    }

    /// <summary>A brawler is unhappy with a glaive only while its spear fire is on.</summary>
    [HarmonyPatch(typeof(ThoughtWorker_IsCarryingRangedWeapon), "CurrentStateInternal")]
    public static class Patch_SpearFireBrawler
    {
        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            if (!__result.Active) return;
            ThingWithComps w = p?.equipment?.Primary;
            if (w != null && SpearMode.HasSpearVerb(w.def) && !SpearMode.IsOn(w, p)) __result = ThoughtState.Inactive;
        }
    }
}
