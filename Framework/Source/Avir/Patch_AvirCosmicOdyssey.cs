using System;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// INTEGRATION: The Cosmic Odyssey (Akarthus.TheCosmicOdyssey.Core). Self-disabling.
    ///
    /// A Strider's whole trade is fighting at a range no beak will ever reach, and the nesting
    /// does not turn down a better bow just because it happens to be a rail gun shaped like
    /// one. When The Cosmic Odyssey is installed, the elven bow pool is retired for this caste
    /// in favour of the Artemis - "ALE_warbow" - a gauss-rail warbow.
    ///
    /// Under Combat Extended the Artemis needs ammunition, so the Strider is also handed a
    /// spare pack of 75 Artemis arrows to carry loose in inventory. CE reloads a pawn's gun
    /// from their own carried ammo on its own; nothing further needs forcing by hand. Without
    /// CE installed the Artemis is a normal magazine-less vanilla gun and needs no ammo at all,
    /// so the pack is skipped entirely in that case.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirCosmicOdyssey
    {
        public static readonly bool Active;

        private static readonly bool CEActive;
        private static readonly ThingDef Warbow;
        private static readonly ThingDef Arrows;

        static AvirCosmicOdyssey()
        {
            Warbow = DefDatabase<ThingDef>.GetNamedSilentFail("ALE_warbow");
            Arrows = DefDatabase<ThingDef>.GetNamedSilentFail("Ammo_ArtemisArrows");
            CEActive = ModsConfig.IsActive("CETeam.CombatExtended");

            Active = Warbow != null;

            if (Active)
            {
                Log.Message("[Avir] The Cosmic Odyssey detected - Striders will carry the Artemis warbow"
                    + (CEActive && Arrows != null ? " with a pack of Artemis arrows." : "."));
            }
        }

        /// <summary>
        /// Arms a Strider with the Artemis instead of an elven bow. Returns false and does
        /// nothing if The Cosmic Odyssey is not installed - the caller then falls back to the
        /// normal elven bow pool.
        /// </summary>
        public static bool TryArmStrider(Pawn pawn)
        {
            try
            {
                if (!Active || pawn == null || pawn.equipment == null) return false;

                ThingWithComps gun = (ThingWithComps)ThingMaker.MakeThing(Warbow, null);

                CompQuality q = gun.TryGetComp<CompQuality>();
                if (q != null) q.SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Outsider);

                AvirGearUtil.Weather(gun);
                pawn.equipment.AddEquipment(gun);

                // A pack of 75 arrows, loose in inventory. CE finds and loads compatible ammo
                // from a pawn's own carried stock on its own - only relevant with CE running,
                // since the vanilla Artemis needs no ammunition at all.
                if (CEActive && Arrows != null && pawn.inventory != null)
                {
                    Thing arrows = ThingMaker.MakeThing(Arrows);
                    arrows.stackCount = Arrows.stackLimit > 0 ? Math.Min(75, Arrows.stackLimit) : 75;
                    pawn.inventory.innerContainer.TryAdd(arrows);
                }

                return true;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] The Cosmic Odyssey integration failed: " + e, 0x4A51C7);
                return false;
            }
        }
    }
}
