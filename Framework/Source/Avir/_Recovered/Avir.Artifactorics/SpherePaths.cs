using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

public static class SpherePaths
{
	public const string Archconstruct = "Archconstruct";

	public const string Snares = "Snares";

	public const string Subspace = "Subspace";

	public const string Creator = "Creator";

	public const string Energy = "Energy";

	public static readonly string[] All = new string[5] { "Archconstruct", "Snares", "Subspace", "Creator", "Energy" };

	public const int MaxStones = 12;

	public static readonly int[] Nodes = new int[4] { 3, 6, 9, 12 };

	private static readonly Dictionary<string, (int tick, bool on)> onMapCache = new Dictionary<string, (int, bool)>();

	public static string Label(string path)
	{
		return path switch
		{
			"Archconstruct" => "the Archconstruct", 
			"Snares" => "the Master of Snares", 
			"Subspace" => "the Lord of Subspace", 
			"Creator" => "the Compulsive Creator", 
			"Energy" => "the Energy Architect", 
			_ => "no path", 
		};
	}

	public static string Summary(string path)
	{
		return path switch
		{
			"Archconstruct" => "the machines answer the architect: they are built, mended and held by the spheres.", 
			"Snares" => "the spheres are laid in the ground as snares that spring when foes come near.", 
			"Subspace" => "everything within the architect's reach is within their hands; the veil swallows shots and pays them back.", 
			"Creator" => "every bench in a room works at once, and the work comes out better, cheaper, sometimes twice.", 
			"Energy" => "the architect pilots the stream: its keepers and the spire feed the spheres, and the spheres raise domes and fire.", 
			_ => "", 
		};
	}

	public static string Lore(string path)
	{
		return path switch
		{
			"Archconstruct" => "Architects of the machine. Their spheres speak to every construct, and the mechanoids they tend work, mend and endure as if a single mind held them all.", 
			"Snares" => "The patient architects. Their spheres sink into the earth as snares, and a field they have prepared is a trap the enemy walks into on its own.", 
			"Subspace" => "Architects of the fold between places. Everything within their reach is within their hands, and the veil they weave swallows whatever is thrown at them.", 
			"Creator" => "The obsessed. A whole room of workbenches answers them at once, and what they make comes out finer, cheaper, and now and then twice.", 
			"Energy" => "Pilots of the stream. Their spheres draw on the keepers and the spire, raise domes against the sky and call fire down on the field.", 
			_ => "", 
		};
	}

	public static string[][] Powers(string path)
	{
		return path switch
		{
			"Archconstruct" => new string[4][]
			{
				new string[2] { "master-constructor", "Every machine the architect tends answers faster: mechanoids are mended and gestated quicker, more of them can be held, and they can be mended from afar." },
				new string[2] { "the lattice", "The architect can mend a mechanoid from across the map." },
				new string[2] { "heart of the machines", "Every mechanoid of the colony on the map works faster and takes blows better; the soulmark binds up to seven constructs instead of three." },
				new string[2] { "the unbound lattice", "For a while, every mechanoid the architect holds is held by the lattice wherever it is." }
			}, 
			"Snares" => new string[4][]
			{
				new string[2] { "binding and stunning snares", "Lay a snare that drags every foe around it into its centre and holds them there, or one that stuns them." },
				new string[2] { "elemental snare", "A snare that bursts in the element of the architect's amulet: with gold, fire that clings and may burst again near other foes; with silver, a frost that bites and slows to a crawl; with neither, a plain blast." },
				new string[2] { "flash snare", "A snare that bursts in light and thunder: foes around it are blinded and stunned." },
				new string[2] { "chained snares", "A sprung snare sets off every other snare near it, and every snare reaches further." }
			}, 
			"Subspace" => new string[4][]
			{
				new string[2] { "long hands", "The architect's constructions take their materials out of every storage and every inventory on the map, with no hauling; things lingering in others' inventories drift into the architect's satchel." },
				new string[2] { "the subspace veil", "A veil around the architect and the allies near them swallows the shots that fly in. What it swallows fills a measure; full, it can be paid back as a blast." },
				new string[2] { "deep pockets", "The architect's satchel holds far more and unloads anywhere; subspace storages on the map hold half as much again." },
				new string[2] { "unfolding", "The veil spreads wider, swallows twice as much, and pays it back harder." }
			}, 
			"Creator" => new string[4][]
			{
				new string[2] { "obsession", "For a time, every workbench in one room works its bills at once, at the architect's own speed: the materials come to them by themselves, and the architect need not stand at any." },
				new string[2] { "thrift", "Part of the materials of the architect's work comes back." },
				new string[2] { "perfection", "The architect's work sometimes comes out a step better." },
				new string[2] { "echo", "Now and then the work comes out twice. Never an artifact." }
			}, 
			"Energy" => new string[4][]
			{
				new string[2] { "the pilot; energy dome", "The spheres draw from the stream's keepers and from the spire anywhere on the map before their own charge. The architect raises a dome that stops shots from outside." },
				new string[2] { "flame strike", "Fire falls on an area: golden with a golden amulet, silver with a silver one, a plain blast with neither." },
				new string[2] { "silver lines", "Stream wires within the spheres' reach are laid at once and without silver; the keepers hold a quarter more." },
				new string[2] { "pilot of the spire", "While the architect is on its map, the spire makes half as much energy again, its barrier is stronger and cheaper, and it flies further." }
			}, 
			_ => new string[0][], 
		};
	}

