using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian
{
    /// <summary>What each tactic does. The button (LinarianTactics) pays for it; the hidden Heralds call these
    /// directly.</summary>
    public static class LinarianActs
    {
        private const int H = GenDate.TicksPerHour;

        public static void Do(Pawn p, Gene_LinarianSong g, string t, LocalTargetInfo target)
        {
            Pawn q = target.Pawn;
            IntVec3 c = target.Cell.IsValid ? target.Cell : p.Position;
            float r = LinarianZone.Area(t, g);
            switch (t)
            {
                // Lullaby
                case "L1": Lullaby(p, g, c, r, 0); break;
                case "L2": QuietSong(p, g); break;
                case "L3": CalmBeasts(p, g, c, r); break;
                case "L4": LinarianSleep.HeavyEyelids(p, g, c, r, 1f); break;
                case "L5": LinarianSleep.Field(p, g, c, r); break;
                // Dreaming
                case "D1": if (p.Faction == Faction.OfPlayer) LinarianDream.IntoTheDream(p, q); else Mark(p, q); break;
                case "D2": SleepwalkAt(p, g, c, r); break;
                case "D3": StartSlowDevour(p, g, q); break;
                case "D4": LinarianDream.Procession(p, g, LinarianTactics.firstZone, r, c); break;
                case "D5": LinarianDream.Retinue(p, g, LinarianTactics.firstZone, r, c); break;
                // The cult
                case "C1": if (g.adoreMass) LinarianCult.SowMany(p, g, c, r); else LinarianCult.SowOne(p, g, q); break;
                case "C2": if (p.Faction == Faction.OfPlayer) LinarianCult.GuardTheMap(p, g); else GatherFlock(p); break;
                case "C3": if (p.Faction == Faction.OfPlayer) LinarianCult.Ecstasy(p, g, c, r); else Ecstasy(p, g, c, r); break;
                case "C4": if (p.Faction == Faction.OfPlayer) LinarianCult.LastOrder(p, g, g.lastOrderMass ? null : q, c, r); else LastOrder(p, q); break;
                case "C5": if (!(g.convertOne ? ConvertOne(p, q) : Conversion(p))) { Gene_ArcaneHunger.Feed(p, LinarianTactics.Cost("C5", g)); g.Cooldown("C5", 0); } break;   // no one turned: the hunger is given back
                // Will
                case "W1": Call(p, g, c, r); break;
                case "W2": FalseOrder(p, g, c, r); break;
                case "W3": Discord(p, g, c, r, 0); break;
                case "W4": Captivate(p, g, c, r); break;
                case "W5": TurnRaid(p, g, c, r); break;
                // The feast
                case "F2": Sip(p, g, q); break;   // anyone in her zone, as before
                case "F3": LinarianFeast.Release(p, g); break;
                case "F4": StrangerVoice(p, g, c, r); break;
                case "F5": Scream(p, g, c, r); break;
                // Phantasmal pain
                case "S1": Rend(p, g, q, c, r); break;
                case "SL": LinarianWounds.Lift(p, g, q); break;
                case "S2": LinarianWounds.MarkedPrey(p, g, q); break;
                case "S3": LinarianWounds.Terror(p, g, c, r); break;
                case "S4": LinarianWounds.String(p, g); break;
                case "S5": LinarianWounds.Flay(p, g); break;
                // Guises
                case "G1": LinarianVeil.Veil(p, LinarianZone.Ticks(H * 2f, g)); break;
                case LinarianTactics.Doe: case LinarianTactics.Crow: case LinarianTactics.Wolf: LinarianVeil.SetForm(p, t); break;
                case "G3": Mirage(p, g, c, r); break;
                case "G4": LinarianGuise.Switch(p, g, c); break;
                case "G5":
                    foreach (Pawn a in LinarianZone.OwnAt(p, c, r)) LinarianGuise.PackVeil(p, a, LinarianZone.Ticks(H / 2f, g));
                    break;
                // The pack
                case "P1": LinarianPack.Gather(p, g, Mathf.Min(20f, 10f + 4f * g.Power)); break;
                case "P2": LinarianPack.DriveHerd(p, g, c, r); break;
                case "P4": LinarianPack.CallPredators(p, g); break;
                case "P5":
                    {
                        bool second = LinarianTactics.RaidArmed(g) && g.raidArmed != Find.TickManager.TicksGame;
                        LinarianPack.AgainstTheRaid(p, g, second);
                        if (!second) g.raidArmed = Find.TickManager.TicksGame;
                        break;
                    }
                // In the shadow of the Flow
                case "T1": Leap(p, c); LinarianTime.ShadowStep(p, g); break;
                case "T2": if (g.ageMode) LinarianTime.Preserve(p, g); else LinarianTime.Theft(p, g); break;
                case "T3": LinarianTime.Distortion(p, g, c, r); break;
                case "T4": LinarianTime.FogOrFlare(p, g, c, r); break;
                case "T5": LinarianShadow.EternalNight(p.Map, p); break;
            }
        }

        // ---------------------------------------------------------------- Lullaby

        public static void Lullaby(Pawn p, Gene_LinarianSong g, float radius, int bonus) => Lullaby(p, g, p.Position, radius, bonus);

        public static void Lullaby(Pawn p, Gene_LinarianSong g, IntVec3 c, float radius, int bonus)
        {
            int n = LinarianTactics.Count(g, bonus);
            int taken = 0, own = 0;
            int ticks = LinarianZone.Ticks(H * 1.5f, g);
            LinarianSleep.ReleaseEternal(p, c, radius);   // her lullaby sung again over those she left in the endless sleep lets them go
            foreach (Pawn q in LinarianZone.Around(p.Map, c, radius).Where(q => q != p && (q.HostileTo(p) || LinarianPackSiphon.Held(p, q) || LinarianCult.SingerOf(q) == p && q.Faction != p.Faction || q.Faction == null && q.RaceProps.Animal) && LinarianTactics.Takeable(q) && !LinarianTactics.Asleep(q))
                         .OrderBy(q => (q.Position - c).LengthHorizontalSquared).ToList())
            {
                if (taken >= n) break;
                if (!Rand.Chance(LinarianTactics.Chance(p, g, q, true, "L"))) continue;
                LinarianHediffs.Sleep(q, ticks, p);
                taken++;
            }
            if (g.lullOwn)
                foreach (Pawn a in LinarianZone.Around(p.Map, c, radius).Where(a => a != p && a.Faction != null && a.Faction == p.Faction && !a.Drafted && LinarianTactics.Takeable(a)))
                {
                    LinarianHediffs.Sleep(a, ticks, p);
                    own++;
                }
            FleckMaker.Static(c, p.Map, FleckDefOf.PsycastAreaEffect, radius);
            LinarianTactics.Notify(p, taken + own > 0 ? (taken + own) + " fall asleep to her lullaby." : "No one sleeps.");
        }

        /// <summary>Her own in her zone: rested, calmed, and pulled back out of a break.</summary>
        private static void QuietSong(Pawn p, Gene_LinarianSong g)
        {
            int broke = 0;
            foreach (Pawn a in LinarianZone.InHerZone(p, g).Where(a => a != p && a.Faction == p.Faction && a.RaceProps.Humanlike))
            {
                if (a.needs?.rest != null) a.needs.rest.CurLevel = Mathf.Min(1f, a.needs.rest.CurLevel + 0.4f);
                LinarianHediffs.Add(a, LinarianHediffs.Avir_SongRest, LinarianZone.Ticks(H * 2, g));
                if (a.InMentalState && !a.MentalStateDef.IsAggro) { a.MentalState.RecoverFromState(); broke++; }
            }
            if (broke > 0) LinarianTactics.Notify(p, broke + " come back to themselves.");
        }

        /// <summary>Maddened beasts round the point grow calm; now and then one stays with her. Those with scaria cannot be
        /// calmed - the sickness holds them - so she sends them away: they run off the map.</summary>
        private static void CalmBeasts(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = 0, fled = 0, joined = 0;
            HediffDef scaria = DefDatabase<HediffDef>.GetNamedSilentFail("Scaria");
            foreach (Pawn a in LinarianZone.Around(p.Map, c, r).Where(a => a.RaceProps.Animal && a.InMentalState).ToList())
            {
                if (scaria != null && a.health.hediffSet.HasHediff(scaria))
                {
                    a.MentalState.RecoverFromState();
                    if (a.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, "a Singer's song", true)) fled++;
                    continue;
                }
                a.MentalState.RecoverFromState();
                n++;
                if (p.Faction == Faction.OfPlayer && a.Faction == null && Rand.Chance(0.1f * g.Power))
                {
                    InteractionWorker_RecruitAttempt.DoRecruit(p, a, true);
                    joined++;
                }
            }
            LinarianTactics.Notify(p, n + fled == 0 ? "No beast here is maddened."
                : (n > 0 ? n + " beasts grow calm" + (joined > 0 ? ", " + joined + " stay with her" : "") + ". " : "") + (fled > 0 ? fled + " sick with scaria run from her." : ""));
        }

        private static void HeavyEyelidsOld(Pawn p, Gene_LinarianSong g)
        {
            int n = 0;
            foreach (Pawn q in LinarianTactics.Foes(p, 10f).Where(LinarianTactics.Takeable))
            {
                Hediff h = LinarianSource.Add(q, LinarianHediffs.Avir_SongDrowsy, p, 600);
                var comp = h?.TryGetComp<HediffComp_SongSource>();
                if (comp != null) comp.flag = Rand.Chance(LinarianTactics.Chance(p, g, q, true, "L"));
                n++;
            }
            LinarianTactics.Notify(p, n > 0 ? "A drowsy haze settles on " + n + "." : "No one is near.");
        }

        // ---------------------------------------------------------------- Dreaming

        /// <summary>Hers in sleep: marked, slowly devoured or walking for her.</summary>
        public static bool HerSleeper(Pawn q, Pawn her) =>
            LinarianSource.Of(q, LinarianHediffs.Avir_DreamMark) == her || LinarianSource.Of(q, LinarianHediffs.Avir_SlowDevour) == her;

        public static IEnumerable<Pawn> Sleepers(Pawn her) =>
            her.Map == null ? Enumerable.Empty<Pawn>() : her.Map.mapPawns.AllPawnsSpawned.Where(q => !q.Dead && HerSleeper(q, her)).ToList();

        public static void Mark(Pawn p, Pawn q)
        {
            if (q == null) return;
            LinarianSource.Add(q, LinarianHediffs.Avir_DreamMark, p, GenDate.TicksPerDay * 3);
            LinarianTactics.Notify(p, q.LabelShortCap + " dreams of her now.");
        }

        /// <summary>Sleepwalking round a point: the sleepers there rise and fall on her foes; with none, they shamble about.</summary>
        public static void SleepwalkAt(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = 0, max = LinarianTactics.Count(g, 1);
            foreach (Pawn q in LinarianDream.SleepersAt(p, c, r).Where(q => (q.RaceProps.Humanlike || q.RaceProps.Animal) && !(q.MentalState is MentalState_Song))
                         .OrderBy(q => (q.Position - c).LengthHorizontalSquared).ToList())
            {
                if (n >= max) break;
                if (Sleepwalk(p, g, q, false)) n++;
            }
            LinarianTactics.Notify(p, n > 0 ? n + " rise in their sleep." : "No one sleeps there.");
        }

        public static bool Sleepwalk(Pawn p, Gene_LinarianSong g, Pawn q, bool tell = true)
        {
            if (q == null) return false;
            if (!HerSleeper(q, p)) LinarianSource.Add(q, LinarianHediffs.Avir_DreamMark, p, GenDate.TicksPerDay);   // walking for her, it is hers
            // her own aria-sleep holds the body down; to walk for her, it lets go
            Hediff aria = q.health?.hediffSet?.GetFirstHediffOfDef(LinarianHediffs.Avir_SongSleep);
            if (aria != null) q.health.RemoveHediff(aria);
            if (q.Downed) return false;
            RestUtility.WakeUp(q);
            LinarianHediffs.Add(q, LinarianHediffs.Avir_Sleepwalking, H * 2);
            bool ok = LinarianMental.Start(q, p, LinarianMental.Sleepwalk, H * 2);
            if (ok && tell) LinarianTactics.Notify(p, q.LabelShortCap + " rises in their sleep.");
            return ok;
        }

        public static int MaxDevours(Gene_LinarianSong g) => g.Has("D5") ? 5 : 3;

        public static bool StartSlowDevour(Pawn p, Gene_LinarianSong g, Pawn q)
        {
            if (q == null || LinarianSource.Of(q, LinarianHediffs.Avir_SlowDevour) != null) return false;
            int now = p.MapHeld == null ? 0 : p.MapHeld.mapPawns.AllPawns.Count(x => LinarianSource.Of(x, LinarianHediffs.Avir_SlowDevour) == p);
            if (now >= MaxDevours(g)) { LinarianTactics.Notify(p, "she cannot drink from more at once."); return false; }
            Hediff h = LinarianSource.Add(q, LinarianHediffs.Avir_SlowDevour, p, -1, 0.05f);
            LinarianSource.Add(q, LinarianHediffs.Avir_DreamMark, p, GenDate.TicksPerDay * 12);
            return h != null;
        }

        public static void Verdict(Pawn p, Gene_LinarianSong g, Pawn target)
        {
            if (target == null || target.Dead) return;
            int n = 0;
            foreach (Pawn q in Sleepers(p).Where(q => q != target).ToList())
            {
                // her aria-sleep holds the body down; to rise for her verdict, it lets go
                Hediff aria = q.health?.hediffSet?.GetFirstHediffOfDef(LinarianHediffs.Avir_SongSleep);
                if (aria != null) q.health.RemoveHediff(aria);
                if (q.Downed) continue;
                if (LinarianMental.StartVerdict(q, p, target, H * 3)) n++;
            }
            LinarianTactics.Notify(p, n > 0 ? n + " of her sleepers rise against " + target.LabelShort + "." : "None of her sleepers can rise.");
        }

        private static void Retinue(Pawn p, Gene_LinarianSong g)
        {
            int n = 0;
            foreach (Pawn q in Sleepers(p).Where(LinarianTactics.Asleep).Take(3).ToList())
                if (Sleepwalk(p, g, q)) n++;
            if (n == 0) LinarianTactics.Notify(p, "none of her sleepers sleeps now.");
        }

        // ---------------------------------------------------------------- The cult

        public static void Adore(Pawn p, Gene_LinarianSong g, Pawn q, float sev)
        {
            if (q == null || !LinarianTactics.Takeable(q)) return;
            Hediff h = q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_Adoration);
            if (h != null && LinarianSource.Of(q, LinarianHediffs.Avir_Adoration) == p)
            {
                h.Severity = Mathf.Min(1f, h.Severity + sev);
                if (h.Severity >= 0.999f && q.Faction == Faction.OfPlayer && p.Faction != Faction.OfPlayer) WildGods.Accept(q, p, true);
                return;
            }
            if (h != null) q.health.RemoveHediff(h);
            LinarianSource.Add(q, LinarianHediffs.Avir_Adoration, p, -1, sev);
            LinarianTactics.Notify(p, q.LabelShortCap + " cannot stop thinking of her.");
        }

        public static IEnumerable<Pawn> Flock(Pawn her) =>
            her.Map == null ? Enumerable.Empty<Pawn>() : her.Map.mapPawns.AllPawnsSpawned.Where(q => !q.Dead && LinarianSource.Of(q, LinarianHediffs.Avir_Adoration) == her).ToList();

        public static float Worship(Pawn q) => q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.Avir_Adoration)?.Severity ?? 0f;

        public static void GatherFlock(Pawn p)
        {
            int n = 0;
            foreach (Pawn q in Flock(p).Where(q => !q.Downed && Worship(q) >= 0.25f))
                if (LinarianMental.Start(q, p, LinarianMental.CultGuard, H * 4)) n++;
            LinarianTactics.Notify(p, n > 0 ? n + " come to her side." : "No worshipper is fervent enough to come.");
        }

        /// <summary>Ecstasy: her worshippers round the point - foes among them too - forget the fight and stand in rapture;
        /// her own are glad.</summary>
        public static void Ecstasy(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            ThoughtDef t = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_SongBlessing");
            int rapt = 0;
            foreach (Pawn q in LinarianZone.Around(p.Map, c, r).Where(q => q != p))
            {
                bool worships = LinarianSource.Of(q, LinarianHediffs.Avir_Adoration) == p;
                if (worships && q.HostileTo(p)) { q.stances?.stunner?.StunFor(LinarianZone.Ticks(600f + 600f * Worship(q), g), p, false); rapt++; }
                if (t != null && (worships || q.Faction == p.Faction) && q.RaceProps.Humanlike) q.needs?.mood?.thoughts?.memories?.TryGainMemory(t);
            }
            FleckMaker.Static(c, p.Map, FleckDefOf.PsycastAreaEffect, r);
            LinarianTactics.Notify(p, rapt > 0 ? rapt + " of her foes stand in rapture." : "Her own are glad.");
        }

        private static void LastOrder(Pawn p, Pawn q)
        {
            if (q == null) return;
            if (q.Spawned) FleckMaker.ThrowSmoke(q.DrawPos, q.Map, 1f);
            q.Kill(null, null);
            Messages.Message(q.LabelShortCap + " took their own life at a Singer's word.", q.Corpse, MessageTypeDefOf.NegativeEvent, false);
        }

        /// <summary>Conversion of one chosen mind: it must worship her fervently.</summary>
        public static bool ConvertOne(Pawn p, Pawn q)
        {
            if (q == null || p.Faction == null || q.Faction == p.Faction || q.Dead || Worship(q) < 0.5f || LinarianCult.SingerOf(q) != p)
            {
                LinarianTactics.Notify(p, "That one does not worship her fervently enough - the hunger comes back to her.");
                return false;
            }
            q.SetFaction(p.Faction);
            q.mindState?.mentalStateHandler?.Reset();
            LinarianTactics.Notify(p, q.LabelShortCap + " turns to her side for good.");
            return true;
        }

        public static bool Conversion(Pawn p)
        {
            int n = 0;
            foreach (Pawn q in Flock(p).Where(q => q.Faction != p.Faction && Worship(q) >= 0.5f).ToList())
            {
                if (p.Faction == null) continue;
                q.SetFaction(p.Faction);
                q.mindState?.mentalStateHandler?.Reset();
                n++;
            }
            LinarianTactics.Notify(p, n > 0 ? n + " turn to her side for good." : "None of her foes worships her enough - the hunger comes back to her.");
            return n > 0;
        }

        // ---------------------------------------------------------------- Will

        public static void Call(Pawn p, Gene_LinarianSong g) => Call(p, g, p.Position, 15f);

        public static void Call(Pawn p, Gene_LinarianSong g, IntVec3 c, float radius)
        {
            int n = LinarianTactics.Count(g, g.Has("W5") ? 2 : 0);
            List<Pawn> foes = LinarianZone.FoesAt(p, c, radius);
            int taken = 0;
            foreach (Pawn q in foes)
            {
                if (taken >= n) break;
                if (!Rand.Chance(LinarianTactics.Chance(p, g, q, true, "W"))) continue;
                int ticks = LinarianZone.Ticks(H / 2f, g);
                if (!LinarianMental.StartCalled(q, p, ticks)) continue;
                HediffDef slow = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CallHeavy");
                if (slow != null) LinarianHediffs.Add(q, slow, ticks + H);   // slow under it, and an hour after
                taken++;
                if (g.Dark("W")) LinarianShadow.ThroughTheDark(q, p);
            }
            LinarianTactics.Notify(p, taken > 0 ? taken + " answer her Call." : "No one answers her Call.");
        }

        /// <summary>The commander's voice: "Hold the position! They are coming!" - those who believe it crowd round the
        /// point and defend it to the last; those who do not walk on.</summary>
        private static void FalseOrder(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = 0;
            MentalStateDef hold = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_HoldPosition");
            foreach (Pawn q in LinarianZone.FoesAt(p, c, r))
            {
                if (!Rand.Chance(LinarianTactics.Chance(p, g, q, false, "W"))) continue;
                if (!LinarianMental.Start(q, p, hold, LinarianZone.Ticks(H * 2, g))) continue;
                if (q.MentalState is MentalState_SongHold h) h.point = c;
                n++;
            }
            if (p.Spawned) MoteMaker.ThrowText(c.ToVector3Shifted(), p.Map, "Hold the position!", 4f);
            LinarianTactics.Notify(p, n > 0 ? n + " crowd round the point to hold it." : "No one believes the order.");
        }

        /// <summary>The sip: she walks up to them first if she must.</summary>
        private static void SipOrApproach(Pawn p, Gene_LinarianSong g, Pawn q)
        {
            if (q == null) return;
            if ((q.Position - p.Position).LengthHorizontal <= 2f) { Sip(p, g, q); return; }
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Avir_LinarianSip");
            if (def == null) { Sip(p, g, q); return; }
            p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, q), JobTag.Misc);
        }

        public static void SipNow(Pawn p, Pawn q) { Gene_LinarianSong g = Linarian.Song(p); if (g != null) Sip(p, g, q); }

        public static void Discord(Pawn p, Gene_LinarianSong g, float radius, int bonus) => Discord(p, g, p.Position, radius, bonus);

        public static void Discord(Pawn p, Gene_LinarianSong g, IntVec3 c, float radius, int bonus)
        {
            int n = LinarianTactics.Count(g, bonus);
            int taken = 0;
            foreach (Pawn q in LinarianZone.FoesAt(p, c, radius))
            {
                if (taken >= n) break;
                if (!Rand.Chance(LinarianTactics.Chance(p, g, q, false, "W"))) continue;
                if (q.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Berserk, "voices of their own", true)) taken++;
            }
            LinarianTactics.Notify(p, taken > 0 ? taken + " turn on their own." : "The voices are not heeded.");
        }

        public static void Captivate(Pawn p, Gene_LinarianSong g) => Captivate(p, g, p.Position, 10f);

        public static void Captivate(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = LinarianTactics.Count(g) / 2 + 1;
            int ticks = LinarianZone.Ticks(H * 2f * (g.Has("W4") ? 1.5f : 1f), g);
            int taken = 0;
            foreach (Pawn q in LinarianZone.FoesAt(p, c, r).Where(q => q.Faction != Faction.OfPlayer && q.RaceProps.Humanlike))
            {
                if (taken >= n) break;
                if (!Rand.Chance(LinarianTactics.Chance(p, g, q, false, "W"))) continue;
                if (LinarianHediffs.Captivate(q, p, ticks)) taken++;
            }
            LinarianTactics.Notify(p, taken > 0 ? taken + " now fight for her." : "No one is taken.");
        }

        private static void TurnRaid(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = LinarianTactics.Count(g, 6);
            int taken = 0;
            foreach (Pawn q in LinarianZone.FoesAt(p, c, r))
            {
                if (taken >= n) break;
                if (!Rand.Chance(LinarianTactics.Chance(p, g, q, false, "W"))) continue;
                bool ok = taken % 2 == 0 || q.Faction == Faction.OfPlayer
                    ? q.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Berserk, "the raid turned on itself", true)
                    : LinarianHediffs.Captivate(q, p, LinarianZone.Ticks(H * 2, g));
                if (ok) taken++;
            }
            LinarianTactics.Notify(p, taken > 0 ? "The raid turns on itself: " + taken + " taken." : "The raid does not hear her.");
        }

        // ---------------------------------------------------------------- The feast

        public static void DeadVoice(Pawn p, Gene_LinarianSong g, SongVoice v)
        {
            Hediff h = LinarianHediffs.Add(p, LinarianHediffs.Avir_DeadVoice, GenDate.TicksPerDay);
            var comp = h?.TryGetComp<HediffComp_DeadVoice>();
            if (comp != null) { comp.skill = v.skill; comp.level = v.level; comp.name = v.merged ? "a merging" : v.name; }
            LinarianFeast.Spend(g, v);
            LinarianTactics.Notify(p, "she speaks with the voice of " + (v.merged ? "a merging" : v.name) + (v.forgotten ? " - for the last time; now it is only a name." : " (" + v.uses + " left)."));
        }

        private static void Sip(Pawn p, Gene_LinarianSong g, Pawn q)
        {
            if (q == null) return;
            float raw = AvirEssence.VictimValue(q) * 0.2f;
            AvirEssenceLifetime.Track(p, raw, "siphon");
            AvirEssence.Gain(p, raw);
            LinarianFeast.FeedOrStore(p, g, Gene_ArcaneHunger.FillPerSiphon * 0.7f, 1f);   // what does not fit is stored
            HediffDef drained = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StrengthDrained");
            if (drained != null) q.health.AddHediff(drained);
            LinarianTactics.Notify(p, "she drinks a little from " + q.LabelShort + ".");
        }

        private static void StrangerVoice(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = 0;
            foreach (Pawn q in LinarianZone.FoesAt(p, c, r))
                if (Rand.Chance(LinarianTactics.Chance(p, g, q, false, "F")) && LinarianFeast.FalseRetreat(p, q)) n++;
            LinarianTactics.Notify(p, n > 0 ? n + " drop their weapons and run at their commander's voice." : "No one believes the voice.");
        }

        private static void Scream(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int ticks = Mathf.Clamp(LinarianZone.Ticks(300 + g.VoiceWorth * 40f, g), 300, 2400);
            int n = 0;
            foreach (Pawn q in LinarianZone.Around(p.Map, c, r).Where(q => q != p && q.HostileTo(p) && LinarianTactics.Takeable(q)).ToList())
            {
                q.stances?.stunner?.StunFor(ticks, p, false);
                bool timid = q.GetStatValue(StatDefOf.PsychicSensitivity) > 1.2f || (LinarianTactics.Nerves != null && q.story?.traits?.DegreeOfTrait(LinarianTactics.Nerves) < 0);
                if (timid) LinarianFeast.Confuse(p, q, ticks + GenDate.TicksPerHour);   // they run about where it caught them, screaming
                n++;
            }
            FleckMaker.Static(c, p.Map, FleckDefOf.PsycastAreaEffect, r);
            LinarianTactics.Notify(p, n > 0 ? "every voice she holds screams: " + n + " struck." : "No one hears the scream.");
        }

        // ---------------------------------------------------------------- Scars

        public static void LiftScar(Pawn p, Pawn q)
        {
            if (q == null) return;
            foreach (Hediff h in q.health.hediffSet.hediffs.Where(h => h.def == LinarianHediffs.PhantomScar).ToList()) q.health.RemoveHediff(h);
            LinarianTactics.Notify(p, q.LabelShortCap + "'s phantasmal pain is lifted.");
        }

        /// <summary>She tears at the pain she left: every place of it bleeds anew, and the pain stuns.</summary>
        /// <summary>She tears at the pain she left - no wound, only pain: it flares and stuns. One alone (and the scar
        /// often runs on up the limb), or everyone round a point (stunned, the scars rarely spread).</summary>
        private static void Rend(Pawn p, Gene_LinarianSong g, Pawn q, IntVec3 c, float r)
        {
            bool mass = g.rendMass;
            List<Pawn> who = mass ? Scarred(p.Map).Where(x => x != p && (x.Position - c).LengthHorizontal <= r).ToList()
                                  : q == null ? new List<Pawn>() : new List<Pawn> { q };
            float spread = mass ? 0.05f : 0.4f;
            int runs = 0;
            foreach (Pawn x in who)
            {
                float total = 0f;
                foreach (Hediff h in x.health.hediffSet.hediffs.Where(h => h.def == LinarianHediffs.PhantomScar).ToList())
                {
                    total += h.Severity;
                    // the scar runs on up the limb, like a blade - unless the mind holds it
                    if (Rand.Chance(spread) && Rand.Chance(Mathf.Clamp01(LinarianTactics.Soc(p, x, "S")))) { LinarianWounds.RunUp(p, g, x, h); runs++; }
                }
                LinarianHediffs.Add(x, LinarianHediffs.Avir_ScarFlare, 600, 1f, p);
                x.stances?.stunner?.StunFor(Mathf.RoundToInt((mass ? 40f : 60f) + (mass ? 120f : 240f) * total), p, false);
            }
            LinarianTactics.Notify(p, who.Count == 0 ? "no one there bears her pain." : "she tears at the pain of " + who.Count + (runs > 0 ? "; " + runs + " scars run on." : "."));
        }

        /// <summary>Marked prey: everyone in her zone who bears her pain is shown, and takes more harm for a while.</summary>
        private static void MarkedPrey(Pawn p, Gene_LinarianSong g)
        {
            HediffDef marked = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_MarkedPrey");
            List<Pawn> l = Scarred(p.Map).Where(q => q != p && LinarianZone.InZone(p, g, q.Position)).ToList();
            foreach (Pawn q in l)
            {
                FleckMaker.ThrowMetaIcon(q.Position, q.Map, FleckDefOf.IncapIcon);
                if (marked != null && q.HostileTo(p)) LinarianHediffs.Add(q, marked, LinarianZone.Ticks(H, g));
            }
            LinarianTactics.Notify(p, l.Count == 0 ? "no one in her reach bears her pain." : "Her prey: " + string.Join(", ", l.Select(q => q.LabelShort)) + ".");
        }

        public static IEnumerable<Pawn> Scarred(Map map) =>
            map.mapPawns.AllPawnsSpawned.Where(q => !q.Dead && q.health.hediffSet.HasHediff(LinarianHediffs.PhantomScar)).ToList();

        private static void ScentMarked(Pawn p)
        {
            List<Pawn> l = Scarred(p.Map).ToList();
            foreach (Pawn q in l) FleckMaker.ThrowMetaIcon(q.Position, q.Map, FleckDefOf.IncapIcon);
            LinarianTactics.Notify(p, l.Count == 0 ? "no one here carries her scars." : "She senses: " + string.Join(", ", l.Select(q => q.LabelShort)) + ".");
        }

        private static void Terror(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = 0;
            IEnumerable<Pawn> who = Scarred(p.Map).Where(q => q.HostileTo(p) && LinarianZone.InZone(p, g, q.Position)).Concat(LinarianZone.FoesAt(p, c, r)).Distinct();
            foreach (Pawn q in who.Where(LinarianTactics.Takeable).ToList())
                if (q.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, "terror of a Singer", true)) n++;
            LinarianTactics.Notify(p, n > 0 ? n + " run in terror." : "No one breaks.");
        }

        private static void String(Pawn p, Gene_LinarianSong g, Pawn q)
        {
            if (q == null) return;
            if (!Rand.Chance(LinarianTactics.Chance(p, g, q, true, "S"))) { LinarianTactics.Notify(p, q.LabelShort + " resists the string."); return; }
            LinarianMental.StartCalled(q, p, LinarianZone.Ticks(H * 2, g));
            if (g.Dark("W")) LinarianShadow.ThroughTheDark(q, p);
        }

        private static void OpenScars(Pawn p, Gene_LinarianSong g)
        {
            int n = 0;
            foreach (Pawn q in Scarred(p.Map).Where(q => q != p && !q.Dead && LinarianZone.InZone(p, g, q.Position)))
            {
                LinarianHediffs.Add(q, LinarianHediffs.Avir_ScarFlare, 1200);
                if (q.health.hediffSet.GetFirstHediffOfDef(LinarianHediffs.PhantomScar).Severity > 0.6f) q.stances?.stunner?.StunFor(180, p, false);
                n++;
            }
            LinarianTactics.Notify(p, n > 0 ? "the scars of " + n + " open." : "no one here carries her scars.");
        }

        // ---------------------------------------------------------------- Guises

        public static void Mirage(Pawn p, Gene_LinarianSong g) => Mirage(p, g, p.Position, 8f);

        /// <summary>Shadows of her rise round the point and draw the fire; the foes there aim worse.</summary>
        public static void Mirage(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
        {
            int n = 0;
            foreach (Pawn q in LinarianZone.FoesAt(p, c, r).Where(LinarianTactics.Takeable))
            {
                LinarianHediffs.Add(q, LinarianHediffs.Mirage, LinarianZone.Ticks(H, g), 1f);
                n++;
            }
            Building_ShadowDecoy.Cast(p, g, c, r);
            LinarianTactics.Notify(p, "Shadows of her rise" + (n > 0 ? "; " + n + " see what is not there." : "."));
        }

        /// <summary>The switch and the step into shadow: she is simply there.</summary>
        public static void Leap(Pawn p, IntVec3 c)
        {
            if (!c.IsValid || !c.Standable(p.Map)) return;
            FleckMaker.ThrowSmoke(p.DrawPos, p.Map, 1.5f);
            p.pather?.StopDead();
            p.Position = c;
            p.Notify_Teleported(true, false);
            FleckMaker.ThrowSmoke(p.DrawPos, p.Map, 1.5f);
        }

        // ---------------------------------------------------------------- The pack

        public static void PackInZone(Pawn p, Gene_LinarianSong g, int ticks)
        {
            int taken = 0;
            foreach (Pawn a in LinarianZone.InHerZone(p, g).Where(a => a.RaceProps.Animal && a.Faction == null && !a.Downed))
                if (LinarianMental.StartPack(a, p, ticks)) taken++;
            LinarianTactics.Notify(p, taken > 0 ? taken + " beasts answer her." : "No beast in her reach hears her.");
        }

        public static void Pack(Pawn p, Gene_LinarianSong g, float radius, float hours)
        {
            int taken = 0;
            foreach (Pawn a in p.Map.mapPawns.AllPawnsSpawned.Where(a => a.RaceProps.Animal && a.Faction == null && !a.Dead && !a.Downed
                                                                          && (a.Position - p.Position).LengthHorizontal <= radius).ToList())
                if (LinarianMental.StartPack(a, p, Mathf.RoundToInt(H * hours))) taken++;
            LinarianTactics.Notify(p, taken > 0 ? taken + " beasts answer her." : "No beast is near enough to hear.");
        }

        private static void Predator(Pawn p, Gene_LinarianSong g)
        {
            List<PawnKindDef> kinds = p.Map.Biome.AllWildAnimals.Where(k => k.RaceProps.predator && k.RaceProps.baseBodySize >= 0.8f).ToList();
            PawnKindDef kind = kinds.RandomElementWithFallback() ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Wolf_Timber");
            if (kind == null) return;
            int count = g.Dark("P") ? Rand.RangeInclusive(2, 3) : 1;
            for (int i = 0; i < count; i++)
            {
                IntVec3 at;
                if (!RCellFinder.TryFindRandomPawnEntryCell(out at, p.Map, 0f)) continue;   // always from the wild, the edge of the map
                Pawn a = PawnGenerator.GeneratePawn(kind);
                GenSpawn.Spawn(a, at, p.Map);
                if (g.Dark("P")) FleckMaker.ThrowSmoke(a.DrawPos, a.Map, 1.5f);
                LinarianMental.StartPack(a, p, H * 6);
            }
            LinarianTactics.Notify(p, (count > 1 ? count + " " + kind.label + "s" : "a " + kind.label) + " answers her from the wild.");
        }

        // ---------------------------------------------------------------- In the shadow of the Flow

        public static void TheftOfYears(Pawn p, Pawn q, float years)
        {
            if (q?.ageTracker == null) return;
            long y = (long)(years * GenDate.TicksPerYear);
            q.ageTracker.AgeBiologicalTicks += y;
            long min = 22L * GenDate.TicksPerYear;
            p.ageTracker.AgeBiologicalTicks = System.Math.Max(min, p.ageTracker.AgeBiologicalTicks - y);
            LinarianShadow.Heal(p, 30f);
            LinarianTactics.Notify(p, "she drinks " + years.ToString("0") + " years from " + q.LabelShort + ".");
        }

        public static void Distortion(Pawn p, float radius, int ticks)
        {
            foreach (Pawn q in LinarianTactics.Foes(p, radius)) LinarianHediffs.Add(q, LinarianHediffs.Avir_TimeSlow, ticks);
            foreach (Pawn a in LinarianTactics.Own(p, radius).Concat(new[] { p })) LinarianHediffs.Add(a, LinarianHediffs.Avir_TimeHaste, ticks);
        }

        public static void Distortion(Pawn p, Gene_LinarianSong g, IntVec3 c, float radius, int ticks)
        {
            foreach (Pawn q in LinarianZone.FoesAt(p, c, radius)) LinarianHediffs.Add(q, LinarianHediffs.Avir_TimeSlow, ticks);
            foreach (Pawn a in LinarianZone.OwnAt(p, c, radius).Concat(new[] { p })) LinarianHediffs.Add(a, LinarianHediffs.Avir_TimeHaste, ticks);
        }
    }
}
