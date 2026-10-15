using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Tip: once a fight, turn one of your dice onto a neighbouring side instead of throwing it again.
// Point at a die that has landed and press T. Every side you can tip onto gets a label floating beside the die, on
// that side (so you can read the far side too). Move the mouse toward a label: it grows, the die leans that way, and
// the strip says what would come up. Click to tip. The die turns over and goes straight into a slot (a tip locks it
// in). Right click or T cancels.
//
// Which sides are "neighbours": the ones next to the top face. On a D6 that's the four around it (never the bottom),
// on a D8 or D20 three, on a D12 five. Bigger dice are harder to tip onto the face you want.
//
// SETUP (game scene):
// - Add this component to any object (the GameController's object is fine).
// - Player, Strip: drag them in. Camera and Mouse Pos can stay empty (main camera and the mouse).
// - Label Prefab (optional): a world-space TextMeshPro (3D text, not a UI text), styled like your die labels.
//   Empty: plain TextMeshPro labels are made at runtime with your default TMP font.
// - Tips Per Fight lives on the PlayerController (default 1).
// - Tip Clip (optional): plays when the die turns over.
public class TipController : MonoBehaviour
{
    public static bool Active { get; private set; }   // tipping: DiceController leaves the clicks alone

    [Header("References")]
    [SerializeField] private PlayerController player;
    [SerializeField] private StripDisplay strip;
    [SerializeField] private Camera cam;
    [SerializeField] private InputActionReference mousePos;   // optional: else the mouse itself

    [Header("Controls")]
    [SerializeField] private Key tipKey = Key.T;

    [Header("Labels")]
    [SerializeField] private TMP_Text labelPrefab;           // optional, see SETUP
    [SerializeField] private float labelOut = 0.6f;          // how far out from the die's centre, toward its side
    [SerializeField] private float labelHeight = 0.35f;      // and how far above the die's centre
    [SerializeField] private float runtimeFontSize = 3f;     // only for labels made at runtime
    [SerializeField] private float chosenScale = 1.5f;       // the side you point at
    [SerializeField] private Color labelColor = new Color(1f, 1f, 1f, 0.55f);

    [Header("Feel")]
    [SerializeField, Range(0f, 0.5f)] private float leanAmount = 0.18f;   // how far it leans toward the side you point at (0..1 of the full tip)
    [SerializeField] private float leanSpeed = 14f;
    [SerializeField] private float tipTime = 0.28f;
    [SerializeField] private float tipLift = 0.25f;                     // it hops this high while it turns over
    [SerializeField] private float glow = 1.3f;                          // the face you point at glows too (when you can see it)
    [SerializeField] private Color textColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private Color bustColor = new Color(1f, 0.35f, 0.3f);

    [Header("Sound")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip tipClip;

    private Die die;
    private DiceController controller;
    private Quaternion baseRotation;
    private Vector3 basePosition;
    private readonly List<int> neighbours = new List<int>();     // socket indices
    private readonly List<Vector3> directions = new List<Vector3>();   // each one's side, flat on the table
    private readonly List<TMP_Text> labels = new List<TMP_Text>();
    private int chosen = -1;                                     // index into neighbours
    private FaceView glowing;
    private bool turning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Active = false; }

    private void OnDisable()
    {
        if (die != null && !turning) Cancel(false);
        ClearLabels();
        Active = false;
    }

    private void Start()
    {
        if (player == null) Debug.LogError("[TipController] Player is empty.");
        if (strip == null) Debug.LogError("[TipController] Strip is empty.");
    }

    private void Update()
    {
        if (turning) return;
        if (SettingsMenu.IsOpen) return;

        if (die != null)
        {
            // The round ended, or the die got knocked: put it back
            if (player.InputBlocked || controller == null || !controller.CanTip) { Cancel(false); return; }
            if (KeyPressed || RightPressed) { Cancel(true); return; }
            UpdateChoice();
            if (LeftPressed && chosen >= 0) StartCoroutine(TipRoutine());
            return;
        }

        if (!KeyPressed || ChipTray.Targeting) return;
        TryBegin();
    }

    private void LateUpdate()
    {
        if (die != null) PlaceLabels();   // after the camera has moved, so they never lag a frame behind
    }

    // ---------- Starting ----------

