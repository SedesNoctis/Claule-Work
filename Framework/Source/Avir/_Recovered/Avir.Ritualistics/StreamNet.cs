using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Avir.Ritualistics;

public class StreamNet
{
	public int id;

	public readonly HashSet<IntVec3> cells = new HashSet<IntVec3>();

	public readonly List<Thing> nodes = new List<Thing>();

	public IEnumerable<CompEnergyKeeper> Keepers => from n in nodes
		select n.TryGetComp<CompEnergyKeeper>() into k
		where k != null
		select k;

	public float Stored => Keepers.Sum((CompEnergyKeeper k) => k.stored);

	public float Capacity => Keepers.Sum((CompEnergyKeeper k) => k.Capacity);
}
