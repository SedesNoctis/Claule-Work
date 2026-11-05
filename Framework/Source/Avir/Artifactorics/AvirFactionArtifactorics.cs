using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using RimWorld.Planet;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // ARTIFACTORICS IN THE AVIR FACTIONS (Oct 4, 2026)
    //
    // Called after the faction wardrobe (Patch_AvirElvesGear: Dress + Arm, or the Barbarian
    // fallback, and the Hospitality redress) - the clothes and weapon must already be there.
    // Every pawn of an Avir faction listed in an AvirFactionGearDef is generated with:
    //   - a neck piece: a named amulet (by chance, from its kind's list) or a traditional amulet
    //     (A'Shaign: silver + essentium/tisserium, every path; A'Schialis: gold + essentium, a caste);
    //   - an artifact, always: inlaid into a torso garment if it wears one, otherwise into any
    //     other garment, otherwise into its weapon; a naked, unarmed Avir carries it in its
    //     inventory.
    // How far both are developed depends on the pawn's status (per kind), age and learning
    // rate. The amulet's stones are spread from the main path to its neighbours; the artifact's
    // cells over the kind's preferred arms (exclusion groups respected).
    // Bonuses work as on any colonist: they are read from the worn amulet / socket.
    //
    // The artificer caravans (TraderKindDef with AvirSubspaceCaravan) carry their whole stock in
    // the trader's subspace storage: no pack animals.
    // =====================================================================================

    public class AvirGearKind
    {
        public PawnKindDef kind;
        /// <summary>0..1: how high the pawn stands. The main weight of its development.</summary>
        public float status = 0.3f;
        /// <summary>Main path of its traditional amulet (A'Shaign: main path; A'Schialis: also the caste).</summary>
        public AmuletSubclassDef path;
        /// <summary>Named amulets this kind may wear instead of a traditional one.</summary>
        public List<ThingDef> named = new List<ThingDef>();
        public float namedChance = 0.25f;
        /// <summary>Preferred armour arms, in order.</summary>
        public List<ArtifactArmDef> arms = new List<ArtifactArmDef>();
        /// <summary>Preferred weapon arms, in order (the weapon's own artifact).</summary>
        public List<ArtifactArmDef> weaponArms = new List<ArtifactArmDef>();
        /// <summary>Chance the weapon artifact carries a burning spear stone (only in the weapons
        /// the stone allows - the glaives). Null - the faction's default.</summary>
        public float spearChance = -1f;
    }

    public class AvirFactionGearDef : Def
    {
        public FactionDef faction;
        public ArtifactMetalDef necklace;
        public List<ArtifactMetalDef> cores = new List<ArtifactMetalDef>();
        public List<ArtifactMetalDef> rims = new List<ArtifactMetalDef>();
        public List<ArtifactMetalDef> hearts = new List<ArtifactMetalDef>();
        /// <summary>Stones of a fully developed pawn's amulet (development 1).</summary>
        public int amuletStonesMax = 50;
        /// <summary>Cells of a fully developed pawn's artifact (development 1).</summary>
        public int artifactCellsMax = 18;
        /// <summary>Glaives: chance their artifact carries a burning spear stone (used on its own in a fight).</summary>
        public float spearChance = 0.75f;
        /// <summary>The spear energies the faction uses (empty - any).</summary>
        public List<SpearEnergyDef> spearEnergies = new List<SpearEnergyDef>();
        /// <summary>Development: status x statusWeight + age x ageWeight, then x learning rate.</summary>
        public float statusWeight = 0.65f;
        public float ageWeight = 0.35f;
        /// <summary>Age (biological years) at which the age part is full.</summary>
        public float fullAge = 60f;
        public float minAge = 14f;
        public List<AvirGearKind> kinds = new List<AvirGearKind>();

        public static AvirFactionGearDef For(Faction f)
        {
            if (f?.def == null) return null;
            foreach (AvirFactionGearDef d in DefDatabase<AvirFactionGearDef>.AllDefsListForReading)
                if (d.faction == f.def) return d;
            return null;
        }

        public AvirGearKind KindFor(PawnKindDef k) => kinds.FirstOrDefault(x => x.kind == k);
    }

    public static class AvirFactionGear
    {
        /// <summary>0..1, from status, age and learning rate, with a little chance in it.</summary>
        public static float Development(Pawn p, AvirFactionGearDef d, AvirGearKind k)
        {
            float age = p.ageTracker?.AgeBiologicalYearsFloat ?? d.minAge;
            float ageF = Mathf.Clamp01((age - d.minAge) / Mathf.Max(1f, d.fullAge - d.minAge));
            float learn = Mathf.Clamp(p.GetStatValue(StatDefOf.GlobalLearningFactor), 0.5f, 1.75f);
            float dev = (k.status * d.statusWeight + ageF * d.ageWeight) * learn * (AvirMod.Settings?.npcStrength ?? 0.5f);
            return Mathf.Clamp01(dev * Rand.Range(0.85f, 1.15f));
        }

        private static QualityCategory QualityFor(float dev)
        {
            if (dev > 0.85f && Rand.Chance(0.15f)) return QualityCategory.Legendary;
            int q = Mathf.Clamp(Mathf.RoundToInt(2f + dev * 3f + Rand.Range(-1f, 1f)), 1, 5);
            return (QualityCategory)q;
        }

        public static void Equip(Pawn p)
        {
            if (p?.apparel == null || p.Dead || !p.RaceProps.Humanlike) return;
            AvirFactionGearDef d = AvirFactionGearDef.For(p.Faction);
            if (d == null || p.Faction.IsPlayer) return;
            AvirGearKind k = d.KindFor(p.kindDef);
            if (k == null) return;
            float dev = Development(p, d, k);
            try { GiveNeckPiece(p, d, k, dev); }
            catch (System.Exception e) { Log.Warning("[Avir] faction amulet for " + p + ": " + e.Message); }
            try { GiveArtifact(p, d, k, Development(p, d, k)); }
            catch (System.Exception e) { Log.Warning("[Avir] faction artifact for " + p + ": " + e.Message); }
            try { GiveWeaponArtifact(p, d, k, Development(p, d, k)); }
            catch (System.Exception e) { Log.Warning("[Avir] faction weapon artifact for " + p + ": " + e.Message); }
        }

        // ---------------------------------------------------------------- amulets

        private static void GiveNeckPiece(Pawn p, AvirFactionGearDef d, AvirGearKind k, float dev)
        {
            // something already sits on the neck (another mod's gear) - leave it
            if (p.apparel.WornApparel.Any(a => a.def.apparel.layers.Contains(AvirAmuletLayerDefOf.Avir_AccessoryLayer))) return;
            Apparel piece;
            if (k.named.Count > 0 && Rand.Chance(k.namedChance))
            {
                ThingDef def = k.named.RandomElement();
                piece = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                // a named amulet is a traditional one remade: same metals, quality and development
                CompAvirAmulet na = piece.TryGetComp<CompAvirAmulet>();
                if (na != null)
                {
                    QualityCategory q = QualityFor(dev);
                    na.necklaceQuality = q; na.coreQuality = q;
                    ArtifactMetalDef core = d.cores.Where(m => m != null && AvirAmuletCraft.CoreBlockReason(na.necklaceMetal, m) == null).RandomElementWithFallback();
                    if (core != null) na.coreMetal = core;
                    if (na.Props.fixedMain == null && k.path != null && AmuletDevelopment.Ring(na).Contains(k.path)) na.subclass = k.path;
                    na.ApplyNamedRules();
                    Develop(na, Mathf.RoundToInt(dev * d.amuletStonesMax));
                }
            }
            else
            {
                piece = MakeAmulet(d.necklace, d.cores, k.path, QualityFor(dev));
                if (piece == null) return;
                Develop(piece.TryGetComp<CompAvirAmulet>(), Mathf.RoundToInt(dev * d.amuletStonesMax));
            }
            if (!ApparelUtility.HasPartsToWear(p, piece.def)) return;
            p.apparel.Wear(piece, false, false);
        }

        /// <summary>A finished, undeveloped traditional amulet. path: the A'Shaign main path / the
        /// caste's subclass (its caste follows); null - chosen later (A'Shaign) or a random caste.</summary>
        public static Apparel MakeAmulet(ArtifactMetalDef necklace, List<ArtifactMetalDef> cores, AmuletSubclassDef path, QualityCategory q)
        {
            if (necklace == null) return null;
            var t = (Apparel)ThingMaker.MakeThing(AvirAmuletDefOf.Avir_TraditionalAmulet);
            CompAvirAmulet a = t.TryGetComp<CompAvirAmulet>();
            if (a == null) return null;
            a.necklaceMetal = necklace;
            List<ArtifactMetalDef> ok = cores.Where(m => m != null && AvirAmuletCraft.CoreBlockReason(necklace, m) == null).ToList();
            a.coreMetal = ok.Count > 0 ? ok.RandomElement() : a.coreMetal;
            a.necklaceQuality = q;
            a.coreQuality = q;
            t.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
            if (necklace.MulticlassWith(a.coreMetal)) { a.caste = null; a.subclass = path; }
            else
            {
                a.caste = path?.caste ?? AmuletCasteDef.All.RandomElementWithFallback();
                a.subclass = path;
            }
            return t;
        }

        /// <summary>Spreads n stones: the main path first, then its neighbours outward, unevenly.</summary>
        public static void Develop(CompAvirAmulet a, int n)
        {
            if (a == null || n <= 0) return;
            AmuletSettingsDef S = AmuletSettingsDef.Main;
            // NPC amulets only (this is the faction gear): never past the settings' deepest path
            int cap = Mathf.Min(S?.stonesPerBranch ?? 18, AvirMod.Settings?.npcBranchCap ?? 12);
            if (a.subclass == null)
            {
                List<AmuletSubclassDef> all = AmuletDevelopment.Ring(a);
                if (all.Count == 0) return;
                a.subclass = all.RandomElement();
            }
            if (a.opened == null) a.opened = new Dictionary<AmuletSubclassDef, int>();
            List<AmuletSubclassDef> ring = AmuletDevelopment.Ring(a);
            int i0 = Mathf.Max(0, ring.IndexOf(a.subclass));
            // main path: most of it
            int main = Mathf.Min(cap, n, Mathf.Max(Mathf.Min(n, 3), Mathf.RoundToInt(n * Rand.Range(0.5f, 0.75f))));
            a.opened[a.subclass] = main;
            a.slotOpen = main >= 3;   // faction pawns: the slot is awake once the main path has some depth
            int left = n - main;
            // neighbours, nearer first, alternating sides
            for (int step = 1; left > 0 && step <= ring.Count; step++)
            {
                foreach (int side in Rand.Bool ? new[] { 1, -1 } : new[] { -1, 1 })
                {
                    if (left <= 0) break;
                    AmuletSubclassDef b = ring[((i0 + side * step) % ring.Count + ring.Count) % ring.Count];
                    int have = a.opened.TryGetValue(b, out int h) ? h : 0;
                    int room = cap - have;
                    if (room <= 0) continue;
                    int give = Mathf.Min(room, left, Mathf.Max(1, Mathf.RoundToInt(left * Rand.Range(0.35f, 0.7f))));
                    a.opened[b] = have + give;
                    left -= give;
                }
            }
            // whatever is left tops up the main path and its neighbours
            for (int guard = 0; left > 0 && guard < 200; guard++)
            {
                AmuletSubclassDef b = ring.Where(x => (a.opened.TryGetValue(x, out int v) ? v : 0) < cap).RandomElementWithFallback();
                if (b == null) break;
                a.opened[b] = (a.opened.TryGetValue(b, out int v2) ? v2 : 0) + 1;
                left--;
            }
            a.spentLedger = -1;   // generated, not bought: counted from what is open
        }

        // ---------------------------------------------------------------- artifacts

        private static void GiveArtifact(Pawn p, AvirFactionGearDef d, AvirGearKind k, float dev)
        {
            int cells = Mathf.Max(1, Mathf.RoundToInt(dev * d.artifactCellsMax));
            ArtifactState st = MakeState(d, QualityFor(dev));
            if (st == null) return;

            // 1. a torso garment, the outermost first; 2. any other garment; 3. the weapon
            List<Apparel> worn = p.apparel.WornApparel
                .Where(a => a.TryGetComp<CompAvirAmulet>() == null && !a.def.apparel.layers.Contains(AvirAmuletLayerDefOf.Avir_AccessoryLayer)
                            && a.Socket() != null && !a.Socket().HasArtifact)
                .OrderByDescending(a => a.def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso))
                .ThenByDescending(a => a.def.apparel.LastLayer?.drawOrder ?? 0)
                .ToList();
            if (worn.Count > 0)
            {
                Fill(st, k.arms, ArtifactCarrier.Apparel, cells);
                worn[0].Socket().inlaid = st;
                return;
            }
            // naked and unarmed (an Avir fights with its beak): it carries the artifact
            if (p.inventory == null || p.inventory.innerContainer.Any(t => t.def == ArtifactoricsDefOf.Avir_Artifact)) return;
            Thing item = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_Artifact);
            CompArtifact ca = item.TryGetComp<CompArtifact>();
            if (ca == null) return;
            Fill(st, k.arms, ArtifactCarrier.Apparel, cells);
            ca.state = st;
            p.inventory.innerContainer.TryAdd(item);
        }

        /// <summary>Every weapon of a faction pawn carries its own artifact; a glaive often has a
        /// burning spear stone in it, set to use itself in a fight.</summary>
        private static void GiveWeaponArtifact(Pawn p, AvirFactionGearDef d, AvirGearKind k, float dev)
        {
            ThingWithComps w = p.equipment?.Primary;
            CompArtifactSocket socket = w?.Socket();
            if (socket == null || socket.HasArtifact) return;
            ArtifactState st = MakeState(d, QualityFor(dev));
            if (st == null) return;
            Fill(st, k.weaponArms, ArtifactCarrier.Weapon, Mathf.Max(1, Mathf.RoundToInt(dev * d.artifactCellsMax)));
            ArtifactAbilityDef spear = ArtifactSpearDefOf.ArtifactAbility_BurningSpear;
            float chance = k.spearChance >= 0f ? k.spearChance : d.spearChance;
            if (spear != null && spear.onlyWeapons.Contains(w.def) && Rand.Chance(chance))
            {
                SpearEnergyDef energy = (d.spearEnergies.Count > 0 ? d.spearEnergies : DefDatabase<SpearEnergyDef>.AllDefsListForReading)
                    .Where(e => e?.metal != null).RandomElementWithFallback();
                if (energy != null)
                    st.stone = new AbilityStoneState
                    {
                        ability = spear, metal = energy.metal, energy = energy,
                        quality = QualityFor(dev), autoUse = true
                    };
            }
            socket.inlaid = st;
        }

        private static ArtifactState MakeState(AvirFactionGearDef d, QualityCategory q)
        {
            ArtifactMetalDef rim = d.rims.Where(m => m != null && m.allowedAsRim).RandomElementWithFallback()
                                   ?? ArtifactoricsDatabase.Metals.Where(m => m.allowedAsRim).RandomElementWithFallback();
            ArtifactMetalDef heart = d.hearts.Where(m => m != null && m.allowedAsHeart && m != rim).RandomElementWithFallback()
                                     ?? ArtifactoricsDatabase.Metals.Where(m => m.allowedAsHeart && m != rim).RandomElementWithFallback();
            if (rim == null || heart == null) return null;
            var st = new ArtifactState
            {
                rimMetal = rim, heartMetal = heart,
                settingQuality = q, heartQuality = q,
            };
            _ = st.Uid;
            return st;
        }

        /// <summary>Opens n cells over the preferred arms (9 each), skipping arms another chosen
        /// arm excludes; falls back to any arm of the carrier.</summary>
        private static void Fill(ArtifactState st, List<ArtifactArmDef> prefer, ArtifactCarrier carrier, int n)
        {
            int perArm = ArtifactoricsSettingsDef.Main?.nodeCosts?.Count ?? 9;
            int maxArms = ArtifactoricsSettingsDef.Main?.maxMilestoneArmsPerPawn ?? 3;
            var order = prefer.Where(a => a != null && a.carrier == carrier).ToList();
            order.AddRange(DefDatabase<ArtifactArmDef>.AllDefsListForReading.Where(a => a.carrier == carrier && !order.Contains(a)).InRandomOrder());
            var groups = new HashSet<string>();
            int arms = 0;
            foreach (ArtifactArmDef arm in order)
            {
                if (n <= 0 || arms >= maxArms) break;
                if (!arm.exclusionGroup.NullOrEmpty() && groups.Contains(arm.exclusionGroup)) continue;
                int give = Mathf.Min(perArm, n);
                st.opened[arm] = give;
                n -= give;
                arms++;
                if (!arm.exclusionGroup.NullOrEmpty()) groups.Add(arm.exclusionGroup);
            }
        }
    }

    [DefOf]
    public static class AvirAmuletLayerDefOf
    {
        public static ApparelLayerDef Avir_AccessoryLayer;
        static AvirAmuletLayerDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(AvirAmuletLayerDefOf)); }
    }

    // ================================================================== artificer caravans

    /// <summary>Trader kinds with this extension carry all their goods in the trader's subspace
    /// storage: no pack animals are generated for them.</summary>
    public class AvirSubspaceCaravan : DefModExtension { }

    [HarmonyPatch(typeof(PawnGroupKindWorker_Trader), "GenerateCarriers")]
    public static class Patch_AvirSubspaceCaravan
    {
        public static bool Prefix(PawnGroupMakerParms parms, Pawn trader, List<Thing> wares)
        {
            if (parms?.traderKind?.GetModExtension<AvirSubspaceCaravan>() == null || trader?.inventory == null) return true;
            foreach (Thing t in wares.Where(x => !(x is Pawn)).ToList())
            {
                if (!trader.inventory.innerContainer.TryAdd(t, true)) t.Destroy();
            }
            return false;
        }
    }

    /// <summary>
    /// A SUBSPACE CARAVAN HAS A TRADER. Its goods ride in the trader's own inventory, but while a caravan trades with
    /// the colony the game counts as goods only what its pack animals carry - so the artificers' caravan had nothing
    /// to sell, could not trade, and no one in it was shown as its trader. For a subspace trader kind the trader's own
    /// inventory is its stock.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_TraderTracker), nameof(Pawn_TraderTracker.Goods), MethodType.Getter)]
    public static class Patch_AvirSubspaceCaravanGoods
    {
        private static readonly AccessTools.FieldRef<Pawn_TraderTracker, Pawn> pawnRef = AccessTools.FieldRefAccess<Pawn_TraderTracker, Pawn>("pawn");

        public static void Postfix(Pawn_TraderTracker __instance, ref IEnumerable<Thing> __result)
        {
            if (__instance?.traderKind?.GetModExtension<AvirSubspaceCaravan>() == null) return;
            Pawn p = pawnRef(__instance);
            if (p?.inventory == null || !(p.GetLord()?.LordJob is LordJob_TradeWithColony)) return;
            __result = __result.Concat(p.inventory.innerContainer.Where(t => !p.inventory.NotForSale(t)).ToList());
        }
    }

    /// <summary>Finished, undeveloped traditional amulets of one necklace metal.</summary>
    public class StockGenerator_AvirAmulets : StockGenerator
    {
        public ArtifactMetalDef necklace;
        public List<ArtifactMetalDef> cores = new List<ArtifactMetalDef>();

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            int n = countRange.RandomInRange;
            for (int i = 0; i < n; i++)
            {
                var q = (QualityCategory)Rand.RangeInclusive(2, 4);
                Apparel t = AvirFactionGear.MakeAmulet(necklace, cores, null, q);
                if (t != null) yield return t;
            }
        }

        // buys back any amulet - traditional or named
        public override bool HandlesThingDef(ThingDef thingDef) => thingDef != null && thingDef.HasComp(typeof(CompAvirAmulet));
    }

    /// <summary>Artifact settings (rims, no hearts) of the given metals.</summary>
    public class StockGenerator_AvirArtifactSettings : StockGenerator
    {
        public List<ArtifactMetalDef> rims = new List<ArtifactMetalDef>();

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            int n = countRange.RandomInRange;
            for (int i = 0; i < n; i++)
            {
                Thing t = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_ArtifactSetting);
                CompArtifact c = t.TryGetComp<CompArtifact>();
                if (c == null) continue;
                c.rimMetal = rims.Where(m => m != null && m.allowedAsRim).RandomElementWithFallback()
                             ?? ArtifactoricsDatabase.Metals.Where(m => m.allowedAsRim).RandomElementWithFallback();
                c.settingQuality = (QualityCategory)Rand.RangeInclusive(2, 4);
                yield return t;
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef) => thingDef == ArtifactoricsDefOf.Avir_ArtifactSetting;
    }
}
