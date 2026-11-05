using RimWorld;
using Verse;

namespace Avir.Linarian;

public class StatPart_LinarianVoices : StatPart
{
	public float perTen = 0.05f;

	private static int Tens(StatRequest req)
	{
		if (req.Thing is Pawn p)
		{
			Gene_LinarianSong gene_LinarianSong = Linarian.Song(p);
			if (gene_LinarianSong != null)
			{
				return LinarianFeast.Count(gene_LinarianSong) / 10;
			}
		}
		return 0;
	}

	public override void TransformValue(StatRequest req, ref float val)
	{
		int num = Tens(req);
		if (num > 0)
		{
			val += perTen * (float)num;
		}
	}

	public override string ExplanationPart(StatRequest req)
	{
		int num = Tens(req);
		if (num <= 0)
		{
			return null;
		}
		return "Voices of her collection (" + num * 10 + "+): +" + (perTen * (float)num).ToStringPercent();
	}
}
