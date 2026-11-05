using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Avir.Linarian;

public static class AvirFaith
{
	private const string Diarchy = "Avir_Structure_CelestialDiarchy";

	private const string Wild = "Avir_Structure_WildGods";

	public static void Settle(Pawn p)
	{
		if (!ModsConfig.IdeologyActive || p?.ideo == null)
		{
			return;
		}
		Faction faction = p.Faction;
		if ((faction != null && faction.IsPlayer) || Find.IdeoManager == null)
		{
			return;
		}
		bool flag = Linarian.Is(p);
		if (!flag && !p.def.defName.StartsWith("Avir"))
		{
			return;
		}
		if (flag && p.Faction == null)
		{
			HeraldRecord heraldRecord = LinarianPresence.Record(p);
			if (heraldRecord == null || !heraldRecord.primal)
			{
				Ideo ideo = WildSong();
				if (ideo != null)
				{
					if (p.Ideo != ideo)
					{
						p.ideo.SetIdeo(ideo);
					}
					return;
				}
			}
		}
		string text = p.Ideo?.StructureMeme?.defName;
		if (!(text == "Avir_Structure_CelestialDiarchy") && !(text == "Avir_Structure_WildGods"))
		{
			Ideo ideo2 = (flag ? (PrimalIdeo() ?? FindIdeo("Avir_Structure_WildGods") ?? FindIdeo("Avir_Structure_CelestialDiarchy")) : (FindIdeo("Avir_Structure_CelestialDiarchy") ?? FindIdeo("Avir_Structure_WildGods")));
			if (ideo2 != null)
			{
				p.ideo.SetIdeo(ideo2);
			}
		}
	}

	public static Ideo WildSong()
	{
		GameComponent_WildGods instance = GameComponent_WildGods.Instance;
		if (instance == null)
		{
			return null;
		}
		if (instance.wildSong != null && Find.IdeoManager.IdeosListForReading.Contains(instance.wildSong))
		{
			return instance.wildSong;
		}
		FactionDef namedSilentFail = DefDatabase<FactionDef>.GetNamedSilentFail("Avir_Primal");
		MemeDef namedSilentFail2 = DefDatabase<MemeDef>.GetNamedSilentFail("Avir_Structure_WildGods");
		if (namedSilentFail == null || namedSilentFail2 == null)
		{
			return null;
		}
		try
		{
			Ideo ideo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(namedSilentFail, forceNoExpansionIdeo: true, null, null, null, classicExtra: false, forceNoWeaponPreference: false, forNewFluidIdeo: false, fixedIdeo: false, "The Wild Song", null, null, hidden: true, "The faith of the Singers of the wild. Its gods walk the earth, and their song carries through the forests at night: it is listened to, and obeyed."));
			if (ideo == null)
			{
				return null;
			}
			ideo.hidden = true;
			ideo.name = "The Wild Song";
			ideo.adjective = "wild-sung";
			ideo.memberName = "listener";
			Find.IdeoManager.Add(ideo);
			instance.wildSong = ideo;
			return ideo;
		}
		catch (Exception ex)
		{
			Log.Warning("[Avir] the Wild Song could not be made: " + ex.Message);
			return null;
		}
	}

	private static Ideo PrimalIdeo()
	{
		Ideo ideo = LinarianPresence.PrimalFaction()?.ideos?.PrimaryIdeo;
		if (!(ideo?.StructureMeme?.defName == "Avir_Structure_WildGods"))
		{
			return null;
		}
		return ideo;
	}

	private static Ideo FindIdeo(string structure)
	{
		return Find.IdeoManager.IdeosListForReading.Where((Ideo i) => i.StructureMeme?.defName == structure).RandomElementWithFallback();
	}
}
