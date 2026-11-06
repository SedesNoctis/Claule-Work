using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The stores of Life and its meetings (the author, Fix145). Each meeting keeps its own store; a store is
    /// shown while a spell of its own is in the book or anything is left in it. When the spells of a store leave the
    /// book (and others of Life stay), about half of it passes into the store that stays - plain Life if it is there,
    /// else the meeting that is. The other half is not lost quietly: it breaks out of the mage as that store's own price
    /// (my proposal - so that it pays to spend a store, not to change it).</summary>
    public static partial class LifeMagic
    {
        public static readonly RuneKind[] AllKinds = { None, RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air };

        /// <summary>The stores the mage's book has spells for: plain Life (None) and the meetings.</summary>
        public static HashSet<RuneKind> KindsInBook(Hediff_ArcaneCore core)
        {
            var set = new HashSet<RuneKind>();
            if (core == null) return set;
            foreach (RitualSchema s in core.Book.spells)
            {
                if (s == null) continue;
                if (s.weave == null) { if (Is(Spells.Element(s))) set.Add(None); continue; }
                foreach (SpellCircle g in Circles(s.weave)) set.Add(PairOf(s.weave, g));
                if (!Circles(s.weave).Any() && s.weave.All().Any(c => Is(c.element))) set.Add(None);
            }
            return set;
        }

        public static string StoreName(RuneKind k) => PairName(k) ?? "Life";

        private static readonly Dictionary<long, int> lastSeen = new Dictionary<long, int>();
        private static long Key(Pawn p, RuneKind k) => ((long)p.thingIDNumber << 8) | (long)((int)k + 2);

        /// <summary>Hourly: a store whose spells have left the book for an hour passes half into the store that stays.</summary>
        public static void Shift(Hediff_ArcaneCore core)
        {
            Pawn p = core.pawn;
            if (p == null) return;
            var here = KindsInBook(core);
            int now = Find.TickManager.TicksGame;
            foreach (RuneKind k in here) lastSeen[Key(p, k)] = now;
            if (here.Count == 0) return;   // no spell of Life at all: the stores stay till they are spent or fade
            foreach (RuneKind k in AllKinds)
            {
                if (here.Contains(k) || core.Store(k) <= 0.5f) continue;
                if (!lastSeen.TryGetValue(Key(p, k), out int seen)) { lastSeen[Key(p, k)] = now; continue; }
                if (now - seen < GenDate.TicksPerHour) continue;
                RuneKind to = here.Contains(None) ? None : here.First();
                float v = core.Store(k);
                core.SetStore(k, 0f);
                core.AddTo(to, v * 0.5f);
                Spill(p, k, v * 0.5f);
                Messages.Message(p.LabelShortCap + " let the store of " + StoreName(k) + " go: half went into " + StoreName(to) + ", the rest broke out of them.", p, MessageTypeDefOf.NegativeEvent, false);
            }
        }

        /// <summary>The half that was not kept breaks out of the mage as the store's own price.</summary>
        private static void Spill(Pawn p, RuneKind k, float amount)
        {
            if (amount < 10f || p.Dead) return;
            if (k != None) { PayPair(k, p, true); return; }
            HediffDef bark = H("Avir_LifeOverflow");
            if (bark != null) { Hediff h = p.health.hediffSet.GetFirstHediffOfDef(bark) ?? p.health.AddHediff(bark); h.Severity = Mathf.Min(1f, h.Severity + 0.3f); }
            MagicFx.Give(p, H("Avir_Entangled"), 600);
        }
    }
}
