using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Verse;

namespace Avir
{
    /// <summary>
    /// The mod's own UI pictures, read straight from their PNG files. RimWorld 1.7 compresses a mod's textures
    /// into an sRGB format: the light parchment of the artificer and ritual windows came out dark and yellow (the
    /// gamma counted twice). Only UI/ of this mod, and only a texture that came in sRGB or clamped; on 1.6 nothing
    /// is touched. Nothing of the game is patched: run from AvirHarmony's static constructor, when every texture is
    /// loaded, it swaps the entries of the mod's own texture list, and the textures the mod's classes already
    /// fetched in their static fields.
    /// </summary>
    public static class AvirUITextures
    {
        public static void Swap()
        {
            try
            {
                ModContentPack pack = AvirMod.Pack;
                Dictionary<string, Texture2D> list = pack?.GetContentHolder<Texture2D>()?.contentList;
                if (list == null) { Log.Message("[Avir] UI textures: no texture list."); return; }
                var map = new Dictionary<Texture2D, Texture2D>();
                string seen = null;
                foreach (string key in list.Keys.Where(k => k.StartsWith("UI/")).ToList())
                {
                    Texture2D tex = list[key];
                    if (tex == null) continue;
                    if (seen == null || key == "UI/Artifactorics/Panel") seen = key + ": " + tex.graphicsFormat + ", wrap " + tex.wrapMode;
                    if (tex.graphicsFormat.ToString().IndexOf("SRGB", StringComparison.OrdinalIgnoreCase) < 0 && tex.wrapMode == TextureWrapMode.Repeat) continue;
                    Texture2D mine = Read(pack, key);
                    if (mine == null) continue;
                    mine.name = tex.name;
                    list[key] = mine;
                    map[tex] = mine;
                }
                int fields = map.Count > 0 ? Remap(map) : 0;
                Log.Message("[Avir] UI textures: colour space " + QualitySettings.activeColorSpace + ", " + (seen ?? "none") + "; read "
                            + map.Count + " from their PNGs, " + fields + " fields updated.");
            }
            catch (Exception e) { Log.Warning("[Avir] UI textures left as loaded: " + e.Message); }
        }

        private static Texture2D Read(ModContentPack pack, string key)
        {
            foreach (string folder in pack.foldersToLoadDescendingOrder)
            {
                string file = Path.Combine(Path.Combine(folder, "Textures"), key + ".png");
                if (!File.Exists(file)) continue;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
                if (!t.LoadImage(File.ReadAllBytes(file))) { UnityEngine.Object.Destroy(t); return null; }
                t.wrapMode = TextureWrapMode.Repeat;
                t.filterMode = FilterMode.Trilinear;
                // packed again as 1.6 had it (DXT, linear): a fraction of the memory of raw pixels
                if (t.width % 4 == 0 && t.height % 4 == 0 && t.width >= 16 && t.height >= 16) t.Compress(false);
                t.Apply(true, true);
                return t;
            }
            return null;
        }

        /// <summary>Static Texture2D fields (and arrays of them) of the mod's classes that already ran their static
        /// constructors point at the old textures: point them at the new ones. Classes not run yet read the list later.</summary>
        private static int Remap(Dictionary<Texture2D, Texture2D> map)
        {
            int n = 0;
            foreach (Type type in typeof(AvirUITextures).Assembly.GetTypes())
            {
                if (type.ContainsGenericParameters || !type.IsDefined(typeof(StaticConstructorOnStartup), false)) continue;
                foreach (FieldInfo f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    try
                    {
                        if (f.FieldType == typeof(Texture2D))
                        {
                            if (f.GetValue(null) is Texture2D old && map.TryGetValue(old, out Texture2D nu)) { f.SetValue(null, nu); n++; }
                        }
                        else if (f.FieldType == typeof(Texture2D[]) && f.GetValue(null) is Texture2D[] arr)
                        {
                            for (int i = 0; i < arr.Length; i++)
                                if (arr[i] != null && map.TryGetValue(arr[i], out Texture2D nu)) { arr[i] = nu; n++; }
                        }
                    }
                    catch { }
                }
            }
            return n;
        }
    }
}
