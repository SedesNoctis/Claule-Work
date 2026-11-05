using System.Collections.Generic;
using Verse;

namespace Avir;

public class GameComponent_AvirTonics : GameComponent
{
	private HashSet<int> allowed = new HashSet<int>();

	public static GameComponent_AvirTonics Get => Current.Game?.GetComponent<GameComponent_AvirTonics>();

	public GameComponent_AvirTonics(Game game)
	{
	}

	public static bool Allowed(Pawn p)
	{
		return Get?.allowed.Contains(p.thingIDNumber) ?? false;
	}

	public static void Set(Pawn p, bool on)
	{
		GameComponent_AvirTonics get = Get;
		if (get != null)
		{
			if (on)
			{
				get.allowed.Add(p.thingIDNumber);
			}
			else
			{
				get.allowed.Remove(p.thingIDNumber);
			}
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref allowed, "allowedTonics", LookMode.Value);
		if (allowed == null)
		{
			allowed = new HashSet<int>();
		}
	}
}
