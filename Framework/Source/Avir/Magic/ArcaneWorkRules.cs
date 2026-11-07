using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The arcane works as the author laid them out (Арканные-работы-черновик.md, agreed after Fix151):
    ///  - who works: one's own element from Novice, another from Apprentice; meetings (pairs, the second order) from
    ///    Novice, at a risk; and never below the stage the player set for that school (the Path page);
    ///  - how well: quicker by the stage (x0.7 ... x1.6);
    ///  - at a price: a work may slip - a light price for a plain element, a heavy one for magma and lava;
    ///  - by what: the caster puts the work's charm together by himself the first time (a real spell in the book, to be
    ///    bettered by the player), and a higher caster betters it himself;
    ///  - in what order: the arcane works sit in groups ("Arcane works" sub-tab of Work, one column a group), and an
    ///    ordinary work can be set to "M" - done by magic only, never by hand; works barred by greatness are "M" by
    ///    themselves where magic can do them.</summary>
    public static class ArcaneWork
    {
        public class Group
        {
            public string key, parent, label, desc;
            public string[] givers;
            public string[] vanilla;   // the ordinary work types it can stand for ("M") - vanilla's and Complex Jobs'
        }

        private static Group G(string key, string parent, string label, string desc, string[] givers, params string[] vanilla) =>
            new Group { key = key, parent = parent, label = label, desc = desc, givers = givers, vanilla = vanilla };

        /// <summary>The arcane works in the order of the Work tab (vanilla), each as finely as Complex Jobs splits the work;
        /// the works of our own after them. With the detail off they merge into their parents (the vanilla work types).</summary>
        private static readonly List<Group> detailed = new List<Group>
        {
            G("Fire", "Fire", "firefight", "Putting out fires by water (or smothering them by the wind), from a few cells off.", new[] { "Avir_ArcaneFirefight" }, "Firefighter"),
            G("Tend", "Tend", "tend", "Tending the wounded by water: no medicine needed.", new[] { "Avir_MagicTend" }, "Doctor", "FSFNurse"),
            G("Heal", "Tend", "heal", "Healing by Life out of its store, each kind of healing with its own after-gift; a healer of Pestilence takes the sicknesses onto himself.", new[] { "Avir_LifeHeal", "Avir_LifeCure" }, "Doctor", "FSFNurse"),
            G("Soothe", "Soothe", "soothe", "Easing the others by Life: a quiet touch for the tired and the low (only magic does this).", new[] { "Avir_LifeSoothe", "Avir_LifeCare", "Avir_LifeBathSteam" }),
            G("Animals", "Animals", "animals", "Gathering from the animals at a touch of Life - milk, wool and the like, whatever the hand.", new[] { "Avir_LifeGather" }, "Handling"),
            G("Cook", "Stores", "cook", "Purifying food.", new[] { "Avir_2Purify" }, "Cooking"),
            G("Hunt", "Hunt", "hunt", "Hunting the marked animals with the battle charm - no gun needed.", new[] { "Avir_ArcaneHunt" }, "Hunting"),
            G("Fish", "Fish", "fish", "Drawing fish out of a fishing zone from the shore: water closes a bubble round a few and lifts it out, the wind yanks one out from farther off. No line, no waiting. Odyssey's fishing.", new[] { "Avir_ArcaneFish" }, "Fishing"),
            G("Build", "Build", "build", "Raising rock where the arcane orders say, drawing basalt.", new[] { "Avir_ArcaneRaiseRock", "Avir_2Basalt" }, "Construction"),
            G("Smooth", "Build", "smooth", "Smoothing floors and walls by earth.", new[] { "Avir_MagicSmooth" }, "Construction"),
            G("Grow", "Grow", "grow", "Quickening growth, enriching soil by bog, watering the fields, ashing the ground.", new[] { "Avir_ArcaneGrowth", "Avir_ArcaneEnrich", "Avir_2Water", "Avir_2Ash", "Avir_LifeCrops", "Avir_LifeEmerald", "Avir_LifeAge" }, "Growing", "FSFHarvesting"),
            G("Clear", "Grow", "clear land", "Clearing land of plants.", new[] { "Avir_2Clear" }, "PlantCutting"),
            G("Mine", "Mine", "mine", "Drawing ore up, quarrying rock, drawing clay.", new[] { "Avir_ArcaneDrill", "Avir_2Quarry", "Avir_2DigClay" }, "Mining", "FSFDrilling"),
            G("StoneCut", "Mine", "stone cut", "Cutting stone into blocks by dust.", new[] { "Avir_ArcaneCut" }, "FSFStoneCut", "Crafting"),
            G("Smelt", "Smith", "smelt", "Smelting slag by magma, smelting things by lava.", new[] { "Avir_ArcaneSmelt", "Avir_2SmeltItems" }, "FSFSmelt", "Smithing"),
            G("Smith", "Smith", "craft", "Making obsidian, firing bricks, honing blades.", new[] { "Avir_2Obsidian", "Avir_2Bricks", "Avir_2Hone" }, "Smithing", "Crafting"),
            G("Haul", "Haul", "haul", "Carrying by the wind - a heap at once.", new[] { "Avir_MagicHaul" }, "Hauling", "FSFHauling", "FSFTransport"),
            G("Mortuary", "Haul", "mortuary", "Cremating the dead.", new[] { "Avir_2Cremate" }, "FSFCremating"),
            G("Clean", "Clean", "clean", "Washing the floor by water (a patch), a whole room by steam, a wide area by a rinse; melting snow.", new[] { "Avir_MagicClean", "Avir_ArcaneSteam", "Avir_2Rinse", "Avir_ArcaneMelt" }, "Cleaning"),
            G("Warm", "Warm", "warmth", "Warming rooms by haze, tying heat knots, airing the rooms (only magic does this).", new[] { "Avir_ArcaneWarm", "Avir_2HeatRoom", "Avir_2Vent" }),
            G("Power", "Power", "power", "Charging batteries, driving the wind turbines (only magic does this).", new[] { "Avir_ArcaneCharge", "Avir_2Charge", "Avir_2Wind" }),
            G("Stores", "Stores", "stores", "Drying and preserving the stores (only magic does this).", new[] { "Avir_2Dry", "Avir_2Preserve" }),
            G("Land", "Land", "land", "The arcane orders on land and water: stone and water, moats, silt, geysers, lava cores, marsh gas, rainbows (only magic does this).", new[] { "Avir_ArcaneWater", "Avir_ArcaneHotSpring", "Avir_ArcaneStone", "Avir_2Moat", "Avir_2SiltWater", "Avir_2SiltLand", "Avir_2SiltDump", "Avir_2RaiseGeyser", "Avir_2SealGeyser", "Avir_2LavaCore", "Avir_2LavaPool", "Avir_2LavaSet", "Avir_2MarshGas", "Avir_2Rainbow" }),
        };
        private static List<Group> merged;
        private static List<Group> Merged => merged ?? (merged = detailed.GroupBy(g => g.parent).Select(gr =>
        {
            Group head = gr.FirstOrDefault(x => x.key == gr.Key) ?? gr.First();
            return gr.Count() == 1 ? head : new Group { key = gr.Key, parent = gr.Key, label = head.label == "craft" ? "smith" : head.label,
                desc = string.Join(" ", gr.Select(x => x.desc)), givers = gr.SelectMany(x => x.givers).ToArray(), vanilla = gr.SelectMany(x => x.vanilla).Distinct().ToArray() };
        }).ToList());

        /// <summary>Works split as Complex Jobs splits them (the author; on by default) - or merged as the vanilla work types.</summary>
        public static bool Detail => AvirMod.Settings == null || AvirMod.Settings.magicWorkDetail;
        public static List<Group> Groups => Detail ? detailed : Merged;

        private static readonly Dictionary<string, Group>[] byGiver = new Dictionary<string, Group>[2];
        public static Group OfGiver(WorkGiverDef d)
        {
            int i = Detail ? 1 : 0;
            if (byGiver[i] == null) { byGiver[i] = new Dictionary<string, Group>(); foreach (Group g in Groups) foreach (string n in g.givers) byGiver[i][n] = g; }
            return d != null && byGiver[i].TryGetValue(d.defName, out Group r) ? r : null;
        }
        public static IEnumerable<Group> OfVanillaAll(WorkTypeDef w) => w == null ? Enumerable.Empty<Group>() : Groups.Where(g => g.vanilla.Contains(w.defName));
        public static Group OfVanilla(WorkTypeDef w) => OfVanillaAll(w).FirstOrDefault();
        public static Group ByKey(string k) => Groups.FirstOrDefault(g => g.key == k);

        // ------------------------------------------------------------------ the settings of a caster

        public static int Prio(Hediff_ArcaneCore c, Group g) => c == null || g == null ? 3 : c.arcanePrio.TryGetValue(g.key, out int v) ? v : 3;
        public static void SetPrio(Hediff_ArcaneCore c, Group g, int v)
        {
            c.arcanePrio[g.key] = Mathf.Clamp(v, 0, 4);
            Dirty(c.pawn);
            if (v > 0) EnsureCharms(c, g);
        }
        public static bool MagicOnly(Hediff_ArcaneCore c, WorkTypeDef w) => c != null && w != null && OfVanilla(w) != null && (c.magicOnly.Contains(w.defName) || c.HeldByGreatness(w));
        public static void SetMagicOnly(Hediff_ArcaneCore c, WorkTypeDef w, bool on)
        {
            if (on)
            {
                c.magicOnly.Add(w.defName); c.magicDeclined.Remove(w.defName);
                // the hands stay ready for when the Arcana runs low (Fix153): the work keeps a priority of its own
                if (c.pawn.workSettings != null && c.pawn.workSettings.GetPriority(w) == 0 && !c.pawn.WorkTypeIsDisabled(w)) c.pawn.workSettings.SetPriority(w, Find.PlaySettings.useWorkPriorities ? 4 : 3);
                foreach (Group g in OfVanillaAll(w)) { if (Prio(c, g) == 0) c.arcanePrio[g.key] = 3; EnsureCharms(c, g); }
            }
            else { c.magicOnly.Remove(w.defName); c.magicDeclined.Add(w.defName); }
            Dirty(c.pawn);
        }

        /// <summary>Good hands: the skills of the work at 6 and above (on average).</summary>
        public const float HandSkill = 6f;
        public static float Hands(Pawn p, WorkTypeDef w) => w == null || w.relevantSkills.NullOrEmpty() || p.skills == null ? 10f : (float)w.relevantSkills.Average(s => p.skills.GetSkill(s)?.Level ?? 0);

        /// <summary>Can the caster do this group by magic right now: something in it he may do, and the Arcana above its
        /// lower mark (the author, Fix153: rather than drain the core below its band, a caster with good hands works by hand).</summary>
        public static bool MagicNow(Hediff_ArcaneCore c, Group g)
        {
            if (c == null || g == null) return false;
            c.Band(out float lo, out _);
            if (c.Fill <= lo) return false;
            return g.givers.Any(n => { var ks = KindsOf(n, c); return ks != null && ks.All(k => MagicWork.Knows(c, k)) && StageOk(c, ks, Works2.All.ContainsKey(n)); });
        }

        /// <summary>Does the caster work this ordinary work by hand now? Not "M" - as usual; "M" - only while magic cannot,
        /// and only with good hands; forbidden by greatness - never by hand.</summary>
        public static bool HandsNow(Hediff_ArcaneCore c, WorkTypeDef w)
        {
            if (!MagicOnly(c, w)) return true;
            if (c.HeldByGreatness(w)) return false;
            return !OfVanillaAll(w).Any(g => MagicNow(c, g)) && Hands(c.pawn, w) >= HandSkill;
        }

        /// <summary>Hourly: the "M" state follows the Arcana (the work lists are made again when it turns), and - if the
        /// player allows it (on by default) - a caster with poor hands takes up "M" by himself where magic serves him.</summary>
        public static void Tick(Hediff_ArcaneCore c)
        {
            Pawn p = c.pawn;
            if (p?.workSettings == null || !p.workSettings.EverWork) return;
            if (AvirMod.Settings == null || AvirMod.Settings.magicAutoM)
                foreach (Group g in Groups)
                    foreach (string wn in g.vanilla)
                    {
                        WorkTypeDef w = DefDatabase<WorkTypeDef>.GetNamedSilentFail(wn);
                        if (w == null || c.magicOnly.Contains(wn) || c.magicDeclined.Contains(wn) || p.WorkTypeIsDisabled(w) || p.workSettings.GetPriority(w) == 0) continue;
                        if (Hands(p, w) < HandSkill && MagicNow(c, g) && Prio(c, g) > 0) SetMagicOnly(c, w, true);
                    }
            string state = string.Join(",", DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(w => MagicOnly(c, w)).Select(w => w.defName + (HandsNow(c, w) ? "h" : "m")));
            if (state != c.mState) { c.mState = state; Dirty(p); }
        }

        private static readonly AccessTools.FieldRef<Pawn_WorkSettings, bool> dirty = AccessTools.FieldRefAccess<Pawn_WorkSettings, bool>("workGiversDirty");
        public static void Dirty(Pawn p) { if (p?.workSettings != null) dirty(p.workSettings) = true; }

        // ------------------------------------------------------------------ the schools and the stages

        public static readonly string[] StageShort = { "Novice", "Apprentice", "Adept", "Expert", "Master", "Grandmaster", "never" };
        public static readonly float[] SpeedAt = { 0.7f, 0.85f, 1f, 1.2f, 1.4f, 1.6f };

        /// <summary>The school a work belongs to: its element, its meeting, or the second order.</summary>
        public static string School(RuneKind[] kinds)
        {
            var ks = kinds.Distinct().ToArray();
            if (ks.Length == 1) return ks[0].ToString();
            if (ks.Length == 2) { ComboKind k = Combos.Of(ks[0], ks[1]); return k != ComboKind.None ? k.ToString() : "Second"; }
            return "Second";
        }
        public static readonly string[] Schools = { "Fire", "Water", "Earth", "Air", "Steam", "Storm", "Magma", "Haze", "Bog", "Dust", "Second" };
        public static int DefaultFrom(string school) => school == "Magma" ? 2 : school == "Second" ? 1 : school.Length > 0 && Schools.Take(4).Contains(school) ? 0 : 1;
        public static int From(Hediff_ArcaneCore c, string school) => c != null && c.workFrom.TryGetValue(school, out int v) ? v : DefaultFrom(school);

        public static int Stage(Hediff_ArcaneCore c, RuneKind[] kinds) => Arcane.Stage(c, kinds.Distinct().ToArray());

        /// <summary>May this caster work these elements at all? (stage, the player's threshold)</summary>
        public static bool StageOk(Hediff_ArcaneCore c, RuneKind[] kinds, bool secondOrder = false)
        {
            if (c == null || kinds == null || kinds.Length == 0) return false;
            var ks = kinds.Distinct().ToArray();
            if (ks.Length == 1)
            {
                RuneKind k = ks[0];
                int need = MageClass.Main(c)?.kind == k ? 0 : 1;   // one's own element from Novice, another from Apprentice
                if (MageClass.Stage(MageClass.XpIn(c, k)) < need) return false;
            }
            string school = secondOrder ? "Second" : School(ks);
            int from = From(c, school);
            if (secondOrder && ks.Contains(RuneKind.Fire) && ks.Contains(RuneKind.Earth)) from = Mathf.Max(from, From(c, "Magma"));   // lava: as magma at least
            return from < 6 && Stage(c, ks) >= from;
        }

        public static float Speed(Hediff_ArcaneCore c, RuneKind[] kinds) => c == null ? 1f : SpeedAt[Mathf.Clamp(Stage(c, kinds), 0, 5)];

        // ------------------------------------------------------------------ the slip of a work

        public static float SlipChance(Hediff_ArcaneCore c, RuneKind[] kinds)
        {
            var ks = kinds.Distinct().ToArray();
            int st = Mathf.Clamp(Stage(c, ks), 0, 5);
            return (ks.Length == 1 ? 0.03f : 0.07f) * MageClass.SlipAt[st] * (st == 0 && ks.Length > 1 ? 1.5f : 1f);
        }

        /// <summary>What stands between this caster and a work giver, in words (null - nothing: he can do it).</summary>
        public static string Why(Hediff_ArcaneCore c, string giver)
        {
            RuneKind[] ks = KindsOf(giver, c);
            if (ks == null) return "not made for casters yet";
            var unknown = ks.Distinct().Where(k => !MagicWork.Knows(c, k)).ToList();
            if (unknown.Count > 0) return "does not know " + string.Join(", ", unknown.Select(k => k.ToString().ToLower()));
            var d = ks.Distinct().ToArray();
            bool second = Works2.All.ContainsKey(giver);
            if (d.Length == 1 && MageClass.Main(c)?.kind != d[0] && MageClass.Stage(MageClass.XpIn(c, d[0])) < 1) return "another element: from Apprentice";
            string school = second ? "Second" : School(d);
            int from = From(c, school);
            if (second && d.Contains(RuneKind.Fire) && d.Contains(RuneKind.Earth)) from = Mathf.Max(from, From(c, "Magma"));
            if (from >= 6) return "the works of " + SchoolLabel(school) + " are set to never";
            if (Stage(c, d) < from) return SchoolLabel(school) + " is taken up from " + StageShort[from];
            return null;
        }
        public static string SchoolLabel(string school) => school == "Second" ? "the second order" : school.ToLower();
        public static bool CanDo(Hediff_ArcaneCore c, string giver) => Why(c, giver) == null;

        // ------------------------------------------------------------------ the priority box (the Work sub-tab and the magic panel)

        private static Texture2D[] bgs;
        private static Texture2D Bg(int i)
        {
            if (bgs == null) bgs = new[] { "Awful", "Bad", "Mid", "Excellent" }.Select(n => ContentFinder<Texture2D>.Get("UI/Widgets/WorkBoxBG_" + n, false)).ToArray();
            return bgs[Mathf.Clamp(i, 0, 3)];
        }
        public static Color ColorOfPriority(int p) => p == 1 ? new Color(0f, 1f, 0f) : p == 2 ? new Color(1f, 0.9f, 0.5f) : p == 3 ? new Color(0.8f, 0.7f, 0.5f) : new Color(0.74f, 0.74f, 0.74f);

        /// <summary>A priority box as the Work tab draws it: the background tells how well the caster does it (by his stage in
        /// what he can do of it), the number its priority, coloured as vanilla colours them. Click - up, right-click - down.</summary>
        public static void PrioBox(Rect box, Hediff_ArcaneCore c, Group g)
        {
            var can = g.givers.Where(n => CanDo(c, n)).ToList();
            // the box itself tells how he stands to it: red - nothing of it is open to him (off by itself, and on again by
            // itself once it opens); orange - open, but poorly (a Novice outside his own element); green - well
            int best = can.Count == 0 ? -1 : can.Max(n => Stage(c, KindsOf(n, c)));
            bool own = can.Any(n => KindsOf(n, c).Distinct().Count() == 1 && MageClass.Main(c)?.kind == KindsOf(n, c)[0]);
            Color bg = can.Count == 0 ? new Color(0.45f, 0.1f, 0.08f) : best >= 1 || own ? new Color(0.12f, 0.38f, 0.14f) : new Color(0.6f, 0.32f, 0.04f);
            Widgets.DrawBoxSolid(box, bg);
            Widgets.DrawBox(box);
            int pr = can.Count == 0 ? 0 : Prio(c, g);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter;
            if (can.Count == 0) { GUI.color = new Color(1f, 0.75f, 0.7f); Widgets.Label(box, "-"); }
            else if (pr > 0) { GUI.color = pr == 1 ? new Color(0.6f, 1f, 0.6f) : Color.white; Widgets.Label(box, pr.ToString()); }
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(box, () => g.desc + "\n\n" + (can.Count == 0 ? "Off by itself - nothing of it is open to this caster yet; it turns on by itself when it opens:\n" + string.Join("\n", g.givers.Select(n => "- " + Label(n) + ": " + Why(c, n)))
                : "Can do: " + string.Join(", ", can.Select(Label)) + "." + (can.Count < g.givers.Length ? "\nNot yet: " + string.Join("; ", g.givers.Except(can).Select(n => Label(n) + " - " + Why(c, n))) + "." : "")
                  + "\n\nGreen - he does it well, orange - poorly (a Novice outside his own element).\nClick: a higher number (1 is first); right-click: back. Empty - off."), c.pawn.thingIDNumber ^ g.key.GetHashCode());
            if (can.Count > 0 && Event.current.type == EventType.MouseDown && Mouse.IsOver(box))
            {
                int v = Event.current.button == 1 ? (pr == 0 ? 4 : pr - 1) : (pr == 4 ? 0 : pr + 1);
                SetPrio(c, g, v);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                Event.current.Use();
            }
        }

        /// <summary>The school a work giver belongs to (the second order apart).</summary>
        public static string SchoolOf(string giver) { RuneKind[] ks = KindsOf(giver); return ks == null ? null : Works2.All.ContainsKey(giver) ? "Second" : School(ks); }
        public static IEnumerable<string> SchoolsOf(string giver) => giver == "Avir_ArcaneGrowth" ? new[] { "Water", "Earth" } : new[] { SchoolOf(giver) };

        /// <summary>What each work gives - why it is worth the Arcana (the author asked: why steam, if water washes too?).</summary>
        public static readonly Dictionary<string, string> Gives = new Dictionary<string, string>
        {
            ["Avir_ArcaneFish"] = "fish drawn out of the fishing zones from the shore - a bubble of water brings a few at once, the wind one from farther off",
            ["Avir_ArcaneHunt"] = "the marked animals hunted with the battle charm from range - no gun, no closing in",
            ["Avir_ArcaneFirefight"] = "every fire in a radius goes out at once, from a few cells off - no walking into the flames",
            ["Avir_MagicTend"] = "tends wounds with no medicine at all; the better the stage, the better the tending",
            ["Avir_LifeHeal"] = "heals the wounded out of the store of life; what it leaves after depends on the healer (Life, Warmth, Sap, Frame, Breath, Wild Flame, Blood, Emerald, Pestilence)",
            ["Avir_LifeCure"] = "a healer of Pestilence takes the sicknesses of the sick onto himself, as fuel",
            ["Avir_LifeCrops"] = "quickens the growing patch, once a day each; a caster of Decay takes the blight off too",
            ["Avir_LifeGather"] = "milks, shears and gathers from the animals at a touch - they stand still for Life",
            ["Avir_LifeEmerald"] = "turns marked plants into emerald statues (Emerald)",
            ["Avir_LifeAge"] = "ages the brew in the fermenting barrels on by a quarter (Decay)",
            ["Avir_LifeSoothe"] = "eases the tired and the low: recreation, a little rest, a good memory",
            ["Avir_LifeCare"] = "comes to the one lying on a medical bed for a massage and works the back loose: recreation, rest, ease",
            ["Avir_LifeBathSteam"] = "raises a light steam for a bath where marked, and renews it (Fever)",
            ["Avir_2Purify"] = "takes the food poisoning out of meals and food - no sickness from a careless cook",
            ["Avir_ArcaneRaiseRock"] = "raises natural rock where marked - walls of real stone of the kind chosen, no materials",
            ["Avir_2Basalt"] = "draws chunks of basalt up out of the ground - stone for building where there is none",
            ["Avir_MagicSmooth"] = "smooths floors and walls from afar, much quicker than by hand",
            ["Avir_ArcaneGrowth"] = "quickens the growth of the marked plants (by water or earth)",
            ["Avir_ArcaneEnrich"] = "turns poor ground into rich arcane soil for fifteen days",
            ["Avir_2Water"] = "waters a whole field patch at once - the crops grow faster",
            ["Avir_2Ash"] = "feeds the ground a little: +20% fertility for ten days",
            ["Avir_2Clear"] = "a mudflow clears a wide patch of plants at once",
            ["Avir_ArcaneDrill"] = "draws a sensed ore vein up to the surface - no deep drill",
            ["Avir_2Quarry"] = "brings rock down as chunks, ready to be cut",
            ["Avir_2DigClay"] = "draws clay out of the ground",
            ["Avir_ArcaneCut"] = "cuts stone chunks into blocks with no stonecutter's table",
            ["Avir_ArcaneSmelt"] = "smelts slag into steel with no smelter and no power",
            ["Avir_2SmeltItems"] = "smelts the marked things down to their metal",
            ["Avir_2Obsidian"] = "quenches magma into obsidian - sharp black glass for blades",
            ["Avir_2Bricks"] = "fires stone into bricks",
            ["Avir_2Hone"] = "hones a colonist's blades - sharper for a day",
            ["Avir_MagicHaul"] = "carries a whole heap to the stockpile at once, from afar",
            ["Avir_2Cremate"] = "burns the dead where they lie - no crematorium, no hauling",
            ["Avir_MagicClean"] = "washes all the filth round a spot at once (about 4 cells round)",
            ["Avir_ArcaneSteam"] = "washes a WHOLE ROOM at once - better than water where filth has piled up indoors (taken only for a room with 5+ filth)",
            ["Avir_2Rinse"] = "a whirl of rain rinses a large dirty area at once (taken only where 8+ filth lies close)",
            ["Avir_ArcaneMelt"] = "melts the snow off paths and fields",
            ["Avir_ArcaneWarm"] = "warms a cold room for hours - no heater, no fuel",
            ["Avir_2HeatRoom"] = "ties a heat knot - a room stays warm for ten hours",
            ["Avir_2Vent"] = "airs a room: clears the gas and cools it if overheated",
            ["Avir_ArcaneCharge"] = "charges batteries by a storm",
            ["Avir_2Charge"] = "makes a ball lightning that stays and charges the batteries (ball lightning)",
            ["Avir_2Wind"] = "drives the wind turbines at full even in still air",
            ["Avir_2Dry"] = "dries the stores - rot is set back",
            ["Avir_2Preserve"] = "keeps stores and corpses on the edge of rotting from rotting",
            ["Avir_ArcaneWater"] = "turns marked stone into water for good (Blood)",
            ["Avir_ArcaneHotSpring"] = "warms marked water into a hot spring for three days (steam) - a joy to sit in, quicker meditation for casters of water and fire",
            ["Avir_ArcaneStone"] = "turns marked water into stone",
            ["Avir_2LavaPool"] = "turns marked ground into shallow lava for seven and a half days; the order renews itself (lava)",
            ["Avir_2LavaSet"] = "lets a held lava pool sink into magma, which sets into stone (lava)",
        };

        /// <summary>How the caster stands to a set of elements: 2 - well (all his own or past Novice), 1 - partly (a meeting
        /// with a poor part), 0 - poorly.</summary>
        public static int Standing(Hediff_ArcaneCore c, RuneKind[] kinds)
        {
            var ks = kinds.Distinct().ToArray();
            int good = ks.Count(k => MageClass.Main(c)?.kind == k || MageClass.Stage(MageClass.XpIn(c, k)) >= 1);
            return good == ks.Length ? 2 : good == 0 ? 0 : 1;
        }
        // strong enough on the parchment (the author asked for it)
        public static readonly Color Good = new Color(0.05f, 0.38f, 0.08f), Partly = new Color(0.55f, 0.22f, 0f), Poor = new Color(0.62f, 0.06f, 0.04f);
        public static Color StandingColor(int st) => st == 2 ? Good : st == 1 ? Partly : Poor;

        public static string Label(string giver) => DefDatabase<WorkGiverDef>.GetNamedSilentFail(giver)?.label ?? giver;

        /// <summary>A work may slip: a plain element seldom and lightly; a meeting oftener, and at its own price.</summary>
        public static bool Slip(Pawn p, RuneKind[] kinds)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null || p.Map == null) return false;
            var ks = kinds.Distinct().ToArray();
            if (!Rand.Chance(SlipChance(c, ks))) return false;
            Map map = p.Map;
            string what;
            if (ks.Length == 1)
                switch (ks[0])
                {
                    case RuneKind.Fire: p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 3f, 0f, -1f, p)); FireUtility.TryStartFireIn(p.Position + GenAdj.AdjacentCells[Rand.Range(0, 8)], map, 0.3f, p); what = "a flame broke loose at their feet"; break;
                    case RuneKind.Water: WaterMagic.Soak(p, 1f); what = "the water soaked them"; break;
                    case RuneKind.Earth: MagicFx.Give(p, EarthAirDefOf.Avir_Rooted, 600); what = "the stone stiffened their feet"; break;
                    default: MagicFx.Stun(p, 90, p); what = "the wind knocked the breath out of them"; break;
                }
            else
            {
                ComboKind pair = ks.Length == 2 ? Combos.Of(ks[0], ks[1]) : ComboKind.None;
                if (pair == ComboKind.None) pair = ks.Contains(RuneKind.Fire) && ks.Contains(RuneKind.Earth) ? ComboKind.Magma : Combos.Of(ks[0], ks[1]);
                if (pair != ComboKind.None) ComboMagic.Price(pair, p);
                what = "the meeting of " + string.Join(" and ", ks.Select(k => k.ToString().ToLower())) + " broke loose";
            }
            Messages.Message(p.LabelShortCap + "'s charm slipped: " + what + ".", p, MessageTypeDefOf.NegativeEvent, false);
            return true;
        }

        // ------------------------------------------------------------------ the charm the caster puts together

        private static RuneDef El(RuneKind k) => DefDatabase<RuneDef>.AllDefsListForReading.FirstOrDefault(d => d.role == RuneRole.Element && d.kind == k);
        private static RuneDef Rn(RuneKind k) => DefDatabase<RuneDef>.AllDefsListForReading.FirstOrDefault(d => d.role != RuneRole.Element && d.kind == k);
        private static RuneDef Mod(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);
        public const string SelfMade = "self-made";

        /// <summary>The elements a work giver needs.</summary>
        public static RuneKind[] KindsOf(string giver, Hediff_ArcaneCore c = null)
        {
            // growth is water's or earth's - whichever the caster knows (water first)
            if (giver == "Avir_ArcaneFish") return c != null && !MagicWork.Knows(c, RuneKind.Water) && MagicWork.Knows(c, RuneKind.Air) ? new[] { RuneKind.Air } : new[] { RuneKind.Water };
            if (giver == "Avir_ArcaneHunt") { RuneKind? e = c != null ? CombatCharms.ElementOf(c) : null; return e != null ? new[] { e.Value } : new[] { RuneKind.Fire }; }
            if (giver == "Avir_ArcaneGrowth") return (c != null ? ArcaneWorks.Grower(c) : null) ?? new[] { RuneKind.Water };
            switch (giver)
            {
                case "Avir_MagicClean": case "Avir_MagicTend": case "Avir_ArcaneFirefight": return new[] { RuneKind.Water };
                case "Avir_MagicHaul": return new[] { RuneKind.Air };
                case "Avir_MagicSmooth": return new[] { RuneKind.Earth };
                case "Avir_LifeHeal": case "Avir_LifeCrops": case "Avir_LifeGather": case "Avir_LifeSoothe": return new[] { RuneKind.Life };
                case "Avir_LifeCure": return new[] { RuneKind.Life, RuneKind.Air };
                case "Avir_LifeEmerald": return new[] { RuneKind.Life, RuneKind.Earth };
                case "Avir_LifeAge": return new[] { RuneKind.Life, RuneKind.Earth, RuneKind.Air };
                case "Avir_LifeCare": return new[] { RuneKind.Life };
                case "Avir_LifeBathSteam": return new[] { RuneKind.Life, RuneKind.Fire, RuneKind.Water };
            }
            if (Works2.All.TryGetValue(giver, out Work2 w)) return Works2.Kinds(w.y);
            if (ArcaneWorks.Specs.TryGetValue(giver, out ArcaneSpec s)) return s.kinds;
            return null;
        }

        /// <summary>The caster puts the work's charm together by himself: the work's elements, its runes, and what his stage
        /// allows - Reach for earth from Apprentice, Breadth from Adept, Amplify from Expert. Made again, better, as he grows.</summary>
        public static void EnsureCharm(Hediff_ArcaneCore c, string work, RuneKind[] kinds)
        {
            if (c == null || kinds == null || kinds.Length == 0 || !AvirMagic.On) return;
            var ks = kinds.Distinct().ToArray();
            int st = Mathf.Clamp(Stage(c, ks), 0, 5);
            int breadth = st >= 4 ? 2 : st >= 2 ? 1 : 0, amplify = st >= 5 ? 2 : st >= 3 ? 1 : 0, reach = ks.Contains(RuneKind.Earth) && st >= 1 ? 1 : 0;
            string label = DefDatabase<WorkGiverDef>.GetNamedSilentFail(work)?.label ?? work;
            string name = c.pawn.LabelShort + "'s charm: " + label;
            RitualSchema old = c.Book.spells.FirstOrDefault(s => s != null && s.name == name);
            int level = breadth * 100 + amplify * 10 + reach;
            if (old != null && old.author == SelfMade + level) return;                 // as good as his stage allows already
            if (old != null && old.author != null && !old.author.StartsWith(SelfMade)) return;   // the player has made it his own: left alone
            RuneKind[] need = WorkSpells.Runes.TryGetValue(work, out RuneKind[] n) ? n : new[] { RuneKind.Release };
            var circles = ks.Select(k => new SpellCircle { element = El(k) }).Where(x => x.element != null).ToList();
            if (circles.Count == 0) return;
            var first = new RitualRing();
            foreach (RuneKind k in need) { RuneDef r = Rn(k); if (r != null) first.runes.Add(r); }
            first.slots = Mathf.Max(1, first.runes.Count);
            circles[0].sections.Add(first);
            var mods = new RitualRing();
            for (int i = 0; i < breadth; i++) { RuneDef r = Mod("Breadth"); if (r != null) mods.runes.Add(r); }
            for (int i = 0; i < amplify; i++) { RuneDef r = Mod("Amplify"); if (r != null) mods.runes.Add(r); }
            for (int i = 0; i < reach; i++) { RuneDef r = Mod("Reach"); if (r != null) mods.runes.Add(r); }
            if (mods.runes.Count > 0) { mods.slots = mods.runes.Count; circles[0].sections.Add(mods); }
            var root = new SpellCircle { binding = true };
            root.inner.AddRange(circles);
            var spell = new RitualSchema { name = name, size = RitualCircleSize.Small, author = SelfMade + level, weave = circles.Count == 1 ? SpellCircle.Bound(circles[0]) : root };
            if (old != null) c.Book.spells[c.Book.spells.IndexOf(old)] = spell; else c.Book.spells.Add(spell);
            if (old == null && c.pawn.Faction == Faction.OfPlayer)
                Messages.Message(c.pawn.LabelShortCap + " put a charm together for the work \"" + label + "\" - it is in the book now, to be bettered.", c.pawn, MessageTypeDefOf.NeutralEvent, false);
            else if (old != null && c.pawn.Faction == Faction.OfPlayer)
                Messages.Message(c.pawn.LabelShortCap + " bettered the charm for \"" + label + "\".", c.pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        public static void EnsureCharms(Hediff_ArcaneCore c, Group g)
        {
            if (c == null || g == null) return;
            foreach (string w in g.givers)
            {
                RuneKind[] ks = KindsOf(w, c);
                if (ks != null && ks.All(k => MagicWork.Knows(c, k)) && StageOk(c, ks, Works2.All.ContainsKey(w))) EnsureCharm(c, w, ks);
            }
        }
    }

    /// <summary>The arcane work givers in the order the caster's groups say, among the ordinary ones; a group at 0 - not
    /// at all. (The "magic" work type's own priority no longer matters: its column is gone, the groups decide.)</summary>
    [HarmonyPatch(typeof(Pawn_WorkSettings), "CacheWorkGiversInOrder")]
    public static class Patch_ArcaneWorkOrder
    {
        private static readonly AccessTools.FieldRef<Pawn_WorkSettings, Pawn> pawnOf = AccessTools.FieldRefAccess<Pawn_WorkSettings, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<Pawn_WorkSettings, List<WorkGiver>> normal = AccessTools.FieldRefAccess<Pawn_WorkSettings, List<WorkGiver>>("workGiversInOrderNormal");
        private static readonly AccessTools.FieldRef<Pawn_WorkSettings, List<WorkGiver>> emerg = AccessTools.FieldRefAccess<Pawn_WorkSettings, List<WorkGiver>>("workGiversInOrderEmerg");

        public static void Postfix(Pawn_WorkSettings __instance)
        {
            Pawn p = pawnOf(__instance);
            WorkTypeDef magic = MagicWork.Type;
            if (p == null || magic == null) return;
            Hediff_ArcaneCore c = AvirMagic.On ? Mages.Core(p) : null;
            foreach (var list in new[] { normal(__instance), emerg(__instance) })
            {
                if (list == null) continue;
                bool isEmerg = list == emerg(__instance);
                list.RemoveAll(w => w.def.workType == magic);
                if (c == null || p.WorkTypeIsDisabled(magic)) continue;
                list.RemoveAll(w => w.def.workType != null && ArcaneWork.MagicOnly(c, w.def.workType) && !ArcaneWork.HandsNow(c, w.def.workType));
                foreach (WorkGiverDef d in magic.workGiversByPriority.Where(d => !isEmerg || d.emergency))
                {
                    ArcaneWork.Group g = ArcaneWork.OfGiver(d);
                    int pr = ArcaneWork.Prio(c, g);
                    if (pr <= 0) continue;
                    // a group standing for an "M" work: not while the Arcana is low (the hands take over, if good)
                    if (g != null && g.vanilla.Any(n => c.magicOnly.Contains(n)) && !ArcaneWork.MagicNow(c, g)) continue;
                    int at = list.FindIndex(w => w.def.workType != magic && __instance.GetPriority(w.def.workType) > pr);
                    if (at < 0) list.Add(d.Worker); else list.Insert(at, d.Worker);
                }
            }
        }
    }

    /// <summary>The "M" in the Work tab: only for casters, only on works magic can stand for.</summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkCellMagic
    {
        private static readonly Texture2D MTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.12f, 0.22f, 0.55f));

        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn)
        {
            if (!AvirMagic.On || pawn == null || pawn.Dead) return true;
            Hediff_ArcaneCore c = Mages.Core(pawn);
            WorkTypeDef w = __instance.def?.workType;
            ArcaneWork.Group g = ArcaneWork.OfVanilla(w);
            if (c == null || g == null) return true;
            Rect box = new Rect(rect.x + (int)((rect.width - 25f) / 2f), rect.y + 2.5f, 25f, 25f);
            bool held = c.HeldByGreatness(w);
            if (ArcaneWork.MagicOnly(c, w))
            {
                GUI.DrawTexture(box, MTex);
                Widgets.DrawBox(box);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.85f, 0.9f, 1f);
                Widgets.Label(box, "M");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(box, "By magic: " + g.desc + "\nWhile the Arcana is above its lower mark the work is done by magic; below it - by hand, if the hands are good (skill 6+), else not at all. The order among the arcane works - in the Arcane works tab."
                    + (held ? "\nGreatness forbids this work by hand: only by magic, and this \"M\" cannot be taken off." : "\nNow: " + (ArcaneWork.HandsNow(c, w) ? "by hand" : "by magic") + ".\nClick: off; right-click: back to by hand only."));
                if (!held && Event.current.type == EventType.MouseDown && Mouse.IsOver(box))
                {
                    bool right = Event.current.button == 1;
                    ArcaneWork.SetMagicOnly(c, w, false);
                    pawn.workSettings.SetPriority(w, right ? (Find.PlaySettings.useWorkPriorities ? 4 : 3) : 0);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    Event.current.Use();
                }
                return false;
            }
            // into "M": past the last priority (or past "on" without priorities)
            if (!pawn.WorkTypeIsDisabled(w) && Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(box))
            {
                int pr = pawn.workSettings.GetPriority(w);
                if ((Find.PlaySettings.useWorkPriorities && pr == 4) || (!Find.PlaySettings.useWorkPriorities && pr > 0))
                {
                    ArcaneWork.SetMagicOnly(c, w, true);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    Event.current.Use();
                    return false;
                }
            }
            return true;
        }

        /// <summary>Blue where the hands are poor but magic can do it.</summary>
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn)
        {
            if (!AvirMagic.On || pawn == null || pawn.Dead) return;
            Hediff_ArcaneCore c = Mages.Core(pawn);
            WorkTypeDef w = __instance.def?.workType;
            if (c == null || ArcaneWork.OfVanilla(w) == null || ArcaneWork.MagicOnly(c, w) || pawn.WorkTypeIsDisabled(w) || w.relevantSkills.NullOrEmpty()) return;
            float lvl = (float)w.relevantSkills.Average(s => pawn.skills?.GetSkill(s)?.Level ?? 0);
            if (lvl >= 6f) return;
            Rect box = new Rect(rect.x + (int)((rect.width - 25f) / 2f), rect.y + 2.5f, 25f, 25f);
            Widgets.DrawBoxSolid(box, new Color(0.2f, 0.45f, 1f, 0.28f));
            TooltipHandler.TipRegion(box, "Poor hands, but this can be done by magic: click past the last priority for \"M\".");
        }
    }

    /// <summary>The Work tab leads to the arcane works (only if there is a caster at all).</summary>
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class Patch_WorkTabArcane
    {
        public static void Postfix(Rect rect)
        {
            if (!AvirMagic.On || Find.CurrentMap == null || !Find.CurrentMap.mapPawns.FreeColonists.Any(p => Mages.Core(p) != null)) return;
            MainButtonDef tab = DefDatabase<MainButtonDef>.GetNamedSilentFail("Avir_ArcaneWork");
            if (tab == null) return;
            if (Widgets.ButtonText(new Rect(rect.xMax - 190f, rect.y + 4f, 180f, 30f), "Arcane works"))
                Find.MainTabsRoot.SetCurrentTab(tab);
        }
    }

    /// <summary>The sub-tab: casters by rows, groups of arcane works by columns, a priority in each.</summary>
    public class MainTabWindow_ArcaneWork : MainTabWindow_PawnTable
    {
        protected override PawnTableDef PawnTableDef => DefDatabase<PawnTableDef>.GetNamed("Avir_ArcaneWork");
        protected override float ExtraTopSpace => 46f;
        protected override IEnumerable<Pawn> Pawns => base.Pawns.Where(p => Mages.Core(p) != null);

        public override void DoWindowContents(Rect rect)
        {
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 4f, 180f, 30f), "Ordinary works")) { MainButtonDef w = DefDatabase<MainButtonDef>.GetNamedSilentFail("Work"); if (w != null) Find.MainTabsRoot.SetCurrentTab(w); }
            Text.Font = GameFont.Small; GUI.color = new Color(0.92f, 0.9f, 0.85f);
            Widgets.Label(new Rect(rect.x + 195f, rect.y + 2f, rect.width - 200f, 40f), "Arcane works by casters. Click a box - a higher priority (1 is first), right-click - back, empty - off. The box shows how well he does it; point at it for what he can and cannot do, and why. More on the Arcane works page of each caster's magic panel.");
            GUI.color = Color.white;
            base.DoWindowContents(rect);
        }
    }

    public class PawnColumnWorker_ArcaneGroup : PawnColumnWorker
    {
        private ArcaneWork.Group G => ArcaneWork.ByKey(def.defName.Substring("Avir_ArcaneCol_".Length));
        public override bool VisibleCurrently => G != null;

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            Hediff_ArcaneCore c = Mages.Core(pawn);
            ArcaneWork.Group g = G;
            if (c == null || g == null) return;
            ArcaneWork.PrioBox(new Rect(rect.x + (int)((rect.width - 25f) / 2f), rect.y + 2.5f, 25f, 25f), c, g);
        }

        public override void DoHeader(Rect rect, PawnTable table)
        {
            ArcaneWork.Group g = G;
            if (g == null) return;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.LowerCenter; GUI.color = new Color(0.95f, 0.92f, 0.85f);
            Widgets.Label(new Rect(rect.x - 10f, rect.y, rect.width + 20f, rect.height - 2f), g.label.CapitalizeFirst());
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, g.desc + (g.vanilla.Length > 0 ? "\nStands for: " + string.Join(", ", g.vanilla.Select(n => DefDatabase<WorkTypeDef>.GetNamedSilentFail(n)?.labelShort).Where(x => x != null)) + " (\"M\" in the Work tab)." : "\nOnly magic does this."));
        }

        public override int GetMinWidth(PawnTable table) => 62;
        public override int GetOptimalWidth(PawnTable table) => 74;
        public override int GetMinHeaderHeight(PawnTable table) => 34;
        public override int Compare(Pawn a, Pawn b) => ArcaneWork.Prio(Mages.Core(a), G).CompareTo(ArcaneWork.Prio(Mages.Core(b), G));
    }

    /// <summary>Casters meditate on their own in free time, to fill the Arcana up to its white mark (the author, Fix152).</summary>
    public class JoyGiver_ArcaneMeditate : JoyGiver
    {
        /// <summary>The emptier the core, the likelier: with no Arcana it is among the first things a caster turns to.</summary>
        public override float GetChance(Pawn pawn)
        {
            Hediff_ArcaneCore c = AvirMagic.On ? Mages.Core(pawn) : null;
            if (c == null || c.arcana >= c.target * c.Capacity - 1f) return 0f;
            float lack = 1f - Mathf.Clamp01(c.Fill / Mathf.Max(0.05f, c.target));
            return base.GetChance(pawn) * (1f + 14f * lack * lack);
        }

        public override Job TryGiveJob(Pawn pawn)
        {
            if (!AvirMagic.On) return null;
            Hediff_ArcaneCore c = Mages.Core(pawn);
            if (c == null || c.arcana >= c.target * c.Capacity - 1f) return null;
            return MeditationUtility.GetMeditationJob(pawn, true);
        }
    }
}

namespace Avir.Magic
{
    public static class ArcaneWorkDev
    {
        private const string Cat = "Magic";

        [LudeonTK.DebugAction(Cat, "Arcane works: put together all charms now", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Charms(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c == null) return; foreach (var g in ArcaneWork.Groups) ArcaneWork.EnsureCharms(c, g); }

        [LudeonTK.DebugAction(Cat, "Arcane works: slip now (as a work of the main element)", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void SlipNow(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p); RuneDef m = c != null ? MageClass.Main(c) : null; if (m == null) return;
            for (int i = 0; i < 200 && !ArcaneWork.Slip(p, new[] { m.kind }); i++) { }
        }
    }
}
