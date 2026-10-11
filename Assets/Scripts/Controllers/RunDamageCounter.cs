using System.Globalization;
using TMPro;
using UnityEngine;

// Every point of damage dealt this run, rolling up in a corner of the screen. For "one run, one number" clips.
public class RunDamageCounter : MonoBehaviour
{
    public static long Total;
    public static void Add(long amount) { if (amount > 0) Total += amount; }

    [SerializeField] private TMP_Text text;
    [SerializeField] private float catchUp = 3f;     // higher = the display catches up faster
    [SerializeField] private float popScale = 1.2f;

    private double shown;
    private long lastTotal;
    private Coroutine pop;

    private void Awake() { Total = 0; }   // statics survive a scene reload (Play Again), so reset here

    private void Update()
    {
        // Ease toward the real total: fast when far behind, slowing as it arrives. That's the "rolling up" feel.
        shown += (Total - shown) * (1f - Mathf.Exp(-catchUp * Time.unscaledDeltaTime));
        if (Total - shown < 1) shown = Total;
        text.text = ((long)shown).ToString("N0", CultureInfo.InvariantCulture);   // 1,234,567, not "1 234 567" on a Swedish PC

        if (Total != lastTotal)
        {
            lastTotal = Total;
            if (pop != null) StopCoroutine(pop);
            pop = StartCoroutine(GameController.Pop(text.transform, Vector3.one, popScale, 0.25f));
        }
    }
}