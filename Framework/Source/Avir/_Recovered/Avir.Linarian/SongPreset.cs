using System.Collections.Generic;
using Verse;

namespace Avir.Linarian;

public class SongPreset : IExposable
{
	public string name = "preset";

	public List<string> tactics = new List<string>();

	public void ExposeData()
	{
		Scribe_Values.Look(ref name, "name");
		Scribe_Collections.Look(ref tactics, "tactics", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && tactics == null)
		{
			tactics = new List<string>();
		}
	}
}
