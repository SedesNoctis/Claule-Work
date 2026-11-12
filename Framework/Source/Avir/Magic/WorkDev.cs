using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Hotfix251: dev tools to try the arcane works on a pawn. The ready ones only gave experience in the four small
    /// elements, so the works of Life and Light (and everything that needs them) could not be tried. Here: any element to any
    /// stage, a Life type in one step, and a list of what stands between this caster and each work.</summary>
    public static class WorkDev
    {
        private const string Cat = "Magic";
        private static readonly RuneKind[] Small = { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air };

        private static List<RuneDef> Elements()
        {
            var want = new[] { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air, RuneKind.Life, RuneKind.Light };
            return DefDatabase<RuneDef>.AllDefsListForReading.Where(r => r.role == RuneRole.Element && want.Contains(r.kind)).ToList();
        }

        private static void Refresh(Pawn p, Hediff_ArcaneCore c)
        {
            MageClass.Grow(c);
            c.arcana = c.Capacity;
            p.Notify_DisabledWorkTypesChanged();
        }

        [DebugAction(Cat, "Experience: set an element to a stage (Life and Light too)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SetStage(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null) { Messages.Message(p.LabelShortCap + " has no arcane core (make wizard or sorcerer first).", MessageTypeDefOf.RejectInput, false); return; }
            var opts = Elements().Select(e => new DebugMenuOption(e.LabelCap, DebugMenuOptionMode.Action, () => PickStage(p, c, e))).ToList();
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        private static void PickStage(Pawn p, Hediff_ArcaneCore c, RuneDef e)
        {
            var opts = new List<DebugMenuOption>();
            for (int i = 0; i < MageClass.StageNames.Length; i++)
            {
                int st = i;
                opts.Add(new DebugMenuOption(MageClass.StageNames[i] + " (" + MageClass.StageAt[i].ToString("0") + ")", DebugMenuOptionMode.Action, () =>
                {
                    c.xp[e.kind] = Mathf.Max(1f, MageClass.StageAt[st]);   // exact: it can lower too, to tune a test
                    Refresh(p, c);
                    Messages.Message(p.LabelShortCap + ": " + e.label + " is now " + MageClass.StageNames[st] + ".", MessageTypeDefOf.NeutralEvent, false);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        /// <summary>The deep meeting of Life (Wild Flame, Blood, Emerald, Pestilence) is the element this caster knows best with
        /// Life, and from Expert. Equal stages give the first element in the order fire, water, earth, air - so one element must
        /// stand above the others for a test to pick it.</summary>
        [DebugAction(Cat, "Life works: make this caster a deep type (choose element)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LifeType(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null) { Messages.Message(p.LabelShortCap + " has no arcane core (make wizard or sorcerer first).", MessageTypeDefOf.RejectInput, false); return; }
            var opts = Small.Select(k => new DebugMenuOption(k.ToString() + " (" + LifeWorksHint(k) + ")", DebugMenuOptionMode.Action, () =>
            {
                foreach (RuneKind s in Small) c.xp[s] = s == k ? MageClass.StageAt[3] : MageClass.StageAt[1];   // the chosen one Expert, the rest Apprentice
                c.xp[RuneKind.Life] = MageClass.StageAt[3];                                                   // Life Expert
                Refresh(p, c);
                Messages.Message(p.LabelShortCap + ": Life Expert, " + k + " Expert, the other three Apprentice.", MessageTypeDefOf.NeutralEvent, false);
            })).ToList();
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        private static string LifeWorksHint(RuneKind k) => k == RuneKind.Fire ? "Wild Flame" : k == RuneKind.Water ? "Blood" : k == RuneKind.Earth ? "Emerald statues" : "Pestilence";

        [DebugAction(Cat, "Arcane works: what stands between this caster and each work (all)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void WhyAll(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null) { Messages.Message(p.LabelShortCap + " has no arcane core (make wizard or sorcerer first).", MessageTypeDefOf.RejectInput, false); return; }
            var lines = new List<string>(); int can = 0, total = 0;
            foreach (WorkGiverDef d in DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(x => x.workType != null && x.workType.defName == "Avir_MagicWork").OrderBy(x => x.defName))
            {
                total++;
                bool known = ArcaneWork.KindsOf(d.defName, c) != null;
                string why = known ? ArcaneWork.Why(c, d.defName) : null;
                bool skip = false; string err = null;
                try { skip = d.Worker is WorkGiver_Scanner w && w.ShouldSkip(p, true); } catch (Exception e) { err = e.GetType().Name; }
                string note = ArcaneWork.OfGiver(d) == null ? " [no work group]" : "";
                string res;
                if (err != null) res = "ERROR in its own check (" + err + ")";
                else if (!skip && why == null) { res = "can"; can++; }
                else if (why != null) res = "NO: " + why;
                else res = "NO: its own conditions (" + (known ? "a spot, an item, cost" : "elements and stages are checked inside the work itself") + ")";
                lines.Add(d.defName.Replace("Avir_", "") + " - " + res + note);
            }
            string text = p.LabelShortCap + " - arcana " + c.arcana.ToString("0") + "/" + c.Capacity.ToString("0") + "; can do " + can + " of " + total + " works:\n\n" + string.Join("\n", lines);
            Log.Message("[Avir works] " + text);
            Find.WindowStack.Add(new Dialog_MessageBox(text));
        }
    }
}
