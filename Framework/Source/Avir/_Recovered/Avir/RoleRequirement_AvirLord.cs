using RimWorld;
using Verse;

namespace Avir;

public class RoleRequirement_AvirLord : RoleRequirement
{
	public override string GetLabel(Precept_Role role)
	{
		if (!AvirSisters.IsSisters(role?.ideo))
		{
			return "";
		}
		return "social " + 6 + "+ (no one follows a Lord who cannot speak)";
	}

	public override bool Met(Pawn p, Precept_Role role)
	{
		if (AvirSisters.IsSisters(role?.ideo))
		{
			if (AvirSistersRoles.SocialCapable(p))
			{
				return AvirSistersRoles.Social(p) >= 6;
			}
			return false;
		}
		return true;
	}
}
