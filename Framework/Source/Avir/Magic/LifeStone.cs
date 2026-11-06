using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The stone curse of Emerald (the author, Fix200): a dear spell that works like a sickness - the body fights it
    /// (immunity), and if it loses, the one cursed does not die: they become a sculpture. And whoever dies while cursed,
    /// dies into stone. The sculpture is a work of art that can be moved (minified, as any statue); its clothes can still be
    /// taken off. The body is kept inside the stone, not destroyed.</summary>
    public static class StoneCurse
    {
        public const float Cost = 30f;
        public static HediffDef Def => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StoneCurse");
        public static ThingDef Statue => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LivingStatue");

        /// <summary>Lays the curse on the living reached, while the Emerald store pays for each.</summary>
        public static int Lay(Pawn caster, IEnumerable<Pawn> who)
        {
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null || Def == null) return 0;
            int n = 0;
            foreach (Pawn p in who.Where(p => !p.Dead && p.RaceProps.IsFlesh).ToList())
            {
                if (core.Store(RuneKind.Earth) < Cost) { MoteMaker.ThrowText(caster.DrawPos, caster.Map, "Not emerald enough for the curse", LifeMagic.PairColor(RuneKind.Earth), 3f); break; }
                core.SetStore(RuneKind.Earth, core.Store(RuneKind.Earth) - Cost);
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Def) ?? p.health.AddHediff(Def);
                h.Severity = Mathf.Max(h.Severity, 0.05f);
                if (p.Spawned) MapComponent_SpellSprites.PutTinted(p.Map, "Life/StoneCrust", LifeMagic.PairColor(RuneKind.Earth), p.DrawPos, 1.4f, 90);
                n++;
            }
            return n;
        }

        /// <summary>Into stone: the body taken into a sculpture of itself where it stood. False - it could not be done here
        /// (no statue def, not on a map): then it dies as anyone.</summary>
        public static bool Turn(Pawn p)
        {
            ThingDef def = Statue;
            if (def == null || p == null || !p.Spawned) return false;
            Map map = p.Map; IntVec3 at = p.Position;
            ThingDef stuff = DefDatabase<ThingDef>.GetNamedSilentFail("Jade") ?? GenStuff.DefaultStuffFor(def);
            var statue = ThingMaker.MakeThing(def, stuff) as Building_LivingStatue;
            if (statue == null) return false;
            p.jobs?.StopAll();
            // what is held in the hands and carried falls; the clothes stay on the stone (they can be taken off later)
            if (p.equipment != null) foreach (ThingWithComps eq in p.equipment.AllEquipmentListForReading.ToList()) p.equipment.TryDropEquipment(eq, out _, at);
            p.inventory?.DropAllNearPawn(at);
            Hediff c = p.health.hediffSet.GetFirstHediffOfDef(Def); if (c != null) p.health.RemoveHediff(c);
            p.DeSpawn();
            statue.Hold(p);
            // the art first (its title and tale), then the stone takes the shape of the one inside
            try { statue.TryGetComp<CompArt>()?.InitializeArt(ArtGenerationContext.Colony); } catch (System.Exception e) { Log.Warning("[Avir] living statue art: " + e.Message); }
            statue.Snapshot();
            statue.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Colony);
            GenSpawn.Spawn(statue, at, map);
            if (map.ParentFaction == Faction.OfPlayer || map.IsPlayerHome) statue.SetFaction(Faction.OfPlayer);
            for (int i = 0; i < 6; i++) FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * 0.5f, map, 1.2f, new Color(0.3f, 0.65f, 0.45f, 0.6f));
            Messages.Message(p.LabelShortCap + " has turned to stone - a statue now.", statue, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }
    }

    public class Building_LivingStatue : Building, IThingHolder
    {
        private ThingOwner<Pawn> inner;
        public Building_LivingStatue() { inner = new ThingOwner<Pawn>(this, true, LookMode.Deep); }
        public Pawn Who => inner.Count > 0 ? inner[0] : null;
        public void Hold(Pawn p) => inner.TryAdd(p);
        public ThingOwner GetDirectlyHeldThings() => inner;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        /// <summary>The stone takes the shape of the one inside (the game's own statue art).</summary>
        public void Snapshot()
        {
            Pawn p = Who;
            CompStatue c = this.TryGetComp<CompStatue>();
            if (p == null || c == null) return;
            try
            {
                // the game's own statue art of a person: its snapshot of the body, then the drawn figure made afresh
                AccessTools.Method(typeof(CompStatue), "CreateSnapshotOfPawn")?.Invoke(c, new object[] { p });
                AccessTools.Field(typeof(CompStatue), "fakePawn")?.SetValue(c, null);
                AccessTools.Method(typeof(CompStatue), "InitFakePawn")?.Invoke(c, null);
            }
            catch (System.Exception e) { Log.Warning("[Avir] living statue snapshot: " + e.Message); }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            Pawn p = Who;
            if (p?.apparel != null && p.apparel.WornApparel.Count > 0 && Faction == Faction.OfPlayer)
                yield return new Command_Action
                {
                    defaultLabel = "Take the clothes off the stone",
                    defaultDesc = "The clothes " + p.LabelShort + " wore are still on the stone. Take them off - they fall beside it, and the statue is bare.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DropCarriedPawn", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        foreach (Apparel a in p.apparel.WornApparel.ToList()) { p.apparel.Remove(a); GenPlace.TryPlaceThing(a, Position, Map, ThingPlaceMode.Near); }
                        Snapshot();
                    }
                };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string mine = Who != null ? "Once " + Who.LabelShort + " - turned to stone by Emerald." : "";
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref inner, "inner", this);
            if (inner == null) inner = new ThingOwner<Pawn>(this, true, LookMode.Deep);
        }
    }

    /// <summary>Whoever dies while the stone curse is on them dies into stone.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_StoneCurseKill
    {
        public static bool Prefix(Pawn __instance)
        {
            HediffDef d = StoneCurse.Def;
            if (d == null || __instance?.health?.hediffSet == null || __instance.health.hediffSet.GetFirstHediffOfDef(d) == null) return true;
            return !StoneCurse.Turn(__instance);
        }

        /// <summary>Decay dries to the bone: the dead it dried do not rot - there is nothing left to rot (Fix200).</summary>
        public static void Postfix(Pawn __instance)
        {
            if (__instance == null || !__instance.Dead || __instance.Corpse == null) return;
            HediffDef dd = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_DesiccatedDecay");
            if (dd == null || !__instance.health.hediffSet.hediffs.Any(h => h.def == dd)) return;
            __instance.Corpse.TryGetComp<CompRottable>()?.RotImmediately(RotStage.Dessicated);
        }
    }

    // ================================================================== desiccation (Fix200)

    /// <summary>Withering dries the flesh (the author): part by part, the body dries - a part dried through is useless but
    /// not lost. No medicine for it. Plain Life dries slowly, and it passes by itself in time, and Restoration turns it back.
    /// Decay dries radically, to the bone: it does not pass by itself, only magic healing of any kind turns it back; a
    /// torso dried through is death, and the dead do not rot - the flesh is gone, a skeleton is left.</summary>
    public static class Desiccation
    {
        public static HediffDef Life => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Desiccated");
        public static HediffDef Decay => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_DesiccatedDecay");

        public static void Dry(Pawn p, float amount, bool decay, Pawn by)
        {
            HediffDef d = decay ? Decay : Life;
            if (d == null || p == null || p.Dead || !p.RaceProps.IsFlesh || amount <= 0f) return;
            BodyPartRecord part = p.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside).Where(x => x.def.hitPoints > 0).RandomElementByWeightWithFallback(x => x.coverageAbs);
            if (part == null) return;
            Hediff h = p.health.hediffSet.hediffs.FirstOrDefault(x => x.def == d && x.Part == part);
            if (h == null) { h = HediffMaker.MakeHediff(d, p, part); h.Severity = 0.01f; p.health.AddHediff(h, part); }
            h.Severity = Mathf.Min(decay ? 1f : 0.9f, h.Severity + amount);
            // Decay: a torso dried through is death
            if (decay && part == p.RaceProps.body.corePart && h.Severity >= 0.999f && !p.Dead) p.Kill(new DamageInfo(LifeMagic.WitherDmg, 1f, 0f, -1f, by));
        }

        /// <summary>Magic healing of any kind turns the drying back (the only cure for Decay's).</summary>
        public static void Undo(Pawn p, float amount)
        {
            if (p?.health?.hediffSet == null) return;
            foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => x.def == Life || x.def == Decay).ToList())
            {
                h.Severity -= amount;
                if (h.Severity <= 0.01f) p.health.RemoveHediff(h);
            }
        }
    }
}
