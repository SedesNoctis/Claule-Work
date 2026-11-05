using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>The Strider bows' light: white textures (Things/Mote/AvirStrider), tinted here by path.</summary>
    public static class StriderBowFx
    {
        public static Color ColorOf(string path)
        {
            switch (path)
            {
                case StriderPaths.Hunt: return new Color(1f, 0.35f, 0.3f);
                case StriderPaths.Storm: return new Color(0.55f, 0.8f, 1f);
                case StriderPaths.Flame: return new Color(1f, 0.6f, 0.25f);
                case StriderPaths.Moon: return new Color(0.85f, 0.85f, 1f);
                case StriderPaths.Gravity: return new Color(0.7f, 0.5f, 1f);
            }
            return Color.white;
        }

        public static readonly Color Judgement = new Color(1f, 0.88f, 0.6f);

        public static void Spawn(string fleck, Vector3 at, Map map, Color color, float scale = 1f, float rotation = -1f)
        {
            FleckDef def = DefDatabase<FleckDef>.GetNamedSilentFail("Avir_StriderFx_" + fleck);
            if (def == null || map == null || !at.ShouldSpawnMotesAt(map)) return;
            FleckCreationData d = FleckMaker.GetDataStatic(at, map, def, scale);
            d.instanceColor = color;
            d.rotation = rotation >= 0f ? rotation : Rand.Range(0f, 360f);
            map.flecks.CreateFleck(d);
        }

        /// <summary>The impact of a path's arrow on the one it struck.</summary>
        public static void Impact(string path, Vector3 at, Map map, float scale = 1f)
        {
            if (path == null) return;
            Spawn(path, at, map, ColorOf(path), scale, path == StriderPaths.Hunt || path == StriderPaths.Moon ? Rand.Range(-25f, 25f) : -1f);
        }

        /// <summary>A beam of light from one point to another (the Moon Arrow, Judgement, the leaping strike).</summary>
        public static void Beam(Vector3 from, Vector3 to, Map map, Color color, float width = 0.5f)
        {
            FleckDef def = DefDatabase<FleckDef>.GetNamedSilentFail("Avir_StriderFx_Beam");
            if (def == null || map == null) return;
            Vector3 mid = (from + to) / 2f;
            if (!mid.ShouldSpawnMotesAt(map)) return;
            float len = (to - from).MagnitudeHorizontal();
            FleckCreationData d = FleckMaker.GetDataStatic(mid, map, def, 1f);
            d.exactScale = new Vector3(len, 1f, width * 2f);
            d.rotation = (to - from).AngleFlat() - 90f;
            d.instanceColor = color;
            map.flecks.CreateFleck(d);
        }
    }
}
