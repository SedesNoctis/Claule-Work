using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE APOSTATE DUALISM. An Avir cannot be talked into a faith without the Sisters - but the player's faith can
    /// still reach one: whoever of the player's faith tries to convert an Avir converts it instead into that faith's
    /// apostate copy. The copy keeps the player's memes and precepts, takes the celestial diarchy as its structure,
    /// holds neither Sister and any light, drops the prey (other races are equals if the player's faith is friendly
    /// to them, lesser if not), and is named "Apostate Dualism".
    ///   its culture is always the Avir's own;
    ///   one copy per player faith, refreshed whenever the player's faith has changed since;
    ///   hidden until one of its own stands on a map (vanilla unhides an ideoligion when a follower spawns);
    ///   no faction ever holds it; an apostate warden converts into it as into any faith;
    ///   every follower of the Sisters hates an apostate.
    /// </summary>
    public class GameComponent_AvirApostasy : GameComponent
    {
        public const string Name = "Apostate Dualism";

        /// <summary>Memes that make a faith unfriendly to other races: its apostates hold them lesser.</summary>
        private static readonly HashSet<string> UnfriendlyMemes = new HashSet<string> { "Supremacist", "HAR_Xenophobia" };

        public static GameComponent_AvirApostasy Instance;

        private List<Ideo> sources = new List<Ideo>();
        private List<Ideo> copies = new List<Ideo>();
        private List<string> signatures = new List<string>();

        public GameComponent_AvirApostasy(Game game) { Instance = this; }

        public static bool IsApostate(Ideo ideo) => ideo != null && Instance != null && Instance.copies.Contains(ideo);

        /// <summary>The apostate copy of a player faith, made or refreshed now.</summary>
        public Ideo CopyOf(Ideo source)
        {
            if (source == null || IsApostate(source) || AvirSisters.IsSisters(source)) return null;
            MemeDef structure = DefDatabase<MemeDef>.GetNamedSilentFail(AvirSisters.StructureName);
            if (structure == null) return null;
            string sig = Signature(source);
            int i = sources.IndexOf(source);
            if (i >= 0 && !Find.IdeoManager.IdeosListForReading.Contains(copies[i]))
            {
                sources.RemoveAt(i); copies.RemoveAt(i); signatures.RemoveAt(i);
                i = -1;
            }
            if (i >= 0 && signatures[i] == sig) return copies[i];

            Ideo copy = i >= 0 ? copies[i] : IdeoGenerator.MakeIdeo(source.foundation.def);
            bool hidden = i < 0 || copy.hidden;
            source.CopyTo(copy);
            Shape(copy, source, structure);
            copy.hidden = hidden;
            copy.initialPlayerIdeo = false;
            if (i < 0)
            {
                Find.IdeoManager.Add(copy);
                sources.Add(source); copies.Add(copy); signatures.Add(sig);
            }
            else signatures[i] = sig;
            return copy;
        }

        private static void Shape(Ideo ideo, Ideo source, MemeDef structure)
        {
            CultureDef avian = DefDatabase<CultureDef>.GetNamedSilentFail("Avian");
            if (avian != null) ideo.culture = avian;   // whatever was borrowed, the culture stays Avir
            ideo.memes.RemoveAll(m => m.category == MemeCategory.Structure);
            ideo.memes.Insert(0, structure);
            if (ideo.foundation is IdeoFoundation_Deity deity) deity.GenerateDeities();   // the Sisters, by name (Patch_AvirSistersDeities)

            Set(ideo, "Avir_Faith_None");
            Set(ideo, "Avir_Light_Neutral");
            bool unfriendly = source.memes.Any(m => UnfriendlyMemes.Contains(m.defName));
            Set(ideo, unfriendly ? "Avir_LesserRaces" : "Avir_OtherRaces_Equals");   // replaces the prey: same issue

            // whatever the player's faith held that needs a structure it no longer has, goes
            foreach (Precept p in ideo.PreceptsListForReading.Where(p => !p.def.requiredMemes.NullOrEmpty()
                         && !p.def.requiredMemes.Any(m => ideo.memes.Contains(m))).ToList())
                ideo.RemovePrecept(p);

            ideo.name = Name;
            ideo.adjective = "apostate";
            ideo.memberName = "apostate";
            ideo.description = "Two Sisters, and neither of them kneeled to. What the Avir once knelt to is remembered here as a story, not a law - "
                               + "and what is left of the faith is borrowed from those who took the apostate in.";
            ideo.descriptionLocked = true;
            ideo.RecachePrecepts();
        }

        private static void Set(Ideo ideo, string defName)
        {
            PreceptDef d = DefDatabase<PreceptDef>.GetNamedSilentFail(defName);
            if (d == null || ideo.HasPrecept(d)) return;
            if (d.issue != null)
                foreach (Precept old in ideo.PreceptsListForReading.Where(p => p.def.issue == d.issue).ToList())
                    ideo.RemovePrecept(old);
            ideo.AddPrecept(PreceptMaker.MakePrecept(d), true);
        }

        private static string Signature(Ideo ideo) =>
            string.Join(",", ideo.memes.Select(m => m.defName)) + "|" + string.Join(",", ideo.PreceptsListForReading.Select(p => p.def.defName));

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref sources, "avirApostateSources", LookMode.Reference);
            Scribe_Collections.Look(ref copies, "avirApostateCopies", LookMode.Reference);
            Scribe_Collections.Look(ref signatures, "avirApostateSignatures", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (sources == null) sources = new List<Ideo>();
                if (copies == null) copies = new List<Ideo>();
                if (signatures == null) signatures = new List<string>();
                for (int i = Count - 1; i >= 0; i--)
                    if (i >= sources.Count || i >= copies.Count || i >= signatures.Count || sources[i] == null || copies[i] == null)
                    {
                        if (i < sources.Count) sources.RemoveAt(i);
                        if (i < copies.Count) copies.RemoveAt(i);
                        if (i < signatures.Count) signatures.RemoveAt(i);
                    }
            }
            Instance = this;
        }

        private int Count => System.Math.Max(sources.Count, System.Math.Max(copies.Count, signatures.Count));
    }
}
