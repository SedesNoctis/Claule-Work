using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

public static class StriderPaths
{
	public const string Hunt = "Hunt";

	public const string Storm = "Storm";

	public const string Flame = "Flame";

	public const string Moon = "Moon";

	public const string Gravity = "Gravity";

	public static readonly string[] All = new string[5] { "Hunt", "Storm", "Flame", "Moon", "Gravity" };

	public const int MaxStones = 18;

	public static readonly int[] Nodes = new int[3] { 3, 9, 18 };

	public static string Label(string path)
	{
		return path switch
		{
			"Hunt" => "the Hunt", 
			"Storm" => "the Storm", 
			"Flame" => "the Flame", 
			"Moon" => "the Moon (A'Shaign)", 
			"Gravity" => "Weight", 
			_ => "no path", 
		};
	}

	public static string Summary(string path)
	{
		return path switch
		{
			"Hunt" => "Against the living. 3: wounds that bleed. 9: the wounded and beasts take more. 18: Judgement - an arrow that weighs everything its target has ever done to your people.", 
			"Storm" => "EMP and lightning. 3: EMP on every hit. 9: the strike leaps to another enemy. 18: Sky Strike - lightning where you point.", 
			"Flame" => "Fire. 3: sets the target alight. 9: a kill bursts into flame. 18: Fire Rain - burning arrows over an area.", 
			"Moon" => "The A'Shaign path. 3: stronger from the dark. 9: the arrow goes through into the one behind. 18: Moon Arrow - a shot across the whole map that no cover stops.", 
			"Gravity" => "Weight. 3: knocks back. 9: slows and staggers. 18: Gravity Knot - pulls enemies into one point.", 
			_ => "", 
		};
	}

	public static string[][] Powers(string path)
	{
		return path switch
		{
			"Hunt" => new string[3][]
			{
				new string[2] { "wounds that bleed", "Every arrow leaves a wound that will not close: the target bleeds on long after the hit." },
				new string[2] { "the hunter's eye", "The wounded and the beasts take more from every arrow: the bow finishes what it starts." },
				new string[2] { "Judgement", "One arrow that weighs everything its target has ever done to your people: the more harm it has dealt them, the harder it strikes." }
			}, 
			"Storm" => new string[3][]
			{
				new string[2] { "the charged string", "Every hit bursts in a small EMP: shields fail, machines stagger." },
				new string[2] { "the leaping strike", "The strike leaps from the target to another enemy nearby." },
				new string[2] { "Sky Strike", "Lightning falls where the archer points, and the storm of it blinds every machine around." }
			}, 
			"Flame" => new string[3][]
			{
				new string[2] { "the burning arrow", "The arrow sets its target alight." },
				new string[2] { "the pyre", "A kill bursts into flame and catches those around it." },
				new string[2] { "Fire Rain", "Burning arrows rain over an area." }
			}, 
			"Moon" => new string[3][]
			{
				new string[2] { "out of the dark", "Shot from the dark, or into it, the arrow strikes harder." },
				new string[2] { "through and on", "The arrow goes through its target into the one behind." },
				new string[2] { "Moon Arrow", "A shot across the whole map: no cover, no distance stops it." }
			}, 
			"Gravity" => new string[3][]
			{
				new string[2] { "the heavy arrow", "The arrow knocks its target back." },
				new string[2] { "the weight", "The struck are slowed and staggered." },
				new string[2] { "Gravity Knot", "Every enemy around the point is pulled into it and staggered." }
			}, 
			_ => new string[0][], 
		};
	}

	public static float XpFor(int stones)
	{
		float num = 0f;
		for (int i = 1; i <= stones; i++)
		{
			num += StoneXp(i);
		}
		return num;
	}

	public static IEnumerable<string> Allowed(CompStriderBow b)
	{
		return All;
	}

	public static bool Favoured(CompStriderBow b, string path)
	{
		if (path == "Moon")
		{
			return true;
		}
		if (b.whiteRobe)
		{
			return false;
		}
		switch (b.Props.dynasty)
		{
		case "Shadowsong":
			return path == "Storm";
		case "Bloodmoon":
			if (!(path == "Hunt"))
			{
				return path == "Flame";
			}
			return true;
		case "Evershadow":
			if (!(path == "Gravity"))
			{
				return path == "Storm";
			}
			return true;
		default:
			return false;
		}
	}

	public static float Affinity(CompStriderBow b)
	{
		if (b.path == null)
		{
			return 0f;
		}
		if (b.whiteRobe)
		{
			if (!(b.path == "Moon"))
			{
				return 1f;
			}
			return 1.4f;
		}
		if (b.path == "Moon")
		{
			return 1.15f;
		}
		if (!Favoured(b, b.path))
		{
			return 1f;
		}
		return 1.25f;
	}

	public static string Lore(string path)
	{
		return path switch
		{
			"Hunt" => "The oldest craft of the Striders. A bow of the Hunt learns the living: where they bleed, how they run, and what they have done. Its archers walk ahead of the host and are seldom seen before the hunt is over.", 
			"Storm" => "Arrows that carry the anger of the sky: every note of the string is a held breath of lightning. Archers of the Storm break shields and machines first, and the rest of a battle follows.", 
			"Flame" => "The bow of the Flame leaves little behind it but ash. Its archers set the field alight and let the fire finish what the arrow began.", 
			"Moon" => "The A'Shaign path, every bow's birthright: arrows of the Night herself, strongest in the dark, stopped by nothing raised against them.", 
			"Gravity" => "A quiet discipline: the arrow carries the weight of a mountain. Foes are thrown down, held and slowed, and a battle is decided before it has begun.", 
			_ => "", 
		};
	}

	public static float StoneXp(int k)
	{
		return (ArtifactoricsSettingsDef.Main?.bowStoneXp ?? 6000f) + (ArtifactoricsSettingsDef.Main?.bowStoneXpStep ?? 1500f) * (float)(k - 1);
	}

	public static int StonesFor(float xp)
	{
		int i = 0;
		for (float num = 0f; i < 18 && xp >= num + StoneXp(i + 1); i++)
		{
			num += StoneXp(i + 1);
		}
		return i;
	}

	public static CompStriderBow Drawn(Pawn p)
	{
		CompStriderBow compStriderBow = CompStriderBow.Of(p?.equipment?.Primary);
		if (compStriderBow == null || compStriderBow.bonded != p)
		{
			return null;
		}
		return compStriderBow;
	}

	public static float Power(CompStriderBow b, Pawn archer)
	{
		int num = archer?.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 8;
		return Affinity(b) * (0.5f + (float)num / 20f);
	}
}
