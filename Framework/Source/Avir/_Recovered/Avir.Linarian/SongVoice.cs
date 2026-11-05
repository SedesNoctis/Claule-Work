using RimWorld;
using Verse;

namespace Avir.Linarian;

public class SongVoice : IExposable
{
	public string name;

	public SkillDef skill;

	public int level;

	public float worth = 1f;

	public int id;

	public int uses = 5;

	public bool forgotten;

	public int since;

	public int count = 1;

	public bool merged;

	public bool Usable
	{
		get
		{
			if (!forgotten && skill != null)
			{
				return uses > 0;
			}
			return false;
		}
	}

	public void ExposeData()
	{
		Scribe_Values.Look(ref name, "name");
		Scribe_Defs.Look(ref skill, "skill");
		Scribe_Values.Look(ref level, "level", 0);
		Scribe_Values.Look(ref worth, "worth", 1f);
		Scribe_Values.Look(ref id, "id", 0);
		Scribe_Values.Look(ref uses, "uses", 5);
		Scribe_Values.Look(ref forgotten, "forgotten", defaultValue: false);
		Scribe_Values.Look(ref since, "since", 0);
		Scribe_Values.Look(ref count, "count", 1);
		Scribe_Values.Look(ref merged, "merged", defaultValue: false);
	}
}
