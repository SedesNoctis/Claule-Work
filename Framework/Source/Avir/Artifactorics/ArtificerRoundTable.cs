using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // AVIR ARTIFICER'S ROUND TABLE (Avir artifactorics): a 3x3 artificer's table that is itself an
    // artifact building with three sockets. Every working stone: faster work and a chance to lift
    // the quality of what is made here one step; its telekinesis gathers the bills' ingredients from
    // around it onto the table. It has every artificer recipe and does everything the plain table
    // does (it is a Building_ArtificerSpot), extraction of artifacts included.
    // =====================================================================================
    public class CompProperties_ArtificerTable : CompProperties_ArtifactAura
    {
        public float speedPerStone = 0.15f;
        /// <summary>Per working stone: chance (x stone power) to raise a product's quality one step.</summary>
        public float qualityChancePerStone = 0.15f;
        public CompProperties_ArtificerTable() { compClass = typeof(CompArtificerTable); }
    }

    public class CompArtificerTable : CompArtifactAura
    {
        private CompProperties_ArtificerTable P => (CompProperties_ArtificerTable)props;

        public float SpeedBonus => Awake(out CompArtifactHost h) ? P.speedPerStone * h.Working * h.Power : 0f;
        public float QualityChance => Awake(out CompArtifactHost h) ? P.qualityChancePerStone * h.Power : 0f;
        public int Stones => Awake(out CompArtifactHost h) ? h.Working : 0;

        /// <summary>Telekinesis: one ingredient stack per working stone a pass, from within range onto the table.</summary>
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!Awake(out CompArtifactHost h) || !(parent is Building_WorkTable table)) return;
            Map map = parent.Map;
            int moves = h.Working;
            float range = Range;
            foreach (Bill bill in table.BillStack.Bills)
            {
                if (moves <= 0) return;
                if (!(bill is Bill_Production) || bill.suspended || !bill.ShouldDoNow() || bill.recipe?.ingredients == null) continue;
                foreach (IngredientCount ing in bill.recipe.ingredients)
                {
                    if (moves <= 0) return;
                    Thing near = null; int have = 0; ThingDef def = null;
                    foreach (Thing t in GenRadial.RadialDistinctThingsAround(parent.Position, map, 3.9f, true))
                        if (t.def.category == ThingCategory.Item && ing.filter.Allows(t) && bill.IsFixedOrAllowedIngredient(t)) { have += t.stackCount; def = t.def; near = t; }
                    Thing far = GenRadial.RadialDistinctThingsAround(parent.Position, map, range, true)
                        .Where(t => t.def.category == ThingCategory.Item && t.def.EverHaulable && ing.filter.Allows(t) && bill.IsFixedOrAllowedIngredient(t)
                                    && (def == null || t.def == def) && (t.Position - parent.Position).LengthHorizontal > 3.9f
                                    && !t.IsForbidden(Faction.OfPlayer) && !map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer)
                                    && !map.physicalInteractionReservationManager.IsReserved(t))
                        .OrderBy(t => (t.Position - parent.Position).LengthHorizontalSquared).FirstOrDefault();
                    if (far == null) continue;
                    int need = Mathf.CeilToInt(ing.CountRequiredOfFor(far.def, bill.recipe));
                    if (have >= need) continue;
                    if (!h.Spend(A.chargePerUse)) return;
                    Thing piece = far.stackCount > need - have ? far.SplitOff(need - have) : far;
                    Vector3 from = far.DrawPos;
                    if (piece.Spawned) piece.DeSpawn();
                    GenPlace.TryPlaceThing(piece, parent.InteractionCell, map, ThingPlaceMode.Near);
                    FleckMaker.ConnectingLine(from, parent.InteractionCell.ToVector3Shifted(), FleckDefOf.PsycastAreaEffect, map, 0.3f);
                    moves--;
                }
            }
        }

        public override string EffectLine() => !Awake(out CompArtifactHost h)
            ? Asleep("An artificer's table that is an artifact itself: faster work, better quality, ingredients gathered by telekinesis.")
            : "Work here " + SpeedBonus.ToStringPercent() + " faster; each of " + h.Working + " stone(s) has a " + QualityChance.ToStringPercent()
              + " chance to lift a product's quality a step; its telekinesis gathers the bills' ingredients from within " + Range.ToString("0") + " cells.";

        /// <summary>The table a pawn is working a bill at, if it is a round table.</summary>
        public static CompArtificerTable At(Pawn p) =>
            (p?.CurJob?.bill?.billStack?.billGiver as Thing)?.TryGetComp<CompArtificerTable>();

        /// <summary>Quality lift for what a pawn makes at a round table.</summary>
        public static QualityCategory Lift(Pawn p, QualityCategory q)
        {
            CompArtificerTable t = At(p);
            if (t == null) return q;
            int n = t.Stones;
            float c = t.QualityChance;
            for (int i = 0; i < n && q < QualityCategory.Legendary; i++)
                if (Rand.Chance(c)) q++;
            return q;
        }
    }

    public class StatPart_ArtificerTable : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            float b = req.HasThing ? req.Thing.TryGetComp<CompArtificerTable>()?.SpeedBonus ?? 0f : 0f;
            if (b > 0f) val *= 1f + b;
        }

        public override string ExplanationPart(StatRequest req)
        {
            float b = req.HasThing ? req.Thing.TryGetComp<CompArtificerTable>()?.SpeedBonus ?? 0f : 0f;
            return b > 0f ? "Inlaid artifacts: x" + (1f + b).ToStringPercent() : null;
        }
    }

    [StaticConstructorOnStartup]
    public static class ArtificerTableInstaller
    {
        static ArtificerTableInstaller()
        {
            StatDef s = StatDefOf.WorkTableWorkSpeedFactor;
            if (s == null) return;
            if (s.parts == null) s.parts = new List<StatPart>();
            if (!s.parts.Any(p => p is StatPart_ArtificerTable)) s.parts.Add(new StatPart_ArtificerTable { parentStat = s });
        }
    }

    /// <summary>Vanilla-quality products (apparel, weapons) made at a round table.</summary>
    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn), new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) })]
    public static class Patch_ArtificerTableQuality
    {
        public static void Postfix(Pawn pawn, ref QualityCategory __result) => __result = CompArtificerTable.Lift(pawn, __result);
    }
}
