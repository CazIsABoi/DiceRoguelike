using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class StripDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private Color normalColor = new Color32(0xFF, 0x71, 0x34, 0xFF);

    [Header("Hover info")]
    [SerializeField] private Camera cam;
    [SerializeField] private InputActionReference mousePos;
    [SerializeField] private PlayerController player;

    private bool showingMessage;   // while true, hover info stays quiet
    private Coroutine flashRoutine;

    // Stays until Clear() (for prompts like PICK A DIE)
    public void Show(string message, Color color)
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = null;
        SetMessage(message, color);
    }

    // Shows for a while, then clears itself (for YOU WIN / YOU LOST)
    public void Flash(string message, Color color, float duration)
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine(message, color, duration));
    }

    private IEnumerator FlashRoutine(string message, Color color, float duration)
    {
        SetMessage(message, color);   // not Show(), that would stop this very coroutine
        yield return new WaitForSeconds(duration);
        flashRoutine = null;
        Clear();
    }

    public void Clear()
    {
        showingMessage = false;
        text.text = "";
        text.color = normalColor;
    }

    private void SetMessage(string message, Color color)
    {
        showingMessage = true;
        text.text = message;
        text.color = color;
    }

    // ---------- Hover info ----------

    private void Update()
    {
        if (showingMessage) return;

        string info = "";
        Die hovered = RaycastPlayerDie();
        if (hovered != null)
        {
            int throws = hovered.GetComponent<DiceController>().GetThrowAttempts();
            info = "THROWS " + throws;
        }

        if (text.text != info) text.text = info;   // only touch TMP when it actually changes
    }

    private Die RaycastPlayerDie()
    {
        Ray ray = cam.ScreenPointToRay(mousePos.action.ReadValue<Vector2>());
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore)) return null;

        Die die = hit.collider.GetComponentInParent<Die>();
        if (die == null || System.Array.IndexOf(player.GetDice(), die) < 0) return null;   // only the player's dice
        return die;
    }
}