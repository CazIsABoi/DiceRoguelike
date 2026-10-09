using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class DiceController : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private InputActionReference leftClick;
    [SerializeField] private InputActionReference rightClick;
    [SerializeField] private InputActionReference mousePos;

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
    private Coroutine respawning;

    // The last few drag positions, so the throw uses how fast you were moving the mouse (not where the die lagged to)
    private readonly Vector3[] trailPos = new Vector3[24];
    private readonly float[] trailTime = new float[24];
    private int trailHead, trailCount;

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

    [Header("Landing")]
    [SerializeField] private float flatThreshold = 0.95f;
    [SerializeField] private float straightenLift = 0.2f;   // a tilted die is lifted this much, straightened, then dropped
    [SerializeField] private float straightenTime = 0.15f;

    [Header("Game Variables")]
    [SerializeField] private int ThrowAttempts = 2;
    [SerializeField] private float killHeight = -0.5f;
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
    private Die die;

    private DiceSide owner;
    private bool playerControlled;

    // How many of the player's dice are in the air right now. While any are, nothing can be locked in,
    // so you can't slot a die and then watch the one still rolling land on something better.
    private static int rollingPlayerDice;
    private bool rolling;

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
        rb.maxAngularVelocity = 40f;   // the default (7) caps the spin, so hard throws looked like soft ones
        SetInterpolation(false);       // only on while physics moves it (see SetInterpolation)
    }

    public void Setup(DiceSide owner, Transform ground, Transform RespawnPoint, bool playerControlled)
    {
        this.owner = owner;
        this.playerControlled = playerControlled;
        respawnPoint = RespawnPoint;
        groundY = ground.GetComponent<Collider>().bounds.max.y;
        hasLanded = false;

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
        SpawnSettle();
    }

    private void OnDisable()
    {
        SetRolling(false);
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
        PlayDiceRotation(1f);
        PlaySound(owner.DiceGrabClip, 0.5f);
    }

    private void Update()
    {
        if (isDragging) Drag();
        UpdateShake();

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
        RecordTrail(targetPosition);   // every frame, even when the mouse is still: stopping before you let go is a gentle drop
    }

    private void FixedUpdate()
    {
        if (!isDragging)
        {
            ApplySettleDamping();
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

        if (ThrowAttempts > 0) ThrowAttempts--;
        else owner.SpendBonusRethrow();   // the Rabbit's Foot throw
        Tutorial.Done("throw");
        Tutorial.Done("lock");     // the hint says "lock it in OR throw again", so throwing again counts too
        Tutorial.Done("noslot");
        Tutorial.Done("rabbit");
        SetRolling(true);
        StartCoroutine(WaitForSettle());
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
        puttingBack = false;
    }

    // ---------- Rethrow safety ----------

    private bool NeedsConfirm()
    {
        if (!confirmBestFace || !hasLanded || Time.time <= confirmUntil) return false;
        if (owner.Table != TableType.High) return false;   // at Low and Target tables the "best" face isn't the biggest one
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
        StartSettleAssist();
        yield return new WaitForSeconds(0.3f);   // give it time to actually start falling
        yield return SettleFlat();
        StopSettleAssist();
        SetInterpolation(false);
        SetRolling(false);

        hasLanded = true;
        if (!playerControlled || !CanThrowAgain)
        {
            owner.MoveDiceToSlot(die);
        }
        else
        {
            rb.isKinematic = true;
            ToggleInputs(true);
            if (ThrowAttempts <= 0) Tutorial.Hint("rabbit", "RABBIT'S FOOT: THROW IT ONCE MORE · OR RIGHT CLICK TO LOCK IT");
            else Tutorial.Hint("lock", "RIGHT CLICK TO LOCK IT IN · OR DRAG TO THROW AGAIN");
        }
    }

    private IEnumerator WaitForSpawnSettle()
    {
        yield return new WaitForSeconds(0.3f);
        yield return SettleFlat();
        SetInterpolation(false);
        SetRolling(false);
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

    public void Respawn()
    {
        if (respawning != null) return;   // already on its way back
        respawning = StartCoroutine(RespawnDice());
    }

    // It used to slide back to the spawn point through the table, which looked like the game pulling your die back.
    // Now it pops in at the spawn point and drops.
    private IEnumerator RespawnDice()
    {
        isDragging = false;
        StopSettleAssist();
        SetInterpolation(false);
        rb.isKinematic = true;
        transform.SetPositionAndRotation(respawnPoint.position, Random.rotation);

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
        if (isDragging || puttingBack || respawning != null) return;   // it's in your hand (or on its way back): drop it first
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

        owner.MoveDiceToSlot(die);
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

    public void SetThrowAttempts(int throwAttempts) { ThrowAttempts = throwAttempts; }

    public void Throw(Vector3 force, Vector3 torque)
    {
        if (spawnSettle != null) StopCoroutine(spawnSettle);
        rb.isKinematic = false;
        rb.useGravity = true;
        SetInterpolation(true);
        landedAt = -1f;

        rb.AddForce(force, ForceMode.Impulse);
        rb.AddTorque(torque, ForceMode.Impulse);

        ThrowAttempts--;
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

        ThrowAttempts = StartingThrowAttempts;
        transform.SetPositionAndRotation(respawnPoint.position, Random.rotation);
        rb.isKinematic = false;
        rb.useGravity = true;
        SpawnSettle();
    }

    private void SpawnSettle()
    {
        if (spawnSettle != null) StopCoroutine(spawnSettle);
        SetRolling(true);   // the drop from the spawn point counts as rolling too: its face can be locked in
        spawnSettle = StartCoroutine(WaitForSpawnSettle());
    }
}