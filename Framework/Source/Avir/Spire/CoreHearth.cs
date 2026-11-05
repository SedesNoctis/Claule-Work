using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;
using Avir.Artifactorics;

namespace Avir.Spire
{
    // =====================================================================================
    // THE CORE HEARTH. A 3x3 artifact set into the ground, with containers in its middle for up to three
    // kindled spire cores. It does not fly and is not part of a spire: it is a battery for the colony's
    // electricity - 6000 Wd for every core, up to 18000 - and a node of the stream network as well.
    //  - it charges from the grid like any battery, from the stream (a Fill ritual, a vortex) and, in the void
    //    of space, by itself;
    //  - it is drained by what the grid feeds - shields first among them;
    //  - now and then it cracks (an event): it goes dark and holds nothing until the stream mends it - a ritual
    //    that Fills it (by reach, by thread or through the network) pours into the cracks until they close.
    // =====================================================================================

    /// <summary>Anything a kindled spire core can be sunk into (the cradle, the hearth).</summary>
    public interface ISpireCoreHolder
    {
        bool CanSink(Thing core);
        void Sink(Thing core);
    }

    public class CompProperties_CoreHearth : CompProperties_Battery
    {
        public int maxCores = 3;
        public float wdPerCore = 6000f;
        /// <summary>Wd a day each core gathers by itself in the void.</summary>
        public float spaceWdPerCorePerDay = 2400f;
        /// <summary>Stream energy that mends a crack.</summary>
        public float mendCost = 600f;
        /// <summary>Where the three hollows are on the hearth's picture (cells from its centre; measured on the 1024 px
        /// sprite, 3 cells wide) and the width of a hollow: a core's light fills it, as a stone's does in a totem.
        /// Fine-tune in the game with "Dev: tune core glow" (kept in the mod settings) or in the def.</summary>
        public List<Vector2> coreOffsets = new List<Vector2> { new Vector2(0f, 0.94f), new Vector2(-0.69f, -0.30f), new Vector2(0.69f, -0.30f) };
        public float coreSize = 0.52f;
        /// <summary>The colour of a kindled core's light.</summary>
        public Color glowColor = new Color(0.62f, 0.52f, 0.98f);
        public CompProperties_CoreHearth() { compClass = typeof(CompCoreHearth); }
    }

    public class CompCoreHearth : CompPowerBattery, IThingHolder, ISpireCoreHolder
    {
        public ThingOwner<Thing> cores;
        public bool cracked;
        public float mended;

        public CompCoreHearth() { cores = new ThingOwner<Thing>(this, false); }

        public CompProperties_CoreHearth HProps => (CompProperties_CoreHearth)props;

        public override void Initialize(CompProperties p)
        {
            // each hearth gets its own copy of the battery's properties: its capacity follows its own cores
            var copy = (CompProperties_CoreHearth)HarmonyLib.AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(p, null);
            base.Initialize(copy);
            UpdateCapacity();
        }

        public void UpdateCapacity()
        {
            float cap = cracked ? 1f : Mathf.Max(1f, HProps.wdPerCore * (cores?.Count ?? 0));
            ((CompProperties_Battery)props).storedEnergyMax = cap;
            if (StoredEnergy > cap) DrawPower(StoredEnergy - cap);
        }

        public bool InSpace => parent.Spawned && parent.Map.Biome != null && parent.Map.Biome.inVacuum;

        // ------------------------------------------------------------------ cores

        public bool CanSink(Thing t) => !cracked && t?.TryGetComp<CompSpireCore>()?.kindled == true && cores.Count < HProps.maxCores;

        public void Sink(Thing core)
        {
            if (!CanSink(core)) return;
            if (core.Spawned) core.DeSpawn();
            core.holdingOwner?.Remove(core);
            cores.TryAdd(core, false);
            UpdateCapacity();
            if (parent.Spawned) RitualFx.Ripple(parent.DrawPos, parent.Map, 2f);
            Messages.Message("A spire core settles into the hearth (" + cores.Count + " / " + HProps.maxCores + ").", parent, MessageTypeDefOf.PositiveEvent, false);
        }

        public ThingOwner GetDirectlyHeldThings() => cores;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        // ------------------------------------------------------------------ the crack

