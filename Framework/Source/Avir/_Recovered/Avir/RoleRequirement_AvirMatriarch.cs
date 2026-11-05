using Avir.Linarian;
using RimWorld;
using Verse;

namespace Avir;

public class RoleRequirement_AvirMatriarch : RoleRequirement
{
	public override string GetLabel(Precept_Role role)
	{
		if (AvirSisters.IsSisters(role?.ideo))
		{
			if (!AvirSistersDetails.IsPrimal(role.ideo))
			{
				return "a female of the central caste, social " + 6 + "+";
			}
			return "a female Singer: the hunt follows only a Matriarch of the Aria";
		}
		return "";
	}

	public override bool Met(Pawn p, Precept_Role role)
	{
		if (!AvirSisters.IsSisters(role?.ideo))
		{
			return true;
		}
		if (AvirSistersDetails.IsPrimal(role.ideo))
		{
			if (p.gender == Gender.Female)
			{
				return Avir.Linarian.Linarian.Is(p);
			}
			return false;
		}
		if (p.gender == Gender.Female && AvirCasteUtility.CasteOf(p) == AvirCaste.Central && AvirSistersRoles.SocialCapable(p))
		{
			return AvirSistersRoles.Social(p) >= 6;
		}
		return false;
	}
}
