using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics;

public abstract class Dialog_PathBook : Window
{
	protected readonly Pawn p;

	private string shown;

	private int selStone = -1;

	private bool sheetMax;

	private Rect sheetNormal;

	private Vector2 scroll;

	private float zoom = 1f;

	private Vector2 pan;

	private bool dragging;

	private float dragged;

	protected static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);

	protected static readonly Color InkSoft = new Color(0.12f, 0.075f, 0.03f);

	protected static readonly Color InkGrey = new Color(0.3f, 0.24f, 0.17f);

	protected static readonly Color InkRed = new Color(0.45f, 0.08f, 0.04f);

	protected static readonly Color Gold = new Color(0.7f, 0.5f, 0.12f);

	private static Texture2D locked;

	private static Texture2D avail;

	private static Texture2D opened;

	protected abstract Thing Item { get; }

	protected abstract string Heading { get; }

	protected abstract string Growth { get; }

	protected abstract string[] Paths { get; }

	protected abstract string Chosen { get; }

	protected abstract int[] Nodes { get; }

	protected abstract int Stones { get; }

	protected abstract int MaxStones { get; }

	protected virtual string Extra => null;

	protected abstract float Xp { get; set; }

	public override Vector2 InitialSize
	{
		get
		{
			float num = Mathf.Min(1280f, (float)Verse.UI.screenWidth - 40f);
			float num2 = num * 0.56279904f;
			if (num2 > (float)Verse.UI.screenHeight - 40f)
			{
				num2 = (float)Verse.UI.screenHeight - 40f;
				num = num2 * 1.7768332f;
			}
			return new Vector2(num, num2);
		}
	}

	protected override float Margin => 0f;

	private string Shown
	{
		get
		{
			if (Chosen != null)
			{
				return Chosen;
			}
			if (shown == null)
			{
				shown = Paths.FirstOrDefault();
			}
			return shown;
		}
	}

	protected Dialog_PathBook(Pawn p)
	{
		this.p = p;
		forcePause = true;
		doCloseX = false;
		doWindowBackground = false;
		drawShadow = false;
		draggable = false;
		closeOnClickedOutside = false;
		absorbInputAroundWindow = false;
	}

	protected abstract void Choose(string path);

	protected abstract string Label(string path);

	protected abstract string Lore(string path);

	protected abstract Texture2D Emblem(string path);

	protected abstract string[][] Powers(string path);

	protected abstract void Progress(out float have, out float need);

	protected abstract Vector2[] Shape(string path);

	protected virtual Texture2D PowerIcon(string path, int k)
	{
		return null;
	}

	protected virtual string Note(string path)
	{
		return null;
	}

	protected abstract float XpFor(int stones);

	protected virtual void Synced()
	{
	}

	protected abstract void Unchoose();

	public override void PostOpen()
	{
		base.PostOpen();
		SheetWindow.StartMaximized(this, ref sheetMax, ref sheetNormal);
	}

	private static Rect Sub(Rect r, float x0, float y0, float x1, float y1)
	{
		return new Rect(r.x + r.width * x0, r.y + r.height * y0, r.width * (x1 - x0), r.height * (y1 - y0));
	}

	public override void DoWindowContents(Rect inRect)
	{
		if (Item == null || Item.Destroyed)
		{
			Close(doCloseSound: false);
			return;
		}
		GUI.DrawTexture(inRect, ArtifactoricsTex.Panel, ScaleMode.StretchToFill);
		Rect close = new Rect(inRect.xMax - inRect.width * 0.052f - 26f, inRect.y + inRect.height * 0.085f, 24f, 24f);
		SheetWindow.MaximizeButton(this, close, ref sheetMax, ref sheetNormal);
		Color color = GUI.color;
		GameFont font = Text.Font;
		TextAnchor anchor = Text.Anchor;
		try
		{
			DrawLeft(Sub(inRect, 0.062f, 0.15f, 0.228f, 0.845f));
			DrawMiddle(Sub(inRect, 0.262f, 0.11f, 0.74f, 0.89f));
			DrawRight(Sub(inRect, 0.774f, 0.15f, 0.94f, 0.845f));
		}
		finally
		{
			GUI.color = color;
			Text.Font = font;
			Text.Anchor = anchor;
		}
		if (SheetWindow.CloseButton(close))
		{
			Close();
		}
	}

	private static float Write(Rect r, ref float y, string text, GameFont font, Color color, TextAnchor anchor = TextAnchor.UpperLeft)
	{
		Text.Font = font;
		GUI.color = color;
		Text.Anchor = anchor;
		float num = Text.CalcHeight(text, r.width);
		ArtInk.Label(new Rect(r.x, y, r.width, num), text);
		Text.Anchor = TextAnchor.UpperLeft;
		y += num;
		return num;
	}

	private static void Rule(Rect r, ref float y)
	{
		y += 6f;
		GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
		GUI.DrawTexture(new Rect(r.x, y, r.width, 1f), BaseContent.WhiteTex);
		y += 8f;
	}

	private void DrawLeft(Rect r)
	{
		float y = r.y;
		Write(r, ref y, Heading, GameFont.Medium, Ink);
		y += 6f;
		int stones = Stones;
		Write(r, ref y, "Stones: " + stones + " / " + MaxStones, GameFont.Small, Ink);
		if (stones < MaxStones)
		{
			Progress(out var have, out var need);
			Write(r, ref y, "Next stone: " + have.ToString("N0") + " / " + need.ToString("N0"), GameFont.Small, InkSoft);
			y += 3f;
			Rect position = new Rect(r.x, y, r.width, 12f);
			GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.22f);
			GUI.DrawTexture(position, BaseContent.WhiteTex);
			GUI.color = new Color(0.45f, 0.3f, 0.08f);
			GUI.DrawTexture(new Rect(position.x, position.y, position.width * ((need > 0f) ? Mathf.Clamp01(have / need) : 0f), position.height), BaseContent.WhiteTex);
			y += 18f;
		}
		else
		{
			Write(r, ref y, "Every stone is lit.", GameFont.Small, InkSoft);
		}
		Write(r, ref y, Growth, GameFont.Small, InkSoft);
		Rule(r, ref y);
		if (Prefs.DevMode)
		{
			float num = (r.width - 8f) / 3f;
			Text.Font = GameFont.Tiny;
			if (Widgets.ButtonText(new Rect(r.x, r.yMax - 24f, num, 24f), "Dev: +1 stone"))
			{
				Xp = XpFor(Mathf.Min(MaxStones, Stones + 1));
				Synced();
			}
			if (Widgets.ButtonText(new Rect(r.x + num + 4f, r.yMax - 24f, num, 24f), "Dev: all stones"))
			{
				Xp = XpFor(MaxStones);
				Synced();
			}
			if (Widgets.ButtonText(new Rect(r.x + (num + 4f) * 2f, r.yMax - 24f, num, 24f), "Dev: reset"))
			{
				Xp = 0f;
				Unchoose();
				selStone = -1;
				Synced();
			}
			Text.Font = GameFont.Small;
			r.height -= 30f;
		}
		if (Chosen != null)
		{
			Texture2D texture2D = Emblem(Chosen);
			float num2 = Mathf.Min(r.width * 0.5f, r.yMax - y - 80f, 140f);
			if (texture2D != null && num2 > 40f)
			{
				GUI.color = Color.white;
				GUI.DrawTexture(new Rect(r.x + (r.width - num2) / 2f, y, num2, num2), texture2D, ScaleMode.ScaleToFit);
				y += num2 + 6f;
			}
			Write(r, ref y, Label(Chosen).CapitalizeFirst(), GameFont.Medium, Ink, TextAnchor.UpperCenter);
			string text = Note(Chosen);
			if (text != null)
			{
				y += 4f;
				Write(r, ref y, text, GameFont.Small, InkSoft, TextAnchor.UpperCenter);
			}
			GUI.color = Color.white;
			return;
		}
		Write(r, ref y, "Choose the path", GameFont.Small, Ink);
		y += 4f;
		float num3 = Mathf.Clamp((r.yMax - y) / (float)Mathf.Max(1, Paths.Length) - 4f, 40f, 64f);
		string[] paths = Paths;
		foreach (string text2 in paths)
		{
			Rect rect = new Rect(r.x, y, r.width, num3);
			bool flag = text2 == Shown;
			if (flag || Mouse.IsOver(rect))
			{
				GUI.color = new Color(Gold.r, Gold.g, Gold.b, flag ? 0.28f : 0.12f);
				GUI.DrawTexture(rect, BaseContent.WhiteTex);
			}
			Texture2D texture2D2 = Emblem(text2);
			Rect position2 = new Rect(rect.x + 2f, rect.y + 2f, num3 - 4f, num3 - 4f);
			GUI.color = Color.white;
			if (texture2D2 != null)
			{
				GUI.DrawTexture(position2, texture2D2, ScaleMode.ScaleToFit);
			}
			string text3 = Note(text2);
			Text.Font = GameFont.Small;
			GUI.color = Ink;
			float num4 = position2.xMax + 8f;
			if (text3 == null)
			{
				Text.Anchor = TextAnchor.MiddleLeft;
				ArtInk.Label(new Rect(num4, rect.y, rect.xMax - num4, rect.height), Label(text2).CapitalizeFirst());
				Text.Anchor = TextAnchor.UpperLeft;
			}
			else
			{
				ArtInk.Label(new Rect(num4, rect.y + num3 / 2f - 22f, rect.xMax - num4, 22f), Label(text2).CapitalizeFirst());
				GUI.color = InkRed;
				ArtInk.Label(new Rect(num4, rect.y + num3 / 2f, rect.xMax - num4, 22f), text3);
			}
			if (Widgets.ButtonInvisible(rect) && shown != text2)
			{
				shown = text2;
				selStone = -1;
				scroll = Vector2.zero;
				SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
			}
			y += num3 + 4f;
		}
		GUI.color = Color.white;
	}

	private void DrawMiddle(Rect r)
	{
		string text = Shown;
		if (text == null)
		{
			return;
		}
		float num = r.y + r.height * 0.06f;
		Rect rect = new Rect(r.x, num, r.width, r.yMax - num);
		Event current = Event.current;
		if (Mouse.IsOver(rect))
		{
			if (current.type == EventType.ScrollWheel)
			{
				float num2 = zoom;
				zoom = Mathf.Clamp(zoom * ((current.delta.y < 0f) ? 1.12f : (25f / 28f)), 0.8f, 3.5f);
				Vector2 vector = current.mousePosition - rect.center;
				pan = vector - (vector - pan) * (zoom / num2);
				current.Use();
			}
			if (current.type == EventType.MouseDown)
			{
				dragging = true;
				dragged = 0f;
			}
		}
		if (dragging && current.type == EventType.MouseDrag)
		{
			pan += current.delta;
			dragged += current.delta.magnitude;
			current.Use();
		}
		if (current.rawType == EventType.MouseUp)
		{
			dragging = false;
		}
		float num3 = Mathf.Min(rect.width, rect.height) * zoom;
		float num4 = Mathf.Max(0f, (num3 - rect.width) / 2f) + rect.width * 0.15f;
		float num5 = Mathf.Max(0f, (num3 - rect.height) / 2f) + rect.height * 0.15f;
		pan = new Vector2(Mathf.Clamp(pan.x, 0f - num4, num4), Mathf.Clamp(pan.y, 0f - num5, num5));
		Widgets.BeginGroup(rect);
		try
		{
			Rect rect2 = new Rect(rect.width / 2f - num3 / 2f + pan.x, rect.height / 2f - num3 / 2f + pan.y, num3, num3);
			Texture2D texture2D = Emblem(text);
			if (texture2D != null)
			{
				GUI.color = new Color(1f, 1f, 1f, 0.16f);
				GUI.DrawTexture(rect2.ContractedBy(num3 * 0.2f), texture2D, ScaleMode.ScaleToFit);
			}
			GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.3f);
			GUI.DrawTexture(rect2.ContractedBy(num3 * 0.02f), RitualTex.Ring);
			DrawStones(rect2.ContractedBy(num3 * 0.1f), text, (text == Chosen) ? Stones : 0, current);
		}
		finally
		{
			Widgets.EndGroup();
		}
		if ((zoom != 1f || pan != Vector2.zero) && Button(new Rect(rect.xMax - 100f, rect.y, 96f, 30f), "Centre", enabled: true))
		{
			zoom = 1f;
			pan = Vector2.zero;
		}
		GUI.color = Color.white;
	}

	private void DrawStones(Rect r, string path, int lit, Event e)
	{
		Vector2[] pts = Shape(path);
		if (pts == null || pts.Length == 0)
		{
			return;
		}
		for (int j = 1; j < pts.Length; j++)
		{
			Widgets.DrawLine(P(j - 1), P(j), (j <= lit - 1) ? new Color(Gold.r, Gold.g, Gold.b, 0.9f) : new Color(Ink.r, Ink.g, Ink.b, 0.3f), 2f);
		}
		float num = Mathf.Clamp(r.width / 560f, 0.55f, 2.2f);
		for (int k = 0; k < pts.Length; k++)
		{
			int num2 = Array.IndexOf(Nodes, k + 1);
			bool flag = num2 >= 0;
			float num3 = (flag ? 54f : 24f) * num;
			Vector2 vector = P(k);
			Rect rect = new Rect(vector.x - num3 / 2f, vector.y - num3 / 2f, num3, num3);
			int num4 = ((k < lit) ? 2 : ((path == Chosen && k == lit) ? 1 : 0));
			Texture2D texture2D = (flag ? PowerIcon(path, num2) : null);
			if (flag)
			{
				GUI.color = new Color(Gold.r, Gold.g, Gold.b, (num4 == 2) ? 0.95f : 0.5f);
				GUI.DrawTexture(rect.ExpandedBy(num3 * 0.18f), RitualTex.Ring);
			}
			GUI.color = Color.white;
			if (texture2D != null)
			{
				GUI.color = new Color(1f, 1f, 1f, (num4 == 2) ? 1f : 0.72f);
				GUI.DrawTexture(rect, texture2D, ScaleMode.ScaleToFit);
			}
			else
			{
				GUI.DrawTexture(rect, StoneTex(num4));
			}
			bool num5 = Mouse.IsOver(rect);
			if (num5 || selStone == k)
			{
				GUI.color = ((selStone == k) ? new Color(InkRed.r, InkRed.g, InkRed.b, 0.9f) : new Color(Ink.r, Ink.g, Ink.b, 0.45f));
				GUI.DrawTexture(rect.ExpandedBy(num3 * (flag ? 0.3f : 0.4f)), RitualTex.Ring);
			}
			if (num5 && e.type == EventType.MouseUp && e.button == 0 && dragged < 6f)
			{
				selStone = ((selStone == k) ? (-1) : k);
				scroll = Vector2.zero;
				SoundDefOf.Tick_High.PlayOneShotOnCamera();
			}
		}
		GUI.color = Color.white;
		Vector2 P(int i)
		{
			return new Vector2(r.x + pts[i].x * r.width, r.y + pts[i].y * r.height);
		}
	}

	private void DrawRight(Rect r)
	{
		string text = Shown;
		if (text == null)
		{
			return;
		}
		bool flag = Chosen == null;
		Rect r2 = new Rect(r.x, r.yMax - 40f, r.width, 36f);
		Rect outRect = new Rect(r.x, r.y, r.width, (flag ? (r2.y - 8f) : r.yMax) - r.y);
		string[][] array = Powers(text);
		float width = outRect.width - 16f;
		List<(string, GameFont, Color, float)> list = new List<(string, GameFont, Color, float)>();
		if (selStone >= 0)
		{
			int stone = selStone + 1;
			int num = Array.IndexOf(Nodes, stone);
			bool flag2 = text == Chosen && Stones >= stone;
			list.Add(("Stone " + stone + (flag2 ? "" : "  (not yet lit)"), GameFont.Medium, Ink, 6f));
			if (num >= 0 && num < array.Length)
			{
				list.Add((array[num][0].CapitalizeFirst(), GameFont.Small, InkRed, 4f));
				list.Add((array[num][1], GameFont.Small, InkSoft, 10f));
			}
			else
			{
				int num2 = Nodes.FirstOrDefault((int x) => x > stone);
				int num3 = Array.IndexOf(Nodes, num2);
				list.Add(((num2 > 0 && num3 < array.Length) ? ("A step on the way. At stone " + num2 + ": " + array[num3][0] + ".") : "A step on the way.", GameFont.Small, InkSoft, 10f));
			}
			list.Add(("Click the stone again, or anywhere on the path's name, to return.", GameFont.Small, InkGrey, 0f));
		}
		else
		{
			list.Add((Label(text).CapitalizeFirst(), GameFont.Medium, Ink, 6f));
			list.Add((Lore(text), GameFont.Small, InkSoft, 12f));
			for (int i = 0; i < array.Length; i++)
			{
				bool flag3 = text == Chosen && Stones >= Nodes[i];
				list.Add(("Stone " + Nodes[i] + ": " + array[i][0].CapitalizeFirst() + (flag3 ? "" : ((text == Chosen) ? "  (not yet)" : "")), GameFont.Small, flag3 ? InkRed : Ink, 2f));
				list.Add((array[i][1], GameFont.Small, InkSoft, 10f));
			}
			if (Extra != null)
			{
				list.Add((Extra, GameFont.Small, InkGrey, 0f));
			}
		}
		float num4 = 0f;
		foreach (var item in list)
		{
			Text.Font = item.Item2;
			num4 += Text.CalcHeight(item.Item1, width) + item.Item4;
		}
		Widgets.BeginScrollView(outRect, ref scroll, new Rect(0f, 0f, width, num4 + 10f));
		try
		{
			Rect r3 = new Rect(0f, 0f, width, num4);
			float y = 0f;
			for (int j = 0; j < list.Count; j++)
			{
				float num5 = y;
				Write(r3, ref y, list[j].Item1, list[j].Item2, list[j].Item3);
				if (j == 0 && selStone >= 0 && Widgets.ButtonInvisible(new Rect(0f, num5, width, y - num5)))
				{
					selStone = -1;
				}
				y += list[j].Item4;
			}
		}
		finally
		{
			Widgets.EndScrollView();
		}
		Text.Font = GameFont.Small;
		if (flag && Button(r2, "Choose this path", enabled: true))
		{
			string pick = text;
			Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Give " + Item.LabelShort + " to " + Label(pick) + "? It can never be changed.", delegate
			{
				Choose(pick);
				selStone = -1;
				SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
			}));
		}
		GUI.color = Color.white;
	}

	protected static Texture2D StoneTex(int state)
	{
		if (locked == null)
		{
			locked = ContentFinder<Texture2D>.Get("UI/Artifactorics/Amulet/Stone_Locked", reportFailure: false) ?? BaseContent.GreyTex;
			avail = ContentFinder<Texture2D>.Get("UI/Artifactorics/Amulet/Stone_Available", reportFailure: false) ?? locked;
			opened = ContentFinder<Texture2D>.Get("UI/Artifactorics/Amulet/Stone_Opened", reportFailure: false) ?? BaseContent.WhiteTex;
		}
		return state switch
		{
			1 => avail, 
			2 => opened, 
			_ => locked, 
		};
	}

	private static bool Button(Rect r, string label, bool enabled)
	{
		Texture2D image = ((!enabled || !Mouse.IsOver(r)) ? ArtifactoricsTex.ButtonNormal : (Input.GetMouseButton(0) ? ArtifactoricsTex.ButtonPressed : ArtifactoricsTex.ButtonHover));
		GUI.color = (enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f));
		GUI.DrawTexture(r, image, ScaleMode.StretchToFill);
		GUI.color = (enabled ? Ink : InkGrey);
		Text.Anchor = TextAnchor.MiddleCenter;
		ArtInk.Label(r, label);
		Text.Anchor = TextAnchor.UpperLeft;
		GUI.color = Color.white;
		if (!enabled)
		{
			return false;
		}
		if (Widgets.ButtonInvisible(r))
		{
			SoundDefOf.Click.PlayOneShotOnCamera();
			return true;
		}
		return false;
	}

	protected static Vector2 V(float x, float y)
	{
		return new Vector2(x, y);
	}

	protected static string PlainName(Thing t)
	{
		QualityCategory qc;
		string text = (t.TryGetQuality(out qc) ? (" (" + qc.GetLabel() + ")") : "");
		return t.def.LabelCap + text;
	}
}
