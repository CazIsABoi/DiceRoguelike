using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Your chips, lying on the table in front of you (hold S to look down at them). Up to two.
// Click a chip on your turn to use it. Extra Throw works straight away; the others want an enemy die:
// click the chip, look up at the enemy's dice, click one. Right click cancels.
//
// SETUP (game scene):
// - An empty object on the table where you can see it when looking down. Add this component.
// - Slots: two empty children, side by side, where the chips lie.
// - Chip Prefab: a poker chip model with a Collider on it. Its mesh gets the chip's colour. A child SpriteRenderer
//   lying flat on top (optional) shows the chip's icon; a TMP text (optional) shows its symbol instead.
// - Plus Face: your + face. Number Faces: 10 slots, slot N holds the face for N (Nudge needs them).
//   Slot 0 is Face_0 if you have one; leave it empty and a 1 just can't be nudged.
// - The rest: the same references the dice use (camera, the click actions), and the strip for messages.
//   Camera and the actions can stay empty: then it uses the main camera and the mouse directly.
//
// Can't click them? Hovering a chip should make it grow and put its name on the strip. If it doesn't, tick
// Log Clicks, click a chip, and the Console lists everything the mouse ray went through.
public class ChipTray : MonoBehaviour
{
    public static bool Targeting { get; private set; }   // a chip is waiting for an enemy die

    [SerializeField] private Transform[] slots = new Transform[2];
    [SerializeField] private GameObject chipPrefab;
    [SerializeField] private float hoverScale = 1.15f;    // a hovered chip grows a little
    [SerializeField] private float armedLift = 0.02f;     // a chip waiting for a target lifts off the table (and grows more)
    [SerializeField] private ChipDefinition[] startWith;  // for testing: chips you have from the start
    [SerializeField] private bool logClicks;              // prints what each click's ray hit

    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private EnemyController enemy;
    [SerializeField] private StripDisplay strip;
    [SerializeField] private Camera cam;
    [SerializeField] private InputActionReference click;       // LMB
    [SerializeField] private InputActionReference cancel;      // RMB
    [SerializeField] private InputActionReference mousePos;
    [SerializeField] private Color textColor = new Color32(0xFF, 0x71, 0x34, 0xFF);

    [Header("Faces")]
    [SerializeField] private FaceDefinition plusFace;
    [SerializeField] private FaceDefinition[] numberFaces;     // index = the number: Face_0 at 0 ... Face_9 at 9

    [Header("Sound")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip chipClip;               // picking one up / putting it down

    private readonly ChipDefinition[] held = new ChipDefinition[2];
    private readonly GameObject[] objects = new GameObject[2];
    private readonly Vector3[] baseScales = new Vector3[2];
    private int armed = -1;
    private int hovered = -1;

    public bool HasRoom => System.Array.IndexOf(held, null) >= 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Targeting = false; }

    private void OnDisable() { Targeting = false; }

    private void OnEnable()
    {
        // Harmless if something else already turned them on (ScreenButtons does the same)
        if (click != null) click.action.Enable();
        if (cancel != null) cancel.action.Enable();
        if (mousePos != null) mousePos.action.Enable();
    }

    private void Start()
    {
        if (chipPrefab == null) Debug.LogError("[ChipTray] Chip Prefab is empty, so no chip can show up.");
        else
        {
            Collider chipCollider = chipPrefab.GetComponentInChildren<Collider>(true);
            if (chipCollider == null) Debug.LogError("[ChipTray] The Chip Prefab has no Collider, so the mouse can't hit it. Add one (a Box or Mesh Collider).");
            else if (chipCollider.isTrigger) Debug.LogError("[ChipTray] The Chip Prefab's Collider is a trigger. Clicks skip triggers: untick Is Trigger.");
        }
        if (slots == null || slots.Length < 2 || slots[0] == null || slots[1] == null)
            Debug.LogError("[ChipTray] Slots needs two Transforms, where the two chips lie.");
        if (startWith != null) foreach (ChipDefinition c in startWith) Add(c);
    }

    // ---------- Getting chips ----------

