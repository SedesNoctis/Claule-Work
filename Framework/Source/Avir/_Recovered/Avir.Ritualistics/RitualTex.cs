using UnityEngine;
using Verse;

namespace Avir.Ritualistics;

[StaticConstructorOnStartup]
public static class RitualTex
{
	public static readonly Texture2D Ring = ContentFinder<Texture2D>.Get("UI/Ritualistics/Circle/SectionRing", reportFailure: false) ?? MakeRing(512, 0.965f);

	public static readonly Texture2D Rim = ContentFinder<Texture2D>.Get("UI/Ritualistics/Circle/RimRing", reportFailure: false) ?? Ring;

	public const float RimInnerArt = 0.88f;

	public static readonly float RimInner = ((Rim == Ring) ? 1f : 0.88f);

	public static readonly Texture2D Disc = MakeRing(128, 0f);

	public static float RimGrow => 1f / RimInner;

	private static Texture2D MakeRing(int size, float hole)
	{
		Texture2D texture2D = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
		{
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear
		};
		float num = (float)(size - 1) / 2f;
		Color32[] array = new Color32[size * size];
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				float num2 = Mathf.Sqrt(((float)j - num) * ((float)j - num) + ((float)i - num) * ((float)i - num)) / num;
				float num3 = Mathf.Clamp01((1f - num2) * (float)size * 0.5f) * ((hole <= 0f) ? 1f : Mathf.Clamp01((num2 - hole) * (float)size * 0.5f));
				array[i * size + j] = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, (byte)(num3 * 255f));
			}
		}
		texture2D.SetPixels32(array);
		texture2D.Apply();
		return texture2D;
	}
}
