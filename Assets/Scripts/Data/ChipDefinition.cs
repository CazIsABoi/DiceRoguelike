using UnityEngine;

public enum ChipType { Reroll, Snatch, Nudge, ExtraThrow }

// A one-use item. You hold at most two, on the table in front of you (hold S to look down at them).
// Make one per chip: right-click in the Project window > Create > Carry the One > Chip.
//   Reroll:      click an enemy die, it rolls again where it sits (a face that doesn't fit its slot busts)
//   Snatch:      click an enemy operator, it counts as + this round
//   Nudge:       click an enemy number, it counts as one less this round (a 7 in the tens is 10 less)
//   Extra Throw: every one of your dice gets one more throw this round
[CreateAssetMenu(menuName = "Carry the One/Chip", fileName = "Chip_")]
public class ChipDefinition : ScriptableObject
{
    public ChipType type;
    public string displayName = "REROLL";
    [TextArea(2, 4)] public string line = "REROLL ONE ENEMY DIE";
    public Sprite icon;                      // the reward screen
    public Color color = Color.red;          // the chip on the table
    public string symbol = "";               // printed on the chip, only if the chip prefab has a TMP text on it (the icon is easier)
}
