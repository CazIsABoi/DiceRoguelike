using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "HOW DO YOU WANT TO THROW?" Two cards side by side, each with a looping preview (FlipbookPreview).
// Clicking a card saves the choice and closes the panel. It opens by itself on the first launch only;
// after that, a Settings button can open it again (OnClick > ThrowStylePicker.Open).
//
// SETUP (in the menu scene):
// - Panel: a full-screen panel, inactive to start with. Put this component on anything that stays active
//   (the Canvas or the MainMenu object), and drag the panel in.
// - Drag Card / Click Card: a Button each. Inside each: a title text, a one-line text, and an Image with a
//   FlipbookPreview showing that style.
// - Drag Check / Click Check (optional): a small "✓ CURRENT" text or image on each card. Shown on the current one
//   when the picker is opened again from Settings.
// - MainMenu: drag this component into its Throw Picker field. MainMenu opens it on the first launch.
public class ThrowStylePicker : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button dragCard;
    [SerializeField] private Button clickCard;
    [SerializeField] private GameObject dragCheck;
    [SerializeField] private GameObject clickCheck;
    [SerializeField] private TMP_Text footer;
    [SerializeField] private string firstTimeFooter = "YOU CAN CHANGE THIS ANY TIME IN SETTINGS";
    [SerializeField] private string againFooter = "PICK ONE";

    // MainMenu can hide its own buttons while this is open
    public bool IsOpen => panel != null && panel.activeSelf;
    public event System.Action OnClosed;

    private void Awake()
    {
        if (dragCard != null) dragCard.onClick.AddListener(ChooseDrag);
        if (clickCard != null) clickCard.onClick.AddListener(ChooseClick);
    }

    // Called by MainMenu on the first launch. True if it opened.
    public bool OpenIfFirstTime()
    {
        if (ThrowStyle.HasChosen) return false;
        Open();
        return true;
    }

    public void Open()
    {
        bool first = !ThrowStyle.HasChosen;
        if (dragCheck != null) dragCheck.SetActive(!first && ThrowStyle.Current == ThrowMode.Drag);
        if (clickCheck != null) clickCheck.SetActive(!first && ThrowStyle.Current == ThrowMode.Click);
        if (footer != null) footer.text = first ? firstTimeFooter : againFooter;
        panel.SetActive(true);
    }

    public void ChooseDrag() => Choose(ThrowMode.Drag);
    public void ChooseClick() => Choose(ThrowMode.Click);

    private void Choose(ThrowMode mode)
    {
        ThrowStyle.Current = mode;
        panel.SetActive(false);
        OnClosed?.Invoke();
    }

    [ContextMenu("Forget the choice (testing)")]
    private void ForgetForTesting() => ThrowStyle.Forget();
}
