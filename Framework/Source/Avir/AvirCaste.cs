using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// CASTES. An Avir's caste is decided by its egg, not by its place in society:
    ///   the first egg of a clutch is the ELDER (defenders, legionaries, warriors - an elder female is always a huntress),
    ///   the middle egg of a clutch of three is the CENTRAL (rare: stewards between the castes - traders, priests),
    ///   the last egg is the YOUNGER (artists, artificers, builders).
    /// None stands above the others: each needs the other two. The mother knows which egg is which, so the caste is
    /// written on the egg when it is laid and the chick takes it whatever order the eggs hatch in.
    ///
    /// Living outside one's caste is heresy. Neutral work (hauling, cleaning, plants, animals, cooking, medicine,
    /// mining) counts for nothing; only experience earned in ANOTHER caste's domain builds up, and it fades on its
    /// own - a warrior hauling between raids never strays. One who truly lives another caste's life is marked an
    /// apostate, the way Fallius was. The caste itself never changes: an egg is what it was. The mark lifts once the
    /// pawn has long returned to its own work.
    /// </summary>
    public enum AvirCaste : byte { None = 0, Elder = 1, Central = 2, Younger = 3 }

    public static class AvirCasteUtility
    {
        /// <summary>Foreign-domain experience that turns a pawn: about ten days of full-time work in another caste's domain.</summary>
        public const float ApostasyThreshold = 40000f;
        /// <summary>Share of the foreign experience that fades each day.</summary>
        public const float DailyFade = 0.07f;

        private static HediffDef def;
        public static HediffDef Def => def ?? (def = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Caste"));

        public static string Label(AvirCaste c) =>
            c == AvirCaste.Elder ? "elder" : c == AvirCaste.Central ? "central" : c == AvirCaste.Younger ? "younger" : "none";

        /// <summary>Avir proper: Fearborn, and not a harpy (harpies have no castes).</summary>
        public static bool HasCaste(Pawn p) =>
            p?.health?.hediffSet != null && AvirRhythmUtility.IsAvir(p) && !AvirHarpyRules.IsHarpy(p.genes?.Xenotype);

        public static Hediff_AvirCaste Get(Pawn p) =>
            Def == null || p?.health?.hediffSet == null ? null : p.health.hediffSet.GetFirstHediffOfDef(Def) as Hediff_AvirCaste;

        public static AvirCaste CasteOf(Pawn p) => Get(p)?.caste ?? AvirCaste.None;

        public static Hediff_AvirCaste Assign(Pawn p, AvirCaste caste)
        {
            if (Def == null || p?.health == null || caste == AvirCaste.None) return null;
            Hediff_AvirCaste h = Get(p);
            if (h == null)
            {
                h = (Hediff_AvirCaste)HediffMaker.MakeHediff(Def, p);
                h.caste = caste;
                h.bornCaste = caste;
                p.health.AddHediff(h);
            }
            else
            {
                // the egg's word overrules a guess made a moment earlier, when the chick spawned
                h.caste = caste;
                if (!h.apostate) h.bornCaste = caste;
            }
            return h;
        }

        /// <summary>The caste whose domain a skill belongs to; None for neutral skills.</summary>
        public static AvirCaste DomainOf(SkillDef s)
        {
            if (s == null) return AvirCaste.None;
            if (s == SkillDefOf.Melee || s == SkillDefOf.Shooting) return AvirCaste.Elder;
            if (s == SkillDefOf.Social) return AvirCaste.Central;
            if (s == SkillDefOf.Crafting || s == SkillDefOf.Artistic || s == SkillDefOf.Construction) return AvirCaste.Younger;
            return AvirCaste.None;
        }

        /// <summary>For an Avir that came without an egg (a recruit, a visitor, one from before castes existed):
        /// grown ones by the domain they are best at, chicks by the clutch odds (two eggs in three clutches,
        /// three in one: elder and younger as common as each other, central the rarest).</summary>
        public static AvirCaste Guess(Pawn p)
        {
            if (p.skills == null || !p.DevelopmentalStage.Adult())
            {
                float r = Rand.Value;
                return r < 0.43f ? AvirCaste.Elder : r < 0.57f ? AvirCaste.Central : AvirCaste.Younger;
            }
            float Lv(SkillDef s) => p.skills.GetSkill(s)?.Level ?? 0;
            float elder = Mathf.Max(Lv(SkillDefOf.Melee), Lv(SkillDefOf.Shooting));
            float central = Lv(SkillDefOf.Social) - 2f;   // the rare one: it has to stand clearly out
            float younger = Mathf.Max(Lv(SkillDefOf.Crafting), Mathf.Max(Lv(SkillDefOf.Artistic), Lv(SkillDefOf.Construction)));
            if (central > elder && central > younger) return AvirCaste.Central;
            return elder >= younger ? AvirCaste.Elder : AvirCaste.Younger;
        }

        public static void EnsureCaste(Pawn p)
        {
            if (!HasCaste(p) || Get(p) != null) return;
            Assign(p, Guess(p));
        }
    }

    /// <summary>The caste mark. Visible on the health tab: elder / central / younger caste, and (apostate).</summary>
    public class Hediff_AvirCaste : HediffWithComps
    {
        public AvirCaste caste;
        public AvirCaste bornCaste;
        public bool apostate;
        /// <summary>Foreign-domain experience, by caste index (1..3).</summary>
        public float[] foreign = new float[4];

        public override string LabelBase => AvirCasteUtility.Label(caste) + " caste";

        public override string LabelInBrackets
        {
            get
            {
                return apostate ? "apostate" : null;
            }
        }

        public override string Description
        {
            get
            {
                string d = caste == AvirCaste.Elder
                    ? "Hatched from the first egg of the clutch: a defender, a legionary, a warrior" + (pawn?.gender == Gender.Female ? " - a huntress, as every elder female is." : ".")
                    : caste == AvirCaste.Central
                        ? "Hatched from the middle egg, the rarest: one who stands between the castes and orders their exchange - a trader, a priest, a steward."
                        : "Hatched from the last egg of the clutch: an artist, an artificer, a builder.";
                d += "\n\nNo caste stands above another: the warrior has no gear without the artificer and nothing at all without the steward; the young cannot give themselves to their arts without the elder; the central can do nothing without both.";
                if (apostate) d += "\n\nThis one has long lived another caste's life and forsaken its own. An apostate - until it returns to its own work for good.";
                float worst = 0f; int at = 0;
                for (int i = 1; i < 4; i++) if (i != (int)caste && foreign[i] > worst) { worst = foreign[i]; at = i; }
                if (worst > AvirCasteUtility.ApostasyThreshold * 0.25f)
                    d += apostate
                        ? "\n\nStill living the " + AvirCasteUtility.Label((AvirCaste)at) + " caste's life (" + (worst / AvirCasteUtility.ApostasyThreshold).ToStringPercent() + "); the mark lifts below " + LiftShare.ToStringPercent() + "."
                        : "\n\nDrifting toward the " + AvirCasteUtility.Label((AvirCaste)at) + " caste's work: " + (worst / AvirCasteUtility.ApostasyThreshold).ToStringPercent() + " of the way to apostasy.";
                return d;
            }
        }

        /// <summary>Below this share of the threshold in every foreign domain, an apostate's mark lifts.</summary>
        public const float LiftShare = 0.25f;

        public void Foreign(AvirCaste domain, float xp)
        {
            if (domain == AvirCaste.None || domain == caste || xp <= 0f) return;
            foreign[(int)domain] += xp;
            if (apostate || foreign[(int)domain] < AvirCasteUtility.ApostasyThreshold) return;
            apostate = true;
            if (pawn.Faction == Faction.OfPlayer)
                Find.LetterStack.ReceiveLetter("Apostate: " + pawn.LabelShort,
                    pawn.LabelShort + " is of the " + AvirCasteUtility.Label(caste) + " caste, but has lived the " + AvirCasteUtility.Label(domain)
                    + " caste's life for too long. Among the Avir that is heresy: " + pawn.LabelShort + " is now an apostate - until "
                    + pawn.Possessive() + " own caste's work is " + pawn.Possessive() + " life again.",
                    LetterDefOf.NeutralEvent, pawn);
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!pawn.IsHashIntervalTick(2500, delta)) return;
            float keep = Mathf.Pow(1f - AvirCasteUtility.DailyFade, 2500f / 60000f);
            float most = 0f;
            for (int i = 0; i < foreign.Length; i++) { foreign[i] *= keep; most = Mathf.Max(most, foreign[i]); }
            if (apostate && most < AvirCasteUtility.ApostasyThreshold * LiftShare) apostate = false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref caste, "avirCaste", AvirCaste.None);
            Scribe_Values.Look(ref bornCaste, "avirBornCaste", AvirCaste.None);
            Scribe_Values.Look(ref apostate, "avirApostate", false);
            float f1 = foreign[1], f2 = foreign[2], f3 = foreign[3];
            Scribe_Values.Look(ref f1, "avirForeign1", 0f);
            Scribe_Values.Look(ref f2, "avirForeign2", 0f);
            Scribe_Values.Look(ref f3, "avirForeign3", 0f);
            if (Scribe.mode == LoadSaveMode.LoadingVars) { foreign = new float[4]; foreign[1] = f1; foreign[2] = f2; foreign[3] = f3; }
            if (bornCaste == AvirCaste.None) bornCaste = caste;
        }
    }

    /// <summary>The caste written on an egg when it is laid. Eggs of different castes never stack.</summary>
    public class CompProperties_AvirEggCaste : CompProperties
    {
        public CompProperties_AvirEggCaste() { compClass = typeof(CompAvirEggCaste); }
    }

    public class CompAvirEggCaste : ThingComp
    {
        public AvirCaste caste;
        /// <summary>Which clutch the egg belongs to (0: unknown - laid before clutches were remembered).</summary>
        public int clutch;
        /// <summary>Set while the egg is opening: its end is a hatching, not a loss.</summary>
        public bool hatching;

        public override string TransformLabel(string label) =>
            caste == AvirCaste.None ? label : label + " (" + AvirCasteUtility.Label(caste) + ")";

        public override bool AllowStackWith(Thing other) =>
            (other as ThingWithComps)?.GetComp<CompAvirEggCaste>()?.caste == caste;

        public override string CompInspectStringExtra() =>
            caste == AvirCaste.None ? null
            : caste == AvirCaste.Elder ? "The first egg of its clutch: an elder - a warrior or a huntress."
            : caste == AvirCaste.Central ? "The middle egg of its clutch: a central, the rarest caste."
            : "The last egg of its clutch: a younger - an artist or an artificer.";

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref caste, "avirEggCaste", AvirCaste.None);
            Scribe_Values.Look(ref clutch, "avirEggClutch", 0);
        }

        /// <summary>Crack the egg: no hotkey, and always asked twice (AvirEggCrack.cs).</summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Spawned && parent.Map.IsPlayerHome) yield return AvirEggCrack.Command(parent);
        }

        /// <summary>An egg that burns, breaks or is cracked without hatching is lost to its clutch. (Vanished - sold,
        /// carried off - is not: it may hatch elsewhere.)</summary>
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (!hatching && clutch != 0 && mode != DestroyMode.Vanish) GameComponent_AvirClutch.Instance?.Lost(clutch);
        }

        public static void SetClutch(Thing egg, int id)
        {
            CompAvirEggCaste comp = (egg as ThingWithComps)?.GetComp<CompAvirEggCaste>();
            if (comp != null) comp.clutch = id;
        }

        public static CompAvirEggCaste Get(Thing egg) => (egg as ThingWithComps)?.GetComp<CompAvirEggCaste>();

        public static void Mark(Thing egg, AvirCaste c)
        {
            CompAvirEggCaste comp = (egg as ThingWithComps)?.GetComp<CompAvirEggCaste>();
            if (comp != null) comp.caste = c;
        }

        public static AvirCaste Of(Thing egg) => (egg as ThingWithComps)?.GetComp<CompAvirEggCaste>()?.caste ?? AvirCaste.None;
    }

    /// <summary>The chick takes the caste of its egg: read before VEF hatches it (the egg goes with the hatching),
    /// given once the chick is found.</summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirCasteHatch
    {
        private static FieldInfo hatcheeParentField, otherParentField;

        static Patch_AvirCasteHatch()
        {
            Type hatcher = AccessTools.TypeByName("VEF.Genes.CompHumanHatcher");
            if (hatcher == null) return;
            hatcheeParentField = AccessTools.Field(hatcher, "hatcheeParent");
            otherParentField = AccessTools.Field(hatcher, "otherParent");
            MethodInfo hatch = AccessTools.Method(hatcher, "Hatch");
            if (hatch == null || hatcheeParentField == null || otherParentField == null)
            {
                Log.Warning("[Avir] CompHumanHatcher did not expose Hatch/hatcheeParent/otherParent - chicks will take no caste from their eggs.");
                return;
            }
            new Harmony("avir.caste.hatch").Patch(hatch,
                prefix: new HarmonyMethod(typeof(Patch_AvirCasteHatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(Patch_AvirCasteHatch), nameof(Postfix)));
        }

        public static void Prefix(ThingComp __instance, ref object[] __state)
        {
            __state = null;
            try
            {
                CompAvirEggCaste egg = CompAvirEggCaste.Get(__instance?.parent);
                if (egg == null) return;
                egg.hatching = true;
                if (egg.caste == AvirCaste.None && egg.clutch == 0) return;
                __state = new object[] { hatcheeParentField.GetValue(__instance) as Pawn, otherParentField.GetValue(__instance) as Pawn, egg.caste, egg.clutch };
            }
            catch (Exception e) { Log.WarningOnce("[Avir] caste: reading the egg failed: " + e, 0x5C1A01); }
        }

        public static void Postfix(object[] __state)
        {
            if (__state == null) return;
            try
            {
                Pawn chick = AvirBirthUtility.FindFreshChick(__state[0] as Pawn, __state[1] as Pawn);
                if (chick == null) return;
                if ((AvirCaste)__state[2] != AvirCaste.None && AvirCasteUtility.HasCaste(chick)) AvirCasteUtility.Assign(chick, (AvirCaste)__state[2]);
                if ((int)__state[3] != 0) GameComponent_AvirClutch.Instance?.Hatched(chick, (int)__state[3]);
            }
            catch (Exception e) { Log.WarningOnce("[Avir] caste: giving the chick its caste failed: " + e, 0x5C1A02); }
        }
    }

    /// <summary>Every Avir on a map has a caste: one without an egg behind it gets one by what it is best at.
    /// A hatching chick is guessed here too, and the egg's own caste replaces the guess right after.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_AvirCasteOnSpawn
    {
        public static void Postfix(Pawn __instance)
        {
            Pawn p = __instance;
            if (p == null || p.Dead || !AvirCasteUtility.HasCaste(p) || AvirCasteUtility.Get(p) != null) return;
            AvirCasteUtility.EnsureCaste(p);
        }
    }

    /// <summary>Experience in another caste's domain builds toward apostasy (grown Avir only; teaching and the
    /// game's own direct grants do not count).</summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class Patch_AvirCasteLearn
    {
        public static void Postfix(SkillRecord __instance, float xp, bool direct, Pawn ___pawn)
        {
            if (direct || xp <= 0f || ___pawn == null || !___pawn.DevelopmentalStage.Adult()) return;
            AvirCasteUtility.Get(___pawn)?.Foreign(AvirCasteUtility.DomainOf(__instance.def), xp);
        }
    }
}
