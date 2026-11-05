using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Avir;

public static class AvirSistersRoles
{
	public const int LordSocial = 6;

	private static Dictionary<string, List<AbilityDef>> lists;

	public static List<AbilityDef> AbilitiesFor(PreceptDef role)
	{
		if (lists == null)
		{
			Dictionary<string, List<AbilityDef>> dictionary = new Dictionary<string, List<AbilityDef>>();
			dictionary["IdeoRole_Leader"] = L(new string[7] { "Avir_Matriarch_LeaderSpeech", "Avir_Matriarch_WorkDrive", "Avir_Matriarch_Trial", "Avir_Matriarch_Convert", "Avir_Matriarch_Reassure", "Avir_Matriarch_Counsel", "Avir_Matriarch_PreachHealth" });
			dictionary["IdeoRole_Moralist"] = L(new string[7] { "Convert", "Counsel", "Reassure", "ConversionRitual", "PreachHealth", "Avir_GuildCall", "Trial" });
			lists = dictionary;
		}
		if (role == null || !lists.TryGetValue(role.defName, out var value) || value.Count <= 0)
		{
			return null;
		}
		return value;
		static List<AbilityDef> L(string[] names)
		{
			return (from n in names
				select DefDatabase<AbilityDef>.GetNamedSilentFail(n) into d
				where d != null
				select d).ToList();
		}
	}

	public static bool IsLordRole(PreceptDef d)
	{
		if (d != null)
		{
			if (!d.leaderRole && !(d.defName == "IdeoRole_Moralist") && !(d.defName == "Avir_Role_WarLord"))
			{
				return d.defName == "Avir_Role_ArtsLord";
			}
			return true;
		}
		return false;
	}

	public static int Social(Pawn p)
	{
		return (p?.skills?.GetSkill(SkillDefOf.Social)?.Level).GetValueOrDefault();
	}

	public static bool SocialCapable(Pawn p)
	{
		if (p?.skills?.GetSkill(SkillDefOf.Social) != null)
		{
			return !p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled;
		}
		return false;
	}
}
