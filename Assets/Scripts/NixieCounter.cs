using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Rolls random digits, then locks in the real ones one at a time, left to right (guide section 6)
public class NixieCounter : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip tickClip;            // the rolling click: plays many times a second
    [SerializeField] private AudioClip lockClip;            // optional: a heavier clunk when a digit locks in
    [SerializeField] private float tickInterval = 0.045f;   // seconds between clicks while rolling
    [SerializeField, Range(0f, 1f)] private float tickVolume = 0.5f;
    [SerializeField] private float minTime = 0.25f;     // the whole roll at intensity 0
    [SerializeField] private float maxTime = 1.0f;      // the whole roll at intensity 1 (the count already built the tension)
    [SerializeField] private float startPitch = 0.8f;
    [SerializeField] private float endPitch = 1.6f;

    private readonly StringBuilder builder = new StringBuilder();

    public IEnumerator Roll(int value, float intensity)
    {
        string target = value.ToString();

        int digits = 0;
        foreach (char c in target) if (char.IsDigit(c)) digits++;

        float duration = Mathf.Lerp(minTime, maxTime, intensity);
        float gap = duration / Mathf.Max(1, digits);
        int locked = 0;
        float timer = 0f;       // time since the last digit locked
        float tickTimer = 0f;   // time since the last click

        while (locked < digits)
        {
            if (SkipPressed) locked = digits;

            builder.Clear();
            int seen = 0;
            foreach (char c in target)
            {
                if (!char.IsDigit(c)) { builder.Append(c); continue; }
                builder.Append(seen < locked ? c : (char)('0' + Random.Range(0, 10)));
                seen++;
            }
            text.text = builder.ToString();

            timer += Time.deltaTime;
            tickTimer += Time.deltaTime;

            // How far through the whole roll we are, 0..1, counting the part of the current digit too
            float progress = Mathf.Clamp01((locked + timer / gap) / digits);

            if (tickTimer >= tickInterval)   // the rattle: steady clicks, rising with progress
            {
                tickTimer -= tickInterval;
                Play(tickClip, Mathf.Lerp(startPitch, endPitch, progress), tickVolume);
            }

            if (timer >= gap && locked < digits)   // a digit locks: the clunk
            {
                timer -= gap;
                locked++;
                Play(lockClip, Mathf.Lerp(startPitch, endPitch, locked / (float)digits), 1f);
            }
            yield return null;
        }
        text.text = target;
    }

    private void Play(AudioClip clip, float pitch, float volume)
    {
        if (audioSource == null || clip == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume);
    }

    private static bool SkipPressed =>
        (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
        (Keyboard.current != null && Keyboard.current.shiftKey.wasPressedThisFrame);
}