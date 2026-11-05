using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // The architects' control spheres. Worn on the hands (accessory layer), fed by the artifact
    // inlaid in them, and they answer only an architect - by caste (backstory or pawn kind) or by
    // the architect path of an amulet. In an architect's hands:
    //   - a telekinetic bonus to building (the hediff Avir_ArchitectSpheres: construction speed
    //     and sureness), and the soulmarking tool;
    //   - while the architect finishes a frame themselves, the spheres finish others within reach
    //     at the same time, at part of the architect's speed and with the architect's own skill
    //     (the finished thing's quality is theirs). This is what the feignallite core does with an
    //     architect at its console - here the console is in the architect's hands.
    // The spheres spend their artifact's charge on the work; empty, they only float.
    // =====================================================================================
    public class CompProperties_ArchitectSpheres : CompProperties
    {
        public HediffDef hediff;

        public CompProperties_ArchitectSpheres()
        {
            compClass = typeof(CompArchitectSpheres);
        }
    }

    public class CompArchitectSpheres : ThingComp
    {
        public CompProperties_ArchitectSpheres Props => (CompProperties_ArchitectSpheres)props;

        public Pawn Wearer => (parent as Apparel)?.Wearer;

        // ---- the path (ArchitectSpherePaths.cs): one, for good, and twelve stones of the architect's work ----
        public string path;
        public float xp;
        /// <summary>When each active is ready again (by its key).</summary>
        public Dictionary<string, int> ready = new Dictionary<string, int>();
        /// <summary>Subspace: the veil is up, and how much it has swallowed.</summary>
        public bool veilOn;
        public float veilScale;
        /// <summary>Compulsive Creator: the room the obsession holds, and until when.</summary>
        public IntVec3 obsessionCell = IntVec3.Invalid;
        public int obsessionUntil = -1;

        public int Stones => SpherePaths.StonesFor(xp);

        public int ReadyIn(string key) => ready.TryGetValue(key, out int t) ? Mathf.Max(0, t - Find.TickManager.TicksGame) : 0;

        public void Cooldown(string key, int ticks) => ready[key] = Find.TickManager.TicksGame + ticks;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref path, "spherePath");
            Scribe_Values.Look(ref xp, "sphereXp");
            Scribe_Collections.Look(ref ready, "sphereReady", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref veilOn, "veilOn");
            Scribe_Values.Look(ref veilScale, "veilScale");
            Scribe_Values.Look(ref obsessionCell, "obsessionCell", IntVec3.Invalid);
            Scribe_Values.Look(ref obsessionUntil, "obsessionUntil", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && ready == null) ready = new Dictionary<string, int>();
        }

        /// <summary>The artifact inlaid in the spheres, when it has a heart.</summary>
        public ArtifactState Heart
        {
            get
            {
                ArtifactState st = parent.Socket()?.inlaid;
                return st != null && st.HasHeart ? st : null;
            }
        }

        public bool Works(Pawn p) => p != null && Heart != null && ArchitectSpheres.IsArchitect(p);

        public static CompArchitectSpheres WornBy(Pawn p)
        {
            List<Apparel> worn = p?.apparel?.WornApparel;
            if (worn == null) return null;
            for (int i = 0; i < worn.Count; i++)
            {
                CompArchitectSpheres c = worn[i].TryGetComp<CompArchitectSpheres>();
                if (c != null) return c;
            }
            return null;
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            Sync(pawn);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            ArchitectSpheres.SetHediff(pawn, Props.hediff, false);
            SpherePassives.Sync(pawn, null);
            veilOn = false;
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            Pawn w = Wearer;
            if (w != null) Sync(w);
        }

        /// <summary>The hediff (bonus and the soulmarking tool) is there exactly while the spheres answer their wearer.</summary>
        public void Sync(Pawn p)
        {
            bool works = Works(p);
            ArchitectSpheres.SetHediff(p, Props.hediff, works);
            SpherePassives.Sync(p, works ? this : null);
        }

        public override string CompInspectStringExtra()
        {
            ArtifactState st = Heart;
            if (st == null) return "No artifact in the spheres: they lie still.";
            Pawn w = Wearer;
            string s = "Charge: " + st.charge.ToStringPercent();
            s += path == null ? "\nNo path chosen yet." : "\n" + SpherePaths.Label(path).CapitalizeFirst() + ": " + Stones + " / " + SpherePaths.MaxStones + " stones.";
            if (w != null && !ArchitectSpheres.IsArchitect(w)) s += "\nThe spheres do not answer " + w.LabelShort + ": not an architect.";
            return s;
        }
    }

    public static class ArchitectSpheres
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        /// <summary>An architect by caste (backstory or pawn kind) or by the architect path of an amulet.</summary>
        public static bool IsArchitect(Pawn p)
        {
            if (p == null) return false;
            if (p.kindDef?.defName == "Avir_Architect") return true;
            if (Has(p.story?.Childhood) || Has(p.story?.Adulthood)) return true;
            return AmuletDevelopment.WornBy(p)?.subclass?.defName == "Avir_Subclass_Architect";
        }

        private static bool Has(BackstoryDef b) => b?.spawnCategories != null && b.spawnCategories.Contains("AvirArchitect");

        public static void SetHediff(Pawn p, HediffDef def, bool on)
        {
            if (p?.health == null || def == null) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(def);
            if (on && h == null) p.health.AddHediff(def);
            else if (!on && h != null) p.health.RemoveHediff(h);
        }

        public static int FrameCount(Pawn p)
        {
            if (S == null) return 0;
            int skill = p.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0;
            return Mathf.Min(S.sphereFramesMax, S.sphereFramesBase + skill / Mathf.Max(1, S.sphereFramesSkillStep));
        }
    }

    /// <summary>The spheres' own work: while an architect finishes a frame, they finish others in reach.</summary>
    public class MapComponent_ArchitectSpheres : MapComponent
    {
        private const int Interval = 30;

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public MapComponent_ArchitectSpheres(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (S == null || Find.TickManager.TicksGame % Interval != 0) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Downed || p.Drafted || p.CurJobDef != JobDefOf.FinishFrame) continue;
                CompArchitectSpheres sp = CompArchitectSpheres.WornBy(p);
                if (sp == null || !sp.Works(p)) continue;
                Work(p, sp);
            }
        }

        private void Work(Pawn p, CompArchitectSpheres sp)
        {
            ArtifactState st = sp.Heart;
            if (st == null || st.charge <= 0f) return;
            int n = ArchitectSpheres.FrameCount(p);
            float reach = S.sphereReach;
            Thing own = p.CurJob?.targetA.Thing;
            float success = p.GetStatValue(StatDefOf.ConstructSuccessChance);
            float baseWork = p.GetStatValue(StatDefOf.ConstructionSpeed) * 1.7f * Interval * S.sphereSpeed;

            List<Frame> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).OfType<Frame>()
                .Where(f => f != own && f.Spawned && f.Faction == p.Faction && f.IsCompleted()
                            && (f.Position - p.Position).LengthHorizontalSquared <= reach * reach
                            && !f.IsForbidden(p) && !map.reservationManager.IsReservedByAnyoneOf(f, p.Faction)
                            && GenConstruct.CanConstruct(f, p, true))
                .OrderBy(f => (f.Position - p.Position).LengthHorizontalSquared).Take(n).ToList();

            for (int i = 0; i < frames.Count; i++)
            {
                Frame f = frames[i];
                float work = baseWork;
                if (f.Stuff != null) work *= f.Stuff.GetStatValueAbstract(StatDefOf.ConstructionSpeedFactor);
                float need = f.WorkToBuild;
                work = Mathf.Min(work, Mathf.Max(0f, need - f.workDone));
                float cost = work / 1000f * S.sphereChargePerKWork;
                if (st.charge < cost) return;
                st.charge -= cost;
                p.skills?.Learn(SkillDefOf.Construction, 0.25f * Interval * S.sphereSpeed);
                if (Rand.Chance(0.35f)) FleckMaker.ThrowMicroSparks(f.DrawPos, map);
                // the spheres are the architect's hands: the same chance to spoil the work
                if (Rand.Value < 1f - Mathf.Pow(success, work / Mathf.Max(1f, need)))
                {
                    f.FailConstruction(p);
                    continue;
                }
                if (f.def.entityDefToBuild is TerrainDef) map.snowGrid.SetDepth(f.Position, 0f);
                f.workDone += work;
                if (f.workDone >= need)
                {
                    FleckMaker.ThrowLightningGlow(f.DrawPos, map, 0.8f);
                    f.CompleteConstruction(p);
                }
            }
        }
    }
}
