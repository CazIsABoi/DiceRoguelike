using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One face in the custom dice editor. Left click: the next face. Right click: the one before.
// The prefab: a UI Image (the cell background) with this component, an Image child for the face icon,
// and a TMP child used only when a face has no icon.
public class FaceCell : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text label;
    public System.Action<int> OnStep;

    public void Show(Sprite sprite, string text, Color color)
    {
        bool hasIcon = sprite != null && icon != null;
        if (icon != null)
        {
            icon.enabled = hasIcon;
            icon.sprite = sprite;
            icon.color = color;
        }
        if (label != null)
        {
            label.enabled = !hasIcon;
            label.text = text;
            label.color = color;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnStep?.Invoke(eventData.button == PointerEventData.InputButton.Right ? -1 : 1);
        StartCoroutine(GameController.Pop(transform, Vector3.one, 1.25f, 0.18f));
    }
}