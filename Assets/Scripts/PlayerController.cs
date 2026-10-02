using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Slots")]
    [SerializeField] private DiceSlot[] diceSlots;
    [SerializeField] private float moveTime = 0.25f;
    private int diceInSlots = 0;

    [Header("Dice")]
    [SerializeField] private Die diePrefab; // Prefab
    [SerializeField] private DieDefinition[] die;
    private Die[] spawnedDice;
    [SerializeField] private Transform spawnPoint; // Where dice spawn
    [SerializeField] private Transform ground;

    [Header("UI")]
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text equationText;
    [SerializeField] private Gradient scoreGradient;
    [SerializeField] private float maxLog = 3;
    [SerializeField] private float maxShake = 15;
    private Coroutine punchRoutine;
    private int Score;
    private int result;

    // Counting

    private void Start()
    {
       spawnedDice = new Die[die.Length];

       SpawnDice();
       equationText.text = BuildEquation();

       scoreText.text = "0";
    }

    public void SpawnDice()
    {
        for (int i = 0; i < die.Length; i++)
        {
            Die newDie = Instantiate(diePrefab, spawnPoint.position, Random.rotation);
            spawnedDice[i] = newDie;
            spawnedDice[i].Initialize(die[i]);

            DiceController controller = newDie.GetComponent<DiceController>();
            controller.Setup(ground, spawnPoint);
        }
    }
    public void GetTopFacesInSlots()
    {

        List<FaceDefinition> faces = new List<FaceDefinition>();
        for (int i = 0; i < diceSlots.Length; i++)
        {
            faces.Add(diceSlots[i].CurrentDie.GetTopFace());
        }
        result = Evaluate(faces);
        scoreText.text = result.ToString();
        Score = result;
    }

    public string BuildEquation()
    {
        string text = "";
        for (int i = 0; i < diceSlots.Length; i++)
        {
            if (diceSlots[i].IsEmpty)
            {
                if (diceSlots[i].AcceptableFace == FaceType.Number) text += "_";
                else text += " ? ";
                continue;
            }
            else
            {
                FaceDefinition face = diceSlots[i].CurrentDie.GetTopFace();
                if (face.type == FaceType.Number)
                {
                    text += face.number;
                }
                else
                {
                    text += " " + OpSymbol(face.op) + " ";
                }
            }
        }
        return text;
    }

    private int Evaluate(List<FaceDefinition> faces)
    {
        int total = 0;
        int currentNumber = 0;
        Operator pendingOp = Operator.Add;

        foreach (FaceDefinition face in faces) 
        {
            if (face.type == FaceType.Number)
            {
                currentNumber = (currentNumber * 10) + face.number;
            }
            else
            {
                total = Apply(pendingOp, total, currentNumber);
                pendingOp = face.op;
                currentNumber = 0;
            }
        }

        total = Apply(pendingOp, total, currentNumber );
        return total;
    }

    private int Apply(Operator op, int a, int b)
    {
        switch (op)
        {
            case Operator.Add: 
                return a + b;
            case Operator.Subtract: 
                return a - b;
            case Operator.Multiply: 
                return a * b;
            case Operator.Divide: 
                if (b == 0)
                {
                    print("Divide by Zero");
                    return 0;
                }
                return Mathf.CeilToInt((float)a / b);
        }
        return 0;
    }
    private string OpSymbol(Operator op)
    {
        switch (op)
        {
            case Operator.Add: return "+";
            case Operator.Subtract: return "-";
            case Operator.Multiply: return "×";
            case Operator.Divide: return "÷";
        }
        return "none found";
    }

    public void MoveDiceToSlot(Die dice)
    {
        Rigidbody rb = dice.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        bool placed = false;

        for (int i = 0; i < diceSlots.Length; i++)
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
            return;
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
        print(localUp);

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveTime;
            t = Mathf.SmoothStep(0f, 1f, t);
            dice.position = Vector3.Lerp(startPos, slot.position, t);
            dice.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        dice.position = slot.position;
        dice.rotation = targetRot;

        diceInSlots++;
        if (diceInSlots == diceSlots.Length)
        {
            GetTopFacesInSlots();
            equationText.text = BuildEquation();
            PlayResultFeedback(result);

            yield return new WaitForSeconds(2f);
            StartNextRound();
        }
        else
        {
            equationText.text = BuildEquation();
            PlayPunch(1.15f, 0.2f, 0f);
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

    private void PlayResultFeedback(int score)
    {
        if (score <= 0)
        {
            StartCoroutine(Punch(scoreText.transform, 0.8f, 0.25f, 0));
            scoreText.color = Color.gray;
            StartCoroutine(FadeColor(scoreText.color, Color.white, 1f, .25f));
            return;
        }

        float intensity = Mathf.InverseLerp(0f, maxLog, Mathf.Log10(score + 1));
        scoreText.color = scoreGradient.Evaluate(intensity);
        float shake = intensity > 0.5f ? intensity * maxShake : 0f;
        float punchSize = Mathf.Lerp(1.1f, 1.6f, intensity);
        StartCoroutine(Punch(scoreText.transform, punchSize, 0.25f, shake));
        StartCoroutine(FadeColor(scoreText.color, Color.white, 1f, .25f));
    }

    private IEnumerator Punch(Transform target, float punchScale, float duration, float shake)
    {
        Vector3 big = Vector3.one * punchScale;
        float elapsed = 0f;
        Vector3 startingPosition = target.localPosition;

       target.localScale = big;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            target.localPosition = startingPosition + (Vector3)Random.insideUnitCircle * shake * (1 - t);
            target.localScale = Vector3.Lerp(big, Vector3.one, t);
            yield return null;
        }

        target.localScale = Vector3.one;
        target.localPosition = startingPosition;
    }
    private void PlayPunch(float scale, float duration, float shake)
    {
        if (punchRoutine != null) StopCoroutine(punchRoutine);
        punchRoutine = StartCoroutine(Punch(equationText.transform, scale, duration, shake));
    }
    private IEnumerator FadeColor(Color from, Color to, float delay, float duration)
    {
        float elapsed = 0f;
        yield return new WaitForSeconds(delay);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            scoreText.color = Color.Lerp(from, to, t);
                
            yield return null;
        }

        scoreText.color = to;
    }

    public void StartNextRound()
    {
        for (int i = 0; i < spawnedDice.Length; i++)
        {
            spawnedDice[i].transform.position = spawnPoint.position;
            spawnedDice[i].GetComponent<DiceController>().ResetForNewRound();
        }

        for (int i = 0; i < diceSlots.Length; i++)
        {
            if (diceSlots[i].IsEmpty) continue;
            diceSlots[i].Clear();
        }
        equationText.text = BuildEquation();
        diceInSlots = 0;
    }
}
