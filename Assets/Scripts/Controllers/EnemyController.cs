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
    private EnemyBrain brain => enemy.brain;
    private int throwAttempts => enemy.throwAttempts;
    private List<Die> landedDice = new List<Die>();
    public string DisplayName => enemy.displayName;
    private Coroutine throwRoutine;
    [SerializeField] private DialogueDisplay dialogue;
    public event System.Action<string> OnThought;
    public int BonusRolls { get; set; }    // Hot Hand: extra rolls per die, reset every fight
    public GameObject Cup { get; set; }    // Poker Face: the cup currently on the table
    public void Announce(string line) { if (!string.IsNullOrEmpty(line)) Say(line); }   // traits talk through his dialogue

    [Header("Slam")]
    [SerializeField] private Transform[] landSpots;          // one per die, on the enemy's felt
    [SerializeField] private ParticleSystem dust;
    [SerializeField] private AudioSource slamSource;
    [SerializeField] private AudioClip[] slamSounds;
    [SerializeField] private float liftHeight = 0.6f, liftTime = 0.12f, hangTime = 0.15f, dropTime = 0.08f;
    [SerializeField] private float slamSpin = 900f;          // degrees per second while it's in the air
    [SerializeField] private float thinkTime = 0.5f;         // the pause before he rerolls, so you can read his line
    private readonly Dictionary<Die, int> rollsLeft = new Dictionary<Die, int>();
    public event System.Action OnSlam;



    // Start: spawn enemy.dice, spawn slots from enemy.layout.pattern

    public void Load(EnemyDefinition def)
    {
        if (Cup != null) Destroy(Cup);
        if (throwRoutine != null) StopCoroutine(throwRoutine);
        DestroyDice();
        landedDice.Clear();
        enemy = def;
        InitHealth(enemy.maxHP);
        SpawnDice(enemy.dice);
        SpawnSlots(new List<FaceType>(enemy.layout.pattern));
        ClearTotal();
        RefreshEquation(true);
    }

    public void StartTurn()
    {
        foreach (Die d in spawnedDice)
        {
            d.GetComponent<DiceController>().SetThrowAttempts(throwAttempts);
        }
        Throw();
    }
    // ---------- Enemy Turn ----------
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

    public IEnumerator TakeTurn()
    {
        // 1 + 2: every die gets its rolls, then one slam each. The first slam IS the first roll, so it costs one.
        rollsLeft.Clear();
        foreach (Die d in spawnedDice)
        {
            rollsLeft[d] = throwAttempts + BonusRolls;
            yield return Slam(d, RollFace(d), LandSpot(d));
            rollsLeft[d]--;
        }

        // 3: everything is on the felt. Look at it and decide what to roll again.
        landedDice = new List<Die>(spawnedDice);
        List<Die> rerolls = ChooseRerolls();


        // 4: as long as he wants another go: say so, pause, slam those dice, then look again
        while (rerolls.Count > 0)
        {
            Mutter(enemy.rerollLines);
            yield return new WaitForSeconds(thinkTime);
            foreach (Die d in rerolls)
            {
                yield return Slam(d, RollFace(d), LandSpot(d));
                rollsLeft[d]--;
            }
            rerolls = ChooseRerolls();   // ← the board changed, so decide again from scratch
        }

        // 5: happy (or out of rolls): place everything
        Mutter(enemy.keepLines);
        PlaceAll();
        landedDice.Clear();
    }

    private IEnumerator Slam(Die die, int faceIndex, Vector3 landAt)
    {
        Transform t = die.transform;
        die.GetComponent<DiceController>().HandControl();   // physics off: from here the enemy's "hand" moves it

        // Where it ends: socket faceIndex pointing straight up (same maths as Straighten), turned randomly around up
        Transform socket = die.GetSocket(faceIndex);
        Quaternion faceUp = Quaternion.FromToRotation(socket.up, Vector3.up) * t.rotation;
        Quaternion endRot = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * faceUp;

        // landAt is ON the felt. The die's centre sits half a die higher (centre to face, like MoveRoutine's restHeight).
        float half = Vector3.Distance(socket.position, t.position);
        Vector3 restPos = landAt + Vector3.up * half;
        Vector3 highPos = restPos + Vector3.up * liftHeight;
        Vector3 spinAxis = Random.onUnitSphere;

        // a. Lift: ease OUT (fast, then slow), spinning
        Vector3 from = t.position;
        for (float time = 0f; time < liftTime; time += Time.deltaTime)
        {
            float k = time / liftTime;
            k = 1f - (1f - k) * (1f - k);
            t.position = Vector3.Lerp(from, highPos, k);
            t.Rotate(spinAxis, slamSpin * Time.deltaTime, Space.World);
            yield return null;
        }

        // b. Hang: keeps spinning in the air
        for (float time = 0f; time < hangTime; time += Time.deltaTime)
        {
            t.position = highPos;
            t.Rotate(spinAxis, slamSpin * Time.deltaTime, Space.World);
            yield return null;
        }

        // c. Drop: ease IN (slow, then fast), turning into the final face on the way down
        Quaternion spun = t.rotation;
        for (float time = 0f; time < dropTime; time += Time.deltaTime)
        {
            float k = time / dropTime;
            k = k * k;
            t.SetPositionAndRotation(Vector3.Lerp(highPos, restPos, k), Quaternion.Slerp(spun, endRot, k));
            yield return null;
        }
        t.SetPositionAndRotation(restPos, endRot);

        // d. Impact
        if (dust != null)
        {
            dust.transform.position = landAt;
            dust.Emit(20);
        }
        if (slamSource != null && slamSounds != null)
        {
            slamSource.pitch = Random.Range(0.9f, 1.1f);
            slamSource.PlayOneShot(slamSounds[Random.Range(0, slamSounds.Length)]);
        }
        StartCoroutine(GameController.Pop(t, t.localScale, 1.15f, 0.2f));   // a little squash on landing
        OnSlam?.Invoke();
    }

    // A fair die: every side equally likely. Blank faces count, they're real sides.
    private int RollFace(Die die)
    {
        FaceDefinition[] faces = die.GetCurrentFaces();
        if (DevPanel.EnemyRoll != DevPanel.RollMode.Normal)
        {
            bool best = DevPanel.EnemyRoll == DevPanel.RollMode.Best;
            int pick = -1;
            for (int i = 0; i < faces.Length; i++)
            {
                FaceDefinition f = faces[i];
                if (f == null || f.type != FaceType.Number) continue;
                if (pick < 0 || (best ? f.number > faces[pick].number : f.number < faces[pick].number)) pick = i;
            }
            if (pick >= 0) return pick;   // operator dice have no numbers: they still roll normally
        }
        return Random.Range(0, faces.Length);
    }

    // Where die number i lands: its own spot, so the dice don't slam into each other
    private Vector3 LandSpot(Die die)
    {
        int i = System.Array.IndexOf(spawnedDice, die);
        bool hasSpot = landSpots != null && i >= 0 && i < landSpots.Length && landSpots[i] != null;
        Vector3 spot = hasSpot ? landSpots[i].position : die.GetComponent<DiceController>().HomePosition;

        Collider felt = GroundCollider;
        if (felt != null && felt.Raycast(new Ray(spot + Vector3.up * 3f, Vector3.down), out RaycastHit hit, 10f))
        {
            spot.y = hit.point.y;
        }
        return spot;
    }

    private void Mutter(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        dialogue.SayNow(lines[Random.Range(0, lines.Length)]);   
    }

    private List<Die> ChooseRerolls()
    {
        List<Die> picks = new List<Die>();
        switch (brain)
        {
            case EnemyBrain.Greedy:
                foreach (Die d in landedDice)
                {
                    if (rollsLeft[d] > 0 && WantsRethrow(d)) picks.Add(d);   // every bad face he can still afford
                }
                break;

            case EnemyBrain.Calculating:
                Die best = ChooseDieToRethrow();   // the one reroll worth the risk, or null
                if (best != null) picks.Add(best);
                break;

                // Impulsive: takes whatever he gets, never rerolls
        }
        return picks;
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
        if (Random.value < enemy.blunderChance)
        {
            Think("Eh, whatever.");
            foreach (Die d in landedDice) base.MoveDiceToSlot(d);
            return;
        }

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

                // 2. Biggest numbers first (smallest first at a Low table)
                if (Game.Table == TableType.Low)
                {
                    numberDice.Sort((a, b) => a.GetTopFace().number.CompareTo(b.GetTopFace().number));
                    Think("Smallest numbers first. Keep it near zero.");
                }
                else
                {
                    numberDice.Sort((a, b) => b.GetTopFace().number.CompareTo(a.GetTopFace().number));
                    Think("Biggest numbers first, obviously.");
                }

                // 3. Hand them out to the slots, left to right
                int n = 0;
                int o = 0;
                foreach (DiceSlot slot in diceSlots)
                {
                    if (slot.AcceptableFace == FaceType.Number)
                    {
                        if (n < numberDice.Count)
                        {
                            PlaceInSlot(numberDice[n], slot);
                            n++;
                        }
                    }
                    else
                    {
                        if (o < operatorDice.Count)
                        {
                            PlaceInSlot(operatorDice[o], slot);
                            o++;
                        }
                    }
                }

                // 4. Whatever didn't get a slot is busted
                for (int i = n; i < numberDice.Count; i++) Bust(numberDice[i]);
                for (int i = o; i < operatorDice.Count; i++) Bust(operatorDice[i]);

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
            default:   // Impulsive: first free slot, in the order they landed
                foreach (Die d in landedDice) base.MoveDiceToSlot(d);
                break;
        }
    }

    private bool WantsRethrow(Die die)
    {
        FaceDefinition face = die.GetTopFace();
        switch (brain)
        {
            case EnemyBrain.Greedy:
                if (Game.Table == TableType.Low)
                {
                    // Closest to 0 wins: big numbers, + and × are the bad faces now
                    if (face.type == FaceType.Number) return face.number >= 10 - enemy.rethrowBelow;
                    return face.op == Operator.Add || face.op == Operator.Multiply;
                }
                if (face.type == FaceType.Number && face.number < enemy.rethrowBelow) return true;
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
            if (rollsLeft[die] <= 0) continue;

            FaceDefinition[] faces = die.GetCurrentFaces();

            float total = 0f;
            int counted = 0;

            foreach (FaceDefinition face in faces)
            {
                if (face == null) continue;                    // skip empty faces
                FindBestOrder(out int score, die, face);
                if (score == int.MinValue) score = Game.WorstScore;   // nothing fits = count it as a fizzle
                total += score;
                counted++;
            }

            if (counted == 0) continue;                        // avoid dividing by zero

            float average = total / counted;
            float gain = average - currentBest;

            Think($"If I rethrow the {Describe(die.GetTopFace())}, I'd expect about {average:0.#} ({gain:+0.#;-0.#}).");

            if (gain > bestGain && gain > enemy.minGain)
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

            // 2. Score it for this table (bigger, closer to 0 or closer to the target), keep it if it's the best so far
            int score = Game.TableScore(GameController.Evaluate(faces), 0);
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

    // ---------- Dialogue ----------
    private void Say(string line) => dialogue.Say(line);

    private void SayOneOf(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        Say(lines[Random.Range(0, lines.Length)]);
    }

    public void SayIntro()
    {
        dialogue.SetSpeaker(enemy.displayName, enemy.portrait, enemy.portraitTalk, enemy.voice);
        dialogue.SayNow(enemy.intro);
    }
    public void SayDefeat() => dialogue.SayNow(enemy.defeatLine);
    public void SayTurnResult(bool iWon) => SayOneOf(iWon ? enemy.winLines : enemy.loseLines);

    protected override void OnBusted(Die die) => SayOneOf(enemy.bustLines);
}