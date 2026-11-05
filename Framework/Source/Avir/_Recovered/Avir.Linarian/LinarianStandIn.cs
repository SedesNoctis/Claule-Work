using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

public static class LinarianStandIn
{
	private static readonly Dictionary<int, Pawn> standIns = new Dictionary<int, Pawn>();

	private static readonly Dictionary<int, string> forms = new Dictionary<int, string>();

	private static readonly Dictionary<Pawn, Pawn> owners = new Dictionary<Pawn, Pawn>();

	private static PawnKindDef KindFor(string form)
	{
		if (!(form == "Crow"))
		{
			if (form == "Wolf")
			{
				return DefDatabase<PawnKindDef>.GetNamedSilentFail("Wolf_Timber");
			}
			return DefDatabase<PawnKindDef>.GetNamedSilentFail("Deer");
		}
		return DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(delegate(PawnKindDef k)
		{
			RaceProperties raceProps = k.RaceProps;
			return raceProps != null && raceProps.Animal && (k.defName.IndexOf("crow", StringComparison.OrdinalIgnoreCase) >= 0 || k.defName.IndexOf("raven", StringComparison.OrdinalIgnoreCase) >= 0);
		});
	}

	public static Pawn For(Pawn her)
	{
		string text = LinarianVeil.InForm(her) ?? "Doe";
		if (standIns.TryGetValue(her.thingIDNumber, out var value) && forms.TryGetValue(her.thingIDNumber, out var value2) && value2 == text)
		{
			return value;
		}
		PawnKindDef pawnKindDef = KindFor(text);
		if (pawnKindDef == null)
		{
			return null;
		}
		try
		{
			PlanetTile? tile = -1;
			Gender? fixedGender = (pawnKindDef.RaceProps.hasGenders ? new Gender?(Gender.Female) : null);
			float? fixedBiologicalAge = Mathf.Max(1.5f, pawnKindDef.RaceProps.lifeExpectancy * 0.3f);
			value = PawnGenerator.GeneratePawn(new PawnGenerationRequest(pawnKindDef, null, PawnGenerationContext.NonPlayer, tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: false, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: false, certainlyBeenInCryptosleep: false, forceRedressWorldPawnIfFormerColonist: false, worldPawnFactionDoesntMatter: false, 0f, 0f, null, 1f, null, null, null, null, null, fixedBiologicalAge, null, fixedGender));
		}
		catch
		{
			return null;
		}
		value.Name = new NameSingle(HeraldLabels.Beast(her).CapitalizeFirst());
		if (standIns.TryGetValue(her.thingIDNumber, out var value3) && value3 != null)
		{
			owners.Remove(value3);
		}
		standIns[her.thingIDNumber] = value;
		forms[her.thingIDNumber] = text;
		owners[value] = her;
		return value;
	}

	public static Pawn OwnerOf(Pawn standIn)
	{
		if (standIn == null || !owners.TryGetValue(standIn, out var value))
		{
			return null;
		}
		return value;
	}
}
