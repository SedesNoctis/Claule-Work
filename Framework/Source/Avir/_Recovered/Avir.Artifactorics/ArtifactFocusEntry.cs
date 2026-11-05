using System.Xml;
using Verse;

namespace Avir.Artifactorics;

public class ArtifactFocusEntry
{
	public ArtifactArmDef arm;

	public float factor;

	public void LoadDataFromXmlCustom(XmlNode xmlRoot)
	{
		DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, "arm", xmlRoot.Name);
		factor = ParseHelper.FromString<float>(xmlRoot.FirstChild.Value);
	}
}
