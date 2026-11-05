using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE AVIR BODY (Defs/ThingDefs_Races/Race_Avir.xml). It inherits the human body; XML inheritance can
    /// add parts but never take one away or rename one, so the rest is done here, once, at startup:
    ///   - the jaw becomes the BEAK and takes the nose's place (the nose is gone; the beak covers both).
    ///     It stays a jaw underneath - eating, talking, the essentium beak and any jaw implant work on it;
    ///   - every hand gets its CLAWS and every foot its TALONS, parts of their own where the essentium
    ///     claws sit (they are never cut off: they chip and heal).
    /// The tail and the wings are plain parts appended in XML.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirBody
    {
        public static readonly BodyDef Body = DefDatabase<BodyDef>.GetNamedSilentFail("Avir_Body");
        /// <summary>The calm harpy's body (Race_AvirHarpy.xml): wings, tail and claws, but a human face and hands.</summary>
        public static readonly BodyDef HarpyBody = DefDatabase<BodyDef>.GetNamedSilentFail("Avir_HarpyBody");
        public static readonly BodyPartDef Claw = DefDatabase<BodyPartDef>.GetNamedSilentFail("Avir_Claw");
        public static readonly BodyPartDef Talon = DefDatabase<BodyPartDef>.GetNamedSilentFail("Avir_Talon");
        public static readonly BodyPartDef Tail = DefDatabase<BodyPartDef>.GetNamedSilentFail("Avir_Tail");
        public static readonly BodyPartDef Tongue = DefDatabase<BodyPartDef>.GetNamedSilentFail("Avir_Tongue");

        static AvirBody()
        {
            if (Body?.corePart == null) return;
            try
            {
                Build(Body);
                if (HarpyBody?.corePart != null) Build(HarpyBody, beakAndPaws: false);
                LeanMeat();
            }
            catch (System.Exception e) { Log.Error("[Avir] could not shape the Avir body: " + e); }
        }

        private static void Build(BodyDef body, bool beakAndPaws = true)
        {
            List<BodyPartRecord> all = body.AllParts.ToList();
            // the beak: jaw and nose in one
            foreach (BodyPartRecord jaw in all.Where(r => beakAndPaws && r.def.defName == "Jaw"))
            {
                BodyPartRecord nose = jaw.parent?.parts.FirstOrDefault(r => r.def.defName == "Nose");
                if (nose != null)
                {
                    jaw.coverage += nose.coverage;
                    jaw.parent.parts.Remove(nose);
                }
                jaw.customLabel = jaw.untranslatedCustomLabel = "beak";
                // the tongue lies in the beak: the beak lost, the tongue is lost; the tongue lost, the beak stays
                if (Tongue != null && !jaw.parts.Any(p => p.def == Tongue))
                    jaw.parts.Add(new BodyPartRecord
                    {
                        body = body, def = Tongue, parent = jaw, coverage = 0.1f, depth = BodyPartDepth.Inside, height = jaw.height,
                        groups = jaw.groups != null ? new List<BodyPartGroupDef>(jaw.groups) : new List<BodyPartGroupDef>(),
                        parts = new List<BodyPartRecord>()
                    });
            }
            // claws on the hands, talons on the feet
            foreach (BodyPartRecord r in all)
            {
                BodyPartDef add = r.def.defName == "Hand" ? Claw : r.def.defName == "Foot" ? Talon : null;
                if (add == null || r.parts.Any(p => p.def == add)) continue;
                string side = (r.customLabel ?? "").Split(' ').FirstOrDefault(w => w == "left" || w == "right");
                r.parts.Add(new BodyPartRecord
                {
                    body = body,
                    def = add,
                    parent = r,
                    coverage = 0.08f,
                    depth = BodyPartDepth.Outside,
                    height = r.height,
                    groups = r.groups != null ? new List<BodyPartGroupDef>(r.groups) : new List<BodyPartGroupDef>(),
                    customLabel = ClawLabel(side, add),
                    untranslatedCustomLabel = ClawLabel(side, add),
                    parts = new List<BodyPartRecord>()
                });
            }
            // paws: four digits each - the smallest finger and toe are gone - and named as paws
            foreach (BodyPartRecord r in all)
            {
                if (!beakAndPaws) break;
                bool hand = r.def.defName == "Hand", foot = r.def.defName == "Foot";
                if (!hand && !foot) continue;
                List<BodyPartRecord> digits = r.parts.Where(p => p.def.defName == (hand ? "Finger" : "Toe")).ToList();
                if (digits.Count == 5)
                {
                    BodyPartRecord least = digits.FirstOrDefault(p => (p.customLabel ?? "").Contains("pinky") || (p.customLabel ?? "").Contains("little")) ?? digits.Last();
                    r.parts.Remove(least);
                }
                string side = Side(r);
                string paw = (side != null ? side + " " : "") + (hand ? "front paw" : "hind paw");
                r.customLabel = r.untranslatedCustomLabel = paw;
            }
            // a capitalised label cached before the renaming would keep showing "Jaw" or "Left hand"
            var capCache = AccessTools.Field(typeof(BodyPartRecord), "cachedCustomLabelCap");
            if (capCache != null) foreach (BodyPartRecord r in body.AllParts) capCache.SetValue(r, null);
            body.ClearCachedData();
            body.ResolveReferences();
        }

        private static string Side(BodyPartRecord r) => (r.customLabel ?? "").Split(' ').FirstOrDefault(w => w == "left" || w == "right");

        private static string ClawLabel(string side, BodyPartDef def) =>
            (side != null ? side + " " : "") + (def == Talon ? "hind claws" : "front claws");

        /// <summary>Avir meat: lean - nothing they eat goes to fat.</summary>
        private static void LeanMeat()
        {
            ThingDef race = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Race");
            ThingDef meat = race?.race?.meatDef;
            if (meat == null) return;
            meat.description = "Meat of an Avir: dark, dense and lean, without a trace of fat - everything an Avir eats goes to its growth, its metabolism and the rest of its demanding body. "
                               + "Most people would regard eating it with horror; the Avir do not.";
        }

        public static bool IsAvirBody(Pawn p) => Body != null && p?.RaceProps?.body == Body;

        public static bool IsHarpyBody(Pawn p) => HarpyBody != null && p?.RaceProps?.body == HarpyBody;

        private static readonly HashSet<string> AvirXenotypes = new HashSet<string> { "Avir", "Avir_HarpyWild", "Avir_HarpyCalm", "Avir_Linarian" };

        public static bool IsAvirXenotype(XenotypeDef x) => x != null && AvirXenotypes.Contains(x.defName);

        /// <summary>An Avir by body (the Avir race) or by xenotype (one made before the race existed, or on a human).</summary>
        public static bool IsAvir(Pawn p) => IsAvirBody(p) || IsAvirXenotype(p?.genes?.Xenotype);

        /// <summary>The essentium claw of this hand or talon of this foot sits on its claw part when the body has one.</summary>
        public static BodyPartRecord ClawSeat(BodyPartRecord limb)
        {
            if (limb?.parts == null) return limb;
            BodyPartDef want = limb.def.defName == "Hand" ? Claw : limb.def.defName == "Foot" ? Talon : null;
            return want == null ? limb : limb.parts.FirstOrDefault(p => p.def == want) ?? limb;
        }
    }

    // =====================================================================================
    // ONE WING DRAWN WHEN ONE IS LOST. The wing graphic shows both wings; seen from behind or the front
    // only the half of the living wing is drawn (a half mesh), from the side the remaining wing still
    // shows. Both lost - nothing. The tail is not drawn once it is lost.
    // =====================================================================================
    [HarmonyPatch(typeof(PawnRenderNode), nameof(PawnRenderNode.GetMesh))]
    public static class Patch_AvirWingHalves
    {
        private static readonly Dictionary<(Mesh, bool), Mesh> halves = new Dictionary<(Mesh, bool), Mesh>();

        public static void Postfix(PawnRenderNode __instance, PawnDrawParms parms, ref Mesh __result)
        {
            if (__result == null || __instance.gene?.def?.defName != "Flight_Gene" || parms.pawn == null) return;
            if (!parms.facing.IsHorizontal)
            {
                AvirWingSides.Of(parms.pawn, out bool left, out bool right);
                if (left == right) return;   // both (drawn whole) or none (not drawn at all)
                // from the front the pawn's left wing is on our right; from behind, on our left
                bool keepScreenLeft = parms.facing == Rot4.North ? left : right;
                __result = Half(__result, keepScreenLeft);
            }
        }

        private static Mesh Half(Mesh src, bool keepLeft)
        {
            if (halves.TryGetValue((src, keepLeft), out Mesh m)) return m;
            Vector3[] v = src.vertices;
            Vector2[] uv = src.uv;
            float midU = 0f;
            for (int i = 0; i < uv.Length; i++) midU += uv[i].x;
            midU /= Mathf.Max(1, uv.Length);
            for (int i = 0; i < v.Length; i++)
            {
                if (keepLeft ? v[i].x > 0f : v[i].x < 0f)
                {
                    v[i].x = 0f;
                    uv[i].x = midU;
                }
            }
            m = new Mesh { name = src.name + (keepLeft ? "_avirLeftHalf" : "_avirRightHalf"), vertices = v, uv = uv, triangles = src.triangles };
            m.RecalculateNormals();
            m.RecalculateBounds();
            halves[(src, keepLeft)] = m;
            return m;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.CanDrawNow))]
    public static class Patch_AvirLostPartsNotDrawn
    {
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            if (!__result || node?.gene == null || parms.pawn?.health?.hediffSet == null) return;
            string g = node.gene.def.defName;
            if (g == "Flight_Gene")
            {
                AvirWingSides.Of(parms.pawn, out bool left, out bool right);
                if (!left && !right && AvirWingParts.WingParts(parms.pawn).Any()) __result = false;
            }
            else if (g == "Avir_GriffinTail" && AvirBody.Tail != null)
            {
                BodyPartRecord tail = parms.pawn.RaceProps.body.AllParts.FirstOrDefault(r => r.def == AvirBody.Tail);
                if (tail != null && parms.pawn.health.hediffSet.PartIsMissing(tail)) __result = false;
            }
        }
    }

    public static class AvirWingSides
    {
        /// <summary>Which wings this pawn still has (true for a body without wing parts - drawn as ever).</summary>
        public static void Of(Pawn p, out bool left, out bool right)
        {
            left = right = true;
            var wings = AvirWingParts.WingParts(p).ToList();
            if (wings.Count == 0 || p.health?.hediffSet == null) return;
            foreach (BodyPartRecord w in wings)
            {
                bool there = !p.health.hediffSet.PartIsMissing(w);
                if ((w.customLabel ?? "").StartsWith("left")) left = there;
                else right = there;
            }
        }
    }
}
