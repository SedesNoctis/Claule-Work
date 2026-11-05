using Verse;

namespace Avir.Linarian;

public class Command_SongTactic : Command_Action
{
	public Pawn her;

	public float radius;

	public override void GizmoUpdateOnMouseover()
	{
		base.GizmoUpdateOnMouseover();
		if (her != null && her.Spawned)
		{
			MapComponent_LinarianSong.DrawZone(her);
			if (radius > 0f)
			{
				GenDraw.DrawRadiusRing(her.Position, radius);
			}
		}
	}
}