        /// <summary>The hearth cracks: its charge spills out and it holds nothing until mended.</summary>
        public void Crack()
        {
            if (cracked || !parent.Spawned) return;
            cracked = true; mended = 0f;
            float spilled = StoredEnergy;
            UpdateCapacity();
            GenExplosion.DoExplosion(parent.Position, parent.Map, 2.4f, DamageDefOf.EMP, null, 10);
            FleckMaker.ThrowLightningGlow(parent.DrawPos, parent.Map, 3f);
            Find.LetterStack.ReceiveLetter("Core hearth cracked",
                "The containers of " + parent.LabelShort + " have cracked" + (spilled > 1f ? " and its charge (" + spilled.ToString("0") + " Wd) has spilled out" : "") +
                ". It holds no charge until it is mended: a ritual that Fills it - reaching it, by thread, or through the stream network - pours into the cracks until they close (" + HProps.mendCost.ToString("0") + " energy).",
                LetterDefOf.NegativeEvent, parent);
        }

        /// <summary>Stream energy poured into a cracked hearth mends it; returns what it took.</summary>
        public float Mend(float amount)
        {
            if (!cracked) return 0f;
            float take = Mathf.Min(amount, HProps.mendCost - mended);
            mended += take;
            if (mended >= HProps.mendCost - 0.01f)
            {
                cracked = false; mended = 0f;
                UpdateCapacity();
                if (parent.Spawned) RitualFx.Ripple(parent.DrawPos, parent.Map, 3f);
                Messages.Message(parent.LabelShortCap + " is mended: its containers are whole again.", parent, MessageTypeDefOf.PositiveEvent, false);
            }
            return take;
        }

        // ------------------------------------------------------------------ drawing: the cores' light in their hollows
        // No sprite is laid over the hearth any more (it sat crooked on the stone): each kindled core is a light in its
        // hollow, drawn exactly as the stones in the artifact totems (SocketDraw) - the energy, a slow shimmer, a halo.
        // It burns brighter the fuller the hearth is; a cracked hearth only gutters, grey.

        /// <summary>The def's own settings (the tuning window changes these, every hearth follows).</summary>
        private CompProperties_CoreHearth Look => parent.def.GetCompProperties<CompProperties_CoreHearth>() ?? HProps;

        public override void PostDraw()
        {
            base.PostDraw();
            if (cores.Count == 0) return;
            CompProperties_CoreHearth lp = Look;
            float now = Time.realtimeSinceStartup;
            float full = Props.storedEnergyMax > 0f ? Mathf.Clamp01(StoredEnergy / Props.storedEnergyMax) : 0f;
            float live = cracked ? 0f : Mathf.Lerp(0.45f, 1f, full);
            for (int i = 0; i < cores.Count && i < lp.coreOffsets.Count; i++)
            {
                Vector2 o = lp.coreOffsets[i];
                Vector3 pos = parent.DrawPos + new Vector3(o.x, SocketDraw.Lift, o.y);
                float size = lp.coreSize;
                Color c = lp.glowColor;
                if (cracked)
                {
                    Color dead = Color.Lerp(c, Color.gray, 0.8f);
                    dead.a = 0.25f + 0.2f * Mathf.Abs(Mathf.Sin(now * 5f + i * 2.1f)) * (Mathf.Sin(now * 1.3f + i) > 0.3f ? 1f : 0.3f);
                    SocketDraw.Quad(pos, size, 0f, SocketDraw.Energy, dead);
                    continue;
                }
                Color fill = Color.Lerp(Color.Lerp(c, Color.gray, 0.5f), c, live);
                fill.a = Mathf.Lerp(0.6f, 0.95f, live);
                SocketDraw.Quad(pos, size, 0f, SocketDraw.Energy, fill);
                Color w = Color.Lerp(c, Color.white, 0.25f); w.a = 0.5f * live;
                SocketDraw.Quad(pos + new Vector3(0f, 0.003f, 0f), size * 0.95f, now * 40f + i * 47f, SocketDraw.Shimmer, w);
                Color w2 = Color.Lerp(c, Color.white, 0.5f); w2.a = 0.32f * live;
                SocketDraw.Quad(pos + new Vector3(0f, 0.006f, 0f), size * 0.8f, -now * 65f + i * 91f, SocketDraw.Shimmer, w2);
                Color halo = c; halo.a = (0.2f + 0.06f * Mathf.Sin(now * 1.7f + i)) * Mathf.Lerp(0.6f, 1f, full);
                SocketDraw.Quad(pos + new Vector3(0f, 0.009f, 0f), size * 1.3f, 0f, SocketDraw.Halo, halo);
            }
        }

