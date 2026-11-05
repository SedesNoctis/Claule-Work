using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    // =====================================================================================
    // GUISES, REWORKED.
    //   Under her veil every blow she strikes is a chance to be seen: the one struck looks for her (by the mind - SOC),
    //   and the more she strikes, the likelier it is. A beast catches her sooner. Those who cannot see her lose her:
    //   they stop chasing a target that is not there.
    //   The switch: a deep veil, a step elsewhere, and a mirage left where she stood that draws every eye.
    //   The mirage's shadows are taken for her by the mind, not by everyone.
    //   Veil of the pack: her veiled own can be seen through too; and her fallen are veiled where they lie.
    // =====================================================================================
    public static class LinarianGuise
    {
        private const int H = GenDate.TicksPerHour;

        private static readonly Dictionary<int, int> strikes = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> lastStrike = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> deepUntil = new Dictionary<int, int>();
        private static readonly Dictionary<Pawn, Pawn> packVeiled = new Dictionary<Pawn, Pawn>();
        private static readonly Dictionary<int, int> fallenVeiled = new Dictionary<int, int>();

        /// <summary>The switch's deep veil: for half an hour no eye and no blow of hers gives her away.</summary>
        public static bool Deep(Pawn p) => p != null && deepUntil.TryGetValue(p.thingIDNumber, out int u) && Find.TickManager.TicksGame < u;

        public static void Forget(Pawn her) { if (her != null) strikes.Remove(her.thingIDNumber); }

        /// <summary>She struck from under her veil: the one struck looks for her.</summary>
        public static void Struck(Pawn her, Pawn victim)
        {
            if (victim == null || victim == her || victim.Dead || victim.Downed || !LinarianVeil.Veiled(her) || Deep(her)) return;
            int now = Find.TickManager.TicksGame, id = her.thingIDNumber;
            int s = strikes.TryGetValue(id, out int n) && lastStrike.TryGetValue(id, out int l) && now - l < H * 2 ? n + 1 : 1;
            strikes[id] = s;
            lastStrike[id] = now;
            float c;
            if (victim.RaceProps.Animal) c = 0.04f * s * s;   // a beast is not fooled for long
            else if (victim.RaceProps.Humanlike) c = 0.06f * s / Mathf.Max(0.3f, LinarianTactics.Soc(her, victim, "G"));
            else return;   // machines see through her by their own sight (LinarianVeil.SeesThrough)
            if (Linarian.Song(her)?.Has("G5") == true) c *= 0.5f;
            if (Rand.Chance(Mathf.Min(0.95f, c))) LinarianVeil.Reveal(her, victim);
        }

        /// <summary>Those who cannot see her lose her: no foe keeps chasing a target that is not there.</summary>
        public static void LoseHer(Pawn her)
        {
            if (!her.Spawned || !her.IsPsychologicallyInvisible()) return;
            foreach (Pawn q in her.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (q == her || q.Dead || q.mindState == null || q.MentalState is MentalState_Song || !q.HostileTo(her)) continue;
                if (q.mindState.enemyTarget == her) q.mindState.enemyTarget = null;
                if (q.mindState.meleeThreat == her) q.mindState.meleeThreat = null;
                Job j = q.CurJob;
                if (j != null && (j.targetA.Thing == her || j.targetB.Thing == her)
                    && (j.def == JobDefOf.AttackMelee || j.def == JobDefOf.AttackStatic || j.def == JobDefOf.UseVerbOnThing))
                    q.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        // ---------------------------------------------------------------- the switch

        public static void Switch(Pawn her, Gene_LinarianSong g, IntVec3 to)
        {
            if (!to.IsValid || !to.Standable(her.Map)) return;
            Map map = her.Map;
            IntVec3 from = her.Position;
            int now = Find.TickManager.TicksGame;
            LinarianVeil.Veil(her, LinarianZone.Ticks(H, g));
            deepUntil[her.thingIDNumber] = now + H / 2;
            Forget(her);
            LinarianActs.Leap(her, to);
            Building_ShadowDecoy d = Building_ShadowDecoy.SpawnOne(her, from, map, now + LinarianZone.Ticks(H / 2, g), true);
            int n = d == null ? 0 : Lure(her, d);
            LinarianTactics.Notify(her, n > 0 ? "she is gone; her mirage draws " + n + "." : "she is gone; her mirage stands where she was.");
        }

        /// <summary>Every foe of hers on the map turns on the mirage - each by its own mind.</summary>
        public static int Lure(Pawn her, Building_ShadowDecoy d)
        {
            int n = 0;
            foreach (Pawn q in d.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (q.Dead || q.Downed || q.InMentalState || q.jobs == null || !q.HostileTo(her)) continue;
                if (!q.RaceProps.Humanlike && !q.RaceProps.IsMechanoid) continue;
                if ((q.Position - d.Position).LengthHorizontal > 50f) continue;
                if (!Rand.Chance(Fooled(her, q))) continue;
                d.fooled.Add(q.thingIDNumber);
                Aim(q, d);
                n++;
            }
            return n;
        }

        public static void Aim(Pawn q, Building_ShadowDecoy d)
        {
            if (q.Dead || q.Downed || q.InMentalState || q.jobs == null) return;
            if (q.CurJob != null && q.CurJob.targetA.Thing == d) return;
            Verb v = q.TryGetAttackVerb(d, false);
            Job j = JobMaker.MakeJob(v != null && !v.IsMeleeAttack ? JobDefOf.AttackStatic : JobDefOf.AttackMelee, d);
            j.expiryInterval = 600;
            j.checkOverrideOnExpire = true;
            q.mindState.enemyTarget = d;
            q.jobs.StartJob(j, JobCondition.InterruptForced, null, false, true);
        }

        /// <summary>How likely a foe takes her mirage for her: by the mind (machines, a quarter of the time).</summary>
        public static float Fooled(Pawn her, Pawn q) =>
            her == null ? 1f : q.RaceProps.IsMechanoid ? 0.25f : Mathf.Clamp01(0.3f + 0.5f * LinarianTactics.Soc(her, q, "G"));

        /// <summary>Once a day, seen through: a ring of mirages round her, and she is veiled again.</summary>
        public static bool Ring(Pawn her, Gene_LinarianSong g)
        {
            int now = Find.TickManager.TicksGame;
            if (g == null || !g.Has("G4") || !her.Spawned || now - g.lastRing < GenDate.TicksPerDay) return false;
            g.lastRing = now;
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                IntVec3 c = her.Position + new IntVec3(Mathf.RoundToInt(Mathf.Cos(a) * 2.6f), 0, Mathf.RoundToInt(Mathf.Sin(a) * 2.6f));
                if (c.InBounds(her.Map) && c.Standable(her.Map) && c.GetEdifice(her.Map) == null)
                    Building_ShadowDecoy.SpawnOne(her, c, her.Map, now + H / 2, false);
            }
            LinarianVeil.Veil(her, H / 2);
            Forget(her);
            if (her.Faction == Faction.OfPlayer) Messages.Message(her.LabelShortCap + " is seen - and is six of herself, and then none.", her, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        // ---------------------------------------------------------------- veil of the pack

        public static void PackVeil(Pawn her, Pawn a, int ticks)
        {
            LinarianVeil.Veil(a, ticks);
            packVeiled[a] = her;
        }

        /// <summary>Her veiled own are looked for as she is: by the mind of those near them.</summary>
        public static void CheckPack(Map map)
        {
            foreach (KeyValuePair<Pawn, Pawn> kv in packVeiled.ToList())
            {
                Pawn a = kv.Key, her = kv.Value;
                if (a == null || a.Dead || her == null || !LinarianVeil.Veiled(a)) { packVeiled.Remove(a); continue; }
                if (a.Map != map || !a.Spawned) continue;
                foreach (Thing t in GenRadial.RadialDistinctThingsAround(a.Position, map, 6.9f, true))
                {
                    float c = LinarianVeil.SeesThrough(a, t) * 0.25f;
                    if (t is Pawn q && !q.Dead && !q.Downed && q.RaceProps.Humanlike && q.HostileTo(a))
                        c += 0.015f / Mathf.Max(0.3f, LinarianTactics.Soc(her, q, "G"));
                    if (c <= 0f || !Rand.Chance(c)) continue;
                    LinarianVeil.Unveil(a);
                    packVeiled.Remove(a);
                    if (a.Faction == Faction.OfPlayer) Messages.Message(t.LabelShortCap + " sees through the veil on " + a.LabelShort + ".", a, MessageTypeDefOf.NeutralEvent, false);
                    break;
                }
            }
        }

        /// <summary>Her fallen are veiled where they lie, for an hour.</summary>
        public static void CoverFallen(Pawn her, Gene_LinarianSong g, int now)
        {
            if (!g.Has("G5") || her.Faction == null) return;
            foreach (Pawn q in her.Map.mapPawns.SpawnedPawnsInFaction(her.Faction).ToList())
            {
                if (q == her || q.Dead || !q.Downed || LinarianVeil.Veiled(q) || !LinarianZone.InZone(her, g, q.Position)) continue;
                if (fallenVeiled.TryGetValue(q.thingIDNumber, out int last) && now - last < H * 3) continue;
                fallenVeiled[q.thingIDNumber] = now;
                PackVeil(her, q, H);
                FleckMaker.ThrowSmoke(q.DrawPos, q.Map, 1.2f);
            }
        }

        // ---------------------------------------------------------------- the mirage, as an aura of her zone

        public static void Haze(Pawn her, Gene_LinarianSong g, int now)
        {
            if (!g.Has("G3")) return;
            int hour = now / H;
            foreach (Pawn q in LinarianZone.InHerZone(her, g).ToList())
            {
                if (q == her || q.Dead || !q.HostileTo(her) || !LinarianTactics.Takeable(q) || q.health.hediffSet.HasHediff(LinarianHediffs.Mirage)) continue;
                // each foe is fooled or not for the hour: by its own mind
                if (Rand.ValueSeeded(Gen.HashCombineInt(q.thingIDNumber, hour)) >= LinarianTactics.Chance(her, g, q, false, "G")) continue;
                LinarianHediffs.Add(q, LinarianHediffs.Mirage, 120, 0.3f);
            }
        }

        /// <summary>Everything the guises do each second for one Singer.</summary>
        public static void Tick(Pawn her, Gene_LinarianSong g, int now)
        {
            if (LinarianVeil.Veiled(her) || LinarianVeil.InForm(her) != null) LoseHer(her);
            Haze(her, g, now);
            if (now % 240 == 0) CoverFallen(her, g, now);
        }
    }

    /// <summary>A blow struck from under her veil: the one struck looks for her.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_LinarianVeilSeenByBlow
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn her) || her == __instance || !Linarian.Is(her)) return;
            LinarianGuise.Struck(her, __instance);
        }
    }

    /// <summary>A fallen one under a veil stays unseen (the game shows every downed pawn).</summary>
    [HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsPsychologicallyInvisible))]
    public static class Patch_LinarianFallenVeiled
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result && pawn.Downed && LinarianVeil.Veiled(pawn)) __result = true;
        }
    }

    /// <summary>...and is not carried off.</summary>
    [HarmonyPatch(typeof(KidnapAIUtility), nameof(KidnapAIUtility.TryFindGoodKidnapVictim))]
    public static class Patch_LinarianFallenNotKidnapped
    {
        public static void Postfix(ref Pawn victim, ref bool __result)
        {
            if (__result && LinarianVeil.Veiled(victim)) { victim = null; __result = false; }
        }
    }
}
