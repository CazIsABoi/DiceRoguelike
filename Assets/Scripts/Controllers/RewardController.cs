using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class RewardController : MonoBehaviour
{
    private enum Phase { None, ChooseFace, ChooseDie, Inspect }

    [Header("Input Actions")]
    [SerializeField] private InputActionReference select;    // LMB
    [SerializeField] private InputActionReference rotateM;   // RMB (hold)
    [SerializeField] private InputActionReference mousePos;  // Pointer position
    [SerializeField] private InputActionReference rotateK;   // 1D Axis: Q = -1, E = +1

    [Header("References")]
    [SerializeField] private Camera cam;
    [SerializeField] private Transform inspectPoint;         // child of the camera
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private StripDisplay strip;
    [SerializeField] private Color promptColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private FaceView numeralPreview;

    [Header("Feel")]
    [SerializeField] private float moveTime = 0.4f;
    [SerializeField] private float snapTime = 0.15f;
    [SerializeField] private float dragSpeed = 0.3f;
    [SerializeField] private float afterSwapPause = 0.3f;
    [SerializeField] private float hoverScale = 1.15f;       // how much things grow when hovered

    [Header("Glow")]
    [SerializeField, ColorUsage(true, true)] private Color glowColor = new Color(1f, 0.6f, 0.2f) * 1.5f;
    [SerializeField] private float pulseSpeed = 4f;

    [Header("Views")]
    [SerializeField] private CameraRig rig;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private Transform tableView;
    [SerializeField] private Transform monitorView;
    [SerializeField] private Transform diceView;

    [Header("Screen")]
    [SerializeField] private Collider screenCollider;     // MeshCollider on the screen mesh
    [SerializeField] private GameObject rewardPanel;      // on the ScreenCanvas, holds the 3 images
    [SerializeField] private Image[] offerImages;         // left, middle, right
    [SerializeField] private GameObject equationPanel;    // the equation texts, hidden during the choice
    [SerializeField] private Color offerColor = new Color32(0xFF, 0x71, 0x34, 0xFF);
    [SerializeField] private Color dimColor = new Color32(0xFF, 0x71, 0x34, 0x40);

    [Header("Chosen Face")]
    [SerializeField] private Transform chosenFacePoint;   // child of the camera, bottom corner
    [SerializeField] private float chosenFaceScale = 0.5f;
    [SerializeField] private float chosenFaceSpin = 45f;  // degrees per second

    [Header("Offers")]
    [SerializeField] private Sprite healIcon;
    [SerializeField] private Sprite growIcon;             // Reward_Grow
    private Die switchTo;
    private RewardOffer chosen;

    private FaceView chosenFaceView;

    private Phase phase = Phase.None;

    // Phase A: choose a reward
    private List<RewardOffer> offers;
    private FaceDefinition chosenFace;

    // Phase B: choose a die
    private Die[] candidates;

    // Phase C: inspect + pick a side
    private Die die;
    private Transform homeParent;
    private Vector3 homePos;
    private Quaternion homeRot;
    private bool isSnapping;
    private int selectedIndex = -1;
    private bool confirmed;
    private bool numbersOnly;          // growing: only number sides can be copied
    private Vector2 lastMousePos;

    // Hover
    private Transform hovered;
    private Vector3 hoveredScale;

    private MaterialPropertyBlock glowBlock;

    // ---------- Entry point ----------

    // GameController does: yield return reward.RewardRoutine(player, facePool.ToArray(), beaten.unlockFaces, heal);
    public IEnumerator RewardRoutine(PlayerController player, FaceDefinition[] facePool, FaceDefinition[] newFaces, int healAmount)
    {
        cameraController.LookEnabled = false;

        offers = BuildOffers(player, facePool, newFaces, healAmount);
        yield return rig.MoveTo(monitorView);
        ShowOffers(true);
        chosen = null;
        SetPrompt(promptText, "PICK A REWARD");
        phase = Phase.ChooseFace;
        yield return new WaitUntil(() => chosen != null);
        ShowOffers(false);
        SetPrompt(promptText, "");

        switch (chosen.kind)
        {
            case RewardKind.Face:
                chosenFace = chosen.face;
                yield return FaceRewardRoutine(player.GetDice());
                break;

            case RewardKind.Heal:
                player.Heal(chosen.heal);
                yield return new WaitForSeconds(1f);   // let the nixies flicker up
                break;

            case RewardKind.Grow:
                yield return GrowRewardRoutine(player);
                break;

            case RewardKind.Die:                        // not offered any more (bosses hand their die over), kept just in case
                player.AddDie(chosen.die);
                yield return new WaitForSeconds(1f);
                break;
        }

        yield return rig.MoveTo(tableView);
        cameraController.LookEnabled = true;
    }

    // New face: pick a die, pick a side, the new face slams onto it
    public IEnumerator FaceRewardRoutine(Die[] playerDice)
    {
        yield return rig.MoveTo(diceView);
        ShowChosenFace();
        yield return PickDie(playerDice, "PICK A DIE");
        ClearChosenFace();

        yield return PickSide("PICK A SIDE", "RIGHT DRAG OR Q/E TO TURN · CLICK A SIDE TWICE TO PICK IT", false);

        // Swap
        yield return die.SwapFace(selectedIndex, chosenFace);
        yield return new WaitForSeconds(afterSwapPause);

        // Back to where it was
        die.transform.SetParent(homeParent, true);
        yield return MoveDieWorld(homePos, homeRot);

        die = null;
    }

    // Grow a die: pick a number die, pick one of its numbers, it comes back one size up with copies of that number
    public IEnumerator GrowRewardRoutine(PlayerController player)
    {
        List<Die> growable = new List<Die>();
        foreach (Die d in player.GetDice())
        {
            if (player.NextShape(d) != null) growable.Add(d);
        }
        if (growable.Count == 0) yield break;    // BuildOffers only offers this when something can grow

        yield return rig.MoveTo(diceView);
        yield return PickDie(growable.ToArray(), "PICK A DIE TO GROW");
        yield return PickSide("PICK A NUMBER TO COPY", "RIGHT DRAG OR Q/E TO TURN · CLICK A NUMBER TWICE TO COPY IT", true);

        // Back to where it was, then swap it for the bigger one
        die.transform.SetParent(homeParent, true);
        yield return MoveDieWorld(homePos, homeRot);

        string newShape = player.NextShape(die).displayName;
        player.GrowDie(die, selectedIndex);
        die = null;

        strip.Flash(string.IsNullOrEmpty(newShape) ? "YOUR DIE GREW!" : $"IT GREW INTO A {newShape.ToUpper()}!", promptColor, 2f);
        yield return new WaitForSeconds(1.5f);   // watch it drop in
    }

    // ---------- Shared steps ----------

    // B. Click one of these dice. Leaves it in 'die'.
    private IEnumerator PickDie(Die[] choices, string prompt)
    {
        candidates = choices;
        die = null;
        strip.Show(prompt, promptColor);
        phase = Phase.ChooseDie;
        yield return new WaitUntil(() => die != null);
        phase = Phase.None;
        ClearHover();
    }

    // C. Bring 'die' up close and wait until a side is clicked twice (selectedIndex).
    //    Clicking another candidate puts this one back and brings that one up.
    private IEnumerator PickSide(string prompt, string hint, bool onlyNumbers)
    {
        numbersOnly = onlyNumbers;
        while (true)
        {
            die.GetComponent<DiceController>().ToggleInputs(false);
            homeParent = die.transform.parent;
            homePos = die.transform.position;
            homeRot = die.transform.rotation;
            die.transform.SetParent(inspectPoint, true);
            yield return MoveDieLocal(Vector3.zero);

            selectedIndex = -1;
            confirmed = false;
            switchTo = null;
            lastMousePos = mousePos.action.ReadValue<Vector2>();
            strip.Show(prompt, promptColor);
            Tutorial.Hint("side", hint);
            phase = Phase.Inspect;
            yield return new WaitUntil(() => confirmed || switchTo != null);
            phase = Phase.None;
            ClearHover();
            if (confirmed) break;

            if (selectedIndex != -1) SetGlow(die.GetFaceView(selectedIndex), 0f);
            die.transform.SetParent(homeParent, true);
            yield return MoveDieWorld(homePos, homeRot);
            die = switchTo;
        }
        strip.Clear();
        Tutorial.Done("side");
    }

    // ---------- Input ----------

    private void OnEnable()
    {
        select.action.performed += OnSelect;
        rotateK.action.performed += OnRotateKey;
    }

    private void OnDisable()
    {
        select.action.performed -= OnSelect;
        rotateK.action.performed -= OnRotateKey;
    }

    private void Update()
    {
        switch (phase)
        {
            case Phase.ChooseFace:
                HighlightOffer(RaycastScreenOption());
                break;

            case Phase.ChooseDie:
                Die hoverDie = RaycastPlayerDie();
                UpdateHover(hoverDie != null ? hoverDie.transform : null);
                if (chosenFaceView != null)
                {
                    chosenFaceView.transform.Rotate(Vector3.up, chosenFaceSpin * Time.deltaTime, Space.Self);
                }
                break;

            case Phase.Inspect:
                UpdateInspect();
                Die other = RaycastPlayerDie();
                UpdateHover(other != null && other != die ? other.transform : null);
                break;
        }
    }

    private void OnSelect(InputAction.CallbackContext context)
    {
        switch (phase)
        {
            case Phase.ChooseFace:
                int option = RaycastScreenOption();
                if (option < 0) return;
                chosen = offers[option];
                phase = Phase.None;
                break;

            case Phase.ChooseDie:
                Die clicked = RaycastPlayerDie();
                if (clicked == null) return;
                ClearHover();
                die = clicked;
                phase = Phase.None;
                break;

            case Phase.Inspect:
                SelectSide();
                break;
        }
    }

    private void OnRotateKey(InputAction.CallbackContext context)
    {
        if (phase != Phase.Inspect || isSnapping) return;
        float dir = context.ReadValue<float>();   // -1 or +1
        Quaternion target = Quaternion.AngleAxis(90f * dir, cam.transform.up) * die.transform.rotation;
        StartCoroutine(SnapRotate(target));
    }

    // ---------- Phase A: offers ----------

    private List<RewardOffer> BuildOffers(PlayerController player, FaceDefinition[] facePool, FaceDefinition[] newFaces, int healAmount)
    {
        List<RewardOffer> result = new List<RewardOffer>();
        List<FaceDefinition> bag = new List<FaceDefinition>(facePool);

        // 1. Two different faces: what this enemy just unlocked comes first, then random ones from the pool
        List<FaceDefinition> faces = new List<FaceDefinition>();
        if (newFaces != null)
        {
            foreach (FaceDefinition f in newFaces)
            {
                if (faces.Count < 2 && !faces.Contains(f) && bag.Remove(f)) faces.Add(f);
            }
        }
        while (faces.Count < 2)
        {
            FaceDefinition f = TakeRandom(bag, faces);
            if (f == null) break;   // the pool ran dry
            faces.Add(f);
        }

        // 2. The special: a heal when you're at half HP or less, else grow a die,
        //    else a heal if you're hurt at all, else a third face
        bool low = player.Health * 2 <= player.MaxHealth;
        if (low)
        {
            result.Add(HealOffer(healAmount));
        }
        else if (player.CanGrowAny())
        {
            result.Add(new RewardOffer { kind = RewardKind.Grow, icon = growIcon, label = "GROW A DIE" });
        }
        else if (player.Health < player.MaxHealth)
        {
            result.Add(HealOffer(healAmount));
        }
        else
        {
            FaceDefinition f = TakeRandom(bag, faces);
            if (f != null) faces.Add(f);
        }

        foreach (FaceDefinition f in faces) result.Add(FaceOffer(f));

        // 3. Shuffle, so the special isn't always in the same spot
        for (int i = 0; i < result.Count; i++)
        {
            int j = Random.Range(i, result.Count);
            RewardOffer temp = result[i];
            result[i] = result[j];
            result[j] = temp;
        }

        return result;
    }

    // A random face from the bag that isn't already offered, or null if there's none left.
    // The pool holds duplicates (lots of 9s later on); this keeps two identical offers off the screen.
    private FaceDefinition TakeRandom(List<FaceDefinition> bag, List<FaceDefinition> alreadyOffered)
    {
        bag.RemoveAll(f => alreadyOffered.Contains(f));
        if (bag.Count == 0) return null;
        int r = Random.Range(0, bag.Count);
        FaceDefinition face = bag[r];
        bag.RemoveAt(r);
        return face;
    }

    private RewardOffer FaceOffer(FaceDefinition f)
    {
        return new RewardOffer { kind = RewardKind.Face, face = f, icon = f.icon, label = "NEW FACE" };
    }

    private RewardOffer HealOffer(int amount)
    {
        return new RewardOffer { kind = RewardKind.Heal, heal = amount, icon = healIcon, label = "+" + amount + " HP" };
    }

    private void ShowOffers(bool on)
    {
        rewardPanel.SetActive(on);
        equationPanel.SetActive(!on);
        if (on)
        {
            for (int i = 0; i < offerImages.Length; i++)
            {
                if (i < offers.Count)
                {
                    offerImages[i].sprite = offers[i].icon;
                    offerImages[i].gameObject.SetActive(true);
                }
                else
                {
                    offerImages[i].gameObject.SetActive(false);
                }
            }
        }
    }

    private int RaycastScreenOption()
    {
        if (!MouseRaycast(out RaycastHit hit)) return -1;
        if (hit.collider != screenCollider) return -1;

        Vector2 uv = hit.textureCoord;
        float across = uv.x;                 // 0 at the left edge, 1 at the right
        int option = (int)(across * offers.Count);
        return Mathf.Clamp(option, 0, offers.Count - 1);
    }

    private void HighlightOffer(int hoveredOption)
    {
        for (int i = 0; i < offerImages.Length; i++)
        {
            if (i < offers.Count)
                offerImages[i].color = (i == hoveredOption || hoveredOption == -1) ? offerColor : dimColor;
        }
        SetPrompt(promptText, hoveredOption >= 0 ? offers[hoveredOption].label : "PICK A REWARD");
    }

    // ---------- Phase B: dice ----------

    private Die RaycastPlayerDie()
    {
        if (!MouseRaycast(out RaycastHit hit)) return null;
        Die hitDie = hit.collider.GetComponentInParent<Die>();
        if (hitDie == null || System.Array.IndexOf(candidates, hitDie) < 0) return null;   // only dice you can pick right now
        return hitDie;
    }

    private void ShowChosenFace()
    {
        FaceView newChosenFace;
        if (chosenFace.faceView != null) newChosenFace = Instantiate(chosenFace.faceView, chosenFacePoint);
        else
        {
            newChosenFace = Instantiate(numeralPreview, chosenFacePoint);
            newChosenFace.SetLabel(chosenFace.type == FaceType.Operator
                ? GameController.OpSymbol(chosenFace.op) : chosenFace.number.ToString());
        }
        newChosenFace.transform.localPosition = Vector3.zero;
        newChosenFace.transform.localScale *= chosenFaceScale;
        newChosenFace.transform.rotation = Quaternion.LookRotation(cam.transform.up, -cam.transform.forward);
        chosenFaceView = newChosenFace;
    }

    private void ClearChosenFace()
    {
        if (chosenFaceView != null) Destroy(chosenFaceView.gameObject);
        chosenFaceView = null;
    }

    // ---------- Phase C: inspect ----------

    private void UpdateInspect()
    {
        Vector2 mouse = mousePos.action.ReadValue<Vector2>();
        Vector2 delta = mouse - lastMousePos;
        lastMousePos = mouse;

        if (rotateM.action.IsPressed() && !isSnapping)
        {
            die.transform.Rotate(cam.transform.up, -delta.x * dragSpeed, Space.World);
            die.transform.Rotate(cam.transform.right, delta.y * dragSpeed, Space.World);
        }

        if (selectedIndex != -1)
        {
            float pulse = Mathf.Lerp(0.6f, 1.2f, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            SetGlow(die.GetFaceView(selectedIndex), pulse);
        }
    }

    private void SelectSide()
    {
        if (!MouseRaycast(out RaycastHit hit)) return;                     // clicked nothing
        Die clicked = hit.collider.GetComponentInParent<Die>();
        if (clicked != die)
        {
            if (clicked != null && System.Array.IndexOf(candidates, clicked) >= 0) switchTo = clicked;
            return;
        }

        int index = die.GetSocketFacing(hit.normal);
        FaceDefinition picked = die.GetCurrentFaces()[index];
        if (numbersOnly && (picked == null || picked.type != FaceType.Number)) return;   // growing copies numbers only

        if (index == selectedIndex)
        {
            confirmed = true;                                              // second click on the same face = yes
            return;
        }

        if (selectedIndex != -1) SetGlow(die.GetFaceView(selectedIndex), 0f);   // old one off
        selectedIndex = index;                                                  // new one pulses in UpdateInspect
    }

    // ---------- Helpers ----------

    private bool MouseRaycast(out RaycastHit hit)
    {
        Ray ray = cam.ScreenPointToRay(mousePos.action.ReadValue<Vector2>());
        return Physics.Raycast(ray, out hit, 100f, ~0, QueryTriggerInteraction.Ignore);
    }

    private void UpdateHover(Transform target)
    {
        if (target == hovered) return;
        ClearHover();
        if (target == null) return;

        hovered = target;
        hoveredScale = target.localScale;
        target.localScale = hoveredScale * hoverScale;
    }

    private void ClearHover()
    {
        if (hovered != null) hovered.localScale = hoveredScale;
        hovered = null;
    }

    private void SetGlow(FaceView face, float intensity)
    {
        if (face != null) face.SetHighlight(intensity);
    }

    private void SetPrompt(TMP_Text textComponent, string text)
    {
        if (textComponent != null) textComponent.text = text;
    }

    // ---------- Movement ----------

    private IEnumerator MoveDieLocal(Vector3 targetLocal)
    {
        Vector3 start = die.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / moveTime);
            die.transform.localPosition = Vector3.Lerp(start, targetLocal, t);
            yield return null;
        }
        die.transform.localPosition = targetLocal;
    }

    private IEnumerator MoveDieWorld(Vector3 targetPos, Quaternion targetRot)
    {
        Vector3 startPos = die.transform.position;
        Quaternion startRot = die.transform.rotation;
        float elapsed = 0f;

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / moveTime);
            die.transform.position = Vector3.Lerp(startPos, targetPos, t);
            die.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }
        die.transform.position = targetPos;
        die.transform.rotation = targetRot;
    }

    private IEnumerator SnapRotate(Quaternion target)
    {
        isSnapping = true;
        Quaternion startRot = die.transform.rotation;
        float elapsed = 0f;

        while (elapsed < snapTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / snapTime);
            die.transform.rotation = Quaternion.Slerp(startRot, target, t);
            yield return null;
        }
        die.transform.rotation = target;
        isSnapping = false;
    }

    public enum RewardKind { Face, Die, Heal, Grow }

    public class RewardOffer
    {
        public RewardKind kind;
        public FaceDefinition face;   // kind == Face
        public DieDefinition die;     // kind == Die
        public int heal;              // kind == Heal
        public Sprite icon;           // what the dot screen shows
        public string label;          // what the sign says when you hover it
    }
}