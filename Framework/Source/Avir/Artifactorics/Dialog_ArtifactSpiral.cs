using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>
    /// Stage 5 - the development window. Same parchment panel as the creation sheet:
    ///   right  - the hovered or selected cell, points and experience, Invest / Re-attune / Reset
    ///   centre - the spiral: seven arms curving out of the heart, nine cells each, milestones at
    ///            3, 7 and 9. Drag to move it, mouse wheel to zoom.
    ///   left   - the heart and rim, focus of the metals, prices, and the two circles
    ///            (telekinesis, abilities) that arrive in later stages.
    /// Opens from the pawn's Artifacts tab and from a loose artifact. The window never keeps its
    /// own copy of the artifact: it looks the state up through the item or garment every frame,
    /// so inlaying or extracting while it is open cannot lose a spent point.
    /// </summary>
    public class Dialog_ArtifactSpiral : Window
    {
        private readonly Thing owner;

        /// <summary>Which spiral is shown: the carrier of the socket, or chosen for a loose artifact.</summary>
        private ArtifactCarrier carrier = ArtifactCarrier.Apparel;
        private bool Loose => owner.TryGetComp<CompArtifact>() != null;

        // view
        private Vector2 pan = Vector2.zero;
        private float zoom = 0.72f;
        private const float ZoomMin = 0.35f, ZoomMax = 1.4f;
        private bool dragging;
        private Vector2 dragStart;
        private bool mouseDownInSpiral;

        // selection
        private ArtifactArmDef selArm;
        private int selNode;
        private ArtifactArmDef hovArm;
        private int hovNode;

        // cell text on the right: scrolls, and resets for each new cell
        private Vector2 cellTextScroll;
        private ArtifactArmDef textArm;
        private int textNode;

        // spiral geometry, in spiral units (scaled by zoom)
        private const float HeartSize = 200f;
        private const float FirstRadius = 140f;
        private const float RadiusStep = 72f;
        private const float CurlDegrees = 34f;
        private const float NodeSize = 56f;
        private const float MilestoneSize = 82f;
        private const float GuidesSize = 820f;

        private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        private static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);
        private static readonly Color InkGrey = new Color(0.22f, 0.17f, 0.12f);
        private static readonly Color Warn = new Color(0.50f, 0.05f, 0.02f);
        private static readonly Color Gold = new Color(0.70f, 0.50f, 0.12f);
        private static readonly Color Good = new Color(0.12f, 0.33f, 0.08f);

        private const float LeftX0 = 0.056f, LeftX1 = 0.232f;
        private const float MidX0 = 0.262f, MidX1 = 0.740f;
        private const float RightX0 = 0.770f, RightX1 = 0.944f;
        private const float SideTopY = 0.165f, SideBotY = 0.845f;
        private const float MidTopY = 0.120f, MidBotY = 0.890f;

        public Dialog_ArtifactSpiral(Thing owner)
        {
            this.owner = owner;
            CompArtifactSocket sock = owner.Socket();
            if (sock != null) carrier = sock.Carrier;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            doCloseX = false;
            doWindowBackground = false;
            drawShadow = false;
            draggable = false;           // dragging belongs to the spiral
            preventCameraMotion = true;  // the wheel zooms the spiral, not the map
            closeOnAccept = false;
        }

        public static void Open(Thing owner)
        {
            if (owner == null) return;
            Find.WindowStack.WindowOfType<Dialog_ArtifactSpiral>()?.Close(false);
            Find.WindowStack.Add(new Dialog_ArtifactSpiral(owner));
        }

        /// <summary>The live state behind a loose artifact or an inlaid garment.</summary>
        public static ArtifactState StateOf(Thing t)
        {
            if (t == null || t.Destroyed) return null;
            CompArtifact ca = t.TryGetComp<CompArtifact>();
            if (ca != null) return ca.state;
            return t.Socket()?.inlaid;
        }

        private bool sheetMax;
        private Rect sheetNormal;

        public override Vector2 InitialSize
        {
            get
            {
                float w = Mathf.Min(1280f, Verse.UI.screenWidth - 40f);
                float h = w * (941f / 1672f);
                if (h > Verse.UI.screenHeight - 40f) { h = Verse.UI.screenHeight - 40f; w = h * (1672f / 941f); }
                return new Vector2(w, h);
            }
        }

        protected override float Margin => 0f;

        private static Rect Sub(Rect r, float x0, float y0, float x1, float y1)
        {
            return new Rect(r.x + r.width * x0, r.y + r.height * y0, r.width * (x1 - x0), r.height * (y1 - y0));
        }

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        private List<ArtifactArmDef> Arms => ArtifactoricsDatabase.ArmsFor(carrier);

        /// <summary>The pawn wearing the garment or wielding the weapon, or null for a loose artifact.</summary>
        private Pawn Wearer => ArtifactCarrierUtility.HolderOf(owner);

        public override void PostOpen()
        {
            base.PostOpen();
            SheetWindow.StartMaximized(this, ref sheetMax, ref sheetNormal);   // whole screen by default
        }

        public override void DoWindowContents(Rect inRect)
        {
            ArtifactState st = StateOf(owner);
            if (st == null || !st.HasHeart || S == null) { Close(false); return; }

            GUI.DrawTexture(inRect, ArtifactoricsTex.Panel, ScaleMode.StretchToFill);
            Rect close = new Rect(inRect.xMax - inRect.width * 0.052f - 26f, inRect.y + inRect.height * 0.085f, 24f, 24f);
            SheetWindow.MaximizeButton(this, close, ref sheetMax, ref sheetNormal);
            Rect left = Sub(inRect, LeftX0, SideTopY, LeftX1, SideBotY);
            Rect mid = Sub(inRect, MidX0, MidTopY, MidX1, MidBotY);
            Rect right = Sub(inRect, RightX0, SideTopY, RightX1, SideBotY);

            Color oc = GUI.color; TextAnchor oa = Text.Anchor; GameFont of = Text.Font;
            try
            {
                DrawSpiral(mid, st);
                // Decided Oct 2, 2026: the eye looks right for the description, so the cell text,
                // points and buttons sit on the right and the heart / focus / prices on the left.
                DrawLeft(right, st);
                DrawRight(left, st);
            }
            finally
            {
                GUI.color = oc; Text.Anchor = oa; Text.Font = of;
            }
            if (SheetWindow.CloseButton(close)) Close();
        }

        // ================================================================== geometry

        private static float ArmAngle(int armIndex, int armCount)
        {
            return -90f + armIndex * 360f / Mathf.Max(1, armCount);
        }

        /// <summary>Centre of cell `node` (1-based) of arm `armIndex`, in spiral units around the heart.</summary>
        private static Vector2 NodePos(int armIndex, int armCount, int node, int nodeCount)
        {
            float t = nodeCount > 1 ? (node - 1f) / (nodeCount - 1f) : 0f;
            float ang = (ArmAngle(armIndex, armCount) + CurlDegrees * t) * Mathf.Deg2Rad;
            float r = FirstRadius + RadiusStep * (node - 1);
            return new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
        }

        private static Vector2 HeartEdge(int armIndex, int armCount)
        {
            float ang = ArmAngle(armIndex, armCount) * Mathf.Deg2Rad;
            float r = HeartSize * 0.36f;
            return new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
        }

        private enum CellState { Locked, Available, Purchased, Excluded }

        private static bool ArmExcluded(ArtifactState st, ArtifactArmDef arm)
        {
            if (st.opened == null) return false;
            foreach (KeyValuePair<ArtifactArmDef, int> kv in st.opened)
                if (kv.Value > 0 && arm.Excludes(kv.Key)) return true;
            return false;
        }

        private static CellState StateOfCell(ArtifactState st, ArtifactArmDef arm, int node)
        {
            int open = st.OpenedIn(arm);
            if (node <= open) return CellState.Purchased;
            if (ArmExcluded(st, arm)) return CellState.Excluded;
            if (arm.requiresPsychicHeart && st.heartMetal != null && !st.heartMetal.IsPsychic) return CellState.Excluded;
            if (node == open + 1) return CellState.Available;
            return CellState.Locked;
        }

        // ================================================================== centre

        private void DrawSpiral(Rect mid, ArtifactState st)
        {
            HandleInput(mid);

            GUI.BeginGroup(mid);
            try
            {
                Rect local = new Rect(0f, 0f, mid.width, mid.height);

                Vector2 c = local.center + pan;
                int armCount = Arms.Count;
                int nodeCount = S.NodeCount;

                // construction guides behind everything
                float gs = GuidesSize * zoom;
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.DrawTexture(new Rect(c.x - gs / 2f, c.y - gs / 2f, gs, gs), ArtifactoricsTex.HeartGuides, ScaleMode.ScaleToFit);

                // links first, then cells on top
                for (int a = 0; a < armCount; a++)
                {
                    ArtifactArmDef arm = Arms[a];
                    int open = st.OpenedIn(arm);
                    Vector2 prev = c + HeartEdge(a, armCount) * zoom;
                    for (int n = 1; n <= nodeCount; n++)
                    {
                        Vector2 p = c + NodePos(a, armCount, n, nodeCount) * zoom;
                        DrawLink(prev, p, n <= open, ArmExcluded(st, arm) && n > open);
                        prev = p;
                    }
                }

                hovArm = null; hovNode = 0;
                Vector2 mouse = Event.current.mousePosition;
                bool mouseInside = local.Contains(mouse);
                for (int a = 0; a < armCount; a++)
                {
                    ArtifactArmDef arm = Arms[a];
                    for (int n = 1; n <= nodeCount; n++)
                    {
                        Vector2 p = c + NodePos(a, armCount, n, nodeCount) * zoom;
                        bool milestone = S.IsMilestone(n);
                        float size = (milestone ? MilestoneSize : NodeSize) * zoom;
                        Rect cell = new Rect(p.x - size / 2f, p.y - size / 2f, size, size);
                        if (!cell.Overlaps(local)) continue;
                        CellState cs = StateOfCell(st, arm, n);
                        bool hovered = mouseInside && !dragging && (mouse - p).sqrMagnitude <= size * size * 0.25f;
                        if (hovered) { hovArm = arm; hovNode = n; }
                        bool selected = arm == selArm && n == selNode;
                        DrawCell(cell, milestone, cs, hovered, selected);
                        if (milestone && arm.Icon != null) DrawEmblem(cell.ContractedBy(cell.width * 0.27f), arm.Icon, cs);
                    }
                }

                // the path's name (and emblem) past the end of each arm: which arm is which at a glance
                for (int a = 0; a < armCount; a++)
                    DrawArmTag(local, c, a, armCount, nodeCount, Arms[a], st);

                DrawHeart(c, st);
            }
            finally
            {
                GUI.EndGroup();
            }

            // hint
            GUI.color = new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.7f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerRight;
            ArtInk.Label(new Rect(mid.x, mid.yMax - 22f, mid.width - 70f, 20f), "Drag to move · wheel to zoom");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void HandleInput(Rect mid)
        {
            Event e = Event.current;
            bool over = mid.Contains(e.mousePosition);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (over && e.button == 0)
                    {
                        mouseDownInSpiral = true;
                        dragging = false;
                        dragStart = e.mousePosition;
                    }
                    break;
                case EventType.MouseDrag:
                    if (mouseDownInSpiral)
                    {
                        if (!dragging && (e.mousePosition - dragStart).sqrMagnitude > 16f) dragging = true;
                        if (dragging)
                        {
                            pan += e.delta;
                            ClampPan(mid);
                            e.Use();
                        }
                    }
                    break;
                case EventType.MouseUp:
                    if (mouseDownInSpiral && e.button == 0)
                    {
                        if (!dragging && over)
                        {
                            if (hovArm != null)
                            {
                                selArm = hovArm; selNode = hovNode;
                                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                            }
                        }
                        mouseDownInSpiral = false;
                        dragging = false;
                        e.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (over)
                    {
                        float old = zoom;
                        zoom = Mathf.Clamp(zoom * (e.delta.y > 0f ? 0.9f : 1.1f), ZoomMin, ZoomMax);
                        // zoom around the mouse
                        Vector2 centre = mid.center + pan;
                        Vector2 rel = e.mousePosition - centre;
                        pan -= rel * (zoom / old - 1f);
                        ClampPan(mid);
                        e.Use();
                    }
                    break;
            }
        }

        private void ClampPan(Rect mid)
        {
            // + room for the path tags past the last cell
            float reach = (FirstRadius + RadiusStep * (S.NodeCount - 1) + MilestoneSize + 110f) * zoom;
            float mx = Mathf.Max(0f, reach - mid.width * 0.3f);
            float my = Mathf.Max(0f, reach - mid.height * 0.3f);
            pan.x = Mathf.Clamp(pan.x, -mx, mx);
            pan.y = Mathf.Clamp(pan.y, -my, my);
        }

        private void DrawLink(Vector2 a, Vector2 b, bool purchased, bool excluded)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float th = (purchased ? 9f : 7f) * Mathf.Max(0.7f, zoom);
            Texture2D tex = purchased ? ArtifactoricsTex.LinkPurchased : ArtifactoricsTex.LinkUnpurchased;
            if (tex.wrapMode != TextureWrapMode.Repeat) tex.wrapMode = TextureWrapMode.Repeat;   // 1.7 loads them clamped
            Matrix4x4 m = GUI.matrix;
            AvirLine.Rotate(ang, a);
            GUI.color = excluded ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            float tile = 128f * (th / 32f);
            Rect seg = new Rect(a.x, a.y - th / 2f, len, th);
            Rect uv = new Rect(0f, 0f, len / tile, 1f);
            GUI.DrawTextureWithTexCoords(seg, tex, uv);
            if (!purchased) GUI.DrawTextureWithTexCoords(seg, tex, uv);   // pencil is faint: trace it twice
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        private void DrawCell(Rect cell, bool milestone, CellState cs, bool hovered, bool selected)
        {
            Texture2D tex;
            if (milestone)
            {
                tex = cs == CellState.Purchased ? ArtifactoricsTex.MilestonePurchased
                    : cs == CellState.Available ? ArtifactoricsTex.MilestoneAvailable
                    : ArtifactoricsTex.MilestoneLocked;
            }
            else
            {
                tex = cs == CellState.Purchased ? ArtifactoricsTex.NodePurchased
                    : cs == CellState.Available ? ArtifactoricsTex.NodeAvailable
                    : cs == CellState.Excluded ? ArtifactoricsTex.NodeExcluded
                    : ArtifactoricsTex.NodeLocked;
            }

            if (selected || hovered)
            {
                Rect glow = cell.ExpandedBy(cell.width * 0.35f);
                GUI.color = selected ? new Color(0.75f, 0.12f, 0.05f, 0.55f) : new Color(1f, 0.82f, 0.35f, 0.45f);
                GUI.DrawTexture(glow, ArtifactoricsTex.Glow, ScaleMode.StretchToFill);
            }

            GUI.color = cs == CellState.Excluded && milestone ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            GUI.DrawTexture(cell, tex, ScaleMode.ScaleToFit);
            if (cs == CellState.Excluded && milestone)
            {
                // milestones have no struck-through picture: borrow the strokes of the excluded node
                GUI.color = Color.white;
                GUI.DrawTexture(cell.ContractedBy(cell.width * 0.12f), ArtifactoricsTex.NodeExcluded, ScaleMode.ScaleToFit);
            }

            if (selected)
            {
                GUI.color = new Color(0.62f, 0.08f, 0.04f, 0.95f);
                GUI.DrawTexture(cell.ExpandedBy(cell.width * 0.14f), ArtifactoricsTex.Ring, ScaleMode.StretchToFill);
            }
            GUI.color = Color.white;
        }

        private static void DrawEmblem(Rect r, Texture2D icon, CellState cs)
        {
            GUI.color = cs == CellState.Purchased ? Color.white
                      : cs == CellState.Excluded ? new Color(1f, 1f, 1f, 0.25f)
                      : new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        /// <summary>Emblem and name of a path, just beyond its last cell.</summary>
        private void DrawArmTag(Rect local, Vector2 c, int a, int armCount, int nodeCount, ArtifactArmDef arm, ArtifactState st)
        {
            Vector2 last = NodePos(a, armCount, nodeCount, nodeCount);
            Vector2 dir = nodeCount > 1 ? (last - NodePos(a, armCount, nodeCount - 1, nodeCount)).normalized : last.normalized;
            Vector2 p = c + (last + dir * (MilestoneSize * 0.5f + 46f)) * zoom;
            float isz = 58f * zoom;
            float lw = 130f * Mathf.Max(zoom, 0.8f);
            Rect box = new Rect(p.x - lw / 2f, p.y - isz / 2f, lw, isz + 20f);
            if (!box.Overlaps(local)) return;

            bool closed = ArmExcluded(st, arm) || (arm.requiresPsychicHeart && st.heartMetal != null && !st.heartMetal.IsPsychic);
            bool active = arm == (hovArm ?? selArm);
            float y = p.y - isz / 2f;
            if (arm.Icon != null)
            {
                GUI.color = closed ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
                GUI.DrawTexture(new Rect(p.x - isz / 2f, y, isz, isz), arm.Icon, ScaleMode.ScaleToFit);
                y += isz;
            }
            else y += isz * 0.5f;
            GUI.color = closed ? InkGrey : active ? Warn : Ink;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(p.x - lw / 2f, y, lw, 20f), arm.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void DrawHeart(Vector2 c, ArtifactState st)
        {
            float hs = HeartSize * zoom;
            Rect heart = new Rect(c.x - hs / 2f, c.y - hs / 2f, hs, hs);
            if (st.rimMetal != null)
            {
                GUI.color = st.rimMetal.RimColor;
                GUI.DrawTexture(heart, ArtifactoricsTex.HeartRimMask, ScaleMode.ScaleToFit);
            }
            if (st.heartMetal != null)
            {
                GUI.color = st.heartMetal.GemColorAt(Time.realtimeSinceStartup);
                GUI.DrawTexture(heart, ArtifactoricsTex.GemBase, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                GUI.DrawTexture(heart, ArtifactoricsTex.GemHighlight, ScaleMode.ScaleToFit);
            }
            GUI.color = st.rimMetal != null ? st.rimMetal.RimColor : Color.white;
            GUI.DrawTexture(heart, st.rimMetal != null ? ArtifactoricsTex.HeartSettingMask : ArtifactoricsTex.HeartSetting, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        // ================================================================== left

        private void DrawLeft(Rect r, ArtifactState st)
        {
            ArtifactArmDef arm = hovArm ?? selArm;
            int node = hovArm != null ? hovNode : selNode;
            float y = r.y;

            // ---- cell description ----
            Rect desc = new Rect(r.x + 4f, y, r.width - 8f, r.height - 250f);
            if (arm == null)
            {
                GUI.color = Ink; Text.Font = GameFont.Medium;
                ArtInk.Label(new Rect(desc.x, desc.y, desc.width, 34f), owner.LabelCapNoCount.Truncate(desc.width));
                Text.Font = GameFont.Small; GUI.color = InkSoft;
                ArtInk.Label(new Rect(desc.x, desc.y + 38f, desc.width, desc.height - 38f),
                    st.ShortLabel().CapitalizeFirst() + ".\n\nSelect a cell on the spiral to read about it and invest a point.");
            }
            else
            {
                DrawCellText(desc, st, arm, node);
            }

            // ---- points ----
            float py = r.yMax - 240f;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(r.x + 2f, py, 30f, 30f), ArtifactoricsTex.PointSeal, ScaleMode.ScaleToFit);
            GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleLeft;
            ArtInk.Label(new Rect(r.x + 38f, py, r.width - 38f, 30f), "Points: " + st.PointsAvailable);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            py += 32f;
            st.NextPointProgress(out float have, out float need);
            GUI.color = InkSoft; Text.Font = GameFont.Tiny;
            ArtInk.Label(new Rect(r.x + 4f, py, r.width - 8f, 18f), "Experience: " + have.ToString("N0") + " / " + need.ToString("N0"));
            py += 18f;
            Rect bar = new Rect(r.x + 4f, py, r.width - 8f, 10f);
            Widgets.DrawBoxSolid(bar, new Color(0.25f, 0.17f, 0.08f, 0.30f));
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * (need > 0f ? have / need : 0f), bar.height), new Color(Gold.r, Gold.g, Gold.b, 0.9f));
            GUI.color = InkSoft; Widgets.DrawBox(bar, 1);
            py += 14f;
            ArtInk.Label(new Rect(r.x + 4f, py, r.width - 8f, 18f),
                "Spent " + st.PointsSpent + (st.burntPoints > 0 ? " (" + st.burntPoints + " burnt)" : "") + " · earned " + st.PointsEarned);
            Text.Font = GameFont.Small;
            py += 26f;

            // ---- buttons ----
            float bh = Mathf.Min(44f, r.width * 0.24f);
            Rect b1 = new Rect(r.x, py, r.width, bh);
            Rect b2 = new Rect(r.x, b1.yMax + 6f, r.width, bh);
            Rect b3 = new Rect(r.x, b2.yMax + 6f, r.width, bh);

            string why = InvestBlockReason(st);
            if (ParchmentButton(b1, "Invest point", why == null)) TryInvest(st);
            if (why != null && Mouse.IsOver(b1)) TooltipHandler.TipRegion(b1, why);

            Pawn wearer = Wearer;
            bool canAttune = wearer != null && wearer.IsColonistPlayerControlled && Dialog_ArtifactAttune.HasAnything(wearer);
            if (ParchmentButton(b2, "Re-attune", canAttune)) Find.WindowStack.Add(new Dialog_ArtifactAttune(wearer));
            TooltipHandler.TipRegion(b2, "Choose which arms give their milestones: at most " + S.maxMilestoneArmsPerPawn
                + " armour arms and " + S.maxMilestoneArmsPerPawn + " weapon arms per pawn, counted apart. The pawn spends one hour on it, wherever it stands."
                + (wearer == null ? "\n\nThe artifact must be worn." : "")
                + (wearer != null ? "\n\n" + (ArtifactMilestones.StatusLine(wearer) ?? "No milestones reached yet.") : ""));

            // no reset: opened cells stay; experience moves only by a ritual of transfer
            GUI.color = InkSoft; Text.Font = GameFont.Tiny;
            ArtInk.Label(b3, "Opened cells stay. Experience moves to another artifact or amulet only by a ritual of transfer.");
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        private void DrawCellText(Rect r, ArtifactState st, ArtifactArmDef arm, int node)
        {
            CellState cs = StateOfCell(st, arm, node);
            bool milestone = S.IsMilestone(node);
            float y = r.y;
            GUI.color = Ink; Text.Font = GameFont.Medium;
            float tx = r.x;
            if (arm.Icon != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(r.x, y + 1f, 32f, 32f), arm.Icon, ScaleMode.ScaleToFit);
                GUI.color = Ink;
                tx += 36f;
            }
            ArtInk.Label(new Rect(tx, y, r.xMax - tx, 34f), arm.LabelCap);
            y += 34f;
            Text.Font = GameFont.Tiny; GUI.color = InkSoft;
            string stateWord = cs == CellState.Purchased ? "opened" : cs == CellState.Available ? "available"
                             : cs == CellState.Excluded ? "closed" : "locked";
            ArtInk.Label(new Rect(r.x, y, r.width, 18f),
                "Cell " + node + " of " + S.NodeCount + (milestone ? " · milestone" : "") + " · " + stateWord
                + " · " + S.NodeCost(node) + " point" + (S.NodeCost(node) == 1 ? "" : "s"));
            y += 22f;
            Text.Font = GameFont.Small;

            // Order matters: the panel is narrow, so what THIS cell does comes first, then what the
            // path is about, and only then the next milestone. Before, the cell stats sat under a
            // long milestone text and fell off the bottom: ordinary cells looked like they did nothing.
            var sb = new System.Text.StringBuilder();
            Pawn wearer = Wearer;
            float k = ArtifactFocus.Strength(st, arm, wearer);
            int have = st.OpenedIn(arm);
            ArtifactMilestoneInfo mi = milestone ? arm.MilestoneAt(node) : null;

            // ---- 1. this cell ----
            if (mi != null)
                sb.Append("Milestone - ").Append(mi.label.CapitalizeFirst()).Append(":\n").Append(mi.description).Append("\n\n");
            if (!arm.cellStats.NullOrEmpty())
            {
                sb.Append(mi != null ? "The cell itself also gives" : "This cell gives")
                  .Append(" (arm strength x").Append(k.ToString("0.##")).Append("):");
                foreach (ArtifactCellStat c in arm.cellStats)
                    if (c.stat != null) sb.Append("\n  ").Append(c.stat.LabelCap).Append(" ").Append(BonusText(c, k));
                int total = Mathf.Max(have, node);
                sb.Append(node <= have ? "\n\nThe arm now gives (" : "\n\nWith this cell the arm gives (")
                  .Append(total).Append(" of ").Append(S.NodeCount).Append(" cells):");
                foreach (ArtifactCellStat c in arm.cellStats)
                    if (c.stat != null) sb.Append("\n  ").Append(c.stat.LabelCap).Append(" ").Append(BonusText(c, k * total));
            }
            else if (mi == null)
            {
                sb.Append(arm.nodeSummary);
            }

            // ---- 2. the path ----
            if (!arm.description.NullOrEmpty()) sb.Append("\n\nThe path: ").Append(arm.description);

            // ---- 3. the next milestone ----
            for (int n = node + 1; n <= S.NodeCount; n++)
            {
                if (!S.IsMilestone(n)) continue;
                ArtifactMilestoneInfo next = arm.MilestoneAt(n);
                if (next != null)
                    sb.Append("\n\nNext milestone (cell ").Append(n).Append("): ").Append(next.label.CapitalizeFirst())
                      .Append(" - ").Append(next.description);
                break;
            }

            // ---- 4. state ----
            if (milestone && cs == CellState.Purchased && wearer != null)
            {
                MilestoneArm ma = ArtifactMilestones.For(wearer).FirstOrDefault(m => m.state == st && m.arm == arm);
                if (ma != null)
                    sb.Append("\n\n").Append(ma.active ? "Working on " + wearer.LabelShort + "."
                        : ma.dormant ? "Asleep: switched off by re-attuning."
                        : "Not working: " + wearer.LabelShort + " already has " + S.maxMilestoneArmsPerPawn + (arm.carrier == ArtifactCarrier.Weapon ? " weapon" : " armour") + " milestone arms. Re-attune to choose.");
            }
            if (cs == CellState.Excluded && ArmExcluded(st, arm))
            {
                ArtifactArmDef by = st.opened.Where(kv => kv.Value > 0 && arm.Excludes(kv.Key)).Select(kv => kv.Key).FirstOrDefault();
                sb.Append("\n\nClosed for good on this artifact").Append(by != null ? " by " + by.label : "").Append('.');
            }
            else if (cs == CellState.Excluded && arm.requiresPsychicHeart)
            {
                sb.Append("\n\nOpens only with an essentium or tisserium heart.");
            }
            if (cs != CellState.Excluded && have == 0 && !arm.exclusionGroup.NullOrEmpty())
            {
                string others = ExcludedBy(arm);
                if (!others.NullOrEmpty()) sb.Append("\n\nOpening this arm closes ").Append(others).Append(" for good.");
            }

            // ---- 5. where the strength comes from ----
            var why = new System.Text.StringBuilder();
            ArtifactFocus.Focus(st, arm, wearer, why);
            sb.Append("\n\nArm strength x").Append(k.ToString("0.##"))
              .Append(" = (1 + focus) x heart quality (").Append(st.heartQuality.GetLabel()).Append(", x")
              .Append(S.HeartQualityFactor(st.heartQuality).ToString("0.##")).Append("). Every number above grows with it.");
            if (why.Length > 0) sb.Append("\nFocus:\n").Append(why.ToString().TrimEnd());

            // Scrolls when it does not fit; starts from the top for every new cell.
            if (arm != textArm || node != textNode) { textArm = arm; textNode = node; cellTextScroll = Vector2.zero; }
            string text = sb.ToString();
            Rect outer = new Rect(r.x, y, r.width, r.yMax - y);
            float th = Text.CalcHeight(text, outer.width - 16f);
            Rect view = new Rect(0f, 0f, th > outer.height ? outer.width - 16f : outer.width, th);
            GUI.color = InkSoft;
            Widgets.BeginScrollView(outer, ref cellTextScroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        private static string BonusText(ArtifactCellStat c, float k)
        {
            if (c.offsetPerCell != 0f)
            {
                float v = c.offsetPerCell * k;
                return c.stat.ValueToString(v, ToStringNumberSense.Offset);
            }
            return ArtifactFocus.Signed(c.factorPerCell * k);
        }

        private string ExcludedBy(ArtifactArmDef arm)
        {
            return Arms.Where(o => arm.Excludes(o)).Select(o => o.label).ToCommaList(true);
        }

        private string InvestBlockReason(ArtifactState st)
        {
            if (selArm == null) return "Select a cell first.";
            int next = st.OpenedIn(selArm) + 1;
            if (selNode < next) return "This cell is already open.";
            if (selNode > next && next <= S.NodeCount) return "Cells open in order: the next one in this arm is cell " + next + ".";
            string why = st.CannotOpenReason(selArm);
            return why == null ? null : why.CapitalizeFirst() + ".";
        }

        private void TryInvest(ArtifactState st)
        {
            ArtifactArmDef arm = selArm;
            void Do()
            {
                ArtifactState live = StateOf(owner);
                if (live == null || !live.TryOpenNext(arm)) return;
                SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                int next = live.OpenedIn(arm) + 1;
                if (next <= S.NodeCount) selNode = next;     // move on to the next cell
            }
            if (st.OpenedIn(arm) == 0)
            {
                string others = ExcludedBy(arm);
                if (!others.NullOrEmpty())
                {
                    Find.WindowStack.Add(new Dialog_ArtConfirm(
                        "Open " + arm.label + "? This closes " + others + " on this artifact for good. Only a full reset frees them again.",
                        Do, destructive: true));
                    return;
                }
            }
            Do();
        }

        // ================================================================== right

        private void DrawRight(Rect r, ArtifactState st)
        {
            float y = r.y;
            float w = r.width - 8f;
            float x = r.x + 4f;

            // ---- which spiral (stage 12) ----
            Text.Font = GameFont.Tiny;
            if (Loose)
            {
                float half = (w - 4f) / 2f;
                SpiralTab(new Rect(x, y, half, 22f), "Armour spiral", ArtifactCarrier.Apparel);
                SpiralTab(new Rect(x + half + 4f, y, half, 22f), "Weapon spiral", ArtifactCarrier.Weapon);
            }
            else
            {
                GUI.color = InkSoft;
                ArtInk.Label(new Rect(x, y + 3f, w, 20f), carrier == ArtifactCarrier.Weapon ? "Weapon spiral (in a weapon)" : "Armour spiral (in armour)");
            }
            y += 26f;
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(x, y, w, 34f), "Heart");
            y += 34f;
            Text.Font = GameFont.Small;
            y = Line(x, y, w, st.heartMetal.LabelCap + ", " + st.heartQuality.GetLabel(), Ink);
            if (!st.heartMaker.NullOrEmpty()) y = Line(x, y, w, "idea by " + st.heartMaker, InkSoft, GameFont.Tiny);
            y = Line(x, y, w, "Rim: " + (st.rimMetal != null ? st.rimMetal.LabelCap + ", " + st.settingQuality.GetLabel() : "-"), Ink);
            if (!st.settingMaker.NullOrEmpty()) y = Line(x, y, w, "set by " + st.settingMaker, InkSoft, GameFont.Tiny);
            y = Line(x, y, w, "Charge: " + st.charge.ToStringPercent() + " of " + ArtifactCharge.Capacity(st, Wearer).ToString("0")
                              + (st.charge <= 0f ? " - empty, milestones stopped" : ""), st.charge < 0.2f ? Warn : InkSoft, GameFont.Tiny);
            y += 6f;

            // ---- focus (stage 6) ----
            Pawn wearer = Wearer;
            GUI.color = Ink;
            ArtInk.Label(new Rect(x, y, w, 22f), "Focus");
            y += 22f;
            float qf = S.HeartQualityFactor(st.heartQuality);
            y = Line(x, y, w, "Heart quality: x" + qf.ToStringPercent(), InkSoft, GameFont.Tiny);
            if (st.heartMetal.IsPsychic && wearer == null)
                y = Line(x, y, w, "Psychic metal: counted from the wearer's psychic sensitivity.", InkGrey, GameFont.Tiny);
            foreach (ArtifactArmDef arm in Arms)
            {
                var sb = new System.Text.StringBuilder();
                float f = ArtifactFocus.Focus(st, arm, wearer, sb);
                bool psychicNote = st.heartMetal.IsPsychic;
                if (Mathf.Abs(f) < 0.001f && !psychicNote) continue;
                Color col = f > 0.001f ? Good : f < -0.001f ? Warn : InkSoft;
                float y0 = y;
                y = Line(x, y, w, arm.LabelCap + "  " + ArtifactFocus.Signed(f), col, GameFont.Tiny);
                Rect tip = new Rect(x, y0, w, y - y0);
                if (Mouse.IsOver(tip))
                {
                    Widgets.DrawHighlight(tip);
                    float strength = Mathf.Max(0f, 1f + f) * qf;
                    TooltipHandler.TipRegion(tip, arm.LabelCap + "\n\n" + sb.ToString().TrimEnd()
                        + "\nHeart quality: x" + qf.ToStringPercent()
                        + "\n\nStrength of this arm: x" + strength.ToStringPercent());
                }
            }
            y += 6f;

            // ---- prices ----
            Text.Font = GameFont.Small; GUI.color = Ink;
            ArtInk.Label(new Rect(x, y, w, 22f), "Prices");
            y += 22f;
            int nextPoint = st.PointsEarned + 1;
            y = Line(x, y, w, "Point " + nextPoint + ": " + S.XpForPoint(nextPoint).ToString("N0") + " xp", InkSoft, GameFont.Tiny);
            y = Line(x, y, w, "Each point costs more; the price counts every point of this artifact.", InkGrey, GameFont.Tiny);
            y = Line(x, y, w, "Cells: " + CellPrices(), InkSoft, GameFont.Tiny);
            y = Line(x, y, w, "Full arm: " + S.ArmPointCost + " points", InkSoft, GameFont.Tiny);

            // ---- circles (later stages) ----
            float cs = Mathf.Min((r.width - 12f) / 2f, r.yMax - y - 22f);
            if (cs >= 48f)
            {
                Rect c1 = new Rect(r.x + (r.width / 2f - cs) / 2f, r.yMax - cs - 20f, cs, cs);
                Rect c2 = new Rect(r.x + r.width / 2f + (r.width / 2f - cs) / 2f, c1.y, cs, cs);
                DrawTelekinesisCircle(c1, st);
                DrawAbilityCircle(c2, st);
                GUI.color = InkSoft; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperCenter;
                ArtInk.Label(new Rect(c1.x - 10f, c1.yMax + 2f, cs + 20f, 18f), "Telekinesis");
                ArtInk.Label(new Rect(c2.x - 10f, c2.yMax + 2f, cs + 20f, 18f), "Abilities");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        /// <summary>Stage 9: five sockets, each a level of telekinesis. Click opens the next one.</summary>
        private void DrawTelekinesisCircle(Rect c, ArtifactState st)
        {
            int slots = S.telekinesisSlotCosts.Count;
            bool over = Mouse.IsOver(c);
            string why = st.CannotOpenTelekinesisReason();
            if (over && why == null)
            {
                GUI.color = new Color(1f, 0.82f, 0.35f, 0.35f);
                GUI.DrawTexture(c.ExpandedBy(c.width * 0.08f), ArtifactoricsTex.Glow, ScaleMode.StretchToFill);
            }
            GUI.color = st.tkLevel > 0 ? Color.white : new Color(1f, 1f, 1f, 0.7f);
            GUI.DrawTexture(c, ArtifactoricsTex.CircleTelekinesis, ScaleMode.ScaleToFit);

            // the same stones as on the amulet: opened, the next one (when it can be opened), the rest
            Color gem = st.heartMetal != null ? st.heartMetal.GemColorAt(Time.realtimeSinceStartup) : Gold;
            float sz = c.width * 0.13f;
            for (int i = 0; i < slots; i++)
            {
                Vector2 pos = ArtifactoricsTex.TkSocketPos(c, i, slots);
                Texture2D stex = i < st.tkLevel ? ArtifactoricsTex.StoneOpened
                    : i == st.tkLevel && why == null ? (ArtifactoricsTex.StoneAvailable ?? ArtifactoricsTex.StoneLocked)
                    : ArtifactoricsTex.StoneLocked;
                float s2 = sz * 1.25f;
                Rect sr = new Rect(Mathf.Round(pos.x - s2 / 2f), Mathf.Round(pos.y - s2 / 2f), Mathf.Round(s2), Mathf.Round(s2));
                if (stex != null)
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(sr, stex, ScaleMode.StretchToFill);
                }
                else if (i < st.tkLevel)
                {
                    GUI.color = gem;
                    GUI.DrawTexture(sr.ContractedBy(s2 * 0.15f), ArtifactoricsTex.Disc, ScaleMode.StretchToFill);
                }
            }
            GUI.color = Color.white;

            Pawn wearer = Wearer;
            var tip = new System.Text.StringBuilder();
            tip.Append("Telekinesis circle: level ").Append(st.tkLevel).Append(" of ").Append(slots);
            if (st.tkLevel > 0) tip.Append(" - radius ").Append(S.TelekinesisRadius(st.tkLevel).ToString("0")).Append(" cells");
            tip.Append(".\n\nThe same telekinesis the amulets give. Only the best artifact on a pawn counts; its radius is added to the radius of the pawn's telekinetic amulet.");
            if (st.tkLevel < slots)
            {
                int next = st.tkLevel + 1;
                tip.Append("\n\nNext socket: ").Append(S.TelekinesisSlotCost(next)).Append(" point")
                   .Append(S.TelekinesisSlotCost(next) == 1 ? "" : "s").Append(" -> radius ")
                   .Append(S.TelekinesisRadius(next).ToString("0")).Append(".");
                tip.Append(why == null ? "\nClick to open it." : "\nCannot open: " + why + ".");
            }
            if (wearer != null && Avir.AvirTelekinesisUtility.TryGetRadius(wearer, out float total))
                tip.Append("\n\n").Append(wearer.LabelShort).Append(" reaches ").Append(total.ToString("0")).Append(" cells in all.");
            TooltipHandler.TipRegion(c, tip.ToString());

            if (why == null && Widgets.ButtonInvisible(c))
            {
                if (st.TryOpenTelekinesis()) SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            }
        }

        /// <summary>Stage 11: the artifact's one stone socket. Click for the stone menu.</summary>
        private void DrawAbilityCircle(Rect c, ArtifactState st)
        {
            bool over = Mouse.IsOver(c);
            if (over)
            {
                GUI.color = new Color(1f, 0.82f, 0.35f, 0.3f);
                GUI.DrawTexture(c.ExpandedBy(c.width * 0.08f), ArtifactoricsTex.Glow, ScaleMode.StretchToFill);
            }
            GUI.color = st.stone != null ? Color.white : new Color(1f, 1f, 1f, 0.7f);
            GUI.DrawTexture(c, ArtifactoricsTex.CircleAbilities, ScaleMode.ScaleToFit);
            Pawn wearer = Wearer;
            var tip = new System.Text.StringBuilder();
            if (st.stone?.ability != null)
            {
                Rect g = c.ContractedBy(c.width * 0.28f);
                AbilityStoneUI.Draw(g, st.stone.metal);
                GUI.DrawTexture(g.ContractedBy(g.width * 0.18f), st.stone.ability.Icon, ScaleMode.ScaleToFit);
                tip.Append(AbilityCodexView.TitleFor(st.stone.ability, st.stone.metal)).Append(", ").Append(st.stone.quality.GetLabel());
                if (wearer != null)
                {
                    ActiveStone a = ArtifactAbilities.For(wearer).FirstOrDefault(x => x.state == st);
                    if (a != null)
                    {
                        tip.Append("\nPower in ").Append(wearer.LabelShort).Append("'s hands: x").Append(ArtifactAbilities.Power(wearer, st, st.stone).ToString("0.00"));
                        tip.Append(a.active ? "\nWorking." : st.stone.dormant ? "\nAsleep (re-attuned off)." : st.charge <= 0f ? "\nThe artifact is empty."
                            : !st.stone.ability.AllowedIn(carrier) ? "\nNot working: this stone works in " + st.stone.ability.WorksInText + "."
                            : st.stone.ability.usesEnergy && st.stone.energy == null ? "\nNot working: the stone has no energy."
                            : !st.stone.ability.onlyWeapons.NullOrEmpty() && !st.stone.ability.onlyWeapons.Contains(owner.def) ? "\nNot working: only in " + string.Join(", ", st.stone.ability.onlyWeapons.Select(w => w.label).Distinct()) + "."
                            : !ArtifactAbilities.PsychicOk(wearer, st.stone.metal, out _) ? "\nNot working: " + wearer.LabelShort + " does not meet the " + st.stone.metal.label + " condition."
                            : "\nNot working: " + wearer.LabelShort + " already has " + S.maxActiveAbilitiesPerPawn + " working abilities in " + (carrier == ArtifactCarrier.Weapon ? "weapons." : "armour."));
                    }
                }
                tip.Append("\nWorks in ").Append(st.stone.ability.WorksInText).Append('.');
                if (st.stone.ability.canAuto) tip.Append(st.stone.autoUse ? " Uses itself in a fight." : " Auto-use is off.");
                tip.Append("\n\nMetal: ").Append(ArtifactAbilities.MetalBonusText(st.stone.metal)).Append(".\n\nClick for the stone menu.");
            }
            else tip.Append("Ability stone socket - empty.\n\nClick to set a stone. Anyone can set one; only an Avir can take it out.");
            TooltipHandler.TipRegion(c, tip.ToString());
            GUI.color = Color.white;

            if (!Widgets.ButtonInvisible(c)) return;
            var opts = new List<FloatMenuOption>();
            Map map = owner.MapHeld;
            if (st.stone == null)
            {
                if (map != null)
                    foreach (Thing s in map.listerThings.ThingsOfDef(ArtifactAbilityDefOf.Avir_AbilityStone))
                    {
                        if (!s.Spawned || s.IsForbidden(Faction.OfPlayer)) continue;
                        Thing stoneThing = s;
                        ArtifactAbilityDef sa = s.TryGetComp<CompAbilityStone>()?.stone?.ability;
                        if (sa != null && !Loose && !sa.AllowedIn(carrier))
                        {
                            opts.Add(new FloatMenuOption(s.LabelCap + " (works in " + sa.WorksInText + ")", null));
                            continue;
                        }
                        opts.Add(new FloatMenuOption("Set: " + s.LabelCap, () => AbilityStoneOrders.OrderInsert(owner, stoneThing)));
                    }
                if (opts.Count == 0) opts.Add(new FloatMenuOption("No ability stones on this map", null));
            }
            else
            {
                opts.Add(new FloatMenuOption("Take the stone out (Avir only)", () => AbilityStoneOrders.OrderExtract(owner)));
                if (st.stone.ability.canAuto)
                {
                    AbilityStoneState stone = st.stone;
                    opts.Add(new FloatMenuOption("Auto-use in a fight: " + (stone.autoUse ? "on - switch off" : "off - switch on"),
                        () => stone.autoUse = !stone.autoUse));
                }
                if (st.stone.ability == ArtifactAbilityDefOf.ArtifactAbility_Architect)
                {
                    AbilityStoneState stone = st.stone;
                    opts.Add(new FloatMenuOption("Background repair: " + (stone.backgroundOn ? "on - switch off" : "off - switch on"),
                        () => stone.backgroundOn = !stone.backgroundOn));
                }
            }
            AbilityStoneState cur = st.stone;
            opts.Add(new FloatMenuOption("Ability codex", () => Find.WindowStack.Add(new Dialog_AbilityCodex(cur?.ability, cur?.metal))));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static string CellPrices()
        {
            var parts = new List<string>();
            int n = 1;
            while (n <= S.NodeCount)
            {
                int cost = S.NodeCost(n);
                int end = n;
                while (end + 1 <= S.NodeCount && S.NodeCost(end + 1) == cost) end++;
                parts.Add((end > n ? n + "-" + end : n.ToString()) + " = " + cost);
                n = end + 1;
            }
            return string.Join(", ", parts);
        }

        private static float Line(float x, float y, float w, string text, Color col, GameFont font = GameFont.Small)
        {
            GameFont of = Text.Font;
            Text.Font = font;
            float h = Text.CalcHeight(text, w);
            GUI.color = col;
            ArtInk.Label(new Rect(x, y, w, h), text);
            Text.Font = of;
            return y + h;
        }

        private void SpiralTab(Rect r, string label, ArtifactCarrier c)
        {
            bool on = carrier == c;
            if (on) { Widgets.DrawBoxSolid(r, new Color(Gold.r, Gold.g, Gold.b, 0.22f)); GUI.color = Gold; Widgets.DrawBox(r, 1); }
            else if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, new Color(1f, 0.9f, 0.6f, 0.18f));
            GUI.color = on ? Ink : InkSoft;
            Text.Anchor = TextAnchor.MiddleCenter;
            ArtInk.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(r, "A loose artifact keeps cells of both spirals. In armour only the armour arms work, in a melee weapon only the weapon arms.");
            if (!on && Widgets.ButtonInvisible(r))
            {
                carrier = c;
                selArm = null; selNode = 0; hovArm = null;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }

        private static bool ParchmentButton(Rect r, string label, bool enabled)
        {
            bool over = enabled && Mouse.IsOver(r);
            Texture2D tex = !over ? ArtifactoricsTex.ButtonNormal
                          : Input.GetMouseButton(0) ? ArtifactoricsTex.ButtonPressed : ArtifactoricsTex.ButtonHover;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = enabled ? Ink : InkGrey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            ArtInk.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!enabled) return false;
            if (Widgets.ButtonInvisible(r))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            return false;
        }
    }
}
