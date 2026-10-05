using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

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
    [SerializeField] private Transform[] offerPoints;        // 3 children of the camera, where the offered faces float
    [SerializeField] private TMP_Text promptText;            // optional: a line on the dot-matrix sign

    [Header("Feel")]
    [SerializeField] private float moveTime = 0.4f;
    [SerializeField] private float snapTime = 0.15f;
    [SerializeField] private float dragSpeed = 0.3f;
    [SerializeField] private float afterSwapPause = 0.3f;
    [SerializeField] private float offerScale = 1f;          // size of the offered faces
    [SerializeField] private float hoverScale = 1.15f;       // how much things grow when hovered
    [SerializeField] private float flingForce = 3f;          // unchosen faces get thrown away
    [SerializeField] private float flingSpin = 5f;

    [Header("Glow")]
    [SerializeField, ColorUsage(true, true)] private Color glowColor = new Color(1f, 0.6f, 0.2f) * 1.5f;
    [SerializeField] private int glowMaterialIndex = -1;     // -1 = whole face, otherwise only that material (e.g. the symbol)
    [SerializeField] private float pulseSpeed = 4f;

    private Phase phase = Phase.None;

    // Phase A: choose a face
    private FaceDefinition[] offers;
    private FaceView[] offerViews;
    private FaceDefinition chosenFace;

    // Phase B: choose a die
    private Die[] candidates;

    // Phase C: inspect + swap
    private Die die;
    private Transform homeParent;
    private Vector3 homePos;
    private Quaternion homeRot;
    private bool isSnapping;
    private int selectedIndex = -1;
    private bool confirmed;
    private Vector2 lastMousePos;

    // Hover
    private Transform hovered;
    private Vector3 hoveredScale;

    private MaterialPropertyBlock glowBlock;

    // ---------- Entry point ----------

    // GameController does: yield return reward.RewardRoutine(player.GetDice(), rewardPool);
    public IEnumerator RewardRoutine(Die[] playerDice, FaceDefinition[] pool)
    {
        // A. Pick 1 of 3 faces
        offers = PickOffers(pool, offerPoints.Length);
        SpawnOffers();
        chosenFace = null;
        SetPrompt("PICK A FACE");
        phase = Phase.ChooseFace;
        yield return new WaitUntil(() => chosenFace != null);

        // B. Pick which die gets it
        candidates = playerDice;
        die = null;
        SetPrompt("PICK A DIE");
        phase = Phase.ChooseDie;
        yield return new WaitUntil(() => die != null);
        phase = Phase.None;
        ClearHover();
        ClearOffers();

        DiceController controller = die.GetComponent<DiceController>();
        controller.ToggleInputs(false);

        // C1. Closeup: parent to the inspect point so it follows the camera
        homeParent = die.transform.parent;
        homePos = die.transform.position;
        homeRot = die.transform.rotation;
        die.transform.SetParent(inspectPoint, true);
        yield return MoveDieLocal(Vector3.zero);

        // C2. Inspect until the player confirms a side
        selectedIndex = -1;
        confirmed = false;
        lastMousePos = mousePos.action.ReadValue<Vector2>();
        SetPrompt("PICK A SIDE");
        phase = Phase.Inspect;
        yield return new WaitUntil(() => confirmed);
        phase = Phase.None;
        SetPrompt("");

        // C3. Swap
        yield return die.SwapFace(selectedIndex, chosenFace);
        yield return new WaitForSeconds(afterSwapPause);

        // C4. Back to where it was
        die.transform.SetParent(homeParent, true);
        yield return MoveDieWorld(homePos, homeRot);

        die = null;
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
                int offer = RaycastOffer();
                UpdateHover(offer >= 0 ? offerViews[offer].transform : null);
                break;

            case Phase.ChooseDie:
                Die hoverDie = RaycastPlayerDie();
                UpdateHover(hoverDie != null ? hoverDie.transform : null);
                break;

            case Phase.Inspect:
                UpdateInspect();
                break;
        }
    }

    private void OnSelect(InputAction.CallbackContext context)
    {
        switch (phase)
        {
            case Phase.ChooseFace:
                int index = RaycastOffer();
                if (index < 0) return;
                ClearHover();
                chosenFace = offers[index];
                for (int i = 0; i < offerViews.Length; i++)
                {
                    if (i == index) StripColliders(offerViews[i]);   // keep it visible, but stop it blocking clicks
                    else
                    {
                        Fling(offerViews[i]);
                        offerViews[i] = null;
                    }
                }
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

    // ---------- Phase A: faces ----------

    private FaceDefinition[] PickOffers(FaceDefinition[] pool, int count)
    {
        List<FaceDefinition> bag = new List<FaceDefinition>(pool);
        count = Mathf.Min(count, bag.Count);
        FaceDefinition[] result = new FaceDefinition[count];

        for (int i = 0; i < count; i++)
        {
            int r = Random.Range(0, bag.Count);
            result[i] = bag[r];
            bag.RemoveAt(r);   // no duplicates
        }
        return result;
    }

    private void SpawnOffers()
    {
        offerViews = new FaceView[offers.Length];
        for (int i = 0; i < offers.Length; i++)
        {
            FaceView view = Instantiate(offers[i].faceView, offerPoints[i]);
            view.transform.localPosition = Vector3.zero;
            // Face's up (its outward side) points at the camera, its forward points up on screen
            view.transform.rotation = Quaternion.LookRotation(cam.transform.up, -cam.transform.forward);
            view.transform.localScale *= offerScale;
            AddClickCollider(view);
            offerViews[i] = view;
        }
    }

    private int RaycastOffer()
    {
        if (!MouseRaycast(out RaycastHit hit)) return -1;
        FaceView view = hit.collider.GetComponentInParent<FaceView>();
        if (view == null) return -1;
        return System.Array.IndexOf(offerViews, view);
    }

    private void ClearOffers()
    {
        if (offerViews == null) return;
        foreach (FaceView view in offerViews)
        {
            if (view != null) Destroy(view.gameObject);
        }
        offerViews = null;
    }

    private void Fling(FaceView view)
    {
        view.transform.SetParent(null, true);
        StripColliders(view);
        Rigidbody rb = view.gameObject.AddComponent<Rigidbody>();
        Vector3 dir = (cam.transform.up + Random.insideUnitSphere * 0.5f).normalized;
        rb.AddForce(dir * flingForce, ForceMode.Impulse);
        rb.AddTorque(Random.onUnitSphere * flingSpin, ForceMode.Impulse);
        Destroy(view.gameObject, 2f);
    }

    private void AddClickCollider(FaceView view)
    {
        if (view.GetComponentInChildren<Collider>() != null) return;
        MeshRenderer mesh = view.GetComponentInChildren<MeshRenderer>();
        if (mesh != null) mesh.gameObject.AddComponent<BoxCollider>();   // auto-fits the mesh
    }

    private void StripColliders(FaceView view)
    {
        foreach (Collider c in view.GetComponentsInChildren<Collider>()) Destroy(c);
    }

    // ---------- Phase B: dice ----------

    private Die RaycastPlayerDie()
    {
        if (!MouseRaycast(out RaycastHit hit)) return null;
        Die hitDie = hit.collider.GetComponentInParent<Die>();
        if (hitDie == null || System.Array.IndexOf(candidates, hitDie) < 0) return null;   // only the player's dice
        return hitDie;
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
        if (hit.collider.GetComponentInParent<Die>() != die) return;       // clicked something else

        int index = die.GetSocketFacing(hit.normal);
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
        if (face == null) return;
        MeshRenderer renderer = face.GetComponentInChildren<MeshRenderer>();
        if (renderer == null) return;

        if (glowBlock == null) glowBlock = new MaterialPropertyBlock();
        Color color = glowColor * intensity;   // intensity 0 = black = off

        bool oneMaterial = glowMaterialIndex >= 0 && glowMaterialIndex < renderer.sharedMaterials.Length;
        if (oneMaterial)
        {
            renderer.GetPropertyBlock(glowBlock, glowMaterialIndex);
            glowBlock.SetColor("_EmissionColor", color);
            renderer.SetPropertyBlock(glowBlock, glowMaterialIndex);
        }
        else
        {
            renderer.GetPropertyBlock(glowBlock);
            glowBlock.SetColor("_EmissionColor", color);
            renderer.SetPropertyBlock(glowBlock);
        }
    }

    private void SetPrompt(string text)
    {
        if (promptText != null) promptText.text = text;
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
}