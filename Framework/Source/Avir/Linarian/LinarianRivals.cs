using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian
{
    // =====================================================================================
    // SINGER AGAINST SINGER, AND WHAT HER ZONE SEES.
    //   The stronger aria (by more than a twentieth) rules where they meet; equal ones leave each other be.
    //   The player's Singer, the stronger: another Singer in her zone cannot keep a beast's shape - she walks in her
    //   own until she leaves the zone. A wild one takes no offence; a Primal one is provoked.
    //   A stronger Singer of another side: a weaker one in her zone sings at half strength whatever the stronger also
    //   knows; and the stronger's zone breaks the weaker's songs of that kind on anyone in it.
    //   In any Singer's zone the unseen of the Anomaly (sightstealers, revenants...) cannot stay unseen.
    //   The player's Singer sees the sleepers of the land (Dreaming 4) as violet shapes through walls, and those who
    //   carry her scars (Phantasmal pain 2) as red ones.
    // =====================================================================================
    public static class LinarianRivals
    {
        private const float Edge = 1.05f;
        private static readonly Dictionary<int, int> unmasked = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> provoked = new Dictionary<int, int>();

        /// <summary>The tactic being sung right now (to know, later, which art a state of hers came from).</summary>
        public static string singing;

        private static bool Apart(Pawn a, Pawn b) => a.Faction == null || b.Faction == null || a.Faction != b.Faction;

        /// <summary>A stronger Singer of another side who knows this stone, with this place in her zone.</summary>
        public static Pawn Stronger(Pawn me, Gene_LinarianSong mg, string node, IntVec3 at)
        {
            if (me?.MapHeld == null || mg == null || node == null) return null;
            float mine = mg.Power;
            foreach (Pawn o in MapComponent_LinarianSong.HeraldsOn(me.MapHeld))
            {
                if (o == me || o.Dead || !o.Spawned || !Apart(me, o)) continue;
                Gene_LinarianSong og = Linarian.Song(o);
                if (og == null || !og.Has(node) || og.Power <= mine * Edge) continue;
                if (LinarianZone.InZone(o, og, at)) return o;
            }
            return null;
        }

        /// <summary>Half strength under a stronger rival who knows the same art.</summary>
        public static float Damp(Pawn me, Gene_LinarianSong mg, string t)
        {
            LinarianNode n = LinarianNetwork.ForTactic(t);
            if (n == null || !me.Spawned) return 1f;
            Pawn o = Stronger(me, mg, n.key, me.Position);
            if (o == null) return 1f;
            if (me.Faction == Faction.OfPlayer) Messages.Message(me.LabelShortCap + "'s song is muffled: " + o.LabelShort + " sings it louder here.", me, MessageTypeDefOf.NeutralEvent, false);
            return 0.5f;
        }

        public static bool Unmasked(Pawn p) =>
            p != null && unmasked.TryGetValue(p.thingIDNumber, out int t) && Find.TickManager.TicksGame - t < 180;

        public static void Tick(Map map, int now)
        {
            if (now % 60 != 0) return;
            List<Pawn> heralds = MapComponent_LinarianSong.HeraldsOn(map).Where(h => !h.Dead && h.Spawned).ToList();
            if (heralds.Count == 0) return;
            foreach (Pawn her in heralds)
            {
                Gene_LinarianSong g = Linarian.Song(her);
                if (g == null) continue;
                Unseen(her, g);
                if (her.Faction == Faction.OfPlayer) continue;
                // a stronger Singer of the player has her in her zone: no beast's shape
                Pawn p = heralds.FirstOrDefault(x => x.Faction == Faction.OfPlayer && Linarian.Song(x) is Gene_LinarianSong pg
                    && pg.Power > g.Power * Edge && LinarianZone.InZone(x, pg, her.Position));
                if (p == null) continue;
                unmasked[her.thingIDNumber] = now;
                if (LinarianVeil.InForm(her) != null) LinarianVeil.ClearForm(her);
                HeraldRecord r = LinarianPresence.Record(her);
                if (r != null && r.primal && (!provoked.TryGetValue(her.thingIDNumber, out int t) || now - t > GenDate.TicksPerDay))
                {
                    provoked[her.thingIDNumber] = now;
                    LinarianPresence.OnRevealed(her, p);   // the Primal one does not bear it
                }
            }
            if (now % 240 == 0) Suppress(map);
        }

        /// <summary>The unseen of the Anomaly cannot stay unseen in a Singer's zone (her own veils excepted).</summary>
        private static void Unseen(Pawn her, Gene_LinarianSong g)
        {
            foreach (Pawn q in LinarianZone.InHerZone(her, g).ToList())
            {
                if (q == her || q.Dead || LinarianVeil.Veiled(q) || her.Faction != null && q.Faction == her.Faction) continue;
                HediffComp_Invisibility inv = q.GetInvisibilityComp();
                if (inv != null) inv.DisruptInvisibility();
            }
        }

        /// <summary>A stronger rival's zone breaks the weaker's songs of the same art on anyone in it.</summary>
        private static void Suppress(Map map)
        {
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!(q.MentalState is MentalState_Song s) || s.herald == null || s.tactic == null) continue;
                Gene_LinarianSong hg = Linarian.Song(s.herald);
                LinarianNode n = LinarianNetwork.ForTactic(s.tactic);
                if (hg == null || n == null) continue;
                Pawn o = Stronger(s.herald, hg, n.key, q.Position);
                if (o == null) continue;
                s.RecoverFromState();
                FleckMaker.ThrowSmoke(q.DrawPos, map, 1f);
                if (s.herald.Faction == Faction.OfPlayer || o.Faction == Faction.OfPlayer)
                    Messages.Message(o.LabelShortCap + "'s aria breaks " + s.herald.LabelShort + "'s hold on " + q.LabelShort + ".", q, MessageTypeDefOf.NeutralEvent, false);
            }
        }

        // ---------------------------------------------------------------- shapes through walls

        public static readonly Color Violet = new Color(0.72f, 0.42f, 1f, 0.85f);
        public static readonly Color Red = new Color(1f, 0.2f, 0.2f, 0.85f);

        private static Texture2D glowTex;
        private static readonly Dictionary<Color, Material> glows = new Dictionary<Color, Material>();

        /// <summary>A soft round glow, made once in memory (no file needed).</summary>
        private static Texture2D GlowTex
        {
            get
            {
                if (glowTex != null) return glowTex;
                const int n = 64;
                glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < n; x++)
                    for (int y = 0; y < n; y++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f - 0.5f, n / 2f - 0.5f)) / (n / 2f);
                        float a = Mathf.Clamp01(1f - d);
                        glowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                glowTex.Apply();
                return glowTex;
            }
        }

        /// <summary>A soft round glow of colour, drawn above walls and roofs.</summary>
        public static void Glow(Vector3 at, float size, Color c)
        {
            if (!glows.TryGetValue(c, out Material m))
            {
                m = MaterialPool.MatFrom(new MaterialRequest(GlowTex, ShaderDatabase.MoteGlow, c));
                glows[c] = m;
            }
            at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(size, 1f, size)), m, 0);
        }

        /// <summary>A glow of colour round the pawn, seen through walls and roofs - pulsing softly.</summary>
        public static void Silhouette(Pawn q, Color c)
        {
            try
            {
                if (!glows.TryGetValue(c, out Material m))
                {
                    m = MaterialPool.MatFrom(new MaterialRequest(GlowTex, ShaderDatabase.MoteGlow, c));
                    glows[c] = m;
                }
                float pulse = 1f + 0.08f * Mathf.Sin(Time.realtimeSinceStartup * 3f + q.thingIDNumber);
                Vector3 at = q.DrawPos;
                at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                float s = 2.4f * pulse;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(s, 1f, s)), m, 0);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at + new Vector3(0f, 0.001f, 0f), Quaternion.identity, new Vector3(s * 0.55f, 1f, s * 0.75f)), m, 0);
            }
            catch { GenDraw.DrawCircleOutline(q.DrawPos, 0.75f, SimpleColor.Magenta); }
        }
    }
}
