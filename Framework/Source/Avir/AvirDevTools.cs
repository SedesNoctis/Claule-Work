using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;
using Avir.Spire;

namespace Avir
{
    /// <summary>
    /// Developer tools for testing every system of the mod: Debug actions menu (dev mode) -> "Avir: ...".
    /// Map tools act on the cell under the mouse; pawn tools on the pawn clicked.
    /// </summary>
    public static class AvirDevTools
    {
        private const string Cat = "Avir";

        // ------------------------------------------------------------------ the north

        /// <summary>Cycles how the northern trees are printed, to find what keeps them from swaying, and logs the
        /// material: its shader, whether the wind manager drives it, and its sway value now.</summary>
        [DebugAction(Cat, "North trees: sway test", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TreeSwayTest()
        {
            Avir.North.NorthCanopy.SwayMode = (Avir.North.NorthCanopy.SwayMode + 1) % 3;
            Find.CurrentMap.mapDrawer.RegenerateEverythingNow();
            string[] names = { "0 - Avir print (as before)", "1 - Avir print, top edge raised 0.1 like vanilla", "2 - vanilla Plant.Print" };
            Material m = Avir.North.NorthCanopy.lastMat;
            var list = HarmonyLib.AccessTools.StaticFieldRefAccess<System.Collections.Generic.List<Material>>(typeof(WindManager), "plantMaterials");
            Log.Message("[Avir north] sway test mode " + names[Avir.North.NorthCanopy.SwayMode]
                + (m == null ? "" : "; last tree material: shader " + m.shader?.name + ", texture " + m.mainTexture?.name
                    + ", driven by the wind: " + (list != null && list.Contains(m))
                    + ", _SwayHead " + (m.HasProperty("_SwayHead") ? m.GetFloat("_SwayHead").ToString("0.0") : "none")
                    + "; wind " + Find.CurrentMap.windManager.WindSpeed.ToString("0.00") + ", sway pref " + Prefs.PlantWindSway));
        }

        /// <summary>Everything that colours the cell under the mouse: its terrain and the texture it is drawn with, the
        /// light on it (the glow grid, the sky), the roof, the weather and the conditions on the map.</summary>
        [DebugAction(Cat, "Cell colour report (under mouse)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CellColourReport()
        {
            Map map = Find.CurrentMap;
            IntVec3 c = Verse.UI.MouseCell();
            if (!c.InBounds(map)) return;
            TerrainDef t = c.GetTerrain(map);
            Material m = t.graphic?.MatSingle;
            Texture tex = m?.mainTexture;
            var sb = new System.Text.StringBuilder("[Avir] cell " + c + ": terrain " + t.defName + " (" + t.modContentPack?.Name + ")");
            sb.Append("; texture ").Append(tex == null ? "NONE" : tex.name + " " + tex.width + "x" + tex.height + " " + (tex as Texture2D)?.format
                + (tex == BaseContent.BadTex ? " = BAD TEXTURE (file not found)" : ""));
            sb.Append(", shader ").Append(m?.shader?.name).Append(", material colour ").Append(m == null ? "-" : m.color.ToString());
            sb.Append("; glow here ").Append(map.glowGrid.VisualGlowAt(c)).Append(" (ground ").Append(map.glowGrid.GroundGlowAt(c).ToString("0.00")).Append(")");
            sb.Append("; sky glow ").Append(map.skyManager.CurSkyGlow.ToString("0.00"));
            sb.Append("; roof ").Append(c.GetRoof(map)?.defName ?? "none").Append("; weather ").Append(map.weatherManager.curWeather?.defName);
            sb.Append("; conditions: ").Append(string.Join(", ", map.gameConditionManager.ActiveConditions.Select(g => g.def.defName))
                + " | world: " + string.Join(", ", Find.World.gameConditionManager.ActiveConditions.Select(g => g.def.defName)));
            sb.Append("; mutators: ").Append(string.Join(", ", map.TileInfo.Mutators.Select(x => x.defName)));
            Log.Message(sb.ToString());
        }

        // ------------------------------------------------------------------ the spire

        private static Thing MakeCore(bool kindle)
        {
            Thing t = ThingMaker.MakeThing(ThingDef.Named("Avir_SpireCore"));
            CompSpireCore c = t.TryGetComp<CompSpireCore>();
            if (kindle && c != null)
            {
                c.kindled = true; c.progress = CompSpireCore.KindleCost;
                c.power = 1.5f; c.capacity = 3000f; c.efficiency = 0.9f; c.ward = 1.5f; c.reach = 1.25f;
            }
            return t;
        }

        [DebugAction(Cat, "Spawn kindled spire core", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnKindledCore() => GenPlace.TryPlaceThing(MakeCore(true), Verse.UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);

        [DebugAction(Cat, "Spawn dead spire core", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnDeadCore() => GenPlace.TryPlaceThing(MakeCore(false), Verse.UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);

        [DebugAction(Cat, "Spire: sink nine cores, fill", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpireFull()
        {
            foreach (CompSpireCradle c in Cradles())
            {
                while (c.cores.Count < c.Props.maxCores) c.Sink(MakeCore(true));
                c.Give(c.Capacity);
            }
        }

        [DebugAction(Cat, "Spire: fill energy", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpireFill() { foreach (CompSpireCradle c in Cradles()) c.Give(c.Capacity); }

        [DebugAction(Cat, "Spire: empty energy", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpireEmpty() { foreach (CompSpireCradle c in Cradles()) c.Take(c.Stored); }

        // ------------------------------------------------------------------ golden and silver fire

        [DebugAction(Cat, "Golden fire burst", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void GoldFireBurst() => FireBurst(Avir.Artifactorics.EssentiumFireDefOf.Avir_GoldFire);

        [DebugAction(Cat, "Silver fire burst", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SilverFireBurst() => FireBurst(Avir.Artifactorics.EssentiumFireDefOf.Avir_SilverFire);

        private static void FireBurst(ThingDef fire)
        {
            Map map = Find.CurrentMap;
            IntVec3 at = Verse.UI.MouseCell();
            FleckMaker.ThrowLightningGlow(at.ToVector3Shifted(), map, 3f);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 3.9f, true))
                if (c.InBounds(map)) Avir.Artifactorics.EssentiumFire.StartIn(fire, c, map, Rand.Range(0.4f, 1f), null);
        }

        // ------------------------------------------------------------------ the north

        [DebugAction(Cat, "North: plant a test grove", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void NorthGrove()
        {
            Map map = Find.CurrentMap;
            IntVec3 at = Verse.UI.MouseCell();
            string[] small = { "Avir_PlantSilverGrass", "Avir_PlantRimebloom", "Avir_PlantArcticBerry", "Avir_PlantArcticPsychoid", "Avir_PlantHushleaf" };
            void Put(string dn, IntVec3 c)
            {
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(dn);
                if (d == null || !c.InBounds(map) || c.GetEdifice(map) != null) return;
                c.GetPlant(map)?.Destroy();
                Plant p = (Plant)ThingMaker.MakeThing(d);
                p.Growth = 1f;
                GenSpawn.Spawn(p, c, map);
            }
            Put("Avir_TreeShelterwing", at);
            Put("Avir_TreeFrostglass", at + new IntVec3(4, 0, 0));
            Put("Avir_TreeRimecrown", at + new IntVec3(-4, 0, 0));
            Put("Avir_TreeSpruce", at + new IntVec3(0, 0, 4));
            int i = 0;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 2.9f, false))
                Put(small[i++ % small.Length], c);
        }

        [DebugAction(Cat, "North: howling storm (power...)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void NorthBlizzard()
        {
            var o = new System.Collections.Generic.List<DebugMenuOption>();
            Map map = Find.CurrentMap;
            var cur = Avir.North.GameCondition_HowlingStorm.On(map);
            if (cur != null) o.Add(new DebugMenuOption("end the storm now", DebugMenuOptionMode.Action, () => cur.End()));
            for (int p = 1; p <= Avir.North.GameCondition_HowlingStorm.MaxPower; p++)
            {
                int pp = p;
                o.Add(new DebugMenuOption("power " + p + " (3 days)", DebugMenuOptionMode.Action, () =>
                {
                    if (cur != null) cur.power = pp; else Avir.North.GameCondition_HowlingStorm.Start(map, 3, pp);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(o));
        }

        // ------------------------------------------------------------------ bird milk (RJW)

        [DebugAction(Cat, "Bird milk: flowing and full", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BirdMilkFull(Pawn p)
        {
            HediffDef flow = AvirBirdMilk.Flow;
            if (flow == null) { Messages.Message("Bird milk needs RJW.", MessageTypeDefOf.RejectInput, false); return; }
            if (!AvirBirdMilk.CanFlow(p)) { Messages.Message(p.LabelShort + " is not a grown Avir female.", MessageTypeDefOf.RejectInput, false); return; }
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(flow) ?? p.health.AddHediff(flow);
            h.Severity = 1f;
            HediffComp_BirdMilk m = h.TryGetComp<HediffComp_BirdMilk>();
            if (m != null) m.fullness = 1f;
        }

        [DebugAction(Cat, "Bird milk: drink from the source (pick mother)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BirdMilkSource(Pawn mother)
        {
            Pawn mate = mother?.relations == null ? null : LovePartnerRelationUtility.ExistingMostLikedLovePartner(mother, false);
            if (mate == null) { Messages.Message("She has no mate.", MessageTypeDefOf.RejectInput, false); return; }
            AvirBirdMilk.Gain(mate, "Avir_FromTheSource", mother);
            AvirBirdMilk.Gain(mother, "Avir_FedMyMate", mate);
            AvirBirdMilk.MilkOf(mother)?.Take(0.5f);
        }

        private static System.Collections.Generic.IEnumerable<CompSpireCradle> Cradles() =>
            Find.CurrentMap.listerBuildings.allBuildingsColonist.Select(b => b.TryGetComp<CompSpireCradle>()).Where(c => c != null).ToList();

        // ------------------------------------------------------------------ energy and the stream

        [DebugAction(Cat, "Energy keepers: fill all", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void KeepersFill() { foreach (CompEnergyKeeper k in CompEnergyKeeper.All.Where(k => k.parent.Map == Find.CurrentMap).ToList()) k.Give(k.Capacity); }

        [DebugAction(Cat, "Energy keepers: empty all", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void KeepersEmpty() { foreach (CompEnergyKeeper k in CompEnergyKeeper.All.Where(k => k.parent.Map == Find.CurrentMap).ToList()) k.Take(k.Capacity); }

        [DebugAction(Cat, "Map stream +375 000", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StreamUp() { MapComponent_ExternalFlow e = MapComponent_ExternalFlow.Of(Find.CurrentMap); if (e != null) e.deeds += 25000f * MapComponent_ExternalFlow.Scale; }

        [DebugAction(Cat, "Map stream: calm it", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StreamCalm() { MapComponent_ExternalFlow e = MapComponent_ExternalFlow.Of(Find.CurrentMap); if (e != null) e.deeds = 0f; }

        [DebugAction(Cat, "Learn every hidden ritual", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LearnAll() { foreach (RitualPresetDef p in DefDatabase<RitualPresetDef>.AllDefsListForReading.Where(p => p.hidden)) RitualDiscoveries.Learn(p); }

        // ------------------------------------------------------------------ pawns

        [DebugAction(Cat, "Saturation +100%", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SatUp(Pawn p) => Hediff_StreamSaturation.Change(p, 100f * Hediff_StreamSaturation.UnitsPerPercent);

        [DebugAction(Cat, "Saturation 1000%", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Sat1000(Pawn p) => Hediff_StreamSaturation.Change(p, (10f - Hediff_StreamSaturation.Level(p)) * 100f * Hediff_StreamSaturation.UnitsPerPercent);

        [DebugAction(Cat, "Saturation 0%", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Sat0(Pawn p) => Hediff_StreamSaturation.Change(p, -Hediff_StreamSaturation.Held(p));

        [DebugAction(Cat, "Composure: +10 000 ritual experience", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ComposureXp(Pawn p) => GameComponent_AvirComposure.AddTurnover(p, 10000f);

        [DebugAction(Cat, "Composure: +5 h meditation", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ComposureCalm(Pawn p) => GameComponent_AvirComposure.AddMeditation(p, 5 * GenDate.TicksPerHour);

        [DebugAction(Cat, "Take the beak (test eating)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TakeBeak(Pawn p)
        {
            BodyPartRecord b = AvirBeakless.Beak;
            if (b == null || !AvirBody.IsAvirBody(p) || p.health.hediffSet.PartIsMissing(b)) return;
            p.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, p, b), b);
        }

        [DebugAction(Cat, "Make a chick of this family (age 1)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MakeChick(Pawn parent)
        {
            if (parent?.Map == null) return;
            var req = new PawnGenerationRequest(parent.kindDef, parent.Faction, PawnGenerationContext.NonPlayer, fixedBiologicalAge: 1f, fixedChronologicalAge: 1f,
                developmentalStages: DevelopmentalStage.Baby, forcedXenotype: parent.genes?.Xenotype, canGeneratePawnRelations: false);
            Pawn chick = PawnGenerator.GeneratePawn(req);
            chick.relations?.AddDirectRelation(PawnRelationDefOf.Parent, parent);
            GenSpawn.Spawn(chick, CellFinder.StandableCellNear(parent.Position, parent.Map, 3f), parent.Map);
        }
    }
}
