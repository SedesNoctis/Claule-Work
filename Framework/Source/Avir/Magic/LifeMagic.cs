using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using LudeonTK;

namespace Avir.Magic
{
    /// <summary>Life, the first great circle (Магия-правила.md 0.28). Its law - Symbiosis - is that it makes nothing: it
    /// only moves. Withering takes from one thing into the mage's store of life; the other verbs give that store away.
    /// With an empty store nothing is given at all: the key rune of Life is Withering. A store filled by one spell can be
    /// spent by another, whenever the player likes. The great circle's own sections are its pure effect, cast where the
    /// spell ends; round small circles it mends the friends the spell touches - from the store too.</summary>
    public static class LifeMagic
    {
        public static readonly Color Necrotic = new Color(0.66f, 0.82f, 0.55f), NecroticDark = new Color(0.22f, 0.3f, 0.2f, 0.7f);
        private static HediffDef H(string n) => DefDatabase<HediffDef>.GetNamedSilentFail(n);
        public static HediffDef Regen => H("Avir_LifeRegen");
        public static DamageDef Wither => DefDatabase<DamageDef>.GetNamedSilentFail("Avir_Withering") ?? DamageDefOf.Burn;
        public static TerrainDef DeadGround => DefDatabase<TerrainDef>.GetNamedSilentFail("Avir_DeadGround");

        public static bool Is(RuneDef e) => e != null && e.kind == RuneKind.Life;
        public static IEnumerable<SpellCircle> Circles(SpellCircle root) => WeaveSpell.Greats(root).Where(g => Is(g.element));
        public static float Radius(SpellCircle g) => 2.5f + 1.5f * g.OwnRunes.Count(x => x.kind == RuneKind.Breadth);

        /// <summary>Verbs Life knows now; the rest of its grammar comes later.</summary>
        public static bool Knows(RuneKind k) => k == RuneKind.Drain || k == RuneKind.Release || k == RuneKind.Restore || k == RuneKind.Growth || k == RuneKind.Hold || k == RuneKind.Purge;
        public static bool Gives(RuneKind k) => k == RuneKind.Release || k == RuneKind.Restore || k == RuneKind.Growth || k == RuneKind.Hold || k == RuneKind.Purge;

        private static bool Friend(Pawn p, Pawn caster) => p != null && !p.Dead && !p.HostileTo(caster) && (p.Faction == caster.Faction || p.HostFaction == caster.Faction || p == caster);
        private static bool Foe(Pawn p, Pawn caster) => p != null && !p.Dead && p.HostileTo(caster);
        private static bool Living(Pawn p) => p != null && p.RaceProps != null && !p.RaceProps.IsMechanoid && p.RaceProps.IsFlesh;

        // ------------------------------------------------------------------ the great circle's own runes

        public static void Cast(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map)
        {
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null || map == null || !at.InBounds(map)) return;
            float r = Radius(g);
            bool gaveNothing = false;
            foreach (RuneDef rn in g.OwnRunes.ToList())
            {
                switch (rn.kind)
                {
                    case RuneKind.Drain: DrainAt(caster, core, at, r, power, map); break;
                    case RuneKind.Release: gaveNothing |= !Pulse(caster, core, at, r, power, map); break;
                    case RuneKind.Restore: gaveNothing |= !Mend(caster, core, at, r, power, map, 12f, 0.25f); break;
                    case RuneKind.Growth: gaveNothing |= !Grow(caster, core, at, r, power, map); break;
                    case RuneKind.Hold: gaveNothing |= !Vines(caster, core, at, r, power, map); break;
                    case RuneKind.Purge: gaveNothing |= !Cleanse(caster, core, at, r, power, map); break;
                }
            }
            if (gaveNothing) MoteMaker.ThrowText(at.ToVector3Shifted(), map, "No life to give", Necrotic, 3f);
        }

