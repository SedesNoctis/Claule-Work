using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// CRACKING AN EGG. A quiet nest does not always hold, and a clutch can come at the worst time - when keeping it
    /// would doom the whole family. An Avir would rather crack the egg before it hatches than let that happen.
    ///   the egg: a command on it, no hotkey, and a confirmation - so it is never done by accident;
    ///   the parents: a grief that does not stack (a new loss renews it) and lasts a month, and their fertility falls
    ///     by three quarters for the same month - an egg-layer lays far more slowly too;
    ///   the clutch: a chick that hatches with no clutch-mate left feels someone missing for the rest of its life,
    ///     worse when two of a clutch of three were lost.
    /// </summary>
    public static class AvirEggCrack
    {
        public const int GriefTicks = GenDate.TicksPerQuadrum;

        public static Command_Action Command(Thing egg) => new Command_Action
        {
            defaultLabel = "Crack the egg",
            defaultDesc = "Break the shell before the chick inside can hatch. It cannot be undone; the parents will grieve, "
                          + "and for a month their bodies will hardly allow another clutch.",
            icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
            action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Crack this egg? The chick inside will never hatch, and its parents will carry the loss.",
                () => Crack(egg), destructive: true)),
        };

        public static void Crack(Thing egg)
        {
            if (egg == null || egg.Destroyed) return;
            CompAvirEggParents.Parents(egg, out Pawn mother, out Pawn father);
            Map map = egg.MapHeld;
            IntVec3 cell = egg.PositionHeld;
            CompAvirEggCaste comp = CompAvirEggCaste.Get(egg);
            int clutch = comp?.clutch ?? 0;
            if (comp != null) comp.clutch = 0;   // counted here, not again by the destroy
            ThingDef filth = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_AmnioticFluid");
            if (filth != null && map != null) FilthMaker.TryMakeFilth(cell, map, filth);
            egg.Destroy(DestroyMode.Vanish);
            if (clutch != 0) GameComponent_AvirClutch.Instance?.Lost(clutch);
            Grieve(mother);
            Grieve(father);
        }

        private static void Grieve(Pawn p)
        {
            if (p == null || p.Dead) return;
            ThoughtDef grief = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_CrackedEgg");
            if (grief != null && p.needs?.mood != null)
            {
                p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(grief);   // does not stack: a new loss renews it
                p.needs.mood.thoughts.memories.TryGainMemory(grief);
            }
            HediffDef stress = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ClutchGrief");
            if (stress != null && p.health != null)
            {
                Hediff old = p.health.hediffSet.GetFirstHediffOfDef(stress);
                if (old != null) p.health.RemoveHediff(old);
                p.health.AddHediff(stress);
            }
        }
    }

    /// <summary>Every clutch: how many eggs it had and how many were lost; and which clutch each chick hatched from.</summary>
    public class GameComponent_AvirClutch : GameComponent
    {
        public static GameComponent_AvirClutch Instance;
        private int nextId = 1;
        private Dictionary<int, int> size = new Dictionary<int, int>();
        private Dictionary<int, int> lost = new Dictionary<int, int>();
        private Dictionary<int, int> chickClutch = new Dictionary<int, int>();

        public GameComponent_AvirClutch(Game game) { Instance = this; }

        public int NewClutch(int eggs)
        {
            int id = nextId++;
            size[id] = eggs;
            return id;
        }

        public void Lost(int clutch) => lost[clutch] = (lost.TryGetValue(clutch, out int n) ? n : 0) + 1;

        public void Hatched(Pawn chick, int clutch) => chickClutch[chick.thingIDNumber] = clutch;

        /// <summary>How many clutch-mates this one lost, when none of them is left (0 otherwise).</summary>
        public int LostAlone(Pawn p)
        {
            if (!chickClutch.TryGetValue(p.thingIDNumber, out int c) || !size.TryGetValue(c, out int s)) return 0;
            int l = lost.TryGetValue(c, out int n) ? n : 0;
            return l >= s - 1 ? l : 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextId, "avirNextClutch", 1);
            Scribe_Collections.Look(ref size, "avirClutchSize", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lost, "avirClutchLost", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref chickClutch, "avirChickClutch", LookMode.Value, LookMode.Value);
            if (size == null) size = new Dictionary<int, int>();
            if (lost == null) lost = new Dictionary<int, int>();
            if (chickClutch == null) chickClutch = new Dictionary<int, int>();
            Instance = this;
        }
    }

    /// <summary>Hatched with no clutch-mate left: someone is missing (two of three lost - worse). For life.</summary>
    public class ThoughtWorker_AvirLostClutch : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            int l = GameComponent_AvirClutch.Instance?.LostAlone(p) ?? 0;
            if (l <= 0) return false;
            return ThoughtState.ActiveAtStage(l >= 2 ? 1 : 0);
        }
    }

    /// <summary>The grief slows an egg-layer too: while it lasts, VEF's egg progress moves at a quarter of its pace.</summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirClutchGriefLaying
    {
        private static FieldInfo progress;
        private static HediffDef grief;

        static Patch_AvirClutchGriefLaying()
        {
            System.Type layer = AccessTools.TypeByName("VEF.Genes.HediffComp_HumanEggLayer");
            progress = layer == null ? null : AccessTools.Field(layer, "eggProgress");
            MethodInfo tick = layer == null ? null : AccessTools.Method(layer, "CompPostTick");
            if (progress == null || progress.FieldType != typeof(float) || tick == null)
            {
                Log.Message("[Avir] clutch grief: VEF egg layer not found as expected - fertility drop applies to the stat only.");
                return;
            }
            new Harmony("avir.clutchgrief").Patch(tick,
                prefix: new HarmonyMethod(typeof(Patch_AvirClutchGriefLaying), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(Patch_AvirClutchGriefLaying), nameof(Postfix)));
        }

        public static void Prefix(HediffComp __instance, out float __state) => __state = (float)progress.GetValue(__instance);

        public static void Postfix(HediffComp __instance, float __state)
        {
            float now = (float)progress.GetValue(__instance);
            if (now <= __state) return;
            if (grief == null) grief = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ClutchGrief");
            if (grief != null && __instance.Pawn?.health?.hediffSet?.HasHediff(grief) == true)
                progress.SetValue(__instance, __state + (now - __state) * 0.25f);
        }
    }
}
