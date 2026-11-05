using System.Collections.Generic;

namespace Avir.Linarian
{
    /// <summary>The words of the network: for every star a line in a Singer's own voice, then plainly what the tactic does and what the
    /// gift gives. A "sleeper" is anyone asleep - in a bed, or put under by her lullaby.</summary>
    public static class LinarianTexts
    {
        public static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>
        {
            // { lore, tactic, gift }
            // ---- Lullaby
            { "L1", new[] { "I don't even need to sing. A breath, a hum - and your eyelids are already too heavy to argue with me.",
                "Foes round a point in her zone grow drowsy, each by chance against their mind: they slow down, and wherever they go they keep nodding off, one by one, into her sleep. The drowsiness goes with them - it is not tied to the place.",
                "Those she puts to sleep sleep half as long again." } },
            { "L2", new[] { "Mine deserve their rest. Sleep, little ones; I am awake, and nothing in this night is more dangerous than me.",
                "Her own people everywhere in her zone regain much of their rest, rest easier for a while, and anyone among them in a break that is not a rage comes back to themselves.",
                "Her own people asleep anywhere in her zone sleep deep: noise does not wake them, they rest half again as fast, and their wounds close as they sleep." } },
            { "L3", new[] { "It isn't hard for me to conquer the mind of the beast, really. They are just as simple as anything beyond my kind.",
                "Maddened beasts round a point in her zone calm down, and now and then one stays with her. Beasts sick with scaria cannot be calmed: they run off the map instead.",
                "Beasts never turn on her, and taming is easier near her." } },
            { "L4", new[] { "Hush now. My mother sang this over my egg, and now I sing it over you. You should feel honoured - few ever hear it twice.",
                "Foes round a point she chooses in her zone fall into a heavy, true sleep - all their weariness at once, people, beasts and insects alike, as many as her power takes, each by chance against their mind; a blow wakes them. A raid whose sleepers wake goes on with its raid. With the box ticked, her own there fall asleep too, their beasts with them (not the drafted). Sung over those in the endless sleep, it lets them go.",
                "Near her, her own rest faster and stay calmer." } },
            { "L5", new[] { "A whole battlefield, quiet as a nursery. Armies are only children who stayed up too late.",
                "Over a wide field round a point in her zone, each foe by chance against their mind: the most yielding fall asleep where they stand, others grow drowsy (with heavy eyelids, three times as yielding to it), and a few sink into a sleep with no waking - until they starve, or her lullaby is sung over them again.",
                "Those woken from her sleep stay dull for a while." } },
            // ---- Dreaming
            { "D1", new[] { "Asleep, your mind leaves its door wide open. Rude of me to walk in uninvited? Perhaps. But I do love what you've done with the place.",
                "A mind in her zone (not her own people) dreams of her for ten days. Awake, it is what it was; each time it falls asleep it rises - speechless, still resting, at four-fifths of its speed - and walks under your hand until a blow or a full rest wakes it, then goes back to its own (and its raid). While it walks no one turns on it, unless you make it attack. By chance against its mind, a sleep may go another way: it lies awake, wretched and useless to anyone.",
                "Sleepers resist her aria less." } },
            { "D2", new[] { "Up you get. No, don't wake - you'll only spoil it. You'll find the mud on your feet in the morning and wonder.",
                "The sleepers round a point in her zone - people or beasts, as many as her song takes - rise still asleep and fall on her active foes, feeling no pain; with no foe about, they shamble where they stand. Whoever walks for her becomes her sleeper.",
                "Her sleepers cannot be woken: struck, they wake in a berserk rage." } },
            { "D3", new[] { "Why gulp what can be savoured? Ten nights, ten sips. You won't even notice until the last one.",
                "She begins to drink a sleeper night by night - up to three at once, and once begun it needs no more singing: on the tenth night it dies. Now and then a mind shakes it off in one night, by chance against it.",
                "Every hour of it feeds her hunger; what her hunger cannot hold goes to her store." } },
            { "D4", new[] { "Strike me once, and every dream I have ever touched will remember your face.",
                "Twice: first the zone whose sleepers rise, then where they walk. They walk there asleep, at four-fifths of their speed, breaking through doors, walls and rock if they must, fall on any foe of your people they find there who was not one of them, and lie down to sleep again.",
                "She sees every sleeper on the map, through walls, outlined in violet (mechanoids do not sleep)." } },
            { "D5", new[] { "A lady should never walk the dark alone. Mine walk with me - and they never complain.",
                "Twice: the zone whose sleepers rise, then where they go. Her retinue shambles about the point she shows and falls on any foe that comes; nothing wakes them but full rest, and while it lasts they rest at a fifth the speed. Pressed again while they walk, it only points them elsewhere, for nothing.",
                "While her retinue walks, into the dream costs her nothing and waits for nothing." } },
            // ---- The cult
            { "C1", new[] { "Kneel? Oh, I never ask. They simply start to, one night, and can't remember why they ever stood.",
                "One mind in her zone begins to worship her: a stream that grows by the hour like a sickness, and a second sowing makes it flow twice as fast (no more); at its height it holds 72 hours a sowing, then ebbs. Ticked: a whole crowd round a point, each by chance, more or less - a day and a half of worship that the night lengthens rather than deepens.",
                "Her worshippers past the first stage are her cultists: they never harm her or her people, cannot leave the land while the cult holds and are drawn in her colour; they are no enemy of their own unless she sends them to guard the land." } },
            { "C2", new[] { "My faithful come when I hum. Gods have had worse congregations.",
                "Her cultists go to guard the edges of the land: for 9 to 12 hours - more, the stronger she is - they walk them and fall on any foe of her people who comes near; then they sleep.",
                "When she sleeps, some of her cultists (each by chance) ring her at forty steps and fall on whoever comes inside. Her cultists eat less, and hungry they eat anything within forty steps - food, a corpse, and a fallen fellow cultist if nothing else is left." } },
            { "C3", new[] { "They came to fight me. Look at them now - they can't even remember why.",
                "Foes and cultists round a point in her zone stand in a wide ring round it, rapt, each by chance: while they do, the worship of her cultists grows, and foes slip into the cult.",
                "Her cultists spread the cult: now and then a fervent one draws someone near into it." } },
            { "C4", new[] { "Devotion means nothing until it is tested. Theirs always passes.",
                "A fervent cultist - or, ticked, all of them - falls on the nearest unsuspecting foe of her people with a terrible first blow (twice as hard and at the neck, when one alone is sent), fights every foe on the land, and with none left dies by their own hand. With no foe on the land, they die at once.",
                "Her cultists feel no pain." } },
            { "C5", new[] { "Banners change so easily. Faith in me doesn't.",
                "Her fervent worshippers among her foes turn to her side for good. If no one turns, the hunger she spent comes back to her.",
                "Worship grows faster, and the cult spreads more readily." } },
            // ---- Will
            { "W1", new[] { "Come to me. Yes, you. Drop that - you won't need it where you're going.",
                "Foes round a point she chooses in her zone stop fighting and walk to her, slow and heavy-limbed - their weapons slip from their hands now and then on the way, and the heaviness stays an hour after; beside her she devours them.",
                "Those answering her Call cannot harm her." } },
            { "W2", new[] { "Your commander's voice, your commander's words. Your commander would be so disappointed in you for running. I'm not.",
                "A commander's voice rings round a point in her zone: 'Hold the position! They are coming!' Foes there who believe it crowd round the point and defend it to the last; those who do not walk on.",
                "She persuades and recruits better: she always finds the voice they trust." } },
            { "W3", new[] { "Two voices at once - the oldest trick of my line, and still they never see it coming. They hear their friends and see their enemies.",
                "Foes round a point in her zone hear their own comrades threaten them and fly into a berserk rage - as many as her power takes.",
                "Minds resist her Call and her sleep less." } },
            { "W4", new[] { "For a while, I am all they love. It's the happiest they will ever be, poor things.",
                "A few foes round a point in her zone fight for her for a while, feeling no pain; then they fall asleep.",
                "The captivated serve half as long again." } },
            { "W5", new[] { "They came for my home. How rude. Let them settle it among themselves.",
                "A whole raid round a point in her zone turns on itself: half go berserk, half fight for her.",
                "Her Call reaches further and takes two more." } },
            // ---- The feast
            { "F1", new[] { "Nothing I devour is wasted. Your craft is mine now - and frankly, I'll use it better.",
                "For a day she has the skill of one of her chosen voices - the best of each skill, or the one chosen for it in her Voices page. A voice lends itself five times; then it is half-forgotten, only a name. Weak voices of one skill merge into one of 10 or more.",
                "Every voice she holds makes her aria stronger - each new one a little less, for a full choir is hard to lift; yet every tenth voice lifts it anew. A half-forgotten voice weighs a quarter; voices that are not chosen fade to half-forgotten in two months." } },
            { "F2", new[] { "Just a taste. I'm not greedy - only curious.",
                "Free: up close, she drinks a little essence from someone asleep, downed, captive or captivated, without killing them. What her hunger cannot hold goes to her store.",
                "A quarter of what a kill gives beyond her hunger goes to her store." } },
            { "F3", new[] { "A wise huntress keeps something sweet for later.",
                "Her store (up to 1000 essence) feeds her hunger. Ticked, she pours all of it out: her hunger filled, the rest into her aria's experience - and with nothing left to learn, into a surge that doubles her aria for a while.",
                "Her store holds 1200; the 200 above the 1000 pay for her chants before her hunger does." } },
            { "F4", new[] { "I devoured a commander once. He had a lovely voice. 'Retreat!' - see? Just like him.",
                "Foes round a point in her zone hear their commander order a retreat: those who believe it drop their weapons and run for the edge - until they understand there was no order, and come back for their weapons ragged and slow.",
                "Leaders, psychics, Avir and Singers leave her voices worth three times as much." } },
            { "F5", new[] { "Every voice I have ever taken, all at once. A lovely choir, isn't it? Don't cover your ears - it won't help.",
                "Every foe round a point in her zone is stunned - the more voices she holds, the longer; the timid and the sensitive drop their weapons and run about where it caught them, screaming.",
                "Every voice she holds gives more." } },
            // ---- Scars
            { "S1", new[] { "You thought the wound had healed? How sweet. Let me remind you whose it is.",
                "She tears at the pain she left - no wound, only pain: it flares and stuns, the deeper the longer. On one who bears it the scars often run on up the limb like a blade; set to everyone round a point (a mark on her network), many are stunned and the scars rarely spread. The mind may hold them back.",
                "Her blows leave phantasmal pain where they land: the wound heals, the pain and her hold on them stay." } },
            { "S2", new[] { "Every pain I leave hums for me, like a plucked string. Hide behind any wall you like. And what I give, I can take back - mercy is a luxury I can afford.",
                "One in her zone who bears her pain becomes her prey: every blow lands deeper for a while, and by chance against their mind the scars burn past bearing. Set to mercy (a mark on her network), she lifts the pain from them instead.",
                "Everyone who bears her pain glows red to her, wherever they hide." } },
            { "S3", new[] { "Scream, darling. Louder. Let your friends hear what waits for them.",
                "In foes in her zone who bear her pain, and foes round a point, the pain swells and they scream where they stand. Their own near them lose their nerve and fight worse; their kin drop everything and run to them.",
                "Foes near her strike worse; those under her terror yield to her aria more, and the more it hurts, the more." } },
            { "S4", new[] { "If it hurts so much - why keep it? Go on. I won't look. Much.",
                "Everyone in her zone with deep enough scars on a limb tries to cut it away - if a hand of theirs can reach it - by chance against their mind. They do not fight while at it, and can be stopped.",
                "The pain wanders: every few hours a scar creeps up the body - finger to hand, hand to arm, and on - and the weaker the mind, the further." } },
            { "S5", new[] { "Look at your arm, darling. No - really look. There. Now you see what I see.",
                "Every limb in her zone that bears her pain looks flayed to the bone: unbearable pain, screams, and the weak of heart who see it run.",
                "Her mercy also takes her terror and her flaying with it, and clears a failing mind. Those who bear her pain and come near breaking see their flesh flayed out of the corner of the eye - and run for the edge of the land, only to fall there." } },
            // ---- Guises
            { "G1", new[] { "I was here, wasn't I? Now I'm not. Let them search for me elsewhere - if they are still able to search once I'm done with my hunt, of course.",
                "She vanishes from sight for a while; those who cannot see her stop chasing her. Every blow she strikes from it lets the one struck look for her - the more blows, the likelier, and a beast sooner still. Pressed again, she steps out of the veil.",
                "Under her veil she moves swiftly, and a blow struck from it lands twice as hard." } },
            { "G2", new[] { "I'd rather turn into an elegant beast than masquerade as those... creatures. Besides, it's good for my looks.",
                "Every Singer has the doe and the crow from birth; this star only deepens them. As a crow she flies; as a doe she keeps to the ground. The same button again gives her back her own shape.",
                "In a beast's form she runs far faster, no one takes her for a target, and once a day a blow struck at the doe or the crow does not give her away: she only slips under her veil." } },
            { "G3", new[] { "Was that me? Or that? Or that one? Choose quickly - you're running out of arrows.",
                "Dark shadows in her own shape rise round a point in her zone: foes take them for her and waste their shots on them until they melt away; nothing harms them. Foes there also see false shapes and aim worse.",
                "Foes in her zone see false shapes and miss more - each by its own mind. Her shadows, too, are taken for her by the mind, not by everyone." } },
            { "G4", new[] { "You were aiming at my double. Don't feel bad; everyone does.",
                "She slips under a deep veil - for half an hour no eye finds her - and is elsewhere in her zone. Where she stood, her mirage stays for half an hour and draws every foe it fools, whatever they were aiming at.",
                "Once a day, when she would be seen through, a ring of her mirages rises round her for half an hour, and she is veiled again." } },
            { "G5", new[] { "My veil fits more than just me. I'm generous like that.",
                "Her own people round a point in her zone vanish from sight for a while; a sharp mind near them may still see through it.",
                "The veil and the forms cost no hunger, and she is seen through half as often. Her own who fall in her zone are veiled where they lie, for an hour." } },
            // ---- The pack
            { "P1", new[] { "The beasts of this land remember who sang to them first.",
                "The beasts round her answer and wait, each by chance - on this path her skill with animals decides, and every song of it teaches her. Sung again (for nothing), anywhere on the land: they go there, falling on her foes on the way, and circle the place wide. Pointed twice at the same place, they hunt the wild beasts there. While her pack is out, she learns as a handler does.",
                "The beasts of the land never fear her." } },
            { "P2", new[] { "Why chase my dinner when my dinner can come to me?",
                "Round a point in her zone, a whole herd (no beast too great for her aria) walks to her home and drops senseless there for hours, marked for the hunters' knives.",
                "Beasts marked for hunting come to her of their own accord to die - one at a time." } },
            { "P3", new[] { "When I want to be seen, I want to be feared as well. The she-wolf manages both.",
                "She takes the form of a she-wolf; in it she keeps to the ground. The same button again gives her back her own shape.",
                "Beasts fighting for her near her are stronger and wear their prey down: every bite harries it, slower and clumsier with each; and she learns from every bite." } },
            { "P4", new[] { "Somewhere out there, something with teeth has just heard its name.",
                "Beasts of the wild come in together from one edge of the land to fight for her - up to twenty, mixed, and more and stronger the stronger her aria is.",
                "A land that runs short of beasts draws new ones in, twice a day at most - for any Singer who has this, wild or not. Meat for ever." } },
            { "P5", new[] { "The land itself is on my side. It usually is.",
                "Most of the land's beasts fall on her foes - her skill with animals decides how many. Sung again within the hour, for a little more hunger, the rest of them - the great beasts too. With her mirages, the beasts now and then cast short-lived shadows of their own shape that draw the fire; with her veil of the pack, some go unseen - the more so, the better she is with animals - until their first bite.",
                "Her beasts feel only a fifth of their pain." } },
            // ---- In the shadow of the Flow
            { "T1", new[] { "Every dark corner is a door, and I hold the key to all of them.",
                "She steps into any dark place in her zone.",
                "Every unlit place of the land is open to her eyes, through the fog of war. Where she steps out of the dark, one of seven of her other arts may sing round her at once, for nothing - heavy eyelids, sleepwalking, ecstasy, captivation, the false order, terror, the mirage - if she has it; once in seventy, all she has." } },
            { "T2", new[] { "Years are only essence that learned patience. I am less patient.",
                "Foes in her zone - more of them, the stronger her song - age fast for a while; with her mirages, skeletons stand beside some of them. Every ten years she drinks heal her wounds, then give her a year of her own back.",
                "She can set her own age aside (a mark on her network), and later take it back: grown younger meanwhile, she grows into it and keeps what those years would have taught; grown older, she sheds the years and what they taught, and is whole again." } },
            { "T3", new[] { "Time is a song too. I simply change the tempo.",
                "Round a point in her zone, for half an hour, a foe that moves is thrown back every ten seconds to where it stood eight seconds ago - and time does not always put it back whole: a wound that never happened, a dropped weapon, a failing mind at any age. Her own there quicken. The fold stays where she made it, seen as a violet ring, and catches whoever walks in; when it closes, every foe still inside ages by 35 to 1500 years at once - most turn to dust.",
                "Foes in her zone slowly lose their minds, and every few hours are thrown back to where they stood hours ago." } },
            { "T4", new[] { "Your lights die and your machines fall silent. Now we're even - I never needed either.",
                "A black fog rises round a point in her zone - a real smoke, creeping through open doors; shots go wild in it and plants stop growing. Or, set to the flare (a mark on her network), the sun flares: every light and machine on the land dies until dawn.",
                "While she is hostile, the fog rises by day and the lights die every night." } },
            { "T5", new[] { "The sun can stay in the sky if it likes. Your minds will believe me over it.",
                "For three days the land believes it is night. Plants grow and the sun still feeds the panels; only minds are fooled. The psychically deaf see through it.",
                "By night her aria is stronger." } },
        };

        /// <summary>A path in a Singer's own words.</summary>
        public static string AreaVoice(string a)
        {
            switch (a)
            {
                case "L": return "Sleep is the gentlest cage. They walk into it smiling.";
                case "D": return "In dreams there are no walls and no guards. Only me.";
                case "C": return "They called my foremothers goddesses. I see no reason to correct them.";
                case "W": return "Will is just a song people sing to themselves. Mine is louder.";
                case "F": return "Every voice I take stays with me. I am never lonely.";
                case "S": return "My claws leave more than wounds. They leave a promise.";
                case "G": return "To be seen is a choice. Mine - never theirs.";
                case "P": return "Beasts are honest. They know who leads the hunt.";
                default: return "The Stream has a shadow, and it is rather cosy in there.";
            }
        }

        public static string Lore(string key) => Texts.TryGetValue(key, out string[] t) ? t[0] : null;

        /// <summary>Puts the plain words into the network's stones.</summary>
        public static void Apply(IEnumerable<LinarianNode> nodes)
        {
            foreach (LinarianNode n in nodes)
                if (Texts.TryGetValue(n.key, out string[] t)) { n.active = t[1]; n.passive = t[2]; }
        }
    }
}
