using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Artifactorics
{
    [DefOf]
    public static class ArtifactEfficiencyDefOf
    {
        public static HediffDef Avir_TelekineticInventory;

        static ArtifactEfficiencyDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactEfficiencyDefOf)); }
    }

    /// <summary>
    /// The telekinetic inventory ("subspace satchel"). A separate store on the pawn: its weight does
    /// not count toward carrying capacity and does not slow the pawn. Written to be shared - the
    /// Efficiency arm gives it now, amulets and carried artifact-containers can give the same hediff
    /// later and only set capacityKg.
    ///   - picks up loose items within reach (walking or standing at work) that belong in storage;
    ///   - drops them telekinetically into a storage cell within reach (twice the reach when almost
    ///     full); never makes the pawn walk;
    ///   - everything falls to the ground if the pawn goes down or dies, or loses the satchel.
    /// </summary>
    /// <summary>On a subspace capacitor's def: the satchel kilos it adds.</summary>
    public class SubspaceCapacitorExtension : DefModExtension
    {
        public float satchelKg = 100f;
        public float kgPerQualityStep = 20f;
    }

    public class Hediff_TelekineticInventory : HediffWithComps, IThingHolder
    {
        public ThingOwner<Thing> container;
        public float capacityKg = 15f;
        /// <summary>False when the satchel is only a subspace capacitor's: no telekinesis, so nothing is
        /// gathered or unloaded at a distance - the bearer packs its own inventory into it instead.</summary>
        public bool telekinetic = true;
        public bool autoPickup = true;
        public bool autoUnload = true;
        /// <summary>Set each pass by the effects manager: architect 15 unloads into any storage on the map.</summary>
        public bool unloadAnywhere;
        /// <summary>Things kept with the bearer: never unloaded into storage (thingIDNumber).</summary>
        public List<int> keep = new List<int>();

        public bool Keeps(Thing t) => t != null && keep.Contains(t.thingIDNumber);

        public void SetKeep(Thing t, bool on)
        {
            if (t == null) return;
            if (on) { if (!keep.Contains(t.thingIDNumber)) keep.Add(t.thingIDNumber); }
            else keep.Remove(t.thingIDNumber);
        }

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public IThingHolder ParentHolder => pawn;

        public ThingOwner GetDirectlyHeldThings() => container;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void PostMake()
        {
            base.PostMake();
            if (container == null) container = new ThingOwner<Thing>(this);
        }

        public float MassCarried
        {
            get
            {
                float m = 0f;
                if (container == null) return 0f;
                for (int i = 0; i < container.Count; i++)
                    m += container[i].GetStatValue(StatDefOf.Mass) * container[i].stackCount;
                return m;
            }
        }

        /// <summary>The bearer's capacity without the satchel (set by the Capacity patch); -1 unknown.</summary>
        public float baseCapacity = -1f;

        /// <summary>Kilos of the bearer's own inventory (and gear) above its normal capacity: they
        /// ride in the satchel too, which is how the satchel extends the inventory.</summary>
        public float InventoryOverflow =>
            baseCapacity < 0f ? 0f : Mathf.Max(0f, MassUtility.GearAndInventoryMass(pawn) - baseCapacity);

        public float FreeKg => Mathf.Max(0f, capacityKg - MassCarried - InventoryOverflow);

        public override string LabelInBrackets => (MassCarried + InventoryOverflow).ToString("0.#") + " / " + capacityKg.ToString("0.#") + " kg";

        /// <summary>Called by the effects manager twice a second.</summary>
        public void Work()
        {
            if (container == null) container = new ThingOwner<Thing>(this);
            if (!pawn.Spawned) return;
            if (pawn.Downed) { DropAll(); return; }
            if (pawn.Faction == Faction.OfPlayer) SatchelInventory.ServeConsumables(pawn, this);
            if (!telekinetic) return;   // a capacitor alone: a larger inventory, nothing at a distance
            if (autoUnload) { UnloadOne(); GoUnloadIfIdle(); }
            // up to three things a pass (twice a second): the satchel reacts to what the bearer walks past -
            // but not past three quarters full, so it never clogs with what it cannot get rid of
            // nothing is picked up while the bearer performs a ritual
            if (autoPickup && MassCarried < capacityKg * FullShare && !Ritualistics.RitualHold.Performing(pawn)) for (int i = 0; i < 3 && PickupOne(); i++) { }
        }

        /// <summary>Past this share of capacity the satchel stops picking up and its bearer, when idle, goes to unload.</summary>
        private const float FullShare = 0.75f;
        private int nextUnloadTrip;

        /// <summary>A full satchel is emptied on purpose: an idle bearer (wandering, waiting) is sent to the nearest
        /// storage that takes something it carries, and the satchel unloads on arrival. At most once an hour.</summary>
        private void GoUnloadIfIdle()
        {
            if (container.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            // full, or something has been riding along for hours: worth the walk
            if (MassCarried < capacityKg * FullShare && now - lastUnloadTick < StaleTicks) return;
            if (now < nextUnloadTrip) return;
            if (pawn.Faction != Faction.OfPlayer || pawn.Drafted || pawn.InMentalState || !pawn.Awake()) return;
            JobDef cur = pawn.CurJobDef;
            bool idle = cur == null || cur == JobDefOf.Wait_Wander || cur == JobDefOf.GotoWander || cur == JobDefOf.Wait
                        || cur == JobDefOf.Wait_MaintainPosture;
            if (!idle) return;
            Map map = pawn.Map;
            IntVec3 best = IntVec3.Invalid;
            float bestDist = float.MaxValue;
            for (int i = 0; i < container.Count; i++)
            {
                Thing t = container[i];
                if (Keeps(t)) continue;
                if (!StoreUtility.TryFindBestBetterStoreCellFor(t, null, map, StoragePriority.Unstored, pawn.Faction, out IntVec3 cell, false)) continue;
                float d = cell.DistanceToSquared(pawn.Position);
                if (d < bestDist && pawn.CanReach(cell, PathEndMode.Touch, Danger.Some)) { bestDist = d; best = cell; }
            }
            nextUnloadTrip = now + 2500;
            if (!best.IsValid) return;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, best);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
        }

        private bool WantsStorage(Thing t)
        {
            if (t == null || !t.Spawned || t.def.category != ThingCategory.Item || !t.def.EverHaulable) return false;
            if (t is Corpse || t.IsForbidden(pawn) || t.IsForbidden(Faction.OfPlayer)) return false;
            // what a pawn has just put down (from its inventory, its hands or this satchel) stays where it was put
            if (SatchelDropMemory.Recent(t)) return false;
            // what lies in a ritual stays there
            if (Ritualistics.RitualHold.Held(t)) return false;
            if (StoreUtility.IsInValidBestStorage(t)) return false;
            Map map = pawn.Map;
            if (map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer)) return false;
            if (map.physicalInteractionReservationManager.IsReserved(t)) return false;
            // telekinesis: no path is needed, so no carrier for the storage search
            return StoreUtility.TryFindBestBetterStoreCellFor(t, null, map, StoreUtility.CurrentStoragePriorityOf(t), pawn.Faction, out _, false);
        }

        private bool PickupOne()
        {
            float free = FreeKg;
            if (free <= 0.01f) return false;
            Map map = pawn.Map;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(pawn.Position, map, S.tkPickupRadius, true))
            {
                if (!WantsStorage(t)) continue;
                float per = t.GetStatValue(StatDefOf.Mass);
                int count = per <= 0.0001f ? t.stackCount : Mathf.Min(t.stackCount, Mathf.FloorToInt(free / per));
                if (count <= 0) continue;
                Vector3 from = t.DrawPos;
                Thing taken = count >= t.stackCount ? t : t.SplitOff(count);
                if (taken.Spawned) taken.DeSpawn();
                if (!container.TryAdd(taken, true))
                {
                    GenPlace.TryPlaceThing(taken, pawn.Position, map, ThingPlaceMode.Near);
                    return false;
                }
                FleckMaker.ThrowMicroSparks(from, map);
                return true;
            }
            return false;
        }

        /// <summary>When the satchel last put something into storage. The longer it carries things without a store
        /// in reach, the further it throws them: the reach doubles every two hours, and after a day any storage
        /// on the map will do - nothing rides in it for ever.</summary>
        private int lastUnloadTick = -1;
        private const int StaleTicks = 10000;   // four hours

        private float UnloadReach()
        {
            if (unloadAnywhere) return float.MaxValue;
            int now = Find.TickManager.TicksGame;
            if (lastUnloadTick < 0 || lastUnloadTick > now) lastUnloadTick = now;
            float hours = (now - lastUnloadTick) / 2500f;
            if (hours >= 24f) return float.MaxValue;
            return S.tkUnloadRadius * (MassCarried >= capacityKg * 0.9f ? 2f : 1f) * Mathf.Pow(2f, hours / 2f);
        }

        private void UnloadOne()
        {
            if (container.Count == 0 || container.All(Keeps)) { lastUnloadTick = Find.TickManager.TicksGame; return; }
            Map map = pawn.Map;
            float reach = UnloadReach();
            for (int i = 0; i < container.Count; i++)
            {
                Thing t = container[i];
                if (Keeps(t)) continue;
                if (!StoreUtility.TryFindBestBetterStoreCellFor(t, null, map, StoragePriority.Unstored, pawn.Faction, out IntVec3 cell, false))
                    continue;
                if (cell.DistanceTo(pawn.Position) > reach) continue;
                if (container.TryDrop(t, cell, map, ThingPlaceMode.Direct, out Thing dropped))
                {
                    if (dropped != null) FleckMaker.ThrowMicroSparks(dropped.DrawPos, map);
                    lastUnloadTick = Find.TickManager.TicksGame;
                    return;
                }
            }
        }

        /// <summary>By order: takes as much of the thing as fits (allowing it if it was forbidden).
        /// Returns the count taken.</summary>
        public int PickUp(Thing t)
        {
            if (container == null) container = new ThingOwner<Thing>(this);
            if (t == null || !t.Spawned || !pawn.Spawned || t.Map != pawn.Map) return 0;
            float per = t.GetStatValue(StatDefOf.Mass);
            int count = per <= 0.0001f ? t.stackCount : Mathf.Min(t.stackCount, Mathf.FloorToInt(FreeKg / per));
            if (count <= 0) return 0;
            Map map = pawn.Map;
            Vector3 from = t.DrawPos;
            Thing taken = count >= t.stackCount ? t : t.SplitOff(count);
            if (taken.Spawned) taken.DeSpawn();
            taken.SetForbidden(false, false);
            // taken by order: kept with the bearer (not merged into a stack the satchel may unload)
            if (!container.TryAdd(taken, false)) { GenPlace.TryPlaceThing(taken, pawn.Position, map, ThingPlaceMode.Near); return 0; }
            SetKeep(taken, true);
            FleckMaker.ThrowMicroSparks(from, map);
            return count;
        }

        /// <summary>Out of the satchel into the bearer's inventory (all of it, or `count`). Returns what landed there.</summary>
        public Thing ToInventory(Thing t, int count = -1)
        {
            ThingOwner<Thing> pack = pawn.inventory?.innerContainer;
            if (pack == null || container == null || !container.Contains(t)) return null;
            if (count <= 0 || count > t.stackCount) count = t.stackCount;
            SetKeep(t, false);
            int moved = container.TryTransferToContainer(t, pack, count, out Thing result, true);
            return moved > 0 ? result : null;
        }

        /// <summary>Whether the bearer could eat or take this now (as the Gear tab offers it for the inventory).</summary>
        public bool CanConsume(Thing t) =>
            t != null && t.def.IsIngestible && pawn.Spawned && pawn.IsColonistPlayerControlled && !pawn.Downed
            && (t.def.IsDrug || FoodUtility.WillEat(pawn, t, pawn, true, false));

        /// <summary>Eat or take it now: what is needed goes into the inventory and the bearer is ordered to ingest it.</summary>
        public void Consume(Thing t)
        {
            if (!CanConsume(t)) return;
            int n = Mathf.Max(1, Mathf.Min(t.stackCount, FoodUtility.WillIngestStackCountOf(pawn, t.def, FoodUtility.NutritionForEater(pawn, t))));
            Thing inInv = ToInventory(t, n);
            if (inInv != null) FoodUtility.IngestFromInventoryNow(pawn, inInv);
        }

        public void Drop(Thing t)
        {
            Map map = pawn.MapHeld;
            if (map == null || container == null || !container.Contains(t)) return;
            container.TryDrop(t, pawn.PositionHeld, map, ThingPlaceMode.Near, out _);
        }

        public void DropAll()
        {
            Map map = pawn.MapHeld;
            if (map == null || container == null || container.Count == 0) return;
            container.TryDropAll(pawn.PositionHeld, map, ThingPlaceMode.Near);
        }

        public override void PostRemoved()
        {
            DropAll();
            base.PostRemoved();
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            DropAll();
            base.Notify_PawnDied(dinfo, culprit);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref container, "container", this);
            Scribe_Values.Look(ref capacityKg, "capacityKg", 15f);
            Scribe_Values.Look(ref autoPickup, "autoPickup", true);
            Scribe_Values.Look(ref autoUnload, "autoUnload", true);
            Scribe_Values.Look(ref lastUnloadTick, "lastUnloadTick", -1);
            Scribe_Values.Look(ref telekinetic, "telekinetic", true);
            Scribe_Collections.Look(ref keep, "keep", LookMode.Value);
            if (keep == null) keep = new List<int>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && container == null) container = new ThingOwner<Thing>(this);
        }
    }

    /// <summary>The subspace satchel's own tab (like Gear), on the artificer's parchment:
    /// weight, the pick-up / unload switches, contents with drop buttons.</summary>
    public class ITab_Pawn_Satchel : ITab
    {
        /// <summary>"Eat" (for what the bearer can ingest now) and "Out" (into the inventory - for a caravan's trade,
        /// the drug policy, or to have it at hand).</summary>
        public static void SatchelRowButtons(Hediff_TelekineticInventory inv, Thing t, Rect r)
        {
            Rect eat = new Rect(r.x, r.y, 44f, r.height), outR = new Rect(r.x + 48f, r.y, 44f, r.height);
            if (inv.CanConsume(t))
            {
                if (Widgets.ButtonText(eat, t.def.IsDrug ? "Take" : "Eat", false)) inv.Consume(t);
                TooltipHandler.TipRegion(eat, "Eat or take it now, straight out of the satchel.");
            }
            if (inv.pawn.inventory != null)
            {
                if (Widgets.ButtonText(outR, "Out", false)) inv.ToInventory(t);
                TooltipHandler.TipRegion(outR, "Into the inventory: traders see it there, the bearer uses it from there.");
            }
        }

        private Vector2 scroll;
        private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        private static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);
        private static readonly Color Gold = new Color(0.70f, 0.50f, 0.12f);

        public ITab_Pawn_Satchel()
        {
            size = new Vector2(460f, 520f);
            labelKey = "Avir_TabSatchel";
        }

        /// <summary>Never taller than the screen above the inspect pane: at a large interface scale the top of the
        /// tab (its title and the list) went off the screen.</summary>
        protected override void UpdateSize()
        {
            base.UpdateSize();
            size.y = Mathf.Max(320f, Mathf.Min(520f, PaneTopY - 30f - 4f));
        }

        private Hediff_TelekineticInventory Inv =>
            SelPawn?.health?.hediffSet?.GetFirstHediffOfDef(ArtifactEfficiencyDefOf.Avir_TelekineticInventory) as Hediff_TelekineticInventory;

        public override bool IsVisible => Inv != null;

        private Vector2 tabScroll;

        /// <summary>The whole tab; when the screen leaves less room, it scrolls.</summary>
        protected override void FillTab()
        {
            Rect outer = new Rect(0f, 0f, size.x, size.y);
            if (size.y >= 520f) { FillContent(outer); return; }
            ArtifactoricsTex.TileParchment(outer);
            Rect view = new Rect(0f, 0f, size.x - 16f, 520f);
            Widgets.BeginScrollView(outer, ref tabScroll, view);
            FillContent(view);
            Widgets.EndScrollView();
        }

        private void FillContent(Rect r)
        {
            Hediff_TelekineticInventory inv = Inv;
            if (inv == null) return;
            ArtifactoricsTex.TileParchment(r);
            Rect inner = r.ContractedBy(14f);
            Color oc = GUI.color; GameFont of = Text.Font; TextAnchor oa = Text.Anchor;
            try
            {
                float y = inner.y;
                GUI.color = Ink; Text.Font = GameFont.Medium;
                ArtInk.Label(new Rect(inner.x, y, inner.width, 32f), "Subspace satchel");
                y += 32f;
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                ArtInk.Label(new Rect(inner.x, y, inner.width, 18f), "A telekinetic inventory: its weight does not burden the bearer.");
                y += 22f;
                Text.Font = GameFont.Small;

                float frac = inv.capacityKg > 0f ? inv.MassCarried / inv.capacityKg : 0f;
                Rect bar = new Rect(inner.x, y, inner.width, 20f);
                Widgets.DrawBoxSolid(bar, new Color(0.25f, 0.17f, 0.08f, 0.30f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(frac), bar.height), new Color(Gold.r, Gold.g, Gold.b, 0.85f));
                GUI.color = InkSoft; Widgets.DrawBox(bar, 1);
                GUI.color = Ink; Text.Anchor = TextAnchor.MiddleCenter;
                ArtInk.Label(bar, inv.LabelInBrackets);
                Text.Anchor = TextAnchor.UpperLeft;
                y += 26f;

                GUI.color = Ink;
                Widgets.CheckboxLabeled(new Rect(inner.x, y, inner.width / 2f - 8f, 24f), "Pick up in passing", ref inv.autoPickup);
                Widgets.CheckboxLabeled(new Rect(inner.x + inner.width / 2f + 8f, y, inner.width / 2f - 8f, 24f), "Unload to storage", ref inv.autoUnload);
                y += 32f;

                Rect list = new Rect(inner.x, y, inner.width, inner.yMax - y - 40f);
                List<Thing> things = inv.container?.ToList() ?? new List<Thing>();
                Rect view = new Rect(0f, 0f, list.width - 16f, Mathf.Max(list.height, things.Count * 32f));
                Widgets.BeginScrollView(list, ref scroll, view);
                float ry = 0f;
                if (things.Count == 0)
                {
                    GUI.color = InkSoft;
                    ArtInk.Label(new Rect(0f, 0f, view.width, 24f), "Empty.");
                }
                foreach (Thing t in things)
                {
                    Rect row = new Rect(0f, ry, view.width, 30f);
                    if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                    GUI.color = Color.white;
                    Widgets.ThingIcon(new Rect(row.x + 2f, row.y + 3f, 24f, 24f), t, 1f);
                    GUI.color = Ink; Text.Anchor = TextAnchor.MiddleLeft;
                    ArtInk.Label(new Rect(row.x + 32f, row.y, row.width - 32f - 210f - 96f, row.height), t.LabelCap);
                    GUI.color = InkSoft;
                    ArtInk.Label(new Rect(row.xMax - 128f, row.y, 64f, row.height), (t.GetStatValue(StatDefOf.Mass) * t.stackCount).ToStringMass());
                    // keep with me: never unloaded into storage
                    bool kept = inv.Keeps(t);
                    Rect kr = new Rect(row.xMax - 206f, row.y + 3f, 74f, 24f);
                    GUI.color = Ink;
                    bool k2 = kept;
                    Widgets.CheckboxLabeled(kr, "Keep", ref k2);
                    if (k2 != kept) inv.SetKeep(t, k2);
                    TooltipHandler.TipRegion(kr, "Keep with me: never put into storage by the satchel.");
                    Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                    if (Widgets.ButtonText(new Rect(row.xMax - 60f, row.y + 3f, 58f, 24f), "Drop", false)) inv.Drop(t);
                    SatchelRowButtons(inv, t, new Rect(row.xMax - 206f - 96f, row.y + 3f, 92f, 24f));
                    ry += 32f;
                }
                Widgets.EndScrollView();
                GUI.color = Color.white;
                if (Widgets.ButtonText(new Rect(inner.xMax - 140f, inner.yMax - 32f, 140f, 30f), "Drop all", false))
                {
                    inv.DropAll();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            finally { GUI.color = oc; Text.Font = of; Text.Anchor = oa; }
        }
    }

    /// <summary>What a pawn has just put down - from its inventory, its hands or the satchel - is left where it was
    /// put for a while: dropping something means "it stays here", not "take it back".</summary>
    public static class SatchelDropMemory
    {
        public const int Ticks = 5000;   // two hours
        private static readonly Dictionary<int, int> until = new Dictionary<int, int>();

        public static void Note(Thing t)
        {
            if (t == null) return;
            int now = Find.TickManager.TicksGame;
            if (until.Count > 2000) foreach (int k in until.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList()) until.Remove(k);
            until[t.thingIDNumber] = now + Ticks;
        }

        public static bool Recent(Thing t) => t != null && until.TryGetValue(t.thingIDNumber, out int u) && Find.TickManager.TicksGame < u;

        public static bool FromPawn(ThingOwner o) =>
            o?.Owner is Pawn_InventoryTracker || o?.Owner is Pawn_CarryTracker || o?.Owner is Hediff_TelekineticInventory;
    }

    [HarmonyLib.HarmonyPatch]
    public static class Patch_SatchelDropMemory
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var m in typeof(ThingOwner).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (m.Name != "TryDrop" || m.IsAbstract) continue;
                var ps = m.GetParameters();
                if (ps.Length > 3 && ps[1].ParameterType == typeof(IntVec3) && ps[2].ParameterType == typeof(Map)) yield return m;
            }
        }

        /// <summary>The thing on the ground: the out parameter (what landed), else the thing dropped whole.</summary>
        public static void Postfix(ThingOwner __instance, bool __result, object[] __args)
        {
            if (!__result || !SatchelDropMemory.FromPawn(__instance)) return;
            for (int i = __args.Length - 1; i >= 1; i--) if (__args[i] is Thing t && t.Spawned) { SatchelDropMemory.Note(t); return; }
            if (__args.Length > 0 && __args[0] is Thing first && first.Spawned) SatchelDropMemory.Note(first);
        }
    }

    /// <summary>In a caravan the satchel empties itself into the bearer's inventory: the caravan's trade, its food and
    /// its loads see everything (the satchel's kilos are carrying capacity, so nothing gets heavier).</summary>
    public class WorldComponent_SatchelCaravan : RimWorld.Planet.WorldComponent
    {
        public WorldComponent_SatchelCaravan(RimWorld.Planet.World world) : base(world) { }

        public override void WorldComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 17) return;
            foreach (RimWorld.Planet.Caravan c in Find.WorldObjects.Caravans)
                foreach (Pawn p in c.PawnsListForReading)
                {
                    var inv = SatchelInventory.Of(p);
                    if (inv?.container == null || inv.container.Count == 0 || p.inventory == null) continue;
                    inv.keep.Clear();
                    inv.container.TryTransferAllToContainer(p.inventory.innerContainer, true);
                }
        }
    }

    /// <summary>The satchel's little menu: contents, weight, switches, drop buttons.</summary>
    public class Dialog_TelekineticInventory : Window
    {
        private readonly Hediff_TelekineticInventory inv;
        private Vector2 scroll;

        public Dialog_TelekineticInventory(Hediff_TelekineticInventory inv)
        {
            this.inv = inv;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(420f, 480f);

        public override void DoWindowContents(Rect inRect)
        {
            if (inv?.pawn == null || inv.pawn.Dead || !inv.pawn.health.hediffSet.hediffs.Contains(inv)) { Close(); return; }
            Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Subspace satchel - " + inv.pawn.LabelShort);
            Text.Font = GameFont.Small;
            float y = inRect.y + 36f;

            float frac = inv.capacityKg > 0f ? inv.MassCarried / inv.capacityKg : 0f;
            Rect bar = new Rect(inRect.x, y, inRect.width, 22f);
            Widgets.FillableBar(bar, Mathf.Clamp01(frac));
            Text.Anchor = TextAnchor.MiddleCenter;
            ArtInk.Label(bar, inv.LabelInBrackets);
            Text.Anchor = TextAnchor.UpperLeft;
            y += 28f;

            Widgets.CheckboxLabeled(new Rect(inRect.x, y, inRect.width / 2f - 6f, 24f), "Pick up", ref inv.autoPickup);
            Widgets.CheckboxLabeled(new Rect(inRect.x + inRect.width / 2f + 6f, y, inRect.width / 2f - 6f, 24f), "Unload", ref inv.autoUnload);
            y += 30f;

            Rect list = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - 40f);
            List<Thing> things = inv.container?.ToList() ?? new List<Thing>();
            Rect view = new Rect(0f, 0f, list.width - 16f, Mathf.Max(list.height, things.Count * 30f));
            Widgets.BeginScrollView(list, ref scroll, view);
            float ry = 0f;
            if (things.Count == 0)
            {
                GUI.color = Color.gray;
                ArtInk.Label(new Rect(0f, 0f, view.width, 24f), "Empty.");
                GUI.color = Color.white;
            }
            foreach (Thing t in things)
            {
                Rect row = new Rect(0f, ry, view.width, 28f);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                Widgets.ThingIcon(new Rect(row.x + 2f, row.y + 2f, 24f, 24f), t, 1f);
                Text.Anchor = TextAnchor.MiddleLeft;
                ArtInk.Label(new Rect(row.x + 32f, row.y, row.width - 32f - 130f - 96f, row.height), t.LabelCap);
                ArtInk.Label(new Rect(row.xMax - 128f, row.y, 64f, row.height),
                    (t.GetStatValue(StatDefOf.Mass) * t.stackCount).ToStringMass());
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonText(new Rect(row.xMax - 60f, row.y + 2f, 58f, 24f), "Drop")) inv.Drop(t);
                ITab_Pawn_Satchel.SatchelRowButtons(inv, t, new Rect(row.xMax - 130f - 96f, row.y + 2f, 92f, 24f));
                ry += 30f;
            }
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(inRect.xMax - 140f, inRect.yMax - 32f, 140f, 30f), "Drop all"))
            {
                inv.DropAll();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
        }
    }

    /// <summary>
    /// Stage 8e - Efficiency, and the Learning mentor aura.
    ///   Efficiency 3  flicks switches marked for flicking within reach, in passing.
    ///   Efficiency 7  thins filth within reach, in passing (home area).
    ///   Efficiency 9  the subspace satchel: 15 kg x arm strength.
    /// "In passing" means while the pawn goes about anything else - walking or standing at work.
    /// </summary>
    public class MapComponent_ArtifactEfficiency : MapComponent
    {
        private const int Interval = 30;
        private readonly Dictionary<int, float> cleanBudget = new Dictionary<int, float>();

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public MapComponent_ArtifactEfficiency(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (S == null || Find.TickManager.TicksGame % Interval != 0) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.apparel == null || p.Dead) continue;
                Hediff_TelekineticInventory inv = p.health.hediffSet.GetFirstHediffOfDef(ArtifactEfficiencyDefOf.Avir_TelekineticInventory)
                    as Hediff_TelekineticInventory;
                // The satchel comes from the Efficiency 9 milestone or from a worn Avir amulet;
                // the larger of the two wins.
                bool anyArtifact = ArtifactWearCache.Worn(p).Count > 0;
                int eff = 0;
                float str = 0f;
                if (anyArtifact)
                {
                    if (ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Learning) >= 7 && !p.Downed)
                        ArtifactLearning.RefreshMentor(p, pawns, Interval * 4);
                    eff = ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Efficiency, out str);
                }

                float tkKg = Mathf.Max(eff >= 9 ? S.tkBaseCapacityKg * str : 0f, AmuletSatchelKg(p));
                float capacity = tkKg + CapacitorKg(p);   // the subspace capacitor adds to whatever there is
                // the Lord of Subspace's deep pockets (control spheres, 9th stone)
                bool deep = SpherePaths.Level(p, SpherePaths.Subspace) >= 9;
                if (deep) capacity += SphereSubspace.SatchelKg;
                if (capacity > 0f)
                {
                    if (inv == null)
                    {
                        inv = (Hediff_TelekineticInventory)HediffMaker.MakeHediff(ArtifactEfficiencyDefOf.Avir_TelekineticInventory, p);
                        p.health.AddHediff(inv);
                    }
                    inv.capacityKg = capacity;
                    inv.telekinetic = tkKg > 0f;
                    inv.unloadAnywhere = deep || AmuletDevelopment.Level(p, AvirAmuletMakerDefOf.Avir_Subclass_Architect) >= 15;
                    if (p.Faction == Faction.OfPlayer) inv.Work();
                }
                else if (inv != null) p.health.RemoveHediff(inv);

                if (!anyArtifact || p.Downed || p.Faction != Faction.OfPlayer) continue;
                if (eff >= 3) FlickInPassing(p);
                if (eff >= 7) CleanInPassing(p, str);
            }
        }

        /// <summary>Worn subspace capacitors (a utility: one in practice).</summary>
        private static float CapacitorKg(Pawn p)
        {
            float kg = 0f;
            List<Apparel> worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                SubspaceCapacitorExtension ext = worn[i].def.GetModExtension<SubspaceCapacitorExtension>();
                if (ext == null) continue;
                // quality: normal 100 kg, +-20 a step (awful 60 ... legendary 180)
                int step = worn[i].TryGetQuality(out QualityCategory q) ? (int)q - (int)QualityCategory.Normal : 0;
                kg += ext.satchelKg + ext.kgPerQualityStep * step;
            }
            return kg;
        }

        /// <summary>Largest satchel among the pawn's worn Avir amulets (0 = none).</summary>
        private static float AmuletSatchelKg(Pawn p)
        {
            float best = 0f;
            List<Apparel> worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                Comp_Telekinesis tk = worn[i].TryGetComp<Comp_Telekinesis>();
                if (tk == null) continue;
                float kg = tk.Props.satchelCapacityKg + AmuletDevelopment.SatchelBonus(worn[i].TryGetComp<CompAvirAmulet>());
                if (kg > best) best = kg;
            }
            return best;
        }

        private void FlickInPassing(Pawn p)
        {
            float r = S.passingRadius;
            foreach (Designation d in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Flick).ToList())
            {
                Thing t = d.target.Thing;
                if (t == null || !t.Spawned || t.Position.DistanceTo(p.Position) > r) continue;
                if (!GenSight.LineOfSight(p.Position, t.Position, map, true)) continue;
                CompFlickable f = t.TryGetComp<CompFlickable>();
                if (f == null) continue;
                f.DoFlick();
                map.designationManager.RemoveDesignation(d);
                FleckMaker.ThrowMicroSparks(t.DrawPos, map);
                return;   // one per call
            }
        }

        private void CleanInPassing(Pawn p, float str)
        {
            cleanBudget.TryGetValue(p.thingIDNumber, out float budget);
            budget += S.cleanPerSecond * str * Interval / 60f;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(p.Position, S.cleanRadius, true))
            {
                if (budget < 1f) break;
                if (!c.InBounds(map) || !map.areaManager.Home[c]) continue;
                List<Thing> things = c.GetThingList(map);
                for (int i = things.Count - 1; i >= 0 && budget >= 1f; i--)
                {
                    if (!(things[i] is Filth f) || f.Destroyed) continue;
                    f.ThinFilth();
                    budget -= 1f;
                }
            }
            cleanBudget[p.thingIDNumber] = Mathf.Min(budget, 3f);
        }
    }
}
