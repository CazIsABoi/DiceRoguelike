using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;

// Plays every count on the monitor: both equations, what the table does to them (a bust, the distance to the
// target, going over the limit), and the margin. One place for the look, the timing and the sound, so every count
// feels like the same machine.
//
// A count is a list of BEATS. A beat is one small sum on screen ("12×4", "520-500", "55-5"):
//   1. READ   the part about to change, token by token (each token pops)
//   2. COUNT  the answer up or down like an odometer, ticking as it goes. Bigger changes take longer.
//   3. LAND   a clunk, a pop, a camera kick, and the pitch climbs one note
// The pitch keeps climbing through the whole round (the enemy, then you, then the margin), so the end of a round
// sounds like a scale going up. GameController calls NewRound() to send it back to the bottom.
//
// SETUP: add it to the monitor (or to the GameController object) and drag in the sounds. GameController finds it.
public class CountShow : MonoBehaviour
{
    [SerializeField] private GameController game;               // empty: found by itself

    [Header("Look")]
    [SerializeField] private Color countColor = new Color32(0xFF, 0xD5, 0x4A, 0xFF);     // the part being counted
    [SerializeField] private Color tableColor = new Color32(0xFF, 0x71, 0x34, 0xFF);     // distances and "OFF"
    [SerializeField] private Color bustColor = new Color32(0xFF, 0x4D, 0x40, 0xFF);      // busts, "OVER", "MAX"
    [SerializeField] private Color knockoutColor = new Color32(0xFF, 0x2E, 0x2E, 0xFF);  // the damage, once it passes the enemy's HP
    [SerializeField] private float readPopSize = 170f;          // percent: a token as it's read...
    [SerializeField] private float readHoldSize = 125f;         // ...shrinking to this
    [SerializeField] private float growSize = 150f;             // percent: a huge count swells up to this mid-count (a small one barely grows)
    [SerializeField] private float landPop = 1.4f;              // the punch when a huge count lands (a small one gets 1.12)

    [Header("Timing")]
    [SerializeField] private float pace = 1f;                   // everything takes this many times as long: 1.3 = slower, 0.7 = snappier
    [SerializeField] private float secondsPerNumber = 0.06f;   // a small count shows each number at least this long
    [SerializeField] private int maxTicks = 20;                // past this many numbers it just runs (big counts blur, that's fine)
    [SerializeField] private float readTime = 0.1f;             // seconds per token
    [SerializeField] private float minCountTime = 0.15f;        // the shortest count
    [SerializeField] private float countTimePerDigit = 0.22f;   // each digit of change adds this much: +7 takes 0.35 s, +9,702 takes 1 s
    [SerializeField] private float maxCountTime = 1.2f;
    [SerializeField] private float holdTime = 0.1f;             // a breath after each landing
    [SerializeField] private float fastForward = 4f;            // holding Shift or the mouse plays it this much faster
    [SerializeField] private float countStretch = 1f;     // every count takes this many times as long (reads and pauses stay the same)
    [SerializeField] private float bigCountExtra = 2f;    // extra seconds a big count gets, on top (scaled by how big it is)

    [Header("Sound")]
    [SerializeField] private AudioClip[] readClips;             // a soft tick per token read
    [SerializeField] private AudioClip[] tickClips;             // the rattle while a number counts
    [SerializeField] private AudioClip[] landClips;             // the clunk when it lands
    [SerializeField] private AudioClip digitClip;               // optional: the number grows a digit (99 → 100), or hits a target exactly
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
    [SerializeField] private float tickInterval = 0.035f;       // at most one tick this often
    [SerializeField] private float basePitch = 0.8f;            // the first note of a round
    [SerializeField] private AudioClip suspenseLoop;              // loops while a big count builds (drumroll, riser, slot whir)
    [SerializeField, Range(0f, 1f)] private float suspenseVolume = 0.6f;
    [SerializeField] private float suspenseLowPitch = 0.9f;       // pitch as it starts...
    [SerializeField] private float suspenseHighPitch = 1.3f;      // ...and at the top of the biggest count
    [SerializeField] private float suspenseFadeIn = 0.25f;        // seconds
    [SerializeField] private float suspenseFadeOut = 0.12f;       // short, so the finish sound lands in the gap
    [SerializeField, Range(1, 8)] private int maxNote = 4;   // how far up the scale one side's count climbs
    [SerializeField] private AudioMixerGroup output;

