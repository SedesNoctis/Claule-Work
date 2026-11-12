using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Artifactorics;

namespace Avir.Linarian
{
    // =====================================================================================
    // THE VEIL AND THE FORMS.
    //   Veil: she vanishes (vanilla invisibility; the player sees their own Herald faintly, never a hidden one).
    //   Forms (doe, crow, she-wolf): she is still herself - the same hunger, skills and gear; only her look and her
    //   way of moving change (the crow flies). Foes and turrets do not take a beautiful beast for a target.
    //   The form falls when she is struck (not when she strikes: she hunts in it).
    //   Both are seen through, near her, by the dull of mind - a pawn of no psychic sensitivity at once, a dull
    //   one by chance - by turrets and machines, and by a huntress with the predator's sense.
    // =====================================================================================
    public static class LinarianVeil
    {
        public static void Veil(Pawn p, int ticks)
        {
            HediffDef def = p.Faction == Faction.OfPlayer ? LinarianHediffs.Avir_LinarianVeil : LinarianHediffs.Avir_LinarianVeilHidden;
            LinarianHediffs.Add(p, def, ticks);
            // under her veil she is not there: no hunt or tame mark is left hanging where she lies
            if (p.Faction != Faction.OfPlayer) HeraldDesignations.Clear(p);
        }

        public static bool Veiled(Pawn p) =>
            p?.health?.hediffSet != null && (p.health.hediffSet.HasHediff(LinarianHediffs.Avir_LinarianVeil) || p.health.hediffSet.HasHediff(LinarianHediffs.Avir_LinarianVeilHidden));

