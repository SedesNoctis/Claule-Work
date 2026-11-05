using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // The simpler artifact buildings (concept: Строения-концепт.md). Every effect reads its host:
    // Power (heart quality x metals) for strength, BestQuality / working stones for range.
    // =====================================================================================

    /// <summary>Shared numbers of an aura building.</summary>
    public class CompProperties_ArtifactAura : CompProperties
    {
        public float baseRange = 5f;
        public float rangePerQuality = 0.5f;
        public float rangePerStone = 0f;
        /// <summary>Charge spent each time the building does its work (per rare tick of work).</summary>
        public float chargePerUse = 0.2f;
    }

    public abstract class CompArtifactAura : CompArtifactTotem
    {
        protected CompProperties_ArtifactAura A => (CompProperties_ArtifactAura)props;
        public override float BaseRange => A.baseRange;
        public override float RangePerQuality => A.rangePerQuality;
        public override float RangePerStone => A.rangePerStone;

        protected bool Awake(out CompArtifactHost h)
        {
            h = Host;
            return parent.Spawned && h != null && h.Working > 0;
        }

        protected bool InRange(IntVec3 c) => Range > 0f && (c - parent.Position).LengthHorizontalSquared <= Range * Range;

        protected static string Asleep(string what) => what + " Asleep: no charged artifact.";

        /// <summary>Working buildings with this effect on a map (for the patches below).</summary>
        public static IEnumerable<T> Active<T>(Map map) where T : CompArtifactAura =>
            CompArtifactHost.All.Where(h => h.parent.Map == map && h.Working > 0).Select(h => h.parent.TryGetComp<T>()).Where(c => c != null);
    }

    // ---------------------------------------------------------------- hearth stone: snow and dirt

    public class CompHearthStone : CompArtifactAura
    {
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h)) return;
            Map map = parent.Map;
            bool worked = false;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(parent.Position, Range, true))
            {
                if (!c.InBounds(map)) continue;
                if (map.snowGrid.GetDepth(c) > 0f) { map.snowGrid.SetDepth(c, 0f); worked = true; }
            }
            // and one patch of dirt a pass (more with a stronger stone)
            int n = Mathf.Max(1, Mathf.RoundToInt(h.Power * h.Working));
            foreach (Filth f in GenRadial.RadialDistinctThingsAround(parent.Position, map, Range, true).OfType<Filth>().Take(n).ToList())
            {
                f.ThinFilth();
                worked = true;
            }
            if (worked) h.Spend(A.chargePerUse);
            Warm(h);
        }

        /// <summary>A rough heater and cooler (one stone at power 1 is about a vanilla heater, 87; with the room's band it never overheats): pulls its room into a comfortable band, but never past it
        /// and only weakly - no exact setting, unlike the Avir temperature attuner.</summary>
        public const float BandLow = 16f, BandHigh = 26f, HeatPerStone = 90f;

        private void Warm(CompArtifactHost h)
        {
            Room room = parent.GetRoom();
            if (room == null) return;
            if (room.UsesOutdoorTemperature)
            {
                // under the open sky: the air within its reach is pulled into the band - roughly, 20 degrees a stone at most
                OutdoorClimate.Hold(parent, Range, BandLow, BandHigh, 20f * h.Working * h.Power);
                h.Spend(A.chargePerUse * 0.5f);
                return;
            }
            float t = room.Temperature;
            float dir = t < BandLow ? 1f : t > BandHigh ? -1f : 0f;
            if (dir == 0f) return;
            GenTemperature.PushHeat(parent.Position, parent.Map, dir * HeatPerStone * h.Working * h.Power);
            h.Spend(A.chargePerUse * 0.5f);
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Keeps the ground clear of snow and dirt, and its room roughly warm or cool.")
            : "Keeps " + Range.ToString("0.#") + " cells around clear of snow and cleans " + Mathf.Max(1, Mathf.RoundToInt(h.Power * h.Working)) + " patch(es) of dirt every few seconds. "
              + "Indoors it roughly heats or cools its room into " + BandLow.ToStringTemperature("F0") + " - " + BandHigh.ToStringTemperature("F0") + " (weakly, no exact setting).";
    }

    // ---------------------------------------------------------------- frost stone
    /// <summary>The hearth stone's cold sister: indoors it cools its room down to about -2 C and no colder - a cold store
    /// without power. Stronger with every stone and a stronger stone; it only cools.</summary>
    public class CompFrostStone : CompArtifactAura
    {
        public const float Target = -2f, ColdPerStone = 250f;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h)) return;
            Room room = parent.GetRoom();
            if (room == null || room.UsesOutdoorTemperature) return;
            float t = room.Temperature;
            if (t <= Target) return;
            // gently near the target, so it settles there instead of overshooting
            float k = Mathf.Clamp01((t - Target) / 6f);
            GenTemperature.PushHeat(parent.Position, parent.Map, -ColdPerStone * h.Working * h.Power * Mathf.Max(0.15f, k));
            h.Spend(A.chargePerUse);
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Cools its room to about " + Target.ToStringTemperature("F0") + ", never colder.")
            : "Indoors it cools its room to about " + Target.ToStringTemperature("F0") + " and no colder (a cold store without power); stronger with more and stronger stones. Outdoors it does nothing.";
    }

    // ---------------------------------------------------------------- growth totem

    public class CompProperties_GrowthTotem : CompProperties_ArtifactAura
    {
        /// <summary>Growth bonus at stone power 1.</summary>
        public float growthBonus = 0.15f;
        public float maxBonus = 0.25f;
        public CompProperties_GrowthTotem() { compClass = typeof(CompGrowthTotem); }
    }

    public class CompGrowthTotem : CompArtifactAura
    {
        private CompProperties_GrowthTotem P => (CompProperties_GrowthTotem)props;
        public float Bonus => Awake(out CompArtifactHost h) ? Mathf.Min(P.maxBonus, P.growthBonus * h.Power) : 0f;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (Awake(out CompArtifactHost h)) h.Spend(A.chargePerUse);
        }

        public override string EffectLine() => !Awake(out _)
            ? Asleep("Plants nearby grow faster.")
            : "Plants within " + Range.ToString("0.#") + " cells grow " + Bonus.ToStringPercent() + " faster.";

        public static float BonusAt(Thing plant)
        {
            if (plant?.Map == null || CompArtifactHost.All.Count == 0) return 0f;
            float best = 0f;
            foreach (CompGrowthTotem t in Active<CompGrowthTotem>(plant.Map))
                if (t.InRange(plant.Position)) best = Mathf.Max(best, t.Bonus);
            return best;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRate), MethodType.Getter)]
    public static class Patch_GrowthTotem
    {
        public static void Postfix(Plant __instance, ref float __result)
        {
            if (__result <= 0f) return;
            float b = CompGrowthTotem.BonusAt(__instance);
            if (b > 0f) __result *= 1f + b;
        }
    }

    // ---------------------------------------------------------------- bell of calm (mood aura)

    public class CompCalmBell : CompArtifactAura
    {
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (Awake(out CompArtifactHost h)) h.Spend(A.chargePerUse);
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Its quiet chime calms whoever is near.")
            : "Colonists within " + Range.ToString("0.#") + " cells feel calm (+" + (h.Power >= 1.1f ? 5 : 3) + " mood).";

        public static int StageFor(Pawn p)
        {
            if (p?.Map == null || CompArtifactHost.All.Count == 0) return -1;
            int best = -1;
            foreach (CompCalmBell b in Active<CompCalmBell>(p.Map))
                if (b.InRange(p.Position)) best = Mathf.Max(best, b.Host.Power >= 1.1f ? 1 : 0);
            return best;
        }
    }

    public class ThoughtWorker_CalmBell : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            int s = CompCalmBell.StageFor(p);
            return s < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(s);
        }
    }

    // ---------------------------------------------------------------- mending stone

    public class CompProperties_MendingStone : CompProperties_ArtifactAura
    {
        public float hitPointsPerPass = 10f;
        public CompProperties_MendingStone() { compClass = typeof(CompMendingStone); }
    }

    public class CompMendingStone : CompArtifactAura
    {
        private CompProperties_MendingStone P => (CompProperties_MendingStone)props;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h)) return;
            Map map = parent.Map;
            Building hurt = GenRadial.RadialDistinctThingsAround(parent.Position, map, Range, true).OfType<Building>()
                .Where(b => b.def.useHitPoints && b.HitPoints < b.MaxHitPoints && b.Faction == parent.Faction && !(b is Frame) && b != parent)
                .OrderBy(b => (float)b.HitPoints / b.MaxHitPoints).FirstOrDefault();
            if (hurt == null || !h.Spend(A.chargePerUse)) return;
            hurt.HitPoints = Mathf.Min(hurt.MaxHitPoints, hurt.HitPoints + Mathf.Max(1, Mathf.RoundToInt(P.hitPointsPerPass * h.Power)));
            map.listerBuildingsRepairable.Notify_BuildingRepaired(hurt);
            FleckMaker.ThrowMicroSparks(hurt.DrawPos, map);
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Mends damaged buildings nearby.")
            : "Mends the most damaged building within " + Range.ToString("0.#") + " cells by " + Mathf.RoundToInt(P.hitPointsPerPass * h.Power) + " hit points every few seconds.";
    }

    // ---------------------------------------------------------------- healing stone (its room)

    public class CompProperties_HealingStone : CompProperties_ArtifactAura
    {
        public HediffDef hediff;
        public CompProperties_HealingStone() { compClass = typeof(CompHealingStone); }
    }

    public class CompHealingStone : CompArtifactAura
    {
        private CompProperties_HealingStone P => (CompProperties_HealingStone)props;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h) || P.hediff == null) return;
            Room room = parent.GetRoom();
            if (room == null || room.PsychologicallyOutdoors) return;
            bool any = false;
            foreach (Pawn p in room.ContainedAndAdjacentThings.OfType<Pawn>().ToList())
            {
                if (!p.InBed() || p.GetRoom() != room || p.health == null) continue;
                any = true;
                Hediff hd = p.health.hediffSet.GetFirstHediffOfDef(P.hediff);
                bool fresh = hd == null;
                if (fresh) { hd = HediffMaker.MakeHediff(P.hediff, p); p.health.AddHediff(hd); }
                // no stacking: the strongest charm in the room counts
                HediffComp_Disappears gone = hd.TryGetComp<HediffComp_Disappears>();
                bool stale = gone != null && gone.ticksToDisappear < 340;   // no other charm refreshed it within the last rare tick
                hd.Severity = fresh || stale ? h.Power : Mathf.Max(hd.Severity, h.Power);
                if (gone != null) gone.ticksToDisappear = 600;
            }
            if (any) h.Spend(A.chargePerUse);
        }

        public override string EffectLine() => !Awake(out _)
            ? Asleep("Those resting in its room heal faster and fight off sickness better.")
            : "Those lying in bed in its room heal faster and gain immunity faster (stronger with a stronger stone).";
    }

    // ---------------------------------------------------------------- workbench pylon (a stat part)

    public class CompProperties_WorkPylon : CompProperties_ArtifactAura
    {
        public float speedPerStone = 0.15f;
        public CompProperties_WorkPylon() { compClass = typeof(CompWorkPylon); }
    }

    public class CompWorkPylon : CompArtifactAura
    {
        private CompProperties_WorkPylon P => (CompProperties_WorkPylon)props;
        public float Bonus => Awake(out CompArtifactHost h) ? P.speedPerStone * h.Working * h.Power : 0f;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h)) return;
            // charge only while someone works at a table in range
            Map map = parent.Map;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.CurJob?.bill?.billStack?.billGiver is Thing bench && bench.Spawned && InRange(bench.Position))
                {
                    h.Spend(A.chargePerUse);
                    return;
                }
            }
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Work tables nearby work faster.")
            : "Work tables within " + Range.ToString("0.#") + " cells work " + Bonus.ToStringPercent() + " faster. Spends charge only while someone works at them.";

        public static float BonusFor(Thing bench)
        {
            if (bench?.Map == null || CompArtifactHost.All.Count == 0) return 0f;
            float best = 0f;
            foreach (CompWorkPylon w in Active<CompWorkPylon>(bench.Map))
                if (w.InRange(bench.Position)) best = Mathf.Max(best, w.Bonus);
            return best;
        }
    }

    public class StatPart_WorkPylon : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!req.HasThing) return;
            float b = CompWorkPylon.BonusFor(req.Thing);
            if (b > 0f) val *= 1f + b;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!req.HasThing) return null;
            float b = CompWorkPylon.BonusFor(req.Thing);
            return b > 0f ? "Workbench pylon: x" + (1f + b).ToStringPercent() : null;
        }
    }

    [StaticConstructorOnStartup]
    public static class WorkPylonStatInstaller
    {
        static WorkPylonStatInstaller()
        {
            StatDef s = StatDefOf.WorkTableWorkSpeedFactor;
            if (s == null) return;
            if (s.parts == null) s.parts = new List<StatPart>();
            if (!s.parts.Any(p => p is StatPart_WorkPylon)) s.parts.Add(new StatPart_WorkPylon { parentStat = s });
        }
    }

    // ---------------------------------------------------------------- drag warden

    public class CompProperties_DragWarden : CompProperties_ArtifactAura
    {
        public HediffDef hediff;
        public CompProperties_DragWarden() { compClass = typeof(CompDragWarden); }
    }

    public class CompDragWarden : CompArtifactAura
    {
        private CompProperties_DragWarden P => (CompProperties_DragWarden)props;
        /// <summary>Slow: 30%, 50% with all three stones working.</summary>
        public float Slow => Awake(out CompArtifactHost h) ? (h.Working >= 3 ? 0.5f : 0.3f) : 0f;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h) || P.hediff == null) return;
            Faction own = parent.Faction ?? Faction.OfPlayer;
            bool any = false;
            foreach (Pawn p in parent.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p.Dead || !p.HostileTo(own) || !InRange(p.Position) || p.health == null) continue;
                any = true;
                Hediff hd = p.health.hediffSet.GetFirstHediffOfDef(P.hediff);
                if (hd == null) { hd = HediffMaker.MakeHediff(P.hediff, p); p.health.AddHediff(hd); }
                hd.Severity = Slow;
                HediffComp_Disappears gone = hd.TryGetComp<HediffComp_Disappears>();
                if (gone != null) gone.ticksToDisappear = 300;
            }
            if (any) h.Spend(A.chargePerUse);
        }

        public override string EffectLine() => !Awake(out _)
            ? Asleep("Foes nearby are dragged back by telekinesis.")
            : "Foes within " + Range.ToString("0.#") + " cells move " + Slow.ToStringPercent() + " slower. No damage.";
    }

    // ---------------------------------------------------------------- telekinetic crane

    public class CompProperties_TkCrane : CompProperties_ArtifactAura
    {
        /// <summary>Cells: how far from the crane the storage may be.</summary>
        public float storageReach = 12f;
        public float storageReachPerStone = 6f;
        public CompProperties_TkCrane() { compClass = typeof(CompTkCrane); }
    }

    public class CompTkCrane : CompArtifactAura
    {
        private CompProperties_TkCrane P => (CompProperties_TkCrane)props;
        private float StorageReach => Awake(out CompArtifactHost h) ? P.storageReach + P.storageReachPerStone * (h.Working - 1) : 0f;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h)) return;
            Map map = parent.Map;
            int moves = h.Working;
            float reach = StorageReach;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(parent.Position, map, Range, true).ToList())
            {
                if (moves <= 0) break;
                if (t.def.category != ThingCategory.Item || !t.def.EverHaulable || t is Corpse || t.IsForbidden(Faction.OfPlayer)) continue;
                if (map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer) || map.physicalInteractionReservationManager.IsReserved(t)) continue;
                if (Ritualistics.RitualHold.Held(t)) continue;
                StoragePriority cur = StoreUtility.CurrentStoragePriorityOf(t);
                if (!StoreUtility.TryFindBestBetterStoreCellFor(t, null, map, cur, Faction.OfPlayer, out IntVec3 cell, true)) continue;
                if ((cell - parent.Position).LengthHorizontalSquared > reach * reach) continue;
                if (!h.Spend(A.chargePerUse)) return;
                Vector3 from = t.DrawPos;
                t.DeSpawn();
                if (!GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near)) GenPlace.TryPlaceThing(t, parent.Position, map, ThingPlaceMode.Near);
                FleckMaker.ConnectingLine(from, cell.ToVector3Shifted(), FleckDefOf.PsycastAreaEffect, map, 0.3f);
                moves--;
            }
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Lifts loose things nearby into storage.")
            : "Lifts " + h.Working + " loose thing(s) every few seconds from within " + Range.ToString("0.#") + " cells into storage up to " + StorageReach.ToString("0") + " cells away.";
    }

    // ---------------------------------------------------------------- the observer (Apogee)

    public class CompProperties_Observer : CompProperties_ArtifactAura
    {
        /// <summary>Suppression a slave in range gains per rare tick, x stone power.</summary>
        public float suppressionPerPass = 0.002f;
        /// <summary>Social fights between pawns in range: chance x this (x it again with both eyes).</summary>
        public float socialFightFactor = 0.5f;
        public CompProperties_Observer() { compClass = typeof(CompObserver); }
    }

    public class CompObserver : CompArtifactAura
    {
        private static Material eye1, eye2;

        /// <summary>The eyes light as stones work: none, one, both.</summary>
        public override void PostDraw()
        {
            base.PostDraw();
            int n = Host?.Working ?? 0;
            if (n <= 0) return;
            if (eye1 == null)
            {
                eye1 = MaterialPool.MatFrom("Things/Building/Artifactorics/Observer_1", ShaderDatabase.Transparent);
                eye2 = MaterialPool.MatFrom("Things/Building/Artifactorics/Observer_2", ShaderDatabase.Transparent);
            }
            Vector2 size = parent.def.graphicData.drawSize;
            Vector3 off = parent.def.graphicData.drawOffset;
            Vector3 pos = parent.DrawPos + off;
            pos.y = parent.DrawPos.y + 0.03f;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size.x, 1f, size.y)), n >= 2 ? eye2 : eye1, 0);
        }

        private CompProperties_Observer P => (CompProperties_Observer)props;
        public bool BothEyes => Host != null && Host.Working >= 2;
        public float FightFactor => Awake(out _) ? (BothEyes ? P.socialFightFactor * P.socialFightFactor : P.socialFightFactor) : 1f;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h)) return;
            bool any = false;
            foreach (Pawn p in parent.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p.Dead || !InRange(p.Position)) continue;
                if (p.IsSlaveOfColony && p.needs?.TryGetNeed<Need_Suppression>() is Need_Suppression sup)
                {
                    sup.CurLevel = Mathf.Min(1f, sup.CurLevel + P.suppressionPerPass * h.Power);
                    any = true;
                }
                // both eyes: the unseen are seen
                if (BothEyes && p.HostileTo(parent.Faction ?? Faction.OfPlayer))
                    foreach (Hediff hd in p.health.hediffSet.hediffs)
                    {
                        HediffComp_Invisibility inv = hd.TryGetComp<HediffComp_Invisibility>();
                        if (inv != null && !inv.PsychologicallyVisible) { inv.DisruptInvisibility(); any = true; }
                    }
            }
            if (any) h.Spend(A.chargePerUse);
        }

        public override string EffectLine() => !Awake(out _)
            ? Asleep("Watches: fewer fights, slaves kept in line; with both eyes it sees the unseen.")
            : "Within " + Range.ToString("0.#") + " cells: social fights x" + FightFactor.ToStringPercent() + ", slaves' suppression rises slowly"
              + (BothEyes ? ", the invisible are revealed." : ". Inlay the second eye to reveal the invisible.");

        public static float FightFactorAt(Pawn p)
        {
            if (p?.Map == null || CompArtifactHost.All.Count == 0) return 1f;
            float f = 1f;
            foreach (CompObserver o in Active<CompObserver>(p.Map))
                if (o.InRange(p.Position)) f = Mathf.Min(f, o.FightFactor);
            return f;
        }
    }

    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.SocialFightChance))]
    public static class Patch_ObserverSocialFight
    {
        public static void Postfix(Pawn initiator, ref float __result)
        {
            if (__result > 0f) __result *= CompObserver.FightFactorAt(initiator);
        }
    }

    // ---------------------------------------------------------------- temperature attuner (indoors)

    public class CompProperties_TempAttuner : CompProperties_ArtifactAura
    {
        /// <summary>Heat pushed per rare tick per working stone at power 1 (a vanilla heater: about 87).</summary>
        public float heatPerStone = 120f;
        public CompProperties_TempAttuner() { compClass = typeof(CompTempAttuner); }
    }

    /// <summary>Moves its room toward the target set with the usual temperature buttons (CompTempControl) -
    /// heats or cools at full strength (stronger with more stones), and within a degree of it sets the room exactly
    /// to the target and holds it there. The third stone lights a sun lamp by day.</summary>
    public class CompTempAttuner : CompArtifactAura
    {
        private CompProperties_TempAttuner P => (CompProperties_TempAttuner)props;
        private CompTempControl Control => parent.TryGetComp<CompTempControl>();
        public bool Sun => Awake(out CompArtifactHost h) && h.Working >= 3;

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h) || Control == null) return;
            Room room = parent.GetRoom();
            if (room == null) return;
            if (room.UsesOutdoorTemperature)
            {
                // under the open sky: the air within its reach is held exactly at the target
                OutdoorClimate.Hold(parent, OutdoorRadius(h), Control.TargetTemperature, Control.TargetTemperature, 999f);
                h.Spend(A.chargePerUse);
                lastWorked = Find.TickManager.TicksGame;
                return;
            }
            float target = Control.TargetTemperature, now = room.Temperature;
            float diff = target - now;
            if (Mathf.Abs(diff) < 0.05f) return;
            if (Mathf.Abs(diff) <= 1f)
            {
                // close enough: the stream sets the air exactly where it was told, and holds it there
                room.Temperature = target;
            }
            else
            {
                GenTemperature.PushHeat(parent.Position, parent.Map, P.heatPerStone * h.Working * h.Power * Mathf.Sign(diff));
            }
            h.Spend(A.chargePerUse);
            lastWorked = Find.TickManager.TicksGame;
        }

        /// <summary>Under the open sky it holds a patch of air: 4 cells, +3 a working stone, by its power; never past 14.</summary>
        public static float OutdoorRadius(CompArtifactHost h) => Mathf.Min(14f, (4f + 3f * h.Working) * h.Power);

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (Awake(out CompArtifactHost h) && parent.GetRoom()?.UsesOutdoorTemperature == true) GenDraw.DrawRadiusRing(parent.Position, OutdoorRadius(h));
        }

        private int lastWorked = -99999;
        private static Material onMat;

        /// <summary>The sun half glows while it works.</summary>
        public override void PostDraw()
        {
            base.PostDraw();
            if (Find.TickManager.TicksGame - lastWorked > 600 && !Sun) return;
            if (onMat == null) onMat = MaterialPool.MatFrom("Things/Building/Artifactorics/TempAttuner_On", ShaderDatabase.Transparent);
            Vector3 pos = parent.DrawPos;
            pos.y = AltitudeLayer.FloorEmplacement.AltitudeFor() + 0.02f;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1f, 1f, 1f)), onMat, 0);
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("Brings its room to the temperature you set.")
            : "Heats or cools its room to exactly " + (Control?.TargetTemperature ?? 21f).ToStringTemperature("F0") + " and holds it, with " + h.Working + " stone(s)"
              + (Sun ? "; a sun lamp by day (6-19 h)." : ". The third stone adds a sun lamp by day.");
    }

    public class CompGlower_ArtifactSun : CompGlower
    {
        protected override bool ShouldBeLitNow
        {
            get
            {
                if (!parent.Spawned) return false;
                CompTempAttuner t = parent.TryGetComp<CompTempAttuner>();
                if (t == null || !t.Sun) return false;
                int hour = GenLocalDate.HourOfDay(parent);
                return hour >= 6 && hour < 19;
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (parent.Spawned) UpdateLit(parent.Map);
        }
    }
}