    private AudioSource suspense;
    private float suspenseTarget, suspenseLevel;                  // 0..1: where it's headed, where it is

    [Header("Big Counts")]
    [SerializeField] private CountZoom zoom;                  // on the Main Camera. Empty: found by itself
    [SerializeField] private float maxZoom = 12f;             // degrees of field of view the biggest count pushes in
    [SerializeField] private float zoomFromDigits = 2f;       // smaller counts don't zoom (2 digits of change = about +100)
    [SerializeField] private float fullZoomDigits = 4f;       // this big zooms all the way (4 = about +10,000)
    [SerializeField] private AudioClip[] finishClips;         // the payoff when a big count lands
    [SerializeField, Range(0f, 1f)] private float finishVolume = 1f;

    // GameController slows this down for a photo finish
    public float Speed { get; set; } = 1f;

    // A major pentatonic scale in semitones: any of these notes sound good one after another, in any order
    private static readonly int[] Scale = { 0, 2, 4, 7, 9, 12, 14, 16, 19 };
    private int note;                                           // how far up the scale this round has climbed

    private AudioSource[] voices;                               // a few sources taking turns, so fast ticks don't cut each other off
    private int nextVoice;
    private readonly StringBuilder builder = new StringBuilder();   // reused: a new string every frame is garbage
    private Coroutine popRoutine;
    private Transform popTarget;
    private string countHex, tableHex, bustHex, knockoutHex;

    // One small sum on screen, and what it turns into
    private class Beat
    {
        public List<int> numbers;      // what's on screen before it, e.g. [12, 4, 7]
        public List<Operator> ops;     //                                 [×, +]
        public int at;                 // the number that changes: 0 is the 12
        public bool pair;              // true: numbers[at] and numbers[at + 1] become one number. false: numbers[at] just changes
        public int from, to;           // the count runs from → to: 12 → 48, or 0 → 63 for a distance
        public int known = -1;         // a number the player just watched land: it isn't read again
        public string color;           // "#FFD54A"
        public string tail = "";       // after the numbers: the bust, "OVER", "MAX"
    }

    private void Awake()
    {
        if (game == null) game = FindAnyObjectByType<GameController>();
        if (zoom == null) zoom = FindAnyObjectByType<CountZoom>();
        CacheColors();

        voices = new AudioSource[6];
        for (int i = 0; i < voices.Length; i++)
        {
            voices[i] = gameObject.AddComponent<AudioSource>();
            voices[i].playOnAwake = false;
            voices[i].spatialBlend = 0f;   // 2D: the count is part of the screen, not the room
            voices[i].outputAudioMixerGroup = output;
        }
        suspense = gameObject.AddComponent<AudioSource>();
        suspense.playOnAwake = false;
        suspense.loop = true;
        suspense.spatialBlend = 0f;
        suspense.outputAudioMixerGroup = output;
        suspense.clip = suspenseLoop;
    }
    private void Update()
    {
        if (suspense == null || suspenseLoop == null) return;

        float time = suspenseTarget > suspenseLevel ? suspenseFadeIn : suspenseFadeOut;
        suspenseLevel = Mathf.MoveTowards(suspenseLevel, suspenseTarget, Time.unscaledDeltaTime / Mathf.Max(0.01f, time));

        suspense.volume = suspenseLevel * suspenseVolume * volume;
        suspense.pitch = Mathf.Lerp(suspenseLowPitch, suspenseHighPitch, suspenseLevel);

        if (suspenseLevel > 0.001f && !suspense.isPlaying) suspense.Play();
        else if (suspenseLevel <= 0.001f && suspense.isPlaying) suspense.Stop();
    }

    private void OnValidate() => CacheColors();   // colours changed in the Inspector show up next count

    private void CacheColors()
    {
        countHex = "#" + ColorUtility.ToHtmlStringRGB(countColor);
        tableHex = "#" + ColorUtility.ToHtmlStringRGB(tableColor);
        bustHex = "#" + ColorUtility.ToHtmlStringRGB(bustColor);
        knockoutHex = "#" + ColorUtility.ToHtmlStringRGB(knockoutColor);
    }

