using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    // =====================================================================================
    // HER INFLUENCE. A Singer needs no line of sight: whatever stands within her influence is within her reach.
    // The zone and her power grow as her Aria deepens - and with every point she offers to the Aria itself
    // instead of lighting a star. Tactics that touch many work round a point she chooses in the zone; how wide,
    // and how long they last, grows with her power.
    // =====================================================================================
    public partial class Gene_LinarianSong
    {
        /// <summary>Points offered to the Aria itself: no star, but a wider zone and a stronger song.</summary>
        public int offered;
        /// <summary>Her lullaby takes her own people too (for the dreaming paths, or to rest them by force).</summary>
        public bool lullOwn;
        /// <summary>Sow adoration on a whole crowd (round a point) instead of one mind; the last order to all the fervent.</summary>
        public bool adoreMass, lastOrderMass;
        /// <summary>Set once the lullaby and heavy eyelids have changed places in this Singer's network.</summary>
        public bool lullSwapped;
        /// <summary>Where her hunting pack was last pointed; and when beasts against the raid was first sung (its second singing).</summary>
        public IntVec3 lastHuntPoint = IntVec3.Invalid;
        public int raidArmed = -1;
        /// <summary>Marked prey set to mercy (lifts the pain); when the guises last taught her; the switch's last ring.</summary>
        public bool preyLift;
        public int guiseXp = -999999, lastRing = -999999;
        /// <summary>Theft of years: years drunk not yet turned into her own; her age set aside (-1: none); the mode that
        /// sets it aside instead of stealing. Fog or flare: the flare.</summary>
        public float stolenYears;
        public long ageReserve = -1;
        public bool ageMode, flareMode;
        /// <summary>Rend the pain round a point instead of on one.</summary>
        public bool rendMass;

        /// <summary>How far round her the Aria reaches, in cells (at the summit of it - the whole map).</summary>
        public float ZoneRadius
        {
            get
            {
                float r = 16f + 1.6f * songLevel + 4f * offered;
                return r >= LinarianZone.WholeMap ? float.MaxValue : r;
            }
        }

        /// <summary>How much wider and longer her tactics work: grows with her power.</summary>
        /// <summary>(While she still sings herself into a tactic, it reaches half as far and lasts half as long.)</summary>
        public float Reach => Mathf.Clamp(0.75f + 0.25f * Power / CastScale, 0.7f, 2.2f) * CastScale;

        /// <summary>A wild one spends her points herself: her own path to its crown first, then the Aria itself.</summary>
        public static void AutoSpend(Gene_LinarianSong g)
        {
            while (g.PointsFree > 0)
            {
                string main = LinarianNetwork.Areas.OrderByDescending(a => g.opened.Count(k => k.StartsWith(a))).FirstOrDefault();
                string next = main == null ? null : Enumerable.Range(1, 5).Select(i => main + i).FirstOrDefault(k => !g.opened.Contains(k));
                if (next != null) { g.opened.Add(next); continue; }
                g.Offer();
            }
        }

        public void Offer()
        {
            if (PointsFree <= 0) return;
            offered++;
        }

        private void ExposeZone()
        {
            Scribe_Values.Look(ref offered, "songOffered");
            Scribe_Values.Look(ref lullOwn, "songLullOwn");
            Scribe_Values.Look(ref adoreMass, "songAdoreMass");
            Scribe_Values.Look(ref lastOrderMass, "songLastOrderMass");
            Scribe_Values.Look(ref lullSwapped, "songLullSwapped");
            Scribe_Values.Look(ref lastHuntPoint, "songHuntPoint", IntVec3.Invalid);
            Scribe_Values.Look(ref raidArmed, "songRaidArmed", -1);
            Scribe_Values.Look(ref preyLift, "songPreyLift");
            Scribe_Values.Look(ref guiseXp, "songGuiseXp", -999999);
            Scribe_Values.Look(ref lastRing, "songLastRing", -999999);
            Scribe_Values.Look(ref stolenYears, "songStolenYears");
            Scribe_Values.Look(ref ageReserve, "songAgeReserve", -1L);
            Scribe_Values.Look(ref ageMode, "songAgeMode");
            Scribe_Values.Look(ref flareMode, "songFlareMode");
            Scribe_Values.Look(ref rendMass, "songRendMass");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && !lullSwapped)
            {
                // her lullaby stays hers: the stone she lit for it is now the fourth, heavy eyelids the first
                bool l1 = opened != null && opened.Remove("L1"), l4 = opened != null && opened.Remove("L4");
                if (l1) opened.Add("L4");
                if (l4) opened.Add("L1");
                lullSwapped = true;
            }
        }
    }

    public static class LinarianZone
    {
        public const float WholeMap = 120f;

        public static bool InZone(Pawn her, Gene_LinarianSong g, IntVec3 c) =>
            g.ZoneRadius >= float.MaxValue || (c - her.Position).LengthHorizontal <= g.ZoneRadius;

        /// <summary>How wide a tactic works round the chosen point (0: a single target, herself, or all her zone).</summary>
        public static float Area(string t, Gene_LinarianSong g)
        {
            float b;
            switch (t)
            {
                case "L1": b = 5f; break;
                case "L3": b = 9f; break;
                case "L4": b = 6f; break;
                case "L5": b = 11f; break;
                case "C3": b = 9f; break;
                case "C1": b = 6f; break;
                case "C4": b = 5f; break;
                case "D4": b = 6f; break;
                case "D2": b = 6f; break;
                case "S1": b = 6f; break;
                case "P2": b = 8f; break;
                case "D5": b = 7f; break;
                case "W1": b = g.Has("W5") ? 10f : 7f; break;
                case "W2": b = 8f; break;
                case "W3": b = 6f; break;
                case "W4": b = 5f; break;
                case "W5": b = 11f; break;
                case "F4": b = 9f; break;
                case "F5": b = 6f; break;
                case "S3": b = 7f; break;
                case "G3": b = 6f; break;
                case "G5": b = 5f; break;
                case "T3": b = 7f; break;
                case "T4": b = 12f; break;
                default: return 0f;
            }
            return Mathf.Min(b * g.Reach, GenRadial.MaxRadialPatternRadius - 1f);
        }

        public static IEnumerable<Pawn> Around(Map map, IntVec3 c, float r) =>
            GenRadial.RadialDistinctThingsAround(c, map, r, true).OfType<Pawn>().Where(q => !q.Dead).ToList();

        public static List<Pawn> FoesAt(Pawn her, IntVec3 c, float r) =>
            Around(her.Map, c, r).Where(q => q != her && (q.HostileTo(her) || LinarianPackSiphon.Held(her, q) || LinarianCult.SingerOf(q) == her && q.Faction != her.Faction) && !q.RaceProps.Animal)
                .OrderBy(q => (q.Position - c).LengthHorizontalSquared).ToList();

        public static List<Pawn> OwnAt(Pawn her, IntVec3 c, float r) =>
            Around(her.Map, c, r).Where(q => q != her && q.Faction != null && q.Faction == her.Faction && q.RaceProps.Humanlike).ToList();

        public static IEnumerable<Pawn> InHerZone(Pawn her, Gene_LinarianSong g) =>
            her.Map == null ? Enumerable.Empty<Pawn>() : her.Map.mapPawns.AllPawnsSpawned.Where(q => !q.Dead && InZone(her, g, q.Position)).ToList();

        public static int Ticks(float baseTicks, Gene_LinarianSong g) => Mathf.RoundToInt(baseTicks * g.Reach);

        // ---------------------------------------------------------------- the drowsy fields of heavy eyelids

        private class Field { public Map map; public Pawn her; public IntVec3 at; public float r; public int until; public Dictionary<Pawn, int> held = new Dictionary<Pawn, int>(); }
        private static readonly List<Field> fields = new List<Field>();

        public static void DrowsyField(Pawn her, Gene_LinarianSong g, IntVec3 c, float r)
        {
            fields.Add(new Field { map = her.Map, her = her, at = c, r = r, until = Find.TickManager.TicksGame + Ticks(GenDate.TicksPerHour, g) });
            FleckMaker.Static(c, her.Map, FleckDefOf.PsycastAreaEffect, r);
        }

        /// <summary>Every second: those inside the haze slow down; whoever stays in it long enough falls asleep.</summary>
        public static void Tick(Map map, int now)
        {
            fields.RemoveAll(f => f.until < now || f.her == null || f.her.Dead || f.map != map && f.map.Disposed());
            foreach (Field f in fields.Where(f => f.map == map))
            {
                Gene_LinarianSong g = Linarian.Song(f.her);
                if (g == null) continue;
                foreach (Pawn q in FoesAt(f.her, f.at, f.r).Where(LinarianTactics.Takeable))
                {
                    if (LinarianTactics.Asleep(q)) continue;
                    LinarianHediffs.Add(q, LinarianHediffs.Avir_TimeSlow, 150);
                    f.held.TryGetValue(q, out int n);
                    f.held[q] = ++n;
                    if (n >= 6 && Rand.Chance(LinarianTactics.Chance(f.her, g, q, true, "L") * 0.35f))
                        LinarianHediffs.Sleep(q, Ticks(GenDate.TicksPerHour * 1.5f, g), f.her);
                }
                if (now % 240 == 0 && Find.CurrentMap == map) FleckMaker.ThrowDustPuffThick(f.at.ToVector3Shifted(), map, f.r * 0.6f, new Color(0.6f, 0.55f, 0.85f, 0.4f));
            }
        }

        private static bool Disposed(this Map m) => m == null || Find.Maps.IndexOf(m) < 0;
    }

    /// <summary>A shadow of her, cast by the mirage: her own shape, darkened and half seen. Foes take it for her and
    /// shoot at it; it cannot be harmed, the player cannot pick it, and it melts away on its own.</summary>
    public class Building_ShadowDecoy : Building, IAttackTarget
    {
        public int until;
        public Pawn her;
        /// <summary>A skeleton's mirage (theft of years): the one whose years she drinks, as they will be.</summary>
        public Pawn skeletonOf;
        /// <summary>A beast's shadow (beasts against the raid, with her mirages): its own shape, darkened.</summary>
        public Pawn beastOf;
        private Material mat;
        private static readonly Color Dark = new Color(0.10f, 0.06f, 0.18f, 0.62f);

        public Thing Thing => this;
        public LocalTargetInfo TargetCurrentlyAimingAt => LocalTargetInfo.Invalid;
        /// <summary>The switch's mirage: it draws every foe it fooled, for as long as it stands.</summary>
        public bool lure;
        /// <summary>Drawn as this sprite instead of her darkened shape (a mage's mirage in the haze).</summary>
        public string sprite;
        public HashSet<int> fooled = new HashSet<int>();
        public float TargetPriorityFactor => lure ? 10f : 2.5f;

        /// <summary>Taken for her by the mind: each foe is fooled by a shadow, or sees through it, once and for all.</summary>
        public bool ThreatDisabled(IAttackTargetSearcher disabledFor)
        {
            if (!(disabledFor?.Thing is Pawn q) || her == null) return false;
            if (fooled.Contains(q.thingIDNumber)) return false;
            return Rand.ValueSeeded(Gen.HashCombineInt(q.thingIDNumber, thingIDNumber)) >= LinarianGuise.Fooled(her, q);
        }

        protected override void Tick()
        {
            base.Tick();
            if (Find.TickManager.TicksGame >= until && !Destroyed) { Destroy(DestroyMode.Vanish); return; }
            if (lure && Spawned && this.IsHashIntervalTick(300))
                foreach (Pawn q in Map.mapPawns.AllPawnsSpawned.Where(q => fooled.Contains(q.thingIDNumber)).ToList()) LinarianGuise.Aim(q, this);
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed) { absorbed = true; }

        /// <summary>Her own look, darkened: her beast's if she wears one, else her portrait drawn wide enough for her wings.</summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (her == null) return;
            if (sprite != null)
            {
                Material sm = MaterialPool.MatFrom(sprite, ShaderDatabase.MoteGlow);
                float pz = 0.85f + 0.15f * Mathf.Sin((Find.TickManager.TicksGame + thingIDNumber * 37) * 0.08f);
                Vector3 sat = drawLoc; sat.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(sat, Quaternion.identity, new Vector3(1.8f * pz, 1f, 1.8f * pz)), sm, 0);
                return;
            }
            if (skeletonOf != null && DrawSkeleton(drawLoc)) return;
            if (beastOf != null && DrawBeast(drawLoc)) return;
            float pulse = 0.9f + 0.1f * Mathf.Sin((Find.TickManager.TicksGame + thingIDNumber * 37) * 0.05f);
            Vector3 at = drawLoc;
            at.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
            string form = LinarianVeil.InForm(her);
            if (form != null)
            {
                foreach (Graphic g in LinarianFormGraphics.Layers(her, form))
                {
                    Material m = MaterialPool.MatFrom(new MaterialRequest(g.MatSouth.mainTexture, ShaderDatabase.Transparent, Dark));
                    Vector2 ds = g.drawSize * pulse;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(ds.x, 1f, ds.y)), m, 0);
                    at.y += 0.0005f;
                }
                return;
            }
            RenderTexture rt = PortraitsCache.Get(her, new Vector2(384f, 384f), Rot4.South, new Vector3(0f, 0f, 0.15f), 0.55f);
            if (rt == null) return;
            if (mat == null) mat = new Material(ShaderDatabase.Transparent) { color = Dark };
            mat.mainTexture = rt;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(3.6f * pulse, 1f, 3.6f * pulse)), mat, 0);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sprite, "sprite");
            Scribe_Values.Look(ref until, "until");
            Scribe_References.Look(ref her, "her");
            Scribe_Values.Look(ref lure, "lure");
            Scribe_References.Look(ref skeletonOf, "skeletonOf");
            Scribe_References.Look(ref beastOf, "beastOf");
        }

        private bool DrawBeast(Vector3 drawLoc)
        {
            PawnKindLifeStage ls = beastOf.ageTracker?.CurKindLifeStage;
            GraphicData gd = ls == null ? null : beastOf.gender == Gender.Female && ls.femaleGraphicData != null ? ls.femaleGraphicData : ls.bodyGraphicData;
            if (gd == null) return false;
            float pulse = 0.92f + 0.08f * Mathf.Sin((Find.TickManager.TicksGame + thingIDNumber * 37) * 0.05f);
            Graphic g = GraphicDatabase.Get<Graphic_Multi>(gd.texPath, ShaderDatabase.Transparent, gd.drawSize, Dark);
            Vector3 at = drawLoc;
            at.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
            Vector2 ds = gd.drawSize * pulse;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(ds.x, 1f, ds.y)), g.MatAt(beastOf.Rotation), 0);
            return true;
        }

        private bool DrawSkeleton(Vector3 drawLoc)
        {
            string body = skeletonOf.story?.bodyType?.bodyDessicatedGraphicPath;
            string head = HeadTypeDefOf.Skull?.graphicPath;
            if (body.NullOrEmpty()) return false;
            Color bone = new Color(0.85f, 0.82f, 0.74f, 0.55f + 0.1f * Mathf.Sin(Find.TickManager.TicksGame * 0.05f + thingIDNumber));
            Vector3 at = drawLoc;
            at.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
            Graphic b = GraphicDatabase.Get<Graphic_Multi>(body, ShaderDatabase.Transparent, Vector2.one * 1.5f, bone);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(1.5f, 1f, 1.5f)), b.MatSouth, 0);
            if (!head.NullOrEmpty())
            {
                Graphic h = GraphicDatabase.Get<Graphic_Multi>(head, ShaderDatabase.Transparent, Vector2.one * 1.5f, bone);
                at.y += 0.001f;
                at.z += 0.34f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(1.5f, 1f, 1.5f)), h.MatSouth, 0);
            }
            return true;
        }

        public static Building_ShadowDecoy SpawnOne(Pawn her, IntVec3 at, Map map, int until, bool lure)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ShadowDecoy");
            if (def == null || map == null || !at.InBounds(map)) return null;
            var d = (Building_ShadowDecoy)ThingMaker.MakeThing(def);
            d.her = her;
            d.until = until;
            d.lure = lure;
            GenSpawn.Spawn(d, at, map, Rot4.South);
            if (her.Faction != null) d.SetFaction(her.Faction);
            FleckMaker.ThrowSmoke(d.DrawPos, map, 1f);
            return d;
        }

        public static void Cast(Pawn her, Gene_LinarianSong g, IntVec3 c, float r)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ShadowDecoy");
            if (def == null) return;
            int n = Mathf.Clamp(2 + Mathf.FloorToInt(g.Power), 2, 5);
            for (int i = 0; i < n; i++)
            {
                if (!CellFinder.TryFindRandomCellNear(c, her.Map, Mathf.Max(1, Mathf.RoundToInt(r * 0.6f)), x => x.Standable(her.Map) && x.GetEdifice(her.Map) == null, out IntVec3 at)) continue;
                var d = (Building_ShadowDecoy)ThingMaker.MakeThing(def);
                d.her = her;
                d.until = Find.TickManager.TicksGame + LinarianZone.Ticks(GenDate.TicksPerHour / 2, g);
                GenSpawn.Spawn(d, at, her.Map, Rot4.South);
                if (her.Faction != null) d.SetFaction(her.Faction);
                FleckMaker.ThrowSmoke(d.DrawPos, her.Map, 1f);
            }
        }
    }
}
