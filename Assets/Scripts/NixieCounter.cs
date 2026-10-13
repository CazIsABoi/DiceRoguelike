using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Rolls random digits, then locks in the real ones one at a time, left to right (guide section 6)
//
// Sound: the roll plays on its own voices (made in Awake), never on Audio Source itself. Changing an AudioSource's
// pitch bends EVERY sound playing on it, and the roll used to leave its source at End Pitch, so whatever played on
// that source next came out high. Audio Source is only read for its mixer group and volume now.
public class NixieCounter : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private AudioSource audioSource;       // its Output (mixer group) and volume are copied, nothing else
    [SerializeField] private AudioClip tickClip;            // the rolling click: plays many times a second
    [SerializeField] private AudioClip lockClip;            // optional: a heavier clunk when a digit locks in
    [SerializeField] private float tickInterval = 0.045f;   // seconds between clicks while rolling
    [SerializeField, Range(0f, 1f)] private float tickVolume = 0.5f;
    [SerializeField] private float minTime = 0.25f;     // the whole roll at intensity 0
    [SerializeField] private float maxTime = 1.0f;      // the whole roll at intensity 1 (the count already built the tension)
    [SerializeField] private float startPitch = 0.9f;
    [SerializeField] private float endPitch = 1.25f;    // was 1.6: the last clicks were shrill
    [SerializeField] private bool clunkOnLastDigit = false;   // off: the win's impact sound is the last digit's clunk

    private readonly StringBuilder builder = new StringBuilder();
    private AudioSource[] voices;                            // a few, taking turns, so a pitch change never bends a sound still ringing
    private int nextVoice;

    private void Awake()
    {
        voices = new AudioSource[3];
        for (int i = 0; i < voices.Length; i++)
        {
            AudioSource v = gameObject.AddComponent<AudioSource>();
            v.playOnAwake = false;
            v.spatialBlend = 0f;   // 2D: part of the screen, not the room
            if (audioSource != null)
            {
                v.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;   // so the SFX slider still turns it down
                v.volume = audioSource.volume;
            }
            voices[i] = v;
        }
    }

    public IEnumerator Roll(int value, float intensity, System.Action<float> onProgress = null, float speed = 1f)
    {
        string target = value.ToString();

        int digits = 0;
        foreach (char c in target) if (char.IsDigit(c)) digits++;
        if (digits == 0) { text.text = target; yield break; }

        float duration = Mathf.Lerp(minTime, maxTime, intensity) / Mathf.Max(0.05f, speed);
        float gap = duration / digits;
        int locked = 0;
        float timer = 0f;       // time since the last digit locked
        float tickTimer = 0f;   // time since the last click

        while (true)
        {
            if (SkipPressed) locked = digits;

            timer += Time.deltaTime;
            tickTimer += Time.deltaTime;

            // Lock first, then draw: the digit you hear lock is the digit you see lock, in the same frame
            if (timer >= gap && locked < digits)
            {
                timer -= gap;
                locked++;
                if (locked < digits || clunkOnLastDigit)
                    Play(lockClip, Mathf.Lerp(startPitch, endPitch, locked / (float)digits), 1f);
            }

            // All locked: the real number goes up right now and the roll is over (no extra frame of a random digit)
            if (locked >= digits) break;

            builder.Clear();
            int seen = 0;
            foreach (char c in target)
            {
                if (!char.IsDigit(c)) { builder.Append(c); continue; }
                builder.Append(seen < locked ? c : (char)('0' + Random.Range(0, 10)));
                seen++;
            }
            text.text = builder.ToString();

            // How far through the whole roll we are, 0..1, counting the part of the current digit too
            float progress = Mathf.Clamp01((locked + timer / gap) / digits);
            onProgress?.Invoke(progress);

            if (tickTimer >= tickInterval)   // the rattle: steady clicks, rising with progress
            {
                tickTimer -= tickInterval;
                Play(tickClip, Mathf.Lerp(startPitch, endPitch, progress), tickVolume);
            }
            yield return null;
        }
        onProgress?.Invoke(1f);
        text.text = target;
    }

    private void Play(AudioClip clip, float pitch, float volume)
    {
        if (clip == null || voices == null) return;
        AudioSource voice = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        voice.pitch = pitch;
        voice.PlayOneShot(clip, volume);
    }

    private static bool SkipPressed =>
        (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
        (Keyboard.current != null && Keyboard.current.shiftKey.wasPressedThisFrame);
}
