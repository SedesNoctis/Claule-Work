using RimWorld;
using Verse;

namespace Avir.Linarian;

public static class LinarianBeastPane
{
	public static bool off;

	public static bool Hers(IInspectPane pane)
	{
		if (off)
		{
			return false;
		}
		try
		{
			return pane is MainTabWindow_Inspect && Find.Selector?.SingleSelectedThing is Pawn { Spawned: not false } pawn && LinarianPresence.Disguised(pawn);
		}
		catch
		{
			return false;
		}
	}

	public static bool Hers()
	{
		if (!off && Find.Selector?.SingleSelectedThing is Pawn { Spawned: not false } pawn)
		{
			return LinarianPresence.Disguised(pawn);
		}
		return false;
	}
}
