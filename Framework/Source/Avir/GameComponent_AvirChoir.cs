using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Owns every currently-active Choir of every kind. What used to be the Silversong amulet's
    /// aura and nothing else is now one engine driving N AvirChoirDefs - see that file for why.
    ///
    /// WHAT IS STILL TRUE OF EVERY CHOIR, REGARDLESS OF DEF:
    ///   - Only Fearborn same-faction pawns ever join as singers. A non-Fearborn ally gets
    ///     nothing, because only Fearborn spend themselves to sustain it.
    ///   - Only NON-Fearborn hostiles are affected by the enemy half. A Fearborn hostile is
    ///     presumed to resist it by their own nature.
    ///   - Enemy hediff severity is kept live-synced to the current singer count, so a hediff
    ///     can scale its own stages - including how often it panics its carrier - by how many
    ///     voices are actually carrying the song.
    ///
    /// WHAT NOW COMES FROM THE DEF: radius, both hediffs, cast cost, hourly upkeep, the two
    /// Arcane Hunger thresholds, the collapse memory, and whether the choir is sustained or
    /// timed. Any of the reserve rules can be switched off per def with a negative value.
    ///
    /// ONE PAWN CAN NOW BE IN SEVERAL CHOIRS AT ONCE - standing inside the amulet aura while a
    /// work hymn is running is the expected case, not an edge case. That is why singer hediffs
    /// are per-def: two choirs sharing one marker hediff would each remove what the other just
    /// added, every 60 ticks, indefinitely.
    /// </summary>
    public class GameComponent_AvirChoir : GameComponent
    {
        private const int ScanIntervalTicks = 60;
        private const int HourTicks = 2500;

        public List<AvirChoirState> activeChoirs = new List<AvirChoirState>();

        /// <summary>
        /// Populated ONLY from choirs whose def sets feedsStatParts. The four consumers of
        /// TryGetChoirFor - the dodge and sensitivity StatParts, PsychicNatureCache and the
        /// hediff tooltip - all mean the amulet choir specifically, and all predate there being
        /// more than one kind. Keeping this dictionary single-valued and restricted keeps their
        /// call sites untouched; the alternative was a list per pawn plus four rewrites for no
        /// behavioural gain.
        /// </summary>
        private static readonly Dictionary<Pawn, AvirChoirState> MemberLookup = new Dictionary<Pawn, AvirChoirState>();

        public GameComponent_AvirChoir(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activeChoirs, "avirActiveChoirs", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (activeChoirs == null)
                {
                    activeChoirs = new List<AvirChoirState>();
                }

                RebuildLookup();
            }
        }

        private void RebuildLookup()
        {
            MemberLookup.Clear();
            for (int i = 0; i < activeChoirs.Count; i++)
            {
                AvirChoirState state = activeChoirs[i];
                if (state?.members == null || state.def == null || !state.def.feedsStatParts) continue;
                for (int m = 0; m < state.members.Count; m++)
                {
                    if (state.members[m] != null) MemberLookup[state.members[m]] = state;
                }
            }
        }

        /// <summary>Looked up by the two Choir StatParts - cheap dictionary hit, no scanning.</summary>
        public static bool TryGetChoirFor(Pawn pawn, out AvirChoirState state)
        {
            return MemberLookup.TryGetValue(pawn, out state);
        }

        /// <summary>True if this pawn currently leads an active Choir of this specific kind. A
        /// single pawn leading both a hymn and an amulet choir is legal - they are separate
        /// abilities with separate gizmos and separate cooldowns.</summary>
        public bool IsLeadingChoir(Pawn caster, AvirChoirDef def)
        {
            for (int i = 0; i < activeChoirs.Count; i++)
            {
                if (activeChoirs[i].caster == caster && activeChoirs[i].def == def)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Called by CompAbilityEffect_Choir.Apply. Ends any choir of the SAME kind this
        /// caster is already running first, so a re-cast restarts it cleanly rather than
        /// stacking two.</summary>
        public void StartChoir(Pawn caster, AvirChoirDef def)
        {
            if (def == null) return;

            EndChoirLedBy(caster, def, applyPenalty: false);

            int now = Find.TickManager.TicksGame;
            AvirChoirState state = new AvirChoirState
            {
                def = def,
                caster = caster,
                nextDrainAbsTick = def.upkeepPerHour > 0f ? now + HourTicks : int.MaxValue,
                endAbsTick = def.durationTicks > 0 ? now + def.durationTicks : 0,
            };
            activeChoirs.Add(state);
            RescanAura(state);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (activeChoirs.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;

            for (int i = activeChoirs.Count - 1; i >= 0; i--)
            {
                AvirChoirState state = activeChoirs[i];

                if (state?.def == null || state.caster == null || state.caster.Dead || !state.caster.Spawned)
                {
                    CollapseChoir(state, applyPenalty: false);
                    activeChoirs.RemoveAt(i);
                    continue;
                }

                // Timed choirs end quietly - reaching the end of a hymn is not a collapse and
                // carries no penalty memory even on a def that declares one.
                if (state.endAbsTick > 0 && now >= state.endAbsTick)
                {
                    CollapseChoir(state, applyPenalty: false);
                    activeChoirs.RemoveAt(i);
                    continue;
                }

                // Caster's own collapse threshold - checked every tick, takes priority over
                // everything else this cycle. Negative means this choir has no such rule.
                if (state.def.casterCollapse >= 0f)
                {
                    Gene_ArcaneHunger casterGene = Gene_ArcaneHunger.Of(state.caster);
                    if (casterGene != null && casterGene.Active && casterGene.Value <= state.def.casterCollapse)
                    {
                        CollapseChoir(state, applyPenalty: true);
                        activeChoirs.RemoveAt(i);
                        continue;
                    }
                }

                if (now % ScanIntervalTicks == 0)
                {
                    CheckSingerOptOuts(state);
                    RescanAura(state);
                }

                if (now >= state.nextDrainAbsTick)
                {
                    state.nextDrainAbsTick += HourTicks;
                    DrainUpkeep(state);
                }
            }
        }

        /// <summary>Non-caster singers whose OWN reserve has dropped below the def's threshold
        /// quietly leave - no penalty, and they're barred from rejoining this same Choir even if
        /// still in range.</summary>
        private static void CheckSingerOptOuts(AvirChoirState state)
        {
            if (state.def.singerOptOut < 0f) return;

            for (int i = state.members.Count - 1; i >= 0; i--)
            {
                Pawn member = state.members[i];
                if (member == null || member == state.caster)
                {
                    continue;
                }

                Gene_ArcaneHunger gene = Gene_ArcaneHunger.Of(member);
                if (gene != null && gene.Active && gene.Value <= state.def.singerOptOut)
                {
                    RemoveSinger(state, member);
                    state.members.RemoveAt(i);
                    if (!state.optedOut.Contains(member))
                    {
                        state.optedOut.Add(member);
                    }
                }
            }
        }

        private void RescanAura(AvirChoirState state)
        {
            Pawn caster = state.caster;
            if (caster?.Map == null)
            {
                return;
            }

            float radiusSq = state.def.radius * state.def.radius;
            List<Pawn> singersInRange = new List<Pawn>();
            List<Pawn> enemiesInRange = new List<Pawn>();

            IReadOnlyList<Pawn> mapPawns = caster.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < mapPawns.Count; i++)
            {
                Pawn p = mapPawns[i];
                if (p == null || p.Dead)
                {
                    continue;
                }

                if ((p.Position - caster.Position).LengthHorizontalSquared > radiusSq)
                {
                    continue;
                }

                Gene_ArcaneHunger gene = Gene_ArcaneHunger.Of(p);
                bool isFearborn = gene != null && gene.Active;

                if (p.Faction == caster.Faction)
                {
                    // Only Fearborn allies ever sing - a non-Fearborn ally gets nothing, and
                    // anyone who's already opted out of THIS Choir stays out until it ends.
                    if (isFearborn && !state.optedOut.Contains(p))
                    {
                        singersInRange.Add(p);
                    }
                }
                else if (state.def.enemyHediff != null
                    && caster.Faction != null && p.Faction != null && caster.Faction.HostileTo(p.Faction))
                {
                    // Only non-Fearborn enemies are affected - a Fearborn hostile is presumed to
                    // resist it via their own nature.
                    if (!isFearborn)
                    {
                        enemiesInRange.Add(p);
                    }
                }
            }

            // ---- Singers ----
            for (int i = state.members.Count - 1; i >= 0; i--)
            {
                Pawn m = state.members[i];
                if (m == null || !singersInRange.Contains(m))
                {
                    RemoveSinger(state, m);
                    state.members.RemoveAt(i);
                }
            }

            for (int i = 0; i < singersInRange.Count; i++)
            {
                Pawn p = singersInRange[i];
                if (!state.members.Contains(p))
                {
                    AddSinger(state, p);
                    state.members.Add(p);
                }
            }

            state.fearbornCount = state.members.Count;

            // ---- Enemies ----
            for (int i = state.enemyTargets.Count - 1; i >= 0; i--)
            {
                Pawn e = state.enemyTargets[i];
                if (e == null || !enemiesInRange.Contains(e))
                {
                    RemoveSongFrom(state, e);
                    state.enemyTargets.RemoveAt(i);
                }
            }

            for (int i = 0; i < enemiesInRange.Count; i++)
            {
                Pawn e = enemiesInRange[i];
                if (!state.enemyTargets.Contains(e))
                {
                    state.enemyTargets.Add(e);
                }

                ApplyOrUpdateSong(state, e);
            }
        }

        private static void AddSinger(AvirChoirState state, Pawn p)
        {
            if (state.def.feedsStatParts)
            {
                MemberLookup[p] = state;
            }

            if (state.def.singerHediff == null) return;

            if (p.health?.hediffSet != null && !p.health.hediffSet.HasHediff(state.def.singerHediff))
            {
                Hediff hediff = HediffMaker.MakeHediff(state.def.singerHediff, p);
                p.health.AddHediff(hediff);
            }
        }

        private static void RemoveSinger(AvirChoirState state, Pawn p)
        {
            if (p == null) return;

            // Only clear the lookup entry if it actually belongs to THIS choir. A pawn leaving a
            // hymn must not evict their amulet-choir entry and silently drop someone's dodge
            // bonus mid-firefight.
            if (state.def.feedsStatParts
                && MemberLookup.TryGetValue(p, out AvirChoirState owner) && owner == state)
            {
                MemberLookup.Remove(p);
            }

            if (state.def.singerHediff == null) return;

            Hediff existing = p.health?.hediffSet?.GetFirstHediffOfDef(state.def.singerHediff);
            if (existing != null)
            {
                p.health.RemoveHediff(existing);
            }
        }

        private static void ApplyOrUpdateSong(AvirChoirState state, Pawn enemy)
        {
            if (enemy?.health?.hediffSet == null || state.def.enemyHediff == null)
            {
                return;
            }

            float severity = state.def.scaleEnemySeverityByVoices
                ? Mathf.Min(state.fearbornCount, state.def.maxEnemySeverity)
                : 1f;

            Hediff existing = enemy.health.hediffSet.GetFirstHediffOfDef(state.def.enemyHediff);
            if (existing != null)
            {
                existing.Severity = severity;
                return;
            }

            Hediff hediff = HediffMaker.MakeHediff(state.def.enemyHediff, enemy);
            hediff.Severity = severity;
            enemy.health.AddHediff(hediff);
        }

        private static void RemoveSongFrom(AvirChoirState state, Pawn enemy)
        {
            if (enemy == null || state.def.enemyHediff == null) return;

            Hediff existing = enemy.health?.hediffSet?.GetFirstHediffOfDef(state.def.enemyHediff);
            if (existing != null)
            {
                enemy.health.RemoveHediff(existing);
            }
        }

        private static void DrainUpkeep(AvirChoirState state)
        {
            if (state.def.upkeepPerHour <= 0f) return;

            for (int i = 0; i < state.members.Count; i++)
            {
                Pawn member = state.members[i];
                if (member == null) continue;

                // Best-effort: a member without enough banked simply isn't charged this hour.
                // Only the CASTER'S OWN reserve determines a disruptive collapse; a singer
                // running low is instead caught by CheckSingerOptOuts before this ever matters.
                Gene_ArcaneHunger.Spend(member, state.def.upkeepPerHour);
            }
        }

        private void CollapseChoir(AvirChoirState state, bool applyPenalty)
        {
            if (state?.def == null)
            {
                return;
            }

            if (state.members != null)
            {
                for (int i = 0; i < state.members.Count; i++)
                {
                    Pawn member = state.members[i];
                    RemoveSinger(state, member);

                    if (applyPenalty && state.def.collapseThought != null
                        && member?.needs?.mood?.thoughts?.memories != null)
                    {
                        member.needs.mood.thoughts.memories.TryGainMemory(state.def.collapseThought);
                    }
                }

                state.members.Clear();
            }

            if (state.enemyTargets != null)
            {
                for (int i = 0; i < state.enemyTargets.Count; i++)
                {
                    RemoveSongFrom(state, state.enemyTargets[i]);
                }

                state.enemyTargets.Clear();
            }
        }

        /// <summary>Ends only the caster's choir of this kind. Passing null for def ends every
        /// choir they lead, which is what death and despawn want.</summary>
        public void EndChoirLedBy(Pawn caster, AvirChoirDef def, bool applyPenalty)
        {
            for (int i = activeChoirs.Count - 1; i >= 0; i--)
            {
                if (activeChoirs[i].caster == caster && (def == null || activeChoirs[i].def == def))
                {
                    CollapseChoir(activeChoirs[i], applyPenalty);
                    activeChoirs.RemoveAt(i);
                }
            }
        }
    }
}
