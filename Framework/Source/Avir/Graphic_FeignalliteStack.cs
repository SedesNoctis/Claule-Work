using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Custom graphic class replacing vanilla Graphic_StackCount.
    ///
    /// WHY. Graphic_StackCount is a compiled vanilla class with no configurable thresholds:
    /// the stackCount-to-texture mapping lives inside Assembly-CSharp.dll and can't be driven
    /// from XML or from file naming. In practice it produced non-monotonic results (a stack of
    /// 2 showing the jar while a stack of 43 showed the single stone), and it can't be bent to
    /// specific ranges at all - it only distinguishes "exactly 1" and "exactly stackLimit",
    /// lumping everything between into one bucket.
    ///
    /// Here the thresholds are explicit and readable at a glance. Neither vanilla's logic nor
    /// mods that rewrite stackLimit at runtime (StackXXL and friends) affect them.
    ///
    /// FILES: exactly three, in the folder given by texPath, in alphabetical order:
    ///   _a -> 1-25   (single stone)
    ///   _b -> 26-50  (jar, partly filled)
    ///   _c -> 51+    (jar, packed full)
    /// There must be NO unsuffixed base file - it would join the collection as a fourth entry
    /// and shift every index.
    /// </summary>
    public class Graphic_FeignalliteStack : Graphic_Collection
    {
        public override Material MatSingle
        {
            get { return subGraphics[subGraphics.Length - 1].MatSingle; }
        }

        /// <summary>Thresholds. The only place that needs editing to retune them.</summary>
        private Graphic SubGraphicForCount(int stackCount)
        {
            int index;
            if (stackCount <= 25) index = 0;
            else if (stackCount <= 50) index = 1;
            else index = 2;

            return subGraphics[Mathf.Clamp(index, 0, subGraphics.Length - 1)];
        }

        private Graphic SubGraphicFor(Thing thing)
        {
            if (thing == null) return subGraphics[0];
            return SubGraphicForCount(thing.stackCount);
        }

        public override Material MatSingleFor(Thing thing)
        {
            if (thing == null) return MatSingle;
            return SubGraphicFor(thing).MatSingle;
        }

        public override Material MatAt(Rot4 rot, Thing thing = null)
        {
            if (thing == null) return MatSingle;
            return MatSingleFor(thing);
        }

        public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing,
            float extraRotation)
        {
            SubGraphicFor(thing).DrawWorker(loc, rot, thingDef, thing, extraRotation);
        }

        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            SubGraphicFor(thing).Print(layer, thing, extraRotation);
        }

        public override string ToString()
        {
            return "Graphic_FeignalliteStack(path=" + path + ", count=" + subGraphics.Length + ")";
        }
    }
}
