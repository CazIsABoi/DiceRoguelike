using System.Collections;
using TMPro;
using UnityEngine;

public class Subtitles : MonoBehaviour
{
    private static Subtitles instance;

    [SerializeField] private TMP_Text dialogueLabel;
    [SerializeField] private TMP_Text hintLabel;
    [SerializeField] private float baseHold = 1.5f;
    [SerializeField] private float holdPerChar = 0.05f;

    private Coroutine dialogueRoutine, hintRoutine;

    private void Awake()
    {
        instance = this;
        dialogueLabel.text = "";
        hintLabel.text = "";
    }

    public static void Dialogue(string speaker, string line)
    {
        if (instance == null) return;
        instance.Run(ref instance.dialogueRoutine, instance.dialogueLabel, $"<b>{speaker.ToUpper()}:</b> {line}", line.Length);
    }

    public static void Hint(string message)
    {
        if (instance == null) return;
        instance.Run(ref instance.hintRoutine, instance.hintLabel, message, message.Length + 20);   // hints stay a bit longer
    }

    private void Run(ref Coroutine routine, TMP_Text label, string text, int length)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ShowRoutine(label, text, baseHold + length * holdPerChar));
    }

    private IEnumerator ShowRoutine(TMP_Text label, string text, float hold)
    {
        label.text = text;
        label.alpha = 1f;
        yield return new WaitForSeconds(hold);

        for (float t = 0; t < 0.3f; t += Time.deltaTime)
        {
            label.alpha = 1f - t / 0.3f;
            yield return null;
        }
        label.text = "";
    }
}