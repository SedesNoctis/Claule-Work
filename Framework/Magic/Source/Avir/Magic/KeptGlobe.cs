using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Magic
{
    /// <summary>A ball lightning made to stay (Fix231, the author): the arcane work of the ball lightning (an adept) makes it
    /// where it is marked. It hangs in place and gives itself away: into the batteries of a power net it touches (fast),
    /// else into the energy keepers of the arcane network near it; with nothing to take it, it swells - and past its limit it
    /// breaks loose and hunts like a wild one. So it is set with care. It can be loosed on purpose to guard the colony: then it
    /// spares the colony's own. Its light is as wide as it is big, and lingers a while after it is spent. A second work on
    /// the same globe makes it bigger - more charge to give.</summary>
    public class KeptGlobe : ThingWithComps
    {
        public float charge = 1.5f;
        public Pawn maker;
        private bool warned;
        private float phase = Rand.Range(0f, 6.28f);

        public const float EnergyPerCharge = 400f;   // Wd for a battery
        public const float BreakLoose = 4.5f;

        public static KeptGlobe At(Map map, IntVec3 c) => c.InBounds(map) ? c.GetThingList(map).OfType<KeptGlobe>().FirstOrDefault() : null;

        public static KeptGlobe Make(Map map, IntVec3 c, float charge, Pawn maker)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_KeptGlobe");
            if (def == null || map == null || !c.InBounds(map)) return null;
            KeptGlobe old = At(map, c);
            if (old != null) { old.charge += charge; old.warned = false; FleckMaker.ThrowLightningGlow(old.DrawPos, map, 2f); return old; }
            var g = (KeptGlobe)ThingMaker.MakeThing(def);
            g.charge = charge; g.maker = maker;
            if (maker?.Faction != null) g.SetFaction(maker.Faction);
            GenSpawn.Spawn(g, c, map);
            FleckMaker.ThrowLightningGlow(c.ToVector3Shifted(), map, 2f);
            MagicSounds.Play("Second:BallLightning", c, map, 0.8f, 0);
            return g;
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned) return;
            if (Rand.Chance(0.02f)) { MapComponent_SpellSprites.Bolt(Map, DrawPos, DrawPos + new Vector3(Rand.Range(-1f, 1f), 0f, Rand.Range(-1f, 1f)), 8); MagicSounds.Play("Arc", Position, Map, 0.35f, 30); }
            if (!this.IsHashIntervalTick(60)) return;
            LightGlow.At(Map, Position, charge >= 2f, 75);
            float gave = GiveToBatteries() ?? GiveToKeepers() ?? -1f;
            if (gave < 0f)
            {
                // nothing to take it: it swells
                charge += 0.008f;
                if (charge >= BreakLoose - 1f && !warned) { warned = true; Messages.Message("A kept ball lightning is swelling - nothing draws its charge. Give it a battery or the arcane network, or it breaks loose.", this, MessageTypeDefOf.CautionInput, false); }
                if (charge >= BreakLoose) { Loose(false); return; }
            }
            if (charge <= 0.05f) Spent();
        }

        /// <summary>Into the batteries of a power net under it or beside it: fast - ~40 Wd a second at charge 1, more when bigger.</summary>
        private float? GiveToBatteries()
        {
            foreach (IntVec3 c in GenAdj.CellsOccupiedBy(this).Concat(GenAdj.CellsAdjacent8Way(this)))
            {
                if (!c.InBounds(Map)) continue;
                PowerNet net = Map.powerNetGrid.TransmittedPowerNetAt(c);
                if (net == null || net.batteryComps.Count == 0) continue;
                CompPowerBattery bat = net.batteryComps.Where(b => b.AmountCanAccept > 0f).OrderBy(b => b.StoredEnergyPct).FirstOrDefault();
                if (bat == null) continue;
                float give = Mathf.Min(bat.AmountCanAccept, 40f * Mathf.Sqrt(charge), charge * EnergyPerCharge);
                bat.AddEnergy(give);
                charge -= give / EnergyPerCharge;
                if (Rand.Chance(0.4f)) MapComponent_SpellSprites.Bolt(Map, DrawPos, bat.parent.TrueCenter(), 8);
                return give;
            }
            return null;
        }

        /// <summary>Into the energy keepers of the arcane network within their reach.</summary>
        private float? GiveToKeepers()
        {
            foreach (CompEnergyKeeper k in CompEnergyKeeper.All)
            {
                if (k.parent.Map != Map || k.IsFull || k.parent.Position.DistanceTo(Position) > k.Props.chargeRadius + 2f) continue;
                float g = k.Give(Mathf.Min(6f * Mathf.Sqrt(charge), charge * 300f));
                if (g <= 0f) continue;
                charge -= g / 300f;
                if (Rand.Chance(0.4f)) MapComponent_SpellSprites.Bolt(Map, DrawPos, k.parent.TrueCenter(), 8);
                return g;
            }
            return null;
        }

        private void Spent()
        {
            Map m = Map; IntVec3 c = Position;
            FleckMaker.ThrowLightningGlow(DrawPos, m, 1.5f);
            MagicSounds.Play("Arc", c, m, 0.6f, 0);
            Destroy();
            LightGlow.At(m, c, true, 2500);   // the light lingers a while after it is spent
        }

        /// <summary>Loose it: on purpose (it spares the colony's own) or broken loose (it spares no one).</summary>
        public void Loose(bool onPurpose)
        {
            Map m = Map; Vector3 at = DrawPos; float ch = charge;
            Pawn owner = onPurpose ? (maker != null && !maker.Dead ? maker : m.mapPawns.FreeColonistsSpawned.FirstOrDefault()) : null;
            Destroy();
            SecondFields f = m.GetComponent<SecondFields>();
            if (f == null) return;
            SecondFields.Globe g = f.AddGlobe(at, owner, 1f, 2500, null);
            g.charge = ch; g.sculpt = onPurpose && owner != null ? 1 : 0;
            if (!onPurpose) Messages.Message("A kept ball lightning has broken loose.", new TargetInfo(at.ToIntVec3(), m), MessageTypeDefOf.NegativeEvent, false);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            yield return new Command_Action
            {
                defaultLabel = "Loose it",
                defaultDesc = "Let the ball lightning go to guard the colony: it hunts within fifty cells and spares the colony's own. Its whole charge goes with it.",
                icon = ContentFinder<Texture2D>.Get("Things/Mote/Avir/Magic/Second/BallLightning", false) ?? BaseContent.BadTex,
                action = () => Loose(true)
            };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string st = "Charge: " + charge.ToString("0.00") + " (about " + Mathf.RoundToInt(charge * EnergyPerCharge) + " Wd); breaks loose at " + BreakLoose.ToString("0.0");
            return s.NullOrEmpty() ? st : s + "\n" + st;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Material m = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Second/BallLightning", ShaderDatabase.MoteGlow, Color.white);
            float t = Time.realtimeSinceStartup;
            float size = (0.8f + 0.1f * Mathf.Sin(t * 7f + phase)) * (0.6f + 0.3f * charge);
            Vector3 p = drawLoc + new Vector3(0f, 0f, 0.35f + 0.08f * Mathf.Sin(t * 2f + phase)); p.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p, Quaternion.AngleAxis((t * 120f + phase * 57f) % 360f, Vector3.up), new Vector3(size, 1f, size)), m, 0);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref charge, "charge", 1.5f);
            Scribe_Values.Look(ref warned, "warned");
            Scribe_References.Look(ref maker, "maker");
        }
    }
}
