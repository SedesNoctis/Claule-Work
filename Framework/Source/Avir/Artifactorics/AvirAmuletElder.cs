using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // AVIR TRADITIONAL AMULET - stage 3: the elder caste (hunter, guard, warrior).
    //
    // A milestone works once its stone is opened, on ANY branch of the caste (the off-branches
    // just cost more). Chances and bonuses scale with the branch strength (1 + focus) x core
    // quality; the numbers below are at strength 1 and provisional.
    // The slot (ambush / anchor / rend) belongs to the amulet's own subclass only.
    //
    //   hunter   3 predator's sense - hostiles in the fog shown within 12 cells (+1 per later stone)
    //            9 the hunt         - auto mark on the first target; +15% damage to it
    //           12 silent strike    - first blow on a target not fighting the hunter: +40%
    //           15 driver           - the marked target moves 15% slower
    //           18 last strike      - killing the mark marks the nearest enemy and readies the next blow
    //            slot: ambush       - 3 s still -> invisible until attacking; that attack +50%
    //   guard    3 vigilance        - hostiles within 10 cells revealed, alarm message
    //            9 the clutch guarded - ally hit within 8 cells -> leap at the attacker (15 s rest)
    //           12 hold the line    - standing still 2 s: -15% damage taken (max -40%)
    //           15 warning blow     - first enemy to come adjacent is stunned 1.5 s (10 s rest)
    //           18 wall of claws    - leaps chain without rest, up to 4 within 5 s
    //            slot: anchor       - enemies within 3 cells nearly rooted for 5 s
    //   warrior  3 predator's leap  - melee target 3-6 cells away: leap instead of running (10 s)
    //            9 killing blow     - chance a melee blow strikes a vital part (grows with wounds)
    //           12 blood calls      - a kill: +30% speed for 3 s
    //           15 unstoppable      - cannot be stunned during a series
    //           18 hunting series   - melee kill -> leap to the next enemy within 8 cells and strike;
    //                                 +10% per link; breaks on a blow that does not kill or a hit of 25+
    //            slot: rend         - teleport-strike a target within 8 cells
    //
    //   A'Shaign   battle telekinesis - any warrior path at 9: no damage; small chances to stop a
    //            projectile, wrest a weapon, disarm a shooter, knock down (AvirAmuletTelekinesis.cs)
    // =====================================================================================

    [DefOf]
    public static class AvirAmuletElderDefOf
    {
        public static AmuletCasteDef Avir_Caste_Elder;
        public static AmuletSubclassDef Avir_Subclass_Hunter;
        public static AmuletSubclassDef Avir_Subclass_Guard;
        public static AmuletSubclassDef Avir_Subclass_Warrior;
        public static HediffDef Avir_AmuletHunted;
        public static HediffDef Avir_AmuletBloodCalls;
        public static HediffDef Avir_AmuletAnchored;
        public static AbilityDef Avir_AmuletAnchor;
        public static AbilityDef Avir_AmuletRend;
        static AvirAmuletElderDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(AvirAmuletElderDefOf)); }
    }

    public static class AmuletElder
    {
        // ---- numbers at strength 1 ----
        public const float SenseRadius = 12f, VigilanceRadius = 10f;
        public const float HuntBonus = 0.15f, SilentBonus = 0.40f, AmbushBonus = 0.50f;
        public const float LastStrikeRadius = 12f;
        public const int AmbushStillTicks = 180;
        public const float GuardLeapRadius = 8f;
        public const int GuardLeapRest = 900, GuardChainWindow = 300, GuardChainMax = 4;
        public const int HoldStillTicks = 120;
        public const float HoldPerStrength = 0.15f, HoldMax = 0.40f;
        public const int WarningStun = 90, WarningRest = 600;
        public const float LeapMin = 3f, LeapMax = 6f;
        public const int LeapRest = 600;
        public const float KillingBlowBase = 0.02f, KillingBlowWounds = 0.10f, KillingBlowMax = 0.15f;
        public const int BloodCallsTicks = 180;
        public const float SeriesRadius = 8f, SeriesPerLink = 0f, SeriesBreakDamage = 25f;   // links no longer grow stronger

        // ---- per pawn state (not saved: all of it is short-lived) ----
        internal static readonly Dictionary<int, Pawn> mark = new Dictionary<int, Pawn>();
        internal static readonly HashSet<long> struck = new HashSet<long>();
        internal static readonly Dictionary<int, int> lastMoved = new Dictionary<int, int>();
        internal static readonly HashSet<int> ambushReady = new HashSet<int>();
        internal static readonly Dictionary<int, int> leapReady = new Dictionary<int, int>();
        internal static readonly Dictionary<int, int> guardReady = new Dictionary<int, int>();
        internal static readonly Dictionary<int, (int tick, int links)> guardChain = new Dictionary<int, (int, int)>();
        internal static readonly Dictionary<int, int> warningReady = new Dictionary<int, int>();
        internal static readonly Dictionary<int, int> series = new Dictionary<int, int>();   // links
        internal static readonly List<(Pawn pawn, Pawn target, bool strike)> pendingLeaps = new List<(Pawn, Pawn, bool)>();

        /// <summary>Opened stones of a branch on the worn elder amulet; strength of that branch.</summary>
        public static int Level(Pawn p, AmuletSubclassDef branch, out float strength)
        {
            strength = 0f;
            CompAvirAmulet a = AmuletDevelopment.WornBy(p);
            if (a == null || !AmuletDevelopment.HasBranch(a, branch) || !AmuletDevelopment.WorksFor(a, p)) return 0;
            int n = AmuletDevelopment.Opened(a, branch);
            if (n > 0) strength = AmuletDevelopment.Strength(a, branch, p);
            return n;
        }

        public static int Level(Pawn p, AmuletSubclassDef branch) => Level(p, branch, out _);

        public static bool SlotIs(Pawn p, AmuletSubclassDef branch)
        {
            CompAvirAmulet a = AmuletDevelopment.WornBy(p);
            return a != null && a.subclass == branch && a.slotOpen;
        }

        public static bool InSeries(Pawn p) => p != null && series.ContainsKey(p.thingIDNumber);

        public static bool Hostile(Pawn a, Pawn b) => a != null && b != null && !b.Dead && b.Spawned && a.HostileTo(b);

        public static Pawn MarkOf(Pawn hunter)
        {
            if (!mark.TryGetValue(hunter.thingIDNumber, out Pawn m)) return null;
            if (m == null || m.Dead || !m.Spawned || m.Map != hunter.Map) { mark.Remove(hunter.thingIDNumber); return null; }
            return m;
        }

        public static Pawn NearestHostile(Pawn p, float radius, Pawn except = null)
        {
            Pawn best = null; float bd = float.MaxValue;
            foreach (Pawn o in p.Map.mapPawns.AllPawnsSpawned)
            {
                if (o == except || o.Downed || !Hostile(p, o) || o.Position.Fogged(o.Map)) continue;
                float d = (o.Position - p.Position).LengthHorizontalSquared;
                if (d <= radius * radius && d < bd) { bd = d; best = o; }
            }
            return best;
        }

        /// <summary>Leap next to the target (if a safe cell is free) and, when asked, strike at once.</summary>
        public static bool LeapTo(Pawn p, Pawn target, bool strike)
        {
            if (p?.Map == null || target == null || !target.Spawned || p.Downed || p.Dead) return false;
            IntVec3 best = IntVec3.Invalid; float bd = float.MaxValue;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(target))
            {
                if (!ArtifactMobility.SafeLanding(c, p.Map, p)) continue;
                float d = (c - p.Position).LengthHorizontalSquared;
                if (d < bd) { bd = d; best = c; }
            }
            if (!best.IsValid) return false;
            ArtifactMobility.Jump(p, best, MoveFx.Leap);
            if (strike)
            {
                p.stances?.SetStance(new Stance_Mobile());
                Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                job.killIncappedTarget = true;
                p.jobs?.StartJob(job, JobCondition.InterruptForced);
                p.meleeVerbs?.TryMeleeAttack(target, null, true);
            }
            return true;
        }

        public static void EndSeries(Pawn p)
        {
            if (p != null) series.Remove(p.thingIDNumber);
        }

        public static void GiveTimed(Pawn p, HediffDef def, int ticks)
        {
            if (def == null || p?.health == null) return;
            if (p.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_ArtifactTimed old) { old.ticksLeft = Mathf.Max(old.ticksLeft, ticks); return; }
            var h = (Hediff_ArtifactTimed)HediffMaker.MakeHediff(def, p);
            h.ticksLeft = ticks;
            p.health.AddHediff(h);
        }

        public static BodyPartRecord VitalPart(Pawn victim)
        {
            var parts = victim.health.hediffSet.GetNotMissingParts().Where(r =>
                r.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource) || r.def.tags.Contains(BodyPartTagDefOf.BloodPumpingSource)
                || r.def.tags.Contains(BodyPartTagDefOf.BreathingSource)).ToList();
            return parts.Count == 0 ? null : parts.RandomElement();
        }
    }

    /// <summary>Everything that runs on a timer: senses, the ambush, leaps, the warning blow,
    /// the hunted slow-down, abilities in the slot. Every 15 ticks.</summary>
    public class MapComponent_AvirAmuletElder : MapComponent
    {
        private const int Interval = 15;
        private readonly List<Pawn> revealed = new List<Pawn>();
        private readonly Dictionary<int, int> alarmReady = new Dictionary<int, int>();
        private static Material markerMat;

        public MapComponent_AvirAmuletElder(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;

            // pending leaps (series, guard) - carried out outside the damage code that queued them
            if (AmuletElder.pendingLeaps.Count > 0)
            {
                var list = AmuletElder.pendingLeaps.ToList();
                AmuletElder.pendingLeaps.Clear();
                foreach (var l in list) if (l.pawn.Map == map) AmuletElder.LeapTo(l.pawn, l.target, l.strike);
            }

            if (now % Interval != 0) return;
            revealed.Clear();
            if (AmuletElder.struck.Count > 4000) AmuletElder.struck.Clear();
            bool threat = GenHostility.AnyHostileActiveThreatToPlayer(map);
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                CompAvirAmulet a = AmuletDevelopment.WornBy(p);
                SyncAbilities(p, a);
                if (a == null || !AmuletDevelopment.HasCasteBranch(a, AvirAmuletElderDefOf.Avir_Caste_Elder)) continue;
                if (p.Dead || p.Downed) { AmuletElder.EndSeries(p); continue; }
                int id = p.thingIDNumber;
                if (p.pather != null && p.pather.Moving)
                {
                    AmuletElder.lastMoved[id] = now;
                    AmuletElder.ambushReady.Remove(id);   // the ambush is only for one who waited
                }
                if (AmuletElder.InSeries(p) && !threat) AmuletElder.EndSeries(p);

                int hunter = AmuletElder.Level(p, AvirAmuletElderDefOf.Avir_Subclass_Hunter, out float hk);
                int guard = AmuletElder.Level(p, AvirAmuletElderDefOf.Avir_Subclass_Guard, out float gk);
                int warrior = AmuletElder.Level(p, AvirAmuletElderDefOf.Avir_Subclass_Warrior, out float wk);

                // hunter 3 / guard 3: what hides in the fog
                float senseR = 0f;
                if (hunter >= 3) senseR = AmuletElder.SenseRadius + (hunter - 3);
                if (guard >= 3) senseR = Mathf.Max(senseR, AmuletElder.VigilanceRadius);
                if (senseR > 0f) Sense(p, senseR, guard >= 3, now);

                // hunter 15: the marked target is slowed
                if (hunter >= 15)
                {
                    Pawn m = AmuletElder.MarkOf(p);
                    if (m != null) AmuletElder.GiveTimed(m, AvirAmuletElderDefOf.Avir_AmuletHunted, Interval * 2 + 5);
                }

                // hunter slot: ambush
                if (AmuletElder.SlotIs(p, AvirAmuletElderDefOf.Avir_Subclass_Hunter) && threat && ArtifactCharge.CombatReady(p))
                {
                    bool still = !AmuletElder.lastMoved.TryGetValue(id, out int lm) || now - lm >= AmuletElder.AmbushStillTicks;
                    bool attacking = p.stances?.curStance is Stance_Busy b && b.verb != null && b.verb.verbProps.violent;
                    if (still && !attacking)
                    {
                        AmuletElder.GiveTimed(p, ArtifactMotionHediffDefOf.Avir_ArtifactUnseen, Interval * 2 + 5);
                        AmuletElder.ambushReady.Add(id);
                    }
                }

                // guard 15: the warning blow
                if (guard >= 15 && (!AmuletElder.warningReady.TryGetValue(id, out int wr) || now >= wr))
                {
                    foreach (Pawn o in GenAdj.CellsAdjacent8Way(p).SelectMany(c => c.InBounds(map) ? c.GetThingList(map).OfType<Pawn>() : Enumerable.Empty<Pawn>()))
                    {
                        if (!AmuletElder.Hostile(p, o) || o.Downed || o.stances?.stunner == null || o.stances.stunner.Stunned) continue;
                        o.stances.stunner.StunFor(Mathf.RoundToInt(AmuletElder.WarningStun * Mathf.Max(1f, gk)), p);
                        AmuletElder.warningReady[id] = now + AmuletElder.WarningRest;
                        break;
                    }
                }

                // A'Shaign: battle telekinesis - disarming and knocking down (no damage)
                if (a.IsMulticlass && threat && ArtifactCharge.CombatReady(p)) AmuletBattleTelekinesis.Tick(p, a, map, now);

                // warrior 3: the predator's leap
                if (warrior >= 3 && a.autoLeap && p.CurJob?.def == JobDefOf.AttackMelee && p.CurJob.targetA.Thing is Pawn t
                    && AmuletElder.Hostile(p, t) && (!AmuletElder.leapReady.TryGetValue(id, out int lr) || now >= lr))
                {
                    float d = p.Position.DistanceTo(t.Position);
                    if (d >= AmuletElder.LeapMin && d <= AmuletElder.LeapMax && GenSight.LineOfSight(p.Position, t.Position, map, true)
                        && AmuletElder.LeapTo(p, t, false))
                        AmuletElder.leapReady[id] = now + AmuletElder.LeapRest;
                }
            }
        }

        private void Sense(Pawn p, float r, bool alarm, int now)
        {
            bool anyNew = false;
            foreach (Pawn o in map.mapPawns.AllPawnsSpawned)
            {
                if (!AmuletElder.Hostile(p, o) || (o.Position - p.Position).LengthHorizontalSquared > r * r) continue;
                if (o.Position.Fogged(map)) { if (!revealed.Contains(o)) revealed.Add(o); }
                anyNew = true;
            }
            if (alarm && anyNew && p.Faction == Faction.OfPlayer
                && (!alarmReady.TryGetValue(p.thingIDNumber, out int ready) || now >= ready))
            {
                alarmReady[p.thingIDNumber] = now + 1800;
                Messages.Message(p.LabelShort + " senses enemies close by.", p, MessageTypeDefOf.ThreatSmall, false);
            }
        }

        public override void MapComponentUpdate()
        {
            if (revealed.Count == 0 || Find.CurrentMap != map) return;
            if (markerMat == null) markerMat = MaterialPool.MatFrom(new MaterialRequest(ArtifactoricsTex.Glow, ShaderDatabase.MetaOverlay, new Color(0.9f, 0.1f, 0.05f, 0.75f)));
            foreach (Pawn o in revealed)
            {
                if (o == null || !o.Spawned || o.Map != map) continue;
                Vector3 pos = o.DrawPos; pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1.6f, 1f, 1.6f)), markerMat, 0);
            }
        }

        /// <summary>Every caste's slot ability: present exactly while it is the amulet's own subclass.</summary>
        private static void SyncAbilities(Pawn p, CompAvirAmulet a)
        {
            if (p.abilities == null) return;
            foreach (AmuletSubclassDef s in DefDatabase<AmuletSubclassDef>.AllDefsListForReading)
                if (s.slotAbility != null) Sync(p, s.slotAbility, a != null && a.subclass == s && a.slotOpen && AmuletDevelopment.WorksFor(a, p));
        }

        private static void Sync(Pawn p, AbilityDef def, bool should)
        {
            if (def == null) return;
            bool has = p.abilities.GetAbility(def) != null;
            if (should && !has) p.abilities.GainAbility(def);
            else if (!should && has) p.abilities.RemoveAbility(def);
        }
    }

    /// <summary>Damage out (hunt, silent strike, ambush, killing blow, series) and damage in
    /// (hold the line, series break).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_AmuletElderDamage
    {
        public static void Prefix(Pawn __instance, ref DamageInfo __0)
        {
            Pawn victim = __instance;
            if (victim == null || !__0.Def.harmsHealth) return;
            int now = Find.TickManager.TicksGame;

            // ---- the victim is an elder ----
            int hold = AmuletElder.Level(victim, AvirAmuletElderDefOf.Avir_Subclass_Guard, out float gk);
            if (hold >= 12 && (!AmuletElder.lastMoved.TryGetValue(victim.thingIDNumber, out int lm) || now - lm >= AmuletElder.HoldStillTicks))
                __0.SetAmount(__0.Amount * (1f - Mathf.Min(AmuletElder.HoldMax, AmuletElder.HoldPerStrength * gk)));
            if (AmuletElder.InSeries(victim) && __0.Amount >= AmuletElder.SeriesBreakDamage) AmuletElder.EndSeries(victim);

            // ---- the attacker is an elder ----
            if (!(__0.Instigator is Pawn att) || att == victim) return;
            int hunter = AmuletElder.Level(att, AvirAmuletElderDefOf.Avir_Subclass_Hunter, out float hk);
            int warrior = AmuletElder.Level(att, AvirAmuletElderDefOf.Avir_Subclass_Warrior, out float wk);
            if (hunter == 0 && warrior == 0 && !AmuletElder.ambushReady.Contains(att.thingIDNumber)) return;
            float factor = 1f;

            if (hunter >= 9 && AmuletElder.Hostile(att, victim))
            {
                Pawn m = AmuletElder.MarkOf(att);
                if (m == null) { AmuletElder.mark[att.thingIDNumber] = victim; m = victim; }
                if (m == victim) factor += AmuletElder.HuntBonus * hk;
            }
            if (hunter >= 12)
            {
                long key = ((long)att.thingIDNumber << 32) | (uint)victim.thingIDNumber;
                bool unaware = victim.mindState?.enemyTarget != att && victim.CurJob?.targetA.Thing != att;
                if (unaware && struck.Add(key)) factor += AmuletElder.SilentBonus * hk;
            }
            if (AmuletElder.ambushReady.Remove(att.thingIDNumber)) factor += AmuletElder.AmbushBonus;

            bool melee = !__0.Def.isRanged && !__0.Def.isExplosive;
            if (warrior >= 9 && melee && __0.HitPart == null)
            {
                float wounds = 1f - victim.health.summaryHealth.SummaryHealthPercent;
                float chance = Mathf.Min(AmuletElder.KillingBlowMax, (AmuletElder.KillingBlowBase + AmuletElder.KillingBlowWounds * wounds) * wk);
                if (Rand.Chance(chance))
                {
                    BodyPartRecord part = AmuletElder.VitalPart(victim);
                    if (part != null) __0.SetHitPart(part);
                }
            }
            if (AmuletElder.series.TryGetValue(att.thingIDNumber, out int links))
                factor += AmuletElder.SeriesPerLink * links;

            if (factor != 1f) __0.SetAmount(__0.Amount * factor);
        }

        private static HashSet<long> struck => AmuletElder.struck;
    }

    /// <summary>After a landed blow: the guard's leap at whoever struck an ally, and the series
    /// breaking on a blow that did not kill.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_AmuletElderAfterHit
    {
        public static void Postfix(Pawn __instance, DamageInfo __0)
        {
            Pawn victim = __instance;
            if (victim?.Map == null || !(__0.Instigator is Pawn att) || att == victim) return;

            // series: a blow that did not kill ends it
            if (AmuletElder.InSeries(att) && !victim.Dead && !__0.Def.isRanged) AmuletElder.EndSeries(att);

            // guard 9 / 18: someone struck an ally of a guard
            if (victim.Dead || !att.Spawned || att.Dead) return;
            int now = Find.TickManager.TicksGame;
            foreach (Pawn g in victim.Map.mapPawns.AllPawnsSpawned)
            {
                if (g == victim || g.Downed || g.Dead || g.HostileTo(victim) || !g.HostileTo(att)) continue;
                if ((g.Position - victim.Position).LengthHorizontalSquared > AmuletElder.GuardLeapRadius * AmuletElder.GuardLeapRadius) continue;
                int lvl = AmuletElder.Level(g, AvirAmuletElderDefOf.Avir_Subclass_Guard);
                if (lvl < 9) continue;
                int id = g.thingIDNumber;
                bool ready = !AmuletElder.guardReady.TryGetValue(id, out int r) || now >= r;
                bool chain = lvl >= 18 && AmuletElder.guardChain.TryGetValue(id, out var ch)
                             && now - ch.tick <= AmuletElder.GuardChainWindow && ch.links < AmuletElder.GuardChainMax;
                if (!ready && !chain) continue;
                int links = chain ? AmuletElder.guardChain[id].links + 1 : 1;
                AmuletElder.guardChain[id] = (now, links);
                AmuletElder.guardReady[id] = now + AmuletElder.GuardLeapRest;
                AmuletElder.pendingLeaps.Add((g, att, true));
                break;   // one guard answers each blow
            }
        }
    }

    /// <summary>Kills: the hunter's last strike, the warrior's blood calls and hunting series.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_AmuletElderKill
    {
        public static void Prefix(Pawn __instance, DamageInfo? __0)
        {
            if (!(__0?.Instigator is Pawn killer) || killer == __instance || killer.Map == null) return;
            Pawn victim = __instance;

            int hunter = AmuletElder.Level(killer, AvirAmuletElderDefOf.Avir_Subclass_Hunter);
            if (hunter >= 18 && AmuletElder.MarkOf(killer) == victim)
            {
                Pawn next = AmuletElder.NearestHostile(killer, AmuletElder.LastStrikeRadius, victim);
                if (next != null) AmuletElder.mark[killer.thingIDNumber] = next; else AmuletElder.mark.Remove(killer.thingIDNumber);
                killer.stances?.SetStance(new Stance_Mobile());
            }

            int warrior = AmuletElder.Level(killer, AvirAmuletElderDefOf.Avir_Subclass_Warrior);
            if (warrior >= 12) AmuletElder.GiveTimed(killer, AvirAmuletElderDefOf.Avir_AmuletBloodCalls, AmuletElder.BloodCallsTicks);
            bool melee = __0.Value.Def != null && !__0.Value.Def.isRanged && !__0.Value.Def.isExplosive;
            if (warrior >= 18 && melee)
            {
                Pawn next = AmuletElder.NearestHostile(killer, AmuletElder.SeriesRadius, victim);
                if (next == null) { AmuletElder.EndSeries(killer); return; }
                AmuletElder.series.TryGetValue(killer.thingIDNumber, out int links);
                AmuletElder.series[killer.thingIDNumber] = links + 1;
                AmuletElder.pendingLeaps.Add((killer, next, true));
            }
        }
    }

    /// <summary>Warrior 15: no stun during a series.</summary>
    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.StunFor))]
    public static class Patch_AmuletElderUnstoppable
    {
        public static bool Prefix(StunHandler __instance)
        {
            if (!(__instance.parent is Pawn p) || !AmuletElder.InSeries(p)) return true;
            return AmuletElder.Level(p, AvirAmuletElderDefOf.Avir_Subclass_Warrior) < 15;
        }
    }

    // ---------------------------------------------------------------- slot abilities

    public class CompProperties_AmuletAnchor : CompProperties_AbilityEffect
    {
        public float radius = 3f;
        public int ticks = 300;
        public CompProperties_AmuletAnchor() { compClass = typeof(CompAbilityEffect_AmuletAnchor); }
    }

    public class CompAbilityEffect_AmuletAnchor : CompAbilityEffect
    {
        private new CompProperties_AmuletAnchor Props => (CompProperties_AmuletAnchor)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn p = parent.pawn;
            if (p?.Map == null) return;
            foreach (Pawn o in p.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!AmuletElder.Hostile(p, o) || (o.Position - p.Position).LengthHorizontalSquared > Props.radius * Props.radius) continue;
                AmuletElder.GiveTimed(o, AvirAmuletElderDefOf.Avir_AmuletAnchored, Props.ticks);
                FleckMaker.ThrowMicroSparks(o.DrawPos, p.Map);
            }
            FleckMaker.Static(p.TrueCenter(), p.Map, FleckDefOf.PsycastAreaEffect, Props.radius * 2f);
        }
    }

    public class CompProperties_AmuletRend : CompProperties_AbilityEffect
    {
        public CompProperties_AmuletRend() { compClass = typeof(CompAbilityEffect_AmuletRend); }
    }

    public class CompAbilityEffect_AmuletRend : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!(target.Thing is Pawn t) || !AmuletElder.Hostile(parent.pawn, t))
            {
                if (throwMessages) Messages.Message("Rend needs an enemy.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (target.Thing is Pawn t && !AmuletElder.LeapTo(parent.pawn, t, true))
                Messages.Message("No free ground next to the target.", parent.pawn, MessageTypeDefOf.RejectInput, false);
        }
    }
}
