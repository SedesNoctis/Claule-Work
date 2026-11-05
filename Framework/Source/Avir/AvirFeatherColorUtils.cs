using System;
using System.Globalization;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>Brightness and hex helpers for the feather colour picker. Copied from the Kharassian scale picker (KharassianColorUtils), itself ported from Anthrosonae's Utils.</summary>
    public static class AvirFeatherColorUtils
    {
        public static int BrightnessLevel(Color color)
        {
            float luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            return Mathf.RoundToInt(luminance * 100f);
        }

        public static Color SetBrightness(Color color, int targetLevel)
        {
            targetLevel = Mathf.Clamp(targetLevel, 1, 100);
            int current = BrightnessLevel(color);
            if (current == targetLevel || current == 0)
            {
                return color;
            }
            float factor = (targetLevel / 100f) / (current / 100f);
            return new Color(
                Mathf.Clamp01(color.r * factor),
                Mathf.Clamp01(color.g * factor),
                Mathf.Clamp01(color.b * factor),
                color.a);
        }

        public static bool TryGetColorFromHex(string hex, out Color color)
        {
            color = Color.white;
            if (hex.NullOrEmpty())
            {
                return false;
            }
            if (hex.StartsWith("#"))
            {
                hex = hex.Substring(1);
            }
            if (hex.Length != 6 && hex.Length != 8)
            {
                return false;
            }
            try
            {
                int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                int a = hex.Length == 8 ? int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber) : 255;
                color = GenColor.FromBytes(r, g, b, a);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
