using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// INTEGRATION: W-corp warp weapons (Kilo.LCA.Wcorp), relabelled into Avir lore.
    ///
    /// Self-disabling: every def is looked up by name at startup. No mod, no defs, Active is
    /// false, and the Avir go back to essentium and tisserium as if this never existed.
    ///
    /// These are OPTIONAL weapons - a talon carries one or carries an elven blade, and both
    /// are respectable. They are offered to the castes that have earned them:
    ///
    ///   Predator      40%   (otherwise the tisserium greataxe)
    ///   Huntress      30%
    ///   Warrior       20%
    ///   Legionnaire   45%   (A'Schialis only - Amber rack, see below)
    ///
    /// THE CENTURION IS DELIBERATELY ABSENT and returns 0%. It is the Legion's line unit - the
    /// rank you are issued, not the rank you are given folded light for. A centurion carries a
    /// Fyrlance or an essentium spear and nothing else, and the day an amber glaive turns up in
    /// a centurion's hands is the day the rank above him stops meaning anything. Do not add it.
    ///
    /// GLAIVES ARE THE FAVOURITE, by a wide margin - listed four times against everything
    /// else's one. It is the reach-weapon of the stoop: an Avir comes down out of the sky and
    /// the glaive arrives first, at the end of a wing-length of folded light. Of course they
    /// prefer it.
    ///
    /// The rod is deliberately absent from the pool. The artificers insist it has uses. No
    /// talon has ever asked what they are.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirWarpBlades
    {
        public static readonly bool Active;

        // Six racks. The blade is matched to the cloak the pawn is wearing.
        //
        //   BLOOD palette (138,0,0)      -> Bloodhymn (Scarlet). Nothing else. A bloodhymn glaive
        //                                   over a blood-red cloak, and the whole silhouette is
        //                                   one colour.
        //   LILAC palette (160,146,199)  -> Duskwhisper (Lilac) only. Duskglass used to ride
        //                                   along here too, but the two don't actually match on
        //                                   the model - Duskglass reads black-and-silver, not
        //                                   pale violet - so it moved to Midnight/Black below.
        //   MIDNIGHT palette (28,55,72)  -> Wardlight (Teal) AND Duskglass (Purple). Dark
        //                                   blue-black sits correctly against both.
        //   TEAL palette (42,110,120)    -> Wardlight (Teal) only.
        //   BLACK palette (18,18,20)     -> Duskglass (Purple) only. The actual black-and-silver
        //                                   match.
        //   NO palette (Barbarians plate, or anything unmatched) -> the "any" rack: Purple,
        //                                   Teal, Scarlet and Lilac all sit here as a fallback.
        //                                   Amber is NOT in this rack - it is reserved for the
        //                                   A'Schialis Legion (below), and the A'Shaign already
        //                                   have enough colours of their own.
        //   LEGIONNAIRE (A'Schialis)     -> Amber, exclusively. This is what makes the Legion's
        //                                   optional warp blade look distinct at a glance.
        private static readonly List<ThingDef> PoolBlood = new List<ThingDef>();
        private static readonly List<ThingDef> PoolLilac = new List<ThingDef>();
        private static readonly List<ThingDef> PoolMidnight = new List<ThingDef>();
        private static readonly List<ThingDef> PoolTeal = new List<ThingDef>();
        private static readonly List<ThingDef> PoolBlack = new List<ThingDef>();
        private static readonly List<ThingDef> PoolAny = new List<ThingDef>();
        private static readonly List<ThingDef> PoolAmber = new List<ThingDef>();

        private static readonly string[] Colours = { "", "_Amber", "_Teal", "_Scarlet", "_Lilac" };

        static AvirWarpBlades()
        {
            foreach (string c in Colours)
            {
                List<List<ThingDef>> racks = new List<List<ThingDef>>();

                // Amber goes ONLY to the Legion rack - it no longer feeds the generic "any" rack
                // that A'Shaign pawns with no matched palette draw from.
                if (c == "_Amber") racks.Add(PoolAmber);
                else racks.Add(PoolAny);

                if (c == "_Scarlet") racks.Add(PoolBlood);
                if (c == "_Lilac") racks.Add(PoolLilac);
                if (c == "" || c == "_Teal") racks.Add(PoolMidnight);  // "" is Purple/Duskglass
                if (c == "_Teal") racks.Add(PoolTeal);
                if (c == "") racks.Add(PoolBlack);

                foreach (List<ThingDef> rack in racks)
                {
                    // The glaive, four times over. It is what an Avir reaches for.
                    Add(rack, "KWC_WarpPolearm" + c);
                    Add(rack, "KWC_WarpPolearm" + c);
                    Add(rack, "KWC_WarpPolearm" + c);
                    Add(rack, "KWC_WarpPolearm" + c);

                    Add(rack, "KWC_WarpSword" + c);
                    Add(rack, "KWC_WarpAxe" + c);
                    Add(rack, "KWC_WarpKnife" + c);
                    Add(rack, "KWC_WarpDagger" + c);

                    // KWC_WarpBaton is NOT added. See above.
                }
            }

            Active = PoolAny.Count > 0;

            if (Active)
                Log.Message("[Avir] W-corp warp weapons detected - racks: blood=" + PoolBlood.Count
                    + " lilac=" + PoolLilac.Count + " midnight=" + PoolMidnight.Count
                    + " teal=" + PoolTeal.Count + " black=" + PoolBlack.Count
                    + " any=" + PoolAny.Count + " amber(legion)=" + PoolAmber.Count
                    + " (glaives weighted heavily).");
        }

        private static void Add(List<ThingDef> rack, string defName)
        {
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (d != null) rack.Add(d);
        }

        /// <summary>The chance this caste reaches for folded light instead of forged metal.</summary>
        public static float ChanceFor(string kind)
        {
            if (kind == "Avir_Predator")    return 0.40f;
            if (kind == "Avir_Huntress")    return 0.30f;
            if (kind == "Avir_Warrior")     return 0.20f;
            // 45/55 split between the amber rack and the ordinary essentium rack. The Legionnaire
            // is the rank the amber blade is actually FOR, so it was underweighted at 25%.
            if (kind == "Avir_Legionnaire") return 0.45f;   // A'Schialis only - see PoolAmber.
            if (kind == "Avir_Inquisitor")  return 0.75f;   // A'Schialis only - see PoolAmber.
            if (kind == "Avir_Highlord")    return 1.00f;   // A'Schialis only - see PoolAmber.
            return 0f;
        }

        /// <summary>
        /// THE BUG THIS REPLACES.
        ///
        /// Legion membership used to be decided from the PAWNKIND defName alone - Legionnaire,
        /// Inquisitor, Highlord. That works for the ranks the Legion does not share. It fails
        /// completely for the ones it does.
        ///
        /// Avir_Huntress, Avir_Predator and Avir_Warrior are used by BOTH factions. An
        /// A'Schialis huntress therefore failed the rank test, fell through to the else-branch,
        /// and drew from PoolAny - the A'Shaign racks. That is how a huntress of A'Schialis ends
        /// up holding a Bloodhymn Glaive, which is a Flight weapon in Flight colours, sworn
        /// against her own faction.
        ///
        /// FACTION IS THE ANSWER, not pawnkind. A legionnaire is a legionnaire because of who
        /// she serves, not because of which job she holds. The kind list is kept as a fallback
        /// for the ranks that only ever exist in the Legion, because a pawn can be generated
        /// with a null faction - quest pawns, world pawns, dev-spawned - and those ranks are
        /// still unambiguous on their own.
        /// </summary>
        public static bool IsLegion(Pawn pawn)
        {
            if (pawn == null) return false;

            // Primary test: who does she serve.
            if (AvirDefOf.Avir_AShialis != null
                && pawn.Faction != null
                && pawn.Faction.def == AvirDefOf.Avir_AShialis) return true;

            // Fallback for faction-less generation: ranks that exist ONLY in the Legion.
            string kind = pawn.kindDef != null ? pawn.kindDef.defName : "";
            return kind == "Avir_Legionnaire" || kind == "Avir_Inquisitor"
                || kind == "Avir_Highlord" || kind == "Avir_Centurion";
        }

        /// <summary>
        /// Tries to arm the pawn with a warp weapon. Returns false if it didn't - the caller
        /// then falls back to the normal elven weapon.
        /// </summary>
        public static bool TryArm(Pawn pawn, bool masterwork, string palette)
        {
            if (!Active || pawn == null || pawn.equipment == null) return false;

            string kind = pawn.kindDef != null ? pawn.kindDef.defName : "";
            if (!Rand.Chance(ChanceFor(kind))) return false;

            List<ThingDef> rack;
            if (IsLegion(pawn))
            {
                // Every A'Schialis pawn that rolls a warp blade draws ONLY from the Amber rack -
                // never Blood/Lilac/any, and regardless of which rank she holds. If Amber items
                // aren't present for some reason, the pawn simply doesn't get a warp blade this
                // roll (falls back to the normal elven weapon), rather than borrowing a colour
                // that belongs to the A'Shaign.
                if (PoolAmber.Count == 0) return false;
                rack = PoolAmber;
            }
            else
            {
                rack = PoolAny;
                if (palette == "blood" && PoolBlood.Count > 0)          rack = PoolBlood;
                else if (palette == "lilac" && PoolLilac.Count > 0)     rack = PoolLilac;
                else if (palette == "midnight" && PoolMidnight.Count > 0) rack = PoolMidnight;
                else if (palette == "teal" && PoolTeal.Count > 0)       rack = PoolTeal;
                else if (palette == "black" && PoolBlack.Count > 0)     rack = PoolBlack;
            }

            ThingDef def = rack[Rand.Range(0, rack.Count)];

            // These are not made from stuff - they are their own thing.
            ThingWithComps w = (ThingWithComps)ThingMaker.MakeThing(
                def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);

            CompQuality q = w.TryGetComp<CompQuality>();
            if (q != null)
            {
                QualityCategory cat;
                if (masterwork) cat = QualityCategory.Masterwork;
                else
                {
                    float r = Rand.Value;
                    cat = r < 0.45f ? QualityCategory.Good
                        : r < 0.85f ? QualityCategory.Excellent
                        : QualityCategory.Masterwork;
                }
                q.SetQuality(cat, ArtGenerationContext.Outsider);
            }

            AvirGearUtil.Weather(w);
            pawn.equipment.AddEquipment(w);
            return true;
        }

        // ---------------------------------------------------------------------------------
        // CORRECTING WHAT IS ALREADY WRONG.
        //
        // Fixing TryArm only fixes pawns generated from now on. Every A'Schialis pawn already
        // walking around in an existing save is still holding a Flight blade. The corrector
        // below swaps the COLOUR while keeping the weapon type and its quality - a huntress who
        // rolled a glaive keeps her glaive, it just stops being scarlet.
        //
        // Deliberately NOT destroying and re-rolling: that would take a masterwork off a pawn
        // and hand back a good, which is a silent downgrade the player never asked for.
        // ---------------------------------------------------------------------------------

        private static readonly string[] Bases =
        {
            "KWC_WarpPolearm", "KWC_WarpSword", "KWC_WarpAxe",
            "KWC_WarpKnife", "KWC_WarpDagger", "KWC_WarpBaton", "KWC_WarpRod"
        };

        /// <summary>Splits a warp weapon defName into (base, colour suffix). Null if not one.</summary>
        private static bool Split(ThingDef def, out string base_, out string colour)
        {
            base_ = null;
            colour = null;
            if (def == null || def.defName == null) return false;

            for (int i = 0; i < Bases.Length; i++)
            {
                string b = Bases[i];
                if (!def.defName.StartsWith(b, StringComparison.Ordinal)) continue;

                string rest = def.defName.Substring(b.Length);
                for (int c = 0; c < Colours.Length; c++)
                {
                    if (rest != Colours[c]) continue;
                    base_ = b;
                    colour = Colours[c];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// If this pawn is holding a warp blade in the other faction's colours, recolour it in
        /// place. Returns true if something was changed.
        /// </summary>
        public static bool CorrectColour(Pawn pawn)
        {
            if (!Active || pawn?.equipment == null) return false;

            ThingWithComps primary = pawn.equipment.Primary;
            if (primary == null) return false;

            string base_, colour;
            if (!Split(primary.def, out base_, out colour)) return false;

            bool legion = IsLegion(pawn);
            bool amber = colour == "_Amber";

            // Legion holding Amber, or Flight holding anything but Amber: already correct.
            if (legion == amber) return false;

            string wantColour;
            if (legion)
            {
                wantColour = "_Amber";
            }
            else
            {
                // A Flight pawn holding Amber. Any other colour will do - the palette that
                // originally dressed her is long gone by the time this runs, so pick at random
                // from what actually exists rather than guessing.
                wantColour = Colours[Rand.Range(1, Colours.Length)];
                if (wantColour == "_Amber") wantColour = "";
            }

            ThingDef want = DefDatabase<ThingDef>.GetNamedSilentFail(base_ + wantColour);
            if (want == null || want == primary.def) return false;

            // Carry the quality across. Losing it here would be a downgrade by accident.
            QualityCategory quality = QualityCategory.Normal;
            bool hadQuality = false;
            CompQuality oldQ = primary.TryGetComp<CompQuality>();
            if (oldQ != null)
            {
                quality = oldQ.Quality;
                hadQuality = true;
            }

            ThingWithComps replacement = (ThingWithComps)ThingMaker.MakeThing(
                want, want.MadeFromStuff ? (primary.Stuff ?? GenStuff.DefaultStuffFor(want)) : null);

            CompQuality newQ = replacement.TryGetComp<CompQuality>();
            if (newQ != null && hadQuality) newQ.SetQuality(quality, ArtGenerationContext.Outsider);

            replacement.HitPoints = Mathf.Max(1, Mathf.RoundToInt(
                replacement.MaxHitPoints * ((float)primary.HitPoints / Mathf.Max(1, primary.MaxHitPoints))));

            // DestroyEquipment does the unequip and the destroy in one call, in the order the
            // tracker expects. Doing it by hand invites a primary slot that still points at a
            // destroyed thing.
            pawn.equipment.DestroyEquipment(primary);
            pawn.equipment.AddEquipment(replacement);

            return true;
        }
    }

    /// <summary>
    /// Runs the warp blade colour corrector when a pawn spawns.
    ///
    /// RESTRICTED TO NON-PLAYER PAWNS ON PURPOSE. Raiders, visitors, traders and caravan guards
    /// are generated fresh and nobody has any attachment to what they carry, so correcting them
    /// is free. A weapon in the player's own colony is the player's property - it may have been
    /// traded for, looted, named, or handed over deliberately - and quietly swapping it out from
    /// under them on map load is not a bug fix, it is a theft with good intentions.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_AvirWarpBladeColourOnSpawn
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null || __instance.Dead) return;
                if (__instance.Faction != null && __instance.Faction.IsPlayer) return;
                if (!AvirFaction.IsAvirPawnKind(__instance)) return;

                AvirWarpBlades.CorrectColour(__instance);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Warp blade colour correction failed: " + e, 0x5B12AB);
            }
        }
    }
}
