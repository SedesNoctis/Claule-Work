using Verse;

namespace Avir;

public sealed class Avir_TeleTransfer
{
	public Thing thing;

	public IntVec3 sourceCell;

	public IntVec3 destination;

	public Thing destinationBench;

	public int count;

	public Avir_TeleTransfer(Thing thing, IntVec3 destination, int count)
	{
		this.thing = thing;
		sourceCell = thing.Position;
		this.destination = destination;
		this.count = count;
	}

	public Avir_TeleTransfer(Thing thing, Thing destinationBench, IntVec3 destination, int count)
	{
		this.thing = thing;
		sourceCell = thing.Position;
		this.destinationBench = destinationBench;
		this.destination = destination;
		this.count = count;
	}
}
