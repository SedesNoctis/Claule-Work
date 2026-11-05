using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class ITab_Pawn_Artifacts : ITab
{
	private Thing selected;

	private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);

	private static readonly Color InkSoft = new Color(0.1f, 0.06f, 0.025f);

	private static readonly Color Gold = new Color(0.7f, 0.5f, 0.12f);

	private Vector2 tabScroll;

	private Pawn Wearer => SelPawn;

	public override bool IsVisible
	{
		get
		{
			Pawn wearer = Wearer;
			if (wearer != null && wearer.equipment?.Primary?.Socket()?.HasArtifact == true)
			{
				return true;
			}
			if (AmuletDevelopment.WornBy(Wearer) != null)
			{
				return true;
			}
			List<Apparel> list = Wearer?.apparel?.WornApparel;
			if (list == null)
			{
				return false;
			}
			for (int i = 0; i < list.Count; i++)
			{
				CompArtifactSocket compArtifactSocket = list[i].Socket();
				if (compArtifactSocket != null && compArtifactSocket.HasArtifact)
				{
					return true;
				}
			}
			if (Prefs.DevMode)
			{
				return EmptySockets().Count > 0;
			}
			return false;
		}
	}

	public ITab_Pawn_Artifacts()
	{
		size = new Vector2(480f, 680f);
		labelKey = "Avir_TabArtifacts";
	}

	protected override void UpdateSize()
	{
		base.UpdateSize();
		size.y = Mathf.Max(320f, Mathf.Min(680f, PaneTopY - 30f - 4f));
	}

	private List<Thing> Inlaid()
	{
		List<Thing> list = new List<Thing>();
		ThingWithComps thingWithComps = Wearer?.equipment?.Primary;
		if (thingWithComps != null)
		{
			CompArtifactSocket compArtifactSocket = thingWithComps.Socket();
			if (compArtifactSocket != null && compArtifactSocket.HasArtifact)
			{
				list.Add(thingWithComps);
			}
		}
		List<Apparel> list2 = Wearer?.apparel?.WornApparel;
		if (list2 == null)
		{
			return list;
		}
		foreach (Apparel item in list2)
		{
			CompArtifactSocket compArtifactSocket2 = item.Socket();
			if (compArtifactSocket2 != null && compArtifactSocket2.HasArtifact)
			{
				list.Add(item);
			}
		}
		CompAvirAmulet compAvirAmulet = AmuletDevelopment.WornBy(Wearer);
		if (compAvirAmulet != null && compAvirAmulet.Wearer() == Wearer)
		{
			list.Insert(0, compAvirAmulet.parent);
		}
		return list;
	}

	private List<Thing> EmptySockets()
	{
		List<Thing> list = new List<Thing>();
		ThingWithComps thingWithComps = Wearer?.equipment?.Primary;
		if (thingWithComps?.Socket() != null && !thingWithComps.Socket().HasArtifact)
		{
			list.Add(thingWithComps);
		}
		List<Apparel> list2 = Wearer?.apparel?.WornApparel;
		if (list2 != null)
		{
			foreach (Apparel item in list2)
			{
				if (item.TryGetComp<CompAvirAmulet>() == null && item.Socket() != null && !item.Socket().HasArtifact)
				{
					list.Add(item);
				}
			}
		}
		return list;
	}

	private void DrawDevBar(Rect r)
	{
		List<(string, Action)> list = new List<(string, Action)>();
		List<Thing> empty = EmptySockets();
		if (empty.Count > 0)
		{
			list.Add(("Dev: inlay...", delegate
			{
				Find.WindowStack.Add(new FloatMenu(empty.Select((Thing t) => new FloatMenuOption(t.LabelCapNoCount, delegate
				{
					ArtifactMetalChoice.Menu(ArtifactMetalChoice.Rims, delegate(ArtifactMetalDef rim)
					{
						ArtifactMetalChoice.Menu(ArtifactMetalChoice.HeartsFor(rim), delegate(ArtifactMetalDef heart)
						{
							CompArtifactSocket compArtifactSocket2 = t.Socket();
							if (compArtifactSocket2 != null && !compArtifactSocket2.HasArtifact)
							{
								compArtifactSocket2.inlaid = ArtifactMetalChoice.NewArtifact(heart, rim, QualityCategory.Normal);
								selected = t;
							}
						});
					});
				})).ToList()));
			}));
		}
		CompAvirAmulet am = selected?.TryGetComp<CompAvirAmulet>();
		ArtifactState st = ((am != null) ? null : selected?.Socket()?.inlaid);
		if (am != null)
		{
			list.Add(("Dev: metals...", delegate
			{
				ArtifactMetalChoice.Menu(ArtifactMetalChoice.Necklaces, delegate(ArtifactMetalDef n)
				{
					ArtifactMetalChoice.Menu(ArtifactMetalChoice.CoresFor(n), delegate(ArtifactMetalDef c)
					{
						ArtifactMetalChoice.SetAmuletMetals(am, n, c);
					});
				});
			}));
			list.Add(("Dev: quality...", delegate
			{
				ArtifactMetalChoice.QualityMenu(delegate(QualityCategory q)
				{
					ArtifactMetalChoice.SetAmuletQuality(am, q);
				});
			}));
		}
		else if (st != null)
		{
			list.Add(("Dev: metals...", delegate
			{
				ArtifactMetalChoice.Menu(ArtifactMetalChoice.Rims, delegate(ArtifactMetalDef rim)
				{
					ArtifactMetalChoice.Menu(ArtifactMetalChoice.HeartsFor(rim), delegate(ArtifactMetalDef heart)
					{
						st.rimMetal = rim;
						st.heartMetal = heart;
					});
				});
			}));
			list.Add(("Dev: quality...", delegate
			{
				ArtifactMetalChoice.QualityMenu(delegate(QualityCategory q)
				{
					st.heartQuality = q;
					st.settingQuality = q;
				});
			}));
			Thing sel = selected;
			list.Add(("Dev: remove", delegate
			{
				CompArtifactSocket compArtifactSocket = sel.Socket();
				if (compArtifactSocket != null)
				{
					compArtifactSocket.inlaid = null;
				}
			}));
		}
		if (list.Count == 0)
		{
			return;
		}
		float num = (r.width - 4f * (float)(list.Count - 1)) / (float)list.Count;
		Text.Font = GameFont.Tiny;
		for (int i = 0; i < list.Count; i++)
		{
			if (Widgets.ButtonText(new Rect(r.x + (float)i * (num + 4f), r.y, num, r.height), list[i].Item1))
			{
				list[i].Item2();
			}
		}
		Text.Font = GameFont.Small;
	}

	protected override void FillTab()
	{
		Rect rect = new Rect(0f, 0f, size.x, size.y);
		if (size.y >= 680f)
		{
			FillContent(rect);
			return;
		}
		ArtifactoricsTex.TileParchment(rect);
		Rect rect2 = new Rect(0f, 0f, size.x - 16f, 680f);
		Widgets.BeginScrollView(rect, ref tabScroll, rect2);
		FillContent(rect2);
		Widgets.EndScrollView();
	}

	private void FillContent(Rect r)
	{
		ArtifactoricsTex.TileParchment(r);
		Rect rect = r.ContractedBy(14f);
		List<Thing> list = Inlaid();
		if (list.Count == 0)
		{
			selected = null;
		}
		else if (selected == null || !list.Contains(selected))
		{
			selected = list[0];
		}
		if (Prefs.DevMode)
		{
			DrawDevBar(new Rect(rect.x, rect.yMax - 24f, rect.width, 24f));
		}
		if (list.Count == 0)
		{
			return;
		}
		Color color = GUI.color;
		GameFont font = Text.Font;
		TextAnchor anchor = Text.Anchor;
		try
		{
			float y = rect.y;
			GUI.color = Ink;
			Text.Font = GameFont.Medium;
			ArtInk.Label(new Rect(rect.x, y, rect.width, 32f), "Artifacts");
			y += 34f;
			Text.Font = GameFont.Small;
			foreach (Thing item in list)
			{
				Rect rect2 = new Rect(rect.x, y, rect.width, 30f);
				if (item == selected)
				{
					Widgets.DrawBoxSolid(rect2, new Color(Gold.r, Gold.g, Gold.b, 0.2f));
					GUI.color = Gold;
					Widgets.DrawBox(rect2);
					GUI.color = Color.white;
				}
				else if (Mouse.IsOver(rect2))
				{
					Widgets.DrawBoxSolid(rect2, new Color(1f, 0.9f, 0.6f, 0.14f));
				}
				Widgets.ThingIcon(new Rect(rect2.x + 3f, rect2.y + 3f, 24f, 24f), item);
				GUI.color = Ink;
				Text.Anchor = TextAnchor.MiddleLeft;
				string text = item.Socket()?.inlaid?.customName ?? item.TryGetComp<CompAvirAmulet>()?.customName;
				ArtInk.Label(new Rect(rect2.x + 32f, rect2.y, rect2.width - 32f - 26f, rect2.height), (text.NullOrEmpty() ? item.def.LabelCap.ToString() : (text + " - " + item.def.label)) + ((ArtifactCarrierUtility.CarrierOf(item) == ArtifactCarrier.Weapon) ? " (weapon)" : ""));
				Text.Anchor = TextAnchor.UpperLeft;
				GUI.color = Color.white;
				Rect rect3 = new Rect(rect2.xMax - 24f, rect2.y + 3f, 24f, 24f);
				TooltipHandler.TipRegion(rect3, "Rename");
				if (Widgets.ButtonImage(rect3, TexButton.Rename))
				{
					Thing thing = item;
					ArtifactState inl = thing.Socket()?.inlaid;
					CompAvirAmulet am = thing.TryGetComp<CompAvirAmulet>();
					if (am != null)
					{
						Find.WindowStack.Add(new Dialog_ArtifactRename(am.customName, delegate(string n)
						{
							am.customName = n;
						}));
					}
					else if (inl != null)
					{
						Find.WindowStack.Add(new Dialog_ArtifactRename(inl.customName, delegate(string n)
						{
							inl.customName = n;
						}));
					}
				}
				else if (Widgets.ButtonInvisible(rect2))
				{
					selected = item;
				}
				y += 32f;
			}
			CompAvirAmulet compAvirAmulet = selected.TryGetComp<CompAvirAmulet>();
			if (compAvirAmulet != null && compAvirAmulet.HasCore)
			{
				DrawAmulet(new Rect(rect.x, y + 8f, rect.width, rect.yMax - y - 8f), compAvirAmulet);
				return;
			}
			ArtifactState artifactState = selected.Socket()?.inlaid;
			if (artifactState != null)
			{
				y += 8f;
				float num = Mathf.Min(150f, rect.yMax - y - 70f);
				Rect position = new Rect(rect.x, y, num, num);
				if (artifactState.rimMetal != null)
				{
					GUI.color = artifactState.rimMetal.RimColor;
					GUI.DrawTexture(position, ArtifactoricsTex.HeartRimMask, ScaleMode.ScaleToFit);
				}
				if (artifactState.heartMetal != null)
				{
					GUI.color = artifactState.heartMetal.GemColorAt(Time.realtimeSinceStartup);
					GUI.DrawTexture(position, ArtifactoricsTex.GemBase, ScaleMode.ScaleToFit);
					GUI.color = Color.white;
					GUI.DrawTexture(position, ArtifactoricsTex.GemHighlight, ScaleMode.ScaleToFit);
				}
				GUI.color = ((artifactState.rimMetal != null) ? artifactState.rimMetal.RimColor : Color.white);
				GUI.DrawTexture(position, (artifactState.rimMetal != null) ? ArtifactoricsTex.HeartSettingMask : ArtifactoricsTex.HeartSetting, ScaleMode.ScaleToFit);
				Rect rect4 = new Rect(position.xMax + 12f, y, rect.xMax - position.xMax - 12f, num);
				GUI.color = Ink;
				ArtInk.Label(new Rect(rect4.x, rect4.y, rect4.width, 24f), selected.def.LabelCap);
				GUI.color = InkSoft;
				ArtInk.Label(new Rect(rect4.x, rect4.y + 26f, rect4.width, rect4.height - 26f), artifactState.InspectLines());
				DrawDevelopment(new Rect(rect.x, position.yMax + 10f, rect.width, rect.yMax - position.yMax - 10f), artifactState);
			}
		}
		finally
		{
			GUI.color = color;
			Text.Font = font;
			Text.Anchor = anchor;
		}
	}

	private void DrawAmulet(Rect r, CompAvirAmulet a)
	{
		float num = Mathf.Min(150f, r.height - 120f);
		Rect rect = new Rect(r.x, r.y, num, num);
		if (a.necklaceMetal != null)
		{
			GUI.color = a.necklaceMetal.RimColor;
			GUI.DrawTexture(rect, ArtifactoricsTex.HeartRimMask, ScaleMode.ScaleToFit);
		}
		GUI.color = a.coreMetal.GemColorAt(Time.realtimeSinceStartup);
		GUI.DrawTexture(rect, ArtifactoricsTex.GemBase, ScaleMode.ScaleToFit);
		GUI.color = Color.white;
		GUI.DrawTexture(rect, ArtifactoricsTex.GemHighlight, ScaleMode.ScaleToFit);
		GUI.color = ((a.necklaceMetal != null) ? a.necklaceMetal.RimColor : Color.white);
		GUI.DrawTexture(rect, (a.necklaceMetal != null) ? ArtifactoricsTex.HeartSettingMask : ArtifactoricsTex.HeartSetting, ScaleMode.ScaleToFit);
		if (a.subclass?.SlotIcon != null)
		{
			GUI.color = new Color(1f, 1f, 1f, a.slotOpen ? 1f : 0.35f);
			GUI.DrawTexture(rect.ContractedBy(num * 0.3f), a.subclass.SlotIcon, ScaleMode.ScaleToFit);
		}
		Rect rect2 = new Rect(rect.xMax + 12f, r.y, r.xMax - rect.xMax - 12f, num);
		GUI.color = Ink;
		ArtInk.Label(new Rect(rect2.x, rect2.y, rect2.width, 44f), a.parent.LabelCapNoCount);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(a.IsMulticlass ? "A'Shaign: every path" : (a.IsCustom ? "Own paths" : ("Caste: " + (a.caste?.LabelCap.ToString() ?? "not chosen"))));
		stringBuilder.Append("\nMain path: ").Append(a.subclass?.LabelCapFor(a) ?? "not chosen");
		if (a.subclass != null && !a.subclass.slotLabel.NullOrEmpty())
		{
			stringBuilder.Append("\nSlot: ").Append(a.subclass.slotLabel.CapitalizeFirst()).Append(a.slotOpen ? " (awake)" : " (asleep)");
		}
		stringBuilder.Append("\nStones opened: ").Append(AmuletDevelopment.TotalOpened(a));
		stringBuilder.Append("\nPoints: ").Append(AmuletDevelopment.PointsAvailable(a));
		if (!AmuletDevelopment.WorksFor(a, Wearer))
		{
			stringBuilder.Append("\nDead on this wearer: only an Avir can use it.");
		}
		GUI.color = InkSoft;
		ArtInk.Label(new Rect(rect2.x, rect2.y + 46f, rect2.width, rect2.height - 46f), stringBuilder.ToString());
		float num2 = rect.yMax + 12f;
		AmuletDevelopment.NextPointProgress(a, out var have, out var need);
		Text.Font = GameFont.Tiny;
		ArtInk.Label(new Rect(r.x, num2, r.width, 18f), "Experience: " + have.ToString("N0") + " / " + need.ToString("N0"));
		num2 += 18f;
		Rect rect3 = new Rect(r.x, num2, r.width, 10f);
		Widgets.DrawBoxSolid(rect3, new Color(0.25f, 0.17f, 0.08f, 0.3f));
		Widgets.DrawBoxSolid(new Rect(rect3.x, rect3.y, rect3.width * ((need > 0f) ? Mathf.Clamp01(have / need) : 0f), rect3.height), new Color(Gold.r, Gold.g, Gold.b, 0.9f));
		Text.Font = GameFont.Small;
		num2 += 20f;
		GUI.color = Color.white;
		if (Widgets.ButtonText(new Rect(r.x, num2, 220f, 32f), "Open development"))
		{
			Find.WindowStack.Add(new Dialog_AmuletDevelopment(a));
		}
	}

	private float DrawSelf(Rect r, float y)
	{
		CompStriderBow compStriderBow = selected.TryGetComp<CompStriderBow>();
		CompArchitectSpheres compArchitectSpheres = selected.TryGetComp<CompArchitectSpheres>();
		int stones;
		int num;
		float num2;
		float num3;
		string text;
		string text2;
		if (compStriderBow != null)
		{
			stones = compStriderBow.Stones;
			num = 18;
			num2 = compStriderBow.xp - StriderPaths.XpFor(stones);
			num3 = StriderPaths.StoneXp(stones + 1);
			text = ((compStriderBow.path == null) ? "not chosen yet" : StriderPaths.Label(compStriderBow.path).CapitalizeFirst());
			text2 = "Grows with every shot of its archer. Every arrow is drawn from its own charge.";
		}
		else
		{
			if (compArchitectSpheres == null)
			{
				return y;
			}
			stones = compArchitectSpheres.Stones;
			num = 12;
			num2 = compArchitectSpheres.xp - SpherePaths.XpFor(stones);
			num3 = SpherePaths.StoneXp(stones + 1);
			text = ((compArchitectSpheres.path == null) ? "not chosen yet" : SpherePaths.Label(compArchitectSpheres.path).CapitalizeFirst());
			text2 = "Grows with the architect's own work: building, crafting, art and thought.";
		}
		GUI.color = Ink;
		ArtInk.Label(new Rect(r.x, y, r.width, 24f), "Path: " + text);
		y += 24f;
		ArtInk.Label(new Rect(r.x, y, r.width, 24f), "Stones: " + stones + " / " + num + ((stones < num) ? ("    next: " + num2.ToString("N0") + " / " + num3.ToString("N0")) : ""));
		y += 24f;
		if (stones < num)
		{
			Rect rect = new Rect(r.x, y, r.width, 14f);
			Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.17f, 0.08f, 0.35f));
			Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * ((num3 > 0f) ? Mathf.Clamp01(num2 / num3) : 0f), rect.height), new Color(Gold.r, Gold.g, Gold.b, 0.85f));
			GUI.color = InkSoft;
			Widgets.DrawBox(rect);
			y += 20f;
		}
		GUI.color = InkSoft;
		float num4 = Text.CalcHeight(text2, r.width);
		ArtInk.Label(new Rect(r.x, y, r.width, num4), text2);
		return y + num4 + 8f;
	}

	private void DrawDevelopment(Rect r, ArtifactState st)
	{
		ArtifactoricsSettingsDef main = ArtifactoricsSettingsDef.Main;
		if (main == null)
		{
			return;
		}
		float y = r.y;
		if (!st.HasHeart)
		{
			GUI.color = InkSoft;
			ArtInk.Label(new Rect(r.x, y, r.width, 24f), "No heart - this artifact cannot develop.");
			return;
		}
		if (ArtifactCarrierUtility.IsSelfArtifact(selected))
		{
			y = DrawSelf(r, y);
		}
		else
		{
			st.NextPointProgress(out var have, out var need);
			GUI.color = Ink;
			ArtInk.Label(new Rect(r.x, y, r.width, 24f), "Next point: " + have.ToString("N0") + " / " + need.ToString("N0") + " xp");
			y += 24f;
			Rect rect = new Rect(r.x, y, r.width, 14f);
			Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.17f, 0.08f, 0.35f));
			Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * ((need > 0f) ? (have / need) : 0f), rect.height), new Color(Gold.r, Gold.g, Gold.b, 0.85f));
			GUI.color = InkSoft;
			Widgets.DrawBox(rect);
			y += 20f;
			GUI.color = Ink;
			string text = "Free points: " + st.PointsAvailable + "    Spent: " + st.PointsSpent + ((st.burntPoints > 0) ? (" (" + st.burntPoints + " burnt)") : "") + "    Earned: " + st.xpEarned.ToString("N0") + " xp";
			ArtInk.Label(new Rect(r.x, y, r.width, 24f), text);
			y += 26f;
			GUI.color = InkSoft;
			ArtInk.Label(new Rect(r.x, y, r.width, 40f), st.CellsLine());
			y += 42f;
			if (st.tkLevel > 0 || ArtifactTelekinesis.Radius(Wearer) > 0f)
			{
				string text2 = "Telekinesis circle: level " + st.tkLevel + "/" + main.telekinesisSlotCosts.Count;
				if (st.tkLevel > 0)
				{
					text2 = text2 + " (" + main.TelekinesisRadius(st.tkLevel).ToString("0") + " cells)";
				}
				if (AvirTelekinesisUtility.TryGetRadius(Wearer, out var radius))
				{
					Thing source;
					int num = ArtifactTelekinesis.BestLevel(Wearer, out source);
					text2 = text2 + ". " + Wearer.LabelShort + " reaches " + radius.ToString("0") + " cells";
					if (num > 0 && source != selected)
					{
						text2 = text2 + " (best circle: " + source.LabelNoCount + ")";
					}
					text2 += ".";
				}
				ArtInk.Label(new Rect(r.x, y, r.width, 36f), text2);
				y += 36f;
			}
			Text.Font = GameFont.Tiny;
			ArtInk.Label(new Rect(r.x, y, r.width, 34f), (ArtifactCarrierUtility.CarrierOf(selected) == ArtifactCarrier.Weapon) ? ("A weapon artifact learns only from Melee, at x" + main.weaponXpFactor.ToString("0.#") + " the earned experience; the wielder keeps " + main.pawnShare.ToStringPercent() + ". Cells are opened in the development window.") : ("The wearer keeps " + main.pawnShare.ToStringPercent() + " of the experience it earns; every worn artifact is credited with all of it. Cells are opened in the development window."));
			Text.Font = GameFont.Small;
			y += 36f;
		}
		GUI.color = Ink;
		ArtInk.Label(new Rect(r.x, y, 110f, 22f), "Charge " + st.charge.ToStringPercent());
		Rect rect2 = new Rect(r.x + 112f, y + 5f, r.width - 112f, 12f);
		Widgets.DrawBoxSolid(rect2, new Color(0.25f, 0.17f, 0.08f, 0.3f));
		Color color = ((st.charge < 0.2f) ? new Color(0.65f, 0.12f, 0.08f) : new Color(0.35f, 0.45f, 0.85f));
		Widgets.DrawBoxSolid(new Rect(rect2.x, rect2.y, rect2.width * st.charge, rect2.height), color);
		GUI.color = InkSoft;
		Widgets.DrawBox(rect2);
		y += 24f;
		if (Wearer != null && Wearer.IsColonistPlayerControlled)
		{
			GameComponent_ArtifactCharge get = GameComponent_ArtifactCharge.Get;
			float num2 = get?.Threshold(Wearer) ?? 0f;
			GUI.color = InkSoft;
			Text.Font = GameFont.Tiny;
			ArtInk.Label(new Rect(r.x, y + 2f, 150f, 20f), (num2 > 0f) ? ("Auto-recharge below " + num2.ToStringPercent()) : "Auto-recharge: off");
			Text.Font = GameFont.Small;
			GUI.color = Color.white;
			float num3 = Widgets.HorizontalSlider(new Rect(r.x + 152f, y + 4f, r.width - 152f - 150f, 18f), num2, 0f, 0.9f, middleAlignment: false, null, null, null, 0.05f);
			if (get != null && Mathf.Abs(num3 - num2) > 0.001f)
			{
				get.SetThreshold(Wearer, num3);
			}
			Rect rect3 = new Rect(r.xMax - 140f, y, 140f, 26f);
			string why;
			bool active = ArtifactCharge.CanRecharge(Wearer, out why);
			ArtifactCharge.RechargeCost(Wearer, out var wanted, out var allowed);
			if (Widgets.ButtonText(rect3, "Recharge now", drawBackground: true, doMouseoverSound: true, active))
			{
				ArtifactCharge.OrderRecharge(Wearer, forced: true);
			}
			TooltipHandler.TipRegion(rect3, "One hour where the pawn stands; fills all worn artifacts. Every 3 days.\n\nWould give " + Mathf.Min(wanted, allowed).ToStringPercent() + " psychic sensitivity for a day" + ((allowed < wanted) ? " (not enough to fill them)" : "") + "." + ((why != null) ? ("\n\nNot now: " + why + ".") : ""));
			y += 30f;
			if (ArtifactAbilities.For(Wearer).Any((ActiveStone x) => x.stone.ability.canAuto && x.stone.autoUse))
			{
				float num4 = get?.StoneThreshold(Wearer) ?? 0.25f;
				GUI.color = InkSoft;
				Text.Font = GameFont.Tiny;
				ArtInk.Label(new Rect(r.x, y + 2f, 150f, 20f), "Auto stones above " + num4.ToStringPercent());
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				float num5 = Widgets.HorizontalSlider(new Rect(r.x + 152f, y + 4f, r.width - 152f, 18f), num4, 0f, 0.9f, middleAlignment: false, null, null, null, 0.05f);
				if (get != null && Mathf.Abs(num5 - num4) > 0.001f)
				{
					get.SetStoneThreshold(Wearer, num5);
				}
				TooltipHandler.TipRegion(new Rect(r.x, y, r.width, 22f), "Stones set to auto-use stop using themselves while their artifact holds less charge than this.");
				y += 26f;
			}
		}
		GUI.color = Color.white;
		Rect rect4 = new Rect(r.x, y, 200f, 30f);
		if (Widgets.ButtonText(rect4, "Open development"))
		{
			SelfArtifactDevelopment.Open(selected, Wearer);
		}
		if (Dialog_ArtifactAttune.HasAnything(Wearer))
		{
			Rect rect5 = new Rect(rect4.xMax + 8f, y, 140f, 30f);
			if (Widgets.ButtonText(rect5, "Re-attune..."))
			{
				Find.WindowStack.Add(new Dialog_ArtifactAttune(Wearer));
			}
			string text3 = ArtifactMilestones.StatusLine(Wearer);
			if (text3 != null)
			{
				TooltipHandler.TipRegion(rect5, text3);
			}
		}
		y += 34f;
		string text4 = ArtifactMilestones.StatusLine(Wearer);
		if (text4 != null)
		{
			GUI.color = InkSoft;
			Text.Font = GameFont.Tiny;
			float num6 = Text.CalcHeight(text4, r.width);
			ArtInk.Label(new Rect(r.x, y, r.width, num6), text4);
			Text.Font = GameFont.Small;
			GUI.color = Color.white;
			y += num6 + 4f;
		}
		if (Prefs.DevMode)
		{
			float num7 = (r.width - 4f) / 2f;
			Rect rect6 = new Rect(r.x, y, num7, 26f);
			if (Widgets.ButtonText(rect6, "Dev: +10k xp"))
			{
				st.AddXp(10000f);
			}
			rect6.x += num7 + 4f;
			if (Widgets.ButtonText(rect6, "Dev: foreign"))
			{
				st.LoseForeignExtractionCells();
			}
		}
	}
}
