using UnityEngine;
using UnityEngine.EventSystems;

// Sits on each icon in the relic bar (GameController adds it). Hover an icon to see what the relic does.
// Needs an EventSystem in the scene and a Graphic Raycaster on the canvas, which a UI canvas has by default.
public class RelicIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public RelicDefinition Relic { get; set; }
    public System.Action<RelicDefinition> OnHover;   // called with null when the mouse leaves

    private bool hovered;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (Relic == null) return;
        hovered = true;
        OnHover?.Invoke(Relic);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!hovered) return;
        hovered = false;
        OnHover?.Invoke(null);
    }

    private void OnDisable()
    {
        if (hovered) OnPointerExit(null);
    }
}