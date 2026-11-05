using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // THE COMPULSIVE CREATOR.
    //   3  - obsession: for six hours every workbench in one room works its bills at once, at the architect's own speed,
    //        wherever the architect is; the materials come to the benches by themselves.
    //   6  - thrift: a fifth of the stackable materials of the architect's work comes back.
    //   9  - perfection: a quarter of the architect's work comes out a quality step better.
    //   12 - echo: one work in twenty comes out twice. Never an artifact.
    // The passives hold for the architect's own work at a bench as for the obsession's.
    // =====================================================================================
    public static class SphereCreator
    {
        public const float Cost = 0.2f;
        public const int Duration = GenDate.TicksPerHour * 6;
        public const int CooldownTicks = GenDate.TicksPerDay * 2;

        private class BenchWork
        {
            public Bill bill;
            public float done;
            public float need;
        }

        private static readonly Dictionary<Thing, BenchWork> works = new Dictionary<Thing, BenchWork>();

        private static MethodInfo findIngredients;

        private static bool FindIngredients(Bill bill, Pawn p, Thing bench, List<ThingCount> chosen)
        {
            if (findIngredients == null) findIngredients = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients");
            if (findIngredients == null) return false;
            chosen.Clear();
            try { return (bool)findIngredients.Invoke(null, new object[] { bill, p, bench, chosen, new List<IngredientCount>() }); }
            catch { return false; }
        }

        public static Command Command(Pawn p, CompArchitectSpheres c)
        {
            bool on = c.obsessionUntil > Find.TickManager.TicksGame;
            var cmd = new Command_Target
            {
                defaultLabel = on ? "Obsession (" + (c.obsessionUntil - Find.TickManager.TicksGame).ToStringTicksToPeriod() + ")" : "Obsession",
                defaultDesc = SpherePaths.Powers(SpherePaths.Creator)[0][1] + " Choose a workbench: its whole room is taken.\n\nLasts " + Duration.ToStringTicksToPeriod()
                              + "; costs " + Cost.ToStringPercent() + " of the spheres' charge.",
                icon = SphereIcons.Named("Obsession") ?? ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Ink/Rune_Reforge", false) ?? SphereActives.Icon(c),
                targetingParams = new TargetingParameters
                {
                    canTargetBuildings = true, canTargetPawns = false, canTargetLocations = false, canTargetItems = false,
                    validator = ti => ti.Thing is IBillGiver && ti.Thing is Building && ti.Thing.Faction == Faction.OfPlayer
                },
                action = ti =>
                {
                    if (!SpherePaths.Pay(p, c, Cost)) return;
                    c.Cooldown("obsession", CooldownTicks);
                    c.obsessionCell = ti.Thing.Position;
                    c.obsessionUntil = Find.TickManager.TicksGame + Duration;
                    Messages.Message(p.LabelShortCap + " is seized by the work: every bench in the room answers.", ti.Thing, MessageTypeDefOf.PositiveEvent, false);
                }
            };
            if (on) cmd.Disable("The obsession holds.");
            else SphereActives.Gate(cmd, p, c, "obsession", Cost);
            return cmd;
        }

        public static void Tick(Pawn p, CompArchitectSpheres c, int delta)
        {
            int now = Find.TickManager.TicksGame;
            if (c.obsessionUntil <= now || !c.obsessionCell.IsValid) return;
            Map map = p.Map;
            Room room = c.obsessionCell.GetRoom(map);
            if (room == null || room.PsychologicallyOutdoors) { c.obsessionUntil = -1; return; }
            foreach (Building b in room.ContainedAndAdjacentThings.OfType<Building>().Where(b => b is IBillGiver && b.Faction == p.Faction).ToList())
                Work(p, b, delta);
        }

        private static void Work(Pawn p, Building bench, int delta)
        {
            IBillGiver giver = (IBillGiver)bench;
            if (!giver.CurrentlyUsableForBills()) return;
            CompPowerTrader power = bench.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return;
            var chosen = new List<ThingCount>();
            if (!works.TryGetValue(bench, out BenchWork w) || w.bill == null || w.bill.DeletedOrDereferenced || !giver.BillStack.Bills.Contains(w.bill))
            {
                w = null;
                foreach (Bill bill in giver.BillStack.Bills)
                {
                    if (bill.suspended || !bill.ShouldDoNow() || !bill.recipe.AvailableNow || !bill.PawnAllowedToStartAnew(p)) continue;
                    if (bill.recipe.workSkill != null && bill.recipe.skillRequirements != null && !bill.recipe.PawnSatisfiesSkillRequirements(p)) continue;
                    if (!FindIngredients(bill, p, bench, chosen)) continue;
                    w = new BenchWork { bill = bill, need = bill.recipe.WorkAmountTotal(chosen.FirstOrDefault(x => x.Thing.def.IsStuff).Thing) };
                    break;
                }
                if (w == null) { works.Remove(bench); return; }
                works[bench] = w;
            }
            RecipeDef r = w.bill.recipe;
            float speed = p.GetStatValue(r.workSpeedStat ?? StatDefOf.WorkSpeedGlobal);
            if (r.workTableSpeedStat != null) speed *= bench.GetStatValue(r.workTableSpeedStat);
            w.done += speed * delta;
            if (r.workSkill != null) p.skills?.Learn(r.workSkill, 0.1f * delta * r.workSkillLearnFactor);
            if (Rand.Chance(0.5f)) FleckMaker.ThrowMicroSparks(bench.DrawPos, bench.Map);
            if (w.done < w.need) return;
            works.Remove(bench);
            if (!FindIngredients(w.bill, p, bench, chosen)) return;
            Finish(p, bench, w.bill, chosen);
        }

        private static void Finish(Pawn p, Building bench, Bill bill, List<ThingCount> chosen)
        {
            Map map = bench.Map;
            RecipeDef r = bill.recipe;
            var ingredients = new List<Thing>();
            foreach (ThingCount tc in chosen)
            {
                if (tc.Thing == null || tc.Thing.Destroyed) continue;
                Thing part = tc.Thing.SplitOff(Mathf.Min(tc.Count, tc.Thing.stackCount));
                ingredients.Add(part);
            }
            if (ingredients.Count == 0) return;
            Thing dominant = r.productHasIngredientStuff ? ingredients[0]
                : r.products.Any(x => x.thingDef.MadeFromStuff) ? ingredients.Where(x => x.def.IsStuff).RandomElementByWeightWithFallback(x => x.stackCount)
                : ingredients.RandomElementByWeight(x => x.stackCount);
            List<Thing> made = GenRecipe.MakeRecipeProducts(r, p, ingredients, dominant, (IBillGiver)bench, null, null, null).ToList();
            foreach (Thing ing in ingredients)
            {
                if (ing.Destroyed) continue;
                if (ing.Spawned) r.Worker.ConsumeIngredient(ing, r, map);
                else ing.Destroy();
            }
            bill.Notify_IterationCompleted(p, ingredients);
            foreach (Thing t in made) GenPlace.TryPlaceThing(t, bench.InteractionCell.IsValid ? bench.InteractionCell : bench.Position, map, ThingPlaceMode.Near);
            FleckMaker.ThrowLightningGlow(bench.DrawPos, map, 0.8f);
        }

        /// <summary>No artifact is ever echoed.</summary>
        public static bool IsArtifact(Thing t) =>
            t.def.comps != null && t.def.comps.Any(cp => cp.compClass != null && cp.compClass.Namespace == typeof(SphereCreator).Namespace);
    }

    /// <summary>Thrift, perfection and echo: the architect's own work at any bench, and the obsession's.</summary>
    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    public static class Patch_SphereCreatorProducts
    {
        public static void Postfix(ref IEnumerable<Thing> __result, RecipeDef recipeDef, Pawn worker, List<Thing> ingredients, IBillGiver billGiver)
        {
            int n = SpherePaths.Level(worker, SpherePaths.Creator);
            if (n < 6) return;
            __result = Wrap(__result, n, worker, ingredients, billGiver);
        }

        private static IEnumerable<Thing> Wrap(IEnumerable<Thing> made, int n, Pawn worker, List<Thing> ingredients, IBillGiver billGiver)
        {
            var extra = new List<Thing>();
            foreach (Thing t in made)
            {
                if (n >= 9 && Rand.Chance(0.25f) && t.TryGetComp<CompQuality>() is CompQuality q && q.Quality < QualityCategory.Legendary)
                    q.SetQuality(q.Quality + 1, ArtGenerationContext.Colony);
                if (n >= 12 && Rand.Chance(0.05f) && !SphereCreator.IsArtifact(t))
                {
                    Thing echo = ThingMaker.MakeThing(t.def, t.Stuff);
                    echo.stackCount = t.stackCount;
                    if (echo.TryGetComp<CompQuality>() is CompQuality eq && t.TryGetComp<CompQuality>() is CompQuality tq) eq.SetQuality(tq.Quality, ArtGenerationContext.Colony);
                    extra.Add(echo);
                    if (worker.Faction == Faction.OfPlayer) Messages.Message("The work echoes: " + t.LabelShort + " came out twice.", worker, MessageTypeDefOf.PositiveEvent, false);
                }
                yield return t;
            }
            foreach (Thing e in extra) yield return e;
            // thrift: a fifth of the stackable materials comes back
            if (ingredients == null) yield break;
            foreach (IGrouping<ThingDef, Thing> g in ingredients.Where(i => i.def.stackLimit > 1 && !i.def.IsCorpse).GroupBy(i => i.def))
            {
                int back = GenMath.RoundRandom(g.Sum(i => i.stackCount) * 0.2f);
                if (back <= 0) continue;
                Thing t = ThingMaker.MakeThing(g.Key, g.First().Stuff);
                t.stackCount = back;
                yield return t;
            }
        }
    }
}
