using System;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class Dialog_StriderBowPaths : Dialog_PathBook
{
	private readonly CompStriderBow b;

	private static Vector2[] bow;

	protected override Thing Item => b?.parent;

	protected override string Heading => Dialog_PathBook.PlainName(b.parent) + ((b.bonded != null) ? ("\n" + b.bonded.LabelShortCap + "'s bow") : "");

	protected override string Growth
	{
		get
		{
			if (b.bonded != null)
			{
				return "The bow grows with " + b.bonded.LabelShort + "'s shooting while it is in hand - most of all with shots at foes; practice and the hunt teach it little.";
			}
			return "Not bonded yet: the first Avir to take it up becomes its archer. The bow grows with its archer's shooting.";
		}
	}

	protected override string[] Paths => StriderPaths.All;

	protected override string Chosen => b.path;

	protected override int[] Nodes => StriderPaths.Nodes;

	protected override int Stones => b.Stones;

	protected override int MaxStones => 18;

	protected override float Xp
	{
		get
		{
			return b.xp;
		}
		set
		{
			b.xp = value;
		}
	}

	protected override string Extra => "The first two powers wake on their own and grow with the archer's shooting; the third is a shot the archer calls, for a tenth of the bow's charge.";

	public Dialog_StriderBowPaths(CompStriderBow b, Pawn p)
		: base(p ?? b?.bonded)
	{
		this.b = b;
	}

	protected override void Choose(string path)
	{
		b.path = path;
	}

	protected override string Label(string path)
	{
		return StriderPaths.Label(path);
	}

	protected override string Lore(string path)
	{
		return StriderPaths.Lore(path);
	}

	protected override Texture2D Emblem(string path)
	{
		return SphereIcons.StriderPath(path);
	}

	protected override string[][] Powers(string path)
	{
		return StriderPaths.Powers(path);
	}

	protected override Texture2D PowerIcon(string path, int k)
	{
		if (k != 2)
		{
			return SphereIcons.StriderPath(path);
		}
		return SphereIcons.StriderActive(path) ?? SphereIcons.StriderPath(path);
	}

	protected override float XpFor(int stones)
	{
		return StriderPaths.XpFor(stones);
	}

	protected override void Unchoose()
	{
		b.path = null;
	}

	protected override string Note(string path)
	{
		if (b.whiteRobe)
		{
			if (!(path == "Moon"))
			{
				return null;
			}
			return "the white bow's own: strongest";
		}
		if (path == "Moon")
		{
			return "every bow's own: stronger";
		}
		if (!StriderPaths.Favoured(b, path))
		{
			return null;
		}
		return "of its Dynasty: stronger";
	}

	protected override void Progress(out float have, out float need)
	{
		int stones = b.Stones;
		have = b.xp - StriderPaths.XpFor(stones);
		need = StriderPaths.StoneXp(stones + 1);
	}

	protected override Vector2[] Shape(string path)
	{
		if (bow != null)
		{
			return bow;
		}
		bow = new Vector2[18];
		for (int i = 0; i < bow.Length; i++)
		{
			float num = (float)i / (float)(bow.Length - 1);
			bow[i] = new Vector2(0.3f + 0.42f * Mathf.Sin(num * MathF.PI), 0.96f - num * 0.92f);
		}
		return bow;
	}
}
