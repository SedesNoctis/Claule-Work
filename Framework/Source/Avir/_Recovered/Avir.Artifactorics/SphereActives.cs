using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public static class SphereActives
{
	public static IEnumerable<Command> Commands(Pawn p, CompArchitectSpheres c)
	{
		int i = c.Stones;
		switch (c.path)
		{
		case "Snares":
			if (i >= 3)
			{
				yield return SphereSnares.Command(p, c, SnareKind.Binding);
				yield return SphereSnares.Command(p, c, SnareKind.Stunning);
			}
			if (i >= 6)
			{
				yield return SphereSnares.Command(p, c, SnareKind.Elemental);
			}
			if (i >= 9)
			{
				yield return SphereSnares.Command(p, c, SnareKind.Flash);
			}
			break;
		case "Subspace":
			if (i >= 6)
			{
				yield return SphereSubspace.VeilCommand(p, c);
				yield return SphereSubspace.DischargeCommand(p, c);
			}
			break;
		case "Creator":
			if (i >= 3)
			{
				yield return SphereCreator.Command(p, c);
			}
			break;
		case "Energy":
			if (i >= 3)
			{
				yield return SphereEnergy.DomeCommand(p, c);
			}
			if (i >= 6)
			{
				yield return SphereEnergy.FlameCommand(p, c);
			}
			break;
		}
	}

	public static void Gate(Command cmd, Pawn p, CompArchitectSpheres c, string key, float share)
	{
		int num = c.ReadyIn(key);
		if (num > 0)
		{
			cmd.Disable("Ready in " + num.ToStringTicksToPeriod() + ".");
		}
		else if (!SpherePaths.Pay(p, c, share, test: true))
		{
			cmd.Disable((c.path == "Energy") ? "Neither the stream's keepers nor the spheres' artifact hold enough." : "The artifact in the spheres lacks the charge.");
		}
		else if (p.Downed || !p.Spawned)
		{
			cmd.Disable("Cannot now.");
		}
	}

	public static Texture2D Icon(CompArchitectSpheres c)
	{
		return ContentFinder<Texture2D>.Get(c.parent.def.graphicData.texPath, reportFailure: false) ?? BaseContent.BadTex;
	}
}
