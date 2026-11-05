using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // ARTIFACT BUILDINGS - the frame (see Строения-концепт.md).
    // A building is raised empty; artifacts (loose artifact items with a heart) are inlaid into its
    // sockets afterwards through its own menu, by the attuner work. The stones' metals and quality
    // decide how well it works; they run on the artifacts' charge, which buildings spend far more
    // slowly than worn artifacts; an attuner (psychic sensitivity) tops it up. When the building
    // is destroyed or deconstructed its artifacts drop out whole.
    // =====================================================================================

    public class CompProperties_ArtifactHost : CompProperties
    {
        public int sockets = 1;
        /// <summary>Where the sockets sit on the sprite, in cells from the centre (for the gem glow).</summary>
        public List<Vector2> socketOffsets = new List<Vector2>();
        /// <summary>Diameter (cells) of the hollow a stone sits in on the sprite.</summary>
        public float socketSize = 0.22f;
        /// <summary>False when the sprite shows the stones itself (the observer's eyes).</summary>
        public bool drawGems = true;
        /// <summary>Charge units a working socket spends per day just by being on.</summary>
        public float drainPerDay = 2f;
        /// <summary>For the artificer's Buildings tab: what it does, by sockets.</summary>
        [MustTranslate] public string effectSummary;
        /// <summary>Era label for the tab: "basic artifactorics", "Apogee".</summary>
        [MustTranslate] public string era;
        /// <summary>Inlay requirements (the essentium projector): lowest heart quality, allowed rims and hearts (empty - any).</summary>
        public QualityCategory minHeartQuality = QualityCategory.Awful;
        public List<ArtifactMetalDef> allowedRims = new List<ArtifactMetalDef>();
        public List<ArtifactMetalDef> allowedHearts = new List<ArtifactMetalDef>();

        public CompProperties_ArtifactHost() { compClass = typeof(CompArtifactHost); }
    }

    /// <summary>A building effect that reads its host; its line goes into the inlay menu.</summary>
    public interface IArtifactHostEffect
    {
        string EffectLine();
    }

    /// <summary>A building whose sockets take only certain stones (the projector's patterns): what does not work does not go in.</summary>
    public interface IArtifactSocketRule
    {
        bool SocketAccepts(int socket, ArtifactMetalDef rim, ArtifactMetalDef heart);
    }

    public class CompArtifactHost : ThingComp
    {
        public List<ArtifactState> slots = new List<ArtifactState>();
        /// <summary>A constellation's view of its stones for one function: its own quality factor.</summary>
        [Unsaved] public float? qualityOverride;
        /// <summary>Per socket: the artifact item waiting to be inlaid there.</summary>
        public List<Thing> pendingInlay = new List<Thing>();
        /// <summary>Per socket: the artifact waits to be taken out.</summary>
        public List<bool> pendingExtract = new List<bool>();

        public CompProperties_ArtifactHost Props => (CompProperties_ArtifactHost)props;
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        /// <summary>Player buildings with sockets on every map (the attuner work shows once there is one).</summary>
        public static readonly HashSet<CompArtifactHost> All = new HashSet<CompArtifactHost>();
        public static bool AnyPlayer => All.Count > 0;

        public void EnsureSize()
        {
            while (slots.Count < Props.sockets) slots.Add(null);
            while (pendingInlay.Count < Props.sockets) pendingInlay.Add(null);
            while (pendingExtract.Count < Props.sockets) pendingExtract.Add(false);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureSize();
            if (parent.Faction == Faction.OfPlayer) All.Add(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            All.Remove(this);
        }

        // ---------------------------------------------------------------- state

        public bool Has(int i) => i >= 0 && i < slots.Count && slots[i] != null;
        public bool Works(int i) => Has(i) && slots[i].HasHeart && slots[i].charge > 0f;
        public int Filled => slots.Count(s => s != null);
        public int Working { get { int n = 0; for (int i = 0; i < slots.Count; i++) if (Works(i)) n++; return n; } }

        public static float Capacity(ArtifactState st) => ArtifactCharge.Capacity(st, null);

        /// <summary>One stone's power: heart quality and both metals.</summary>
        public static float StonePower(ArtifactState st)
        {
            if (st == null || S == null) return 0f;
            float metal = ((st.heartMetal?.buildingPower ?? 1f) * 2f + (st.rimMetal?.buildingPower ?? 1f)) / 3f;
            return S.HeartQualityFactor(st.heartQuality) * metal;
        }

        /// <summary>The building's own quality (its builder's skill): Awful x0.8 ... Legendary x1.3.</summary>
        public float BuildingQualityFactor
        {
            get
            {
                if (!parent.TryGetQuality(out QualityCategory q)) return 1f;
                List<float> l = S?.buildingQualityFactors;
                int i = (int)q;
                return l != null && i >= 0 && i < l.Count ? l[i] : 1f;
            }
        }

        public static float StonePowerOf(Thing t) => StonePower(t?.TryGetComp<CompArtifact>()?.state);

        /// <summary>Primitive human work: no essentium or tisserium heart fits it.</summary>
        public bool IsPrimitive => Props.era == "basic artifactorics";
        /// <summary>The middle works (human, finer): essentium fits, tisserium does not.</summary>
        public bool IsMid => Props.era == "mid";

        /// <summary>A heart this building cannot hold: primitive work - neither essentium nor tisserium; the middle works - no tisserium.</summary>
        public bool Refuses(ArtifactState st) =>
            (IsPrimitive && EssentialHeart(st)) || (IsMid && st?.heartMetal?.defName == "ArtifactMetal_Tisserium");

        /// <summary>An Avir building (Apogee, the round table, ritualistics, constellations).</summary>
        public bool IsAvir => parent.TryGetComp<CompConstellation>() != null
                              || Props.era == "Apogee" || Props.era == "Avir artifactorics" || Props.era == "ritualistics";

        public static bool EssentialHeart(ArtifactState st) =>
            st?.heartMetal != null && (st.heartMetal.defName == "ArtifactMetal_Essentium" || st.heartMetal.defName == "ArtifactMetal_Tisserium");

        /// <summary>Avir artifacts work in full only on essentium (or tisserium) hearts; other hearts give half.</summary>
        public float StoneFactor(ArtifactState st) => (IsAvir && !EssentialHeart(st) ? 0.5f : 1f) * (Feathered ? FeatherConduction : 1f);

        // ---- Avir feathers: for primitive human work they are a strong conductor ----
        public const float FeatherCharge = 5f, FeatherConduction = 1.25f;
        private CompRefuelable Feathers => parent.TryGetComp<CompRefuelable>();

        /// <summary>A primitive building with Avir feathers in it: its stones work 25% stronger.</summary>
        public bool Feathered => IsPrimitive && Feathers != null && Feathers.Fuel >= 1f;

        /// <summary>One feather at a time tops up the emptiest stone.</summary>
        private void FeedFeathers()
        {
            CompRefuelable f = Feathers;
            if (!IsPrimitive || f == null || f.Fuel < 1f) return;
            int low = -1; float lowest = 0.95f;
            for (int i = 0; i < slots.Count; i++)
                if (Has(i) && slots[i].charge < lowest) { lowest = slots[i].charge; low = i; }
            if (low < 0) return;
            f.ConsumeFuel(1f);
            slots[low].charge = Mathf.Min(1f, slots[low].charge + FeatherCharge / Mathf.Max(1f, Capacity(slots[low])));
        }

        /// <summary>Average power of the working stones (0 = none works).</summary>
        public float Power
        {
            get
            {
                float sum = 0f; int n = 0;
                for (int i = 0; i < slots.Count; i++) if (Works(i)) { sum += StonePower(slots[i]) * StoneFactor(slots[i]); n++; }
                return n == 0 ? 0f : sum / n * (qualityOverride ?? BuildingQualityFactor);
            }
        }

        /// <summary>Best heart quality among the working stones (ranges grow with it).</summary>
        public QualityCategory BestQuality
        {
            get
            {
                QualityCategory q = QualityCategory.Awful;
                for (int i = 0; i < slots.Count; i++) if (Works(i) && slots[i].heartQuality > q) q = slots[i].heartQuality;
                return q;
            }
        }

        /// <summary>Experience a stone gathers by working in a building: per whole charge spent on its work
        /// (100 for every tenth of the charge). Standing idle drains it too, but teaches nothing.</summary>
        public const float XpPerCharge = 1000f;

        /// <summary>Spends charge units, shared over the working stones. False when there is none. The work teaches
        /// the stones: they gain experience by the share of their charge it took.</summary>
        public bool Spend(float units)
        {
            int n = Working;
            if (n == 0) return false;
            float each = units / n;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!Works(i)) continue;
                ArtifactState st = slots[i];
                float before = st.charge;
                st.charge = Mathf.Max(0f, st.charge - each / Mathf.Max(1f, Capacity(st)));
                if (before > st.charge) st.AddXp((before - st.charge) * XpPerCharge);
            }
            return true;
        }

        public float LowestCharge
        {
            get
            {
                float low = 1f; bool any = false;
                for (int i = 0; i < slots.Count; i++) if (Has(i)) { any = true; low = Mathf.Min(low, slots[i].charge); }
                return any ? low : 1f;
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (parent.Faction == Faction.OfPlayer) All.Add(this); else All.Remove(this);   // claimed or lost
            FeedFeathers();
            // standing on costs a little; buildings spend far more slowly than worn artifacts
            float units = Props.drainPerDay * 250f / 60000f;
            for (int i = 0; i < slots.Count; i++)
                if (Works(i)) slots[i].charge = Mathf.Max(0f, slots[i].charge - units / Mathf.Max(1f, Capacity(slots[i])));
            // an ordered artifact that is gone (used elsewhere, destroyed) - the order goes
            for (int i = 0; i < pendingInlay.Count; i++)
                if (pendingInlay[i] != null && (pendingInlay[i].Destroyed || Has(i))) pendingInlay[i] = null;
        }

        // ---------------------------------------------------------------- inlay / extract (by the jobs)

        public void Inlay(int i, Thing item)
        {
            CompArtifact ca = item?.TryGetComp<CompArtifact>();
            if (ca == null || Has(i)) return;
            EnsureSize();
            slots[i] = ca.state.Clone();
            pendingInlay[i] = null;
            if (item.Spawned) item.DeSpawn();
            item.Destroy();
            FleckMaker.ThrowLightningGlow(parent.DrawPos, parent.Map, 1f);
        }

        public Thing Extract(int i)
        {
            if (!Has(i)) return null;
            Thing item = MakeItem(slots[i]);
            slots[i] = null;
            pendingExtract[i] = false;
            return item;
        }

        public static Thing MakeItem(ArtifactState st)
        {
            Thing item = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_Artifact);
            CompArtifact ca = item.TryGetComp<CompArtifact>();
            if (ca != null) ca.state = st;
            return item;
        }

        /// <summary>Destroyed, deconstructed, burnt: the artifacts come out whole.</summary>
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            All.Remove(this);
            if (previousMap == null || mode == DestroyMode.WillReplace) return;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null) continue;
                GenPlace.TryPlaceThing(MakeItem(slots[i]), parent.Position, previousMap, ThingPlaceMode.Near);
                slots[i] = null;
            }
        }

        // ---------------------------------------------------------------- UI

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Artifacts...",
                defaultDesc = "Inlay artifacts into this building, take them out, see their charge.",
                icon = ArtifactoricsTex.IconCreate,
                action = () => Find.WindowStack.Add(new Dialog_ArtifactHost(this))
            };
            if (Prefs.DevMode && Props.drawGems && Props.sockets > 0)
                yield return new Command_Action
                {
                    defaultLabel = "Dev: tune socket gems",
                    defaultDesc = "Move the gems drawn on this building's sockets until they sit right; copy the result into the def's socketOffsets.",
                    action = () => Find.WindowStack.Add(new Dialog_TuneSockets(this))
                };
            if (Prefs.DevMode)
                yield return new Command_Action
                {
                    defaultLabel = "Dev: fill charge",
                    action = () => { foreach (ArtifactState st in slots) if (st != null) st.charge = 1f; }
                };
            if (Prefs.DevMode)
                yield return new Command_Action
                {
                    defaultLabel = "Dev: inlay random",
                    action = () =>
                    {
                        EnsureSize();
                        for (int i = 0; i < slots.Count; i++)
                        {
                            if (slots[i] != null) continue;
                            // only stones that work here: allowed metals, quality, the socket's own pattern
                            for (int tries = 0; tries < 200 && slots[i] == null; tries++)
                            {
                                ArtifactMetalDef rim = ArtifactMetalChoice.Rims.RandomElementWithFallback();
                                ArtifactMetalDef heart = ArtifactMetalChoice.HeartsFor(rim).RandomElementWithFallback();
                                if (rim == null || heart == null) continue;
                                QualityCategory q = QualityUtility.AllQualityCategories.Where(c => c >= Props.minHeartQuality).RandomElement();
                                ArtifactState st = ArtifactMetalChoice.NewArtifact(heart, rim, q);
                                if (!Fits(i, st)) continue;
                                st.charge = 1f;
                                slots[i] = st;
                            }
                        }
                    }
                };
        }

        public override string CompInspectStringExtra()
        {
            EnsureSize();
            var sb = new StringBuilder();
            sb.Append("Artifacts: ").Append(Filled).Append(" / ").Append(Props.sockets);
            if (Working > 0) sb.Append(", power x").Append(Power.ToString("0.00"));
            if (Filled > 0) sb.Append(", charge ").Append(LowestCharge.ToStringPercent()).Append(Working == 0 ? " - empty, asleep" : "");
            if (pendingInlay.Any(t => t != null)) sb.Append("\nWaiting for an artifact to be inlaid.");
            if (pendingExtract.Any(b => b)) sb.Append("\nAn artifact waits to be taken out.");
            return sb.ToString();
        }

        public override void PostDraw()
        {
            base.PostDraw();
            EnsureSize();
            if (!Props.drawGems) return;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!Has(i)) continue;
                Vector2 off = i < Props.socketOffsets.Count ? Props.socketOffsets[i] : Vector2.zero;
                // above the building's tilted sprite (SocketDraw.Lift), below anything drawn over it (a turret's head sits one altitude step up)
                Vector3 pos = parent.DrawPos + new Vector3(off.x, SocketDraw.Lift, off.y);
                ArtifactMetalDef heart = slots[i].heartMetal;
                float now = Time.realtimeSinceStartup;
                Color c = heart?.GemColorAt(now + i * 0.37f) ?? Color.white;
                bool live = slots[i].charge > 0f;
                float size = Props.socketSize;
                // no stone is drawn: the dark hollow itself fills with the stone's energy
                Color fill = live ? c : Color.Lerp(c, Color.gray, 0.65f);
                fill.a = live ? 0.95f : 0.6f;
                SocketDraw.Quad(pos, size, 0f, SocketDraw.Energy, fill);
                if (!live) continue;
                if (heart != null && heart.Shimmers)
                {
                    // essentium, tisserium: the energy shifts and swirls
                    Color w = heart.GemColorAt(now * 1.6f + 0.5f + i * 0.37f); w.a = 0.55f;
                    SocketDraw.Quad(pos + new Vector3(0f, 0.003f, 0f), size * 0.95f, now * 40f + i * 47f, SocketDraw.Shimmer, w);
                    Color w2 = Color.Lerp(heart.GemColorAt(now * 0.7f + i), Color.white, 0.35f); w2.a = 0.35f;
                    SocketDraw.Quad(pos + new Vector3(0f, 0.006f, 0f), size * 0.8f, -now * 65f + i * 91f, SocketDraw.Shimmer, w2);
                }
                else
                {
                    // other stones: a quiet pulse of their own light
                    Color w = Color.Lerp(c, Color.white, 0.3f); w.a = 0.25f + 0.15f * Mathf.Sin(now * 2.2f + i);
                    SocketDraw.Quad(pos + new Vector3(0f, 0.003f, 0f), size * 0.7f, 0f, SocketDraw.Energy, w);
                }
                // a soft halo just past the rim
                c.a = 0.22f + 0.06f * Mathf.Sin(now * 1.7f + i);
                SocketDraw.Quad(pos + new Vector3(0f, 0.009f, 0f), size * 1.3f, 0f, SocketDraw.Halo, c);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref slots, "slots", LookMode.Deep);
            Scribe_Collections.Look(ref pendingInlay, "pendingInlay", LookMode.Reference);
            Scribe_Collections.Look(ref pendingExtract, "pendingExtract", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (slots == null) slots = new List<ArtifactState>();
                if (pendingInlay == null) pendingInlay = new List<Thing>();
                if (pendingExtract == null) pendingExtract = new List<bool>();
                EnsureSize();
            }
        }

        /// <summary>Would a stone of these metals and quality work in this socket (-1: any socket)?</summary>
        public bool Fits(int socket, ArtifactState st)
        {
            if (st == null || st.heartMetal == null || st.rimMetal == null) return false;
            if (st.heartQuality < Props.minHeartQuality) return false;
            if (Refuses(st)) return false;   // human work cannot hold an Avir heart
            if (Props.allowedRims.Count > 0 && !Props.allowedRims.Contains(st.rimMetal)) return false;
            if (Props.allowedHearts.Count > 0 && !Props.allowedHearts.Contains(st.heartMetal)) return false;
            if (socket >= 0)
                foreach (ThingComp c in parent.AllComps)
                    if (c is IArtifactSocketRule rule && !rule.SocketAccepts(socket, st.rimMetal, st.heartMetal)) return false;
            return true;
        }

        /// <summary>Loose artifacts with a heart that could go in here (on this map, not forbidden), for one socket or any.</summary>
        public IEnumerable<Thing> Candidates(int socket = -1)
        {
            Map map = parent.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsOfDef(ArtifactoricsDefOf.Avir_Artifact))
            {
                CompArtifact ca = t.TryGetComp<CompArtifact>();
                if (ca == null || !ca.HasHeart || ca.rimMetal == null || t.IsForbidden(Faction.OfPlayer)) continue;
                if (All.Any(h => h.pendingInlay.Contains(t))) continue;
                if (!Fits(socket, ca.state)) continue;
                yield return t;
            }
        }
    }

    /// <summary>Draws the energy in a socket: fixed materials, the colour goes through a property block
    /// (a new material per colour every frame would pile up).</summary>
    [StaticConstructorOnStartup]
    public static class SocketDraw
    {
        /// <summary>How far above the building the glow is drawn. A sprite is printed with its TOP edge raised by 0.01
        /// (Printer_Plane's topVerticesAltitudeBias), so its plane is tilted; a quad drawn lower than that is hidden by
        /// the sprite wherever the sprite has risen past it - the glow was cut in half on a line across the socket.
        /// 0.02 clears the tilt; the layers of one glow add up to 0.029, below the next altitude layer (~0.038).</summary>
        public const float Lift = 0.02f;
        public static readonly Material Energy = MaterialPool.MatFrom("Things/Building/Artifactorics/SocketEnergy", ShaderDatabase.Transparent);
        public static readonly Material Shimmer = MaterialPool.MatFrom("Things/Building/Artifactorics/SocketShimmer", ShaderDatabase.MoteGlow);
        public static readonly Material Halo = MaterialPool.MatFrom(ArtifactoricsTex.Glow, ShaderDatabase.MoteGlow, Color.white);
        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static void Quad(Vector3 pos, float size, float angle, Material mat, Color col)
        {
            block.SetColor(ColorId, col);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(size, 1f, size)), mat, 0, null, 0, block);
        }
    }

    // ---------------------------------------------------------------- the inlay menu

    /// <summary>The inlay window: the building's sockets laid out as on the building - one in the middle,
    /// two side by side, three on an arc - joined by a setting line; the chosen socket's stone below with
    /// its buttons, and what the building does now and what being inlaid means for the artifacts.</summary>
    public class Dialog_ArtifactHost : Window
    {
        private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        private static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);
        private static readonly Color Gold = new Color(0.70f, 0.50f, 0.12f);
        private readonly CompArtifactHost host;
        private int chosen;
        private Vector2 scroll;

        public Dialog_ArtifactHost(CompArtifactHost host)
        {
            this.host = host;
            doWindowBackground = false;
            drawShadow = true;
            doCloseX = true;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = true;
            preventCameraMotion = false;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(860f, Many ? 760f : 640f);

        private int N => host.Props.sockets;
        /// <summary>Many sockets (the cradle's fifteen): a pattern of two rings, not an arc.</summary>
        private bool Many => N >= 9;
        private int Inner => Mathf.Max(3, N / 3);
        private float StageHeight => Many ? 340f : 230f;

        /// <summary>Socket centres in the stage: one centred, two side by side, three or more on an upward arc.</summary>
        private Vector2[] Layout(Rect stage)
        {
            var pts = new Vector2[N];
            Vector2 c = stage.center;
            if (Many)
            {
                // a celestial pattern: an inner ring and an outer ring, the outer turned half a step
                float rOut = Mathf.Min(stage.height * 0.45f, 150f), rIn = rOut * 0.48f;
                int inner = Inner, outer = N - inner;
                for (int i = 0; i < inner; i++)
                {
                    float a = (-90f + 360f * i / inner) * Mathf.Deg2Rad;
                    pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rIn;
                }
                for (int i = 0; i < outer; i++)
                {
                    float a = (-90f + 360f * (i + 0.5f) / outer) * Mathf.Deg2Rad;
                    pts[inner + i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rOut;
                }
                return pts;
            }
            if (N == 1) pts[0] = c;
            else if (N == 2) { pts[0] = c + new Vector2(-150f, 0f); pts[1] = c + new Vector2(150f, 0f); }
            else
            {
                float rad = Mathf.Min(stage.width * 0.42f, 230f), span = Mathf.Min(150f, 50f * (N - 1));
                Vector2 o = new Vector2(c.x, stage.y + stage.height * 0.18f + rad);
                for (int i = 0; i < N; i++)
                {
                    float ang = (-90f - span / 2f + span * i / (N - 1)) * Mathf.Deg2Rad;
                    pts[i] = o + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                }
            }
            return pts;
        }

        public override void DoWindowContents(Rect box)
        {
            host.EnsureSize();
            chosen = Mathf.Clamp(chosen, 0, N - 1);
            Rect r = Avir.Ritualistics.ParchmentFrame.Draw(box).ContractedBy(6f);
            GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), host.parent.LabelCap);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;

            // the stage: the sockets as they sit on the building
            Rect stage = new Rect(r.x, r.y + 36f, r.width, StageHeight);
            Vector2[] pts = Layout(stage);
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.75f);
            if (Many)
            {
                // the pattern's lines: each ring closed, and every inner stone to the outer stones beside it
                int inner = Inner, outer = N - inner;
                for (int i = 0; i < inner; i++) AvirLine.Draw(pts[i], pts[(i + 1) % inner], GUI.color, 2f);
                for (int i = 0; i < outer; i++) AvirLine.Draw(pts[inner + i], pts[inner + (i + 1) % outer], GUI.color, 2f);
                for (int i = 0; i < outer; i++)
                {
                    int to = Mathf.Clamp(Mathf.FloorToInt((i + 0.5f) * inner / outer), 0, inner - 1);
                    AvirLine.Draw(pts[inner + i], pts[to], new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.45f), 1.5f);
                }
            }
            else
                for (int i = 0; i + 1 < N; i++) Setting(pts[i], pts[i + 1], N >= 3 ? 22f : 34f);
            for (int i = 0; i < N; i++) Socket(pts[i], i);

            // below: the chosen socket and what it all means
            float y = stage.yMax + 10f, half = (r.width - 16f) / 2f;
            Rect leftBox = new Rect(r.x, y, half, r.yMax - y);
            Rect rightBox = new Rect(r.x + half + 16f, y, half, r.yMax - y);
            Panel(leftBox); Panel(rightBox);
            SocketDetails(leftBox.ContractedBy(10f), chosen);
            Meaning(rightBox.ContractedBy(10f));
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
        }

        private static void Panel(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(1f, 0.92f, 0.7f, 0.18f));
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
            Widgets.DrawBox(r, 1);
        }

        /// <summary>The metal setting between two sockets: a sagging line of beads.</summary>
        private static void Setting(Vector2 a, Vector2 b, float sag)
        {
            Vector2 m = (a + b) / 2f + new Vector2(0f, sag);
            Vector2 prev = a;
            for (int k = 1; k <= 24; k++)
            {
                float t = k / 24f;
                Vector2 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * m + t * t * b;
                AvirLine.Draw(prev, p, GUI.color, 3f);
                prev = p;
            }
        }

        private void Socket(Vector2 c, int i)
        {
            ArtifactState st = host.slots[i];
            Thing pending = host.pendingInlay[i];
            float size = Many ? 54f : N >= 3 ? 104f : 128f;
            Rect icon = new Rect(c.x - size / 2f, c.y - size / 2f, size, size);
            if (i == chosen)
            {
                GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
                GUI.DrawTexture(icon.ExpandedBy(10f), ArtifactoricsTex.Disc, ScaleMode.StretchToFill);
            }
            Texture2D stone = st != null ? (st.charge > 0f ? ArtifactoricsTex.StoneOpened : ArtifactoricsTex.StoneLocked)
                : pending != null ? ArtifactoricsTex.StoneAvailable : ArtifactoricsTex.StoneLocked;
            GUI.color = Color.white;
            if (stone != null) GUI.DrawTexture(icon, stone, ScaleMode.ScaleToFit);
            // the artifact itself sits in the socket (dimmed while it has no charge)
            ArtifactState shown = st ?? pending?.TryGetComp<CompArtifact>()?.state;
            Texture2D art = shown != null ? Thing_Artifact.IconFor(shown) : null;
            if (art != null)
            {
                GUI.color = st == null ? new Color(1f, 1f, 1f, 0.45f) : st.charge > 0f ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
                GUI.DrawTexture(icon.ContractedBy(size * 0.12f), art, ScaleMode.ScaleToFit);
            }
            else if (st?.heartMetal != null)
            {
                GUI.color = st.heartMetal.GemColorAt(Time.realtimeSinceStartup);
                GUI.DrawTexture(icon.ContractedBy(size * 0.3f), ArtifactoricsTex.Disc, ScaleMode.StretchToFill);
            }
            GUI.color = Color.white;
            // charge as an arc of light under the socket
            if (st != null)
            {
                Rect bar = new Rect(icon.x + 12f, icon.yMax + 2f, icon.width - 24f, 5f);
                Widgets.DrawBoxSolid(bar, new Color(0.25f, 0.17f, 0.08f, 0.35f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * st.charge, bar.height), new Color(0.35f, 0.45f, 0.95f, 0.9f));
            }
            GUI.color = Ink; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperCenter;
            string tag = st != null ? (st.heartMetal?.label ?? "?") + " · x" + CompArtifactHost.StonePower(st).ToString("0.00")
                : pending != null ? "waiting" : "empty";
            if (Many) ArtInk.Label(new Rect(c.x - 20f, icon.yMax - 2f, 40f, 18f), (i + 1).ToString());
            else ArtInk.Label(new Rect(c.x - 90f, icon.yMax + 9f, 180f, 18f), (i + 1) + ". " + tag);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(icon)) TooltipHandler.TipRegion(icon, "Socket " + (i + 1) + " - click to choose it");
            if (Widgets.ButtonInvisible(icon)) { chosen = i; Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Tick_High); }
        }

        private void SocketDetails(Rect r, int i)
        {
            ArtifactState st = host.slots[i];
            Thing pending = host.pendingInlay[i];
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 30f), "Socket " + (i + 1));
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            string body;
            if (st != null)
                body = (st.heartMetal?.LabelCap ?? "?") + " heart, " + (st.rimMetal?.LabelCap ?? "?") + " rim, " + st.heartQuality.GetLabel() + "\n"
                       + "Power x" + CompArtifactHost.StonePower(st).ToString("0.00") + " (heart quality and both metals)\n"
                       + "Charge " + st.charge.ToStringPercent() + " of " + CompArtifactHost.Capacity(st).ToString("0")
                       + "\n" + XpLine(st)
                       + (st.charge <= 0f ? "\nEmpty: this stone does nothing until an attuner charges it." : "")
                       + (host.pendingExtract[i] ? "\nWaiting to be taken out." : "");
            else if (pending != null) body = "An attuner will bring " + pending.LabelNoCount + " and inlay it here.";
            else body = "Empty. Any artifact with a heart fits; its heart quality and metals decide how strongly the building works.";
            CompConstellation cn = host.parent.TryGetComp<CompConstellation>();
            if (cn != null) body = "Function: " + cn.FunctionLabel(i) + "\n" + body;
            if (host.IsAvir) body += st != null && !CompArtifactHost.EssentialHeart(st)
                ? "\nAn Avir artifact: without an essentium (or tisserium) heart this stone works at half strength."
                : st == null ? "\nAn Avir artifact: it works in full only on essentium or tisserium hearts." : "";
            ArtInk.Label(new Rect(r.x, r.y + 34f, r.width, r.height - 80f), body);
            Rect btn = new Rect(r.x, r.yMax - 38f, Mathf.Min(220f, r.width), 36f);
            if (st != null)
            {
                if (Button(btn, host.pendingExtract[i] ? "Keep it" : "Take out")) host.pendingExtract[i] = !host.pendingExtract[i];
            }
            else if (pending != null)
            {
                if (Button(btn, "Cancel")) host.pendingInlay[i] = null;
            }
            else if (Button(btn, "Inlay..."))
            {
                var opts = host.Candidates(i)
                    .OrderByDescending(CompArtifactHost.StonePowerOf)
                    .Select(t =>
                    {
                        CompArtifact ca = t.TryGetComp<CompArtifact>();
                        string label = ca.heartMetal.LabelCap + " heart, " + ca.rimMetal.LabelCap + " rim, " + ca.heartQuality.GetLabel()
                                       + " (power x" + CompArtifactHost.StonePower(ca.state).ToString("0.00") + ")";
                        return new FloatMenuOption(label, () => host.pendingInlay[i] = t);
                    }).ToList();
                if (opts.Count == 0) opts.Add(new FloatMenuOption(Needs() ?? "No loose artifact with a heart on this map", null));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        /// <summary>What the stone has learnt in this building: points earned, free to spend, and the way to the next.</summary>
        private static string XpLine(ArtifactState st)
        {
            ArtifactoricsSettingsDef S = ArtifactoricsSettingsDef.Main;
            if (S == null) return "";
            int earned = st.PointsEarned;
            float have = st.xpEarned - S.XpForPoints(earned), need = S.XpForPoint(earned + 1);
            return "Experience " + st.xpEarned.ToString("N0") + ": " + earned + " point" + (earned == 1 ? "" : "s")
                   + " (" + st.PointsAvailable + " free), next in " + Mathf.Max(0f, need - have).ToString("N0")
                   + ". Every charge it spends on work teaches it.";
        }

        /// <summary>The building's inlay requirements in words, or null.</summary>
        private string Needs()
        {
            var P = host.Props;
            var parts = new List<string>();
            if (P.minHeartQuality > QualityCategory.Awful) parts.Add(P.minHeartQuality.GetLabel() + " heart or better");
            if (host.IsPrimitive) parts.Add("a heart that is not essentium or tisserium (human work cannot hold them)");
            if (host.IsMid) parts.Add("a heart that is not tisserium (this work cannot hold it)");
            if (P.allowedHearts.Count > 0) parts.Add(string.Join(" or ", P.allowedHearts.Select(m => m.label)) + " heart");
            if (P.allowedRims.Count > 0) parts.Add(string.Join(" or ", P.allowedRims.Select(m => m.label)) + " rim");
            return parts.Count == 0 ? null : "No fitting artifact: needs " + string.Join(", ", parts);
        }

        private void Meaning(Rect r)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 30f), "What it does");
            Text.Font = GameFont.Small;
            var sb = new System.Text.StringBuilder();
            CompConstellation cons = host.parent.TryGetComp<CompConstellation>();
            string effect = string.Join("\n", host.parent.AllComps.OfType<IArtifactHostEffect>()
                .Where(e => cons == null || !(e is CompArtifactTotem t) || cons.Assigned(t))
                .Select(e => e.EffectLine()).Where(x => !x.NullOrEmpty()));
            if (cons != null && effect.NullOrEmpty()) effect = "No function chosen yet: open Functions... to give each socket a function.";
            if (effect.NullOrEmpty()) effect = host.Props.effectSummary ?? "";
            sb.AppendLine(effect);
            sb.AppendLine();
            sb.AppendLine("Working stones: " + host.Working + " of " + N + ". Power x" + host.Power.ToString("0.00")
                          + " (their average, x" + host.BuildingQualityFactor.ToString("0.00") + " for the building's quality).");
            sb.AppendLine();
            sb.AppendLine("TO THE ARTIFACTS INSIDE");
            sb.AppendLine("• Each working stone spends " + host.Props.drainPerDay.ToString("0.#") + " charge a day just by being on, and more whenever the building works.");
            sb.AppendLine("• An attuner tops the charge up (Attuning work); an empty stone simply rests.");
            sb.AppendLine("• The artifact is not worn down and keeps its paths and experience.");
            sb.AppendLine("• Taken out - or the building destroyed or deconstructed - it comes out whole.");
            if (host.IsPrimitive) sb.AppendLine("• Avir feathers loaded into it (haulers bring them) top its stones up, " + CompArtifactHost.FeatherCharge + " charge a feather, and while it holds feathers its stones work 25% stronger. No essentium or tisserium heart fits human work.");
            string text = sb.ToString();
            GUI.color = InkSoft;
            Rect area = new Rect(r.x, r.y + 34f, r.width, r.height - 34f);
            float th = Text.CalcHeight(text, area.width - 18f);
            Rect view = new Rect(0f, 0f, area.width - 18f, th);
            Widgets.BeginScrollView(area, ref scroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
        }

        private static bool Button(Rect r, string label)
        {
            bool over = Mouse.IsOver(r);
            Texture2D tex = !over ? ArtifactoricsTex.ButtonNormal : Input.GetMouseButton(0) ? ArtifactoricsTex.ButtonPressed : ArtifactoricsTex.ButtonHover;
            GUI.color = Color.white;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = Ink;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(r)) { Verse.Sound.SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click); return true; }
            return false;
        }
    }

    // ---------------------------------------------------------------- the attuner's work

    [DefOf]
    public static class ArtifactBuildingDefOf
    {
        public static WorkTypeDef Avir_Attuning;
        public static JobDef Avir_InlayIntoBuilding;
        public static JobDef Avir_ExtractFromBuilding;
        public static JobDef Avir_RechargeBuilding;
        static ArtifactBuildingDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactBuildingDefOf)); }
    }

    public abstract class WorkGiver_ArtifactHost : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            CompArtifactHost.All.Where(h => h.parent.Map == pawn.Map).Select(h => (Thing)h.parent);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !CompArtifactHost.AnyPlayer;

        protected static CompArtifactHost Host(Thing t) => t.TryGetComp<CompArtifactHost>();
    }

    public class WorkGiver_InlayBuilding : WorkGiver_ArtifactHost
    {
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) => Next(pawn, t, forced, out _) >= 0;

        private static int Next(Pawn pawn, Thing t, bool forced, out Thing item)
        {
            item = null;
            CompArtifactHost h = Host(t);
            if (h == null || t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced)) return -1;
            for (int i = 0; i < h.pendingInlay.Count; i++)
            {
                Thing it = h.pendingInlay[i];
                if (it == null || h.Has(i) || !it.Spawned || it.IsForbidden(pawn) || !pawn.CanReserveAndReach(it, PathEndMode.ClosestTouch, Danger.Deadly, 1, -1, null, forced)) continue;
                item = it;
                return i;
            }
            return -1;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (Next(pawn, t, forced, out Thing item) < 0) return null;
            Job job = JobMaker.MakeJob(ArtifactBuildingDefOf.Avir_InlayIntoBuilding, t, item);
            job.count = 1;
            return job;
        }
    }

    public class WorkGiver_ExtractFromBuilding : WorkGiver_ArtifactHost
    {
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompArtifactHost h = Host(t);
            return h != null && h.pendingExtract.Any(b => b) && !t.IsForbidden(pawn) && pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            JobMaker.MakeJob(ArtifactBuildingDefOf.Avir_ExtractFromBuilding, t);
    }

    public class WorkGiver_RechargeBuilding : WorkGiver_ArtifactHost
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompArtifactHost h = Host(t);
            if (h == null || h.Filled == 0 || t.IsForbidden(pawn)) return false;
            if (ArtifactFocus.Sensitivity(pawn) < (S?.buildingAttunerMinSensitivity ?? 0.5f)) return false;
            if (h.LowestCharge >= (forced ? 0.99f : S?.buildingRechargeBelow ?? 0.5f)) return false;
            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            JobMaker.MakeJob(ArtifactBuildingDefOf.Avir_RechargeBuilding, t);
    }

    public class JobDriver_InlayIntoBuilding : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed) && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil work = Toils_General.Wait(400, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return work;
            yield return Toils_General.Do(() =>
            {
                CompArtifactHost h = job.targetA.Thing.TryGetComp<CompArtifactHost>();
                Thing item = pawn.carryTracker.CarriedThing;
                if (h == null || item == null) return;
                int i = h.pendingInlay.IndexOf(item);
                if (i < 0) i = h.slots.IndexOf(null);
                if (i < 0) return;
                pawn.carryTracker.innerContainer.Remove(item);
                h.Inlay(i, item);
                Messages.Message(pawn.LabelShort + " inlaid an artifact into " + h.parent.LabelNoCount + ".", h.parent, MessageTypeDefOf.PositiveEvent, false);
            });
        }
    }

    public class JobDriver_ExtractFromBuilding : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(400, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                CompArtifactHost h = job.targetA.Thing.TryGetComp<CompArtifactHost>();
                if (h == null) return;
                for (int i = 0; i < h.pendingExtract.Count; i++)
                {
                    if (!h.pendingExtract[i]) continue;
                    Thing item = h.Extract(i);
                    if (item != null) GenPlace.TryPlaceThing(item, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }
            });
        }
    }

    /// <summary>The attuner gives its psychic sensitivity to the building's artifacts, slowly.</summary>
    public class JobDriver_RechargeBuilding : JobDriver
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil t = ToilMaker.MakeToil("AttuneBuilding");
            t.defaultCompleteMode = ToilCompleteMode.Delay;
            t.defaultDuration = S?.buildingRechargeTicks ?? 2000;
            t.WithProgressBarToilDelay(TargetIndex.A);
            t.tickIntervalAction = delta =>
            {
                CompArtifactHost h = job.targetA.Thing.TryGetComp<CompArtifactHost>();
                if (h == null) return;
                float units = ArtifactFocus.Sensitivity(pawn) * (S?.buildingRechargePerTick ?? 0.02f) * delta;
                bool any = false;
                for (int i = 0; i < h.slots.Count; i++)
                {
                    ArtifactState st = h.slots[i];
                    if (st == null || st.charge >= 1f) continue;
                    any = true;
                    st.charge = Mathf.Min(1f, st.charge + units / Mathf.Max(1f, CompArtifactHost.Capacity(st)));
                }
                if (!any) ReadyForNextToil();
                if (pawn.IsHashIntervalTick(200, delta)) FleckMaker.ThrowMicroSparks(job.targetA.Thing.DrawPos, Map);
            };
            yield return t;
        }
    }

    /// <summary>The attuner work has no column in the Work tab until the colony has an artifact building.</summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), "VisibleCurrently", MethodType.Getter)]
    public static class Patch_AttunerColumnHidden
    {
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, ref bool __result)
        {
            if (__result && __instance.def?.workType == ArtifactBuildingDefOf.Avir_Attuning && !CompArtifactHost.AnyPlayer) __result = false;
        }
    }
}

