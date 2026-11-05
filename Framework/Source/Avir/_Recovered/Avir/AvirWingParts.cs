using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Avir;

[StaticConstructorOnStartup]
public static class AvirWingParts
{
	private static BodyPartDef wing;

	private static HediffDef absent;

	private static GeneDef flightGene;

	public static BodyPartDef Wing => wing ?? (wing = DefDatabase<BodyPartDef>.GetNamedSilentFail("Avir_Wing"));

	public static HediffDef Absent => absent ?? (absent = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WingAbsent"));

	private static GeneDef WingGene => flightGene ?? (flightGene = DefDatabase<GeneDef>.GetNamedSilentFail("Flight_Gene"));

	public static bool HasWingGene(Pawn p)
	{
		if (WingGene != null && p?.genes != null)
		{
			return p.genes.HasActiveGene(WingGene);
		}
		return false;
	}

	public static IEnumerable<BodyPartRecord> WingParts(Pawn p)
	{
		if (Wing != null && p?.RaceProps?.body != null)
		{
			return p.RaceProps.body.AllParts.Where((BodyPartRecord r) => r.def == Wing);
		}
		return Enumerable.Empty<BodyPartRecord>();
	}

	public static void Ensure(Pawn p)
	{
		DropStaleAbsent(p);
		if (Wing == null || Absent == null || p?.health?.hediffSet == null || p.Dead)
		{
			return;
		}
		bool flag = HasWingGene(p);
		foreach (BodyPartRecord part in WingParts(p).ToList())
		{
			Hediff hediff = p.health.hediffSet.hediffs.FirstOrDefault((Hediff h) => h.def == Absent && h.Part == part);
			if (flag && hediff != null)
			{
				p.health.RemoveHediff(hediff);
			}
			else if (!flag && hediff == null && !p.health.hediffSet.PartIsMissing(part))
			{
				Hediff_AvirAbsentWing hediff_AvirAbsentWing = (Hediff_AvirAbsentWing)HediffMaker.MakeHediff(Absent, p, part);
				hediff_AvirAbsentWing.IsFresh = false;
				p.health.AddHediff(hediff_AvirAbsentWing, part);
			}
		}
	}

	private static void DropStaleAbsent(Pawn p)
	{
		if (Absent == null || p?.health?.hediffSet == null)
		{
			return;
		}
		foreach (Hediff item in p.health.hediffSet.hediffs.Where((Hediff h) => h.def == Absent && (h.Part == null || h.Part.def != Wing)).ToList())
		{
			p.health.RemoveHediff(item);
		}
	}

	public static float SongFactor(Pawn p)
	{
		if (Wing == null || p?.health?.hediffSet == null)
		{
			return 1f;
		}
		List<BodyPartRecord> list = WingParts(p).ToList();
		if (list.Count == 0)
		{
			return 1f;
		}
		int num = list.Count((BodyPartRecord r) => p.health.hediffSet.PartIsMissing(r) || p.health.hediffSet.hediffs.Any((Hediff h) => h.def == Absent && h.Part == r));
		if (num >= list.Count)
		{
			return 0f;
		}
		if (num > 0)
		{
			return 0.5f;
		}
		if (!list.Any((BodyPartRecord r) => p.health.hediffSet.GetPartHealth(r) < r.def.GetMaxHealth(p) * 0.5f))
		{
			return 1f;
		}
		return 0.5f;
	}

	public static bool Clipped(Pawn p)
	{
		if (Wing == null || !HasWingGene(p) || p.health?.hediffSet == null)
		{
			return false;
		}
		foreach (BodyPartRecord item in WingParts(p))
		{
			if (p.health.hediffSet.PartIsMissing(item))
			{
				return true;
			}
			if (p.health.hediffSet.GetPartHealth(item) < item.def.GetMaxHealth(p) * 0.5f)
			{
				return true;
			}
		}
		return false;
	}
}
