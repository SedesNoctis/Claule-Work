using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class Building_ShadowDecoy : Building, IAttackTarget, ILoadReferenceable
{
	public int until;

	public Pawn her;

	public Pawn skeletonOf;

	public Pawn beastOf;

	private Material mat;

	private static readonly Color Dark = new Color(0.1f, 0.06f, 0.18f, 0.62f);

	public bool lure;

	public string sprite;

	/// <summary>The colour the silhouette is projected in (the haze's amber mirage); none - the Linarian's own shadow.</summary>
	public Color? glow;

	public HashSet<int> fooled = new HashSet<int>();

	public Thing Thing => this;

	public LocalTargetInfo TargetCurrentlyAimingAt => LocalTargetInfo.Invalid;

	public float TargetPriorityFactor
	{
		get
		{
			if (!lure)
			{
				return 2.5f;
			}
			return 10f;
		}
	}

	public bool ThreatDisabled(IAttackTargetSearcher disabledFor)
	{
		if (!(disabledFor?.Thing is Pawn pawn) || her == null)
		{
			return false;
		}
		if (fooled.Contains(pawn.thingIDNumber))
		{
			return false;
		}
		return Rand.ValueSeeded(Gen.HashCombineInt(pawn.thingIDNumber, thingIDNumber)) >= LinarianGuise.Fooled(her, pawn);
	}

	protected override void Tick()
	{
		base.Tick();
		if (Find.TickManager.TicksGame >= until && !base.Destroyed)
		{
			Destroy();
		}
		else
		{
			if (!lure || !base.Spawned || !this.IsHashIntervalTick(300))
			{
				return;
			}
			foreach (Pawn item in base.Map.mapPawns.AllPawnsSpawned.Where((Pawn q) => fooled.Contains(q.thingIDNumber)).ToList())
			{
				LinarianGuise.Aim(item, this);
			}
		}
	}

	public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
	{
		absorbed = true;
	}

	protected override void DrawAt(Vector3 drawLoc, bool flip = false)
	{
		if (her == null)
		{
			return;
		}
		if (sprite != null)
		{
			Material material = MaterialPool.MatFrom(sprite, ShaderDatabase.MoteGlow);
			float num = 0.85f + 0.15f * Mathf.Sin((float)(Find.TickManager.TicksGame + thingIDNumber * 37) * 0.08f);
			Vector3 pos = drawLoc;
			pos.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
			Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1.8f * num, 1f, 1.8f * num)), material, 0);
		}
		else
		{
			if ((skeletonOf != null && DrawSkeleton(drawLoc)) || (beastOf != null && DrawBeast(drawLoc)))
			{
				return;
			}
			float num2 = 0.9f + 0.1f * Mathf.Sin((float)(Find.TickManager.TicksGame + thingIDNumber * 37) * 0.05f);
			Vector3 pos2 = drawLoc;
			pos2.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
			string text = LinarianVeil.InForm(her);
			if (text != null)
			{
				foreach (Graphic item in LinarianFormGraphics.Layers(her, text))
				{
					Material material2 = MaterialPool.MatFrom(new MaterialRequest(item.MatSouth.mainTexture, ShaderDatabase.Transparent, glow ?? Dark));
					Vector2 vector = item.drawSize * num2;
					Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos2, Quaternion.identity, new Vector3(vector.x, 1f, vector.y)), material2, 0);
					pos2.y += 0.0005f;
				}
				return;
			}
			RenderTexture renderTexture = PortraitsCache.Get(her, new Vector2(384f, 384f), Rot4.South, new Vector3(0f, 0f, 0.15f), 0.55f);
			if (!(renderTexture == null))
			{
				if (mat == null)
				{
					mat = new Material(ShaderDatabase.Transparent)
					{
						color = glow ?? Dark
					};
				}
				mat.mainTexture = renderTexture;
				Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos2, Quaternion.identity, new Vector3(3.6f * num2, 1f, 3.6f * num2)), mat, 0);
			}
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref sprite, "sprite");
		Scribe_Values.Look(ref until, "until", 0);
		Scribe_References.Look(ref her, "her");
		Scribe_Values.Look(ref lure, "lure", defaultValue: false);
		Scribe_References.Look(ref skeletonOf, "skeletonOf");
		Scribe_References.Look(ref beastOf, "beastOf");
	}

	private bool DrawBeast(Vector3 drawLoc)
	{
		PawnKindLifeStage pawnKindLifeStage = beastOf.ageTracker?.CurKindLifeStage;
		GraphicData graphicData = ((pawnKindLifeStage == null) ? null : ((beastOf.gender == Gender.Female && pawnKindLifeStage.femaleGraphicData != null) ? pawnKindLifeStage.femaleGraphicData : pawnKindLifeStage.bodyGraphicData));
		if (graphicData == null)
		{
			return false;
		}
		float num = 0.92f + 0.08f * Mathf.Sin((float)(Find.TickManager.TicksGame + thingIDNumber * 37) * 0.05f);
		Graphic graphic = GraphicDatabase.Get<Graphic_Multi>(graphicData.texPath, ShaderDatabase.Transparent, graphicData.drawSize, Dark);
		Vector3 pos = drawLoc;
		pos.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
		Vector2 vector = graphicData.drawSize * num;
		Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(vector.x, 1f, vector.y)), graphic.MatAt(beastOf.Rotation), 0);
		return true;
	}

	private bool DrawSkeleton(Vector3 drawLoc)
	{
		string text = skeletonOf.story?.bodyType?.bodyDessicatedGraphicPath;
		string text2 = HeadTypeDefOf.Skull?.graphicPath;
		if (text.NullOrEmpty())
		{
			return false;
		}
		Color color = new Color(0.85f, 0.82f, 0.74f, 0.55f + 0.1f * Mathf.Sin((float)Find.TickManager.TicksGame * 0.05f + (float)thingIDNumber));
		Vector3 pos = drawLoc;
		pos.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
		Graphic graphic = GraphicDatabase.Get<Graphic_Multi>(text, ShaderDatabase.Transparent, Vector2.one * 1.5f, color);
		Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1.5f, 1f, 1.5f)), graphic.MatSouth, 0);
		if (!text2.NullOrEmpty())
		{
			Graphic graphic2 = GraphicDatabase.Get<Graphic_Multi>(text2, ShaderDatabase.Transparent, Vector2.one * 1.5f, color);
			pos.y += 0.001f;
			pos.z += 0.34f;
			Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1.5f, 1f, 1.5f)), graphic2.MatSouth, 0);
		}
		return true;
	}

	public static Building_ShadowDecoy SpawnOne(Pawn her, IntVec3 at, Map map, int until, bool lure)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ShadowDecoy");
		if (namedSilentFail == null || map == null || !at.InBounds(map))
		{
			return null;
		}
		Building_ShadowDecoy building_ShadowDecoy = (Building_ShadowDecoy)ThingMaker.MakeThing(namedSilentFail);
		building_ShadowDecoy.her = her;
		building_ShadowDecoy.until = until;
		building_ShadowDecoy.lure = lure;
		GenSpawn.Spawn(building_ShadowDecoy, at, map, Rot4.South);
		if (her.Faction != null)
		{
			building_ShadowDecoy.SetFaction(her.Faction);
		}
		FleckMaker.ThrowSmoke(building_ShadowDecoy.DrawPos, map, 1f);
		return building_ShadowDecoy;
	}

	public static void Cast(Pawn her, Gene_LinarianSong g, IntVec3 c, float r)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ShadowDecoy");
		if (namedSilentFail == null)
		{
			return;
		}
		int num = Mathf.Clamp(2 + Mathf.FloorToInt(g.Power), 2, 5);
		for (int i = 0; i < num; i++)
		{
			if (CellFinder.TryFindRandomCellNear(c, her.Map, Mathf.Max(1, Mathf.RoundToInt(r * 0.6f)), (IntVec3 x) => x.Standable(her.Map) && x.GetEdifice(her.Map) == null, out var result))
			{
				Building_ShadowDecoy building_ShadowDecoy = (Building_ShadowDecoy)ThingMaker.MakeThing(namedSilentFail);
				building_ShadowDecoy.her = her;
				building_ShadowDecoy.until = Find.TickManager.TicksGame + LinarianZone.Ticks(1250f, g);
				GenSpawn.Spawn(building_ShadowDecoy, result, her.Map, Rot4.South);
				if (her.Faction != null)
				{
					building_ShadowDecoy.SetFaction(her.Faction);
				}
				FleckMaker.ThrowSmoke(building_ShadowDecoy.DrawPos, her.Map, 1f);
			}
		}
	}
}
