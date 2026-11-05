using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.North;

public class BiomeKin : DefModExtension
{
	public BiomeDef kin;

	public float animalFactor = 0.8f;

	public List<string> skipMutators = new List<string>();

	public bool officialAnimals;

	public List<string> skipAnimalPrefixes = new List<string>();

	public bool mutators = true;

	public bool animals = true;

	public bool fish = true;

	public List<string> extraMutators = new List<string>();
}
