using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEngine.Rendering.RayTracingAccelerationStructure;

public class PlayerController : DiceSide
{
    protected override bool IsPlayerControlled => true;
    [SerializeField] private DieDefinition[] die;

    [Header("Health")]
    [SerializeField] private int baseHealth = 100;          // max HP on 2 number dice; every extra number die adds a zero

    [Header("Growing dice")]
    [SerializeField] private DieDefinition[] growShapes;    // Die_D8, Die_D10, Die_D12, smallest first: the sizes a number die grows into
    [SerializeField, Range(0, 6)] private int growCopies = 1;   // how many of the new sides copy the number you picked. The rest get the
                                                                // bigger shape's own number for that side (a D6 growing into a D8 gets your pick + an 8)

    [Header("× cap")]
    [SerializeField] private int facesPerMultiply = 3;      // a die holds one × per this many faces, rounded up: D6 2, D8 3, D10 4, D12 4, D20 7

    [Header("Spare dice")]
    [SerializeField] private DieDefinition spareDie;        // Die_D6_Low (- - ÷ ÷ + +): swaps in for your operator die
    [SerializeField] private DieDefinition pocketDie;       // Die_D6_Pocket (1 1 2 2 3 3): swaps in for a number die, only with the Pocket Die relic

    [Header("Relics")]
    [SerializeField, Range(0, 100)] private int insurancePercent = 25;   // Insurance leaves you on this much of your max HP

    [Header("Speed Up")]
    [SerializeField] private float speedUp = 3f;   // how fast time runs while you hold Shift and dice are rolling

    public int NumberDice { get; private set; }
    public int HpBonusPercent { get; private set; }         // the Max HP reward: +20% each time, kept when a boss die adds a zero
    public int Stake { get; private set; } = 1;             // 1, 10, 100: one more zero for every number die past two

    private readonly List<RelicType> relics = new List<RelicType>();
    public event System.Action OnRelicsChanged;             // the relic bar listens to this
    public event System.Action<RelicType> OnRelicTriggered; // a relic just did something (or was just picked): its icon pops
    public bool InsuranceUsed { get; private set; }
    private int bonusRethrows;                              // Rabbit's Foot: refilled at the start of every fight

    [Header("Tip")]
    [SerializeField, Min(0)] private int tipsPerFight = 1;  // tips you get every fight (TipController). 0 turns tipping off
    public int TipsLeft { get; private set; }
    public int TipsPerFight => tipsPerFight;


    // The spares are spawned once and parked (inactive) when they're not in play.
    // While one is in, the die it replaced is parked instead, faces and all.
    private Die spareObject, pocketObject;
    private Die swappedOutOperator, swappedOutNumber;

    private void Start()
    {
        HpBonusPercent = RunSettings.RaiseStartingHpPercent;   // Raise (Short Stack): starts below 0, the Max HP reward adds to it
        InitHealth(baseHealth);
        SpawnDice(RunSettings.StartingDice(die));
        RebuildSlots();
        RefreshEquation(true);
    }

    private List<FaceType> GenerateLayout(int numbers, int operators)
    {
        List<FaceType> pattern = new List<FaceType>();
        int groups = operators + 1;
        int baseSize = numbers / groups;
        int extra = numbers % groups;      // leftovers that don't divide evenly

        for (int g = 0; g < groups; g++)
        {
            int size = baseSize;
            if (g < extra) size++;
            for (int i = 0; i < size; i++)
            {
                pattern.Add(FaceType.Number);
            }
            if (g < groups - 1) pattern.Add(FaceType.Operator);
        }
        return pattern;
    }

    public static bool IsNumberDie(Die d)
    {
        if (d.Role == DieRole.Number) return true;
        if (d.Role == DieRole.Operator) return false;
        int opSides = 0;
        int numSides = 0;
        foreach (FaceDefinition face in d.GetCurrentFaces())
        {
            if (face == null) continue;
            if (face.type == FaceType.Operator) opSides++;
            else numSides++;
        }
        return opSides <= numSides;
    }

