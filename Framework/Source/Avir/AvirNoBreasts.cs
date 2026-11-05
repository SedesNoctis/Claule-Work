using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    // =====================================================================================
    // THE AVIR CHEST. The Avir are chimeras made of energy. Under RimJobWorld a male with the avian frame has
    // nothing on the chest - not even RJW's featureless chest, which is only a layer laid over it - and RJW is kept
    // from putting anything there again. A female keeps her breasts, but small ones: whatever RJW gives her is
    // brought down to a small size.
    // Works by reflection: no reference to RJW or to AvirRJW.
    // =====================================================================================
    [StaticConstructorOnStartup]
    public static class AvirNoBreasts
    {
        private static GeneDef frame;
        private static MethodInfo breastsBpr, partsList, isSexPart;
        private static FieldInfo featureless;

        static AvirNoBreasts()
        {
            if (!ModsConfig.IsActive("rim.job.world")) return;
            try
            {
                frame = DefDatabase<GeneDef>.GetNamedSilentFail("Avir_AvianFrame");
                Type helper = AccessTools.TypeByName("rjw.Genital_Helper");
                if (frame == null || helper == null) return;
                breastsBpr = AccessTools.Method(helper, "get_breastsBPR", new[] { typeof(Pawn) });
                partsList = AccessTools.Method(helper, "get_PartsHediffList");
                isSexPart = AccessTools.Method(helper, "is_sex_part", new[] { typeof(Hediff) });
                featureless = AccessTools.Field(helper, "featureless_chest");
                var h = new Harmony("Tenebris.Avir.NoBreasts");
                MethodInfo addBreasts = AccessTools.Method(AccessTools.TypeByName("rjw.SexPartAdder"), "add_breasts");
                if (addBreasts != null) h.Patch(addBreasts, prefix: new HarmonyMethod(typeof(AvirNoBreasts), nameof(SkipForAvir)) { priority = Priority.First });
                // AvirRJW gave males a featureless chest: not any more
                MethodInfo maleChest = AccessTools.Method(AccessTools.TypeByName("AvirRJW.RjwPartEnforcer"), "EnforceMaleChest");
                if (maleChest != null) h.Patch(maleChest, prefix: new HarmonyMethod(typeof(AvirNoBreasts), nameof(SkipForAvir)) { priority = Priority.First });
                MethodInfo sexualize = AccessTools.Method(AccessTools.TypeByName("rjw.Sexualizer"), "sexualize_pawn");
                if (sexualize != null) h.Patch(sexualize, postfix: new HarmonyMethod(typeof(AvirNoBreasts), nameof(After)) { priority = Priority.Last });
            }
            catch (Exception e) { Log.Warning("[Avir] no-breasts patch for RJW failed: " + e.Message); }
        }

        public static bool IsFramed(Pawn p) => frame != null && p?.genes != null && p.genes.HasActiveGene(frame);

        /// <summary>The size a female's breasts are brought down to (RJW's part size; small).</summary>
        public const float SmallSize = 0.15f;

        public static bool SkipForAvir(Pawn pawn)
        {
            if (!IsFramed(pawn)) return true;
            if (pawn.gender == Gender.Female) return true;   // she keeps them - shrunk afterwards
            ClearChest(pawn);
            return false;
        }

        public static void After(Pawn pawn) => Settle(pawn);

        /// <summary>A framed male: a bare chest; a framed female: small breasts.</summary>
        public static void Settle(Pawn pawn)
        {
            if (!IsFramed(pawn)) return;
            if (pawn.gender == Gender.Female) ShrinkBreasts(pawn); else ClearChest(pawn);
        }

        private static IEnumerable<Hediff> ChestParts(Pawn pawn)
        {
            if (pawn?.health == null || breastsBpr == null || partsList == null) return null;
            BodyPartRecord bpr = breastsBpr.Invoke(null, new object[] { pawn }) as BodyPartRecord;
            if (bpr == null) return null;
            return partsList.Invoke(null, new object[] { pawn, bpr }) as IEnumerable<Hediff>;
        }

        /// <summary>Brings every breast part on her chest down to SmallSize: the part's own size (RJW 5 keeps it in
        /// the sex-part comp) and the severity (older RJW reads the size from it).</summary>
        public static void ShrinkBreasts(Pawn pawn)
        {
            try
            {
                var onPart = ChestParts(pawn);
                if (onPart == null) return;
                HediffDef flat = featureless?.GetValue(null) as HediffDef;
                foreach (Hediff hd in onPart.ToList())
                {
                    if (hd == null || hd.def == flat) continue;
                    if (isSexPart != null && !(bool)isSexPart.Invoke(null, new object[] { hd })) continue;
                    if (hd is HediffWithComps hc)
                        foreach (HediffComp c in hc.comps)
                        {
                            if (!c.GetType().Name.Contains("SexPart")) continue;
                            MethodInfo force = AccessTools.Method(c.GetType(), "ForceSize", new[] { typeof(float) });
                            FieldInfo size = AccessTools.Field(c.GetType(), "baseSize") ?? AccessTools.Field(c.GetType(), "size");
                            if (force != null) force.Invoke(c, new object[] { SmallSize });
                            else if (size != null && size.FieldType == typeof(float) && (float)size.GetValue(c) > SmallSize) size.SetValue(c, SmallSize);
                        }
                    if (hd.Severity > SmallSize) hd.Severity = SmallSize;
                }
            }
            catch (Exception e) { Log.WarningOnce("[Avir] could not size an Avir chest: " + e.Message, 0x5A1B7F); }
        }

        /// <summary>Takes every RJW part off the chest (breasts, and the featureless chest too).</summary>
        public static void ClearChest(Pawn pawn)
        {
            try
            {
                var onPart = ChestParts(pawn);
                if (onPart == null) return;
                HediffDef flat = featureless?.GetValue(null) as HediffDef;
                var remove = new List<Hediff>();
                foreach (Hediff hd in onPart)
                {
                    if (hd == null) continue;
                    bool sex = isSexPart != null && (bool)isSexPart.Invoke(null, new object[] { hd });
                    if (sex || hd.def == flat) remove.Add(hd);
                }
                foreach (Hediff hd in remove) pawn.health.RemoveHediff(hd);
            }
            catch (Exception e) { Log.WarningOnce("[Avir] could not clear an Avir chest: " + e.Message, 0x5A1B7E); }
        }
    }

    /// <summary>Pawns already in a save are settled once, when the game is loaded: bare males, small-breasted females.</summary>
    public class GameComponent_AvirNoBreasts : GameComponent
    {
        public GameComponent_AvirNoBreasts(Game game) { }

        public override void FinalizeInit()
        {
            if (!ModsConfig.IsActive("rim.job.world")) return;
            foreach (Pawn p in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
                AvirNoBreasts.Settle(p);
        }
    }
}
