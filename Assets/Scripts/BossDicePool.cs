using System.Collections.Generic;
using UnityEngine;

// The dice an act boss can offer. Beat a boss and you pick 1 of Choices dice, drawn at random from this list,
// so every run offers a different set. A die with Unlocked By set only joins the list once that difficulty has been
// cleared on this computer, so the pool also grows as players clear modes.
// Make one: right-click in the Project window > Create > Carry the One > Boss Dice Pool.
// Every die in here should be a number die (Role: Number): it adds a digit, and a zero to max HP.
[CreateAssetMenu(menuName = "Carry the One/Boss Dice Pool", fileName = "BossDice_")]
public class BossDicePool : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public DieDefinition die;
        [Tooltip("Optional: only in the pool once this difficulty has been cleared. Empty = always in the pool.")]
        public DifficultyDefinition unlockedBy;
    }

    public Entry[] dice;
    [Range(1, 5)] public int choices = 3;

    // Choices dice from the pool, all different, in random order. Fewer if the pool is smaller.
    public List<DieDefinition> Roll()
    {
        List<DieDefinition> open = new List<DieDefinition>();
        if (dice != null)
        {
            foreach (Entry e in dice)
            {
                if (e == null || e.die == null || open.Contains(e.die)) continue;
                if (e.unlockedBy != null && !RunSettings.HasCleared(e.unlockedBy)) continue;
                open.Add(e.die);
            }
        }
        for (int i = open.Count - 1; i > 0; i--)          // shuffle
        {
            int j = Random.Range(0, i + 1);
            DieDefinition t = open[i]; open[i] = open[j]; open[j] = t;
        }
        if (open.Count > choices) open.RemoveRange(choices, open.Count - choices);
        return open;
    }
}
