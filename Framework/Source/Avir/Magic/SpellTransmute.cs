using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Transmutation in spells (Магия-правила.md, the author's rules):
    ///   - in a circle that meets no one (or with a Closing rune written right after it) it is plain transmutation - the
    ///     element works on matter where the spell lands: fire smelts slag, melts snow and dries the wet; water washes
    ///     and puts fires out; earth cuts chunks into blocks and smooths rough stone ground; air clears gas and blows the
    ///     snow off; a meeting does both of its elements' work, and some have their own (storm charges batteries, bog
    ///     enriches the soil for a while, haze warms a room);
    ///   - "Transmutation, Closing" right after one another is that plain transmutation, and that Closing seals nothing:
    ///     the circle still meets its neighbour;
    ///   - in a circle that meets its neighbour, with no Closing after it, it turns the meeting onto its secret path - the
    ///     bog's Moss, magma's Hot Metal (decided); the others proposed, not made yet.
    /// And the Closing rune that keeps two circles from meeting must stand between them: in the outermost section of the
    /// first, or in the first section of the second.</summary>
    public static class SpellTransmute
    {
        public enum Mode { None, Plain, Path }

        /// <summary>Is the rune at i a Closing written right after a Transmutation (the plain-transmutation mark)?</summary>
        public static bool MarkClosing(List<RuneDef> seq, int i) => seq[i] != null && seq[i].kind == RuneKind.Closing && i > 0 && seq[i - 1] != null && seq[i - 1].kind == RuneKind.Transmute;

        private static bool SealsIn(RitualRing r)
        {
            if (r?.runes == null) return false;
            var seq = r.runes.Where(x => x != null).ToList();
            for (int i = 0; i < seq.Count; i++) if (seq[i].kind == RuneKind.Closing && !MarkClosing(seq, i)) return true;
            return false;
        }

        /// <summary>Does a Closing stand between these two circles (a before b): in a's outermost section or b's first?</summary>
        public static bool Seals(SpellCircle a, SpellCircle b) =>
            (a?.sections != null && a.sections.Count > 0 && SealsIn(a.sections[a.sections.Count - 1]))
            || (b?.sections != null && b.sections.Count > 0 && SealsIn(b.sections[0]));

        public static Mode Of(RitualSchema part, ComboKind k)
        {
            if (part?.rings == null) return Mode.None;
            var seq = part.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
            int i = seq.FindIndex(x => x.kind == RuneKind.Transmute);
            if (i < 0) return Mode.None;
            if (k == ComboKind.None) return Mode.Plain;
            return i + 1 < seq.Count && seq[i + 1].kind == RuneKind.Closing ? Mode.Plain : Mode.Path;
        }

        /// <summary>The secret path of a meeting (Moss and Hot Metal decided; the rest proposed).</summary>
        public static string PathName(ComboKind k) =>
            k == ComboKind.Bog ? "Moss" : k == ComboKind.Magma ? "Hot Metal" : k == ComboKind.Steam ? "Mist (proposed)" : k == ComboKind.Storm ? "Current (proposed)"
            : k == ComboKind.Haze ? "Glass (proposed)" : k == ComboKind.Dust ? "Sand (proposed)" : "-";

        public static RuneKind[] Kinds(RuneDef el, ComboKind k) => k != ComboKind.None ? Combos.Parts(k) : el != null ? new[] { el.kind } : new RuneKind[0];

        /// <summary>Plain transmutation where the spell lands: each element's work on matter.</summary>
        public static int Plain(Pawn caster, Map map, IntVec3 at, float radius, float power, RuneDef el, ComboKind k)
        {
            if (map == null || !at.InBounds(map)) return 0;
            RuneKind[] ks = Kinds(el, k);
            var cells = GenRadial.RadialCellsAround(at, Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f), true).Where(c => c.InBounds(map)).ToList();
            int n = 0;
            foreach (IntVec3 c in cells)
            {
                List<Thing> here = c.GetThingList(map).ToList();
                if (ks.Contains(RuneKind.Fire))
                {
                    if (map.snowGrid.GetDepth(c) > 0.01f) { map.snowGrid.SetDepth(c, 0f); n++; }
                    foreach (Thing t in Reactions.Pools(c, map).Where(t => !(t is MirePatch)).ToList()) { t.Destroy(); n++; }
                    foreach (Thing t in here.Where(t => !t.Destroyed && t.def == ThingDefOf.ChunkSlagSteel && !t.def.smeltProducts.NullOrEmpty())) { Turn(t, t.def.smeltProducts); n++; }
                }
                if (ks.Contains(RuneKind.Water))
                {
                    foreach (Thing t in here.Where(t => !t.Destroyed && (t is Filth || t is Fire))) { t.Destroy(); n++; }
                }
                if (ks.Contains(RuneKind.Earth))
                {
                    foreach (Thing t in here.Where(t => !t.Destroyed && t.def.thingCategories?.Contains(ThingCategoryDefOf.StoneChunks) == true && !t.def.butcherProducts.NullOrEmpty())) { Turn(t, t.def.butcherProducts); n++; }
                    TerrainDef tr = map.terrainGrid.TerrainAt(c);
                    if (tr.smoothedTerrain != null && c.GetEdifice(map) == null) { map.terrainGrid.SetTerrain(c, tr.smoothedTerrain); n++; }
                }
                if (ks.Contains(RuneKind.Air))
                {
                    foreach (Thing t in here.Where(t => !t.Destroyed && t is Gas)) { t.Destroy(); n++; }
                    if (map.snowGrid.GetDepth(c) > 0.01f) { map.snowGrid.SetDepth(c, 0f); n++; }
                }
                // the meetings' own work
                if (k == ComboKind.Storm)
                    foreach (Thing t in here.Where(t => !t.Destroyed))
                    {
                        CompPowerBattery b = t.TryGetComp<CompPowerBattery>();
                        if (b != null && b.StoredEnergyPct < 0.999f) { b.AddEnergy(150f * power); FleckMaker.ThrowLightningGlow(t.DrawPos, map, 1.2f); n++; }
                    }
                if (k == ComboKind.Bog) n += Enrich(map, c);
                if (Rand.Chance(0.25f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 0.7f, Tint(el, k));
            }
            if (k == ComboKind.Haze)
            {
                Room room = at.GetRoom(map);
                if (room != null && !room.UsesOutdoorTemperature) { room.Temperature = Mathf.Min(21f, room.Temperature + 8f * power); n++; }
            }
            if (n > 0 && caster?.Spawned == true) MoteMaker.ThrowText(at.ToVector3Shifted(), map, "Transmuted", 2.5f);
            return n;
        }

        private static void Turn(Thing t, List<ThingDefCountClass> into)
        {
            IntVec3 at = t.Position; Map map = t.Map;
            foreach (ThingDefCountClass pr in into)
            {
                Thing got = ThingMaker.MakeThing(pr.thingDef);
                got.stackCount = pr.count;
                GenPlace.TryPlaceThing(got, at, map, ThingPlaceMode.Near);
            }
            t.Destroy();
        }

        private static int Enrich(Map map, IntVec3 c)
        {
            var comp = MapComponent_ArcaneOrders.Of(map);
            TerrainDef was = map.terrainGrid.TerrainAt(c), to = ArcaneWorks.Richer(was);
            if (comp == null || to == null || c.GetEdifice(map) != null) return 0;
            if (!comp.enrichFrom.ContainsKey(c)) comp.enrichFrom[c] = was;
            comp.enrichTo[c] = to;
            comp.enrichUntil[c] = Find.TickManager.TicksGame + ArcaneWorks.EnrichDays * GenDate.TicksPerDay;
            map.terrainGrid.SetTerrain(c, to);
            return 1;
        }

        private static Color Tint(RuneDef el, ComboKind k)
        {
            Color c = k != ComboKind.None ? Combos.ColorOf(k) : el?.color ?? Color.white;
            c.a = 0.5f;
            return c;
        }
    }
}
