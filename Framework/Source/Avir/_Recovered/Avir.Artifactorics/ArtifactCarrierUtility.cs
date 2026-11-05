using RimWorld;
using Verse;

namespace Avir.Artifactorics;

public static class ArtifactCarrierUtility
{
	public static bool IsSelfArtifact(Thing t)
	{
		if (t is ThingWithComps thingWithComps)
		{
			if (thingWithComps.GetComp<CompStriderBow>() == null)
			{
				return thingWithComps.GetComp<CompArchitectSpheres>() != null;
			}
			return true;
		}
		return false;
	}

	public static bool IsSocketableWeapon(ThingDef d)
	{
		bool flag = SpearMode.HasSpearVerb(d);
		if (d == null || (!d.IsMeleeWeapon && !flag) || d.comps == null || d.IsApparel)
		{
			return false;
		}
		if (!typeof(ThingWithComps).IsAssignableFrom(d.thingClass))
		{
			return false;
		}
		if (d.equipmentType != EquipmentType.Primary)
		{
			return false;
		}
		if (!flag && d.Verbs != null && d.Verbs.Any((VerbProperties v) => v != null && !v.IsMeleeAttack))
		{
			return false;
		}
		return true;
	}

	public static ArtifactCarrier CarrierOf(Thing t)
	{
		if (t?.def == null || !t.def.IsWeapon || t.def.IsApparel)
		{
			return ArtifactCarrier.Apparel;
		}
		return ArtifactCarrier.Weapon;
	}

	public static Pawn HolderOf(Thing t)
	{
		if (t is Apparel apparel)
		{
			return apparel.Wearer;
		}
		return (t?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
	}

	public static bool DropFromHolder(Pawn p, Thing t, IntVec3 at, out Thing dropped)
	{
		dropped = null;
		if (p == null || t == null)
		{
			return false;
		}
		if (t is Apparel ap)
		{
			if (p.apparel == null || !p.apparel.TryDrop(ap, out var resultingAp, at, forbid: false) || resultingAp == null)
			{
				return false;
			}
			dropped = resultingAp;
			return true;
		}
		if (t is ThingWithComps eq && p.equipment != null && p.equipment.Contains(eq))
		{
			ThingWithComps resultingEq;
			bool result = p.equipment.TryDropEquipment(eq, out resultingEq, at, forbid: false) && resultingEq != null;
			dropped = resultingEq;
			return result;
		}
		return false;
	}

	public static JobDef ReturnJob(Thing t)
	{
		if (!(t is Apparel))
		{
			return JobDefOf.Equip;
		}
		return JobDefOf.Wear;
	}

	public static string Noun(Thing t)
	{
		if (CarrierOf(t) != ArtifactCarrier.Weapon)
		{
			return "garment";
		}
		return "weapon";
	}
}
