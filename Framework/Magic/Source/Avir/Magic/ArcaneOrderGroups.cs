using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Avir.Ritualistics;

namespace Avir.Magic
{
    // =====================================================================================
    // ONE ORDER, ONE RESULT (the author, after Fix265): orders that do one thing in different ways are one button now,
    // as a building is one button with its material chosen. The way is switched on a panel of the mod's own (never the
    // game's pop-up menu); the first way is "as the ground says" - the cell, and the casters the colony has, decide.
    // What is built (walls, geysers, springs, crystals) stays an order of its own. The old orders and their marks are
    // kept underneath: a group only chooses which of them to lay, so saves and the work givers are untouched.
    // =====================================================================================
    public class OrderWay
    {
        public string key;          // the designation (and the work) it lays
        public string label, note;  // on the panel
        public Designator_Arcane proto;
        public Texture2D Icon => proto?.icon as Texture2D;
    }

    public abstract class Designator_ArcaneGroup : Designator_Arcane
    {
        private static readonly Dictionary<Type, int> chosen = new Dictionary<Type, int>();
        private List<OrderWay> ways;
        protected abstract string Name { get; }
        protected abstract string AutoNote { get; }
        protected abstract IEnumerable<OrderWay> MakeWays();
        /// <summary>"As the ground says": which way fits this cell, the casters at hand weighed.</summary>
        protected abstract OrderWay Auto(IntVec3 c);

        protected List<OrderWay> Ways => ways ?? (ways = MakeWays().Where(w => w.proto != null).ToList());
        protected OrderWay Way(string key) => Ways.FirstOrDefault(w => w.key == key);
        private int Chosen { get => chosen.TryGetValue(GetType(), out int i) ? i : 0; set => chosen[GetType()] = value; }
        private OrderWay Fixed => Chosen > 0 && Chosen <= Ways.Count ? Ways[Chosen - 1] : null;

        public override bool Visible => base.Visible && Ways.Any(w => w.proto.Visible);
        protected override DesignationDef Designation => DefDatabase<DesignationDef>.GetNamedSilentFail(Ways.FirstOrDefault()?.key ?? "");
        public override string Label => Name + ": " + (Fixed?.label ?? "as the ground says");
        public override string Desc => Name + ". " + (Fixed != null ? Fixed.note : AutoNote) + "\n\nThe way is switched on the panel that opens with the order.";
        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions => Enumerable.Empty<FloatMenuOption>();

        private OrderWay For(IntVec3 c)
        {
            if (Fixed != null) return Fixed;
            return Auto(c);
        }

