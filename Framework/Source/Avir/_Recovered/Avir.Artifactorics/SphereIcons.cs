using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public static class SphereIcons
{
	private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

	public static Texture2D Get(string path)
	{
		if (cache.TryGetValue(path, out var value))
		{
			return value;
		}
		value = ContentFinder<Texture2D>.Get(path, reportFailure: false);
		cache[path] = value;
		return value;
	}

	public static Texture2D Path(string path)
	{
		return Get("UI/Avir/Spheres/Path_" + path);
	}

	public static Texture2D Named(string name)
	{
		return Get("UI/Avir/Spheres/" + name);
	}

	public static Texture2D StriderPath(string path)
	{
		return Get("UI/Avir/Strider/Path_" + path);
	}

	public static Texture2D StriderActive(string path)
	{
		return Get("UI/Avir/Strider/Active_" + path);
	}
}
