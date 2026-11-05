using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    // =====================================================================================
    // OPTIONAL CONTENT. Both on by default, both named in the Credits window:
    //   - the author's own music, played only in the Avir lands (a song carrying AvirSong);
    //   - tattoos borrowed from other mods (a style item carrying AvirBorrowedArt).
    // =====================================================================================

    /// <summary>On a SongDef: plays only while the Avir music option is on, and only on maps of these biomes
    /// (empty = any of the four Avir lands).</summary>
    public class AvirSong : DefModExtension
    {
        public List<BiomeDef> biomes = new List<BiomeDef>();

        private static readonly string[] AvirLands = { "Avir_TwilightForest", "Avir_BurningForest", "Avir_FrozenWaste", "Avir_FrostOasis" };

        public bool PlaysOn(Map map)
        {
            BiomeDef b = map?.Biome;
            if (b == null) return false;
            if (!biomes.NullOrEmpty()) return biomes.Contains(b);
            foreach (string s in AvirLands) if (b.defName == s) return true;
            return false;
        }
    }

    /// <summary>On a borrowed style item (tattoo): who made it. Hidden unless the borrowed tattoos option is on.</summary>
    public class AvirBorrowedArt : DefModExtension
    {
        public string fromMod;
        public string authors;
    }

    [HarmonyPatch(typeof(MusicManagerPlay), "AppropriateNow")]
    public static class Patch_AvirSongs
    {
        private static readonly HashSet<string> Skipped = new HashSet<string>();

        public static void Postfix(SongDef song, ref bool __result)
        {
            if (!__result || song == null) return;
            // A song with no sound would crash MusicManagerPlay.PlaySong. Ask the content database once more (some
            // loaders fill it late); only a song that still has none is skipped, and named once in the log.
            if (song.clip == null) song.clip = ContentFinder<AudioClip>.Get(song.clipPath, false);
            if (song.clip == null)
            {
                __result = false;
                if (Skipped.Add(song.defName))
                    Log.Warning("[Avir] music: skipped song " + song.defName + " (" + (song.modContentPack?.Name ?? "?")
                        + "): its sound " + song.clipPath + " is not loaded, playing it would crash the game's music player.");
                return;
            }
            AvirSong e = song.GetModExtension<AvirSong>();
            if (e == null) return;
            if (!AvirMod.Settings.avirMusic || !e.PlaysOn(Find.CurrentMap)) __result = false;
        }
    }

    /// <summary>The last guard: a song started directly (a music manager's play button skips AppropriateNow) with
    /// no sound is not started, so the game's music player does not throw every frame.</summary>
    [HarmonyPatch(typeof(MusicManagerPlay), "PlaySong")]
    public static class Patch_AvirSilentSong
    {
        public static bool Prefix(SongDef song)
        {
            if (song == null) return false;
            if (song.clip == null) song.clip = ContentFinder<AudioClip>.Get(song.clipPath, false);
            return song.clip != null;
        }
    }

    /// <summary>Says at startup which songs (any mod's) have no sound loaded, so a silent track is never a mystery.</summary>
    [StaticConstructorOnStartup]
    public static class AvirSongCheck
    {
        static AvirSongCheck()
        {
            int all = 0, silent = 0;
            var names = new List<string>();
            foreach (SongDef s in DefDatabase<SongDef>.AllDefsListForReading)
            {
                all++;
                if (s.clip != null) continue;
                silent++;
                if (names.Count < 12) names.Add(s.defName + " (" + (s.modContentPack?.Name ?? "?") + ")");
            }
            if (silent > 0)
                Log.Warning("[Avir] music: " + silent + " of " + all + " songs have no sound loaded at startup: "
                    + string.Join(", ", names) + (silent > names.Count ? ", ..." : ""));
        }
    }

    /// <summary>Borrowed tattoos switched off: taken out of the game at startup, so no pawn wears or is offered them.</summary>
    [StaticConstructorOnStartup]
    public static class AvirBorrowedTattoos
    {
        static AvirBorrowedTattoos()
        {
            if (AvirMod.Settings.borrowedTattoos) return;
            var remove = new List<TattooDef>();
            foreach (TattooDef d in DefDatabase<TattooDef>.AllDefsListForReading)
                if (d.GetModExtension<AvirBorrowedArt>() != null) remove.Add(d);
            if (remove.Count == 0) return;
            var m = AccessTools.Method(typeof(DefDatabase<TattooDef>), "Remove");
            foreach (TattooDef d in remove) m?.Invoke(null, new object[] { d });
            Log.Message("[Avir] Borrowed tattoos are off in the settings: " + remove.Count + " left out.");
        }
    }

    /// <summary>The Credits window: the mod's Credits.txt, as written.</summary>
    public class Dialog_AvirCredits : Window
    {
        private Vector2 scroll;
        private readonly string text;

        public override Vector2 InitialSize => new Vector2(640f, 620f);

        public Dialog_AvirCredits()
        {
            doCloseButton = true;
            absorbInputAroundWindow = true;
            string path = AvirMod.Pack != null ? Path.Combine(AvirMod.Pack.RootDir, "Credits.txt") : null;
            text = path != null && File.Exists(path) ? File.ReadAllText(path) : "Credits.txt not found.";
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "Credits");
            Text.Font = GameFont.Small;
            Rect outer = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f - CloseButSize.y - 10f);
            float h = Text.CalcHeight(text, outer.width - 20f) + 10f;
            Rect view = new Rect(0f, 0f, outer.width - 20f, h);
            Widgets.BeginScrollView(outer, ref scroll, view);
            Widgets.Label(view, text);
            Widgets.EndScrollView();
        }
    }
}