namespace Avir.Artifactorics
{
    /// <summary>Dev: nudge the gem positions of a building's sockets (all buildings of the def at once) and copy the XML.</summary>
    public class Dialog_TuneSockets : Window
    {
        private readonly CompArtifactHost host;
        private int sel;
        private float step = 0.02f;

        public Dialog_TuneSockets(CompArtifactHost host)
        {
            this.host = host;
            doCloseX = true; draggable = true; preventCameraMotion = false; absorbInputAroundWindow = false; closeOnClickedOutside = false;
            while (host.Props.socketOffsets.Count < host.Props.sockets) host.Props.socketOffsets.Add(Vector2.zero);
            // show gems even in empty sockets while tuning
            host.EnsureSize();
        }

        public override Vector2 InitialSize => new Vector2(360f, 240f + 26f * host.Props.sockets);

        public override void PostClose()
        {
            base.PostClose();
            SocketTuningStore.Save(host.parent.def, host.Props);
        }

        public override void DoWindowContents(Rect r)
        {
            var offs = host.Props.socketOffsets;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x, r.y, r.width, 24f), host.parent.def.defName + " - socket gems");
            float y = r.y + 28f;
            for (int i = 0; i < host.Props.sockets; i++)
            {
                Rect row = new Rect(r.x, y, r.width, 24f);
                if (i == sel) Widgets.DrawHighlightSelected(row);
                Widgets.Label(row, (i + 1) + ":  (" + offs[i].x.ToString("0.00") + ", " + offs[i].y.ToString("0.00") + ")");
                if (Widgets.ButtonInvisible(row)) sel = i;
                y += 26f;
            }
            y += 6f;
            float b = 40f;
            Vector2 o = offs[sel];
            if (Widgets.ButtonText(new Rect(r.x + b, y, b, 30f), "^")) o.y += step;
            if (Widgets.ButtonText(new Rect(r.x, y + 32f, b, 30f), "<")) o.x -= step;
            if (Widgets.ButtonText(new Rect(r.x + 2 * b, y + 32f, b, 30f), ">")) o.x += step;
            if (Widgets.ButtonText(new Rect(r.x + b, y + 64f, b, 30f), "v")) o.y -= step;
            offs[sel] = o;
            if (Widgets.ButtonText(new Rect(r.x + 3 * b + 10f, y, 120f, 30f), "step " + step.ToString("0.00"))) step = step >= 0.1f ? 0.01f : step * 2.5f;
            // size: smaller and larger, by 0.01
            Rect sz = new Rect(r.x + 3 * b + 10f, y + 32f, 120f, 30f);
            if (Widgets.ButtonText(new Rect(sz.x, sz.y, 30f, 30f), "-")) host.Props.socketSize = Mathf.Max(0.03f, host.Props.socketSize - 0.01f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(sz.x + 30f, sz.y, 60f, 30f), host.Props.socketSize.ToString("0.00"));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(sz.x + 90f, sz.y, 30f, 30f), "+")) host.Props.socketSize = Mathf.Min(1.5f, host.Props.socketSize + 0.01f);
            if (Widgets.ButtonText(new Rect(r.x + 3 * b + 10f, y + 64f, 120f, 30f), "Copy XML"))
            {
                string xml = "<socketOffsets>" + string.Concat(offs.Take(host.Props.sockets).Select(v => "<li>(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ")</li>")) + "</socketOffsets>\n<socketSize>" + host.Props.socketSize.ToString("0.00") + "</socketSize>";
                GUIUtility.systemCopyBuffer = xml;
                Log.Message("[Avir] " + host.parent.def.defName + " sockets:\n" + xml);
                Messages.Message("Socket offsets copied (and written to the log).", MessageTypeDefOf.SilentInput, false);
            }
        }
    }

    /// <summary>Keeps the dev socket tuning in the mod settings, so it survives a restart; applied to the defs at startup.</summary>
    [StaticConstructorOnStartup]
    public static class SocketTuningStore
    {
        static SocketTuningStore()
        {
            try
            {
                foreach (string line in AvirMod.Settings?.socketTuning ?? new List<string>())
                {
                    string[] parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(parts[0]);
                    CompProperties_ArtifactHost props = def?.GetCompProperties<CompProperties_ArtifactHost>();
                    if (props == null) continue;
                    props.socketSize = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    var offs = new List<Vector2>();
                    foreach (string v in parts[2].Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] xy = v.Split(',');
                        offs.Add(new Vector2(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    if (offs.Count > 0) props.socketOffsets = offs;
                }
            }
            catch (System.Exception e) { Log.Warning("[Avir] Socket tuning could not be read: " + e.Message); }
        }

        public static void Save(ThingDef def, CompProperties_ArtifactHost props)
        {
            AvirSettings st = AvirMod.Settings;
            if (st == null || def == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string line = def.defName + "|" + props.socketSize.ToString("0.000", ci) + "|"
                          + string.Join(";", props.socketOffsets.Take(props.sockets).Select(v => v.x.ToString("0.000", ci) + "," + v.y.ToString("0.000", ci)));
            st.socketTuning.RemoveAll(l => l.StartsWith(def.defName + "|"));
            st.socketTuning.Add(line);
            st.Write();
        }
    }
}