    private void TryBegin()
    {
        if (player.InputBlocked) { Tutorial.Notice("TIP ON YOUR TURN"); return; }
        if (player.TipsPerFight <= 0) return;
        Die target = DieUnderMouse();
        if (target == null || !player.Owns(target)) { Tutorial.Notice("POINT AT ONE OF YOUR DICE AND PRESS T"); return; }
        if (player.TipsLeft <= 0) { Tutorial.Notice("YOUR TIP IS USED UP THIS FIGHT"); return; }

        DiceController c = target.GetComponent<DiceController>();
        if (c.State == DieState.Hovering) { Tutorial.Notice("THROW IT FIRST · THEN YOU CAN TIP IT"); return; }
        if (c.State == DieState.Locked || c.State == DieState.Busted) { Tutorial.Notice("THAT DIE IS ALREADY LOCKED IN"); return; }
        if (!c.CanTip) { Tutorial.Notice("WAIT FOR IT TO LAND"); return; }

        die = target;
        controller = c;
        baseRotation = die.transform.rotation;
        basePosition = die.transform.position;
        FindNeighbours();
        if (neighbours.Count == 0)
        {
            Debug.LogWarning("[TipController] This die has no sockets to tip onto.", die);
            die = null; controller = null;
            return;
        }
        MakeLabels();
        chosen = -1;
        Active = true;
        Tutorial.Done("tip");
        UpdateChoice();
        PlaceLabels();
    }

    // The sides next to the top face: the ones whose direction is closest to the top's, without being the top.
    // On a D6 the four sides all sit at 90 degrees from the top (the bottom is at 180), so all four count.
    private void FindNeighbours()
    {
        neighbours.Clear();
        directions.Clear();
        int faces = die.GetCurrentFaces().Length;
        int top = die.GetSocketFacing(Vector3.up);
        Vector3 up = die.GetSocket(top).up;
        float best = -2f;
        float[] dots = new float[faces];
        for (int i = 0; i < faces; i++)
        {
            if (i == top) continue;
            dots[i] = Vector3.Dot(die.GetSocket(i).up, up);
            if (dots[i] > best) best = dots[i];
        }
        for (int i = 0; i < faces; i++)
        {
            if (i == top || dots[i] < best - 0.1f) continue;
            Vector3 side = die.GetSocket(i).up;
            side.y = 0f;
            if (side.sqrMagnitude < 0.0001f) continue;
            neighbours.Add(i);
            directions.Add(side.normalized);
        }
    }

    // ---------- Labels ----------

    private void MakeLabels()
    {
        ClearLabels();
        for (int k = 0; k < neighbours.Count; k++)
        {
            TMP_Text label;
            if (labelPrefab != null) label = Instantiate(labelPrefab);
            else
            {
                TextMeshPro made = new GameObject("TipLabel").AddComponent<TextMeshPro>();
                made.fontSize = runtimeFontSize;
                made.alignment = TextAlignmentOptions.Center;
                label = made;
            }
            label.text = Describe(die.GetCurrentFaces()[neighbours[k]]);
            label.color = labelColor;
            labels.Add(label);
        }
    }

    private void PlaceLabels()
    {
        Camera c = Cam;
        for (int k = 0; k < labels.Count; k++)
        {
            if (labels[k] == null) continue;
            Transform t = labels[k].transform;
            t.position = basePosition + directions[k] * labelOut + Vector3.up * labelHeight;
            if (c != null) t.rotation = Quaternion.LookRotation(t.position - c.transform.position, c.transform.up);   // faces the camera
            t.localScale = Vector3.one * (k == chosen ? chosenScale : 1f);
        }
    }

    private void ClearLabels()
    {
        foreach (TMP_Text label in labels) if (label != null) Destroy(label.gameObject);
        labels.Clear();
    }

    // ---------- Pointing ----------

    private void UpdateChoice()
    {
        // Where the mouse points on a flat plane through the die, as a direction away from it
        Ray ray = Cam.ScreenPointToRay(MousePosition);
        Plane plane = new Plane(Vector3.up, basePosition + Vector3.up * labelHeight);
        int pick = chosen;
        if (plane.Raycast(ray, out float distance))
        {
            Vector3 dir = ray.GetPoint(distance) - basePosition;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0004f || pick < 0)
            {
                if (dir.sqrMagnitude < 0.0004f) dir = Vector3.forward;
                dir.Normalize();
                float best = -2f;
                for (int k = 0; k < directions.Count; k++)
                {
                    float d = Vector3.Dot(directions[k], dir);
                    if (d > best) { best = d; pick = k; }
                }
            }
        }
        if (pick < 0) pick = 0;
        if (pick != chosen)
        {
            chosen = pick;
            ShowChoice();
        }

