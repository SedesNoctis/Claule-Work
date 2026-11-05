using Verse;

namespace Avir.Artifactorics;

public class Verb_StriderBow : Verb_Shoot
{
	public override ThingDef Projectile
	{
		get
		{
			string text = CompStriderBow.Of(base.EquipmentSource)?.Props.dynasty;
			return ((text == null) ? null : DefDatabase<ThingDef>.GetNamedSilentFail("Avir_StriderArrow_" + text)) ?? base.Projectile;
		}
	}

	public override bool Available()
	{
		if (base.Available())
		{
			return StriderBow.CanFire(base.EquipmentSource);
		}
		return false;
	}

	protected override bool TryCastShot()
	{
		bool num = base.TryCastShot();
		if (num)
		{
			StriderBow.Spend(base.EquipmentSource, CasterPawn);
		}
		return num;
	}
}
