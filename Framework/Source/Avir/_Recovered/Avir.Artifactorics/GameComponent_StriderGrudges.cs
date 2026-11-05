using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

public class GameComponent_StriderGrudges : GameComponent
{
	public static GameComponent_StriderGrudges Instance;

	private Dictionary<string, float> dealt = new Dictionary<string, float>();

	public GameComponent_StriderGrudges(Game game)
	{
		Instance = this;
	}

	private static string Key(Pawn attacker, Faction victims)
	{
		return attacker.thingIDNumber + ":" + victims.loadID;
	}

	public void Add(Pawn attacker, Faction victims, float amount)
	{
		string key = Key(attacker, victims);
		dealt[key] = (dealt.TryGetValue(key, out var value) ? value : 0f) + amount;
	}

	public float Dealt(Pawn attacker, Faction victims)
	{
		if (attacker != null && victims != null)
		{
			if (!dealt.TryGetValue(Key(attacker, victims), out var value))
			{
				return 0f;
			}
			return value;
		}
		return 0f;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref dealt, "avirGrudges", LookMode.Value, LookMode.Value);
		if (dealt == null)
		{
			dealt = new Dictionary<string, float>();
		}
		Instance = this;
	}
}
