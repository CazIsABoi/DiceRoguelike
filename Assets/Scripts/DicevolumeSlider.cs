using UnityEngine;
using UnityEngine.UI;

// Put this on a UI Slider (Min 0, Max 1) in your menu or pause screen. It shows the saved dice volume
// and changes it as you drag. Works in any scene, the value is shared.
[RequireComponent(typeof(Slider))]
public class DiceVolumeSlider : MonoBehaviour
{
    [SerializeField] private AudioClip previewClip;   // optional: a clack plays as you let go, at the new volume
    [SerializeField] private AudioSource previewSource;

    private Slider slider;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
    }

    private void OnEnable()
    {
        slider.SetValueWithoutNotify(DiceAudio.Volume);
        slider.onValueChanged.AddListener(OnSlider);
    }

    private void OnDisable()
    {
        slider.onValueChanged.RemoveListener(OnSlider);
        DiceAudio.Save();
    }

    private void OnSlider(float value)
    {
        DiceAudio.Volume = value;
        if (previewClip != null && previewSource != null && !previewSource.isPlaying)
            previewSource.PlayOneShot(previewClip, value);
    }
}