using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// INTEGRATION: Elves (Continued). Self-disabling. THE CASTE WARDROBE.
    ///
    /// PALETTES. Five, chosen in matched PAIRS by structure - the leather is the one constant
    /// across all of them, and each palette's metal either matches the leather (a flat, unlit
    /// look) or lifts clear of it (a brighter, colder look). A pawn never mixes two palettes.
    ///
    ///   BLOOD     cloak/hood 138,0,0    leather 92,95,105    metal = leather   (Bloodmoon)
    ///   LILAC     cloak/hood 160,146,199 leather 92,95,105   metal 182,183,186 (Shadowsong)
    ///   MIDNIGHT  cloak/hood 28,55,72   leather 92,95,105    metal = leather   (Evershadow - Blood's structure, dark instead of red)
    ///   TEAL      cloak/hood 42,110,120 leather 92,95,105    metal 150,165,170 (Evershadow - Lilac's structure, cold instead of pale)
    ///   BLACK     cloak/hood 18,18,20   leather 92,95,105    metal 200,205,210 (no Dynasty - matched to Duskglass, needs the brightest metal lift of the five or the silver disappears into the cloak)
    ///
    /// Barbarians gear is NEVER recoloured - it ships coloured and looks right as it is.
    ///
    /// BOOTS AND GLOVES ARE UNIVERSAL. Every caste, every body type, plate or robe or nothing.
    /// Handled by AvirGearUtil so the Barbarians path gets them too.
    /// </summary>
    [StaticConstructorOnStartup]
    public static partial class AvirElvesGear
    {
        public static readonly bool Active;

        private static ThingDef FeatheredFur, Cloudcloth, Essentium, Tisserium;

        private struct Palette
        {
            public Color cloak;
            public Color leather;
            public Color metal;
        }

        private static readonly Palette Blood = new Palette
        {
            cloak   = new Color(138f/255f,   0f/255f,   0f/255f),
            leather = new Color( 92f/255f,  95f/255f, 105f/255f),
            metal   = new Color( 92f/255f,  95f/255f, 105f/255f),
        };

        private static readonly Palette Lilac = new Palette
        {
            cloak   = new Color(160f/255f, 146f/255f, 199f/255f),
            leather = new Color( 92f/255f,  95f/255f, 105f/255f),
            metal   = new Color(182f/255f, 183f/255f, 186f/255f),
        };

        // ---- EVERSHADOW PALETTES ----
        // Midnight mirrors Blood's structure (metal = leather, no separate tone) with a dark
        // blue-black cloak in place of red. Teal mirrors Lilac's structure (metal lifts clear
        // of the leather) with a cold teal cloak in place of pale purple.
        private static readonly Palette Midnight = new Palette
        {
            cloak   = new Color( 28f/255f,  55f/255f,  72f/255f),
            leather = new Color( 92f/255f,  95f/255f, 105f/255f),
            metal   = new Color( 92f/255f,  95f/255f, 105f/255f),
        };

        private static readonly Palette Teal = new Palette
        {
            cloak   = new Color( 42f/255f, 110f/255f, 120f/255f),
            leather = new Color( 92f/255f,  95f/255f, 105f/255f),
            metal   = new Color(150f/255f, 165f/255f, 170f/255f),
        };

        // ---- BLACK PALETTE ----
        // No Dynasty - this is the palette matched to Duskglass (black and silver, not violet).
        // Needs the brightest metal lift of any palette here, or the silver simply disappears
        // into a near-black cloak instead of reading as silver-on-black.
        private static readonly Palette Black = new Palette
        {
            cloak   = new Color( 18f/255f,  18f/255f,  20f/255f),
            leather = new Color( 92f/255f,  95f/255f, 105f/255f),
            metal   = new Color(200f/255f, 205f/255f, 210f/255f),
        };

        // ---- YOUNG HUNTER PALETTES ----
        // Field colours. Nothing bright, nothing regal - a young hunter is meant to disappear
        // into a treeline, not announce herself. Cloak, hood AND tunic take the drab colour;
        // the armour and metal are a flat dull grey.
        private static readonly Palette YoungOlive = new Palette
        {
            cloak   = new Color(122f/255f, 126f/255f, 112f/255f),   // olive-grey
            leather = new Color(129f/255f, 130f/255f, 133f/255f),
            metal   = new Color(129f/255f, 130f/255f, 133f/255f),
        };

        private static readonly Palette YoungEarth = new Palette
        {
            cloak   = new Color( 91f/255f,  85f/255f,  67f/255f),   // dead-leaf brown
            leather = new Color(129f/255f, 130f/255f, 133f/255f),
            metal   = new Color(129f/255f, 130f/255f, 133f/255f),
        };

        // Armour
        private static ThingDef Tunic, Scalemail, Chainmail, Gambeson;
        private static ThingDef LeatherArmorWood, LeatherArmorDark, Dreadspear;
        private static ThingDef StagKnight, BlackGuard;

        // Cloaks. Warriors and young hunters get ONLY these two - the wood elven cloak and
        // the dark elven cape. The Cold One Knight cloak is not used AT ALL, anywhere.
        // The HIGH elven cloak, not the wood elven one. (Corrected - I had the wrong one.)
        private static ThingDef CloakHigh, CapeDark;

        // Regalia. Matriarchs and Priestesses only - it is what makes them read as royalty
        // rather than as a soldier who happens to be wearing a dress.
        private static ThingDef CloakBlackGuard;
        private static ThingDef PauldronsBlackGuard, PauldronsDragonPrince;

        // Hoods
        private static ThingDef HoodArmored, Hood, HoodMasked;

        // Robes, each paired with the ONE crown that belongs to it.
        private static readonly List<ThingDef> Robes = new List<ThingDef>();
        private static readonly Dictionary<ThingDef, ThingDef> CrownFor = new Dictionary<ThingDef, ThingDef>();

        // Bladedancer / Witch
        private static ThingDef BD_Helm, BD_Skin, BD_Middle, BD_Shell;
        private static ThingDef Witch_Helm, Witch_Skin, Witch_Armor;

        // ---- ARTIFICER / GUILD MASTER WARDROBE ----
        // Imperial Attires + Cross Bikini (Cat) and Medieval Coastal Outfits (Gerrymon),
        // integrated so the two "office" castes stop looking identical to each other and to
        // every other Avir. Self-disabling per-piece via Wear()'s null check, same as
        // everything else in this file - if one of these mods isn't installed, that branch's
        // roll just skips the missing piece rather than erroring.
        //
        // FEMALE base layer (pick one, never mixed with another base):
        //   - Imperial dancer top + bottom
        //   - Cross bikini top + bottom
        //   - Witch undergarment ALONE - no Witch_Armor over it here, unlike every other
        //     caste that wears this piece.
        // Then always a cape OR half-cape (Coastal), and sometimes the BASIC elven hood
        // (never the armored one - that belongs to Warrior-line castes).
        //
        // MALE base layer (pick one): coastal adventurer outfit, or coastal shirt. Then the
        // same cape/half-cape roll, and sometimes the basic hood.
        //
        // These pieces are their OWN set - never combined with Gravetender robes, Tunic/
        // Gambeson, or any Elves warrior-line armor. They are an ADDITIONAL option, not a
        // replacement - both castes still roll the classic uniform sometimes too.
        private static ThingDef ImperialDancerTop, ImperialDancerBottom;
        private static ThingDef CrossBikiniTop, CrossBikiniBottom;
        private static ThingDef CoastalAdventurerOutfit, CoastalShirt;
        private static ThingDef CoastalCape, CoastalHalfCape;

        // The Guild Master's classic uniform. Grey, sober, unmistakable across a trade floor -
        // still in the rotation, just no longer the ONLY thing a guild master can wear.
        private static ThingDef GravetenderRobes;
        private static readonly Color GuildGrey = new Color(87f/255f, 87f/255f, 90f/255f);

        // ---- PROFANED (BotchJob.Profaned) - micro-integration, self-disabling ----
        // Pauldrons are SHELL layer, same as body armour. They are therefore worn LAST, and
        // CanWearTogether silently skips them if they would displace the armour. The armour
        // always wins; the pauldron is a bonus or it is nothing.
        private static ThingDef PauldronWeathered, PauldronHeadhunter;

        // STRIDER PALETTE. Fixed, not rolled - every Strider, male or female, wears exactly
        // this pair: pale lilac-white on cloak/hood/pauldron, slate on the rest of the armour.
        private static readonly Color StriderCloak = new Color(244f/255f, 230f/255f, 255f/255f);
        private static readonly Color StriderMetal = new Color(137f/255f, 137f/255f, 166f/255f);
        /// <summary>Share of Striders in the original white (the pure A'Shaign path); the rest wear a Dynasty's colours.</summary>
        private const float StriderWhiteChance = 0.4f;

        // Bows. Strider-only - the one caste in the nesting that fights at range instead of
        // closing the distance. Self-disabling like everything else: if Elves has no bow by
        // this name, the pool stays empty and the Strider simply carries nothing.
        private static readonly List<ThingDef> Bows = new List<ThingDef>();

        // Weapons
        private static readonly List<ThingDef> Weapons = new List<ThingDef>();
        private static readonly List<ThingDef> WeaponsFine = new List<ThingDef>();
        private static readonly List<ThingDef> Staves = new List<ThingDef>();
        private static ThingDef Greataxe;

        static AvirElvesGear()
        {
            FeatheredFur = Find("Leather_AvirFeatheredFur");
            Cloudcloth   = Find("Cloudcloth");
            Essentium    = Find("Essentium");
            Tisserium    = Find("BloodstoneTisserium");

            Tunic     = Find("DevDesigner_WoodElves_Tunic");
            Scalemail = Find("DevDesigner_WoodElves_Scalemail");
            Chainmail = Find("DevDesigner_Elves_Chainmail");
            Gambeson  = Find("DevDesigner_Elves_Gambeson");

            LeatherArmorWood = Find("DevDesigner_WoodElves_LeatherArmor");
            LeatherArmorDark = Find("DevDesigner_DarkElves_LeatherArmor");
            Dreadspear       = Find("DevDesigner_DarkElves_DreadspearArmor");
            StagKnight       = Find("DevDesigner_WoodElves_StagKnightArmor");
            BlackGuard       = Find("DevDesigner_DarkElves_BlackGuardArmor");

            CloakHigh       = Find("DevDesigner_HighElves_Cloak");
            CapeDark        = Find("DevDesigner_DarkElves_Cape");

            // Regal only.
            CloakBlackGuard       = Find("DevDesigner_DarkElves_BlackGuardCloak");
            PauldronsBlackGuard   = Find("DevDesigner_DarkElves_BlackGuardPauldrons");
            PauldronsDragonPrince = Find("DevDesigner_HighElves_DragonPrincePauldrons");

            HoodArmored = Find("DevDesigner_WoodElves_ArmoredHood");
            Hood        = Find("DevDesigner_WoodElves_Hood");
            HoodMasked  = Find("DevDesigner_WoodElves_MaskedHood");

            // A crown belongs to a robe. A sorceress wears the SORCERESS crown - not Morathi's.
            Pair("DevDesigner_DarkElves_SorceressRobe", "DevDesigner_DarkElves_SorceressHelmet");
            Pair("DevDesigner_DarkElves_MorathiRobe",   "DevDesigner_DarkElves_MorathiHelmet");
            Pair("DevDesigner_DarkElves_MorathiArmor",  "DevDesigner_DarkElves_MorathiHelmet");
            Pair("DevDesigner_DarkElves_WitchArmor",    "DevDesigner_DarkElves_WitchHelmet");
            Pair("DevDesigner_WoodElves_ShamanRobe",    "DevDesigner_WoodElves_ShamanCrown");

            BD_Helm   = Find("DevDesigner_WoodElves_BladeDancerHelmetA");
            BD_Skin   = Find("DevDesigner_WoodElves_Bikini");
            BD_Middle = Find("DevDesigner_WoodElves_Lioncloth");
            BD_Shell  = Find("DevDesigner_WoodElves_Corset");

            Witch_Helm  = Find("DevDesigner_DarkElves_WitchHelmet");
            Witch_Skin  = Find("DevDesigner_DarkElves_WitchBikini");
            Witch_Armor = Find("DevDesigner_DarkElves_WitchArmor");

            Add(Weapons, "DevDesigner_WoodElves_Sword");
            Add(Weapons, "DevDesigner_WoodElves_Sword");
            Add(Weapons, "DevDesigner_WoodElves_Axe");
            Add(Weapons, "DevDesigner_WoodElves_Spear");
            Add(Weapons, "DevDesigner_DarkElves_Sword");
            Add(Weapons, "DevDesigner_DarkElves_Dagger");
            Add(Weapons, "DevDesigner_DarkElves_Spear");

            Add(WeaponsFine, "DevDesigner_WoodElves_Greatsword");
            Add(WeaponsFine, "DevDesigner_WoodElves_Greataxe");
            Add(WeaponsFine, "DevDesigner_DarkElves_Greatsword");
            Add(WeaponsFine, "DevDesigner_DarkElves_Halberd");

            Add(Staves, "DevDesigner_WoodElves_ShamanStaff");
            Add(Staves, "DevDesigner_WoodElves_AlarielleStaff");

            Greataxe = Find("DevDesigner_WoodElves_Greataxe");

            Add(Bows, "DevDesigner_WoodElves_Longbow");
            Add(Bows, "DevDesigner_WoodElves_Greatbow");
            Add(Bows, "DevDesigner_WoodElves_RecurveBow");
            Add(Bows, "DevDesigner_DarkElves_Longbow");

            PauldronWeathered  = Find("BotchJob_WeatheredPauldron");
            PauldronHeadhunter = Find("BotchJob_HeadhunterPauldron");
            GravetenderRobes   = Find("BotchJob_GravetenderRobes");

            ImperialDancerTop    = Find("CAT_Apparel_ImperialDancerTop");
            ImperialDancerBottom = Find("CAT_Apparel_ImperialDancerBottom");
            CrossBikiniTop       = Find("CAT_Apparel_CrossBikiniTop");
            CrossBikiniBottom    = Find("CAT_Apparel_CrossBikiniBottom");
            CoastalAdventurerOutfit = Find("MCO_AdventurerOutfit");
            CoastalShirt            = Find("MCO_SimpleShirt");
            CoastalCape             = Find("MCO_CoastalCape");
            CoastalHalfCape         = Find("MCO_CoastalHalfCape");

            // A'SCHIALIS LEGION wardrobe defs (Ignivar / Divine Order / Cold One / GB suits).
            // Resolved in the partial file; all self-disabling via GetNamedSilentFail.
            InitAShialis();

            // Make A'Shaign's essentium/tisserium elven weapons colorable so their metal can be
            // forced to the nesting's own hue at generation (SetColor in RecolorAShaignWeapon).
            // Done on the DEFS so the colour survives save/load. Stuff is left untouched.
            MakeElvenWeaponsColorable();

            Active = Weapons.Count > 0 && LeatherArmorWood != null;
            if (Active) Log.Message("[Avir] Elves (Continued) detected - Avir gear integration active.");
        }

        private static ThingDef Find(string n) { return DefDatabase<ThingDef>.GetNamedSilentFail(n); }

        // ============================================================
        //  A'SHAIGN WEAPON RECOLOUR
        //
        //  The elven melee/ranged weapons are made from stuff and draw in the stuff's colour, so
        //  an essentium blade reads yellow and a tisserium one reads red - both jarring next to
        //  the nesting's Blood/Lilac palette. Vanilla lets you recolour such a weapon (the "Make
        //  colorable" prompt), which tints the metal WITHOUT changing the material. We do the same
        //  automatically: inject a CompColorable onto the weapon defs at startup, then force the
        //  colour at generation. The STUFF is never touched - it stays essentium/tisserium for all
        //  stats, trade value and smithing; only the drawn tint changes.
        // ============================================================
        private static readonly Color AShaignWeaponColor = new Color(245f / 255f, 231f / 255f, 255f / 255f);

        private static void MakeElvenWeaponsColorable()
        {
            List<ThingDef> all = new List<ThingDef>();
            all.AddRange(Weapons);
            all.AddRange(WeaponsFine);
            all.AddRange(Staves);
            all.AddRange(Bows);
            if (Greataxe != null) all.Add(Greataxe);
            // The young-hunter spears are resolved inline in Arm(), not kept in a pool.
            ThingDef sp1 = Find("DevDesigner_WoodElves_Spear");
            ThingDef sp2 = Find("DevDesigner_DarkElves_Spear");
            if (sp1 != null) all.Add(sp1);
            if (sp2 != null) all.Add(sp2);

            foreach (ThingDef def in all)
            {
                if (def == null) continue;
                if (def.comps == null) def.comps = new List<CompProperties>();
                bool has = false;
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i] != null && def.comps[i].compClass == typeof(CompColorable)) { has = true; break; }
                }
                if (!has) def.comps.Add(new CompProperties { compClass = typeof(CompColorable) });
            }
        }

        /// <summary>
        /// After Arm(): if this is an A'Shaign pawn holding a stuff-made weapon of essentium or
        /// tisserium, force the metal tint to the nesting's hue. A'Schialis is left alone (its own
        /// palette), as are weapons of any other stuff and the folded-light warp blades.
        /// </summary>
        public static void RecolorAShaignWeapon(Pawn pawn)
        {
            if (pawn == null || pawn.equipment == null) return;
            if (IsAShialis(pawn)) return;                       // A'Shaign only

            ThingWithComps wep = pawn.equipment.Primary;
            if (wep == null || wep.def == null || !wep.def.MadeFromStuff) return;
            if (wep.Stuff != Essentium && wep.Stuff != Tisserium) return;

            CompColorable col = wep.TryGetComp<CompColorable>();
            if (col != null) col.SetColor(AShaignWeaponColor);
        }

        private static void Add(List<ThingDef> l, string n)
        {
            ThingDef d = Find(n);
            if (d != null) l.Add(d);
        }

        private static void Pair(string robe, string crown)
        {
            ThingDef r = Find(robe), c = Find(crown);
            if (r == null) return;
            Robes.Add(r);
            if (c != null) CrownFor[r] = c;
        }

        private static ThingDef Pick(List<ThingDef> p)
        {
            return p == null || p.Count == 0 ? null : p[Rand.Range(0, p.Count)];
        }

        /// <summary>
        /// THE HOOD BUG. I had this backwards.
        ///
        /// Elves marks female-only pieces "(F)". It marks SOME dual-texture pieces "(T,M,F)" -
        /// but not all of them. The hoods are labelled plain "elven armored hood", with no
        /// suffix whatsoever. My test REQUIRED the "(T,M,F)" string, so it rejected every hood
        /// for every male - which is exactly why no male warrior has ever worn one.
        ///
        /// The correct test is the other way round: reject only what is explicitly female-only.
        /// Absence of a marker means the piece is fine for anyone.
        /// </summary>
        private static bool MaleSafe(ThingDef d)
        {
            if (d == null || d.label == null) return true;
            if (d.label.Contains("T,M,F")) return true;      // explicitly dual-texture
            return !d.label.Contains("(F)");                 // otherwise: only (F) is barred
        }

        private static bool IsHulk(Pawn p)
        {
            return p.story != null && p.story.bodyType != null && p.story.bodyType.defName == "Hulk";
        }

        private static ThingDef StuffFor(ThingDef ap, bool warTier)
        {
            if (ap == null || ap.stuffCategories == null) return null;

            foreach (StuffCategoryDef c in ap.stuffCategories)
            {
                if (c == StuffCategoryDefOf.Leathery && FeatheredFur != null) return FeatheredFur;
                if (c == StuffCategoryDefOf.Fabric && Cloudcloth != null) return Cloudcloth;
                if (c == StuffCategoryDefOf.Metallic)
                {
                    // Artificers and Guild Masters never touch tisserium. It is the war-metal.
                    // NoTisserium additionally bars it for the ENTIRE A'Schialis faction - the
                    // Legion will not carry a metal forged by the kin it is at war with.
                    if (warTier && !NoTisserium && Tisserium != null) return Tisserium;
                    return Essentium;
                }
            }
            return null;
        }

        /// <summary>
        /// Young hunters are capped at GOOD. Nothing they carry is Excellent or Masterwork -
        /// they have not earned the artificers' best work yet, and everyone in the nesting knows
        /// it. This is set by CapGood, which overrides everything else.
        /// </summary>
        private static bool CapGood;

        /// <summary>
        /// The palette the CURRENT pawn was dressed in. Dress() runs immediately before Arm()
        /// in the same postfix, so this is a safe hand-off - and it lets the weapon be matched
        /// to the outfit. A blood-cloaked talon carries a scarlet blade; a lilac one carries
        /// duskglass or moonrend. Nobody carries a weapon that clashes with their own cloak.
        /// </summary>
        private static Pawn palettePawn;
        private static string paletteName = "";

        private static void SetQuality(Thing t, bool masterwork)
        {
            CompQuality c = t.TryGetComp<CompQuality>();
            if (c == null) return;

            QualityCategory q;

            if (CapGood) q = QualityCategory.Good;
            else if (masterwork) q = QualityCategory.Masterwork;
            else
            {
                float r = Rand.Value;
                q = r < 0.45f ? QualityCategory.Good
                  : r < 0.85f ? QualityCategory.Excellent
                  : QualityCategory.Masterwork;
            }
            c.SetQuality(q, ArtGenerationContext.Outsider);
        }

        private static Apparel Wear(Pawn pawn, ThingDef def, bool masterwork, bool warTier,
                                    Palette pal, bool isCloakOrHood = false)
        {
            if (def == null || pawn.apparel == null) return null;
            if (pawn.gender != Gender.Female && !MaleSafe(def)) return null;

            foreach (Apparel w in pawn.apparel.WornApparel)
            {
                if (!ApparelUtility.CanWearTogether(def, w.def, pawn.RaceProps.body)) return null;
            }

            ThingDef stuff = StuffFor(def, warTier);
            if (def.MadeFromStuff && stuff == null) return null;

            Apparel a = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
            SetQuality(a, masterwork);

            CompColorable col = a.TryGetComp<CompColorable>();
            if (col != null)
            {
                Color c;
                if (isCloakOrHood)              c = pal.cloak;
                else if (stuff == FeatheredFur) c = pal.leather;
                else if (stuff == Cloudcloth)   c = pal.cloak;
                else                            c = pal.metal;

                col.SetColor(c);
            }

            AvirGearUtil.Weather(a);
            pawn.apparel.Wear(a, false);
            return a;
        }

        /// <summary>Wears a piece in one exact colour, regardless of palette or stuff.</summary>
        private static Apparel WearFixedColor(Pawn pawn, ThingDef def, Color color,
                                              bool masterwork, bool warTier)
        {
            if (def == null || pawn.apparel == null) return null;
            if (pawn.gender != Gender.Female && !MaleSafe(def)) return null;

            foreach (Apparel w in pawn.apparel.WornApparel)
            {
                if (!ApparelUtility.CanWearTogether(def, w.def, pawn.RaceProps.body)) return null;
            }

            ThingDef stuff = StuffFor(def, warTier);
            if (def.MadeFromStuff && stuff == null) return null;

            Apparel a = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
            SetQuality(a, masterwork);

            CompColorable col = a.TryGetComp<CompColorable>();
            if (col != null) col.SetColor(color);

            AvirGearUtil.Weather(a);
            pawn.apparel.Wear(a, false);
            return a;
        }

        /// <summary>
        /// PROFANED PAULDRONS. Warrior-class only - young hunters, warriors, huntresses. Not
        /// predators: they have Warlord plate or a bladedancer's bare shoulders, and neither
        /// wants a pauldron bolted to it.
        ///
        /// Worn LAST, so if the pauldron would displace the body armour, CanWearTogether skips
        /// it and the armour stays. The armour always wins.
        ///
        /// They carry a cloth element, so they take the CLOAK colour.
        /// </summary>
        private static void TryPauldron(Pawn pawn, Palette pal)
        {
            if (PauldronWeathered == null && PauldronHeadhunter == null) return;

            if (!Rand.Chance(0.35f)) return;   // may not at all

            List<ThingDef> pool = new List<ThingDef>();
            if (PauldronWeathered != null) pool.Add(PauldronWeathered);
            if (PauldronHeadhunter != null) pool.Add(PauldronHeadhunter);

            WearFixedColor(pawn, Pick(pool), pal.cloak, false, true);
        }

        /// <summary>
        /// FEMALE Artificer / Guild Master wardrobe. One base layer, never mixed with another
        /// base: imperial dancer (top+bottom), cross bikini (top+bottom), or the witch
        /// undergarment worn ALONE - deliberately without Witch_Armor, unlike every other
        /// caste that touches this piece. Always a cape or half-cape on top, and sometimes the
        /// plain elven hood (not the armored one).
        /// </summary>
        private static void DressArtificerFemale(Pawn pawn, Palette pal)
        {
            float roll = Rand.Value;
            if (roll < 0.40f)
            {
                Wear(pawn, ImperialDancerTop, false, false, pal);
                Wear(pawn, ImperialDancerBottom, false, false, pal);
            }
            else if (roll < 0.70f)
            {
                Wear(pawn, CrossBikiniTop, false, false, pal);
                Wear(pawn, CrossBikiniBottom, false, false, pal);
            }
            else
            {
                Wear(pawn, Witch_Skin, false, false, pal);
            }

            Wear(pawn, Rand.Chance(0.5f) ? CoastalCape : CoastalHalfCape, false, false, pal, true);

            if (Rand.Chance(0.35f))
                Wear(pawn, Hood, false, false, pal, true);
        }

        /// <summary>
        /// MALE Artificer / Guild Master wardrobe. Coastal adventurer outfit or coastal shirt,
        /// a cape or half-cape, and sometimes the plain elven hood (never the armored one -
        /// that stays with the Warrior line).
        /// </summary>
        private static void DressArtificerMale(Pawn pawn, Palette pal)
        {
            Wear(pawn, Rand.Chance(0.5f) ? CoastalAdventurerOutfit : CoastalShirt, false, false, pal);
            Wear(pawn, Rand.Chance(0.5f) ? CoastalCape : CoastalHalfCape, false, false, pal, true);

            if (Rand.Chance(0.35f))
                Wear(pawn, Hood, false, false, pal, true);
        }

        public static void Dress(Pawn pawn)
        {
            if (!Active || pawn == null || pawn.apparel == null) return;

            string kind = pawn.kindDef != null ? pawn.kindDef.defName : "";

            // A'SCHIALIS. Every Legion pawn bans tisserium (the A'Shaign war-metal); the ranks
            // and kept castes the equipment pass redesigned (including Huntress, recoloured in
            // Legion grey/red/gold rather than the A'Shaign Blood/Lilac pair) get a wholly
            // separate wardrobe in Patch_AvirAShialisGear.cs. Any future kept caste with no
            // Legion look yet still falls through to the normal wardrobe below, tisserium-free.
            NoTisserium = IsAShialis(pawn);
            if (NoTisserium && IsAShialisDressedCaste(kind)) { DressAShialis(pawn); return; }
            bool female = pawn.gender == Gender.Female;
            bool young = kind == "Avir_YoungHunter";

            // Young hunters get the drab field palette and are capped at Good quality.
            CapGood = young;

            Palette pal;
            if (young)
            {
                pal = Rand.Chance(0.5f) ? YoungOlive : YoungEarth;
                paletteName = "young";
            }
            else
            {
                // Five palettes, equal odds (20% each). Blood/Lilac are the two existing
                // Dynasty-adjacent looks; Midnight/Teal are Evershadow's pair; Black carries no
                // Dynasty at all and is the one matched to Duskglass (see AvirWarpBlades).
                int roll = Rand.RangeInclusive(0, 4);
                switch (roll)
                {
                    case 0: pal = Blood;    paletteName = "blood";    break;
                    case 1: pal = Lilac;    paletteName = "lilac";    break;
                    case 2: pal = Midnight; paletteName = "midnight"; break;
                    case 3: pal = Teal;     paletteName = "teal";     break;
                    default: pal = Black;   paletteName = "black";    break;
                }
            }
            palettePawn = pawn;

            pawn.apparel.DestroyAll();

            // ---------- HATCHLING: nothing. A chick wears boots, gloves and a belt. ----------
            if (kind == "Avir_Hatchling")
            {
                AvirGearUtil.WearBootsAndGloves(pawn, false);
                return;
            }

            // ---------- ARTIFICER / GUILD MASTER ----------
            // No tisserium anywhere. No weapon at all (see Arm). Plain, essentium-stitched work
            // clothes: these are the makers and the counters, not the killers.
            if (kind == "Avir_GuildMaster")
            {
                // Two full uniforms in rotation, picked whole - never mixed with each other:
                // the classic grey Gravetender set (unisex, as it always was), or the newer
                // imperial/coastal wardrobe (gendered). Still no tisserium, still no weapon.
                if (IsHulk(pawn)) AvirBarbarianGear.DressHulk(pawn);
                else if (Rand.Chance(0.5f))
                {
                    if (GravetenderRobes != null)
                    {
                        WearFixedColor(pawn, GravetenderRobes, GuildGrey, false, false);
                        WearFixedColor(pawn, HoodArmored, GuildGrey, false, false);
                    }
                    else
                    {
                        // Profaned not installed - fall back to plain work clothes.
                        Wear(pawn, Tunic, false, false, pal);
                        Wear(pawn, Gambeson, false, false, pal);
                        Wear(pawn, HoodArmored, false, false, pal, true);
                    }
                }
                else if (female) DressArtificerFemale(pawn, pal);
                else DressArtificerMale(pawn, pal);

                AvirGearUtil.WearBootsAndGloves(pawn, false);
                return;
            }

            if (kind == "Avir_Artificer")
            {
                // Same idea: classic Tunic/Gambeson/Cloak (unisex, as it always was) or the
                // newer imperial/coastal wardrobe (gendered) - one whole set, never mixed.
                if (IsHulk(pawn)) AvirBarbarianGear.DressHulk(pawn);
                else if (Rand.Chance(0.5f))
                {
                    Wear(pawn, Tunic, false, false, pal);
                    Wear(pawn, Gambeson, false, false, pal);
                    Wear(pawn, CloakHigh, false, false, pal, true);
                }
                else if (female) DressArtificerFemale(pawn, pal);
                else DressArtificerMale(pawn, pal);

                AvirGearUtil.WearBootsAndGloves(pawn, false);
                return;
            }

            // ---------- HIGH ARTIFICIERS (Architect / Artist) ----------
            // Fantasy Biotech dedicated torso suit when installed - artificelord suit for the
            // Architect, artificer suit for the Artist. NO HEADGEAR AT ALL for either caste;
            // that is the entire point of this pass, not a roll. Falls back to the old
            // placeholder Elves wardrobe (borrowed from the ordinary Artificer caste) if
            // Fantasy Biotech isn't installed, same self-disabling convention as everything
            // else in this file.
            if (kind == "Avir_Architect" || kind == "Avir_Artist")
            {
                bool isArchitect = kind == "Avir_Architect";

                if (AvirFantasyBiotechGear.Dress(pawn, isArchitect))
                {
                    AvirGearUtil.WearBootsAndGloves(pawn, false);
                    return;
                }

                if (IsHulk(pawn)) AvirBarbarianGear.DressHulk(pawn);
                else
                {
                    Wear(pawn, Tunic, false, false, pal);
                    Wear(pawn, Gambeson, false, false, pal);
                    Wear(pawn, CloakHigh, false, false, pal, true);
                }

                AvirGearUtil.WearBootsAndGloves(pawn, false);
                return;
            }

            // ---------- PRIESTESS & MATRIARCH ----------
            // Robe + THE MATCHING CROWN. Never armour.
            if (kind == "Avir_Priestess" || kind == "Avir_Matriarch")
            {
                ThingDef robe = Pick(Robes);

                // BUG FIX: the Witch Armor is a Shell piece and covers nothing underneath, so a
                // matriarch wearing it came out with a bare midriff. It needs its undergarment
                // beneath it. Worn FIRST, so the layer check lets both through.
                if (robe == Witch_Armor) Wear(pawn, Witch_Skin, true, true, pal);

                Wear(pawn, robe, true, true, pal);

                // The crown that belongs to THIS robe - sorceress crown on a sorceress robe.
                ThingDef crown;
                if (robe != null && CrownFor.TryGetValue(robe, out crown))
                {
                    Wear(pawn, crown, true, true, pal, true);
                }

                // REGALIA. The Black Guard cloak is reserved for these two castes - on a common
                // warrior it looked like stolen finery. Here it looks like rank.
                List<ThingDef> regalCloaks = new List<ThingDef> { CloakHigh, CapeDark, CloakBlackGuard, CloakBlackGuard };
                Wear(pawn, Pick(regalCloaks), true, true, pal, true);

                // Pauldrons, sometimes. Not every matriarch stands on ceremony, but the ones
                // who do are unmistakable across a battlefield.
                if (Rand.Chance(0.45f))
                {
                    List<ThingDef> pauldrons = new List<ThingDef> { PauldronsBlackGuard, PauldronsDragonPrince };
                    Wear(pawn, Pick(pauldrons), true, true, pal, true);
                }

                AvirGearUtil.WearBootsAndGloves(pawn, true);
                return;
            }

            // ---------- PREDATOR ----------
            if (kind == "Avir_Predator")
            {
                if (female)
                {
                    if (Rand.Chance(0.5f))
                    {
                        Wear(pawn, BD_Skin, true, true, pal);
                        Wear(pawn, BD_Middle, true, true, pal);
                        Wear(pawn, BD_Shell, true, true, pal);
                        Wear(pawn, BD_Helm, true, true, pal, true);
                    }
                    else
                    {
                        // Undergarment first - the Witch Armor is a Shell piece and covers
                        // nothing beneath it.
                        Wear(pawn, Witch_Skin, true, true, pal);
                        Wear(pawn, Witch_Armor, true, true, pal);
                        Wear(pawn, Witch_Helm, true, true, pal, true);
                    }
                }
                else
                {
                    // Warlord plate and Warlord helm. Plus an elven tunic underneath.
                    AvirBarbarianGear.DressPredator(pawn);
                    Wear(pawn, Tunic, true, true, pal);
                }

                AvirGearUtil.WearBootsAndGloves(pawn, true);
                return;
            }

            // ---------- STRIDER ----------
            // FIXED PALETTE, not rolled - (244,230,255) on cloak/hood/pauldron, (137,137,166)
            // on the rest. Almost a copy of the Warrior's wardrobe for the male, and the
            // Predator's witch line for the female - but always this one palette, and the
            // Headhunter pauldron is GRANTED, not a 35% roll like everyone else's.
            if (kind == "Avir_Strider")
            {
                // The original Striders wear white and walk the pure A'Shaign path; the rest wear the
                // colours of their Dynasty (the palette rolled above), and their bow matches it (Arm).
                Color cloak = StriderCloak, metal = StriderMetal;
                if (Rand.Chance(StriderWhiteChance)) paletteName = "white";
                else { cloak = pal.cloak; metal = pal.metal; }
                if (female)
                {
                    // Undergarment first - the Witch Armor is a Shell piece and covers
                    // nothing beneath it.
                    WearFixedColor(pawn, Witch_Skin, metal, true, true);
                    WearFixedColor(pawn, Witch_Armor, metal, true, true);
                    WearFixedColor(pawn, Hood, cloak, true, true);
                    WearFixedColor(pawn, CapeDark, cloak, true, true);
                }
                else
                {
                    WearFixedColor(pawn, Scalemail, metal, true, true);
                    WearFixedColor(pawn, HoodArmored, cloak, true, true);
                    WearFixedColor(pawn, CloakHigh, cloak, true, true);
                }

                // Headhunter pauldron, always - not the 35% roll TryPauldron gives everyone else.
                WearFixedColor(pawn, PauldronHeadhunter, cloak, true, true);

                AvirGearUtil.WearBootsAndGloves(pawn, true);
                return;
            }

            // ---------- HUNTRESS: bladedancer, head to foot ----------
            if (kind == "Avir_Huntress")
            {
                Wear(pawn, BD_Skin, false, true, pal);
                Wear(pawn, BD_Middle, false, true, pal);
                Wear(pawn, BD_Shell, false, true, pal);
                Wear(pawn, BD_Helm, false, true, pal, true);
                TryPauldron(pawn, pal);
                AvirGearUtil.WearBootsAndGloves(pawn, false);
                return;
            }

            // ---------- YOUNG HUNTER & WARRIOR ----------
            // Both ALWAYS get a hood. Young hunters get ONLY the wood elven cloak or the dark
            // elven cape - never the Cold One Knight cloak, which is a thing you have to earn.
            if (IsHulk(pawn))
            {
                AvirBarbarianGear.DressHulk(pawn);
                AvirGearUtil.WearBootsAndGloves(pawn, false);
                return;
            }

            // A young hunter's tunic takes the CLOAK colour, not the leather colour - the whole
            // silhouette needs to read as one drab shape.
            Wear(pawn, Tunic, false, true, pal, young);
            Wear(pawn, Rand.Chance(0.7f) ? Scalemail : Chainmail, false, true, pal);

            List<ThingDef> shell = young
                ? new List<ThingDef> { LeatherArmorWood, LeatherArmorWood, LeatherArmorDark }
                : new List<ThingDef> { LeatherArmorWood, Dreadspear, StagKnight, BlackGuard };

            Wear(pawn, Pick(shell), false, true, pal);

            // Cloak: the wood elven cloak or the dark elven cape. That is the entire list, for
            // young hunters AND warriors. The Black Guard cloak belongs to the matriarchs now.
            Wear(pawn, Rand.Chance(0.5f) ? CloakHigh : CapeDark, false, true, pal, true);

            // HOOD - always. Males: armored hood. Females: hood or masked hood.
            ThingDef hood = female
                ? (Rand.Chance(0.5f) ? Hood : HoodMasked)
                : HoodArmored;

            Wear(pawn, hood, false, true, pal, true);

            // Pauldron last - so the armour can never be displaced by it.
            TryPauldron(pawn, pal);

            AvirGearUtil.WearBootsAndGloves(pawn, false);
        }

        public static void Arm(Pawn pawn)
        {
            if (!Active || pawn == null || pawn.equipment == null) return;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return;

            string kind = pawn.kindDef != null ? pawn.kindDef.defName : "";

            pawn.equipment.DestroyAllEquipment();

            // A'SCHIALIS. No tisserium weapons; every centurion carries a Fyrlance (and CE fuel).
            // Legionnaire / Inquisitor / Highlord fall through to the generic pool below, which
            // now resolves to essentium because NoTisserium is set.
            NoTisserium = IsAShialis(pawn);
            if (NoTisserium && kind == "Avir_Centurion") { ArmCenturion(pawn); return; }

            // Hatchlings, artificers, guild masters and the High Artificers carry NOTHING. A
            // maker's hands are for making. Most High Artificer adulthoods already disable the
            // Violent WorkTag (which the check above would catch), but the Weaponsmith line
            // does not - and no gear has been designed for this lineage yet regardless, so both
            // pawnkinds are excluded outright until that design pass happens.
            if (kind == "Avir_Hatchling" || kind == "Avir_Artificer" || kind == "Avir_GuildMaster"
                || kind == "Avir_Architect" || kind == "Avir_Artist")
                return;

            // STRIDER: a bow, and nothing else. Unlike every other caste in the nesting, a
            // Strider does not close the distance - no warp blade, no elven sword, no
            // tisserium greataxe. The Cosmic Odyssey's Artemis warbow takes priority when it
            // is installed; otherwise this falls back to an elven bow. If Elves has no bow
            // under these names either, the Strider simply carries none; the point stands
            // either way, the belt and the eye do the rest.
            if (kind == "Avir_Strider")
            {
                // the Strider's own longbow in the colours of the Dynasty it wears (white - any of the four)
                string pn = (palettePawn == pawn) ? paletteName : "white";
                if (Artifactorics.StriderBowGear.Arm(pawn, pn)) return;
                if (AvirCosmicOdyssey.TryArmStrider(pawn)) return;

                ThingDef bow = Pick(Bows);
                if (bow == null) return;

                ThingDef bowStuff = bow.MadeFromStuff ? (Tisserium ?? Essentium) : null;
                if (bow.MadeFromStuff && bowStuff == null) return;

                ThingWithComps b = (ThingWithComps)ThingMaker.MakeThing(bow, bowStuff);
                SetQuality(b, true);
                AvirGearUtil.Weather(b);
                pawn.equipment.AddEquipment(b);
                return;
            }

            // OPTIONAL: folded light instead of forged metal. Predators 40%, huntresses 30%,
            // warriors 20% - and when they take one, it is usually a glaive.
            // Match the blade to the cloak. A male predator in Barbarians plate has no elven
            // palette, so he draws from the whole rack.
            string pal = (palettePawn == pawn) ? paletteName : "";
            if (AvirWarpBlades.TryArm(pawn, kind == "Avir_Predator", pal)) return;

            // Predator: a tisserium wood elven greataxe. No rolls.
            if (kind == "Avir_Predator" && Greataxe != null && Tisserium != null)
            {
                ThingWithComps ax = (ThingWithComps)ThingMaker.MakeThing(
                    Greataxe, Greataxe.MadeFromStuff ? Tisserium : null);
                SetQuality(ax, true);
                AvirGearUtil.Weather(ax);
                pawn.equipment.AddEquipment(ax);
                return;
            }

            // YOUNG HUNTER: an essentium spear, or nothing at all. No swords, no axes, no
            // tisserium. A spear is what you give someone whose job is to keep the thing at
            // arm's length until an adult arrives.
            if (kind == "Avir_YoungHunter")
            {
                if (Rand.Chance(0.30f)) return;   // a fair few carry nothing but beak and claws

                ThingDef spear = Rand.Chance(0.6f)
                    ? DefDatabase<ThingDef>.GetNamedSilentFail("DevDesigner_WoodElves_Spear")
                    : DefDatabase<ThingDef>.GetNamedSilentFail("DevDesigner_DarkElves_Spear");

                if (spear == null || Essentium == null) return;

                ThingWithComps sp = (ThingWithComps)ThingMaker.MakeThing(
                    spear, spear.MadeFromStuff ? Essentium : null);

                CompQuality sq = sp.TryGetComp<CompQuality>();
                if (sq != null) sq.SetQuality(QualityCategory.Good, ArtGenerationContext.Outsider);

                AvirGearUtil.Weather(sp);
                pawn.equipment.AddEquipment(sp);
                return;
            }

            bool staffBearer = kind == "Avir_Priestess" || kind == "Avir_Matriarch";
            bool fine = kind == "Avir_Warrior" || kind == "Avir_Huntress";

            // MASTERWORK WITHOUT THE FINE POOL. The Legionnaire's pawnkind def declares
            // <itemQuality>Masterwork</itemQuality>, but that value never applied - weaponMoney
            // is 0 and the weapon comes from here instead, so the apex rank of the Legion was
            // rolling the same Good-and-up spread as a centurion. It is honoured now.
            //
            // Deliberately NOT folded into `fine`: that flag also unlocks the WeaponsFine pool
            // and TISSERIUM, and no A'Schialis pawn may ever hold tisserium. Quality only.
            bool masterworkWeapon = fine || kind == "Avir_Legionnaire";

            List<ThingDef> pool = new List<ThingDef>();
            if (staffBearer) pool.AddRange(Staves);
            else
            {
                pool.AddRange(Weapons);
                if (fine) pool.AddRange(WeaponsFine);
            }

            ThingDef chosen = Pick(pool);
            if (chosen == null) return;

            ThingDef stuff = null;
            if (chosen.MadeFromStuff)
            {
                stuff = ((fine || staffBearer) && Tisserium != null) ? Tisserium : Essentium;
                if (stuff == null) return;
            }

            ThingWithComps w = (ThingWithComps)ThingMaker.MakeThing(chosen, stuff);
            SetQuality(w, masterworkWeapon || staffBearer);
            AvirGearUtil.Weather(w);
            pawn.equipment.AddEquipment(w);
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirElvesGear
    {
        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || !AvirFaction.IsAvirPawnKind(pawn)) return;

                // The player's own Avir are never dressed by us. They start naked and
                // empty-handed on purpose.
                if (AvirFaction.IsPlayerAvir(pawn)) return;
                // The Primal are naked and weaponless by nature.
                if (AvirFaction.IsPrimal(pawn)) return;

                if (!AvirElvesGear.Active)
                {
                    AvirBarbarianGear.DressHulk(pawn);
                    AvirGearUtil.WearBootsAndGloves(pawn, false);
                    // without the Elves wardrobe a Strider still carries its own bow (white - the pure A'Shaign path)
                    if (pawn.kindDef?.defName == "Avir_Strider" && pawn.equipment != null && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        pawn.equipment.DestroyAllEquipment();
                        Artifactorics.StriderBowGear.Arm(pawn, "white");
                    }
                }
                else
                {
                    AvirElvesGear.Dress(pawn);
                    AvirElvesGear.Arm(pawn);
                    AvirElvesGear.RecolorAShaignWeapon(pawn);
                }

                // Artifactorics: amulet on the neck, artifact in the torso garment (or weapon /
                // inventory) - after the wardrobe, which strips and redresses the pawn.
                Artifactorics.AvirFactionGear.Equip(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Elves gear integration failed: " + e, 0x4A51C1);
            }
        }
    }

    /// <summary>
    /// HOSPITALITY VISITOR DRESSER.
    /// Hospitality's SpawnGroupUtility.GenerateNewGearFor() calls vanilla RedressPawn, which
    /// strips the Avir gear our generator applied and replaces it with random apparel. We
    /// postfix that method and redress any Avir pawn it just processed. We bind the pawn by
    /// POSITION (__0) rather than name, since the compiled method has no readable param names.
    /// Self-disabling if Hospitality isn't loaded.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_HospitalityAvirDress
    {
        static Patch_HospitalityAvirDress()
        {
            if (!ModsConfig.IsActive("Orion.Hospitality")) return;
            try
            {
                Assembly hosp = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "Hospitality") { hosp = a; break; }
                if (hosp == null) return;

                Type util = hosp.GetType("Hospitality.Utilities.SpawnGroupUtility");
                if (util == null) return;

                var h = new Harmony("yourname.avir.hospitality.dress");
                int patched = 0;

                // Primary: GenerateNewGearFor calls RedressPawn (the actual stripper).
                MethodInfo gen = AccessTools.Method(util, "GenerateNewGearFor");
                if (gen != null)
                {
                    h.Patch(gen, postfix: new HarmonyMethod(
                        typeof(Patch_HospitalityAvirDress), nameof(Postfix)));
                    patched++;
                }

                // Backup: SetupAsVisitor runs after gear on the spawn path.
                MethodInfo setup = AccessTools.Method(util, "SetupAsVisitor");
                if (setup != null)
                {
                    h.Patch(setup, postfix: new HarmonyMethod(
                        typeof(Patch_HospitalityAvirDress), nameof(Postfix)));
                    patched++;
                }

                Log.Message($"[Avir] Hospitality visitor redress hook active ({patched} hook(s)).");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Hospitality dress hook failed: " + e.Message);
            }
        }

        // Bind by position: first argument of both methods is the Pawn.
        public static void Postfix(object __0)
        {
            try
            {
                Pawn pawn = __0 as Pawn;
                if (pawn == null || !AvirFaction.IsAvirPawnKind(pawn)) return;
                if (AvirFaction.IsPlayerAvir(pawn)) return;
                if (AvirFaction.IsPrimal(pawn)) return;

                if (!AvirElvesGear.Active)
                {
                    AvirBarbarianGear.DressHulk(pawn);
                    AvirGearUtil.WearBootsAndGloves(pawn, false);
                    // without the Elves wardrobe a Strider still carries its own bow (white - the pure A'Shaign path)
                    if (pawn.kindDef?.defName == "Avir_Strider" && pawn.equipment != null && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        pawn.equipment.DestroyAllEquipment();
                        Artifactorics.StriderBowGear.Arm(pawn, "white");
                    }
                }
                else
                {
                    AvirElvesGear.Dress(pawn);
                    AvirElvesGear.Arm(pawn);
                    AvirElvesGear.RecolorAShaignWeapon(pawn);
                }

                // Artifactorics: amulet on the neck, artifact in the torso garment (or weapon /
                // inventory) - after the wardrobe, which strips and redresses the pawn.
                Artifactorics.AvirFactionGear.Equip(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] HospitalityDress failed: " + e, 0x4A51C3);
            }
        }
    }
}
