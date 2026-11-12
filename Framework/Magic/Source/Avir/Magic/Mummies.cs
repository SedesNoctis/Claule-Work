using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>MUMMIES (Fix255, the dry wind held - the author left the how of it to me). A corpse lying under a drought
    /// dries through instead of rotting: in about a minute and a half of the drought it is a mummy. A mummy does not rot
    /// any more, does not stink, breeds no sickness and draws no flies - and has no meat left: nothing eats it, butchering
    /// gives what a dried body gives (leather, never meat). It is drawn as it was when it dried, darkened to old leather.
    /// A corpse already rotting dries as it is (the rot stops where it stood); bare bones are past drying.
    /// Kept for the whole game (a corpse may be carried to another map or into a grave), and saved with it.</summary>
    public class GameComponent_Mummies : GameComponent
    {
        private Dictionary<Corpse, float> dried = new Dictionary<Corpse, float>();
        private List<Corpse> keys; private List<float> vals;
        public const float Full = 1f, PerDroughtTick = 0.04f;   // a drought field ticks once a second: ~25 seconds of it

        // asked on every rot step of every food and corpse on every map: the game's component is kept, not looked up
        private static GameComponent_Mummies inst;
        public GameComponent_Mummies(Game g) { inst = this; }
        public static GameComponent_Mummies Get => inst;

        public static float Of(Corpse c) => c != null && inst != null && inst.dried.Count > 0 && inst.dried.TryGetValue(c, out float f) ? f : 0f;
        public static bool Mummy(Corpse c) => Of(c) >= Full;

        /// <summary>The drought works on a corpse a little more.</summary>
        public static void Dry(Corpse c, float amount)
        {
            GameComponent_Mummies g = Get;
            if (g == null || c == null || c.Destroyed || c.InnerPawn == null) return;
            CompRottable rot = c.GetComp<CompRottable>();
            if (rot != null && rot.Stage == RotStage.Dessicated) return;   // bones are past drying
            g.dried.TryGetValue(c, out float was);
            if (was >= Full) return;
            float now = Mathf.Min(Full, was + amount);
            g.dried[c] = now;
            if (c.Spawned && Rand.Chance(0.35f)) FleckMaker.ThrowDustPuffThick(c.DrawPos, c.Map, 0.9f, new Color(0.85f, 0.7f, 0.45f, 0.5f));
            if (now >= Full && c.Spawned)
            {
                c.InnerPawn.Drawer?.renderer?.SetAllGraphicsDirty();
                FilthMaker.TryMakeFilth(c.Position, c.Map, DefDatabase<ThingDef>.GetNamedSilentFail("Filth_Sand") ?? ThingDefOf.Filth_Dirt, 1);
                MoteMaker.ThrowText(c.DrawPos, c.Map, "Mummified", new Color(0.85f, 0.7f, 0.45f), 2.5f);
            }
        }

        public override void GameComponentTick()
        {
            if (dried.Count == 0 || Find.TickManager.TicksGame % 2500 != 0) return;
            foreach (Corpse c in dried.Keys.Where(c => c == null || c.Destroyed).ToList()) dried.Remove(c);
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) foreach (Corpse c in dried.Keys.Where(c => c == null || c.Destroyed).ToList()) dried.Remove(c);
            Scribe_Collections.Look(ref dried, "mummies", LookMode.Reference, LookMode.Value, ref keys, ref vals);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (dried == null) dried = new Dictionary<Corpse, float>();
                foreach (Corpse c in dried.Keys.Where(c => c == null).ToList()) dried.Remove(c);
            }
        }

        [DebugAction("Magic", "Mummy: dry the corpse under the mouse", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevDry()
        {
            Corpse c = Verse.UI.MouseCell().GetThingList(Find.CurrentMap).OfType<Corpse>().FirstOrDefault();
            if (c == null) { Messages.Message("No corpse there.", MessageTypeDefOf.RejectInput, false); return; }
            Dry(c, Full);
        }
    }

    /// <summary>A mummy does not rot on.</summary>
    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.RotProgress), MethodType.Setter)]
    public static class Patch_MummyNoRot
    {
        public static bool Prefix(CompRottable __instance, float value) =>
            !(__instance.parent is Corpse c && value > __instance.RotProgress && GameComponent_Mummies.Mummy(c));
    }

    /// <summary>Nothing eats a mummy.</summary>
    [HarmonyPatch(typeof(Corpse), nameof(Corpse.IngestibleNow), MethodType.Getter)]
    public static class Patch_MummyNotFood
    {
        public static void Postfix(Corpse __instance, ref bool __result) { if (__result && GameComponent_Mummies.Mummy(__instance)) __result = false; }
    }

    /// <summary>Butchered, a mummy gives no meat.</summary>
    [HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
    public static class Patch_MummyNoMeat
    {
        public static IEnumerable<Thing> Postfix(IEnumerable<Thing> __result, Corpse __instance)
        {
            bool mummy = GameComponent_Mummies.Mummy(__instance);
            foreach (Thing t in __result) { if (mummy && t.def.IsMeat) { t.Destroy(); continue; } yield return t; }
        }
    }

    [HarmonyPatch(typeof(Corpse), nameof(Corpse.GetInspectString))]
    public static class Patch_MummyInspect
    {
        public static void Postfix(Corpse __instance, ref string __result)
        {
            float f = GameComponent_Mummies.Of(__instance);
            if (f <= 0f) return;
            string line = f >= GameComponent_Mummies.Full ? "Mummified by the dry wind: it will not rot, and has no meat." : "Drying out: " + f.ToStringPercent();
            __result = __result.NullOrEmpty() ? line : __result + "\n" + line;
        }
    }

    /// <summary>A mummy is drawn darkened to old leather (the more the drier).</summary>
    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    public static class Patch_MummyTint
    {
        private static readonly Color Leather = new Color(0.58f, 0.44f, 0.31f);
        public static void Postfix(Pawn ___pawn, ref PawnDrawParms __result)
        {
            if (___pawn == null || !___pawn.Dead) return;
            float f = GameComponent_Mummies.Of(___pawn.Corpse);
            if (f <= 0f) return;
            Color t = __result.tint;
            float grey = (t.r + t.g + t.b) / 3f;
            Color dry = new Color(grey * Leather.r * 1.25f, grey * Leather.g * 1.25f, grey * Leather.b * 1.25f, t.a);
            __result.tint = Color.Lerp(t, dry, Mathf.Clamp01(f) * 0.85f);
        }
    }
}
