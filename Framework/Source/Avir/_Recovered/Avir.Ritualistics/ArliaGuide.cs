using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Ritualistics;

public static class ArliaGuide
{
	public class Step
	{
		public string spot;

		public string text;

		public Step(string spot, string text)
		{
			this.spot = spot;
			this.text = text;
		}
	}

	public static readonly List<Step> Book = new List<Step>
	{
		new Step(null, "Pay attention, would you? I won't bother explaining basics again."),
		new Step("list", "Your rituals live here - the ones in the book, and the ones you wrote yourself. Pick one; look before you draw."),
		new Step("picture", "Here the chosen ritual is drawn as it will lie on the floor. Click a rune to read about it."),
		new Step("new", "And here you write your own. Do try to be original. Or at least correct."),
		new Step("gauge", "The stream of the land. The higher it climbs, the angrier the sky. Watch it, if you value your roof."),
		new Step("codex", "The codex. Every rune I am willing to explain. Open it before you open your mouth."),
		new Step("conduit", "Who holds the ritual: the living, or a thing of energy. Choose someone with nerves. Or something without any."),
		new Step(null, "That is the book. Open \"New ritual...\" when you feel brave - I will be waiting there.")
	};

	public static readonly List<Step> Editor = new List<Step>
	{
		new Step("circle", "This is the circle. The rings are sections; what you write into them happens - more or less. Inner ring first. Yes, the order matters. No, I will not say why twice."),
		new Step("runes", "Runes. Verbs do things; the rest shape what is done. Click one, then a section. It is not sorcery. Well. It is if you can't get it."),
		new Step("components", "The small circles feed the great one: crystals, feathers, batteries. Leave them empty and it starves."),
		new Step("reading", "Here the ritual tells you what it will do. Read it. Reading is cheaper than burying ritualists. To each their own, of course."),
		new Step("arlia", "And this here - is for your charming betters. Send me away if you're eager to fail."),
		new Step(null, "That is all. The rest you will learn the way everyone does - by blowing something up. Class dismissed. Try not to die.")
	};

	private static readonly Dictionary<string, Rect> spots = new Dictionary<string, Rect>();

	private static List<Step> running;

	private static int step;

	private static Rect lastBox;

	private static readonly Color Shade = new Color(0f, 0f, 0f, 0.62f);

	private static readonly Color Back = new Color(0.07f, 0.02f, 0.03f, 0.95f);

	private static readonly Color Crimson = new Color(0.62f, 0.06f, 0.09f);

	public static void Spot(string name, Rect r)
	{
		spots[name] = r;
	}

	public static bool Active(List<Step> which)
	{
		return running == which;
	}

	public static void Offer(List<Step> which)
	{
		if (Arlia.On && AvirMod.Settings != null && !((which == Book) ? AvirMod.Settings.arliaIntroBook : AvirMod.Settings.arliaIntroEditor) && running == null)
		{
			running = which;
			step = 0;
			spots.Clear();
		}
	}

	private static void Finish()
	{
		if (running == Book)
		{
			AvirMod.Settings.arliaIntroBook = true;
		}
		else if (running == Editor)
		{
			AvirMod.Settings.arliaIntroEditor = true;
		}
		AvirMod.Settings.Write();
		running = null;
	}

	public static void Abandon(List<Step> which)
	{
		if (running == which)
		{
			running = null;
		}
	}

	private static Rect NextR(Rect box)
	{
		return new Rect(box.xMax - 120f, box.yMax - 40f, 108f, 30f);
	}

	private static Rect EnoughR(Rect box)
	{
		return new Rect(box.xMax - 236f, box.yMax - 40f, 108f, 30f);
	}

	public static void Block(List<Step> which)
	{
		if (running != which || which == null)
		{
			return;
		}
		Event current = Event.current;
		if (current.type == EventType.MouseDown && current.button == 0 && lastBox.width > 0f)
		{
			if (NextR(lastBox).Contains(current.mousePosition))
			{
				step++;
				SoundDefOf.Tick_High.PlayOneShotOnCamera();
				if (step >= which.Count)
				{
					Finish();
				}
				current.Use();
				return;
			}
			if (step + 1 < which.Count && EnoughR(lastBox).Contains(current.mousePosition))
			{
				Finish();
				current.Use();
				return;
			}
		}
		if (current.type == EventType.MouseDown || current.type == EventType.MouseUp || current.type == EventType.ScrollWheel)
		{
			current.Use();
		}
	}

