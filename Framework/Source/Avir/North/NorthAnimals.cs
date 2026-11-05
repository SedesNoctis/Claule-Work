using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.North
{
    // =====================================================================================
    // BEASTS OF OTHER MODS IN THE NORTH. Added in code, not by XML patch: a beast whose def is not loaded (its mod is
    // off, or the mod's own settings removed it) is simply skipped - an XML reference to a missing PawnKindDef leaves
    // a null record in the biome and breaks the biome's checks.
    // =====================================================================================
    [StaticConstructorOnStartup]
    public static class NorthAnimals
    {
        private const string Waste = "Avir_FrozenWaste", Oasis = "Avir_FrostOasis", Dusk = "Avir_TwilightForest", Burn = "Avir_BurningForest";

        private static readonly (string biome, string kind, float commonality)[] Beasts =
        {
            // Odyssey
            (Oasis, "Moose", 0.3f), (Oasis, "Wolf_Great", 0.04f), (Oasis, "Wolverine", 0.08f), (Oasis, "Mink", 0.15f), (Oasis, "Porcupine", 0.1f),
            (Waste, "Wolf_Great", 0.02f), (Waste, "Wolverine", 0.03f),
            // Medieval Overhaul
            (Oasis, "DankPyon_Daer", 0.4f), (Oasis, "DankPyon_NorthernBoar", 0.3f), (Oasis, "DankPyon_DireBoar", 0.08f),
            (Oasis, "DankPyon_Direwolf", 0.06f), (Oasis, "DankPyon_Rox", 0.15f),
            (Waste, "DankPyon_Daer", 0.3f), (Waste, "DankPyon_Direwolf", 0.03f), (Waste, "DankPyon_Rox", 0.12f), (Waste, "DankPyon_UnholdSnow", 0.01f),
            // Alpha Animals
            (Oasis, "AA_FrostAve", 0.3f), (Oasis, "AA_GreyCoatedMouflon", 0.12f), (Oasis, "AA_Murkling", 0.15f), (Oasis, "AA_ChameleonYak", 0.08f),
            (Oasis, "AA_CrystalMit", 0.05f), (Oasis, "AA_CrystallineCaracal", 0.03f), (Oasis, "AA_AuroraSylph", 0.03f), (Oasis, "AA_Nightling", 0.05f),
            (Oasis, "AA_FrostLynx", 0.03f), (Oasis, "AA_ArcticLion", 0.03f),
            (Waste, "AA_Frostmite", 0.3f), (Waste, "AA_FrostAve", 0.2f), (Waste, "AA_Blizzarisk", 0.04f), (Waste, "AA_PebbleMit", 0.06f),
            (Waste, "AA_BoulderMit", 0.05f), (Waste, "AA_ArcticLion", 0.02f), (Waste, "AA_FrostLynx", 0.02f), (Waste, "AA_Radyak", 0.02f),
            // Dark Ages: Beasts and Monsters (trolls also come by their own events - Patches/Avir_DarkAges.xml)
            (Oasis, "DA_DwarvenMuffton", 0.4f), (Oasis, "DA_NorthernDrog", 0.12f), (Oasis, "DA_BeardedTroll", 0.08f), (Oasis, "DA_ArcticOwlcat", 0.08f),
            (Oasis, "DA_Crestel", 0.06f), (Oasis, "DA_ForestOwlcat", 0.06f), (Oasis, "DA_Taraal", 0.05f), (Oasis, "DA_SnowTaraal", 0.05f), (Oasis, "DA_RedhornedLarpah", 0.005f),
            (Waste, "DA_DwarvenMuffton", 0.2f), (Waste, "DA_ArcticOwlcat", 0.12f), (Waste, "DA_SnowTaraal", 0.08f), (Waste, "DA_NorthernDrog", 0.06f),
            (Waste, "DA_BeardedTroll", 0.05f), (Waste, "DA_Crestel", 0.04f),

            // THE TWILIGHT FOREST - gentle and shining: the quiet, the bright and the strange; no horrors, no flesh, no swarms.
            // Odyssey
            (Dusk, "Moose", 0.15f), (Dusk, "Mink", 0.12f), (Dusk, "Porcupine", 0.1f), (Dusk, "Wolverine", 0.03f),
            // Medieval Overhaul
            (Dusk, "DankPyon_Daer", 0.35f), (Dusk, "DankPyon_NorthernBoar", 0.2f), (Dusk, "DankPyon_Direwolf", 0.03f),
            // Vanilla Animals Expanded
            (Dusk, "AEXP_Hedgehog", 0.6f), (Dusk, "AEXP_WildGoose", 0.4f), (Dusk, "AEXP_Otter", 0.3f), (Dusk, "AEXP_Beaver", 0.2f),
            (Dusk, "AEXP_RedPanda", 0.15f), (Dusk, "AEXP_Badger", 0.15f), (Dusk, "AEXP_Moose", 0.15f), (Dusk, "AEXP_Porcupine", 0.1f),
            (Dusk, "AEXP_BlackBear", 0.08f),
            // Alpha Animals
            (Dusk, "AA_MeadowAve", 0.3f), (Dusk, "AA_Murkling", 0.2f), (Dusk, "AA_Groundrunner", 0.15f), (Dusk, "AA_Gigantelope", 0.1f),
            (Dusk, "AA_Aerofleet", 0.1f), (Dusk, "AA_Nightling", 0.06f), (Dusk, "AA_ChameleonYak", 0.06f), (Dusk, "AA_Wildpawn", 0.06f),
            (Dusk, "AA_AngelMoth", 0.05f), (Dusk, "AA_AngelMothLarva", 0.04f), (Dusk, "AA_DuskProwler", 0.05f), (Dusk, "AA_Thunderox", 0.05f),
            (Dusk, "AA_FissionMouse", 0.05f), (Dusk, "AA_PebbleMit", 0.05f), (Dusk, "AA_Bumbledrone", 0.05f), (Dusk, "AA_AuroraSylph", 0.04f),
            (Dusk, "AA_RayHound", 0.04f), (Dusk, "AA_ShockGoat", 0.04f), (Dusk, "AA_Mantrap", 0.03f), (Dusk, "AA_EmpressButterfly", 0.02f),
            (Dusk, "AA_EmpressButterflyLarva", 0.015f), (Dusk, "AA_PhoenixOwlcat", 0.02f), (Dusk, "AA_CrystalMit", 0.02f), (Dusk, "AA_Thunderbeast", 0.02f),
            (Dusk, "AA_LuciferBug", 0.01f), (Dusk, "AA_BumbledroneHierophant", 0.008f), (Dusk, "AA_OvergrownColossus", 0.006f),
            (Dusk, "AA_GiantCrownedSilkie", 0.005f), (Dusk, "AA_AnimaColossus", 0.004f), (Dusk, "AA_WindBeast", 0.002f),
            // THE BURNING FOREST (besides all of the twilight forest's): beasts of fire and lava
            (Burn, "LavaSnail", 0.08f), (Burn, "AA_Cinderlisk", 0.08f), (Burn, "AA_Thermadon", 0.04f), (Burn, "AA_PhoenixOwlcat", 0.03f),
            // Dark Ages
            (Dusk, "DA_ForestOwlcat", 0.2f), (Dusk, "DA_DwarvenMuffton", 0.15f), (Dusk, "DA_Taraal", 0.12f), (Dusk, "DA_NorthernDrog", 0.05f),
            (Dusk, "DA_BeardedTroll", 0.03f), (Dusk, "DA_Crestel", 0.03f), (Dusk, "DA_RedhornedLarpah", 0.01f),
        };

        private static readonly AccessTools.FieldRef<BiomeDef, List<BiomeAnimalRecord>> wildOf =
            AccessTools.FieldRefAccess<BiomeDef, List<BiomeAnimalRecord>>("wildAnimals");

        static NorthAnimals()
        {
            int added = 0;
            var all = new List<(string, string, float)>(Beasts);
            foreach (var (b, k, c) in Beasts) if (b == Dusk) all.Add((Burn, k, c));   // the burning forest: the same beasts
            foreach (var (b, k, c) in all)
            {
                BiomeDef biome = DefDatabase<BiomeDef>.GetNamedSilentFail(b);
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(k);
                if (biome == null || kind == null) continue;
                List<BiomeAnimalRecord> wild = wildOf(biome) ?? (wildOf(biome) = new List<BiomeAnimalRecord>());
                if (wild.Exists(r => r.animal == kind)) continue;
                wild.Add(new BiomeAnimalRecord { animal = kind, commonality = c });
                added++;
            }
            if (added > 0) Log.Message("[Avir] the north: " + added + " beasts of other mods settled in the Avir biomes.");
        }
    }
}
