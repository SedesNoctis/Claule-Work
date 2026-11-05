using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class Dialog_SphereConstellation : Dialog_PathBook
{
	private readonly CompArchitectSpheres c;

	private static readonly Dictionary<string, Vector2[]> Shapes = new Dictionary<string, Vector2[]>
	{
		{
			"Archconstruct",
			Ring(12, 0.5f, 0.5f, 0.36f, 0.08f)
		},
		{
			"Snares",
			new Vector2[12]
			{
				Dialog_PathBook.V(0.5f, 0.5f),
				Dialog_PathBook.V(0.3f, 0.35f),
				Dialog_PathBook.V(0.2f, 0.15f),
				Dialog_PathBook.V(0.5f, 0.22f),
				Dialog_PathBook.V(0.72f, 0.33f),
				Dialog_PathBook.V(0.85f, 0.15f),
				Dialog_PathBook.V(0.8f, 0.55f),
				Dialog_PathBook.V(0.92f, 0.8f),
				Dialog_PathBook.V(0.55f, 0.78f),
				Dialog_PathBook.V(0.4f, 0.92f),
				Dialog_PathBook.V(0.25f, 0.65f),
				Dialog_PathBook.V(0.08f, 0.8f)
			}
		},
		{
			"Subspace",
			Spiral(12)
		},
		{
			"Creator",
			new Vector2[12]
			{
				Dialog_PathBook.V(0.5f, 0.92f),
				Dialog_PathBook.V(0.5f, 0.8f),
				Dialog_PathBook.V(0.5f, 0.68f),
				Dialog_PathBook.V(0.5f, 0.56f),
				Dialog_PathBook.V(0.5f, 0.44f),
				Dialog_PathBook.V(0.5f, 0.32f),
				Dialog_PathBook.V(0.5f, 0.2f),
				Dialog_PathBook.V(0.35f, 0.2f),
				Dialog_PathBook.V(0.2f, 0.12f),
				Dialog_PathBook.V(0.2f, 0.3f),
				Dialog_PathBook.V(0.65f, 0.2f),
				Dialog_PathBook.V(0.8f, 0.12f)
			}
		},
		{
			"Energy",
			new Vector2[12]
			{
				Dialog_PathBook.V(0.62f, 0.06f),
				Dialog_PathBook.V(0.52f, 0.2f),
				Dialog_PathBook.V(0.42f, 0.34f),
				Dialog_PathBook.V(0.32f, 0.48f),
				Dialog_PathBook.V(0.48f, 0.48f),
				Dialog_PathBook.V(0.64f, 0.48f),
				Dialog_PathBook.V(0.56f, 0.6f),
				Dialog_PathBook.V(0.48f, 0.72f),
				Dialog_PathBook.V(0.4f, 0.84f),
				Dialog_PathBook.V(0.32f, 0.96f),
				Dialog_PathBook.V(0.6f, 0.8f),
				Dialog_PathBook.V(0.75f, 0.66f)
			}
		}
	};

	protected override Thing Item => c?.parent;

	protected override string Heading => Dialog_PathBook.PlainName(c.parent) + ((p != null) ? ("\n" + p.LabelShortCap + "'s spheres") : "");

	protected override string Growth => "The spheres grow with the architect's own work: building, crafting, art and thought.";

	protected override string[] Paths => SpherePaths.All;

	protected override string Chosen => c.path;

	protected override int[] Nodes => SpherePaths.Nodes;

	protected override int Stones => c.Stones;

	protected override int MaxStones => 12;

	protected override float Xp
	{
		get
		{
			return c.xp;
		}
		set
		{
			c.xp = value;
		}
	}

	protected override string Extra => "On every path the spheres also finish other frames beside the architect's own, and carry the soulmark.";

	public Dialog_SphereConstellation(CompArchitectSpheres c, Pawn p)
		: base(p)
	{
		this.c = c;
	}

	protected override void Choose(string path)
	{
		c.path = path;
		c.Sync(p);
	}

	protected override string Label(string path)
	{
		return SpherePaths.Label(path);
	}

	protected override string Lore(string path)
	{
		return SpherePaths.Lore(path);
	}

	protected override Texture2D Emblem(string path)
	{
		return SphereIcons.Path(path);
	}

	protected override string[][] Powers(string path)
	{
		return SpherePaths.Powers(path);
	}

	protected override float XpFor(int stones)
	{
		return SpherePaths.XpFor(stones);
	}

	protected override void Synced()
	{
		if (p != null)
		{
			c.Sync(p);
		}
	}

	protected override void Unchoose()
	{
		c.path = null;
	}

	protected override void Progress(out float have, out float need)
	{
		int stones = c.Stones;
		have = c.xp - SpherePaths.XpFor(stones);
		need = SpherePaths.StoneXp(stones + 1);
	}

	protected override Texture2D PowerIcon(string path, int k)
	{
		string text = null;
		switch (path)
		{
		case "Archconstruct":
			text = ((k == 2) ? "Soulmark" : null);
			break;
		case "Snares":
			text = (new string[4] { "Snare_Binding", "Snare_Fire", "Snare_Flash", "Snare_Blast" })[k];
			break;
		case "Subspace":
			text = k switch
			{
				3 => "Veil_Discharge", 
				1 => "Veil", 
				_ => null, 
			};
			break;
		case "Creator":
			text = ((k == 0) ? "Obsession" : null);
			break;
		case "Energy":
			text = k switch
			{
				1 => "Flame_Gold", 
				0 => "Energy_Dome", 
				_ => null, 
			};
			break;
		}
		if (text != null)
		{
			return SphereIcons.Named(text);
		}
		return SphereIcons.Path(path);
	}

	protected override Vector2[] Shape(string path)
	{
		if (!Shapes.TryGetValue(path, out var value))
		{
			return null;
		}
		return value;
	}

	private static Vector2[] Ring(int n, float cx, float cy, float r, float wob)
	{
		Vector2[] array = new Vector2[n];
		for (int i = 0; i < n; i++)
		{
			float f = (float)i * MathF.PI * 2f / (float)n - MathF.PI / 2f;
			float num = r + ((i % 2 == 0) ? wob : (0f - wob));
			array[i] = new Vector2(cx + Mathf.Cos(f) * num, cy + Mathf.Sin(f) * num);
		}
		return array;
	}

	private static Vector2[] Spiral(int n)
	{
		Vector2[] array = new Vector2[n];
		for (int i = 0; i < n; i++)
		{
			float f = (float)i * 0.85f;
			float num = 0.44f - (float)i * 0.033f;
			array[i] = new Vector2(0.5f + Mathf.Cos(f) * num, 0.5f + Mathf.Sin(f) * num);
		}
		return array;
	}
}
