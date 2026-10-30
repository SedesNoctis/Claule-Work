using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>The cosmology of the book: the great aspects and the primitive ones.</summary>
    public enum RitualAspect { Light, Thought, Time, Space, Nature }

    /// <summary>
    /// ASPECTS. Every rune carries shares of the aspects (not one element - a leaning); the verbs weigh double, for the
    /// heart of a ritual is what it strives for. A ritual's colour is the blend of its aspects:
    ///   Light - white gold, the stars;  Thought - purple born of darkness;  Time - sand, the desert, time running to
    ///   its end;  Space - purple with a starry tint (light and thought together);  Nature and the primitive - fire,
    ///   earth, green.
    /// </summary>
    public static class RitualAspects
    {
        public static readonly Color[] Colors =
        {
            new Color(1.00f, 0.88f, 0.58f),   // Light: white gold
            new Color(0.50f, 0.16f, 0.80f),   // Thought: deep purple
            new Color(0.88f, 0.74f, 0.48f),   // Time: sand
            new Color(0.62f, 0.52f, 1.00f),   // Space: starry purple
            new Color(0.46f, 0.78f, 0.36f),   // Nature: green, earth, fire
        };

        public static readonly string[] Names = { "Light", "Thought", "Time", "Space", "Nature" };

        // shares: Light, Thought, Time, Space, Nature
        private static readonly Dictionary<RuneKind, float[]> Shares = new Dictionary<RuneKind, float[]>
        {
            { RuneKind.Fill,      new[] { 0.5f, 0.3f, 0f,   0f,   0.2f } },
            { RuneKind.Transfer,  new[] { 0f,   0.2f, 0.5f, 0.3f, 0f   } },
            { RuneKind.Purge,     new[] { 0.7f, 0f,   0f,   0f,   0.3f } },
            { RuneKind.Reforge,   new[] { 0.2f, 0.2f, 0.6f, 0f,   0f   } },
            { RuneKind.Infuse,    new[] { 0.3f, 0.3f, 0.4f, 0f,   0f   } },
            { RuneKind.Transmute, new[] { 0.2f, 0f,   0.3f, 0f,   0.5f } },
            { RuneKind.Release,   new[] { 0.6f, 0f,   0f,   0f,   0.4f } },
            { RuneKind.Hold,      new[] { 0f,   0.6f, 0.2f, 0.2f, 0f   } },
            { RuneKind.Drain,     new[] { 0f,   0.8f, 0.2f, 0f,   0f   } },
            { RuneKind.Lift,      new[] { 0f,   0.3f, 0f,   0.7f, 0f   } },
            { RuneKind.Growth,    new[] { 0.2f, 0f,   0f,   0f,   0.8f } },
            { RuneKind.Amplify,   new[] { 0.4f, 0.4f, 0f,   0.2f, 0f   } },
            { RuneKind.Reach,     new[] { 0.1f, 0f,   0f,   0.9f, 0f   } },
            { RuneKind.Breadth,   new[] { 0.3f, 0f,   0f,   0.7f, 0f   } },
            { RuneKind.Anchor,    new[] { 0f,   0f,   0.7f, 0.3f, 0f   } },
            { RuneKind.Safety,    new[] { 0.5f, 0f,   0.5f, 0f,   0f   } },
            { RuneKind.Closing,   new[] { 0f,   0.5f, 0.5f, 0f,   0f   } },
            { RuneKind.Focus,     new[] { 0f,   0.7f, 0f,   0.3f, 0f   } },
            { RuneKind.Barrier,   new[] { 0.7f, 0f,   0f,   0.3f, 0f   } },
            { RuneKind.Thread,    new[] { 0f,   0.4f, 0f,   0.6f, 0f   } },
            { RuneKind.Blood,     new[] { 0f,   0.3f, 0.2f, 0f,   0.5f } },
            { RuneKind.Shroud,    new[] { 0f,   0.4f, 0f,   0.6f, 0f   } },
            { RuneKind.Fire,      new[] { 0.3f, 0f,   0f,   0f,   0.7f } },
            { RuneKind.Water,     new[] { 0f,   0.1f, 0.1f, 0f,   0.8f } },
            { RuneKind.Earth,     new[] { 0f,   0f,   0.2f, 0f,   0.8f } },
            { RuneKind.Air,       new[] { 0f,   0f,   0f,   0.4f, 0.6f } },
            { RuneKind.Light,     new[] { 1f,   0f,   0f,   0f,   0f   } },
            { RuneKind.Time,      new[] { 0f,   0f,   1f,   0f,   0f   } },
            { RuneKind.Life,      new[] { 0.2f, 0f,   0f,   0f,   0.8f } },
            { RuneKind.Darkness,  new[] { 0f,   1f,   0f,   0f,   0f   } },
            { RuneKind.Space,     new[] { 0f,   0.2f, 0f,   0.8f, 0f   } },
            { RuneKind.Aura,      new[] { 0.3f, 0.2f, 0f,   0.3f, 0.2f } },
            { RuneKind.Enchant,   new[] { 0.3f, 0.3f, 0.2f, 0f,   0.2f } },
            { RuneKind.Step,      new[] { 0f,   0.2f, 0f,   0.8f, 0f   } },
            { RuneKind.Restore,   new[] { 0.3f, 0f,   0.4f, 0f,   0.3f } },
            { RuneKind.Beam,      new[] { 0.6f, 0f,   0f,   0.4f, 0f   } },
            { RuneKind.Shape,     new[] { 0f,   0.2f, 0.2f, 0.2f, 0.4f } },
            { RuneKind.Summon,    new[] { 0.2f, 0.4f, 0f,   0.2f, 0.2f } },
            { RuneKind.Sense,     new[] { 0.2f, 0.5f, 0.2f, 0.1f, 0f   } },
        };

        /// <summary>Normalised shares of the five aspects in a scheme.</summary>
        public static float[] Of(RitualSchema s)
        {
            var a = new float[5];
            if (s == null) return a;
            foreach (RuneDef r in s.AllRunes)
            {
                if (!Shares.TryGetValue(r.kind, out float[] sh)) continue;
                float w = r.role == RuneRole.Verb ? 2f : 1f;
                for (int i = 0; i < 5; i++) a[i] += sh[i] * w;
            }
            float sum = a.Sum();
            if (sum > 0f) for (int i = 0; i < 5; i++) a[i] /= sum;
            return a;
        }

        public static Color ColorOf(float[] a)
        {
            if (a == null || a.Sum() <= 0f) return Colors[1];
            Color c = new Color(0f, 0f, 0f, 1f);
            for (int i = 0; i < 5; i++) { c.r += Colors[i].r * a[i]; c.g += Colors[i].g * a[i]; c.b += Colors[i].b * a[i]; }
            return c;
        }

        /// <summary>A ritual's colour: with runes of the elements, their blend (the aspects only tint it); without, the aspects.</summary>
        public static Color ColorOfRitual(RitualSchema s)
        {
            Color asp = ColorOf(Of(s));
            List<RuneDef> el = s?.AllRunes.Where(r => r.role == RuneRole.Element).ToList();
            if (el == null || el.Count == 0) return asp;
            Color c = new Color(0f, 0f, 0f, 1f);
            foreach (RuneDef r in el) { c.r += r.color.r; c.g += r.color.g; c.b += r.color.b; }
            c.r /= el.Count; c.g /= el.Count; c.b /= el.Count;
            return Color.Lerp(c, asp, 0.2f);
        }

        public static string Describe(float[] a)
        {
            if (a == null || a.Sum() <= 0f) return null;
            return "Aspects: " + string.Join(", ", Enumerable.Range(0, 5).Where(i => a[i] >= 0.05f).OrderByDescending(i => a[i]).Select(i => Names[i] + " " + a[i].ToStringPercent("0")));
        }
    }
}
