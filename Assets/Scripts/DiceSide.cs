using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public abstract class DiceSide : MonoBehaviour
{
    protected abstract bool IsPlayerControlled { get; }
    [Header("References")]
    [SerializeField] private GameController gameController;
    [SerializeField] private AudioSource audio;
    protected GameController Game => gameController;
    public TableType Table => gameController != null ? gameController.Table : TableType.High;

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
    [SerializeField] private Transform ground;

    [Header("Bust")]
    [SerializeField] private FaceDefinition numberFiller;     // a number face with value 0
    [SerializeField] private FaceDefinition operatorFiller;   // your + face
    protected List<Die> bustedDice = new List<Die>();

    [Header("UI")]
    [SerializeField] protected TMP_Text equationText;
    private Coroutine equationPunchRoutine;

    [Header("UI")]
    [SerializeField] private AudioClip equationSFX;

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
    public AudioClip[] DiceHitClips => diceHitClips;
    public AudioClip DiceGrabClip => diceGrabClip;
    public AudioClip[] DiceShakeClips => diceShakeClips;
    public bool HasDiceSounds => (diceHitClips != null && diceHitClips.Length > 0) || diceGrabClip != null || (diceShakeClips != null && diceShakeClips.Length > 0);

    // While this is true the player's dice can't be grabbed or locked in (the table intro and the spare swap)
    public bool InputBlocked { get; set; }

    // Rabbit's Foot: PlayerController overrides these. Enemies never get a bonus rethrow.
    public virtual bool HasBonusRethrow => false;
    public virtual void SpendBonusRethrow() { }

    // ---------- Dice ----------

    protected void SpawnDice(DieDefinition[] dice)
    {
        spawnedDice = new Die[dice.Length];
        for (int i = 0; i < dice.Length; i++)
        {
            spawnedDice[i] = SpawnOneDie(dice[i]);
        }
    }
    protected Die SpawnOneDie(DieDefinition def)
    {
        Die prefab = def.prefab != null ? def.prefab : diePrefab;
        Die newDie = Instantiate(prefab, spawnPoint.position, Random.rotation);
        newDie.Initialize(def);
        newDie.GetComponent<DiceController>().Setup(this, ground, spawnPoint, IsPlayerControlled);
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

        CheckComplete();
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

    // The plain result of what's in the slots. Busts are kept apart (BustPenalty), because a Low or
    // Target table adds them to your distance instead of taking them off your result.
    private int EvaluateSlots()
    {
        List<FaceDefinition> faces = new List<FaceDefinition>();
        for (int i = 0; i < diceSlots.Count; i++)
        {
            if (diceSlots[i].IsEmpty)
            {
                faces.Add(diceSlots[i].AcceptableFace == FaceType.Number ? numberFiller : operatorFiller);
                continue;
            }
            faces.Add(diceSlots[i].CurrentDie.GetTopFace());
        }
        return GameController.Evaluate(faces);
    }

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
        equationPunchRoutine = StartCoroutine(GameController.Punch(equationText.transform, scale, duration, 0f));
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
        bustedDice.Add(die);
        Rigidbody body = die.GetComponent<Rigidbody>();
        if (body != null) body.isKinematic = true;   // it stays where it landed; with physics on, the reward screen could drop it
        OnBusted(die);
        RefreshEquation(true);
        CheckComplete();
    }

    protected virtual void OnBusted(Die die) { }

    private void CheckComplete()
    {
        if (diceInSlots + bustedDice.Count == spawnedDice.Length)
        {
            gameController.OnEquationComplete(this, EvaluateSlots(), BustPenalty());
        }
        else PunchEquation(1.15f, 0.2f);
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