using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // Starting amulets and artifacts of chosen metals: two scenario parts ("start with Avir amulet",
    // "start with artifact") and the dev buttons of the Artifacts tab that set the metals of an
    // amulet or an artifact a pawn already has (after a character editor, for one).
    // =====================================================================================

    public static class ArtifactMetalChoice
    {
        public static IEnumerable<ArtifactMetalDef> Necklaces =>
            ArtifactoricsDatabase.Metals.Where(m => m.allowedAsNecklace);

        public static IEnumerable<ArtifactMetalDef> CoresFor(ArtifactMetalDef necklace) =>
            ArtifactoricsDatabase.Metals.Where(m => AvirAmuletCraft.CoreBlockReason(necklace, m) == null);

        public static IEnumerable<ArtifactMetalDef> Rims => ArtifactoricsDatabase.Metals.Where(m => m.allowedAsRim);

        public static IEnumerable<ArtifactMetalDef> HeartsFor(ArtifactMetalDef rim) =>
            ArtifactoricsDatabase.Metals.Where(m => m.allowedAsHeart && m != rim);

        public static void Menu(IEnumerable<ArtifactMetalDef> metals, Action<ArtifactMetalDef> pick)
        {
            var opts = metals.Select(m => new FloatMenuOption(m.LabelCap, () => pick(m))).ToList();
            if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
        }

        public static void QualityMenu(Action<QualityCategory> pick)
        {
            var opts = QualityUtility.AllQualityCategories.Select(q => new FloatMenuOption(q.GetLabel().CapitalizeFirst(), () => pick(q))).ToList();
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        /// <summary>New metals on an amulet. A switch between A'Shaign and a caste amulet keeps what
        /// still fits: the main path's caste becomes the caste, stones of other castes go.</summary>
        public static void SetAmuletMetals(CompAvirAmulet a, ArtifactMetalDef necklace, ArtifactMetalDef core)
        {
            if (a == null || necklace == null || core == null) return;
            a.necklaceMetal = necklace;
            a.coreMetal = core;
            if (a.Props.IsNamed) { a.ApplyNamedRules(); return; }
            if (necklace.MulticlassWith(core)) a.caste = null;
            else if (a.caste == null)
            {
                a.caste = a.subclass?.caste;
                if (a.caste != null && a.opened != null)
                    { foreach (AmuletSubclassDef b in a.opened.Keys.Where(b => b.caste != a.caste).ToList()) a.opened.Remove(b); a.spentLedger = -1; }
            }
        }

        public static void SetAmuletQuality(CompAvirAmulet a, QualityCategory q)
        {
            if (a == null) return;
            a.necklaceQuality = q;
            a.coreQuality = q;
            a.parent.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
        }

        public static ArtifactState NewArtifact(ArtifactMetalDef heart, ArtifactMetalDef rim, QualityCategory q)
        {
            var st = new ArtifactState { heartMetal = heart, rimMetal = rim, heartQuality = q, settingQuality = q };
            _ = st.Uid;
            return st;
        }
    }

    // ---------------------------------------------------------------- scenario parts

    public abstract class ScenPart_AvirArtifactoricsThing : ScenPart
    {
        public QualityCategory quality = QualityCategory.Normal;
        public int count = 1;

        protected static void Row(Rect r, int i, string label, string value, Action onClick)
        {
            Rect row = new Rect(r.x, r.y + i * RowHeight, r.width, RowHeight);
            Widgets.Label(row.LeftPart(0.35f), label);
            if (Widgets.ButtonText(row.RightPart(0.65f), value)) onClick();
        }

        protected void QualityAndCount(Rect r, int i)
        {
            Row(r, i, "Quality", quality.GetLabel().CapitalizeFirst(), () => ArtifactMetalChoice.QualityMenu(q => quality = q));
            Rect row = new Rect(r.x, r.y + (i + 1) * RowHeight, r.width, RowHeight);
            Widgets.Label(row.LeftPart(0.35f), "Count");
            string buf = count.ToString();
            Widgets.TextFieldNumeric(row.RightPart(0.65f), ref count, ref buf, 1f, 20f);
        }

        public override string Summary(Scenario scen) =>
            ScenSummaryList.SummaryWithList(scen, "PlayerStartsWith", ScenPart_StartingThing_Defined.PlayerStartWithIntro);

        public override IEnumerable<string> GetSummaryListEntries(string tag)
        {
            if (tag == "PlayerStartsWith") yield return SummaryEntry() + (count > 1 ? " x" + count : "");
        }

        protected abstract string SummaryEntry();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref quality, "quality", QualityCategory.Normal);
            Scribe_Values.Look(ref count, "count", 1);
        }
    }

    /// <summary>Start with a traditional Avir amulet of chosen necklace and core (and caste: none
    /// for an A'Shaign amulet, "chosen later" when left empty).</summary>
    public class ScenPart_AvirAmulet : ScenPart_AvirArtifactoricsThing
    {
        public ArtifactMetalDef necklace;
        public ArtifactMetalDef core;
        public AmuletCasteDef caste;

        private bool AShaign => necklace != null && necklace.MulticlassWith(core);

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            Rect r = listing.GetScenPartRect(this, RowHeight * 5f);
            Row(r, 0, "Necklace", necklace?.LabelCap ?? "-", () => ArtifactMetalChoice.Menu(ArtifactMetalChoice.Necklaces, m =>
            {
                necklace = m;
                if (core != null && AvirAmuletCraft.CoreBlockReason(necklace, core) != null) core = null;
            }));
            Row(r, 1, "Core", core?.LabelCap ?? "-", () => ArtifactMetalChoice.Menu(ArtifactMetalChoice.CoresFor(necklace), m => core = m));
            if (AShaign) Row(r, 2, "Caste", "A'Shaign (none)", () => { });
            else Row(r, 2, "Caste", caste?.LabelCap ?? "chosen later", () =>
            {
                var opts = new List<FloatMenuOption> { new FloatMenuOption("chosen later", () => caste = null) };
                opts.AddRange(AmuletCasteDef.All.Select(c => new FloatMenuOption(c.LabelCap, () => caste = c)));
                Find.WindowStack.Add(new FloatMenu(opts));
            });
            QualityAndCount(r, 3);
        }

        protected override string SummaryEntry() =>
            (necklace?.label ?? "?") + "-" + (core?.label ?? "?") + " Avir amulet (" + (AShaign ? "A'Shaign" : caste?.label ?? "caste later") + ", " + quality.GetLabel() + ")";

        public override IEnumerable<Thing> PlayerStartingThings()
        {
            if (necklace == null || core == null) yield break;
            for (int i = 0; i < count; i++)
            {
                Thing t = ThingMaker.MakeThing(AvirAmuletDefOf.Avir_TraditionalAmulet);
                CompAvirAmulet a = t.TryGetComp<CompAvirAmulet>();
                if (a == null) yield break;
                a.necklaceMetal = necklace;
                a.coreMetal = core;
                a.caste = AShaign ? null : caste;
                a.subclass = null;
                ArtifactMetalChoice.SetAmuletQuality(a, quality);
                yield return t;
            }
        }

        public override void Randomize()
        {
            necklace = ArtifactMetalChoice.Necklaces.RandomElementWithFallback();
            core = ArtifactMetalChoice.CoresFor(necklace).RandomElementWithFallback();
            caste = AmuletCasteDef.All.RandomElementWithFallback();
            quality = QualityCategory.Normal;
            count = 1;
        }

        public override bool HasNullDefs() => base.HasNullDefs() || necklace == null || core == null;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref necklace, "necklace");
            Scribe_Defs.Look(ref core, "core");
            Scribe_Defs.Look(ref caste, "caste");
        }

        public override int GetHashCode() =>
            base.GetHashCode() ^ (necklace?.shortHash ?? 0) ^ ((core?.shortHash ?? 0) << 8) ^ ((caste?.shortHash ?? 0) << 16) ^ ((int)quality << 24) ^ count;
    }

    /// <summary>Start with a loose artifact of a chosen heart and rim (inlay it at the artificer's table).</summary>
    public class ScenPart_AvirArtifact : ScenPart_AvirArtifactoricsThing
    {
        public ArtifactMetalDef heart;
        public ArtifactMetalDef rim;

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            Rect r = listing.GetScenPartRect(this, RowHeight * 4f);
            Row(r, 0, "Rim", rim?.LabelCap ?? "-", () => ArtifactMetalChoice.Menu(ArtifactMetalChoice.Rims, m =>
            {
                rim = m;
                if (heart == rim) heart = null;
            }));
            Row(r, 1, "Heart", heart?.LabelCap ?? "-", () => ArtifactMetalChoice.Menu(ArtifactMetalChoice.HeartsFor(rim), m => heart = m));
            QualityAndCount(r, 2);
        }

        protected override string SummaryEntry() =>
            "artifact: " + (heart?.label ?? "?") + " heart, " + (rim?.label ?? "?") + " rim (" + quality.GetLabel() + ")";

        public override IEnumerable<Thing> PlayerStartingThings()
        {
            if (heart == null || rim == null) yield break;
            for (int i = 0; i < count; i++)
            {
                Thing t = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_Artifact);
                CompArtifact c = t.TryGetComp<CompArtifact>();
                if (c == null) yield break;
                c.state = ArtifactMetalChoice.NewArtifact(heart, rim, quality);
                yield return t;
            }
        }

        public override void Randomize()
        {
            rim = ArtifactMetalChoice.Rims.RandomElementWithFallback();
            heart = ArtifactMetalChoice.HeartsFor(rim).RandomElementWithFallback();
            quality = QualityCategory.Normal;
            count = 1;
        }

        public override bool HasNullDefs() => base.HasNullDefs() || heart == null || rim == null;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref heart, "heart");
            Scribe_Defs.Look(ref rim, "rim");
        }

        public override int GetHashCode() =>
            base.GetHashCode() ^ (heart?.shortHash ?? 0) ^ ((rim?.shortHash ?? 0) << 8) ^ ((int)quality << 24) ^ count;
    }
}
