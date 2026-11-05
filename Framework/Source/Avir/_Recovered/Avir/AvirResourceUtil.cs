using System.Reflection;
using HarmonyLib;
using RimWorld;

namespace Avir;

public static class AvirResourceUtil
{
	private static FieldInfo targetField;

	private static bool resolved;

	public static float TargetValue(Gene_Resource gene, float fallback)
	{
		if (gene == null)
		{
			return fallback;
		}
		if (!resolved)
		{
			resolved = true;
			foreach (FieldInfo declaredField in AccessTools.GetDeclaredFields(typeof(Gene_Resource)))
			{
				if (declaredField.FieldType == typeof(float) && declaredField.Name.ToLowerInvariant().Contains("target"))
				{
					targetField = declaredField;
					break;
				}
			}
		}
		if (targetField == null)
		{
			return fallback;
		}
		try
		{
			return (float)targetField.GetValue(gene);
		}
		catch
		{
			return fallback;
		}
	}
}
