using HarmonyLib;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>Every word in a ritual window in italics, as by hand (a setting, on by default).</summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) })]
    public static class Patch_RitualItalic
    {
        public static void Prefix(ref string label)
        {
            if (!FlowStyle.Inside || AvirMod.Settings == null || !AvirMod.Settings.ritualItalic || string.IsNullOrEmpty(label)) return;
            if (label.StartsWith("<i>")) return;
            label = "<i>" + label + "</i>";
        }
    }
}
