using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics;

public class MapComponent_StreamNetwork : MapComponent
{
	private readonly HashSet<IntVec3> wires = new HashSet<IntVec3>();

	private readonly HashSet<IntVec3> conduct = new HashSet<IntVec3>();

	private readonly Dictionary<IntVec3, StreamNet> netAt = new Dictionary<IntVec3, StreamNet>();

	private readonly List<StreamNet> nets = new List<StreamNet>();

	private bool dirty = true;

	private static readonly Material[] Pulse = new Material[8];

	private static readonly Material[] NodePulse = new Material[8];

	public int WireCount => wires.Count;

	public MapComponent_StreamNetwork(Map map)
		: base(map)
	{
	}

	public static MapComponent_StreamNetwork Of(Map map)
	{
		return map?.GetComponent<MapComponent_StreamNetwork>();
	}

	public void AddWire(IntVec3 c)
	{
		wires.Add(c);
		dirty = true;
	}

	public void RemoveWire(IntVec3 c)
	{
		wires.Remove(c);
		dirty = true;
	}

	public void SetDirty()
	{
		dirty = true;
	}

	public bool HasWire(IntVec3 c)
	{
		return wires.Contains(c);
	}

	public override void MapComponentTick()
	{
		if (Find.TickManager.TicksGame % 250 != 0)
		{
			return;
		}
		dirty = true;
		Rebuild();
		if (nets.Count == 0)
		{
			return;
		}
		foreach (StreamNet net in nets)
		{
			Share(net);
		}
	}

	private static void Share(StreamNet net)
	{
		List<CompEnergyKeeper> list = net.Keepers.Where((CompEnergyKeeper k) => k.parent.Spawned).ToList();
		if (list.Count == 0)
		{
			return;
		}
		float num = list.Sum((CompEnergyKeeper k) => k.stored);
		float num2 = list.Sum((CompEnergyKeeper k) => k.Capacity);
		if (num <= 0.01f || num2 <= 0f)
		{
			return;
		}
		float num3 = Mathf.Min(num, 3f * (float)list.Count);
		foreach (Thing node in net.nodes)
		{
			if (num3 <= 0.01f)
			{
				break;
			}
			if (node.Spawned && node.TryGetComp<CompArtifactHost>() != null)
			{
				float num4 = CompRitualCircle.GiveTo(node, num3);
				num3 -= num4;
				num -= num4;
			}
		}
		float num5 = Mathf.Clamp01(num / num2);
		foreach (CompEnergyKeeper item in list)
		{
			float num6 = num5 * item.Capacity;
			if (num6 > item.stored)
			{
				item.Give(num6 - item.stored);
			}
			else
			{
				item.Take(item.stored - num6);
			}
		}
	}

	private void Rebuild()
	{
		dirty = false;
		nets.Clear();
		netAt.Clear();
		int num = 0;
		conduct.Clear();
		conduct.UnionWith(wires);
		foreach (Building item in map.listerBuildings.allBuildingsColonist)
		{
			if (!StreamNetwork.IsNode(item))
			{
				continue;
			}
			foreach (IntVec3 item2 in item.OccupiedRect())
			{
				conduct.Add(item2);
			}
		}
		foreach (IntVec3 item3 in conduct)
		{
			if (netAt.ContainsKey(item3))
			{
				continue;
			}
			StreamNet streamNet = new StreamNet
			{
				id = num++
			};
			Stack<IntVec3> stack = new Stack<IntVec3>();
			stack.Push(item3);
			netAt[item3] = streamNet;
			while (stack.Count > 0)
			{
				IntVec3 intVec = stack.Pop();
				streamNet.cells.Add(intVec);
				for (int i = 0; i < 4; i++)
				{
					IntVec3 intVec2 = intVec + GenAdj.CardinalDirections[i];
					if (conduct.Contains(intVec2) && !netAt.ContainsKey(intVec2))
					{
						netAt[intVec2] = streamNet;
						stack.Push(intVec2);
					}
				}
			}
			HashSet<Thing> hashSet = new HashSet<Thing>();
			foreach (IntVec3 cell in streamNet.cells)
			{
				for (int j = -1; j < 4; j++)
				{
					IntVec3 c = ((j < 0) ? cell : (cell + GenAdj.CardinalDirections[j]));
					if (!c.InBounds(map))
					{
						continue;
					}
					List<Thing> list = map.thingGrid.ThingsListAtFast(c);
					for (int k = 0; k < list.Count; k++)
					{
						if (StreamNetwork.IsNode(list[k]) && hashSet.Add(list[k]))
						{
							streamNet.nodes.Add(list[k]);
						}
					}
				}
			}
			nets.Add(streamNet);
		}
	}

