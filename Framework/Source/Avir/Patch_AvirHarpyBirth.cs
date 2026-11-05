using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Decides which harpy a cross produces, and makes the result absolute.
    ///
    /// THE RULE, IN ORDER:
    ///   1. If the mother is herself a harpy, the child IS her xenotype. Checked FIRST and it
    ///      wins outright. Without this a wild harpy would lay an egg - she is Fearborn and
    ///      egg-laying like any Avir - and the hatch path would hand back a calm daughter,
    ///      running the degeneration backwards one generation at a time.
    ///   2. Otherwise, an Avir mother and a non-Avir father give a CALM harpy if the child came
    ///      out of an egg, and a WILD one if it was carried live. The egg's second shell is the
    ///      difference: a human womb has nothing equivalent, and the child comes out wrong.
    ///   3. Avir on both sides, or neither, is not a harpy at all and nothing happens here.
    ///
    /// WHY VANILLA INHERITANCE CANNOT BE LEFT TO ITS OWN DEVICES. RimWorld mixes endogenes from
    /// both parents and would hand a wild harpy some scattering of her human mother's traits,
    /// different every birth. Maximus wants the xenotypes strict, so SetXenotype is called on the
    /// finished pawn: it replaces the endogene set wholesale with the xenotype's list. Whatever
    /// the game rolled a moment earlier is discarded. 100%, every time, both parents irrelevant
    /// past deciding WHICH of the two xenotypes applies.
    ///
    /// ORDER WITH THE SUBRACE PATCH MATTERS. Patch_AvirAShacsaignInheritance also postfixes both
    /// of these methods, and A'Shacsaign is a HEDIFF rather than a gene, so SetXenotype cannot
    /// disturb it. But a hediff added before the gene set is replaced would sit on a pawn whose
    /// Fearborn gene momentarily vanishes, so this patch is registered to run FIRST and the
    /// subrace roll lands on a pawn whose genes are already final.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirHarpyBirth
    {
        private const string HatcherCompType = "VEF.Genes.CompHumanHatcher";

        private static XenotypeDef wildCached;
        private static XenotypeDef calmCached;
        private static bool resolved;

        private static FieldInfo hatcheeParentField;
        private static FieldInfo otherParentField;

        static Patch_AvirHarpyBirth()
        {
            Harmony harmony = new Harmony("avir.harpy.birth");

            MethodInfo birth = AccessTools.Method(typeof(PregnancyUtility), "ApplyBirthOutcome");
            if (birth != null)
            {
                // priority is a FIELD on HarmonyMethod, not a constructor argument - the ctor
                // is (Type, string, Type[]) and passing an int there would bind to nothing.
                // First, so the xenotype is final before Patch_AvirAShacsaignInheritance runs.
                HarmonyMethod post = new HarmonyMethod(typeof(Patch_AvirHarpyBirth), nameof(BirthPostfix));
                post.priority = Priority.First;
                harmony.Patch(birth, postfix: post);
            }
            else
            {
                Log.Warning("[Avir] PregnancyUtility.ApplyBirthOutcome not found - wild harpies "
                            + "will not be produced by live birth.");
            }

            Type hatcher = AccessTools.TypeByName(HatcherCompType);
            if (hatcher == null) return;

            hatcheeParentField = AccessTools.Field(hatcher, "hatcheeParent");
            otherParentField   = AccessTools.Field(hatcher, "otherParent");
            MethodInfo hatch   = AccessTools.Method(hatcher, "Hatch");

            if (hatch == null || hatcheeParentField == null || otherParentField == null)
            {
                Log.Warning("[Avir] CompHumanHatcher did not expose Hatch/hatcheeParent/"
                            + "otherParent - calm harpies will not be produced by hatching.");
                return;
            }

            HarmonyMethod hatchPost = new HarmonyMethod(typeof(Patch_AvirHarpyBirth), nameof(HatchPostfix));
            hatchPost.priority = Priority.First;
            harmony.Patch(hatch,
                prefix:  new HarmonyMethod(typeof(Patch_AvirHarpyBirth), nameof(HatchPrefix)),
                postfix: hatchPost,
                finalizer: new HarmonyMethod(typeof(Patch_AvirHarpyBirth), nameof(HatchFinalizer)));
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            wildCached = DefDatabase<XenotypeDef>.GetNamedSilentFail("Avir_HarpyWild");
            calmCached = DefDatabase<XenotypeDef>.GetNamedSilentFail("Avir_HarpyCalm");
            if (wildCached == null || calmCached == null)
                Log.Warning("[Avir] Harpy xenotypes not found - crosses will fall back to vanilla "
                            + "gene mixing.");
        }

        public static XenotypeDef Wild { get { Resolve(); return wildCached; } }
        public static XenotypeDef Calm { get { Resolve(); return calmCached; } }

        // ---------------------------------------------------------------------------------------

        // Parameter names read off Assembly-CSharp: (outcome, quality, ritual, genes,
        // geneticMother, birtherThing, father, doctor, lordJobRitual, assignments, preventLetter).
        // Harmony binds by name, so geneticMother is not interchangeable with "mother".
        //
        // RETURN TYPE IS Thing. Declaring Pawn __result made Harmony throw inside this class's
        // static constructor, so Patch_AvirHarpyBirth never initialised and no harpy was ever
        // born by live birth OR from an egg. Same failure as Patch_AvirAShacsaignInheritance,
        // same cause, same fix - see the longer note there.
        //
        // This also settles the pigman question from earlier: this class was not running at all,
        // so it cannot have put Avir genes on anyone's child. I was right that no path here
        // reached that baby, but for a reason neither of us had found.
        public static void BirthPostfix(Thing __result, Pawn geneticMother, Pawn father)
        {
            Apply(__result as Pawn, geneticMother, father, false);
            NameAvirXenotype(__result as Pawn, geneticMother, father);
            Linarian.LinarianLine.Apply(__result as Pawn, geneticMother, father);
            // a child of the Fearborn line born alive is named after its mother as well
            if (AvirFaction.HasFearborn(__result as Pawn)) InheritSurname(__result as Pawn, geneticMother, father);
        }

        public static void HatchPrefix(object __instance, ref Pawn[] __state)
        {
            __state = null;
            if (hatcheeParentField == null) return;
            __state = new Pawn[]
            {
                hatcheeParentField.GetValue(__instance) as Pawn,
                otherParentField.GetValue(__instance) as Pawn
            };
            // a calm harpy is hatched on her own race (a human face and hands), so the chick has to be
            // generated as one - the race cannot be changed once she exists
            Patch_AvirRaceFromXenotype.hatchingCalmHarpy = WillHatchCalm(__state[0], __state[1]);
            // an egg of an Avir-bodied parent hatches an Avir-bodied chick: VEF builds the chick on a human kind
            Patch_AvirRaceFromXenotype.hatchingAvir = AvirBody.IsAvirBody(__state[0]) || AvirBody.IsAvirBody(__state[1]);
        }

        private static bool WillHatchCalm(Pawn mother, Pawn father)
        {
            XenotypeDef calm = Calm;
            if (calm == null) return false;
            XenotypeDef motherXeno = mother?.genes?.Xenotype;
            if (motherXeno == calm) return true;
            if (motherXeno == Wild) return false;
            return AvirFaction.HasFearborn(mother) && !AvirFaction.HasFearborn(father);
        }

        public static Exception HatchFinalizer(Exception __exception)
        {
            Patch_AvirRaceFromXenotype.hatchingCalmHarpy = false;
            Patch_AvirRaceFromXenotype.hatchingAvir = false;
            return __exception;
        }

        public static void HatchPostfix(object __instance, Pawn[] __state)
        {
            Patch_AvirRaceFromXenotype.hatchingCalmHarpy = false;
            Patch_AvirRaceFromXenotype.hatchingAvir = false;
            if (__state == null) return;
            Pawn chick = AvirBirthUtility.FindFreshChick(__state[0], __state[1]);
            if (chick == null) return;

            // The egg records the layer first, so parent[0] is the mother.
            Apply(chick, __state[0], __state[1], true);
            NameAvirXenotype(chick, __state[0], __state[1]);
            Linarian.LinarianLine.Apply(chick, __state[0], __state[1]);
            InheritSurname(chick, __state[0], __state[1]);
            Patch_AvirEarlyHatch.AgeHatchling(chick);
        }

        /// <summary>
        /// A CHICK CARRIES ITS PARENTS' NAME. The egg makes the chick as a stranger and names it like one, with a
        /// surname of its own. The Avir are matriarchal: the chick takes its mother's surname; with no mother, the
        /// father's. The given name stays.
        /// </summary>
        public static void InheritSurname(Pawn child, Pawn mother, Pawn father)
        {
            try
            {
                if (!(child?.Name is NameTriple n)) return;
                string last = (mother?.Name as NameTriple)?.Last;
                if (last.NullOrEmpty()) last = (father?.Name as NameTriple)?.Last;
                if (last.NullOrEmpty() || last == n.Last) return;
                child.Name = new NameTriple(n.First, n.Nick, last);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] giving a chick its parents' surname failed: " + e.Message, 0x4A5331);
            }
        }

        /// <summary>
        /// A CHICK OF AVIR IS AN AVIR, NOT A BASELINER. Neither the egg (VEF) nor a birth names the
        /// xenotype of a child whose genes were inherited rather than given, so a chick of two Avir came out
        /// "Baseliner" with every Avir gene. If the child has the Fearborn line, has no harpy xenotype by now,
        /// and a parent is of an Avir xenotype, the child is named that xenotype - its inherited genes are
        /// kept exactly as they are.
        /// </summary>
        public static void NameAvirXenotype(Pawn child, Pawn mother, Pawn father)
        {
            try
            {
                if (child?.genes == null || !AvirFaction.HasFearborn(child)) return;
                XenotypeDef cur = child.genes.Xenotype;
                if (cur != null && cur != XenotypeDefOf.Baseliner) return;
                XenotypeDef mx = mother?.genes?.Xenotype, fx = father?.genes?.Xenotype;
                XenotypeDef pick = mx != null && mx == fx && AvirBody.IsAvirXenotype(mx) && mx != Wild && mx != Calm ? mx
                    : mx != null && mx.defName == "Avir" ? mx
                    : fx != null && fx.defName == "Avir" ? fx
                    : null;
                if (pick == null) return;
                child.genes.SetXenotypeDirect(pick);
                Log.Message("[Avir] " + child.LabelShort + " hatched or born of " + (mother?.LabelShort ?? "?") + " and "
                    + (father?.LabelShort ?? "?") + ": named xenotype " + pick.defName + " (was Baseliner).");
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] naming a chick's xenotype failed: " + e.Message, 0x4A5320);
            }
        }

        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// <paramref name="fromEgg"/> is the whole difference between the two lines.
        /// </summary>
        public static void Apply(Pawn child, Pawn mother, Pawn father, bool fromEgg)
        {
            if (child == null || child.genes == null) return;

            XenotypeDef wild = Wild;
            XenotypeDef calm = Calm;
            if (wild == null || calm == null) return;

            // 1. A harpy's daughter is her mother's kind, full stop. This is checked before
            //    anything else precisely so that gestation cannot override descent.
            XenotypeDef motherXeno = mother != null && mother.genes != null ? mother.genes.Xenotype : null;
            if (motherXeno == wild || motherXeno == calm)
            {
                Report(child, mother, father, fromEgg, "mother is herself a harpy");
                Force(child, motherXeno);
                return;
            }

            // 2. Otherwise this is a first cross: it takes an Avir mother and a father who is not.
            //    Either way round outside that and there is no harpy.
            bool motherIsAvir = AvirFaction.HasFearborn(mother);
            bool fatherIsAvir = AvirFaction.HasFearborn(father);
            if (!motherIsAvir || fatherIsAvir) return;

            Report(child, mother, father, fromEgg, "Avir mother, non-Avir father");
            Force(child, fromEgg ? calm : wild);
        }

        /// <summary>
        /// EVIDENCE FOR THE PIGMAN.
        ///
        /// Maximus reported a pigman born with Avir genes to two parents who had none. I read
        /// every path in this mod that can grant a gene or set a xenotype and could not find one
        /// that reaches that child - this class is the only place SetXenotype is called at all,
        /// and both of its branches require an Avir or harpy MOTHER.
        ///
        /// The one place I cannot rule out from here is WHICH child this ran on.
        /// AvirBirthUtility.FindFreshChick does not receive the newborn - it searches the map for
        /// the newest pawn under one day old that is a child of either egg-parent. If two births
        /// landed close together and shared a parent, that search could return the wrong infant,
        /// and this method would then stamp a harpy xenotype onto somebody else's baby.
        ///
        /// Narrow, but it is the only candidate I have, and guessing further from here would be
        /// making things up. So: every forced xenotype now names the child and both parents. If
        /// the pigman happens again, this line will say so outright instead of leaving us to
        /// reason backwards from a gene list weeks later.
        /// </summary>
        private static void Report(Pawn child, Pawn mother, Pawn father, bool fromEgg, string rule)
        {
            try
            {
                Log.Message("[Avir harpy] forcing xenotype on " + Describe(child)
                    + " - rule: " + rule
                    + "; mother " + Describe(mother)
                    + "; father " + Describe(father)
                    + "; from egg: " + fromEgg
                    + ". If this child is not the one that was just born to those two parents, "
                    + "THIS is the pigman bug and FindFreshChick picked the wrong infant.");
            }
            catch
            {
                // A diagnostic must never be the thing that breaks a birth.
            }
        }

        private static string Describe(Pawn p)
        {
            if (p == null) return "(none)";

            string xeno = p.genes != null && p.genes.Xenotype != null
                ? p.genes.Xenotype.defName
                : "no xenotype";

            return p.LabelShort + " [kind " + (p.kindDef != null ? p.kindDef.defName : "?")
                + ", " + xeno + ", Fearborn " + AvirFaction.HasFearborn(p) + "]";
        }

        /// <summary>
        /// SetXenotype replaces the endogene set outright, discarding whatever vanilla inheritance
        /// rolled from the two parents a moment earlier. That is the point: the xenotypes are
        /// meant to be exact, not a blend that varies per birth.
        /// </summary>
        private static void Force(Pawn child, XenotypeDef xeno)
        {
            if (child.genes.Xenotype != xeno)
            {
                child.genes.SetXenotype(xeno);
            }
            ForceFemale(child);
        }

        /// <summary>
        /// THERE IS NO SUCH THING AS A MALE HARPY. Not rare - nonexistent.
        ///
        /// No vanilla gene forces a child's sex, so it is set here, on the finished pawn. The
        /// gender field is assigned directly; nothing else in RimWorld reads sex through a
        /// property that would need notifying.
        ///
        /// THE NAME HAS TO BE REDONE. It was generated a moment earlier against the sex the game
        /// rolled, so a harpy born male keeps a male given name unless it is regenerated. That is
        /// the only derived thing worth chasing at birth: body and head types are still the
        /// infant set and get resolved as the pawn grows, and Patch_AvirNeverHeavyset already
        /// owns body type for every Fearborn pawn.
        ///
        /// The last name is preserved deliberately - she is still her mother's daughter, and a
        /// newly rolled surname would cut her out of the family she was just born into.
        /// </summary>
        private static void ForceFemale(Pawn child)
        {
            if (child.gender == Gender.Female) return;

            child.gender = Gender.Female;

            NameTriple old = child.Name as NameTriple;
            string lastName = old != null ? old.Last : null;

            try
            {
                // (pawn, style, forcedLastName, forceNoNick, xenotype) - read off the assembly.
                child.Name = PawnBioAndNameGenerator.GeneratePawnName(
                    child, NameStyle.Full, lastName, false, child.genes?.Xenotype);
            }
            catch (Exception e)
            {
                // A wrong-sexed name is cosmetic; a throw inside a birth postfix is not. If the
                // generator ever changes shape, she keeps the old name and the birth completes.
                Log.Warning("[Avir] Could not regenerate a harpy's name after forcing female: " + e);
            }
        }
    }
}
