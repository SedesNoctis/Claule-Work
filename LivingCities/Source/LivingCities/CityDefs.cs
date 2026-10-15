using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LivingCities
{
    // =====================================================================================
    // THE DATA. A city is described, never drawn: a CityDef says which faction lives there, which
    // figures its nests may take, which rooms it needs and who lives in them; a CityStyleDef says what
    // it is built of; a ZoneDef says what one kind of room holds. Every thing and terrain is named by
    // a string and looked up silently, so a city may list Medieval Overhaul or DLC furniture and simply
    // go without it where that content is absent - no load errors, no MayRequire on every line.
    // =====================================================================================

    /// <summary>Lookups that never complain: the first name of a list that exists, or null.</summary>
    public static class Named
    {
        public static ThingDef Thing(string name) => name.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(name);
        public static TerrainDef Terrain(string name) => name.NullOrEmpty() ? null : DefDatabase<TerrainDef>.GetNamedSilentFail(name);
        public static PawnKindDef Kind(string name) => name.NullOrEmpty() ? null : DefDatabase<PawnKindDef>.GetNamedSilentFail(name);
        public static ThingDef FirstThing(List<string> names) => names?.Select(Thing).FirstOrDefault(d => d != null);
        public static TerrainDef FirstTerrain(List<string> names) => names?.Select(Terrain).FirstOrDefault(d => d != null);
    }

    /// <summary>The three sizes a city comes in.</summary>
    public enum CitySize { Outpost, Nest, City }

    /// <summary>The figures a nest complex can take (see CityFigures).</summary>
    public enum FigureKind { SmallRing, RingInRing, NestChain, Spire }

    /// <summary>Where a room lies in its figure, so the zones can ask for the right kind of place.</summary>
    public enum RoomPlace { Hub, Band, Nest, Center, Yard }

    public class CityStyleDef : Def
    {
        public List<string> wallStuff = new List<string>();
        public string wall = "Wall";
        public string heavyDoor = "LC_HeavyDoor";
        public List<string> heavyDoorStuff = new List<string>();
        public string innerDoor = "Door";
        public List<string> innerDoorStuff = new List<string>();
        public List<string> furnitureStuff = new List<string>();
        /// <summary>Floor of the galleries and of any room whose zone names none.</summary>
        public List<string> floor = new List<string>();
        /// <summary>Floor of the "fine" rooms (zone fineFloor) - one is picked per city, so two cities of one
        /// style differ by their carpets.</summary>
        public List<string> fineFloors = new List<string>();
        /// <summary>The ground cleared around the walls (and laid over water there).</summary>
        public List<string> ground = new List<string>();
        public List<string> lights = new List<string>();
        /// <summary>One light every this many floor cells (rooms) and along the galleries.</summary>
        public int lightSpacing = 40;

        public ThingDef Stuff(List<string> names) => Named.FirstThing(names);
    }

    public class ZoneThing
    {
        public string thing;
        /// <summary>Alternatives tried in order when <see cref="thing"/> is absent (a DLC or MO bench).</summary>
        public List<string> orElse;
        public IntRange count = new IntRange(1, 1);
        /// <summary>Extra copies per floor cell of the room (0.05 = one more per twenty cells).</summary>
        public float perArea;
        public bool nearWall;
        /// <summary>Beds: for prisoners.</summary>
        public bool forPrisoners;

        public ThingDef Def()
        {
            ThingDef d = Named.Thing(thing);
            if (d == null && orElse != null) d = Named.FirstThing(orElse);
            return d;
        }
    }

    public class ZoneResident
    {
        public string role;
        public IntRange count = new IntRange(1, 1);
    }

    public class ZoneDef : Def
    {
        public List<RoomPlace> places = new List<RoomPlace>();
        public int minArea = 9;
        public int maxArea = 99999;
        /// <summary>Laid with the city's fine floor (carpet) instead of the plain one.</summary>
        public bool fineFloor;
        public List<string> floor;
        public List<ZoneThing> things = new List<ZoneThing>();
        public List<ZoneResident> residents = new List<ZoneResident>();
        /// <summary>Stockpile value laid in the room: base plus per floor cell.</summary>
        public float lootBase;
        public float lootPerCell;
        /// <summary>No sky well even in a large room (cells, storage).</summary>
        public bool noWell;

        public bool Fits(RoomPlace place, int area) => places.Contains(place) && area >= minArea && area <= maxArea;
    }

    public class CityZoneEntry
    {
        public ZoneDef zone;
        public int min;
        public int max = 99;
        public float weight = 1f;
    }

    public class CityRole
    {
        public string role;
        public List<string> kinds = new List<string>();
        public bool child;

        public PawnKindDef RandomKind() => kinds.Select(Named.Kind).Where(k => k != null).RandomElementWithFallback();
    }

    public class CityDef : Def
    {
        /// <summary>FactionDef defNames that build this city.</summary>
        public List<string> factions = new List<string>();
        public List<CityStyleDef> styles = new List<CityStyleDef>();
        public float outpostWeight = 3f, nestWeight = 5f, cityWeight = 2f;
        public List<FigureKind> figures = new List<FigureKind> { FigureKind.RingInRing, FigureKind.NestChain, FigureKind.Spire };
        public List<CityZoneEntry> zones = new List<CityZoneEntry>();
        /// <summary>The gallery between the outer wall and the rooms.</summary>
        public ZoneDef hubZone;
        public List<CityRole> roles = new List<CityRole>();
        /// <summary>The garrison on top of the room residents (the faction's settlement group), by size.</summary>
        public FloatRange garrisonOutpost = new FloatRange(150, 250), garrisonNest = new FloatRange(300, 500), garrisonCity = new FloatRange(500, 900);

        public CityRole Role(string name) => roles.FirstOrDefault(r => r.role == name);

        public CitySize RandomSize()
        {
            float total = outpostWeight + nestWeight + cityWeight, r = Rand.Value * total;
            if (r < outpostWeight) return CitySize.Outpost;
            return r < outpostWeight + nestWeight ? CitySize.Nest : CitySize.City;
        }

        public FloatRange Garrison(CitySize s) => s == CitySize.Outpost ? garrisonOutpost : s == CitySize.Nest ? garrisonNest : garrisonCity;

        public static CityDef For(FactionDef f) =>
            f == null ? null : DefDatabase<CityDef>.AllDefsListForReading.FirstOrDefault(c => c.factions.Contains(f.defName));

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (styles.NullOrEmpty()) yield return "no styles";
            if (figures.NullOrEmpty()) yield return "no figures";
        }
    }
}
