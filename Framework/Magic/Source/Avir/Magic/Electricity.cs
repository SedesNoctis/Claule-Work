using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Electricity as a thing of the world (the author, Fix182): a strike - the sky's or a storm mage's - is a
    /// charge that runs on along what conducts it: water and wet ground, metal (walls, things, armour, metal limbs),
    /// the colony's wires, the mechanoids. It strikes whoever stands in its way, charges the batteries it reaches (and
    /// blows the net if it is too much), fries the components lying in it. Stone, wood, cloth hold it back; under a
    /// stone roof one is safe. The sky's lightning seeks the tall and the metal.
    /// Materials are known by themselves (the game's metals, any mod's metals by their category, our own by name), and
    /// the player can set any of them by hand (Mod settings - "Materials and lightning").</summary>
    public static class Electricity
    {
        public enum Cond { Auto = -1, Insulator = 0, Weak = 1, Conductor = 2, Excellent = 3 }
        public static readonly string[] CondNames = { "insulator", "weak", "conductor", "excellent" };

        /// <summary>How a material (or a thing's own def) conducts, 0..3.</summary>
        public static int Of(ThingDef d)
        {
            if (d == null) return 0;
            if (AvirMod.Settings?.condOverrides != null && AvirMod.Settings.condOverrides.TryGetValue(d.defName, out int o) && o >= 0) return o;
            return Auto(d);
        }

        public static int Auto(ThingDef d)
        {
            if (d == null) return 0;
            string n = d.defName;
            if (n.Contains("Tisserium")) return 3;
            if (n.IndexOf("Akhar", System.StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (n.Contains("Essence")) return 1;
            if (n == "Gold" || n == "Silver" || n.EndsWith("_Gold") || n.EndsWith("_Silver")) return 3;
            if (n == "Uranium") return 2;
            if (n.Contains("Coal") || n.Contains("Charcoal")) return 1;   // coal conducts a little (Medieval Overhaul's coal, any)
            if (d.IsStuff && d.stuffProps?.categories != null && d.stuffProps.categories.Contains(StuffCategoryDefOf.Metallic)) return 2;   // any metal - the game's, Medieval Overhaul's iron, any mod's
            if (n.StartsWith("Component")) return 2;
            if (!d.IsStuff && d.IsMetal) return 2;
            return 0;
        }

        /// <summary>How a thing conducts: built of a stuff - by it; a wire - well; else by its own def or its costs.</summary>
        public static int OfThing(Thing t)
        {
            if (t == null) return 0;
            if (t.TryGetComp<CompPower>() is CompPowerTransmitter) return 2;
            if (t.Stuff != null) return Of(t.Stuff);
            int own = Of(t.def);
            if (own > 0 || t.def.costList == null) return own;
            return t.def.costList.Any(c => Of(c.thingDef) >= 2) ? 2 : 0;
        }

        /// <summary>A metal limb, beak, eye: a part added to the body that is made of (or costs) metal - the game's
        /// bionics, our iron beak, any mod's iron limb alike. Archotech is shielded.</summary>
        public static bool MetalPart(Hediff h)
        {
            if (!(h is Hediff_AddedPart) || h.def.defName.Contains("Archotech")) return false;
            ThingDef th = h.def.spawnThingOnRemoved;
            if (th == null) return h.def.defName.Contains("Bionic") || h.def.defName.Contains("Iron") || h.def.defName.Contains("Steel");
            return Of(th) >= 2 || (th.costList?.Any(c => Of(c.thingDef) >= 2) ?? false) || th.defName.Contains("Bionic");
        }

        /// <summary>How a body conducts: a mechanoid as metal; others by the metal they wear and are fitted with, and wet.</summary>
        public static float OfPawn(Pawn p)
        {
            if (p.RaceProps.IsMechanoid) return 2f;
            float f = 0f;
            if (p.apparel != null) f += Mathf.Min(1f, 0.5f * p.apparel.WornApparel.Count(a => OfThing(a) >= 2));
            // every metal part fitted into the body is a conductor in it (Fix227, the author: bionics are a good conductor -
            // the more of them, the harder the charge goes through): +0.35 each, up to +1
            f += Mathf.Min(1f, 0.35f * p.health.hediffSet.hediffs.Count(MetalPart));
            if (WaterDefOf.Avir_Soaked != null && p.health.hediffSet.HasHediff(WaterDefOf.Avir_Soaked)) f += 0.7f;
            return f;
        }

        /// <summary>Does a cell carry the charge on: water, metal standing or lying, a wire, a mechanoid.</summary>
        public static int CellCond(IntVec3 c, Map map)
        {
            int best = Reactions.Wet(c, map) ? 2 : 0;
            foreach (Thing t in c.GetThingList(map))
            {
                int k = t is Pawn p ? (p.RaceProps.IsMechanoid ? 2 : 0) : (t is Building || t.def.EverHaulable) ? OfThing(t) : 0;
                if (k > best) best = k;
            }
            return best;
        }

        /// <summary>The charge runs on from a cell along what conducts, weaker with each step (less through the best
        /// conductors), and acts where it goes. spare - a mage's own spell spares the mage's own (as Conduct did).</summary>
        public static void Spread(IntVec3 from, Map map, float charge, Pawn caster, bool spare)
        {
            if (map == null || !from.InBounds(map) || charge <= 0.05f) return;
            var level = new Dictionary<IntVec3, float> { [from] = charge };
            var open = new Queue<IntVec3>(); open.Enqueue(from);
            while (open.Count > 0 && level.Count < 160)
            {
                IntVec3 c = open.Dequeue();
                float here = level[c];
                if (here < 0.08f) continue;
                foreach (IntVec3 n in GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)))
                {
                    if (!n.InBounds(map) || level.ContainsKey(n)) continue;
                    int k = CellCond(n, map);
                    if (k <= 0) continue;
                    level[n] = here * (k >= 3 ? 0.97f : k == 2 ? 0.93f : 0.8f);
                    open.Enqueue(n);
                }
            }
            // the charge running on crackles (Fix225) - once where it starts, and once more on a long run
            if (level.Count > 1) MagicSounds.Play("Arc", from, map, 1f, 4);
            if (level.Count > 30) MagicSounds.Play("Arc", level.Keys.RandomElement(), map, 0.7f, 0);
            var batteries = new HashSet<Building>();
            foreach (var kv in level)
            {
                IntVec3 c = kv.Key; float q = kv.Value;
                if (Rand.Chance(0.12f)) FleckMaker.ThrowLightningGlow(c.ToVector3Shifted(), map, 0.6f + q * 0.3f);
                if (Rand.Chance(0.08f)) MapComponent_SpellSprites.Put(map, "Storm/Bolt_Fork", c.ToVector3Shifted(), 1.4f, 14);
                foreach (Thing t in c.GetThingList(map).ToList())
                {
                    if (t is Pawn p) { Shock(p, q, caster, spare); continue; }
                    if (t is Building b && b.TryGetComp<CompPowerBattery>() != null) batteries.Add(b);
                    else if (t.def.EverHaulable && t.def.defName.StartsWith("Component") && q > 1f && Rand.Chance(0.1f)) { FleckMaker.ThrowMicroSparks(t.DrawPos, map); t.Destroy(); }
                }
            }
            foreach (Building b in batteries)
            {
                CompPowerBattery bat = b.TryGetComp<CompPowerBattery>();
                float q = level.TryGetValue(b.Position, out float lv) ? lv : charge * 0.5f;
                float give = 250f * q;
                if (bat.AmountCanAccept < give * 0.5f && q > 1.2f && Rand.Chance(0.3f)) { ShortCircuitUtility.DoShortCircuit(b); continue; }   // too much at once
                bat.AddEnergy(Mathf.Min(bat.AmountCanAccept, give));
                FleckMaker.ThrowMicroSparks(b.TrueCenter(), map);
            }
        }

        /// <summary>The charge in a body: a burn and a jolt, more the better it conducts; a mechanoid takes it as an EMP;
        /// metal limbs are struck themselves.</summary>
        public static void Shock(Pawn p, float q, Pawn caster, bool spare)
        {
            if (p.Dead) return;   // the mage too, if the charge reaches them (Fix190)
            if (spare && caster != null && !MagicFx.Foe(p, caster)) return;
            Hit(p, 4f * q, caster, 40);   // Fix227: the common rule
        }

        /// <summary>How much harder a charge strikes this body than a dry, bare one (Fix227): wet, metal worn, metal fitted
        /// in - from x1 up to about x2.5.</summary>
        public static float Factor(Pawn p) => 1f + 0.5f * OfPawn(p);

        /// <summary>A bolt or a discharge into a body (Fix227: one rule for the storm, the ball lightning and the current).
        /// dmg - for a dry bare body. A machine takes it as an EMP; metal fitted into the body burns at its place, and
        /// bionics stutter for a while.</summary>
        public static void Hit(Pawn p, float dmg, Pawn caster, int stun)
        {
            if (p == null || p.Dead) return;
            if (p.RaceProps.IsMechanoid) { p.TakeDamage(new DamageInfo(DamageDefOf.EMP, dmg * 1.4f, 0f, -1f, caster)); p.TakeDamage(new DamageInfo(DamageDefOf.Burn, dmg * 0.3f, 0f, -1f, caster)); return; }
            float f = Factor(p);
            p.TakeDamage(new DamageInfo(DamageDefOf.Burn, dmg * f, 0f, -1f, caster));
            var metal = p.health.hediffSet.hediffs.Where(MetalPart).Where(h => h.Part != null).Take(3).ToList();
            foreach (Hediff h in metal) if (!p.Dead) p.TakeDamage(new DamageInfo(DamageDefOf.Burn, dmg * 0.4f, 99f, -1f, caster, h.Part));
            if (p.Dead) return;
            if (metal.Count > 0) { HediffDef gl = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Glitched"); if (gl != null) MagicFx.Give(p, gl, 300 + 150 * metal.Count); }
            if (stun > 0) MagicFx.Stun(p, Mathf.RoundToInt(stun * f), caster);
        }

        /// <summary>Where the sky's lightning goes: near where it was to strike, the tallest and most metal thing under
        /// the open sky draws it (a lightning rod most of all - Fix183).</summary>
        public static IntVec3 Draw(IntVec3 at, Map map)
        {
            IntVec3 best = at; float bestScore = 0f;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 9f, true))
            {
                if (!c.InBounds(map) || c.Roofed(map)) continue;
                float sc = 0f;
                Building e = c.GetEdifice(map);
                if (e != null) { sc += OfThing(e) * 1.5f + (e.def.Fillage == FillCategory.Full ? 1f : 0f); if (e.def.defName.Contains("LightningRod")) sc += 20f; }
                foreach (Pawn p in c.GetThingList(map).OfType<Pawn>()) sc += OfPawn(p);
                Plant pl = c.GetPlant(map); if (pl != null && pl.def.plant.IsTree) sc += 0.8f * pl.Growth;
                sc -= c.DistanceTo(at) * 0.15f;
                if (sc > bestScore) { bestScore = sc; best = c; }
            }
            return bestScore >= 1.5f && Rand.Chance(0.75f) ? best : at;
        }
    }

    /// <summary>The sky's lightning: it seeks the tall and the metal, and its charge runs on (Fix182).</summary>
    [HarmonyPatch(typeof(WeatherEvent_LightningStrike), nameof(WeatherEvent_LightningStrike.DoStrike))]
    public static class Patch_LightningStrike
    {
        public static void Prefix(ref IntVec3 strikeLoc, Map map)
        {
            if (!AvirMagic.On || map == null || !strikeLoc.IsValid || AvirMod.Settings != null && !AvirMod.Settings.electricity) return;
            strikeLoc = Electricity.Draw(strikeLoc, map);
        }

        public static void Postfix(IntVec3 strikeLoc, Map map)
        {
            if (!AvirMagic.On || map == null || !strikeLoc.IsValid || AvirMod.Settings != null && !AvirMod.Settings.electricity) return;
            Electricity.Spread(strikeLoc, map, 1.6f, null, false);
        }
    }

    /// <summary>Materials and lightning: every stuff of the game and the mods, how it conducts - by itself, or set by hand.</summary>
    public class Dialog_Conductivity : Window
    {
        private Vector2 scroll; private string filter = "";
        public override Vector2 InitialSize => new Vector2(620f, 640f);
        public Dialog_Conductivity() { doCloseX = true; absorbInputAroundWindow = true; closeOnClickedOutside = true; }

        public override void DoWindowContents(Rect r)
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(r.x, r.y, r.width, 32f), "Materials and lightning"); Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x, r.y + 34f, r.width, 40f), "How each material conducts a charge. \"Auto\" - found by itself (metals by their category, ours by name). Click to set it by hand.");
            filter = Widgets.TextField(new Rect(r.x, r.y + 78f, 240f, 26f), filter);
            var all = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsStuff || d.defName.StartsWith("Component")).Where(d => filter.NullOrEmpty() || d.label.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(d => d.label).ToList();
            Rect outR = new Rect(r.x, r.y + 112f, r.width, r.height - 112f), view = new Rect(0f, 0f, outR.width - 20f, all.Count * 28f);
            var ov = AvirMod.Settings.condOverrides ?? (AvirMod.Settings.condOverrides = new Dictionary<string, int>());
            Widgets.BeginScrollView(outR, ref scroll, view);
            float y = 0f;
            foreach (ThingDef d in all)
            {
                Rect row = new Rect(0f, y, view.width, 26f);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                Widgets.DefIcon(new Rect(row.x, row.y + 1f, 24f, 24f), d);
                Widgets.Label(new Rect(row.x + 30f, row.y + 3f, 300f, 24f), d.LabelCap);
                int cur = ov.TryGetValue(d.defName, out int v) ? v : -1;
                string txt = cur < 0 ? "Auto (" + Electricity.CondNames[Electricity.Auto(d)] + ")" : Electricity.CondNames[cur];
                if (Widgets.ButtonText(new Rect(row.xMax - 200f, row.y, 200f, 26f), txt))
                {
                    int next = cur + 1; if (next > 3) next = -1;
                    if (next < 0) ov.Remove(d.defName); else ov[d.defName] = next;
                }
                y += 28f;
            }
            Widgets.EndScrollView();
        }
    }
}
