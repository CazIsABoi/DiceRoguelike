using TMPro;
using UnityEngine;

// Shows a side's HP on nixie tubes, and counts it down (or up) instead of jumping, with a ticking sound.
// The count slows down at the end, so a big hit on a weak enemy turns into "will it stop before 0?"
//
// SETUP: put it on the tube row. Drag in the DiceSide whose HP it shows (PlayerController or EnemyController),
// and the tube texts LEFT TO RIGHT, one digit per tube. Switch off whatever wrote the HP into the tubes before.
public class NixieHealth : MonoBehaviour
{
    [SerializeField] private DiceSide side;
    [SerializeField] private TMP_Text[] tubes;              // left to right, one digit each
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip tickClip;            // a short click, played as the digits change
    [SerializeField] private AudioClip zeroClip;            // optional: the clunk when it lands on 0
    [SerializeField] private float minTime = 0.35f;         // a tiny change takes this long
    [SerializeField] private float maxTime = 1.4f;          // losing (or healing) the whole bar takes this long
    [SerializeField] private float tickInterval = 0.05f;    // at most one click this often
    [SerializeField] private float highPitch = 1.4f;
    [SerializeField] private float lowPitch = 0.7f;

    public bool Ticking => elapsed < duration;              // GameController waits on this before the kill moment

    private int from, target, shown = -1;
    private float elapsed, duration;
    private float lastTick;
    private bool ready;

    private void OnEnable()
    {
        if (side != null) side.OnHealthChanged += OnHealthChanged;
    }

    private void OnDisable()
    {
        if (side != null) side.OnHealthChanged -= OnHealthChanged;
    }

    private void Start()
    {
        if (!ready) Snap(side != null ? side.Health : 0);   // first value: no animation, just show it
    }

    private void OnHealthChanged(int current, int max)
    {
        if (!ready) { Snap(current); return; }
        from = shown;                                       // start from what's on the tubes NOW, even mid-count
        target = current;
        elapsed = 0f;
        float share = max > 0 ? Mathf.Abs(target - from) / (float)max : 1f;
        duration = Mathf.Lerp(minTime, maxTime, Mathf.Clamp01(share));
    }

    private void Update()
    {
        if (!Ticking) return;

        elapsed += Time.deltaTime;
        float k = Mathf.Clamp01(elapsed / duration);
        float eased = 1f - (1f - k) * (1f - k) * (1f - k);  // fast at first, crawls at the end
        int value = Mathf.RoundToInt(Mathf.Lerp(from, target, eased));
        if (k >= 1f) value = target;

        if (value != shown)
        {
            Write(value);
            if (Time.time - lastTick >= tickInterval)
            {
                lastTick = Time.time;
                bool losing = target < from;
                // losing: the pitch falls as it drains. Healing: it rises.
                Play(tickClip, losing ? Mathf.Lerp(highPitch, lowPitch, k) : Mathf.Lerp(lowPitch, highPitch, k), 0.6f);
            }
        }

        if (k >= 1f && target == 0 && from > 0) Play(zeroClip, 1f, 1f);
    }

    private void Snap(int value)
    {
        ready = true;
        from = target = value;
        elapsed = duration = 0f;
        Write(value);
    }

    // 437 on five tubes: two unlit tubes, then 4, 3, 7
    private void Write(int value)
    {
        shown = value;
        string digits = Mathf.Max(0, value).ToString();
        int offset = tubes.Length - digits.Length;           // how many tubes on the left stay dark
        for (int i = 0; i < tubes.Length; i++)
        {
            if (tubes[i] == null) continue;
            int d = i - offset;
            tubes[i].text = d >= 0 && d < digits.Length ? digits[d].ToString() : "";
        }
    }

    private void Play(AudioClip clip, float pitch, float volume)
    {
        if (audioSource == null || clip == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume);
    }
}