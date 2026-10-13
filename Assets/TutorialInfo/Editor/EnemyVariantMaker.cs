#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Carry the One > Make Enemy Variants: builds the 20 enemy variants from the simulator's numbers, so you don't type
// them in by hand. Put this file in an Editor folder (Assets/Editor/). Open the game scene first, then run it.
//
// What it does:
// - Dice: for every die a variant needs, it uses one of your dice that already has exactly those faces. If there's
//   none, it makes a new DieDefinition (in a Variants folder next to your dice), copying the model from one of your
//   dice with the same number of sides.
// - Enemies: each variant starts as a copy of the enemy whose slot it shares (so the unlock faces, the boss die, the
//   voice and the portraits come along as placeholders until you draw its own), then gets its own name, HP, dice,
//   layout, brain, throws, table and lines. They go in a Variants folder next to your enemies.
// - The ladder: GameController's Slots (made from the Ladder if they're empty), each variant added to its slot.
// Run it again any time: existing variants get their numbers updated, and their lines and art are left alone.
public static class EnemyVariantMaker
{
    private class Spec
    {
        public string slot, name, table, layout, brain;
        public Vector2Int? target;
        public int fight, hp, throws, rethrowBelow;
        public float minGain, blunder;
        public string[] dice, traits;
        public string intro, defeat;
        public string[] reroll, keep, win, lose, bust;
    }

    // Faces, one token per side: 0-9, + - × ÷ %
    private static readonly Dictionary<string, string> DiceFaces = new Dictionary<string, string>
    {
        { "D4", "1 2 3 4" },
        { "D4_Plus", "+ + + +" },
        { "D6_Minus", "- - - ÷ ÷ +" },
        { "D8_OneTimes", "× + + + + + + +" },
        { "D12_Mod", "+ + + + + + + % % % % ×" },
        { "D10_Odd", "1 1 1 3 3 3 5 5 7 9" },
        { "D6_PlusMinus", "+ + + - - -" },
        { "D6_Tiny", "1 1 2 2 3 3" },
        { "D6_Seven", "7 7 7 7 7 7" },
        { "D6_PlusTimes", "+ + + × × ×" },
        { "D6_Slot", "1 1 7 7 7 9" },
        { "D6", "1 2 3 4 5 6" },
        { "D6_Plus", "+ + + + + +" },
        { "D6_Low", "- - ÷ ÷ + +" },
        { "D8", "1 2 3 4 5 6 7 8" },
        { "D8_Plus", "+ + + + + + + +" },
        { "D8_Op", "× × + + + + + +" },
        { "D8_Times", "× × × × + + + +" },
        { "D10", "0 1 2 3 4 5 6 7 8 9" },
        { "D12_Mid", "0 1 2 3 4 5 6 7 8 9 5 6" },
        { "D12_Hi", "4 5 6 7 8 9 6 7 8 9 9 9" },
        { "D20_Gold", "1 2 3 4 5 6 7 8 9 1 2 3 4 5 6 7 8 9 9 9" },
    };

