using System.Linq;
using RimWorld;
using Verse;

namespace Avir;

public class CompAbilityEffect_AvirGuildCall : CompAbilityEffect
{
	private static Faction Guild()
	{
		return Find.FactionManager.AllFactionsListForReading.Where((Faction f) => !f.IsPlayer && !f.defeated && !f.temporary && f.def.defName.StartsWith("Avir_") && !f.HostileTo(Faction.OfPlayer) && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any((PawnGroupMaker m) => m.kindDef == PawnGroupKindDefOf.Trader)).RandomElementWithFallback();
	}

	public override bool GizmoDisabled(out string reason)
	{
		if (Guild() == null)
		{
			reason = "No Avir faction at peace with you to call a guild from.";
			return true;
		}
		return base.GizmoDisabled(out reason);
	}

	public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
	{
		base.Apply(target, dest);
		Map map = parent.pawn?.Map;
		Faction faction = Guild();
		if (map != null && faction != null)
		{
			IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, map);
			incidentParms.faction = faction;
			incidentParms.forced = true;
			if (!IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(incidentParms))
			{
				Messages.Message("The guild of " + faction.Name + " could not send a caravan.", parent.pawn, MessageTypeDefOf.RejectInput, historical: false);
			}
		}
	}
}