    // ---------- What GameController and DiceSide call ----------

    // A round's counting starts: the pitch goes back to the bottom
    public void NewRound()
    {
        Release();
    }

    // DiceSide.PlayCount: one side's equation, then what the table does to the result.
    // Ends showing the number the margin is about to use: "55" at a High table, "63 OFF" at a Target table.
    public IEnumerator PlaySide(TMP_Text text, List<GameController.CountStep> steps, int bust,
                                System.Action onLand, bool isPlayer = false)
    {
        note = 0;
        int result = steps[steps.Count - 1].numbers[0];
        string bustTail = bust > 0 ? Paint(game.BustText(bust), bustHex) : "";

        List<Beat> beats = FromSteps(steps, countHex, bustTail);
        int final = AddTableBeats(beats, result, bust, bustTail, out string tag, out bool exact);

        int expected = game.MarginNumber(result, bust);
        if (final != expected)
            Debug.LogWarning($"[Carry the One] CountShow ends on {final}, but the table says {expected}. " +
                             "Did TableScore change? Make CountShow.AddTableBeats do the same thing.");

        yield return PlayBeats(text, beats, onLand, true, null, 0f, 0, null,
                               allowSuspense: isPlayer, allowZoomFinish: false);

        text.text = final + tag;
        if (exact) yield return Stamp(text, 1.5f, holdTime * 4f, true);
        else if (tag.Length > 0 || beats.Count == 0) yield return Stamp(text, 1.2f, holdTime, false);
        Release();
    }

    // GameController.PlayMargin: "63-20", the gap counted up from 0 (then × the stake at a Low table).
    // slotRoll: a big win finishes on the nixie roll instead. knockoutAt: the count passing this calls onKnockout (0 = never).
    public IEnumerator PlayMargin(TMP_Text text, List<GameController.CountStep> steps, NixieCounter slotRoll, float intensity,
                                  int knockoutAt, System.Action onKnockout, bool isPlayerDiff = false)
    {
        List<Beat> beats = FromSteps(steps, countHex, "");
        if (beats.Count > 0) beats[0].from = 0;

        // Margin: suspense no, zoom/snare only on player's positive diff
        yield return PlayBeats(text, beats, null, false, slotRoll, intensity, knockoutAt, onKnockout,
                               allowSuspense: false, allowZoomFinish: isPlayerDiff);

        StopPop();
        Release();
        if (slotRoll == null) text.text = steps[steps.Count - 1].numbers[0].ToString();
    }

    // ---------- Building the beats ----------

    // GameController's count steps ("12×4+7" → "48+7" → "55") as beats
    private static List<Beat> FromSteps(List<GameController.CountStep> steps, string color, string tail)
    {
        List<Beat> beats = new List<Beat>();
        foreach (GameController.CountStep step in steps)
        {
            if (step.next < 0) continue;   // the last step is the answer, not a sum
            int a = step.numbers[step.next], b = step.numbers[step.next + 1];
            beats.Add(new Beat
            {
                numbers = step.numbers,
                ops = step.ops,
                at = step.next,
                pair = true,
                from = a,
                to = GameController.Apply(step.ops[step.next], a, b),
                known = beats.Count > 0 ? beats[beats.Count - 1].at : -1,   // where the last answer landed
                color = color,
                tail = tail
            });
        }
        return beats;
    }

