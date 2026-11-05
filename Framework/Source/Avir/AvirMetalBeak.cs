using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>The metal beak goes only on an Avir that has lost its own: no beak, and the head still there.</summary>
    public class Recipe_InstallAvirMetalBeak : Recipe_InstallArtificialBodyPart
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            BodyPartRecord beak = AvirBeakless.Beak;
            if (beak == null || !AvirBody.IsAvirBody(pawn) || pawn.health?.hediffSet == null) yield break;
            if (!pawn.health.hediffSet.PartIsMissing(beak)) yield break;
            if (beak.parent != null && pawn.health.hediffSet.PartIsMissing(beak.parent)) yield break;
            yield return beak;
        }
    }

    /// <summary>
    /// AVIR ITEMS AT THE ARTIFICER'S TABLE. The Avir drugs (Phoenix essence, crimson rage, psychic tea,
    /// shadowsong wort) can be made at the artificer's table too, listed in its Items tab - as well as
    /// where they were made before. Done here rather than in XML because some of them are recipes the
    /// game generates from the item itself.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirItemsAtArtificer
    {
        private static readonly string[] Drugs = { "Avir_PhoenixEssence", "Avir_CrimsonRage", "Avir_PsychicTea", "Avir_ShadowsongWort", "Avir_HeartwingDraught" };

        static AvirItemsAtArtificer()
        {
            try
            {
                ThingDef table = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ArtificerSpot");
                if (table == null) return;
                foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
                {
                    ThingDef prod = r.ProducedThingDef;
                    if (prod == null || !Drugs.Contains(prod.defName)) continue;
                    if (r.recipeUsers == null) r.recipeUsers = new List<ThingDef>();
                    if (!r.recipeUsers.Contains(table)) r.recipeUsers.Add(table);
                    if (!r.HasModExtension<Artifactorics.ArtificerItemRecipe>())
                    {
                        if (r.modExtensions == null) r.modExtensions = new List<DefModExtension>();
                        r.modExtensions.Add(new Artifactorics.ArtificerItemRecipe());
                    }
                }
                // the table's recipe list is cached on first use: forget it so it is rebuilt with these
                AccessTools.Field(typeof(ThingDef), "allRecipesCached")?.SetValue(table, null);
            }
            catch (System.Exception e) { Log.Error("[Avir] could not add the Avir drugs to the artificer's table: " + e); }
        }
    }

    /// <summary>
    /// THE ARTIFICER'S LUCK. An order of the Items tab at the artificer's table now and then comes out twice
    /// for the price of one: 3% plus 0.35% per level of the skill the work uses (10% at 20). Not the battery
    /// charge, and not artifacts or amulets - those are one of a kind.
    /// </summary>
    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    public static class Patch_ArtificerDoubleWork
    {
        public static void Postfix(RecipeDef recipeDef, Pawn worker, IBillGiver billGiver, ref IEnumerable<Thing> __result)
        {
            if (recipeDef == null || worker == null || !(billGiver is Thing bench) || bench.def.defName != "Avir_ArtificerSpot") return;
            if (!Artifactorics.Dialog_ArtifactCreation.IsItemRecipe(recipeDef) || recipeDef == Artifactorics.ArtifactBatteryDefOf.Avir_ChargeCrystalBattery) return;
            int level = recipeDef.workSkill != null ? worker.skills?.GetSkill(recipeDef.workSkill)?.Level ?? 0 : 0;
            if (!Rand.Chance(0.03f + 0.0035f * level)) return;
            __result = Twice(__result.ToList(), worker);
        }

        private static IEnumerable<Thing> Twice(List<Thing> made, Pawn worker)
        {
            foreach (Thing t in made) yield return t;
            foreach (Thing t in made)
            {
                if (t is Pawn || t.def.stackLimit <= 1 && t.TryGetComp<Artifactorics.CompArtifact>() != null) continue;
                Thing copy = ThingMaker.MakeThing(t.def, t.Stuff);
                copy.stackCount = t.stackCount;
                CompQuality q = t.TryGetComp<CompQuality>();
                if (q != null) copy.TryGetComp<CompQuality>()?.SetQuality(q.Quality, ArtGenerationContext.Colony);
                yield return copy;
            }
            if (made.Count > 0)
                Messages.Message(worker.LabelShortCap + " made " + made[0].LabelNoCount + " twice for the price of one.", MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