    private void RebuildSlots()
    {
        int operators = 0;
        int numbers = 0;
        foreach (Die d in spawnedDice)
        {
            if (IsNumberDie(d)) numbers++;
            else operators++;
        }
        NumberDice = numbers;

        // Every number die puts one more digit on your results, so it puts one more zero on your max HP:
        // 2 number dice = 100, 3 = 1,000, 4 = 10,000. Current HP keeps the same share (63/100 becomes 630/1000).
        Stake = 1;
        for (int i = 2; i < numbers; i++) Stake *= 10;
        SetMaxHealthKeepRatio(baseHealth * Stake * (100 + HpBonusPercent) / 100);

        operators = Mathf.Min(operators, numbers - 1);
        SpawnSlots(GenerateLayout(numbers, operators));
    }

    public void AddDie(DieDefinition def)
    {
        Die newDie = SpawnOneDie(def);
        System.Array.Resize(ref spawnedDice, spawnedDice.Length + 1);
        spawnedDice[spawnedDice.Length - 1] = newDie;
        AssignSpawnSpots(true);
        RebuildSlots();
    }

    private void Update()   // if PlayerController already has an Update, add the call to it instead
    {
        UpdateSpeedUp();
    }

    private void UpdateSpeedUp()
    {
        if (Time.timeScale == 0f) return;   // a hit-stop is freezing the game: leave it alone

        bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
        bool wanted = shift && DiceController.AnyPlayerDiceRolling && !HoldingADie();
        Time.timeScale = wanted ? speedUp : 1f;
    }

    // Is any of my dice in my hand right now? (flick speed is measured in game time, so never speed up then)
    private bool HoldingADie()
    {
        if (spawnedDice == null) return false;
        foreach (Die d in spawnedDice)
        {
            if (d != null && d.GetComponent<DiceController>().State == DieState.Held) return true;
        }
        return false;
    }

    private void OnDisable()
    {
        Time.timeScale = 1f;   // never leave the whole game stuck at 3x (scene change, end screen...)
    }

    // ---------- Growing dice ----------

    // The next size up for this die, or null if it can't grow (operator dice, D12s and the Gold D20s)
    public DieDefinition NextShape(Die d)
    {
        if (!IsNumberDie(d)) return null;
        int faces = d.GetCurrentFaces().Length;
        foreach (DieDefinition shape in growShapes)
        {
            if (shape.faceDefinitions.Length > faces) return shape;
        }
        return null;
    }

    public bool CanGrowAny()
    {
        foreach (Die d in spawnedDice)
        {
            if (NextShape(d) != null) return true;
        }
        return false;
    }

    // Swaps the die for the next size up. Every face it has stays, and the new faces copy the side at copyIndex.
    // The new die drops in from the spawn point, the same way a boss die does.
    public Die GrowDie(Die oldDie, int copyIndex)
    {
        DieDefinition shape = NextShape(oldDie);
        if (shape == null) return oldDie;

        FaceDefinition[] oldFaces = oldDie.GetCurrentFaces();
        FaceDefinition[] faces = new FaceDefinition[shape.faceDefinitions.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            if (i < oldFaces.Length) faces[i] = oldFaces[i];                                   // every side it had stays
            else if (i - oldFaces.Length < growCopies) faces[i] = oldFaces[copyIndex];        // your pick
            else faces[i] = shape.faceDefinitions[i] != null ? shape.faceDefinitions[i] : oldFaces[copyIndex];   // the shape's own side
        }

        // A one-off definition that only lives for this run: the bigger shape, with this die's faces
        DieDefinition grown = ScriptableObject.CreateInstance<DieDefinition>();
        grown.name = shape.name + "_Grown";
        grown.prefab = shape.prefab;
        grown.displayName = shape.displayName;
        grown.icon = shape.icon;
        grown.faceDefinitions = faces;
        grown.role = oldDie.Role;

        Die newDie = SpawnOneDie(grown);
        int index = System.Array.IndexOf(spawnedDice, oldDie);
        spawnedDice[index] = newDie;
        AssignSpawnSpots(true);
        Destroy(oldDie.gameObject);
        RebuildSlots();
        return newDie;
    }

    public Die[] GetDice() { return spawnedDice; }

    // ---------- × cap ----------

