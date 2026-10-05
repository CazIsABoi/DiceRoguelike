using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyController : DiceSide
{
    protected override bool IsPlayerControlled => false;
    [SerializeField] private EnemyDefinition enemy;
    [SerializeField] private float throwForce = 10f;
    [SerializeField] private float sidewaysForce = 10f;
    [SerializeField] private float spinForce = 10f;
    [SerializeField] private float minGain = 2f;
    private EnemyBrain brain => enemy.brain;
    private int throwAttempts => enemy.throwAttempts;
    private List<Die> landedDice = new List<Die>();
    public string DisplayName => enemy.displayName;
    private Coroutine throwRoutine;

    // Anything can listen to this later (a speech bubble, a log panel...)
    public event System.Action<string> OnThought;

    // Start: spawn enemy.dice, spawn slots from enemy.layout.pattern
    private void Start()
    {
        SpawnDice(enemy.dice);
        SpawnSlots(new List<FaceType>(enemy.layout.pattern));
        equationText.text = BuildEquation();
        StartTurn();
    }

    public void StartTurn()
    {
        foreach (Die d in spawnedDice)
        {
            d.GetComponent<DiceController>().SetThrowAttempts(throwAttempts);
        }
        Throw();
    }

    private IEnumerator ThrowRoutine()
    {
        for (int i = 0; i < spawnedDice.Length; i++)
        {
            yield return new WaitForSeconds(Random.Range(.3f, 1.2f));

            Throw(spawnedDice[i]);
        }
    }

    private void Throw(Die d)
    {
        d.GetComponent<DiceController>().Throw(Vector3.up * throwForce + Random.insideUnitSphere * sidewaysForce, Random.onUnitSphere * spinForce);
    }

    public override void MoveDiceToSlot(Die die)
    {
        if (brain == EnemyBrain.Impulsive)
        {
            base.MoveDiceToSlot(die);
            return;
        }

        landedDice.Add(die);
        if (landedDice.Count < spawnedDice.Length) return;   // still waiting for the rest

        if (TryRethrow()) return;   // something went back in the air, wait for it
        PlaceAll();
        landedDice.Clear();
    }

    private void Throw()
    {
        if (throwRoutine != null) StopCoroutine(throwRoutine);
        throwRoutine = StartCoroutine(ThrowRoutine());
    }

    // ---------- Thinking out loud ----------

    private void Think(string thought)
    {
        Debug.Log($"[{DisplayName}] {thought}");
        OnThought?.Invoke(thought);
    }

    private string Describe(FaceDefinition face)
    {
        if (face == null) return "blank";
        return face.type == FaceType.Number ? face.number.ToString() : GameController.OpSymbol(face.op);
    }

    private string DescribeOrder(List<Die> order)
    {
        string text = "";
        foreach (Die d in order)
        {
            FaceDefinition face = d.GetTopFace();
            text += face.type == FaceType.Number ? face.number.ToString() : " " + GameController.OpSymbol(face.op) + " ";
        }
        return text;
    }

    private bool TryRethrow()
    {
        switch (brain)
        {
            case EnemyBrain.Greedy:
                bool rethrewAny = false;
                for (int i = landedDice.Count - 1; i >= 0; i--)
                {
                    Die d = landedDice[i];
                    if (!WantsRethrow(d) || d.GetComponent<DiceController>().GetThrowAttempts() <= 0) continue;
                    Think($"A {Describe(d.GetTopFace())}? No thanks. Rethrowing.");
                    Throw(d);
                    landedDice.Remove(d);
                    rethrewAny = true;
                }
                return rethrewAny;

            case EnemyBrain.Calculating:
                Die dieToRethrow = ChooseDieToRethrow();
                if (dieToRethrow == null) return false;
                landedDice.Remove(dieToRethrow);
                Throw(dieToRethrow);
                return true;

            default:
                return false;
        }
    }

    private void PlaceAll()
    {
        switch (brain)
        {
            case EnemyBrain.Greedy:
                List<Die> numberDice = new List<Die>();
                List<Die> operatorDice = new List<Die>();

                // 1. Sort the landed dice into the two lists
                foreach (Die d in landedDice)
                {
                    if (d.GetTopFace().type == FaceType.Number) numberDice.Add(d);
                    else operatorDice.Add(d);
                }

                // 2. Biggest numbers first
                numberDice.Sort((a, b) => b.GetTopFace().number.CompareTo(a.GetTopFace().number));

                Think("Biggest numbers first, obviously.");

                // 3. Hand them out to the slots, left to right
                int n = 0;
                int o = 0;
                foreach (DiceSlot slot in diceSlots)
                {
                    if (slot.AcceptableFace == FaceType.Number) { PlaceInSlot(numberDice[n], slot); n++; }
                    else { PlaceInSlot(operatorDice[o], slot); o++; }
                }
                break;

            case EnemyBrain.Calculating:
                List<Die> best = FindBestOrder(out int bestScore);
                if (best != null) Think($"Going with {DescribeOrder(best)} = {bestScore}.");
                if (best == null)
                {
                    // Nothing fits the slots: fall back to the default "first free slot" placement
                    foreach (Die d in landedDice) base.MoveDiceToSlot(d);
                    break;
                }
                for (int i = 0; i < best.Count; i++)
                {
                    PlaceInSlot(best[i], diceSlots[i]);
                }
                break;
        }
    }

    private bool WantsRethrow(Die die)
    {
        FaceDefinition face = die.GetTopFace();
        switch (brain)
        {
            case EnemyBrain.Greedy:
                if (face.type == FaceType.Number && face.number < 2) return true;
                else if (face.type == FaceType.Operator && (face.op == Operator.Subtract || face.op == Operator.Divide)) return true;
                else return false;
            default:
                return false;
        }
    }

    private Die ChooseDieToRethrow()
    {
        List<Die> currentOrder = FindBestOrder(out int currentBest);
        if (currentOrder != null) Think($"Best I can do right now: {DescribeOrder(currentOrder)} = {currentBest}.");

        Die bestDie = null;
        float bestGain = 0f;

        foreach (Die die in landedDice)
        {
            if (die.GetComponent<DiceController>().GetThrowAttempts() <= 0) continue;

            FaceDefinition[] faces = die.GetCurrentFaces();

            float total = 0f;
            int counted = 0;

            foreach (FaceDefinition face in faces)
            {
                if (face == null) continue;                    // skip empty faces
                FindBestOrder(out int score, die, face);
                if (score == int.MinValue) score = 0;          // nothing fits = count it as a fizzle
                total += score;
                counted++;
            }

            if (counted == 0) continue;                        // avoid dividing by zero

            float average = total / counted;
            float gain = average - currentBest;

            Think($"If I rethrow the {Describe(die.GetTopFace())}, I'd expect about {average:0.#} ({gain:+0.#;-0.#}).");

            if (gain > bestGain && gain > minGain)
            {
                bestGain = gain;
                bestDie = die;
            }
        }

        if (bestDie != null) Think($"Worth the risk. Rethrowing the {Describe(bestDie.GetTopFace())}.");
        else Think("Not worth risking it. Keeping these.");

        return bestDie;   // null = keep everything
    }

    private List<Die> FindBestOrder(out int bestScore, Die pretendDie = null, FaceDefinition pretendFace = null)
    {
        List<List<Die>> orders = new List<List<Die>>();
        Permute(new List<Die>(landedDice), 0, orders);

        List<Die> bestOrder = null;
        bestScore = int.MinValue;

        foreach (List<Die> order in orders)
        {
            // 1. Does this seating plan fit the slots?
            bool fits = true;
            List<FaceDefinition> faces = new List<FaceDefinition>();
            for (int i = 0; i < order.Count; i++)
            {
                FaceDefinition face = (order[i] == pretendDie) ? pretendFace : order[i].GetTopFace();
                if (face.type != diceSlots[i].AcceptableFace) { fits = false; break; }
                faces.Add(face);
            }
            if (!fits) continue;

            // 2. Score it, keep it if it's the best so far
            int score = GameController.Evaluate(faces);
            if (score > bestScore)
            {
                bestScore = score;
                bestOrder = order;
            }
        }
        return bestOrder;
    }

    private void Permute(List<Die> dice, int k, List<List<Die>> results)
    {
        if (k == dice.Count)
        {
            results.Add(new List<Die>(dice));   // one finished ordering, save a copy
            return;
        }

        for (int i = k; i < dice.Count; i++)
        {
            Swap(dice, k, i);
            Permute(dice, k + 1, results);
            Swap(dice, k, i);                   // swap back so the next loop starts clean
        }
    }

    private void Swap(List<Die> list, int a, int b)
    {
        Die temp = list[a];
        list[a] = list[b];
        list[b] = temp;
    }
}