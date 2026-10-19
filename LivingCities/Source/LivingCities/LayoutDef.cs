using System.Collections.Generic;
using Verse;

namespace LivingCities
{
    // =====================================================================================
    // A LAYOUT: a piece of a real map, saved whole - so a base someone played for a hundred days can
    // become a nest. The exporter writes it as a ready Def file (Config/../LivingCities/Exports), so it can
    // be dropped into a mod's Defs as it is; the placer stamps it back onto any map.
    //
    // Everything is stored by defName in plain strings and looked up silently when placed, so a layout
    // made with mods still places where some of them are missing (it just goes without those things), and
    // the mods list says what it was made with and which of them it actually uses.
    //
    // Compact formats (one string per entry, fields split by '|'):
    //   terrain rows   - two characters per cell, an index into terrainKeys (base 62), north row first
    //   roof rows      - one character per cell: '.' none, 'c' constructed, 't' thin rock, 'h' thick rock,
    //                    others a 0-based index into roofKeys as a digit
    //   under          - x|z|terrain           (what lies under a floor: bridges, foundations)
    //   colors         - x|z|colorDef          (painted floors)
    //   things         - def|stuff|x|z|rot|quality|style|paint|color(r,g,b,a)|faction
    //   items          - def|stuff|x|z|count|quality
    //   plants         - def|x|z|growth
    //   zones          - kind|label|plant|runs   (runs: x,z,length;... rows of cells)
    //   rooms          - role|cells|x,z|owners
    //   bills          - x|z|giver|recipe;recipe;...
    //   residents      - name|kind|xenotype|gender|age|bed x,z|skills|title
    //   animals        - kind|count
    // =====================================================================================
    public class LayoutMod
    {
        public string packageId;
        public string name;
        public bool used;
    }

    public class LayoutDef : Def
    {
        public string exported;
        public string gameVersion;
        public string colony;
        public string biome;
        public int daysPlayed;
        public IntVec2 size;

        public List<LayoutMod> mods = new List<LayoutMod>();
        public List<string> terrainKeys = new List<string>();
        public List<string> terrain = new List<string>();
        public List<string> roofKeys = new List<string>();
        public List<string> roof = new List<string>();
        public List<string> under = new List<string>();
        public List<string> colors = new List<string>();
        public List<string> things = new List<string>();
        public List<string> items = new List<string>();
        public List<string> plants = new List<string>();
        public List<string> zones = new List<string>();
        public List<string> rooms = new List<string>();
        public List<string> bills = new List<string>();
        public List<string> residents = new List<string>();
        public List<string> animals = new List<string>();
        /// <summary>The colony's ideoligions: "primary|name|culture|memes;...|style:priority;..." - the styles its buildings are made in.</summary>
        public List<string> ideos = new List<string>();

        public const string Digits = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static string Code(int i) => "" + Digits[i / 62 % 62] + Digits[i % 62];
        public static int Decode(string s, int at) => Digits.IndexOf(s[at]) * 62 + Digits.IndexOf(s[at + 1]);
    }
}
