using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Settings storage. First ModSettings class in this mod - if more toggles are needed
    /// later, add the fields here and a line in ExposeData.
    /// </summary>
    public class AvirSettings : ModSettings
    {
        /// <summary>
        /// Whether feignallite may stand in for ordinary materials in crafting recipes.
        /// Read once at startup by Patch_AvirFeignalliteSubstitution.
        /// </summary>
        public bool feignalliteSubstitution = true;

        /// <summary>How far the amulets and artifacts of Avir pawns of other factions (raiders, traders, allies)
        /// are developed, as a share of what their status and age would give. Applied when a pawn is generated.</summary>
        public float npcStrength = 0.5f;
        /// <summary>No path of an NPC's amulet goes past this many stones (12: no unstoppable, no hunting series).</summary>
        public int npcBranchCap = 12;
        /// <summary>Most Avir in a trade caravan of an Avir faction, the trader included.</summary>
        public int caravanMax = 10;

        /// <summary>
        /// CEILING of the feignallite crafting speed ladder - the factor a job gets when it is
        /// made entirely of feignallite. Partial jobs get 75%, 50% or 25% of the bonus depending
        /// on share; see Patch_AvirFeignalliteWorkSpeed. Read per job, so unlike the
        /// substitution toggle this takes effect immediately. 1.0 disables the whole patch.
        /// </summary>
        public float feignalliteWorkMultiplier = 5f;

        // ---- artifactorics: experience and point prices (live, no restart) ----
        /// <summary>Multiplies the experience every artifact (worn or inlaid) is credited with.</summary>
        public float artifactXpFactor = 1f;
        /// <summary>Multiplies the experience every Avir amulet is credited with.</summary>
        public float amuletXpFactor = 1f;
        /// <summary>A'Shaign amulets learn from all other skills: this share of the artifacts' cut.</summary>
        public float ashaignOffPathShare = 0.5f;
        /// <summary>Experience price of points (artifacts and amulets): the first, the step after each,
        /// the larger step after `lateStepAfterPoint` points.</summary>
        public float firstPointXp = 10000f;
        public float pointXpStep = 2500f;
        public float pointXpStepLate = 5000f;
        public int lateStepAfterPoint = 10;
        /// <summary>Amulet: price multiplier of the stones of paths other than its own.</summary>
        public int offBranchCostFactor = 1;

        /// <summary>RimJobWorld: an Avir seeks love only while its sex need is below this.</summary>
        public float avirLovinThreshold = 0.75f;

        /// <summary>Folder for the player's ritual files; empty - "AvirRituals" beside the mod folder.</summary>
        public string ritualFolder = "";

        /// <summary>New pawns (traders, raiders, visitors) are not made random relatives of the colony's pawns.</summary>
        public bool noRandomRelativesOfColonists = true;

        /// <summary>Points the amulet's milestones cost, for the 2nd..5th milestone (stones 9, 12, 15, 18). The first
        /// milestone (stone 3) always costs 1.</summary>
        public int milestone9Cost = 2, milestone12Cost = 2, milestone15Cost = 2, milestone18Cost = 2;

        /// <summary>How much of the world the Avir regions take (the north and the twilight forest around it): 1 as
        /// made, 0 none, up to 3. Read when a world is generated.</summary>
        public float avirRegionCoverage = 1f;

        /// <summary>Socket glows placed with the dev tuning tool, kept across restarts:
        /// "defName|size|x,y;x,y;..." per building (applied at startup, see SocketTuningStore).</summary>
        public List<string> socketTuning = new List<string>();

        /// <summary>Optional content, on by default: the author's own music in the Avir lands, and tattoos borrowed
        /// (with thanks) from other mods. See the Credits window in the settings.</summary>
        public bool avirMusic = true;
        /// <summary>Arlia's remarks in ritualistics (and her red ink round what was left empty).</summary>
        public bool arliaOn = true;
        public bool ritualItalic = true;
        public bool magicOn;
        /// <summary>Magic: the map's stream strengthens mages (and presses greatness on them); a mage weighs in the stream as this many pawns.</summary>
        public bool flowStrengthensMages = true;
        // air's momentum: seconds the wind holds between casts, share per stage, stages
        public float airMomentumSeconds = 10f, airMomentumStep = 0.15f;
        public int airMomentumStages = 3;
        // earth (and magma, bog) casts slower by this much
        public float earthCastFactor = 1.5f;
        // armour penetration of magic's blows, at a blow of 10 (it grows with the blow): with Combat Extended - MPa (blunt) and
        // mm (sharp); without it - the game's own share of armour pierced
        public float magicPenWaterCE = 4f, magicPenEarthCE = 6f, magicPenAirCE = 2.5f, magicPenSharpCE = 1f;
        public float magicPenWater = 0.1f, magicPenEarth = 0.15f, magicPenAir = 0.05f, magicPenSharp = 0.05f;
        // how far penetration grows with the craft of the blow (Amplify on its verb, a great circle), and no further
        public float magicPenDepthMax = 4f;
        public float mageFlowWeight = 3f;
        /// <summary>Where a kept spell goes: 0 the colony's book, 1 files beside the mod (every game), 2 both.</summary>
        public int spellKeep = 0;
        /// <summary>Her introduction given (the book, the editor); the codex in plain words.</summary>
        public bool arliaIntroBook, arliaIntroEditor, codexSimple = true;
        public List<string> codexPinned = new List<string>();
        // the feast of the Singers: how long a voice lends its skill, what a half-forgotten one weighs, how fast the unused fade
        public int singerVoiceUses = 5;
        public float singerForgottenShare = 0.25f;
        public float singerFadeDays = 30f;
        // the Singer's network: the limit of points (by default as many as there are stones)
        public bool singerUncapped;
        public int singerPointCap = 45;
        // how much the will of a mind and the Singer's own voice weigh on each path (0-100%)
        public Dictionary<string, float> singerSocWeights = new Dictionary<string, float>();
        public bool borrowedTattoos = true;

        public void ResetArtifactorics()
        {
            artifactXpFactor = 1f; amuletXpFactor = 1f; ashaignOffPathShare = 0.5f;
            firstPointXp = 10000f; pointXpStep = 2500f; pointXpStepLate = 5000f; lateStepAfterPoint = 10;
            offBranchCostFactor = 1;
            milestone9Cost = milestone12Cost = milestone15Cost = milestone18Cost = 2;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref feignalliteSubstitution, "feignalliteSubstitution", true);
            Scribe_Values.Look(ref npcStrength, "npcStrength", 0.5f);
            Scribe_Values.Look(ref npcBranchCap, "npcBranchCap", 12);
            Scribe_Values.Look(ref caravanMax, "caravanMax", 10);
            Scribe_Values.Look(ref feignalliteWorkMultiplier, "feignalliteWorkMultiplier", 5f);
            Scribe_Values.Look(ref artifactXpFactor, "artifactXpFactor", 1f);
            Scribe_Values.Look(ref amuletXpFactor, "amuletXpFactor", 1f);
            Scribe_Values.Look(ref ashaignOffPathShare, "ashaignOffPathShare", 0.5f);
            Scribe_Values.Look(ref firstPointXp, "firstPointXp", 10000f);
            Scribe_Values.Look(ref pointXpStep, "pointXpStep", 2500f);
            Scribe_Values.Look(ref pointXpStepLate, "pointXpStepLate", 5000f);
            Scribe_Values.Look(ref lateStepAfterPoint, "lateStepAfterPoint", 10);
            Scribe_Values.Look(ref offBranchCostFactor, "offBranchCostFactor", 1);
            Scribe_Values.Look(ref avirLovinThreshold, "avirLovinThreshold", 0.75f);
            Scribe_Values.Look(ref ritualFolder, "ritualFolder", "");
            Scribe_Values.Look(ref noRandomRelativesOfColonists, "noRandomRelativesOfColonists", true);
            Scribe_Values.Look(ref milestone9Cost, "milestone9Cost", 2);
            Scribe_Values.Look(ref milestone12Cost, "milestone12Cost", 2);
            Scribe_Values.Look(ref milestone15Cost, "milestone15Cost", 2);
            Scribe_Values.Look(ref milestone18Cost, "milestone18Cost", 2);
            Scribe_Values.Look(ref avirRegionCoverage, "avirRegionCoverage", 1f);
            Scribe_Collections.Look(ref socketTuning, "socketTuning", LookMode.Value);
            Scribe_Values.Look(ref avirMusic, "avirMusic", true);
            Scribe_Values.Look(ref arliaOn, "arliaOn", true);
            Scribe_Values.Look(ref ritualItalic, "ritualItalic", true);
            Scribe_Values.Look(ref magicOn, "magicOn", false);
            Scribe_Values.Look(ref flowStrengthensMages, "flowStrengthensMages", true);
            Scribe_Values.Look(ref airMomentumSeconds, "airMomentumSeconds", 10f);
            Scribe_Values.Look(ref airMomentumStep, "airMomentumStep", 0.15f);
            Scribe_Values.Look(ref airMomentumStages, "airMomentumStages", 3);
            Scribe_Values.Look(ref earthCastFactor, "earthCastFactor", 1.5f);
            Scribe_Values.Look(ref magicPenWaterCE, "magicPenWaterCE2", 4f); Scribe_Values.Look(ref magicPenEarthCE, "magicPenEarthCE2", 6f);
            Scribe_Values.Look(ref magicPenAirCE, "magicPenAirCE2", 2.5f); Scribe_Values.Look(ref magicPenSharpCE, "magicPenSharpCE2", 1f);
            Scribe_Values.Look(ref magicPenWater, "magicPenWater2", 0.1f); Scribe_Values.Look(ref magicPenEarth, "magicPenEarth2", 0.15f);
            Scribe_Values.Look(ref magicPenAir, "magicPenAir2", 0.05f); Scribe_Values.Look(ref magicPenSharp, "magicPenSharp2", 0.05f);
            Scribe_Values.Look(ref magicPenDepthMax, "magicPenDepthMax2", 4f);
            Scribe_Values.Look(ref mageFlowWeight, "mageFlowWeight", 3f);
            Scribe_Values.Look(ref spellKeep, "spellKeep", 0);
            Scribe_Values.Look(ref arliaIntroBook, "arliaIntroBook");
            Scribe_Values.Look(ref arliaIntroEditor, "arliaIntroEditor");
            Scribe_Values.Look(ref codexSimple, "codexSimple", true);
            Scribe_Collections.Look(ref codexPinned, "codexPinned", LookMode.Value);
            if (codexPinned == null) codexPinned = new List<string>();
            Scribe_Values.Look(ref singerVoiceUses, "singerVoiceUses", 5);
            Scribe_Values.Look(ref singerForgottenShare, "singerForgottenShare", 0.25f);
            Scribe_Values.Look(ref singerFadeDays, "singerFadeDays", 30f);
            Scribe_Values.Look(ref singerUncapped, "singerUncapped");
            Scribe_Values.Look(ref singerPointCap, "singerPointCap", 45);
            Scribe_Collections.Look(ref singerSocWeights, "singerSocWeights", LookMode.Value, LookMode.Value);
            if (singerSocWeights == null) singerSocWeights = new Dictionary<string, float>();
            Scribe_Values.Look(ref borrowedTattoos, "borrowedTattoos", true);
            if (socketTuning == null) socketTuning = new List<string>();
        }
    }

    /// <summary>
    /// Mod settings entry point. Shows up under Options -> Mod settings -> Avir.
    /// </summary>
    public class AvirMod : Mod
    {
        public static AvirSettings Settings;
        public static ModContentPack Pack;

        public AvirMod(ModContentPack content) : base(content)
        {
            Pack = content;
            Settings = GetSettings<AvirSettings>();
            // The old-gene back-compat hook must be in place before anything reads custom
            // xenotype files or saves - some mods load xenotypes in their own startup code,
            // which ran before our [StaticConstructorOnStartup] and logged "Could not load
            // reference to Verse.GeneDef named Avir_...".
            try { AvirGeneMerge.EnsureEarly(); }
            catch (System.Exception e) { Log.Error("[Avir] Gene merge: early hook failed: " + e); }
        }

        public override string SettingsCategory()
        {
            return "Avir";
        }

        private static Vector2 scroll, singerScroll;
        private static bool singersTab, magicTab;

        /// <summary>Magic, on its own tab.</summary>
        private static void DrawMagic(Listing_Standard list, AvirSettings o)
        {
            Text.Font = GameFont.Medium;
            list.Label("Magic");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Magic (in development)", ref o.magicOn,
                "The magic system: Arcana, casters (wizards and sorcerers), spells written like rituals, sanctuaries. Off: none of it exists in the game - "
                + "no traits, no implants, no spells, no new runes; the rest of the mod (artifacts, ritualistics, the Song) works as before. "
                + "Left off while the system is being built. Takes effect after a restart.");
            list.Gap();
            list.CheckboxLabeled("The stream strengthens casters", ref o.flowStrengthensMages,
                "Under a strong stream on the map (restless and above) a caster's spells grow stronger - and greatness presses on them harder with every spell.");
            list.Label("A caster weighs in the stream as " + o.mageFlowWeight.ToString("0.#") + " pawns", -1f,
                new TipSignal("How much a wizard or sorcerer adds to the map's stream, counted in ordinary pawns (their psychic sensitivity times this). 1 - like anyone else."));
            o.mageFlowWeight = Mathf.Round(list.Slider(o.mageFlowWeight, 1f, 6f) * 2f) / 2f;
            list.Gap();
            list.Label("Air's momentum: casts within " + o.airMomentumSeconds.ToString("0") + " s of each other", -1f,
                new TipSignal("Spells of the air (and storm, haze, dust) cast one after another come quicker and cheaper while the wind holds. A longer pause lets it drop."));
            o.airMomentumSeconds = Mathf.Round(list.Slider(o.airMomentumSeconds, 3f, 30f));
            list.Label("each stage -" + o.airMomentumStep.ToStringPercent() + " cast time and Arcana, up to " + o.airMomentumStages + " stages (-" + Mathf.Min(0.9f, o.airMomentumStep * o.airMomentumStages).ToStringPercent() + ")");
            o.airMomentumStep = Mathf.Round(list.Slider(o.airMomentumStep, 0.05f, 0.3f) * 100f) / 100f;
            o.airMomentumStages = Mathf.RoundToInt(list.Slider(o.airMomentumStages, 1f, 5f));
            list.Label("Earth casts slower: ×" + o.earthCastFactor.ToString("0.0#"), -1f,
                new TipSignal("Earth is heavy: spells of earth, and of magma and bog, take this much longer to cast."));
            o.earthCastFactor = Mathf.Round(list.Slider(o.earthCastFactor, 1f, 2.5f) * 10f) / 10f;
            list.Gap();
            // how magic's blows go through armour: the measures of Combat Extended when it is on, the game's own otherwise
            bool ce = ModsConfig.IsActive("CETeam.CombatExtended");
            list.Label(ce ? "Armour penetration of spells (Combat Extended found) - at a blow of 10, grows with the blow:" : "Armour penetration of spells - at a blow of 10, grows with the blow:", -1f,
                new TipSignal("Fire, steam, haze and lightning burn: they meet armour as heat, by the game's own rules, and are not set here."));
            if (ce)
            {
                list.Label("  water, bog: " + o.magicPenWaterCE.ToString("0.#") + " MPa"); o.magicPenWaterCE = Mathf.Round(list.Slider(o.magicPenWaterCE, 0f, 40f) * 2f) / 2f;
                list.Label("  earth, magma: " + o.magicPenEarthCE.ToString("0.#") + " MPa"); o.magicPenEarthCE = Mathf.Round(list.Slider(o.magicPenEarthCE, 0f, 40f) * 2f) / 2f;
                list.Label("  air and the rest: " + o.magicPenAirCE.ToString("0.#") + " MPa"); o.magicPenAirCE = Mathf.Round(list.Slider(o.magicPenAirCE, 0f, 40f) * 2f) / 2f;
                list.Label("  cutting blows (sand, shards): " + o.magicPenSharpCE.ToString("0.#") + " mm"); o.magicPenSharpCE = Mathf.Round(list.Slider(o.magicPenSharpCE, 0f, 20f) * 2f) / 2f;
            }
            else
            {
                list.Label("  water, bog: " + o.magicPenWater.ToStringPercent()); o.magicPenWater = Mathf.Round(list.Slider(o.magicPenWater, 0f, 1f) * 20f) / 20f;
                list.Label("  earth, magma: " + o.magicPenEarth.ToStringPercent()); o.magicPenEarth = Mathf.Round(list.Slider(o.magicPenEarth, 0f, 1f) * 20f) / 20f;
                list.Label("  air and the rest: " + o.magicPenAir.ToStringPercent()); o.magicPenAir = Mathf.Round(list.Slider(o.magicPenAir, 0f, 1f) * 20f) / 20f;
                list.Label("  cutting blows (sand, shards): " + o.magicPenSharp.ToStringPercent()); o.magicPenSharp = Mathf.Round(list.Slider(o.magicPenSharp, 0f, 1f) * 20f) / 20f;
            }
            list.Label("Penetration grows with the craft of the blow up to ×" + o.magicPenDepthMax.ToString("0.0#"), -1f,
                new TipSignal("Amplify runes on the striking verb drive a blow deeper, each less than the one before (1 + half the square root of their number: 1 - x1.5, 4 - x2, 9 - x2.5). Many circles of one element and focus make it stronger, not deeper. Away from the heart of the blow it goes in shallower: at its edge, by half."));
            o.magicPenDepthMax = Mathf.Round(list.Slider(o.magicPenDepthMax, 1f, 6f) * 4f) / 4f;
            list.Gap();
            list.Label("Where a kept spell goes:");
            if (list.RadioButton("The colony's book (this game)", o.spellKeep == 0)) o.spellKeep = 0;
            if (list.RadioButton("Files beside the mod (every game sees them)", o.spellKeep == 1)) o.spellKeep = 1;
            if (list.RadioButton("Both", o.spellKeep == 2)) o.spellKeep = 2;
        }

        /// <summary>Everything of the Singers, on their own tab.</summary>
        private static void DrawSingers(Listing_Standard list, AvirSettings o)
        {
            Text.Font = GameFont.Medium;
            list.Label("Singers (Linarians)");
            Text.Font = GameFont.Small;
            // ---- the feast ----
            float vu = o.singerVoiceUses;
            Slider(list, "a voice lends its skill " + o.singerVoiceUses + " times",
                "After that it is half-forgotten: only a name, no skill to lend.", ref vu, 1f, 20f, 1f);
            o.singerVoiceUses = Mathf.RoundToInt(vu);
            Slider(list, "a half-forgotten voice weighs " + o.singerForgottenShare.ToStringPercent() + " of a voice in her collection",
                "In her aria's strength and in the scream of the collection. In the bonus for every ten voices it always counts whole.",
                ref o.singerForgottenShare, 0.05f, 1f, 0.05f);
            Slider(list, "unchosen voices fade in " + Mathf.RoundToInt(o.singerFadeDays) + " days",
                "A voice that is not one of her chosen (one per skill) fades to half-forgotten after this long.", ref o.singerFadeDays, 5f, 120f, 1f);

            list.CheckboxLabeled("change the limit of the Aria's points (45 by default)", ref o.singerUncapped,
                "On: choose the limit yourself, from 25 to 100. Points beyond the stones of the network go to the Aria itself: a wider zone and a stronger song.");
            if (o.singerUncapped)
            {
                float pcap = o.singerPointCap;
                Slider(list, "  Limit of points: " + o.singerPointCap, "From 25 to 100.", ref pcap, 25f, 100f, 1f);
                o.singerPointCap = Mathf.RoundToInt(pcap);
            }

            // ---- the Singers' susceptibility, per path ----
            list.GapLine();
            list.Label("how much a mind's will and her own voice decide, per path (0% - only psychic sensitivity counts)");
            foreach (string path in Avir.Linarian.LinarianTactics.SocPaths)
            {
                float v = Avir.Linarian.LinarianTactics.SocWeight(path);
                Slider(list, "  " + Avir.Linarian.LinarianNetwork.AreaLabel(path) + ": " + v.ToStringPercent(),
                    "The will is the certainty loss of the mind's faith (a firm faith resists); her voice is her social impact. "
                    + "At 0% neither matters on this path, at 100% both decide fully.", ref v, 0f, 1f, 0.05f);
                o.singerSocWeights[path] = v;
            }
            if (list.ButtonText("susceptibility to defaults")) o.singerSocWeights.Clear();
        }

        private static void MilestoneSlider(Listing_Standard list, int stone, ref int cost)
        {
            float v = cost;
            Slider(list, "Milestone at stone " + stone + ": " + cost + " point" + (cost == 1 ? "" : "s"),
                "Price of this milestone stone on the amulet's own path (other paths multiply it). Already bought stones keep what they cost.",
                ref v, 1f, 8f, 1f);
            cost = Mathf.RoundToInt(v);
        }

        private static void Slider(Listing_Standard list, string caption, string tip, ref float value, float min, float max, float round)
        {
            list.Label(caption, -1f, new TipSignal(tip));
            value = Mathf.Round(list.Slider(value, min, max) / round) * round;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // two tabs: the Avir and everything else, and the Singers on their own
            Rect tabs = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            if (Widgets.ButtonText(new Rect(tabs.x, tabs.y, 180f, 30f), (singersTab || magicTab ? "" : "> ") + "Avir and the mod")) { singersTab = false; magicTab = false; }
            if (Widgets.ButtonText(new Rect(tabs.x + 190f, tabs.y, 180f, 30f), (singersTab ? "> " : "") + "Singers (Linarians)")) { singersTab = true; magicTab = false; }
            if (Widgets.ButtonText(new Rect(tabs.x + 380f, tabs.y, 180f, 30f), (magicTab ? "> " : "") + "Magic")) { magicTab = true; singersTab = false; }
            inRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);
            if (magicTab)
            {
                Listing_Standard ml = new Listing_Standard();
                ml.Begin(inRect);
                DrawMagic(ml, Settings);
                ml.End();
                base.DoSettingsWindowContents(inRect);
                return;
            }
            if (singersTab)
            {
                Rect sview = new Rect(0f, 0f, inRect.width - 20f, 760f);
                Widgets.BeginScrollView(inRect, ref singerScroll, sview);
                Listing_Standard sl = new Listing_Standard();
                sl.Begin(sview);
                DrawSingers(sl, Settings);
                sl.End();
                Widgets.EndScrollView();
                base.DoSettingsWindowContents(inRect);
                return;
            }
            Rect view = new Rect(0f, 0f, inRect.width - 20f, 1200f);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            Listing_Standard list = new Listing_Standard();
            list.Begin(view);

            // ---- optional content and credits ----
            Text.Font = GameFont.Medium;
            list.Label("Optional content");
            Text.Font = GameFont.Small;
            list.CheckboxLabeled("Avir music in the Avir lands", ref Settings.avirMusic,
                "The author's own compositions play in the twilight forest, the burning forest, the frozen waste and the frost oasis, "
                + "alongside the game's music. Takes effect from the next song.");
            list.CheckboxLabeled("Borrowed tattoos", ref Settings.borrowedTattoos,
                "Tattoos taken, with thanks, from other mods and recoloured for the Avir. See Credits for who made them. "
                + "Takes effect after a restart.");
            bool arlia = Settings.arliaOn;
            list.CheckboxLabeled("Arlie's remarks in ritualistics", ref arlia,
                "A teacher in the margins: remarks on your drawings and rituals (in the ritual windows, and in a corner of the screen for what happens on the land), "
                + "and red ink round whatever you left empty. She grows sharper when made to repeat herself.");
            if (arlia != Settings.arliaOn) Avir.Ritualistics.Arlia.Toggle();
            list.CheckboxLabeled("Ritual windows in italics (the book, the editor, the codex)", ref Settings.ritualItalic, "Every word in the ritual windows written in italics, as by hand. Off: the game's plain letters.");
            if (list.ButtonText("Replay Arlie's introduction (next time the ritual book and editor open)")) { Settings.arliaIntroBook = false; Settings.arliaIntroEditor = false; }
            if (list.ButtonText("Credits")) Find.WindowStack.Add(new Dialog_AvirCredits());
            list.GapLine();

            // ---- Avir of other factions ----
            Text.Font = GameFont.Medium;
            list.Label("Avir of other factions");
            Text.Font = GameFont.Small;
            Slider(list, "Development of their amulets and artifacts: " + Settings.npcStrength.ToStringPercent(),
                "Raiders, traders, visitors and allies of the Avir factions come with amulets and artifacts developed by their status and age. "
                + "This share scales it: fewer stones, fewer artifact cells, lower qualities. Applies to pawns generated from now on.",
                ref Settings.npcStrength, 0.1f, 1f, 0.05f);
            float cap = Settings.npcBranchCap;
            Slider(list, "Deepest path of their amulets: " + Settings.npcBranchCap + " stones",
                "No path of an NPC's amulet is opened past this. 12 stops below unstoppable and the hunting series (15, 18); 18 - no limit.",
                ref cap, 3f, 18f, 1f);
            Settings.npcBranchCap = Mathf.RoundToInt(cap);
            float cm = Settings.caravanMax;
            Slider(list, "Most Avir in a trade caravan: " + Settings.caravanMax,
                "The trader and its escort together. Pack animals do not count.", ref cm, 3f, 24f, 1f);
            Settings.caravanMax = Mathf.RoundToInt(cm);
            list.GapLine();

            list.CheckboxLabeled(
                "No random relatives of my colonists",
                ref Settings.noRandomRelativesOfColonists,
                "New pawns - traders, raiders, visitors, wanderers - are not made random family of your colony's pawns "
                + "(nor of their close kin). Relations among the starting colonists and relatives brought by quests on purpose stay.");

            list.Gap();

            list.CheckboxLabeled(
                "Feignallite as a material substitute",
                ref Settings.feignalliteSubstitution,
                "When enabled, feignallite can replace ordinary materials in most crafting "
                + "recipes - stone, metal, manufactured components and bionics. Textiles, "
                + "medicine, drugs, plant matter, wood and the other Avir materials are never "
                + "substituted.\n\nTakes effect after a restart, because recipe filters are "
                + "rebuilt when the game loads.");

            list.Gap();

            // Work speed multiplier. Live - the patch reads this per job, so moving the slider
            // changes the next bill a pawn starts. At 1.0 the patch short-circuits entirely.
            float ceil = Settings.feignalliteWorkMultiplier;

            // Caption and tooltip are hoisted into typed locals deliberately - do not inline
            // them back. Listing_Standard declares TWO three-argument Label overloads,
            // (TaggedString, float, string) and (string, float, TipSignal?), and a bare string
            // literal in the third slot converts implicitly into either one, so the call is
            // ambiguous and fails with CS0121. Passing a real TipSignal settles it on the
            // second overload with no cast needed.
            string speedCaption = "Feignallite work speed (max): " + ceil.ToString("0.0") + "x";
            TipSignal speedTip = new TipSignal(
                "How much faster a crafting job runs when feignallite is the material. This is "
                + "the CEILING, reached when the job is made entirely of feignallite; partial "
                + "jobs get a proportional share of the bonus.\n\n"
                + "   all feignallite   " + ceil.ToString("0.0") + "x\n"
                + "   75% or more       " + (1f + (ceil - 1f) * 0.75f).ToString("0.0") + "x\n"
                + "   50% or more       " + (1f + (ceil - 1f) * 0.50f).ToString("0.0") + "x\n"
                + "   any at all        " + (1f + (ceil - 1f) * 0.25f).ToString("0.0") + "x\n\n"
                + "Share is counted in units of material, not ingredient lines. Set to 1.0 to "
                + "switch the speedup off. Takes effect immediately; no restart needed.");
            list.Label(speedCaption, -1f, speedTip);
            Settings.feignalliteWorkMultiplier =
                (float)System.Math.Round(list.Slider(Settings.feignalliteWorkMultiplier, 1f, 6f) * 10f) / 10f;

            list.Gap();

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            list.Label("The substitution toggle requires restarting RimWorld. The work speed "
                       + "slider does not.");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // ---- artifactorics ----
            list.GapLine();
            Text.Font = GameFont.Medium;
            list.Label("Artifactorics: experience and prices");
            Text.Font = GameFont.Small;
            AvirSettings o = Settings;
            Slider(list, "Artifact experience: x" + o.artifactXpFactor.ToString("0.0#"),
                "Multiplies the experience every artifact is credited with (the pawn's own share is unchanged).",
                ref o.artifactXpFactor, 0.1f, 5f, 0.05f);
            Slider(list, "Amulet experience: x" + o.amuletXpFactor.ToString("0.0#"),
                "Multiplies the experience every Avir amulet is credited with.", ref o.amuletXpFactor, 0.1f, 5f, 0.05f);
            Slider(list, "A'Shaign learning from other skills: " + o.ashaignOffPathShare.ToStringPercent() + " of the artifacts' cut ("
                         + (o.ashaignOffPathShare * 0.2f).ToStringPercent("0.#") + " of a gain)",
                "An A'Shaign amulet learns from every skill outside its own line too. Of each gain the artifacts take a cut "
                + "(20% by default); the amulet gets this share of that cut. 0.5 = 10% of the gain.",
                ref o.ashaignOffPathShare, 0f, 1f, 0.05f);
            Slider(list, "First point: " + o.firstPointXp.ToString("N0") + " xp",
                "Experience price of an artifact's or amulet's first point.", ref o.firstPointXp, 1000f, 50000f, 500f);
            Slider(list, "Each next point: +" + o.pointXpStep.ToString("N0") + " xp",
                "How much more each next point costs (early points).", ref o.pointXpStep, 0f, 20000f, 250f);
            float after = o.lateStepAfterPoint;
            Slider(list, "Late points start after point " + o.lateStepAfterPoint,
                "From this point on the price grows by the late step instead.", ref after, 1f, 40f, 1f);
            o.lateStepAfterPoint = Mathf.RoundToInt(after);
            Slider(list, "Each late point: +" + o.pointXpStepLate.ToString("N0") + " xp",
                "How much more each point after the late threshold costs.", ref o.pointXpStepLate, 0f, 40000f, 250f);
            float off = o.offBranchCostFactor;
            Slider(list, "Amulet: stones of other paths cost x" + o.offBranchCostFactor,
                "Price multiplier of an amulet's stones outside its own path (milestones keep their own price).",
                ref off, 1f, 4f, 1f);
            o.offBranchCostFactor = Mathf.RoundToInt(off);
            list.GapLine();
            Slider(list, "Avir regions on the world map: x" + o.avirRegionCoverage.ToString("0.00") + (o.avirRegionCoverage <= 0.001f ? " (none)" : ""),
                "How much of a new world the Avir regions cover - the frozen waste, the frost oasis and the twilight forest around them. Takes effect when a world is generated.",
                ref o.avirRegionCoverage, 0f, 3f, 0.05f);
            list.GapLine();
            list.Label("Amulet milestones: the first (stone 3) always costs 1 point.");
            MilestoneSlider(list, 9, ref o.milestone9Cost);
            MilestoneSlider(list, 12, ref o.milestone12Cost);
            MilestoneSlider(list, 15, ref o.milestone15Cost);
            MilestoneSlider(list, 18, ref o.milestone18Cost);
            list.Gap();
            string ex = "Points 1-5 cost: ";
            float run = 0f;
            for (int i = 1; i <= 5; i++)
            {
                float c = i <= o.lateStepAfterPoint ? o.firstPointXp + o.pointXpStep * (i - 1)
                    : o.firstPointXp + o.pointXpStep * (o.lateStepAfterPoint - 1) + o.pointXpStepLate * (i - o.lateStepAfterPoint);
                run += c;
                ex += c.ToString("N0") + (i < 5 ? ", " : "");
            }
            list.Label(ex + "  (total " + run.ToString("N0") + ")");
            if (list.ButtonText("Reset artifactorics to defaults")) o.ResetArtifactorics();

            // ---- RimJobWorld ----
            list.GapLine();
            Slider(list, "RimJobWorld: an Avir seeks love only below " + o.avirLovinThreshold.ToStringPercent() + " sex need",
                "Above this an Avir neither starts lovin' nor joins a partner in bed - it sleeps instead. No effect without RimJobWorld.",
                ref o.avirLovinThreshold, 0.3f, 1f, 0.05f);

            // ---- ritualistics ----
            list.GapLine();
            list.Label("Ritual files folder (empty - \"AvirRituals\" beside the mod folder). Now: " + Avir.Ritualistics.RitualFiles.Folder);
            o.ritualFolder = list.TextEntry(o.ritualFolder ?? "");

            list.End();
            Widgets.EndScrollView();
            base.DoSettingsWindowContents(inRect);
        }
    }
}
