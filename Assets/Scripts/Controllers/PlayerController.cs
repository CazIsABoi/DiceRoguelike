using System.Collections.Generic;
using UnityEngine;

public class PlayerController : DiceSide
{
    protected override bool IsPlayerControlled => true;
    [SerializeField] private DieDefinition[] die;

    [Header("Health")]
    [SerializeField] private int baseHealth = 100;          // max HP on 2 number dice; every extra number die adds a zero

    [Header("Growing dice")]
    [SerializeField] private DieDefinition[] growShapes;    // Die_D8, Die_D10, Die_D12, smallest first: the sizes a number die grows into

    public int NumberDice { get; private set; }
    public int Stake { get; private set; } = 1;             // 1, 10, 100: one more zero for every number die past two

    private void Start()
    {
        InitHealth(baseHealth);
        SpawnDice(die);
        RebuildSlots();
        equationText.text = BuildEquation();
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

    private static bool IsNumberDie(Die d)
    {
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
        SetMaxHealthKeepRatio(baseHealth * Stake);

        operators = Mathf.Min(operators, numbers - 1);
        SpawnSlots(GenerateLayout(numbers, operators));
    }

    public void AddDie(DieDefinition def)
    {
        Die newDie = SpawnOneDie(def);
        System.Array.Resize(ref spawnedDice, spawnedDice.Length + 1);
        spawnedDice[spawnedDice.Length - 1] = newDie;
        RebuildSlots();
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
            faces[i] = i < oldFaces.Length ? oldFaces[i] : oldFaces[copyIndex];
        }

        // A one-off definition that only lives for this run: the bigger shape, with this die's faces
        DieDefinition grown = ScriptableObject.CreateInstance<DieDefinition>();
        grown.name = shape.name + "_Grown";
        grown.prefab = shape.prefab;
        grown.displayName = shape.displayName;
        grown.icon = shape.icon;
        grown.faceDefinitions = faces;

        Die newDie = SpawnOneDie(grown);
        int index = System.Array.IndexOf(spawnedDice, oldDie);
        spawnedDice[index] = newDie;
        Destroy(oldDie.gameObject);
        RebuildSlots();
        return newDie;
    }

    public Die[] GetDice() { return spawnedDice; }
}