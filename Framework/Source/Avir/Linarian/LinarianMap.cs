using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian
{
    /// <summary>
    /// What a Herald does on a map by herself, every second or so: being seen through, devouring those her Call has
    /// brought to her side, the dread and rest around her, the song-hunt, and the Song she learns from it.
    /// </summary>
    public class MapComponent_LinarianSong : MapComponent
    {
        private readonly List<Pawn> heralds = new List<Pawn>();
        private int lastNight = -1;
        private static readonly Dictionary<Map, List<Pawn>> byMap = new Dictionary<Map, List<Pawn>>();

        public MapComponent_LinarianSong(Map map) : base(map) { }

        /// <summary>While a tactic is aimed: how far it reaches round her.</summary>
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            Pawn p = LinarianTactics.aimer;
            if (p != null && !Find.Targeter.IsTargeting) { LinarianTactics.aimer = p = null; }
            if (p != null && p.Map == map)
            {
                DrawZone(p);
                IntVec3 m = Verse.UI.MouseCell();
                if (LinarianTactics.aimRadius > 0f && m.InBounds(map)) GenDraw.DrawRadiusRing(m, LinarianTactics.aimRadius);
                return;
            }
            foreach (Pawn her in heralds)
                if (her.Faction == Faction.OfPlayer && Find.Selector.IsSelected(her)) DrawZone(her);
        }

        /// <summary>The edge of her influence (nothing to draw when it holds the whole map).</summary>
        public static void DrawZone(Pawn her)
        {
            Gene_LinarianSong g = Linarian.Song(her);
            if (g == null || g.ZoneRadius >= float.MaxValue) return;
            GenDraw.DrawCircleOutline(her.DrawPos, g.ZoneRadius, SimpleColor.Cyan);
        }

        /// <summary>The Heralds on a map, as of the last second.</summary>
        public static List<Pawn> HeraldsOn(Map m) => m != null && byMap.TryGetValue(m, out List<Pawn> l) ? l : new List<Pawn>();

        public override void MapRemoved()
        {
            base.MapRemoved();
            byMap.Remove(map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastNight, "lastNight", -1);
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0) return;
            heralds.Clear();
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
                if (all[i].RaceProps.Humanlike && Linarian.Is(all[i])) heralds.Add(all[i]);
            byMap[map] = heralds.ToList();
            foreach (Pawn her in heralds)
            {
                if (her.Dead || !her.Spawned) continue;
                LinarianVeil.CheckReveal(her);
                if (her.Downed) continue;
                Gene_LinarianSong g = Linarian.Song(her);
                if (g == null) continue;
                if (!LinarianPresence.Managed(her)) Devour(her, g);
                Auras(her, g, now);
                LinarianGuise.Tick(her, g, now);
                if (g.Has("T3") && now % 240 == 0)   // the distortion's passive: foes in her zone drift in time
                    foreach (Pawn q in LinarianZone.InHerZone(her, g).Where(q => q != her && !q.Dead && !q.RaceProps.Animal && q.HostileTo(her)).ToList())
                        LinarianHediffs.Add(q, LinarianTime.Drift, 600, 1f, her);
                if ((LinarianVeil.Veiled(her) || LinarianVeil.InForm(her) != null) && now % 2520 == 0) g.Learn(50f);
                if (now % 2520 == 0 && g.voices.Count > 0) { LinarianFeast.Fade(g); LinarianFeast.Merge(g); }
                if (now % 2520 == 0) LinarianPack.DrawBeasts(her, g, now);
                if (now % 2520 == 0 && LinarianPack.HuntActive(her)) LinarianPack.Teach(her, 150f);   // she learns while they hunt
                if (g.Has("P2") && her.Faction == Faction.OfPlayer && now % 2520 == 0) LinarianSongHunt.CallOne(her, g, false);
                if (g.shadow && now % 2520 == 0) Gene_ArcaneHunger.Spend(her, 0.01f);   // the shadow: the hungriest of them
            }
            int day = GenDate.DaysPassed;
            if (lastNight != day && GenLocalDate.HourOfDay(map) == 2)
            {
                lastNight = day;
                LinarianNights.Night(map);
            }
            if (now % 2520 == 0) LinarianNights.Sleep(map, now);   // about hourly, on the component's own beat
            if (now % 2520 == 0) LinarianCult.Hourly(map, now);
            LinarianPresence.Tick(map, now);
            LinarianZone.Tick(map, now);
            LinarianGuise.CheckPack(map);
            LinarianWounds.Tick(map, now);
            LinarianTime.Tick(map, now);
            LinarianCult.Tick(map, now);
            LinarianRivals.Tick(map, now);
        }

        /// <summary>Beside her, one called at a time: she shows herself, devours, and veils again.</summary>
        private void Devour(Pawn her, Gene_LinarianSong g)
        {
            if (g.ReadyIn("devour") > 0) return;
            Pawn q = GenRadial.RadialDistinctThingsAround(her.Position, map, 1.9f, true).OfType<Pawn>()
                .FirstOrDefault(x => !x.Dead && LinarianMental.CalledBy(x) == her);
            if (q == null) return;
            g.Cooldown("devour", 180);
            bool wasVeiled = LinarianVeil.Veiled(her);
            string form = LinarianVeil.InForm(her);
            LinarianVeil.Unveil(her);
            LinarianVeil.ClearForm(her);
            LinarianDevour.Devour(her, q);
            g.Learn(300f);
            if (wasVeiled || form != null) LinarianVeil.Veil(her, GenDate.TicksPerHour / 2);
        }

        private void Auras(Pawn her, Gene_LinarianSong g, int now)
        {
            bool rest = g.Has("L4"), dread = g.Has("S3"), haze = false, beasts = g.Has("P3"), slow = false;
            if (rest || dread || haze || beasts || slow)
                foreach (Pawn q in GenRadial.RadialDistinctThingsAround(her.Position, map, 7.9f, true).OfType<Pawn>())
                {
                    if (q == her || q.Dead) continue;
                    bool foe = q.HostileTo(her);
                    if (foe && LinarianTactics.Takeable(q))
                    {
                        if (dread) LinarianHediffs.Add(q, LinarianHediffs.Avir_SongDread, 300);
                    }
                    if (foe && slow && (q.Position - her.Position).LengthHorizontal <= 5f) LinarianHediffs.Add(q, LinarianHediffs.Avir_TimeSlow, 120, 0.4f);
                    if (rest && !foe && q.Faction == her.Faction && q.RaceProps.Humanlike) LinarianHediffs.Add(q, LinarianHediffs.Avir_SongRest, 300);
                    if (beasts && q.RaceProps.Animal && q.MentalState is MentalState_Song s && s.herald == her) LinarianHediffs.Add(q, LinarianHediffs.Avir_PackBlessing, 300);
                }
            // the quiet aria's gift: her own asleep anywhere in her zone sleep deep - they rest faster and their wounds close
            if (g.Has("L2"))
                foreach (Pawn q in map.mapPawns.SpawnedPawnsInFaction(her.Faction ?? Faction.OfPlayer).Where(q => q != her && q.RaceProps.Humanlike && LinarianTactics.Asleep(q) && LinarianZone.InZone(her, g, q.Position)).ToList())
                {
                    LinarianHediffs.Add(q, LinarianHediffs.Avir_SongRest, 300);
                    LinarianShadow.Heal(q, 0.01f);
                }
        }
    }

    /// <summary>Devouring: the whole of a victim's essence (a Herald's siphon), and death. The feast keeps its voice.</summary>
    public static class LinarianDevour
    {
        public static void Devour(Pawn her, Pawn victim)
        {
            if (victim == null || victim.Dead) return;
            Map map = victim.MapHeld;
            Vector3 at = victim.DrawPos;
            float raw = AvirEssence.VictimValue(victim);
            if (AvirEssence.HasGene(her, AvirEssenceDefOf.Avir_EssenceGlutton)) raw *= 2f;
            AvirEssenceLifetime.Track(her, raw, "siphon");
            AvirEssence.Gain(her, raw);
            Gene_LinarianSong g = Linarian.Song(her);
            // she devours whole: half her hunger at once - and with the sip's stone a quarter of what overflows is stored
            LinarianFeast.FeedOrStore(her, g, 0.5f, g != null && g.Has("F2") ? 0.25f : 0f);
            if (g != null)
            {
                g.TakeVoice(victim);
                if (g.Dark("F")) LinarianShadow.Younger(her, 1f);
            }
            victim.Kill(null, null);
            if (map != null) Avir.Ritualistics.RitualFx.Mote(at, map, 1.8f, new Color(0.85f, 0.75f, 1f));
            if (her.Faction == Faction.OfPlayer || victim.Faction == Faction.OfPlayer)
                Messages.Message((LinarianPresence.Disguised(her) ? "Something" : her.LabelShortCap) + " devours " + victim.LabelShort + ".", new LookTargets(victim.Corpse), MessageTypeDefOf.NegativeEvent, false);
        }
    }

    /// <summary>The song-hunt: an animal marked for hunting comes to her and dies as if slaughtered. The larger the
    /// beast, the stronger her song must be.</summary>
    public static class LinarianSongHunt
    {
        public static float MaxSize(Gene_LinarianSong g) => 0.6f + g.Power * 0.9f;   // no more great beasts for free

        public static void CallOne(Pawn her, Gene_LinarianSong g, bool forced)
        {
            if (her.Map == null || her.Downed) return;
            if (!forced && !Rand.Chance(0.35f)) return;
            // the gift: one beast at a time
            if (!forced && her.Map.mapPawns.AllPawnsSpawned.Any(x => LinarianMental.CalledBy(x) == her || x.MentalState is MentalState_Song s && s.herald == her && x.MentalStateDef == LinarianMental.HuntCalled)) return;
            float max = MaxSize(g);
            Pawn a = her.Map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Hunt)
                .Select(d => d.target.Thing as Pawn)
                .Where(x => x != null && !x.Dead && !x.Downed && x.RaceProps.Animal && x.BodySize <= max && !(x.MentalState is MentalState_Song))
                .OrderBy(x => (x.Position - her.Position).LengthHorizontalSquared).FirstOrDefault();
            if (a == null)
            {
                if (forced && her.Faction == Faction.OfPlayer) Messages.Message("No beast marked for hunting is small enough for her aria (up to body size " + max.ToString("0.0") + ").", her, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (LinarianMental.StartHunt(a, her, GenDate.TicksPerHour * 3) && her.Faction == Faction.OfPlayer)
                Messages.Message(a.LabelShortCap + " hears " + her.LabelShort + "'s aria and comes.", a, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    // ---------------------------------------------------------------- textures

    [StaticConstructorOnStartup]
    public static class LinarianTex
    {
        public static readonly Texture2D Song = ContentFinder<Texture2D>.Get("UI/Avir/Herald/Song", false)
                                                ?? ContentFinder<Texture2D>.Get("Avir/AbilityIcons/Avir_ShroudgatesIcon", false) ?? BaseContent.BadTex;

        public static readonly Texture2D Flow = ContentFinder<Texture2D>.Get("UI/Avir/Herald/Flow", false) ?? Song;
        public static readonly Texture2D OwnForm = ContentFinder<Texture2D>.Get("UI/Avir/Herald/OwnForm", false) ?? Song;

        private static readonly Dictionary<string, Texture2D> tactics = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> paths = new Dictionary<string, Texture2D>();

        /// <summary>A path's emblem, once drawn (UI/Avir/Herald/Path_X); null until then.</summary>
        public static Texture2D Path(string area)
        {
            if (!paths.TryGetValue(area, out Texture2D t)) paths[area] = t = ContentFinder<Texture2D>.Get("UI/Avir/Herald/Path_" + area, false);
            return t;
        }

        /// <summary>A tactic's icon: its own once drawn (UI/Avir/Herald/Tactic_X), else a rune of ritualism.</summary>
        public static Texture2D Tactic(string t)
        {
            if (t == null) return Song;
            if (t == "SL") t = "S1";   // lifting the pain wears the old art of the scars
            if (tactics.TryGetValue(t, out Texture2D tex)) return tex;
            string rune;
            switch (LinarianTactics.IsForm(t) ? "G" : t.Substring(0, 1))
            {
                case "L": rune = "Focus"; break;
                case "D": case "T": rune = "Shroud"; break;
                case "W": rune = "Hold"; break;
                case "F": rune = "Drain"; break;
                case "S": rune = "Release"; break;
                case "G": rune = "Barrier"; break;
                case "P": case "C": rune = "Growth"; break;
                default: rune = "Lift"; break;
            }
            tex = ContentFinder<Texture2D>.Get("UI/Avir/Herald/Tactic_" + t, false)
                  ?? ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Ink/Rune_" + rune, false) ?? Song;
            tactics[t] = tex;
            return tex;
        }
    }

    // ---------------------------------------------------------------- the circle answers her

    /// <summary>The song in the circle: a Herald has two pairs of wings, and the second gives as much as the first;
    /// she steadies the stream. A Herald who leads a ritual in a stormy Flow may wake the shadow in her song.</summary>
    public static class LinarianRitual
    {
        public static float GiveFactor(Pawn src, Pawn performer)
        {
            float f = Linarian.Is(src) ? 2f : 1f;
            if (performer != null && src == performer) Touch(performer);
            return f;
        }

        public static float RiskFactor(Pawn performer) => Linarian.Is(performer) ? 0.8f : 1f;

        public static bool NoBacklash(Pawn performer) => false;

        private static void Touch(Pawn performer)
        {
            Gene_LinarianSong g = Linarian.Song(performer);
            if (g == null || g.shadow || performer.Map == null || !performer.IsHashIntervalTick(600)) return;
            var ext = Avir.Ritualistics.MapComponent_ExternalFlow.Of(performer.Map);
            if (ext != null && ext.Total >= Avir.Ritualistics.MapComponent_ExternalFlow.Restless && Rand.Chance(0.15f))
                g.WakeShadow("led a ritual in a stormy Stream, and the stream answered her.");
        }
    }
}