    // What the table does to a finished result, as more beats. Returns the number it ends on, which must match
    // GameController.MarginNumber (PlaySide checks). tag goes after that number: " OFF", " OVER".
    private int AddTableBeats(List<Beat> beats, int result, int bust, string bustTail, out string tag, out bool exact)
    {
        int v = result;
        tag = "";
        exact = false;

        switch (game.Table)
        {
            case TableType.High:   // a bust comes off the result
                if (bust > 0) { beats.Add(Pair(v, Operator.Subtract, bust, v, 0, bustHex, "")); v -= bust; }
                break;

            case TableType.Limit:   // over the limit: the whole result drains away
                if (v > game.Target)
                {
                    tag = Paint(" OVER", bustHex);
                    beats.Add(Change(v, v, 0, bustHex, tag + bustTail));
                    v = 0;
                }
                else exact = v == game.Target;
                if (bust > 0) { beats.Add(Pair(v, Operator.Subtract, bust, v, 0, bustHex, tag)); v -= bust; exact = false; }
                if (exact) tag = Paint(" EXACT!", countHex);
                break;

            case TableType.Target:   // how far from the target: bigger minus smaller, counted up from 0
                if (v >= game.Target) beats.Add(Pair(v, Operator.Subtract, game.Target, 0, 0, tableHex, bustTail));
                else beats.Add(Pair(game.Target, Operator.Subtract, v, 0, 1, tableHex, bustTail));
                v = Mathf.Abs(v - game.Target);
                if (bust > 0) { beats.Add(Pair(v, Operator.Add, bust, v, 0, bustHex, "")); v += bust; }
                exact = v == 0;
                tag = exact ? Paint(" EXACT!", countHex) : Paint(" OFF", tableHex);
                break;

            case TableType.Low:   // how far from 0: a minus sign just drops off
                if (v < 0) { beats.Add(Change(v, -v, -v, tableHex, bustTail)); v = -v; }
                if (bust > 0) { beats.Add(Pair(v, Operator.Add, bust, v, 0, bustHex, "")); v += bust; }
                if (v > game.LowCap) { beats.Add(Change(v, v, game.LowCap, bustHex, Paint(" MAX", bustHex))); v = game.LowCap; }
                exact = v == 0;
                tag = exact ? Paint(" EXACT!", countHex) : Paint(" OFF", tableHex);
                break;
        }
        return v;
    }

    // "a op b" becoming one number. known: which of the two is the result that just landed (0 = a, 1 = b, -1 = neither)
    private static Beat Pair(int a, Operator op, int b, int from, int known, string color, string tail) => new Beat
    {
        numbers = new List<int> { a, b },
        ops = new List<Operator> { op },
        at = 0,
        pair = true,
        from = from,
        to = GameController.Apply(op, a, b),
        known = known,
        color = color,
        tail = tail
    };

    // One number turning into another without a sum: over the limit (520 → 0), a minus sign dropping off, the Low cap
    private static Beat Change(int shown, int from, int to, string color, string tail) => new Beat
    {
        numbers = new List<int> { shown },
        ops = new List<Operator>(),
        at = 0,
        pair = false,
        from = from,
        to = to,
        color = color,
        tail = tail
    };

    // ---------- Playing them ----------

    private IEnumerator PlayBeats(TMP_Text text, List<Beat> beats, System.Action onLand, bool landLast,
                                  NixieCounter slotRoll, float intensity, int knockoutAt, System.Action onKnockout,
                                  bool allowSuspense = false, bool allowZoomFinish = false)
    {
        for (int i = 0; i < beats.Count; i++)
        {
            Beat beat = beats[i];
            bool last = i == beats.Count - 1;

            int first = beat.at * 2;
            int end = beat.pair ? first + 2 : first;
            for (int token = first; token <= end; token++)
            {
                if (beat.pair && token % 2 == 0 && token / 2 == beat.known) continue;
                yield return Read(text, beat, token);
            }

            if (last && slotRoll != null)
            {
                float big = allowZoomFinish ? Bigness(beat) : 0f;
                System.Action<float> zoomDrive = null;
                if (allowZoomFinish && zoom != null && big > 0f)
                    zoomDrive = p => zoom.Target = Mathf.Max(zoom.Target, maxZoom * big * p);

                yield return slotRoll.Roll(beat.to, intensity, zoomDrive, Speed);
                if (knockoutAt > 0 && beat.to >= knockoutAt) onKnockout?.Invoke();
            }
            else yield return Count(text, beat, last ? knockoutAt : 0, onKnockout, allowSuspense, allowZoomFinish);

            if (!last || landLast) yield return Wait(holdTime);   // the margin's last number goes straight into the hit
            else Finish(beat, allowZoomFinish);
            yield return Wait(holdTime);
        }
    }

