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
        for (int i = 0; i < die.Length; i++)
        {
            int opSides = 0;
            int numSides = 0;
            for (int j = 0; j < die[i].faceDefinitions.Length; j++)
            {
                if (die[i].faceDefinitions[j] == null)
                {
                    numSides++;
                    continue;
                }
                if (die[i].faceDefinitions[j].type == FaceType.Number) numSides++;
                if (die[i].faceDefinitions[j].type == FaceType.Operator) opSides++;
            }

            if (opSides > numSides) operators++;
            else numbers++;
        }
        operators = Mathf.Min(operators, numbers - 1);
        SpawnSlots(GenerateLayout(numbers, operators));
    }
}