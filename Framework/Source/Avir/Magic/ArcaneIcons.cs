using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The arcane orders' own pictures (the author, Fix161): UI/Designators/Avir/&lt;name&gt; - the button, and the same
    /// picture as the mark on the marked cells. Till a picture is there, the game's own stands in.</summary>
    [StaticConstructorOnStartup]
    public static class ArcaneIcons
    {
        public const string Folder = "UI/Designators/Avir/";
        public static string Name(string defName) => defName == null ? null : defName.StartsWith("Avir_2") ? defName.Substring(6) : defName.StartsWith("Avir_Arcane") ? defName.Substring(11) : defName.StartsWith("Avir_") ? defName.Substring(5) : defName;

        public static Texture2D For(string defName, string fallback)
        {
            string n = Name(defName);
            Texture2D t = n != null ? ContentFinder<Texture2D>.Get(Folder + n, false) : null;
            return t ?? ContentFinder<Texture2D>.Get(fallback, false) ?? ContentFinder<Texture2D>.Get("UI/Designators/Harvest", false)
                ?? ContentFinder<Texture2D>.Get("UI/Designators/SmoothSurface", false) ?? BaseContent.BadTex;
        }

        static ArcaneIcons()
        {
            var field = AccessTools.Field(typeof(DesignationDef), "iconMat");
            if (field == null) return;
            foreach (DesignationDef d in DefDatabase<DesignationDef>.AllDefsListForReading.Where(x => x.defName.StartsWith("Avir_")))
            {
                string n = Name(d.defName);
                if (ContentFinder<Texture2D>.Get(Folder + n, false) == null) continue;
                d.texturePath = Folder + n;
                field.SetValue(d, MaterialPool.MatFrom(Folder + n, ShaderDatabase.MetaOverlay));
            }
        }
    }
}
