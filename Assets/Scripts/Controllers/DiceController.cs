using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class DiceController : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private InputActionReference leftClick;
    [SerializeField] private InputActionReference rightClick;
    [SerializeField] private InputActionReference mousePos;

    [Header("Dice Throwing Variables")]
    [SerializeField] private float liftHeight = 1.5f;
    [SerializeField] private float followSpeed = 15f;
    [SerializeField] private float spinStrength = 10f;
    [SerializeField] private float maxSpeed = 5f;
    private Vector3 spinDirection;
    private Coroutine rotationRoutine;
    private Transform respawnPoint;
    private Coroutine respawning;

    [Header("Rethrow Safety")]
    [SerializeField] private bool confirmBestFace = true;   // rethrowing the best face on the die needs a second grab
    [SerializeField] private float confirmWindow = 2.5f;    // seconds the second grab counts as "yes"
    private float confirmUntil = -1f;
    private bool hasLanded;                                 // thrown and settled at least once this round

    [Header("Landing")]
    [SerializeField] private float flatThreshold = 0.95f;
    [SerializeField] private float straightenLift = 0.2f;   // a tilted die is lifted this much, straightened, then dropped
    [SerializeField] private float straightenTime = 0.15f;

    [Header("Game Variables")]
    [SerializeField] private int ThrowAttempts = 2;
    [SerializeField] private float killHeight = -0.5f;
    private int StartingThrowAttempts;
    private Coroutine spawnSettle;
    private Coroutine wobbleRoutine;
    private Quaternion wobbleBase;

    private Camera cam;
    private bool isDragging;
    private bool puttingBack;
    private Vector2 grabMouse;
    private bool inputsOn;
    private Plane dragPlane;
    private Vector3 grabOffset;
    private Vector3 targetPosition;
    private Vector3 restPos;
    private Quaternion restRot;
    private Rigidbody rb;
    private float groundY;
    private Die die;

    private DiceSide owner;
    private bool playerControlled;

    private void Awake()
    {
        die = GetComponent<Die>();
        cam = Camera.main;
        rb = GetComponent<Rigidbody>();
        StartingThrowAttempts = ThrowAttempts;
    }

    public void Setup(DiceSide owner, Transform ground, Transform RespawnPoint, bool playerControlled)
    {
        this.owner = owner;
        this.playerControlled = playerControlled;
        respawnPoint = RespawnPoint;
        groundY = ground.GetComponent<Collider>().bounds.max.y;
        hasLanded = false;
        SpawnSettle();
    }

    private void OnDisable()
    {
        ToggleInputs(false);
    }

    // ---------- Input ----------

    public void ToggleInputs(bool toggle)
    {
        if (toggle && !playerControlled) return;
        if (toggle == inputsOn) return;   // never subscribe twice (that made one click count double)
        inputsOn = toggle;

        if (toggle)
        {
            leftClick.action.started += Grab;
            leftClick.action.canceled += Drop;
            rightClick.action.started += MoveDiceToSlot;
        }
        else
        {
            leftClick.action.started -= Grab;
            leftClick.action.canceled -= Drop;
            rightClick.action.started -= MoveDiceToSlot;
        }
    }

    private Ray GetMouseRay()
    {
        Vector2 screenPos = mousePos.action.ReadValue<Vector2>();
        return cam.ScreenPointToRay(screenPos);
    }

    private bool IsMouseOverMe()
    {
        return Physics.Raycast(GetMouseRay(), out RaycastHit hit) && hit.transform == transform;
    }

    // ---------- Grab, drag, throw ----------

    private void Grab(InputAction.CallbackContext context)
    {
        if (puttingBack || !IsMouseOverMe()) return;

        // Grabbing your best face asks first. The second grab within the window goes through.
        if (NeedsConfirm())
        {
            confirmUntil = Time.time + confirmWindow;
            StartWobble();
            Tutorial.Notice($"THROW AWAY THE {Describe(die.GetTopFace())}? GRAB IT AGAIN");
            return;
        }
        confirmUntil = -1f;
        StopWobble();

        Ray ray = GetMouseRay();
        dragPlane = new Plane(Vector3.up, new Vector3(0f, groundY + liftHeight, 0f));
        if (!dragPlane.Raycast(ray, out float distance)) return;
        grabMouse = mousePos.action.ReadValue<Vector2>();

        // Remember where it was, so a click without a real drag can put it back
        restPos = transform.position;
        restRot = transform.rotation;
        targetPosition = transform.position;

        rb.isKinematic = false;
        rb.useGravity = false;
        spinDirection = Random.onUnitSphere;
        grabOffset = transform.position - ray.GetPoint(distance);
        grabOffset.y = 0f;
        isDragging = true;
        PlayDiceRotation(1f);
    }

    private void Update()
    {
        if (isDragging) Drag();

        if (transform.position.y < killHeight)
        {
            Respawn();
        }
    }

    private void Drag()
    {
        Ray ray = GetMouseRay();
        if (dragPlane.Raycast(ray, out float distance))
        {
            targetPosition = ray.GetPoint(distance) + grabOffset;
        }
    }

    private void FixedUpdate()
    {
        if (!isDragging) return;

        Vector3 toTarget = targetPosition - rb.position;
        rb.linearVelocity = toTarget * followSpeed;
        rb.angularVelocity = spinDirection * spinStrength;
    }

    private void Drop(InputAction.CallbackContext context)
    {
        if (!isDragging) return;
        isDragging = false;

        Vector2 moved = mousePos.action.ReadValue<Vector2>() - grabMouse;
        if (moved.magnitude < 5f)   // pixels: a click, not a throw
        {
            StartCoroutine(PutBack());
            return;
        }

        Vector3 v = rb.linearVelocity;
        if (v.y > 0f) v.y = 0f;
        rb.linearVelocity = Vector3.ClampMagnitude(v, maxSpeed);
        rb.useGravity = true;

        ThrowAttempts--;
        Tutorial.Done("throw");
        Tutorial.Done("lock");     // the hint says "lock it in OR throw again", so throwing again counts too
        Tutorial.Done("noslot");
        StartCoroutine(WaitForSettle());
    }

    private IEnumerator PutBack()
    {
        puttingBack = true;
        rb.useGravity = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        Vector3 fromPos = transform.position;
        Quaternion fromRot = transform.rotation;
        const float time = 0.15f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / time);
            transform.SetPositionAndRotation(Vector3.Lerp(fromPos, restPos, k), Quaternion.Slerp(fromRot, restRot, k));
            yield return null;
        }
        transform.SetPositionAndRotation(restPos, restRot);
        puttingBack = false;
    }

    // ---------- Rethrow safety ----------

    private bool NeedsConfirm()
    {
        if (!confirmBestFace || !hasLanded || Time.time <= confirmUntil) return false;
        if (!owner.HasSlotFor(die)) return false;   // it doesn't fit anyway, so rethrowing is the right call
        FaceDefinition top = die.GetTopFace();
        return top != null && IsBestFace(top);
    }

    // True if no face on this die beats the one showing (a 6 on a plain D6, a × on an operator die)
    private bool IsBestFace(FaceDefinition top)
    {
        bool isNumber = top.type == FaceType.Number;
        foreach (FaceDefinition f in die.GetCurrentFaces())
        {
            if (f == null || (f.type == FaceType.Number) != isNumber) continue;
            bool better = isNumber ? f.number > top.number : OpRank(f.op) > OpRank(top.op);
            if (better) return false;
        }
        return true;
    }

    private static int OpRank(Operator op)
    {
        switch (op)
        {
            case Operator.Multiply: return 3;
            case Operator.Add: return 2;
            case Operator.Subtract: return 1;
            default: return 0;   // ÷ and %
        }
    }

    private static string Describe(FaceDefinition face)
    {
        return face.type == FaceType.Number ? face.number.ToString() : GameController.OpSymbol(face.op);
    }

    // ---------- Settling ----------

    private IEnumerator WaitForSettle()
    {
        ToggleInputs(false);
        yield return new WaitForSeconds(0.3f);   // give it time to actually start falling
        yield return SettleFlat();

        hasLanded = true;
        if (ThrowAttempts <= 0 || !playerControlled)
        {
            owner.MoveDiceToSlot(die);
        }
        else
        {
            rb.isKinematic = true;
            ToggleInputs(true);
            Tutorial.Hint("lock", "RIGHT CLICK TO LOCK IT IN · OR DRAG TO THROW AGAIN");
        }
    }

    private IEnumerator WaitForSpawnSettle()
    {
        yield return new WaitForSeconds(0.3f);
        yield return SettleFlat();
        rb.isKinematic = true;
        ToggleInputs(true);
    }

    // Waits until the die is still AND flat. A die that stops tilted (leaning on another die or the rail)
    // used to get kicked into the air, which often changed its face. Now it's lifted a little, turned so the
    // face that was most "up" is exactly up, and dropped again. Same face, no flip.
    private IEnumerator SettleFlat()
    {
        int tries = 0;
        while (true)
        {
            while (respawning != null
                   || rb.linearVelocity.sqrMagnitude > 0.01f
                   || rb.angularVelocity.sqrMagnitude > 0.01f)
            {
                yield return null;
            }

            if (die.IsFlat(flatThreshold)) yield break;

            if (++tries > 3)   // wedged somewhere weird: start over from the spawn point
            {
                tries = 0;
                Respawn();
            }
            else
            {
                yield return Straighten();
            }
            yield return new WaitForSeconds(0.35f);   // let it land before checking again
        }
    }

    private IEnumerator Straighten()
    {
        rb.isKinematic = true;

        Transform top = die.GetTopSocket();
        Quaternion fromRot = transform.rotation;
        Quaternion toRot = Quaternion.FromToRotation(top.up, Vector3.up) * fromRot;
        Vector3 fromPos = transform.position;
        Vector3 toPos = fromPos + Vector3.up * straightenLift;

        for (float t = 0f; t < straightenTime; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / straightenTime);
            transform.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, k), Quaternion.Slerp(fromRot, toRot, k));
            yield return null;
        }
        transform.SetPositionAndRotation(toPos, toRot);

        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // ---------- Respawn ----------

    public void Respawn()
    {
        if (respawning != null) return;   // already on its way back
        respawning = StartCoroutine(RespawnDice(0.5f));
    }

    private IEnumerator RespawnDice(float moveTime)
    {
        isDragging = false;
        rb.linearVelocity = Vector3.zero;
        Vector3 startPos = transform.position;
        Vector3 targetPos = respawnPoint.position;

        for (float t = 0f; t < moveTime; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(startPos, targetPos, Mathf.SmoothStep(0f, 1f, t / moveTime));
            yield return null;
        }
        transform.position = targetPos;
        rb.useGravity = true;
        respawning = null;       // the old version never cleared this, so a die that fell off the table never settled again
    }

    // ---------- Lock in ----------

    private void MoveDiceToSlot(InputAction.CallbackContext context)
    {
        if (!IsMouseOverMe()) return;
        if (!owner.HasSlotFor(die) && ThrowAttempts > 0 && owner.CouldFitLater(die))
        {
            StartWobble();
            Tutorial.Hint("noslot", "NO SLOT FOR THAT FACE · THROW IT AGAIN");
            return;
        }

        owner.MoveDiceToSlot(die);
        ToggleInputs(false);
        Tutorial.Done("lock");
    }

    private void StartWobble()
    {
        if (wobbleRoutine != null) StopCoroutine(wobbleRoutine);
        else wobbleBase = transform.rotation;   // only remember the rotation when it isn't mid-wobble
        wobbleRoutine = StartCoroutine(Wobble());
    }

    private void StopWobble()
    {
        if (wobbleRoutine == null) return;
        StopCoroutine(wobbleRoutine);
        wobbleRoutine = null;
        transform.rotation = wobbleBase;
    }

    private IEnumerator Wobble()
    {
        const float wobbleTime = 0.2f;
        const float wobbleAngle = 15f;
        for (float t = 0f; t < wobbleTime; t += Time.deltaTime)
        {
            float k = t / wobbleTime;
            float angle = wobbleAngle * Mathf.Sin(k * Mathf.PI * 4f) * (1f - k);   // oscillate and fade out
            transform.rotation = wobbleBase * Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
        transform.rotation = wobbleBase;
        wobbleRoutine = null;
    }

    // ---------- Spin while dragging ----------

    private void PlayDiceRotation(float timeBetween)
    {
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        rotationRoutine = StartCoroutine(ChangeDiceRotation(timeBetween));
    }

    private IEnumerator ChangeDiceRotation(float timeBetween)
    {
        while (isDragging)
        {
            Vector3 from = spinDirection;
            Vector3 to = Random.onUnitSphere;
            float elapsed = 0f;
            while (elapsed < timeBetween && isDragging)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / timeBetween);
                spinDirection = Vector3.Slerp(from, to, t);
                yield return null;
            }
        }
    }

    // ---------- Called by DiceSide / EnemyController ----------

    public int GetThrowAttempts() { return ThrowAttempts; }

    public void SetThrowAttempts(int throwAttempts) { ThrowAttempts = throwAttempts; }

    public void Throw(Vector3 force, Vector3 torque)
    {
        if (spawnSettle != null) StopCoroutine(spawnSettle);
        rb.isKinematic = false;
        rb.useGravity = true;

        rb.AddForce(force, ForceMode.Impulse);
        rb.AddTorque(torque, ForceMode.Impulse);

        ThrowAttempts--;
        StartCoroutine(WaitForSettle());
    }

    public void ResetForNewRound()
    {
        StopAllCoroutines();   // nothing from last round keeps running
        respawning = null;
        wobbleRoutine = null;
        rotationRoutine = null;
        spawnSettle = null;
        isDragging = false;
        puttingBack = false;
        hasLanded = false;
        confirmUntil = -1f;
        ToggleInputs(false);

        ThrowAttempts = StartingThrowAttempts;
        transform.SetPositionAndRotation(respawnPoint.position, Random.rotation);
        rb.isKinematic = false;
        rb.useGravity = true;
        SpawnSettle();
    }

    private void SpawnSettle()
    {
        if (spawnSettle != null) StopCoroutine(spawnSettle);
        spawnSettle = StartCoroutine(WaitForSpawnSettle());
    }
}