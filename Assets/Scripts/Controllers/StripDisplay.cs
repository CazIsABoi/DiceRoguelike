using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class StripDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private Color normalColor = new Color32(0xFF, 0x71, 0x34, 0xFF);

    [Header("Scrolling")]
    [SerializeField] private TMP_Text scrollLabel;    // second TMP: left anchored/pivot, left aligned, no wrapping, starts inactive
    [SerializeField] private float scrollSpeed = 80f; // canvas units per second
    [SerializeField] private float dotStep = 0f;      // one dot's width = moves in whole dots, 0 = smooth
    [Header("Sound")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip popClip;   // e.g. your sfx_blip

    [Header("Hover info")]
    [SerializeField] private Camera cam;
    [SerializeField] private InputActionReference mousePos;
    [SerializeField] private PlayerController player;

    private bool showingMessage;   // while true, hover info stays quiet
    private Coroutine flashRoutine;
    private Coroutine scrollRoutine;

    // Stays until Clear() (for prompts like PICK A DIE)
    public void Show(string message, Color color)
    {
        StopAll();
        SetMessage(message, color);
    }

    // Shows for a while, then clears itself (for YOU WIN / YOU LOST)
    public void Flash(string message, Color color, float duration)
    {
        StopAll();
        flashRoutine = StartCoroutine(FlashRoutine(message, color, duration));
    }

    // Scrolls across if it's too long for the strip, otherwise just shows it
    public void ShowScrolling(string message, Color color, int loops = 2)
    {
        StopAll();
        if (audioSource != null && popClip != null) audioSource.PlayOneShot(popClip);

        scrollLabel.text = message;
        scrollLabel.ForceMeshUpdate();
        RectTransform area = (RectTransform)scrollLabel.rectTransform.parent;
        if (scrollLabel.preferredWidth <= area.rect.width)
        {
            SetMessage(message, color);
            return;
        }

        scrollRoutine = StartCoroutine(ScrollRoutine(message, color, loops));
    }

    public void Clear()
    {
        StopAll();
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

    public void StopAll()
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = null;

        if (scrollRoutine != null) StopCoroutine(scrollRoutine);
        scrollRoutine = null;

        scrollLabel.gameObject.SetActive(false);
        text.gameObject.SetActive(true);
    }

    private IEnumerator FlashRoutine(string message, Color color, float duration)
    {
        SetMessage(message, color);   // not Show(), that would stop this very coroutine
        yield return new WaitForSeconds(duration);
        flashRoutine = null;
        Clear();
    }

    private IEnumerator ScrollRoutine(string message, Color color, int loops)
    {
        showingMessage = true;
        text.gameObject.SetActive(false);

        RectTransform rt = scrollLabel.rectTransform;
        RectTransform area = (RectTransform)rt.parent;
        float textWidth = scrollLabel.preferredWidth;

        // Force a left-middle anchor so x = 0 is the strip's left edge, whatever the Inspector says
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(textWidth, rt.sizeDelta.y);
        rt.anchoredPosition = new Vector2(area.rect.width, rt.anchoredPosition.y);   // just off the right edge

        scrollLabel.text = message;
        scrollLabel.color = color;
        scrollLabel.gameObject.SetActive(true);   // only now, so it never flashes in the middle

        for (int i = 0; loops < 0 || i < loops; i++)
        {
            float x = area.rect.width;            // start just off the right edge
            while (x > -textWidth)
            {
                x -= scrollSpeed * Time.deltaTime;
                float shown = dotStep > 0f ? Mathf.Round(x / dotStep) * dotStep : x;
                rt.anchoredPosition = new Vector2(shown, rt.anchoredPosition.y);
                yield return null;
            }
        }

        scrollRoutine = null;
        Clear();
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