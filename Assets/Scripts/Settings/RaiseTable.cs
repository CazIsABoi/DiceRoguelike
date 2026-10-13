using UnityEngine;

// What one Raise level adds. The levels stack: Raise 4 plays with the rules of levels 1, 2, 3 and 4.
public enum RaiseRule
{
    EnemyHp,        // every enemy has Amount % more HP (two of these add up: 5 + 5 = 10% more)
    StartingMaxHp,  // you start with Amount % less max HP, and it stays that much lower all run
    HealReward,     // heal rewards heal Amount % of what they normally would (75 = a 40% heal becomes 30%)
    BossThrows,     // act bosses and the last fight get Amount more throw attempts (tested: far too much, see the sim)
    BustCost,       // a busted operator costs more: Amount is added to GameController's Bust Base
    RelicChoices,   // relic screens show Amount relics
    BadBeat,        // your first number die starts with Face where its best number was
    ActTrait,       // every enemy from Act Amount on also has Trait
    BossHeal,       // the full heal after an act boss heals Amount % of the HP you're missing
    BossHp,         // act bosses and the last fight have Amount % more HP (on top of Enemy Hp)
    LowCap,         // a miss at a Low table counts up to Amount more (GameController's Low Cap 20 becomes 30)
    MaxHpReward,    // the Max HP reward gives Amount % of what it normally would (50 = +10% instead of +20%)
}

[System.Serializable]
public class RaiseLevel
{
    public string displayName = "HOUSE EDGE";
    [TextArea(2, 3)] public string line = "ENEMIES HAVE 10% MORE HP";   // what the menu says it does
    public RaiseRule rule;
    public int amount = 10;
    public FaceDefinition face;     // Bad Beat only: what your best number turns into (Face_1)
    public EnemyTrait trait;        // Act Trait only: the trait every enemy gets (your Hot Hand asset)
}

// The Raise levels, in order. Clearing a difficulty unlocks the next level for that difficulty.
// Make one: right-click in the Project window > Create > Carry the One > Raise Table. It comes filled in with the
// ten levels the simulator tested; fill in Face on Bad Beat (Face_1) and Trait on Hot Streak (your Hot Hand asset),
// then drag it onto MainMenu. Normal clears about 23% with no Raise and about 3% at Raise 10 in the sim.
[CreateAssetMenu(menuName = "Carry the One/Raise Table", fileName = "RaiseTable")]
public class RaiseTable : ScriptableObject
{
    public RaiseLevel[] levels =
    {
        new RaiseLevel { displayName = "HOUSE EDGE",  rule = RaiseRule.EnemyHp,       amount = 5,  line = "ENEMIES HAVE 5% MORE HP" },
        new RaiseLevel { displayName = "PIT WATCH",   rule = RaiseRule.BossHp,        amount = 15, line = "BOSSES HAVE 15% MORE HP" },
        new RaiseLevel { displayName = "LOW BLOW",    rule = RaiseRule.LowCap,        amount = 10, line = "MISSES AT LOW TABLES COST MORE" },
        new RaiseLevel { displayName = "SHORT STACK", rule = RaiseRule.StartingMaxHp, amount = 5,  line = "YOU HAVE 5% LESS MAX HP" },
        new RaiseLevel { displayName = "NO COMPS",    rule = RaiseRule.HealReward,    amount = 75, line = "HEALS ARE A QUARTER SMALLER" },
        new RaiseLevel { displayName = "SKIMMED",     rule = RaiseRule.MaxHpReward,   amount = 50, line = "MAX HP REWARDS GIVE HALF" },
        new RaiseLevel { displayName = "TIGHT TABLE", rule = RaiseRule.RelicChoices,  amount = 2,  line = "RELIC SCREENS SHOW 2 RELICS" },
        new RaiseLevel { displayName = "BAD BEAT",    rule = RaiseRule.BadBeat,       amount = 0,  line = "YOUR BEST NUMBER STARTS AS A 1" },
        new RaiseLevel { displayName = "HOT STREAK",  rule = RaiseRule.ActTrait,      amount = 3,  line = "ACT 3 ENEMIES HAVE HOT HAND" },
        new RaiseLevel { displayName = "LAST CALL",   rule = RaiseRule.BossHeal,      amount = 50, line = "BOSSES ONLY HEAL YOU HALFWAY" },
    };

    public int Count => levels != null ? levels.Length : 0;

    public RaiseLevel Level(int n) => levels != null && n >= 1 && n <= levels.Length ? levels[n - 1] : null;
}
