using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>What a spell finds in a body it strikes (Магия-правила.md, "Тело и стихии"): its weight with all it wears
    /// and carries, whether its gear is sealed against the outside, and what of it is flesh, bionic, wooden or archotech -
    /// the eyes and the legs above all. Read only when a spell reaches the pawn, and kept a few seconds: nobody counts the
    /// eyes of every mouse on the map.</summary>
    public static class SpellBody
    {
        public enum Make { Flesh, Wood, Bionic, Archotech, Missing }

        public class Info
        {
            public int at;
            public float weight;            // kg, body and gear
            public bool sealed_;
            public bool sealedFull;         // wholly sealed: holds the void outright, or full head and body cover of a spacer's make
            public bool woodAny, bionicLimb, metalArmour;
            public float eyeDazzle = 1f;    // how much a flash blinds: flesh 1, bionic 0.5, archotech 0.1, none 0
            public float eyeSeesThrough;    // the chance to see through a mirage: archotech eyes
            public float legsLand = 1f;     // how hard a fall and a knockdown take them, by their legs
            public float flesh = 1f;        // the share of the body that is living flesh
            public bool pegLeg;
        }

        private static readonly Dictionary<int, Info> seen = new Dictionary<int, Info>();
        private const int Keep = 300;

        public static Info Of(Pawn p)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (p == null) return new Info();
            if (seen.TryGetValue(p.thingIDNumber, out Info i) && now - i.at < Keep) return i;
            if (seen.Count > 400) seen.Clear();
            i = Read(p);
            i.at = now;
            seen[p.thingIDNumber] = i;
            return i;
        }

        public static Make MakeOf(Pawn p, BodyPartRecord part)
        {
            HediffSet hs = p.health?.hediffSet;
            if (hs == null || part == null) return Make.Flesh;
            if (hs.PartIsMissing(part)) return Make.Missing;
            // an added part on it, or on a part it hangs from (a bionic arm carries its hand)
            for (BodyPartRecord q = part; q != null; q = q.parent)
            {
                Hediff_AddedPart a = hs.hediffs.OfType<Hediff_AddedPart>().FirstOrDefault(h => h.Part == q);
                if (a != null) return Kind(a);
            }
            return Make.Flesh;
        }

        private static Make Kind(Hediff_AddedPart a)
        {
            string n = a.def.defName;
            TechLevel t = a.def.spawnThingOnRemoved?.techLevel ?? TechLevel.Industrial;
            if (t == TechLevel.Archotech || n.Contains("Archotech")) return Make.Archotech;
            if (t <= TechLevel.Medieval || n.Contains("Peg") || n.Contains("Wood")) return Make.Wood;
            return Make.Bionic;
        }

        private static Info Read(Pawn p)
        {
            var i = new Info();
            i.weight = p.GetStatValue(StatDefOf.Mass) + (p.inventory != null || p.apparel != null || p.equipment != null ? MassUtility.GearAndInventoryMass(p) : 0f);
            i.sealed_ = Sealed(p, out i.sealedFull);
            HediffSet hs = p.health?.hediffSet;
            if (hs == null) return i;
            var added = hs.hediffs.OfType<Hediff_AddedPart>().ToList();
            int all = Mathf.Max(1, hs.GetNotMissingParts().Count());
            int artificial = 0;
            foreach (Hediff_AddedPart a in added)
            {
                Make m = Kind(a);
                artificial += 1 + (a.Part?.parts?.Count ?? 0);
                if (m == Make.Wood) i.woodAny = true;
                bool limb = a.Part != null && (a.Part.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore) || a.Part.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbCore));
                if (m == Make.Bionic && limb) i.bionicLimb = true;
            }
            i.flesh = Mathf.Clamp01(1f - (float)artificial / all);
            // the eyes, one by one
            var eyes = p.RaceProps.body.AllParts.Where(x => x.def.tags.Contains(BodyPartTagDefOf.SightSource)).ToList();
            if (eyes.Count > 0)
            {
                float dz = 0f, through = 0f;
                foreach (BodyPartRecord e in eyes)
                {
                    Make m = MakeOf(p, e);
                    dz += m == Make.Flesh || m == Make.Wood ? 1f : m == Make.Bionic ? 0.5f : m == Make.Archotech ? 0.1f : 0f;
                    if (m == Make.Archotech) through += 0.5f;
                }
                i.eyeDazzle = dz / eyes.Count;
                i.eyeSeesThrough = through / eyes.Count;
            }
            // the legs: bionic ones take the blow, a peg or a stump does not
            var legs = p.RaceProps.body.AllParts.Where(x => x.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore)).ToList();
            if (legs.Count > 0)
            {
                float land = 0f;
                foreach (BodyPartRecord l in legs)
                {
                    Make m = MakeOf(p, l);
                    if (m == Make.Wood) i.pegLeg = true;
                    land += m == Make.Bionic || m == Make.Archotech ? 0.7f : m == Make.Flesh ? 1f : 1.3f;
                }
                i.legsLand = land / legs.Count;
            }
            if (p.apparel != null)
                i.metalArmour = p.apparel.WornApparel.Any(a => a.Stuff != null && a.Stuff.stuffProps?.categories != null && a.Stuff.stuffProps.categories.Contains(StuffCategoryDefOf.Metallic)
                    || a.def.costList != null && a.def.costList.Any(c => c.thingDef == ThingDefOf.Plasteel || c.thingDef == ThingDefOf.Steel));
            return i;
        }

        /// <summary>Gear sealed against the outside: what holds the void (Odyssey), or full head and body cover of a
        /// spacer's make or finer.</summary>
        private static bool Sealed(Pawn p, out bool full)
        {
            full = false;
            StatDef vac = DefDatabase<StatDef>.GetNamedSilentFail("VacuumResistance");
            float v = vac != null ? p.GetStatValue(vac) : 0f;
            if (v >= 0.99f) { full = true; return true; }
            if (v >= 0.5f) return true;
            if (p.apparel == null) return false;
            bool head = false, body = false;
            foreach (Apparel a in p.apparel.WornApparel)
            {
                if (a.def.techLevel < TechLevel.Spacer) continue;
                var g = a.def.apparel?.bodyPartGroups;
                if (g == null) continue;
                if (g.Contains(BodyPartGroupDefOf.FullHead)) head = true;
                if (g.Contains(BodyPartGroupDefOf.Torso)) body = true;
            }
            full = head && body;
            return head && body;
        }

        /// <summary>Weight against a man in plain clothes (~75 kg): halved for the lightest, doubled for the heaviest.</summary>
        public static float Heavy(Pawn p) => Mathf.Clamp(Of(p).weight / 75f, 0.5f, 2f);
    }

    /// <summary>What each element's mark on the body comes to in this body. Every element has those it is strong against
    /// and those it struggles with - archotech above all.</summary>
    public static class SpellBodyEffects
    {
        private static HediffDef grit;
        public static HediffDef Grit => grit ?? (grit = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_JointGrit"));

        /// <summary>How long a magic state stays on this body.</summary>
        public static int Ticks(Pawn p, HediffDef def, int ticks)
        {
            if (p == null || def == null) return ticks;
            SpellBody.Info b = SpellBody.Of(p);
            string n = def.defName;
            if (n == "Avir_Dazzled" || n == "Avir_Hazed")
                // a flash: living eyes are blinded, a bionic one's sensor recovers twice as fast, archotech filters it, the eyeless see nothing to blind
                return b.eyeDazzle <= 0.05f ? 0 : Mathf.RoundToInt(ticks * b.eyeDazzle);
            if (n == "Avir_Dusted")
            {
                // sealed gear keeps the dust out: only the veil before it is left; open, the grit gets into bionic joints too
                if (b.sealed_) return Mathf.RoundToInt(ticks * 0.3f);
                if (b.bionicLimb && Grit != null && def != Grit) MagicFx.Give(p, Grit, ticks);
                return ticks;
            }
            if (n == "Avir_Mired" || n == "Avir_Sinking")
                // the mire draws by weight, not by armour: a heavy one sinks faster, a peg leg sinks like a stake
                return Mathf.RoundToInt(ticks * SpellBody.Heavy(p) * (b.pegLeg ? 1.25f : 1f));
            return ticks;
        }

        /// <summary>A magic blow on this body, by its element: lightning loves metal and bionics, steam gets under open
        /// plate, fire takes wood. Returns the new amount.</summary>
        public static float Blow(Pawn p, DamageDef d, float amount, RuneKind[] inc, ComboKind combo, Pawn by)
        {
            if (p == null || d == null) return amount;
            bool heat = d == DamageDefOf.Burn || d == DamageDefOf.Flame;
            if (!heat) return amount;
            SpellBody.Info b = SpellBody.Of(p);
            if (combo == ComboKind.Storm)
            {
                amount *= 1f + (b.metalArmour ? 0.5f : 0f) + (b.bionicLimb ? 0.25f : 0f);
                // bionics falter for a moment; archotech is shielded
                if (b.bionicLimb && !p.Dead && Rand.Chance(0.5f)) MagicFx.Stun(p, 40, by);
            }
            else if (combo == ComboKind.Steam) amount *= b.sealed_ ? 0.7f : 1.4f;
            if (b.woodAny && (inc.Contains(RuneKind.Fire))) amount *= 1.25f;
            return amount;
        }
    }
}
