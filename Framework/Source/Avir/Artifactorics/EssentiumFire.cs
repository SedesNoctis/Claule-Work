using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // GOLDEN AND SILVER FIRE. The essentium projector's sweep scorches the ground in its own light:
    // the game's own flames (the same shapes, the same flicker), recoloured at startup - gold for the
    // golden beam, silver for moonfire. They burn and spread as fire does, keep their colour when they
    // spread, and colonists fight them as any fire.
    // =====================================================================================

    public class EssentiumFire : Fire
    {
        /// <summary>Start a fire of this kind in a cell (as the game starts any fire there).</summary>
        public static bool StartIn(ThingDef def, IntVec3 c, Map map, float size, Thing instigator)
        {
            if (def == null || map == null || !c.InBounds(map) || FireUtility.ChanceToStartFireIn(c, map) <= 0f) return false;
            var f = (Fire)ThingMaker.MakeThing(def);
            f.fireSize = size;
            f.instigator = instigator;
            GenSpawn.Spawn(f, c, map, Rot4.North);
            return true;
        }
    }

    [DefOf]
    public static class EssentiumFireDefOf
    {
        public static ThingDef Avir_GoldFire;
        public static ThingDef Avir_SilverFire;
        static EssentiumFireDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(EssentiumFireDefOf)); }
    }

    /// <summary>The game's flame frames, recoloured: graphicData color is the bright of the flame, colorTwo its dark.</summary>
    public class Graphic_EssentiumFire : Graphic_Flicker
    {
        public override void Init(GraphicRequest req)
        {
            base.Init(req);
            if (subGraphics == null) return;
            Color light = req.color, dark = req.colorTwo;
            for (int i = 0; i < subGraphics.Length; i++)
            {
                Texture src = subGraphics[i]?.MatSingle?.mainTexture;
                if (src == null) continue;
                Texture2D t = Recolor(src, light, dark);
                if (t != null) subGraphics[i] = new Graphic_Fixed(MaterialPool.MatFrom(new MaterialRequest(t, req.shader)));
            }
        }

        private static Texture2D Recolor(Texture src, Color light, Color dark)
        {
            try
            {
                int w = src.width, h = src.height;
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(src, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = src.name + "_essentium" };
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Color[] px = t.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = px[i];
                    // the flame's brightness (its yellow core high, its red edge low) mapped onto our two colours
                    float v = Mathf.Clamp01(0.25f * c.r + 0.65f * c.g + 0.10f * c.b + 0.15f);
                    Color o = Color.Lerp(dark, light, Mathf.Pow(v, 0.8f));
                    o.a = c.a;
                    px[i] = o;
                }
                t.SetPixels(px);
                t.filterMode = FilterMode.Bilinear;
                t.Apply(true, true);
                return t;
            }
            catch (System.Exception e)
            {
                Log.WarningOnce("[Avir] Could not recolour the fire frames: " + e.Message, 61842211);
                return null;
            }
        }
    }

    /// <summary>One fixed material.</summary>
    public class Graphic_Fixed : Graphic
    {
        private readonly Material mat;
        public Graphic_Fixed(Material mat) { this.mat = mat; }
        public override Material MatSingle => mat;
        public override Material MatAt(Rot4 rot, Thing thing = null) => mat;
    }

    /// <summary>A golden or silver fire spreads as its own kind.</summary>
    [HarmonyPatch(typeof(Fire), "TrySpread")]
    public static class Patch_EssentiumFireSpreads
    {
        [System.ThreadStatic] public static ThingDef spreadAs;
        public static void Prefix(Fire __instance) { spreadAs = __instance is EssentiumFire ? __instance.def : null; }
        public static void Postfix() { spreadAs = null; }
    }

    [HarmonyPatch(typeof(FireUtility), nameof(FireUtility.TryStartFireIn))]
    public static class Patch_EssentiumFireStart
    {
        public static bool Prefix(IntVec3 c, Map map, float fireSize, Thing instigator, ref bool __result)
        {
            ThingDef d = Patch_EssentiumFireSpreads.spreadAs;
            if (d == null) return true;
            __result = EssentiumFire.StartIn(d, c, map, fireSize, instigator);
            return false;
        }
    }

    /// <summary>Colonists fight every kind of fire, ours too.</summary>
    [HarmonyPatch]
    public static class Patch_FightEveryFire
    {
        public static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.PropertyGetter(AccessTools.TypeByName("RimWorld.WorkGiver_FightFires"), "PotentialWorkThingRequest");

        public static void Postfix(ref ThingRequest __result) => __result = ThingRequest.ForGroup(ThingRequestGroup.Fire);
    }
}