    private static readonly Spec[] Variants =
    {
        new Spec { fight = 2, slot = "TALLY", name = "NIL", hp = 8, table = "Low",
                   layout = "N O N", dice = new string[] { "D10", "D10", "D6_Minus" }, brain = "Calculating", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Nothing. That's what I'm after. Nothing at all.", defeat = "Back to nothing.",
                   reroll = new string[] { "Too much.", "Less.", "Still something." }, keep = new string[] { "Close to nothing.", "Barely there." },
                   win = new string[] { "Nothing beats nothing.", "Zero. Feel it." }, lose = new string[] { "You found less than me?", "Emptier than I thought." }, bust = new string[] { "That's... something." } },
        new Spec { fight = 3, slot = "QUARTZ", name = "ABACUS", hp = 5, table = "High",
                   layout = "N O N O N", dice = new string[] { "D4", "D4", "D4", "D4_Plus", "D4_Plus" }, brain = "Greedy", throws = 3,
                   minGain = 0f, rethrowBelow = 4, blunder = 0.0f, traits = new string[] {  },
                   intro = "One, two, three... I'll count you out too.", defeat = "...carry the one.",
                   reroll = new string[] { "Miscounted.", "Again, properly.", "That bead slipped." }, keep = new string[] { "The sum is the sum.", "Tallied." },
                   win = new string[] { "Exactly as counted.", "Into the ledger." }, lose = new string[] { "I must recount.", "That can't add up." }, bust = new string[] { "A bead too many." } },
        new Spec { fight = 4, slot = "DEE", name = "OCTAVIA", hp = 9, table = "High",
                   layout = "N O N", dice = new string[] { "D8", "D8", "D8_OneTimes" }, brain = "Calculating", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Eight arms. Eight sides. One of them multiplies.", defeat = "I'll need all eight arms to count that loss.",
                   reroll = new string[] { "Not that one.", "Another arm.", "Again." }, keep = new string[] { "Arms full.", "Good enough for all eight." },
                   win = new string[] { "Times eight, darling.", "Did you see that multiply?" }, lose = new string[] { "All arms and no luck.", "Ink on my face." }, bust = new string[] { "Tangled." } },
        new Spec { fight = 5, slot = "DODECA", name = "REMY", hp = 11, table = "High",
                   layout = "N O N O N", dice = new string[] { "D12_Mid", "D12_Mid", "D12_Mid", "D12_Mod", "D12_Mod" }, brain = "Calculating", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Divide all you like. Something's always left over.", defeat = "No remainder...",
                   reroll = new string[] { "Not divisible.", "Again.", "Leftovers." }, keep = new string[] { "And the remainder is mine.", "Leftovers." },
                   win = new string[] { "What's left? Me.", "Modulo, friend." }, lose = new string[] { "Nothing left over.", "Clean division. Rude." }, bust = new string[] { "That doesn't divide." } },
        new Spec { fight = 6, slot = "LOU", name = "ODDS", hp = 9, table = "High",
                   layout = "N O N O N", dice = new string[] { "D10_Odd", "D10_Odd", "D10_Odd", "D6_Plus", "D6_Plus" }, brain = "Impulsive", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Never even. Never have been.", defeat = "Against all odds.",
                   reroll = new string[] { "Odd.", "Again.", "Stranger." }, keep = new string[] { "Odd. Perfect.", "Another odd one." },
                   win = new string[] { "Odds are with me.", "Beat the odds? Not today." }, lose = new string[] { "Even you got lucky.", "Odd." }, bust = new string[] { "Oddly unlucky." } },
        new Spec { fight = 7, slot = "KNUCKLES", name = "SHOOTER", hp = 12, table = "High",
                   layout = "N O N O N", dice = new string[] { "D8", "D8", "D6", "D8_Plus", "D8_Plus" }, brain = "Greedy", throws = 1,
                   minGain = 0f, rethrowBelow = 5, blunder = 0.0f, traits = new string[] { "Hot Hand" },
                   intro = "First roll's free. After that, I'm hot.", defeat = "Crapped out.",
                   reroll = new string[] { "Let it ride.", "Again, I'm on fire.", "One more." }, keep = new string[] { "Shooter's hot.", "That'll play." },
                   win = new string[] { "Heating up!", "Don't stop me now." }, lose = new string[] { "Cold dice.", "Just warming up." }, bust = new string[] { "Seven out." } },
        new Spec { fight = 8, slot = "PIT BOSS", name = "CROUPIER", hp = 57, table = "Target",
                   layout = "N N O N", dice = new string[] { "D10", "D10", "D10", "D6_PlusMinus" }, brain = "Calculating", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Place your bets. The house sets the number.", defeat = "The table is closed.",
                   reroll = new string[] { "Irregular.", "Again.", "No." }, keep = new string[] { "No more bets.", "As calculated." },
                   win = new string[] { "The rake takes it.", "Exactly on the number." }, lose = new string[] { "A rare miss.", "The table favours you. Briefly." }, bust = new string[] { "An irregular hand." } },
        new Spec { fight = 9, slot = "TWINS", name = "TRIPLETS", hp = 480, table = "High",
                   layout = "N N N", dice = new string[] { "D6_Tiny", "D6_Tiny", "D6_Tiny" }, brain = "Impulsive", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "We're three! Three is more than two!", defeat = "We're telling!",
                   reroll = new string[] { "Again!", "Me next!", "My turn!" }, keep = new string[] { "We did it together!", "Three digits!" },
                   win = new string[] { "Three beats two!", "Again! Again! Again!" }, lose = new string[] { "Mooom!", "It was his fault." }, bust = new string[] { "Who dropped it?" } },
        new Spec { fight = 10, slot = "CARRIE", name = "STEADY EDDIE", hp = 610, table = "High",
                   layout = "N N O N", dice = new string[] { "D6_Seven", "D6_Seven", "D6_Seven", "D6_PlusTimes" }, brain = "Calculating", throws = 2,
                   minGain = 60f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Same dice. Same number. Every time. It's relaxing.", defeat = "Well. Seven out of seven.",
                   reroll = new string[] { "Just the operator.", "Nice and slow.", "Once more." }, keep = new string[] { "Seventy-seven. Lovely.", "Steady." },
                   win = new string[] { "Told you. Every time.", "Slow and steady." }, lose = new string[] { "Hm. That's new.", "Unsteady." }, bust = new string[] { "Oh dear." } },
        new Spec { fight = 11, slot = "BONES", name = "GHOST", hp = 39, table = "Low",
                   layout = "N O N O N", dice = new string[] { "D10", "D10", "D10", "D6_Low", "D6_Low" }, brain = "Impulsive", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Boo. Or nothing. Whichever is lower.", defeat = "Passing on.",
                   reroll = new string[] { "Fading...", "Again.", "Boo." }, keep = new string[] { "Barely there.", "Faint." },
                   win = new string[] { "Can't beat what isn't there.", "Boo." }, lose = new string[] { "I'm... fading.", "You see right through me." }, bust = new string[] { "Spooked myself." } },
        new Spec { fight = 12, slot = "ROUGE", name = "NOIR", hp = 2700, table = "High",
                   layout = "N N O N", dice = new string[] { "D12_Hi", "D12_Hi", "D12_Hi", "D8_Op" }, brain = "Calculating", throws = 2,
                   minGain = 200f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "My sister bets on red. I bet on you losing.", defeat = "Fade to black.",
                   reroll = new string[] { "No.", "Not tonight." }, keep = new string[] { "Black. Always black.", "Quietly done." },
                   win = new string[] { "Noir wins.", "Lights out." }, lose = new string[] { "Hm. Red's luck.", "A shadow passes." }, bust = new string[] { "Smudged." } },
        new Spec { fight = 13, slot = "BARTENDER", name = "SOMMELIER", hp = 360, table = "Limit",
                   layout = "N N O N", dice = new string[] { "D10", "D10", "D10", "D6_PlusTimes" }, brain = "Calculating", throws = 3,
                   minGain = 50f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "A fine vintage needs a limit. Just enough. Never too much.", defeat = "The bottle's empty.",
                   reroll = new string[] { "Corked.", "Not this one.", "Pour another." }, keep = new string[] { "Exquisite.", "Just under." },
                   win = new string[] { "A perfect pour.", "Notes of victory." }, lose = new string[] { "Overpoured.", "Vinegar." }, bust = new string[] { "Spilled." } },
        new Spec { fight = 14, slot = "STICKY", name = "BANDIT", hp = 310, table = "High",
                   layout = "N N N", dice = new string[] { "D6_Slot", "D6_Slot", "D6_Slot" }, brain = "Impulsive", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Pull the lever, kid. Let's see some sevens.", defeat = "...insert coin.",
                   reroll = new string[] { "Spin!", "Again!", "Pull!" }, keep = new string[] { "Jackpot!", "Cherries." },
                   win = new string[] { "Seven seven seven!", "Pay up." }, lose = new string[] { "Tilt!", "Out of order." }, bust = new string[] { "Jammed." } },
        new Spec { fight = 15, slot = "BOUNCER", name = "BRUISER", hp = 1200, table = "High",
                   layout = "N N O N", dice = new string[] { "D20_Gold", "D20_Gold", "D20_Gold", "D8_Times" }, brain = "Calculating", throws = 2,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.75f, traits = new string[] {  },
                   intro = "Big dice. Big hands. Don't ask me where they go.", defeat = "Ow.",
                   reroll = new string[] { "Again!", "Smash it!" }, keep = new string[] { "Good enough!", "Whatever!" },
                   win = new string[] { "SMASH!", "Big number!" }, lose = new string[] { "Huh?", "Hands too big." }, bust = new string[] { "Oops. Dropped one." } },
        new Spec { fight = 16, slot = "BANKER", name = "THE SHARK", hp = 400, table = "Target",
                   layout = "N N N", dice = new string[] { "D10", "D10", "D10" }, brain = "Calculating", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "You owe me a number. I always collect exactly.", defeat = "Consider it... forgiven.",
                   reroll = new string[] { "Not enough.", "Again." }, keep = new string[] { "Exactly what you owe.", "Collected." },
                   win = new string[] { "Interest's due.", "Pay up." }, lose = new string[] { "I'll add it to your tab.", "Smells like blood. Yours, later." }, bust = new string[] { "Bad debt." } },
        new Spec { fight = 17, slot = "HIGH ROLLER", name = "WHALE", hp = 2000, table = "High",
                   layout = "N N N N", dice = new string[] { "D12_Hi", "D12_Hi", "D12_Hi", "D12_Hi" }, brain = "Impulsive", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "I don't count chips. I count digits. Four of them.", defeat = "Cash me out.",
                   reroll = new string[] { "Splash.", "Again." }, keep = new string[] { "Pocket change.", "Big." },
                   win = new string[] { "Splash.", "That's how whales play." }, lose = new string[] { "Beached.", "Small fry got lucky." }, bust = new string[] { "Belly flop." } },
        new Spec { fight = 18, slot = "FLIPSIDE", name = "LIMBO", hp = 4100, table = "Low",
                   layout = "N N O N N", dice = new string[] { "D10", "D10", "D10", "D10", "D6_Low" }, brain = "Impulsive", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "How low can you go? Lower than me? Never.", defeat = "Can't... bend... any more.",
                   reroll = new string[] { "Lower.", "Again." }, keep = new string[] { "Low.", "Lower." },
                   win = new string[] { "Under the bar!", "How low? This low." }, lose = new string[] { "Knocked the bar.", "Too stiff." }, bust = new string[] { "Fell over." } },
        new Spec { fight = 19, slot = "JACKPOT", name = "MIDAS", hp = 4500, table = "High",
                   layout = "N N N N", dice = new string[] { "D8", "D8", "D8", "D8" }, brain = "Greedy", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Everything I touch goes in the right order. Gold first.", defeat = "...turned to lead.",
                   reroll = new string[] { "Not gold enough.", "Again." }, keep = new string[] { "Golden.", "Arranged." },
                   win = new string[] { "Gold always wins.", "Polished." }, lose = new string[] { "Tarnished.", "Fool's gold." }, bust = new string[] { "Lead." } },
        new Spec { fight = 20, slot = "SNAKE EYES", name = "CARD COUNTER", hp = 10000, table = "High",
                   layout = "N N N O N", dice = new string[] { "D10", "D10", "D10", "D10", "D8_Times" }, brain = "Calculating", throws = 3,
                   minGain = 50f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Don't mind me. I'm just keeping track. Of everything.", defeat = "I lost count.",
                   reroll = new string[] { "The odds say again.", "Expected value, baby.", "Recalculating." }, keep = new string[] { "Plus EV.", "Counted." },
                   win = new string[] { "The math was right.", "Probability, my friend." }, lose = new string[] { "Variance.", "Within the margin." }, bust = new string[] { "Miscounted." } },
        new Spec { fight = 21, slot = "THE HOUSE", name = "THE VAULT", hp = 6000, table = "Limit", target = new Vector2Int(2000, 9999),
                   layout = "N N N N", dice = new string[] { "D10", "D10", "D10", "D10" }, brain = "Calculating", throws = 1,
                   minGain = 0f, rethrowBelow = 2, blunder = 0.0f, traits = new string[] {  },
                   intro = "Four digits. One combination. Go over and you're locked out.", defeat = "...open.",
                   reroll = new string[] { "Wrong combination.", "Again." }, keep = new string[] { "Click.", "Tumblers set." },
                   win = new string[] { "Access denied.", "Locked tight." }, lose = new string[] { "A tumbler slipped.", "Close to the combination." }, bust = new string[] { "Alarm!" } },
    };

