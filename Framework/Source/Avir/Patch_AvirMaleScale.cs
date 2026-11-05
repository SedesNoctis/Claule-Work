using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// The Avir build: taller and leaner than people, a clean vertical silhouette rather than a bigger
    /// block. The whole pawn is stretched at the root matrix every render node is composed from, so
    /// body, gear and attachments move together; then the parts that must not distort are corrected
    /// node by node:
    ///   - the head (and everything on it - hair, eyes, headgear) is kept in true proportion;
    ///   - the wings are kept in true proportion and sit a touch higher;
    ///   - the tail is drawn longer.
    /// Portraits get the same proportions normalised to human height, so the figure still fits its frame.
    /// </summary>
    public static class AvirBuild
    {
        public struct Shape
        {
            public float width, height;   // the whole pawn
            public float head;            // the head's own, even, scale
            public float wings;           // the wings' own, even, scale
            public float tail;            // the tail's length factor
            public float wingLift;        // how much higher the wings sit
        }

        public static readonly Shape Male   = new Shape { width = 1.15f, height = 1.30f, head = 1.25f, wings = 1.22f, tail = 1.20f, wingLift = 0.05f };
        public static readonly Shape Female = new Shape { width = 1.00f, height = 1.10f, head = 1.00f, wings = 1.05f, tail = 1.12f, wingLift = 0.03f };
        /// <summary>A Herald: slimmer and a little shorter than an Avir woman - only a little taller and thinner than a human.</summary>
        public static readonly Shape Herald = new Shape { width = 0.90f, height = 1.04f, head = 0.97f, wings = 1.12f, tail = 1.12f, wingLift = 0.03f };

        public const float TailThin = 0.88f;   // how much thinner the tail is drawn

        public static bool For(Pawn p, bool portrait, out Shape s)
        {
            s = default;
            if (p == null || p.DevelopmentalStage != DevelopmentalStage.Adult) return false;
            if (p.gender != Gender.Male && p.gender != Gender.Female) return false;
            if (!AvirBody.IsAvir(p)) return false;
            s = p.gender == Gender.Male ? Male : Linarian.Linarian.Is(p) ? Herald : Female;
            if (portrait)
            {
                float k = 1f / s.height;
                s.width *= k; s.height = 1f; s.head *= k; s.wings *= k;
            }
            return true;
        }

        public static bool Portrait(PawnDrawParms parms) => (parms.flags & PawnRenderFlags.Portrait) != 0;
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    public static class Patch_AvirMaleScale
    {
        public static void Postfix(ref PawnDrawParms __result)
        {
            if (!AvirBuild.For(__result.pawn, AvirBuild.Portrait(__result), out var s)) return;
            if (s.width == 1f && s.height == 1f) return;
            __result.matrix *= Matrix4x4.Scale(new Vector3(s.width, 1f, s.height));
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode), nameof(PawnRenderNode.GetTransform))]
    public static class Patch_AvirBuildParts
    {
        public static void Postfix(PawnRenderNode __instance, PawnDrawParms parms, ref Vector3 offset, ref Vector3 scale)
        {
            if (!AvirBuild.For(parms.pawn, AvirBuild.Portrait(parms), out var s)) return;
            if (__instance.Props?.tagDef == PawnRenderNodeTagDefOf.Head)
            {
                scale.x *= s.head / s.width;
                scale.z *= s.head / s.height;
                return;
            }
            string gene = __instance.gene?.def?.defName;
            if (gene == "Flight_Gene")
            {
                scale.x *= s.wings / s.width;
                scale.z *= s.wings / s.height;
                offset.z += s.wingLift;
            }
            else if (gene == "Avir_GriffinTail")
            {
                // longer along its length, a touch thinner across it: seen from the side the tail runs
                // left-right, so it thins top to bottom; from front or back it curls up, so it thins sideways
                bool side = parms.facing == Rot4.East || parms.facing == Rot4.West;
                scale.x *= s.tail * (side ? 1f : AvirBuild.TailThin);
                scale.z *= s.tail * (side ? AvirBuild.TailThin : 1f);
                offset.x *= s.tail;   // grows outward from the body, not into it
                offset.z *= s.tail;
            }
        }
    }
}
