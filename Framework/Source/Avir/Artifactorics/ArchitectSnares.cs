using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Avir.Ritualistics;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // THE MASTER OF SNARES. The spheres are laid in the ground as snares; a snare springs when a foe of the colony
    // comes within a step and a half of it. Foes do not see them. The architect may keep two at once, one more for
    // every third stone; laying another past that takes the oldest back.
    //   Binding   - drags the foes around it into its centre and holds them there.
    //   Stunning  - stuns them.
    //   Elemental - by the architect's amulet: gold - fire that clings, and the burning may burst again near another
    //               foe (Avir_LivingBomb); silver - frostbite and a crawl (Avir_CryoSlow); neither - a plain blast.
    //   Flash     - light and thunder: blinded (Avir_Flashblind) and stunned.
    //   12th stone - chained: a sprung snare sets off every other snare of the architect near it; every snare reaches further.
    // =====================================================================================
    public enum SnareKind { Binding, Stunning, Elemental, Flash }

    public class Snare : IExposable
    {
        public IntVec3 cell;
        public SnareKind kind;
        public string element;
        public float power = 1f;
        public bool chained;
        public Pawn owner;
        public int placed;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref element, "element");
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_Values.Look(ref chained, "chained");
            Scribe_References.Look(ref owner, "owner");
            Scribe_Values.Look(ref placed, "placed");
        }
    }

    public class MapComponent_SphereSnares : MapComponent
    {
        public List<Snare> snares = new List<Snare>();
        private const float TriggerRadius = 1.5f;
        private const float ChainReach = 6f;

        public MapComponent_SphereSnares(Map map) : base(map) { }

        public static MapComponent_SphereSnares Of(Map map) => map?.GetComponent<MapComponent_SphereSnares>();

        public void Lay(Snare s, int max)
        {
            List<Snare> own = snares.Where(x => x.owner == s.owner).OrderBy(x => x.placed).ToList();
            for (int i = 0; i <= own.Count - max; i++) snares.Remove(own[i]);
            snares.Add(s);
        }

        public override void MapComponentTick()
        {
            if (snares.Count == 0 || Find.TickManager.TicksGame % 20 != 0) return;
            Faction own = Faction.OfPlayer;
            foreach (Snare s in snares.ToList())
            {
                if (!snares.Contains(s)) continue;
                bool foe = false;
                foreach (Thing t in GenRadial.RadialDistinctThingsAround(s.cell, map, TriggerRadius, true))
                    if (t is Pawn q && !q.Dead && !q.Downed && q.HostileTo(own)) { foe = true; break; }
                if (foe) Spring(s);
            }
        }

        private void Spring(Snare s)
        {
            if (!snares.Remove(s)) return;
            SphereSnares.Effect(s, map);
            if (!s.chained) return;
            foreach (Snare o in snares.Where(o => o.owner == s.owner && (o.cell - s.cell).LengthHorizontal <= ChainReach).ToList()) Spring(o);
        }

        public override void MapComponentUpdate()
        {
            if (snares.Count == 0 || Find.CurrentMap != map) return;
            for (int i = 0; i < snares.Count; i++)
            {
                Snare s = snares[i];
                Vector3 v = s.cell.ToVector3Shifted();
                v.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(v, Quaternion.identity, new Vector3(0.9f, 1f, 0.9f)), SphereSnares.Mat(s.kind), 0);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref snares, "sphereSnares", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (snares == null) snares = new List<Snare>();
                snares.RemoveAll(x => x == null);
            }
        }
    }

    public static class SphereSnares
    {
        public const float Cost = 0.05f;
        public const int CooldownTicks = 600;
        public const float Range = 30f;

        private static string Key(SnareKind k) => "snare" + k;

        public static string Label(SnareKind k)
        {
            switch (k)
            {
                case SnareKind.Binding: return "binding snare";
                case SnareKind.Stunning: return "stunning snare";
                case SnareKind.Elemental: return "elemental snare";
                default: return "flash snare";
            }
        }

        public static int Max(CompArchitectSpheres c) => 2 + c.Stones / 3;

        private static readonly Dictionary<SnareKind, Material> mats = new Dictionary<SnareKind, Material>();

        public static Material Mat(SnareKind k)
        {
            if (mats.TryGetValue(k, out Material m)) return m;
            string glyph = k == SnareKind.Binding ? "Hold" : k == SnareKind.Stunning ? "Barrier" : k == SnareKind.Elemental ? "Release" : "Focus";
            // the snare's mark on the ground (white, tinted); a rune's glow while it is not drawn
            Texture2D tex = ContentFinder<Texture2D>.Get("Things/Mote/AvirSpheres/Snare_" + k, false)
                            ?? ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Glow/Rune_" + glyph, false) ?? BaseContent.WhiteTex;
            Color col = k == SnareKind.Binding ? new Color(0.65f, 0.5f, 1f, 0.45f) : k == SnareKind.Stunning ? new Color(0.6f, 0.85f, 1f, 0.45f)
                : k == SnareKind.Elemental ? new Color(1f, 0.6f, 0.3f, 0.45f) : new Color(1f, 1f, 0.85f, 0.45f);
            m = MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, col);
            mats[k] = m;
            return m;
        }

        public static Command Command(Pawn p, CompArchitectSpheres c, SnareKind kind)
        {
            string el = SpherePaths.Element(p);
            string desc = SpherePaths.Powers(SpherePaths.Snares)[kind == SnareKind.Binding || kind == SnareKind.Stunning ? 0 : kind == SnareKind.Elemental ? 1 : 2][1];
            int laid = MapComponent_SphereSnares.Of(p.Map)?.snares.Count(s => s.owner == p) ?? 0;
            var cmd = new Command_Target
            {
                defaultLabel = Label(kind).CapitalizeFirst() + (kind == SnareKind.Elemental ? (el == "Gold" ? " (fire)" : el == "Silver" ? " (frost)" : " (blast)") : ""),
                defaultDesc = desc + "\n\nSnares laid: " + laid + " / " + Max(c) + " (laying more takes the oldest back). Costs " + Cost.ToStringPercent() + " of the spheres' charge.",
                icon = SphereIcons.Named(kind == SnareKind.Binding ? "Snare_Binding" : kind == SnareKind.Stunning ? "Snare_Stunning"
                           : kind == SnareKind.Flash ? "Snare_Flash" : el == "Gold" ? "Snare_Fire" : el == "Silver" ? "Snare_Frost" : "Snare_Blast")
                       ?? ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Ink/Rune_" + (kind == SnareKind.Binding ? "Hold" : kind == SnareKind.Stunning ? "Barrier" : kind == SnareKind.Elemental ? "Release" : "Focus"), false) ?? SphereActives.Icon(c),
                targetingParams = new TargetingParameters
                {
                    canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false, canTargetItems = false,
                    validator = ti => ti.Cell.IsValid && ti.Cell.InBounds(p.Map) && ti.Cell.Standable(p.Map) && (ti.Cell - p.Position).LengthHorizontal <= Range
                },
                action = ti => Lay(p, c, kind, ti.Cell)
            };
            SphereActives.Gate(cmd, p, c, Key(kind), Cost);
            return cmd;
        }

        private static void Lay(Pawn p, CompArchitectSpheres c, SnareKind kind, IntVec3 cell)
        {
            if (!p.Spawned || !SpherePaths.Pay(p, c, Cost)) return;
            c.Cooldown(Key(kind), CooldownTicks);
            MapComponent_SphereSnares.Of(p.Map)?.Lay(new Snare
            {
                cell = cell, kind = kind, element = SpherePaths.Element(p), power = SpherePaths.Power(p, SpherePaths.Snares),
                chained = c.Stones >= 12, owner = p, placed = Find.TickManager.TicksGame
            }, Max(c));
            RitualFx.Mote(cell.ToVector3Shifted(), p.Map, 0.9f, new Color(0.7f, 0.6f, 1f));
        }

        private static IEnumerable<Pawn> Foes(IntVec3 at, Map map, float r) =>
            GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().Where(q => !q.Dead && q.HostileTo(Faction.OfPlayer)).ToList();

        public static void Effect(Snare s, Map map)
        {
            float r = 2.9f * (s.chained ? 1.5f : 1f);
            float pw = s.power;
            Thing by = s.owner;
            Vector3 v = s.cell.ToVector3Shifted();
            switch (s.kind)
            {
                case SnareKind.Binding:
                    foreach (Pawn q in Foes(s.cell, map, r * 1.3f))
                    {
                        IntVec3 land = CellFinder.StandableCellNear(s.cell, map, 1.9f);
                        if (land.IsValid && land.GetFirstPawn(map) == null)
                        {
                            FleckMaker.ThrowDustPuff(q.Position, map, 1f);
                            q.Position = land;
                            q.Notify_Teleported(true, true);
                        }
                        q.stances?.stunner?.StunFor(Mathf.RoundToInt(240f * pw), by, false);
                    }
                    StriderBowFx.Spawn(StriderPaths.Gravity, v, map, StriderBowFx.ColorOf(StriderPaths.Gravity), r * 2f);
                    break;
                case SnareKind.Stunning:
                    foreach (Pawn q in Foes(s.cell, map, r)) q.stances?.stunner?.StunFor(Mathf.RoundToInt(300f * pw), by, true);
                    FleckMaker.ThrowLightningGlow(v, map, r);
                    StriderBowFx.Spawn("Shockwave", v, map, new Color(0.6f, 0.85f, 1f), r * 2f);
                    break;
                case SnareKind.Elemental:
                    if (s.element == "Gold")
                    {
                        GenExplosion.DoExplosion(s.cell, map, r, DamageDefOf.Flame, by, Mathf.RoundToInt(14f * pw), ignoredThings: by != null ? new List<Thing> { by } : null);
                        HediffDef bomb = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_LivingBomb");
                        foreach (Pawn q in Foes(s.cell, map, r).Where(q => !q.Dead))
                        {
                            FireUtility.TryAttachFire(q, 0.5f, by);
                            if (bomb != null && q.health != null) q.health.AddHediff(bomb);
                        }
                    }
                    else if (s.element == "Silver")
                    {
                        HediffDef slow = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CryoSlow");
                        foreach (Pawn q in Foes(s.cell, map, r))
                        {
                            q.TakeDamage(new DamageInfo(DamageDefOf.Frostbite, 12f * pw, 0f, -1f, by));
                            if (!q.Dead && slow != null)
                            {
                                Hediff h = q.health.hediffSet.GetFirstHediffOfDef(slow) ?? q.health.AddHediff(slow);
                                h.Severity = 1f;
                            }
                        }
                        for (int i = 0; i < 6; i++) FleckMaker.ThrowAirPuffUp(v + Rand.InsideUnitCircleVec3 * r, map);
                        StriderBowFx.Spawn("FrostBurst", v, map, new Color(0.7f, 0.9f, 1f), r * 2f);
                    }
                    else GenExplosion.DoExplosion(s.cell, map, r, DamageDefOf.Bomb, by, Mathf.RoundToInt(22f * pw), ignoredThings: by != null ? new List<Thing> { by } : null);
                    break;
                default:
                    HediffDef blind = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Flashblind");
                    foreach (Pawn q in Foes(s.cell, map, r * 1.5f))
                    {
                        q.stances?.stunner?.StunFor(Mathf.RoundToInt(150f * pw), by, false);
                        if (blind != null)
                        {
                            Hediff h = q.health.hediffSet.GetFirstHediffOfDef(blind) ?? q.health.AddHediff(blind);
                            h.Severity = 1f;
                        }
                    }
                    FleckMaker.ThrowLightningGlow(v, map, r * 2f);
                    StriderBowFx.Spawn("FlashBurst", v, map, new Color(1f, 1f, 0.9f), r * 3f);
                    SoundDefOf.Thunder_OnMap?.PlayOneShot(new TargetInfo(s.cell, map));
                    break;
            }
        }
    }

    /// <summary>A burning foe marked by a golden snare may burst when it comes near another foe.</summary>
    public class HediffCompProperties_LivingBomb : HediffCompProperties
    {
        public float chancePerSecond = 0.15f;
        public float radius = 1.9f;
        public int damage = 12;
        public HediffCompProperties_LivingBomb() { compClass = typeof(HediffComp_LivingBomb); }
    }

    public class HediffComp_LivingBomb : HediffComp
    {
        private HediffCompProperties_LivingBomb Props => (HediffCompProperties_LivingBomb)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = Pawn;
            if (!p.IsHashIntervalTick(60) || !p.Spawned || !p.IsBurning()) return;
            Map map = p.Map;
            bool near = GenRadial.RadialDistinctThingsAround(p.Position, map, Props.radius, true)
                .Any(t => t is Pawn q && q != p && !q.Dead && q.HostileTo(Faction.OfPlayer));
            if (!near || !Rand.Chance(Props.chancePerSecond)) return;
            IntVec3 at = p.Position;
            p.health.RemoveHediff(parent);
            GenExplosion.DoExplosion(at, map, Props.radius, DamageDefOf.Flame, null, Props.damage, chanceToStartFire: 0.6f);
        }
    }
}