    private static readonly List<string> report = new List<string>();

    [MenuItem("Carry the One/Make Enemy Variants")]
    private static void Make()
    {
        report.Clear();
        List<FaceDefinition> faces = LoadAll<FaceDefinition>();
        List<DieDefinition> dice = LoadAll<DieDefinition>();
        List<EnemyDefinition> enemies = LoadAll<EnemyDefinition>();
        List<EnemyTrait> traits = LoadAll<EnemyTrait>();

        FieldInfo layoutField = typeof(EnemyDefinition).GetField("layout");
        List<Object> layouts = new List<Object>();
        if (layoutField != null)
            foreach (string guid in AssetDatabase.FindAssets("t:" + layoutField.FieldType.Name))
            {
                Object o = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), layoutField.FieldType);
                if (o != null) layouts.Add(o);
            }

        // Faces by token, preferring the ones named Face_...
        Dictionary<string, FaceDefinition> faceBy = new Dictionary<string, FaceDefinition>();
        foreach (FaceDefinition f in faces)
        {
            string key = Token(f);
            if (key == null) continue;
            if (!faceBy.ContainsKey(key) || (f.name.StartsWith("Face_") && !faceBy[key].name.StartsWith("Face_"))) faceBy[key] = f;
        }

        EnemyDefinition[] ladder = LadderInScene();
        string diceFolder = FolderNextTo(dice.Count > 0 ? dice[0] : null, "Assets", "Variant Dice");
        int made = 0, updated = 0;

        foreach (Spec s in Variants)
        {
            // The enemy it shares a slot with: by name, else whoever is at that fight in your Ladder
            EnemyDefinition original = enemies.Find(e => Same(e.displayName, s.slot) || Same(e.name, s.slot));
            if (original == null && ladder != null && s.fight - 1 < ladder.Length) original = ladder[s.fight - 1];
            if (original == null) { report.Add($"SKIPPED {s.name}: no enemy called {s.slot}, and no Ladder to look up fight {s.fight} in."); continue; }

            // Its dice
            DieDefinition[] its = new DieDefinition[s.dice.Length];
            bool ok = true;
            for (int i = 0; i < s.dice.Length; i++)
            {
                its[i] = GetDie(s.dice[i], dice, faceBy, diceFolder);
                if (its[i] == null) ok = false;
            }
            if (!ok) { report.Add($"SKIPPED {s.name}: a die is missing (see above)."); continue; }

            // Its layout
            Object layout = layouts.Find(l => Pattern(l) == s.layout.Replace(" ", ""));
            if (layoutField != null && layout == null) report.Add($"{s.name}: no layout asset for \"{s.layout}\" yet. Make one and drag it in.");

            EnemyDefinition v = enemies.Find(e => Same(e.displayName, s.name));
            bool isNew = v == null;
            if (isNew)
            {
                v = Object.Instantiate(original);   // every field of the original, as a starting point
                v.name = "Enemy_" + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.name.ToLower()).Replace(" ", "");
                v.intro = s.intro;
                v.defeatLine = s.defeat;
                v.rerollLines = s.reroll;
                v.keepLines = s.keep;
                v.winLines = s.win;
                v.loseLines = s.lose;
                v.bustLines = s.bust;
                string enemyFolder = FolderNextTo(original, "Assets", "Variants");   // a Variants folder next to the original
                AssetDatabase.CreateAsset(v, AssetDatabase.GenerateUniqueAssetPath($"{enemyFolder}/{v.name}.asset"));
                enemies.Add(v);
                made++;
            }
            else updated++;

            v.displayName = s.name;
            v.maxHP = s.hp;
            v.dice = its;
            if (layoutField != null && layout != null) layoutField.SetValue(v, layout);
            v.brain = (EnemyBrain)System.Enum.Parse(typeof(EnemyBrain), s.brain);
            v.throwAttempts = s.throws;
            v.minGain = s.minGain;
            v.rethrowBelow = s.rethrowBelow;
            v.blunderChance = s.blunder;
            v.table = (TableType)System.Enum.Parse(typeof(TableType), s.table);
            v.targetRange = s.target ?? original.targetRange;

            List<EnemyTrait> mine = new List<EnemyTrait>();
            foreach (string t in s.traits)
            {
                EnemyTrait found = traits.Find(x => Same(x.displayName, t) || Same(x.name, t) || Same(x.name, "Trait_" + t));
                if (found != null) mine.Add(found);
                else report.Add($"{s.name}: no trait called {t}. Add yours to its Traits by hand.");
            }
            v.traits = mine.ToArray();
            EditorUtility.SetDirty(v);
        }
        AssetDatabase.SaveAssets();
        report.Insert(0, $"Enemy variants: {made} made, {updated} updated (each in a Variants folder next to its slot's original).");

        AddToLadder(enemies);
        Debug.Log("[Carry the One] " + string.Join("\n", report));
    }

    // ---------- Dice ----------

    private static DieDefinition GetDie(string name, List<DieDefinition> dice, Dictionary<string, FaceDefinition> faceBy, string folder)
    {
        if (!DiceFaces.TryGetValue(name, out string spec)) { report.Add($"No faces listed for {name}."); return null; }
        string[] tokens = spec.Split(' ');
        string want = Sorted(tokens);
        foreach (DieDefinition d in dice) if (Signature(d) == want) return d;   // you already have one with these faces

        DieDefinition model = dice.Find(d => d.faceDefinitions != null && d.faceDefinitions.Length == tokens.Length && d.prefab != null)
                           ?? dice.Find(d => d.faceDefinitions != null && d.faceDefinitions.Length == tokens.Length);   // dice without a prefab use your default one
        if (model == null) { report.Add($"{name}: none of your dice has {tokens.Length} sides to copy the model from."); return null; }

        FaceDefinition[] fs = new FaceDefinition[tokens.Length];
        int operators = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (!faceBy.TryGetValue(tokens[i], out fs[i])) { report.Add($"{name}: no face asset for \"{tokens[i]}\"."); return null; }
            if (fs[i].type == FaceType.Operator) operators++;
        }
        DieDefinition die = Object.Instantiate(model);
        die.name = name;
        die.displayName = name.Replace("_", " ");
        die.faceDefinitions = fs;
        die.role = operators * 2 > tokens.Length ? DieRole.Operator : DieRole.Number;
        AssetDatabase.CreateAsset(die, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset"));
        dice.Add(die);
        report.Add($"New die {name} ({spec}), model from {model.name}.");
        return die;
    }

    // A die's faces in sorted order, so 1 2 3 4 matches 4 3 2 1 (the order only decides which side is where)
    private static string Signature(DieDefinition d)
    {
        if (d == null || d.faceDefinitions == null) return "";
        List<string> t = new List<string>();
        foreach (FaceDefinition f in d.faceDefinitions) t.Add(Token(f) ?? "_");
        return Sorted(t.ToArray());
    }

    private static string Sorted(string[] tokens)
    {
        string[] copy = (string[])tokens.Clone();
        System.Array.Sort(copy, string.CompareOrdinal);
        return string.Join(" ", copy);
    }

    private static string Token(FaceDefinition f)
    {
        if (f == null) return null;
        if (f.type == FaceType.Number) return f.number.ToString();
        switch (f.op)
        {
            case Operator.Add: return "+";
            case Operator.Subtract: return "-";
            case Operator.Multiply: return "×";
            case Operator.Divide: return "÷";
            case Operator.Modulus: return "%";
        }
        return null;
    }

    // ---------- Layouts ----------

    // "NON" for an N O N layout, read from its pattern (whatever the layout class calls its list)
    private static string Pattern(Object layout)
    {
        if (layout == null) return "";
        System.Type type = layout.GetType();
        object value = null;
        FieldInfo f = type.GetField("pattern");
        if (f != null) value = f.GetValue(layout);
        else
        {
            PropertyInfo p = type.GetProperty("pattern") ?? type.GetProperty("Pattern");
            if (p != null) value = p.GetValue(layout);
        }
        if (!(value is IEnumerable list)) return "";
        string s = "";
        foreach (object x in list) s += x is FaceType t && t == FaceType.Operator ? "O" : "N";
        return s;
    }

    // ---------- The ladder ----------

    private static void AddToLadder(List<EnemyDefinition> enemies)
    {
        GameController game = Object.FindAnyObjectByType<GameController>();
        if (game == null) { report.Add("No GameController in the open scene: open the game scene and run this again to put the variants in the Slots."); return; }

        SerializedObject so = new SerializedObject(game);
        SerializedProperty slots = so.FindProperty("slots");
        SerializedProperty ladder = so.FindProperty("ladder");
        if (slots == null || ladder == null) { report.Add("GameController has no Slots: is it the new GameController.cs?"); return; }

        if (slots.arraySize == 0)   // first time: one slot per Ladder entry
        {
            slots.arraySize = ladder.arraySize;
            for (int i = 0; i < ladder.arraySize; i++)
            {
                SerializedProperty list = slots.GetArrayElementAtIndex(i).FindPropertyRelative("enemies");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = ladder.GetArrayElementAtIndex(i).objectReferenceValue;
            }
            report.Add($"Slots made from the Ladder ({ladder.arraySize} fights).");
        }

        int added = 0;
        foreach (Spec s in Variants)
        {
            EnemyDefinition v = enemies.Find(e => Same(e.displayName, s.name));
            if (v == null) continue;
            for (int i = 0; i < slots.arraySize; i++)
            {
                SerializedProperty list = slots.GetArrayElementAtIndex(i).FindPropertyRelative("enemies");
                bool isItsSlot = i == s.fight - 1, already = false;
                for (int k = 0; k < list.arraySize; k++)
                {
                    EnemyDefinition e = list.GetArrayElementAtIndex(k).objectReferenceValue as EnemyDefinition;
                    if (e == null) continue;
                    if (e == v) already = true;
                }
                if (!isItsSlot || already) continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = v;
                added++;
            }
        }
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        report.Add($"{added} variant(s) added to GameController's Slots. Save the scene.");
    }

    // The Ladder of the GameController in the open scene (null if there isn't one)
    private static EnemyDefinition[] LadderInScene()
    {
        GameController game = Object.FindAnyObjectByType<GameController>();
        if (game == null) return null;
        SerializedProperty ladder = new SerializedObject(game).FindProperty("ladder");
        if (ladder == null) return null;
        EnemyDefinition[] result = new EnemyDefinition[ladder.arraySize];
        for (int i = 0; i < ladder.arraySize; i++) result[i] = ladder.GetArrayElementAtIndex(i).objectReferenceValue as EnemyDefinition;
        return result;
    }

    // ---------- Helpers ----------

    private static List<T> LoadAll<T>() where T : Object
    {
        List<T> all = new List<T>();
        foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            T a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) all.Add(a);
        }
        return all;
    }

    // A subfolder next to an existing asset (or in fallback), made if it isn't there
    private static string FolderNextTo(Object asset, string fallback, string sub)
    {
        string parent = asset != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(asset)).Replace('\\', '/') : fallback;
        string path = parent + "/" + sub;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, sub);
        return path;
    }

    private static bool Same(string a, string b) =>
        a != null && b != null && a.Replace(" ", "").Replace("_", "").ToUpperInvariant() == b.Replace(" ", "").Replace("_", "").ToUpperInvariant();
}
#endif