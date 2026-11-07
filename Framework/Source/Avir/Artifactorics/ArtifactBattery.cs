using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using LudeonTK;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // CRYSTAL BATTERY (Oct 4, 2026) - the "ammunition" of the artifacts.
    //   - Two items: an empty battery (Avir_CrystalBatteryEmpty - what the table makes, dull) and
    //     a charged one (Avir_CrystalBattery, holds up to batteryCapacity = 150 charge units - the
    //     same units as an artifact's capacity: 100 x setting quality x wearer's sensitivity).
    //     Carried in the inventory, stack by 5, 0.25 kg. Charged batteries stack only at equal charge.
    //   - Used by itself when the wearer's lowest artifact drops below the recharge threshold
    //     (before the wearer's own psychic recharge), or at once by order (gizmo). It fills the
    //     emptiest artifact first, then tops up the others; what is not needed stays in it.
    //     A spent battery turns back into an empty one and the pawn puts it down.
    //   - Charged at the artificer's table by whoever can give psychic sensitivity: one battery
    //     takes batteryChargeSensitivity (1.5); a pawn that can give less fills it partly (a 100%
    //     pawn keeps its 10% and gives 90% - 60% of a battery), and another tops it up later.
    //     What is given is lost for a day, like a recharge.
    // =====================================================================================

    public class CompProperties_CrystalBattery : CompProperties
    {
        public CompProperties_CrystalBattery() { compClass = typeof(CompCrystalBattery); }
    }

    /// <summary>On the charged battery: how much it holds.</summary>
    public class CompCrystalBattery : ThingComp
    {
        public float stored = -1f;   // -1: never set (trader, dev mode) - full

        public static float Capacity => ArtifactoricsSettingsDef.Main?.batteryCapacity ?? 150f;

        public float Stored => stored < 0f ? Capacity : stored;
        public bool IsFull => Stored >= Capacity - 0.01f;

        public override bool AllowStackWith(Thing other)
        {
            CompCrystalBattery o = other.TryGetComp<CompCrystalBattery>();
            return o != null && Mathf.Abs(o.Stored - Stored) < 0.5f;
        }

        public override void PostSplitOff(Thing piece)
        {
            base.PostSplitOff(piece);
            CompCrystalBattery o = piece.TryGetComp<CompCrystalBattery>();
            if (o != null) o.stored = stored;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref stored, "stored", -1f);
        }

        public override string TransformLabel(string label) => IsFull ? label : label + " (" + (Stored / Capacity).ToStringPercent() + ")";

        public override string CompInspectStringExtra() => "Charge: " + Stored.ToString("0") + " / " + Capacity.ToString("0");

        /// <summary>Charged at the artificer's table: what was already in it plus what the charger gave.</summary>
        public override void Notify_RecipeProduced(Pawn pawn)
        {
            base.Notify_RecipeProduced(pawn);
            if (pawn != null && RecipeWorker_CrystalBattery.pending.TryGetValue(pawn.thingIDNumber, out float units))
            {
                RecipeWorker_CrystalBattery.pending.Remove(pawn.thingIDNumber);
                stored = Mathf.Clamp(units, 0f, Capacity);
            }
        }
    }

    [DefOf]
    public static class ArtifactBatteryDefOf
    {
        public static ThingDef Avir_CrystalBattery;
        public static ThingDef Avir_CrystalBatteryEmpty;
        public static RecipeDef Avir_ChargeCrystalBattery;
        static ArtifactBatteryDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactBatteryDefOf)); }
    }

    public static class ArtifactBattery
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public static IEnumerable<Thing> Charged(Pawn p)
        {
            if (p?.inventory == null) yield break;
            foreach (Thing t in p.inventory.innerContainer)
                if (t.def == ArtifactBatteryDefOf.Avir_CrystalBattery) yield return t;
        }

        public static bool HasUsable(Pawn p) => Charged(p).Any();

        /// <summary>Charge units the worn artifacts still miss.</summary>
        public static float Missing(Pawn p)
        {
            float m = 0f;
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
                if (s.inlaid != null && s.inlaid.HasHeart) m += (1f - s.inlaid.charge) * ArtifactCharge.Capacity(s.inlaid, p);
            return m;
        }

        /// <summary>One battery into the worn artifacts: the emptiest first, then the others. Partly
        /// spent batteries go first, so full ones stay whole.</summary>
        public static bool Use(Pawn p, bool forced)
        {
            if (p?.inventory == null || Missing(p) < 0.5f) return false;
            Thing stack = Charged(p).OrderBy(t => t.TryGetComp<CompCrystalBattery>().Stored).FirstOrDefault();
            if (stack == null) return false;
            Thing one = stack.stackCount > 1 ? stack.SplitOff(1) : stack;
            if (one != stack) p.inventory.innerContainer.TryAdd(one, false);
            CompCrystalBattery b = one.TryGetComp<CompCrystalBattery>();
            float left = b.Stored, given = 0f;
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p).Where(s => s.inlaid != null && s.inlaid.HasHeart).OrderBy(s => s.inlaid.charge))
            {
                if (left <= 0f) break;
                float cap = ArtifactCharge.Capacity(s.inlaid, p);
                float give = Mathf.Min((1f - s.inlaid.charge) * cap, left);
                if (give <= 0f) continue;
                s.inlaid.charge = Mathf.Min(1f, s.inlaid.charge + give / cap);
                left -= give; given += give;
            }
            ArtifactMilestones.Invalidate(p);
            ArtifactBonusCache.Invalidate(p);
            bool spent = left <= 0.5f;
            if (spent)
            {
                // the crystal goes dull: an empty battery, put down at once
                p.inventory.innerContainer.Remove(one);
                one.Destroy();
                Thing empty = ThingMaker.MakeThing(ArtifactBatteryDefOf.Avir_CrystalBatteryEmpty);
                if (p.Spawned) GenPlace.TryPlaceThing(empty, p.Position, p.Map, ThingPlaceMode.Near);
                else p.inventory.innerContainer.TryAdd(empty);
            }
            else b.stored = left;
            if (p.Spawned)
            {
                FleckMaker.ThrowMicroSparks(p.DrawPos, p.Map);
                SoundDefOf.Psycast_Skip_Pulse.PlayOneShot(new TargetInfo(p.Position, p.Map));
            }
            if (p.Faction == Faction.OfPlayer)
                Messages.Message(p.LabelShort + " drew " + given.ToString("0") + " charge from a crystal battery" + (spent ? " and put the empty one down." : "."),
                    p, MessageTypeDefOf.SilentInput, false);
            return true;
        }

        /// <summary>Empty batteries are not carried: put down where the pawn stands.</summary>
        public static void DropEmpties(Pawn p)
        {
            if (p?.inventory == null || !p.Spawned) return;
            foreach (Thing t in p.inventory.innerContainer.Where(t => t.def == ArtifactBatteryDefOf.Avir_CrystalBatteryEmpty).ToList())
                p.inventory.innerContainer.TryDrop(t, p.Position, p.Map, ThingPlaceMode.Near, out Thing _);
        }

        // ---------------------------------------------------------------- charging

        /// <summary>Psychic sensitivity the pawn may give now (the same floor as a recharge).</summary>
        public static float CanGive(Pawn p)
        {
            if (p == null || S == null) return 0f;
            ArtifactCharge.RechargeCost(p, out _, out float allowed);
            return allowed;
        }

        /// <summary>Sensitivity for one full battery.</summary>
        public static float Cost => S?.batteryChargeSensitivity ?? 1.5f;

        /// <summary>Can give at least a little (5% of a battery).</summary>
        public static bool CanCharge(Pawn p) => CanGive(p) >= Cost * 0.05f;

        public static void Drain(Pawn p, float give)
        {
            if (give <= 0f) return;
            var h = p.health.hediffSet.GetFirstHediffOfDef(ArtifactChargeDefOf.Avir_ArtifactDrained) as Hediff_ArtifactDrained;
            if (h == null)
            {
                h = (Hediff_ArtifactDrained)HediffMaker.MakeHediff(ArtifactChargeDefOf.Avir_ArtifactDrained, p);
                p.health.AddHediff(h);
            }
            h.given += give;
            h.ticksLeft = GenDate.TicksPerDay;
            h.Refresh();
        }

        public static float StoredIn(Thing t) =>
            t?.def == ArtifactBatteryDefOf.Avir_CrystalBattery ? t.TryGetComp<CompCrystalBattery>()?.Stored ?? 0f : 0f;
    }

    /// <summary>Charging: the charger gives what it can, up to what the battery misses.</summary>
    public class RecipeWorker_CrystalBattery : RecipeWorker
    {
        internal static readonly Dictionary<int, float> pending = new Dictionary<int, float>();

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            if (billDoer == null || recipe != ArtifactBatteryDefOf.Avir_ChargeCrystalBattery) return;
            float had = ingredients.Sum(ArtifactBattery.StoredIn);
            float cap = CompCrystalBattery.Capacity;
            float give = Mathf.Min(ArtifactBattery.CanGive(billDoer), (cap - had) / cap * ArtifactBattery.Cost);
            ArtifactBattery.Drain(billDoer, give);
            pending[billDoer.thingIDNumber] = had + give / ArtifactBattery.Cost * cap;
            if (billDoer.Spawned) FleckMaker.ThrowMicroSparks(billDoer.DrawPos, billDoer.Map);
            if (billDoer.Faction == Faction.OfPlayer)
                Messages.Message(billDoer.LabelShort + " gave " + give.ToStringPercent() + " psychic sensitivity to a crystal battery.",
                    billDoer, MessageTypeDefOf.SilentInput, false);
        }
    }

    /// <summary>Charging takes empty and partly charged batteries, and only a pawn who can give.</summary>
    [HarmonyPatch(typeof(Bill), nameof(Bill.IsFixedOrAllowedIngredient), new[] { typeof(Thing) })]
    public static class Patch_CrystalBatteryIngredient
    {
        public static void Postfix(Bill __instance, Thing __0, ref bool __result)
        {
            if (!__result || __0?.def != ArtifactBatteryDefOf.Avir_CrystalBattery) return;
            if (__instance.recipe == ArtifactBatteryDefOf.Avir_ChargeCrystalBattery && __0.TryGetComp<CompCrystalBattery>()?.IsFull == true) __result = false;
        }
    }

    [HarmonyPatch(typeof(Bill), nameof(Bill.PawnAllowedToStartAnew))]
    public static class Patch_CrystalBatteryCharger
    {
        public static void Postfix(Bill __instance, Pawn p, ref bool __result)
        {
            if (__result && __instance.recipe == ArtifactBatteryDefOf.Avir_ChargeCrystalBattery && !ArtifactBattery.CanCharge(p))
            {
                __result = false;
                JobFailReason.Is("no psychic sensitivity left to give");
            }
        }
    }

    /// <summary>"Use a battery now" on the wearer.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_CrystalBatteryGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            Pawn p = __instance;
            if (p?.Faction != Faction.OfPlayer || !p.IsColonistPlayerControlled || !ArtifactBattery.HasUsable(p)) yield break;
            float missing = ArtifactBattery.Missing(p);
            var cmd = new Command_Action
            {
                defaultLabel = "Use crystal battery",
                defaultDesc = "Draw charge from a carried crystal battery at once: the emptiest artifact first, then the others. "
                              + "The artifacts miss " + missing.ToString("0") + " charge now.",
                icon = ArtifactBatteryDefOf.Avir_CrystalBattery.uiIcon,
                action = () => ArtifactBattery.Use(p, true)
            };
            if (missing < 0.5f) cmd.Disable("the artifacts are full");
            yield return cmd;
        }
    }

    // ================================================================== dev mode

    public static class ArtifactoricsDebugActions
    {
        private static void Spawn(ThingDef d, int count, System.Action<Thing> setup = null)
        {
            Thing t = ThingMaker.MakeThing(d, d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null);
            t.stackCount = Mathf.Min(count, d.stackLimit);
            setup?.Invoke(t);
            GenPlace.TryPlaceThing(t, Verse.UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
        }

        [DebugAction("Artifactorics", "Spawn empty batteries x5", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void EmptyBatteries() => Spawn(ArtifactBatteryDefOf.Avir_CrystalBatteryEmpty, 5);

        [DebugAction("Artifactorics", "Spawn charged batteries x5", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ChargedBatteries() => Spawn(ArtifactBatteryDefOf.Avir_CrystalBattery, 5, t => t.TryGetComp<CompCrystalBattery>().stored = CompCrystalBattery.Capacity);

        [DebugAction("Artifactorics", "Spawn half-charged battery", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void HalfBattery() => Spawn(ArtifactBatteryDefOf.Avir_CrystalBattery, 1, t => t.TryGetComp<CompCrystalBattery>().stored = CompCrystalBattery.Capacity / 2f);

        [DebugAction("Artifactorics", "Give charged batteries x5", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void GiveBatteries(Pawn p)
        {
            if (p?.inventory == null) return;
            Thing t = ThingMaker.MakeThing(ArtifactBatteryDefOf.Avir_CrystalBattery);
            t.stackCount = 5;
            t.TryGetComp<CompCrystalBattery>().stored = CompCrystalBattery.Capacity;
            p.inventory.innerContainer.TryAdd(t);
        }

        [DebugAction("Artifactorics", "Drain worn artifacts to 10%", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Drain(Pawn p)
        {
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
                if (s.inlaid != null) s.inlaid.charge = 0.1f;
            ArtifactMilestones.Invalidate(p);
        }

        [DebugAction("Artifactorics", "Fill worn artifacts' charge", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Fill(Pawn p)
        {
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
                if (s.inlaid != null) s.inlaid.charge = 1f;
            ArtifactMilestones.Invalidate(p);
        }

        [DebugAction("Artifactorics", "Amulet: +10 points", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AmuletPoints(Pawn p)
        {
            CompAvirAmulet a = AmuletDevelopment.WornBy(p);
            if (a == null) return;
            int target = AmuletDevelopment.PointsEarned(a) + 10;
            for (int guard = 0; guard < 100000 && AmuletDevelopment.PointsEarned(a) < target; guard++) a.xpEarned += 500f;
        }

        [DebugAction("Artifactorics", "Artifacts: +5 points each", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ArtifactPoints(Pawn p)
        {
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
            {
                ArtifactState st = s.inlaid;
                if (st == null) continue;
                int target = st.PointsAvailable + 5;
                for (int guard = 0; guard < 100000 && st.PointsAvailable < target; guard++) st.AddXp(500f);
            }
        }

        [DebugAction("Artifactorics", "Spawn Avir amulet (choose metals)...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ChosenAmulet()
        {
            Map map = Find.CurrentMap;
            IntVec3 at = Verse.UI.MouseCell();
            ArtifactMetalChoice.Menu(ArtifactMetalChoice.Necklaces, n =>
                ArtifactMetalChoice.Menu(ArtifactMetalChoice.CoresFor(n), c =>
                    ArtifactMetalChoice.QualityMenu(q =>
                    {
                        Thing t = ThingMaker.MakeThing(AvirAmuletDefOf.Avir_TraditionalAmulet);
                        CompAvirAmulet a = t.TryGetComp<CompAvirAmulet>();
                        if (a == null) return;
                        a.caste = null; a.subclass = null;
                        a.necklaceMetal = n; a.coreMetal = c;
                        ArtifactMetalChoice.SetAmuletQuality(a, q);
                        if (!at.InBounds(map)) at = map.Center;
                        GenPlace.TryPlaceThing(t, at, map, ThingPlaceMode.Near);
                    })));
        }

        [DebugAction("Artifactorics", "Spawn artifact (choose metals)...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ChosenArtifact()
        {
            Map map = Find.CurrentMap;
            IntVec3 at = Verse.UI.MouseCell();
            ArtifactMetalChoice.Menu(ArtifactMetalChoice.Rims, rim =>
                ArtifactMetalChoice.Menu(ArtifactMetalChoice.HeartsFor(rim), heart =>
                    ArtifactMetalChoice.QualityMenu(q =>
                    {
                        Thing t = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_Artifact);
                        CompArtifact ca = t.TryGetComp<CompArtifact>();
                        if (ca == null) return;
                        ca.state = ArtifactMetalChoice.NewArtifact(heart, rim, q);
                        if (!at.InBounds(map)) at = map.Center;
                        GenPlace.TryPlaceThing(t, at, map, ThingPlaceMode.Near);
                    })));
        }

        [DebugAction("Artifactorics", "Spawn A'Shaign amulet (tisserium)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AShaignAmulet()
        {
            Apparel t = AvirFactionGear.MakeAmulet(DefDatabase<ArtifactMetalDef>.GetNamedSilentFail("ArtifactMetal_Silver"),
                new List<ArtifactMetalDef> { DefDatabase<ArtifactMetalDef>.GetNamedSilentFail("ArtifactMetal_Tisserium") }, null, QualityCategory.Excellent);
            if (t != null) GenPlace.TryPlaceThing(t, Verse.UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
        }

        [DebugAction("Artifactorics", "Spawn A'Schialis amulet (essentium)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ASchialisAmulet()
        {
            Apparel t = AvirFactionGear.MakeAmulet(DefDatabase<ArtifactMetalDef>.GetNamedSilentFail("ArtifactMetal_Gold"),
                new List<ArtifactMetalDef> { DefDatabase<ArtifactMetalDef>.GetNamedSilentFail("ArtifactMetal_Essentium") }, null, QualityCategory.Excellent);
            if (t != null) GenPlace.TryPlaceThing(t, Verse.UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
        }
    }
}
