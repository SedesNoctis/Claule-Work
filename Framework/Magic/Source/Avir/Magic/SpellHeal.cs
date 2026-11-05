using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The healing turn (Магия-правила.md, the author's rule): a verb, a Thread right outward of it and Restoration
    /// right outward of the Thread - "Release + Thread + Restore" - tie that one verb to the mending: its blow on the living
    /// becomes healing. The bond is local: other verbs of the circle strike as written, and another circle of the same
    /// spell is its own (Release + two Barriers in the next circle is a plain blast on foes). A Release so written is a
    /// burst of healing; a Lift lifts gently. Whom it reaches is the usual rule of the circle: everyone in it;
    /// with two Barriers on the outer edge, only foes (Selective Protection); with two Barriers and a Thread, only the
    /// caster's own (Selective Aid). A blow so turned harms nothing: no wound, no fire, no knock, no ill state; a machine
    /// has no life to mend and takes it as a blow. Hidden until first cast (SpellLore, "Thread:Heal").</summary>
    public static class SpellHeal
    {
        /// <summary>Set for the verb being applied (SpellMods.Verb); cleared at the start and end of every part.</summary>
        public static bool Now { get => now && at == GenTicks.TicksGame; set { now = value; at = GenTicks.TicksGame; } }
        private static bool now;
        private static int at = -1, depth;
        public static bool Active => Now || depth > 0;

        /// <summary>The wider the mending, the thinner it is spread (the author, Fix105): x1 / (1 + 0.5 per Breadth) - one
        /// Breadth two thirds, two a half, four a third. Set with the tied verb (SpellMods.Verb).</summary>
        public static float WidthShare(float breadth) => 1f / (1f + 0.5f * Mathf.Max(0f, breadth));
        public static float Share = 1f;
        private static float shareIn = 1f;
        public static float CurShare => depth > 0 ? shareIn : Share;

        private sealed class Scope : IDisposable
        {
            private readonly float keep;
            public Scope(float keep) { this.keep = keep; }
            public void Dispose() { if (depth > 0) depth--; shareIn = keep; }
        }
        public static IDisposable In(bool on, float share = 1f) { if (!on) return null; depth++; var s = new Scope(shareIn); shareIn = share; return s; }

        /// <summary>The verbs of this circle tied to the mending: each followed (its own modifiers between allowed) by a
        /// Thread, and that Thread at once by Restoration.</summary>
        public static HashSet<RuneDef> Bound(RitualSchema s)
        {
            var bound = new HashSet<RuneDef>();
            if (s?.rings == null) return bound;
            List<RuneDef> seq = s.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
            RuneDef verb = null;
            for (int i = 0; i < seq.Count; i++)
            {
                RuneDef x = seq[i];
                if (x.role == RuneRole.Form && x.kind == RuneKind.Restore && verb != null && i > 0 && seq[i - 1].kind == RuneKind.Thread) bound.Add(verb);
                if (x.role == RuneRole.Verb) verb = x; else if (x.role == RuneRole.Form) verb = null;
            }
            return bound;
        }

        /// <summary>Each copy of each verb in a circle, in order: tied to the mending or not. The same verb may be written
        /// tied once and plain once ("Release + Thread + Restore | Release"): only the tied copy heals.</summary>
        public static Dictionary<RuneKind, List<bool>> Copies(RitualSchema s)
        {
            var copies = new Dictionary<RuneKind, List<bool>>();
            if (s?.rings == null) return copies;
            List<RuneDef> seq = s.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
            int verbAt = -1;
            for (int i = 0; i < seq.Count; i++)
            {
                RuneDef x = seq[i];
                if (x.role == RuneRole.Form && x.kind == RuneKind.Restore && verbAt >= 0 && i > 0 && seq[i - 1].kind == RuneKind.Thread)
                { List<bool> l = copies[seq[verbAt].kind]; l[l.Count - 1] = true; }
                if (x.role == RuneRole.Verb)
                {
                    verbAt = i;
                    if (!copies.TryGetValue(x.kind, out List<bool> l)) copies[x.kind] = l = new List<bool>();
                    l.Add(false);
                }
                else if (x.role == RuneRole.Form) verbAt = -1;
            }
            return copies;
        }

        // the part being cast: its copies, and how many times each verb has been applied so far (a verb goes through
        // Verb once per copy, in the order written - and again in the same order for every form that carries it)
        private static Dictionary<RuneKind, List<bool>> part = new Dictionary<RuneKind, List<bool>>();
        private static readonly Dictionary<RuneKind, int> calls = new Dictionary<RuneKind, int>();
        public static void Start(RitualSchema s) { part = Copies(s); calls.Clear(); }

        /// <summary>Is this application of the verb its tied copy?</summary>
        public static bool Tied(RuneDef v)
        {
            if (v == null || v.role != RuneRole.Verb || !part.TryGetValue(v.kind, out List<bool> l) || l.Count == 0) return false;
            calls.TryGetValue(v.kind, out int n);
            calls[v.kind] = n + 1;
            return l[n % l.Count];
        }

        /// <summary>Who a circle's blow reaches: everyone, only foes (Selective Protection), only one's own (Selective Aid).</summary>
        public enum Side { All, Foes, Own }

        /// <summary>The spell's circles as the codex sees them: whom each reaches, whether it heals, whether it wounds.</summary>
        public static IEnumerable<(Side side, bool heals, bool harms)> Circles(RitualSchema s)
        {
            var list = new List<(RitualSchema, Side)>();
            if (s?.weave != null)
                foreach (SpellCircle k in s.weave.Working) list.Add((k.AsSchema(), SpellSculpt.AidOf(k) ? Side.Own : SpellSculpt.Of(k) ? Side.Foes : Side.All));
            else if (s != null)
            {
                SpellSculpt.Stripped(s, out int shaped, out _, out int aid);
                list.Add((s, aid > 0 ? Side.Own : shaped > 0 ? Side.Foes : Side.All));
            }
            foreach (var (c, side) in list)
            {
                var copies = Copies(c);
                bool heals = copies.Values.Any(l => l.Any(t => t));
                bool harms = copies.Any(kv => kv.Key != RuneKind.Growth && kv.Key != RuneKind.Purge && kv.Value.Any(t => !t));
                yield return (side, heals, harms);
            }
        }

        /// <summary>The codex's warnings on a spell that heals and wounds the same people - or heals the foes and wounds
        /// one's own (Arlie's words, the author's).</summary>
        public static IEnumerable<KeyValuePair<string, string>> Warnings(RitualSchema s)
        {
            var c = Circles(s).ToList();
            bool healOwn = c.Any(x => x.heals && x.side != Side.Foes), healFoes = c.Any(x => x.heals && x.side == Side.Foes);
            bool harmOwn = c.Any(x => x.harms && x.side == Side.Own);
            if (harmOwn && healFoes)
                yield return new KeyValuePair<string, string>("Mends foes, strikes own", "Arlie: \"I see you intend to confuse your enemies?\"\nThe healing here goes to the foes (Selective Protection), and a blow goes to the caster's own (Selective Aid).");
            else if (harmOwn && healOwn)
                yield return new KeyValuePair<string, string>("Mends and strikes own", "Arlie: \"Blueberry and a Poison, huh?\"\nThis spell heals the caster's own and strikes them too: a plain verb in a circle under Selective Aid goes to one's own like the healing.");
        }

        /// <summary>Does any verb of this circle (or of any circle of the spell) turn to healing?</summary>
        public static bool Turns(RitualSchema s) => Circles(s).Any(c => c.heals);

        /// <summary>What a Restoration form carries, like any form: the verbs of its circle - the tied ones heal, the rest
        /// strike as written (the author: "Release + Thread + Restore + Release" - the second Release strikes; with Selective
        /// Aid on the circle it strikes the caster's own - a badly built spell is the player's to answer for). Growth and
        /// Purge, when not tied, are Restoration's own helpers (deeper sealing, burnt infection) and are not cast. Nothing
        /// when another form carries the verbs already (they would strike twice).</summary>
        public static List<RuneDef> Carried(RitualSchema s, List<RuneDef> forms, List<RuneDef> verbs)
        {
            if (forms.Any(f => f.kind != RuneKind.Restore)) return new List<RuneDef>();
            HashSet<RuneDef> b = Bound(s);
            // Growth and Purge stay Restoration's helpers unless tied
            return verbs.Where(v => (v.kind != RuneKind.Growth && v.kind != RuneKind.Purge) || b.Contains(v)).ToList();
        }

        public static bool Living(Pawn p) => p != null && !p.Dead && p.RaceProps.IsFlesh;

        /// <summary>The healing thread (the author's concept): a Beam with a Thread right after it and Restoration right
        /// after the Thread - the beam itself is tied to the mending, and whoever it passes through is healed (whom - by the
        /// circle's rule, as any blow). Its verbs are its own business: tied, they heal; plain, they strike.</summary>
        public static bool BeamTied(RitualSchema s)
        {
            if (s?.rings == null) return false;
            List<RuneDef> seq = s.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
            for (int i = 0; i + 2 < seq.Count; i++)
                if (seq[i].kind == RuneKind.Beam && seq[i + 1].kind == RuneKind.Thread && seq[i + 2].kind == RuneKind.Restore) return true;
            return false;
        }

        /// <summary>The healing thread along its line: a mending of 8 x power on everyone it reaches, spread thinner with
        /// its Breadth.</summary>
        public static void Beam(Pawn caster, RitualSchema s, List<IntVec3> line, Map map, float power, RitualReading rf)
        {
            if (!BeamTied(s) || line == null) return;
            float share = WidthShare(rf?.breadth ?? 0);
            foreach (Pawn p in line.Where(c => c.InBounds(map)).SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().ToList())
            {
                if (!Living(p) || !MagicFx.Foe(p, caster)) continue;
                using (In(true, share)) Mend(p, 8f * power / 0.7f);
            }
            foreach (IntVec3 c in line) if (c.InBounds(map) && Rand.Chance(0.35f)) FleckMaker.ThrowMetaIcon(c, map, FleckDefOf.HealingCross);
        }

        /// <summary>Is this body under a turned blow just now - to be spared every harm of it?</summary>
        public static bool Spares(Pawn p) => Active && SpellWards.AtWork && Living(p);

        /// <summary>A blow turned to healing: its strength goes into the wounds, the worst first.</summary>
        public static void Mend(Pawn p, float amount)
        {
            if (!Living(p) || amount <= 0f) return;
            float left = amount * 0.7f * CurShare;
            foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => !i.IsPermanent()).OrderByDescending(i => i.Severity).ToList())
            {
                if (left <= 0.01f) break;
                float h = Mathf.Min(left, inj.Severity);
                inj.Heal(h);
                left -= h;
            }
            if (p.Spawned) FleckMaker.ThrowMetaIcon(p.Position, p.Map, FleckDefOf.HealingCross);
        }
    }

    /// <summary>A turned blow on a living body heals instead of wounding.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_SpellHealTurn
    {
        [HarmonyPriority(Priority.First + 1)]
        public static bool Prefix(Pawn __instance, ref DamageInfo __0, ref bool __1)
        {
            if (__1 || !AvirMagic.On || !SpellHeal.Active || !SpellWards.AtWork || !SpellHeal.Living(__instance)) return true;
            SpellHeal.Mend(__instance, __0.Amount);
            __1 = true;
            return false;
        }
    }

    /// <summary>A turned blow harms no thing: walls, items and plants are left as they were.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_SpellHealSparesThings
    {
        public static bool Prefix(Thing __instance, ref DamageWorker.DamageResult __result)
        {
            if (!SpellHeal.Active || !SpellWards.AtWork || __instance is Pawn) return true;
            __result = new DamageWorker.DamageResult();
            return false;
        }
    }

    /// <summary>A turned blow stuns no one.</summary>
    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.StunFor))]
    public static class Patch_SpellHealNoStun
    {
        public static bool Prefix(StunHandler __instance) => !(__instance.parent is Pawn p && SpellHeal.Spares(p));
    }

    /// <summary>A turned blow leaves no ill state on the body (burns, chill, sickness of the spell).</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_SpellHealNoIll
    {
        public static bool Prefix(Pawn ___pawn, Hediff hediff) => hediff == null || !hediff.def.isBad || hediff is Hediff_Injury || !SpellHeal.Spares(___pawn);
    }

    /// <summary>A turned blow sets nothing alight.</summary>
    [HarmonyPatch(typeof(FireUtility), nameof(FireUtility.TryStartFireIn))]
    public static class Patch_SpellHealNoFire
    {
        public static bool Prefix(ref bool __result)
        {
            if (!SpellHeal.Active || !SpellWards.AtWork) return true;
            __result = false;
            return false;
        }
    }
}