    public int MultiplyCap(Die d)
    {
        int faces = d.GetCurrentFaces().Length;
        int cap = Mathf.CeilToInt(faces / (float)Mathf.Max(1, facesPerMultiply));
        if (HasRelic(RelicType.TimesTable)) cap++;
        return cap;
    }

    public static int CountMultiply(Die d)
    {
        int count = 0;
        foreach (FaceDefinition f in d.GetCurrentFaces())
        {
            if (f != null && f.type == FaceType.Operator && f.op == Operator.Multiply) count++;
        }
        return count;
    }

    // Can this die take one more ×? (It's under its cap and has a face that isn't × already.)
    public bool HasMultiplyRoom(Die d)
    {
        return CountMultiply(d) < MultiplyCap(d) && CountMultiply(d) < d.GetCurrentFaces().Length;
    }

    // The reward screen only offers × while one of your operator dice has room
    public bool OperatorDieHasRoom()
    {
        foreach (Die d in spawnedDice)
        {
            if (!IsNumberDie(d) && HasMultiplyRoom(d)) return true;
        }
        return false;
    }

    // ---------- Relics ----------

    public bool HasRelic(RelicType type) => relics.Contains(type);
    public IReadOnlyList<RelicType> Relics => relics;

    public void AddRelic(RelicDefinition relic)
    {
        if (relic == null || relics.Contains(relic.type)) return;
        relics.Add(relic.type);
        if (relic.type == RelicType.RabbitsFoot) bonusRethrows = 1;   // works from the next throw, not just the next fight
        OnRelicsChanged?.Invoke();
        OnRelicTriggered?.Invoke(relic.type);
    }

    // For relics that GameController applies itself (Carry the One)
    public void TriggerRelic(RelicType type) => OnRelicTriggered?.Invoke(type);

    // GameController calls this as every fight starts
    public void OnFightStart()
    {
        bonusRethrows = HasRelic(RelicType.RabbitsFoot) ? 1 : 0;
        TipsLeft = tipsPerFight;
        OnRelicsChanged?.Invoke();
    }

    // Tip: once a fight (Tips Per Fight), a die you've thrown can be turned onto a neighbouring side. It locks in.
    public void SpendTip()
    {
        if (TipsLeft > 0) TipsLeft--;
        OnRelicsChanged?.Invoke();
    }

    // Fold: only before your first throw of the round, so all you've seen is the enemy's number
    public bool CanFold => NoDiceThrownYet && DiceDone == 0;

    // Rabbit's Foot: once a fight, a die that's out of throws can be thrown again
    public bool RabbitsFootReady => bonusRethrows > 0;
    public override bool HasBonusRethrow => bonusRethrows > 0;
    public override void SpendBonusRethrow()
    {
        if (bonusRethrows <= 0) return;
        bonusRethrows--;
        Telemetry.BonusRethrow();
        OnRelicsChanged?.Invoke();
        OnRelicTriggered?.Invoke(RelicType.RabbitsFoot);
    }

    // Insurance: once a run, a knockout leaves you on insurancePercent of your max HP. Call it right after a hit.
    public bool TryInsurance()
    {
        if (!IsDead || InsuranceUsed || !HasRelic(RelicType.Insurance)) return false;
        InsuranceUsed = true;
        Heal(Mathf.Max(1, MaxHealth * insurancePercent / 100));
        OnRelicsChanged?.Invoke();
        OnRelicTriggered?.Invoke(RelicType.Insurance);
        return true;
    }

    // ---------- Spare dice ----------

    public bool HasSpare => spareDie != null;
    public bool HasPocket => pocketDie != null && HasRelic(RelicType.PocketDie);
    public DieDefinition SpareDefinition => spareDie;
    public DieDefinition PocketDefinition => pocketDie;

    public List<Die> NumberDiceInPlay()
    {
        List<Die> list = new List<Die>();
        foreach (Die d in spawnedDice)
        {
            if (IsNumberDie(d)) list.Add(d);
        }
        return list;
    }

    // Before a Low fight: the spare goes in for your (first) operator die, for this fight only
    public void SwapInSpare()
    {
        if (!HasSpare || swappedOutOperator != null) return;
        int index = -1;
        for (int i = 0; i < spawnedDice.Length; i++)
        {
            if (!IsNumberDie(spawnedDice[i])) { index = i; break; }
        }
        if (index < 0) return;

        spareObject = BringIn(spareObject, spareDie);
        swappedOutOperator = spawnedDice[index];
        Park(swappedOutOperator);
        spawnedDice[index] = spareObject;
        AssignSpawnSpots(true);
        RefreshEquation(false);
    }