        public static void Unveil(Pawn p)
        {
            foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => h.def == LinarianHediffs.Avir_LinarianVeil || h.def == LinarianHediffs.Avir_LinarianVeilHidden).ToList())
                p.health.RemoveHediff(h);
        }

        /// <summary>WHO WEARS A FORM NOW, known without walking anyone's hediffs (Fix254). InForm is asked for every pawn
        /// on the map in every phase of every frame's drawing, on every question of whether a pawn can be seen and
        /// aimed at, and by the flight rules on every path - and each time it walked that pawn's whole list of
        /// hediffs to find that there was no form. The form's own hediff says when it comes and goes; everyone else
        /// leaves on one hash lookup, and with no one in a form at all - on one integer.
        /// A missing name would leave a beast drawn as a woman, so the name is put in from every side: when the form
        /// is taken, when a save is read, and on each tick of the form itself.</summary>
        private static readonly HashSet<int> formed = new HashSet<int>();
        internal static void NoteForm(Pawn p, bool on) { if (p == null) return; if (on) formed.Add(p.thingIDNumber); else formed.Remove(p.thingIDNumber); }

        public static string InForm(Pawn p)
        {
            if (p == null || formed.Count == 0 || !formed.Contains(p.thingIDNumber)) return null;
            return (p.health?.hediffSet?.GetFirstHediffOfDef(LinarianHediffs.Avir_LinarianForm) as Hediff_LinarianForm)?.form;
        }

        public static void SetForm(Pawn p, string form)
        {
            if (LinarianRivals.Unmasked(p)) return;   // a stronger Singer of the player has her in her zone
            Hediff_LinarianForm h = p.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_LinarianForm) as Hediff_LinarianForm
                                    ?? (Hediff_LinarianForm)p.health.AddHediff(LinarianHediffs.Avir_LinarianForm);
            h.form = form;
            if (form == LinarianTactics.Crow && FlightGene.AvirFlightUtility.CanTakeOff(p)) FlightGene.AvirFlightUtility.TakeOff(p, true);
            else if (form != LinarianTactics.Crow) Ground(p);
            if (p.Spawned) FleckMaker.ThrowSmoke(p.DrawPos, p.Map, 1.2f);
            p.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        /// <summary>Down out of the air, whatever state of flight holds her.</summary>
        public static void Ground(Pawn p)
        {
            if (FlightGene.AvirFlightUtility.IsFlying(p)) FlightGene.AvirFlightUtility.Land(p);
            HediffDef fd = DefDatabase<HediffDef>.GetNamedSilentFail("Flight_Hediff");
            Hediff h = fd == null ? null : p.health.hediffSet.GetFirstHediffOfDef(fd);
            if (h != null) p.health.RemoveHediff(h);
        }

        public static void ClearForm(Pawn p)
        {
            Hediff h = p?.health?.hediffSet?.GetFirstHediffOfDef(LinarianHediffs.Avir_LinarianForm);
            if (h == null) return;
            p.health.RemoveHediff(h);
            if (p.Spawned) FleckMaker.ThrowSmoke(p.DrawPos, p.Map, 1.2f);
            p.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        /// <summary>Seen through: the veil and the form fall. A double (Illusions) takes it instead once an hour.</summary>
        private static readonly Dictionary<int, int> lastReveal = new Dictionary<int, int>();

        public static bool RecentlyRevealed(Pawn p, int now) =>
            lastReveal.TryGetValue(p.thingIDNumber, out int last) && now - last < GenDate.TicksPerHour * 2;

        public static void Reveal(Pawn p, Thing by)
        {
            if (!Veiled(p) && InForm(p) == null) return;
            Gene_LinarianSong g = Linarian.Song(p);
            int now = Find.TickManager.TicksGame;
            // seen through once is enough for a while: she slips back under her veil, and the same eyes do not catch her
            // again the next moment (a deaf watcher would otherwise see through her every second)
            if (lastReveal.TryGetValue(p.thingIDNumber, out int last) && now - last < GenDate.TicksPerHour * 2) return;
            lastReveal[p.thingIDNumber] = now;
            // the switch's gift: once a day, seen, she is six of herself - and then none
            if (LinarianGuise.Ring(p, g)) return;
            LinarianGuise.Forget(p);
            Unveil(p);
            ClearForm(p);
            if (p.Spawned)
            {
                Avir.Ritualistics.RitualFx.Mote(p.DrawPos, p.Map, 1.6f, new Color(0.9f, 0.9f, 1f));
                if (p.Faction == Faction.OfPlayer || by?.Faction == Faction.OfPlayer)
                    Messages.Message((by != null ? by.LabelShortCap + " sees through " : "Seen through: ") + p.LabelShort + ".", p, MessageTypeDefOf.NeutralEvent, false);
            }
            LinarianPresence.OnRevealed(p, by);
        }

        /// <summary>Whether this thing can see through her near it.</summary>
        public static float SeesThrough(Pawn her, Thing t)
        {
            if (t == her || t == null) return 0f;
            if (t.Faction != null && her.Faction != null && t.Faction == her.Faction) return 0f;
            if (t is Building_Turret tur) return tur.Faction != null && tur.Faction != her.Faction && tur.Spawned ? 0.5f : 0f;
            if (!(t is Pawn q) || q.Dead || q.Downed) return 0f;
            if (q.RaceProps.Animal) return 0f;
            // her sleepers and her worshippers cannot see through her
            if (LinarianActs.HerSleeper(q, her) || LinarianSource.Of(q, LinarianHediffs.Avir_Adoration) == her) return 0f;
            // the dark guise: in her beast's form, in the dark, nothing sees her
            if (InForm(her) != null && Linarian.Song(her)?.Dark("G") == true && her.Map.glowGrid.GroundGlowAt(her.Position) < 0.3f) return 0f;
            if (her.Faction == null && q.Faction == null) return 0f;
            if (q.RaceProps.IsMechanoid) return 0.5f;
            if (AmuletDevelopment.Level(q, HunterBranch) >= 3) return 1f;
            float sens = q.GetStatValue(StatDefOf.PsychicSensitivity);
            if (sens <= 0.01f) return 1f;
            if (sens < 0.5f) return (0.5f - sens) * 0.4f;
            return 0f;
        }

        private static AmuletSubclassDef hunter;
        private static AmuletSubclassDef HunterBranch => hunter ?? (hunter = DefDatabase<AmuletSubclassDef>.GetNamedSilentFail("Avir_Subclass_Hunter"));

        public static void CheckReveal(Pawn her)
        {
            if (!Veiled(her) && InForm(her) == null || LinarianGuise.Deep(her)) return;
            Gene_LinarianSong g = Linarian.Song(her);
            float k = g != null && g.Has("G5") ? 0.5f : 1f;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(her.Position, her.Map, 7.9f, true))
            {
                float c = SeesThrough(her, t) * k;
                if (c > 0f && Rand.Chance(c)) { Reveal(her, t); return; }
            }
        }
    }

    public class Hediff_LinarianForm : HediffWithComps
    {
        public string form;

        public override string LabelInBrackets => form == null ? null : LinarianTactics.Label(form);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref form, "form");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) LinarianVeil.NoteForm(pawn, true);
        }

        public override void PostAdd(DamageInfo? dinfo) { base.PostAdd(dinfo); LinarianVeil.NoteForm(pawn, true); }
        public override void PostRemoved() { base.PostRemoved(); LinarianVeil.NoteForm(pawn, false); }
        public override void PostTick() { base.PostTick(); LinarianVeil.NoteForm(pawn, true); }
    }

    /// <summary>In a beast's form she is no one's target: a beautiful beast, nothing more - until the beast bites. One
    /// that has struck within the hour, or was seen through within two, is a target like any other.</summary>
    [HarmonyPatch(typeof(InvisibilityUtility), nameof(InvisibilityUtility.IsPsychologicallyInvisible))]
    public static class Patch_LinarianFormUntargetable
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || LinarianVeil.InForm(pawn) == null) return;
            if (LinarianPresence.Disguised(pawn)) return;   // the wild one is a beast like any other: it can be aimed at - see Patch_HeraldEvades
            int now = Find.TickManager.TicksGame;
            int struck = pawn.mindState?.lastAttackTargetTick ?? -99999;
            if (now - struck < GenDate.TicksPerHour || LinarianVeil.RecentlyRevealed(pawn, now)) return;
            __result = true;
        }
    }

    /// <summary>The player's Singer comes out of her beast's form by herself for anything a beast cannot do - work,
    /// reading, a meal at a table, a conversation. Walking, waiting, sleeping, eating, fleeing, biting and her own
    /// songs keep the form.</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_LinarianFormForHands
    {
        private static readonly HashSet<string> beastly = new HashSet<string>
        {
            "Ingest", "AttackMelee", "Flee", "FleeAndCower", "Follow", "FollowClose", "Hunt", "PredatorHunt",
            "Vomit", "Mate", "Lovin", "GiveSpeech", "Nuzzle", "ReleaseAnimalToWild"
        };

        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            if (newJob?.def == null || ___pawn == null || ___pawn.Faction != Faction.OfPlayer) return;
            if (LinarianVeil.InForm(___pawn) == null || ___pawn.InMentalState) return;
            if (newJob.targetA.Thing is Building_Bed) { LinarianVeil.ClearForm(___pawn); return; }   // into her own bed in her own shape
            if (Beastly(newJob)) return;
            LinarianVeil.ClearForm(___pawn);
        }

        private static bool Beastly(Job j)
        {
            string n = j.def.defName;
            if (n.StartsWith("Wait") || n.StartsWith("Goto") || n.StartsWith("LayDown") || n.StartsWith("Wander") || n.StartsWith("Idle")) return true;
            if (beastly.Contains(n) || j.ability != null || j.verbToUse is Verb_CastAbility) return true;
            string ns = j.def.driverClass?.Namespace ?? "";
            return ns.StartsWith("Avir") || ns.StartsWith("FlightGene");   // her own songs and wings
        }
    }

    // (A form no longer falls when she strikes: a Singer hunts in her beast's shape.)

    /// <summary>...and when she is struck: struck, she is seen.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_LinarianRevealedByHarm
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (!Linarian.Is(__instance)) return;
            Gene_LinarianSong g = Linarian.Song(__instance);
            if (g != null && g.Has("D4") && dinfo.Instigator is Pawn offender && offender != __instance) g.offender = offender;
            if (!LinarianVeil.Veiled(__instance) && LinarianVeil.InForm(__instance) == null) return;
            // the deepened doe and crow: the first blow of a day does not give her away - she only slips under the veil
            string form = LinarianVeil.InForm(__instance);
            int now = Find.TickManager.TicksGame;
            if (g != null && g.Has("G2") && (form == LinarianTactics.Doe || form == LinarianTactics.Crow) && now - g.lastShrug > GenDate.TicksPerDay)
            {
                g.lastShrug = now;
                LinarianVeil.Veil(__instance, GenDate.TicksPerHour / 2);
                return;
            }
            LinarianVeil.Reveal(__instance, dinfo.Instigator);
        }
    }

    // ---------------------------------------------------------------- the beast's look

    /// <summary>The beast's look. Her own art comes in three layers, each tinted from her own colours, so every
    /// Herald's beast is her own: the body in the colour of her body, the chest fur in the colour of her breast, the
    /// mask round the eyes in the colour of her wings. Until the art is drawn: the game's doe, white wolf and crow.</summary>
    public static class LinarianFormGraphics
    {
        private static readonly Dictionary<string, List<Graphic>> cache = new Dictionary<string, List<Graphic>>();

        private static System.Type eyeComp;
        private static System.Reflection.MethodInfo eyeColor;
        private static bool eyeLooked;

        private static System.Reflection.PropertyInfo eyeSecond;

        /// <summary>Her other eye's colour under Facial Animation, when it differs from the first (heterochromia).</summary>
        public static Color? FacialSecondEye(Pawn p)
        {
            Color? first = FacialEyes(p);
            if (first == null) return null;
            if (eyeSecond == null) eyeSecond = HarmonyLib.AccessTools.Property(eyeComp, "FaceSecondColor");
            if (eyeSecond == null) return null;
            foreach (ThingComp c in p.AllComps)
                if (eyeComp.IsInstanceOfType(c))
                {
                    try
                    {
                        Color e = (Color)eyeSecond.GetValue(c);
                        if (e.a <= 0.01f) return null;
                        var s = new Color(e.r, e.g, e.b, 1f);
                        Color f = first.Value;
                        return Mathf.Abs(s.r - f.r) + Mathf.Abs(s.g - f.g) + Mathf.Abs(s.b - f.b) < 0.04f ? (Color?)null : s;
                    }
                    catch { return null; }
                }
            return null;
        }

        /// <summary>Her eye colour as Facial Animation paints it (its eyeball controller); null without that mod.</summary>
        public static Color? FacialEyes(Pawn p)
        {
            if (!eyeLooked)
            {
                eyeLooked = true;
                eyeComp = HarmonyLib.AccessTools.TypeByName("FacialAnimation.EyeballControllerComp");
                eyeColor = eyeComp == null ? null : HarmonyLib.AccessTools.Method(eyeComp, "GetCurrentColor");
            }
            if (eyeColor == null || p?.AllComps == null) return null;
            foreach (ThingComp c in p.AllComps)
                if (eyeComp.IsInstanceOfType(c))
                {
                    try { Color e = (Color)eyeColor.Invoke(c, null); return e.a <= 0.01f ? (Color?)null : new Color(e.r, e.g, e.b, 1f); }
                    catch { return null; }
                }
            return null;
        }

        public static List<Graphic> Layers(Pawn p, string form)
        {
            Gene_AvirFeathers f = Gene_AvirFeathers.For(p);
            Color body = f?.colorOne ?? Color.white;
            Color chest = f?.colorTwo ?? body;
            Color eyes = FacialEyes(p) ?? f?.wingColor ?? body;   // the mask round the eyes: her eyes' colour under Facial Animation
            Color? second = FacialSecondEye(p);                   // ...and each eye its own, when they differ
            // the crow has her own look in flight
            string art = form == LinarianTactics.Crow && FlightGene.AvirFlightUtility.IsFlying(p) ? "CrowFlight" : form;
            string key = art + body + chest + eyes + second;
            if (cache.TryGetValue(key, out List<Graphic> l)) return l;
            l = new List<Graphic>();
            string own = "Things/Pawn/Animal/AvirHerald/" + art + "/" + art;
            // the crow in flight is drawn larger: her spread wings take the room her body had, and she keeps her size
            float size = art == "CrowFlight" ? 2.3f : form == LinarianTactics.Crow ? 1.1f : form == LinarianTactics.Wolf ? 1.7f : 2.0f;
            if (ContentFinder<Texture2D>.Get(own + "_Body_south", false) != null)
            {
                l.Add(GraphicDatabase.Get<Graphic_Multi>(own + "_Body", ShaderDatabase.Cutout, new Vector2(size, size), body));
                if (ContentFinder<Texture2D>.Get(own + "_Chest_south", false) != null)
                    l.Add(GraphicDatabase.Get<Graphic_Multi>(own + "_Chest", ShaderDatabase.Cutout, new Vector2(size, size), chest));
                if (second.HasValue && ContentFinder<Texture2D>.Get(own + "_EyeR_south", false) != null)
                {
                    l.Add(GraphicDatabase.Get<Graphic_Multi>(own + "_EyeR", ShaderDatabase.Cutout, new Vector2(size, size), eyes));
                    l.Add(GraphicDatabase.Get<Graphic_Multi>(own + "_EyeL", ShaderDatabase.Cutout, new Vector2(size, size), second.Value));
                }
                else if (ContentFinder<Texture2D>.Get(own + "_Eyes_south", false) != null)
                    l.Add(GraphicDatabase.Get<Graphic_Multi>(own + "_Eyes", ShaderDatabase.Cutout, new Vector2(size, size), eyes));
            }
            else
            {
                string kind = form == LinarianTactics.Crow ? "Crow" : form == LinarianTactics.Wolf ? "Wolf_Arctic" : "Deer";
                PawnKindDef k = DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Deer");
                GraphicData gd = k?.lifeStages?.LastOrDefault()?.femaleGraphicData ?? k?.lifeStages?.LastOrDefault()?.bodyGraphicData;
                if (gd != null) l.Add(GraphicDatabase.Get<Graphic_Multi>(gd.texPath, ShaderDatabase.Cutout, gd.drawSize, Color.white));
            }
            cache[key] = l;
            return l;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    public static class Patch_LinarianFormDraw
    {
        private static readonly Dictionary<Material, Material> ghosts = new Dictionary<Material, Material>();

        /// <summary>Her beast under the veil, as her own people see it: a pale ghost. The layers' own shader cuts away
        /// anything faint, so the ghost is drawn with a transparent one.</summary>
        private static Material Ghost(Material m)
        {
            if (ghosts.TryGetValue(m, out Material g)) return g;
            Color c = m.color;
            g = MaterialPool.MatFrom(new MaterialRequest(m.mainTexture, ShaderDatabase.Transparent, new Color(c.r, c.g, c.b, 0.38f)));
            ghosts[m] = g;
            return g;
        }

        public static bool Prefix(DrawPhase phase, Vector3 drawLoc, Pawn ___pawn)
        {
            string form = LinarianVeil.InForm(___pawn);
            if (form == null) return true;
            if (phase != DrawPhase.Draw) return false;
            List<Graphic> layers = LinarianFormGraphics.Layers(___pawn, form);
            if (layers.Count == 0) return true;
            Rot4 rot = ___pawn.Rotation;
            Quaternion turn = Quaternion.identity;
            // asleep, downed or lying in bed: the beast lies down on its side, as the game's own animals do
            if (___pawn.GetPosture() != PawnPosture.Standing)
            {
                PawnRenderer r = ___pawn.Drawer?.renderer;
                if (r != null)
                {
                    rot = r.LayingFacing();
                    turn = Quaternion.AngleAxis(r.BodyAngle(PawnRenderFlags.None), Vector3.up);
                }
            }
            Vector3 at = drawLoc;
            at.y = AltitudeLayer.Pawn.AltitudeFor();
            bool veiled = LinarianVeil.Veiled(___pawn);
            if (veiled && ___pawn.Faction != Faction.OfPlayer) return false;   // a hidden one under her veil is simply not there
            foreach (Graphic g in layers)
            {
                Material mat = g.MatAt(rot);
                if (veiled) mat = Ghost(mat);
                Graphics.DrawMesh(g.MeshAt(rot), at, turn, mat, 0);
                at.y += 0.0005f;
            }
            return false;
        }
    }

    /// <summary>A Singer in her beast's form, not the player's (the wild Singer, the Primal one), gives nothing away:
    /// her beak, her claws, her form and the rest are hidden - only wounds show, as on any beast.</summary>
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.Visible), MethodType.Getter)]
    public static class Patch_LinarianFormHidesHediffs
    {
        public static void Postfix(Hediff __instance, ref bool __result)
        {
            if (!__result) return;
            Pawn p = __instance.pawn;
            if (p == null || p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony) return;
            if (__instance is Hediff_Injury || __instance is Hediff_MissingPart) return;
            if (LinarianVeil.InForm(p) != null) __result = false;
        }
    }
}