    private IEnumerator Read(TMP_Text text, Beat beat, int token)
    {
        Play(readClips, Note(note), 0.6f);
        int lastHighlighted = beat.pair ? beat.at + 1 : beat.at;
        for (float t = 0f; t < readTime; t += Time.deltaTime * CountSpeed)
        {
            float k = t / readTime;
            k = 1f - (1f - k) * (1f - k);   // fast, then settles
            text.text = Render(beat.numbers, beat.ops, beat.at, lastHighlighted, beat.color, token,
                               Mathf.Lerp(readPopSize, readHoldSize, k), beat.tail);
            yield return null;
        }
    }

    // The odometer: every number between from and to goes past, the pitch slides up one note on the way
    private IEnumerator Count(TMP_Text text, Beat beat, int knockoutAt, System.Action onKnockout,
                          bool allowSuspense = false, bool allowZoomFinish = false)
    {
        // What's on screen while it counts: the pair has become one number, at beat.at
        List<int> numbers = new List<int>(beat.numbers);
        List<Operator> ops = new List<Operator>(beat.ops);
        if (beat.pair)
        {
            numbers.RemoveAt(beat.at + 1);
            ops.RemoveAt(beat.at);
        }

        if (beat.from == beat.to)   // nothing to count (a minus sign dropping off): just show it
        {
            numbers[beat.at] = beat.to;
            text.text = Render(numbers, ops, beat.at, beat.at, beat.color, -1, 100f, beat.tail);
            yield break;
        }

        float digits = Digits(beat);
        float big = Bigness(beat);                                                    // 0 for small counts, up to 1 for huge ones
        float duration = Mathf.Min(maxCountTime, minCountTime + countTimePerDigit * digits);
        duration = Mathf.Max(duration, Mathf.Min(Mathf.Abs(beat.to - beat.from), maxTicks) * secondsPerNumber);   // small counts: every number gets its moment
        duration = duration * countStretch + big * bigCountExtra;
        float grow = Mathf.Lerp(100f, growSize, Mathf.Clamp01(digits / 4f));
        string color = beat.color;
        bool crossed = knockoutAt <= 0;
        int shown = beat.from;
        float lastTick = -1f;

        for (float t = 0f; ; t += Time.deltaTime * CountSpeed)
        {
            float k = Mathf.Clamp01(t / duration);
            float eased = k * k * (3f - 2f * k);

            if (allowSuspense && big > 0f)
                suspenseTarget = Mathf.Max(suspenseTarget, big * eased);

            if (allowZoomFinish && big > 0f && zoom != null)
                zoom.Target = Mathf.Max(zoom.Target, maxZoom * big * eased);
            // double, so a count into the millions doesn't lose digits on the way
            int value = k >= 1f ? beat.to : (int)System.Math.Round(beat.from + ((double)beat.to - beat.from) * eased);

            if (!crossed && value >= knockoutAt)   // this hit kills: the count changes colour from here on
            {
                crossed = true;
                color = knockoutHex;
                onKnockout?.Invoke();
            }

            if (value != shown)
            {
                if (DigitCount(value) > DigitCount(shown) && DigitCount(value) >= 3) NewDigit(text);   // 99 → 100, 999 → 1000
                if (Time.unscaledTime - lastTick >= tickInterval)
                {
                    lastTick = Time.unscaledTime;
                    Play(tickClips, Mathf.Lerp(Note(note), Note(note + 1), eased), 0.35f);
                }
                shown = value;
            }

            // It swells through the blur and settles back to normal size as it lands
            numbers[beat.at] = value;
            text.text = Render(numbers, ops, beat.at, beat.at, color, beat.at * 2, Mathf.Lerp(100f, grow, Mathf.Sin(eased * Mathf.PI)), beat.tail);

            if (k >= 1f) break;
            yield return null;
        }
    }

    private void Land(TMP_Text text, Beat beat, System.Action onLand, bool allowZoomFinish = false)
    {
        note = Mathf.Min(note + 1, maxNote);
        Play(landClips, Note(note), 0.9f);
        Pop(text, Mathf.Lerp(1.12f, landPop, Mathf.Clamp01(Digits(beat) / 4f)));
        onLand?.Invoke();
        if (Bigness(beat) > 0f) suspenseTarget = 0f;
        Finish(beat, allowZoomFinish);
    }

    private void NewDigit(TMP_Text text)
    {
        Play(digitClip, Note(note + 1), 0.8f);
        Pop(text, 1.18f);
    }