        protected bool Takes(OrderWay w, IntVec3 c)
        {
            if (w?.proto == null || !w.proto.Visible) return false;
            return w.proto.CanDesignateCell(c).Accepted;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map)) return false;
            OrderWay w = For(c);
            if (w == null) return Fixed == null ? "Nothing here answers this order." : false;
            return w.proto.CanDesignateCell(c);
        }

        public override void DesignateSingleCell(IntVec3 c) => For(c)?.proto.DesignateSingleCell(c);

        public override void DoExtraGuiControls(float leftX, float bottomY)
        {
            float w = 236f, row = 30f;
            int n = Ways.Count + 1;
            Rect r = new Rect(leftX, bottomY - (n * row + 40f), w, n * row + 40f);
            Find.WindowStack.ImmediateWindow(73460 + GetType().Name.GetHashCode() % 1000, r, WindowLayer.GameUI, () =>
            {
                Rect inR = new Rect(0f, 0f, r.width, r.height).ContractedBy(6f);
                Widgets.DrawBoxSolid(new Rect(0f, 0f, r.width, r.height), new Color(0.11f, 0.09f, 0.07f, 0.92f));
                GUI.color = new Color(0.85f, 0.7f, 0.35f, 0.85f); Widgets.DrawBox(new Rect(0f, 0f, r.width, r.height), 1); GUI.color = Color.white;
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(0.95f, 0.88f, 0.7f);
                Widgets.Label(new Rect(inR.x + 2f, inR.y, inR.width, 24f), Name);
                GUI.color = Color.white;
                for (int i = 0; i < n; i++)
                {
                    Rect b = new Rect(inR.x, inR.y + 28f + i * row, inR.width, row - 2f);
                    bool on = Chosen == i;
                    OrderWay way = i == 0 ? null : Ways[i - 1];
                    bool can = way == null || way.proto.Visible;
                    Widgets.DrawBoxSolid(b, on ? new Color(0.55f, 0.42f, 0.18f, 0.55f) : Mouse.IsOver(b) ? new Color(1f, 0.9f, 0.6f, 0.12f) : new Color(0f, 0f, 0f, 0.15f));
                    if (on) { GUI.color = new Color(1f, 0.85f, 0.45f); Widgets.DrawBox(b, 1); GUI.color = Color.white; }
                    Texture2D ic = way?.Icon ?? (icon as Texture2D);
                    if (ic != null) { GUI.color = can ? Color.white : new Color(1f, 1f, 1f, 0.35f); GUI.DrawTexture(new Rect(b.x + 3f, b.y + 2f, b.height - 4f, b.height - 4f), ic, ScaleMode.ScaleToFit); }
                    GUI.color = can ? new Color(0.94f, 0.9f, 0.82f) : new Color(0.6f, 0.58f, 0.55f);
                    Widgets.Label(new Rect(b.x + b.height + 4f, b.y, b.width - b.height - 6f, b.height), way == null ? "As the ground says" : way.label);
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(b, way == null ? AutoNote : way.note + (can ? "" : "\n\n(Not in this game.)"));
                    if (can && Widgets.ButtonInvisible(b)) { Chosen = i; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                }
                Text.Anchor = TextAnchor.UpperLeft;
            }, doBackground: false, absorbInputAroundWindow: false, shadowAlpha: 0f);
        }

        /// <summary>What "as the ground says" would lay on this cell (for the developer's look).</summary>
        public string Explain(IntVec3 c) => Name + ": " + (Auto(c)?.label ?? "nothing here");

        [LudeonTK.DebugAction("Magic", "Arcane orders: what each order lays here (as the ground says)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevExplain()
        {
            IntVec3 c = Verse.UI.MouseCell();
            var all = new Designator_ArcaneGroup[] { new Designator_OrderExtract(), new Designator_OrderWater(), new Designator_OrderLand(), new Designator_OrderFertile(), new Designator_OrderLava(), new Designator_OrderGlowWater() };
            Messages.Message(c + " - " + string.Join("; ", all.Select(d => d.Explain(c))), MessageTypeDefOf.NeutralEvent, false);
        }

        // ---------------------------------------------------------------- who can do what, in this colony

        /// <summary>Some free colonist here could do this work now (its elements known; the Arcana is not weighed).</summary>
        public static bool ColonyCan(Map m, string key)
        {
            if (m == null) return false;
            foreach (Pawn p in m.mapPawns.FreeColonistsSpawned)
            {
                if (Works2.All.TryGetValue(key, out Work2 w)) { if (Works2.Able(p, w, 0f) != null) return true; continue; }
                if (ArcaneWorks.Specs.TryGetValue(key, out ArcaneSpec s))
                {
                    if (s.kinds.Length > 0) { if (Arcane.Able(p, 0f, s.kinds) != null) return true; }
                    else { Hediff_ArcaneCore c = Mages.Core(p); if (MagicWork.Knows(c, RuneKind.Water) || MagicWork.Knows(c, RuneKind.Earth)) return true; }
                }
            }
            return false;
        }

        /// <summary>The first way that takes the cell and that someone here can do; else the first that takes it at all.</summary>
        protected OrderWay FirstAble(IntVec3 c, params string[] keys)
        {
            OrderWay any = null;
            foreach (string k in keys)
            {
                OrderWay w = Way(k);
                if (!Takes(w, c)) continue;
                if (ColonyCan(Map, k)) return w;
                if (any == null) any = w;
            }
            return any;
        }
    }

    /// <summary>Draw from the ground: ore, rock, clay, marsh gas, basalt, a lava core - whatever the ground holds.</summary>
    public class Designator_OrderExtract : Designator_ArcaneGroup
    {
        public Designator_OrderExtract() { defaultLabel = "Draw from the ground"; icon = ArcaneIcons.For("Avir_2Quarry", "UI/Designators/Mine"); }
        protected override string Name => "Draw from the ground";
        protected override string AutoNote => "The ground decides: a rock wall gives chunks, a sensed vein its ore, a bog marsh gas, bare stone basalt, soil, mud or gravel clay. By lava - a lava core, if a caster here holds lava; else basalt.";
        protected override IEnumerable<OrderWay> MakeWays() => new[]
        {
            new OrderWay { key = "Avir_ArcaneDrill", label = "Ore of a sensed vein", note = "Casters of earth draw a sensed vein's ore up, a portion at a time, until it is spent.", proto = new Designator_ArcaneDrill() },
            new OrderWay { key = "Avir_2Quarry", label = "Rock into chunks", note = "A caster of earth brings rock walls down into chunks, one at a time; the rockfall (earth, air) - several at once. Marked through unseen rock like any mining.", proto = new Designator_2Quarry() },
            new OrderWay { key = "Avir_2DigClay", label = "Clay", note = "Earth, air and water draw clay out of soil, mud or gravel - twenty at a time, twice a day at most.", proto = new Designator_2DigClay() },
            new OrderWay { key = "Avir_2MarshGas", label = "Marsh gas", note = "Water, earth and fire draw marsh gas out of a bog into a canister - twice a day at most. A fuel stronger than chemfuel.", proto = new Designator_2MarshGas() },
            new OrderWay { key = "Avir_2Basalt", label = "Basalt", note = "The volcano (fire, earth; an adept of earth) draws two chunks of basalt out of bare stone ground, or beside lava.", proto = new Designator_2Basalt() },
            new OrderWay { key = "Avir_2LavaCore", label = "Lava core", note = "Lava seals three cores of lava in basalt, at lava or magma: a long fuel for any wood-fed forge, smelter or stove.", proto = new Designator_2LavaCore() },
        };
        protected override OrderWay Auto(IntVec3 c)
        {
            if (c.Fogged(Map) || c.GetEdifice(Map)?.def.building?.isNaturalRock == true) return Takes(Way("Avir_2Quarry"), c) ? Way("Avir_2Quarry") : null;
            if (Takes(Way("Avir_ArcaneDrill"), c)) return Way("Avir_ArcaneDrill");
            if (Works2.NearLava(Map, c))
            {
                // the author's rule: by lava a core, if a caster here holds lava; else basalt
                if (ColonyCan(Map, "Avir_2LavaCore") && Takes(Way("Avir_2LavaCore"), c)) return Way("Avir_2LavaCore");
                if (Takes(Way("Avir_2Basalt"), c)) return Way("Avir_2Basalt");
            }
            if (Takes(Way("Avir_2MarshGas"), c)) return Way("Avir_2MarshGas");
            if (Works2.StoneGround(Map, c) && Takes(Way("Avir_2Basalt"), c)) return Way("Avir_2Basalt");
            if (Takes(Way("Avir_2DigClay"), c)) return Way("Avir_2DigClay");
            return null;
        }
    }

    /// <summary>Ground into water: a moat of the quagmire (30 days), silt (15 days), or Blood's water (for good).</summary>
    public class Designator_OrderWater : Designator_ArcaneGroup
    {
        public Designator_OrderWater() { defaultLabel = "Ground to water"; icon = ArcaneIcons.For("Avir_2Moat", "UI/Designators/SmoothSurface"); }
        protected override string Name => "Ground to water";
        protected override string AutoNote => "Whoever here can: the quagmire floods it for 30 days, else silt for 15 days. Blood's water is for good - it is laid only where nothing else can, or when chosen.";
        protected override IEnumerable<OrderWay> MakeWays() => new[]
        {
            new OrderWay { key = "Avir_2Moat", label = "Moat - 30 days (quagmire)", note = "The quagmire (water, earth; an adept of water) floods the ground into shallow water for 30 days.", proto = new Designator_2Moat() },
            new OrderWay { key = "Avir_2SiltWater", label = "Water - 15 days (silt)", note = "Silt (water, earth; an adept of earth) turns the ground into shallow water for 15 days.", proto = new Designator_2SiltWater() },
            new OrderWay { key = "Avir_ArcaneWater", label = "Water for good (blood)", note = "Blood (life and water) turns natural stone ground into shallow water - for good. Earth can make it stone again.", proto = new Designator_ArcaneWater() },
        };
        protected override OrderWay Auto(IntVec3 c) => FirstAble(c, "Avir_2Moat", "Avir_2SiltWater") is OrderWay w && (ColonyCan(Map, w.key) || !ColonyCan(Map, "Avir_ArcaneWater") || !Takes(Way("Avir_ArcaneWater"), c)) ? w : Takes(Way("Avir_ArcaneWater"), c) ? Way("Avir_ArcaneWater") : null;
    }

    /// <summary>Water into ground: silt for 15 days, or earth's stone for good.</summary>
    public class Designator_OrderLand : Designator_ArcaneGroup
    {
        public Designator_OrderLand() { defaultLabel = "Water to ground"; icon = ArcaneIcons.For("Avir_2SiltLand", "UI/Designators/SmoothSurface"); }
        protected override string Name => "Water to ground";
        protected override string AutoNote => "Whoever here can: silt makes it ground for 15 days, else earth makes it rough stone for good.";
        protected override IEnumerable<OrderWay> MakeWays() => new[]
        {
            new OrderWay { key = "Avir_2SiltLand", label = "Ground - 15 days (silt)", note = "Silt turns shallow water into ground for 15 days.", proto = new Designator_2SiltLand() },
            new OrderWay { key = "Avir_ArcaneStone", label = "Stone for good (earth)", note = "Earth turns shallow water into the rough stone of this land - for good.", proto = new Designator_ArcaneStone() },
        };
        protected override OrderWay Auto(IntVec3 c) => FirstAble(c, "Avir_2SiltLand", "Avir_ArcaneStone");
    }

    /// <summary>Feed the ground: the bog's rich soil, ash, or quickened growth.</summary>
    public class Designator_OrderFertile : Designator_ArcaneGroup
    {
        public Designator_OrderFertile() { defaultLabel = "Feed the ground"; icon = ArcaneIcons.For("Avir_ArcaneEnrich", "UI/Designators/PlantsHarvest"); }
        protected override string Name => "Feed the ground";
        protected override string AutoNote => "Whoever here can: the bog enriches the soil for 15 days, else ash feeds it for 10; where neither can, a growing plant is quickened.";
        protected override IEnumerable<OrderWay> MakeWays() => new[]
        {
            new OrderWay { key = "Avir_ArcaneEnrich", label = "Rich soil - 15 days (bog)", note = "The bog (water, earth) makes poor ground arcane soil, soil - rich arcane soil, for 15 days.", proto = new Designator_ArcaneEnrich() },
            new OrderWay { key = "Avir_2Ash", label = "Ash - 10 days", note = "Fire, earth and air let ash fall: +20% fertility for ten days.", proto = new Designator_2Ash() },
            new OrderWay { key = "Avir_ArcaneGrowth", label = "Quicken growth", note = "Water or earth quicken a growing plant, once a day for each spot, until it is grown.", proto = new Designator_ArcaneGrowth() },
        };
        protected override OrderWay Auto(IntVec3 c)
        {
            foreach (string k in new[] { "Avir_ArcaneEnrich", "Avir_2Ash", "Avir_ArcaneGrowth" })
                if (ColonyCan(Map, k) && Takes(Way(k), c)) return Way(k);
            return FirstAble(c, "Avir_ArcaneEnrich", "Avir_2Ash", "Avir_ArcaneGrowth");
        }
    }

    /// <summary>Lava: pour a pool, or let a held one set.</summary>
    public class Designator_OrderLava : Designator_ArcaneGroup
    {
        public Designator_OrderLava() { defaultLabel = "Lava"; icon = ArcaneIcons.For("Avir_2LavaPool", "UI/Designators/SmoothSurface"); }
        protected override string Name => "Lava";
        protected override string AutoNote => "On a held pool - let it set; elsewhere - pour shallow lava for seven and a half days (the order renews itself while a caster keeps it).";
        protected override IEnumerable<OrderWay> MakeWays() => new[]
        {
            new OrderWay { key = "Avir_2LavaPool", label = "Pour a pool", note = "Lava turns the ground into shallow lava for seven and a half days - heat for a forge, a trap at the gate. Renewed while a caster keeps it.", proto = new Designator_2LavaPool() },
            new OrderWay { key = "Avir_2LavaSet", label = "Let it set", note = "A held pool is let go: it sinks into magma and sets into stone in its own time.", proto = new Designator_2LavaSet() },
        };
        protected override OrderWay Auto(IntVec3 c) => LavaPools.Held(Map, c) ? (Takes(Way("Avir_2LavaSet"), c) ? Way("Avir_2LavaSet") : null) : Takes(Way("Avir_2LavaPool"), c) ? Way("Avir_2LavaPool") : null;
    }

    /// <summary>Shining water: make water shine for good, or fill flasks at it.</summary>
    public class Designator_OrderGlowWater : Designator_ArcaneGroup
    {
        public Designator_OrderGlowWater() { defaultLabel = "Shining water"; icon = ArcaneIcons.For("Avir_GlowWaterStill", "UI/Designators/SmoothSurface"); }
        protected override string Name => "Shining water";
        protected override string AutoNote => "On still shining water - fill flasks of it; on other water - the Mirror makes it shine for good.";
        protected override IEnumerable<OrderWay> MakeWays() => new[]
        {
            new OrderWay { key = "Avir_GlowWaterStill", label = "Make it shine", note = "The Mirror (Light and Water) makes water shine for good - a lamp with no power, a reflector, a well for flasks.", proto = new Designator_GlowWaterStill() },
            new OrderWay { key = "Avir_GlowFlask", label = "Fill flasks", note = "The Mirror fills " + MirrorWorks.FlasksPerWork + " flasks at still shining water.", proto = new Designator_GlowFlask() },
        };
        protected override OrderWay Auto(IntVec3 c) => Takes(Way("Avir_GlowFlask"), c) ? Way("Avir_GlowFlask") : Takes(Way("Avir_GlowWaterStill"), c) ? Way("Avir_GlowWaterStill") : null;
    }
}