        // Lean toward the chosen side, from where it was lying
        Quaternion target = Quaternion.Slerp(baseRotation, TippedRotation(chosen), leanAmount);
        die.transform.rotation = Quaternion.Slerp(die.transform.rotation, target, 1f - Mathf.Exp(-leanSpeed * Time.deltaTime));
    }

    // The die's rotation with that side pointing straight up: a turn over the edge between the top and that side
    private Quaternion TippedRotation(int k)
    {
        Vector3 socketUp = baseRotation * Quaternion.Inverse(die.transform.rotation) * die.GetSocket(neighbours[k]).up;   // as it was before leaning
        return Quaternion.FromToRotation(socketUp, Vector3.up) * baseRotation;
    }

    private void ShowChoice()
    {
        if (glowing != null) glowing.SetHighlight(0f);
        glowing = die.GetFaceView(neighbours[chosen]);
        if (glowing != null) glowing.SetHighlight(glow);

        FaceDefinition face = die.GetCurrentFaces()[neighbours[chosen]];
        bool fits = player.HasSlotForFace(face);
        for (int k = 0; k < labels.Count; k++)
            if (labels[k] != null) labels[k].color = k != chosen ? labelColor : fits ? textColor : bustColor;

        string message = $"TIP ONTO {Describe(face)}{(fits ? "" : " · IT WOULD BUST")} · CLICK TO TIP · RIGHT CLICK CANCELS";
        strip.ShowScrolling(message, fits ? textColor : bustColor, -1);
    }

    // ---------- Tipping ----------

    private IEnumerator TipRoutine()
    {
        turning = true;
        if (glowing != null) { glowing.SetHighlight(0f); glowing = null; }
        ClearLabels();
        strip.Clear();

        Quaternion from = die.transform.rotation;
        Quaternion to = TippedRotation(chosen);
        string landed = Describe(die.GetCurrentFaces()[neighbours[chosen]]);
        for (float t = 0f; t < tipTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / tipTime);
            float hop = Mathf.Sin(k * Mathf.PI) * tipLift;
            die.transform.SetPositionAndRotation(basePosition + Vector3.up * hop, Quaternion.Slerp(from, to, k));
            yield return null;
        }
        die.transform.SetPositionAndRotation(basePosition, to);
        if (audioSource != null && tipClip != null) audioSource.PlayOneShot(tipClip);

        player.SpendTip();
        Telemetry.Choice("tip", new[] { landed }, landed, "");
        controller.LockInByTip();

        die = null;
        controller = null;
        chosen = -1;
        turning = false;
        Active = false;
    }

    private void Cancel(bool putBack)
    {
        if (glowing != null) { glowing.SetHighlight(0f); glowing = null; }
        ClearLabels();
        if (die != null && (putBack || (controller != null && controller.State == DieState.Resting)))
            die.transform.SetPositionAndRotation(basePosition, baseRotation);
        if (Active) strip.Clear();
        die = null;
        controller = null;
        chosen = -1;
        Active = false;
    }

    private static string Describe(FaceDefinition face)
    {
        if (face == null) return "?";
        return face.type == FaceType.Number ? face.number.ToString() : GameController.OpSymbol(face.op);
    }

    // ---------- Input ----------

    private Camera Cam => cam != null ? cam : Camera.main;

    private Die DieUnderMouse()
    {
        if (Cam == null) return null;
        float best = float.MaxValue;
        Die found = null;
        foreach (RaycastHit hit in Physics.RaycastAll(Cam.ScreenPointToRay(MousePosition), 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            Die d = hit.collider.GetComponentInParent<Die>();
            if (d != null && hit.distance < best) { best = hit.distance; found = d; }
        }
        return found;
    }

    private bool KeyPressed => Keyboard.current != null && Keyboard.current[tipKey].wasPressedThisFrame;
    private bool LeftPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    private bool RightPressed => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

    private Vector2 MousePosition => mousePos != null ? mousePos.action.ReadValue<Vector2>()
                                   : Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
}