    // The final word ("OFF", "EXACT!") stamps on
    private IEnumerator Stamp(TMP_Text text, float size, float hold, bool big)
    {
        if (big && digitClip != null) Play(digitClip, Note(note + 1), 1f);
        else Play(readClips, Note(note), 0.7f);
        Pop(text, size);
        yield return Wait(hold);
    }

    // ---------- Text ----------

    // "12×4+7", numbers hiFrom..hiTo coloured, one token (focus) drawn at focusSize percent, then the tail
    private string Render(List<int> numbers, List<Operator> ops, int hiFrom, int hiTo, string color, int focus, float focusSize, string tail)
    {
        builder.Clear();
        int token = 0;
        for (int i = 0; i < numbers.Count; i++)
        {
            if (i == hiFrom) builder.Append("<color=").Append(color).Append('>');
            AppendToken(numbers[i].ToString(), token++ == focus, focusSize);
            if (i == hiTo) builder.Append("</color>");
            if (i < ops.Count) AppendToken(GameController.OpSymbol(ops[i]), token++ == focus, focusSize);
        }
        builder.Append(tail);
        return builder.ToString();
    }

    private void AppendToken(string token, bool focused, float size)
    {
        if (focused) builder.Append("<size=").Append(Mathf.RoundToInt(size)).Append("%>").Append(token).Append("</size>");
        else builder.Append(token);
    }

    // Coloured, with no-break spaces so a tag never wraps onto its own line
    private static string Paint(string text, string hex) => $"<color={hex}>{text.Replace(' ', '\u00A0')}</color>";

    // ---------- Helpers ----------

    // 0..1: how big a count this is. 0 below zoomFromDigits, 1 at fullZoomDigits and up
    private float Bigness(Beat beat) => Mathf.InverseLerp(zoomFromDigits, fullZoomDigits, Digits(beat));

    // The payoff sound for a big count landing
    private void Finish(Beat beat, bool allowZoomFinish = false)
    {
        float big = Bigness(beat);
        if (allowZoomFinish && big > 0f)
        {
            Play(finishClips, 1f, finishVolume * Mathf.Lerp(0.6f, 1f, big));
            if (zoom != null) zoom.Target = 0f;   // start the zoom-out as the snare hits
        }
    }
    private void Release()
    {
        if (zoom != null) zoom.Target = 0f;
        suspenseTarget = 0f;
    }
    private float CountSpeed
    {
        get
        {
            bool held = (Keyboard.current != null && Keyboard.current.shiftKey.isPressed)
                     || (Mouse.current != null && Mouse.current.leftButton.isPressed);
            return Speed * (held ? fastForward : 1f) / Mathf.Max(0.05f, pace);
        }
    }

    private IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime * CountSpeed) yield return null;
    }

    // How many digits of change a beat counts: +7 → 0.9, +36 → 1.6, +9,702 → 4
    private static float Digits(Beat beat) => Mathf.Log10(Mathf.Abs((float)beat.to - beat.from) + 1f);

    private static int DigitCount(int value)
    {
        long v = System.Math.Abs((long)value);
        int n = 1;
        while (v >= 10) { v /= 10; n++; }
        return n;
    }

    private float Note(int i) => basePitch * Mathf.Pow(2f, Scale[Mathf.Clamp(i, 0, maxNote)] / 12f);

    private void Play(AudioClip[] clips, float pitch, float loudness)
    {
        if (clips == null || clips.Length == 0) return;
        Play(clips[Random.Range(0, clips.Length)], pitch, loudness);
    }

    private void Play(AudioClip clip, float pitch, float loudness)
    {
        if (clip == null || voices == null) return;
        AudioSource voice = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        voice.pitch = pitch;
        voice.PlayOneShot(clip, loudness * volume);
    }

    private void Pop(TMP_Text text, float size)
    {
        StopPop();
        popTarget = text.transform;
        popRoutine = StartCoroutine(GameController.Pop(text.transform, Vector3.one, size, 0.22f));
    }

    private void StopPop()
    {
        if (popRoutine != null) StopCoroutine(popRoutine);
        popRoutine = null;
        if (popTarget != null) popTarget.localScale = Vector3.one;
        popTarget = null;
    }
}