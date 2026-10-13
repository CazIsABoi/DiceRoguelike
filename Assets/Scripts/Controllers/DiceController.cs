using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class DiceController : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private InputActionReference leftClick;
    [SerializeField] private InputActionReference rightClick;
    [SerializeField] private InputActionReference mousePos;

    [Header("Hover")]
    [SerializeField] private float hoverHeight = 0.8f;   // above its spawn spot
    [SerializeField] private float bobHeight = 0.06f;
    [SerializeField] private float bobSpeed = 2.5f;
    [SerializeField] private float spinSpeed = 120f;     // degrees per second
    private Vector3 spinAxis;
    private float bobPhase;                              // so the dice don't all bob in step

    [Header("Holding")]
    [SerializeField] private float liftHeight = 1.5f;
    [SerializeField] private float followSpeed = 15f;
    [SerializeField] private float holdSpin = 3f;           // a slow idle spin while you hold it (the old Spin Strength was 10)
    [SerializeField] private float leanSpin = 1f;           // it tumbles the way you move it, so it feels like it's in your hand

    [Header("Throwing")]
    [SerializeField] private float throwStrength = 1f;      // how much of your flick speed the die gets
    [SerializeField] private float maxThrowSpeed = 8f;      // the old Max Speed was 5
    [SerializeField] private float throwArc = 1.5f;         // upward speed on a full-power throw: it flies a little instead of dropping flat
    [SerializeField] private float rollSpin = 2.5f;         // forward roll per unit of throw speed, so it rolls the way you threw it
    [SerializeField] private float randomSpin = 4f;         // a bit of chaos on top: the same flick doesn't always land the same face
    [SerializeField] private float flickWindow = 0.08f;     // seconds of mouse movement the throw speed is measured over
    private Vector3 spinDirection;
    private Coroutine rotationRoutine;
    private Transform respawnPoint;
    private Vector3 spawnOffset;                            // each die gets its own spot next to the spawn point (DiceSide hands these out)
    private Coroutine respawning;

    // The last few drag positions, so the throw uses how fast you were moving the mouse (not where the die lagged to)
    private readonly Vector3[] trailPos = new Vector3[24];
    private readonly float[] trailTime = new float[24];
    private int trailHead, trailCount;

    [Header("Click To Throw")]   // ThrowStyle Click: the same slam the enemy does
    [SerializeField] private float slamLift = 0.6f;          // how high it goes above where it lands
    [SerializeField] private float slamLiftTime = 0.12f;
    [SerializeField] private float slamHangTime = 0.15f;
    [SerializeField] private float slamDropTime = 0.08f;
    [SerializeField] private float slamSpin = 900f;          // degrees per second in the air
    [SerializeField] private float slamJitter = 0.08f;       // lands a little off its spot, so it doesn't look stamped

    [Header("Settling")]
    [SerializeField] private float freeRollTime = 0.8f;     // after the first bounce it rolls freely this long...
    [SerializeField] private float settleDamping = 5f;      // ...then damping ramps up to this...
    [SerializeField] private float settleRampTime = 0.7f;   // ...over this long. No throw rolls forever, and it can't flip at the very end
    private float baseLinearDamping, baseAngularDamping;
    private Coroutine settleAssist;
    private float landedAt = -1f;
    private float settleAmount;                             // 0 = rolling freely, 1 = fully damped
    private Collider dieCollider;                           // when it first touched something after the throw

    [Header("Rethrow Safety")]
    [SerializeField] private bool confirmBestFace = true;   // rethrowing the best face on the die needs a second grab
    [SerializeField] private float confirmWindow = 2.5f;    // seconds the second grab counts as "yes"
    private float confirmUntil = -1f;
    private bool hasLanded;                                 // thrown and settled at least once this round

    [Header("Weight")]
    [SerializeField] private float gravityScale = 2.5f;   // 1 = Unity's normal gravity

    [Header("Landing")]
    [SerializeField] private float settleTimeout = 4f;   // not settled after this = stuck somewhere: respawn
    private Collider playArea;                            // DiceSide passes it in Setup
    [SerializeField] private float flatThreshold = 0.95f;
    [SerializeField] private float straightenLift = 0.2f;   // a tilted die is lifted this much, straightened, then dropped
    [SerializeField] private float straightenTime = 0.15f;

    [Header("Game Variables")]
    [SerializeField] private int ThrowAttempts = 3;
    [SerializeField] private float fallDepth = 1f;          // this far below the felt counts as "fell off the table" (was a fixed Kill Height of -0.5)
    [SerializeField] private bool logRespawns = true;       // writes why a die respawned to the Console
    [SerializeField, Min(0.01f)] private float respawnPopTime = 0.2f;   // a die that fell off pops back in at the spawn point
    private int StartingThrowAttempts;
    private Coroutine spawnSettle;
    private Coroutine wobbleRoutine;
    private Quaternion wobbleBase;

    // Sound: the clips live on PlayerController and EnemyController (Dice Sounds), so every die prefab gets them
    private AudioSource sfx;
    private AudioSource shakeSource;                         // its own source, so the rattle can fade and stop without cutting off clacks
    private float shakeLevel;                                // 0..1, follows how fast you move the held die
    [Header("Shake Sound")]
    [SerializeField] private float shakeFullSpeed = 6f;      // hand speed (units/s) for a full-volume rattle
    [SerializeField, Range(0f, 1f)] private float shakeIdleVolume = 0.15f;   // a quiet rattle even when you hold still
    [SerializeField] private float shakeFade = 10f;          // how fast the volume follows your hand
    private float nextImpact;
    private const float impactMinSpeed = 0.4f;   // softer touches are silent
    private const float impactFullSpeed = 6f;    // this hard or harder is full volume
    public DieState State { get; private set; } = DieState.Hovering;
    private DieState stateBeforeGrab;   // so it can go back to hovering or whatever after a grab
    public int ThrowsLeft => ThrowAttempts;
    public event System.Action<DiceController> OnChanged;
    [SerializeField] private float dustMinSpeed = 3f;   // softer landings don't puff

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
    private Vector3 baseScale;
    private Rigidbody rb;
    private float groundY;
    private Collider groundCollider;
    private Die die;
    private FaceView glowing;   // the face currently pulsing, so we can switch it off
    private Coroutine settling;

    private DiceSide owner;
    private bool playerControlled;
    public int MaxThrows => StartingThrowAttempts;
    public Vector3 HomePosition => SpawnPosition;  


    // How many of the player's dice are in the air right now. While any are, nothing can be locked in,
    // so you can't slot a die and then watch the one still rolling land on something better.
    private static int rollingPlayerDice;
    private bool rolling;
    public static bool AnyPlayerDiceRolling => rollingPlayerDice > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { rollingPlayerDice = 0; }   // in case domain reload is off in Play Mode settings

    private void SetRolling(bool on)
    {
        if (on == rolling) return;
        rolling = on;
        if (playerControlled) rollingPlayerDice += on ? 1 : -1;
    }

    // A throw left on this die, or the Rabbit's Foot is still unused this fight
    private bool CanThrowAgain => ThrowAttempts > 0 || (playerControlled && owner != null && owner.HasBonusRethrow);

    private void Awake()
    {
        die = GetComponent<Die>();
        cam = Camera.main;
        rb = GetComponent<Rigidbody>();
        StartingThrowAttempts = ThrowAttempts;
        baseScale = transform.localScale;
        baseLinearDamping = rb.linearDamping;
        dieCollider = GetComponent<Collider>();
        baseAngularDamping = rb.angularDamping;
        rb.maxAngularVelocity = 25f;   // above the default (7) so hard throws spin, but not so fast a corner whips through the felt
        // Speculative catches fast SPINNING corners (Discrete misses them, and Continuous Dynamic only sweeps straight-line
        // movement). It also works on kinematic bodies, so it never has to be switched back and forth.
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        SetInterpolation(false);       // only on while physics moves it (see SetInterpolation)
    }

    public void Setup(DiceSide owner, Transform ground, Transform RespawnPoint, Collider playArea, bool playerControlled)
    {
        this.owner = owner;
        this.playerControlled = playerControlled;
        respawnPoint = RespawnPoint;
        groundCollider = ground.GetComponent<Collider>();
        groundY = groundCollider.bounds.max.y;
        hasLanded = false;
        this.playArea = playArea;

        sfx = GetComponent<AudioSource>();

        if (sfx == null && owner.HasDiceSounds)
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
        }
        if (owner.DiceShakeClips != null && owner.DiceShakeClips.Length > 0)
        {
            shakeSource = gameObject.AddComponent<AudioSource>();
            shakeSource.playOnAwake = false;
            shakeSource.loop = false;
            if (sfx != null)   // same mixer/3D settings as the die's own source
            {
                shakeSource.outputAudioMixerGroup = sfx.outputAudioMixerGroup;
                shakeSource.spatialBlend = sfx.spatialBlend;
            }
        }
        if (playerControlled)
        {
            GoToHover();
        }
        else
        {
            SpawnSettle();
        }
    }

    private void OnDisable()
    {
        SetRolling(false);
        ToggleInputs(false);
    }

    // The enemy moves this die by hand now (the slam): stop all physics and anything still settling it
    public void HandControl()
    {
        StopAllCoroutines();
        spawnSettle = null; settleAssist = null; settling = null;
        respawning = null; wobbleRoutine = null; rotationRoutine = null;
        SetRolling(false);
        StopSettleAssist();
        rb.isKinematic = true;
        SetInterpolation(false);
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

    // ---------- Spawn and hover ----------
    private void GoToHover()
    {
        rb.isKinematic = true;
        SetInterpolation(false);
        spinAxis = Random.onUnitSphere;
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        SetState(DieState.Hovering);
        ToggleInputs(true);
    }

    private void UpdateHover()   // call it from Update
    {
        if (!playerControlled || State != DieState.Hovering) return;
        float height = owner.InputBlocked ? hoverHeight - 0.1f : hoverHeight + Mathf.Sin(Time.time * bobSpeed + bobPhase) * bobHeight;
        float degreesThisFrame = (owner.InputBlocked ? spinSpeed * 0.5f : spinSpeed) * Time.deltaTime;

        transform.position = spawnOffset + respawnPoint.position + Vector3.up * (height);
        transform.Rotate(spinAxis, degreesThisFrame, Space.World);
    }

    // ---------- Grab, drag, throw ----------

    private void Grab(InputAction.CallbackContext context)
    {
        if (SettingsMenu.IsOpen || ChipTray.Targeting) return;   // a chip is waiting for an enemy die: clicks are for that
        if (puttingBack || owner.InputBlocked || !IsMouseOverMe()) return;

        // Waiting on the Rabbit's Foot, but another die used it first
        if (!CanThrowAgain)
        {
            StartWobble();
            Tutorial.Notice("NO THROWS LEFT · RIGHT CLICK TO LOCK IT IN");
            return;
        }

        // Grabbing your best face asks first. The second grab within the window goes through.
        if (NeedsConfirm())
        {
            confirmUntil = Time.time + confirmWindow;
            StartWobble();
            Tutorial.Notice($"CAN'T BEAT THE {Describe(die.GetTopFace())} · GRAB AGAIN TO THROW ANYWAY");
            return;
        }
        confirmUntil = -1f;
        StopWobble();
        if (ThrowStyle.ClickToThrow) { ClickThrow(); return; }

        Ray ray = GetMouseRay();
        dragPlane = new Plane(Vector3.up, new Vector3(0f, groundY + liftHeight, 0f));
        if (!dragPlane.Raycast(ray, out float distance)) return;
        grabMouse = mousePos.action.ReadValue<Vector2>();

        // Remember where it was, so a click without a real drag can put it back
        restPos = transform.position;
        restRot = transform.rotation;
        targetPosition = transform.position;
        stateBeforeGrab = State;

        StopSettleAssist();
        rb.isKinematic = false;
        rb.useGravity = false;
        SetInterpolation(true);
        spinDirection = Random.onUnitSphere;
        grabOffset = transform.position - ray.GetPoint(distance);
        grabOffset.y = 0f;
        trailCount = 0;
        RecordTrail(transform.position);
        isDragging = true;
        SetState(DieState.Held);
        PlayDiceRotation(1f);
        PlaySound(owner.DiceGrabClip, 0.5f);
    }

    // Click to throw: the bookkeeping of a throw, then the enemy-style slam
    private void ClickThrow()
    {
        if (ThrowAttempts > 0) SetThrowAttempts(ThrowAttempts - 1);
        else owner.SpendBonusRethrow();   // the Rabbit's Foot throw
        Telemetry.DieThrown(owner);
        Tutorial.Done("throw");
        Tutorial.Done("lock");
        Tutorial.Done("noslot");
        Tutorial.Done("rabbit");
        settling = StartCoroutine(SlamThrow());
    }

    // Picks a face (every side equally likely), lifts, spins, and slams down on its own spot with that face up.
    // The same steps as EnemyController.Slam, so both sides throw the same way.
    private IEnumerator SlamThrow()
    {
        ToggleInputs(false);
        SetState(DieState.Rolling);       // first: while it's Hovering, UpdateHover pins it in the air
        SetRolling(true);
        StopSettleAssist();
        SetInterpolation(false);
        rb.isKinematic = true;            // no physics: we move it by hand, like the enemy's "hand"
        PlaySound(owner.DiceGrabClip, 0.5f);

        Transform t = transform;
        int faceIndex = Random.Range(0, die.GetCurrentFaces().Length);
        Transform socket = die.GetSocket(faceIndex);
        Quaternion faceUp = Quaternion.FromToRotation(socket.up, Vector3.up) * t.rotation;
        Quaternion endRot = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * faceUp;

        // Where it lands: its own spot (the spawn point plus this die's offset), so your dice never land on each other
        Vector2 jitter = Random.insideUnitCircle * slamJitter;
        Vector3 spot = SpawnPosition + new Vector3(jitter.x, 0f, jitter.y);
        spot.y = groundCollider.Raycast(new Ray(spot + Vector3.up * 3f, Vector3.down), out RaycastHit hit, 10f)
               ? hit.point.y : groundY;
        float half = Vector3.Distance(socket.position, t.position);
        Vector3 restPos = spot + Vector3.up * half;
        Vector3 highPos = restPos + Vector3.up * slamLift;
        Vector3 spinAxis = Random.onUnitSphere;

        // a. Lift: fast, then slow, spinning
        Vector3 from = t.position;
        for (float time = 0f; time < slamLiftTime; time += Time.deltaTime)
        {
            float k = time / slamLiftTime;
            k = 1f - (1f - k) * (1f - k);
            t.position = Vector3.Lerp(from, highPos, k);
            t.Rotate(spinAxis, slamSpin * Time.deltaTime, Space.World);
            yield return null;
        }

        // b. Hang, still spinning
        for (float time = 0f; time < slamHangTime; time += Time.deltaTime)
        {
            t.position = highPos;
            t.Rotate(spinAxis, slamSpin * Time.deltaTime, Space.World);
            yield return null;
        }

        // c. Drop: slow, then fast, turning into the chosen face
        Quaternion spun = t.rotation;
        for (float time = 0f; time < slamDropTime; time += Time.deltaTime)
        {
            float k = time / slamDropTime;
            k = k * k;
            t.SetPositionAndRotation(Vector3.Lerp(highPos, restPos, k), Quaternion.Slerp(spun, endRot, k));
            yield return null;
        }
        t.SetPositionAndRotation(restPos, endRot);

        // d. Impact: dust, a clack, a little squash
        if (owner.LandDust != null)
        {
            owner.LandDust.transform.position = spot;
            owner.LandDust.Emit(14);
        }
        AudioClip[] clips = owner.DiceHitClips;
        if (clips != null && clips.Length > 0) PlaySound(clips[Random.Range(0, clips.Length)], 1f);
        StartCoroutine(GameController.Pop(t, baseScale, 1.15f, 0.2f));

        // e. Landed: the same ending as WaitForSettle
        SetRolling(false);
        hasLanded = true;
        settling = null;
        if (!CanThrowAgain)
        {
            LockIn();
        }
        else
        {
            ToggleInputs(true);
            SetState(DieState.Resting);
            if (ThrowAttempts <= 0) Tutorial.Hint("rabbit", "RABBIT'S FOOT: THROW IT ONCE MORE · OR RIGHT CLICK TO LOCK IT");
            else Tutorial.Hint("lock", "RIGHT CLICK TO LOCK IT IN · OR " + ThrowStyle.AgainHint);
        }
    }
    private void Update()
    {
        if (isDragging) Drag();
        UpdateShake();
        UpdateHover();

        if (State == DieState.Resting)
        {
            glowing = die.GetFaceView(die.GetSocketFacing(Vector3.up));
            float pulse = Mathf.Lerp(0.6f, 1.2f, (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f);
            glowing.SetHighlight(pulse);
        }

        if (transform.position.y < groundY - fallDepth) FellOff();
    }
    private void FellOff()
    {
        if (!playerControlled || State != DieState.Rolling)
        {
            Respawn("fell off the table");   // enemy dice keep the old behaviour
            return;
        }
        if (settling != null)
        {
            StopCoroutine(settling);
            settling = null;
        }
        StopSettleAssist();   
        SetRolling(false);    
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = Vector3.zero;

        rb.isKinematic = true;
        ToggleInputs(true);
        SetThrowAttempts(ThrowAttempts + 1);   // give it back so the player can throw it again

        GoToHover();
        Tutorial.Notice("IT FELL OFF THE TABLE · GRAB IT AGAIN TO THROW");
    }

    private void Drag()
    {
        Ray ray = GetMouseRay();
        if (dragPlane.Raycast(ray, out float distance))
        {
            targetPosition = ray.GetPoint(distance) + grabOffset;
        }
        RecordTrail(targetPosition);   // every frame, even when the mouse is still: stopping before you let go is a gentle drop
    }

    private void FixedUpdate()
    {
        if (!isDragging)
        {
            ApplySettleDamping();
            CatchTunneling();

            // If the die isn't kinematic and uses gravity, push it down with the EXTRA gravity:
            if (!rb.isKinematic && rb.useGravity) rb.AddForce(Physics.gravity * (gravityScale - 1), ForceMode.Acceleration);
            return;
        }

        Vector3 toTarget = targetPosition - rb.position;
        Vector3 velocity = toTarget * followSpeed;
        rb.linearVelocity = velocity;


        // Tumble the way it's moving (like it's rolling off your fingers), plus a slow idle spin
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        rb.angularVelocity = Vector3.Cross(Vector3.up, flat) * leanSpin + spinDirection * holdSpin;
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

        // Speed from your flick, capped. A little lift on hard throws so they fly, and a roll that goes the way you threw.
        Vector3 v = Vector3.ClampMagnitude(FlickVelocity() * throwStrength, maxThrowSpeed);
        float power = maxThrowSpeed > 0f ? v.magnitude / maxThrowSpeed : 0f;   // 0 = dropped, 1 = full flick
        Vector3 direction = v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        v.y = throwArc * power;

        rb.useGravity = true;
        rb.linearVelocity = v;
        rb.angularVelocity = Vector3.Cross(Vector3.up, direction) * (v.magnitude * rollSpin)
                           + Random.insideUnitSphere * randomSpin;
        landedAt = -1f;

        if (ThrowAttempts > 0) SetThrowAttempts(ThrowAttempts - 1);
        else owner.SpendBonusRethrow(); 
        Telemetry.DieThrown(owner);
        Tutorial.Done("throw");
        Tutorial.Done("lock");     // the hint says "lock it in OR throw again", so throwing again counts too
        Tutorial.Done("noslot");
        Tutorial.Done("rabbit");
        SetRolling(true);
        SetState(DieState.Rolling);
        settling = StartCoroutine(WaitForSettle());
    }

    private void RecordTrail(Vector3 position)
    {
        trailHead = (trailHead + 1) % trailPos.Length;
        trailPos[trailHead] = position;
        trailTime[trailHead] = Time.time;
        if (trailCount < trailPos.Length) trailCount++;
    }

    // How fast the mouse moved the die over the last flickWindow seconds, flat on the table
    private Vector3 FlickVelocity()
    {
        if (trailCount < 2) return Vector3.zero;
        int newest = trailHead;
        int oldest = trailHead;
        for (int k = 1; k < trailCount; k++)
        {
            oldest = (trailHead - k + trailPos.Length) % trailPos.Length;
            if (trailTime[newest] - trailTime[oldest] >= flickWindow) break;
        }
        float dt = trailTime[newest] - trailTime[oldest];
        if (dt < 0.005f) return Vector3.zero;
        Vector3 v = (trailPos[newest] - trailPos[oldest]) / dt;
        v.y = 0f;
        return v;
    }

    private IEnumerator PutBack()
    {
        puttingBack = true;
        SetInterpolation(false);   // the transform is moved by hand below, interpolation would fight it
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
        SetState(stateBeforeGrab);   // at the END
        puttingBack = false;
    }

    // ---------- Rethrow safety ----------

    private bool NeedsConfirm()
    {
        if (!confirmBestFace || !hasLanded || Time.time <= confirmUntil) return false;
        if (!owner.HasSlotFor(die)) return false;   // it busts if you lock it, so rethrowing is fair game
        return owner.IsBest(die);
    }
    private static string Describe(FaceDefinition face)
    {
        return face.type == FaceType.Number ? face.number.ToString() : GameController.OpSymbol(face.op);
    }

    // ---------- Settling ----------

    private IEnumerator WaitForSettle()
    {
        ToggleInputs(false);
        StartSettleAssist();
        yield return new WaitForSeconds(0.3f);   // give it time to actually start falling
        yield return SettleFlat();
        while (playArea != null && !playArea.bounds.Contains(transform.position))
        {
            Respawn("landed outside the play area");
            yield return SettleFlat();   // SettleFlat already waits for the respawn to finish
        }
        StopSettleAssist();
        SetInterpolation(false);
        SetRolling(false);

        hasLanded = true;
        if (!playerControlled || !CanThrowAgain)
        {
            LockIn();
        }
        else
        {
            rb.isKinematic = true;
            ToggleInputs(true);
            SetState(DieState.Resting);
            if (ThrowAttempts <= 0) Tutorial.Hint("rabbit", "RABBIT'S FOOT: THROW IT ONCE MORE · OR RIGHT CLICK TO LOCK IT");
            else Tutorial.Hint("lock", "RIGHT CLICK TO LOCK IT IN · OR " + ThrowStyle.AgainHint);
        }
    }

    private IEnumerator WaitForSpawnSettle()
    {
        yield return new WaitForSeconds(0.3f);
        yield return SettleFlat();
        SetInterpolation(false);
        SetRolling(false);
        rb.isKinematic = true;
        SetState(DieState.Resting);          // landed
        ToggleInputs(true);
    }

    // Waits until the die is still AND flat. A die that stops tilted (leaning on another die or the rail)
    // used to get kicked into the air, which often changed its face. Now it's lifted a little, turned so the
    // face that was most "up" is exactly up, and dropped again. Same face, no flip.
    private IEnumerator SettleFlat()
    {
        int tries = 0;
        float started = Time.time;
        while (true)
        {
            while (respawning != null
                   || rb.linearVelocity.sqrMagnitude > 0.01f
                   || rb.angularVelocity.sqrMagnitude > 0.01f)
            {
                if (Time.time - started > settleTimeout)
                {
                    Respawn("stuck rolling for too long");
                    started = Time.time;   // don't respawn again immediately
                }
                yield return null;
            }

            if (die.IsFlat(flatThreshold)) yield break;

            if (++tries > 3)   // wedged somewhere weird: start over from the spawn point
            {
                tries = 0;
                Respawn("stuck tilted after 3 straighten tries");
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
        SetInterpolation(false);
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

    // After the first bounce the die rolls freely for a moment, then damping ramps up until it stops.
    // Before this a throw could roll for ages and flip right at the end, which felt random and punishing.
    private void StartSettleAssist()
    {
        StopSettleAssist();
        settleAssist = StartCoroutine(SettleAssist());
    }

    private void StopSettleAssist()
    {
        if (settleAssist != null) StopCoroutine(settleAssist);
        settleAssist = null;
        settleAmount = 0f;
        rb.linearDamping = baseLinearDamping;
        rb.angularDamping = baseAngularDamping;
    }

    private IEnumerator SettleAssist()
    {
        float giveUp = Time.time + 1f;   // if it somehow never touches anything, start anyway
        while (landedAt < 0f && Time.time < giveUp) yield return null;
        yield return new WaitForSeconds(freeRollTime);

        // Only spin and sliding get damped, never falling: Linear Damping also slows gravity, which made a die
        // that was still in the air (bounced off another die) float down in slow motion.
        for (float t = 0f; t < settleRampTime; t += Time.deltaTime)
        {
            settleAmount = t / settleRampTime;
            rb.angularDamping = Mathf.Lerp(baseAngularDamping, settleDamping, settleAmount);
            yield return null;
        }
        settleAmount = 1f;
        rb.angularDamping = settleDamping;
    }

    // Safety net: if a die still ends up under the felt (a corner pushed through it), lift it straight back up
    // onto the felt where it went through, instead of letting it fall off and respawn at the spawn point.
    private void CatchTunneling()
    {
        if (rb.isKinematic || respawning != null || groundCollider == null || dieCollider == null) return;
        if (rb.linearVelocity.y > 0.5f) return;   // going up: it's bouncing, not sinking

        Bounds b = dieCollider.bounds;
        Ray down = new Ray(new Vector3(b.center.x, b.max.y + 3f, b.center.z), Vector3.down);
        if (!groundCollider.Raycast(down, out RaycastHit hit, 6f)) return;   // not over the felt
        if (b.max.y >= hit.point.y) return;                                   // at least partly above the surface: fine

        if (logRespawns) Debug.Log($"[Dice] {name} went through the felt at {transform.position}, lifted back up", this);
        Vector3 v = rb.linearVelocity;
        rb.position = new Vector3(rb.position.x, hit.point.y + b.extents.y + 0.02f, rb.position.z);
        rb.linearVelocity = new Vector3(v.x * 0.5f, 0f, v.z * 0.5f);
    }

    // Slows the sideways roll by hand, and only while the die is down on the table
    private void ApplySettleDamping()
    {
        if (settleAmount <= 0f || rb.isKinematic || dieCollider == null) return;
        if (dieCollider.bounds.min.y > groundY + 0.05f) return;   // still in the air: let gravity do its thing
        float slow = 1f / (1f + settleDamping * settleAmount * Time.fixedDeltaTime);
        Vector3 v = rb.linearVelocity;
        rb.linearVelocity = new Vector3(v.x * slow, v.y, v.z * slow);
    }

    // Interpolation smooths a moving die between physics steps (50 a second) while the game draws 60 to 144 frames.
    // Without it a held die moves in little jumps. It's switched off whenever the code moves the die by hand.
    private void SetInterpolation(bool on)
    {
        rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
    }

    // ---------- Sound ----------

    private void OnCollisionEnter(Collision collision)
    {
        // Only the table counts as landing. Hitting another die in mid-air used to start the settle timer early.
        if (landedAt < 0f && collision.collider.GetComponentInParent<Die>() == null) landedAt = Time.time;

        float speed = collision.relativeVelocity.magnitude;
        if (collision.collider == groundCollider && speed >= dustMinSpeed && owner != null && owner.LandDust != null)
        {
            ParticleSystem dust = owner.LandDust;
            dust.transform.position = collision.GetContact(0).point;   // where the corner actually hit the felt
            float hardness = Mathf.InverseLerp(dustMinSpeed, impactFullSpeed, speed);   // 0 = just hard enough, 1 = slammed
            dust.Emit(Mathf.RoundToInt(Mathf.Lerp(4f, 14f, hardness)));
        }
        if (speed < impactMinSpeed || Time.time < nextImpact) return;
        nextImpact = Time.time + 0.05f;   // one clack per bounce, not five

        AudioClip[] clips = owner != null ? owner.DiceHitClips : null;
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        PlaySound(clip, Mathf.Lerp(0.1f, 1f, Mathf.InverseLerp(impactMinSpeed, impactFullSpeed, speed)));
    }

    // While held: rattle clips back to back, louder the faster you move the die. Fades out fast when you let go.
    private void UpdateShake()
    {
        if (shakeSource == null) return;

        float target = 0f;
        if (isDragging)
        {
            float handSpeed = rb.linearVelocity.magnitude;
            target = Mathf.Lerp(shakeIdleVolume, 1f, Mathf.InverseLerp(0f, shakeFullSpeed, handSpeed));
        }
        float fade = isDragging ? shakeFade : shakeFade * 3f;
        shakeLevel = Mathf.Lerp(shakeLevel, target, 1f - Mathf.Exp(-fade * Time.deltaTime));

        if (isDragging && !shakeSource.isPlaying)
        {
            AudioClip[] clips = owner.DiceShakeClips;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip != null)
            {
                shakeSource.clip = clip;
                shakeSource.pitch = Random.Range(0.9f, 1.1f);
                shakeSource.Play();
            }
        }
        shakeSource.volume = shakeLevel * DiceAudio.Volume;
        if (!isDragging && shakeLevel < 0.01f && shakeSource.isPlaying) shakeSource.Stop();
    }

    private void PlaySound(AudioClip clip, float volume)
    {
        if (sfx == null || clip == null) return;
        sfx.pitch = Random.Range(0.9f, 1.15f);
        if (DiceAudio.Volume <= 0f) return;
        sfx.PlayOneShot(clip, volume * DiceAudio.Volume);
    }

    // ---------- Respawn ----------

    public void Respawn(string reason = "")
    {
        if (respawning != null) return;   // already on its way back
        if (logRespawns) Debug.Log($"[Dice] {name} ({(playerControlled ? "player" : "enemy")}) respawned: {reason} at {transform.position}", this);
        respawning = StartCoroutine(RespawnDice());
    }

    private Vector3 SpawnPosition => respawnPoint.position + spawnOffset;

    // DiceSide calls this so dice never spawn inside each other. Overlapping dice get shoved apart by the physics,
    // hard enough to fly off the table or land wedged, and both of those respawned them.
    public void SetSpawnOffset(Vector3 offset, bool moveNow)
    {
        spawnOffset = offset;
        if (moveNow && rb.isKinematic == false && !isDragging) transform.position = SpawnPosition;
    }

    // It used to slide back to the spawn point through the table, which looked like the game pulling your die back.
    // Now it pops in at the spawn point and drops.
    private IEnumerator RespawnDice()
    {
        isDragging = false;
        StopSettleAssist();
        SetInterpolation(false);
        rb.isKinematic = true;
        transform.SetPositionAndRotation(SpawnPosition, Random.rotation);

        for (float t = 0f; t < respawnPopTime; t += Time.deltaTime)
        {
            transform.localScale = baseScale * Mathf.SmoothStep(0.3f, 1f, t / respawnPopTime);
            yield return null;
        }
        transform.localScale = baseScale;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Random.insideUnitSphere * randomSpin;
        respawning = null;       // the old version never cleared this, so a die that fell off the table never settled again
    }

    // ---------- Lock in ----------

    private void MoveDiceToSlot(InputAction.CallbackContext context)
    {
        if (SettingsMenu.IsOpen || ChipTray.Targeting) return;   // a chip is waiting for an enemy die: clicks are for that
        if (!IsMouseOverMe()) return;
        if (isDragging || puttingBack || respawning != null) return;   // it's in your hand (or on its way back): drop it first
        if (State == DieState.Hovering)
        {
            StartWobble();
            Tutorial.Notice("THROW IT FIRST");
            return;
        }
        if (owner.InputBlocked || !IsMouseOverMe()) return;
        if (rollingPlayerDice > 0)
        {
            StartWobble();
            Tutorial.Notice("WAIT FOR YOUR DICE TO LAND");
            return;
        }
        if (!owner.HasSlotFor(die) && ThrowAttempts > 0 && owner.CouldFitLater(die))   // the Rabbit's Foot never has to be spent
        {
            StartWobble();
            Tutorial.Hint("noslot", "NO SLOT FOR THAT FACE · THROW IT AGAIN");
            return;
        }

        LockIn();
        ToggleInputs(false);
        Tutorial.Done("lock");
        Tutorial.Done("rabbit");
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

    public void SetThrowAttempts(int throwAttempts) { ThrowAttempts = throwAttempts; OnChanged?.Invoke(this); }

    public void Throw(Vector3 force, Vector3 torque)
    {
        if (spawnSettle != null) StopCoroutine(spawnSettle);
        rb.isKinematic = false;
        rb.useGravity = true;
        SetInterpolation(true);
        SetState(DieState.Rolling);          // the enemy threw it
        landedAt = -1f;

        rb.AddForce(force, ForceMode.Impulse);
        rb.AddTorque(torque, ForceMode.Impulse);

        SetThrowAttempts(ThrowAttempts - 1);
        OnChanged?.Invoke(this);
        Telemetry.DieThrown(owner);
        StartCoroutine(WaitForSettle());
    }

    public void ResetForNewRound()
    {
        StopAllCoroutines();   // nothing from last round keeps running
        SetRolling(false);
        respawning = null;
        wobbleRoutine = null;
        rotationRoutine = null;
        spawnSettle = null;
        settleAssist = null;
        isDragging = false;
        puttingBack = false;
        hasLanded = false;
        confirmUntil = -1f;
        ToggleInputs(false);
        StopSettleAssist();
        SetInterpolation(false);
        transform.localScale = baseScale;   // in case a respawn pop got cut off

        SetThrowAttempts(StartingThrowAttempts);
        transform.SetPositionAndRotation(SpawnPosition, Random.rotation);
        rb.isKinematic = false;
        rb.useGravity = true;
        if (playerControlled) GoToHover();
        else SpawnSettle();
    }

    private void SpawnSettle()
    {
        if (spawnSettle != null) StopCoroutine(spawnSettle);
        SetRolling(true);
        SetState(DieState.Rolling);          // dropping from the spawn point
        spawnSettle = StartCoroutine(WaitForSpawnSettle());
    }

    // On the table but out of play (swapped back after a fight): no hover, no input, no physics
    public void Idle()
    {
        HandControl();               // the method from the enemy slam: stops coroutines and physics
        ToggleInputs(false);
        SetState(DieState.Locked);   // anything but Hovering
    }

    // ------------------- State -------------------
    private void SetState(DieState next)
    {
        if (State == next) return;
        Debug.Log($"{name}: {State} -> {next}", this);   // log while State is still the OLD one
        State = next;
        if (glowing != null)
        {
            glowing.SetHighlight(0f);
            glowing = null;
        }
        OnChanged?.Invoke(this);
    }

    // Puts the die in a slot, or busts it if no slot takes its face
    private void LockIn()
    {
        bool fits = owner.HasSlotFor(die);   // ask BEFORE moving: afterwards the answer has changed
        owner.MoveDiceToSlot(die);
        SetState(fits ? DieState.Locked : DieState.Busted);
    }
}