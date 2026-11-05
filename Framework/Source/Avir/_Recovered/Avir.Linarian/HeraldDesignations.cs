using RimWorld;
using Verse;

namespace Avir.Linarian;

public static class HeraldDesignations
{
	public static void Set(Pawn her, DesignationDef def)
	{
		DesignationManager designationManager = her.MapHeld?.designationManager;
		if (designationManager != null)
		{
			designationManager.TryRemoveDesignationOn(her, DesignationDefOf.Hunt);
			designationManager.TryRemoveDesignationOn(her, DesignationDefOf.Tame);
			designationManager.AddDesignation(new Designation(her, def));
		}
	}

	public static void Clear(Pawn her)
	{
		DesignationManager designationManager = her?.MapHeld?.designationManager;
		if (designationManager != null)
		{
			designationManager.TryRemoveDesignationOn(her, DesignationDefOf.Hunt);
			designationManager.TryRemoveDesignationOn(her, DesignationDefOf.Tame);
		}
	}
}