        /// <summary>Round small circles: the friends the spell touched are mended - out of the store.</summary>
        public static void Wrap(Pawn caster, SpellCircle g, IntVec3 at, float radius, float power, Map map)
        {
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null) return;
            float heal = (6f + 3f * g.OwnRunes.Count(x => x.kind == RuneKind.Restore || x.kind == RuneKind.Growth)) * power;
            if (!Mend(caster, core, at, radius, power, map, heal / Mathf.Max(0.01f, power), 0.1f)) MoteMaker.ThrowText(at.ToVector3Shifted(), map, "No life to give", Necrotic, 3f);
        }

        // ------------------------------------------------------------------ taking

        /// <summary>Withering: the grass greys to dead ground, the foes dry, fresh dead are drunk dry - into the store.
        /// Cultivated fields of one's own are spared.</summary>
        public static float DrainAt(Pawn caster, Hediff_ArcaneCore core, IntVec3 at, float r, float power, Map map)
        {
            float got = 0f;
            var temp = MapComponent_TempTerrain.Of(map);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map)).ToList())
            {
                Plant pl = c.GetPlant(map);
                if (pl != null && !(map.zoneManager.ZoneAt(c) is Zone_Growing) && pl.def.plant != null)
                {
                    float v = pl.def.plant.IsTree ? 6f : pl.def.plant.harvestYield > 0f || pl.def.size.x > 1 ? 2f : 0.6f;
                    got += v * Mathf.Max(0.3f, pl.Growth);
                    pl.Destroy();
                    if (DeadGround != null && map.terrainGrid.TerrainAt(c).fertility > 0f) temp?.Set(c, DeadGround, GenDate.TicksPerDay * 3);
                    if (Rand.Chance(0.4f)) Wisp(c.ToVector3Shifted(), map);
                }
                foreach (Thing t in c.GetThingList(map).ToList())
                {
                    if (t is Pawn p && Foe(p, caster) && Living(p))
                    {
                        float dmg = 5f * power;
                        p.TakeDamage(new DamageInfo(Wither, dmg, 2f, -1f, caster));
                        got += dmg;
                        Wisp(p.DrawPos, map);
                    }
                    else if (t is Corpse k && k.InnerPawn != null && Living(k.InnerPawn))
                    {
                        CompRottable rot = k.GetComp<CompRottable>();
                        if (rot != null && rot.Stage == RotStage.Fresh) { rot.RotProgress = 1e7f; got += 8f; Wisp(k.DrawPos, map); }
                    }
                }
            }
            got *= power;
            if (got > 0f) core.AddVita(got);
            return got;
        }

        private static void Wisp(Vector3 v, Map map)
        {
            FleckMaker.ThrowDustPuffThick(v, map, 1f, new Color(Necrotic.r, Necrotic.g, Necrotic.b, 0.55f));
        }

        // ------------------------------------------------------------------ giving: each asks the store first

        /// <summary>Takes what is there of `need` out of the store; the share that could be paid (0..1).</summary>
        private static float Pay(Hediff_ArcaneCore core, float need)
        {
            if (need <= 0f) return 1f;
            if (core.vita <= 0.01f) return 0f;
            float f = Mathf.Clamp01(core.vita / need);
            core.vita = Mathf.Max(0f, core.vita - need * f);
            return f;
        }

        /// <summary>A pulse: it wounds the foes and mends the friends - half a point of life for each point of either.</summary>
        private static bool Pulse(Pawn caster, Hediff_ArcaneCore core, IntVec3 at, float r, float power, Map map)
        {
            var near = GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().Where(Living).ToList();
            var foes = near.Where(p => Foe(p, caster)).ToList();
            var friends = near.Where(p => Friend(p, caster) && Hurt(p)).ToList();
            float need = (foes.Count * 6f + friends.Count * 8f) * power * 0.5f;
            if (need <= 0f) return true;
            float f = Pay(core, need);
            if (f <= 0f) return false;
            foreach (Pawn p in foes) p.TakeDamage(new DamageInfo(Wither, 6f * power * f, 2f, -1f, caster));
            foreach (Pawn p in friends) HealInjuries(p, 8f * power * f);
            FleckMaker.Static(at, map, FleckDefOf.PsycastAreaEffect, r * 0.8f);
            return true;
        }

        /// <summary>Life's mending - the best plain healing there is, and it asks nothing of the healed (the author): it
        /// closes wounds, stops the bleeding and leaves a regrowth that deepens with every mending.</summary>
        private static bool Mend(Pawn caster, Hediff_ArcaneCore core, IntVec3 at, float r, float power, Map map, float perHead, float regen)
        {
            var friends = GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().Where(p => Living(p) && Friend(p, caster)).ToList();
            if (friends.Count == 0) return true;
            float need = friends.Sum(p => Hurt(p) ? perHead * power : 0f) * 0.6f + friends.Count * 2f * regen;
            float f = Pay(core, need);
            if (f <= 0f) return false;
            foreach (Pawn p in friends)
            {
                HealInjuries(p, perHead * power * f);
                AddRegen(p, regen * power * f);
                FleckMaker.ThrowMetaIcon(p.Position, map, FleckDefOf.HealingCross);
            }
            return true;
        }

        private static bool Grow(Pawn caster, Hediff_ArcaneCore core, IntVec3 at, float r, float power, Map map)
        {
            var plants = GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map)).Select(c => c.GetPlant(map)).Where(p => p != null && p.LifeStage == PlantLifeStage.Growing).ToList();
            var friends = GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().Where(p => Living(p) && Friend(p, caster)).ToList();
            float need = plants.Count * 0.3f + friends.Count * 1f;
            if (need <= 0f) return true;
            float f = Pay(core, need);
            if (f <= 0f) return false;
            foreach (Plant pl in plants) { pl.Growth = Mathf.Min(1f, pl.Growth + 0.15f * power * f); if (Rand.Chance(0.2f)) Wisp(pl.DrawPos, map); }
            foreach (Pawn p in friends) AddRegen(p, 0.1f * power * f);
            return true;
        }

        private static bool Vines(Pawn caster, Hediff_ArcaneCore core, IntVec3 at, float r, float power, Map map)
        {
            var foes = GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().Where(p => Foe(p, caster)).ToList();
            if (foes.Count == 0) return true;
            float f = Pay(core, foes.Count * 2f);
            if (f <= 0f) return false;
            foreach (Pawn p in foes) MagicFx.Give(p, H("Avir_Entangled"), Mathf.RoundToInt(480f * power * f));
            return true;
        }

        private static bool Cleanse(Pawn caster, Hediff_ArcaneCore core, IntVec3 at, float r, float power, Map map)
        {
            var friends = GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().Where(p => Living(p) && Friend(p, caster)).ToList();
            var bad = friends.Select(p => new { p, h = p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.FoodPoisoning || h.def == HediffDefOf.ToxicBuildup || h.def.defName == "Avir_Withered").ToList() }).Where(x => x.h.Count > 0).ToList();
            if (bad.Count == 0) return true;
            float f = Pay(core, bad.Count * 3f);
            if (f <= 0f) return false;
            foreach (var x in bad) foreach (Hediff h in x.h) { h.Severity -= 0.3f * power * f; if (h.Severity <= 0.01f && x.p.health.hediffSet.hediffs.Contains(h)) x.p.health.RemoveHediff(h); }
            return true;
        }

        // ------------------------------------------------------------------ helpers

        public static bool Hurt(Pawn p) => p.health.hediffSet.hediffs.Any(h => h is Hediff_Injury i && !i.IsPermanent()) || p.health.hediffSet.BleedRateTotal > 0f;

        public static void HealInjuries(Pawn p, float amount)
        {
            float left = amount;
            foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => !i.IsPermanent()).OrderByDescending(i => i.Bleeding ? 1 : 0).ThenByDescending(i => i.Severity).ToList())
            {
                if (left <= 0f) break;
                float h = Mathf.Min(left, inj.Severity); inj.Heal(h); left -= h;
            }
        }

        /// <summary>The regrowth deepens with every mending (Avir_LifeRegen; like the Kharassian vapour, only quicker).</summary>
        public static void AddRegen(Pawn p, float amount)
        {
            if (Regen == null || amount <= 0f) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Regen) ?? p.health.AddHediff(Regen);
            h.Severity = Mathf.Min(1.5f, (h.Severity <= 0.011f ? 0f : h.Severity) + amount);
        }

        // ------------------------------------------------------------------ the store spilling over, and its slow fading

        /// <summary>Overflow: what the store cannot hold breaks out round the mage - the grass bursts, every wound near
        /// closes a little (the foes' too), and bark and moss creep over the mage's hands.</summary>
        public static void Overflow(Pawn caster, float excess)
        {
            if (caster == null || !caster.Spawned || excess <= 0.5f) return;
            Map map = caster.Map;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(caster.Position, 3.5f, true).Where(c => c.InBounds(map)))
            {
                Plant pl = c.GetPlant(map);
                if (pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.02f * excess);
            }
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(caster.Position, map, 3.5f, true).OfType<Pawn>().Where(Living).ToList())
                HealInjuries(p, 0.5f * excess);
            HediffDef bark = H("Avir_LifeOverflow");
            if (bark != null)
            {
                Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(bark) ?? caster.health.AddHediff(bark);
                h.Severity = Mathf.Min(1f, h.Severity + 0.02f * excess);
            }
            for (int i = 0; i < 6; i++) Wisp(caster.DrawPos + new Vector3(Rand.Range(-2f, 2f), 0f, Rand.Range(-2f, 2f)), map);
            MoteMaker.ThrowText(caster.DrawPos, map, "Overflow", Necrotic, 3f);
        }

        /// <summary>The store fades slowly, and what fades goes out of the mage: their own wounds close, the plants by
        /// them green, and above half the store their body fights sickness quicker (Avir_VitaFlow).</summary>
        public static void Fade(Hediff_ArcaneCore core, float hours)
        {
            Pawn p = core.pawn;
            HediffDef flowDef = H("Avir_VitaFlow");
            Hediff flow = flowDef != null ? p.health.hediffSet.GetFirstHediffOfDef(flowDef) : null;
            if (core.vita <= 0.01f) { core.vita = 0f; if (flow != null) p.health.RemoveHediff(flow); return; }
            float gone = core.vita * 0.02f * hours;
            core.vita -= gone;
            if (Living(p)) HealInjuries(p, gone * 0.5f);
            if (p.Spawned)
                foreach (IntVec3 c in GenRadial.RadialCellsAround(p.Position, 2.9f, true).Where(c => c.InBounds(p.Map)).InRandomOrder().Take(3))
                {
                    Plant pl = c.GetPlant(p.Map);
                    if (pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.01f * gone);
                }
            if (flowDef != null)
            {
                if (flow == null) flow = p.health.AddHediff(flowDef);
                flow.Severity = Mathf.Clamp(core.vita / Mathf.Max(1f, core.VitaMax), 0.01f, 1f);
            }
        }
    }

    // ================================================================== the regrowth (after the Kharassian sahrumet, quicker)

    public class HediffCompProperties_LifeRegen : HediffCompProperties
    {
        public float daysPerLayer = 1.5f, daysPerOrgan = 15f, daysPerChronicCure = 5f, scarHealFractionPerDay = 0.35f;
        public float scarsFrom = 0.5f, saturatedFrom = 1f, fadePerDay = 0.25f;
        public HediffCompProperties_LifeRegen() { compClass = typeof(HediffComp_LifeRegen); }
    }

    /// <summary>What Life's mending leaves behind. Its depth grows with every mending and fades a quarter a day. Light, it
    /// quickens healing (its stages); deep (0.5+), it walks scars down to nothing; saturated (1+), it brings back what was
    /// lost - an outer part a layer at a time (the parent before its children), the organs all at once on a long clock,
    /// chronic ailments on a shorter one. The logic follows Kharassian.HediffComp_LimbRegrowth, kept here in Avir (the
    /// Kharassians will lean on Avir, not the other way round).</summary>
    public class HediffComp_LifeRegen : HediffComp
    {
        private float layerProgress, organProgress, chronicProgress;
        private int check = Rand.Range(0, 250);
        public HediffCompProperties_LifeRegen Props => (HediffCompProperties_LifeRegen)props;

        public override string CompTipStringExtra => parent.Severity >= Props.saturatedFrom
            ? "Next part: " + (layerProgress * 100f).ToString("F0") + "%\nOrgans: " + (organProgress * 100f).ToString("F0") + "%\nChronic ailments: " + (chronicProgress * 100f).ToString("F0") + "%"
            : parent.Severity >= Props.scarsFrom ? "Old scars are fading." : null;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            check -= delta;
            if (check > 0) return;
            int dt = 250 - check; check = 250;
            Pawn p = parent.pawn;
            if (p == null || p.Dead) return;
            float days = dt / (float)GenDate.TicksPerDay;
            severityAdjustment -= Props.fadePerDay * days;
            float sev = parent.Severity;
            if (sev >= Props.scarsFrom) MendScars(p, days * (sev >= Props.saturatedFrom ? 1.5f : 1f));
            if (sev < Props.saturatedFrom) return;
            layerProgress += days / Props.daysPerLayer;
            organProgress += days / Props.daysPerOrgan;
            chronicProgress += days / Props.daysPerChronicCure;
            if (layerProgress >= 1f)
            {
                var parts = Missing(p, x => x.depth != BodyPartDepth.Inside || x.def.IsSolid(x, p.health.hediffSet.hediffs));
                if (parts.Count > 0) { layerProgress = 0f; foreach (BodyPartRecord x in parts) RestoreBare(p, x); Note(p, "grown back " + string.Join(", ", parts.Select(x => x.Label))); }
                else layerProgress = 1f;
            }
            if (organProgress >= 1f)
            {
                var organs = Missing(p, x => x.depth == BodyPartDepth.Inside && !x.def.IsSolid(x, p.health.hediffSet.hediffs));
                if (organs.Count > 0) { organProgress = 0f; foreach (BodyPartRecord x in organs) RestoreBare(p, x); Note(p, "grown back the organs"); }
                else organProgress = 1f;
            }
            if (chronicProgress >= 1f)
            {
                var chronic = p.health.hediffSet.hediffs.Where(h => h.def.chronic).ToList();
                if (chronic.Count > 0) { chronicProgress = 0f; foreach (Hediff h in chronic) p.health.RemoveHediff(h); Note(p, "been rid of " + string.Join(", ", chronic.Select(h => h.LabelBase))); }
                else chronicProgress = 1f;
            }
        }

        private void MendScars(Pawn p, float days)
        {
            foreach (Hediff_Injury scar in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.IsPermanent() && i.Part != null).ToList())
            {
                scar.Severity -= scar.Part.def.GetMaxHealth(p) * Props.scarHealFractionPerDay * days;
                if (scar.Severity <= 0.001f && p.health.hediffSet.hediffs.Contains(scar)) p.health.RemoveHediff(scar);
            }
        }

        private static List<BodyPartRecord> Missing(Pawn p, System.Func<BodyPartRecord, bool> accept)
        {
            var res = new List<BodyPartRecord>();
            foreach (Hediff_MissingPart m in p.health.hediffSet.hediffs.OfType<Hediff_MissingPart>())
            {
                if (m.Part == null || !accept(m.Part) || res.Contains(m.Part)) continue;
                bool ancestorGone = false;
                for (BodyPartRecord a = m.Part.parent; a != null; a = a.parent) if (p.health.hediffSet.PartIsMissing(a)) { ancestorGone = true; break; }
                if (!ancestorGone) res.Add(m.Part);
            }
            return res;
        }

        private static void RestoreBare(Pawn p, BodyPartRecord part)
        {
            p.health.RestorePart(part);
            if (part.parts == null) return;
            foreach (BodyPartRecord child in part.parts)
            {
                if (p.health.hediffSet.PartIsMissing(child)) continue;
                var miss = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, p, child);
                miss.IsFresh = false;
                p.health.AddHediff(miss, child);
            }
        }

        private static void Note(Pawn p, string what)
        {
            if (PawnUtility.ShouldSendNotificationAbout(p)) Messages.Message(p.LabelShortCap + " has " + what + ": life's regrowth.", p, MessageTypeDefOf.PositiveEvent, false);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref layerProgress, "lifeLayer");
            Scribe_Values.Look(ref organProgress, "lifeOrgan");
            Scribe_Values.Look(ref chronicProgress, "lifeChronic");
        }
    }

    // ================================================================== the store on the screen

    public class Gizmo_Vita : Gizmo
    {
        public Hediff_ArcaneCore core;
        private static readonly Texture2D Fill = SolidColorMaterials.NewSolidColorTexture(new Color(0.55f, 0.72f, 0.46f));
        private static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(new Color(0.08f, 0.1f, 0.08f));
        private static Texture2D art; private static bool looked;
        public Gizmo_Vita() { Order = -100.5f; }
        public override float GetWidth(float maxWidth) => 150f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            if (!looked) { looked = true; art = ContentFinder<Texture2D>.Get("UI/Magic/Scale_Life", false); }
            Rect r = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect bar;
            if (art != null)
            {
                // the plaque has the Arcana plaque's layout: a title band above, a window for the bar below (13-87% across, 59-81% down)
                bar = new Rect(r.x + r.width * 0.13f, r.y + r.height * 0.59f, r.width * 0.74f, r.height * 0.22f);
                Widgets.FillableBar(bar, Mathf.Clamp01(core.vita / Mathf.Max(1f, core.VitaMax)), Fill, Empty, false);
                GUI.DrawTexture(r, art);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.9f, 0.95f, 0.85f);
                Widgets.Label(new Rect(r.x, r.y + r.height * 0.33f, r.width, r.height * 0.2f), "Life");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                Widgets.DrawWindowBackground(r);
                Rect inner = r.ContractedBy(5f);
                Text.Font = GameFont.Small;
                GUI.color = LifeMagic.Necrotic; Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), "Life"); GUI.color = Color.white;
                bar = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
                Widgets.FillableBar(bar, Mathf.Clamp01(core.vita / Mathf.Max(1f, core.VitaMax)), Fill, Empty, false);
            }
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, Mathf.FloorToInt(core.vita) + " / " + Mathf.FloorToInt(core.VitaMax));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, "The store of life: what Withering took, ready to be given. Life makes nothing - it only moves (Symbiosis): no spell of Life gives anything with the store empty. It fades by 2% an hour, and what fades mends the mage and greens the plants by them. Past the top it spills over (Overflow).");
            return new GizmoResult(GizmoState.Clear);
        }
    }

    // ================================================================== developer mode

    public static class LifeDev
    {
        private const string Cat = "Avir magic";

        [DebugAction(Cat, "Life: store full / empty (toggle)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Store(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c == null) return; c.vita = c.vita > 1f ? 0f : c.VitaMax; Messages.Message(p.LabelShort + ": store of life " + c.vita.ToString("0") + " / " + c.VitaMax.ToString("0"), MessageTypeDefOf.NeutralEvent, false); }

        [DebugAction(Cat, "Life: overflow now (+50 past the top)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Over(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c == null) return; c.vita = c.VitaMax; c.AddVita(50f); }

        [DebugAction(Cat, "Life: regrowth saturated (1.2)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Sat(Pawn p) => LifeMagic.AddRegen(p, 1.2f);

        [DebugAction(Cat, "Life: cut off a hand and scar (regrowth test)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Cut(Pawn p)
        {
            BodyPartRecord hand = p.health.hediffSet.GetNotMissingParts().FirstOrDefault(x => x.def.defName == "Hand");
            if (hand != null) { var m = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, p, hand); m.IsFresh = false; p.health.AddHediff(m, hand); }
        }

        [DebugAction(Cat, "Life: test spells into this caster's book", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Tests(Pawn p)
        {
            Hediff_ArcaneCore core = Mages.Core(p);
            if (core == null) return;
            RuneDef R(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);
            RitualSchema Spell(string name, string life, params string[] smalls)
            {
                var root = new SpellCircle { binding = true };
                var g = new SpellCircle { element = R("Life") };
                foreach (string sec in life.Split('|'))
                {
                    var runes = sec.Split(',').Select(x => R(x.Trim())).Where(x => x != null).ToList();
                    if (runes.Count > 0) g.sections.Add(new RitualRing { runes = runes, slots = Mathf.Max(1, runes.Count) });
                }
                foreach (string c in smalls)
                {
                    string[] parts = c.Split(':');
                    var k = new SpellCircle { element = R(parts[0]) };
                    foreach (string sec in parts[1].Split('|')) { var runes = sec.Split(',').Select(x => R(x.Trim())).Where(x => x != null).ToList(); k.sections.Add(new RitualRing { runes = runes, slots = Mathf.Max(1, runes.Count) }); }
                    g.inner.Add(k);
                }
                root.inner.Add(g);
                return new RitualSchema { name = name, weave = root };
            }
            var list = new List<RitualSchema>
            {
                Spell("TEST Life: wither (fill the store)", "Drain,Breadth"),
                Spell("TEST Life: wither, then mend", "Drain|Restore,Breadth"),
                Spell("TEST Life: mend from the store only", "Restore,Amplify"),
                Spell("TEST Life: pulse (wounds foes, mends own)", "Release,Breadth"),
                Spell("TEST Life: growth", "Growth,Breadth"),
                Spell("TEST Life: vines", "Hold,Breadth"),
                Spell("TEST Life: cleanse", "Purge"),
                Spell("TEST Life round fire (mends whom it touches)", "", "Fire:Release"),
            };
            foreach (RitualSchema s in list) core.Book.spells.Add(s);
            Messages.Message(list.Count + " test spells of Life written into " + p.LabelShort + "'s book.", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
