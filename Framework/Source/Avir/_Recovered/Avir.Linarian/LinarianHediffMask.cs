using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.Linarian;

public static class LinarianHediffMask
{
	private static readonly Dictionary<string, (string label, string desc)> masks = new Dictionary<string, (string, string)>
	{
		{
			"Avir_SongSleep",
			("dead sleep", "Asleep like a stone, and no shaking will help. Some nights are simply like that. There is nothing out there in the dark, of course.")
		},
		{
			"Avir_SongDrowsy",
			("drowsiness", "Eyelids heavy as wet feathers. The weather, no doubt. It is always the weather.")
		},
		{
			"Avir_SongGroggy",
			("foggy head", "A bad night. Everyone has them. Nobody remembers what woke them - or what sang them back down.")
		},
		{
			"Avir_SongRest",
			("deep rest", "Slept unusually well. Be grateful, and do not ask whom to thank.")
		},
		{
			"Avir_DreamMark",
			("vivid dreams", "Dreams of something tall at the edge of the firelight. Only dreams. Do go back to sleep.")
		},
		{
			"Avir_SlowDevour",
			("malaise", "Pale and tired, and the doctor cannot say why. The doctor will not find out, either.")
		},
		{
			"Avir_Sleepwalking",
			("sleepwalking", "Walks in their sleep. A harmless habit, mostly. Where to - that is nobody's business.")
		},
		{
			"Avir_Adoration",
			("longing", "Misses something they have never seen. Poets call it inspiration. Let them.")
		},
		{
			"Avir_PhantomScar",
			("old ache", "An ache in a place that was never hurt. Bodies are such strange, honest things.")
		},
		{
			"Avir_StrangeAche",
			("strange ache", "Something stings, and there is no wound. A bird, perhaps. There are so many birds at night.")
		},
		{
			"Avir_ScarFlare",
			("flare of pain", "Every old ache wakes at once. It will pass. Most things that bite do.")
		},
		{
			"Avir_SongCaptivated",
			("daydreaming", "Staring at nothing with a soft smile. The nothing is very flattered.")
		},
		{
			"Avir_SongDread",
			("night terror", "The dark has got under their skin. Silly. The dark is empty. Mostly.")
		},
		{
			"Avir_MarkedPrey",
			("unease", "Feels watched. Paranoia, surely. Who would bother watching someone so ordinary?")
		},
		{
			"Avir_TimeSlow",
			("sluggishness", "Moves as if wading through honey. Too little sleep, or too much.")
		}
	};

	public static bool Masked(Hediff h, out (string label, string desc) mask)
	{
		mask = default((string, string));
		if (h?.def == null || !masks.TryGetValue(h.def.defName, out mask))
		{
			return false;
		}
		Pawn pawn = h.pawn;
		if (pawn == null || (pawn.Faction != Faction.OfPlayer && !pawn.IsPrisonerOfColony))
		{
			return false;
		}
		if (DebugSettings.godMode)
		{
			return false;
		}
		Pawn pawn2 = h.TryGetComp<HediffComp_SongSource>()?.herald;
		if (pawn2 != null)
		{
			if (pawn2.Faction != Faction.OfPlayer)
			{
				return !Revealed(pawn2);
			}
			return false;
		}
		Map mapHeld = pawn.MapHeld;
		if (mapHeld != null)
		{
			return !mapHeld.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Any(Linarian.Is);
		}
		return false;
	}

	private static bool Revealed(Pawn her)
	{
		HeraldRecord heraldRecord = LinarianPresence.Record(her);
		if (heraldRecord != null)
		{
			return Find.TickManager.TicksGame <= heraldRecord.revealedUntil;
		}
		return true;
	}
}
