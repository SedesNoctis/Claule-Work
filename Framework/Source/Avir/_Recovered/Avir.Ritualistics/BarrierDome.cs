using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics;

public class BarrierDome
{
	public static readonly List<BarrierDome> All = new List<BarrierDome>();

	public Map map;

	public IntVec3 center;

	public float radius;

	public bool sealsVacuum;

	public object group;

	public Func<Projectile, bool> hit;

	public bool InGroup(Vector3 v)
	{
		if (group != null)
		{
			return All.Any((BarrierDome o) => o.group == group && o.map == map && o.Inside(v));
		}
		return Inside(v);
	}

	public static bool Covers(Map map, IntVec3 c)
	{
		return All.Any((BarrierDome d) => d.map == map && (float)(c - d.center).LengthHorizontalSquared <= d.radius * d.radius);
	}

	public static bool Seals(Map map, IntVec3 c)
	{
		for (int i = 0; i < All.Count; i++)
		{
			BarrierDome barrierDome = All[i];
			if (barrierDome.sealsVacuum && barrierDome.map == map && (float)(c - barrierDome.center).LengthHorizontalSquared <= barrierDome.radius * barrierDome.radius)
			{
				return true;
			}
		}
		return false;
	}

	public bool Inside(Vector3 v)
	{
		return (new Vector3(v.x, 0f, v.z) - center.ToVector3Shifted()).WithY(0f).sqrMagnitude <= radius * radius;
	}
}
