using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AVIR FACTION RELATIONS.
    ///
    ///   - The two orders (A'Shaign / A'Schialis) are PERMANENT enemies of EACH OTHER. Their
    ///     hostility can never be bought off - the war between the Night and the Flame does not end.
    ///   - Each order starts HOSTILE to every OTHER faction, but only TEMPORARILY: that goodwill
    ///     can recover the ordinary way (gifts, quests, trade), so peace is possible with anyone
    ///     except the opposing order.
    ///   - The Free Nesting (the player's Avir faction) is NEUTRAL to both orders, but - like
    ///     them - starts TEMPORARILY hostile to every non-Avir faction. So all three Avir
    ///     factions fight the outside world; only the two orders fight each other, and only that
    ///     fight is permanent.
    ///
    /// Vanilla's permanentEnemy flag is all-or-nothing (permanent to EVERYONE), which is why this
    /// lives in code: initial goodwill is set per-pair, and only the order-vs-order pair is locked.
    /// Patched MANUALLY in a guarded static ctor (like the mod's other risky hooks) so a missing
    /// target method can never abort startup.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirFactionRelations
    {
        public const string AShaign     = "Avir_HighNesting";
        public const string AShialis    = "Avir_AShialis";
        public const string FreeNesting = "Avir_FreeNesting";

        private const int Hostile = -100; // clearly below the hostility threshold
        private const int Neutral = 0;

        static AvirFactionRelations()
        {
            Harmony h = new Harmony("yourname.avir.factionrelations");

            try
            {
                var m = AccessTools.Method(typeof(Faction), "TryMakeInitialRelationsWith");
                if (m != null)
                    h.Patch(m, postfix: new HarmonyMethod(typeof(AvirFactionRelations), nameof(InitialRelationsPostfix)));
                else
                    Log.Warning("[Avir] TryMakeInitialRelationsWith not found; faction relations not applied.");
            }
            catch (Exception e) { Log.Warning("[Avir] failed to patch TryMakeInitialRelationsWith: " + e.Message); }

            try
            {
                var m = AccessTools.Method(typeof(Faction), "TryAffectGoodwillWith");
                if (m != null)
                    h.Patch(m, prefix: new HarmonyMethod(typeof(AvirFactionRelations), nameof(PermanentWarPrefix)));
                else
                    Log.Warning("[Avir] TryAffectGoodwillWith not found; order war is not locked permanent.");
            }
            catch (Exception e) { Log.Warning("[Avir] failed to patch TryAffectGoodwillWith: " + e.Message); }
        }

        // ---- Harmony hooks ----

        // After each pair's initial relation is wired at world gen, override it to the Avir rules.
        public static void InitialRelationsPostfix(Faction __instance, Faction other)
        {
            try { Apply(__instance, other); }
            catch (Exception e) { Log.WarningOnce("[Avir] Faction relation setup failed: " + e, 0x4A51D2); }
        }

        // Block any RISE in goodwill between the two orders, so their war never ends. Every other
        // pair passes straight through, so those relations stay fully changeable.
        public static bool PermanentWarPrefix(Faction __instance, Faction other, int goodwillChange, ref bool __result)
        {
            if (goodwillChange > 0 && BothOrders(__instance, other))
            {
                __result = false; // refuse the increase
                return false;     // skip the original method
            }
            return true;
        }

        // ---- Logic ----

        private static bool Is(Faction f, string defName)
        {
            return f != null && f.def != null && f.def.defName == defName;
        }

        public static bool IsOrder(Faction f)
        {
            return Is(f, AShaign) || Is(f, AShialis);
        }

        // Any of the three Avir factions - the two orders plus the player's Free Nesting.
        public static bool IsAvir(Faction f)
        {
            return IsOrder(f) || Is(f, FreeNesting);
        }

        public static bool BothOrders(Faction a, Faction b)
        {
            return IsOrder(a) && IsOrder(b) && a != b;
        }

        private static void SetGoodwill(Faction a, Faction b, int target)
        {
            if (a == null || b == null || a == b) return;
            // During world generation there is no player faction and no ideos yet: the goodwill machinery
            // (situations, letters, OfPlayer) cannot run. Write the relation directly; the game-start pass
            // (GameComponent_AvirRelations) goes through the proper path once everything exists.
            if (Current.ProgramState != ProgramState.Playing || Faction.OfPlayerSilentFail == null)
            {
                SetDirect(a, b, target);
                SetDirect(b, a, target);
                return;
            }
            int current = a.GoodwillWith(b);
            if (current == target) return;
            a.TryAffectGoodwillWith(b, target - current, false, false);
        }

        private static void SetDirect(Faction from, Faction to, int goodwill)
        {
            FactionRelation r = from.RelationWith(to, true);
            if (r == null) return;
            r.baseGoodwill = goodwill;
            r.kind = goodwill <= -75 ? FactionRelationKind.Hostile : goodwill >= 75 ? FactionRelationKind.Ally : FactionRelationKind.Neutral;
        }

        public static void Apply(Faction a, Faction b)
        {
            if (a == null || b == null || a == b) return;

            bool aAvir = IsAvir(a);
            bool bAvir = IsAvir(b);
            if (!aAvir && !bAvir) return; // neither is an Avir faction - leave vanilla alone

            // Both sides are Avir. The two orders are permanent enemies of each other; every other
            // in-family pairing (i.e. anything involving the Free Nesting) stays neutral.
            if (aAvir && bAvir)
            {
                if (BothOrders(a, b)) SetGoodwill(a, b, Hostile);  // permanent (locked by prefix)
                else                  SetGoodwill(a, b, Neutral);  // Free Nesting <-> either order
                return;
            }

            // Exactly one side is Avir (an order OR the Free Nesting); the other is an outside
            // faction. All three Avir factions start hostile to outsiders - but recoverably, so
            // this is temporary, never permanent.
            SetGoodwill(a, b, Hostile);
        }
    }

    /// <summary>
    /// Applies the Avir relation rules ONCE, at the start of a game, after every faction - the
    /// player's included - has been created and its relations wired. This is what the worldgen hook
    /// alone can't guarantee: the player faction's opening goodwill is set through a separate path
    /// (natural colony goodwill) that runs afterward, so player-involving pairs need this later pass
    /// to stick.
    ///
    /// The saved 'applied' flag means it runs only the first time - never again on reload - so the
    /// TEMPORARY hostilities are free to recover afterward and are not reset every load. (The
    /// order-vs-order war stays locked permanently by the goodwill prefix regardless.) An existing
    /// save that predates this component gets the pass applied once on its next load.
    /// </summary>
    public class GameComponent_AvirRelations : GameComponent
    {
        private bool applied;

        public GameComponent_AvirRelations(Game game) { }

        public override void FinalizeInit()
        {
            if (applied) return;
            applied = true;

            try
            {
                if (Find.FactionManager == null) return;
                List<Faction> all = Find.FactionManager.AllFactionsListForReading;
                for (int i = 0; i < all.Count; i++)
                    for (int j = i + 1; j < all.Count; j++)
                        AvirFactionRelations.Apply(all[i], all[j]);
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] game-start faction relation pass failed: " + e.Message);
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref applied, "Avir_relationsApplied", false);
        }
    }
}
