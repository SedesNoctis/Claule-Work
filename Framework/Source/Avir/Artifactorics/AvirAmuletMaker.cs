using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // AVIR TRADITIONAL AMULET - stage 3: the maker caste (craftsman, artist, architect).
    // Numbers at branch strength 1, provisional. "Working at a bench" = the pawn's current job
    // carries a bill, so Hauler's Dream's own crafting jobs count too.
    //
    //   craftsman  3 thrift            - 10% chance to get a quarter of the materials back
    //              9 fuelling          - telekinesis refuels buildings within reach from fuel within reach
    //             12 second hands      - while working at a bench, a plain bill at another bench within
    //                                    reach is worked at 30% speed (ingredients must lie by it)
    //             15 long work         - +25% work speed on recipes of 10 000 work and more
    //             18 workshop          - second hands reach two more benches
    //              slot: rush order    - 25% of the remaining work of an unfinished thing, at once (1 day)
    //   artist     3 beauty            - art made by the artist: +25% beauty
    //              9 inspiration       - each finished work: 2% chance of creative inspiration
    //             12 fine assembly     - 15% of the ingredients back on recipes of 3000 work and more
    //             15 artist's touch    - apparel, weapons and tools it made mend themselves (artist alive, same map)
    //             18 heart of the clutch - touched apparel and weapons +10% armour / damage, artist on the map
    //              slot: restoration   - 40 hp x strength x (1 + stones/18) x psychic sensitivity (12 h)
    //   architect  3 precise reckoning - construction does not fail (milestoneStats)
    //              9 builder's will    - blueprints within reach become frames; materials fly to frames
    //             12 larger pocket     - satchel +15 kg (AmuletDevelopment.SatchelBonus)
    //             15 subspace unloading - the satchel unloads into any storage on the map
    //             18 architect's spark - buildings within reach are repaired telekinetically
    //              slot: wide reach    - +25% telekinetic reach (AvirTelekinesisUtility)
    // =====================================================================================

    [DefOf]
    public static class AvirAmuletMakerDefOf
    {
        public static AmuletCasteDef Avir_Caste_Maker;
        public static AmuletSubclassDef Avir_Subclass_Craftsman;
        public static AmuletSubclassDef Avir_Subclass_Artist;
        public static AmuletSubclassDef Avir_Subclass_Architect;
        static AvirAmuletMakerDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(AvirAmuletMakerDefOf)); }
    }

    public static class AmuletMaker
    {
        public const float ThriftChance = 0.10f, ThriftShare = 0.25f;
        public const float FineShare = 0.15f, FineMinWork = 3000f;
        public const float InspirationChance = 0.02f;
        public const float SecondHandsSpeed = 0.30f;
        public const float LongWorkMin = 10000f, LongWorkBonus = 0.25f;
        public const float BeautyBonus = 0.25f, HeartBonus = 0.10f;
        public const float RestoreBase = 40f;
        public const float SparkPerCall = 5f;
        public const float DefaultReach = 10f;

        public static AmuletSubclassDef Craftsman => AvirAmuletMakerDefOf.Avir_Subclass_Craftsman;
        public static AmuletSubclassDef Artist => AvirAmuletMakerDefOf.Avir_Subclass_Artist;
        public static AmuletSubclassDef Architect => AvirAmuletMakerDefOf.Avir_Subclass_Architect;

        public static float Reach(Pawn p) => AvirTelekinesisUtility.TryGetRadius(p, out float r) ? r : DefaultReach;

        /// <summary>Gives back a share of the ingredients next to the worker.</summary>
        public static void Refund(Pawn worker, List<Thing> ingredients, float share)
        {
            if (worker?.Map == null || ingredients == null) return;
            foreach (Thing t in ingredients)
            {
                if (t == null || t.def.stackLimit <= 1 || t.def.IsCorpse || t.TryGetComp<CompQuality>() != null) continue;
                int n = GenMath.RoundRandom(t.stackCount * share);
                if (n <= 0) continue;
                Thing back = ThingMaker.MakeThing(t.def, t.Stuff);
                back.stackCount = Mathf.Min(n, t.def.stackLimit);
                GenPlace.TryPlaceThing(back, worker.Position, worker.Map, ThingPlaceMode.Near);
            }
        }

        public static float ArtistStrength(Pawn artist, int minLevel)
        {
            if (artist == null || artist.Dead) return 0f;
            return AmuletDevelopment.Level(artist, Artist, out float k) >= minLevel ? k : 0f;
        }
    }

    /// <summary>Who made what: the artist's touched works, saved with the world.</summary>
    public class WorldComponent_AvirArtistTouch : WorldComponent
    {
        private Dictionary<Thing, Pawn> touched = new Dictionary<Thing, Pawn>();
        private List<Thing> tmpThings;
        private List<Pawn> tmpPawns;

        public WorldComponent_AvirArtistTouch(World world) : base(world) { }

        public static WorldComponent_AvirArtistTouch Get => Find.World?.GetComponent<WorldComponent_AvirArtistTouch>();

        public void Mark(Thing t, Pawn artist) { if (t != null && artist != null) touched[t] = artist; }

        public Pawn ArtistOf(Thing t) => t != null && touched.TryGetValue(t, out Pawn a) ? a : null;

        public IEnumerable<KeyValuePair<Thing, Pawn>> All => touched;

        public void Clean()
        {
            foreach (Thing t in touched.Keys.Where(k => k == null || k.Destroyed).ToList()) touched.Remove(t);
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) Clean();
            Scribe_Collections.Look(ref touched, "touched", LookMode.Reference, LookMode.Reference, ref tmpThings, ref tmpPawns);
            if (touched == null) touched = new Dictionary<Thing, Pawn>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                foreach (Thing t in touched.Keys.Where(k => k == null).ToList()) touched.Remove(t);
        }
    }

    /// <summary>Thrift, fine assembly, inspiration and the artist's mark - when a bill makes its products.</summary>
    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    public static class Patch_AmuletMakerProducts
    {
        public static void Postfix(RecipeDef recipeDef, Pawn worker, List<Thing> ingredients, ref IEnumerable<Thing> __result)
        {
            if (worker == null || recipeDef == null) return;
            CompAvirAmulet a = AmuletDevelopment.WornBy(worker);
            if (a == null || !AmuletDevelopment.HasCasteBranch(a, AvirAmuletMakerDefOf.Avir_Caste_Maker)) return;
            __result = Wrap(__result, recipeDef, worker, ingredients);
        }

        private static IEnumerable<Thing> Wrap(IEnumerable<Thing> products, RecipeDef recipe, Pawn worker, List<Thing> ingredients)
        {
            int craft = AmuletDevelopment.Level(worker, AmuletMaker.Craftsman, out float ck);
            int art = AmuletDevelopment.Level(worker, AmuletMaker.Artist, out float ak);
            bool any = false;
            foreach (Thing t in products)
            {
                any = true;
                if (art >= 3 && t != null) WorldComponent_AvirArtistTouch.Get?.Mark(t, worker);
                yield return t;
            }
            if (!any) yield break;
            float work = recipe.workAmount;
            if (art >= 12 && work >= AmuletMaker.FineMinWork) AmuletMaker.Refund(worker, ingredients, AmuletMaker.FineShare);
            else if (craft >= 3 && Rand.Chance(AmuletMaker.ThriftChance * ck)) AmuletMaker.Refund(worker, ingredients, AmuletMaker.ThriftShare);
            if (art >= 9 && Rand.Chance(AmuletMaker.InspirationChance * ak) && worker.mindState?.inspirationHandler != null && !worker.Inspired)
                worker.mindState.inspirationHandler.TryStartInspiration(InspirationDefOf.Inspired_Creativity, "Inspired by the work of the clutch.", true);
        }
    }

    /// <summary>Long work (craftsman 15) on every bill speed stat; beauty and heart of the clutch on items.</summary>
    public class StatPart_AvirAmuletMaker : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            float f = Factor(req);
            if (f != 1f) val *= f;
        }

        private float Factor(StatRequest req)
        {
            if (req.Thing is Pawn p)
            {
                RecipeDef r = p.CurJob?.bill?.recipe;
                if (r == null || r.workSpeedStat != parentStat || r.workAmount < AmuletMaker.LongWorkMin) return 1f;
                return AmuletDevelopment.Level(p, AmuletMaker.Craftsman, out float k) >= 15 ? 1f + AmuletMaker.LongWorkBonus * k : 1f;
            }
            Thing t = req.Thing;
            if (t == null || Current.ProgramState != ProgramState.Playing) return 1f;
            Pawn artist = WorldComponent_AvirArtistTouch.Get?.ArtistOf(t);
            if (artist == null) return 1f;
            if (parentStat == StatDefOf.Beauty)
            {
                float k = AmuletMaker.ArtistStrength(artist, 3);
                return k > 0f && t.TryGetComp<CompArt>() != null ? 1f + AmuletMaker.BeautyBonus * k : 1f;
            }
            float h = AmuletMaker.ArtistStrength(artist, 18);
            return h > 0f && artist.Spawned && artist.MapHeld == t.MapHeld ? 1f + AmuletMaker.HeartBonus * h : 1f;
        }

        public override string ExplanationPart(StatRequest req)
        {
            float f = Factor(req);
            return f == 1f ? null : "Avir amulet (maker): x" + f.ToStringPercent();
        }
    }

    [StaticConstructorOnStartup]
    public static class AmuletMakerStatInjector
    {
        static AmuletMakerStatInjector()
        {
            var stats = new HashSet<StatDef> { StatDefOf.Beauty, StatDefOf.ArmorRating_Sharp, StatDefOf.ArmorRating_Blunt, StatDefOf.ArmorRating_Heat, StatDefOf.MeleeWeapon_DamageMultiplier };
            foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
                if (r.workSpeedStat != null && r.workAmount >= AmuletMaker.LongWorkMin) stats.Add(r.workSpeedStat);
            foreach (StatDef s in stats)
            {
                if (s == null) continue;
                if (s.parts == null) s.parts = new List<StatPart>();
                if (!s.parts.Any(x => x is StatPart_AvirAmuletMaker)) s.parts.Add(new StatPart_AvirAmuletMaker { parentStat = s });
            }
        }
    }

    /// <summary>Fuelling, second hands, the artist's touch, builder's will, architect's spark.</summary>
    public class MapComponent_AvirAmuletMaker : MapComponent
    {
        private class Work { public Thing bench; public Bill_Production bill; public float progress; public float total; }
        private readonly Dictionary<int, List<Work>> works = new Dictionary<int, List<Work>>();

        public MapComponent_AvirAmuletMaker(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0) return;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                CompAvirAmulet a = AmuletDevelopment.WornBy(p);
                if (a == null || !AmuletDevelopment.HasCasteBranch(a, AvirAmuletMakerDefOf.Avir_Caste_Maker) || p.Downed) { works.Remove(p.thingIDNumber); continue; }
                int craft = AmuletDevelopment.Level(p, AmuletMaker.Craftsman, out float ck);
                int arch = AmuletDevelopment.Level(p, AmuletMaker.Architect, out float rk);
                float reach = AmuletMaker.Reach(p);

                if (craft >= 12) SecondHands(p, craft >= 18 ? 3 : 1, reach);
                else works.Remove(p.thingIDNumber);
                if (craft >= 9 && now % 250 < 60) Fuel(p, reach);
                // the architect's slot (wide reach): materials within reach fly onto blueprints and frames even before
                // builder's will (9); with it, twice as far
                bool archSlot = AmuletDevelopment.SlotIs(p, AmuletMaker.Architect);
                if ((arch >= 9 || archSlot) && now % 120 < 60) BuildersWill(p, archSlot && arch >= 9 ? reach * 2f : reach);
                if (arch >= 18 && now % 250 < 60) Spark(p, reach, rk);
            }
            if (now % 2500 == 0) Touch();
        }

        // ---- craftsman 9 ----
        private void Fuel(Pawn p, float reach)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if ((b.Position - p.Position).LengthHorizontalSquared > reach * reach) continue;
                CompRefuelable r = b.TryGetComp<CompRefuelable>();
                if (r == null || !r.ShouldAutoRefuelNow) continue;
                int need = r.GetFuelCountToFullyRefuel();
                if (need <= 0) continue;
                foreach (Thing t in GenRadial.RadialDistinctThingsAround(p.Position, map, reach, true))
                {
                    if (t.def.category != ThingCategory.Item || !r.Props.fuelFilter.Allows(t) || t.IsForbidden(p)) continue;
                    if (map.reservationManager.IsReservedByAnyoneOf(t, p.Faction)) continue;
                    Thing take = t.SplitOff(Mathf.Min(need, t.stackCount));
                    r.Refuel(new List<Thing> { take });
                    FleckMaker.ThrowMicroSparks(b.DrawPos, map);
                    return;   // one building per call
                }
            }
        }

        // ---- craftsman 12 / 18 ----
        private static bool Plain(RecipeDef r) =>
            r != null && !r.products.NullOrEmpty() && !r.IsSurgery && r.GetModExtension<ArtifactRecipeExtension>() == null
            && (r.workerClass == typeof(RecipeWorker) || r.workerClass == typeof(RecipeWorkerCounter));

        private void SecondHands(Pawn p, int max, float reach)
        {
            Bill own = p.CurJob?.bill;
            if (own == null) { works.Remove(p.thingIDNumber); return; }
            Thing ownBench = own.billStack?.billGiver as Thing;
            if (!works.TryGetValue(p.thingIDNumber, out List<Work> list)) works[p.thingIDNumber] = list = new List<Work>();

            // advance and finish
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Work w = list[i];
                if (w.bench == null || !w.bench.Spawned || w.bill.deleted || w.bill.suspended || !w.bill.ShouldDoNow()) { list.RemoveAt(i); continue; }
                float speed = w.bill.recipe.workSpeedStat != null ? p.GetStatValue(w.bill.recipe.workSpeedStat) : 1f;
                w.progress += 60f * AmuletMaker.SecondHandsSpeed * speed;
                if (w.progress >= w.total) { Finish(p, w); list.RemoveAt(i); }
            }

            // start new ones
            if (list.Count >= max) return;
            foreach (Thing bench in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver))
            {
                if (list.Count >= max) break;
                if (bench == ownBench || !(bench is IBillGiver g) || g.BillStack == null) continue;
                if ((bench.Position - p.Position).LengthHorizontalSquared > reach * reach) continue;
                if (list.Any(w => w.bench == bench) || map.reservationManager.IsReservedByAnyoneOf(bench, p.Faction)) continue;
                foreach (Bill bill in g.BillStack.Bills)
                {
                    if (!(bill is Bill_Production bp) || bp.suspended || !bp.ShouldDoNow() || !Plain(bp.recipe)) continue;
                    if (!bp.recipe.AvailableNow || !bp.PawnAllowedToStartAnew(p) || !bp.recipe.PawnSatisfiesSkillRequirements(p)) continue;
                    List<(Thing t, int n)> plan = Plan(p, reach, bp);
                    if (plan == null) continue;
                    list.Add(new Work { bench = bench, bill = bp, total = Mathf.Max(60f, bp.recipe.WorkAmountTotal(plan.Count > 0 ? plan[0].t : null)) });
                    FleckMaker.ThrowMicroSparks(bench.DrawPos, map);
                    break;
                }
            }
        }

        /// <summary>Ingredients within the craftsman's telekinetic reach (the telekinesis brings them to the bench),
        /// one def per ingredient line; null if short. Nothing another pawn reserved, nothing a ritual holds.</summary>
        private List<(Thing t, int n)> Plan(Pawn p, float reach, Bill_Production bill)
        {
            var plan = new List<(Thing, int)>();
            var used = new HashSet<Thing>();
            List<Thing> near = GenRadial.RadialDistinctThingsAround(p.Position, map, Mathf.Min(reach, GenRadial.MaxRadialPatternRadius - 1f), true)
                .Where(t => t.def.category == ThingCategory.Item && !t.IsForbidden(Faction.OfPlayer)
                            && !map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer) && !Ritualistics.RitualHold.Held(t)).ToList();
            foreach (IngredientCount ic in bill.recipe.ingredients)
            {
                bool done = false;
                foreach (IGrouping<ThingDef, Thing> grp in near.Where(t => !used.Contains(t) && ic.filter.Allows(t) && bill.IsFixedOrAllowedIngredient(t)).GroupBy(t => t.def))
                {
                    int need = ic.CountRequiredOfFor(grp.Key, bill.recipe, bill);
                    if (grp.Sum(t => t.stackCount) < need) continue;
                    foreach (Thing t in grp)
                    {
                        if (need <= 0) break;
                        int take = Mathf.Min(need, t.stackCount);
                        plan.Add((t, take)); used.Add(t); need -= take;
                    }
                    done = true;
                    break;
                }
                if (!done) return null;
            }
            return plan;
        }

        private void Finish(Pawn p, Work w)
        {
            List<(Thing t, int n)> plan = Plan(p, AmuletMaker.Reach(p), w.bill);
            if (plan == null) return;   // the ingredients went elsewhere: nothing made
            var ingredients = new List<Thing>();
            foreach (var (t, n) in plan) ingredients.Add(n >= t.stackCount ? t : t.SplitOff(n));
            foreach (Thing t in ingredients) if (t.Spawned) t.DeSpawn();
            List<Thing> products = GenRecipe.MakeRecipeProducts(w.bill.recipe, p, ingredients, ingredients.FirstOrDefault(), (IBillGiver)w.bench).ToList();
            foreach (Thing prod in products) GenPlace.TryPlaceThing(prod, w.bench.InteractionCell.IsValid ? w.bench.InteractionCell : w.bench.Position, map, ThingPlaceMode.Near);
            w.bill.Notify_IterationCompleted(p, ingredients);
            MoteMaker.ThrowText(w.bench.DrawPos, map, "second hands: " + (products.FirstOrDefault()?.LabelShort ?? w.bill.recipe.label), 4f);
            foreach (Thing t in ingredients) if (!t.Destroyed) t.Destroy();
            FleckMaker.ThrowMicroSparks(w.bench.DrawPos, map);
        }

        // ---- architect 9 ----
        private void BuildersWill(Pawn p, float reach)
        {
            float r2 = reach * reach;
            foreach (Thing bp in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).ToList())
            {
                if (!(bp is Blueprint b) || b.Faction != p.Faction || (b.Position - p.Position).LengthHorizontalSquared > r2) continue;
                if (b is Blueprint_Install || GenConstruct.FirstBlockingThing(b, p) != null) continue;
                b.TryReplaceWithSolidThing(p, out _, out _);
                return;
            }
            foreach (Thing f in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                if (!(f is Frame frame) || frame.Faction != p.Faction || (frame.Position - p.Position).LengthHorizontalSquared > r2) continue;
                foreach (ThingDefCountClass need in frame.TotalMaterialCost())
                {
                    int lack = frame.ThingCountNeeded(need.thingDef);
                    if (lack <= 0) continue;
                    foreach (Thing t in GenRadial.RadialDistinctThingsAround(p.Position, map, reach, true))
                    {
                        if (t.def != need.thingDef || t.IsForbidden(p) || map.reservationManager.IsReservedByAnyoneOf(t, p.Faction)) continue;
                        Thing take = t.SplitOff(Mathf.Min(lack, t.stackCount));
                        if (take.Spawned) take.DeSpawn();
                        if (!frame.resourceContainer.TryAdd(take, true)) { GenPlace.TryPlaceThing(take, p.Position, map, ThingPlaceMode.Near); return; }
                        FleckMaker.ThrowMicroSparks(frame.DrawPos, map);
                        return;   // one delivery per call
                    }
                }
            }
        }

        // ---- architect 18 ----
        private void Spark(Pawn p, float reach, float k)
        {
            foreach (Building b in map.listerBuildingsRepairable.RepairableBuildings(p.Faction).ToList())
            {
                if ((b.Position - p.Position).LengthHorizontalSquared > reach * reach || b.HitPoints >= b.MaxHitPoints || b.IsBurning()) continue;
                b.HitPoints = Mathf.Min(b.MaxHitPoints, b.HitPoints + Mathf.Max(1, Mathf.RoundToInt(AmuletMaker.SparkPerCall * k)));
                map.listerBuildingsRepairable.Notify_BuildingRepaired(b);
                FleckMaker.ThrowMicroSparks(b.DrawPos, map);
                return;
            }
        }

        // ---- artist 15 ----
        private void Touch()
        {
            WorldComponent_AvirArtistTouch w = WorldComponent_AvirArtistTouch.Get;
            if (w == null) return;
            foreach (var kv in w.All.ToList())
            {
                Thing t = kv.Key;
                if (t == null || t.Destroyed || !t.def.useHitPoints || t.HitPoints >= t.MaxHitPoints) continue;
                if (t.MapHeld != map || !(t.def.IsApparel || t.def.IsWeapon || t.def.IsTool())) continue;
                float k = AmuletMaker.ArtistStrength(kv.Value, 15);
                if (k <= 0f || kv.Value.MapHeld != map) continue;
                t.HitPoints = Mathf.Min(t.MaxHitPoints, t.HitPoints + Mathf.Max(1, Mathf.RoundToInt(2f * k)));
            }
        }
    }

    internal static class ThingDefToolExt
    {
        public static bool IsTool(this ThingDef d) => d.thingCategories != null && d.thingCategories.Any(c => c.defName == "Tools");
    }

    // ---------------------------------------------------------------- slot abilities

    public class CompProperties_AmuletRushOrder : CompProperties_AbilityEffect
    {
        public float share = 0.25f;
        public CompProperties_AmuletRushOrder() { compClass = typeof(CompAbilityEffect_AmuletRushOrder); }
    }

    public class CompAbilityEffect_AmuletRushOrder : CompAbilityEffect
    {
        private new CompProperties_AmuletRushOrder Props => (CompProperties_AmuletRushOrder)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!(target.Thing is UnfinishedThing))
            {
                if (throwMessages) Messages.Message("A rush order needs an unfinished thing.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (target.Thing is UnfinishedThing u)
            {
                u.workLeft *= 1f - Props.share;
                FleckMaker.ThrowMicroSparks(u.DrawPos, u.Map);
            }
        }
    }

    public class CompProperties_AmuletRestoration : CompProperties_AbilityEffect
    {
        public CompProperties_AmuletRestoration() { compClass = typeof(CompAbilityEffect_AmuletRestoration); }
    }

    public class CompAbilityEffect_AmuletRestoration : CompAbilityEffect
    {
        /// <summary>What the restoration mends: a single item on the ground; or everything a pawn (the artist itself or an ally)
        /// wears and holds - each piece gets the full mending.</summary>
        private static List<Thing> Pick(LocalTargetInfo target)
        {
            var list = new List<Thing>();
            if (target.Thing is Pawn pw)
            {
                if (pw.apparel != null) list.AddRange(pw.apparel.WornApparel.Where(a => a.def.useHitPoints && a.HitPoints < a.MaxHitPoints));
                if (pw.equipment != null) list.AddRange(pw.equipment.AllEquipmentListForReading.Where(e => e.def.useHitPoints && e.HitPoints < e.MaxHitPoints));
                return list;
            }
            Thing t = target.Thing;
            if (t != null && t.def.useHitPoints && t.def.category == ThingCategory.Item && t.HitPoints < t.MaxHitPoints) list.Add(t);
            return list;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (target.Thing is Pawn pw && pw.HostileTo(parent.pawn)) return false;
            if (Pick(target).Count == 0)
            {
                if (throwMessages) Messages.Message("Nothing there needs restoring.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            List<Thing> things = Pick(target);
            Pawn p = parent.pawn;
            CompAvirAmulet a = AmuletDevelopment.WornBy(p);
            if (things.Count == 0 || a == null) return;
            float k = AmuletDevelopment.Strength(a, AmuletMaker.Artist, p);
            int stones = AmuletDevelopment.Opened(a, AmuletMaker.Artist);
            float sens = ArtifactFocus.Sensitivity(p);
            int hp = Mathf.Max(1, Mathf.RoundToInt(AmuletMaker.RestoreBase * k * (1f + stones / 18f) * sens));
            foreach (Thing t in things) t.HitPoints = Mathf.Min(t.MaxHitPoints, t.HitPoints + hp);
            Thing first = things[0];
            FleckMaker.ThrowMicroSparks(target.Thing != null && target.Thing.SpawnedOrAnyParentSpawned ? target.Thing.PositionHeld.ToVector3Shifted() : p.DrawPos, p.Map);
            Messages.Message((things.Count == 1 ? first.LabelShortCap : things.Count + " things on " + target.Thing.LabelShort) + " restored by " + hp + " hit points"
                             + (things.Count == 1 ? "." : " each."), p, MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
