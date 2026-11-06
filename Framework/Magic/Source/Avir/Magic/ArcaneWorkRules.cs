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
            public string key, label, desc;
            public string[] givers;
            public string[] vanilla;   // the ordinary work types it can stand for ("M")
        }

        public static readonly List<Group> Groups = new List<Group>
        {
            new Group { key = "Clean", label = "clean", desc = "Washing the floor by water, a room by steam, the home by a rinse.", givers = new[] { "Avir_MagicClean", "Avir_ArcaneSteam", "Avir_2Rinse", "Avir_ArcaneMelt" }, vanilla = new[] { "Cleaning" } },
            new Group { key = "Haul", label = "haul", desc = "Carrying by the wind.", givers = new[] { "Avir_MagicHaul" }, vanilla = new[] { "Hauling" } },
            new Group { key = "Tend", label = "tend", desc = "Tending the wounded by water.", givers = new[] { "Avir_MagicTend" }, vanilla = new[] { "Doctor" } },
            new Group { key = "Build", label = "build", desc = "Raising rock, smoothing by earth, drawing basalt.", givers = new[] { "Avir_MagicSmooth", "Avir_ArcaneRaiseRock", "Avir_2Basalt" }, vanilla = new[] { "Construction" } },
            new Group { key = "Mine", label = "mine", desc = "Drawing ore up, cutting stone by dust, quarrying, drawing clay.", givers = new[] { "Avir_ArcaneDrill", "Avir_ArcaneCut", "Avir_2Quarry", "Avir_2DigClay" }, vanilla = new[] { "Mining" } },
            new Group { key = "Grow", label = "grow", desc = "Quickening growth, enriching soil by bog, watering the fields, ashing the ground, clearing land.", givers = new[] { "Avir_ArcaneGrowth", "Avir_ArcaneEnrich", "Avir_2Water", "Avir_2Ash", "Avir_2Clear" }, vanilla = new[] { "Growing", "PlantCutting" } },
            new Group { key = "Smith", label = "smith", desc = "Smelting slag by magma, smelting things by lava, making obsidian, firing bricks, honing blades.", givers = new[] { "Avir_ArcaneSmelt", "Avir_2SmeltItems", "Avir_2Obsidian", "Avir_2Bricks", "Avir_2Hone" }, vanilla = new[] { "Smithing", "Crafting" } },
            new Group { key = "Warm", label = "warmth", desc = "Warming rooms by haze, tying heat knots, airing the rooms.", givers = new[] { "Avir_ArcaneWarm", "Avir_2HeatRoom", "Avir_2Vent" }, vanilla = new string[0] },
            new Group { key = "Power", label = "power", desc = "Charging batteries, driving the wind turbines.", givers = new[] { "Avir_ArcaneCharge", "Avir_2Charge", "Avir_2Wind" }, vanilla = new string[0] },
            new Group { key = "Stores", label = "stores", desc = "Cremating the dead, purifying food, drying and preserving the stores.", givers = new[] { "Avir_2Cremate", "Avir_2Purify", "Avir_2Dry", "Avir_2Preserve" }, vanilla = new[] { "Cooking" } },
            new Group { key = "Land", label = "land", desc = "The arcane orders on land and water: stone and water, moats, silt, geysers, lava cores, marsh gas, rainbows.", givers = new[] { "Avir_ArcaneWater", "Avir_ArcaneStone", "Avir_2Moat", "Avir_2SiltWater", "Avir_2SiltLand", "Avir_2SiltDump", "Avir_2RaiseGeyser", "Avir_2SealGeyser", "Avir_2LavaCore", "Avir_2MarshGas", "Avir_2Rainbow" }, vanilla = new string[0] },
        };

        private static Dictionary<string, Group> byGiver;
        public static Group OfGiver(WorkGiverDef d)
        {
            if (byGiver == null) { byGiver = new Dictionary<string, Group>(); foreach (Group g in Groups) foreach (string n in g.givers) byGiver[n] = g; }
            return d != null && byGiver.TryGetValue(d.defName, out Group r) ? r : null;
        }
        public static Group OfVanilla(WorkTypeDef w) => w == null ? null : Groups.FirstOrDefault(g => g.vanilla.Contains(w.defName));
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
            if (on) { c.magicOnly.Add(w.defName); c.pawn.workSettings?.SetPriority(w, 0); Group g = OfVanilla(w); if (Prio(c, g) == 0) c.arcanePrio[g.key] = 3; EnsureCharms(c, g); }
            else c.magicOnly.Remove(w.defName);
            Dirty(c.pawn);
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

        /// <summary>A work may slip: a plain element seldom and lightly; a meeting oftener, and at its own price.</summary>
        public static bool Slip(Pawn p, RuneKind[] kinds)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null || p.Map == null) return false;
            var ks = kinds.Distinct().ToArray();
            int st = Mathf.Clamp(Stage(c, ks), 0, 5);
            float chance = (ks.Length == 1 ? 0.03f : 0.07f) * MageClass.SlipAt[st] * (st == 0 && ks.Length > 1 ? 1.5f : 1f);
            if (!Rand.Chance(chance)) return false;
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
        public static RuneKind[] KindsOf(string giver)
        {
            switch (giver)
            {
                case "Avir_MagicClean": case "Avir_MagicTend": return new[] { RuneKind.Water };
                case "Avir_MagicHaul": return new[] { RuneKind.Air };
                case "Avir_MagicSmooth": return new[] { RuneKind.Earth };
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
                RuneKind[] ks = KindsOf(w);
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
                foreach (WorkGiverDef d in magic.workGiversByPriority.Where(d => !isEmerg || d.emergency))
                {
                    int pr = ArcaneWork.Prio(c, ArcaneWork.OfGiver(d));
                    if (pr <= 0) continue;
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
                TooltipHandler.TipRegion(box, "By magic only: " + g.desc + "\nNever by hand; if magic cannot do it now, it is not done. The order among the arcane works - in the Arcane works tab."
                    + (held ? "\nGreatness forbids this work by hand - by magic it may." : "\nClick: back to off; right-click: back to by hand."));
                if (!held && Event.current.type == EventType.MouseDown && Mouse.IsOver(box))
                {
                    bool right = Event.current.button == 1;
                    ArcaneWork.SetMagicOnly(c, w, false);
                    if (right) pawn.workSettings.SetPriority(w, Find.PlaySettings.useWorkPriorities ? 4 : 3);
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
        protected override float ExtraTopSpace => 40f;
        protected override IEnumerable<Pawn> Pawns => base.Pawns.Where(p => Mages.Core(p) != null);

        public override void DoWindowContents(Rect rect)
        {
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 4f, 180f, 30f), "Ordinary works")) { MainButtonDef w = DefDatabase<MainButtonDef>.GetNamedSilentFail("Work"); if (w != null) Find.MainTabsRoot.SetCurrentTab(w); }
            Text.Font = GameFont.Small; GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(rect.x + 195f, rect.y + 9f, rect.width - 200f, 26f), "Click: higher number (1 first); right-click: back. Each caster uses the best charm he has put together for it.");
            GUI.color = Color.white;
            base.DoWindowContents(rect);
        }
    }

    public class PawnColumnWorker_ArcaneGroup : PawnColumnWorker
    {
        private ArcaneWork.Group G => ArcaneWork.ByKey(def.defName.Substring("Avir_ArcaneCol_".Length));

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            Hediff_ArcaneCore c = Mages.Core(pawn);
            ArcaneWork.Group g = G;
            if (c == null || g == null) return;
            Rect box = new Rect(rect.x + (int)((rect.width - 25f) / 2f), rect.y + 2.5f, 25f, 25f);
            int pr = ArcaneWork.Prio(c, g);
            var can = g.givers.Where(n => { var ks = ArcaneWork.KindsOf(n); return ks != null && ks.All(k => MagicWork.Knows(c, k)) && ArcaneWork.StageOk(c, ks, Works2.All.ContainsKey(n)); }).ToList();
            Widgets.DrawBoxSolid(box, can.Count > 0 ? new Color(0.1f, 0.12f, 0.2f) : new Color(0.12f, 0.12f, 0.12f, 0.5f));
            Widgets.DrawBox(box);
            if (pr > 0)
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = can.Count == 0 ? Color.gray : pr == 1 ? new Color(0.5f, 0.75f, 1f) : pr == 2 ? new Color(0.75f, 0.85f, 1f) : pr == 3 ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.65f, 0.65f, 0.65f);
                Widgets.Label(box, pr.ToString());
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            }
            TooltipHandler.TipRegion(box, () => g.desc + "\n\n" + (can.Count == 0 ? "This caster can do none of it yet (elements not known, stage too low, or below the stage set on the Path page)."
                : "Can do: " + string.Join(", ", can.Select(n => DefDatabase<WorkGiverDef>.GetNamedSilentFail(n)?.label ?? n)) + "."), pawn.thingIDNumber ^ g.key.GetHashCode());
            if (Event.current.type == EventType.MouseDown && Mouse.IsOver(box))
            {
                int v = Event.current.button == 1 ? (pr == 0 ? 4 : pr - 1 == 0 ? 0 : pr - 1) : (pr == 0 ? 1 : pr == 4 ? 0 : pr + 1);
                if (Event.current.button == 1 && pr == 1) v = 0;
                if (Event.current.button == 1 && pr == 0) v = 4;
                ArcaneWork.SetPrio(c, g, v);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                Event.current.Use();
            }
        }

        public override int GetMinWidth(PawnTable table) => 56;
        public override int GetOptimalWidth(PawnTable table) => 64;
        protected override string GetHeaderTip(PawnTable table) => G?.desc;
        public override int Compare(Pawn a, Pawn b) => ArcaneWork.Prio(Mages.Core(a), G).CompareTo(ArcaneWork.Prio(Mages.Core(b), G));
    }

    /// <summary>Casters meditate on their own in free time, to fill the Arcana up to its white mark (the author, Fix152).</summary>
    public class JoyGiver_ArcaneMeditate : JoyGiver
    {
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
        private const string Cat = "Avir magic";

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
