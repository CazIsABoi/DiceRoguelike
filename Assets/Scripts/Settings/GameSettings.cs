using UnityEngine;
using UnityEngine.Audio;

// Volumes, saved on this computer (0 to 1). SettingsMenu changes them; Apply pushes them out.
// Master goes through AudioListener.volume, so it works with no setup at all.
// Music and Sound Effects need an Audio Mixer with exposed parameters "MusicVolume" and "SfxVolume".
// Dice: make DiceAudio.Volume return GameSettings.Dice (see the setup notes).
public static class GameSettings
{
    public static AudioMixer Mixer;   // SettingsMenu hands this over in Awake

    public static float Master { get => Get("Vol_Master"); set => Set("Vol_Master", value); }
    public static float Music  { get => Get("Vol_Music");  set => Set("Vol_Music", value); }
    public static float Sfx    { get => Get("Vol_Sfx");    set => Set("Vol_Sfx", value); }
    public static float Dice   { get => Get("Vol_Dice");   set => Set("Vol_Dice", value); }

    private static float Get(string key) => PlayerPrefs.GetFloat(key, 1f);

    private static void Set(string key, float value)
    {
        PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
        PlayerPrefs.Save();
        Apply();
    }

    public static void Apply()
    {
        AudioListener.volume = Master;
        if (Mixer != null)
        {
            Mixer.SetFloat("MusicVolume", ToDecibels(Music));
            Mixer.SetFloat("SfxVolume", ToDecibels(Sfx));
        }
    }

    // A slider at 0.5 should sound half as loud, so the mixer gets decibels, not the raw number
    private static float ToDecibels(float v) => v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyOnStart() => Apply();
}
