using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    // =====================================================================================
    // THE SANCTUARY. A stone ritual circle (medium). Its one rite - the binding - pours energy into it and binds it to
    // the element chosen on it; the element's rune lies at its centre, drawn by the stone itself (the ritualist's
    // drawing is untouched). Filled, it holds the element's stream: the energy circles over the stone in its colour,
    // fading slowly, and lights the ground. A mage meditating beside it draws on that store. Empty, it stays bound -
    // dark and quiet - until filled again. Only with magic on.
    // =====================================================================================
    public class Building_Sanctuary : Building
    {
        /// <summary>Plain stone: whatever stone it is built of, it is not tinted.</summary>
        public override Color DrawColor { get => Color.white; set { } }
    }

    public class CompProperties_Sanctuary : CompProperties
    {
        public float capacity = 500f;      // ritual energy it holds
        public float fadePerDay = 50f;     // what it loses on its own
        public float reach = 7f;           // how near a mage must meditate
        public CompProperties_Sanctuary() { compClass = typeof(CompSanctuary); }
    }

    public class CompSanctuary : ThingComp, IThingGlower
    {
        public RuneDef element;            // bound to
        public RuneDef chosen;             // the binding will bind to this
        public float stored;

        public CompProperties_Sanctuary Props => (CompProperties_Sanctuary)props;
        public float Fill => stored / Props.capacity;
        public bool Lit => AvirMagic.On && element != null && stored > Props.capacity * 0.02f;

        public static readonly HashSet<CompSanctuary> All = new HashSet<CompSanctuary>();

        public override void PostSpawnSetup(bool respawningAfterLoad) { base.PostSpawnSetup(respawningAfterLoad); All.Add(this); Relight(); }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish) { base.PostDeSpawn(map, mode); All.Remove(this); }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref element, "element");
            Scribe_Defs.Look(ref chosen, "chosen");
            Scribe_Values.Look(ref stored, "stored");
        }

        public bool ShouldBeLitNow() => Lit;

        private bool wasLit;
        private void Relight()
        {
            CompGlower g = parent.GetComp<CompGlower>();
            if (g == null || !parent.Spawned) return;
            if (element != null) g.GlowColor = ColorInt(element.color);
            if (wasLit != Lit) { wasLit = Lit; g.UpdateLit(parent.Map); }
        }

        private static ColorInt ColorInt(Color c) => new ColorInt((int)(c.r * 255f), (int)(c.g * 255f), (int)(c.b * 255f), 0);

        /// <summary>The binding pours into the stone: to the chosen element (another one binds it afresh, empty).</summary>
        public float Pour(float amount)
        {
            RuneDef to = chosen ?? element;
            if (to == null || amount <= 0f) return 0f;
            if (element != to)
            {
                element = to; stored = 0f;
                CompGlower g = parent.GetComp<CompGlower>();
                if (g != null) { g.GlowColor = ColorInt(to.color); if (g.Glows) { parent.Map.glowGrid.DeRegisterGlower(g); parent.Map.glowGrid.RegisterGlower(g); } }
            }
            float give = Mathf.Min(amount, Props.capacity - stored);
            stored += give;
            Relight();
            return give;
        }

        public bool WantsPour => (chosen ?? element) != null && (chosen != null && chosen != element || stored < Props.capacity - 0.5f);

        /// <summary>A mage draws from it while meditating near.</summary>
        public float Draw(float want)
        {
            float g = Mathf.Min(want, stored);
            stored -= g;
            Relight();
            return g;
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (stored > 0f) { stored = Mathf.Max(0f, stored - Props.fadePerDay * 250f / GenDate.TicksPerDay); }
            Relight();
        }

        public static CompSanctuary Near(Pawn p, RuneDef prefer = null)
        {
            if (p?.Map == null) return null;
            CompSanctuary best = null;
            foreach (CompSanctuary s in All)
            {
                if (s.parent.Map != p.Map || !s.Lit || (s.parent.Position - p.Position).LengthHorizontal > s.Props.reach) continue;
                if (best == null || s.stored > best.stored) best = s;
            }
            return best;
        }

        // ---------------------------------------------------------------- looks

        private static Texture2D flow;
        private static bool looked;
        private static Texture2D Flow
        {
            get
            {
                if (looked) return flow;
                looked = true;
                flow = ContentFinder<Texture2D>.Get("Things/Building/Magic/SanctuaryFlow", false) ?? MakeFlow();
                return flow;
            }
        }

        /// <summary>A plain stand-in for the flow art: soft rings and a bright centre.</summary>
        private static Texture2D MakeFlow()
        {
            const int n = 256;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                {
                    float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / (n / 2f);
                    float ang = Mathf.Atan2(dy, dx);
                    float swirl = 0.5f + 0.5f * Mathf.Sin(ang * 4f + d * 14f);
                    float a = Mathf.Clamp01(1f - d) * (0.25f + 0.55f * swirl * Mathf.Clamp01(d * 2f)) + Mathf.Clamp01(1f - d * 4f) * 0.8f;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            t.Apply();
            return t;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (!AvirMagic.On || element == null) return;
            Vector3 c = parent.DrawPos;
            float f = Mathf.Clamp01(Fill);
            // the element's rune at the centre, always (bright while it holds a store, faint when empty)
            Color rc = new Color(element.color.r, element.color.g, element.color.b, Lit ? 0.95f : 0.35f);
            Vector3 cc = c; cc.y = AltitudeLayer.FloorEmplacement.AltitudeFor() + 0.03f;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(cc, Quaternion.identity, new Vector3(1.1f, 1f, 1.1f)),
                MaterialPool.MatFrom(Lit ? element.Glow : element.Ink, ShaderDatabase.Transparent, rc), 0);
            if (!Lit) return;
            // the stream circling over the stone
            float t = Time.realtimeSinceStartup;
            float pulse = 0.8f + 0.2f * Mathf.Sin(t * 1.7f);
            Color fc = new Color(element.color.r, element.color.g, element.color.b, (0.25f + 0.55f * f) * pulse);
            Vector3 fp = c; fp.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            Material m = MaterialPool.MatFrom(Flow, ShaderDatabase.MoteGlow, fc);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(fp, Quaternion.AngleAxis(t * 20f % 360f, Vector3.up), new Vector3(5.2f, 1f, 5.2f)), m, 0);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(fp + new Vector3(0f, 0.001f, 0f), Quaternion.AngleAxis(-t * 13f % 360f, Vector3.up), new Vector3(3.6f, 1f, 3.6f)), m, 0);
        }

        public override string CompInspectStringExtra()
        {
            if (!AvirMagic.On) return null;
            if (element == null) return "Unbound sanctuary" + (chosen != null ? " - the binding will bind it to " + chosen.label : " - choose an element, then draw its binding");
            string s = "Sanctuary of " + element.label + ": " + Fill.ToStringPercent() + (Lit ? "" : " - empty, dark");
            if (chosen != null && chosen != element) s += "\nThe next binding rebinds it to " + chosen.label + ".";
            return s;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (!AvirMagic.On || parent.Faction != Faction.OfPlayer) yield break;
            RuneDef show = chosen ?? element;
            yield return new Command_Action
            {
                defaultLabel = show == null ? "Element..." : "Element: " + show.LabelCap,
                defaultDesc = "Which element the binding binds this sanctuary to. Its rune lies at the centre of the stone. Choosing another rebinds the sanctuary - empty - with the next binding.",
                icon = show?.Glow ?? DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Fire")?.Glow ?? BaseContent.BadTex,
                defaultIconColor = show?.color ?? Color.white,
                action = () => Find.WindowStack.Add(new FloatMenu(RuneDef.All.Where(r => r.role == RuneRole.Element)
                    .Select(r => new FloatMenuOption(r.LabelCap, () => chosen = r, r.Glow, r.color)).ToList()))
            };
            CompRitualCircle circle = parent.GetComp<CompRitualCircle>();
            RitualPresetDef binding = DefDatabase<RitualPresetDef>.GetNamedSilentFail("Avir_Ritual_SanctuaryBinding");
            if (circle != null && binding != null)
                yield return new Command_Action
                {
                    defaultLabel = "Sanctuary rituals...",
                    defaultDesc = "The sanctuary's own rites, apart from the ritual book: its binding. Choose it to draw it into the stone.",
                    icon = binding.rings[0].runes[0].Ink,
                    action = () => Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        new FloatMenuOption(binding.LabelCap + (show != null ? " (" + show.label + ")" : ""), () =>
                        {
                            if (show == null) { Messages.Message("Choose the element first.", MessageTypeDefOf.RejectInput, false); return; }
                            circle.Assign(binding.Schema());
                        })
                    }))
                };
        }
    }

    /// <summary>A thing of magic: it cannot be placed while magic is off.</summary>
    public class PlaceWorker_MagicOn : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef def, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
            => AvirMagic.On ? AcceptanceReport.WasAccepted : new AcceptanceReport("Magic is off (mod settings).");
    }

    /// <summary>With magic off, the things of magic are not in the build menu at all.</summary>
    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.Visible), MethodType.Getter)]
    public static class Patch_MagicBuildHidden
    {
        public static void Postfix(Designator_Build __instance, ref bool __result)
        {
            if (__result && !AvirMagic.On && __instance.PlacingDef is ThingDef td && td.placeWorkers != null && td.placeWorkers.Contains(typeof(PlaceWorker_MagicOn))) __result = false;
        }
    }
}
