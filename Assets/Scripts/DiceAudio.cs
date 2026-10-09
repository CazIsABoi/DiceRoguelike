using UnityEngine;

// One volume for every dice sound (grab, shake, hits) on both sides of the table. 0 = silent, 1 = full.
// It's saved, so it stays between sessions (in WebGL too).
//   DiceAudio.Volume = 0.5f;   // from anywhere
public static class DiceAudio
{
    private const string Key = "DiceVolume";
    private static float volume = -1f;   // -1 = not loaded yet

    public static event System.Action<float> OnChanged;

    public static float Volume
    {
        get
        {
            if (volume < 0f) volume = PlayerPrefs.GetFloat(Key, 1f);
            return volume;
        }
        set
        {
            volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(Key, volume);
            OnChanged?.Invoke(volume);
        }
    }

    // Call once when leaving a settings screen (or on quit), so the value is written to disk now
    public static void Save() => PlayerPrefs.Save();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { volume = -1f; OnChanged = null; }   // in case domain reload is off
}