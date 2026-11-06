using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>A rounded, glassy bar (the author: the plain square fill looked flat and lay over the frame): a soft dark
    /// trough, a fill with rounded ends that is darker below and lit above, a bright streak along the top and a glow at its
    /// leading edge. The textures are made once, in code.</summary>
    [StaticConstructorOnStartup]
    public static class GlassBar
    {
        private const int W = 96, H = 32;
        private static Texture2D body, shine;

        private static void Make()
        {
            body = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            shine = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = H / 2f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = 1f - (y + 0.5f) / H;   // Unity's rows go up: v = 0 at the top
                    // the rounded ends: a soft-edged capsule
                    float cx = x + 0.5f < r ? r : x + 0.5f > W - r ? W - r : x + 0.5f;
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - r) * (y + 0.5f - r));
                    float edge = Mathf.Clamp01((r - d) / 2.2f);
                    // volume: lit above, darker below
                    float b = Mathf.Lerp(1f, 0.55f, Mathf.SmoothStep(0.25f, 1f, v));
                    body.SetPixel(x, y, new Color(b, b, b, edge));
                    // the streak of light along the top
                    float s = Mathf.Exp(-Mathf.Pow((v - 0.28f) / 0.09f, 2f));
                    shine.SetPixel(x, y, new Color(1f, 1f, 1f, s * edge * 0.55f));
                }
            body.Apply(); shine.Apply();
        }

        private static void Capsule(Rect r, Texture2D t)
        {
            float cap = Mathf.Min(r.height * 0.5f, r.width * 0.5f), u = (H * 0.5f) / W;
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, cap, r.height), t, new Rect(0f, 0f, u, 1f));
            if (r.width > 2f * cap) GUI.DrawTextureWithTexCoords(new Rect(r.x + cap, r.y, r.width - 2f * cap, r.height), t, new Rect(u, 0f, 1f - 2f * u, 1f));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - cap, r.y, cap, r.height), t, new Rect(1f - u, 0f, u, 1f));
        }

        /// <summary>The bar in r, filled to frac with col. trough: how dark the empty part is; a: how solid the fill.</summary>
        public static void Draw(Rect r, float frac, Color col, float trough = 0.75f, float a = 1f)
        {
            if (body == null) Make();
            Color keep = GUI.color;
            GUI.color = new Color(0.02f, 0.02f, 0.03f, trough);
            Capsule(r, body);
            frac = Mathf.Clamp01(frac);
            if (frac > 0.002f)
            {
                Rect f = new Rect(r.x, r.y, Mathf.Max(r.height * 0.6f, r.width * frac), r.height);
                GUI.color = new Color(col.r, col.g, col.b, a);
                Capsule(f, body);
                GUI.color = new Color(1f, 1f, 1f, a);
                Capsule(new Rect(f.x + 2f, f.y, f.width - 4f, f.height), shine);
                // the leading edge glows a little
                GUI.color = new Color(Mathf.Min(1f, col.r + 0.4f), Mathf.Min(1f, col.g + 0.4f), Mathf.Min(1f, col.b + 0.4f), 0.5f * a);
                Capsule(new Rect(f.xMax - r.height * 0.7f, f.y + r.height * 0.1f, r.height * 0.7f, r.height * 0.8f), shine);
            }
            GUI.color = keep;
        }
    }
}
