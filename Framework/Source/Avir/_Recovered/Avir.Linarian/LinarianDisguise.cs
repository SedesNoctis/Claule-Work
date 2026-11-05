using Verse;
using Verse.AI;

namespace Avir.Linarian;

public static class LinarianDisguise
{
	public static string Description(Pawn p)
	{
		string text = LinarianVeil.InForm(p);
		if (!(text == "Crow"))
		{
			if (text == "Wolf")
			{
				return "A lean she-wolf, alone - which is unusual for wolves. She keeps her distance, circles, and never quite leaves. Nature is full of small mysteries, and you surely have more pressing things to deal with.";
			}
			return "A slender doe of the deep woods, soft-eyed and unhurried. It watches your colony with what is certainly nothing more than the idle curiosity of a herbivore. It would be silly to read anything more into it.";
		}
		return "A large crow with a pale mask round its eyes. Crows are clever birds, and this one seems to have taken a particular interest in where your colonists sleep. Crows do that. Mostly.";
	}

	public static string Activity(Pawn p)
	{
		if (p.Downed)
		{
			return "Lying down.";
		}
		if (LinarianTactics.Asleep(p))
		{
			return "Sleeping.";
		}
		Pawn_PathFollower pather = p.pather;
		if (pather != null && pather.Moving)
		{
			return "Wandering.";
		}
		if (!(LinarianVeil.InForm(p) == "Crow"))
		{
			return "Grazing.";
		}
		return "Watching.";
	}
}
