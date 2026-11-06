using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The mixed triples of Life (the author, Fix193): Life with two small circles of DIFFERENT elements in its box
    /// - the meeting of two levels of the body, each from two wide meetings:
    ///   Fever  (fire + water: Warmth + Sap)    - fever as a weapon and as a cure;
    ///   Forge  (fire + earth: Warmth + Frame)  - bone forged by heat;
    ///   Ardour (fire + air: Warmth + Breath)   - hot breath, the heat of the spirit;
    ///   Humus  (water + earth: Sap + Frame)    - decay turned to soil;
    ///   Nerve  (water + air: Sap + Breath)     - the current in the body, as lightning in a storm;
    ///   Decay  (earth + air: Frame + Breath)   - time on the body.
    /// The direct side only; the distorted side (the undead of each) waits for Darkness. Nothing in them is plainly harm or
    /// plainly good (the rule of ambiguity): Withering of Ardour puts a raging friend to sleep, Restoration of Fever is a
    /// sweat that breaks the sickness. They keep Life's store and grammar, like the wide meetings.</summary>
    public enum LifeMix { None, Fever, Forge, Ardour, Humus, Nerve, Decay }

    public static partial class LifeMagic
    {
        private static LifeMix mix = LifeMix.None;

        /// <summary>A Life box with exactly two small circles inside, of two different elements, neither doubled.</summary>
        public static LifeMix MixOf(SpellCircle root, SpellCircle box)
        {
            if (box == null || !box.Great || !Is(box.element)) return LifeMix.None;
            var inside = box.inner.Where(k => !k.Great).ToList();
            if (box.OwnRunes.Concat(inside.SelectMany(k => k.OwnRunes)).Any(x => x.kind == RuneKind.Closing)) return LifeMix.None;
            var els = inside.Where(Elemental).Select(k => k.element.kind).ToList();
            var kinds = els.Distinct().ToList();
            if (kinds.Count != 2 || els.Count != 2) return LifeMix.None;
            return Mix(kinds[0], kinds[1]);
        }

        public static LifeMix Mix(RuneKind a, RuneKind b)
        {
            bool Has(RuneKind x) => a == x || b == x;
            if (Has(RuneKind.Fire) && Has(RuneKind.Water)) return LifeMix.Fever;
            if (Has(RuneKind.Fire) && Has(RuneKind.Earth)) return LifeMix.Forge;
            if (Has(RuneKind.Fire) && Has(RuneKind.Air)) return LifeMix.Ardour;
            if (Has(RuneKind.Water) && Has(RuneKind.Earth)) return LifeMix.Humus;
            if (Has(RuneKind.Water) && Has(RuneKind.Air)) return LifeMix.Nerve;
            if (Has(RuneKind.Earth) && Has(RuneKind.Air)) return LifeMix.Decay;
            return LifeMix.None;
        }

        public static string MixName(LifeMix m) => m == LifeMix.None ? null : m.ToString();

        public static RuneKind[] MixParts(LifeMix m) =>
            m == LifeMix.Fever ? new[] { RuneKind.Fire, RuneKind.Water } : m == LifeMix.Forge ? new[] { RuneKind.Fire, RuneKind.Earth } :
            m == LifeMix.Ardour ? new[] { RuneKind.Fire, RuneKind.Air } : m == LifeMix.Humus ? new[] { RuneKind.Water, RuneKind.Earth } :
            m == LifeMix.Nerve ? new[] { RuneKind.Water, RuneKind.Air } : m == LifeMix.Decay ? new[] { RuneKind.Earth, RuneKind.Air } : new RuneKind[0];

        public static Color MixColor(LifeMix m) =>
            m == LifeMix.Fever ? new Color(1f, 0.55f, 0.45f) : m == LifeMix.Forge ? new Color(0.95f, 0.5f, 0.2f) : m == LifeMix.Ardour ? new Color(1f, 0.35f, 0.3f) :
            m == LifeMix.Humus ? new Color(0.5f, 0.42f, 0.25f) : m == LifeMix.Nerve ? new Color(0.55f, 0.85f, 1f) : m == LifeMix.Decay ? new Color(0.72f, 0.68f, 0.55f) : Necrotic;

        public static string MixText(LifeMix m)
        {
            switch (m)
            {
                case LifeMix.Fever: return "Fever (Life with fire and water: Warmth and Sap) - fever as a weapon and as a cure. Withering drives a fever into the reached: weak, clumsy, dull; with Hold they stew in their own sweat. Restoration gives a healing sweat: the body learns its sicknesses quicker, poison sweats out; with Hold - a steam room, quicker still. Purge - the sickness is driven out: the immunity jumps. Beam - steam from the pores of all on the line, a burn from inside.";
                case LifeMix.Forge: return "Forge (Life with fire and earth: Warmth and Frame) - bone forged by heat. Withering bakes the joints: slow, the hands do not obey; with Hold the joints fuse while it holds. Restoration forges back what was lost - a finger, a toe, an ear; at mastery a whole limb, dear and slow; with Hold - a forge-bed: the wounded lie in its heat and the bone grows deep. Beam - the bone of the first on the line glows red.";
                case LifeMix.Ardour: return "Ardour (Life with fire and air: Warmth and Breath) - the heat of the spirit. Restoration gives ardour: no tiredness, no fear for a time - then the reckoning (the more ardour, the heavier); it ends panic and terror. Withering blows the ardour out: apathy - the weapon may fall from the hands; with Hold - sleep.";
                case LifeMix.Humus: return "Humus (Life with water and earth: Sap and Frame) - decay turned to soil. Withering makes flesh fester (wounds take infection) and turns the dead in reach into fertile ground. Restoration grows the new out of the humus: wounds cleaned, the ground richer; with Hold - compost: the green round grows fast. Withering with Hold - a mire of bodies: they wade.";
                case LifeMix.Nerve: return "Nerve (Life with water and air: Sap and Breath) - the current in the body. Withering - a spasm: stunned and trembling, the aim lost; with Hold - shaking, the hands useless. Restoration starts the heart: the fallen stand up (pain dulled), the dying stop dying - not the dead; with Hold it keeps the dying alive while it holds.";
                case LifeMix.Decay: return "Decay (Life with earth and air: Frame and Breath) - time on the body. Withering ages the reached, slowly and dearly; with Hold the body crumbles (its own wound); Beam - what they wear wears out. Restoration makes young - only at mastery, very dear, a little at a time; with Hold it holds the age: the held do not grow older while it holds.";
            }
            return null;
        }

        /// <summary>Mastery of a mixed triple: Life with either of its elements at the fourth stage.</summary>
        private static bool MixMaster(Pawn mage)
        {
            Hediff_ArcaneCore c = Mages.Core(mage);
            if (c == null) return false;
            return MixParts(mix).Any(e => Arcane.Stage(c, RuneKind.Life, e) >= 4);
        }

        /// <summary>What a mixed triple does to one body a Withering reached (the plain withering is then at 60%).</summary>
        private static void MixWither(Pawn caster, Pawn p, float dmg)
        {
            if (p.Spawned) MixPicture(p.Map, p.DrawPos, 1.1f, true);
            int ticks = Mathf.RoundToInt(900f * Mathf.Max(0.5f, dmg / 6f));
            bool hold = curVerb == RuneKind.Hold, beam = curVerb == RuneKind.Beam;
            switch (mix)
            {
                case LifeMix.Fever:
                    MagicFx.Give(p, HD(hold ? "Avir_LifeFeverBoil" : "Avir_LifeFever"), ticks);
                    if (hold) { Hediff hs = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Heatstroke) ?? p.health.AddHediff(HediffDefOf.Heatstroke); hs.Severity += 0.06f * dmg / 6f; }
                    if (beam) p.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamedSilentFail("Avir_InnerBoil") ?? DamageDefOf.Burn, dmg * 0.5f, 2f, -1f, caster));   // steam from the pores - its own wound (Fix199)
                    break;
                case LifeMix.Forge:
                    MagicFx.Give(p, HD(hold ? "Avir_LifeFused" : "Avir_LifeBaked"), ticks);
                    if (beam && !mixBeamHit) { mixBeamHit = true; p.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamedSilentFail("Avir_BoneHeat") ?? DamageDefOf.Burn, dmg, 2f, -1f, caster)); }   // the first on the line - the bone glows (Fix199)
                    break;
                case LifeMix.Ardour:
                    if (hold && p.RaceProps.IsFlesh) { Hediff an = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic) ?? p.health.AddHediff(HediffDefOf.Anesthetic); an.Severity = Mathf.Max(an.Severity, 0.5f); break; }
                    MagicFx.Give(p, HD("Avir_LifeApathy"), ticks);
                    RemoveOf(p, HD("Avir_LifeArdour"));
                    if (p.equipment?.Primary != null && p.Spawned && Rand.Chance(Mathf.Clamp01(0.25f * dmg / 6f))) p.equipment.TryDropEquipment(p.equipment.Primary, out _, p.Position);
                    if (p.InMentalState && p.MentalStateDef != null && p.MentalStateDef.IsAggro) p.MentalState.RecoverFromState();   // a rage blown out
                    break;
                case LifeMix.Humus:
                    if (hold) { MagicFx.Give(p, ComboDefOf.Avir_Mired, ticks / 3); break; }
                    var raw = p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => !i.IsPermanent() && !i.IsTended() && i.Part != null).ToList();
                    if (raw.Count > 0 && Rand.Chance(Mathf.Clamp01(0.3f * dmg / 6f))) { Hediff_Injury w = raw.RandomElement(); p.health.AddHediff(HediffDefOf.WoundInfection, w.Part); }
                    break;
                case LifeMix.Nerve:
                    MagicFx.Stun(p, Mathf.RoundToInt(80f * dmg / 6f), caster);
                    MagicFx.Give(p, HD(hold ? "Avir_LifeShaking" : "Avir_LifeTremor"), ticks);
                    break;
                case LifeMix.Decay:
                    if (hold) { DamageDef crumble = DefDatabase<DamageDef>.GetNamedSilentFail("Avir_Crumble"); if (crumble != null) p.TakeDamage(new DamageInfo(crumble, dmg * 0.6f, 0f, -1f, caster)); }
                    if (beam && p.apparel != null) foreach (Apparel a in p.apparel.WornApparel.ToList()) if (a.def.useHitPoints) a.HitPoints = Mathf.Max(1, a.HitPoints - Mathf.RoundToInt(a.MaxHitPoints * 0.1f * dmg / 6f));
                    if (p.ageTracker != null) p.ageTracker.AgeBiologicalTicks += (long)(GenDate.TicksPerYear * 0.08f * dmg / 6f);   // slowly, dearly
                    Desiccation.Dry(p, 0.18f * dmg / 6f, true, caster);   // dried to the bone (Fix200)
                    break;
            }
        }
        private static bool mixBeamHit;

        /// <summary>What a mixed triple gives to the bodies a Restoration reached (on top of plain mending).</summary>
        private static void MixGive(Pawn caster, Hediff_ArcaneCore core, List<Pawn> who, RuneKind verb, float pw, Map map, IntVec3 at, float r)
        {
            int ticks = Mathf.RoundToInt(1500f * Mathf.Max(0.5f, pw));
            bool hold = verb == RuneKind.Hold;
            MixPicture(map, at.ToVector3Shifted(), Mathf.Max(1.5f, r * 1.6f), false);
            if (mix == LifeMix.Humus)
            {
                var fert = MapComponent_LifeFertility.Of(map);
                foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map)))
                {
                    if (map.terrainGrid.TerrainAt(c).fertility > 0f) fert?.Add(c, 0.25f * pw, 5);
                    Plant pl = c.GetPlant(map);
                    if (hold && pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.25f * pw);   // compost
                }
            }
            foreach (Pawn p in who)
            {
                switch (mix)
                {
                    case LifeMix.Fever:
                        MagicFx.Give(p, HD(hold ? "Avir_LifeSteamRoom" : "Avir_LifeSweat"), ticks);
                        RemoveOf(p, HD("Avir_LifeFever")); RemoveOf(p, HD("Avir_LifeFeverBoil"));
                        { Hediff tox = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup); if (tox != null) tox.Severity -= 0.08f * pw; }
                        if (verb == RuneKind.Purge)
                            foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => h.TryGetComp<HediffComp_Immunizable>() != null).ToList())
                            { ImmunityRecord rec = p.health.immunity.GetImmunityRecord(h.def); if (rec != null) rec.immunity = Mathf.Min(1f, rec.immunity + 0.1f * pw); }
                        break;
                    case LifeMix.Forge:
                        RemoveOf(p, HD("Avir_LifeBaked")); RemoveOf(p, HD("Avir_LifeFused"));
                        if (hold) AddRegen(p, 0.45f * pw);   // the forge-bed
                        Forge(caster, core, p, pw);
                        break;
                    case LifeMix.Ardour:
                        RemoveOf(p, HD("Avir_LifeApathy"));
                        { Hediff h = p.health.hediffSet.GetFirstHediffOfDef(HD("Avir_LifeArdour")); if (h == null && HD("Avir_LifeArdour") != null) h = p.health.AddHediff(HD("Avir_LifeArdour")); if (h is Hediff_LifeArdour la) la.Feed(ticks * (hold ? 2 : 1), hold); }
                        if (p.InMentalState && p.MentalStateDef != null && !p.MentalStateDef.IsAggro) p.MentalState.RecoverFromState();   // panic and terror end
                        break;
                    case LifeMix.Humus:
                        foreach (Hediff inf in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.WoundInfection).ToList()) { inf.Severity -= 0.2f * pw; if (inf.Severity <= 0.01f) p.health.RemoveHediff(inf); }
                        break;
                    case LifeMix.Nerve:
                        RemoveOf(p, HD("Avir_LifeTremor")); RemoveOf(p, HD("Avir_LifeShaking"));
                        foreach (Hediff ha in p.health.hediffSet.hediffs.Where(h => h.def.defName == "HeartAttack").ToList()) p.health.RemoveHediff(ha);
                        { Hediff bl = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss); if (bl != null) bl.Severity -= 0.12f * pw; }
                        MagicFx.Give(p, HD(hold ? "Avir_LifeKeptAlive" : "Avir_LifeHeartStarted"), hold ? ticks : ticks / 2);
                        if (p.Spawned) FleckMaker.ThrowLightningGlow(p.DrawPos, p.Map, 0.8f);
                        break;
                    case LifeMix.Decay:
                        if (hold) { MagicFx.Give(p, HD("Avir_LifeAgeHeld"), ticks * 4); break; }
                        if (!MixMaster(caster) || p.ageTracker == null) break;   // young again - only at mastery
                        {
                            long floor = (long)(p.ageTracker.AdultMinAge * GenDate.TicksPerYear);
                            if (p.ageTracker.AgeBiologicalTicks <= floor) break;
                            float f = Pay(core, 20f);   // very dear
                            if (f <= 0f) break;
                            p.ageTracker.AgeBiologicalTicks = System.Math.Max(floor, p.ageTracker.AgeBiologicalTicks - (long)(GenDate.TicksPerYear * 0.05f * pw * f));
                        }
                        break;
                }
            }
        }

        /// <summary>Forge: the bone is forged back - a small part (a finger, a toe, an ear, a nose) at any stage; at mastery
        /// any lost limb, dearly. One part a cast, a tender new one for a while.</summary>
        private static void Forge(Pawn caster, Hediff_ArcaneCore core, Pawn p, float pw)
        {
            var lost = p.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Where(m => m.Part != null && m.Part.def.defName.IndexOf("Eye", System.StringComparison.OrdinalIgnoreCase) < 0).ToList();
            if (lost.Count == 0) return;
            bool master = MixMaster(caster);
            Hediff_MissingPart pick = lost.Where(m => master || m.Part.parts.Count == 0).OrderBy(m => m.Part.coverageAbsWithChildren).FirstOrDefault();
            if (pick == null) return;
            float cost = 6f + 60f * pick.Part.coverageAbsWithChildren;   // a finger next to nothing, an arm dear
            if (Pay(core, cost) < 0.99f) { if (p.Spawned) MoteMaker.ThrowText(p.DrawPos, p.Map, "Not life enough to forge it", Necrotic, 3f); return; }
            BodyPartRecord part = pick.Part;
            p.health.RemoveHediff(pick);
            HediffDef tender = HD("Avir_LifeForged");
            if (tender != null) p.health.AddHediff(tender, part);
            if (p.Spawned) { FleckMaker.ThrowFireGlow(p.DrawPos, p.Map, 1.2f); MoteMaker.ThrowText(p.DrawPos, p.Map, "Forged: " + part.LabelCap, MixColor(LifeMix.Forge), 3.5f); }
        }

        /// <summary>The dead in reach of a Humus withering turn to fertile ground; Life drinks what was in them.</summary>
        private static float MixGround(IntVec3 at, float r, Map map)
        {
            if (mix != LifeMix.Humus || r <= 0f || map == null) return 0f;
            float got = 0f; var fert = MapComponent_LifeFertility.Of(map);
            foreach (Corpse k in GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Corpse>().Where(k => k.InnerPawn != null && k.InnerPawn.RaceProps.IsFlesh).ToList())
            {
                got += 6f;
                foreach (IntVec3 c in GenRadial.RadialCellsAround(k.Position, 1.9f, true)) if (c.InBounds(map) && map.terrainGrid.TerrainAt(c).fertility > 0f) fert?.Add(c, 0.4f, 8);
                FleckMaker.ThrowDustPuffThick(k.DrawPos, map, 1.2f, new Color(0.4f, 0.32f, 0.2f, 0.6f));
                k.Destroy();
            }
            return got;
        }

        /// <summary>The mixed triples' pictures: Life's own sprites, recoloured and combined (no new assets for Life).</summary>
        private static void MixPicture(Map map, Vector3 at, float size, bool dark)
        {
            if (map == null || mix == LifeMix.None) return;
            Color col = MixColor(mix); if (dark) col = Color.Lerp(col, Color.black, 0.35f);
            switch (mix)
            {
                case LifeMix.Fever: MapComponent_SpellSprites.PutTinted(map, "Life/PoisonFog", col, at, size, 70, -1f, 0.5f); FleckMaker.ThrowDustPuffThick(at, map, 0.9f, new Color(1f, 1f, 1f, 0.3f)); break;
                case LifeMix.Forge: MapComponent_SpellSprites.PutTinted(map, "Life/StoneCrust", col, at, size, 60); FleckMaker.ThrowFireGlow(at, map, 0.6f); break;
                case LifeMix.Ardour: MapComponent_SpellSprites.PutTinted(map, "Life/Heart", col, at, size * 0.8f, 55); break;
                case LifeMix.Humus: MapComponent_SpellSprites.PutTinted(map, "Life/Petals", col, at, size, 80, -1f, 0.4f); break;
                case LifeMix.Nerve: MapComponent_SpellSprites.PutTinted(map, "Life/Vein", col, at, size, 30, Rand.Range(0f, 360f)); FleckMaker.ThrowLightningGlow(at, map, 0.6f); break;
                case LifeMix.Decay: MapComponent_SpellSprites.PutTinted(map, "Life/Drain", col, at, size, 70, -1f, -1f); FleckMaker.ThrowDustPuffThick(at, map, 1f, new Color(0.7f, 0.66f, 0.55f, 0.4f)); break;
            }
        }
    }

    /// <summary>Ardour: no tiredness, no fear - and when it ends, the reckoning, heavier the more was given (Fix193).</summary>
    public class Hediff_LifeArdour : HediffWithComps
    {
        public int given; public bool heavy;
        public void Feed(int ticks, bool hold)
        {
            given += ticks; heavy |= hold;
            HediffComp_Disappears d = this.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, ticks);
            if (pawn.needs?.rest != null) pawn.needs.rest.CurLevel = Mathf.Max(pawn.needs.rest.CurLevel, 0.6f);
        }
        public override void PostRemoved()
        {
            base.PostRemoved();
            if (pawn == null || pawn.Dead) return;
            HediffDef spent = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_LifeArdourSpent");
            if (spent == null) return;
            Hediff h = pawn.health.AddHediff(spent);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.RoundToInt(given * (heavy ? 1.2f : 0.6f));
            if (pawn.needs?.rest != null) pawn.needs.rest.CurLevel *= heavy ? 0.4f : 0.7f;
        }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref given, "given"); Scribe_Values.Look(ref heavy, "heavy"); }
    }

    /// <summary>Nerve held: the dying kept alive while it holds - the blood loss eased, the heart kept going (Fix193).</summary>
    public class Hediff_LifeKeptAlive : HediffWithComps
    {
        public override void PostTick()
        {
            base.PostTick();
            if (!pawn.IsHashIntervalTick(60)) return;
            Hediff bl = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bl != null && bl.Severity > 0.5f) bl.Severity -= 0.015f;
        }
    }

    /// <summary>Decay held: the age held - the body does not grow older while it holds (Fix193).</summary>
    public class Hediff_LifeAgeHeld : HediffWithComps
    {
        public override void PostTick()
        {
            base.PostTick();
            if (pawn.ageTracker != null && pawn.IsHashIntervalTick(250)) pawn.ageTracker.AgeBiologicalTicks -= 250;
        }
    }
}