	public StreamNet NetOf(Thing t)
	{
		if (t == null || !t.Spawned)
		{
			return null;
		}
		if (dirty)
		{
			Rebuild();
		}
		if (nets.Count == 0)
		{
			return null;
		}
		foreach (IntVec3 item in t.OccupiedRect())
		{
			for (int i = -1; i < 4; i++)
			{
				IntVec3 key = ((i < 0) ? item : (item + GenAdj.CardinalDirections[i]));
				if (netAt.TryGetValue(key, out var value) && value.nodes.Contains(t))
				{
					return value;
				}
			}
		}
		return null;
	}

	private static Material PulseMat(float b)
	{
		int num = Mathf.Clamp(Mathf.FloorToInt(b * 8f), 0, 7);
		if (Pulse[num] == null)
		{
			float num2 = (float)(num + 1) / 8f;
			Pulse[num] = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Transparent, new Color(0.62f + 0.3f * num2, 0.3f + 0.3f * num2, 1f, 0.35f + 0.55f * num2));
		}
		return Pulse[num];
	}

	private bool ShouldShow()
	{
		if (Find.CurrentMap != map || conduct.Count == 0)
		{
			return false;
		}
		if (Find.DesignatorManager.SelectedDesignator is Designator_Build { PlacingDef: ThingDef placingDef } && (placingDef.thingClass == typeof(Building_StreamWire) || StreamNetwork.IsNodeDef(placingDef)))
		{
			return true;
		}
		foreach (object selectedObject in Find.Selector.SelectedObjects)
		{
			if (selectedObject is Thing thing && (thing is Building_StreamWire || StreamNetwork.IsNode(thing)))
			{
				return true;
			}
		}
		return false;
	}

	public override void MapComponentUpdate()
	{
		if (!ShouldShow())
		{
			return;
		}
		if (dirty)
		{
			Rebuild();
		}
		float y = AltitudeLayer.MetaOverlays.AltitudeFor();
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		int num = 0;
		foreach (IntVec3 item in conduct)
		{
			if (++num > 6000)
			{
				break;
			}
			float b = 0.5f + 0.5f * Mathf.Sin(realtimeSinceStartup * 4f - (float)(item.x + item.z) * 0.7f);
			Material material = PulseMat(b);
			Vector3 vector = item.ToVector3Shifted();
			vector.y = y;
			bool flag = wires.Contains(item);
			Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(vector, Quaternion.identity, flag ? new Vector3(0.22f, 1f, 0.22f) : new Vector3(0.9f, 1f, 0.9f)), flag ? material : NodeMat(b), 0);
			if (conduct.Contains(item + IntVec3.East))
			{
				Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(vector + new Vector3(0.5f, 0f, 0f), Quaternion.identity, new Vector3(1f, 1f, 0.09f)), material, 0);
			}
			if (conduct.Contains(item + IntVec3.North))
			{
				Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(vector + new Vector3(0f, 0f, 0.5f), Quaternion.identity, new Vector3(0.09f, 1f, 1f)), material, 0);
			}
		}
	}

	private static Material NodeMat(float b)
	{
		int num = Mathf.Clamp(Mathf.FloorToInt(b * 8f), 0, 7);
		if (NodePulse[num] == null)
		{
			float num2 = (float)(num + 1) / 8f;
			NodePulse[num] = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Transparent, new Color(0.62f + 0.3f * num2, 0.3f + 0.3f * num2, 1f, 0.1f + 0.12f * num2));
		}
		return NodePulse[num];
	}
}
