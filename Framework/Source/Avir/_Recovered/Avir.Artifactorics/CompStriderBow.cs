using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class CompStriderBow : ThingComp
{
	public Pawn bonded;

	public string path;

	public bool whiteRobe;

	public float xp;

	public int activeReadyTick;

	public int Stones => StriderPaths.StonesFor(xp);

	public CompProperties_StriderBow Props => (CompProperties_StriderBow)props;

	public bool BondedToOther(Pawn p)
	{
		if (bonded != null)
		{
			return bonded != p;
		}
		return false;
	}

	public static bool CanBond(Pawn p)
	{
		if (p != null && p.RaceProps.Humanlike)
		{
			return AvirRhythmUtility.IsAvir(p);
		}
		return false;
	}

	public override void Notify_Equipped(Pawn pawn)
	{
		base.Notify_Equipped(pawn);
		if (bonded == null && CanBond(pawn))
		{
			bonded = pawn;
			if (pawn.Faction == Faction.OfPlayer)
			{
				Messages.Message(pawn.LabelShortCap + " has bonded with " + parent.LabelShort + ". No one else can draw it until a reforging ritual breaks the bond.", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}
	}

	public void Unbond()
	{
		bonded = null;
	}

	public override void PostDestroy(DestroyMode mode, Map previousMap)
	{
		base.PostDestroy(mode, previousMap);
		if (bonded != null && !bonded.Dead && (mode != 0 || parent.ParentHolder is Pawn_EquipmentTracker))
		{
			ThoughtDef namedSilentFail = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_LostBondedBow");
			if (namedSilentFail != null)
			{
				bonded.needs?.mood?.thoughts?.memories?.TryGainMemory(namedSilentFail);
			}
		}
	}

	public override string CompInspectStringExtra()
	{
		string text = ((bonded == null) ? "Not bonded: the first Avir to take it up becomes its archer." : ("Bonded to " + bonded.LabelShortCap + (bonded.Dead ? " (dead - the bond holds until a reforging ritual breaks it)" : "") + "."));
		string text2 = "Stones: " + Stones + " / " + 18 + ", path: " + ((path != null) ? StriderPaths.Label(path) : (whiteRobe ? "the Moon (white)" : "not chosen")) + ".";
		ArtifactState artifactState = StriderBow.Quiver(parent);
		string text3 = ((artifactState == null) ? "No artifact in the grip: no arrows." : ("Arrows left: " + Mathf.FloorToInt(artifactState.charge / StriderBow.ShotCharge) + "."));
		return text + "\n" + text2 + "\n" + text3;
	}

	public override void PostExposeData()
	{
		base.PostExposeData();
		Scribe_References.Look(ref bonded, "avirBowBonded");
		Scribe_Values.Look(ref path, "avirBowPath");
		Scribe_Values.Look(ref whiteRobe, "avirBowWhite", defaultValue: false);
		Scribe_Values.Look(ref xp, "avirBowXp", 0f);
		Scribe_Values.Look(ref activeReadyTick, "avirBowActiveReady", 0);
	}

	public static CompStriderBow Of(Thing t)
	{
		return (t as ThingWithComps)?.GetComp<CompStriderBow>();
	}
}
