using System.Collections.Generic;
using UnityEngine;

public class Tutorial : MonoBehaviour
{
    private static Tutorial instance;
    private static readonly HashSet<string> shown = new HashSet<string>();   // survives Play Again

    [SerializeField] private StripDisplay strip;
    [SerializeField] private Color hintColor = new Color32(0xFF, 0x71, 0x34, 0xFF);

    private void Awake() => instance = this;

    public static void Hint(string key, string message)
    {
        if (instance == null || !shown.Add(key)) return;   // Add returns false if it was already shown
        instance.strip.ShowScrolling(message, instance.hintColor);
    }
}