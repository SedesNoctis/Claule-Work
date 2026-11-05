using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>
    /// Stage 11 - an active ability carried by an ability stone. Any stone can carry any ability;
    /// the stone's metal adds its own bonus to the ability and a weak focus to the artifact.
    /// Numbers are given at power x1; power = stone quality x metal x the owner's psychic
    /// sensitivity x the artifact's development. Abilities depend strongly on their owner.
    /// </summary>
    public class ArtifactAbilityDef : Def
    {
        /// <summary>The castable ability granted while the stone works. Null for passive stones
        /// (Architect's blueprint).</summary>
        public AbilityDef ability;
        public bool combat = true;
        public string iconPath;
        /// <summary>Charge cost per use, in units of a 100-unit artifact.</summary>
        public float chargeCost;
        /// <summary>Held abilities (paralysis): cost per second while held.</summary>
        public float chargePerSecond;
        public float baseRadius;
        public float baseDurationSeconds;
        public float basePower = 1f;
        public int baseCooldownTicks;
        public int uiOrder;
        /// <summary>Stage 12c: a weapon stone works only in a melee weapon. Other combat stones work
        /// in armour and in weapons; peaceful stones only in armour.</summary>
        public bool weaponOnly;
        /// <summary>Stage 12c: the stone can be set to use itself in a fight.</summary>
        public bool canAuto;
        /// <summary>Stage 12d: the stone carries an energy type, fixed when it is made (burning spear).</summary>
        public bool usesEnergy;
        /// <summary>Stage 12d: works only in these weapons (Avir spears). Empty = any melee weapon.</summary>
        public List<ThingDef> onlyWeapons = new List<ThingDef>();

        public bool AllowedIn(ArtifactCarrier c)
        {
            if (weaponOnly) return c == ArtifactCarrier.Weapon;
            return c == ArtifactCarrier.Apparel || combat;
        }

        public string WorksInText => weaponOnly ? "melee weapons only" : combat ? "armour and melee weapons" : "armour only";

        /// <summary>Numbers block shown in the codex.</summary>
        [MustTranslate] public string numbers;

        [Unsaved] private Texture2D icon;

        public Texture2D Icon
        {
            get
            {
                if (icon == null)
                    icon = (!iconPath.NullOrEmpty() ? ContentFinder<Texture2D>.Get(iconPath, false) : null)
                           ?? ability?.uiIcon ?? BaseContent.BadTex;
                return icon;
            }
        }
    }

    /// <summary>An ability stone, loose (in CompAbilityStone) or set into an artifact.</summary>
    public class AbilityStoneState : IExposable
    {
        public ArtifactAbilityDef ability;
        public ArtifactMetalDef metal;
        /// <summary>Stage 12d: the energy of a burning spear stone.</summary>
        public SpearEnergyDef energy;
        public QualityCategory quality = QualityCategory.Normal;
        public string maker;
        /// <summary>Switched off by re-attuning (only two abilities work per pawn).</summary>
        public bool dormant;
        /// <summary>Architect's blueprint: background repair on.</summary>
        public bool backgroundOn = true;
        /// <summary>Stage 12c: used on its own in a fight (combat stones that allow it).</summary>
        public bool autoUse;
        /// <summary>Burning spear: the glaive's spear fire (a ranged mode). Unset - on, except for brawlers.</summary>
        public bool rangedModeSet;
        public bool rangedMode;

        public AbilityStoneState Clone() => (AbilityStoneState)MemberwiseClone();

        public string Label => (ability?.label ?? "?") + (energy != null ? " (" + energy.label + ")" : "") + ", " + (metal?.label ?? "?");

        public void ExposeData()
        {
            Scribe_Defs.Look(ref ability, "ability");
            Scribe_Defs.Look(ref metal, "metal");
            Scribe_Values.Look(ref quality, "quality", QualityCategory.Normal);
            Scribe_Values.Look(ref maker, "maker");
            Scribe_Values.Look(ref dormant, "dormant");
            Scribe_Values.Look(ref backgroundOn, "backgroundOn", true);
            Scribe_Values.Look(ref autoUse, "autoUse");
            Scribe_Defs.Look(ref energy, "energy");
            Scribe_Values.Look(ref rangedModeSet, "rangedModeSet");
            Scribe_Values.Look(ref rangedMode, "rangedMode");
        }
    }

    public class CompProperties_AbilityStone : CompProperties
    {
        public CompProperties_AbilityStone() { compClass = typeof(CompAbilityStone); }
    }

    public class CompAbilityStone : ThingComp
    {
        public AbilityStoneState stone = new AbilityStoneState();

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (stone.ability == null) stone.ability = DefDatabase<ArtifactAbilityDef>.AllDefsListForReading.RandomElementWithFallback();
            if (stone.metal == null) stone.metal = ArtifactoricsDatabase.Metals.Where(m => m.allowedAsStone).RandomElementWithFallback();
            if (stone.ability != null && stone.ability.usesEnergy && stone.energy == null)
            {
                stone.energy = SpearEnergyDef.For(stone.metal).RandomElementWithFallback();
                if (stone.energy == null)   // a random stone of a metal with no energies: give it another ability
                    stone.ability = DefDatabase<ArtifactAbilityDef>.AllDefsListForReading.Where(d => !d.usesEnergy).RandomElementWithFallback();
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (stone == null) stone = new AbilityStoneState();
            stone.ExposeData();
        }

        public override string TransformLabel(string label) => stone.ability == null ? label : label + " (" + stone.Label + ")";

        public override bool AllowStackWith(Thing other) => false;

        public override string CompInspectStringExtra()
        {
            if (stone.ability == null) return null;
            return "Ability: " + stone.ability.LabelCap.ToString() + "\nMetal: " + (stone.metal != null ? stone.metal.LabelCap.ToString() : "?")
                   + ", " + stone.quality.GetLabel()
                   + (stone.maker.NullOrEmpty() ? "" : " (by " + stone.maker + ")");
        }

    }

    /// <summary>The stone item: one pre-tinted texture per metal,
    /// Things/Item/Artifactorics/AbilityStone_{texKey}; the grey one when missing.</summary>
    public class Thing_AbilityStone : ThingWithComps
    {
        private static readonly Dictionary<string, Graphic> graphics = new Dictionary<string, Graphic>();

        public override Graphic Graphic
        {
            get
            {
                ArtifactMetalDef m = GetComp<CompAbilityStone>()?.stone?.metal;
                if (m == null || m.texKey.NullOrEmpty()) return base.Graphic;
                string path = "Things/Item/Artifactorics/AbilityStone_" + m.texKey;
                if (!graphics.TryGetValue(path, out Graphic g))
                {
                    g = ContentFinder<Texture2D>.Get(path, false) == null ? null
                        : GraphicDatabase.Get<Graphic_Single>(path, ShaderDatabase.Cutout, def.graphicData.drawSize, Color.white);
                    graphics[path] = g;
                }
                return g ?? base.Graphic;
            }
        }
    }

    /// <summary>Draws a stone in the UI: the grey base tinted by the metal, highlights on top.</summary>
    public static class AbilityStoneUI
    {
        public static void Draw(Rect r, ArtifactMetalDef m, float alpha = 1f)
        {
            Color c = m != null ? m.GemColorAt(Time.realtimeSinceStartup) : Color.white;
            GUI.color = new Color(Mathf.Min(1f, c.r * 1.2f), Mathf.Min(1f, c.g * 1.2f), Mathf.Min(1f, c.b * 1.2f), alpha);
            GUI.DrawTexture(r, ArtifactoricsTex.StoneBase, ScaleMode.ScaleToFit);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(r, ArtifactoricsTex.StoneHighlight, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }
    }

    /// <summary>A stone that works on a pawn: the artifact it sits in and the stone itself.</summary>
    public class ActiveStone
    {
        public CompArtifactSocket socket;
        public ArtifactState state;
        public AbilityStoneState stone;
        public bool active;
        public string Key => state.Uid + "|stone";
    }

    public static class ArtifactAbilities
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        /// <summary>All stones in the pawn's worn artifacts; at most maxActiveAbilitiesPerPawn of
        /// them (not dormant, artifact not empty) are active.</summary>
        public static List<ActiveStone> For(Pawn p)
        {
            var list = new List<ActiveStone>();
            if (p == null) return list;
            // Stage 12: stones in armour and in the weapon are counted separately.
            int usedArmour = 0, usedWeapon = 0, limit = S?.maxActiveAbilitiesPerPawn ?? 2;
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
            {
                ArtifactState st = s.inlaid;
                if (st?.stone?.ability == null || !st.HasHeart || s.Self) continue;
                var a = new ActiveStone { socket = s, state = st, stone = st.stone };
                bool weapon = s.Carrier == ArtifactCarrier.Weapon;
                a.active = !st.stone.dormant && st.charge > 0f && (weapon ? usedWeapon : usedArmour) < limit
                           && PsychicOk(p, st.stone.metal, out _) && st.stone.ability.AllowedIn(s.Carrier)
                           && (!st.stone.ability.usesEnergy || st.stone.energy != null)
                           && (st.stone.ability.onlyWeapons.NullOrEmpty() || st.stone.ability.onlyWeapons.Contains(s.parent.def));
                if (a.active) { if (weapon) usedWeapon++; else usedArmour++; }
                list.Add(a);
            }
            return list;
        }

        public static ActiveStone Source(Pawn p, ArtifactAbilityDef def)
        {
            foreach (ActiveStone a in For(p))
                if (a.active && a.stone.ability == def) return a;
            return null;
        }

        public static ActiveStone SourceForAbility(Pawn p, AbilityDef ability)
        {
            foreach (ActiveStone a in For(p))
                if (a.active && a.stone.ability?.ability == ability) return a;
            return null;
        }

        /// <summary>Essentium and tisserium stones follow their metal's psychic rule: below its
        /// threshold the stone does not work at all; above it every step adds stonePerStepBonus.</summary>
        public static bool PsychicOk(Pawn owner, ArtifactMetalDef m, out int steps)
        {
            steps = 0;
            if (m?.psychic == null || m.stonePerStepBonus <= 0f || owner == null) return true;
            float sens = ArtifactFocus.Sensitivity(owner);
            steps = m.psychic.Steps(sens);
            return m.psychic.IsActive(sens, ArtifactFocus.HasFearborn(owner));
        }

        public static string PsychicRuleText(ArtifactMetalDef m)
        {
            if (m?.psychic == null || m.stonePerStepBonus <= 0f) return null;
            ArtifactPsychicFocus ps = m.psychic;
            string cond = ps.thresholdWithFearborn > 0f
                ? "works only from " + ps.thresholdWithFearborn.ToStringPercent() + " psychic sensitivity with Fearborn, " + ps.thresholdWithoutFearborn.ToStringPercent() + " without"
                : "works for Fearborn owners; without Fearborn only from " + ps.thresholdWithoutFearborn.ToStringPercent() + " psychic sensitivity";
            return "+" + m.stonePerStepBonus.ToStringPercent() + " power for every " + ps.stepSize.ToStringPercent() + " of psychic sensitivity above "
                   + ps.stepsFrom.ToStringPercent() + "; " + cond;
        }

        /// <summary>Power of a stone in its owner's hands.</summary>
        public static float Power(Pawn owner, ArtifactState st, AbilityStoneState stone)
        {
            ArtifactMetalDef m = stone.metal;
            float q = S.HeartQualityFactor(stone.quality);
            float metal = m?.stonePower ?? 1f;
            if (stone.ability != null && stone.ability.combat) metal *= m?.stoneCombatPower ?? 1f;
            float sens = owner != null ? ArtifactFocus.Sensitivity(owner) : 1f;
            float scale = m?.stonePsychicScale ?? 1f;
            float sensFactor = Mathf.Clamp(1f + (sens - 1f) * scale, 0.25f, 4f);
            float development = 1f + S.abilityPowerPerPoint * (st?.PointsSpent ?? 0);
            PsychicOk(owner, m, out int steps);
            float psychic = 1f + steps * (m?.stonePerStepBonus ?? 0f);
            return Mathf.Max(0.05f, q * metal * sensFactor * development * psychic);
        }

        public static float Radius(ActiveStone a) => a.stone.ability.baseRadius * (a.stone.metal?.stoneRadius ?? 1f);
        public static float Duration(ActiveStone a) => a.stone.ability.baseDurationSeconds * (a.stone.metal?.stoneDuration ?? 1f);
        public static float Cost(ActiveStone a, float baseCost) => baseCost * (a.stone.metal?.stoneCost ?? 1f);

        public static int Cooldown(ActiveStone a)
        {
            return Mathf.RoundToInt(a.stone.ability.baseCooldownTicks * (a.stone.metal?.stoneCooldown ?? 1f));
        }

        /// <summary>Spends charge from the stone's own artifact. False (nothing spent) if short.</summary>
        public static bool Spend(Pawn p, ActiveStone a, float units)
        {
            if (units <= 0f) return true;
            float share = units / ArtifactCharge.Capacity(a.state, p);
            if (a.state.charge < share)
            {
                if (p.Faction == Faction.OfPlayer)
                    Messages.Message(p.LabelShort + "'s artifact has too little charge for " + a.stone.ability.label + ".",
                        p, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            a.state.charge -= share;
            if (a.state.charge <= 0f) { a.state.charge = 0f; ArtifactMilestones.Invalidate(p); }
            return true;
        }

        /// <summary>Gives and takes the castable abilities of the working stones.</summary>
        public static void Sync(Pawn p)
        {
            if (p.abilities == null) return;
            var want = new HashSet<AbilityDef>();
            foreach (ActiveStone a in For(p))
                if (a.active && a.stone.ability.ability != null) want.Add(a.stone.ability.ability);
            foreach (ArtifactAbilityDef d in DefDatabase<ArtifactAbilityDef>.AllDefsListForReading)
            {
                if (d.ability == null) continue;
                bool has = p.abilities.GetAbility(d.ability) != null;
                if (want.Contains(d.ability) && !has) p.abilities.GainAbility(d.ability);
                else if (!want.Contains(d.ability) && has) p.abilities.RemoveAbility(d.ability);
            }
        }

        public static string Describe(ArtifactAbilityDef d)
        {
            var sb = new StringBuilder();
            sb.Append(d.description);
            if (!d.numbers.NullOrEmpty()) sb.Append("\n\n").Append(d.numbers);
            return sb.ToString();
        }

        public static string MetalBonusText(ArtifactMetalDef m)
        {
            if (m == null) return "";
            var parts = new List<string>();
            void Add(float v, string what, bool lowerIsBetter = false)
            {
                if (Mathf.Abs(v - 1f) < 0.001f) return;
                parts.Add((v > 1f ? "+" : "") + (v - 1f).ToStringPercent() + " " + what);
            }
            Add(m.stonePower, "power");
            Add(m.stoneCombatPower, "power of combat abilities");
            Add(m.stoneRadius, "radius and area");
            Add(m.stoneDuration, "duration");
            Add(m.stoneCooldown, "cooldown");
            Add(m.stoneCost, "charge cost");
            if (Mathf.Abs(m.stonePsychicScale - 1f) > 0.001f) parts.Add("psychic sensitivity counts x" + m.stonePsychicScale.ToString("0.##"));
            string rule = PsychicRuleText(m);
            if (rule != null) parts.Add(rule);
            return parts.Count == 0 ? "no special bonus" : string.Join(", ", parts);
        }
    }

    /// <summary>Keeps the castable abilities in step, runs held paralyses, the Architect's
    /// blueprint and automatic teleports.</summary>
    public class MapComponent_ArtifactAbilities : MapComponent
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public MapComponent_ArtifactAbilities(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (S == null) return;
            int now = Find.TickManager.TicksGame;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            if (now % 250 == 0)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p.abilities == null) continue;
                    ArtifactAbilities.Sync(p);
                }
            }
            if (now % 30 == 0) ArtifactParalysis.TickHolds(map);
            if (now % 60 == 0)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p.Dead || p.Downed || p.Faction != Faction.OfPlayer) continue;
                    if (ArtifactWearCache.Worn(p).Count == 0) continue;
                    foreach (ActiveStone a in ArtifactAbilities.For(p))
                    {
                        if (!a.active) continue;
                        if (a.stone.ability == ArtifactAbilityDefOf.ArtifactAbility_Architect) ArtifactArchitect.Tick(p, a, 60);
                        else if (a.stone.ability == ArtifactAbilityDefOf.ArtifactAbility_Teleport) ArtifactTeleport.TryAuto(p, a);
                    }
                }
            }
        }
    }

    [DefOf]
    public static class ArtifactAbilityDefOf
    {
        public static ArtifactAbilityDef ArtifactAbility_Paralysis;
        public static ArtifactAbilityDef ArtifactAbility_MassParalysis;
        public static ArtifactAbilityDef ArtifactAbility_Teleport;
        public static ArtifactAbilityDef ArtifactAbility_Architect;
        public static ArtifactAbilityDef ArtifactAbility_Barrier;
        public static ArtifactAbilityDef ArtifactAbility_Push;
        public static ArtifactAbilityDef ArtifactAbility_Shroud;
        public static ArtifactAbilityDef ArtifactAbility_Stanch;
        public static ThingDef Avir_AbilityStone;
        public static JobDef Avir_InsertAbilityStone;
        public static JobDef Avir_ExtractAbilityStone;

        static ArtifactAbilityDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactAbilityDefOf)); }
    }

    // ------------------------------------------------------------------ insert / extract

    /// <summary>Anyone can set a stone: the wearer does it in the field, or for a loose artifact
    /// the nearest free colonist. Only an Avir can take a stone out.</summary>
    public static class AbilityStoneOrders
    {
        public static Pawn WorkerFor(Thing owner, bool needAvir)
        {
            Pawn holder = ArtifactCarrierUtility.HolderOf(owner);
            if (holder != null)
            {
                if (needAvir && !ArtifactoricsSettingsDef.Main.CountsAsAvir(holder)) return null;
                return holder;
            }
            Map map = owner.MapHeld;
            if (map == null) return null;
            return map.mapPawns.FreeColonistsSpawned
                .Where(p => !p.Downed && !p.Drafted && (!needAvir || ArtifactoricsSettingsDef.Main.CountsAsAvir(p))
                            && p.CanReserveAndReach(owner, PathEndMode.Touch, Danger.Some))
                .OrderBy(p => p.Position.DistanceToSquared(owner.PositionHeld)).FirstOrDefault();
        }

        public static void OrderInsert(Thing owner, Thing stone)
        {
            Pawn p = WorkerFor(owner, false);
            if (p == null) { Messages.Message("Nobody can reach it.", MessageTypeDefOf.RejectInput, false); return; }
            Job j = JobMaker.MakeJob(ArtifactAbilityDefOf.Avir_InsertAbilityStone, stone, owner);
            j.count = 1;
            j.playerForced = true;
            p.jobs.TryTakeOrderedJob(j, JobTag.Misc);
        }

        public static void OrderExtract(Thing owner)
        {
            Pawn p = WorkerFor(owner, true);
            if (p == null)
            {
                Messages.Message(ArtifactCarrierUtility.HolderOf(owner) != null
                    ? "Only an Avir can take a stone out, and the wearer is not one."
                    : "No Avir can reach it - only an Avir can take a stone out.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Job j = JobMaker.MakeJob(ArtifactAbilityDefOf.Avir_ExtractAbilityStone, owner);
            j.playerForced = true;
            p.jobs.TryTakeOrderedJob(j, JobTag.Misc);
        }
    }

    public class JobDriver_InsertAbilityStone : JobDriver
    {
        private Thing Stone => job.targetA.Thing;
        private Thing Owner => job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Owner == null || Owner.Destroyed || Dialog_ArtifactSpiral.StateOf(Owner) == null
                              || Dialog_ArtifactSpiral.StateOf(Owner).stone != null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            bool worn = ArtifactCarrierUtility.HolderOf(Owner) == pawn;
            if (!worn) yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            Toil wait = Toils_General.Wait(ArtifactoricsSettingsDef.Main?.stoneSetTicks ?? 600);
            wait.WithProgressBarToilDelay(TargetIndex.None);
            yield return wait;
            yield return new Toil
            {
                initAction = () =>
                {
                    ArtifactState st = Dialog_ArtifactSpiral.StateOf(Owner);
                    Thing carried = pawn.carryTracker.CarriedThing;
                    CompAbilityStone cs = carried?.TryGetComp<CompAbilityStone>();
                    if (st == null || cs == null || st.stone != null) return;
                    st.stone = cs.stone.Clone();
                    st.stone.dormant = false;
                    pawn.carryTracker.DestroyCarriedThing();
                    ArtifactBonusCache.ClearAll();
                    Messages.Message(pawn.LabelShort + " set the " + st.stone.Label + " stone into " + Owner.LabelNoCount + ".",
                        Owner, MessageTypeDefOf.TaskCompletion, false);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    public class JobDriver_ExtractAbilityStone : JobDriver
    {
        private Thing Owner => job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Owner == null || Owner.Destroyed || Dialog_ArtifactSpiral.StateOf(Owner)?.stone == null);
            bool worn = ArtifactCarrierUtility.HolderOf(Owner) == pawn;
            if (!worn) yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil wait = Toils_General.Wait(ArtifactoricsSettingsDef.Main?.stoneSetTicks ?? 600);
            wait.WithProgressBarToilDelay(TargetIndex.None);
            yield return wait;
            yield return new Toil
            {
                initAction = () =>
                {
                    ArtifactState st = Dialog_ArtifactSpiral.StateOf(Owner);
                    if (st?.stone == null) return;
                    Thing stone = ThingMaker.MakeThing(ArtifactAbilityDefOf.Avir_AbilityStone);
                    stone.TryGetComp<CompAbilityStone>().stone = st.stone.Clone();
                    st.stone = null;
                    GenPlace.TryPlaceThing(stone, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    ArtifactBonusCache.ClearAll();
                    ArtifactAbilities.Sync(pawn);
                    Messages.Message(pawn.LabelShort + " took the stone out of " + Owner.LabelNoCount + ".", stone,
                        MessageTypeDefOf.TaskCompletion, false);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    // ------------------------------------------------------------------ crafting

    /// <summary>The stone bill remembers which ability the player chose.</summary>
    public class Bill_AbilityStone : Bill_Production
    {
        public ArtifactAbilityDef ability;
        public SpearEnergyDef energy;

        public Bill_AbilityStone() { }

        public Bill_AbilityStone(RecipeDef recipe, ArtifactAbilityDef ability, SpearEnergyDef energy = null) : base(recipe, null)
        {
            this.ability = ability;
            this.energy = energy;
        }

        public override string LabelCap => base.LabelCap + (ability != null ? " (" + ability.label + (energy != null ? ", " + energy.label : "") + ")" : "");

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref ability, "ability");
            Scribe_Defs.Look(ref energy, "energy");
        }
    }

    public class RecipeWorker_AbilityStone : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            ArtifactRecipeExtension ext = recipe.GetModExtension<ArtifactRecipeExtension>();
            if (ext?.metal == null || billDoer?.Map == null) return;
            ArtifactAbilityDef ability = (billDoer.CurJob?.bill as Bill_AbilityStone)?.ability
                                         ?? DefDatabase<ArtifactAbilityDef>.AllDefsListForReading.FirstOrDefault();
            Thing stone = ThingMaker.MakeThing(ArtifactAbilityDefOf.Avir_AbilityStone);
            AbilityStoneState s = stone.TryGetComp<CompAbilityStone>().stone;
            s.ability = ability;
            s.metal = ext.metal;
            if (ability != null && ability.usesEnergy)
            {
                SpearEnergyDef e = (billDoer.CurJob?.bill as Bill_AbilityStone)?.energy;
                s.energy = e != null && e.metal == ext.metal ? e : SpearEnergyDef.For(ext.metal).FirstOrDefault();
            }
            s.quality = ArtifactCraftUtility.RollQuality(billDoer, ArtifactCraftUtility.HeartLevel(billDoer));
            s.maker = billDoer.LabelShort;
            ArtifactCraftUtility.Place(stone, billDoer);
            Messages.Message(billDoer.LabelShort + " made an ability stone: " + s.Label + " (" + s.quality.GetLabel() + ").",
                stone, MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
