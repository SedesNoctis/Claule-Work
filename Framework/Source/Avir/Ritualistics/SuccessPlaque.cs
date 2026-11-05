using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>"Success chance - N%" on its own plaque: in the ritual book and in the editor.</summary>
    public static class SuccessPlaque
    {
        public static float Height => 104f;

        private static Texture2D tex;
        private static bool looked;

        /// <summary>"Success chance - N%": the chosen conduit (else the steadiest colonist) against what the ritual asks.
        /// Arlie writes this plaque - and now and then she writes it wrong (the same ritual, the same answer, for a day).</summary>
        public static void Draw(Rect r, RitualSchema cur, CompRitualCircle circle)
        {
            RitualReading rd = RitualEvaluator.Read(cur);
            Map map = circle?.parent.Map ?? Find.CurrentMap;
            MapComponent_ExternalFlow ext = map != null ? MapComponent_ExternalFlow.Of(map) : null;
            float risk = rd.Risk(ext == null ? 1f : Mathf.Max(1f, ext.Total / MapComponent_ExternalFlow.Calm), 1f, 0f);
            Pawn who = circle?.chosenThing != null ? null
                : circle?.chosenPerformer ?? map?.mapPawns.FreeColonistsSpawned.ToList().Where(p => !p.Downed).OrderByDescending(p => GameComponent_AvirComposure.For(p, cur, circle)).FirstOrDefault();
            float comp = circle?.chosenThing != null ? CompRitualCircle.ConduitComposure : who != null ? GameComponent_AvirComposure.For(who, cur, circle) : 0.25f;
            // a backlash is tried every ten seconds of the ritual; about five of them in an ordinary one
            float chance = comp >= risk ? 1f : Mathf.Pow(Mathf.Clamp01(1f - (risk - comp) * 0.4f), 5f);
            int seed = Gen.HashCombineInt((cur.name ?? "").GetHashCode(), GenDate.DaysPassed);
            if (Arlia.On && Rand.ValueSeeded(seed) < 0.15f) chance = Mathf.Clamp01(1f - chance + Rand.ValueSeeded(seed + 3) * 0.2f);   // she lies
            Rect line = r;
            if (!looked) { looked = true; tex = ContentFinder<Texture2D>.Get("UI/Ritualistics/SuccessPlaque", false); }
            GUI.color = Color.white;
            if (tex != null)
            {
                // the scales at the ends and the crescent in the middle keep their shape; the line sits in the dark band
                float s = PlaqueArt.Draw(r, tex, new[] { 0f, 380f, 700f, 1240f, 1570f, 1952f }, new[] { false, true, false, true, false });
                line = new Rect(r.x + 330f * s, r.y + 215f * s, r.width - 660f * s, 210f * s);
            }
            else
            {
                Widgets.DrawBoxSolid(r, new Color(0.18f, 0.10f, 0.05f, 0.85f));
                GUI.color = FlowStyle.Gold; Widgets.DrawBox(r, 2); GUI.color = Color.white;
            }
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 0.92f, 0.75f);
            Widgets.Label(line, "Success chance - " + chance.ToStringPercent("0"));
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            TooltipHandler.TipRegion(r, "The chosen conduit (or your steadiest colonist) against the composure the ritual asks, at the stream as it is now. Written in " + Arlia.Name + "'s hand.");
        }

    }
}