        // ------------------------------------------------------------------ ticks

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.IsHashIntervalTick(250) || cracked || cores.Count == 0) return;
            if (InSpace) AddEnergy(HProps.spaceWdPerCorePerDay * cores.Count * 250f / 60000f);
        }

        // ------------------------------------------------------------------ gizmos and text

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;
            var sink = new Command_Action
            {
                defaultLabel = "Sink a core (" + cores.Count + "/" + HProps.maxCores + ")",
                defaultDesc = "Have a colonist carry a kindled spire core into one of the hearth's containers. Each core holds " + HProps.wdPerCore.ToString("0") + " Wd.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/SinkCore", false) ?? BaseContent.BadTex,
                action = () => SpireCoreUtil.ChooseCoreFor(parent, this)
            };
            if (cracked) sink.Disable("cracked - mend it first");
            else if (cores.Count >= HProps.maxCores) sink.Disable("all containers are full");
            yield return sink;
            if (cores.Count > 0)
                yield return new Command_Action
                {
                    defaultLabel = "Take a core out",
                    defaultDesc = "Lift a core out of the hearth. The hearth's capacity falls with it; what it holds above that is lost.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/TakeCore", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        var opts = new List<FloatMenuOption>();
                        foreach (Thing t in cores.InnerListForReading.ToList())
                            opts.Add(new FloatMenuOption(t.LabelCap, () =>
                            {
                                cores.TryDrop(t, parent.Position, parent.Map, ThingPlaceMode.Near, out Thing _);
                                UpdateCapacity();
                            }));
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                };
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: sink a kindled core",
                    action = () =>
                    {
                        Thing core = ThingMaker.MakeThing(ThingDef.Named("Avir_SpireCore"));
                        CompSpireCore c = core.TryGetComp<CompSpireCore>();
                        c.kindled = true; c.progress = CompSpireCore.KindleCost; c.power = 1.5f; c.capacity = 3000f; c.efficiency = 0.9f; c.ward = 1.5f; c.reach = 1.25f;
                        Sink(core);
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "Dev: tune core glow",
                    defaultDesc = "Move the three lights, change their size, until they sit in the hollows. Kept in the mod settings.",
                    action = () => Find.WindowStack.Add(new Dialog_TuneHearthGlow(parent.def))
                };
                yield return new Command_Action { defaultLabel = "Dev: fill the hearth", action = () => AddEnergy(AmountCanAccept) };
                yield return new Command_Action { defaultLabel = cracked ? "Dev: mend" : "Dev: crack", action = () => { if (cracked) Mend(HProps.mendCost); else Crack(); } };
            }
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            sb.Append("Spire cores: ").Append(cores.Count).Append(" / ").Append(HProps.maxCores);
            if (cracked) sb.Append("\nCracked: holds nothing. Mended ").Append(mended.ToString("0")).Append(" / ").Append(HProps.mendCost.ToString("0")).Append(" - a Fill ritual reaching it pours into the cracks.");
            else
            {
                string b = base.CompInspectStringExtra();
                if (!b.NullOrEmpty()) sb.Append("\n").Append(b.TrimEnd());
                if (InSpace && cores.Count > 0) sb.Append("\nIn the void it gathers ").Append((HProps.spaceWdPerCorePerDay * cores.Count).ToString("0")).Append(" Wd a day by itself.");
            }
            return sb.ToString();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (previousMap != null && cores.Count > 0) cores.TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref cores, "cores", this);
            Scribe_Values.Look(ref cracked, "cracked");
            Scribe_Values.Look(ref mended, "mended");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (cores == null) cores = new ThingOwner<Thing>(this, false);
                UpdateCapacity();
            }
        }
    }

    public static class SpireCoreUtil
    {
        /// <summary>Pick a kindled core on the map and have a colonist carry it into the holder.</summary>
        public static void ChooseCoreFor(ThingWithComps holder, ISpireCoreHolder h)
        {
            var opts = new List<FloatMenuOption>();
            foreach (Thing t in holder.Map.listerThings.ThingsOfDef(ThingDef.Named("Avir_SpireCore")).ToList())
            {
                CompSpireCore c = t.TryGetComp<CompSpireCore>();
                if (c == null || !c.kindled || t.IsForbidden(Faction.OfPlayer) || !h.CanSink(t)) continue;
                opts.Add(new FloatMenuOption(t.LabelCap + " - power " + c.power.ToString("0.00"), () =>
                {
                    Pawn p = holder.Map.mapPawns.FreeColonistsSpawned
                        .Where(x => !x.Downed && x.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Deadly) && x.CanReach(holder, PathEndMode.Touch, Danger.Deadly))
                        .OrderBy(x => x.Position.DistanceToSquared(t.Position)).FirstOrDefault();
                    if (p == null) { Messages.Message("No colonist can carry it there.", holder, MessageTypeDefOf.RejectInput, false); return; }
                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_SinkSpireCore"), t, holder);
                    job.count = 1;
                    p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("No kindled spire core on this map", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }
    }

    /// <summary>Now and then a core hearth cracks.</summary>
    public class IncidentWorker_CoreHearthCracks : IncidentWorker
    {
        private static IEnumerable<CompCoreHearth> Hearths(Map map) =>
            map.listerBuildings.allBuildingsColonist.Select(b => b.TryGetComp<CompCoreHearth>()).Where(h => h != null && !h.cracked && h.cores.Count > 0);

        protected override bool CanFireNowSub(IncidentParms parms) => parms.target is Map map && Hearths(map).Any();

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map)) return false;
            CompCoreHearth h = Hearths(map).RandomElementWithFallback();
            if (h == null) return false;
            h.Crack();
            return true;
        }
    }

    /// <summary>The dev window for the hearth's lights: arrows move the chosen one, +/- the size of the hollows.
    /// Like the artifact totems' socket tuning (Dialog_TuneSockets), and kept the same way.</summary>
    public class Dialog_TuneHearthGlow : Window
    {
        private readonly ThingDef def;
        private readonly CompProperties_CoreHearth lp;
        private int sel;
        private float step = 0.02f;

        public Dialog_TuneHearthGlow(ThingDef def)
        {
            this.def = def;
            lp = def.GetCompProperties<CompProperties_CoreHearth>();
            while (lp.coreOffsets.Count < lp.maxCores) lp.coreOffsets.Add(Vector2.zero);
            doCloseX = true; draggable = true; preventCameraMotion = false; absorbInputAroundWindow = false; closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(360f, 250f + 26f * lp.maxCores);

        public override void PostClose()
        {
            base.PostClose();
            HearthGlowStore.Save(def, lp);
        }

        public override void DoWindowContents(Rect r)
        {
            var offs = lp.coreOffsets;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x, r.y, r.width, 24f), def.defName + " - core lights");
            float y = r.y + 28f;
            for (int i = 0; i < lp.maxCores; i++)
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
            Rect sz = new Rect(r.x + 3 * b + 10f, y + 32f, 120f, 30f);
            if (Widgets.ButtonText(new Rect(sz.x, sz.y, 30f, 30f), "-")) lp.coreSize = Mathf.Max(0.05f, lp.coreSize - 0.01f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(sz.x + 30f, sz.y, 60f, 30f), lp.coreSize.ToString("0.00"));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(sz.x + 90f, sz.y, 30f, 30f), "+")) lp.coreSize = Mathf.Min(1.5f, lp.coreSize + 0.01f);
            if (Widgets.ButtonText(new Rect(r.x + 3 * b + 10f, y + 64f, 120f, 30f), "Copy XML"))
            {
                string xml = "<coreOffsets>" + string.Concat(offs.Take(lp.maxCores).Select(v => "<li>(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ")</li>")) + "</coreOffsets>\n<coreSize>" + lp.coreSize.ToString("0.00") + "</coreSize>";
                GUIUtility.systemCopyBuffer = xml;
                Log.Message("[Avir] " + def.defName + " core lights:\n" + xml);
                Messages.Message("Core light offsets copied (and written to the log).", MessageTypeDefOf.SilentInput, false);
            }
        }
    }

    /// <summary>The hearth's light tuning, kept in the mod settings next to the totems' (the same line format).</summary>
    [StaticConstructorOnStartup]
    public static class HearthGlowStore
    {
        static HearthGlowStore()
        {
            try
            {
                foreach (string line in AvirMod.Settings?.socketTuning ?? new List<string>())
                {
                    string[] parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    CompProperties_CoreHearth lp = DefDatabase<ThingDef>.GetNamedSilentFail(parts[0])?.GetCompProperties<CompProperties_CoreHearth>();
                    if (lp == null) continue;
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    lp.coreSize = float.Parse(parts[1], ci);
                    var offs = new List<Vector2>();
                    foreach (string v in parts[2].Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] xy = v.Split(',');
                        offs.Add(new Vector2(float.Parse(xy[0], ci), float.Parse(xy[1], ci)));
                    }
                    if (offs.Count > 0) lp.coreOffsets = offs;
                }
            }
            catch (System.Exception e) { Log.Warning("[Avir] Core hearth tuning could not be read: " + e.Message); }
        }

        public static void Save(ThingDef def, CompProperties_CoreHearth lp)
        {
            AvirSettings st = AvirMod.Settings;
            if (st == null || def == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string line = def.defName + "|" + lp.coreSize.ToString("0.000", ci) + "|"
                          + string.Join(";", lp.coreOffsets.Take(lp.maxCores).Select(v => v.x.ToString("0.000", ci) + "," + v.y.ToString("0.000", ci)));
            st.socketTuning.RemoveAll(l => l.StartsWith(def.defName + "|"));
            st.socketTuning.Add(line);
            st.Write();
        }
    }
}
