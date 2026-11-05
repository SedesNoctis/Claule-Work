using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    [DefOf]
    public static class AvirEssenceDefOf
    {
        // Avir_EssenceSiphon is now part of Avir_ArcaneEssence, Avir_EssenceAttuned part of
        // Avir_EssenceGlutton (AvirGeneMerge.cs). Their fields are gone with their defs.
        public static GeneDef Avir_ArcaneEssence;
        public static GeneDef Avir_EssenceReaver;
        public static GeneDef Avir_EssenceGlutton;

        static AvirEssenceDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirEssenceDefOf));
        }
    }

    /// <summary>
    /// Arcane essence: the pool of stolen light. A plain Gene with a saved value - deliberately NOT
    /// a Gene_Resource, and deliberately with NO gizmo.
    ///
    /// A bar earns its place when the player has a decision to make about it. Essence never drains,
    /// is never spent, and cannot be topped up on demand - so its bar was just a number creeping
    /// upward. Worse, Gene_Resource.PostAdd initialises the value to the MAXIMUM, so every Avir was
    /// being born with a full 10 essence and the siphon was adding nothing to a pool that was
    /// already at its ceiling.
    ///
    /// The pool still exists and still accumulates. Its effect shows up where it belongs: in the
    /// Psychic sensitivity breakdown, as "Siphoned essence: +x.xx".
    /// </summary>
    public class Gene_ArcaneEssence : Gene
    {
        public const float BaselineReserve = 0.25f;

        /// No real ceiling any more: a hard stop at +2.0 left grown Avir stuck with no way forward. What keeps the pool
        /// in bounds is the diminishing return in AvirEssence.Gain - every draw is divided by how full the reserve
        /// already is, so it slows and slows but never stops. This number only guards against a broken save.
        public const float Ceiling = 1000f;

        private float essence = BaselineReserve;

        public float Value
        {
            get { return essence; }
            set { essence = Mathf.Clamp(value, 0f, Ceiling); }
        }

        public override void PostAdd()
        {
            base.PostAdd();

            float baselineBonus = AvirEssenceBaseline.RollFor(pawn);
            essence = Mathf.Min(BaselineReserve + baselineBonus, Ceiling);

            if (baselineBonus > 0f)
            {
                AvirEssenceLifetime.Track(pawn, baselineBonus, "faction baseline");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref essence, "avirArcaneEssence", BaselineReserve);
        }
    }

    public static class AvirEssence
    {
        /// Where the diminishing return begins to bite. Modelled on Big and Small's
        /// soulFalloffStart (0.5-1.0): their soul power stays in the 0.5-3 band across an entire
        /// campaign, and ours must too.
        private const float FalloffStart = 1.5f;
        private const float FalloffStartGlutton = 3.0f;

        /// <summary>
        /// How much of the reserve is expressed as psychic sensitivity. 1:1 by default - the pool
        /// simply IS the stolen sensitivity. An essence glutton (which absorbed the old
        /// essence-attuned gene) squeezes half again as much out of it.
        /// </summary>
        public static float AttunementFactor(Pawn pawn)
        {
            return HasGene(pawn, AvirEssenceDefOf.Avir_EssenceGlutton) ? 1.5f : 1.0f;
        }

        public static Gene_ArcaneEssence GeneOf(Pawn pawn)
        {
            if (pawn == null || pawn.genes == null) return null;
            return pawn.genes.GetFirstGeneOfType<Gene_ArcaneEssence>();
        }

        public static float Get(Pawn pawn)
        {
            Gene_ArcaneEssence gene = GeneOf(pawn);
            return gene == null ? 0f : gene.Value;
        }

        public static bool HasGene(Pawn pawn, GeneDef def)
        {
            return pawn != null && pawn.genes != null && def != null && pawn.genes.HasActiveGene(def);
        }

        /// <summary>
        /// Adds essence with an asymptotic diminishing return - the draw is divided by how full the
        /// reserve already is, so growth slows continuously and never quite stops:
        ///   at 0.0 a kill gives the full amount; at 1.0 half; at 3.0 a quarter; at 7.0 an eighth.
        /// </summary>
        public static float Gain(Pawn pawn, float rawAmount)
        {
            Gene_ArcaneEssence gene = GeneOf(pawn);
            if (gene == null || rawAmount <= 0f) return 0f;

            float current = gene.Value;
            float falloff = HasGene(pawn, AvirEssenceDefOf.Avir_EssenceGlutton)
                ? FalloffStartGlutton
                : FalloffStart;

            // the fuller the reserve, the harder it is to add to - by the square, so it never quite stops but slows down hard
            float k = current / falloff;
            float actual = rawAmount / (1f + k * k);

            gene.Value = Mathf.Min(current + actual, Gene_ArcaneEssence.Ceiling);
            return actual;
        }

        /// <summary>
        /// What a victim is worth: their psychic sensitivity, plus whatever essence they had
        /// themselves. This is the number the siphon takes a cut of - kill something radiant and
        /// you take radiance. Animals count, scaled by body size.
        /// </summary>
        public static float VictimValue(Pawn victim)
        {
            if (victim == null) return 0f;

            // AN ILLITHID IS WORTH NOTHING. Zero here closes every essence-gain path at once -
            // the lethal siphon and the essence reaver's melee kill both take a cut of this
            // number, so neither pays out and neither needs its own guard.
            if (AvirIllithidGuard.IsIllithid(victim)) return 0f;

            float psy = Mathf.Clamp(victim.GetStatValue(StatDefOf.PsychicSensitivity), 0.1f, 6f);
            float own = Get(victim);   // an Avir's hoard passes to whoever kills it

            float mass = 1f;
            if (victim.RaceProps != null && !victim.RaceProps.Humanlike)
            {
                mass = Mathf.Clamp(victim.BodySize, 0.25f, 1.5f);
            }

            return (psy + own) * mass;
        }
    }

    // ================= THE SIPHON =================

    public class CompProperties_AvirEssenceSiphon : CompProperties_AbilityEffect
    {
        public float essenceGain = 0.10f;
        public bool doKill = true;

        public CompProperties_AvirEssenceSiphon()
        {
            compClass = typeof(CompAbilityEffect_AvirEssenceSiphon);
        }
    }

    public class CompAbilityEffect_AvirEssenceSiphon : CompAbilityEffect
    {
        public new CompProperties_AvirEssenceSiphon Props
        {
            get { return (CompProperties_AvirEssenceSiphon)props; }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn victim = target.Pawn;
            if (victim == null || victim == parent.pawn) return false;

            bool songAsleep = Avir.Linarian.LinarianHediffs.Avir_SongSleep != null && victim.health?.hediffSet?.HasHediff(Avir.Linarian.LinarianHediffs.Avir_SongSleep) == true;
            bool hersStanding = Avir.Linarian.LinarianPackSiphon.Hers(parent.pawn, victim);   // a beast of her pack, standing
            if (!victim.Downed && !victim.Dead && !songAsleep && !hersStanding)
            {
                if (throwMessages)
                {
                    Messages.Message("The prey must be down first.", victim, MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent.pawn;
            Pawn victim = target.Pawn;
            if (caster == null || victim == null) return;

            // a Herald takes all of a victim, not a fifth
            float raw = (Linarian.Linarian.Is(caster) ? 1f : Props.essenceGain) * AvirEssence.VictimValue(victim);
            if (AvirEssence.HasGene(caster, AvirEssenceDefOf.Avir_EssenceGlutton)) raw *= 2f;

            AvirEssenceLifetime.Track(caster, raw, "siphon");
            float gained = AvirEssence.Gain(caster, raw);
            Gene_ArcaneHunger.Feed(caster, Gene_ArcaneHunger.FillPerSiphon);

            Messages.Message(
                caster.LabelShortCap + " siphoned " + gained.ToString("0.00") + " essence from " + victim.LabelShortCap + ".",
                caster, MessageTypeDefOf.PositiveEvent, false);

            // Capture where to drop the bottle BEFORE the kill - a killed pawn's PositionHeld can
            // stop being useful the moment the corpse is created.
            Map dropMap = victim.MapHeld;
            IntVec3 dropCell = victim.PositionHeld;

            // a Singer keeps the voice of whoever she drinks dry
            if (Props.doKill && Linarian.Linarian.Song(caster) is Linarian.Gene_LinarianSong song) song.TakeVoice(victim);
            if (Props.doKill && !victim.Dead) victim.Kill(null, null);

            // The siphon itself bottles nothing (it used to drop a free extract - a bug). Only an
            // essence gatherer worn by the caster draws extract from the kill.
            if (Props.doKill) AvirEssenceGatherer.OnKill(caster, victim, dropMap, dropCell);
        }
    }

    /// <summary>Essence reaver: a fatal melee blow draws a little essence on its own.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_AvirEssenceReaver
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                if (__instance == null || __instance.Dead || !dinfo.HasValue) return;

                Pawn killer = dinfo.Value.Instigator as Pawn;
                if (killer == null || killer == __instance) return;
                if (!AvirEssence.HasGene(killer, AvirEssenceDefOf.Avir_EssenceReaver)) return;
                // a glaive's own blows are melee (its spear-fire bolts carry no weapon)
                if (dinfo.Value.Weapon != null && dinfo.Value.Weapon.IsRangedWeapon && !Artifactorics.SpearMode.HasSpearVerb(dinfo.Value.Weapon)) return;

                float raw = 0.02f * AvirEssence.VictimValue(__instance) * (Linarian.Linarian.Is(killer) ? 2f : 1f);   // a Herald draws twice as much
                AvirEssenceLifetime.Track(killer, raw, "reaver");
                AvirEssence.Gain(killer, raw);
                Gene_ArcaneHunger.Feed(killer, Gene_ArcaneHunger.FillPerKillingBlow);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Essence reaver failed: " + e, 0x4A51B8);
            }
        }
    }

    // ================= ESSENCE -> PSYCHIC SENSITIVITY =================

    /// <summary>
    /// The essence pool IS stolen psychic sensitivity, and it is added to the caster one-for-one.
    /// This is the whole point of the siphon: kill something radiant, keep a piece of its radiance,
    /// permanently. Psychic Nature then turns that sensitivity into everything else an Avir is.
    ///
    /// The conversion is inherent to the arcane essence gene - no second gene required. The
    /// essence glutton gene is a multiplier on top (see AvirEssence.AttunementFactor).
    /// </summary>
    public class StatPart_AvirEssenceAttunement : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            Pawn pawn = req.Thing as Pawn;
            if (pawn == null) return;

            val += StolenSensitivity(pawn);
        }

        public override string ExplanationPart(StatRequest req)
        {
            Pawn pawn = req.Thing as Pawn;
            if (pawn == null) return null;

            float bonus = StolenSensitivity(pawn);
            if (Mathf.Abs(bonus) < 0.001f) return null;

            return "Siphoned essence: +" + bonus.ToStringPercent();
        }

        private static float StolenSensitivity(Pawn pawn)
        {
            if (AvirEssence.GeneOf(pawn) == null) return 0f;
            return AvirEssence.Get(pawn) * AvirEssence.AttunementFactor(pawn);
        }
    }

    [StaticConstructorOnStartup]
    public static class AvirEssenceStatInjector
    {
        static AvirEssenceStatInjector()
        {
            StatDef stat = StatDefOf.PsychicSensitivity;
            if (stat == null) return;

            if (stat.parts == null) stat.parts = new List<StatPart>();

            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is StatPart_AvirEssenceAttunement) return;
            }

            stat.parts.Add(new StatPart_AvirEssenceAttunement { parentStat = stat });
        }
    }
}