	public static void Draw(List<Step> which, Rect window)
	{
		if (running != which || which == null)
		{
			return;
		}
		if (ArliaGuide.step >= which.Count)
		{
			Finish();
			return;
		}
		Step step = which[ArliaGuide.step];
		Rect value;
		Rect rect = ((step.spot != null && spots.TryGetValue(step.spot, out value)) ? value.ExpandedBy(6f) : Rect.zero);
		Color color = GUI.color;
		GUI.color = Shade;
		if (rect.width <= 0f)
		{
			GUI.DrawTexture(window, BaseContent.WhiteTex);
		}
		else
		{
			GUI.DrawTexture(new Rect(window.x, window.y, window.width, Mathf.Max(0f, rect.y - window.y)), BaseContent.WhiteTex);
			GUI.DrawTexture(new Rect(window.x, rect.yMax, window.width, Mathf.Max(0f, window.yMax - rect.yMax)), BaseContent.WhiteTex);
			GUI.DrawTexture(new Rect(window.x, rect.y, Mathf.Max(0f, rect.x - window.x), rect.height), BaseContent.WhiteTex);
			GUI.DrawTexture(new Rect(rect.xMax, rect.y, Mathf.Max(0f, window.xMax - rect.xMax), rect.height), BaseContent.WhiteTex);
			GUI.color = new Color(Crimson.r * 1.5f, Crimson.g * 1.5f, Crimson.b * 1.5f, 0.9f);
			Widgets.DrawBox(rect, 3);
		}
		float num = 460f;
		string text = "<i>" + step.text + "</i>";
		Text.Font = GameFont.Small;
		float num2 = Text.CalcHeight(text, num - 28f) + 70f;
		float x;
		float y;
		if (rect.width <= 0f)
		{
			x = window.center.x - num / 2f;
			y = window.center.y - num2 / 2f;
		}
		else
		{
			x = ((rect.center.x < window.center.x) ? Mathf.Min(window.xMax - num - 20f, rect.xMax + 24f) : Mathf.Max(window.x + 20f, rect.x - num - 24f));
			y = Mathf.Clamp(rect.center.y - num2 / 2f, window.y + 20f, window.yMax - num2 - 20f);
			if (rect.width > window.width * 0.6f)
			{
				x = window.center.x - num / 2f;
				y = ((rect.yMax + 20f > window.yMax - num2) ? (window.y + 20f) : (rect.yMax + 20f));
			}
		}
		Rect rect2 = (lastBox = new Rect(x, y, num, num2));
		GUI.color = Back;
		GUI.DrawTexture(rect2, BaseContent.WhiteTex);
		GUI.color = Crimson;
		Widgets.DrawBox(rect2, 2);
		GUI.DrawTexture(new Rect(rect2.x, rect2.y, 4f, rect2.height), BaseContent.WhiteTex);
		GUI.color = new Color(0.93f, 0.86f, 0.84f);
		Widgets.Label(new Rect(rect2.x + 16f, rect2.y + 12f, rect2.width - 28f, num2 - 60f), text);
		Text.Font = GameFont.Tiny;
		GUI.color = new Color(Crimson.r * 1.4f, Crimson.g * 1.4f, Crimson.b * 1.4f);
		Widgets.Label(new Rect(rect2.x + 16f, rect2.yMax - 44f, 200f, 20f), "<i>- Arlie</i>   " + (ArliaGuide.step + 1) + " / " + which.Count);
		Text.Font = GameFont.Small;
		GUI.color = Color.white;
		Widgets.DrawButtonGraphic(NextR(rect2));
		Text.Anchor = TextAnchor.MiddleCenter;
		Widgets.Label(NextR(rect2), (ArliaGuide.step + 1 < which.Count) ? "Next" : "Done");
		if (ArliaGuide.step + 1 < which.Count)
		{
			Widgets.DrawButtonGraphic(EnoughR(rect2));
			Widgets.Label(EnoughR(rect2), "Enough");
		}
		Text.Anchor = TextAnchor.UpperLeft;
		GUI.color = color;
		TooltipHandler.ClearTooltipsFrom(window);
	}
}
