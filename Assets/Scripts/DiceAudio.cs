using UnityEngine;

// One volume for every dice sound (grab, shake, hits) on both sides of the table. 0 = silent, 1 = full.
// It's saved, so it stays between sessions (in WebGL too).
//   DiceAudio.Volume = 0.5f;   // from anywhere
public static class DiceAudio
{

    public static float Volume => GameSettings.Dice;

    // Call once when leaving a settings screen (or on quit), so the value is written to disk now
    public static void Save() => PlayerPrefs.Save();
}