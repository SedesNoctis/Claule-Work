using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Brings Phoenix-marked pawns back 180 ticks after they die.
    ///
    /// WHY THIS IS A GAMECOMPONENT AND NOT A HEDIFFCOMP. Hediffs survive death - they stay on
    /// Corpse.InnerPawn - but DEAD PAWNS DO NOT TICK. CompPostTick is never called on a corpse,
    /// so a countdown living inside the hediff would sit at 180 forever. The countdown has to be
    /// driven from outside, and a GameComponent is the same mechanism already used by
    /// GameComponent_AvirRelationRepair.
    ///
    /// THE CORPSE IS NOT LOad-BEARING. The entry holds a direct reference to the Pawn plus the
    /// map and cell it died on. If the body is vaporised by an explosion, burned to ash, or
    /// butchered in the three seconds before the timer runs out, the Pawn object is still
    /// reachable through that reference and the resurrection happens at the recorded cell
    /// anyway. That is the point: she rises from the ashes, and there do not need to be any
    /// ashes left for it to work.
    /// </summary>
    public class GameComponent_AvirPhoenixRebirth : GameComponent
    {
        public const int RebirthDelayTicks = 180;

        private class Pending : IExposable
        {
            public Pawn pawn;
            public Map map;
            public IntVec3 cell;
            public int ticksLeft;

            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn", true);
                Scribe_References.Look(ref map, "map");
                Scribe_Values.Look(ref cell, "cell");
                Scribe_Values.Look(ref ticksLeft, "ticksLeft", 0);
            }
        }

        private List<Pending> pending = new List<Pending>();

        public GameComponent_AvirPhoenixRebirth(Game game)
        {
        }

        /// <summary>Named Instance rather than Current so it cannot be confused with Verse.Current,
        /// which is what it reads through - the mod's other components use the plain
        /// Current.Game.GetComponent&lt;T&gt;() form at their call sites.</summary>
        public static GameComponent_AvirPhoenixRebirth Instance
        {
            get { return Verse.Current.Game != null ? Verse.Current.Game.GetComponent<GameComponent_AvirPhoenixRebirth>() : null; }
        }

        /// <summary>Called from the Pawn.Kill patch, with the position captured BEFORE the kill
        /// despawned the pawn into its corpse.</summary>
        public void Register(Pawn pawn, Map map, IntVec3 cell)
        {
            if (pawn == null) return;

            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].pawn == pawn) return;
            }

            pending.Add(new Pending { pawn = pawn, map = map, cell = cell, ticksLeft = RebirthDelayTicks });
        }

        public bool IsPending(Pawn pawn)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].pawn == pawn) return true;
            }
            return false;
        }

        public override void GameComponentTick()
        {
            if (pending.Count == 0) return;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending p = pending[i];
                if (p.pawn == null)
                {
                    pending.RemoveAt(i);
                    continue;
                }

                p.ticksLeft--;
                if (p.ticksLeft > 0) continue;

                pending.RemoveAt(i);
                try { RiseFromTheAshes(p.pawn, p.map, p.cell); }
                catch (Exception e) { Log.Error("[Avir] Phoenix rebirth failed: " + e); }
            }
        }

        // ------------------------------------------------------------
        //  RESURRECTION
        // ------------------------------------------------------------
        private static MethodInfo resurrectMethod;
        private static bool resurrectResolved;

        /// <summary>
        /// ResurrectionUtility.TryResurrect has changed signature across RimWorld versions - it
        /// has been Resurrect(Pawn), TryResurrect(Pawn) and TryResurrect(Pawn, ResurrectionParams).
        /// Resolved by reflection once so a signature change degrades into a logged warning
        /// rather than a mod that will not compile.
        /// </summary>
        private static MethodInfo ResurrectMethod
        {
            get
            {
                if (resurrectResolved) return resurrectMethod;
                resurrectResolved = true;

                Type t = typeof(ResurrectionUtility);
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "TryResurrect" && m.Name != "Resurrect") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 0 || ps[0].ParameterType != typeof(Pawn)) continue;
                    if (ps.Length == 1 || ps[1].IsOptional)
                    {
                        resurrectMethod = m;
                        break;
                    }
                }

                if (resurrectMethod == null)
                    Log.Warning("[Avir] Could not resolve ResurrectionUtility.TryResurrect - Phoenix rebirth will not work.");

                return resurrectMethod;
            }
        }

        private static void RiseFromTheAshes(Pawn pawn, Map map, IntVec3 cell)
        {
            Corpse corpse = pawn.Corpse;
            if (corpse != null && corpse.Spawned)
            {
                map = corpse.Map;
                cell = corpse.Position;
            }

            MethodInfo m = ResurrectMethod;
            if (m == null) return;

            // WHAT SHE WAS ASSIGNED TO DO, WRITTEN DOWN BEFORE SHE COMES BACK.
            //
            // Maximus: "after Phoenix essence the pawn's work priorities reset. COMPLETELY."
            // He is right, and on a colony with hand-tuned priorities across twenty work types
            // that is a genuinely miserable thing to have to redo by hand every rebirth - all the
            // more so because the rebirth is supposed to be a triumph, not a chore.
            //
            // Resurrection reinitialises the work settings, which for a pawn that has just been
            // reassembled from nothing is defensible: the game cannot know the new body can still
            // do what the old one did. But an Avir who came back through the fire IS the same
            // person with the same trade, and this is exactly the case where the game's caution is
            // wrong and the mod knows better.
            //
            // Captured rather than repaired afterwards, because afterwards the old numbers are
            // already gone. Whatever clears them - vanilla, or any other mod's resurrection
            // patch - this survives it, since it does not depend on knowing who did the clearing.
            Dictionary<WorkTypeDef, int> priorities = CaptureWorkPriorities(pawn);

            object[] args = m.GetParameters().Length == 1
                ? new object[] { pawn }
                : new object[] { pawn, Type.Missing };
            m.Invoke(null, args);

            RestoreWorkPriorities(pawn, priorities);

            // Vanilla resurrection can leave the pawn unspawned if the corpse was gone. Put it
            // back on the map by hand at the cell it died on.
            if (!pawn.Spawned && map != null)
            {
                IntVec3 target = cell.IsValid && cell.InBounds(map) ? cell : CellFinder.RandomCell(map);
                GenSpawn.Spawn(pawn, target, map);
            }

            MakeWhole(pawn);
        }

        private static Dictionary<WorkTypeDef, int> CaptureWorkPriorities(Pawn pawn)
        {
            Dictionary<WorkTypeDef, int> result = new Dictionary<WorkTypeDef, int>();
            if (pawn?.workSettings == null) return result;

            List<WorkTypeDef> all = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                try
                {
                    int priority = pawn.workSettings.GetPriority(all[i]);
                    if (priority > 0) result[all[i]] = priority;
                }
                catch
                {
                    // A work type this pawn cannot answer for is simply not recorded. Reading
                    // priorities must never be the thing that stops a resurrection.
                }
            }

            return result;
        }

        /// <summary>
        /// Puts the assignments back, one at a time and forgivingly.
        ///
        /// EACH ONE IS GUARDED SEPARATELY ON PURPOSE. A rebirth can legitimately change what a
        /// pawn is capable of, and setting a priority for work she can no longer do will throw.
        /// One refusal must not cost her the other nineteen assignments, so the loop keeps going
        /// and she simply comes back unassigned from that single job.
        /// </summary>
        private static void RestoreWorkPriorities(Pawn pawn, Dictionary<WorkTypeDef, int> priorities)
        {
            if (pawn?.workSettings == null || priorities == null || priorities.Count == 0) return;

            foreach (KeyValuePair<WorkTypeDef, int> entry in priorities)
            {
                try
                {
                    pawn.workSettings.SetPriority(entry.Key, entry.Value);
                }
                catch
                {
                    // She cannot do this one any more. Leave it unassigned and carry on.
                }
            }
        }

        /// <summary>
        /// "Полностью здоровая." Every injury gone, every missing part back, resurrection
        /// sickness removed outright, the stage hediff cleared, and the one-hour rebirth hediff
        /// left in place so the pawn cannot be set alight on the way out.
        /// </summary>
        private static void MakeWhole(Pawn pawn)
        {
            if (pawn.health == null) return;

            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Hediff h = all[i];
                if (h.def == null) continue;
                if (h.def == AvirPhoenixDefOf.Avir_PhoenixRebirth) continue;

                // ---- THE ESSENCE ITSELF, FIRST, BEFORE ANY OTHER TEST. ----
                //
                // THIS LINE BEING MISSING MADE PAWNS IMMORTAL. Two separate guards below were
                // both keeping it: it is declared isBad=false, so the "if it is bad, it is gone"
                // rule never looked at it, and its defName starts with "Avir_", so the
                // our-own-parts guard actively skipped it. The pawn came back still carrying the
                // essence, died again, came back again, for the full seventy-two hours.
                //
                // Dying under the Phoenix spends it. That is the whole rule: one death, one
                // rebirth, effect gone. It has to be removed here and it has to be removed
                // before anything else gets a chance to protect it.
                if (h.def == AvirPhoenixDefOf.Avir_PhoenixEssenceHediff)
                {
                    pawn.health.RemoveHediff(h);
                    continue;
                }

                // ---- KEEP LIST. Everything else that is bad goes. ----
                //
                // THE OLD VERSION LISTED WHAT TO REMOVE AND MISSED THINGS. It named injuries,
                // blood loss, resurrection sickness, addictions - and left chemical damage
                // sitting on a kidney at -80% efficiency after the pawn had supposedly come back
                // whole. Any hediff nobody thought to name survived the rebirth.
                //
                // Inverted: the rule is now "if it is bad, it is gone", with a short list of
                // things that are not damage and must not be swept up with it.
                if (h is Hediff_AddedPart) continue;      // implants that survived the fire
                if (h is Hediff_Implant) continue;        // psylink and the like
                if (h is Hediff_Pregnant) continue;       // not an injury
                if (h.def.defName != null && h.def.defName.StartsWith("Avir_")) continue;
                if (h.def.countsAsAddedPartOrImplant) continue;

                bool damage = h is Hediff_Injury
                    || h.def == HediffDefOf.BloodLoss
                    || h.def == HediffDefOf.ResurrectionSickness
                    || h is Hediff_Addiction
                    || h.def.IsAddiction
                    || h.def.isBad;   // chemical damage, disease, frostbite, everything else

                if (damage) pawn.health.RemoveHediff(h);
            }

            // Missing parts last, so removing injuries first cannot re-add them.
            for (int guard = 0; guard < 40; guard++)
            {
                List<Hediff_MissingPart> missing = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
                if (missing == null || missing.Count == 0) break;
                pawn.health.RestorePart(missing[0].Part);
            }

            if (!AvirPhoenixUtil.HasRebirth(pawn))
            {
                pawn.health.AddHediff(AvirPhoenixDefOf.Avir_PhoenixRebirth);
            }

            BurnTheClothes(pawn);
            RestorePsychicBonds(pawn);
            SwapFriendThoughts(pawn);

            if (pawn.needs != null)
            {
                if (pawn.needs.rest != null) pawn.needs.rest.CurLevel = pawn.needs.rest.MaxLevel;
                if (pawn.needs.food != null) pawn.needs.food.CurLevelPercentage = 0.90f;
            }

            Gene_ArcaneHunger hunger = Gene_ArcaneHunger.Of(pawn);
            if (hunger != null) hunger.Value = 1f;

            if (pawn.needs != null && pawn.needs.mood != null && pawn.needs.mood.thoughts != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(AvirPhoenixDefOf.Avir_PhoenixRose);
            }
        }

        private static ThingDef cloudcloth;
        private static bool cloudclothResolved;

        /// <summary>
        /// WHAT ACTUALLY BURNS OFF A RISEN PAWN.
        ///
        /// THE FIRST VERSION TOOK EVERYTHING. It destroyed any apparel with a Flammability above
        /// zero, and in RimWorld that is essentially all of it - plate, power armour, marine
        /// armour, the lot. A pawn came back from the pyre stripped to the skin regardless of
        /// what she had been wearing, which is not the design and is a genuinely expensive
        /// surprise.
        ///
        /// The rule is about MATERIAL, not about a flammability number:
        ///
        ///   CLOUDCLOTH        untouched, always. The Avir textile does not take this fire.
        ///                     Matched on the STUFF, so the exemption follows the material into
        ///                     any garment anyone ever cuts from it.
        ///   FABRIC, LEATHERY  destroyed. Cloth and hide are what a fire consumes.
        ///   EVERYTHING ELSE   survives, SCORCHED. Steel, plasteel, uranium, and anything not
        ///                     made from stuff at all - power armour, most spacer kit. It comes
        ///                     out the other side battered rather than gone: hit points are cut
        ///                     to between a third and two thirds, floored at one so nothing is
        ///                     destroyed by the damage itself.
        /// </summary>
        private static void BurnTheClothes(Pawn pawn)
        {
            if (pawn.apparel == null) return;

            if (!cloudclothResolved)
            {
                cloudclothResolved = true;
                cloudcloth = DefDatabase<ThingDef>.GetNamedSilentFail("Cloudcloth");
            }

            List<Apparel> worn = new List<Apparel>(pawn.apparel.WornApparel);
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel a = worn[i];
                if (a == null) continue;

                if (cloudcloth != null && a.Stuff == cloudcloth) continue;

                if (!IsSoftMaterial(a))
                {
                    Scorch(a);
                    continue;
                }

                pawn.apparel.Remove(a);
                a.Destroy(DestroyMode.Vanish);
            }
        }

        /// <summary>Fabric or leather - the things fire actually eats. Anything with no stuff at
        /// all (power armour and friends) counts as hard by definition.</summary>
        private static bool IsSoftMaterial(Apparel a)
        {
            if (a.Stuff == null || a.Stuff.stuffProps == null) return false;

            List<StuffCategoryDef> cats = a.Stuff.stuffProps.categories;
            if (cats == null) return false;

            for (int i = 0; i < cats.Count; i++)
            {
                if (cats[i] == StuffCategoryDefOf.Fabric) return true;
                if (cats[i] == StuffCategoryDefOf.Leathery) return true;
            }
            return false;
        }

        /// <summary>Came through the fire, and looks it.</summary>
        private static void Scorch(Apparel a)
        {
            if (a.def == null || !a.def.useHitPoints || a.MaxHitPoints <= 1) return;

            int target = Mathf.RoundToInt(a.HitPoints * Rand.Range(0.33f, 0.66f));
            a.HitPoints = Mathf.Clamp(target, 1, a.MaxHitPoints);
        }

        // ------------------------------------------------------------
        //  PSYCHIC BONDS - REPAIRED, NOT DEFENDED
        // ------------------------------------------------------------
        private static HediffDef bondDef;
        private static FieldInfo bondOtherField;
        private static bool bondResolved;

        private static bool ResolveBond()
        {
            if (bondResolved) return bondDef != null && bondOtherField != null;
            bondResolved = true;

            bondDef = DefDatabase<HediffDef>.GetNamedSilentFail("PsychicBond");
            if (bondDef == null || bondDef.hediffClass == null)
            {
                Log.Message("[Avir] No PsychicBond hediff - Phoenix bond repair inactive.");
                return false;
            }

            // WALKS THE WHOLE HIERARCHY. The first version used
            // AccessTools.GetDeclaredFields(hediffClass), which returns only fields DECLARED on
            // that exact type - so if the partner reference lives on a base class it finds
            // nothing, which is exactly what happened: "PsychicBond partner field not found".
            //
            // Searched by TYPE rather than by name so a rename cannot break it, and by name as
            // a tiebreak when a class has more than one Pawn field.
            List<FieldInfo> candidates = new List<FieldInfo>();
            Type t = bondDef.hediffClass;
            while (t != null && t != typeof(object))
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType == typeof(Pawn)) candidates.Add(f);
                }
                t = t.BaseType;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string n = candidates[i].Name.ToLowerInvariant();
                if (n.Contains("other") || n.Contains("partner") || n.Contains("bond"))
                {
                    bondOtherField = candidates[i];
                    break;
                }
            }
            if (bondOtherField == null && candidates.Count > 0) bondOtherField = candidates[0];

            if (bondOtherField == null)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[Avir] PsychicBond partner field not found on ")
                  .Append(bondDef.hediffClass.FullName)
                  .Append(" - Phoenix bond repair inactive. Fields seen:");
                t = bondDef.hediffClass;
                while (t != null && t != typeof(object))
                {
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        sb.Append("\n    ").Append(t.Name).Append('.').Append(f.Name)
                          .Append(" : ").Append(f.FieldType.Name);
                    }
                    t = t.BaseType;
                }
                Log.Warning(sb.ToString());
            }
            else
            {
                Log.Message("[Avir] Phoenix bond repair active - partner field is "
                    + bondOtherField.DeclaringType.Name + "." + bondOtherField.Name + ".");
            }

            return bondOtherField != null;
        }

        /// <summary>
        /// REPAIR, NOT PREVENTION. The earlier approach tried to stop the bond breaking by
        /// skipping Gene_PsychicBonding.Notify_PawnDied, and that only ever half-worked: the
        /// mental break still fired through a different path, and any route to the bond that
        /// did not go through that one method was simply missed.
        ///
        /// This looks at the problem from the other end, which is where it actually lives. The
        /// thing that matters is not the corpse - it is the pawn still standing who was bonded
        /// to it. So after the rebirth: find everyone who still holds a bond pointing at the
        /// risen pawn, and give the risen pawn the matching half back if it is gone.
        ///
        /// One surviving half is enough to rebuild the pair. It does not matter which half
        /// broke, or how, or whether some future update adds a fourth way to break it.
        /// </summary>
        private static void RestorePsychicBonds(Pawn risen)
        {
            if (!ResolveBond() || risen.health == null) return;

            List<Pawn> everyone = AvirPhoenixUtil.AllBondCandidates();
            for (int i = 0; i < everyone.Count; i++)
            {
                Pawn partner = everyone[i];
                if (partner == risen || partner.health == null || partner.health.hediffSet == null) continue;

                bool partnerBondsToRisen = false;
                List<Hediff> ph = partner.health.hediffSet.hediffs;
                for (int j = 0; j < ph.Count; j++)
                {
                    if (ph[j].def != bondDef) continue;
                    if (bondOtherField.GetValue(ph[j]) as Pawn == risen) { partnerBondsToRisen = true; break; }
                }
                if (!partnerBondsToRisen) continue;

                bool risenBondsBack = false;
                List<Hediff> rh = risen.health.hediffSet.hediffs;
                for (int j = 0; j < rh.Count; j++)
                {
                    if (rh[j].def != bondDef) continue;
                    if (bondOtherField.GetValue(rh[j]) as Pawn == partner) { risenBondsBack = true; break; }
                }
                if (risenBondsBack) continue;

                Hediff rebuilt = HediffMaker.MakeHediff(bondDef, risen);
                bondOtherField.SetValue(rebuilt, partner);
                risen.health.AddHediff(rebuilt);

                Log.Message("[Avir] Phoenix: psychic bond rebuilt between " + risen.LabelShort
                    + " and " + partner.LabelShort + ".");
            }
        }

        /// <summary>Replaces "burned" with "rose from the ashes" on everyone who took the first
        /// thought when this pawn died.</summary>
        private static void SwapFriendThoughts(Pawn risen)
        {
            List<Pawn> all = AvirPhoenixUtil.Onlookers();
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == risen || p.needs == null || p.needs.mood == null) continue;

                MemoryThoughtHandler mem = p.needs.mood.thoughts.memories;
                mem.RemoveMemoriesOfDefWhereOtherPawnIs(AvirPhoenixDefOf.Avir_PhoenixFriendBurned, risen);
                mem.TryGainMemory(AvirPhoenixDefOf.Avir_PhoenixFriendRose, risen);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "avirPhoenixPending", LookMode.Deep);
            if (pending == null) pending = new List<Pending>();
        }
    }
}
