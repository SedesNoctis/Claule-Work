using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Log), "Error", new Type[] { typeof(string) })]
public static class Patch_AvirGuiErrors
{
	private const int Max = 8;

	private static readonly HashSet<string> seen = new HashSet<string>();

	private static string path;

	private static bool broken;

	public static void Prefix(string text)
	{
		if (broken || seen.Count >= 8 || text == null || text.IndexOf("OnGUI", StringComparison.Ordinal) < 0)
		{
			return;
		}
		try
		{
			string item = ((text.Length > 300) ? text.Substring(0, 300) : text);
			if (seen.Add(item))
			{
				if (path == null)
				{
					path = Path.Combine(GenFilePaths.ConfigFolderPath, "Avir_gui_errors.txt");
					File.WriteAllText(path, "Avir GUI errors - AVIR-2026-10-31-MAGIC92 - " + DateTime.Now.ToString() + Environment.NewLine);
				}
				File.AppendAllText(path, Environment.NewLine + "==== " + DateTime.Now.ToString("HH:mm:ss") + " ====" + Environment.NewLine + text + Environment.NewLine);
			}
		}
		catch
		{
			broken = true;
		}
	}
}
