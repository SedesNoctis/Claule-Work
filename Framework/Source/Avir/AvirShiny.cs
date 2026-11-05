using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// SHINY! An Avir eye picks out the faintest accents of colour, and there is little it loves
    /// more than something that shines. A carrier of Apex Pride who wakes in a room holding silver
    /// or gold gets "Shiny!" (+3); tisserium or essentium, "Shiny!" (+5) - the colours of those seem
    /// to settle themselves exactly where an Avir eye likes them.
    ///
    /// Checked once per sleep, at waking (Toils_LayDown.ApplyBedThoughts - where vanilla hands out
    /// its own bedroom thoughts), on everything in the room and its walls: an item made OF the
    /// material (a silver statue, a gold-framed bed, a tisserium wall) counts the same as a pile
    /// of it. Outdoors there is no room, and no thought.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirShiny
    {
        // Stage 0 - silver, gold. Stage 1 - tisserium, essentium.
        private static readonly string[] Precious = { "Silver", "Gold" };
        private static readonly string[] Avirian = { "BloodstoneTisserium", "Essentium" };

        private static ThoughtDef thought;
        private static GeneDef gene;
        private static HashSet<ThingDef> preciousDefs;
        private static HashSet<ThingDef> avirianDefs;

        static AvirShiny()
        {
            try
            {
                new Harmony("Tenebris.Avir.Shiny").Patch(
                    AccessTools.Method(typeof(Toils_LayDown), "ApplyBedThoughts"),
                    postfix: new HarmonyMethod(typeof(AvirShiny), nameof(Postfix)));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Shiny! thought could not be applied: " + e);
            }
        }

        private static bool Resolve()
        {
            if (thought == null)
            {
                thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_Shiny");
                gene = DefDatabase<GeneDef>.GetNamedSilentFail("Avir_Prideful");
                preciousDefs = Defs(Precious);
                avirianDefs = Defs(Avirian);
            }
            return thought != null && gene != null;
        }

        private static HashSet<ThingDef> Defs(string[] names)
        {
            HashSet<ThingDef> set = new HashSet<ThingDef>();
            foreach (string name in names)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def != null)
                {
                    set.Add(def);
                }
            }
            return set;
        }

        public static void Postfix(Pawn actor)
        {
            try
            {
                if (actor?.needs?.mood == null || actor.genes == null || !actor.Spawned || !Resolve())
                {
                    return;
                }
                if (!actor.genes.HasActiveGene(gene))
                {
                    return;
                }

                Room room = actor.GetRoom();
                if (room == null || room.PsychologicallyOutdoors)
                {
                    return;
                }

                int stage = -1;
                List<Thing> things = room.ContainedAndAdjacentThings;
                for (int i = 0; i < things.Count && stage < 1; i++)
                {
                    Thing t = things[i];
                    if (Is(t, avirianDefs))
                    {
                        stage = 1;
                    }
                    else if (stage < 0 && Is(t, preciousDefs))
                    {
                        stage = 0;
                    }
                }

                MemoryThoughtHandler memories = actor.needs.mood.thoughts.memories;
                memories.RemoveMemoriesOfDef(thought);
                if (stage >= 0)
                {
                    memories.TryGainMemory(ThoughtMaker.MakeThought(thought, stage));
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Shiny! check failed: " + e, 0x51A1F);
            }
        }

        private static bool Is(Thing t, HashSet<ThingDef> set)
        {
            if (t == null || set.Count == 0)
            {
                return false;
            }
            return set.Contains(t.def) || (t.Stuff != null && set.Contains(t.Stuff));
        }
    }
}
