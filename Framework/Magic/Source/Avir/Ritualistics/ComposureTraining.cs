using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Ritualistics
{
    /// <summary>
    /// COMPOSURE IS TRAINED - by a ritualist, not by everyone.
    ///   Practice: every ritual worked puts energy through the pawn. It has no ceiling, but each step comes slower
    ///     (about +10% after the first rituals, +25% after long work, +40% over years). A ritual teaches most when it
    ///     asks about as much as the pawn holds, or a little more; an easy one teaches an old hand almost nothing.
    ///     The first time a pattern is performed teaches three times as much. Practice and intellectual work grow
    ///     together; a passion for intellectual work speeds practice (x1.5, x2). A lash survived is a lesson too.
    ///   A familiar pattern: each ritual performed again and again steadies the hand for that ritual alone - up to +15%.
    ///   Strain: each ritual performed lowers composure until sleep or meditation takes it off.
    ///   A steady hand near: the most practised colonist standing in the circle lends a quarter of their practice.
    ///   Calm: hours of meditation over the last three days, +2.5% an hour, up to +15%.
    /// </summary>
    public class GameComponent_AvirComposure : GameComponent
    {
        public class Record : IExposable
        {
            public float turnover;   // the old count of energy held (kept for older saves)
            public float practice;
            public float strain;
            public int day = -1;
            public int[] meditated = new int[3];   // ticks meditated on day, day-1, day-2
            public Dictionary<string, int> familiar = new Dictionary<string, int>();

            public void Roll(int today)
            {
                if (day < 0) { day = today; return; }
                int shift = today - day;
                if (shift <= 0) return;
                for (int i = meditated.Length - 1; i >= 0; i--) meditated[i] = i - shift >= 0 ? meditated[i - shift] : 0;
                day = today;
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref turnover, "turnover");
                Scribe_Values.Look(ref practice, "practice");
                Scribe_Values.Look(ref strain, "strain");
                Scribe_Values.Look(ref day, "day", -1);
                List<int> m = meditated?.ToList();
                Scribe_Collections.Look(ref m, "meditated", LookMode.Value);
                Scribe_Collections.Look(ref familiar, "familiar", LookMode.Value, LookMode.Value);
                if (Scribe.mode == LoadSaveMode.LoadingVars) meditated = m != null && m.Count == 3 ? m.ToArray() : new int[3];
                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    if (familiar == null) familiar = new Dictionary<string, int>();
                    if (practice <= 0f && turnover > 0f) { practice = turnover * 0.5f; turnover = 0f; }   // an older save: what was held becomes practice
                }
            }
        }

        public const float PerMeditationHour = 0.025f, MeditationMax = 0.15f;
        public const float StrainPerRitual = 0.06f, FamiliarMax = 0.15f, SteadyShare = 0.25f;

        private Dictionary<Pawn, Record> records = new Dictionary<Pawn, Record>();
        private List<Pawn> keys; private List<Record> vals;
        private static GameComponent_AvirComposure instance;

        public GameComponent_AvirComposure(Game game) { instance = this; }

        public static GameComponent_AvirComposure Get => instance ?? (instance = Current.Game?.GetComponent<GameComponent_AvirComposure>());

        private static int Today => GenTicks.TicksAbs / GenDate.TicksPerDay;

        private Record For(Pawn p, bool create)
        {
            if (p == null) return null;
            if (records.TryGetValue(p, out Record r)) { r.Roll(Today); return r; }
            if (!create) return null;
            r = new Record();
            r.Roll(Today);
            records[p] = r;
            return r;
        }

        /// <summary>What a pattern is known by: its name in the book, or its runes as written.</summary>
        public static string KeyOf(RitualSchema s)
        {
            if (s == null) return null;
            RitualReading r = RitualEvaluator.Read(s);
            if (r.pattern != null) return r.pattern.defName;
            return string.Join("|", s.rings.Select(g => string.Join(",", g.runes.Where(x => x != null).Select(x => x.defName))));
        }

        private static float Passion(Pawn p)
        {
            Passion pa = p.skills?.GetSkill(SkillDefOf.Intellectual)?.passion ?? RimWorld.Passion.None;
            return pa == RimWorld.Passion.Major ? 2f : pa == RimWorld.Passion.Minor ? 1.5f : 1f;
        }

        /// <summary>A ritual worked to its end (or broken): energy held, what it asked against what the pawn holds.</summary>
        public static void Performed(Pawn p, float energy, float risk, float composure, string key)
        {
            Record r = Get?.For(p, true);
            if (r == null) return;
            float ratio = risk / Mathf.Max(0.05f, composure);
            float teach = 0.05f + 0.95f * Mathf.Exp(-Mathf.Pow((ratio - 1.05f) / 0.3f, 2f));
            bool first = key != null && !r.familiar.ContainsKey(key);
            float gain = Mathf.Max(5f, energy) * teach * (first ? 3f : 1f) * Passion(p);
            r.practice += gain;
            p.skills?.Learn(SkillDefOf.Intellectual, Mathf.Min(3000f, gain * 3f));
            if (key != null) r.familiar[key] = (r.familiar.TryGetValue(key, out int n) ? n : 0) + 1;
            r.strain += StrainPerRitual;
        }

        /// <summary>A small lesson: a circle drawn.</summary>
        public static void Drew(Pawn p) { Record r = Get?.For(p, true); if (r != null) r.practice += 15f * Passion(p); }

        /// <summary>A lash survived: a lesson the body keeps.</summary>
        public static void Lashed(Pawn p, float severity) { Record r = Get?.For(p, true); if (r != null) r.practice += 150f * Mathf.Clamp(severity, 0.2f, 1f) * Passion(p); }

        public static void AddTurnover(Pawn p, float energy) { Record r = energy > 0f ? Get?.For(p, true) : null; if (r != null) r.practice += energy; }

        public static void AddMeditation(Pawn p, int ticks)
        {
            Record r = Get?.For(p, true);
            if (r == null) return;
            r.meditated[0] += ticks;
            if (r.strain > 0f) r.strain = Mathf.Max(0f, r.strain - StrainPerRitual * ticks / GenDate.TicksPerHour);
        }

        public static void Slept(Pawn p, int ticks)
        {
            Record r = Get?.For(p, false);
            if (r != null && r.strain > 0f) r.strain = Mathf.Max(0f, r.strain - StrainPerRitual * ticks / (2f * GenDate.TicksPerHour));
        }

        public static float Practice(Pawn p) => Get?.For(p, false)?.practice ?? 0f;

        /// <summary>No ceiling; each step slower.</summary>
        public static float PracticeShift(Pawn p) => 0.08f * Mathf.Log(1f + Practice(p) / 200f);

        public static float Strain(Pawn p) => Get?.For(p, false)?.strain ?? 0f;

        public static float Familiar(Pawn p, string key)
        {
            Record r = key != null ? Get?.For(p, false) : null;
            if (r == null || !r.familiar.TryGetValue(key, out int n)) return 0f;
            return FamiliarMax * (1f - Mathf.Exp(-n / 6f));
        }

        public static int TimesPerformed(Pawn p, string key) { Record r = key != null ? Get?.For(p, false) : null; return r != null && r.familiar.TryGetValue(key, out int n) ? n : 0; }

        /// <summary>The most practised colonist standing in the circle, besides the performer, lends a quarter of their practice.</summary>
        public static float SteadyHand(Pawn p, CompRitualCircle c, out Pawn who)
        {
            who = null;
            if (p == null || c?.parent?.Map == null) return 0f;
            float best = 0f;
            foreach (Pawn q in c.parent.Map.mapPawns.FreeColonistsSpawned.ToList())
            {
                if (q == p || q.Downed || !c.Inside(q.Position)) continue;
                float s = PracticeShift(q) * SteadyShare;
                if (s > best) { best = s; who = q; }
            }
            return best;
        }

        /// <summary>Composure for this ritual: the stat, and the hand that knows this pattern, and a steady hand near.</summary>
        public static float For(Pawn p, RitualSchema s, CompRitualCircle c)
        {
            if (p == null) return 0f;
            return p.GetStatValue(RitualDefOf.Avir_Composure) + Familiar(p, KeyOf(s)) + SteadyHand(p, c, out _);
        }

        public static float ExperienceShift(Pawn p) => PracticeShift(p);

        public static float MeditationHours(Pawn p)
        {
            Record r = Get?.For(p, false);
            return r == null ? 0f : r.meditated.Sum() / (float)GenDate.TicksPerHour;
        }

        public static float MeditationShift(Pawn p) => Mathf.Min(MeditationMax, PerMeditationHour * MeditationHours(p));

        public static float Turnover(Pawn p) => Practice(p);

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) records.RemoveAll(kv => kv.Key == null || kv.Key.Destroyed);
            Scribe_Collections.Look(ref records, "composureRecords", LookMode.Reference, LookMode.Deep, ref keys, ref vals);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null) records = new Dictionary<Pawn, Record>();
                records.RemoveAll(kv => kv.Key == null);
                instance = this;
            }
        }
    }

    /// <summary>Counts the time a pawn meditates (any meditation: focus, worship, at a shrine), and sleep that takes the strain off.</summary>
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.DriverTickInterval))]
    public static class Patch_ComposureMeditation
    {
        public static void Postfix(JobDriver __instance, int delta)
        {
            Pawn p = __instance.pawn;
            if (p == null || !p.RaceProps.Humanlike) return;
            if (__instance is JobDriver_Meditate) { GameComponent_AvirComposure.AddMeditation(p, delta); Avir.Magic.MagicFill.Meditated(p, delta); }
            else if (__instance.asleep) GameComponent_AvirComposure.Slept(p, delta);
        }
    }
}