    // Pocket Die: goes in for the number die you picked, for this fight only
    public void SwapInPocket(Die numberDie)
    {
        if (!HasPocket || swappedOutNumber != null) return;
        int index = System.Array.IndexOf(spawnedDice, numberDie);
        if (index < 0 || !IsNumberDie(numberDie)) return;

        pocketObject = BringIn(pocketObject, pocketDie);
        swappedOutNumber = numberDie;
        Park(swappedOutNumber);
        spawnedDice[index] = pocketObject;
        AssignSpawnSpots(true);
        RefreshEquation(false);
        OnRelicTriggered?.Invoke(RelicType.PocketDie);
    }

    // After the fight: your own dice come back before the reward screen, so rewards never land on a spare
    public void RevertSwaps()
    {
        if (swappedOutOperator != null) SwapBack(spareObject, swappedOutOperator);
        if (swappedOutNumber != null) SwapBack(pocketObject, swappedOutNumber);
        swappedOutOperator = null;
        swappedOutNumber = null;
    }

    private void SwapBack(Die spare, Die original)
    {
        int index = System.Array.IndexOf(spawnedDice, spare);
        if (index < 0) return;

        original.transform.SetPositionAndRotation(spare.transform.position, spare.transform.rotation);
        foreach (DiceSlot slot in diceSlots)
        {
            if (slot.CurrentDie != spare) continue;
            slot.Clear();
            slot.Place(original);     // the slot now points at the die that's actually sitting in it
            break;
        }

        Park(spare);
        original.gameObject.SetActive(true);
        original.GetComponent<DiceController>().Idle();   // lie still: no hover, no physics, no input
        spawnedDice[index] = original;
    }

    private Die BringIn(Die parked, DieDefinition def)
    {
        if (parked == null) return SpawnOneDie(def);
        parked.gameObject.SetActive(true);
        parked.GetComponent<DiceController>().ResetForNewRound();
        return parked;
    }

    private void Park(Die d)
    {
        Rigidbody rb = d.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        d.gameObject.SetActive(false);
    }
    // The Max HP reward: max HP goes up by 'percent' of the base, and you get that much HP on top
    public void AddMaxHpPercent(int percent)
    {
        int before = Health;
        int oldMax = MaxHealth;
        HpBonusPercent += percent;
        int newMax = baseHealth * Stake * (100 + HpBonusPercent) / 100;
        SetMaxHealthKeepRatio(newMax);
        Heal(before + (newMax - oldMax) - Health);   // the new HP arrives full, on top of what you had
    }

    // Double Down has to be called before the round's first throw: true while every die is still hovering
    public bool NoDiceThrownYet
    {
        get
        {
            foreach (Die d in spawnedDice)
                if (d != null && d.GetComponent<DiceController>().State != DieState.Hovering) return false;
            return true;
        }
    }

    // Lucky Seven: at High tables your 7s count as 14 (GameController's Lucky Seven Bonus), before the maths
    public override int SevenValue =>
        HasRelic(RelicType.LuckySeven) && Game != null && Game.Table == TableType.High ? Game.LuckySevenValue : 7;

    // The Extra Throw chip: every die still in play gets one more throw this round. Returns how many got one.
    public int AddThrowToAll()
    {
        int given = 0;
        foreach (Die d in spawnedDice)
        {
            if (d == null) continue;
            DiceController c = d.GetComponent<DiceController>();
            if (c.State != DieState.Hovering && c.State != DieState.Resting) continue;   // locked in, busted, or in the air
            c.SetThrowAttempts(c.GetThrowAttempts() + 1);
            given++;
        }
        return given;
    }

    public bool HasAnyMultiply()
    {
        foreach (Die die in GetDice())
            foreach (FaceDefinition f in die.GetCurrentFaces())
                if (f != null && f.type == FaceType.Operator && f.op == Operator.Multiply) return true;
        return false;
    }
}