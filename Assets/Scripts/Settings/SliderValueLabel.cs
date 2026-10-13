using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows a slider's value as a percentage ("50%") in a text next to it.
// Put it on the Slider, and drag the text in.
[RequireComponent(typeof(Slider))]
public class SliderValueLabel : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private string suffix = "%";

    private Slider slider;
    private int shown = -1;

    private void Awake() => slider = GetComponent<Slider>();

    // Checked every frame (cheap) instead of on "value changed", because SettingsMenu sets the sliders
    // without firing that event when the menu opens
    private void Update()
    {
        float range = slider.maxValue - slider.minValue;
        int percent = Mathf.RoundToInt(range > 0f ? (slider.value - slider.minValue) / range * 100f : 0f);
        if (percent == shown) return;
        shown = percent;
        label.text = percent + suffix;
    }
}