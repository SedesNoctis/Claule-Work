using Verse;

namespace Avir
{
    public class Comp_Telekinesis : ThingComp
    {
        public CompProperties_Telekinesis Props => (CompProperties_Telekinesis)props;

        // An Avir traditional amulet grows reach and satchel with its development.
        private Artifactorics.CompAvirAmulet Amulet => parent.TryGetComp<Artifactorics.CompAvirAmulet>();
        public float Radius => Props.radius + Artifactorics.AmuletDevelopment.RadiusBonus(Amulet);
        public float SatchelKg => Props.satchelCapacityKg + Artifactorics.AmuletDevelopment.SatchelBonus(Amulet);

        public override string CompInspectStringExtra()
        {
            string s = "Avir_TelekinesisRadius".Translate(Radius.ToString("0.#"));
            if (SatchelKg > 0f)
                s += "\nSubspace satchel: " + SatchelKg.ToString("0.#") + " kg";
            return s;
        }
    }
}
