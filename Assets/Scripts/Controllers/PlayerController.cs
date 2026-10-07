using System.Collections.Generic;
using UnityEngine;

public class PlayerController : DiceSide
{
    protected override bool IsPlayerControlled => true;
    [SerializeField] private DieDefinition[] die;

    private void Start()
    {
        InitHealth(maxHealth);
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

    private void RebuildSlots()
    {
        int operators = 0;
        int numbers = 0;
        foreach (Die d in spawnedDice)
        {
            int opSides = 0;
            int numSides = 0;
            foreach (FaceDefinition face in d.GetCurrentFaces())
            {
                if (face.type == FaceType.Operator) opSides++;
                else numSides++;
            }

            if (opSides > numSides) operators++;
            else numbers++;
        }
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

    public Die[] GetDice() { return spawnedDice; }
}