using Verse;

namespace Avir.Artifactorics;

public static class StriderHit
{
	public static bool inner;

	private static DamageDef arrow;

	public static DamageDef Arrow
	{
		get
		{
			object obj = arrow;
			if (obj == null)
			{
				obj = DefDatabase<DamageDef>.GetNamedSilentFail("Avir_LightArrow") ?? DefDatabase<DamageDef>.GetNamed("Arrow");
				arrow = (DamageDef)obj;
			}
			return (DamageDef)obj;
		}
	}

	public static bool IsArrow(DamageInfo d, out Pawn archer, out CompStriderBow bow)
	{
		archer = d.Instigator as Pawn;
		bow = null;
		if (inner || archer == null || d.Def != Arrow)
		{
			return false;
		}
		bow = StriderPaths.Drawn(archer);
		if (bow != null && bow.path != null)
		{
			return d.Weapon == bow.parent.def;
		}
		return false;
	}

	public static void Hit(Pawn target, Pawn archer, float amount, DamageDef def, float ap = 0.3f)
	{
		if (target == null || target.Dead || !target.Spawned)
		{
			return;
		}
		inner = true;
		try
		{
			target.TakeDamage(new DamageInfo(def, amount, ap, -1f, archer));
		}
		finally
		{
			inner = false;
		}
	}
}
