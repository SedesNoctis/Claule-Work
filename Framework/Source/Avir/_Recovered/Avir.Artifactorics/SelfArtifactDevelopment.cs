using Verse;

namespace Avir.Artifactorics;

public static class SelfArtifactDevelopment
{
	public static void Open(Thing t, Pawn p)
	{
		if (t == null)
		{
			return;
		}
		CompArchitectSpheres compArchitectSpheres = t.TryGetComp<CompArchitectSpheres>();
		if (compArchitectSpheres != null)
		{
			Find.WindowStack.Add(new Dialog_SphereConstellation(compArchitectSpheres, p));
			return;
		}
		CompStriderBow compStriderBow = t.TryGetComp<CompStriderBow>();
		if (compStriderBow != null)
		{
			OpenBow(compStriderBow);
		}
		else
		{
			Dialog_ArtifactSpiral.Open(t);
		}
	}

	private static void OpenBow(CompStriderBow b)
	{
		Find.WindowStack.Add(new Dialog_StriderBowPaths(b, b.bonded));
	}
}
