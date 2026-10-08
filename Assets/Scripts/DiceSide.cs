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
        equationText.text = BuildEquation();
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
        if (IsAccountedFor(die)) return;   // a die can only be locked in or busted once per round
        DiceSlot slot = ChooseSlot(die);
        if (slot == null) { Bust(die); return; }
        PlaceInSlot(die, slot);
    }

    private bool IsAccountedFor(Die die)
    {
        if (bustedDice.Contains(die)) return true;
        foreach (DiceSlot slot in diceSlots)
            if (!slot.IsEmpty && slot.CurrentDie == die) return true;
        return false;
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
        equationText.text = BuildEquation();

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

    private int EvaluateSlots()
    {
        List<FaceDefinition> faces = new List<FaceDefinition>();
        for (int i = 0; i < diceSlots.Count; i++)
        {
            if (diceSlots[i].IsEmpty)
            {
                if (diceSlots[i].AcceptableFace == FaceType.Operator) faces.Add(operatorFiller);
                continue;   // empty number slots add no digit, so a busted 7_ stays 7 instead of 70
            }
            faces.Add(diceSlots[i].CurrentDie.GetTopFace());
        }
        return GameController.Evaluate(faces) - BustPenalty();
    }
    private bool GroupHasDigit(int index)
    {
        for (int i = index; i >= 0 && diceSlots[i].AcceptableFace == FaceType.Number; i--)
            if (!diceSlots[i].IsEmpty) return true;
        for (int i = index + 1; i < diceSlots.Count && diceSlots[i].AcceptableFace == FaceType.Number; i++)
            if (!diceSlots[i].IsEmpty) return true;
        return false;
    }

    protected string BuildEquation()
    {
        string text = "";
        for (int i = 0; i < diceSlots.Count; i++)
        {
            if (diceSlots[i].IsEmpty)
            {
                bool filled = bustedDice.Count > 0;   // after a bust, empty slots get their filler
                if (diceSlots[i].AcceptableFace == FaceType.Number)
                {
                    bool firstOfGroup = i == 0 || diceSlots[i - 1].AcceptableFace != FaceType.Number;
                    if (!filled) text += "_";
                    else if (!GroupHasDigit(i) && firstOfGroup) text += "0";   // a fully empty number shows as one 0
                                                                               // otherwise show nothing: the digits that are there are the whole number
                }
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
        audio.pitch = Random.Range(.5f, 1.5f);
        audio.PlayOneShot(equationSFX);
        audio.pitch = 1f;
        if (bustedDice.Count > 0) text += " - " + BustPenalty();
        return text;
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
        OnBusted(die);
        equationText.text = BuildEquation();
        CheckComplete();
    }

    protected virtual void OnBusted(Die die) { }

    private void CheckComplete()
    {
        if (diceInSlots + bustedDice.Count == spawnedDice.Length)
        {
            gameController.OnEquationComplete(this, EvaluateSlots());
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