    public bool Add(ChipDefinition chip)
    {
        int i = System.Array.IndexOf(held, null);
        if (i < 0 || chip == null) return false;
        held[i] = chip;
        GameObject go = Instantiate(chipPrefab, slots[i].position, slots[i].rotation, slots[i]);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is SpriteRenderer || r.GetComponent<TMP_Text>() != null) continue;   // the symbol on top keeps its own colour
            r.material.color = chip.color;
        }
        SpriteRenderer face = go.GetComponentInChildren<SpriteRenderer>();
        if (face != null) face.sprite = chip.icon;          // the chip's icon, lying on top of it
        TMP_Text label = go.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = chip.symbol;
        objects[i] = go;
        baseScales[i] = go.transform.localScale;
        Play(chipClip);
        StartCoroutine(GameController.Pop(go.transform, go.transform.localScale, 1.4f, 0.4f));
        return true;
    }

    // ---------- Using them ----------

    private void Update()
    {
        if (SettingsMenu.IsOpen) return;
        if (armed >= 0 && player.InputBlocked) Disarm();   // the round ended with a chip still up: put it down

        int over = ChipUnderMouse();
        if (over != hovered) ShowHover(over);

        if (CancelPressed && armed >= 0) { Disarm(); return; }
        if (!ClickPressed) return;
        if (logClicks) LogRay();

        if (over >= 0) { ClickChip(over); return; }
        if (armed >= 0)
        {
            Die target = DieUnderMouse();
            if (target != null && enemy.Owns(target)) UseOn(armed, target);
        }
    }

    private void ClickChip(int i)
    {
        if (armed == i) { Disarm(); return; }                  // clicking it again puts it down
        if (player.InputBlocked) { Tutorial.Notice("USE CHIPS ON YOUR TURN"); return; }

        ChipDefinition chip = held[i];
        if (chip.type == ChipType.ExtraThrow)
        {
            if (DiceController.AnyPlayerDiceRolling) { Tutorial.Notice("WAIT FOR YOUR DICE TO LAND"); return; }
            if (player.AddThrowToAll() == 0) { Tutorial.Notice("ALL YOUR DICE ARE LOCKED IN"); return; }
            strip.FlashScrolling("EXTRA THROW · EVERY DIE GETS ONE MORE THIS ROUND", textColor, 2f);
            Spend(i);
            return;
        }

        if (armed >= 0) Disarm();
        armed = i;
        Targeting = true;
        Pose(i);
        Play(chipClip);
        strip.ShowScrolling(TargetPrompt(chip.type), textColor, -1);   // scrolls (if it's too long) until the chip is used or put down
    }

    private static string TargetPrompt(ChipType type)
    {
        switch (type)
        {
            case ChipType.Snatch: return "CLICK AN ENEMY OPERATOR · RIGHT CLICK TO CANCEL";
            case ChipType.Nudge: return "CLICK AN ENEMY NUMBER · RIGHT CLICK TO CANCEL";
            default: return "CLICK AN ENEMY DIE · RIGHT CLICK TO CANCEL";
        }
    }

    private void UseOn(int i, Die target)
    {
        if (player.InputBlocked) { Disarm(); return; }          // the round is already being counted
        FaceDefinition face = enemy.PlacedFace(target);
        if (face == null) { Tutorial.Notice("PICK A DIE THAT'S IN ONE OF ITS SLOTS"); return; }

        switch (held[i].type)
        {
            case ChipType.Reroll:
                StartCoroutine(enemy.RerollPlaced(target));
                break;

            case ChipType.Snatch:
                if (face.type != FaceType.Operator || face.op == Operator.Add) { Tutorial.Notice("PICK AN OPERATOR THAT ISN'T A +"); return; }
                enemy.OverrideFace(target, plusFace);
                break;

            case ChipType.Nudge:
                if (face.type != FaceType.Number || face.number <= 0) { Tutorial.Notice("PICK A NUMBER ABOVE 0"); return; }
                FaceDefinition lower = face.number - 1 < numberFaces.Length ? numberFaces[face.number - 1] : null;
                if (lower == null) { Tutorial.Notice("THAT ONE CAN'T GO ANY LOWER"); return; }   // a 1, and no Face_0 in slot 0
                enemy.OverrideFace(target, lower);
                break;
        }
        Spend(i, "on " + Telemetry.Name(face));
    }

    // note: what it hit, for the sheet
    private void Spend(int i, string note = "")
    {
        Telemetry.Choice("chip", new[] { held[i].displayName }, held[i].displayName, note);
        if (armed == i) { armed = -1; Targeting = false; strip.Clear(); }
        if (objects[i] != null) Destroy(objects[i]);
        held[i] = null;
        objects[i] = null;
        if (hovered == i) hovered = -1;
        Play(chipClip);
    }

    private void Disarm()
    {
        int was = armed;
        armed = -1;
        Targeting = false;
        if (was >= 0) Pose(was);
        strip.Clear();
    }

    // Where a chip sits and how big it is: lying flat, hovered (a bit bigger), or armed (lifted, bigger still).
    // Hovering only scales, so the chip never slides out from under the mouse and flickers.
    private void Pose(int i)
    {
        if (objects[i] == null) return;
        Transform t = objects[i].transform;
        bool isArmed = i == armed;
        t.position = slots[i].position + (isArmed ? Vector3.up * armedLift : Vector3.zero);
        float size = isArmed ? hoverScale * hoverScale : i == hovered ? hoverScale : 1f;
        t.localScale = baseScales[i] * size;
    }

    // ---------- Pointing ----------

    private void ShowHover(int over)
    {
        int was = hovered;
        hovered = over;
        if (was >= 0) Pose(was);
        if (over >= 0) Pose(over);
        if (over < 0)
        {
            if (armed < 0) strip.Clear();
            return;
        }
        if (armed < 0) strip.ShowScrolling($"{held[over].displayName.ToUpper()} · {held[over].line.ToUpper()}", textColor, -1);   // scrolls while hovered
    }

    // The nearest chip anywhere along the mouse ray. Everything the ray passes is checked, not just the first thing it
    // hits, so the felt's collider, an invisible wall or a dice rail lying over the chips can't hide them.
    private int ChipUnderMouse()
    {
        RaycastHit[] hits = RayHits();
        float best = float.MaxValue;
        int found = -1;
        foreach (RaycastHit hit in hits)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null && hit.distance < best && hit.collider.transform.IsChildOf(objects[i].transform))
                {
                    best = hit.distance;
                    found = i;
                }
            }
        }
        return found;
    }

    // The nearest die along the mouse ray, the same way
    private Die DieUnderMouse()
    {
        float best = float.MaxValue;
        Die found = null;
        foreach (RaycastHit hit in RayHits())
        {
            Die d = hit.collider.GetComponentInParent<Die>();
            if (d != null && hit.distance < best) { best = hit.distance; found = d; }
        }
        return found;
    }

    private RaycastHit[] RayHits()
    {
        Camera c = cam != null ? cam : Camera.main;
        if (c == null) return new RaycastHit[0];
        return Physics.RaycastAll(c.ScreenPointToRay(MousePosition), 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private void LogRay()
    {
        RaycastHit[] hits = RayHits();
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        string list = hits.Length == 0 ? "nothing" : "";
        foreach (RaycastHit h in hits) list += $"\n  {h.collider.name} (layer {LayerMask.LayerToName(h.collider.gameObject.layer)}, {h.distance:0.00} away)";
        Debug.Log($"[ChipTray] Click at {MousePosition}. Chips held: {(objects[0] != null ? 1 : 0) + (objects[1] != null ? 1 : 0)}. " +
                  $"Player input blocked: {player.InputBlocked}. The ray hit: {list}");
    }

    // ---------- Input (the actions if they're set, else the mouse itself) ----------

    private bool ClickPressed => (click != null && click.action.WasPressedThisFrame())
                              || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);

    private bool CancelPressed => (cancel != null && cancel.action.WasPressedThisFrame())
                               || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);

    private Vector2 MousePosition => mousePos != null ? mousePos.action.ReadValue<Vector2>()
                                   : Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

    private void Play(AudioClip clip)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
    }
}
