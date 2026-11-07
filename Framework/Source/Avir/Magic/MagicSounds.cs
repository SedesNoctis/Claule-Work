using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Magic
{
    /// <summary>The sounds of the magic (Fix225, the calibration). We make no sounds of our own: every one is a sound the
    /// game already has (core first, then Royalty / Anomaly / Odyssey if they are there - each line lists them in that
    /// order and the first one found plays). One table, so the author can hear it through and have any line changed.
    /// What is not in the game is skipped in silence; the dev action "Sound table: check" lists what plays and what is missing.</summary>
    public static class MagicSounds
    {
        public class Line { public string[] names; public float volume = 1f; public Line(float v, params string[] n) { volume = v; names = n; } }

        /// <summary>What a spell sounds like where it lands: El:* - a plain element, Combo:* - a meeting of two,
        /// Second:* - a meeting of three, and the separate moments (Thunder, Bolt, Arc, Discharge...).</summary>
        public static readonly Dictionary<string, Line> Table = new Dictionary<string, Line>
        {
            // the elements
            ["El:Fire"] = new Line(0.8f, "Explosion_Flame", "Interact_Ignite"),
            ["El:Water"] = new Line(0.9f, "EmergeFromWater", "Interact_CleanFilth_Fluid"),
            ["El:Air"] = new Line(0.7f, "HissJet", "HissSmall"),
            ["El:Earth"] = new Line(0.6f, "Roof_Collapse", "ChunkRock_Drop"),
            ["El:Light"] = new Line(0.7f, "Psycast_Skip_Exit", "Psycast_Skip_Entry", "EnergyShield_Reset"),
            ["El:Life"] = new Line(0.8f, "Interact_Sow", "Psycast_Skip_Entry"),
            // the meetings of two
            ["Combo:Steam"] = new Line(0.9f, "HissJet", "GeyserSpray"),
            ["Combo:Storm"] = new Line(1f, "Thunder_OnMap"),
            ["Combo:Magma"] = new Line(0.9f, "Explosion_Flame", "Roof_Collapse"),
            ["Combo:Haze"] = new Line(0.6f, "HissSmall"),
            ["Combo:Bog"] = new Line(0.9f, "EmergeFromWater", "Interact_CleanFilth_Fluid"),
            ["Combo:Dust"] = new Line(0.6f, "Roof_Collapse", "ChunkRock_Drop"),
            // the meetings of three
            ["Second:Inferno"] = new Line(1f, "Explosion_Flame"),
            ["Second:Boil"] = new Line(0.8f, "HissSmall"),
            ["Second:Geyser"] = new Line(1f, "GeyserSpray", "HissJet"),
            ["Second:Typhoon"] = new Line(1f, "Thunder_OffMap", "HissJet"),
            ["Second:Lava"] = new Line(1f, "Explosion_Flame"),
            ["Second:Volcano"] = new Line(1f, "Roof_Collapse", "Explosion_Flame"),
            ["Second:Obsidian"] = new Line(0.9f, "HissJet"),
            ["Second:Ash"] = new Line(0.7f, "HissSmall"),
            ["Second:Deluge"] = new Line(0.9f, "Thunder_OffMap"),
            ["Second:Hurricane"] = new Line(1f, "HissJet"),
            ["Second:BallLightning"] = new Line(0.9f, "EnergyShield_Reset"),
            ["Second:Mudflow"] = new Line(1f, "EmergeFromWater", "Roof_Collapse"),
            ["Second:Swelter"] = new Line(0.6f, "Interact_Ignite"),
            ["Second:Thermal"] = new Line(0.6f, "HissSmall"),
            ["Second:Rainbow"] = new Line(0.7f, "PsycastPsychicPulse", "Psycast_Skip_Exit", "Psycast_Skip_Entry"),
            ["Second:DryWind"] = new Line(0.7f, "HissJet"),
            ["Second:Quag"] = new Line(0.9f, "EmergeFromWater"),
            ["Second:Silt"] = new Line(0.8f, "EmergeFromWater"),
            ["Second:MarshGas"] = new Line(0.7f, "HissSmall"),
            ["Second:MarshHaze"] = new Line(0.6f, "HissSmall"),
            ["Second:Rockfall"] = new Line(1f, "Roof_Collapse"),
            ["Second:Emery"] = new Line(0.7f, "HissJet"),
            ["Second:BurningSand"] = new Line(0.8f, "Interact_Ignite", "Explosion_Flame"),
            ["Second:Clay"] = new Line(0.7f, "ChunkRock_Drop"),
            // the moments
            ["Thunder"] = new Line(1f, "Thunder_OnMap"),
            ["Bolt"] = new Line(0.9f, "Thunder_OnMap"),
            ["Arc"] = new Line(0.8f, "Pawn_Nociosphere_Arc", "EnergyShield_AbsorbDamage"),
            ["Discharge"] = new Line(0.9f, "Explosion_EMP"),
            ["Steam"] = new Line(0.8f, "HissJet"),
            ["Rockfall:Crash"] = new Line(1f, "DropPod_Impact", "Roof_Collapse"),
            // the long sounds of fields that stay (sustained while the field is in view)
            ["Hold:Deluge"] = new Line(0.8f, "Ambient_Rain"),
            ["Hold:Hurricane"] = new Line(0.5f, "Tornado"),
            ["Hold:Typhoon"] = new Line(0.5f, "Tornado"),
            ["Hold:Whirl"] = new Line(0.22f, "Tornado"),
            ["Hold:Inferno"] = new Line(0.7f, "FireBurning"),
        };

        private static readonly Dictionary<string, SoundDef> found = new Dictionary<string, SoundDef>();
        public static SoundDef Def(string key)
        {
            if (found.TryGetValue(key, out SoundDef d)) return d;
            d = null;
            if (Table.TryGetValue(key, out Line l)) foreach (string n in l.names) { d = DefDatabase<SoundDef>.GetNamedSilentFail(n); if (d != null) break; }
            found[key] = d;
            return d;
        }

        // the same sound twice in one moment at one place is one sound (a storm striking five at once, an echo of a reflector)
        private static readonly Dictionary<string, int> last = new Dictionary<string, int>();

        /// <summary>A sound once, at a place on the map.</summary>
        public static void Play(string key, IntVec3 at, Map map, float volume = 1f, int gap = 8)
        {
            if (map == null || !at.InBounds(map)) return;
            SoundDef d = Def(key);
            if (d == null || d.sustain) return;
            int now = Find.TickManager.TicksGame;
            string k = key + "@" + map.uniqueID;
            if (last.TryGetValue(k, out int t) && now - t < gap) return;
            last[k] = now;
            SoundInfo info = SoundInfo.InMap(new TargetInfo(at, map));
            info.volumeFactor = Table[key].volume * volume;
            try { d.PlayOneShot(info); } catch (System.Exception e) { Log.WarningOnce("[Avir] sound " + key + ": " + e.Message, key.GetHashCode()); }
        }
        public static void Play(string key, Vector3 at, Map map, float volume = 1f, int gap = 8) => Play(key, at.ToIntVec3(), map, volume, gap);

        // the long sounds: one for each field, kept up tick by tick while it lasts and is in view
        private static readonly Dictionary<object, Sustainer> held = new Dictionary<object, Sustainer>();

        /// <summary>Keep the long sound of `owner` (a field) going this tick. Call it every tick while it should sound.</summary>
        public static void Keep(object owner, string key, IntVec3 at, Map map)
        {
            if (owner == null || map == null || !at.InBounds(map)) return;
            // only what is on the screen is heard (some of the game's weather sounds play on the camera, not at a place)
            if (Find.CurrentMap != map || !Find.CameraDriver.CurrentViewRect.ExpandedBy(8).Contains(at)) { Stop(owner); return; }
            if (!held.TryGetValue(owner, out Sustainer s) || s == null || s.Ended)
            {
                SoundDef d = Def(key);
                if (d == null || !d.sustain) return;
                SoundInfo info = SoundInfo.InMap(new TargetInfo(at, map), MaintenanceType.PerTick);
                info.volumeFactor = Table[key].volume;
                try { s = d.TrySpawnSustainer(info); } catch (System.Exception e) { Log.WarningOnce("[Avir] sound " + key + ": " + e.Message, key.GetHashCode()); s = null; }
                if (s == null) { Log.WarningOnce("[Avir] long sound " + key + " (" + d.defName + ") did not start", key.GetHashCode() ^ 0x51); return; }
                // the game's weather sounds are silent until the weather turns them up (its "LerpFactor" - how far the
                // weather has come in): ours is fully in at once (Fix227 - the deluge's rain was silent because of it)
                try { s.externalParams["LerpFactor"] = 1f; } catch { }
                held[owner] = s;
            }
            s.Maintain();
        }

        public static void Stop(object owner)
        {
            if (owner != null && held.TryGetValue(owner, out Sustainer s)) { if (s != null && !s.Ended) s.End(); held.Remove(owner); }
        }

        /// <summary>Ends the long sounds whose owners are gone.</summary>
        public static void Sweep(System.Func<object, bool> alive)
        {
            if (held.Count == 0) return;
            foreach (object o in held.Keys.ToList()) if (!alive(o)) Stop(o);
        }

        [LudeonTK.DebugAction("Magic", "Sound table: check", allowedGameStates = LudeonTK.AllowedGameStates.Playing)]
        private static void Check()
        {
            var sb = new StringBuilder("[Avir] magic sounds (key -> what plays):\n");
            int miss = 0;
            foreach (var kv in Table)
            {
                found.Remove(kv.Key);
                SoundDef d = Def(kv.Key);
                if (d == null) miss++;
                sb.AppendLine("  " + kv.Key + " -> " + (d == null ? "MISSING (" + string.Join(", ", kv.Value.names) + ")" : d.defName + (d.sustain ? " (long)" : "")));
            }
            sb.AppendLine(miss == 0 ? "all present" : miss + " missing - silent");
            Log.Message(sb.ToString());
        }

        [LudeonTK.DebugAction("Magic", "Ball lightning: loose globes here", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void GlobesHere() { IntVec3 c = Verse.UI.MouseCell(); var f = Find.CurrentMap.GetComponent<SecondFields>(); for (int i = 0; i < 2; i++) f?.AddGlobe(c.ToVector3Shifted(), null, 1f, 1200, null); }

        [LudeonTK.DebugAction("Magic", "Ball lightning: a field of globes here", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void GlobeFieldHere()
        {
            IntVec3 c = Verse.UI.MouseCell(); Map map = Find.CurrentMap; var f = map.GetComponent<SecondFields>();
            var cells = GenRadial.RadialCellsAround(c, 4.5f, true).Where(x => x.InBounds(map)).ToList();
            f?.Add(SecondKind.BallLightning, cells, 2500, null, 1f);
            var zone = new HashSet<IntVec3>(cells);
            for (int i = 0; i < 4; i++) f?.AddGlobe(zone.RandomElement().ToVector3Shifted(), null, 1f, 2500, zone);
        }

        [LudeonTK.DebugAction("Magic", "Ball lightning: a wild one from a strike here", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void WildHere()
        {
            IntVec3 c = Verse.UI.MouseCell(); Map map = Find.CurrentMap;
            map.weatherManager.eventHandler.AddEvent(new WeatherEvent_MagicBolt(map, c));
            Play("Bolt", c, map, 1f, 0);
            map.GetComponent<SecondFields>()?.AddGlobe(c.ToVector3Shifted(), null, 1f, 2500, null);
        }

        [LudeonTK.DebugAction("Magic", "Ball lightning: a kept one here (lamp, charges batteries)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void KeptHere() { IntVec3 c = Verse.UI.MouseCell(); Find.CurrentMap.GetComponent<SecondFields>()?.AddGlobe(c.ToVector3Shifted(), null, 1f, 15000, null, null, false, true); }

        [LudeonTK.DebugAction("Magic", "Deluge here (an hour)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DelugeHere() { IntVec3 c = Verse.UI.MouseCell(); Map map = Find.CurrentMap; map.GetComponent<SecondFields>()?.Add(SecondKind.Deluge, GenRadial.RadialCellsAround(c, 5f, true).Where(x => x.InBounds(map)).ToList(), 2500, null, 1f); }

        [LudeonTK.DebugAction("Magic", "Hurricane field here (20 s)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void HurricaneHere() { IntVec3 c = Verse.UI.MouseCell(); Map map = Find.CurrentMap; map.GetComponent<SecondFields>()?.Add(SecondKind.Hurricane, GenRadial.RadialCellsAround(c, 5f, true).Where(x => x.InBounds(map)).ToList(), 1200, null, 1f); }

        [LudeonTK.DebugAction("Magic", "Sound table: play each here", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void PlayAll()
        {
            IntVec3 c = Verse.UI.MouseCell(); Map map = Find.CurrentMap;
            var f = map.GetComponent<SecondFields>();
            int i = 0;
            foreach (string key in Table.Keys.Where(k => !k.StartsWith("Hold:")).ToList())
            {
                string k = key;
                f?.Later(1 + 90 * i++, () => { MoteMaker.ThrowText(c.ToVector3Shifted(), map, k, 2f); Play(k, c, map, 1f, 0); });
            }
        }
    }
}
