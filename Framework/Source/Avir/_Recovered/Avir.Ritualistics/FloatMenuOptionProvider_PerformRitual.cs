using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Ritualistics;

public class FloatMenuOptionProvider_PerformRitual : FloatMenuOptionProvider
{
	protected override bool Drafted => true;

	protected override bool Undrafted => true;

	protected override bool Multiselect => false;

	protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
	{
		Pawn p = context?.FirstSelectedPawn;
		CompRitualCircle c = clickedThing?.TryGetComp<CompRitualCircle>();
		if (p == null || c == null || !c.Drawn || c.Running || clickedThing.Faction != Faction.OfPlayer)
		{
			return null;
		}
		if (c.Reading.trap)
		{
			return null;
		}
		string text = "Perform ritual: " + c.drawn.name;
		string text2 = c.CannotPerform(p);
		if (text2 != null)
		{
			return new FloatMenuOption(text + " (" + text2 + ")", null);
		}
		if (!p.CanReach(clickedThing.Position, PathEndMode.OnCell, Danger.Deadly))
		{
			return new FloatMenuOption(text + " (no path)", null);
		}
		return new FloatMenuOption(text, delegate
		{
			c.OrderBy(p);
		});
	}
}
