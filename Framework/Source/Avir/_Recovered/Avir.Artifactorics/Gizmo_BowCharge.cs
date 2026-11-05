using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class Gizmo_BowCharge : Gizmo
{
	public CompStriderBow bow;

	private static readonly Dictionary<string, Texture2D> fills = new Dictionary<string, Texture2D>();

	private static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(new Color(0.08f, 0.08f, 0.1f, 0.6f));

	public Gizmo_BowCharge()
	{
		Order = -100f;
	}

	public override float GetWidth(float maxWidth)
	{
		return 140f;
	}

	private static Texture2D Fill(string dynasty)
	{
		string key = dynasty ?? "";
		if (fills.TryGetValue(key, out var value))
		{
			return value;
		}
		Color color = dynasty switch
		{
			"Shadowsong" => new Color(0.62f, 0.5f, 0.9f), 
			"Bloodmoon" => new Color(0.8f, 0.22f, 0.2f), 
			"Evershadow" => new Color(0.3f, 0.58f, 0.9f), 
			_ => new Color(0.78f, 0.8f, 0.88f), 
		};
		return fills[key] = SolidColorMaterials.NewSolidColorTexture(color);
	}

	public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
	{
		Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
		Widgets.DrawWindowBackground(rect);
		Rect rect2 = rect.ContractedBy(6f);
		ArtifactState artifactState = StriderBow.Quiver(bow.parent);
		float num = artifactState?.charge ?? 0f;
		int num2 = ((artifactState != null) ? Mathf.FloorToInt(num / StriderBow.ShotCharge + 0.0001f) : 0);
		Text.Font = GameFont.Tiny;
		Widgets.Label(new Rect(rect2.x, rect2.y, rect2.width, rect2.height / 2f), "Charge");
		Rect rect3 = new Rect(rect2.x, rect2.y + rect2.height / 2f, rect2.width, rect2.height / 2f);
		Widgets.FillableBar(rect3, Mathf.Clamp01(num), Fill(bow.Props.dynasty), Empty, doBorder: true);
		Text.Anchor = TextAnchor.MiddleCenter;
		Widgets.Label(rect3, (artifactState == null) ? "no heart" : num.ToStringPercent());
		Text.Anchor = TextAnchor.UpperLeft;
		Text.Font = GameFont.Small;
		TooltipHandler.TipRegion(rect, (artifactState == null) ? "The bow has no heart: it cannot shoot." : ("Every arrow is drawn from the bow's own charge. " + num2 + " arrows left; an empty bow does not shoot until it is recharged."));
		return new GizmoResult(GizmoState.Clear);
	}
}
