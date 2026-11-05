using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>The player's own rituals: one file per ritual in a folder next to the saves, the way
    /// ideoligions are kept - shared between games, copied by hand between players.</summary>
    public static class RitualFiles
    {
        public const string Extension = ".rit";

        /// <summary>Not the game's save folder (on the system drive): the folder set in the mod settings,
        /// or a folder "AvirRituals" beside the mod folder (in the Mods folder, wherever the mod is installed).</summary>
        public static string Folder
        {
            get
            {
                string custom = AvirMod.Settings?.ritualFolder;
                // beside the mod, not inside it: replacing the mod folder on an update keeps the rituals
                string root = LoadedModManager.GetMod<AvirMod>()?.Content?.RootDir;
                string beside = root != null ? Directory.GetParent(Path.GetFullPath(root).TrimEnd('/', '\\'))?.FullName : null;
                string path = !custom.NullOrEmpty() ? custom : Path.Combine(beside ?? GenFilePaths.SaveDataFolderPath, "AvirRituals");
                try
                {
                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                    MoveOld(path);
                }
                catch (Exception e) { Log.Warning("[Avir] ritual folder " + path + ": " + e.Message); }
                return path;
            }
        }

        private static bool moved;

        /// <summary>Rituals saved by an earlier version next to the game's saves move to the new folder once.</summary>
        private static void MoveOld(string path)
        {
            if (moved) return;
            moved = true;
            string old = Path.Combine(GenFilePaths.SaveDataFolderPath, "AvirRituals");
            if (!Directory.Exists(old) || Path.GetFullPath(old) == Path.GetFullPath(path)) return;
            foreach (string f in Directory.GetFiles(old, "*" + Extension))
            {
                string to = Path.Combine(path, Path.GetFileName(f));
                if (!File.Exists(to)) File.Move(f, to);
            }
            if (Directory.GetFileSystemEntries(old).Length == 0) Directory.Delete(old);
        }

        public static string PathFor(string name) => Path.Combine(Folder, Sanitize(name) + Extension);

        public static bool Exists(string name) => File.Exists(PathFor(name));

        private static string Sanitize(string name)
        {
            string s = (name ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.NullOrEmpty() ? "Untitled ritual" : s;
        }

        public static bool Save(RitualSchema schema)
        {
            if (schema == null) return false;
            RitualSchema s = schema.Copy();
            s.preset = null;
            try
            {
                SafeSaver.Save(PathFor(s.name), "AvirRitual", () => Scribe_Deep.Look(ref s, "schema"));
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[Avir] could not save ritual \"" + schema.name + "\": " + e);
                return false;
            }
        }

        public static RitualSchema Load(string file)
        {
            RitualSchema s = null;
            try
            {
                Scribe.loader.InitLoading(file);
                try
                {
                    Scribe_Deep.Look(ref s, "schema");
                    Scribe.loader.FinalizeLoading();
                }
                catch
                {
                    Scribe.ForceStop();
                    throw;
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] could not read ritual file " + file + ": " + e.Message);
                return null;
            }
            if (s != null && s.name.NullOrEmpty()) s.name = Path.GetFileNameWithoutExtension(file);
            return s;
        }

        public static List<RitualSchema> LoadAll()
        {
            var list = new List<RitualSchema>();
            try
            {
                foreach (string f in Directory.GetFiles(Folder, "*" + Extension).OrderBy(x => x))
                {
                    RitualSchema s = Load(f);
                    if (s != null) list.Add(s);
                }
            }
            catch (Exception e) { Log.Warning("[Avir] could not list ritual files: " + e.Message); }
            return list;
        }

        public static void Delete(string name)
        {
            try { if (Exists(name)) File.Delete(PathFor(name)); }
            catch (Exception e) { Log.Warning("[Avir] could not delete ritual \"" + name + "\": " + e.Message); }
        }
    }
}