	public static float StoneXp(int k)
	{
		return (ArtifactoricsSettingsDef.Main?.sphereStoneXp ?? 4000f) + (ArtifactoricsSettingsDef.Main?.sphereStoneXpStep ?? 1000f) * (float)(k - 1);
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

	public static int StonesFor(float xp)
	{
		int i = 0;
		for (float num = 0f; i < 12 && xp >= num + StoneXp(i + 1); i++)
		{
			num += StoneXp(i + 1);
		}
		return i;
	}

	public static CompArchitectSpheres Of(Pawn p)
	{
		CompArchitectSpheres compArchitectSpheres = CompArchitectSpheres.WornBy(p);
		if (compArchitectSpheres == null || !compArchitectSpheres.Works(p))
		{
			return null;
		}
		return compArchitectSpheres;
	}

	public static int Level(Pawn p, string path)
	{
		CompArchitectSpheres compArchitectSpheres = Of(p);
		if (compArchitectSpheres == null || !(compArchitectSpheres.path == path))
		{
			return 0;
		}
		return compArchitectSpheres.Stones;
	}

	public static Pawn OnMap(Map map, string path, int stones)
	{
		if (map == null)
		{
			return null;
		}
		IReadOnlyList<Pawn> freeColonistsSpawned = map.mapPawns.FreeColonistsSpawned;
		for (int i = 0; i < freeColonistsSpawned.Count; i++)
		{
			if (!freeColonistsSpawned[i].Dead && Level(freeColonistsSpawned[i], path) >= stones)
			{
				return freeColonistsSpawned[i];
			}
		}
		return null;
	}

	public static bool OnMapCached(Map map, string path, int stones)
	{
		if (map == null)
		{
			return false;
		}
		int ticksGame = Find.TickManager.TicksGame;
		string key = map.uniqueID + path + stones;
		if (onMapCache.TryGetValue(key, out (int, bool) value) && ticksGame - value.Item1 < 250 && ticksGame >= value.Item1)
		{
			return value.Item2;
		}
		bool flag = OnMap(map, path, stones) != null;
		onMapCache[key] = (ticksGame, flag);
		return flag;
	}

	public static float Power(Pawn p, string path)
	{
		SkillDef skillDef = ((path == "Creator") ? SkillDefOf.Crafting : ((path == "Energy") ? SkillDefOf.Intellectual : SkillDefOf.Construction));
		int num = p?.skills?.GetSkill(skillDef)?.Level ?? 8;
		return 0.5f + (float)num / 20f;
	}

	public static string Element(Pawn p)
	{
		string text = AmuletDevelopment.WornBy(p)?.necklaceMetal?.defName;
		if (!(text == "ArtifactMetal_Gold"))
		{
			if (!(text == "ArtifactMetal_Silver"))
			{
				return null;
			}
			return "Silver";
		}
		return "Gold";
	}

	public static bool Pay(Pawn p, CompArchitectSpheres c, float share, bool test = false)
	{
		float num = share * 1000f;
		if (c.path == "Energy" && c.Stones >= 3 && p.Map != null && SphereEnergy.Available(p.Map) >= num)
		{
			if (!test)
			{
				SphereEnergy.Take(p.Map, num);
			}
			return true;
		}
		ArtifactState heart = c.Heart;
		if (heart == null || heart.charge < share)
		{
			return false;
		}
		if (!test)
		{
			heart.charge -= share;
		}
		return true;
	}
}
