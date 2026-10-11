using TMPro;
using UnityEngine;

// A small floating label above a die: its face and its throws left, e.g. "6  ● ● ○" (guide section 3).
//
// HOW TO THINK
// - This is a VIEW: it shows what the die knows and owns no game state itself. Delete every label and the game
//   still plays the same. Keep it that way: game code never asks the label anything.
// - It is NOT a child of the die. A child would tumble with the die. It follows the die in LateUpdate instead
//   (LateUpdate runs after everything else has moved this frame, so the label never trails a frame behind).
// - It hears about changes through DiceController's change event (guide section 2), so it only rebuilds its text
//   when something changed, not every frame.
//
// SETUP: a prefab with a TextMeshPro (3D) text: GameObject > 3D Object > Text - TextMeshPro.
// DiceSide spawns one per die and calls Follow (guide section 3).
public class DieLabel : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private float screenOffset = 0.7f;   // world units toward the top of the screen
    [SerializeField] private string fullPip = "●";
    [SerializeField] private string emptyPip = "○";                          // check your font has both, or use "I" and "."

    private DiceSide side;
    private DiceController target;
    private Die die;
    private Camera cam;

    private void Awake()
    {
        cam = Camera.main;
    }

    // DiceSide calls this right after spawning the die
    public void Follow(DiceController controller, DiceSide side)
    {
        target = controller;
        this.side = side;
        die = controller.GetComponent<Die>();
        name = $"Label ({controller.name})";

        side.OnDiceChanged += Refresh;   // ANY die on this side changed: re-check, BEST may have flipped
        Refresh();                       // show something right away, don't wait for the first change
    }

    private void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }

        bool visible = target.gameObject.activeInHierarchy;
        text.enabled = visible;          // hide the text, but keep the label alive for when the die comes back
        if (!visible) return;

        transform.position = target.transform.position + cam.transform.up * screenOffset;
        transform.rotation = cam.transform.rotation;
    }
    private void Refresh()
    {
        if (target == null) return;   // the die can be destroyed a moment before this label is

        string top;
        string bottom = Pips(target);
        switch (target.State)
        {
            case DieState.Hovering:
                top = "?";
                break;
            case DieState.Resting:
                top = FaceText();
                if (side.IsBest(die)) bottom = "BEST";
                break;
            case DieState.Locked:
            case DieState.Busted:
                text.text = "";
                return;
            default:   // held or rolling
                top = "";
                break;
        }
        text.text = $"{top}\n<size=50%>{bottom}</size>";
    }

    private void OnDestroy()
    {
        if (side != null) side.OnDiceChanged -= Refresh;
    }
    private string FaceText()
    {
        FaceDefinition face = die.GetTopFace();
        if (face == null) return "?";
        return face.type == FaceType.Number ? face.number.ToString() : GameController.OpSymbol(face.op);
    }
    private string Pips(DiceController c)
    {
        int full = Mathf.Clamp(c.ThrowsLeft, 0, c.MaxThrows);   // never negative, never more than the max
        return new string(fullPip[0], full) + new string(emptyPip[0], c.MaxThrows - full);
    }
}
