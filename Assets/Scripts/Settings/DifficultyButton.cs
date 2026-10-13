using UnityEngine;
using UnityEngine.EventSystems;

// Put it on a difficulty button: hovering it shows that difficulty's description on the menu.
public class DifficultyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private DifficultyDefinition difficulty;
    [SerializeField] private MainMenu menu;

    public void OnPointerEnter(PointerEventData eventData) => menu.Describe(difficulty);
    public void OnPointerExit(PointerEventData eventData) => menu.Describe(null);
}
