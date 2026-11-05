using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    [DefOf]
    public static class ArtifactoricsDefOf
    {
        public static ThingDef Avir_ArtifactSetting;
        public static ThingDef Avir_Artifact;
        public static ThingDef Avir_ArtificerSpot;

        static ArtifactoricsDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactoricsDefOf));
        }
    }

    [StaticConstructorOnStartup]
    public static class ArtifactoricsTex
    {
        private const string Root = "UI/Artifactorics/";
        public static readonly Texture2D Panel = ContentFinder<Texture2D>.Get(Root + "Panel");
        public static readonly Texture2D Parchment = ContentFinder<Texture2D>.Get(Root + "ParchmentTile");
        /// <summary>The parchment laid tile by tile across a rect. Each tile is drawn inside the picture's own
        /// bounds: 1.7 hands textures over clamped, and one stretched over UVs past 1 smeared into streaks.</summary>
        public static void TileParchment(Rect r)
        {
            const float tile = 512f;
            for (float y = r.y; y < r.yMax - 0.5f; y += tile)
                for (float x = r.x; x < r.xMax - 0.5f; x += tile)
                {
                    float w = Mathf.Min(tile, r.xMax - x), h = Mathf.Min(tile, r.yMax - y);
                    // UV origin is bottom-left: a cut tile shows the picture's top
                    GUI.DrawTextureWithTexCoords(new Rect(x, y, w, h), Parchment, new Rect(0f, 1f - h / tile, w / tile, h / tile));
                }
        }
        public static readonly Texture2D HeartGuides = ContentFinder<Texture2D>.Get(Root + "HeartGuides");
        public static readonly Texture2D HeartSetting = ContentFinder<Texture2D>.Get(Root + "Heart_Setting");
        public static readonly Texture2D HeartRim = ContentFinder<Texture2D>.Get(Root + "Heart_Rim");
        public static readonly Texture2D HeartRimMask = ContentFinder<Texture2D>.Get(Root + "Heart_RimMask");
        public static readonly Texture2D HeartSettingMask = ContentFinder<Texture2D>.Get(Root + "Heart_SettingMask");
        public static readonly Texture2D GemBase = ContentFinder<Texture2D>.Get(Root + "Heart_GemBase");
        public static readonly Texture2D GemHighlight = ContentFinder<Texture2D>.Get(Root + "Heart_GemHighlight");
        public static readonly Texture2D ButtonNormal = ContentFinder<Texture2D>.Get(Root + "Button_Normal");
        public static readonly Texture2D ButtonHover = ContentFinder<Texture2D>.Get(Root + "Button_Hover");
        public static readonly Texture2D ButtonPressed = ContentFinder<Texture2D>.Get(Root + "Button_Pressed");
        public static readonly Texture2D PointSeal = ContentFinder<Texture2D>.Get(Root + "PointSeal");
        public static readonly Texture2D IconCreate = ContentFinder<Texture2D>.Get(Root + "Icon_CreateArtifact");
        public static readonly Texture2D IconSetHeart = ContentFinder<Texture2D>.Get(Root + "Icon_SetHeart");
        public static readonly Texture2D IconDevelop = ContentFinder<Texture2D>.Get(Root + "PointSeal_Large");

        // spiral
        public static readonly Texture2D NodeLocked = ContentFinder<Texture2D>.Get(Root + "Node_Locked");
        public static readonly Texture2D NodeAvailable = ContentFinder<Texture2D>.Get(Root + "Node_Available");
        public static readonly Texture2D NodePurchased = ContentFinder<Texture2D>.Get(Root + "Node_Purchased");
        public static readonly Texture2D NodeExcluded = ContentFinder<Texture2D>.Get(Root + "Node_Excluded");
        public static readonly Texture2D MilestoneLocked = ContentFinder<Texture2D>.Get(Root + "Milestone_Locked");
        public static readonly Texture2D MilestoneAvailable = ContentFinder<Texture2D>.Get(Root + "Milestone_Available");
        public static readonly Texture2D MilestonePurchased = ContentFinder<Texture2D>.Get(Root + "Milestone_Purchased");
        public static readonly Texture2D LinkPurchased = ContentFinder<Texture2D>.Get(Root + "Link_Purchased");
        public static readonly Texture2D LinkUnpurchased = ContentFinder<Texture2D>.Get(Root + "Link_Unpurchased");
        public static readonly Texture2D CircleTelekinesis = ContentFinder<Texture2D>.Get(Root + "Circle_Telekinesis");
        public static readonly Texture2D CircleAbilities = ContentFinder<Texture2D>.Get(Root + "Circle_Abilities");
        public static readonly Texture2D AmuletNecklace = ContentFinder<Texture2D>.Get(Root + "Amulet_Necklace", false);
        public static readonly Texture2D AmuletGem = ContentFinder<Texture2D>.Get(Root + "Amulet_Gem", false);
        public static readonly Texture2D StoneBase = ContentFinder<Texture2D>.Get(Root + "Stone_Base");
        public static readonly Texture2D StoneHighlight = ContentFinder<Texture2D>.Get(Root + "Stone_Highlight");

        /// <summary>Soft round glow, drawn by code (tinted per use).</summary>
        public static readonly Texture2D Glow = MakeRadial(128, false);
        /// <summary>Thin soft ring for the selected cell.</summary>
        public static readonly Texture2D Ring = MakeRadial(128, true);
        /// <summary>Crisp filled disc and a crisp bold ring (amulet stones): tinted by GUI.color.</summary>
        public static readonly Texture2D Disc = MakeDisc(96, 0f);
        /// <summary>Where the five sockets of Circle_Telekinesis really are: offsets from the centre
        /// in fractions of the drawn width (top first, clockwise). Measured on the shipped 512px
        /// texture (square crop of the circle sheet) - the picture does not space them evenly.</summary>
        public static readonly Vector2[] TkSockets =
        {
            new Vector2(0.0013f, -0.3654f),
            new Vector2(0.3517f, -0.1058f),
            new Vector2(0.2896f, 0.2501f),
            new Vector2(-0.2411f, 0.3019f),
            new Vector2(-0.3508f, -0.1059f),
        };
        public static Vector2 TkSocketPos(Rect c, int i, int slots)
        {
            if (i < TkSockets.Length) return c.center + TkSockets[i] * c.width;
            float ang = (-90f + i * 360f / slots) * Mathf.Deg2Rad;
            return c.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * c.width * 0.34f;
        }
        /// <summary>The three stone pictures of the amulet pattern (opened / next / locked).</summary>
        public static readonly Texture2D StoneOpened = ContentFinder<Texture2D>.Get(Root + "Amulet/Stone_Opened", false);
        public static readonly Texture2D StoneAvailable = ContentFinder<Texture2D>.Get(Root + "Amulet/Stone_Available", false);
        public static readonly Texture2D StoneLocked = ContentFinder<Texture2D>.Get(Root + "Amulet/Stone_Locked", false);
        public static readonly Texture2D RingBold = MakeDisc(96, 0.72f);

        private static Texture2D MakeDisc(int size, float hole)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            float c = (size - 1) / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float outer = Mathf.Clamp01(c - d + 0.5f);                 // anti-aliased edge
                    float inner = hole > 0f ? Mathf.Clamp01(d - c * hole + 0.5f) : 1f;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(255f * outer * inner));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        static ArtifactoricsTex()
        {
            if (Parchment != null) Parchment.wrapMode = TextureWrapMode.Repeat;
            if (LinkPurchased != null) LinkPurchased.wrapMode = TextureWrapMode.Repeat;
            if (LinkUnpurchased != null) LinkUnpurchased.wrapMode = TextureWrapMode.Repeat;
        }

        private static Texture2D MakeRadial(int size, bool ring)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            float c = (size - 1) / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;   // 0 centre .. 1 edge
                    float a;
                    if (ring)
                    {
                        float k = Mathf.Abs(d - 0.86f) / 0.07f;
                        a = Mathf.Clamp01(1f - k * k);
                    }
                    else
                    {
                        a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }

    /// <summary>Lookups shared by the recipe workers, the bill and the creation panel.</summary>
    public static class ArtifactCraftUtility
    {
        private static Dictionary<ArtifactMetalDef, RecipeDef> settingRecipes;
        private static Dictionary<ArtifactMetalDef, RecipeDef> heartRecipes;
        private static Dictionary<ArtifactMetalDef, RecipeDef> stoneRecipes;

        private static void EnsureCache()
        {
            if (settingRecipes != null) return;
            settingRecipes = new Dictionary<ArtifactMetalDef, RecipeDef>();
            heartRecipes = new Dictionary<ArtifactMetalDef, RecipeDef>();
            stoneRecipes = new Dictionary<ArtifactMetalDef, RecipeDef>();
            foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                ArtifactRecipeExtension ext = r.GetModExtension<ArtifactRecipeExtension>();
                if (ext?.metal == null) continue;
                if (ext.step != ArtifactCraftStep.Setting && ext.step != ArtifactCraftStep.Heart && ext.step != ArtifactCraftStep.Stone) continue;   // amulet steps: AvirAmuletCraft
                (ext.step == ArtifactCraftStep.Setting ? settingRecipes : ext.step == ArtifactCraftStep.Stone ? stoneRecipes : heartRecipes)[ext.metal] = r;
            }
        }

        public static RecipeDef StoneRecipe(ArtifactMetalDef m)
        {
            EnsureCache();
            return m != null && stoneRecipes.TryGetValue(m, out RecipeDef r) ? r : null;
        }

        public static RecipeDef SettingRecipe(ArtifactMetalDef m)
        {
            EnsureCache();
            return m != null && settingRecipes.TryGetValue(m, out RecipeDef r) ? r : null;
        }

        public static RecipeDef HeartRecipe(ArtifactMetalDef m)
        {
            EnsureCache();
            return m != null && heartRecipes.TryGetValue(m, out RecipeDef r) ? r : null;
        }

        /// <summary>How much of the metal the recipe consumes.</summary>
        public static int MetalCount(RecipeDef r, ArtifactMetalDef m)
        {
            if (r == null || m?.material == null) return 0;
            // CountRequiredOfFor knows about small-volume resources (gold, silver): 1.6 -> 16 units.
            foreach (IngredientCount ic in r.ingredients)
                if (ic.filter.Allows(m.material)) return ic.CountRequiredOfFor(m.material, r, null);
            return 0;
        }

        public static int MinSkill(RecipeDef r)
        {
            if (r?.skillRequirements == null || r.skillRequirements.Count == 0) return 0;
            return r.skillRequirements.Max(s => s.minLevel);
        }

        public static int InStock(Map map, ArtifactMetalDef m)
        {
            if (map == null || m?.material == null) return 0;
            return map.resourceCounter.GetCount(m.material);
        }

        /// <summary>Skill a jeweller brings to a setting: mean of crafting and artistic.</summary>
        public static int SettingLevel(Pawn p)
        {
            if (p?.skills == null) return 0;
            int c = p.skills.GetSkill(SkillDefOf.Crafting).Level;
            int a = p.skills.GetSkill(SkillDefOf.Artistic).Level;
            return Mathf.RoundToInt((c + a) / 2f);
        }

        public static int HeartLevel(Pawn p)
        {
            return p?.skills == null ? 0 : p.skills.GetSkill(SkillDefOf.Intellectual).Level;
        }

        private static bool CanWork(Pawn p, WorkTypeDef w)
        {
            return p?.skills != null && (w == null || !p.WorkTypeIsDisabled(w));
        }

        /// <summary>Best colonist on the map who meets the recipe's skill requirements.</summary>
        public static Pawn BestFor(Map map, RecipeDef r, ArtifactCraftStep step)
        {
            if (map == null || r == null) return null;
            Pawn best = null; int bestLevel = -1;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (!CanWork(p, r.requiredGiverWorkType)) continue;
                if (r.FirstSkillRequirementPawnDoesntSatisfy(p) != null) continue;
                int lv = step == ArtifactCraftStep.Setting || step == ArtifactCraftStep.Necklace ? SettingLevel(p) : HeartLevel(p);
                if (lv > bestLevel) { bestLevel = lv; best = p; }
            }
            return best;
        }

        public static Building_ArtificerSpot NearestSpot(Thing near)
        {
            if (near?.Map == null) return null;
            Building_ArtificerSpot best = null; float bestD = float.MaxValue;
            foreach (Building_ArtificerSpot s in near.Map.listerBuildings.AllBuildingsColonistOfClass<Building_ArtificerSpot>())
            {
                float d = (s.Position - near.Position).LengthHorizontalSquared;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>Quality of one half of the work. Creative inspiration counts and is used up,
        /// as with any vanilla craft.</summary>
        public static QualityCategory RollQuality(Pawn p, int level)
        {
            bool inspired = p.InspirationDef == InspirationDefOf.Inspired_Creativity;
            QualityCategory q = CompArtificerTable.Lift(p, QualityUtility.GenerateQualityCreatedByPawn(level, inspired));
            if (inspired) p.mindState.inspirationHandler.EndInspiration(InspirationDefOf.Inspired_Creativity);
            return q;
        }

        public static void Place(Thing product, Pawn doer)
        {
            if (!GenPlace.TryPlaceThing(product, doer.Position, doer.Map, ThingPlaceMode.Near))
                Log.Error("[Avir] Artifactorics: could not place " + product);
        }
    }

    /// <summary>Jeweller's half: makes the setting with its rim. No vanilla products - the worker
    /// creates the item itself so it can roll quality from crafting AND artistic.</summary>
    public class RecipeWorker_ArtifactSetting : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            ArtifactRecipeExtension ext = recipe.GetModExtension<ArtifactRecipeExtension>();
            if (ext?.metal == null || billDoer?.Map == null) return;

            Thing setting = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_ArtifactSetting);
            CompArtifact c = setting.TryGetComp<CompArtifact>();
            c.rimMetal = ext.metal;
            c.settingQuality = ArtifactCraftUtility.RollQuality(billDoer, ArtifactCraftUtility.SettingLevel(billDoer));
            c.settingMaker = billDoer.LabelShort;

            // workSkill (crafting) is trained by the bill itself; artistic gets the same amount here.
            billDoer.skills?.Learn(SkillDefOf.Artistic, recipe.workAmount * 0.1f);

            ArtifactCraftUtility.Place(setting, billDoer);
            Messages.Message(billDoer.LabelShort + " finished " + setting.LabelNoCount + " (" + c.settingQuality.GetLabel() + ").",
                setting, MessageTypeDefOf.TaskCompletion, false);
        }
    }

    /// <summary>Designer's half: sets the heart into a setting and turns it into an artifact.</summary>
    public class RecipeWorker_ArtifactHeart : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            ArtifactRecipeExtension ext = recipe.GetModExtension<ArtifactRecipeExtension>();
            if (ext?.metal == null || billDoer?.Map == null) return;

            // The setting was consumed as an ingredient a moment ago; destroyed things keep their
            // comps, so its state is still readable here.
            CompArtifact from = null;
            foreach (Thing t in ingredients)
            {
                CompArtifact ca = t.TryGetComp<CompArtifact>();
                if (ca != null && !ca.HasHeart) { from = ca; break; }
            }
            if (from == null)
            {
                Log.Error("[Avir] Artifactorics: heart recipe " + recipe.defName + " finished without a setting among its ingredients.");
                return;
            }

            Thing artifact = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_Artifact);
            CompArtifact c = artifact.TryGetComp<CompArtifact>();
            c.CopyFrom(from);
            c.heartMetal = ext.metal;
            c.heartQuality = ArtifactCraftUtility.RollQuality(billDoer, ArtifactCraftUtility.HeartLevel(billDoer));
            c.heartMaker = billDoer.LabelShort;

            ArtifactCraftUtility.Place(artifact, billDoer);
            Messages.Message(billDoer.LabelShort + " set the heart: " + artifact.LabelNoCount + " (heart " + c.heartQuality.GetLabel() + ").",
                artifact, MessageTypeDefOf.PositiveEvent, false);
        }
    }

    /// <summary>A heart order that only takes a setting with the chosen rim, so "silver rim +
    /// gold heart" never grabs a steel-rimmed setting by accident. Orders added from the vanilla
    /// bills tab are plain bills and take any setting.</summary>
    public class Bill_ArtifactHeart : Bill_ProductionWithUft
    {
        public ArtifactMetalDef requiredRim;

        public Bill_ArtifactHeart()
        {
        }

        public Bill_ArtifactHeart(RecipeDef recipe, ArtifactMetalDef rim) : base(recipe, null)
        {
            requiredRim = rim;
        }

        public override bool IsFixedOrAllowedIngredient(Thing t)
        {
            CompArtifact c = t.TryGetComp<CompArtifact>();
            if (c != null)
            {
                if (c.HasHeart) return false;
                if (requiredRim != null && c.rimMetal != requiredRim) return false;
            }
            return base.IsFixedOrAllowedIngredient(t);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref requiredRim, "requiredRim");
        }
    }

    /// <summary>Artificer spot: a plain crafting spot plus the creation panel.</summary>
    public class Building_ArtificerSpot : Building_WorkTable
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Create artifact...",
                    defaultDesc = "Open the artificer's sheet: choose a rim and a heart and order the work here.",
                    icon = ArtifactoricsTex.IconCreate,
                    action = () => Find.WindowStack.Add(new Dialog_ArtifactCreation(this, null))
                };
            }
        }
    }
}
