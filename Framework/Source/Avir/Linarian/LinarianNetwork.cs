using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Avir.Linarian
{
    /// <summary>
    /// A stone of the Song. Every stone gives a tactic (what she does) and a gift (what she is). Four stones lie on
    /// the rim of their path's ring - the one facing the Song is the way in, the two at the sides touch the
    /// neighbouring rings, the outer one lies on the shadow's ring - and the fifth, the crown, in its middle.
    /// </summary>
    public class LinarianNode
    {
        public string key, area, label, active, passive;
        public string[] unlocks;
        public Vector2 pos;   // in the unit square of the network page

        public LinarianNode(string key, string label, string active, string passive, params string[] unlocks)
        {
            this.key = key; this.area = key.Substring(0, 1); this.label = label; this.active = active; this.passive = passive;
            this.unlocks = unlocks == null || unlocks.Length == 0 ? new[] { key } : unlocks;
        }

        public int Index => key[1] - '0';
        public bool Crown => Index == 5;
        public bool Shadow => area == "T";
        public string Desc => "Tactic: " + active + "\n\nGift: " + passive;
    }

    // =====================================================================================
    // THE SONG'S NETWORK. Illusion is the school every Herald sings in; the network is how she sings it. Eight
    // rings round the Song, each a path of five stones; neighbouring rings touch, so a path can be entered from the
    // side. Round them all, hidden until it is woken by ritual or by an arcane dependence, the shadow's ring: four
    // stones between the rings and its crown in the very middle, where the Song becomes the Flow. The outer stone
    // of every ring lies on the shadow's ring: once the shadow is open, it gives that path its dark side too.
    // =====================================================================================
    public static class LinarianNetwork
    {
        /// <summary>The rings, in their order round the Song (neighbours are kin).</summary>
        public static readonly string[] Areas = { "L", "D", "C", "W", "F", "S", "G", "P" };

        public static string AreaLabel(string a)
        {
            switch (a)
            {
                case "L": return "Lullaby";
                case "D": return "Dreaming";
                case "C": return "The cult";
                case "W": return "Will";
                case "F": return "The feast";
                case "S": return "Phantasmal pain";
                case "G": return "Guises";
                case "P": return "The pack";
                default: return "In the shadow of the Stream";
            }
        }

        /// <summary>What a path is about, in a line.</summary>
        public static string AreaSummary(string a)
        {
            switch (a)
            {
                case "L": return "Sleep in battle: foes fall asleep where they stand, her own rest easy.";
                case "D": return "The sleeping are hers: marked dreamers, sleepwalkers, the slow devouring.";
                case "C": return "Worship: minds that adore her, gather to her and fight for her.";
                case "W": return "The Call and the voice: foes come to her, flee, or turn on each other.";
                case "F": return "The collection of voices: what she devours stays with her.";
                case "S": return "The leash: phantasmal pain that binds everyone she has wounded.";
                case "G": return "Illusion in its purest: veils, mirages, the doe and the crow.";
                case "P": return "Beasts: the animals of the land and the predators of the wild fight for her.";
                default: return "The dark of the Stream: shadow steps, stolen years, the black fog and the eternal night. Once open, it binds her to the Stream: her aria grows with the Stream of the land, and her hunger runs out faster. Nothing on it asks a mind's will.";
            }
        }

        /// <summary>The dark side of each path, given by its outer stone once the shadow is open.</summary>
        public static string ShadowGift(string a)
        {
            switch (a)
            {
                case "L": return "Timelessness: those her heavy eyelids put under fall out of time - they can neither act nor be harmed.";
                case "D": return "Sleep longer than life: her slow devouring takes years as well - the victim ages with every night.";
                case "C": return "The black mass: her worshippers feed her hunger and her flow every night, without devouring.";
                case "W": return "Call through the dark: one called from a dark place falls through the shadow straight to her side.";
                case "F": return "Voices of the past: every voice taken makes her younger.";
                case "S": return "Sicknesses of the mind: her pain may bring dementia and worse on those who bear it.";
                case "G": return "The dark guise: in her beast's form, in the dark, nothing sees through her.";
                case "P": return "Beasts of the shadow: the predators she calls rise from the dark in twos and threes.";
                default: return null;
            }
        }

        public static readonly List<LinarianNode> Nodes = new List<LinarianNode>
        {
            // ---- Lullaby: sleep in battle
            new LinarianNode("L1", "heavy eyelids", "foes round a point grow drowsy, slow, and one by one fall asleep.", "those she puts under sleep half as long again.", "L4"),
            new LinarianNode("L2", "quiet aria", "her own around her rest at once.", "her own asleep in her zone sleep deep: noise does not wake them, they rest faster and heal."),
            new LinarianNode("L3", "calm the beast", "maddened beasts around her are calmed.", "beasts never turn on her; taming is easier near her."),
            new LinarianNode("L4", "lullaby", "foes around her fall asleep.", "around her, her own rest faster and are calmer.", "L1"),
            new LinarianNode("L5", "lullaby of the field", "over a wide field: drowsiness, sleep - and for some, a sleep with no waking.", "those woken from her sleep stay dull for a while."),
            // ---- Dreaming: the sleeping are hers
            new LinarianNode("D1", "into the dream", "marks a mind as hers: asleep, it walks under your hand.", "sleepers resist her aria less."),
            new LinarianNode("D2", "sleepwalking", "the sleepers round a point rise and fall on her foes.", "her sleepers cannot be woken: struck, they wake in a berserk rage."),
            new LinarianNode("D3", "slow devouring", "she drinks a sleeper night by night - up to three at once: on the tenth night it dies.", "every night of it feeds her hunger."),
            new LinarianNode("D4", "night procession", "the sleepers of one zone rise and walk to another, through walls and rock, and fall on the foes they find there.", "she sees every sleeper on the map, through walls, in violet."),
            new LinarianNode("D5", "sleeping retinue", "the sleepers of a zone follow her pointing until fully rested.", "while her retinue walks, pointing and into the dream cost her nothing."),
            // ---- The cult: worship
            new LinarianNode("C1", "sow adoration", "one mind - or a crowd - begins to worship her.", "her worshippers past the first stage are her cultists: they never harm her people and cannot leave."),
            new LinarianNode("C2", "gather the flock", "her cultists guard the edges of the land, then sleep.", "cultists guard her sleep, eat less, and eat anything."),
            new LinarianNode("C3", "ecstasy", "foes and cultists stand rapt in a ring; the cult grows.", "her cultists spread the cult."),
            new LinarianNode("C4", "last order", "a fervent cultist - or all of them - falls on the foe, then dies by their own hand.", "her cultists feel no pain."),
            new LinarianNode("C5", "conversion", "fervent worshippers among her foes turn to her side; if no one turns, the hunger is given back.", "worship grows faster; the cult spreads more readily."),
            // ---- Will: the Call and the voice
            new LinarianNode("W1", "the Call", "foes come to her slow and heavy-limbed, dropping weapons on the way; she devours them.", "those under her Call cannot harm her."),
            new LinarianNode("W2", "false order", "foes hear their commander: 'hold the position!' - and crowd round the point.", "she persuades and recruits better (mimicry)."),
            new LinarianNode("W3", "discord", "foes hear the voices of their own and turn on each other.", "minds resist her Call and her sleep less (the double voice)."),
            new LinarianNode("W4", "captivation", "a few foes fight for her for a while, feeling no pain.", "the captivated serve half as long again."),
            new LinarianNode("W5", "turn the raid", "a whole raid turns on itself.", "her Call reaches further and takes two more."),
            // ---- The feast: the collection of voices
            new LinarianNode("F1", "dead man's voice", "for a day she has the skill of one of her chosen voices (five uses a voice).", "every voice she has taken strengthens her aria - each new one a little less than the last, for a full choir is hard to lift. Yet every tenth voice lifts it anew: a great hunt is never wasted."),
            new LinarianNode("F2", "a sip", "free, up close: she drinks from a sleeping or captive mind without killing it; what overflows is stored.", "a quarter of what a kill overflows is stored."),
            new LinarianNode("F3", "release the store", "her store feeds her - or, poured out, becomes her aria's experience.", "her store holds 1200; the overflow pays for her chants."),
            new LinarianNode("F4", "a stranger's voice", "foes hear their commander order a retreat, drop their weapons and run - then come back ragged.", "the voices of leaders, psychics, Avir and Singers are worth more."),
            new LinarianNode("F5", "scream of the collection", "every voice screams at once: foes around her are stunned for long, the timid and the sensitive panic.", "every voice gives more."),
            // ---- Scars: the leash
            new LinarianNode("S1", "rend the pain", "she tears at her phantom scars - pain, not wounds: one, or everyone round a point.", "her blows leave phantom scars: the wound heals, the pain stays."),
            new LinarianNode("S2", "marked prey", "one who bears her scars is her prey - or, set to mercy, is freed of the pain.", "everyone who carries her scars glows red to her eyes."),
            new LinarianNode("S3", "terror", "pain swells and they scream; their own lose their nerve, and kin run to them.", "those under her terror yield to her more, the more it hurts."),
            new LinarianNode("S4", "the string", "those with deep scars try to cut the aching limbs away.", "the pain wanders: every few hours a scar creeps further up the body."),
            new LinarianNode("S5", "flaying", "every scarred limb looks flayed - to its owner, and to the weak of heart who see it.", "her mercy clears the failing mind; the scarred who break flee for the edge of the land, and fall there."),
            // ---- Guises: illusion in its purest
            new LinarianNode("G1", "veil", "she vanishes from sight; every blow she strikes from it may give her away.", "her veil lasts twice as long."),
            new LinarianNode("G2", "the doe and the crow", "the form of the doe, or of the crow (she flies).", "in a beast's form she is no one's target.", "Doe", "Crow"),
            new LinarianNode("G3", "mirage", "foes around her see false shapes.", "foes in her zone miss more - each by its own mind."),
            new LinarianNode("G4", "the switch", "a deep veil and a step elsewhere; where she stood, her mirage draws every eye.", "once a day, seen through, she is six of herself - and then veiled again."),
            new LinarianNode("G5", "veil of the pack", "her own around her vanish - though sharp minds may see through it.", "the veil and the forms cost nothing, she is seen through half as often, and her fallen are veiled where they lie."),
            // ---- The pack: beasts
            new LinarianNode("P1", "the hunt", "the beasts round her answer and wait; pointed, they go, fall on her foes and circle the place.", "the beasts of the land never fear her."),
            new LinarianNode("P2", "aria-hunt", "a whole herd walks to her home and drops there, marked for the hunters.", "beasts marked for hunting come to her of their own accord, one at a time."),
            new LinarianNode("P3", "form of the she-wolf", "the form of the she-wolf.", "beasts fighting near her wear their prey down, and she learns from every bite.", "Wolf"),
            new LinarianNode("P4", "call a predator", "beasts of the wild come to fight for her - more and stronger, the stronger she is.", "she draws beasts to a land that runs short of them."),
            new LinarianNode("P5", "beasts against the raid", "most of the land's beasts fall on her foes; sung again within the hour, the rest - the great beasts too.", "her beasts feel a fifth of their pain."),
            // ---- In the shadow of the Flow
            new LinarianNode("T1", "step into shadow", "she steps into any dark place of the map.", "the dark is open to her eyes; stepping out of it, her other arts may sing at once."),
            new LinarianNode("T2", "theft of years", "foes in her zone age; every ten years heal her, then give her one back.", "she can set her own age aside, and take it back."),
            new LinarianNode("T3", "distortion of time", "the moving are thrown back eight seconds, every ten; her own quicken.", "in her zone foes' minds fail, and now and then they are thrown back hours."),
            new LinarianNode("T4", "fog and flare", "a black fog rises around her - or a flare kills every light and machine until dawn.", "while she is hostile, the fog rises by day and the lights die every night."),
            new LinarianNode("T5", "eternal night", "for three days the land believes it is night. Plants bloom on; only minds are fooled.", "by night her aria is stronger."),
        };

        /// <summary>The crown of a ring opens with three of its rim open; the shadow's crown, with three of its four.</summary>
        public const int CrownNeeds = 3;

        private static Dictionary<string, LinarianNode> byKey;
        public static LinarianNode Get(string key)
        {
            if (byKey == null) byKey = Nodes.ToDictionary(n => n.key);
            return key != null && byKey.TryGetValue(key, out LinarianNode n) ? n : null;
        }

        public static LinarianNode ForTactic(string t) => Nodes.FirstOrDefault(n => n.unlocks.Contains(t));

        /// <summary>Stones joined to this one: along its ring, the side stones to the neighbouring rings, the outer
        /// stones to the shadow's ring.</summary>
        public static IEnumerable<string> Neighbours(string key)
        {
            LinarianNode n = Get(key);
            if (n == null) yield break;
            if (n.Shadow)
            {
                int i = n.Index;
                if (i <= 4)
                {
                    if (i > 1) yield return "T" + (i - 1);
                    if (i < 4) yield return "T" + (i + 1);
                    // each shadow stone sits between two pairs of rings
                    for (int r = 0; r < 2; r++) yield return Areas[((i - 1) * 2 + r) % 8] + "4";
                }
                yield break;
            }
            int a = System.Array.IndexOf(Areas, n.area);
            switch (n.Index)
            {
                case 1: yield return n.area + "2"; yield return n.area + "3"; break;
                case 2: yield return n.area + "1"; yield return n.area + "4"; yield return Areas[(a + 7) % 8] + "3"; break;
                case 3: yield return n.area + "1"; yield return n.area + "4"; yield return Areas[(a + 1) % 8] + "2"; break;
                case 4:
                    yield return n.area + "2"; yield return n.area + "3";
                    yield return "T" + (a / 2 + 1);
                    break;
            }
        }

        public static string CannotOpen(Gene_LinarianSong g, LinarianNode n)
        {
            if (g.Has(n.key)) return "already lit";
            if (n.Shadow && !g.shadow) return "hidden";
            if (g.PointsFree <= 0) return "no free point - the Aria must deepen first";
            if (n.Crown)
            {
                int open = Enumerable.Range(1, 4).Count(i => g.Has(n.area + i));
                return open >= CrownNeeds ? null : "the crown wakes when " + CrownNeeds + " stars of its ring are lit";
            }
            if (n.Index == 1) return null;
            if (!Neighbours(n.key).Any(g.Has)) return "light a star joined to it first";
            return null;
        }

        // ---------------------------------------------------------------- the layout

        public const float RingCentre = 0.34f, RingRadius = 0.085f, ShadowRadius = RingCentre + RingRadius;

        public static Vector2 RingCentreOf(int a)
        {
            float ang = a * Mathf.PI / 4f - Mathf.PI / 2f;
            return new Vector2(0.5f + Mathf.Cos(ang) * RingCentre, 0.5f + Mathf.Sin(ang) * RingCentre);
        }

        static LinarianNetwork()
        {
            for (int a = 0; a < Areas.Length; a++)
            {
                float ang = a * Mathf.PI / 4f - Mathf.PI / 2f;
                Vector2 c = RingCentreOf(a);
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 side = new Vector2(-dir.y, dir.x);
                foreach (LinarianNode n in Nodes.Where(n => n.area == Areas[a]))
                {
                    switch (n.Index)
                    {
                        case 1: n.pos = c - dir * RingRadius; break;
                        case 2: n.pos = c - side * RingRadius; break;
                        case 3: n.pos = c + side * RingRadius; break;
                        case 4: n.pos = c + dir * RingRadius; break;
                        default: n.pos = c; break;
                    }
                }
            }
            LinarianTexts.Apply(Nodes);
            foreach (LinarianNode n in Nodes.Where(n => n.Shadow))
            {
                if (n.Crown) { n.pos = new Vector2(0.5f, 0.5f); continue; }
                // between the rings 2i-1 and 2i (the gap after each pair)
                float ang = ((n.Index - 1) * 2 + 0.5f) * Mathf.PI / 4f - Mathf.PI / 2f;
                n.pos = new Vector2(0.5f + Mathf.Cos(ang) * ShadowRadius, 0.5f + Mathf.Sin(ang) * ShadowRadius);
            }
        }
    }
}
