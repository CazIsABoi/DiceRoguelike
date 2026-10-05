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

    [Header("Game Variables")]
    [SerializeField] private int ThrowAttempts = 2;
    [SerializeField] private float killHeight = -0.5f;
    private int StartingThrowAttempts;
    private Coroutine spawnSettle;

    private Camera cam;
    private bool isDragging;
    private Plane dragPlane;
    private Vector3 grabOffset;
    private Vector3 targetPosition;
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
        SpawnSettle();
    }

    private void OnEnable()
    {

    }

    private void OnDisable()
    {
        ToggleLeftClick(false);
        ToggleRightClick(false);
    }

    private void ToggleLeftClick(bool toggle)
    {
        if (toggle && !playerControlled) return;
        if (toggle)
        {
            leftClick.action.started += Grab;
            leftClick.action.canceled += Drop;
        }
        else
        {
            leftClick.action.started -= Grab;
            leftClick.action.canceled -= Drop;
        }
    }


    private void ToggleRightClick(bool toggle)
    {
        if (toggle && !playerControlled) return;
        if (toggle)
        {
            rightClick.action.started += MoveDiceToSlot;
        }
        else
        {
            rightClick.action.started -= MoveDiceToSlot;
        }
    }
    private Ray GetMouseRay()
    {
        Vector2 screenPos = mousePos.action.ReadValue<Vector2>();
        return cam.ScreenPointToRay(screenPos);
    }

    private void Grab(InputAction.CallbackContext context)
    {
        Ray ray = GetMouseRay();

        if (!IsMouseOverMe()) return;
        rb.isKinematic = false;

        dragPlane = new Plane(Vector3.up, new Vector3(0f, groundY + liftHeight, 0f));
        rb.useGravity = false;
        spinDirection = Random.onUnitSphere;

        if (dragPlane.Raycast(ray, out float distance))
        {
            grabOffset = transform.position - ray.GetPoint(distance);
            isDragging = true;
            grabOffset.y = 0f;
            PlayDiceRotation(1f);
        }
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

    private void Drop(InputAction.CallbackContext context)
    {
        if (!isDragging) return;

        Vector3 v = rb.linearVelocity;
        if (v.y > 0f) v.y = 0f;
        rb.linearVelocity = v;

        isDragging = false;
        rb.useGravity = true;
        rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, maxSpeed);

        ThrowAttempts--;
        CheckThrowAttempts();
    }

    private void FixedUpdate()
    {
        if (!isDragging) return;

        Vector3 toTarget = targetPosition - rb.position;

        rb.linearVelocity = toTarget * followSpeed;
        rb.angularVelocity = spinDirection * spinStrength;
    }

    public void Respawn()
    {
        if (respawning != null) StopCoroutine(respawning);
        respawning = StartCoroutine(RespawnDice(0.5f));
    }

    private IEnumerator RespawnDice(float moveTime)
    {
        yield return new WaitForSeconds(moveTime);

        if (rb.isKinematic != true) rb.linearVelocity = Vector3.zero;
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        float elapsed = 0f;

        Vector3 targetPos = respawnPoint.position;

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveTime;
            t = Mathf.SmoothStep(0f, 1f, t);
            transform.position = Vector3.Lerp(startPos, targetPos, t);
            yield return null;
        }
    }

    public int GetThrowAttempts() { return ThrowAttempts; }

    public void SetThrowAttempts(int throwAttempts) { ThrowAttempts = throwAttempts; }

    private void CheckThrowAttempts()
    {
        StartCoroutine(WaitForSettle());
    }

    private void MoveDiceToSlot(InputAction.CallbackContext context)
    {
        if (!IsMouseOverMe()) return;
        ThrowAttempts = 0;
        CheckThrowAttempts();
    }

    private bool IsMouseOverMe()
    {
        Ray ray = GetMouseRay();

        return Physics.Raycast(ray, out RaycastHit hit) && hit.transform == transform;
    }

    private IEnumerator WaitForSettle()
    {
        ToggleLeftClick(false);
        ToggleRightClick(false);
        yield return new WaitForSeconds(0.3f); // give it time to actually start falling

        int nudges = 0;
        while (true)
        {
            while (rb.linearVelocity.sqrMagnitude > 0.01f || rb.angularVelocity.sqrMagnitude > 0.01f)
            {
                yield return null;
            }

            if (die.IsFlat(0.95f)) break;

            rb.AddForce(Vector3.up * 3f, ForceMode.Impulse);
            rb.AddTorque(Random.onUnitSphere * 2f, ForceMode.Impulse);
            nudges++;

            if (nudges == 5)
            {
                Respawn();
                nudges = 0;
            }
            yield return new WaitForSeconds(0.3f); // Make sure it's flat
        }
        die.LogTopFace();
        if (ThrowAttempts <= 0 || !playerControlled) owner.MoveDiceToSlot(die);
        else
        {
            ToggleLeftClick(true);
            ToggleRightClick(true);
            rb.isKinematic = true;
        }
    }
    private IEnumerator WaitForSpawnSettle()
    {
        yield return new WaitForSeconds(0.3f); // give it time to actually start falling

        int nudges = 0;
        while (true)
        {
            while (rb.linearVelocity.sqrMagnitude > 0.01f || rb.angularVelocity.sqrMagnitude > 0.01f)
            {
                yield return null;
            }

            if (die.IsFlat(0.95f)) break;

            rb.AddForce(Vector3.up * 3f, ForceMode.Impulse);
            rb.AddTorque(Random.onUnitSphere * 2f, ForceMode.Impulse);
            nudges++;

            if (nudges == 5)
            {
                Respawn();
                nudges = 0;
            }
            yield return new WaitForSeconds(0.3f); // Make sure it's flat
        }

        rb.isKinematic = true;
        ToggleLeftClick(true);
        ToggleRightClick(true);
    }

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
                float t = elapsed / timeBetween;
                t = Mathf.SmoothStep(0f, 1f, t);
                spinDirection = Vector3.Slerp(from, to, t);
                yield return null;
            }
        }
    }

    public void Throw(Vector3 force, Vector3 torque)
    {
        StopCoroutine(spawnSettle);
        rb.isKinematic = false;
        rb.useGravity = true;

        rb.AddForce(force, ForceMode.Impulse);
        rb.AddTorque(torque, ForceMode.Impulse);

        ThrowAttempts--;

        StartCoroutine(WaitForSettle());
    }

    private void SpawnSettle()
    {
        if (spawnSettle != null) StopCoroutine(spawnSettle);
        spawnSettle = StartCoroutine(WaitForSpawnSettle());
    }

    public void ResetForNewRound()
    {
        transform.position = respawnPoint.position;
        rb.isKinematic = false;
        SpawnSettle();
        ThrowAttempts = StartingThrowAttempts;
        transform.rotation = Random.rotation;
    }
}
