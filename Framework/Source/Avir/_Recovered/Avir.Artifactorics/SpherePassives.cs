using RimWorld;
using Verse;

namespace Avir.Artifactorics;

public static class SpherePassives
{
	public const string MasterConstructor = "Avir_MasterConstructor";

	public const string MachineHeart = "Avir_ArchconstructHeart";

	private static HediffDef Def(string n)
	{
		return DefDatabase<HediffDef>.GetNamedSilentFail(n);
	}

	private static AbilityDef Ab(string n)
	{
		return DefDatabase<AbilityDef>.GetNamedSilentFail(n);
	}

	public static void Sync(Pawn p, CompArchitectSpheres c)
	{
		if (p?.health == null)
		{
			return;
		}
		int num = ((c != null && c.path == "Archconstruct") ? c.Stones : 0);
		TraitDef namedSilentFail = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Archstructror");
		if (namedSilentFail != null)
		{
			Pawn_StoryTracker story = p.story;
			if (story != null && story.traits?.HasTrait(namedSilentFail) == true)
			{
				num = 12;
			}
		}
		ArchitectSpheres.SetHediff(p, Def("Avir_MasterConstructor"), num >= 3);
		ArchitectSpheres.SetHediff(p, Def("Avir_ArchconstructHeart"), num >= 9);
		SetAbility(p, Ab("Avir_LatticeRepair"), num >= 6);
		SetAbility(p, Ab("Avir_LatticeUnbound"), num >= 12);
	}

	private static void SetAbility(Pawn p, AbilityDef def, bool on)
	{
		if (def != null && p.abilities != null)
		{
			bool flag = p.abilities.GetAbility(def) != null;
			if (on && !flag)
			{
				p.abilities.GainAbility(def);
			}
			else if (!on && flag)
			{
				p.abilities.RemoveAbility(def);
			}
		}
	}

	public static int SoulmarkBonds(Pawn p)
	{
		HediffDef hediffDef = Def("Avir_ArchconstructHeart");
		if (hediffDef == null || p == null || p.health?.hediffSet?.HasHediff(hediffDef) != true)
		{
			return 3;
		}
		return 7;
	}
}
