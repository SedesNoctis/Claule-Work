using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using Verse;

namespace Avir.Linarian;

public static class Linarian
{
	private static readonly string[] PsychicDefPrefixes = new string[5] { "Illithid_", "GS_", "VPE_", "VPEP_", "Avir_Linarian" };

	private static readonly string[] PsychicCompWords = new string[10] { "Psychic", "Psy", "Mind", "Dominion", "Berserk", "Thrall", "Sleep", "Intrusion", "Discord", "Telepath" };

	private static readonly Dictionary<AbilityDef, bool> psychicCache = new Dictionary<AbilityDef, bool>();

	public static bool Is(Pawn p)
	{
		if (p?.genes != null && LinarianDefOf.Avir_LinarianSong != null)
		{
			Gene gene = p.genes.GetGene(LinarianDefOf.Avir_LinarianSong);
			if (gene != null)
			{
				return gene.Active;
			}
		}
		return false;
	}

	public static Gene_LinarianSong Song(Pawn p)
	{
		return p?.genes?.GetGene(LinarianDefOf.Avir_LinarianSong) as Gene_LinarianSong;
	}

	public static bool IsArtifactGear(Thing t)
	{
		if (t == null)
		{
			return false;
		}
		if (t.TryGetComp<CompAvirAmulet>() != null || t.TryGetComp<CompStriderBow>() != null || t.TryGetComp<CompArchitectSpheres>() != null)
		{
			return true;
		}
		return t.Socket()?.inlaid != null;
	}

	public static bool IsPsychic(AbilityDef def)
	{
		if (def == null)
		{
			return false;
		}
		if (psychicCache.TryGetValue(def, out var value))
		{
			return value;
		}
		value = def.IsPsycast || PsychicDefPrefixes.Any((string x) => def.defName.StartsWith(x)) || (def.comps != null && def.comps.Any((AbilityCompProperties c) => c?.compClass != null && PsychicCompWords.Any((string w) => c.compClass.Name.Contains(w)))) || (def.statBases != null && def.statBases.Any((StatModifier s) => s.stat == StatDefOf.PsychicSensitivity));
		psychicCache[def] = value;
		return value;
	}
}
