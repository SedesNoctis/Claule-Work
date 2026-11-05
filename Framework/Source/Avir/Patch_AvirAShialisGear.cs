using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE A'SCHIALIS LEGION WARDROBE. A partial extension of AvirElvesGear, so it reuses that
    /// file's private helpers (Wear/WearFixedColor/StuffFor/SetQuality/IsHulk/MaleSafe/Pick) and
    /// its resolved shared defs (Tunic, Gambeson, CloakHigh, CapeDark, Hood, HoodArmored,
    /// Witch_Skin, Coastal*, GravetenderRobes, Essentium, FeatheredFur, Cloudcloth) directly.
    /// The A'Shaign wardrobe in the main file is untouched.
    ///
    /// HOW IT HOOKS IN (all in the main file):
    ///   - static ctor        -> InitAShialis() resolves the Legion-only defs.
    ///   - Dress()            -> NoTisserium = IsAShialis(pawn); Legion castes -> DressAShialis().
    ///   - Arm()              -> centurions -> ArmCenturion(); the rest fall through tisserium-free.
    ///   - StuffFor()         -> honours NoTisserium, so NO A'Schialis pawn ever gets tisserium.
    ///
    /// PALETTES (your RGBA values). Main leather/metal is grey 61,61,61 everywhere; the brighter
    /// colours are for cloth only:
    ///   grey 61,61,61   red 128,45,45   gold 182,142,65   tan 237,212,172
    ///   white 255,255,255   lilac 226,216,246
    ///
    /// Pieces are worn in ONE exact colour via WearFixedColor - no palette rolling - so the
    /// output matches the spec's colours precisely. Every piece is self-disabling: a missing
    /// def (mod not installed) is silently skipped by WearFixedColor's null guard.
    ///
    /// CONFIRMED (this pass): Inquisitors are grey garb with a red-or-gold hood and cloak - the
    /// hood and cloak share one per-pawn roll of AS_Red or AS_Gold, garb is always AS_Grey.
    /// Highlords are grey-only (AS_Grey, 61,61,61) - no red/gold anywhere on that rank. Huntresses
    /// of the Legion are recoloured in these same Legion colours (grey leather/metal, red-or-gold
    /// cloak) via the AS_HuntressRed/Gold palette pair, NOT the A'Shaign Blood/Lilac pair used by
    /// every other faction's Huntress.
    /// </summary>
    public static partial class AvirElvesGear
    {
        // ---- Tisserium ban flag. Set per-pawn in Dress()/Arm(); read in StuffFor(). ----
        private static bool NoTisserium;

        // ---- Palette ----
        private static Color RGB(int r, int g, int b) { return new Color(r / 255f, g / 255f, b / 255f); }
        private static readonly Color AS_Grey  = RGB(61, 61, 61);
        private static readonly Color AS_Red   = RGB(128, 45, 45);
        private static readonly Color AS_Gold  = RGB(182, 142, 65);
        private static readonly Color AS_Tan   = RGB(237, 212, 172);
        private static readonly Color AS_White = RGB(255, 255, 255);
        private static readonly Color AS_Lilac = RGB(226, 216, 246);
        private static Color PickC(params Color[] cs) { return cs[Rand.Range(0, cs.Length)]; }

        // ---- Huntress palette pair (Legion colours, not the A'Shaign Blood/Lilac pair).
        // Cloak rolls red or gold, per pawn; leather and metal stay Legion grey either way. ----
        private static readonly Palette AS_HuntressRed = new Palette
        {
            cloak = AS_Red, leather = AS_Grey, metal = AS_Grey,
        };
        private static readonly Palette AS_HuntressGold = new Palette
        {
            cloak = AS_Gold, leather = AS_Grey, metal = AS_Grey,
        };

        // ---- Legion-only defs (resolved in InitAShialis) ----
        private static ThingDef Fyrlance;
        private static ThingDef IgRobe, IgAshCloak, IgToolbelt, IgPlate, IgBarbute, IgKettle, IgKettleFace;
        private static ThingDef DOBreeches, DOHood, DOZealot, DOPlate, DOHerald, DOInqArmor, DOInqCloak,
                                DOCrusaderArmor, DOCrusaderHelmet;
        private static ThingDef COKnightArmor, COKnightHelmet, SorcRobe, SorcHelmet;
        private static ThingDef GBArtistSuit, GBArchitectSuit;
        private static ThingDef WEBoots, WEGloves;

        private static void InitAShialis()
        {
            Fyrlance     = Find("Ignivar_Fyrlance");
            IgRobe       = Find("Ignivar_Robe");
            IgAshCloak   = Find("Ignivar_Scarf");        // label: "Ignivar Ash Cloak"
            IgToolbelt   = Find("Ignivar_Toolbelt");
            IgPlate      = Find("Ignivar_Plate");
            IgBarbute    = Find("Ignivar_Barbute");
            IgKettle     = Find("Ignivar_Kettle");
            IgKettleFace = Find("Ignivar_Kettle_Face");

            DOBreeches       = Find("BotchJob_DivineOrderBreeches");
            DOHood           = Find("BotchJob_DivineOrderHood");
            DOZealot         = Find("BotchJob_DivineOrderZealotRobes");
            DOPlate          = Find("BotchJob_DivineOrderPlateArmor");
            DOHerald         = Find("BotchJob_HeraldRobes");
            DOInqArmor       = Find("BotchJob_InquisitorArmor");    // label: "inquisitor garb"
            DOInqCloak       = Find("BotchJob_InquisitorCloak");
            DOCrusaderArmor  = Find("BotchJob_CrusaderArmor");
            DOCrusaderHelmet = Find("BotchJob_CrusaderHelmet");

            COKnightArmor  = Find("DevDesigner_DarkElves_ColdOneKnightArmor");
            COKnightHelmet = Find("DevDesigner_DarkElves_ColdOneKnightHelmet");
            SorcRobe       = Find("DevDesigner_DarkElves_SorceressRobe");
            SorcHelmet     = Find("DevDesigner_DarkElves_SorceressHelmet");

            // "GB" (Golden-Black) recolours already shipped in Compat/FantasyBiotech.
            GBArtistSuit    = Find("Avir_VR_Apparel_ArtificerSuit_Basic_GB");
            GBArchitectSuit = Find("Avir_VR_Apparel_ArtificerSuit_Lord_GB");

            // Avir extremity wraps. Field names kept as WEBoots/WEGloves so every use site below
            // stays untouched - only what they resolve to has changed.
            WEBoots  = Find("Avir_ToeGuards");
            WEGloves = Find("Avir_ClawWraps");
        }

        // ---- Faction / caste gates ----
        // The four Legion-only ranks. These kinds exist ONLY in the A'Schialis faction and are
        // never A'Shaign, so they are recognised by kind - exactly the way AvirFaction recognises
        // Avir pawns (by the Avir_ prefix), and NOT by faction. This is what makes them wear the
        // Legion wardrobe even when they spawn "Independent": dev-placed, quest/ritual pawns,
        // slaves, wanderers, or before a faction is stamped on them.
        private static bool IsAShialisUniqueKind(string kind)
        {
            switch (kind)
            {
                case "Avir_Centurion":
                case "Avir_Legionnaire":
                case "Avir_Inquisitor":
                case "Avir_Highlord":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAShialis(Pawn p)
        {
            if (p == null) return false;
            // Legion-only kind -> always A'Schialis, whatever faction it carries.
            if (p.kindDef != null && IsAShialisUniqueKind(p.kindDef.defName)) return true;
            // Shared castes (Artificer, GuildMaster, Architect, Artist, Hatchling, Huntress) live
            // in BOTH orders, so only the faction can tell them apart.
            return p.Faction != null && p.Faction.def != null
                && p.Faction.def.defName == "Avir_AShialis";
        }

        /// <summary>
        /// Public face of IsAShialis, for systems outside the wardrobe that need to tell the two
        /// orders apart - currently AvirShieldBelt. Single-sourced deliberately: the kind-first,
        /// faction-second rule above is subtle (it is what makes a dev-placed or Independent
        /// centurion still read as Legion) and must not be reimplemented anywhere else.
        /// </summary>
        public static bool IsAShialisPawn(Pawn p)
        {
            return IsAShialis(p);
        }

        // Castes the equipment pass gave a dedicated A'Schialis look. Anything else falls
        // through to the shared A'Shaign wardrobe - tisserium still barred - until a Legion
        // look is designed for it. Huntress is now dressed here too (Legion grey/red/gold,
        // not the A'Shaign Blood/Lilac pair) - see DressAShialisHuntress.
        private static bool IsAShialisDressedCaste(string kind)
        {
            switch (kind)
            {
                case "Avir_Hatchling":
                case "Avir_Artificer":
                case "Avir_GuildMaster":
                case "Avir_Architect":
                case "Avir_Artist":
                case "Avir_Centurion":
                case "Avir_Legionnaire":
                case "Avir_Inquisitor":
                case "Avir_Highlord":
                case "Avir_Huntress":
                    return true;
                default:
                    return false;
            }
        }

        // ---- Universal boots + gloves, coloured to the Legion grey (not the A'Shaign slate) ----
        private static void ASBootsGloves(Pawn pawn)
        {
            WearFixedColor(pawn, WEBoots, AS_Grey, false, false);
            WearFixedColor(pawn, WEGloves, AS_Grey, false, false);
        }

        // ============================================================
        //  DRESS
        // ============================================================
        private static void DressAShialis(Pawn pawn)
        {
            if (pawn.apparel == null) return;

            string kind = pawn.kindDef != null ? pawn.kindDef.defName : "";
            bool female = pawn.gender == Gender.Female;

            // Hand-off for Arm(): A'Schialis has no rolled palette, so clear it.
            palettePawn = pawn;
            paletteName = "";
            CapGood = false;

            pawn.apparel.DestroyAll();

            switch (kind)
            {
                case "Avir_Hatchling":
                    break; // boots + gloves only, added below

                case "Avir_Artificer":
                    DressAShialisArtificer(pawn, female);
                    break;

                case "Avir_GuildMaster":
                    DressAShialisGuild(pawn);
                    break;

                case "Avir_Architect":
                case "Avir_Artist":
                    DressAShialisHighArtificer(pawn, kind == "Avir_Architect");
                    break;

                case "Avir_Centurion":
                    DressAShialisCenturion(pawn);
                    break;

                case "Avir_Legionnaire":
                    DressAShialisLegionnaire(pawn);
                    break;

                case "Avir_Inquisitor":
                    DressAShialisInquisitor(pawn);
                    break;

                case "Avir_Highlord":
                    DressAShialisHighlord(pawn, female);
                    break;

                case "Avir_Huntress":
                    DressAShialisHuntress(pawn);
                    break;
            }

            ASBootsGloves(pawn);
            TryCarryPhoenixEssence(pawn, kind);
        }

        /// <summary>
        /// A vial of the Academy's work, in the inventory rather than worn.
        ///
        ///   Inquisitor   1%
        ///   Highlord     5%
        ///
        /// Nobody else in the Legion carries one, and the numbers are deliberately tiny: at
        /// well over two thousand silver a bottle this is loot, not equipment, and a raid that
        /// reliably dropped one would be a farm. A Highlord is rare enough on its own that 5%
        /// still makes finding one an event.
        ///
        /// Self-disabling: if the def is missing the method returns without touching anything.
        /// </summary>
        private static void TryCarryPhoenixEssence(Pawn pawn, string kind)
        {
            float chance;
            if (kind == "Avir_Highlord") chance = 0.05f;
            else if (kind == "Avir_Inquisitor") chance = 0.01f;
            else return;

            if (!Rand.Chance(chance)) return;
            if (pawn.inventory == null) return;

            ThingDef essence = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_PhoenixEssence");
            if (essence == null) return;

            Thing vial = ThingMaker.MakeThing(essence);
            vial.stackCount = 1;
            pawn.inventory.innerContainer.TryAdd(vial, true);
        }

        /// <summary>
        /// ARTIFICER. Four whole torso sets, never mixed, plus a rare Divine Order zealot look.
        /// Divine Order breeches (grey) go under every set EXCEPT the witch undergarment set.
        /// Imperial Dancer is gone entirely - the coastal adventurer set replaces it here.
        /// Male artificers skip the (F)-only witch base and draw from sets 1/2/4.
        /// </summary>
        private static void DressAShialisArtificer(Pawn pawn, bool female)
        {
            // Rare: Divine Order zealot robes, worn alone, all grey, NO hood on an artificer.
            if (Rand.Chance(0.08f) && DOZealot != null)
            {
                WearFixedColor(pawn, DOZealot, AS_Grey, false, false);
                WearFixedColor(pawn, DOBreeches, AS_Grey, false, false);
                return;
            }

            int set = female ? Rand.RangeInclusive(1, 4)
                             : new[] { 1, 2, 4 }[Rand.Range(0, 3)];

            switch (set)
            {
                case 1: // Ignivar Robe (+ Ash Cloak ALWAYS, no other) [+ toolbelt] [+ hood = robe colour]
                {
                    Color robe = PickC(AS_Red, AS_Gold, AS_Grey);
                    WearFixedColor(pawn, IgRobe, robe, false, false);
                    WearFixedColor(pawn, IgAshCloak, Rand.Chance(0.5f) ? AS_White : AS_Lilac, false, false);
                    if (Rand.Chance(0.35f)) WearFixedColor(pawn, IgToolbelt, AS_White, false, false);
                    if (Rand.Chance(0.40f)) WearFixedColor(pawn, Hood, robe, false, false);
                    WearFixedColor(pawn, DOBreeches, AS_Grey, false, false);
                    break;
                }
                case 2: // Coastal Adventurer + cape/half-cape [+ hood under cape], OR toolbelt instead (no hood)
                {
                    WearFixedColor(pawn, CoastalAdventurerOutfit, Rand.Chance(0.5f) ? AS_Grey : AS_Tan, false, false);
                    if (Rand.Chance(0.75f))
                    {
                        Color cape = PickC(AS_Red, AS_Gold);
                        WearFixedColor(pawn, Rand.Chance(0.5f) ? CoastalCape : CoastalHalfCape, cape, false, false);
                        if (Rand.Chance(0.40f)) WearFixedColor(pawn, Hood, cape, false, false);
                    }
                    else
                    {
                        WearFixedColor(pawn, IgToolbelt, AS_White, false, false); // no hood with the toolbelt
                    }
                    WearFixedColor(pawn, DOBreeches, AS_Grey, false, false);
                    break;
                }
                case 3: // Witch undergarment (grey) + coastal cape/half-cape same grey. Sometimes hood. NO breeches.
                {
                    WearFixedColor(pawn, Witch_Skin, AS_Grey, false, false);
                    WearFixedColor(pawn, Rand.Chance(0.5f) ? CoastalCape : CoastalHalfCape, AS_Grey, false, false);
                    if (Rand.Chance(0.40f)) WearFixedColor(pawn, Hood, AS_Grey, false, false);
                    break;
                }
                case 4: // Elven tunic (gold/red) + High Elven cloak/Dark Elven cape (gold/red) + gambeson (grey)
                {
                    Color cloth = PickC(AS_Gold, AS_Red);
                    WearFixedColor(pawn, Tunic, cloth, false, false);
                    WearFixedColor(pawn, Gambeson, AS_Grey, false, false);
                    WearFixedColor(pawn, Rand.Chance(0.5f) ? CloakHigh : CapeDark, PickC(AS_Gold, AS_Red), false, false);
                    if (Rand.Chance(0.40f)) WearFixedColor(pawn, Hood, cloth, false, false);
                    if (Rand.Chance(0.30f)) WearFixedColor(pawn, IgToolbelt, AS_White, false, false);
                    WearFixedColor(pawn, DOBreeches, AS_Grey, false, false);
                    break;
                }
            }
        }

        /// <summary>
        /// GUILD MASTER. The classic grey Gravetender uniform, recoloured: robe + hood in red/gold;
        /// or the coastal look with the adventurer outfit in grey and the cape in red/gold. Breeches
        /// grey. (Boots/gloves grey via ASBootsGloves.)
        /// </summary>
        private static void DressAShialisGuild(Pawn pawn)
        {
            if (Rand.Chance(0.5f) && GravetenderRobes != null)
            {
                Color c = PickC(AS_Red, AS_Gold);
                WearFixedColor(pawn, GravetenderRobes, c, false, false);
                WearFixedColor(pawn, HoodArmored, c, false, false);
                WearFixedColor(pawn, DOBreeches, AS_Grey, false, false);
            }
            else
            {
                WearFixedColor(pawn, CoastalAdventurerOutfit, AS_Grey, false, false);
                Color cape = PickC(AS_Red, AS_Gold);
                WearFixedColor(pawn, Rand.Chance(0.5f) ? CoastalCape : CoastalHalfCape, cape, false, false);
                if (Rand.Chance(0.35f)) WearFixedColor(pawn, Hood, cape, false, false);
                WearFixedColor(pawn, DOBreeches, AS_Grey, false, false);
            }
        }

        /// <summary>
        /// ARCHITECT / ARTIST. The (GB) recolour of the Fantasy Biotech suit - Lord for the
        /// Architect, Basic for the Artist. NO headgear, same rule as the A'Shaign high artificers.
        /// The recolour is baked into the GB texture; the mask colour is set to gold for the
        /// "Golden-Black" read. Falls back to a plain grey tunic/gambeson if the GB suit (i.e.
        /// Fantasy Biotech) isn't present.
        /// </summary>
        private static void DressAShialisHighArtificer(Pawn pawn, bool isArchitect)
        {
            ThingDef suit = isArchitect ? GBArchitectSuit : GBArtistSuit;
            if (suit != null)
            {
                ThingDef stuff = suit.MadeFromStuff ? (FeatheredFur ?? Cloudcloth) : null;
                if (!suit.MadeFromStuff || stuff != null)
                {
                    Apparel a = (Apparel)ThingMaker.MakeThing(suit, stuff);
                    SetQuality(a, false);
                    CompColorable col = a.TryGetComp<CompColorable>();
                    if (col != null) col.SetColor(AS_Gold); // "Golden-Black" mask tint
                    AvirGearUtil.Weather(a);
                    pawn.apparel.Wear(a, false);
                    return;
                }
            }

            // Fallback if Fantasy Biotech / the GB suit isn't installed.
            WearFixedColor(pawn, Tunic, AS_Grey, false, false);
            WearFixedColor(pawn, Gambeson, AS_Grey, false, false);
        }

        /// <summary>
        /// CENTURION (male-only). Ignivar plate (essentium, forced by NoTisserium) + divine
        /// breeches, all grey, with a CHANCE of one head piece. This is now the only centurion
        /// silhouette.
        ///
        /// THE BRANCH THAT WAS REMOVED. There used to be a 50/50 roll against a second look:
        /// elven tunic (grey or gold) + gambeson + a coin-flip armored hood + breeches. It was
        /// a straight recolour of the A'Shaign young hunter, so half of every centurion squad
        /// read as grey A'Shaign militia rather than as a Legion line unit - and it was the one
        /// A'Schialis set built out of cloth rather than plate, which put it out of step with
        /// the rest of the wardrobe. Gone entirely; do not reintroduce it.
        ///
        /// HEADGEAR is now a 70% roll rather than a certainty, so a fraction of any line goes
        /// bare-headed. Adjust BareHeadChance below to taste.
        /// </summary>
        private static void DressAShialisCenturion(Pawn pawn)
        {
            // Ignivar plate + breeches, always. Plate is metallic -> essentium via StuffFor.
            WearFixedColor(pawn, IgPlate, AS_Grey, false, false);

            if (Rand.Chance(CenturionHelmChance))
            {
                List<ThingDef> heads = new List<ThingDef>();
                if (IgBarbute != null)    heads.Add(IgBarbute);
                if (IgKettle != null)     heads.Add(IgKettle);
                if (Hood != null)         heads.Add(Hood);           // elven hood
                if (IgKettleFace != null) heads.Add(IgKettleFace);
                if (heads.Count > 0) WearFixedColor(pawn, heads[Rand.Range(0, heads.Count)], AS_Grey, false, false);
            }

            WearFixedColor(pawn, DOBreeches, AS_Grey, false, false);
        }

        /// <summary>Chance a centurion wears a helm at all. 1 - this is the bare-headed share.</summary>
        private const float CenturionHelmChance = 0.70f;

        /// <summary>
        /// LEGIONNAIRE (male-only). Standard body: Cold One Knight armour + helmet (grey) + elven
        /// tunic (grey) + High Elven cloak (gold/red) + divine breeches (grey). Hulk: Crusader or
        /// Divine Order plate + Crusader helmet + elven tunic + divine breeches, all grey. The
        /// universal MKI shield belt is added by AvirShieldBelt.
        ///
        /// FIXED THIS PASS - the standard body wore NO LEGWEAR AT ALL. Armour, helmet and cloak,
        /// and nothing below the waist; only the hulk branch had breeches. Both the tunic and the
        /// breeches are now present on both branches.
        ///
        /// LAYER ORDER: armour first, tunic after - the same order the hulk branch has always
        /// used. WearFixedColor bails out silently on a CanWearTogether clash, so if Cold One
        /// Knight armour turns out to occupy the tunic's layer the tunic is skipped rather than
        /// displacing the armour. That is the correct failure direction: the plate must win.
        ///
        /// QUALITY: masterwork, matching the <itemQuality>Masterwork</itemQuality> the pawnkind
        /// def has always declared. The XML value never actually applied, because apparelMoney
        /// is 0 and every piece comes from this file instead; the rank was silently rolling the
        /// same Good-and-up spread as a centurion. The declaration is now honoured in code.
        /// </summary>
        private static void DressAShialisLegionnaire(Pawn pawn)
        {
            if (IsHulk(pawn))
            {
                WearFixedColor(pawn, Rand.Chance(0.5f) ? DOCrusaderArmor : DOPlate, AS_Grey, true, false);
                WearFixedColor(pawn, DOCrusaderHelmet, AS_Grey, true, false);
                WearFixedColor(pawn, Tunic, AS_Grey, true, false);
                WearFixedColor(pawn, DOBreeches, AS_Grey, true, false);
            }
            else
            {
                WearFixedColor(pawn, COKnightArmor, AS_Grey, true, false);
                WearFixedColor(pawn, COKnightHelmet, AS_Grey, true, false);
                WearFixedColor(pawn, Tunic, AS_Grey, true, false);
                WearFixedColor(pawn, CloakHigh, PickC(AS_Gold, AS_Red), true, false);
                WearFixedColor(pawn, DOBreeches, AS_Grey, true, false);
            }
        }

        /// <summary>
        /// INQUISITOR. One set: Divine Order hood + inquisitor garb + inquisitor cloak. Garb is
        /// always grey (per your correction); hood and cloak are always red-and-gold, one roll
        /// per pawn from AS_Red / AS_Gold, shared between the two so they always match each other.
        /// </summary>
        private static void DressAShialisInquisitor(Pawn pawn)
        {
            Color cloth = PickC(AS_Red, AS_Gold);
            WearFixedColor(pawn, DOInqArmor, AS_Grey, false, false);
            WearFixedColor(pawn, DOInqCloak, cloth, false, false);
            WearFixedColor(pawn, DOHood, cloth, false, false);
        }

        /// <summary>
        /// HIGHLORD. Males: Divine Order hood + Herald robes. Females: that, or the recoloured
        /// Sorceress set (robe + helmet) inherited from the A'Shaign Matriarch. Grey-only, per
        /// your confirmation (61,61,61) - no red/gold anywhere on this rank; that colour belongs
        /// to the Inquisitors.
        /// </summary>
        private static void DressAShialisHighlord(Pawn pawn, bool female)
        {
            bool sorceress = female && Rand.Chance(0.5f);
            if (sorceress)
            {
                WearFixedColor(pawn, SorcRobe, AS_Grey, false, false);
                WearFixedColor(pawn, SorcHelmet, AS_Grey, false, false);
            }
            else
            {
                WearFixedColor(pawn, DOHerald, AS_Grey, false, false);
                WearFixedColor(pawn, DOHood, AS_Grey, false, false);
            }
        }

        /// <summary>
        /// HUNTRESS. Same bladedancer silhouette as the A'Shaign Huntress (BD skin/middle/shell/
        /// helm + a chance at a pauldron), but recoloured in Legion colours instead of the
        /// A'Shaign Blood/Lilac pair: leather and metal are Legion grey, and the cloth element
        /// (cloak/helm trim) rolls red or gold, per pawn - matching the rest of the Legion.
        /// Boots/gloves are added by the shared ASBootsGloves() call after the switch, same as
        /// every other A'Schialis caste.
        /// </summary>
        private static void DressAShialisHuntress(Pawn pawn)
        {
            Palette pal = Rand.Chance(0.5f) ? AS_HuntressRed : AS_HuntressGold;

            Wear(pawn, BD_Skin, false, true, pal);
            Wear(pawn, BD_Middle, false, true, pal);
            Wear(pawn, BD_Shell, false, true, pal);
            Wear(pawn, BD_Helm, false, true, pal, true);
            TryPauldron(pawn, pal);
        }

        // ============================================================
        //  ARM
        // ============================================================
        /// <summary>
        /// CENTURION weapon: a Fyrlance, always, essentium (never tisserium). Plus - CE only -
        /// flamethrower fuel in the inventory. The ammo load is fully self-disabling: if Combat
        /// Extended (and thus these ammo defs) isn't present, nothing is added and nothing errors.
        /// </summary>
        private static void ArmCenturion(Pawn pawn)
        {
            // THE FUEL BUG. Fuel lives in the INVENTORY, and Arm() only clears EQUIPMENT
            // (DestroyAllEquipment at the top). Arm() runs a second time on any Avir that
            // Hospitality redresses - visitors and trade caravans - via
            // Patch_HospitalityAvirDress. A centurion who drew the Fyrlance on the first pass
            // and a spear on the second kept the first pass's 90 units of flamethrower fuel and
            // walked in with a spear and a bag of prometheum. Purge before we roll; only the
            // Fyrlance branch puts fuel back.
            PurgeFlameFuel(pawn);

            // 50/50: an essentium elven spear (wood or dark) or the Fyrlance. If the spear defs
            // are absent (Elves mod not installed) this quietly falls through to the Fyrlance.
            if (Rand.Chance(0.50f) && TryArmCenturionSpear(pawn)) return;

            if (Fyrlance == null) return;

            ThingDef stuff = Fyrlance.MadeFromStuff ? Essentium : null;
            if (Fyrlance.MadeFromStuff && stuff == null) return;

            ThingWithComps w = (ThingWithComps)ThingMaker.MakeThing(Fyrlance, stuff);
            SetQuality(w, false);
            AvirGearUtil.Weather(w);
            pawn.equipment.AddEquipment(w);

            LoadFlameFuel(pawn);
        }

        /// <summary>
        /// Arms a centurion with a wood- or dark-elven spear (50/50), forged from essentium
        /// (never tisserium). Returns false if neither spear def is available, so the caller
        /// can fall back to the Fyrlance.
        /// </summary>
        private static bool TryArmCenturionSpear(Pawn pawn)
        {
            List<ThingDef> opts = new List<ThingDef>();
            ThingDef wood = Find("DevDesigner_WoodElves_Spear");
            ThingDef dark = Find("DevDesigner_DarkElves_Spear");
            if (wood != null) opts.Add(wood);
            if (dark != null) opts.Add(dark);
            if (opts.Count == 0) return false;

            ThingDef spear = opts[Rand.Range(0, opts.Count)];
            ThingDef stuff = spear.MadeFromStuff ? Essentium : null;
            if (spear.MadeFromStuff && stuff == null) return false;

            ThingWithComps w = (ThingWithComps)ThingMaker.MakeThing(spear, stuff);
            SetQuality(w, false);
            AvirGearUtil.Weather(w);
            pawn.equipment.AddEquipment(w);
            return true;
        }

        /// <summary>
        /// CE flamethrower fuel for the Fyrlance (AmmoSet_Flamethrower). A centurion carries 90
        /// units of EITHER bioferrite OR prometheum (50/50) - never napalm. defNames verified
        /// against Combat Extended. If CE isn't loaded the def resolves to null and no fuel is
        /// added (nothing errors).
        /// </summary>
        private static void LoadFlameFuel(Pawn pawn)
        {
            if (pawn.inventory == null) return;
            AddFuel(pawn, 90, Rand.Chance(0.5f) ? "Ammo_Flamethrower_Prometheum"
                                                : "Ammo_Flamethrower_Bioferrite");
        }

        /// <summary>
        /// Strips any flamethrower fuel already in the pawn's inventory. Called at the top of
        /// ArmCenturion so a re-arm (Hospitality redress) cannot leave fuel behind from a
        /// previous pass that armed a Fyrlance. Matches on the CE ammo prefix, so it catches
        /// napalm too even though we never load it. No-ops without CE, since nothing matches.
        /// </summary>
        private static void PurgeFlameFuel(Pawn pawn)
        {
            if (pawn.inventory == null || pawn.inventory.innerContainer == null) return;

            ThingOwner<Thing> inv = pawn.inventory.innerContainer;
            for (int i = inv.Count - 1; i >= 0; i--)
            {
                Thing t = inv[i];
                if (t == null || t.def == null || t.def.defName == null) continue;
                if (!t.def.defName.StartsWith("Ammo_Flamethrower")) continue;

                inv.RemoveAt(i);
                t.Destroy(DestroyMode.Vanish);
            }
        }

        private static void AddFuel(Pawn pawn, int count, params string[] candidates)
        {
            foreach (string n in candidates)
            {
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(n);
                if (d == null) continue;
                Thing t = ThingMaker.MakeThing(d);
                t.stackCount = count;
                pawn.inventory.innerContainer.TryAdd(t, true);
                return;
            }
        }
    }
}
