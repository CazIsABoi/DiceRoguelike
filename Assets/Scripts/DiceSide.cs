using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public abstract class DiceSide : MonoBehaviour
{
    protected abstract bool IsPlayerControlled { get; }
    [Header("References")]
    [SerializeField] private GameController gameController;

    [Header("Slots")]
    protected List<DiceSlot> diceSlots = new List<DiceSlot>();
    [SerializeField] private float moveTime = 0.25f;
    private int diceInSlots = 0;
    [SerializeField] private DiceSlot slotPrefab;
    [SerializeField] private float spacing = 1.2f;
    [SerializeField] private Transform anchor;

    [Header("Dice")]
    [SerializeField] private Die diePrefab; // Prefab
    protected Die[] spawnedDice;
    [SerializeField] private float dieHeight = 0.5f;
    [SerializeField] private Transform spawnPoint; // Where dice spawn
    [SerializeField] private Transform ground;

    [Header("UI")]
    [SerializeField] protected TMP_Text equationText;
    private Coroutine equationPunchRoutine;

    // ---------- Dice ----------

    protected void SpawnDice(DieDefinition[] dice)
    {
        spawnedDice = new Die[dice.Length];
        for (int i = 0; i < dice.Length; i++)
        {
            Die newDie = Instantiate(diePrefab, spawnPoint.position, Random.rotation);
            spawnedDice[i] = newDie;
            spawnedDice[i].Initialize(dice[i]);

            DiceController controller = newDie.GetComponent<DiceController>();
            controller.Setup(this, ground, spawnPoint, IsPlayerControlled);
        }
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

    public void MoveDiceToSlot(Die dice)
    {
        Rigidbody rb = dice.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        bool placed = false;

        for (int i = 0; i < diceSlots.Count; i++)
        {
            if (!diceSlots[i].CanAccept(dice)) continue;
            StartCoroutine(MoveRoutine(dice.transform, diceSlots[i].transform));
            diceSlots[i].Place(dice);
            placed = true;
            break;
        }

        if (!placed)
        {
            rb.isKinematic = false; // No free slot, let it stay physical
        }
    }

    private IEnumerator MoveRoutine(Transform dice, Transform slot)
    {
        Vector3 startPos = dice.position;
        Quaternion startRot = dice.rotation;
        float elapsed = 0f;

        Vector3 localUp = ClosestLocalAxis(dice, Vector3.up);
        Vector3 localForward = ClosestLocalAxis(dice, slot.forward);
        Quaternion targetRot = slot.rotation * Quaternion.Inverse(Quaternion.LookRotation(localForward, localUp));
        Vector3 targetPos = slot.position + slot.up * dieHeight;

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

        if (diceInSlots == diceSlots.Count)
        {
            gameController.OnEquationComplete(this, EvaluateSlots());
        }
        else
        {
            PunchEquation(1.15f, 0.2f);
        }
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
            faces.Add(diceSlots[i].CurrentDie.GetTopFace());
        }
        return GameController.Evaluate(faces);
    }

    protected string BuildEquation()
    {
        string text = "";
        for (int i = 0; i < diceSlots.Count; i++)
        {
            if (diceSlots[i].IsEmpty)
            {
                if (diceSlots[i].AcceptableFace == FaceType.Number) text += "_";
                else text += " ? ";
                continue;
            }

            FaceDefinition face = diceSlots[i].CurrentDie.GetTopFace();
            if (face.type == FaceType.Number)
            {
                text += face.number;
            }
            else
            {
                text += " " + GameController.OpSymbol(face.op) + " ";
            }
        }
        return text;
    }

    private void PunchEquation(float scale, float duration)
    {
        if (equationPunchRoutine != null) StopCoroutine(equationPunchRoutine);
        equationPunchRoutine = StartCoroutine(GameController.Punch(equationText.transform, scale, duration, 0f));
    }
}