using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The ladder of Life (the author, after Fix164): Life with ONE small circle of an element is the wide
    /// meeting - the body's own share of that element, found in all that lives; with TWO or more of the same element it
    /// is the deep one (Wild Flame, Blood, Emerald, Pestilence - Fix130-160, as they were). The wide meetings:
    ///   Warmth (fire) - the body's heat; Sap (water) - its fluids; Frame (earth) - what it stands on; Breath (air).
    /// They keep plain Life's store and its grammar (Withering strikes and takes, Restoration mends and gives), and add
    /// their own to each body reached. Nothing in them is plainly harm or plainly good (the author's rule of ambiguity):
    /// Withering of Warmth puts out a burning friend, Withering of Breath leaves a foe in a gas mask untouched.</summary>
    public static partial class LifeMagic
    {
        /// <summary>The wide meeting of a Life box: one small circle of an element inside it (None - none, or the deep one).</summary>
        public static RuneKind AspectOf(SpellCircle root, SpellCircle box)
        {
            if (box == null || !box.Great || !Is(box.element)) return None;
            var inside = box.inner.Where(k => !k.Great).ToList();
            if (box.OwnRunes.Concat(inside.SelectMany(k => k.OwnRunes)).Any(x => x.kind == RuneKind.Closing)) return None;
            SpellCircle first = inside.FirstOrDefault(k => Elemental(k));
            if (first == null) return None;
            return inside.Count(k => k.element == first.element) >= 2 ? None : first.element.kind;
        }

        private static bool Elemental(SpellCircle k) => k.element != null && (k.element.kind == RuneKind.Fire || k.element.kind == RuneKind.Water || k.element.kind == RuneKind.Earth || k.element.kind == RuneKind.Air);

        public static string AspectName(RuneKind k) => k == RuneKind.Fire ? "Warmth" : k == RuneKind.Water ? "Sap" : k == RuneKind.Earth ? "Frame" : k == RuneKind.Air ? "Breath" : null;
        public static Color AspectColor(RuneKind k) => k == RuneKind.Fire ? new Color(1f, 0.6f, 0.28f) : k == RuneKind.Water ? new Color(0.45f, 0.8f, 0.72f) : k == RuneKind.Earth ? new Color(0.86f, 0.8f, 0.64f) : k == RuneKind.Air ? new Color(0.74f, 0.88f, 1f) : Necrotic;
        public static string AspectText(RuneKind k)
        {
            switch (k)
            {
                case RuneKind.Fire: return "Warmth - the body's own heat. Withering takes it: the reached are chilled, slow, hardly hungry; fire on them and round them goes out (a burning friend can be saved by it). Restoration gives it: quicker mending, quicker steps, more hunger, no fear of cold. Withering with Hold - torpor: the held sleep, not dying, not starving. Lift - a warm updraft: a fall does not maim; Lift with a Thread - a cold weight. Purge - the heat drives infection out. Growth - the green ripens a little quicker.";
                case RuneKind.Water: return "Sap - the fluids of all that lives, plants as much as creatures. Withering dries the reached: thirst, weakness, the green wilts. Restoration waters them: burns mend first, poison is washed out, the green swells. Restoration with Hold - a cocoon of water. Lift - the sap rises in the plants, they ripen; Lift with a Thread - the creatures sink and wade. Growth - the green swells.";
                case RuneKind.Earth: return "Frame - what the body stands on. Withering softens it: the reached are slower and take blows harder; with Hold they cannot get up. Restoration hardens it: a little armour for a time; with Hold - a splint, the body mends deep. Lift - the felled stand up, the stunned come to; Lift with a Thread - rooted: slow, but hard to knock over. Purge - bruises and crushes mend. Growth - bark and hide: the animals harden.";
                case RuneKind.Air: return "Breath. Withering takes it from the reached - those sealed from the air (a gas mask, a sealed suit) are not touched; with Hold, held breath - no running. Restoration gives it: easier breathing, the smoke and gas cleared out of the lungs; with Hold, a bubble of air that keeps off gas and smoke. Lift - a deep breath: a burst of speed; Lift with a Thread - a heavy breath out: they cough. Purge - the lungs cleared of what was breathed in. Growth - pollen: the green ripens.";
            }
            return null;
        }

        private static RuneKind aspect = None;
        private static RuneKind curVerb = (RuneKind)(-1);
        private static HediffDef HD(string n) => DefDatabase<HediffDef>.GetNamedSilentFail(n);

        /// <summary>What a wide meeting does to one body a Withering reached; the plain withering is then at 60%.</summary>
        private static void AspectWither(Pawn caster, Pawn p, float dmg)
        {
            if (p.Spawned) MapComponent_SpellSprites.PutTinted(p.Map, aspect == RuneKind.Fire ? "Life/Petals" : aspect == RuneKind.Water ? "Life/BloodHold" : aspect == RuneKind.Earth ? "Life/StoneCrust" : "Life/PoisonFog", Color.Lerp(AspectColor(aspect), Color.black, 0.35f), p.DrawPos, 1.2f, 55);
            int ticks = Mathf.RoundToInt(900f * Mathf.Max(0.5f, dmg / 6f));
            switch (aspect)
            {
                case RuneKind.Fire:
                    MagicFx.Give(p, HD("Avir_LifeChill"), ticks);
                    FireMagic.Extinguish(p.Position, p.Map);
                    foreach (Fire f in p.Map.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>().Where(f => f.parent == p).ToList()) f.Destroy();
                    Hediff hyp = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Heatstroke); if (hyp != null) hyp.Severity -= 0.2f;
                    if (curVerb == RuneKind.Hold) MagicFx.Give(p, HD("Avir_LifeTorpor"), Mathf.RoundToInt(2500f * Mathf.Max(0.5f, dmg / 3f)));
                    break;
                case RuneKind.Water: MagicFx.Give(p, HD("Avir_LifeDried"), ticks); break;
                case RuneKind.Earth:
                    MagicFx.Give(p, HD("Avir_LifeSoftened"), ticks);
                    if (curVerb == RuneKind.Hold && p.Spawned) MagicFx.Fell(p, p.Position, 0, p.Map, caster, dmg / 3f, 240);
                    break;
                case RuneKind.Air:
                    if (SecondOrder.Masked(p)) break;   // sealed from the air: the breath is not there to take
                    MagicFx.Give(p, HD(curVerb == RuneKind.Hold ? "Avir_LifeBreathHeld" : "Avir_LifeBreathless"), ticks);
                    break;
            }
        }

        /// <summary>What a wide meeting gives to the bodies a Restoration reached (on top of plain mending).</summary>
        private static void AspectGive(List<Pawn> who, RuneKind verb, float pw, Map map, IntVec3 at, float r)
        {
            int ticks = Mathf.RoundToInt(1500f * Mathf.Max(0.5f, pw));
            AspectPlants(verb, pw, map, at, r);
            AspectPicture(map, at, r, who, false);
            foreach (Pawn p in who)
            {
                switch (aspect)
                {
                    case RuneKind.Fire:
                        if (verb == RuneKind.Purge) { Hediff inf = p.health.hediffSet.hediffs.FirstOrDefault(h => h.def.defName == "WoundInfection"); if (inf != null) inf.Severity -= 0.2f * pw; }   // the heat drives infection out
                        MagicFx.Give(p, HD("Avir_LifeWarmth"), ticks * (verb == RuneKind.Hold ? 2 : 1));
                        Hediff hy = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Hypothermia); if (hy != null) hy.Severity -= 0.3f * pw;
                        RemoveOf(p, HD("Avir_LifeChill"));
                        break;
                    case RuneKind.Water:
                        foreach (Hediff_Injury b in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.def.defName == "Burn").ToList()) b.Heal(4f * pw);
                        Hediff tox = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup); if (tox != null) tox.Severity -= 0.1f * pw;
                        RemoveOf(p, HD("Avir_LifeDried"));
                        if (verb == RuneKind.Hold) AddRegen(p, 0.2f * pw);
                        break;
                    case RuneKind.Earth:
                        MagicFx.Give(p, HD("Avir_LifeHardened"), ticks);
                        RemoveOf(p, HD("Avir_LifeSoftened"));
                        if (verb == RuneKind.Purge) foreach (Hediff_Injury b in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.def.defName == "Bruise" || i.def.defName == "Crush").ToList()) b.Heal(4f * pw);   // the blows struck into the frame
                        if (verb == RuneKind.Hold) AddRegen(p, 0.35f * pw);   // a splint: the body mends deep
                        break;
                    case RuneKind.Air:
                        MagicFx.Give(p, HD("Avir_LifeBreath"), ticks);
                        if (verb == RuneKind.Purge) { Hediff tx = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup); if (tx != null) tx.Severity -= 0.15f * pw; }   // the lungs cleared of what was breathed in
                        RemoveOf(p, HD("Avir_LifeBreathless")); RemoveOf(p, HD("Avir_LifeBreathHeld"));
                        if (verb == RuneKind.Hold) MagicFx.Give(p, HD("Avir_LifeAirBubble"), ticks);
                        break;
                }
            }
        }

        /// <summary>Lifting in the wide meetings (Fix166) - not the same throw four times over, each by its nature:
        ///   Warmth - a warm updraft: a fall from a height does not maim; with a Thread, a cold weight: they freeze to the ground.
        ///   Sap - the sap rises: the plants reach up and ripen; with a Thread it grows heavy: the creatures sink and wade.
        ///   Frame - the body draws itself up: the felled stand, the stunned come to; with a Thread it is rooted: slow, hard to fell.
        ///   Breath - a deep breath: a burst of speed; with a Thread, a heavy breath out: coughing, the aim lost.</summary>
        private static void AspectLift(List<Pawn> who, bool press, float pw, Map map, IntVec3 at, float r)
        {
            int ticks = Mathf.RoundToInt(900f * Mathf.Max(0.5f, pw));
            switch (aspect)
            {
                case RuneKind.Fire: foreach (Pawn p in who) MagicFx.Give(p, HD(press ? "Avir_LifeChill" : "Avir_Updraft"), ticks); break;
                case RuneKind.Water:
                    if (press) foreach (Pawn p in who) MagicFx.Give(p, HD("Avir_Mired"), ticks / 2);
                    else foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true)) { Plant pl = c.InBounds(map) ? c.GetPlant(map) : null; if (pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.2f * pw); }
                    break;
                case RuneKind.Earth:
                    foreach (Pawn p in who)
                    {
                        if (press) { MagicFx.Give(p, HD("Avir_LifeRooted"), ticks); continue; }
                        p.stances?.stunner?.StopStun();
                        RemoveOf(p, HD("Avir_LifeSoftened"));
                    }
                    break;
                case RuneKind.Air: foreach (Pawn p in who) { if (press && SecondOrder.Masked(p)) continue; MagicFx.Give(p, HD(press ? "Avir_LifeBreathless" : "Avir_LifeGasp"), press ? ticks : ticks / 3); } break;
            }
            AspectPicture(map, at, r, who, press);
        }

        /// <summary>Growth in the wide meetings: the green answers each its own way.</summary>
        private static void AspectPlants(RuneKind verb, float pw, Map map, IntVec3 at, float r)
        {
            if (verb != RuneKind.Growth || map == null) return;
            float more = aspect == RuneKind.Water ? 0.15f : aspect == RuneKind.Air ? 0.1f : aspect == RuneKind.Fire ? 0.08f : 0f;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true))
            {
                if (!c.InBounds(map)) continue;
                Plant pl = c.GetPlant(map);
                if (more > 0f && pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + more * pw);
                // Frame: bark and hide - the animals there harden
                if (aspect == RuneKind.Earth) foreach (Pawn a in c.GetThingList(map).OfType<Pawn>().Where(a => a.RaceProps.Animal).ToList()) MagicFx.Give(a, HD("Avir_LifeHardened"), Mathf.RoundToInt(2500f * pw));
            }
        }

        /// <summary>The wide meetings' pictures: Life's own, recoloured and combined (the author: no new assets for Life).</summary>
        private static void AspectPicture(Map map, IntVec3 at, float r, List<Pawn> who, bool dark)
        {
            if (map == null || aspect == None) return;
            Color col = AspectColor(aspect); if (dark) col = Color.Lerp(col, Color.black, 0.35f);
            string pic = aspect == RuneKind.Fire ? "Life/Petals" : aspect == RuneKind.Water ? "Life/BloodHold" : aspect == RuneKind.Earth ? "Life/StoneCrust" : "Life/PoisonFog";
            MapComponent_SpellSprites.PutTinted(map, pic, col, at.ToVector3Shifted(), Mathf.Max(1.5f, r * 1.6f), 70, -1f, aspect == RuneKind.Air ? 0.4f : 1f);
            foreach (Pawn p in who.Take(8)) if (p.Spawned) MapComponent_SpellSprites.PutTinted(map, aspect == RuneKind.Fire ? "Life/Heart" : "Life/Mend", col, p.DrawPos, 0.9f, 50);
        }

        /// <summary>Warmth taken over an area puts the fires there out (Release, a bare Withering, the Beam's line).</summary>
        private static void AspectArea(IEnumerable<IntVec3> cells, Map map)
        {
            if (aspect != RuneKind.Fire) return;
            foreach (IntVec3 c in cells) if (c.InBounds(map)) FireMagic.Extinguish(c, map);
        }

        private static void RemoveOf(Pawn p, HediffDef d) { Hediff h = d != null ? p.health.hediffSet.GetFirstHediffOfDef(d) : null; if (h != null) p.health.RemoveHediff(h); }
    }
}
