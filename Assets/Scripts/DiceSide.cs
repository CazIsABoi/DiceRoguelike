using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public abstract class DiceSide : MonoBehaviour
{
    [ContextMenu("Log best possible")]
    private void LogBest() => Debug.Log($"best possible: {BestPossibleScore()}");

    public event System.Action OnDiceChanged;   // any of my dice changed: anything board-wide may be stale
    protected abstract bool IsPlayerControlled { get; }
    [Header("References")]
    [SerializeField] private GameController gameController;
    [SerializeField] private AudioSource audio;
    protected GameController Game => gameController;
    public TableType Table => gameController != null ? gameController.Table : TableType.High;
    [SerializeField] private Collider playArea;

    [Header("Health")]
    [SerializeField] protected int maxHealth = 100;
    public int MaxHealth => maxHealth;
    public int Health { get; private set; }
    public bool IsDead => Health <= 0;
    public event System.Action<int, int> OnHealthChanged; // (current, max)

    [Header("Slots")]
    protected List<DiceSlot> diceSlots = new List<DiceSlot>();
    [SerializeField] private float moveTime = 0.25f;
    private int diceInSlots = 0;
    [SerializeField] private DiceSlot slotPrefab;
    [SerializeField] private float spacing = 1.2f;
    [SerializeField] private Transform anchor;
    public bool HasSlotFor(Die die) => ChooseSlot(die) != null;

    [Header("Dice")]
    [SerializeField] private Die diePrefab; // Prefab
    protected Die[] spawnedDice;
    [SerializeField] private Transform spawnPoint; // Where dice spawn
    [SerializeField] private float spawnSpacing = 0.7f;   // dice spawn in a row along the spawn point's red arrow, this far apart (a bit more than one die)
    [SerializeField] private Transform ground;
    protected Collider GroundCollider => ground != null ? ground.GetComponent<Collider>() : null;

    [Header("Bust")]
    [SerializeField] private FaceDefinition numberFiller;     // a number face with value 0
    [SerializeField] private FaceDefinition operatorFiller;   // your + face
    protected List<Die> bustedDice = new List<Die>();

    [Header("Count")]
    [SerializeField] private float countStepTime = 0.4f;
    [SerializeField] private float countFastForward = 4f;   // holding the mouse button plays it this many times faster
    [SerializeField] private ParticleSystem placeSparks;   // ONE system in the scene, reused for every placement
    [SerializeField] private int luckySparks = 40;         // the best face
    [SerializeField] private int maxPlaceSparks = 8;       // any other face: 0 to this many, by luck
    [SerializeField] private float readTime = 0.18f;      // seconds per token being read
    [SerializeField] private float readPopSize = 170f;    // percent: how big a token jumps when it's read
    [SerializeField] private float readHoldSize = 125f;   // ...and what it settles to before the next one
    [SerializeField] private AudioClip[] readClips;       // a soft tick per token (a different sound from the collapse)

    [Header("UI")]
    [SerializeField] protected TMP_Text equationText;
    private Coroutine equationPunchRoutine;
    [SerializeField] private AudioClip equationSFX;
    [SerializeField] private DieLabel labelPrefab;
    [Header("Effects")]
    [SerializeField] private ParticleSystem landDust; 

    [Header("Total")]
    // New names on purpose, so the old Inspector values (60% size, 35% see-through) don't carry over:
    // a dot/pixel font breaks apart when it's scaled down or faded, so the total is full size and only dimmed a little.
    [SerializeField, Range(0f, 1f)] private float totalOpacity = 0.55f;    // "= 437"
    [SerializeField, Range(0f, 1f)] private float accentOpacity = 1f;      // "12 OFF", the number that decides a Low or Target round
    [SerializeField, Range(20, 100)] private int totalSize = 100;          // percent of the equation's size. Keep 100 with a pixel font
    [SerializeField] private bool totalBelow = false;                      // put the total on its own line under the equation (for a narrow screen)
    [SerializeField] private bool centerWithTotal = false;                 // an invisible copy on the left keeps the equation centred, but doubles the line width
    private string totalText = "";
    private string totalAccent = "";
    private const string NoBreakSpace = "\u00A0";

    [Header("Dice Sounds")]
    [SerializeField] private AudioClip[] diceHitClips;   // clacks when a die hits the felt, the rail or another die (a random one each time)
    [SerializeField] private AudioClip diceGrabClip;     // optional
    [SerializeField] private AudioClip[] diceShakeClips; // rattles while you hold a die, louder the faster you move it (one after another, random order)
    [SerializeField] private AudioClip[] luckyClips;
    [SerializeField] private AudioClip[] countClips;
    private int lastClip = -1;
    public AudioClip[] DiceHitClips => diceHitClips;
    public AudioClip DiceGrabClip => diceGrabClip;
    public AudioClip[] DiceShakeClips => diceShakeClips;
    public bool HasDiceSounds => (diceHitClips != null && diceHitClips.Length > 0) || diceGrabClip != null || (diceShakeClips != null && diceShakeClips.Length > 0);

    [Header("Juice")]
    [SerializeField] private float placePopMin = 1.15f;   // placing this die's worst face
    [SerializeField] private float placePopMax = 1.9f;    // placing its best face
    [SerializeField] private float luckyDiePop = 1.6f;    // the die itself, on its best face
    [SerializeField] private float countPop = 1.5f;       // each step of the count
    [SerializeField] private float luckyHitStop = 0.06f;  // seconds the game freezes on a lucky face (0 = off)
    public void DebugSetHealth(int hp) => InitHealth(hp);   // dev panel: max and current both become hp

    // While this is true the player's dice can't be grabbed or locked in (the table intro and the spare swap)
    public bool InputBlocked { get; set; }
    private int hiddenSlot = -1;
    public int HiddenSlot => hiddenSlot;
    public int SlotCount => diceSlots.Count;
    public DiceSlot GetSlot(int index) => diceSlots[index];
    public void HideSlot(int index) { hiddenSlot = index; RefreshEquation(false); }
    public void RevealSlots() { if (hiddenSlot < 0) return; hiddenSlot = -1; RefreshEquation(true); }

    // Rabbit's Foot: PlayerController overrides these. Enemies never get a bonus rethrow.
    public virtual bool HasBonusRethrow => false;
    public virtual void SpendBonusRethrow() { }

    private void HandleDieChanged(DiceController changed) => OnDiceChanged?.Invoke();

    // ---------- Dice ----------

    protected void SpawnDice(DieDefinition[] dice)
    {
        spawnedDice = new Die[dice.Length];
        for (int i = 0; i < dice.Length; i++)
        {
            spawnedDice[i] = SpawnOneDie(dice[i]);

        }
        AssignSpawnSpots(true);
    }

    // Every die gets its own spot in a row at the spawn point, so none of them spawn inside another.
    // moveNow: also move them there right away (for dice that were just instantiated on the same point).
    protected void AssignSpawnSpots(bool moveNow)
    {
        if (spawnedDice == null) return;
        for (int i = 0; i < spawnedDice.Length; i++)
        {
            if (spawnedDice[i] == null) continue;
            Vector3 offset = spawnPoint.right * ((i - (spawnedDice.Length - 1) / 2f) * spawnSpacing);
            spawnedDice[i].GetComponent<DiceController>().SetSpawnOffset(offset, moveNow);
        }
    }
    protected Die SpawnOneDie(DieDefinition def)
    {
        Die prefab = def.prefab != null ? def.prefab : diePrefab;
        Die newDie = Instantiate(prefab, spawnPoint.position, Random.rotation);
        newDie.Initialize(def);
        DiceController controller = newDie.GetComponent<DiceController>();
        controller.Setup(this, ground, spawnPoint, playArea,IsPlayerControlled);

        controller.OnChanged += HandleDieChanged;
        if (IsPlayerControlled && labelPrefab != null)
        {
            Instantiate(labelPrefab).Follow(controller, this);
        }
        return newDie;
    }
    protected void DestroyDice()
    {
        if (spawnedDice == null) return;   // first fight: nothing to destroy yet
        foreach (Die d in spawnedDice)
        {
            if (d != null) Destroy(d.gameObject);
        }
        spawnedDice = null;
        diceInSlots = 0;
        bustedDice.Clear();
    }

    // Called by GameController when it's time for a new round
    public void ResetRound()
    {
        if (IsPlayerControlled) Telemetry.StartRound();
        AssignSpawnSpots(false);
        for (int i = 0; i < spawnedDice.Length; i++)
        {
            spawnedDice[i].GetComponent<DiceController>().ResetForNewRound();
        }

        for (int i = 0; i < diceSlots.Count; i++)
        {
            diceSlots[i].Clear();
        }

        diceInSlots = 0;
        bustedDice.Clear();
        RefreshEquation(true);
    }

    // ---------- Slots ----------


    protected void SpawnSlots(List<FaceType> pattern)
    {
        for (int i = 0; i < diceSlots.Count; i++)
        {
            Destroy(diceSlots[i].gameObject);
        }
        diceSlots.Clear();

        for (int i = 0; i < pattern.Count; i++)
        {
            float x = (i - (pattern.Count - 1) / 2f) * spacing;
            DiceSlot newSlot = Instantiate(slotPrefab, anchor);
            newSlot.transform.localPosition = new Vector3(x, 0f, 0f);
            newSlot.Setup(pattern[i]);
            diceSlots.Add(newSlot);
        }
    }

    public virtual void MoveDiceToSlot(Die die)
    {
        DiceSlot slot = ChooseSlot(die);
        if (slot == null) { Bust(die); return; }
        PlaceInSlot(die, slot);
    }
    protected virtual DiceSlot ChooseSlot(Die die)
    {
        foreach (DiceSlot slot in diceSlots)
        {
            if (!slot.CanAccept(die)) continue;
            return slot;
        }
        return null; // no slots
    }

    protected void PlaceInSlot(Die die, DiceSlot slot)
    {
        Rigidbody rb = die.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        slot.Place(die);
        StartCoroutine(MoveRoutine(die.transform, slot.transform));
    }

    private IEnumerator MoveRoutine(Transform dice, Transform slot)
    {
        Vector3 startPos = dice.position;
        Quaternion startRot = dice.rotation;
        float elapsed = 0f;

        Transform top = dice.GetComponent<Die>().GetTopSocket();
        Vector3 localUp = dice.InverseTransformDirection(top.up);
        Vector3 localForward = dice.InverseTransformDirection(top.forward);
        Quaternion targetRot = slot.rotation * Quaternion.Inverse(Quaternion.LookRotation(localForward, localUp));
        float restHeight = Vector3.Distance(top.position, dice.position);   // center to the top face
        Vector3 targetPos = slot.position + slot.up * restHeight;

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveTime;
            t = Mathf.SmoothStep(0f, 1f, t);
            dice.position = Vector3.Lerp(startPos, targetPos, t);
            dice.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        dice.position = targetPos;
        dice.rotation = targetRot;

        diceInSlots++;
        RefreshEquation(true);

        Die placed = dice.GetComponent<Die>();
        float luck = Luck(placed, placed.GetTopFace());
        if (luck >= 1f)
        {
            StartCoroutine(GameController.HitStop(luckyHitStop));
            StartCoroutine(GameController.Pop(placed.transform, placed.transform.localScale, luckyDiePop, 0.35f));
            PlayRandom(luckyClips);
            Sparks(placed.transform.position, luckySparks);
        }
        else
        {
            Sparks(placed.transform.position, Mathf.RoundToInt(luck * maxPlaceSparks));
        }
        CheckComplete(Mathf.Lerp(placePopMin, placePopMax, luck));
    }

    // Plays a random clip from the array, never the same one twice in a row
    private void PlayRandom(AudioClip[] clips, float pitch = 1f, float volume = 1f)
    {
        if (audio == null || clips == null || clips.Length == 0) return;
        int i = Random.Range(0, clips.Length);
        if (clips.Length > 1 && i == lastClip) i = (i + 1) % clips.Length;   // repeated? take the next one instead
        lastClip = i;
        audio.pitch = pitch;
        audio.PlayOneShot(clips[i], volume);
    }

    private static readonly Vector3[] axes =
    {
        Vector3.up, Vector3.down, Vector3.right,
        Vector3.left, Vector3.forward, Vector3.back
    };

    private Vector3 ClosestLocalAxis(Transform t, Vector3 worldDir)
    {
        Vector3 best = axes[0];
        float bestDot = -Mathf.Infinity;

        foreach (Vector3 axis in axes)
        {
            Vector3 worldAxis = t.TransformDirection(axis);
            float dot = Vector3.Dot(worldAxis, worldDir);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = axis;
            }
        }

        return best;
    }

    // ---------- Equation ----------

    // The faces in the slots, left to right, with fillers in the empty ones
    private List<FaceDefinition> SlotFaces()
    {
        List<FaceDefinition> faces = new List<FaceDefinition>();
        foreach (DiceSlot slot in diceSlots)
        {
            if (slot.IsEmpty) faces.Add(slot.AcceptableFace == FaceType.Number ? numberFiller : operatorFiller);
            else faces.Add(slot.CurrentDie.GetTopFace());
        }
        return faces;
    }

    private int EvaluateSlots() => GameController.Evaluate(SlotFaces());

    protected string BuildEquation(bool playSound = true)
    {
        string text = "";
        for (int i = 0; i < diceSlots.Count; i++)
        {
            if (diceSlots[i].IsEmpty)
            {
                bool filled = bustedDice.Count > 0;   // after a bust, empty slots get their filler
                if (diceSlots[i].AcceptableFace == FaceType.Number) text += filled ? "0" : "_";
                else text += filled ? GameController.OpSymbol(operatorFiller.op) : "?";
                continue;
            }

            if (i == hiddenSlot) { text += "?"; continue; }   // Poker Face: the die is there, you just can't see it

            FaceDefinition face = diceSlots[i].CurrentDie.GetTopFace();
            if (face.type == FaceType.Number)
            {
                text += face.number;
            }
            else
            {
                text += GameController.OpSymbol(face.op);
            }
        }
        if (playSound)
        {
            audio.pitch = Random.Range(.5f, 1.5f);
            audio.PlayOneShot(equationSFX);
            audio.pitch = 1f;
        }
        if (bustedDice.Count > 0) text += gameController.BustText(BustPenalty());
        return text;
    }

    // Rebuilds the equation text, with the last total (if there is one) small and faint on the right.
    // At Low and Target tables the distance ("12 OFF") is brighter than the rest, because that's the number that wins.
    protected void RefreshEquation(bool playSound)
    {
        string equation = BuildEquation(playSound);
        if (string.IsNullOrEmpty(totalText))
        {
            equationText.text = equation; 
            return;
        }

        string faint = Mathf.RoundToInt(totalOpacity * 255f).ToString("X2");
        string strong = Mathf.RoundToInt(accentOpacity * 255f).ToString("X2");
        string gap = totalBelow ? "\n" : NoBreakSpace + NoBreakSpace;
        string plain = NoBreak(totalText) + (string.IsNullOrEmpty(totalAccent) ? "" : $"{NoBreakSpace}·{NoBreakSpace}{NoBreak(totalAccent)}");
        string shown = string.IsNullOrEmpty(totalAccent)
            ? NoBreak(totalText)
            : $"{NoBreak(totalText)}{NoBreakSpace}·{NoBreakSpace}<alpha=#{strong}>{NoBreak(totalAccent)}";

        string total = $"<size={totalSize}%>{gap}<alpha=#{faint}>{shown}</size>";
        string balance = centerWithTotal && !totalBelow ? $"<alpha=#00><size={totalSize}%>{plain}{gap}</size>" : "";
        equationText.text = $"{balance}<alpha=#FF>{equation}{total}";
    }

    // How lucky this face was, from 0 (the worst this die has for this table) to 1 (the best)
    private float Luck(Die die, FaceDefinition face)
    {
        if (face == null || face.type != FaceType.Number) return 0.5f;   // operators: middle pop for now

        int min = int.MaxValue;
        int max = int.MinValue;
        foreach (FaceDefinition f in die.GetCurrentFaces())
        {
            if (f == null || f.type != FaceType.Number) continue;
            min = Mathf.Min(min, f.number);
            max = Mathf.Max(max, f.number);
        }
        if (max == min) return 1f;   // only one number on the die: nothing better to roll

        float up = (face.number - min) / (float)(max - min);   // 0 at its lowest number, 1 at its highest
        switch (Table)
        {
            case TableType.High: return up;        // big is lucky
            case TableType.Low: return 1f - up;   // small is lucky
            default: return 0.5f;      // Target: luck depends on the whole equation, not one die
        }
    }

    // No-break spaces: the total never wraps onto its own line
    private static string NoBreak(string text) => text.Replace(" ", NoBreakSpace);

    // GameController calls this when this side's equation is done: ShowTotal("= 437") or ShowTotal("= 437", "12 OFF").
    // It stays through the round reset until the next equation is done, like the score.
    public void ShowTotal(string text, string accent = "")
    {
        totalText = text;
        totalAccent = accent ?? "";
        RefreshEquation(false);
    }

    public void ClearTotal()
    {
        totalText = "";
        totalAccent = "";
        if (diceSlots.Count > 0) RefreshEquation(false);
    }

    private void PunchEquation(float scale, float duration)
    {
        if (equationPunchRoutine != null) StopCoroutine(equationPunchRoutine);
        equationPunchRoutine = StartCoroutine(GameController.Pop(equationText.transform, Vector3.one, scale, duration));
    }
    public bool CouldFitLater(Die die)
    {
        foreach (FaceDefinition face in die.GetCurrentFaces())
        {
            if (face == null) continue;
            foreach (DiceSlot slot in diceSlots)
            {
                if (slot.IsEmpty && slot.AcceptableFace == face.type) return true;
            }
        }
        return false;
    }

    protected void Bust(Die die)
    {
        if (bustedDice.Contains(die)) return;
        Telemetry.Bust(IsPlayerControlled, die.GetTopFace());
        bustedDice.Add(die);
        Rigidbody body = die.GetComponent<Rigidbody>();
        if (body != null) body.isKinematic = true;   // it stays where it landed; with physics on, the reward screen could drop it
        OnBusted(die);
        RefreshEquation(true);
        CheckComplete();
    }

    protected virtual void OnBusted(Die die) { }

    private void CheckComplete(float punch = 1.15f) 
    {
        if (diceInSlots + bustedDice.Count == spawnedDice.Length)
        {
            int result = EvaluateSlots();
            int penalty = BustPenalty();
            Telemetry.EquationDone(IsPlayerControlled, EquationForLog(), result, penalty, IsPlayerControlled ? DescribeDice() : null);
            gameController.OnEquationComplete(this, result, penalty);
        }
        else PunchEquation(punch, 0.2f);
    }
    public bool IsBest(Die die)
    {
        if (CanImprove(die)) return false;   // also false while dice are still in the air
        int current = BestPossibleScore();
        foreach (FaceDefinition face in die.GetCurrentFaces())
        {
            if (face != null && BestPossibleScore(die, face) < current) return true;   // this face would be worse
        }
        return false;   // every face ties: the die is irrelevant, not "best"
    }

    // Counts this side's equation on the monitor, then what the table does to the result. CountShow does the show.
    public IEnumerator PlayCount(System.Action onLand)
    {
        List<FaceDefinition> faces = new List<FaceDefinition>();
        foreach (DiceSlot slot in diceSlots)
        {
            if (slot.IsEmpty) faces.Add(slot.AcceptableFace == FaceType.Number ? numberFiller : operatorFiller);
            else faces.Add(slot.CurrentDie.GetTopFace());
        }

        CountShow counter = Game.Counter;
        if (counter == null) { equationText.text = GameController.Evaluate(faces).ToString(); yield break; }
        yield return counter.PlaySide(equationText, GameController.CountSteps(faces), BustPenalty(), onLand);
    }

    // One token jumps big and eases down to "slightly big" while the rest stays normal
    private IEnumerator ReadToken(GameController.CountStep step, int token)
    {
        float t = 0f;
        while (t < readTime)
        {
            float k = t / readTime;
            k = 1f - (1f - k) * (1f - k);   // ease out: fast shrink at first, gentle at the end
            equationText.text = GameController.StepText(step, token, Mathf.Lerp(readPopSize, readHoldSize, k));
            t += Time.deltaTime * CountSpeed;
            yield return null;
        }
    }

    private float CountSpeed => Mouse.current != null && Mouse.current.leftButton.isPressed ? countFastForward : 1f;

    private IEnumerator CountWait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime * CountSpeed) yield return null;
    }

    // The finished equation as plain text for the sheet, e.g. "6+3×2" (empty slots after a bust show their filler)
    private string EquationForLog()
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        foreach (DiceSlot slot in diceSlots)
        {
            if (slot.IsEmpty) text.Append(slot.AcceptableFace == FaceType.Number ? "0" : GameController.OpSymbol(operatorFiller.op));
            else text.Append(Telemetry.Name(slot.CurrentDie.GetTopFace()));
        }
        return text.ToString();
    }

    // Every face of every die, e.g. "[1 2 3 + 5 6] [2 2 × 4 4 6]"
    private string DescribeDice()
    {
        if (spawnedDice == null) return "";
        List<string> dice = new List<string>();
        foreach (Die die in spawnedDice)
        {
            if (die == null) continue;
            List<string> faces = new List<string>();
            foreach (FaceDefinition face in die.GetCurrentFaces()) faces.Add(face == null ? "-" : Telemetry.Name(face));
            dice.Add("[" + string.Join(" ", faces) + "]");
        }
        return string.Join(" ", dice);
    }

    // The best score your resting dice can still reach.
    public int BestPossibleScore(Die pretendDie = null, FaceDefinition pretendFace = null)
    {
        // 1. What's already locked in. null = empty slot.
        List<FaceDefinition> slots = new List<FaceDefinition>();
        foreach (DiceSlot slot in diceSlots)
            slots.Add(slot.IsEmpty ? null : slot.CurrentDie.GetTopFace());

        // 2. The faces still on the table, waiting to be placed
        List<FaceDefinition> free = new List<FaceDefinition>();
        foreach (Die d in spawnedDice)
        {
            if (d == null) continue;
            if (d.GetComponent<DiceController>().State != DieState.Resting) continue;
            free.Add(d == pretendDie ? pretendFace : d.GetTopFace());   // "what if this die showed that face?"
        }

        // 3. Start with nothing placed and the busts that already happened
        int best = int.MinValue;
        Search(slots, free, 0, BustPenalty(), ref best);
        return best;
    }
    private void Search(List<FaceDefinition> slots, List<FaceDefinition> free, int index, int bust, ref int best)
    {
        // 4. Everyone is seated: score this arrangement
        if (index == free.Count)
        {
            List<FaceDefinition> filled = new List<FaceDefinition>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null) filled.Add(slots[i]);
                else filled.Add(diceSlots[i].AcceptableFace == FaceType.Number ? numberFiller : operatorFiller);
            }
            int score = Game.TableScore(GameController.Evaluate(filled), bust);
            if (score > best) best = score;
            return;
        }

        // 5. Try this face in every empty slot that takes it
        FaceDefinition face = free[index];
        bool fitted = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null || diceSlots[i].AcceptableFace != face.type) continue;
            fitted = true;
            slots[i] = face;                                  // sit down here
            Search(slots, free, index + 1, bust, ref best);   // see how everyone else does
            slots[i] = null;                                  // stand back up before trying the next seat
        }

        // 6. No seat for it: it busts, and the search carries on without it
        if (!fitted)
        {
            int penalty = face.type == FaceType.Number ? face.number : Game.OperatorBustPenalty;
            Search(slots, free, index + 1, bust + penalty, ref best);
        }
    }

    public bool CanImprove(Die die)
    {
        for (int i = 0; i < spawnedDice.Length; i++)
        {
            if (spawnedDice[i] == null) continue;
            if (spawnedDice[i].GetComponent<DiceController>().State != DieState.Resting) return true;
        }
        int current = BestPossibleScore();
        
        foreach (FaceDefinition face in die.GetCurrentFaces())
        {
            if (face == null) continue;
            if (BestPossibleScore(die, face) > current) return true;
        }
        return false;
    }

    // Moves the spark system to the die and fires 'count' particles from there
    private void Sparks(Vector3 at, int count)
    {
        if (placeSparks == null || count <= 0) return;
        placeSparks.transform.position = at;
        placeSparks.Emit(count);
    }
    public ParticleSystem LandDust
    {
        get
        {
            if (landDust != null && !landDust.gameObject.scene.IsValid()) landDust = Instantiate(landDust);
            return landDust;
        }
    }
    private int BustPenalty()
    {
        int total = 0;
        foreach (Die die in bustedDice)
        {
            FaceDefinition face = die.GetTopFace();
            total += face.type == FaceType.Number ? face.number : gameController.OperatorBustPenalty;
        }
        return total;
    }
    public void NotifyDiceChanged()
    {
        OnDiceChanged?.Invoke();
        if (diceSlots.Count > 0) RefreshEquation(false);
    }

    #region Health

    protected void InitHealth(int max)
    {
        maxHealth = max;
        Health = max;
        OnHealthChanged?.Invoke(Health, maxHealth);
    }

    public void ResetHealth() => InitHealth(maxHealth);

    public void TakeDamage(int amount)
    {
        Health = Mathf.Max(0, Health - amount);
        OnHealthChanged?.Invoke(Health, maxHealth);
    }

    public void Heal(int amount)
    {
        Health = Mathf.Min(maxHealth, Health + amount);
        OnHealthChanged?.Invoke(Health, maxHealth);
    }

    // Changes max HP but keeps the same share of it (63/100 becomes 630/1000)
    public void SetMaxHealthKeepRatio(int newMax)
    {
        if (newMax == maxHealth) return;
        Health = Mathf.Max(1, Mathf.RoundToInt((float)Health * newMax / maxHealth));
        maxHealth = newMax;
        OnHealthChanged?.Invoke(Health, maxHealth);
    }
    #endregion
}