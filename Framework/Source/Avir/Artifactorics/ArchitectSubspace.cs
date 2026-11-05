using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // THE LORD OF SUBSPACE.
    //   3  - long hands: blueprints and frames around the architect take their materials out of every storage on the map
    //        and every pawn's inventory (only what the pawn would unload anyway - never what the player told it to carry),
    //        and the lingering things in other colonists' inventories drift into the architect's satchel.
    //   6  - the veil: a barrier around the architect and the allies near them, moving with the architect, that swallows
    //        the shots flying in. Every shot swallowed fills a measure (seen only while the veil is up); a full veil lets
    //        shots through again, until it is paid back as a blast around the architect.
    //   9  - deep pockets: the satchel holds far more and unloads anywhere; subspace storages hold half as much again.
    //   12 - unfolding: the veil spreads wider, swallows twice as much and pays it back harder.
    // =====================================================================================
    public static class SphereSubspace
    {
        public const float VeilCost = 0.05f;
        public const float VeilDrainPerHour = 0.01f;
        public const float SatchelKg = 40f;
        public const float StorageFactor = 1.5f;

        public static float VeilRadius(CompArchitectSpheres c) => c.Stones >= 12 ? 7.9f : 4.9f;
        public static float VeilMax(CompArchitectSpheres c) => 300f * (c.Stones >= 12 ? 2f : 1f);
        public static float BlastRadius(CompArchitectSpheres c) => c.Stones >= 12 ? 8.9f : 6.9f;

        public static Command VeilCommand(Pawn p, CompArchitectSpheres c)
        {
            var cmd = new Command_Toggle
            {
                defaultLabel = c.veilOn ? "Veil: " + c.veilScale.ToString("0") + " / " + VeilMax(c).ToString("0") : "Subspace veil",
                defaultDesc = SpherePaths.Powers(SpherePaths.Subspace)[1][1] + "\n\nRaising it costs " + VeilCost.ToStringPercent()
                              + " of the spheres' charge, holding it " + VeilDrainPerHour.ToStringPercent() + " an hour.",
                icon = SphereIcons.Named("Veil") ?? ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Ink/Rune_Barrier", false) ?? SphereActives.Icon(c),
                isActive = () => c.veilOn,
                toggleAction = () =>
                {
                    if (c.veilOn) { c.veilOn = false; return; }
                    if (!SpherePaths.Pay(p, c, VeilCost)) return;
                    c.veilOn = true;
                }
            };
            if (!c.veilOn) SphereActives.Gate(cmd, p, c, "veil", VeilCost);
            return cmd;
        }

        public static Command DischargeCommand(Pawn p, CompArchitectSpheres c)
        {
            var cmd = new Command_Action
            {
                defaultLabel = "Pay the veil back",
                defaultDesc = "Everything the veil has swallowed bursts out around the architect: a blast that strikes only foes, within "
                              + BlastRadius(c).ToString("0.#") + " cells. The veil's measure must be full.",
                icon = SphereIcons.Named("Veil_Discharge") ?? ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Ink/Rune_Release", false) ?? SphereActives.Icon(c),
                action = () => Discharge(p, c)
            };
            if (c.veilScale < VeilMax(c) - 0.01f) cmd.Disable("The veil's measure is not full (" + c.veilScale.ToString("0") + " / " + VeilMax(c).ToString("0") + ").");
            else if (p.Downed || !p.Spawned) cmd.Disable("Cannot now.");
            return cmd;
        }

        private static void Discharge(Pawn p, CompArchitectSpheres c)
        {
            if (!p.Spawned) return;
            Map map = p.Map;
            float dmg = c.veilScale * 0.25f * SpherePaths.Power(p, SpherePaths.Subspace) * (c.Stones >= 12 ? 1.5f : 1f);
            c.veilScale = 0f;
            foreach (Pawn q in GenRadial.RadialDistinctThingsAround(p.Position, map, BlastRadius(c), true).OfType<Pawn>()
                         .Where(q => !q.Dead && q.HostileTo(p)).ToList())
                q.TakeDamage(new DamageInfo(DamageDefOf.Bomb, dmg, ArtifactCE.Pen(0.5f, 12f), -1f, p));
            RitualFx.Blast(p.DrawPos, map, BlastRadius(c) / 3f);
            StriderBowFx.Spawn("Shockwave", p.DrawPos, map, new Color(0.8f, 0.6f, 1f), BlastRadius(c) * 2f);
            FleckMaker.ThrowLightningGlow(p.DrawPos, map, BlastRadius(c));
        }

        /// <summary>The veil's barrier, kept with the spheres while it is up (not saved: raised again on load).</summary>
        private static readonly Dictionary<CompArchitectSpheres, BarrierDome> veils = new Dictionary<CompArchitectSpheres, BarrierDome>();

        public static void VeilTick(Pawn p, CompArchitectSpheres c)
        {
            veils.TryGetValue(c, out BarrierDome d);
            bool up = c.veilOn && p.Spawned && !p.Dead && c.path == SpherePaths.Subspace && c.Stones >= 6;
            if (!up)
            {
                if (d != null) { BarrierDome.All.Remove(d); veils.Remove(c); }
                if (!p.Spawned || p.Dead) c.veilOn = false;
                return;
            }
            if (d == null || !BarrierDome.All.Contains(d) || d.map != p.Map)
            {
                if (d != null) BarrierDome.All.Remove(d);
                d = new BarrierDome { map = p.Map, center = p.Position, radius = VeilRadius(c), hit = proj => Swallow(c, proj) };
                veils[c] = d;
                BarrierDome.All.Add(d);
            }
            d.center = p.Position;
            d.radius = VeilRadius(c);
            if (p.IsHashIntervalTick(2500))
            {
                ArtifactState st = c.Heart;
                if (st == null || st.charge < VeilDrainPerHour) { c.veilOn = false; return; }
                st.charge -= VeilDrainPerHour;
            }
        }

        /// <summary>Veils whose architect has left the map (a caravan, death) fall.</summary>
        public static void Cleanup()
        {
            foreach (var kv in veils.ToList())
            {
                Pawn w = kv.Key.Wearer;
                if (w != null && w.Spawned && w.Map == kv.Value.map && kv.Key.veilOn) continue;
                BarrierDome.All.Remove(kv.Value);
                veils.Remove(kv.Key);
            }
        }

        private static bool Swallow(CompArchitectSpheres c, Projectile proj)
        {
            float max = VeilMax(c);
            if (c.veilScale >= max) return false;   // full: shots pass until it is paid back
            c.veilScale = Mathf.Min(max, c.veilScale + Mathf.Max(1f, proj.DamageAmount));
            if (c.veilScale >= max && c.Wearer?.Faction == Faction.OfPlayer)
                Messages.Message(c.Wearer.LabelShortCap + "'s veil is full: pay it back.", c.Wearer, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        public static void DrawVeil(Pawn p, CompArchitectSpheres c)
        {
            if (!veils.TryGetValue(c, out BarrierDome d) || d == null) return;
            Vector3 v = p.DrawPos; v.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float full = c.veilScale / VeilMax(c);
            Color col = Color.Lerp(new Color(0.55f, 0.55f, 1f, 0.16f), new Color(0.85f, 0.6f, 1f, 0.34f), full);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(v, Quaternion.identity, new Vector3(d.radius * 2f, 1f, d.radius * 2f)),
                MaterialPool.MatFrom(FlowTex.Dome ?? BaseContent.WhiteTex, ShaderDatabase.Transparent, col), 0);
            // the measure: seen only while the veil is up
            GenDraw.DrawFillableBar(new GenDraw.FillableBarRequest
            {
                center = p.DrawPos + new Vector3(0f, 0f, 0.95f), size = new Vector2(0.9f, 0.12f), fillPercent = full,
                filledMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.8f, 0.6f, 1f)),
                unfilledMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.1f, 0.1f, 0.15f, 0.6f)),
                margin = 0.02f, rotation = Rot4.North
            });
        }

        // ---------------------------------------------------------------- long hands

        public static void LongHands(Pawn p, Map map)
        {
            float reach = 15f;
            Faction own = p.Faction;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(p.Position, map, reach, true).ToList())
            {
                if (t.Faction != own || t.Destroyed) continue;
                if (t is Blueprint_Build bp)
                {
                    // a blueprint becomes a frame at once: the materials will come to it
                    bp.TryReplaceWithSolidThing(p, out Thing made, out bool _);
                    if (made != null) FleckMaker.ThrowMicroSparks(made.DrawPos, map);
                    continue;
                }
                if (!(t is Frame f)) continue;
                foreach (ThingDefCountClass need in f.TotalMaterialCost())
                {
                    int want = f.ThingCountNeeded(need.thingDef);
                    if (want <= 0) continue;
                    if (Bring(p, map, need.thingDef, want, f.resourceContainer) > 0) FleckMaker.ThrowMicroSparks(f.DrawPos, map);
                }
            }
        }

        /// <summary>Moves up to count of def into the container from storage, the satchel and unloadable inventories.</summary>
        private static int Bring(Pawn p, Map map, ThingDef def, int count, ThingOwner into)
        {
            int got = 0;
            foreach (Thing s in map.listerThings.ThingsOfDef(def).ToList())
            {
                if (got >= count) return got;
                if (!s.Spawned || s.IsForbidden(p) || !s.IsInAnyStorage() || map.reservationManager.IsReservedByAnyoneOf(s, p.Faction)) continue;
                int n = Mathf.Min(count - got, s.stackCount);
                Thing part = s.SplitOff(n);
                if (into.TryAdd(part)) got += n;
                else GenPlace.TryPlaceThing(part, p.Position, map, ThingPlaceMode.Near);
            }
            Hediff_TelekineticInventory sat = SatchelInventory.Of(p);
            if (sat?.container != null) got += FromOwner(sat.container, def, count - got, into);
            foreach (Pawn q in map.mapPawns.FreeColonistsSpawned)
            {
                if (got >= count) break;
                if (q.inventory == null || !q.inventory.UnloadEverything) continue;
                got += FromOwner(q.inventory.innerContainer, def, count - got, into);
            }
            return got;
        }

        private static int FromOwner(ThingOwner from, ThingDef def, int count, ThingOwner into)
        {
            int got = 0;
            for (int i = from.Count - 1; i >= 0 && got < count; i--)
            {
                Thing t = from[i];
                if (t.def != def) continue;
                got += from.TryTransferToContainer(t, into, Mathf.Min(count - got, t.stackCount));
            }
            return got;
        }

        /// <summary>Lingering things in other colonists' inventories (what they would unload anyway) drift into the satchel.</summary>
        public static void Drift(Pawn p, Map map)
        {
            Hediff_TelekineticInventory sat = SatchelInventory.Of(p);
            if (sat?.container == null) return;
            foreach (Pawn q in map.mapPawns.FreeColonistsSpawned)
            {
                if (q == p || q.inventory == null || !q.inventory.UnloadEverything) continue;
                ThingCount tc = q.inventory.FirstUnloadableThing;
                if (tc.Thing == null || tc.Count <= 0) continue;
                float mass = tc.Thing.GetStatValue(StatDefOf.Mass);
                int n = mass <= 0f ? tc.Count : Mathf.Min(tc.Count, Mathf.FloorToInt(sat.FreeKg / mass));
                if (n <= 0) continue;
                q.inventory.innerContainer.TryTransferToContainer(tc.Thing, sat.container, n);
            }
        }
    }

    /// <summary>Ticks the paths that live on the map: the veil, the long hands, the obsession, the wires.</summary>
    public class MapComponent_SpherePaths : MapComponent
    {
        public MapComponent_SpherePaths(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 60 == 0) SphereSubspace.Cleanup();
            IReadOnlyList<Pawn> ps = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < ps.Count; i++)
            {
                Pawn p = ps[i];
                CompArchitectSpheres c = CompArchitectSpheres.WornBy(p);
                if (c == null || c.path == null) continue;
                if (c.path == SpherePaths.Subspace) SphereSubspace.VeilTick(p, c);
                if (now % 250 != p.thingIDNumber % 250 || p.Downed || !c.Works(p)) continue;
                int n = c.Stones;
                switch (c.path)
                {
                    case SpherePaths.Subspace:
                        if (n >= 3) { SphereSubspace.LongHands(p, map); if (now % 1000 < 250) SphereSubspace.Drift(p, map); }
                        break;
                    case SpherePaths.Creator:
                        SphereCreator.Tick(p, c, 250);
                        break;
                    case SpherePaths.Energy:
                        if (n >= 9) SphereEnergy.SilverLines(p, map);
                        break;
                }
            }
        }

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            IReadOnlyList<Pawn> ps = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < ps.Count; i++)
            {
                CompArchitectSpheres c = CompArchitectSpheres.WornBy(ps[i]);
                if (c != null && c.veilOn) SphereSubspace.DrawVeil(ps[i], c);
            }
        }
    }
}
